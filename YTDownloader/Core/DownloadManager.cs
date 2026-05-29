using System.Collections.ObjectModel;
using System.IO;
using YTDownloader.Models;

namespace YTDownloader.Core;

public class DownloadManager
{
    private readonly YtDlpService _ytDlp = new();
    private readonly SemaphoreSlim _semaphore = new(2, 2);

    public ObservableCollection<DownloadItem> Queue { get; } = new();
    public bool IsReady => _ytDlp.IsAvailable();
    public string OutputFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads", "YTDownloader");

    public async Task AddAndStartAsync(string url, string format, string quality)
    {
        Directory.CreateDirectory(OutputFolder);

        var item = new DownloadItem { Url = url, Format = format, Quality = quality };
        Queue.Add(item);
        await ProcessItemAsync(item);
    }

    private async Task ProcessItemAsync(DownloadItem item)
    {
        await _semaphore.WaitAsync();
        try
        {
            item.Status = DownloadStatus.Fetching;
            item.StatusText = "Obteniendo info...";

            try
            {
                var info = await _ytDlp.GetVideoInfoAsync(item.Url);
                item.Title = info.Title;
                item.Thumbnail = info.Thumbnail;
                item.Duration = info.Duration;
            }
            catch { /* si falla el fetch de info, continuamos igual */ }

            item.Status = DownloadStatus.Downloading;

            var prog = new Progress<(double percent, string status)>(t =>
            {
                if (t.percent >= 0)
                    item.Progress = t.percent;
                item.StatusText = t.status;
                if (t.status.StartsWith("Convirtiendo") || t.status.StartsWith("Uniendo"))
                    item.Status = DownloadStatus.Converting;
            });

            using var cts = new CancellationTokenSource();
            await _ytDlp.DownloadAsync(item, OutputFolder, prog, cts.Token);

            item.Progress = 100;
            item.Status = DownloadStatus.Done;
            item.StatusText = "Completado";
        }
        catch (Exception ex)
        {
            item.Status = DownloadStatus.Error;
            item.StatusText = $"Error: {ex.Message[..Math.Min(80, ex.Message.Length)]}";
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
