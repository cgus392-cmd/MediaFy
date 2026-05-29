namespace YTDownloader.Models;

public class VideoInfo
{
    public string Title { get; set; } = string.Empty;
    public string Thumbnail { get; set; } = string.Empty;
    public string Duration { get; set; } = string.Empty;
    public string Uploader { get; set; } = string.Empty;
    public long FileSizeApprox { get; set; }
    public bool IsPlaylist { get; set; }
    public List<string> Entries { get; set; } = new();
}
