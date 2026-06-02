using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using YTDownloader.Core;

namespace YTDownloader.Models;

public enum DownloadStatus { Pending, Queued, Fetching, Downloading, Converting, Done, Error, Canceled }

public partial class DownloadItem : ObservableObject
{
    public DownloadItem()
    {
        // Refleja el ajuste global "mostrar registro" al vuelo
        AppSettings.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.ShowLogs))
                OnPropertyChanged(nameof(ShowLogs));
        };
    }

    /// <summary>El panel de registro solo se muestra si el usuario lo activa en Configuración.</summary>
    public bool ShowLogs => AppSettings.Current.ShowLogs;

    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private string _title = "Obteniendo info...";
    [ObservableProperty] private string _thumbnail = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAudio))]
    private string _format = "MP4";

    [ObservableProperty] private string _quality = "Mejor";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _statusText = "En espera";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDone), nameof(IsError), nameof(IsActive),
        nameof(IsCanceled), nameof(CanCancel), nameof(CanRetry), nameof(ShowDetails))]
    private DownloadStatus _status = DownloadStatus.Pending;

    [ObservableProperty] private string _outputPath = string.Empty;
    [ObservableProperty] private string _duration = string.Empty;

    /// <summary>Si no es nulo, es una descarga de Spotify (match con YouTube + re-tag).</summary>
    public SpotifyTrack? Spotify { get; set; }

    /// <summary>Idioma de subtítulos a incrustar: Off | Auto | ES | EN.</summary>
    public string Subtitles { get; set; } = "Off";
    /// <summary>Si true, descarga la lista de reproducción completa (quita --no-playlist).</summary>
    public bool WholePlaylist { get; set; }

    // ── Progreso rico (Fase 2) ──
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDetails), nameof(DetailLine))]
    private string _speed = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailLine))]
    private string _eta = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailLine))]
    private string _sizeText = string.Empty;

    /// <summary>Línea compuesta: "1.6MiB/s · ETA 00:07 · 5.2 / 12.3 MiB".</summary>
    public string DetailLine
    {
        get
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrEmpty(Speed))    parts.Add(Speed);
            if (!string.IsNullOrEmpty(Eta))      parts.Add($"ETA {Eta}");
            if (!string.IsNullOrEmpty(SizeText)) parts.Add(SizeText);
            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>Líneas de log crudas de yt-dlp para el panel de registro.</summary>
    public ObservableCollection<string> Logs { get; } = new();

    public bool IsAudio    => Format is "MP3" or "M4A" or "OGG" or "FLAC" or "WAV" or "OPUS";
    public bool IsDone     => Status == DownloadStatus.Done;
    public bool IsError    => Status == DownloadStatus.Error;
    public bool IsCanceled => Status == DownloadStatus.Canceled;
    public bool IsActive   => Status is DownloadStatus.Downloading or DownloadStatus.Converting or DownloadStatus.Fetching or DownloadStatus.Queued;

    public bool CanCancel  => Status is DownloadStatus.Downloading or DownloadStatus.Converting or DownloadStatus.Fetching or DownloadStatus.Queued;
    public bool CanRetry   => Status is DownloadStatus.Error or DownloadStatus.Canceled;
    public bool ShowDetails => Status == DownloadStatus.Downloading && !string.IsNullOrEmpty(Speed);

    public void AddLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        Logs.Add(line);
        // Limita el historial para no crecer infinito
        if (Logs.Count > 500) Logs.RemoveAt(0);
    }
}
