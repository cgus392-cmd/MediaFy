namespace YTDownloader.Core;

public enum Platform
{
    YouTube, Spotify, SoundCloud, TikTok, Instagram, TwitterX,
    Facebook, Vimeo, Twitch, Dailymotion, Bandcamp, Other
}

/// <summary>
/// Detecta la plataforma de un enlace. Casi todas se descargan directo con yt-dlp
/// (~1000 sitios). Spotify es el caso especial: no es descarga directa (DRM), se
/// resuelve por coincidencia de metadatos con YouTube.
/// </summary>
public static class PlatformDetector
{
    public static Platform Detect(string url)
    {
        string u = url.ToLowerInvariant();
        if (u.Contains("youtube.com") || u.Contains("youtu.be")) return Platform.YouTube;
        if (u.Contains("spotify.com"))                            return Platform.Spotify;
        if (u.Contains("soundcloud.com"))                         return Platform.SoundCloud;
        if (u.Contains("tiktok.com"))                             return Platform.TikTok;
        if (u.Contains("instagram.com"))                          return Platform.Instagram;
        if (u.Contains("twitter.com") || u.Contains("x.com"))     return Platform.TwitterX;
        if (u.Contains("facebook.com") || u.Contains("fb.watch")) return Platform.Facebook;
        if (u.Contains("vimeo.com"))                              return Platform.Vimeo;
        if (u.Contains("twitch.tv"))                              return Platform.Twitch;
        if (u.Contains("dailymotion.com"))                        return Platform.Dailymotion;
        if (u.Contains("bandcamp.com"))                           return Platform.Bandcamp;
        return Platform.Other;
    }

    public static string Name(Platform p) => p switch
    {
        Platform.YouTube     => "YouTube",
        Platform.Spotify     => "Spotify",
        Platform.SoundCloud  => "SoundCloud",
        Platform.TikTok      => "TikTok",
        Platform.Instagram   => "Instagram",
        Platform.TwitterX    => "Twitter / X",
        Platform.Facebook    => "Facebook",
        Platform.Vimeo       => "Vimeo",
        Platform.Twitch      => "Twitch",
        Platform.Dailymotion => "Dailymotion",
        Platform.Bandcamp    => "Bandcamp",
        _                    => "Enlace"
    };

    /// <summary>Glifo Segoe Fluent: vídeo, música o globo (genérico web).</summary>
    public static string Glyph(Platform p) => char.ConvertFromUtf32(p switch
    {
        Platform.YouTube => 0xE714,                                          // video
        Platform.Spotify or Platform.SoundCloud or Platform.Bandcamp => 0xEC4F, // music note
        _ => 0xE774                                                          // globe
    });

    /// <summary>True si yt-dlp puede descargarla directamente (todo menos Spotify).</summary>
    public static bool IsDirect(Platform p) => p != Platform.Spotify;

    public static bool LooksLikeUrl(string s)
    {
        s = s.Trim();
        return (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) &&
               s.Contains('.');
    }
}
