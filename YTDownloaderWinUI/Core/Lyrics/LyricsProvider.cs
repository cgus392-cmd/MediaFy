using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace YTDownloader.Core;

/// <summary>Una palabra con su tiempo de inicio y duración (karaoke a nivel de palabra).</summary>
public readonly record struct LyricWord(string Text, TimeSpan Start, TimeSpan Duration);

/// <summary>
/// Una línea de letra sincronizada. <see cref="Words"/> solo viene si la fuente ofrece tiempos
/// por palabra; si es null, el karaoke se aproxima repartiendo la línea en el tiempo.
/// </summary>
public partial class LyricLineVm : ObservableObject
{
    public TimeSpan Time { get; }
    public string Text { get; }
    public IReadOnlyList<LyricWord>? Words { get; }
    [ObservableProperty] private bool _isCurrent;

    public LyricLineVm(TimeSpan time, string text, IReadOnlyList<LyricWord>? words = null)
    {
        Time = time;
        Text = text;
        Words = words is { Count: > 0 } ? words : null;
    }
}

/// <summary>Datos con los que se identifica la canción ante los proveedores.</summary>
public record TrackInfo(string Title, string Artist, string Album, double DurationSeconds);

/// <summary>Letra encontrada por un proveedor, con la marca de quién la sirvió.</summary>
public record LyricsResult(IReadOnlyList<LyricLineVm> Lines, string ProviderName, bool HasWords);

/// <summary>
/// Una fuente de letras. Añadir una fuente nueva es implementar esta interfaz y registrarla en
/// <see cref="LyricsService"/>; el usuario decide el orden de preferencia desde Ajustes.
/// </summary>
public interface ILyricsProvider
{
    /// <summary>Nombre visible en Ajustes (y clave con la que se guarda el orden).</summary>
    string Name { get; }

    /// <summary>True si la fuente puede entregar tiempos por palabra (karaoke real).</summary>
    bool SupportsWordByWord { get; }

    /// <summary>Devuelve la letra sincronizada, o null si esta fuente no la tiene.</summary>
    Task<LyricsResult?> FetchAsync(TrackInfo track, CancellationToken ct);
}

/// <summary>
/// Normaliza los datos de la canción antes de consultar a las fuentes.
///
/// Los archivos descargados de YouTube arrastran títulos como "Tema (Video Oficial) | Álbum".
/// Buscar con esa cadena literal devuelve versiones instrumentales o directamente nada, así que
/// todas las fuentes deben partir del título limpio.
/// </summary>
public static class LyricsQuery
{
    private static readonly Regex Brackets = new(@"[\(\[\{].*?[\)\]\}]", RegexOptions.Compiled);
    private static readonly Regex Noise = new(
        @"(?i)\b(official|oficial|video|audio|lyric[s]?|letra|hd|4k|mv|visualizer|remaster(ed)?)\b",
        RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Título sin adornos de YouTube ni sufijos de álbum.</summary>
    public static string CleanTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        string s = Brackets.Replace(title, " ");
        s = Noise.Replace(s, " ");
        int bar = s.IndexOf('|');                  // "Tema | Álbum" → "Tema"
        if (bar > 0) s = s[..bar];
        return Spaces.Replace(s, " ").Trim(' ', '-', '·', '|', '"');
    }

    /// <summary>Consulta "artista título" lista para buscar en cualquier fuente.</summary>
    public static string Build(TrackInfo t)
    {
        string title = CleanTitle(t.Title);
        string artist = t.Artist?.Trim() ?? "";
        if (artist.Length > 0 && !title.Contains(artist, StringComparison.OrdinalIgnoreCase))
            return $"{artist} {title}".Trim();
        return title;
    }
}
