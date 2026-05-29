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

    public YtDlpService()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _ytDlpPath = Path.Combine(baseDir, "Assets", "yt-dlp.exe");
        _ffmpegPath = Path.Combine(baseDir, "Assets", "ffmpeg.exe");
    }

    public bool IsAvailable() => File.Exists(_ytDlpPath) && File.Exists(_ffmpegPath);

    public async Task<VideoInfo> GetVideoInfoAsync(string url, CancellationToken ct = default)
    {
        string json = await RunAsync(_ytDlpPath, $"--dump-json --no-playlist \"{url}\"", ct);
        var obj = JObject.Parse(json);

        return new VideoInfo
        {
            Title = obj["title"]?.ToString() ?? "Sin título",
            Thumbnail = obj["thumbnail"]?.ToString() ?? string.Empty,
            Duration = FormatDuration(obj["duration"]?.ToObject<int>() ?? 0),
            Uploader = obj["uploader"]?.ToString() ?? string.Empty,
            FileSizeApprox = obj["filesize_approx"]?.ToObject<long>() ?? 0
        };
    }

    public async Task DownloadAsync(
        DownloadItem item,
        string outputFolder,
        IProgress<(double percent, string status)> progress,
        CancellationToken ct = default)
    {
        string formatArg = BuildFormatArg(item.Format, item.Quality);
        string ext = GetExtension(item.Format);
        string outputTemplate = Path.Combine(outputFolder, "%(title)s.%(ext)s");

        string args = $"{formatArg} " +
                      $"--ffmpeg-location \"{_ffmpegPath}\" " +
                      $"--embed-thumbnail " +
                      $"--embed-metadata " +
                      $"--no-playlist " +
                      $"--newline " +
                      $"-o \"{outputTemplate}\" " +
                      $"\"{item.Url}\"";

        if (item.IsAudio)
        {
            args = $"-x --audio-format {ext} --audio-quality 0 " +
                   $"--ffmpeg-location \"{_ffmpegPath}\" " +
                   $"--embed-thumbnail " +
                   $"--embed-metadata " +
                   $"--add-metadata " +
                   $"--no-playlist " +
                   $"--newline " +
                   $"-o \"{outputTemplate}\" " +
                   $"\"{item.Url}\"";
        }

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
                _       => "-f \"bestvideo[ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4]\"",
            },
            "WEBM" => "-f \"bestvideo[ext=webm]+bestaudio[ext=webm]/best[ext=webm]\"",
            _ => "-f best"
        };
    }

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
        IProgress<(double, string)> progress,
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

        var percentRegex = new Regex(@"\[download\]\s+([\d.]+)%");
        var destRegex    = new Regex(@"\[download\] Destination: (.+)");

        proc.Start();

        await Task.Run(async () =>
        {
            while (!proc.StandardOutput.EndOfStream)
            {
                string? line = await proc.StandardOutput.ReadLineAsync(ct);
                if (line is null) continue;

                var pm = percentRegex.Match(line);
                if (pm.Success && double.TryParse(pm.Groups[1].Value,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double pct))
                {
                    progress.Report((pct, $"Descargando {pct:F0}%"));
                }
                else if (line.Contains("[ExtractAudio]") || line.Contains("Destination:"))
                {
                    progress.Report((-1, "Convirtiendo..."));
                }
                else if (line.Contains("[Merger]"))
                {
                    progress.Report((-1, "Uniendo pistas..."));
                }
                else if (line.Contains("has already been downloaded"))
                {
                    progress.Report((100, "Ya existe"));
                }
            }
            await proc.WaitForExitAsync(ct);
        }, ct);

        if (proc.ExitCode != 0)
        {
            string err = await proc.StandardError.ReadToEndAsync();
            throw new Exception($"yt-dlp error: {err}");
        }
    }
}
