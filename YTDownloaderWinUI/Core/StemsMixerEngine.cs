using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;

namespace YTDownloader.Core;

public class StemTrack
{
    public string Name { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public MediaPlayer Player { get; set; } = null!;
    
    private double _volume = 1.0;
    public double Volume 
    { 
        get => _volume; 
        set 
        {
            _volume = Math.Clamp(value, 0, 1);
            UpdateRealVolume();
        } 
    }

    private bool _isMuted = false;
    public bool IsMuted 
    { 
        get => _isMuted; 
        set 
        {
            _isMuted = value;
            UpdateRealVolume();
        } 
    }

    internal bool IsSoloedGlobally = false;
    internal bool IsSoloActiveOnTrack = false;

    private void UpdateRealVolume()
    {
        if (_isMuted)
        {
            Player.Volume = 0;
            return;
        }

        if (IsSoloedGlobally && !IsSoloActiveOnTrack)
        {
            Player.Volume = 0;
            return;
        }

        Player.Volume = _volume;
    }
}

public class StemsMixerEngine : IDisposable
{
    private readonly MediaTimelineController _timelineController = new();
    private readonly List<StemTrack> _tracks = new();
    
    public IReadOnlyList<StemTrack> Tracks => _tracks;
    
    public TimeSpan Position 
    { 
        get => _timelineController.Position; 
        set => _timelineController.Position = value; 
    }
    
    public TimeSpan Duration { get; private set; }

    public bool IsPlaying => _timelineController.State == MediaTimelineControllerState.Running;

    public event Action? PositionChanged;
    public event Action? StateChanged;

    public StemsMixerEngine()
    {
        _timelineController.PositionChanged += (s, e) => PositionChanged?.Invoke();
        _timelineController.StateChanged += (s, e) => StateChanged?.Invoke();
    }

    public async Task LoadStemsAsync(string folderPath)
    {
        DisposeAllTracks();
        
        var files = Directory.GetFiles(folderPath, "*.wav")
            .Concat(Directory.GetFiles(folderPath, "*.mp3"))
            .OrderBy(f => f)
            .ToList();

        if (files.Count == 0) return;

        TimeSpan maxDuration = TimeSpan.Zero;

        foreach (var file in files)
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(file);
            var source = MediaSource.CreateFromStorageFile(storageFile);
            
            var player = new MediaPlayer
            {
                CommandManager = { IsEnabled = false },
                AutoPlay = false,
                Source = source,
                TimelineController = _timelineController
            };

            // Intentar obtener la duración de la pista para establecer la duración máxima del mezclador
            try 
            {
                var props = await storageFile.Properties.GetMusicPropertiesAsync();
                if (props.Duration > maxDuration)
                    maxDuration = props.Duration;
            } 
            catch { }

            _tracks.Add(new StemTrack
            {
                Name = Path.GetFileNameWithoutExtension(file),
                FilePath = file,
                Player = player,
                Volume = 1.0,
                IsMuted = false
            });
        }

        Duration = maxDuration;
    }

    public void Play()
    {
        if (_tracks.Count > 0 && _timelineController.State != MediaTimelineControllerState.Running)
        {
            _timelineController.Start();
        }
    }

    public void Pause()
    {
        if (_timelineController.State == MediaTimelineControllerState.Running)
        {
            _timelineController.Pause();
        }
    }

    public void TogglePlayPause()
    {
        if (IsPlaying) Pause(); else Play();
    }

    public void ToggleSolo(StemTrack targetTrack)
    {
        targetTrack.IsSoloActiveOnTrack = !targetTrack.IsSoloActiveOnTrack;
        
        // Si hay al menos una pista en solo, el estado global es soloed
        bool anySolo = _tracks.Any(t => t.IsSoloActiveOnTrack);
        
        foreach (var track in _tracks)
        {
            track.IsSoloedGlobally = anySolo;
            // Forzamos actualización de volumen real llamando al setter de Volume internamente
            track.Volume = track.Volume; 
        }
    }

    public void DisposeAllTracks()
    {
        Pause();
        foreach (var track in _tracks)
        {
            track.Player.TimelineController = null;
            track.Player.Source = null;
            track.Player.Dispose();
        }
        _tracks.Clear();
        Duration = TimeSpan.Zero;
        Position = TimeSpan.Zero;
    }

    public void Dispose()
    {
        DisposeAllTracks();
    }
}
