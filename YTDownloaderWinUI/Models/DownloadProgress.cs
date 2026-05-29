namespace YTDownloader.Models;

/// <summary>
/// Instantánea de progreso de una descarga. Percent = -1 significa "fase sin %"
/// (conversión, fusión de pistas, etc.).
/// </summary>
public readonly record struct DownloadProgress(
    double Percent,
    string Status,
    string Speed = "",
    string Eta = "",
    string SizeText = "",
    string? LogLine = null);
