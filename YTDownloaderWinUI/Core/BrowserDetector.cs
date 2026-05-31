using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace YTDownloader.Core;

public enum Browser { Edge, Chrome, Brave, Opera, None }

/// <summary>
/// Detecta el navegador del usuario y abre su página de extensiones.
/// Para apps unpackaged, la extensión se instala "modo desarrollador"
/// apuntando a la carpeta extension/ que viaja con MediaFy.
/// </summary>
public static class BrowserDetector
{
    public static string ExtensionFolder =>
        Path.Combine(AppContext.BaseDirectory, "extension");

    public static bool ExtensionAvailable() =>
        Directory.Exists(ExtensionFolder) &&
        File.Exists(Path.Combine(ExtensionFolder, "manifest.json"));

    public static Browser Detect()
    {
        if (IsInstalled("msedge.exe")) return Browser.Edge;
        if (IsInstalled("chrome.exe")) return Browser.Chrome;
        if (IsInstalled("brave.exe"))  return Browser.Brave;
        if (IsInstalled("opera.exe"))  return Browser.Opera;
        return Browser.None;
    }

    private static bool IsInstalled(string exe)
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
            if (k != null) return true;
            using var ku = Registry.CurrentUser.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}");
            return ku != null;
        }
        catch { return false; }
    }

    public static string ExtensionsUrl(Browser b) => b switch
    {
        Browser.Edge   => "edge://extensions",
        Browser.Chrome => "chrome://extensions",
        Browser.Brave  => "brave://extensions",
        Browser.Opera  => "opera://extensions",
        _              => "chrome://extensions"
    };

    /// <summary>
    /// Abre la página de extensiones en el navegador detectado.
    /// Como los esquemas chrome://, edge://, etc. solo los entiende el propio
    /// navegador, llamamos al exe directamente con la URL como argumento.
    /// </summary>
    public static void OpenExtensionsPage()
    {
        var b = Detect();
        string url = ExtensionsUrl(b);
        string? exe = b switch
        {
            Browser.Edge   => "msedge",
            Browser.Chrome => "chrome",
            Browser.Brave  => "brave",
            Browser.Opera  => "opera",
            _              => null
        };
        try
        {
            if (exe != null)
                Process.Start(new ProcessStartInfo { FileName = exe, Arguments = url, UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }

    public static void OpenExtensionFolder()
    {
        if (Directory.Exists(ExtensionFolder))
            Process.Start("explorer.exe", ExtensionFolder);
    }
}
