namespace YTDownloader.Models;

public class VideoInfo
{
    public string Title { get; set; } = string.Empty;
    public string Thumbnail { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string Uploader { get; set; } = string.Empty;
    public long FileSizeApprox { get; set; }
    public bool IsPlaylist { get; set; }
    public int PlaylistCount { get; set; }
    public List<string> Entries { get; set; } = new();

    /// <summary>Alturas de vídeo disponibles (p. ej. 2160, 1080, 720...), de mayor a menor.</summary>
    public List<int> AvailableHeights { get; set; } = new();

    /// <summary>Etiquetas legibles de calidad disponibles (p. ej. "4K", "1080p", "720p").</summary>
    public List<string> QualityLabels => AvailableHeights.Select(HeightToLabel).ToList();

    public static string HeightToLabel(int h) => h switch
    {
        >= 4320 => "8K",
        >= 2160 => "4K",
        >= 1440 => "1440p",
        >= 1080 => "1080p",
        >= 720  => "720p",
        >= 480  => "480p",
        >= 360  => "360p",
        >= 240  => "240p",
        _       => $"{h}p"
    };
}
