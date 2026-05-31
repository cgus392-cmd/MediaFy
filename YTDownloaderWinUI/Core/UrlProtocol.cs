using System.IO;
using Microsoft.Win32;

namespace YTDownloader.Core;

/// <summary>
/// Registra/desregistra el protocolo URL "mediafy://" en HKCU (sin admin).
/// Incluye RegisteredApplications + Capabilities + ProgId para que Windows 11
/// lo reconozca de inmediato sin pasar por el cuadro de "buscar app".
/// </summary>
public static class UrlProtocol
{
    public const string Scheme = "mediafy";
    private const string ProgId = "MediaFy.Url";
    private const string AppName = "MediaFy";

    private static string ExePath => Path.Combine(AppContext.BaseDirectory, "MediaFy.exe");

    public static bool IsRegistered()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Scheme}");
            return k != null;
        }
        catch { return false; }
    }

    public static bool Register()
    {
        try
        {
            string exe = ExePath;
            string cmd = $"\"{exe}\" \"%1\"";
            string icon = $"\"{exe}\",0";

            // 1) Esquema clásico: HKCU\Software\Classes\mediafy
            using (var root = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}")!)
            {
                root.SetValue("", "URL:MediaFy Protocol");
                root.SetValue("URL Protocol", "");
                using var di = root.CreateSubKey("DefaultIcon")!; di.SetValue("", icon);
                using var c  = root.CreateSubKey(@"shell\open\command")!; c.SetValue("", cmd);
            }

            // 2) ProgId formal (lo que Windows 11 prefiere usar al elegir handler)
            using (var prog = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}")!)
            {
                prog.SetValue("", "MediaFy URL");
                prog.SetValue("FriendlyTypeName", "MediaFy by CG");
                using var di = prog.CreateSubKey("DefaultIcon")!; di.SetValue("", icon);
                using var c  = prog.CreateSubKey(@"shell\open\command")!; c.SetValue("", cmd);
            }

            // 3) Capabilities + RegisteredApplications: hace que Windows 11
            //    asocie MediaFy como handler oficial del esquema sin preguntar.
            using (var caps = Registry.CurrentUser.CreateSubKey(@"Software\MediaFy\Capabilities")!)
            {
                caps.SetValue("ApplicationName", AppName);
                caps.SetValue("ApplicationDescription", "Gestor de descargas multiplataforma");
                caps.SetValue("ApplicationIcon", icon);
                using var ua = caps.CreateSubKey("UrlAssociations")!;
                ua.SetValue(Scheme, ProgId);
            }
            using (var ra = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications")!)
            {
                ra.SetValue(AppName, @"Software\MediaFy\Capabilities");
            }

            return true;
        }
        catch { return false; }
    }

    public static bool Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{Scheme}",  throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\MediaFy",           throwOnMissingSubKey: false);
            try
            {
                using var ra = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true);
                ra?.DeleteValue(AppName, throwOnMissingValue: false);
            }
            catch { }
            return true;
        }
        catch { return false; }
    }
}
