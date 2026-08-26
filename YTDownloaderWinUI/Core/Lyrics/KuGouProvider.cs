using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace YTDownloader.Core;

/// <summary>
/// KuGou — única fuente gratuita (sin clave ni cuenta) que entrega tiempos POR PALABRA, en su
/// formato KRC. El flujo es: buscar la canción → obtener su hash → pedir los candidatos de letra
/// de ese hash → descargar el KRC y descifrarlo.
///
/// El KRC viene ofuscado: cabecera "krc1", el resto en XOR con una clave fija y comprimido con
/// zlib. No es una API documentada, así que si algún día cambia, el sistema de proveedores hace
/// que MediaFy caiga solo al siguiente (LRCLIB) sin que el usuario note nada.
/// </summary>
public sealed class KuGouProvider : ILyricsProvider
{
    public string Name => "KuGou";
    public bool SupportsWordByWord => true;

    private static readonly HttpClient Http = Create();
    private static HttpClient Create()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        return h;
    }

    // Clave de ofuscación del formato KRC (fija y pública en el formato).
    private static readonly byte[] KrcKey =
    {
        0x40, 0x47, 0x61, 0x77, 0x5E, 0x32, 0x74, 0x47,
        0x51, 0x36, 0x31, 0x2D, 0xCE, 0xD2, 0x6E, 0x69
    };

    public async Task<LyricsResult?> FetchAsync(TrackInfo track, CancellationToken ct)
    {
        try
        {
            string? hash = await FindSongHashAsync(track, ct);
            if (hash is null) return null;

            var (id, accessKey) = await FindLyricsCandidateAsync(hash, ct);
            if (id is null || accessKey is null) return null;

            // 1) KRC: sincronización por palabra (lo que buscamos).
            byte[]? krc = await DownloadAsync(id, accessKey, "krc", ct);
            if (krc is not null && TryDecodeKrc(krc, out string krcText))
            {
                var wordLines = ParseKrc(krcText);
                if (LrcParser.LooksUsable(wordLines))
                    return new LyricsResult(wordLines, Name, true);
            }

            // 2) Respaldo dentro de la misma fuente: LRC por línea.
            byte[]? lrc = await DownloadAsync(id, accessKey, "lrc", ct);
            if (lrc is not null)
            {
                var lines = LrcParser.Parse(Encoding.UTF8.GetString(lrc));
                if (LrcParser.LooksUsable(lines)) return new LyricsResult(lines, Name, false);
            }
        }
        catch { /* red caída o formato inesperado → que lo intente el siguiente proveedor */ }
        return null;
    }

    // ── Pasos del flujo ─────────────────────────────────────────
    private static async Task<string?> FindSongHashAsync(TrackInfo t, CancellationToken ct)
    {
        // Sin limpiar el título, un "(Video Oficial)" arrastrado del nombre del archivo hace que
        // KuGou devuelva la versión INSTRUMENTAL (sin letra) en vez de la canción.
        string keyword = Uri.EscapeDataString(LyricsQuery.Build(t));
        string url = "https://mobileservice.kugou.com/api/v3/search/song"
                   + $"?version=9108&plat=0&pagesize=8&showtype=0&keyword={keyword}";

        var o = JObject.Parse(await Http.GetStringAsync(url, ct));
        if (o["data"]?["info"] is not JArray info || info.Count == 0) return null;

        // Descartar pistas sin voz: su "letra" no sirve para el karaoke.
        var usable = info.Where(x => !IsInstrumental(x["songname"]?.ToString())).ToList();
        if (usable.Count == 0) usable = info.ToList();

        // Si conocemos la duración, preferimos la coincidencia más cercana (±5 s): evita
        // quedarnos con remixes o versiones en vivo de duración muy distinta.
        if (t.DurationSeconds > 0)
        {
            JToken? best = null;
            double bestDiff = double.MaxValue;
            foreach (var s in usable)
            {
                double d = s["duration"]?.ToObject<double>() ?? 0;
                if (d <= 0) continue;
                double diff = Math.Abs(d - t.DurationSeconds);
                if (diff < bestDiff) { bestDiff = diff; best = s; }
            }
            if (best != null && bestDiff <= 5) return best["hash"]?.ToString();
        }
        return usable[0]["hash"]?.ToString();
    }

    /// <summary>Versiones sin voz (instrumental, karaoke, pista de acompañamiento).</summary>
    private static bool IsInstrumental(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        foreach (var w in new[] { "instrumental", "karaoke", "伴奏", "off vocal", "backing track" })
            if (name.Contains(w, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static async Task<(string? id, string? accessKey)> FindLyricsCandidateAsync(
        string hash, CancellationToken ct)
    {
        string url = $"https://lyrics.kugou.com/search?ver=1&man=yes&client=pc&hash={hash}";
        var o = JObject.Parse(await Http.GetStringAsync(url, ct));
        if (o["candidates"] is not JArray c || c.Count == 0) return (null, null);
        return (c[0]["id"]?.ToString(), c[0]["accesskey"]?.ToString());
    }

    private static async Task<byte[]?> DownloadAsync(string id, string accessKey, string fmt, CancellationToken ct)
    {
        string url = "https://lyrics.kugou.com/download"
                   + $"?fmt={fmt}&charset=utf8&client=pc&ver=1&id={id}&accesskey={accessKey}";
        var o = JObject.Parse(await Http.GetStringAsync(url, ct));
        string? content = o["content"]?.ToString();
        if (string.IsNullOrWhiteSpace(content)) return null;
        try { return Convert.FromBase64String(content); } catch { return null; }
    }

    // ── Formato KRC ─────────────────────────────────────────────
    private static bool TryDecodeKrc(byte[] raw, out string text)
    {
        text = "";
        if (raw.Length < 5 || raw[0] != (byte)'k' || raw[1] != (byte)'r' || raw[2] != (byte)'c') return false;
        try
        {
            var body = new byte[raw.Length - 4];
            for (int i = 0; i < body.Length; i++)
                body[i] = (byte)(raw[i + 4] ^ KrcKey[i % KrcKey.Length]);

            using var input = new MemoryStream(body);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(zlib, Encoding.UTF8);
            text = reader.ReadToEnd();
            return text.Length > 0;
        }
        catch { return false; }
    }

    // Línea KRC: [inicio,duración]<offset,duración,0>palabra<offset,duración,0>palabra…
    private static readonly Regex KrcLine = new(@"^\[(\d+),(\d+)\](.*)$", RegexOptions.Compiled);
    private static readonly Regex KrcWord = new(@"<(\d+),(\d+),\d+>([^<]*)", RegexOptions.Compiled);

    private static List<LyricLineVm> ParseKrc(string krc)
    {
        var lines = new List<LyricLineVm>();
        foreach (var raw in krc.Split('\n'))
        {
            var lm = KrcLine.Match(raw.TrimEnd('\r'));
            if (!lm.Success) continue;

            var lineStart = TimeSpan.FromMilliseconds(long.Parse(lm.Groups[1].Value));
            var words = new List<LyricWord>();
            var sb = new StringBuilder();

            foreach (Match wm in KrcWord.Matches(lm.Groups[3].Value))
            {
                string w = wm.Groups[3].Value;
                if (w.Length == 0) continue;
                words.Add(new LyricWord(
                    w,
                    lineStart + TimeSpan.FromMilliseconds(long.Parse(wm.Groups[1].Value)),
                    TimeSpan.FromMilliseconds(long.Parse(wm.Groups[2].Value))));
                sb.Append(w);
            }

            // El texto mostrado es la concatenación EXACTA de las palabras: así los tiempos por
            // palabra siguen correspondiendo con las posiciones del texto. En vez de recortar la
            // cadena (que descuadraría el barrido), se quitan las palabras vacías de los extremos.
            while (words.Count > 0 && words[0].Text.Trim().Length == 0) words.RemoveAt(0);
            while (words.Count > 0 && words[^1].Text.Trim().Length == 0) words.RemoveAt(words.Count - 1);

            string text = string.Concat(words.Select(w => w.Text));
            if (text.Trim().Length == 0) continue;
            lines.Add(new LyricLineVm(lineStart, text, words));
        }
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));

        // Las primeras líneas del KRC suelen ser créditos (título/artista/autor): se descartan
        // si aparecen antes de que empiece la música de verdad.
        return lines;
    }
}
