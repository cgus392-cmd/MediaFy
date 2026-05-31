using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using YTDownloader.Core;

namespace YTDownloader;

public partial class App : Application
{
    public static DownloadManager DownloadManager { get; } = new();
    public static CascadeManager CascadeManager { get; } = new();
    public static PlaybackService Playback { get; } = new();
    public static Window? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        // Identidad ante Windows: SMTC, notificaciones y bandeja muestran "MediaFy" con su logo
        try { SetCurrentProcessExplicitAppUserModelID("CGLabs.MediaFy"); } catch { }
        NotificationService.Register();
        UnhandledException += (s, e) =>
        {
            Log($"UNHANDLED: {e.Message}\n{e.Exception}");
        };
    }

    public static bool StartedInTray { get; private set; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            // ¿Arrancamos en bandeja? (--tray es el argumento del autoarranque con Windows)
            StartedInTray = Environment.GetCommandLineArgs().Any(a => string.Equals(a, "--tray", StringComparison.OrdinalIgnoreCase));

            MainWindow = new MainWindow();
            if (!StartedInTray) MainWindow.Activate();

            // Procesa la activación inicial (CLI args o mediafy://...)
            var initial = AppInstance.GetCurrent().GetActivatedEventArgs();
            ProcessActivation(initial);

            // Auto-update yt-dlp en background si está activado
            if (AppSettings.Current.AutoUpdateYtDlp)
                _ = Task.Run(async () => { try { await DownloadManager.UpdateYtDlpAsync(); } catch { } });
        }
        catch (Exception ex)
        {
            Log($"OnLaunched: {ex}");
            throw;
        }
    }

    /// <summary>Llamado desde Program cuando una instancia adicional se redirige a esta.</summary>
    public static void HandleActivation(AppActivationArguments args)
    {
        // Volver al hilo de UI
        if (MainWindow is MainWindow mw)
        {
            mw.DispatcherQueue.TryEnqueue(() =>
            {
                ProcessActivation(args);
                mw.BringToFront();
            });
        }
    }

    private static void ProcessActivation(AppActivationArguments args)
    {
        try
        {
            string? url = null;

            if (args.Kind == ExtendedActivationKind.Protocol &&
                args.Data is ProtocolActivatedEventArgs proto)
            {
                // mediafy://download?url=https%3A%2F%2F...
                url = ExtractUrlFromProtocol(proto.Uri);
            }
            else
            {
                // CLI: MediaFy.exe "<url>"
                var cli = Environment.GetCommandLineArgs();
                for (int i = 1; i < cli.Length; i++)
                {
                    var a = cli[i];
                    if (a.StartsWith("mediafy://", StringComparison.OrdinalIgnoreCase))
                        url = ExtractUrlFromProtocol(new Uri(a));
                    else if (a.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                             a.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        url = a;
                    if (url != null) break;
                }
            }

            if (!string.IsNullOrEmpty(url) && MainWindow is MainWindow mw)
                mw.HandleIncomingUrl(url);
        }
        catch (Exception ex) { Log($"ProcessActivation: {ex.Message}"); }
    }

    private static string? ExtractUrlFromProtocol(Uri uri)
    {
        // Acepta: mediafy://download?url=<encoded>  o  mediafy://<encoded-or-plain>
        if (!string.IsNullOrEmpty(uri.Query))
        {
            var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var u = q["url"];
            if (!string.IsNullOrEmpty(u)) return Uri.UnescapeDataString(u);
        }
        // Fallback: lo que venga tras el host
        string tail = (uri.Host + uri.AbsolutePath).TrimStart('/');
        tail = Uri.UnescapeDataString(tail);
        return tail.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? tail : null;
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
