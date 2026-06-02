using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json;

namespace YTDownloader.Core;

/// <summary>Cómo reproducir un archivo desde la Biblioteca.</summary>
public enum PlayerMode { Ask, Integrated, System }

/// <summary>Cómo guardar el resultado de un corte.</summary>
public enum CutSaveMode { Ask, NewCopy, Replace }

/// <summary>Material de fondo de la ventana (telón de fondo).</summary>
public enum BackdropKind { Mica, MicaAlt, Acrylic, AcrylicThin, None }

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
    [ObservableProperty] private BackdropKind _backdropKind = BackdropKind.Mica;
    [ObservableProperty] private Dictionary<string, bool> _enabledPlatforms = DefaultPlatforms();
    [ObservableProperty] private string _spotifyClientId = string.Empty;
    [ObservableProperty] private string _spotifyClientSecret = string.Empty;
    [ObservableProperty] private bool _libraryShowCovers = true;
    [ObservableProperty] private bool _autoUpdateYtDlp = true;
    [ObservableProperty] private bool _urlProtocolRegistered;
    [ObservableProperty] private bool _notifyOnComplete = true;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private bool _autoCheckUpdates = true;
    /// <summary>Si está activo, MediaFy detecta URLs copiadas al portapapeles y ofrece descargarlas.</summary>
    [ObservableProperty] private bool _clipboardWatch;
    /// <summary>True una vez que el tutorial de bienvenida se completa por primera vez.</summary>
    [ObservableProperty] private bool _welcomeShown;

    /// <summary>Última carpeta del panel destino del organizador (para recordarla entre sesiones).</summary>
    [ObservableProperty] private string _organizerRightPath = string.Empty;

    // ── Preferencias de descarga predeterminadas ────────────────
    /// <summary>Formato predeterminado para nuevas descargas (MP4, MKV, MP3, FLAC…).</summary>
    [ObservableProperty] private string _defaultFormat = "MP4";
    /// <summary>Calidad predeterminada para nuevas descargas (Mejor, 1080p, 720p…).</summary>
    [ObservableProperty] private string _defaultQuality = "Mejor";
    /// <summary>Subtítulos predeterminados (Off, Auto, ES, EN).</summary>
    [ObservableProperty] private string _defaultSubtitles = "Off";

    private static Dictionary<string, bool> DefaultPlatforms() => new()
    {
        [nameof(Platform.YouTube)]     = true,
        [nameof(Platform.SoundCloud)]  = true,
        [nameof(Platform.TikTok)]      = true,
        [nameof(Platform.Instagram)]   = true,
        [nameof(Platform.TwitterX)]    = true,
        [nameof(Platform.Vimeo)]       = true,
        [nameof(Platform.Twitch)]      = true,
        [nameof(Platform.Facebook)]    = true,
        [nameof(Platform.Dailymotion)] = true,
        [nameof(Platform.Bandcamp)]    = true,
        [nameof(Platform.Other)]       = true,
        [nameof(Platform.Spotify)]     = false, // se habilita al configurarlo (paso 2)
    };

    public bool IsPlatformEnabled(Platform p) =>
        EnabledPlatforms.TryGetValue(p.ToString(), out var v) ? v : true;

    public void SetPlatformEnabled(Platform p, bool enabled)
    {
        EnabledPlatforms[p.ToString()] = enabled;
        OnPropertyChanged(nameof(EnabledPlatforms)); // dispara el guardado
    }
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
