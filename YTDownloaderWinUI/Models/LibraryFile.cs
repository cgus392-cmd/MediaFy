using System.IO;

namespace YTDownloader.Models;

public class LibraryFile
{
    public string FullPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTime Modified { get; set; }

    public bool IsAudio => Extension is ".mp3" or ".m4a" or ".ogg" or ".opus" or ".wav" or ".flac" or ".aac";
    public bool IsVideo => Extension is ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi";

    /// <summary>Glifo Segoe Fluent por código: MusicNote / Video / Page.</summary>
    public string Icon => char.ConvertFromUtf32(IsAudio ? 0xEC4F : IsVideo ? 0xE714 : 0xE7C3);

    public string TypeLabel => Extension.TrimStart('.').ToUpperInvariant();

    public string SizeText => SizeBytes switch
    {
        >= 1L << 30 => $"{SizeBytes / (double)(1L << 30):F2} GB",
        >= 1L << 20 => $"{SizeBytes / (double)(1L << 20):F1} MB",
        >= 1L << 10 => $"{SizeBytes / (double)(1L << 10):F0} KB",
        _           => $"{SizeBytes} B"
    };

    public string MetaLine => $"{TypeLabel}  ·  {SizeText}  ·  {Modified:dd/MM/yyyy HH:mm}";

    public static readonly string[] MediaExtensions =
    {
        ".mp4", ".webm", ".mkv", ".mov", ".avi",
        ".mp3", ".m4a", ".ogg", ".opus", ".wav", ".flac", ".aac"
    };

    public static LibraryFile From(FileInfo fi) => new()
    {
        FullPath = fi.FullName,
        Name = Path.GetFileNameWithoutExtension(fi.Name),
        Extension = fi.Extension.ToLowerInvariant(),
        SizeBytes = fi.Length,
        Modified = fi.LastWriteTime
    };
}
