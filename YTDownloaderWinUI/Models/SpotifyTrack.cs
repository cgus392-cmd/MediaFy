namespace YTDownloader.Models;

/// <summary>Una pista de Spotify con sus metadatos (para buscar el audio en YouTube).</summary>
public class SpotifyTrack
{
    public string Title { get; set; } = string.Empty;
    public string Artists { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string CoverUrl { get; set; } = string.Empty;
    public int DurationMs { get; set; }

    /// <summary>Consulta de búsqueda en YouTube.</summary>
    public string SearchQuery => $"{Artists} {Title}";
}
