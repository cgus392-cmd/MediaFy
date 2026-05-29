using YTDownloader.Core;
using Application = System.Windows.Application;

namespace YTDownloader;

public partial class App : Application
{
    public static DownloadManager DownloadManager { get; } = new();
}
