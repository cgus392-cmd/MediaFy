using Microsoft.UI.Xaml;
using YTDownloader.Core;

namespace YTDownloader;

public partial class App : Application
{
    public static DownloadManager DownloadManager { get; } = new();
    public static Window? MainWindow { get; private set; }

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
