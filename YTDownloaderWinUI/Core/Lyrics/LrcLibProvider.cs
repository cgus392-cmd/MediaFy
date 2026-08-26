using System.Net.Http;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace YTDownloader.Core;

/// <summary>
/// lrclib.net — base comunitaria de letras sincronizadas. API pública, gratuita y sin token.
/// Solo ofrece sincronización por línea (no por palabra), pero su cobertura es la mejor para
/// música occidental, así que es el respaldo natural.
/// </summary>
public sealed class LrcLibProvider : ILyricsProvider
{
    public string Name => "LRCLIB";
    public bool SupportsWordByWord => false;

    private static readonly HttpClient Http = Create();
    private static HttpClient Create()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        // lrclib pide un User-Agent identificable.
        h.DefaultRequestHeaders.UserAgent.ParseAdd("MediaFy (CG LABS; https://github.com/cgus392-cmd/MediaFy)");
        return h;
    }

    public async Task<LyricsResult?> FetchAsync(TrackInfo track, CancellationToken ct)
    {
        // 1) Coincidencia exacta (artista + título + álbum + duración): la vía precisa.
        var lines = await GetExactAsync(track, ct);

        // 2) Respaldo: búsqueda por texto (cuando el archivo no trae etiquetas fiables).
        lines ??= await SearchAsync(LyricsQuery.Build(track), ct);

        return lines is null ? null : new LyricsResult(lines, Name, false);
    }

    private static async Task<List<LyricLineVm>?> GetExactAsync(TrackInfo t, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(t.Artist) || string.IsNullOrWhiteSpace(t.Title)) return null;
        try
        {
            string url = "https://lrclib.net/api/get"
                       + $"?artist_name={Uri.EscapeDataString(t.Artist)}"
                       + $"&track_name={Uri.EscapeDataString(t.Title)}";
            if (!string.IsNullOrWhiteSpace(t.Album)) url += $"&album_name={Uri.EscapeDataString(t.Album)}";
            if (t.DurationSeconds > 0) url += $"&duration={(int)Math.Round(t.DurationSeconds)}";

            using var resp = await Http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;   // 404 = sin coincidencia exacta

            var o = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
            return FromSynced(o["syncedLyrics"]?.ToString());
        }
        catch { return null; }
    }

    private static async Task<List<LyricLineVm>?> SearchAsync(string query, CancellationToken ct)
    {
        try
        {
            string url = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(query)}";
            var arr = JArray.Parse(await Http.GetStringAsync(url, ct));
            foreach (var it in arr)
            {
                var lines = FromSynced(it["syncedLyrics"]?.ToString());
                if (lines != null) return lines;
            }
        }
        catch { }
        return null;
    }

    private static List<LyricLineVm>? FromSynced(string? synced)
    {
        if (string.IsNullOrWhiteSpace(synced)) return null;
        var lines = LrcParser.Parse(synced);
        return LrcParser.LooksUsable(lines) ? lines : null;
    }

}
