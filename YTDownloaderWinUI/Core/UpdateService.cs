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

    /// <summary>Consulta GitHub y compara versiones. No descarga aún.</summary>
    public async Task<bool> CheckAsync(CancellationToken ct = default)
    {
        State = UpdateState.Checking;
        try
        {
            string json = await Http.GetStringAsync(ApiLatest, ct);
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
            LastError = ex.Message;
            State = UpdateState.Error;
            return false;
        }
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
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = DownloadedInstallerPath,
                // /SILENT muestra una pequeña ventana de progreso. /VERYSILENT no muestra nada.
                // Mantenemos /SILENT para que el user vea que algo pasa, y al terminar relanza solo.
                Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART",
                UseShellExecute = true,
                Verb = "open"
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
