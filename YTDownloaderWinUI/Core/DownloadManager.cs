using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using YTDownloader.Models;

namespace YTDownloader.Core;

public class DownloadManager
{
    private readonly YtDlpService _ytDlp = new();
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
    public string OutputFolder
    {
        get => AppSettings.Current.OutputFolder;
        set => AppSettings.Current.OutputFolder = value;
    }

    /// <summary>Añade a la cola y arranca en segundo plano (permite varias en paralelo).</summary>
    public void AddAndStart(string url, string format, string quality)
    {
        Directory.CreateDirectory(OutputFolder);
        var item = new DownloadItem
        {
            Url = url, Format = format, Quality = quality,
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

    private async Task ProcessItemAsync(DownloadItem item)
    {
        var cts = new CancellationTokenSource();
        _cts[item] = cts;
        var sem = _semaphore;
        await sem.WaitAsync();
        try
        {
            cts.Token.ThrowIfCancellationRequested();

            // 1) Info previa
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

            // 2) Descarga
            item.Status = DownloadStatus.Downloading;
            item.StatusText = "Descargando...";

            var prog = new Progress<DownloadProgress>(p =>
            {
                if (p.LogLine is not null) item.AddLog(p.LogLine);

                switch (p.Percent)
                {
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

            await _ytDlp.DownloadAsync(item, OutputFolder, prog, cts.Token);

            item.Progress = 100;
            item.Speed = item.Eta = string.Empty;
            item.Status = DownloadStatus.Done;
            item.StatusText = "Completado";
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
        }
        finally
        {
            sem.Release();
            _cts.TryRemove(item, out _);
            cts.Dispose();
        }
    }
}
