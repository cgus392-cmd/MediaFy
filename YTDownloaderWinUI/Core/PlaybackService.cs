using System.IO;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace YTDownloader.Core;

/// <summary>
/// Reproductor global persistente basado en MediaPlayer:
/// - Sin microcortes (el motor de Windows trabaja a bajo nivel, no carga la UI)
/// - Integra automáticamente con SMTC (System Media Transport Controls): Windows
///   ve el audio, muestra la tarjeta en el panel de volumen, hardware keys, etc.
/// - Niveles de VU vía AudioStateMonitor (ligero, sin tocar muestras PCM).
/// </summary>
public class PlaybackService
{
    private readonly MediaPlayer _player = new();
    private SystemMediaTransportControls? _smtc;
    private string? _currentPath;
    private string? _currentCover;

    public MediaPlayer Player => _player;
    public string CurrentTitle { get; private set; } = string.Empty;
    public string CurrentArtist { get; private set; } = string.Empty;
    public bool HasMedia { get; private set; }

    public bool IsPlaying => _player.PlaybackSession?.PlaybackState == MediaPlaybackState.Playing;
    public double Volume
    {
        get => _player.Volume;
        private set => _player.Volume = Math.Clamp(value, 0, 1);
    }

    /// <summary>Niveles aproximados por canal (0..1). Derivados del estado de audio del MediaPlayer.</summary>
    public double LevelLeft  { get; private set; }
    public double LevelRight { get; private set; }

    public event Action? Changed;

    public PlaybackService()
    {
        _player.AutoPlay = false;
        _player.Volume = 1.0;
        _player.CommandManager.IsEnabled = false; // gestionamos SMTC manualmente
        _player.MediaEnded += (_, _) => { Changed?.Invoke(); };
        _player.PlaybackSession.PlaybackStateChanged += (_, _) => { UpdateSmtcState(); Changed?.Invoke(); };
    }

    public async Task PlayAsync(string path, string title, string? artist = null, string? coverPath = null)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        _player.Source = MediaSource.CreateFromStorageFile(file);
        _player.Play();

        _currentPath = path;
        _currentCover = coverPath;
        CurrentTitle = title;
        CurrentArtist = artist ?? string.Empty;
        HasMedia = true;

        EnsureSmtc();
        await UpdateSmtcMetadataAsync();
        Changed?.Invoke();
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
        _smtc.IsNextEnabled = false;
        _smtc.IsPreviousEnabled = false;
        _smtc.ButtonPressed += (_, e) =>
        {
            switch (e.Button)
            {
                case SystemMediaTransportControlsButton.Play:  _player.Play(); break;
                case SystemMediaTransportControlsButton.Pause: _player.Pause(); break;
                case SystemMediaTransportControlsButton.Stop:  Close(); break;
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
            // Portada: la del archivo si la pasaron; si no, el logo de la app
            string coverPath = !string.IsNullOrEmpty(_currentCover) && File.Exists(_currentCover)
                ? _currentCover
                : Path.Combine(AppContext.BaseDirectory, "Assets", "logo.png");
            if (File.Exists(coverPath))
            {
                var coverFile = await StorageFile.GetFileFromPathAsync(coverPath);
                u.Thumbnail = RandomAccessStreamReference.CreateFromFile(coverFile);
            }
        }
        catch { }
        u.Update();
    }
}
