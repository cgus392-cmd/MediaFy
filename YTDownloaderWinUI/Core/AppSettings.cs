using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace YTDownloader.Core;

/// <summary>Cómo reproducir un archivo desde la Biblioteca.</summary>
public enum PlayerMode { Ask, Integrated, System }

/// <summary>Cómo guardar el resultado de un corte.</summary>
public enum CutSaveMode { Ask, NewCopy, Replace }

/// <summary>
/// Configuración persistente de la app. Singleton accesible desde XAML vía
/// AppSettings.Current. Se guarda en %LocalAppData%\YTDownloader\settings.json.
/// </summary>
public partial class AppSettings : ObservableObject
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YTDownloader");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public static AppSettings Current { get; } = Load();

    [ObservableProperty] private bool _showLogs;
    [ObservableProperty] private int _maxConcurrent = 3;
    [ObservableProperty] private int _cascadeThreshold = 70;
    [ObservableProperty] private PlayerMode _playerMode = PlayerMode.Ask;
    [ObservableProperty] private CutSaveMode _cutSaveMode = CutSaveMode.Ask;
    [ObservableProperty] private string _outputFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "YTDownloader");

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var s = JsonConvert.DeserializeObject<AppSettings>(File.ReadAllText(SettingsPath));
                if (s != null) { s.HookSave(); return s; }
            }
        }
        catch { /* config corrupta → usar defaults */ }

        var def = new AppSettings();
        def.HookSave();
        return def;
    }

    private void HookSave()
    {
        PropertyChanged += (_, _) => Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsPath, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
        catch { /* sin permisos de escritura → ignorar */ }
    }
}
