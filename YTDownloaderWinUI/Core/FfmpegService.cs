using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace YTDownloader.Core;

public class FfmpegService
{
    private readonly string _ffmpegPath;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public FfmpegService()
    {
        _ffmpegPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ffmpeg.exe");
    }

    public bool IsAvailable() => File.Exists(_ffmpegPath);

    /// <summary>
    /// Genera una imagen PNG con la forma de onda del audio del archivo.
    /// Devuelve la ruta del PNG temporal.
    /// </summary>
    public async Task<string> GenerateWaveformAsync(string input, int width, int height,
        string colorHex, CancellationToken ct = default)
    {
        string outPng = Path.Combine(Path.GetTempPath(), $"ytwave_{Guid.NewGuid():N}.png");
        // showwavespic dibuja la onda completa; split_channels=0 = mezcla ambos canales en una onda
        string filter = $"showwavespic=s={width}x{height}:colors={colorHex}";
        string args = $"-y -i \"{input}\" -filter_complex \"{filter}\" -frames:v 1 \"{outPng}\"";
        await RunAsync(args, ct);
        return outPng;
    }

    /// <summary>
    /// Recorta [start, end] con fades opcionales. Si replaceOriginal, sustituye el original;
    /// si no, crea "&lt;nombre&gt;_corte.&lt;ext&gt;". Devuelve la ruta resultante.
    /// </summary>
    public async Task<string> TrimAsync(string input, TimeSpan start, TimeSpan end,
        bool replaceOriginal, double fadeIn, double fadeOut, bool isVideo,
        CancellationToken ct = default)
    {
        if (!File.Exists(input)) throw new FileNotFoundException("Archivo no encontrado", input);
        if (end <= start) throw new ArgumentException("El fin debe ser mayor que el inicio");

        string dir = Path.GetDirectoryName(input)!;
        string name = Path.GetFileNameWithoutExtension(input);
        string ext = Path.GetExtension(input);
        string tempOut = Path.Combine(dir, $"{name}_corte_tmp{ext}");
        string finalOut = replaceOriginal ? input : Path.Combine(dir, $"{name}_corte{ext}");

        string ss = start.ToString(@"hh\:mm\:ss\.fff");
        string to = end.ToString(@"hh\:mm\:ss\.fff");
        double dur = (end - start).TotalSeconds;

        // Construye filtros de fade (relativos al inicio del segmento recortado)
        var audio = new List<string>();
        if (fadeIn > 0)  audio.Add($"afade=t=in:st=0:d={fadeIn.ToString(Inv)}");
        if (fadeOut > 0) audio.Add($"afade=t=out:st={(dur - fadeOut).ToString(Inv)}:d={fadeOut.ToString(Inv)}");

        var video = new List<string>();
        if (isVideo)
        {
            if (fadeIn > 0)  video.Add($"fade=t=in:st=0:d={fadeIn.ToString(Inv)}");
            if (fadeOut > 0) video.Add($"fade=t=out:st={(dur - fadeOut).ToString(Inv)}:d={fadeOut.ToString(Inv)}");
        }

        string filters = "";
        if (audio.Count > 0) filters += $" -af \"{string.Join(",", audio)}\"";
        if (video.Count > 0) filters += $" -vf \"{string.Join(",", video)}\"";

        string args = $"-y -i \"{input}\" -ss {ss} -to {to}{filters} \"{tempOut}\"";
        await RunAsync(args, ct);

        if (replaceOriginal)
        {
            File.Delete(input);
            File.Move(tempOut, input);
            return input;
        }
        else
        {
            if (File.Exists(finalOut)) File.Delete(finalOut);
            File.Move(tempOut, finalOut);
            return finalOut;
        }
    }

    /// <summary>
    /// Exporta una mezcla de múltiples pistas (Stems) aplicando los volúmenes y muteos actuales.
    /// </summary>
    public async Task<string> ExportMixAsync(IReadOnlyList<StemTrack> tracks, string outPath, CancellationToken ct = default)
    {
        if (tracks.Count == 0) return outPath;

        var sb = new StringBuilder();
        var filter = new StringBuilder();
        int activeInputs = 0;
        
        for (int i = 0; i < tracks.Count; i++)
        {
            sb.Append($"-i \"{tracks[i].FilePath}\" ");
            
            // Calculamos el volumen real (0 si está silenciada o si hay otro track en solo)
            double vol = tracks[i].IsMuted ? 0 : 
                         (tracks[i].IsSoloedGlobally && !tracks[i].IsSoloActiveOnTrack) ? 0 : 
                         tracks[i].Volume;
            
            filter.Append($"[{i}:a]volume={vol.ToString(Inv)}[a{i}];");
            activeInputs++;
        }

        for (int i = 0; i < tracks.Count; i++)
        {
            filter.Append($"[a{i}]");
        }
        
        filter.Append($"amix=inputs={activeInputs}:duration=longest[aout]");

        string args = $"-y {sb.ToString().Trim()} -filter_complex \"{filter.ToString()}\" -map \"[aout]\" \"{outPath}\"";
        
        await RunAsync(args, ct);
        return outPath;
    }

    /// <summary>
    /// Extrae la portada (audio) o un fotograma (vídeo) a una imagen cacheada.
    /// Devuelve la ruta de la imagen o null si no hay portada.
    /// </summary>
    public async Task<string?> ExtractCoverAsync(string input, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(input)) return null;

            string cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "YTDownloader", "thumbs");
            Directory.CreateDirectory(cacheDir);

            var fi = new FileInfo(input);
            string key = $"{input}|{fi.LastWriteTimeUtc.Ticks}|{fi.Length}";
            string hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(key)));
            string outJpg = Path.Combine(cacheDir, hash + ".jpg");

            if (File.Exists(outJpg))
                return new FileInfo(outJpg).Length > 0 ? outJpg : null;

            string ext = Path.GetExtension(input).ToLowerInvariant();
            bool isVideo = ext is ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi";

            string args = isVideo
                ? $"-y -ss 1 -i \"{input}\" -frames:v 1 -vf scale=200:-1 \"{outJpg}\""
                : $"-y -i \"{input}\" -an -vframes 1 -vf scale=200:-1 \"{outJpg}\"";

            await RunQuietAsync(args, ct);
            return File.Exists(outJpg) && new FileInfo(outJpg).Length > 0 ? outJpg : null;
        }
        catch { return null; }
    }

    private async Task RunQuietAsync(string args, CancellationToken ct)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath, Arguments = args,
            RedirectStandardError = true, RedirectStandardOutput = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        proc.Start();
        ProcessTuning.RunInBackground(proc);
        await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
    }

    private async Task RunAsync(string args, CancellationToken ct)
    {
        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            Arguments = args,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        proc.Start();
        ProcessTuning.RunInBackground(proc);
        string err = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
            throw new Exception($"ffmpeg falló: {err[..Math.Min(200, err.Length)]}");
    }
}
