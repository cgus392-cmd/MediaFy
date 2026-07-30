using System.IO;
using System.Net.NetworkInformation;

namespace YTDownloader.Core;

/// <summary>Semáforo de un chequeo de salud.</summary>
public enum HealthStatus { Ok, Warning, Error }

/// <summary>Resultado de un chequeo del sistema (dato puro; la presentación la hace la UI).</summary>
public class HealthCheck
{
    public string Name { get; init; } = "";
    public HealthStatus Status { get; set; }
    public string Detail { get; set; } = "";
    /// <summary>Clave de acción de arreglo (ej. "import-cookies"), o null si no hay acción.</summary>
    public string? ActionKey { get; set; }
    public string? ActionLabel { get; set; }
}

/// <summary>
/// Comprobación integrada del estado de MediaFy: dependencias (yt-dlp, ffmpeg, motor JS),
/// cookies, conexión, carpeta de descargas y —lo más importante— una prueba REAL de extracción
/// de YouTube que detecta rupturas como el cambio anti-bot antes de que el usuario las sufra.
/// </summary>
public static class DiagnosticsService
{
    private static readonly string AssetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");

    // Video estable para la prueba real (Rick Astley — prácticamente nunca desaparece).
    private const string ProbeVideo = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";

    // ── Estado compartido (lo leen el semáforo de la barra y la tarjeta de Inicio) ──
    public static IReadOnlyList<HealthCheck> LastResults { get; private set; } = new List<HealthCheck>();
    public static HealthStatus Overall => Worst(LastResults);
    /// <summary>Se dispara cuando cambia el resultado del diagnóstico (para refrescar la UI).</summary>
    public static event Action? Updated;
    private static bool _busy;

    private static void Publish(List<HealthCheck> results)
    {
        LastResults = results;
        Updated?.Invoke();
    }

    /// <summary>Corre solo los chequeos ligeros e instantáneos y publica el resultado.</summary>
    public static void RefreshLight() => Publish(RunLight());

    /// <summary>Corre TODO (ligeros + prueba real de YouTube) y publica. Throttle propio anti-reentrada.</summary>
    public static async Task RunFullAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var list = RunLight();
            var yt = await CheckYouTubeExtractionAsync();
            list.Insert(Math.Min(4, list.Count), yt); // junto a cookies/motor JS
            AppSettings.Current.LastHealthCheckUtc = DateTime.UtcNow;
            Publish(list);
        }
        finally { _busy = false; }
    }

    /// <summary>Peor estado del conjunto (para el semáforo global).</summary>
    public static HealthStatus Worst(IEnumerable<HealthCheck> checks)
    {
        var worst = HealthStatus.Ok;
        foreach (var c in checks)
            if (c.Status > worst) worst = c.Status; // Ok(0) < Warning(1) < Error(2)
        return worst;
    }

    /// <summary>Chequeos ligeros e instantáneos (no tocan la red de YouTube). Aptos para el arranque.</summary>
    public static List<HealthCheck> RunLight() => new()
    {
        CheckBinary("yt-dlp",  "yt-dlp.exe", "Motor de descargas."),
        CheckBinary("ffmpeg",  "ffmpeg.exe", "Conversión y mezcla de audio/video."),
        CheckJsRuntime(),
        CheckCookies(),
        CheckInternet(),
        CheckOutputFolder(),
    };

    /// <summary>
    /// Chequeo pesado: prueba REAL de extracción de YouTube con el pipeline completo (cookies + motor JS).
    /// Es el que detecta que "YouTube cambió algo" sin esperar a que falle una descarga del usuario.
    /// </summary>
    public static async Task<HealthCheck> CheckYouTubeExtractionAsync()
    {
        var hc = new HealthCheck { Name = "Extracción de YouTube" };

        if (!YtDlpService.JsRuntimeAvailable)
        {
            hc.Status = HealthStatus.Error;
            hc.Detail = "Falta el motor JS incluido (reinstala MediaFy).";
            return hc;
        }
        try
        {
            string? url = await App.DownloadManager.GetStreamUrlAsync(ProbeVideo);
            if (!string.IsNullOrEmpty(url))
            {
                hc.Status = HealthStatus.Ok;
                hc.Detail = "Descargas y reproducción de YouTube funcionando.";
            }
            else
            {
                hc.Status = HealthStatus.Error;
                hc.Detail = "YouTube no entregó el audio. Suele ser cookies vencidas o un cambio anti-bot.";
                hc.ActionKey = "import-cookies";
                hc.ActionLabel = "Cookies";
            }
        }
        catch
        {
            hc.Status = HealthStatus.Error;
            hc.Detail = "No se pudo comprobar la extracción de YouTube (¿sin conexión?).";
        }
        return hc;
    }

    // ── Chequeos individuales ───────────────────────────────────
    private static HealthCheck CheckBinary(string name, string file, string desc)
    {
        bool ok = File.Exists(Path.Combine(AssetsDir, file));
        return new HealthCheck
        {
            Name = name,
            Status = ok ? HealthStatus.Ok : HealthStatus.Error,
            Detail = ok ? desc : $"No se encontró {file}. Reinstala MediaFy."
        };
    }

    private static HealthCheck CheckJsRuntime() => new()
    {
        Name = "Motor JS (YouTube)",
        Status = YtDlpService.JsRuntimeAvailable ? HealthStatus.Ok : HealthStatus.Error,
        Detail = YtDlpService.JsRuntimeAvailable
            ? $"Incluido — {YtDlpService.JsRuntimeName}."
            : "No disponible. Reinstala MediaFy."
    };

    private static HealthCheck CheckCookies()
    {
        var hc = new HealthCheck { Name = "Cookies de YouTube" };
        string path = AppSettings.Current.YouTubeCookiesPath;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            hc.Status = HealthStatus.Warning;
            hc.Detail = "Sin configurar. Muchos videos fallarán sin ellas.";
            hc.ActionKey = "import-cookies";
            hc.ActionLabel = "Importar";
            return hc;
        }

        int days = (int)(DateTime.Now - File.GetLastWriteTime(path)).TotalDays;
        if (days > 30)
        {
            hc.Status = HealthStatus.Warning;
            hc.Detail = $"Importadas hace {days} días. Si YouTube empieza a fallar, vuelve a importarlas.";
            hc.ActionKey = "import-cookies";
            hc.ActionLabel = "Actualizar";
        }
        else
        {
            hc.Status = HealthStatus.Ok;
            hc.Detail = days <= 0 ? "Configuradas (hoy)." : $"Configuradas (hace {days} días).";
        }
        return hc;
    }

    private static HealthCheck CheckInternet() => new()
    {
        Name = "Conexión a internet",
        Status = NetworkInterface.GetIsNetworkAvailable() ? HealthStatus.Ok : HealthStatus.Error,
        Detail = NetworkInterface.GetIsNetworkAvailable() ? "Conectado." : "Sin conexión de red."
    };

    private static HealthCheck CheckOutputFolder()
    {
        var hc = new HealthCheck { Name = "Carpeta de descargas" };
        string folder = App.DownloadManager.OutputFolder;
        try
        {
            if (!Directory.Exists(folder))
            {
                hc.Status = HealthStatus.Warning;
                hc.Detail = "Aún no existe; se creará al descargar.";
                return hc;
            }
            string probe = Path.Combine(folder, ".mediafy_write_test");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            hc.Status = HealthStatus.Ok;
            hc.Detail = folder;
        }
        catch
        {
            hc.Status = HealthStatus.Error;
            hc.Detail = "No se puede escribir en la carpeta de descargas.";
        }
        return hc;
    }
}
