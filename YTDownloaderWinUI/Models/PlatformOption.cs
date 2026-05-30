using YTDownloader.Core;

namespace YTDownloader.Models;

/// <summary>Una plataforma mostrada en Configuración con su interruptor de activación.</summary>
public class PlatformOption
{
    public Platform Platform { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Glyph { get; init; } = string.Empty;
    public bool IsSpotify => Platform == Platform.Spotify;

    public bool IsEnabled
    {
        get => AppSettings.Current.IsPlatformEnabled(Platform);
        set => AppSettings.Current.SetPlatformEnabled(Platform, value);
    }

    public static List<PlatformOption> All() => new()
    {
        Make(Platform.YouTube,     "Vídeos, música y shorts de YouTube",        0xE714),
        Make(Platform.SoundCloud,  "Canciones y sets de SoundCloud",            0xEC4F),
        Make(Platform.Spotify,     "Spotify no permite descarga directa (DRM). La app obtiene los datos de la canción (título, artista, portada) y descarga el audio equivalente desde YouTube, etiquetándolo. Requiere configuración.", 0xEC4F),
        Make(Platform.TikTok,      "Vídeos de TikTok (sin marca de agua)",       0xE714),
        Make(Platform.Instagram,   "Reels y publicaciones de Instagram",        0xE714),
        Make(Platform.TwitterX,    "Vídeos de Twitter / X",                     0xE774),
        Make(Platform.Vimeo,       "Vídeos de Vimeo",                           0xE714),
        Make(Platform.Twitch,      "Clips y VODs de Twitch",                    0xE714),
        Make(Platform.Facebook,    "Vídeos de Facebook",                        0xE774),
        Make(Platform.Dailymotion, "Vídeos de Dailymotion",                     0xE714),
        Make(Platform.Bandcamp,    "Música de Bandcamp",                        0xEC4F),
    };

    private static PlatformOption Make(Platform p, string desc, int glyph) => new()
    {
        Platform = p,
        Name = PlatformDetector.Name(p),
        Description = desc,
        Glyph = char.ConvertFromUtf32(glyph)
    };
}
