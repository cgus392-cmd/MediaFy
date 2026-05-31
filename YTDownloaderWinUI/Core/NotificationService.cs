using System.Diagnostics;
using System.IO;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace YTDownloader.Core;

/// <summary>
/// Envía notificaciones nativas de Windows (toast) y maneja los clics en sus
/// botones. Usa AppNotificationManager del Windows App SDK (funciona unpackaged).
/// </summary>
public static class NotificationService
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        try
        {
            var mgr = AppNotificationManager.Default;
            mgr.NotificationInvoked += OnInvoked;
            mgr.Register();
            _registered = true;
        }
        catch { /* notificaciones no disponibles en este entorno */ }
    }

    public static void Unregister()
    {
        if (!_registered) return;
        try { AppNotificationManager.Default.Unregister(); } catch { }
        _registered = false;
    }

    /// <summary>Toast con título, mensaje y botones "Abrir archivo" / "Mostrar en carpeta".</summary>
    public static void DownloadCompleted(string title, string filePath)
    {
        try
        {
            string logo = Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png");
            var builder = new AppNotificationBuilder()
                .AddText("Descarga completada")
                .AddText(title)
                .AddButton(new AppNotificationButton("Abrir archivo")
                    .AddArgument("action", "open").AddArgument("path", filePath))
                .AddButton(new AppNotificationButton("Mostrar en carpeta")
                    .AddArgument("action", "reveal").AddArgument("path", filePath));
            if (File.Exists(logo))
                builder.SetAppLogoOverride(new Uri(logo), AppNotificationImageCrop.Default);

            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch { }
    }

    public static void DownloadFailed(string title, string reason)
    {
        try
        {
            var n = new AppNotificationBuilder()
                .AddText("Descarga fallida")
                .AddText(title)
                .AddText(reason)
                .BuildNotification();
            AppNotificationManager.Default.Show(n);
        }
        catch { }
    }

    private static void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        try
        {
            if (!args.Arguments.TryGetValue("action", out var action)) return;
            if (!args.Arguments.TryGetValue("path", out var path)) return;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            switch (action)
            {
                case "open":
                    Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                    break;
                case "reveal":
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                    break;
            }
        }
        catch { }
    }
}
