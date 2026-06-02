using Windows.ApplicationModel.DataTransfer;

namespace YTDownloader.Core;

/// <summary>
/// Vigila el portapapeles y dispara un evento cuando aparece una URL válida
/// soportada por MediaFy. No procesa la misma URL dos veces seguidas.
/// </summary>
public class ClipboardWatcher
{
    private string _lastUrl = string.Empty;
    private bool _wired;

    /// <summary>Se dispara con una URL válida lista para sugerir descarga.</summary>
    public event Action<string>? UrlDetected;

    public void Enable()
    {
        if (_wired) return;
        Clipboard.ContentChanged += OnContentChanged;
        _wired = true;
    }

    public void Disable()
    {
        if (!_wired) return;
        try { Clipboard.ContentChanged -= OnContentChanged; } catch { }
        _wired = false;
        _lastUrl = string.Empty;
    }

    private async void OnContentChanged(object? sender, object e)
    {
        // El toggle puede haber sido apagado por el usuario en este instante
        if (!AppSettings.Current.ClipboardWatch) return;
        try
        {
            var dp = Clipboard.GetContent();
            if (!dp.Contains(StandardDataFormats.Text)) return;

            string text = (await dp.GetTextAsync()).Trim();
            if (!PlatformDetector.LooksLikeUrl(text)) return;

            var platform = PlatformDetector.Detect(text);
            // Solo nos interesan plataformas conocidas y habilitadas
            if (!AppSettings.Current.IsPlatformEnabled(platform)) return;

            // Evita repetir la misma URL una y otra vez
            if (text == _lastUrl) return;
            _lastUrl = text;

            UrlDetected?.Invoke(text);
        }
        catch { /* el portapapeles puede estar bloqueado por otra app un instante */ }
    }
}
