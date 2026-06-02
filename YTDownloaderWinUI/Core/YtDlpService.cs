using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using YTDownloader.Models;

namespace YTDownloader.Core;

public class YtDlpService
{
    private readonly string _ytDlpPath;
    private readonly string _ffmpegPath;

    // Plantilla de progreso máquina-legible: campos separados por '|' con prefijo fijo.
    private const string ProgressTemplate =
        "DLP|%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s|" +
        "%(progress._downloaded_bytes_str)s|%(progress._total_bytes_str)s";

    public YtDlpService()
    {
        string baseDir = AppContext.BaseDirectory;
        _ytDlpPath = Path.Combine(baseDir, "Assets", "yt-dlp.exe");
        _ffmpegPath = Path.Combine(baseDir, "Assets", "ffmpeg.exe");
    }

    public bool IsAvailable() => File.Exists(_ytDlpPath) && File.Exists(_ffmpegPath);

    /// <summary>Ejecuta yt-dlp -U (auto-actualización al arrancar).</summary>
    public async Task SelfUpdateAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_ytDlpPath)) return;
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = _ytDlpPath, Arguments = "-U --no-colors",
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            })!;
            await proc.WaitForExitAsync(ct);
        }
        catch { /* sin red, sin permisos, etc. */ }
    }

    public async Task<VideoInfo> GetVideoInfoAsync(string url, CancellationToken ct = default)
    {
        string json = await RunAsync(_ytDlpPath, $"--dump-json --no-playlist \"{url}\"", ct);
        var obj = JObject.Parse(json);

        var info = new VideoInfo
        {
            Title = obj["title"]?.ToString() ?? "Sin título",
            Thumbnail = obj["thumbnail"]?.ToString() ?? string.Empty,
            Duration = FormatDuration(obj["duration"]?.ToObject<int>() ?? 0),
            Uploader = obj["uploader"]?.ToString() ?? obj["channel"]?.ToString() ?? string.Empty,
            FileSizeApprox = obj["filesize_approx"]?.ToObject<long>() ?? 0
        };

        // Extrae alturas de vídeo disponibles de los formatos
        if (obj["formats"] is JArray formats)
        {
            var heights = new HashSet<int>();
            foreach (var f in formats)
            {
                if (f["vcodec"]?.ToString() is string vc && vc != "none" && vc.Length > 0)
                {
                    int h = f["height"]?.ToObject<int>() ?? 0;
                    if (h > 0) heights.Add(h);
                }
            }
            info.AvailableHeights = heights.OrderByDescending(h => h).ToList();
        }

        return info;
    }

    /// <summary>
    /// Busca en YouTube usando ytsearch de yt-dlp.
    /// Sin API key, sin costo, usa el yt-dlp ya bundled.
    /// </summary>
    public async Task<List<Models.SearchResultItem>> SearchAsync(
        string query, int count = 8, CancellationToken ct = default)
    {
        // --flat-playlist: no descarga info completa de cada video → ~2-4s en lugar de ~20s
        string args = $"--dump-json --flat-playlist \"ytsearch{count}:{query}\"";
        string output;
        try { output = await RunAsync(_ytDlpPath, args, ct); }
        catch { return new(); }

        var results = new List<Models.SearchResultItem>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var obj = JObject.Parse(line.Trim());
                string id = obj["id"]?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(id)) continue;

                string url = obj["url"]?.ToString()
                           ?? obj["webpage_url"]?.ToString()
                           ?? string.Empty;
                if (string.IsNullOrEmpty(url) || !url.StartsWith("http"))
                    url = $"https://www.youtube.com/watch?v={id}";

                // Thumbnail: usar la del JSON o construirla desde el id
                string thumb = obj["thumbnail"]?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(thumb) && !string.IsNullOrEmpty(id))
                    thumb = $"https://i.ytimg.com/vi/{id}/mqdefault.jpg";

                results.Add(new Models.SearchResultItem
                {
                    Url      = url,
                    Title    = obj["title"]?.ToString() ?? "Sin título",
                    Uploader = obj["uploader"]?.ToString()
                             ?? obj["channel"]?.ToString()
                             ?? string.Empty,
                    Duration = FormatDuration(obj["duration"]?.ToObject<int>() ?? 0),
                    Thumbnail = thumb,
                    Views    = FormatViews(obj["view_count"]?.ToObject<long>() ?? 0)
                });
            }
            catch { /* línea malformada, ignorar */ }
        }
        return results;
    }

    private static string FormatViews(long v)
    {
        if (v >= 1_000_000_000) return $"{v / 1_000_000_000.0:F1}B vistas";
        if (v >= 1_000_000)     return $"{v / 1_000_000.0:F1}M vistas";
        if (v >= 1_000)         return $"{v / 1_000.0:F0}K vistas";
        return v > 0 ? $"{v} vistas" : string.Empty;
    }

    public async Task DownloadAsync(
        DownloadItem item,
        string outputFolder,
        IProgress<DownloadProgress> progress,
        CancellationToken ct = default)
    {
        string formatArg = BuildFormatArg(item.Format, item.Quality);
        string ext = GetExtension(item.Format);
        string outputTemplate = Path.Combine(outputFolder, "%(title)s.%(ext)s");

        // Playlist: por defecto solo este vídeo; si el usuario quiere la lista completa, quitamos --no-playlist
        string playlistFlag = item.WholePlaylist ? string.Empty : "--no-playlist";

        // Subtítulos
        string subFlags = item.Subtitles switch
        {
            "Auto" => "--write-subs --embed-subs --sub-langs \"auto\"",
            "ES"   => "--write-subs --embed-subs --sub-langs \"es\"",
            "EN"   => "--write-subs --embed-subs --sub-langs \"en\"",
            "Todos"=> "--write-subs --embed-subs --sub-langs \"all\"",
            _      => string.Empty
        };

        string common =
            $"--ffmpeg-location \"{_ffmpegPath}\" " +
            $"--embed-thumbnail --embed-metadata {playlistFlag} {subFlags} --newline " +
            $"--progress-template \"{ProgressTemplate}\" " +
            $"-o \"{outputTemplate}\" \"{item.Url}\"";

        // Para formatos sin pérdida (FLAC/WAV) la calidad de audio no aplica, siempre mejor
        string audioQ = item.Format is "FLAC" or "WAV" ? "0" : AudioQuality(item.Quality);
        string args = item.IsAudio
            ? $"-x --audio-format {ext} --audio-quality {audioQ} --add-metadata {common}"
            : $"{formatArg} {common}";

        await RunWithProgressAsync(_ytDlpPath, args, progress, ct);
    }

    private static readonly HttpClient Http = new();

    /// <summary>
    /// Descarga de Spotify: busca el equivalente en YouTube (ytsearch1), descarga el audio
    /// como MP3 y lo re-etiqueta con los metadatos y portada reales de Spotify.
    /// </summary>
    public async Task DownloadFromSpotifyAsync(DownloadItem item, SpotifyTrack track,
        string outputFolder, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        string safe = Sanitize($"{track.Artists} - {track.Title}");
        string outNoExt = Path.Combine(outputFolder, safe);
        string mp3 = outNoExt + ".mp3";

        string query = $"ytsearch1:{track.Artists} {track.Title} audio";
        string args = $"-x --audio-format mp3 --audio-quality 0 " +
                      $"--ffmpeg-location \"{_ffmpegPath}\" --no-playlist --newline " +
                      $"--progress-template \"{ProgressTemplate}\" " +
                      $"-o \"{outNoExt}.%(ext)s\" \"{query}\"";

        await RunWithProgressAsync(_ytDlpPath, args, progress, ct);

        progress.Report(new DownloadProgress(-1, "Etiquetando con datos de Spotify..."));
        await TagSpotifyAsync(mp3, track, ct);
        item.OutputPath = mp3;
    }

    private async Task TagSpotifyAsync(string mp3, SpotifyTrack track, CancellationToken ct)
    {
        if (!File.Exists(mp3)) return;

        // Descarga la portada de Spotify
        string? cover = null;
        try
        {
            if (!string.IsNullOrEmpty(track.CoverUrl))
            {
                cover = Path.Combine(Path.GetTempPath(), $"cov_{Guid.NewGuid():N}.jpg");
                var bytes = await Http.GetByteArrayAsync(track.CoverUrl, ct);
                await File.WriteAllBytesAsync(cover, bytes, ct);
            }
        }
        catch { cover = null; }

        string temp = mp3 + ".tag.mp3";
        string meta = $"-metadata title=\"{Esc(track.Title)}\" -metadata artist=\"{Esc(track.Artists)}\" " +
                      $"-metadata album=\"{Esc(track.Album)}\"";
        string args = cover != null
            ? $"-y -i \"{mp3}\" -i \"{cover}\" -map 0:a -map 1 -c copy -id3v2_version 3 " +
              $"-metadata:s:v title=\"Album cover\" -metadata:s:v comment=\"Cover (front)\" {meta} \"{temp}\""
            : $"-y -i \"{mp3}\" -c copy -id3v2_version 3 {meta} \"{temp}\"";

        try
        {
            await RunFfmpegAsync(args, ct);
            if (File.Exists(temp)) { File.Delete(mp3); File.Move(temp, mp3); }
        }
        catch { if (File.Exists(temp)) try { File.Delete(temp); } catch { } }
        finally { if (cover != null) try { File.Delete(cover); } catch { } }
    }

    private async Task RunFfmpegAsync(string args, CancellationToken ct)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath, Arguments = args,
            RedirectStandardError = true, RedirectStandardOutput = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        proc.Start();
        await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
    }

    private static string Esc(string s) => s.Replace("\"", "'");
    private static string Sanitize(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Length > 120 ? s[..120] : s;
    }

    private static string BuildFormatArg(string format, string quality)
    {
        return format switch
        {
            "MP4" => quality switch
            {
                "4K"    => "-f \"bestvideo[height<=2160][ext=mp4]+bestaudio[ext=m4a]/best[height<=2160][ext=mp4]\"",
                "2K"    => "-f \"bestvideo[height<=1440][ext=mp4]+bestaudio[ext=m4a]/best[height<=1440][ext=mp4]\"",
                "1080p" => "-f \"bestvideo[height<=1080][ext=mp4]+bestaudio[ext=m4a]/best[height<=1080][ext=mp4]\"",
                "720p"  => "-f \"bestvideo[height<=720][ext=mp4]+bestaudio[ext=m4a]/best[height<=720][ext=mp4]\"",
                "480p"  => "-f \"bestvideo[height<=480][ext=mp4]+bestaudio[ext=m4a]/best[height<=480][ext=mp4]\"",
                "360p"  => "-f \"bestvideo[height<=360][ext=mp4]+bestaudio[ext=m4a]/best[height<=360][ext=mp4]\"",
                "240p"  => "-f \"bestvideo[height<=240][ext=mp4]+bestaudio[ext=m4a]/best[height<=240][ext=mp4]\"",
                _       => "-f \"bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]\"",
            },
            "MKV" => quality switch
            {
                "4K"    => "-f \"bestvideo[height<=2160]+bestaudio/best[height<=2160]\" --merge-output-format mkv",
                "2K"    => "-f \"bestvideo[height<=1440]+bestaudio/best[height<=1440]\" --merge-output-format mkv",
                "1080p" => "-f \"bestvideo[height<=1080]+bestaudio/best[height<=1080]\" --merge-output-format mkv",
                "720p"  => "-f \"bestvideo[height<=720]+bestaudio/best[height<=720]\" --merge-output-format mkv",
                "480p"  => "-f \"bestvideo[height<=480]+bestaudio/best[height<=480]\" --merge-output-format mkv",
                "360p"  => "-f \"bestvideo[height<=360]+bestaudio/best[height<=360]\" --merge-output-format mkv",
                "240p"  => "-f \"bestvideo[height<=240]+bestaudio/best[height<=240]\" --merge-output-format mkv",
                _       => "-f \"bestvideo+bestaudio/best\" --merge-output-format mkv",
            },
            "WEBM" => "-f \"bestvideo[ext=webm]+bestaudio[ext=webm]/best[ext=webm]\"",
            _ => "-f best"
        };
    }

    private static string AudioQuality(string quality) => quality switch
    {
        "320kbps" => "0",
        "256kbps" => "2",
        "192kbps" => "5",
        "128kbps" => "7",
        _ => "0"
    };

    private static string GetExtension(string format) => format.ToLower() switch
    {
        "mp3"  => "mp3",
        "m4a"  => "m4a",
        "ogg"  => "vorbis",
        "flac" => "flac",
        "wav"  => "wav",
        "opus" => "opus",
        "webm" => "webm",
        "mkv"  => "mkv",
        _      => "mp4"
    };

    private static string FormatDuration(int seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    private static async Task<string> RunAsync(string exe, string args, CancellationToken ct)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        proc.Start();
        string output = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
        {
            string err = await proc.StandardError.ReadToEndAsync(ct);
            throw new Exception($"yt-dlp error: {err}");
        }
        return output;
    }

    private static async Task RunWithProgressAsync(
        string exe, string args,
        IProgress<DownloadProgress> progress,
        CancellationToken ct)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        proc.Start();

        // Si se cancela, matamos el árbol de procesos de yt-dlp.
        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
            catch { /* ya terminó */ }
        });

        await Task.Run(async () =>
        {
            while (!proc.StandardOutput.EndOfStream)
            {
                string? line = await proc.StandardOutput.ReadLineAsync();
                if (line is null) break;
                ParseLine(line, progress);
            }
        });

        await proc.WaitForExitAsync(CancellationToken.None);

        if (ct.IsCancellationRequested)
            throw new OperationCanceledException(ct);

        if (proc.ExitCode != 0)
        {
            string err = await proc.StandardError.ReadToEndAsync();
            throw new Exception(string.IsNullOrWhiteSpace(err) ? "yt-dlp falló" : err.Trim());
        }
    }

    private static void ParseLine(string line, IProgress<DownloadProgress> progress)
    {
        // Línea de progreso máquina-legible: DLP|  45.2%| 1.23MiB/s|00:07|5.20MiB|12.34MiB
        if (line.StartsWith("DLP|"))
        {
            var p = line.Split('|');
            if (p.Length >= 6)
            {
                double pct = ParsePercent(p[1]);
                string speed = Clean(p[2]);
                string eta   = Clean(p[3]);
                string down  = Clean(p[4]);
                string total = Clean(p[5]);
                string sizeText = (down.Length > 0 && total.Length > 0 && total != "N/A")
                    ? $"{down} / {total}"
                    : down;
                progress.Report(new DownloadProgress(pct, $"Descargando {pct:F0}%", speed, eta, sizeText, line));
            }
            return;
        }

        // Fases de post-proceso
        if (line.Contains("[ExtractAudio]"))
            progress.Report(new DownloadProgress(-1, "Convirtiendo audio...", LogLine: line));
        else if (line.Contains("[Merger]"))
            progress.Report(new DownloadProgress(-1, "Uniendo pistas...", LogLine: line));
        else if (line.Contains("[EmbedThumbnail]"))
            progress.Report(new DownloadProgress(-1, "Incrustando portada...", LogLine: line));
        else if (line.Contains("[Metadata]"))
            progress.Report(new DownloadProgress(-1, "Escribiendo metadatos...", LogLine: line));
        else if (line.Contains("has already been downloaded"))
            progress.Report(new DownloadProgress(100, "Ya existe", LogLine: line));
        else
            // Log genérico (sin cambiar estado): percent NaN-like -2 = solo log
            progress.Report(new DownloadProgress(-2, string.Empty, LogLine: line));
    }

    private static double ParsePercent(string raw)
    {
        var m = Regex.Match(raw, @"([\d.]+)");
        return m.Success && double.TryParse(m.Groups[1].Value,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : -1;
    }

    private static string Clean(string s)
    {
        s = s.Trim();
        return (s == "N/A" || s == "Unknown" || s == "NA") ? string.Empty : s;
    }
}
