using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using YTDownloader.Models;

namespace YTDownloader.Core;

public class DownloadManager
{
    private readonly YtDlpService _ytDlp = new();
    private readonly SpotifyService _spotify = new();
    private SemaphoreSlim _semaphore;
    private readonly ConcurrentDictionary<DownloadItem, CancellationTokenSource> _cts = new();

    public DownloadManager()
    {
        int n = Math.Clamp(AppSettings.Current.MaxConcurrent, 1, 10);
        _semaphore = new SemaphoreSlim(n, n);
    }

    public int MaxConcurrent
    {
        get => AppSettings.Current.MaxConcurrent;
        set
        {
            value = Math.Clamp(value, 1, 10);
            if (value == AppSettings.Current.MaxConcurrent) return;
            AppSettings.Current.MaxConcurrent = value;
            // Reemplaza el semáforo (los activos terminan con el viejo, los nuevos usan el nuevo)
            _semaphore = new SemaphoreSlim(value, value);
        }
    }

    public ObservableCollection<DownloadItem> Queue { get; } = new();
    public bool IsReady => _ytDlp.IsAvailable();

    /// <summary>Obtiene metadatos del enlace para la vista previa (Fase 3).</summary>
    public Task<Models.VideoInfo> GetInfoAsync(string url, CancellationToken ct = default)
        => _ytDlp.GetVideoInfoAsync(url, ct);

    /// <summary>Ejecuta yt-dlp -U en background (auto-actualización).</summary>
    public Task UpdateYtDlpAsync() => _ytDlp.SelfUpdateAsync();

    /// <summary>Busca en YouTube usando ytsearch de yt-dlp (sin API, gratis).</summary>
    public Task<List<Models.SearchResultItem>> SearchAsync(
        string query, int count = 8, CancellationToken ct = default)
        => _ytDlp.SearchAsync(query, count, ct);

    /// <summary>Resuelve la URL directa de audio para reproducir en streaming (sin descargar).</summary>
    public Task<string?> GetStreamUrlAsync(string url, CancellationToken ct = default)
        => _ytDlp.GetStreamUrlAsync(url, ct);

    // ── Spotify ────────────────────────────────────────────────
    public bool SpotifyReady => true; // ya no requiere credenciales

    public Task<List<Models.SpotifyTrack>> ResolveSpotifyAsync(string url, CancellationToken ct = default)
        => _spotify.ResolveAsync(url, ct);

    public void AddSpotify(Models.SpotifyTrack track)
    {
        Directory.CreateDirectory(OutputFolder);
        var item = new DownloadItem
        {
            Url = "spotify", Format = "MP3", Quality = "320kbps",
            Status = DownloadStatus.Queued, StatusText = "En cola",
            Title = $"{track.Artists} - {track.Title}",
            Thumbnail = track.CoverUrl,
            Spotify = track
        };
        Queue.Add(item);
        _ = ProcessItemAsync(item);
    }
    public string OutputFolder
    {
        get => AppSettings.Current.OutputFolder;
        set => AppSettings.Current.OutputFolder = value;
    }

    /// <summary>Añade a la cola y arranca en segundo plano (permite varias en paralelo).</summary>
    public void AddAndStart(string url, string format, string quality,
        string subtitles = "Off", bool wholePlaylist = false)
    {
        Directory.CreateDirectory(OutputFolder);
        var item = new DownloadItem
        {
            Url = url, Format = format, Quality = quality,
            Subtitles = subtitles, WholePlaylist = wholePlaylist,
            Status = DownloadStatus.Queued, StatusText = "En cola"
        };
        Queue.Add(item);
        _ = ProcessItemAsync(item);
    }

    public void Cancel(DownloadItem item)
    {
        if (_cts.TryGetValue(item, out var cts))
        {
            cts.Cancel();
            item.StatusText = "Cancelando...";
        }
    }

    public void Retry(DownloadItem item)
    {
        if (item.Status is not (DownloadStatus.Error or DownloadStatus.Canceled)) return;
        item.Progress = 0;
        item.Speed = item.Eta = item.SizeText = string.Empty;
        item.Logs.Clear();
        item.Status = DownloadStatus.Queued;
        item.StatusText = "En cola";
        _ = ProcessItemAsync(item);
    }

    /// <summary>Busca el archivo más reciente en la carpeta de salida (heurística para notificación).</summary>
    private string FindResultPath(DownloadItem item)
    {
        try
        {
            var dir = new DirectoryInfo(OutputFolder);
            if (!dir.Exists) return string.Empty;
            var newest = dir.EnumerateFiles()
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            return newest?.FullName ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    private async Task ProcessItemAsync(DownloadItem item)
    {
        var cts = new CancellationTokenSource();
        _cts[item] = cts;
        var sem = _semaphore;
        await sem.WaitAsync();
        try
        {
            cts.Token.ThrowIfCancellationRequested();

            // 1) Info previa (no aplica a Spotify: ya tenemos los metadatos)
            if (item.Spotify == null)
            {
                item.Status = DownloadStatus.Fetching;
                item.StatusText = "Obteniendo info...";
                try
                {
                    var info = await _ytDlp.GetVideoInfoAsync(item.Url, cts.Token);
                    item.Title = info.Title;
                    item.Thumbnail = info.Thumbnail;
                    item.Duration = info.Duration;
                }
                catch (OperationCanceledException) { throw; }
                catch { /* si falla el fetch, seguimos con la descarga igual */ }
            }

            // 2) Descarga
            item.Status = DownloadStatus.Downloading;
            item.StatusText = "Descargando...";

            var prog = new Progress<DownloadProgress>(p =>
            {
                if (p.LogLine is not null) item.AddLog(p.LogLine);

                switch (p.Percent)
                {
                    case -3: // nueva pista de una lista: "N|M"
                        var parts = p.Status.Split('|');
                        if (parts.Length == 2)
                        {
                            item.PlaylistInfo = $"Pista {parts[0]} de {parts[1]}";
                            item.Progress = 0;            // reinicia la barra para la nueva pista
                            item.Speed = item.Eta = item.SizeText = string.Empty;
                            item.Status = DownloadStatus.Downloading;
                            item.StatusText = "Descargando...";
                        }
                        break;
                    case -2: // solo log
                        break;
                    case -1: // cambio de fase (conversión, fusión...)
                        item.Status = DownloadStatus.Converting;
                        item.StatusText = p.Status;
                        item.Speed = item.Eta = string.Empty;
                        break;
                    default:
                        item.Progress = p.Percent;
                        item.StatusText = p.Status;
                        item.Speed = p.Speed;
                        item.Eta = p.Eta;
                        item.SizeText = p.SizeText;
                        break;
                }
            });

            if (item.Spotify != null)
                await _ytDlp.DownloadFromSpotifyAsync(item, item.Spotify, OutputFolder, prog, cts.Token);
            else
                await _ytDlp.DownloadAsync(item, OutputFolder, prog, cts.Token);

            item.Progress = 100;
            item.Speed = item.Eta = string.Empty;
            item.Status = DownloadStatus.Done;
            item.StatusText = "Completado";

            if (AppSettings.Current.NotifyOnComplete)
            {
                string fp = item.OutputPath;
                if (string.IsNullOrEmpty(fp) || !File.Exists(fp))
                    fp = FindResultPath(item);
                NotificationService.DownloadCompleted(item.Title, fp);
            }
        }
        catch (OperationCanceledException)
        {
            item.Status = DownloadStatus.Canceled;
            item.StatusText = "Cancelado";
            item.Speed = item.Eta = string.Empty;
        }
        catch (Exception ex)
        {
            item.Status = DownloadStatus.Error;
            item.StatusText = $"Error: {ex.Message[..Math.Min(80, ex.Message.Length)]}";
            item.AddLog($"ERROR: {ex.Message}");
            if (AppSettings.Current.NotifyOnComplete)
                NotificationService.DownloadFailed(item.Title, ex.Message);
        }
        finally
        {
            sem.Release();
            _cts.TryRemove(item, out _);
            cts.Dispose();
        }
    }
}
