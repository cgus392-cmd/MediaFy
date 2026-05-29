using CommunityToolkit.Mvvm.ComponentModel;

namespace YTDownloader.Models;

public enum DownloadStatus { Pending, Fetching, Downloading, Converting, Done, Error }

public partial class DownloadItem : ObservableObject
{
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
    [NotifyPropertyChangedFor(nameof(IsDone), nameof(IsError), nameof(IsActive))]
    private DownloadStatus _status = DownloadStatus.Pending;

    [ObservableProperty] private string _outputPath = string.Empty;
    [ObservableProperty] private string _duration = string.Empty;
    [ObservableProperty] private string _fileSize = string.Empty;

    public bool IsAudio => Format is "MP3" or "M4A" or "OGG";
    public bool IsDone   => Status == DownloadStatus.Done;
    public bool IsError  => Status == DownloadStatus.Error;
    public bool IsActive => Status is DownloadStatus.Downloading or DownloadStatus.Converting or DownloadStatus.Fetching;
}
