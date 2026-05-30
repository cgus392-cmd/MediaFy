using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using YTDownloader.Models;

namespace YTDownloader.Core;

/// <summary>
/// Obtiene metadatos de Spotify SIN credenciales ni Premium: lee los datos del
/// reproductor incrustado público (open.spotify.com/embed). No descarga audio:
/// solo título/artista/portada para luego buscar el equivalente en YouTube.
/// </summary>
public class SpotifyService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36");
        return c;
    }

    public async Task<List<SpotifyTrack>> ResolveAsync(string url, CancellationToken ct = default)
    {
        var (type, id) = Parse(url);
        string embed = $"https://open.spotify.com/embed/{type}/{id}";

        string html = await Http.GetStringAsync(embed, ct);
        string json = ExtractNextData(html) ?? throw new Exception("No se pudo leer la página de Spotify");
        var root = JObject.Parse(json);

        var entity = FindEntity(root) ?? throw new Exception("Datos de Spotify no encontrados");
        var list = new List<SpotifyTrack>();

        string albumName = Str(entity["title"] ?? entity["name"]);
        string fallbackCover = CoverOf(entity);

        if (entity["trackList"] is JArray tracks && tracks.Count > 0)
        {
            // Álbum o playlist
            foreach (var t in tracks)
            {
                list.Add(new SpotifyTrack
                {
                    Title = Str(t["title"] ?? t["name"]),
                    Artists = Str(t["subtitle"] ?? ArtistsToken(t)),
                    Album = albumName,
                    CoverUrl = CoverOf(t) is { Length: > 0 } c ? c : fallbackCover,
                    DurationMs = Int(t["duration"])
                });
            }
        }
        else
        {
            // Canción individual
            list.Add(new SpotifyTrack
            {
                Title = Str(entity["name"] ?? entity["title"]),
                Artists = Str(entity["subtitle"] ?? ArtistsToken(entity)),
                Album = albumName,
                CoverUrl = fallbackCover,
                DurationMs = Int(entity["duration"])
            });
        }

        list = list.Where(t => !string.IsNullOrWhiteSpace(t.Title)).ToList();
        if (list.Count == 0)
            throw new Exception("No se encontraron canciones en ese enlace");
        return list;
    }

    private static JToken? FindEntity(JObject root)
    {
        // Ruta conocida del embed
        var e = root.SelectToken("props.pageProps.state.data.entity");
        if (e != null) return e;
        // Búsqueda flexible: el primer objeto con trackList o con name+coverArt
        foreach (var tok in root.DescendantsAndSelf().OfType<JObject>())
        {
            if (tok["trackList"] is JArray) return tok;
        }
        foreach (var tok in root.DescendantsAndSelf().OfType<JObject>())
        {
            if (tok["name"] != null && (tok["coverArt"] != null || tok["subtitle"] != null)) return tok;
        }
        return null;
    }

    private static string CoverOf(JToken t)
    {
        // 1) coverArt.sources[].url
        if (t["coverArt"]?["sources"] is JArray sources && sources.Count > 0)
            return Largest(sources);
        // 2) visualIdentity.image[].url  (donde Spotify pone la portada en el embed)
        if (t["visualIdentity"]?["image"] is JArray img && img.Count > 0)
            return Largest(img);
        return string.Empty;
    }

    private static string Largest(JArray images)
    {
        var best = images.OfType<JObject>()
            .OrderByDescending(o => o["maxWidth"]?.ToObject<int>() ?? o["width"]?.ToObject<int>()
                                  ?? o["maxHeight"]?.ToObject<int>() ?? 0)
            .FirstOrDefault();
        return best?["url"]?.ToString() ?? images.Last()?["url"]?.ToString() ?? string.Empty;
    }

    private static string ArtistsToken(JToken t)
    {
        if (t["artists"] is JArray arr)
            return string.Join(", ", arr.Select(a => a["name"]?.ToString()).Where(s => !string.IsNullOrEmpty(s)));
        return string.Empty;
    }

    private static string Str(JToken? t) => t?.ToString() ?? string.Empty;
    private static int Int(JToken? t) => t?.ToObject<int>() ?? 0;

    private static string? ExtractNextData(string html)
    {
        int i = html.IndexOf("__NEXT_DATA__", StringComparison.Ordinal);
        if (i < 0) return null;
        int start = html.IndexOf('>', i);
        if (start < 0) return null;
        start++;
        int end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        if (end < 0) return null;
        return html[start..end].Trim();
    }

    private static (string type, string id) Parse(string url)
    {
        var m = Regex.Match(url, @"(track|album|playlist)[/:]([A-Za-z0-9]+)");
        if (!m.Success) throw new Exception("Enlace de Spotify no reconocido");
        return (m.Groups[1].Value, m.Groups[2].Value);
    }

    private static string Trunc(string? s, int n) =>
        string.IsNullOrEmpty(s) ? "(vacío)" : (s.Length > n ? s[..n] : s);

    private static void Log(string msg)
    {
        try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "spotify.log"), $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); }
        catch { }
    }
}
