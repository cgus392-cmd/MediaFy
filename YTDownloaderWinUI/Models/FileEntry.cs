using System.IO;

namespace YTDownloader.Models;

/// <summary>Una entrada (archivo o carpeta) mostrada en un panel del organizador.</summary>
public class FileEntry
{
    public string FullPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long SizeBytes { get; set; }
    public DateTime Modified { get; set; }
    public string Extension { get; set; } = string.Empty;

    public bool IsAudio => !IsDirectory && Extension is ".mp3" or ".m4a" or ".ogg" or ".opus" or ".wav" or ".flac" or ".aac";
    public bool IsVideo => !IsDirectory && Extension is ".mp4" or ".webm" or ".mkv" or ".mov" or ".avi";

    /// <summary>Glifo Segoe Fluent: Folder / MusicNote / Video / Page.</summary>
    public string Icon => char.ConvertFromUtf32(
        IsDirectory ? 0xE8B7 :
        IsAudio     ? 0xEC4F :
        IsVideo     ? 0xE714 :
                      0xE7C3);

    public string SizeText => IsDirectory ? "" : SizeBytes switch
    {
        >= 1L << 30 => $"{SizeBytes / (double)(1L << 30):F2} GB",
        >= 1L << 20 => $"{SizeBytes / (double)(1L << 20):F1} MB",
        >= 1L << 10 => $"{SizeBytes / (double)(1L << 10):F0} KB",
        _           => $"{SizeBytes} B"
    };

    public string DateText => Modified.ToString("dd/MM/yyyy HH:mm");

    public static FileEntry FromDir(DirectoryInfo d) => new()
    {
        FullPath = d.FullName, Name = d.Name, IsDirectory = true,
        Modified = d.LastWriteTime
    };

    public static FileEntry FromFile(FileInfo f) => new()
    {
        FullPath = f.FullName, Name = f.Name, IsDirectory = false,
        Extension = f.Extension.ToLowerInvariant(),
        SizeBytes = f.Length, Modified = f.LastWriteTime
    };
}
