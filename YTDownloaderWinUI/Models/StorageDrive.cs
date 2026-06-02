using System.IO;

namespace YTDownloader.Models;

/// <summary>Una unidad de almacenamiento (o la carpeta "hogar" de la biblioteca).</summary>
public class StorageDrive
{
    public string Root { get; set; } = string.Empty;     // "D:\" o la carpeta de descargas
    public string Letter { get; set; } = string.Empty;   // "D:"
    public string Label { get; set; } = string.Empty;    // etiqueta del volumen
    public DriveType Type { get; set; } = DriveType.Fixed;
    public long FreeBytes { get; set; }
    public long TotalBytes { get; set; }

    /// <summary>True si es la carpeta de descargas de MediaFy (no una unidad física).</summary>
    public bool IsHome { get; set; }
    public bool IsRemovable => Type == DriveType.Removable;

    public string TypeLabel => IsHome ? "Carpeta" : Type switch
    {
        DriveType.Fixed     => "Local",
        DriveType.Removable => "Extraíble",
        DriveType.Network   => "Red",
        DriveType.CDRom     => "CD/DVD",
        _                   => "Disco"
    };

    public string Icon => IsHome
        ? char.ConvertFromUtf32(0xE8B7)   // carpeta
        : char.ConvertFromUtf32(0xEDA2);  // disco

    public string DisplayName => IsHome
        ? "Descargas de MediaFy"
        : (string.IsNullOrWhiteSpace(Label) ? $"Disco ({Letter})" : $"{Label} ({Letter})");

    public double UsedFraction => TotalBytes > 0 ? (double)(TotalBytes - FreeBytes) / TotalBytes : 0;

    public string SpaceText => TotalBytes > 0
        ? $"{Human(FreeBytes)} libres de {Human(TotalBytes)}"
        : string.Empty;

    private static string Human(long b) => b switch
    {
        >= 1L << 40 => $"{b / (double)(1L << 40):F1} TB",
        >= 1L << 30 => $"{b / (double)(1L << 30):F1} GB",
        >= 1L << 20 => $"{b / (double)(1L << 20):F0} MB",
        _           => $"{b} B"
    };
}
