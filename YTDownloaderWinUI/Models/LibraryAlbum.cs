using System.Collections.ObjectModel;

namespace YTDownloader.Models;

/// <summary>Un álbum/lista = subcarpeta de la biblioteca que contiene pistas.</summary>
public class LibraryAlbum
{
    public string Name { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public ObservableCollection<LibraryFile> Tracks { get; } = new();

    public int TrackCount => Tracks.Count;
    public string MetaLine => TrackCount == 1 ? "1 pista" : $"{TrackCount} pistas";
}
