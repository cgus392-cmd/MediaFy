using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YTDownloader.Models;

namespace YTDownloader.Views;

/// <summary>Elige plantilla según el tipo de entrada de la biblioteca: álbum (carpeta) o archivo.</summary>
public class LibraryItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FileTemplate { get; set; }
    public DataTemplate? AlbumTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item)
        => item is LibraryAlbum ? AlbumTemplate : FileTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}
