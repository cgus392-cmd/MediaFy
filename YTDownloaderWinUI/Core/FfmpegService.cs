using System.Diagnostics;
using System.IO;

namespace YTDownloader.Core;

public class FfmpegService
{
    private readonly string _ffmpegPath;

    public FfmpegService()
    {
        _ffmpegPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ffmpeg.exe");
    }

    public bool IsAvailable() => File.Exists(_ffmpegPath);

    /// <summary>
    /// Recorta [start, end] del archivo de entrada. Si replaceOriginal es true,
    /// sustituye el archivo original; si no, crea "<nombre>_corte.<ext>".
    /// Devuelve la ruta resultante.
    /// </summary>
    public async Task<string> TrimAsync(string input, TimeSpan start, TimeSpan end,
        bool replaceOriginal, CancellationToken ct = default)
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

        // -ss/-to con re-codificación ligera para cortes precisos en cualquier formato
        string args = $"-y -i \"{input}\" -ss {ss} -to {to} \"{tempOut}\"";

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
