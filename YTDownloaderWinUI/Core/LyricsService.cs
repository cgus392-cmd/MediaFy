using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace YTDownloader.Core;

/// <summary>Una línea de letra sincronizada (tiempo + texto). IsCurrent lo mueve el reproductor.</summary>
public partial class LyricLineVm : ObservableObject
{
    public TimeSpan Time { get; }
    public string Text { get; }
    [ObservableProperty] private bool _isCurrent;

    public LyricLineVm(TimeSpan time, string text) { Time = time; Text = text; }
}

/// <summary>
/// Descarga letras sincronizadas desde lrclib.net (gratis, sin token) y las parsea a líneas
/// con timestamp (formato LRC) para el modo karaoke.
/// </summary>
public static class LyricsService
{
    private static readonly HttpClient Http = Create();
    private static HttpClient Create()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        // lrclib pide un User-Agent identificable.
        h.DefaultRequestHeaders.UserAgent.ParseAdd("MediaFy (CG LABS; https://github.com/cgus392-cmd/MediaFy)");
        return h;
    }

    // Cache simple del último resultado (evita re-descargar al reabrir la misma canción).
    private static string _cacheKey = "";
    private static List<LyricLineVm>? _cacheLines;

    /// <summary>
    /// Devuelve la letra sincronizada de la canción. Lee las etiquetas ID3 del archivo local
    /// (artista/título/álbum/duración) para identificarla bien —incluso en álbumes con pistas
    /// numeradas "07…"— y usa el match EXACTO de lrclib; cae a búsqueda por texto si no hay match.
    /// </summary>
    public static async Task<List<LyricLineVm>?> FetchAsync(
        string? filePath, string fallbackTitle, string? fallbackArtist, double? durationSec,
        CancellationToken ct = default)
    {
        string title  = fallbackTitle;
        string artist = fallbackArtist ?? "";
        string album  = "";
        double duration = durationSec ?? 0;

        // Etiquetas ID3 del archivo local: la clave para acertar la canción real.
        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
        {
            try
            {
                using var f = TagLib.File.Create(filePath);
                if (!string.IsNullOrWhiteSpace(f.Tag.Title))  title  = f.Tag.Title.Trim();
                string? perf = f.Tag.FirstPerformer ?? f.Tag.FirstAlbumArtist;
                if (!string.IsNullOrWhiteSpace(perf))          artist = perf.Trim();
                if (!string.IsNullOrWhiteSpace(f.Tag.Album))   album  = f.Tag.Album.Trim();
                double d = f.Properties?.Duration.TotalSeconds ?? 0;
                if (d > 0) duration = d;
            }
            catch { /* archivo sin tags legibles → seguimos con lo que haya */ }
        }

        string cacheKey = $"{artist}|{title}|{album}|{(int)duration}";
        if (_cacheKey == cacheKey) return _cacheLines;

        List<LyricLineVm>? result = null;

        // 1) Match EXACTO (artista + título + duración) — la vía precisa de lrclib.
        if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(title))
            result = await GetExactAsync(title, artist, album, duration, ct);

        // 2) Fallback: búsqueda por texto (sin tags, o si el exacto no acertó).
        result ??= await SearchAsync(BuildQuery(title, artist), ct);

        _cacheKey = cacheKey; _cacheLines = result;
        return result;
    }

    private static async Task<List<LyricLineVm>?> GetExactAsync(
        string title, string artist, string album, double duration, CancellationToken ct)
    {
        try
        {
            string url = "https://lrclib.net/api/get"
                       + $"?artist_name={Uri.EscapeDataString(artist)}"
                       + $"&track_name={Uri.EscapeDataString(title)}";
            if (!string.IsNullOrWhiteSpace(album)) url += $"&album_name={Uri.EscapeDataString(album)}";
            if (duration > 0)                      url += $"&duration={(int)Math.Round(duration)}";

            using var resp = await Http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null; // 404 = no hay match exacto

            var o = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
            string? synced = o["syncedLyrics"]?.ToString();
            if (string.IsNullOrWhiteSpace(synced)) return null;
            var lines = ParseLrc(synced);
            return lines.Count > 0 ? lines : null;
        }
        catch { return null; }
    }

    private static async Task<List<LyricLineVm>?> SearchAsync(string query, CancellationToken ct)
    {
        try
        {
            string url = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(query)}";
            string json = await Http.GetStringAsync(url, ct);
            var arr = JArray.Parse(json);
            foreach (var it in arr)
            {
                string? synced = it["syncedLyrics"]?.ToString();
                if (string.IsNullOrWhiteSpace(synced)) continue;
                var lines = ParseLrc(synced);
                if (lines.Count > 0) return lines;
            }
        }
        catch { }
        return null;
    }

    // Limpia el título (quita "(Official Video)", "[Audio]", etc.) para mejorar el match.
    private static string BuildQuery(string title, string? artist)
    {
        string t = Regex.Replace(title, @"[\(\[\{].*?[\)\]\}]", " ");         // quita paréntesis/corchetes
        t = Regex.Replace(t, @"(?i)\b(official|video|audio|lyric[s]?|hd|4k|mv)\b", " ");
        t = Regex.Replace(t, @"\s+", " ").Trim(' ', '-', '·', '|');
        if (!string.IsNullOrWhiteSpace(artist) && !t.Contains(artist, StringComparison.OrdinalIgnoreCase))
            t = $"{artist} {t}";
        return t;
    }

    private static readonly Regex LrcTag = new(@"\[(\d{1,2}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);

    private static List<LyricLineVm> ParseLrc(string lrc)
    {
        var lines = new List<LyricLineVm>();
        foreach (var raw in lrc.Split('\n'))
        {
            var matches = LrcTag.Matches(raw);
            if (matches.Count == 0) continue;
            string text = LrcTag.Replace(raw, "").Trim();
            foreach (Match m in matches)
            {
                int min = int.Parse(m.Groups[1].Value);
                int sec = int.Parse(m.Groups[2].Value);
                int frac = 0;
                if (m.Groups[3].Success)
                {
                    string f = m.Groups[3].Value.PadRight(3, '0')[..3];
                    frac = int.Parse(f);
                }
                var time = new TimeSpan(0, 0, min, sec, frac);
                lines.Add(new LyricLineVm(time, text)); // texto vacío = pausa instrumental
            }
        }
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines;
    }
}
