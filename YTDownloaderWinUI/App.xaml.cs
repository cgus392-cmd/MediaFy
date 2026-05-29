using System.IO;
using Microsoft.UI.Xaml;
using YTDownloader.Core;

namespace YTDownloader;

public partial class App : Application
{
    public static DownloadManager DownloadManager { get; } = new();
    public static CascadeManager CascadeManager { get; } = new();
    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (s, e) =>
        {
            Log($"UNHANDLED: {e.Message}\n{e.Exception}");
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            MainWindow = new MainWindow();
            MainWindow.Activate();
        }
        catch (Exception ex)
        {
            Log($"OnLaunched: {ex}");
            throw;
        }
    }

    private static void Log(string msg)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "startup_error.log"),
                $"[{DateTime.Now:HH:mm:ss}] {msg}\n\n");
        }
        catch { }
    }
}
