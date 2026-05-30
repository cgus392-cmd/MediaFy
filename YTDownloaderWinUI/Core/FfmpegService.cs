using System.Diagnostics;
using System.Globalization;
using System.IO;

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
        string err = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
            throw new Exception($"ffmpeg falló: {err[..Math.Min(200, err.Length)]}");
    }
}
