using System.Diagnostics;
using System.IO;
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

    public async Task DownloadAsync(
        DownloadItem item,
        string outputFolder,
        IProgress<DownloadProgress> progress,
        CancellationToken ct = default)
    {
        string formatArg = BuildFormatArg(item.Format, item.Quality);
        string ext = GetExtension(item.Format);
        string outputTemplate = Path.Combine(outputFolder, "%(title)s.%(ext)s");

        string common =
            $"--ffmpeg-location \"{_ffmpegPath}\" " +
            $"--embed-thumbnail --embed-metadata --no-playlist --newline " +
            $"--progress-template \"{ProgressTemplate}\" " +
            $"-o \"{outputTemplate}\" \"{item.Url}\"";

        string args = item.IsAudio
            ? $"-x --audio-format {ext} --audio-quality {AudioQuality(item.Quality)} --add-metadata {common}"
            : $"{formatArg} {common}";

        await RunWithProgressAsync(_ytDlpPath, args, progress, ct);
    }

    private static string BuildFormatArg(string format, string quality)
    {
        return format switch
        {
            "MP4" => quality switch
            {
                "4K"    => "-f \"bestvideo[height<=2160][ext=mp4]+bestaudio[ext=m4a]/best[height<=2160][ext=mp4]\"",
                "1080p" => "-f \"bestvideo[height<=1080][ext=mp4]+bestaudio[ext=m4a]/best[height<=1080][ext=mp4]\"",
                "720p"  => "-f \"bestvideo[height<=720][ext=mp4]+bestaudio[ext=m4a]/best[height<=720][ext=mp4]\"",
                "480p"  => "-f \"bestvideo[height<=480][ext=mp4]+bestaudio[ext=m4a]/best[height<=480][ext=mp4]\"",
                "360p"  => "-f \"bestvideo[height<=360][ext=mp4]+bestaudio[ext=m4a]/best[height<=360][ext=mp4]\"",
                _       => "-f \"bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]\"",
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
        "mp3" => "mp3",
        "m4a" => "m4a",
        "ogg" => "vorbis",
        "webm" => "webm",
        _ => "mp4"
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
