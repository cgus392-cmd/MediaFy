using System.IO;

namespace YTDownloader.Core;

/// <summary>
/// Orquesta las fuentes de letras. Identifica la canción por las etiquetas del archivo y consulta
/// los proveedores en el orden de preferencia del usuario, quedándose con la primera letra válida.
///
/// Ninguna fuente requiere clave ni cuenta de pago: KuGou aporta sincronización por palabra y
/// LRCLIB una cobertura muy amplia por línea.
/// </summary>
public static class LyricsService
{
    /// <summary>Todas las fuentes disponibles (el orden de uso lo decide el usuario).</summary>
    public static IReadOnlyList<ILyricsProvider> AllProviders { get; } = new ILyricsProvider[]
    {
        new KuGouProvider(),
        new LrcLibProvider(),
    };

    /// <summary>Nombre del proveedor que sirvió la última letra (para mostrarlo en la UI).</summary>
    public static string? LastProviderUsed { get; private set; }
    /// <summary>True si la última letra trae tiempos por palabra.</summary>
    public static bool LastHadWords { get; private set; }

    private static string _cacheKey = "";
    private static List<LyricLineVm>? _cacheLines;
    private static string? _cacheProvider;
    private static bool _cacheWords;

    /// <summary>Proveedores en el orden configurado, ignorando los desactivados.</summary>
    public static IEnumerable<ILyricsProvider> OrderedProviders()
    {
        var order = AppSettings.Current.LyricsProviderOrder;
        if (order is { Count: > 0 })
        {
            foreach (var name in order)
            {
                var p = AllProviders.FirstOrDefault(
                    x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (p != null) yield return p;
            }
            // Fuentes nuevas que aún no estén en la lista guardada: van al final.
            foreach (var p in AllProviders)
                if (!order.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) yield return p;
        }
        else foreach (var p in AllProviders) yield return p;
    }

    /// <summary>
    /// Devuelve la letra sincronizada de la canción. Lee las etiquetas ID3 del archivo local
    /// (artista/título/álbum/duración) para identificarla bien —incluso en álbumes con pistas
    /// numeradas "07…"— y prueba las fuentes en orden hasta encontrar una con letra.
    /// </summary>
    public static async Task<List<LyricLineVm>?> FetchAsync(
        string? filePath, string fallbackTitle, string? fallbackArtist, double? durationSec,
        CancellationToken ct = default)
    {
        var track = BuildTrackInfo(filePath, fallbackTitle, fallbackArtist, durationSec);

        string cacheKey = $"{track.Artist}|{track.Title}|{track.Album}|{(int)track.DurationSeconds}";
        if (_cacheKey == cacheKey)
        {
            LastProviderUsed = _cacheProvider;
            LastHadWords = _cacheWords;
            return _cacheLines;
        }

        LyricsResult? found = null;
        foreach (var provider in OrderedProviders())
        {
            if (ct.IsCancellationRequested) return null;
            try
            {
                found = await provider.FetchAsync(track, ct);
                if (found is not null) break;
            }
            catch { /* una fuente caída no debe impedir probar la siguiente */ }
        }

        _cacheKey = cacheKey;
        _cacheLines = found?.Lines as List<LyricLineVm> ?? found?.Lines.ToList();
        _cacheProvider = LastProviderUsed = found?.ProviderName;
        _cacheWords = LastHadWords = found?.HasWords ?? false;
        return _cacheLines;
    }

    /// <summary>Identifica la canción: las etiquetas del archivo mandan sobre el nombre del fichero.</summary>
    private static TrackInfo BuildTrackInfo(
        string? filePath, string fallbackTitle, string? fallbackArtist, double? durationSec)
    {
        string title = fallbackTitle;
        string artist = fallbackArtist ?? "";
        string album = "";
        double duration = durationSec ?? 0;

        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            try
            {
                using var f = TagLib.File.Create(filePath);
                if (!string.IsNullOrWhiteSpace(f.Tag.Title)) title = f.Tag.Title.Trim();
                string? perf = f.Tag.FirstPerformer ?? f.Tag.FirstAlbumArtist;
                if (!string.IsNullOrWhiteSpace(perf)) artist = perf.Trim();
                if (!string.IsNullOrWhiteSpace(f.Tag.Album)) album = f.Tag.Album.Trim();
                double d = f.Properties?.Duration.TotalSeconds ?? 0;
                if (d > 0) duration = d;
            }
            catch { /* archivo sin etiquetas legibles → seguimos con lo que haya */ }
        }

        return new TrackInfo(title, artist, album, duration);
    }
}
