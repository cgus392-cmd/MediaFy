using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace YTDownloader.Core;

/// <summary>Estado del proceso de actualización (para que la UI lo binde).</summary>
public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, ReadyToInstall, Error }

public class UpdateInfo
{
    public string Version { get; set; } = "";
    public string TagName { get; set; } = "";
    public string Notes { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public long Size { get; set; }
    public string HtmlUrl { get; set; } = "";

    /// <summary>
    /// Actualización obligatoria: la marca un release cuyas notas incluyen el marcador
    /// <c>[obligatoria]</c> o <c>[mandatory]</c>. La UI la presenta como no descartable.
    /// </summary>
    public bool Mandatory { get; set; }
}

/// <summary>
/// Consulta GitHub Releases para detectar actualizaciones de MediaFy, descargarlas
/// con progreso y aplicar el instalador silencioso. Usa la API pública (sin token).
/// </summary>
public class UpdateService
{
    private const string Owner = "cgus392-cmd";
    private const string Repo  = "MediaFy";
    private const string ApiLatest = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient();
        // GitHub exige User-Agent. Y aceptamos JSON / octet-stream.
        h.DefaultRequestHeaders.UserAgent.ParseAdd($"MediaFy/{CurrentVersion()} (CG LABS)");
        h.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return h;
    }

    public static string CurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "1.0.0";
    }

    public event Action<UpdateState>? StateChanged;
    public event Action<double>? DownloadProgress; // 0..100

    private UpdateState _state = UpdateState.Idle;
    public UpdateState State
    {
        get => _state;
        private set { _state = value; StateChanged?.Invoke(value); }
    }
    public UpdateInfo? Latest { get; private set; }
    public string? DownloadedInstallerPath { get; private set; }
    public string LastError { get; private set; } = "";
    /// <summary>True si el último fallo fue por el límite de la API de GitHub (aviso temporal, no un error real).</summary>
    public bool RateLimited { get; private set; }

    /// <summary>
    /// Consulta GitHub y compara versiones. No descarga aún.
    /// <paramref name="manual"/>=true cuando lo dispara el usuario (botón "Buscar"): solo entonces
    /// se muestran errores. En el chequeo automático de arranque, los fallos (403/rate-limit, red)
    /// se silencian para no alarmar con algo que no es culpa de la app.
    /// </summary>
    public async Task<bool> CheckAsync(bool manual = false, CancellationToken ct = default)
    {
        AppSettings.Current.LastUpdateCheckUtc = DateTime.UtcNow; // registra el intento (para el throttle)
        RateLimited = false;
        State = UpdateState.Checking;
        try
        {
            using var resp = await Http.GetAsync(ApiLatest, ct);

            // Límite de la API pública de GitHub (60/hora por IP sin token). No es un fallo de la app.
            if (resp.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                (int)resp.StatusCode == 429)
            {
                RateLimited = true;
                if (manual) { LastError = RateLimitMessage(resp); State = UpdateState.Error; }
                else        { State = UpdateState.Idle; } // arranque: fallar en silencio
                return false;
            }
            resp.EnsureSuccessStatusCode();

            string json = await resp.Content.ReadAsStringAsync(ct);
            var o = JObject.Parse(json);

            var info = new UpdateInfo
            {
                TagName = o["tag_name"]?.ToString() ?? "",
                Notes   = o["body"]?.ToString() ?? "",
                HtmlUrl = o["html_url"]?.ToString() ?? ""
            };
            info.Version = info.TagName.TrimStart('v', 'V');

            // Obligatoria: marcador [obligatoria] / [mandatory] en las notas del release.
            info.Mandatory = info.Notes.Contains("[obligatoria]", StringComparison.OrdinalIgnoreCase)
                          || info.Notes.Contains("[mandatory]", StringComparison.OrdinalIgnoreCase);

            // Busca el .exe del instalador en los assets
            if (o["assets"] is JArray assets)
            {
                foreach (var a in assets)
                {
                    string n = a["name"]?.ToString() ?? "";
                    if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && n.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                    {
                        info.DownloadUrl = a["browser_download_url"]?.ToString() ?? "";
                        info.Size = a["size"]?.ToObject<long>() ?? 0;
                        break;
                    }
                }
            }

            Latest = info;

            if (IsNewer(info.Version, CurrentVersion()))
            {
                State = UpdateState.Available;
                return true;
            }
            State = UpdateState.UpToDate;
            return false;
        }
        catch (Exception ex)
        {
            // Red caída u otro error: solo molestar si el usuario lo pidió (manual).
            if (manual)
            {
                LastError = "No se pudo comprobar ahora. Revisa tu conexión e inténtalo de nuevo.";
                State = UpdateState.Error;
            }
            else State = UpdateState.Idle;
            System.Diagnostics.Debug.WriteLine($"Update check: {ex.Message}");
            return false;
        }
    }

    /// <summary>Mensaje amable con el tiempo estimado de reseteo del límite de GitHub.</summary>
    private static string RateLimitMessage(HttpResponseMessage resp)
    {
        try
        {
            if (resp.Headers.TryGetValues("X-RateLimit-Reset", out var vals))
            {
                string? first = null;
                foreach (var v in vals) { first = v; break; }
                if (long.TryParse(first, out var reset))
                {
                    var when = DateTimeOffset.FromUnixTimeSeconds(reset);
                    int mins = Math.Max(1, (int)Math.Ceiling((when - DateTimeOffset.UtcNow).TotalMinutes));
                    return $"GitHub limitó las comprobaciones por un rato. Reintenta en ~{mins} min.";
                }
            }
        }
        catch { /* sin cabecera → mensaje genérico */ }
        return "GitHub limitó las comprobaciones por un rato. Inténtalo más tarde.";
    }

    /// <summary>Descarga el instalador al directorio temporal con progreso.</summary>
    public async Task<bool> DownloadAsync(CancellationToken ct = default)
    {
        if (Latest == null || string.IsNullOrEmpty(Latest.DownloadUrl)) return false;
        State = UpdateState.Downloading;
        try
        {
            string fileName = $"MediaFy-Setup-{Latest.Version}.exe";
            string outPath = Path.Combine(Path.GetTempPath(), fileName);

            using var req = new HttpRequestMessage(HttpMethod.Get, Latest.DownloadUrl);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? Latest.Size;
            using var src = await resp.Content.ReadAsStreamAsync(ct);
            using var dst = File.Create(outPath);

            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                received += read;
                if (total > 0)
                    DownloadProgress?.Invoke(received * 100.0 / total);
            }
            DownloadedInstallerPath = outPath;
            State = UpdateState.ReadyToInstall;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            State = UpdateState.Error;
            return false;
        }
    }

    /// <summary>Lanza el instalador silencioso y cierra MediaFy. Inno Setup hará la sustitución.</summary>
    public bool Install()
    {
        if (DownloadedInstallerPath == null || !File.Exists(DownloadedInstallerPath)) return false;
        try
        {
            // CRÍTICO: el instalador se lanza DESACOPLADO del árbol de procesos de MediaFy.
            // Si fuera proceso hijo, el `taskkill /F /T /IM MediaFy.exe` del propio instalador
            // (y el Kill(entireProcessTree:true) al cerrar la app) matarían también al instalador
            // por estar dentro del mismo árbol → crash de la app Y del instalador a la vez.
            // `cmd /c start` lo crea como proceso independiente, fuera de nuestro árbol.
            // /SILENT: ventana de progreso mínima · /SUPPRESSMSGBOXES · /NORESTART.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c start \"\" \"{DownloadedInstallerPath}\" /SILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            State = UpdateState.Error;
            return false;
        }
    }

    /// <summary>True si MediaFy.exe está en una ruta instalada (puede actualizarse en sitio).</summary>
    public bool IsInstalledLocation()
    {
        string baseDir = AppContext.BaseDirectory.ToLowerInvariant();
        return baseDir.Contains(@"\programs\") || baseDir.Contains(@"\program files");
    }

    private static bool IsNewer(string a, string b)
    {
        // Compara "1.2.3" semánticamente
        int[] A = Parse(a), B = Parse(b);
        for (int i = 0; i < 3; i++)
            if (A[i] != B[i]) return A[i] > B[i];
        return false;
    }

    private static int[] Parse(string v)
    {
        var parts = v.Trim().TrimStart('v', 'V').Split('.', StringSplitOptions.RemoveEmptyEntries);
        var r = new int[] { 0, 0, 0 };
        for (int i = 0; i < Math.Min(parts.Length, 3); i++)
            int.TryParse(new string(parts[i].TakeWhile(char.IsDigit).ToArray()), out r[i]);
        return r;
    }
}
