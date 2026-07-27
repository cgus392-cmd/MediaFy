using System.IO;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace YTDownloader.Core;

/// <summary>Un elemento de la cola de reproducción (archivo local o stream).</summary>
public record QueueItem(string Source, bool IsStream, string Title, string? Artist, string? Cover);

/// <summary>
/// Reproductor global persistente basado en MediaPlayer:
/// - Sin microcortes (el motor de Windows trabaja a bajo nivel, no carga la UI)
/// - Integra automáticamente con SMTC (System Media Transport Controls): Windows
///   ve el audio, muestra la tarjeta en el panel de volumen, hardware keys, etc.
/// - Niveles de VU vía AudioStateMonitor (ligero, sin tocar muestras PCM).
/// - Cola con auto-siguiente y fundido suave entre canciones (crossfade tipo transición).
/// </summary>
public class PlaybackService
{
    private readonly MediaPlayer _player = new();
    private SystemMediaTransportControls? _smtc;
    private string? _currentPath;
    private string? _currentCover;

    // Cola de reproducción
    private readonly List<QueueItem> _queue = new();
    private int _qIndex = -1;

    // Volumen objetivo del usuario (el fundido mueve _player.Volume por debajo de este techo).
    private double _targetVolume = 1.0;

    public MediaPlayer Player => _player;
    public string CurrentTitle { get; private set; } = string.Empty;
    public string CurrentArtist { get; private set; } = string.Empty;
    /// <summary>Ruta de la portada actual (si la hay), para el mini-reproductor.</summary>
    public string? CurrentCover => _currentCover;
    public bool HasMedia { get; private set; }

    /// <summary>Hay una canción siguiente/anterior en la cola.</summary>
    public bool HasNext => _qIndex >= 0 && _qIndex < _queue.Count - 1;
    public bool HasPrev => _qIndex > 0;

    public bool IsPlaying => _player.PlaybackSession?.PlaybackState == MediaPlaybackState.Playing;

    /// <summary>Volumen elegido por el usuario (0..1). El fundido no lo altera.</summary>
    public double Volume
    {
        get => _targetVolume;
        private set
        {
            _targetVolume = Math.Clamp(value, 0, 1);
            if (!_fading) _player.Volume = _targetVolume;
        }
    }

    // ── Fundido (crossfade tipo transición) ──
    private bool _fading;                              // true mientras una envolvente de fundido controla el volumen
    private double CrossfadeSeconds => AppSettings.Current.CrossfadeSeconds;
    private bool CrossfadeOn => CrossfadeSeconds > 0.1;

    /// <summary>Niveles aproximados por canal (0..1). Derivados del estado de audio del MediaPlayer.</summary>
    public double LevelLeft  { get; private set; }
    public double LevelRight { get; private set; }

    public event Action? Changed;

    public PlaybackService()
    {
        _player.AutoPlay = false;
        _player.Volume = 1.0;
        _player.CommandManager.IsEnabled = false; // gestionamos SMTC manualmente
        _player.MediaEnded += (_, _) =>
        {
            // Auto-siguiente: si hay más en la cola, encadena la próxima con fundido de entrada.
            if (HasNext) { _qIndex++; _ = PlayCurrentAsync(); }
            else Changed?.Invoke();
        };
        _player.PlaybackSession.PlaybackStateChanged += (_, _) => { UpdateSmtcState(); Changed?.Invoke(); };
    }

    // ── Reproducción de un solo elemento (resetea la cola a ese único elemento) ──
    public Task PlayAsync(string path, string title, string? artist = null, string? coverPath = null)
        => PlaySingleAsync(new QueueItem(path, false, title, artist, coverPath));

    /// <summary>
    /// Reproduce en streaming desde una URL directa (p. ej. la resuelta por yt-dlp), sin descargar.
    /// Usa el mismo MediaPlayer/SMTC que la reproducción local (buffering a bajo nivel, sin microcortes).
    /// </summary>
    public Task PlayStreamAsync(string streamUrl, string title, string? artist = null, string? coverUrl = null)
        => PlaySingleAsync(new QueueItem(streamUrl, true, title, artist, coverUrl));

    private Task PlaySingleAsync(QueueItem item)
    {
        _queue.Clear();
        _queue.Add(item);
        _qIndex = 0;
        return PlayCurrentAsync();
    }

    /// <summary>Reproduce una cola completa empezando en startIndex (álbumes/carpetas) con auto-siguiente.</summary>
    public Task PlayQueueAsync(IReadOnlyList<QueueItem> items, int startIndex)
    {
        _queue.Clear();
        _queue.AddRange(items);
        _qIndex = Math.Clamp(startIndex, 0, Math.Max(0, _queue.Count - 1));
        return PlayCurrentAsync();
    }

    public void Next()     { if (HasNext) { _qIndex++; _ = PlayCurrentAsync(); } }
    public void Previous() { if (HasPrev) { _qIndex--; _ = PlayCurrentAsync(); } }

    /// <summary>Reproduce el elemento actual de la cola. Arranca en silencio si el fundido está activo.</summary>
    private async Task PlayCurrentAsync()
    {
        if (_qIndex < 0 || _qIndex >= _queue.Count) return;
        var it = _queue[_qIndex];

        MediaSource source = it.IsStream
            ? MediaSource.CreateFromUri(new Uri(it.Source))
            : MediaSource.CreateFromStorageFile(await StorageFile.GetFileFromPathAsync(it.Source));

        // Fundido de entrada: arranca en silencio y TickFade sube el volumen ("entrada épica").
        _fading = CrossfadeOn;
        _player.Volume = _fading ? 0 : _targetVolume;

        _player.Source = source;
        _player.Play();

        _currentPath  = it.IsStream ? null : it.Source;
        _currentCover = it.Cover;
        CurrentTitle  = it.Title;
        CurrentArtist = it.Artist ?? string.Empty;
        HasMedia = true;

        EnsureSmtc();
        if (_smtc != null) { _smtc.IsNextEnabled = HasNext; _smtc.IsPreviousEnabled = HasPrev; }
        await UpdateSmtcMetadataAsync();
        Changed?.Invoke();
    }

    /// <summary>
    /// Envolvente de fundido (crossfade tipo transición). La llama el timer de UI (~70ms):
    /// sube el volumen al empezar (fade-in) y lo baja al acercarse el final si hay canción siguiente
    /// (fade-out), sin superponer audio. El volumen del usuario (_targetVolume) es el techo.
    /// </summary>
    public void TickFade()
    {
        if (!HasMedia) { _fading = false; return; }
        if (!CrossfadeOn)
        {
            if (_fading) { _fading = false; _player.Volume = _targetVolume; }
            return;
        }
        var s = _player.PlaybackSession;
        if (s == null) return;

        double pos  = s.Position.TotalSeconds;
        double dur  = s.NaturalDuration.TotalSeconds;
        double fade = CrossfadeSeconds;

        double volIn  = Math.Clamp(pos / fade, 0, 1);                                  // 0→1 al inicio
        double volOut = (HasNext && dur > 0) ? Math.Clamp((dur - pos) / fade, 0, 1) : 1; // 1→0 al final
        double env = Math.Min(volIn, volOut);

        _fading = env < 0.999;
        _player.Volume = _targetVolume * env;
    }

    public void Toggle()
    {
        if (!HasMedia) return;
        if (IsPlaying) _player.Pause(); else _player.Play();
    }

    public void SetVolume(double v) => Volume = v;

    public void Seek(TimeSpan pos)
    {
        if (_player.PlaybackSession != null)
            _player.PlaybackSession.Position = pos;
    }

    public void Close()
    {
        try { _player.Pause(); } catch { }
        _player.Source = null;
        HasMedia = false;
        _queue.Clear(); _qIndex = -1; _fading = false;
        CurrentTitle = CurrentArtist = string.Empty;
        LevelLeft = LevelRight = 0;
        if (_smtc != null) _smtc.IsEnabled = false;
        Changed?.Invoke();
    }

    /// <summary>Actualiza los niveles del VU. Llamado por el timer de UI ~15Hz.</summary>
    public void TickLevels()
    {
        if (!HasMedia || !IsPlaying)
        {
            LevelLeft *= 0.6; LevelRight *= 0.6;
            return;
        }
        // AudioStateMonitor da una pista del estado sonoro del MediaPlayer sin tocar samples.
        // Modulamos con una pequeña variación natural para que las dos barras se sientan vivas.
        var monitor = _player.AudioStateMonitor;
        double baseLvl = monitor.SoundLevel switch
        {
            SoundLevel.Full  => 0.75,
            SoundLevel.Muted => 0.05,
            _                => 0.4
        };
        baseLvl *= _player.Volume;
        var rng = Random.Shared;
        double tL = Math.Clamp(baseLvl * (0.55 + rng.NextDouble() * 0.55), 0, 1);
        double tR = Math.Clamp(baseLvl * (0.55 + rng.NextDouble() * 0.55), 0, 1);
        LevelLeft  = tL > LevelLeft  ? tL : LevelLeft  + (tL - LevelLeft)  * 0.3;
        LevelRight = tR > LevelRight ? tR : LevelRight + (tR - LevelRight) * 0.3;
    }

    // ── SMTC: integración con el panel de volumen / hardware keys ──
    private void EnsureSmtc()
    {
        if (_smtc != null) return;
        _smtc = _player.SystemMediaTransportControls;
        _smtc.IsEnabled = true;
        _smtc.IsPlayEnabled = true;
        _smtc.IsPauseEnabled = true;
        _smtc.IsStopEnabled = true;
        _smtc.IsNextEnabled = HasNext;
        _smtc.IsPreviousEnabled = HasPrev;
        _smtc.ButtonPressed += (_, e) =>
        {
            switch (e.Button)
            {
                case SystemMediaTransportControlsButton.Play:     _player.Play(); break;
                case SystemMediaTransportControlsButton.Pause:    _player.Pause(); break;
                case SystemMediaTransportControlsButton.Stop:     Close(); break;
                case SystemMediaTransportControlsButton.Next:     Next(); break;
                case SystemMediaTransportControlsButton.Previous: Previous(); break;
            }
        };
    }

    private void UpdateSmtcState()
    {
        if (_smtc == null) return;
        _smtc.PlaybackStatus = _player.PlaybackSession.PlaybackState switch
        {
            MediaPlaybackState.Playing => MediaPlaybackStatus.Playing,
            MediaPlaybackState.Paused  => MediaPlaybackStatus.Paused,
            MediaPlaybackState.None    => MediaPlaybackStatus.Closed,
            _                          => MediaPlaybackStatus.Stopped
        };
    }

    private async Task UpdateSmtcMetadataAsync()
    {
        if (_smtc == null) return;
        var u = _smtc.DisplayUpdater;
        u.Type = MediaPlaybackType.Music;
        u.MusicProperties.Title = string.IsNullOrEmpty(CurrentTitle) ? "MediaFy" : CurrentTitle;
        u.MusicProperties.Artist = string.IsNullOrEmpty(CurrentArtist) ? "MediaFy by CG" : CurrentArtist;
        try
        {
            // Portada remota (streaming): miniatura por URL → SMTC la carga directamente.
            if (!string.IsNullOrEmpty(_currentCover) && _currentCover.StartsWith("http"))
            {
                u.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(_currentCover));
            }
            else
            {
                // Portada local: la del archivo si la pasaron; si no, el logo de la app
                string coverPath = !string.IsNullOrEmpty(_currentCover) && File.Exists(_currentCover)
                    ? _currentCover
                    : Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png");
                if (File.Exists(coverPath))
                {
                    var coverFile = await StorageFile.GetFileFromPathAsync(coverPath);
                    u.Thumbnail = RandomAccessStreamReference.CreateFromFile(coverFile);
                }
            }
        }
        catch { }
        u.Update();
    }
}
