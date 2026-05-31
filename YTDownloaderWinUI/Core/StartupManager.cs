using System.IO;
using Microsoft.Win32;

namespace YTDownloader.Core;

/// <summary>
/// Gestiona el inicio automático con Windows usando HKCU\...\Run (sin admin).
/// Cuando MediaFy se inicia así, pasa el argumento --tray para arrancar oculto en bandeja.
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MediaFy";

    public static string ExePath => Path.Combine(AppContext.BaseDirectory, "MediaFy.exe");

    public static bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(ValueName) is string;
        }
        catch { return false; }
    }

    public static bool Enable()
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey)!;
            k.SetValue(ValueName, $"\"{ExePath}\" --tray");
            return true;
        }
        catch { return false; }
    }

    public static bool Disable()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            k?.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch { return false; }
    }
}
