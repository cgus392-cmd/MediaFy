using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace YTDownloader.Core;

public class StemService
{
    private static readonly HttpClient Http = new();
    
    // URL de Python portable standalone (Windows x64 CPython 3.10)
    private const string PythonUrl = "https://github.com/astral-sh/python-build-standalone/releases/download/20240224/cpython-3.10.13+20240224-x86_64-pc-windows-msvc-shared-install_only.tar.gz";
    
    private readonly string _engineDir;
    private readonly string _modelsDir;

    public StemService()
    {
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _engineDir = Path.Combine(localApp, "MediaFy", "stem-engine");
        _modelsDir = Path.Combine(_engineDir, "models");
    }

    /// <summary>
    /// Verifica si el entorno de Python y la biblioteca audio-separator están listos.
    /// </summary>
    public bool IsEngineInstalled()
    {
        try
        {
            if (!Directory.Exists(_engineDir)) return false;
            
            string pythonExe = GetPythonExePath();
            if (string.IsNullOrEmpty(pythonExe) || !File.Exists(pythonExe)) return false;

            // Intenta importar audio_separator para verificar que el paquete está instalado
            var psi = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = "-c \"import audio_separator\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            
            if (!proc.WaitForExit(4000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                return false;
            }
            
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Descarga e instala Python standalone y la biblioteca audio-separator.
    /// </summary>
    public async Task InstallEngineAsync(IProgress<(double pct, string msg)> progress, CancellationToken ct)
    {
        // 1. Crear directorios
        Directory.CreateDirectory(_engineDir);
        Directory.CreateDirectory(_modelsDir);

        string tempTarGz = Path.Combine(Path.GetTempPath(), $"python_standalone_{Guid.NewGuid():N}.tar.gz");

        try
        {
            // 2. Descargar Python portable (0% -> 40% del progreso total)
            progress.Report((5, "Descargando entorno de Python (35 MB)..."));
            using (var resp = await Http.GetAsync(PythonUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                resp.EnsureSuccessStatusCode();
                long totalBytes = resp.Content.Headers.ContentLength ?? 35_000_000;
                using (var src = await resp.Content.ReadAsStreamAsync(ct))
                using (var dst = File.Create(tempTarGz))
                {
                    var buffer = new byte[81920];
                    long received = 0;
                    int read;
                    while ((read = await src.ReadAsync(buffer, ct)) > 0)
                    {
                        await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                        received += read;
                        
                        double downloadPct = (double)received / totalBytes;
                        double totalPct = 5.0 + (downloadPct * 35.0); // mapea 0..100% a 5..40%
                        progress.Report((totalPct, $"Descargando Python: {totalPct:F0}% ({FormatBytes(received)} / {FormatBytes(totalBytes)})"));
                    }
                }
            }

            ct.ThrowIfCancellationRequested();

            // 3. Extraer el archivo (40% -> 60% del progreso total)
            progress.Report((40, "Extrayendo entorno de Python con tar..."));
            await ExtractTarGzAsync(tempTarGz, _engineDir, ct);
            
            ct.ThrowIfCancellationRequested();

            string pythonExe = GetPythonExePath();
            if (string.IsNullOrEmpty(pythonExe) || !File.Exists(pythonExe))
                throw new FileNotFoundException("No se encontró python.exe en el entorno extraído.");

            // 4. Asegurar pip (60% -> 70%)
            progress.Report((60, "Configurando administrador de paquetes (ensurepip)..."));
            await RunProcessAsync(pythonExe, "-m ensurepip", "Configurando pip...", progress, ct);

            // 5. Instalar audio-separator (70% -> 100%)
            bool hasNvidia = HardwareInfo.HasNvidiaGpu;
            if (hasNvidia)
            {
                progress.Report((70, "Instalando motor de IA con soporte GPU/CUDA (audio-separator[gpu])..."));
                try
                {
                    await RunProcessAsync(pythonExe, "-m pip install \"audio-separator[gpu]\"", "Instalando paquetes GPU (PyTorch/CUDA)... Esto puede tardar unos minutos.", progress, ct);
                }
                catch (Exception)
                {
                    progress.Report((80, "Error en instalación GPU. Retrocediendo a versión CPU..."));
                    // Fallback a CPU
                    await RunProcessAsync(pythonExe, "-m pip install \"audio-separator[cpu]\"", "Instalando paquetes CPU...", progress, ct);
                }
            }
            else
            {
                progress.Report((70, "Instalando motor de IA versión CPU (audio-separator[cpu])..."));
                await RunProcessAsync(pythonExe, "-m pip install \"audio-separator[cpu]\"", "Instalando paquetes CPU...", progress, ct);
            }

            progress.Report((100, "¡Motor de IA instalado con éxito!"));
        }
        finally
        {
            // Limpiar el tar.gz temporal
            try
            {
                if (File.Exists(tempTarGz)) File.Delete(tempTarGz);
            }
            catch { }
        }
    }

    /// <summary>
    /// Ejecuta la separación de pistas de audio de forma asíncrona.
    /// </summary>
    public async Task<string> SeparateAsync(string inputFile, int stemsCount, IProgress<(double pct, string msg)> progress, CancellationToken ct)
    {
        string pythonExe = GetPythonExePath();
        if (string.IsNullOrEmpty(pythonExe) || !File.Exists(pythonExe))
            throw new FileNotFoundException("El motor de IA no está instalado.");

        string libraryFolder = AppSettings.Current.OutputFolder;
        string songName = Path.GetFileNameWithoutExtension(inputFile);
        string albumFolder = Path.Combine(libraryFolder, "Stems de " + Sanitize(songName));
        
        Directory.CreateDirectory(albumFolder);
        Directory.CreateDirectory(_modelsDir);

        // Elegir el modelo correspondiente
        string modelFilename = stemsCount == 4 ? "htdemucs.yaml" : "UVR-MDX-NET-Inst_HQ_3.onnx";
        string modelLabel = stemsCount == 4 ? "Demucs (4 pistas)" : "MDX-Net (2 pistas)";

        progress.Report((5, $"Iniciando separación con {modelLabel}..."));

        // Preparar argumentos (usando python -c para importar y ejecutar main(), ya que -m solo define la función pero no la invoca)
        string args = $"-c \"from audio_separator.utils.cli import main; main()\" \"{inputFile}\" --model_filename \"{modelFilename}\" --output_dir \"{albumFolder}\" --model_file_dir \"{_modelsDir}\" --output_format wav";

        using var proc = new Process();
        proc.StartInfo = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Inyectar ffmpeg.exe y las DLLs de CUDA de PyTorch (para ONNX Runtime GPU) en el PATH de la ejecución
        string assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");
        string torchLibDir = Path.Combine(_engineDir, "python", "Lib", "site-packages", "torch", "lib");
        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        proc.StartInfo.EnvironmentVariables["PATH"] = assetsDir + Path.PathSeparator + torchLibDir + Path.PathSeparator + currentPath;

        proc.Start();

        // En caso de cancelación, matamos todo el árbol de procesos
        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); }
            catch { }
        });

        // Leer la salida estándar y de error para reportar logs y porcentaje de progreso
        var readOutputTask = Task.Run(async () =>
        {
            while (!proc.StandardOutput.EndOfStream)
            {
                string? line = await proc.StandardOutput.ReadLineAsync();
                if (line == null) break;
                ParseAndReportProgress(line, progress);
            }
        });

        var errorLog = new System.Text.StringBuilder();
        var readErrorTask = Task.Run(async () =>
        {
            while (!proc.StandardError.EndOfStream)
            {
                string? line = await proc.StandardError.ReadLineAsync();
                if (line == null) break;
                lock (errorLog)
                {
                    errorLog.AppendLine(line);
                }
                ParseAndReportProgress(line, progress);
            }
        });

        await Task.WhenAll(readOutputTask, readErrorTask);
        await proc.WaitForExitAsync(CancellationToken.None);

        if (ct.IsCancellationRequested)
            throw new OperationCanceledException(ct);

        if (proc.ExitCode != 0)
        {
            string errStr;
            lock (errorLog)
            {
                errStr = errorLog.ToString().Trim();
            }
            throw new Exception($"El proceso de separación falló con código de salida {proc.ExitCode}. Detalle:\n{errStr}");
        }

        // Renombrar los archivos de salida a nombres legibles en español
        progress.Report((95, "Renombrando pistas de salida..."));
        RenameStems(albumFolder);

        progress.Report((100, "¡Separación lista!"));
        return albumFolder;
    }

    private void ParseAndReportProgress(string line, IProgress<(double pct, string msg)> progress)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        // Intentar parsear el porcentaje (ej: 45% o [download] 20.3%)
        var match = Regex.Match(line, @"(\d+(?:\.\d+)?)%");
        if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pct))
        {
            // Mapeamos a un rango de 10% a 90% para no pisar el inicio/fin
            double mappedPct = 10.0 + (pct * 0.8);
            progress.Report((mappedPct, $"Procesando: {pct:F0}%"));
            return;
        }

        // Traducir mensajes comunes de audio-separator
        string statusText = line;
        if (line.Contains("Loading model", StringComparison.OrdinalIgnoreCase))
            statusText = "Cargando modelo de IA...";
        else if (line.Contains("Downloading model", StringComparison.OrdinalIgnoreCase))
            statusText = "Descargando archivos del modelo...";
        else if (line.Contains("Separation completed", StringComparison.OrdinalIgnoreCase))
            statusText = "Separación de pistas completada.";
        else if (line.Contains("Performing separation", StringComparison.OrdinalIgnoreCase))
            statusText = "Separando el audio...";
        
        // Log simple al progreso
        progress.Report((-1, statusText));
    }

    private void RenameStems(string folder)
    {
        if (!Directory.Exists(folder)) return;

        var files = Directory.GetFiles(folder);
        foreach (var file in files)
        {
            string name = Path.GetFileName(file);
            string ext = Path.GetExtension(file);
            string? newName = null;

            if (name.Contains("Vocals", StringComparison.OrdinalIgnoreCase))
                newName = "Voz" + ext;
            else if (name.Contains("Instrumental", StringComparison.OrdinalIgnoreCase))
                newName = "Musica" + ext;
            else if (name.Contains("Bass", StringComparison.OrdinalIgnoreCase))
                newName = "Bajo" + ext;
            else if (name.Contains("Drums", StringComparison.OrdinalIgnoreCase))
                newName = "Bateria" + ext;
            else if (name.Contains("Other", StringComparison.OrdinalIgnoreCase))
                newName = "Otros" + ext;
            else if (name.Contains("Guitar", StringComparison.OrdinalIgnoreCase))
                newName = "Guitarra" + ext;

            if (newName != null)
            {
                string newPath = Path.Combine(folder, newName);
                try
                {
                    if (File.Exists(newPath)) File.Delete(newPath);
                    File.Move(file, newPath);
                }
                catch { }
            }
        }
    }

    private string GetPythonExePath()
    {
        if (!Directory.Exists(_engineDir)) return string.Empty;
        var files = Directory.GetFiles(_engineDir, "python.exe", SearchOption.AllDirectories);
        return files.FirstOrDefault() ?? string.Empty;
    }

    private async Task ExtractTarGzAsync(string tarPath, string outputDir, CancellationToken ct)
    {
        string pythonDir = Path.Combine(outputDir, "python");
        if (Directory.Exists(pythonDir))
        {
            try
            {
                Directory.Delete(pythonDir, true);
            }
            catch (Exception)
            {
                // Si falla por archivos bloqueados, intentamos matar procesos python activos y reintentar
                try
                {
                    var processes = Process.GetProcessesByName("python");
                    foreach (var p in processes)
                    {
                        try { p.Kill(entireProcessTree: true); } catch { }
                    }
                    await Task.Delay(500, ct);
                    Directory.Delete(pythonDir, true);
                }
                catch (Exception deleteEx)
                {
                    throw new Exception($"No se pudo limpiar la instalación anterior de Python en '{pythonDir}'. Asegúrese de que la aplicación no esté usando el motor de IA en segundo plano. Detalle: {deleteEx.Message}", deleteEx);
                }
            }
        }

        // Usamos TarReader de forma manual para limpiar los nombres de archivo truncando en la primera marca nula (\0),
        // solucionando un bug conocido en System.Formats.Tar de .NET 8 con ciertos archivos PAX/USTAR.
        await Task.Run(async () =>
        {
            using var fs = File.OpenRead(tarPath);
            using var gzip = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress);
            using var reader = new System.Formats.Tar.TarReader(gzip);

            while (true)
            {
                var entry = await reader.GetNextEntryAsync(copyData: false, ct);
                if (entry == null) break;

                // Limpiar nombre del entry truncándolo en el carácter nulo (\0)
                string cleanName = entry.Name;
                int nullIdx = cleanName.IndexOf('\0');
                if (nullIdx >= 0)
                {
                    cleanName = cleanName.Substring(0, nullIdx);
                }

                // Evitar caracteres de navegación de ruta hacia atrás por seguridad
                cleanName = cleanName.Replace("..", "__");

                string destPath = Path.GetFullPath(Path.Combine(outputDir, cleanName));

                // Protección contra Path Traversal: asegurar que está dentro del directorio destino
                string fullOutputDir = Path.GetFullPath(outputDir);
                if (!destPath.StartsWith(fullOutputDir, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (entry.EntryType == System.Formats.Tar.TarEntryType.Directory)
                {
                    Directory.CreateDirectory(destPath);
                }
                else if (entry.EntryType == System.Formats.Tar.TarEntryType.RegularFile ||
                         entry.EntryType == System.Formats.Tar.TarEntryType.V7RegularFile)
                {
                    string? parentDir = Path.GetDirectoryName(destPath);
                    if (parentDir != null)
                    {
                        Directory.CreateDirectory(parentDir);
                    }

                    await entry.ExtractToFileAsync(destPath, overwrite: true, ct);
                }
            }
        }, ct);
    }

    private async Task RunProcessAsync(string exe, string arguments, string statusText, IProgress<(double pct, string msg)> progress, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) throw new Exception($"No se pudo iniciar {Path.GetFileName(exe)}.");

        using var reg = ct.Register(() => { try { proc.Kill(entireProcessTree: true); } catch { } });

        var stdOutTask = Task.Run(async () =>
        {
            var buffer = new char[256];
            while (true)
            {
                int read = await proc.StandardOutput.ReadAsync(buffer, 0, buffer.Length);
                if (read == 0) break;
                string text = new string(buffer, 0, read);
                var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (lines.Length > 0)
                {
                    string lastLine = lines.Last().Trim();
                    if (lastLine.Length > 80) lastLine = lastLine.Substring(0, 80) + "...";
                    if (!string.IsNullOrWhiteSpace(lastLine))
                        progress.Report((-1, lastLine));
                }
            }
        });

        var stdErrTask = proc.StandardError.ReadToEndAsync();

        await Task.WhenAll(stdOutTask, stdErrTask, proc.WaitForExitAsync(ct));

        if (proc.ExitCode != 0)
        {
            string err = stdErrTask.Result;
            throw new Exception($"El proceso {Path.GetFileName(exe)} {arguments} falló con código {proc.ExitCode}. Detalle: {err.Trim()}");
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):F1} GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):F1} MB";
        if (bytes >= 1L << 10) return $"{bytes / (double)(1L << 10):F0} KB";
        return $"{bytes} B";
    }

    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Length > 120 ? name[..120] : name;
    }
}
