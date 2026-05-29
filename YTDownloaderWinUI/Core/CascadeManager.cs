using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using YTDownloader.Models;

namespace YTDownloader.Core;

/// <summary>
/// Motor de descargas en cascada (escalonado): el siguiente ítem arranca cuando el
/// actual alcanza el umbral configurable (por defecto 70%), estilo Google Play Store.
/// Máximo 5 ítems. Un tope duro de activas reales evita saturar la red.
/// </summary>
public class CascadeManager
{
    public const int MaxItems = 5;
    private const int HardActiveCap = 3; // nunca más de 3 descargando físicamente a la vez

    private readonly YtDlpService _ytDlp = new();
    private readonly ConcurrentDictionary<DownloadItem, CancellationTokenSource> _cts = new();

    public ObservableCollection<DownloadItem> Items { get; } = new();
    public bool IsRunning { get; private set; }

    public bool CanAdd => Items.Count < MaxItems && !IsRunning;
    public bool HasAnalyzable => Items.Any(i => i.Status == DownloadStatus.Pending);
    public bool CanStart => !IsRunning && Items.Any(i => i.Status == DownloadStatus.Queued);

    private static string OutputFolder => AppSettings.Current.OutputFolder;

    public DownloadItem? Add(string url, string format, string quality)
    {
        if (!CanAdd) return null;
        var item = new DownloadItem
        {
            Url = url, Format = format, Quality = quality,
            Status = DownloadStatus.Pending, StatusText = "Sin analizar",
            Title = url.Length > 50 ? url[..50] + "..." : url
        };
        Items.Add(item);
        return item;
    }

    public void Remove(DownloadItem item)
    {
        if (IsRunning && item.IsActive) Cancel(item);
        Items.Remove(item);
    }

    public void Clear()
    {
        if (IsRunning) return;
        Items.Clear();
    }

    public void Cancel(DownloadItem item)
    {
        if (_cts.TryGetValue(item, out var cts)) { cts.Cancel(); item.StatusText = "Cancelando..."; }
    }

    /// <summary>Paso 1: analiza todos los enlaces pendientes y rellena su preview.</summary>
    public async Task AnalyzeAllAsync()
    {
        foreach (var item in Items.ToList())
        {
            if (item.Status != DownloadStatus.Pending) continue;
            item.Status = DownloadStatus.Fetching;
            item.StatusText = "Analizando...";
            try
            {
                var info = await _ytDlp.GetVideoInfoAsync(item.Url);
                item.Title = info.Title;
                item.Thumbnail = info.Thumbnail;
                item.Duration = info.Duration;
                item.Status = DownloadStatus.Queued;
                item.StatusText = "Listo";
            }
            catch
            {
                item.Status = DownloadStatus.Error;
                item.StatusText = "No se pudo analizar";
            }
        }
    }

    /// <summary>Paso 2: arranca la cascada escalonada según el umbral configurado.</summary>
    public async Task StartCascadeAsync()
    {
        if (IsRunning) return;
        IsRunning = true;

        double threshold = Math.Clamp(AppSettings.Current.CascadeThreshold, 30, 95);
        var tasks = new List<Task>();

        try
        {
            foreach (var item in Items.ToList())
            {
                if (item.Status != DownloadStatus.Queued) continue; // saltar los que fallaron al analizar

                tasks.Add(ProcessOneAsync(item));

                // Espera a que este ítem alcance el umbral (o termine) antes de soltar el siguiente,
                // respetando además el tope de activas reales.
                await WaitToReleaseNextAsync(item, threshold);
            }

            await Task.WhenAll(tasks);
        }
        finally
        {
            IsRunning = false;
        }
    }

    private async Task WaitToReleaseNextAsync(DownloadItem current, double threshold)
    {
        while (true)
        {
            // Si el ítem terminó (ok/error/cancelado), pasamos al siguiente de inmediato.
            if (current.Status is DownloadStatus.Done or DownloadStatus.Error or DownloadStatus.Canceled)
                return;

            int active = Items.Count(i => i.Status is DownloadStatus.Downloading or DownloadStatus.Converting);

            if (current.Progress >= threshold && active < HardActiveCap)
                return;

            await Task.Delay(200);
        }
    }

    private async Task ProcessOneAsync(DownloadItem item)
    {
        var cts = new CancellationTokenSource();
        _cts[item] = cts;
        try
        {
            item.Status = DownloadStatus.Downloading;
            item.StatusText = "Descargando...";
            item.Progress = 0;

            var prog = new Progress<DownloadProgress>(p =>
            {
                if (p.LogLine is not null) item.AddLog(p.LogLine);
                switch (p.Percent)
                {
                    case -2: break;
                    case -1:
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

            Directory.CreateDirectory(OutputFolder);
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
            item.StatusText = $"Error: {ex.Message[..Math.Min(60, ex.Message.Length)]}";
        }
        finally
        {
            _cts.TryRemove(item, out _);
            cts.Dispose();
        }
    }
}
