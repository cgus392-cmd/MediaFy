using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
using Windows.UI.ViewManagement;

namespace YTDownloader.Core;

/// <summary>
/// Sincroniza el tema (claro/oscuro) y el color de acento con Windows en tiempo real.
/// Usa UISettings, que notifica cambios del sistema operativo al vuelo.
/// </summary>
public sealed class SystemTheme
{
    private readonly UISettings _ui = new();
    private readonly DispatcherQueue _dispatcher;
    private readonly Action _onColorChanged;

    public SystemTheme(DispatcherQueue dispatcher, Action onColorChanged)
    {
        _dispatcher = dispatcher;
        _onColorChanged = onColorChanged;
        // El evento llega en un hilo distinto: hay que volver al hilo de UI.
        _ui.ColorValuesChanged += (_, _) => _dispatcher.TryEnqueue(() => _onColorChanged());
    }
}
