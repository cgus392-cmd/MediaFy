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

    // ── Autenticación / anti-bot de YouTube (2025+) ─────────────────────────
    // YouTube ahora exige DOS cosas para extraer la mayoría de videos:
    //   1) cookies de una sesión logueada  → supera "Sign in to confirm you're not a bot"
    //   2) un runtime de JavaScript          → resuelve el reto `nsig` y expone los formatos reales
    // Sin ambas, ~90% de los videos fallan. Estos flags se anteponen a cada llamada de extracción.
    //
    // Runtime JS: MediaFy incluye su propio `deno.exe` en Assets (bundled) para NO depender de que
    // el usuario tenga Node instalado. Si por algo faltara, se cae a un Node del sistema.

    /// <summary>deno incluido con la app (runtime JS por defecto de yt-dlp).</summary>
    private static readonly string BundledDenoPath =
        Path.Combine(AppContext.BaseDirectory, "Assets", "deno.exe");

    /// <summary>node.exe del sistema (solo como respaldo si faltara el deno incluido).</summary>
    private static readonly string? SystemNodePath = DetectNode();

    /// <summary>Argumento listo para --js-runtimes (deno incluido preferido), o null si no hay runtime.</summary>
    private static string? JsRuntimeArg()
    {
        if (File.Exists(BundledDenoPath)) return $"deno:{BundledDenoPath}";
        if (SystemNodePath != null)       return $"node:{SystemNodePath}";
        return null;
    }

    /// <summary>True si hay un runtime JS disponible (deno incluido o Node del sistema).</summary>
    public static bool JsRuntimeAvailable => File.Exists(BundledDenoPath) || SystemNodePath != null;

    /// <summary>Nombre legible del runtime JS activo (para mostrar en la UI).</summary>
    public static string JsRuntimeName =>
        File.Exists(BundledDenoPath) ? "deno (incluido)"
        : SystemNodePath != null     ? "Node.js (del sistema)"
        : "ninguno";

    /// <summary>Compatibilidad con la UI existente: ahora significa "hay runtime JS" (deno incluido cuenta).</summary>
    public static bool NodeInstalled => JsRuntimeAvailable;

    /// <summary>True si MediaFy puede autenticar contra YouTube (hay cookies válidas y runtime JS).</summary>
    public static bool YouTubeAuthReady =>
        JsRuntimeAvailable
        && !string.IsNullOrWhiteSpace(AppSettings.Current.YouTubeCookiesPath)
        && File.Exists(AppSettings.Current.YouTubeCookiesPath);

    private static string? DetectNode()
    {
        string[] candidates =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe"),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        // Fallback: buscar node.exe en el PATH del sistema.
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var p = Path.Combine(dir.Trim(), "node.exe");
                if (File.Exists(p)) return p;
            }
            catch { /* entrada de PATH inválida, ignorar */ }
        }
        return null;
    }

    /// <summary>
    /// Flags de autenticación que se anteponen a los argumentos de yt-dlp: cookies del usuario
    /// (si están configuradas) + runtime JS (deno incluido). Devuelve "" si no hay nada.
    /// </summary>
    private static string AuthFlags()
    {
        var sb = new System.Text.StringBuilder();
        string cookies = AppSettings.Current.YouTubeCookiesPath;
        if (!string.IsNullOrWhiteSpace(cookies) && File.Exists(cookies))
            sb.Append($"--cookies \"{cookies}\" ");
        var rt = JsRuntimeArg();
        if (rt != null)
            sb.Append($"--js-runtimes \"{rt}\" ");
        return sb.ToString();
    }

    // ── Auto-actualización de yt-dlp ────────────────────────────
    // `yt-dlp -U` consulta la API de GitHub, que sin token limita a 60 peticiones/hora por IP.
    // Al agotarse devuelve 403 y la actualización fallaba EN SILENCIO, así que yt-dlp se quedó
    // meses atrás hasta que YouTube dejó de servirle los datos (403 al descargar). Aquí se evita
    // la API por completo: la versión publicada se deduce de la redirección de /releases/latest
    // y el binario se baja de la URL directa de la release.
    private const string LatestReleaseUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest";
    private const string LatestBinaryUrl  = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

    /// <summary>Versión instalada de yt-dlp (cacheada tras la comprobación), o null si no se pudo leer.</summary>
    public static string? InstalledVersion { get; private set; }
    /// <summary>Última versión publicada conocida, o null si no se pudo consultar.</summary>
    public static string? LatestVersion { get; private set; }
    /// <summary>True si consta que hay una versión más nueva que la instalada.</summary>
    public static bool UpdateAvailable =>
        InstalledVersion != null && LatestVersion != null &&
        string.CompareOrdinal(LatestVersion, InstalledVersion) > 0;

    /// <summary>Lee la versión instalada ejecutando `yt-dlp --version`.</summary>
    public async Task<string?> ReadInstalledVersionAsync(CancellationToken ct = default)
    {
        try
        {
            string v = (await RunAsync(_ytDlpPath, "--version", ct)).Trim();
            InstalledVersion = string.IsNullOrWhiteSpace(v) ? null : v;
        }
        catch { InstalledVersion = null; }
        return InstalledVersion;
    }

    /// <summary>Versión publicada, leída de la redirección de /releases/latest (sin usar la API).</summary>
    private static async Task<string?> ReadLatestVersionAsync(CancellationToken ct)
    {
        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MediaFy (CG LABS)");

            using var resp = await http.GetAsync(LatestReleaseUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            string? loc = resp.Headers.Location?.ToString();
            if (string.IsNullOrEmpty(loc)) return null;

            string tag = loc[(loc.LastIndexOf('/') + 1)..].Trim();
            return string.IsNullOrWhiteSpace(tag) ? null : tag;
        }
        catch { return null; }
    }

    /// <summary>
    /// Comprueba si hay una versión más nueva de yt-dlp y la instala. Devuelve true si se actualizó.
    /// Mantener yt-dlp al día no es opcional: es lo único que sigue el ritmo de los cambios de YouTube.
    /// </summary>
    public async Task<bool> SelfUpdateAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_ytDlpPath)) return false;

        await ReadInstalledVersionAsync(ct);
        LatestVersion = await ReadLatestVersionAsync(ct);
        if (!UpdateAvailable) return false;

        string tmp = _ytDlpPath + ".new";
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("MediaFy (CG LABS)");
                byte[] bytes = await http.GetByteArrayAsync(LatestBinaryUrl, ct);
                if (bytes.Length < 1_000_000) return false;   // descarga incompleta: no tocar nada
                await File.WriteAllBytesAsync(tmp, bytes, ct);
            }

            // Si el ejecutable está en uso (descarga en curso), esto falla y se reintenta al
            // próximo arranque: nunca se deja a medias.
            File.Move(tmp, _ytDlpPath, overwrite: true);
            InstalledVersion = LatestVersion;
            return true;
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return false;
        }
    }

    /// <summary>
    /// True si la URL es una lista de reproducción "pura" (álbum, playlist) y no un
    /// vídeo individual. En esos casos --no-playlist no aplica y hay que tratarla aparte.
    /// </summary>
    public static bool IsPlaylistUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var u = url.ToLowerInvariant();
        if (u.Contains("/playlist")) return true;            // .../playlist?list=...
        bool hasList  = u.Contains("list=");
        bool hasVideo = u.Contains("watch?v=") || u.Contains("youtu.be/") || u.Contains("/shorts/");
        return hasList && !hasVideo;                          // list= sin vídeo concreto → lista pura
    }

    public async Task<VideoInfo> GetVideoInfoAsync(string url, CancellationToken ct = default)
    {
        // Listas/álbumes: análisis rápido y plano (no extrae cada vídeo uno por uno)
        if (IsPlaylistUrl(url))
            return await GetPlaylistInfoAsync(url, ct);

        string json = await RunAsync(_ytDlpPath, $"{AuthFlags()}--dump-json --no-playlist \"{url}\"", ct);
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
    /// Análisis rápido de una lista/álbum: --flat-playlist devuelve solo títulos e ids
    /// (sin extraer cada vídeo), así que tarda ~2-4s en lugar de ~30s. Un único JSON.
    /// </summary>
    private async Task<VideoInfo> GetPlaylistInfoAsync(string url, CancellationToken ct = default)
    {
        string json = await RunAsync(_ytDlpPath,
            $"{AuthFlags()}--dump-single-json --flat-playlist \"{url}\"", ct);
        var obj = JObject.Parse(json);

        var entries = obj["entries"] as JArray ?? new JArray();
        var info = new VideoInfo
        {
            IsPlaylist    = true,
            Title         = obj["title"]?.ToString() ?? "Lista de reproducción",
            Uploader      = obj["uploader"]?.ToString()
                          ?? obj["channel"]?.ToString()
                          ?? obj["uploader_id"]?.ToString()
                          ?? string.Empty,
            PlaylistCount = obj["playlist_count"]?.ToObject<int>() ?? entries.Count
        };

        // Miniatura: la de la lista o, si no, la del primer vídeo
        string thumb = obj["thumbnails"]?.LastOrDefault()?["url"]?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(thumb) && entries.Count > 0)
        {
            string firstId = entries[0]?["id"]?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(firstId))
                thumb = $"https://i.ytimg.com/vi/{firstId}/mqdefault.jpg";
        }
        info.Thumbnail = thumb;

        foreach (var e in entries)
            info.Entries.Add(e["title"]?.ToString() ?? string.Empty);

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
        string args = $"{AuthFlags()}--dump-json --flat-playlist \"ytsearch{count}:{query}\"";
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

    /// <summary>
    /// Resuelve la URL directa del mejor stream de audio de un video (sin descargarlo),
    /// para reproducir en vivo con el MediaPlayer. La URL apunta al CDN de YouTube y
    /// caduca en unas horas — se vuelve a resolver al reproducir de nuevo.
    /// </summary>
    public async Task<string?> GetStreamUrlAsync(string url, CancellationToken ct = default)
    {
        // -f bestaudio/best: mejor pista de audio disponible · -g: solo imprime la URL directa
        // --no-playlist: si el link trae lista, resolvemos solo el video pedido
        string args = $"{AuthFlags()}-f \"bestaudio/best\" -g --no-playlist \"{url}\"";
        string output;
        try { output = await RunAsync(_ytDlpPath, args, ct); }
        catch { return null; }

        // yt-dlp puede imprimir varias líneas (audio + video); tomamos la última URL http válida.
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            var t = lines[i].Trim();
            if (t.StartsWith("http")) return t;
        }
        return null;
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
        // Listas/álbumes: se descargan PISTA POR PISTA en procesos frescos.
        // Esto evita el throttling acumulado de YouTube (que congelaba la sesión
        // larga tras ~11 pistas) y permite progreso real "Pista N de M" + que una
        // pista fallida no detenga el resto.
        if (item.WholePlaylist)
        {
            await DownloadPlaylistAsync(item, outputFolder, progress, ct);
            return;
        }

        await DownloadSingleAsync(item, outputFolder, "%(title)s.%(ext)s", item.Url, progress, ct);
    }

    /// <summary>Descarga un único vídeo/pista con su plantilla de salida relativa a <paramref name="outputFolder"/>.</summary>
    private async Task DownloadSingleAsync(
        DownloadItem item, string outputFolder, string fileTemplate, string url,
        IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        string ext = GetExtension(item.Format);
        string outputTemplate = Path.Combine(outputFolder, fileTemplate);

        string subFlags = SubtitleFlags(item.Subtitles);
        const string netFlags = "--socket-timeout 30 --retries 10 --fragment-retries 10";

        string common =
            $"{AuthFlags()}{netFlags} " +
            $"--ffmpeg-location \"{_ffmpegPath}\" " +
            $"--embed-thumbnail --embed-metadata --no-playlist {subFlags} --newline " +
            $"--progress-template \"{ProgressTemplate}\" " +
            $"-o \"{outputTemplate}\" \"{url}\"";

        string audioQ = item.Format is "FLAC" or "WAV" ? "0" : AudioQuality(item.Quality);
        string args = item.IsAudio
            ? $"-x --audio-format {ext} --audio-quality {audioQ} --add-metadata {common}"
            : $"{BuildFormatArg(item.Format, item.Quality)} {common}";

        await RunWithProgressAsync(_ytDlpPath, args, progress, ct);
    }

    /// <summary>
    /// Descarga una lista/álbum completa pista por pista (cada una en su propio proceso),
    /// reportando "Pista N de M" y tolerando fallos individuales.
    /// </summary>
    private async Task DownloadPlaylistAsync(
        DownloadItem item, string outputFolder,
        IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        var entries = await GetPlaylistEntriesAsync(item.Url, ct);

        // Si no es realmente una lista (un solo vídeo), cae a descarga única
        if (entries.Count <= 1)
        {
            await DownloadSingleAsync(item, outputFolder, "%(title)s.%(ext)s", item.Url, progress, ct);
            return;
        }

        bool albumFolder = AppSettings.Current.PlaylistSubfolder;
        string albumName = Sanitize(string.IsNullOrWhiteSpace(item.Title) ? "Lista de reproducción" : item.Title);
        string targetDir = albumFolder ? Path.Combine(outputFolder, albumName) : outputFolder;
        Directory.CreateDirectory(targetDir);

        int total = entries.Count;
        int ok = 0;
        var failed = new List<int>();

        for (int i = 0; i < total; i++)
        {
            ct.ThrowIfCancellationRequested();
            int trackNo = i + 1;

            // Marca de pista: el manager pone "Pista N de M" y reinicia la barra
            progress.Report(new DownloadProgress(-3, $"{trackNo}|{total}"));

            // Numeración solo si agrupamos en carpeta de álbum
            string prefix = albumFolder ? $"{trackNo:D2} - " : string.Empty;
            string fileTemplate = $"{prefix}%(title)s.%(ext)s";

            try
            {
                await DownloadSingleAsync(item, targetDir, fileTemplate, entries[i].Url, progress, ct);
                ok++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                failed.Add(trackNo);
                progress.Report(new DownloadProgress(-2, string.Empty,
                    LogLine: $"[MediaFy] Pista {trackNo} falló y se omitió: {ex.Message.Trim()}"));
            }

            // Pausa breve entre pistas: gentil con YouTube, evita rate-limit por IP
            try { await Task.Delay(500, ct); } catch (OperationCanceledException) { throw; }
        }

        if (ok == 0)
            throw new Exception("No se pudo descargar ninguna pista de la lista.");

        if (failed.Count > 0)
            progress.Report(new DownloadProgress(-2, string.Empty,
                LogLine: $"[MediaFy] {ok}/{total} pistas completadas · fallaron: {string.Join(", ", failed)}"));
    }

    /// <summary>Lista plana (url + título) de las pistas de una lista/álbum. Rápido (--flat-playlist).</summary>
    private async Task<List<(string Url, string Title)>> GetPlaylistEntriesAsync(
        string url, CancellationToken ct = default)
    {
        var list = new List<(string, string)>();
        try
        {
            string json = await RunAsync(_ytDlpPath, $"--dump-single-json --flat-playlist \"{url}\"", ct);
            var obj = JObject.Parse(json);
            if (obj["entries"] is not JArray entries) return list;

            foreach (var e in entries)
            {
                string id = e["id"]?.ToString() ?? string.Empty;
                string eUrl = e["url"]?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(eUrl) || !eUrl.StartsWith("http"))
                    eUrl = string.IsNullOrEmpty(id) ? string.Empty : $"https://www.youtube.com/watch?v={id}";
                if (string.IsNullOrEmpty(eUrl)) continue;
                list.Add((eUrl, e["title"]?.ToString() ?? string.Empty));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* devolvemos lo que haya */ }
        return list;
    }

    private static string SubtitleFlags(string subtitles) => subtitles switch
    {
        "Auto"  => "--write-subs --embed-subs --sub-langs \"auto\"",
        "ES"    => "--write-subs --embed-subs --sub-langs \"es\"",
        "EN"    => "--write-subs --embed-subs --sub-langs \"en\"",
        "Todos" => "--write-subs --embed-subs --sub-langs \"all\"",
        _       => string.Empty
    };

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
        string args = $"{AuthFlags()}-x --audio-format mp3 --audio-quality 0 " +
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
        ProcessTuning.RunInBackground(proc);
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
        ProcessTuning.RunInBackground(proc);
        string output = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);
        if (proc.ExitCode != 0)
        {
            string err = await proc.StandardError.ReadToEndAsync(ct);
            throw new Exception($"yt-dlp error: {err}");
        }
        return output;
    }

    /// <summary>
    /// Limita la frecuencia con la que el progreso llega a la UI. yt-dlp emite una línea por cada
    /// trozo descargado (decenas por segundo, y por cada descarga en paralelo); como el reporter
    /// se creó en el hilo de UI, cada línea cruzaba a ese hilo y disparaba bindings y layout.
    /// Refrescar ~8 veces por segundo es indistinguible para el usuario y elimina la avalancha.
    /// Los eventos que no son porcentaje (cambio de fase, pista de lista, logs) pasan siempre.
    /// </summary>
    private sealed class ThrottledProgress : IProgress<DownloadProgress>
    {
        private readonly IProgress<DownloadProgress> _inner;
        private readonly int _minIntervalMs;
        private long _lastSentTicks;
        private DownloadProgress? _pending;

        public ThrottledProgress(IProgress<DownloadProgress> inner, int minIntervalMs = 125)
        {
            _inner = inner;
            _minIntervalMs = minIntervalMs;
        }

        public void Report(DownloadProgress value)
        {
            if (value.Percent < 0) { Flush(); _inner.Report(value); return; }

            long now = Environment.TickCount64;
            if (now - _lastSentTicks >= _minIntervalMs)
            {
                _lastSentTicks = now;
                _pending = null;
                _inner.Report(value);
            }
            else _pending = value;   // se descarta si llega otro antes; Flush garantiza el último
        }

        /// <summary>Emite el último valor retenido (para no perder el 100% final).</summary>
        public void Flush()
        {
            if (_pending is { } p) { _pending = null; _inner.Report(p); }
        }
    }

    private static async Task RunWithProgressAsync(
        string exe, string args,
        IProgress<DownloadProgress> uiProgress,
        CancellationToken ct)
    {
        var throttled = new ThrottledProgress(uiProgress);
        var progress = (IProgress<DownloadProgress>)throttled;
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
        ProcessTuning.RunInBackground(proc);

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
        throttled.Flush();   // asegura que se vea el último progreso (100%)

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

        // Descarga de lista: "[download] Downloading item 3 of 13" (o "video 3 of 13")
        var pl = Regex.Match(line, @"Downloading (?:item|video) (\d+) of (\d+)");
        if (pl.Success)
        {
            progress.Report(new DownloadProgress(-3, $"{pl.Groups[1].Value}|{pl.Groups[2].Value}", LogLine: line));
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
