using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;
using YTDownloader.Models;

namespace YTDownloader;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
        => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, string l)
        => value is Visibility v && v == Visibility.Visible;
}

public class StringToBitmapConverter : IValueConverter
{
    public object? Convert(object value, Type t, object p, string l)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s) && Uri.TryCreate(s, UriKind.Absolute, out var uri))
            return new BitmapImage(uri);
        return null;
    }
    public object ConvertBack(object value, Type t, object p, string l) => throw new NotImplementedException();
}

public class StatusToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
    {
        var c = value is DownloadStatus s ? s switch
        {
            DownloadStatus.Done        => Color.FromArgb(255, 0x6C, 0xCB, 0x5F),
            DownloadStatus.Error       => Color.FromArgb(255, 0xFF, 0x6B, 0x6B),
            DownloadStatus.Downloading => Color.FromArgb(255, 0x60, 0xCD, 0xFF),
            DownloadStatus.Fetching    => Color.FromArgb(255, 0x60, 0xCD, 0xFF),
            DownloadStatus.Queued      => Color.FromArgb(255, 0xC0, 0xC0, 0xC0),
            DownloadStatus.Converting  => Color.FromArgb(255, 0xB4, 0xA0, 0xFF),
            DownloadStatus.Canceled    => Color.FromArgb(255, 0xFF, 0xB0, 0x5C),
            _                          => Color.FromArgb(255, 0xC0, 0xC0, 0xC0),
        } : Colors.Gray;
        return new SolidColorBrush(c);
    }
    public object ConvertBack(object value, Type t, object p, string l) => throw new NotImplementedException();
}

/// <summary>Estado de salud → ícono (semáforo de diagnóstico).</summary>
public class HealthGlyphConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        char.ConvertFromUtf32(value is Core.HealthStatus s ? s switch
        {
            Core.HealthStatus.Ok      => 0xEC61, // check relleno
            Core.HealthStatus.Warning => 0xE7BA, // triángulo de aviso
            _                         => 0xEA39, // insignia de error
        } : 0xEC61);
    public object ConvertBack(object value, Type t, object p, string l) => throw new NotImplementedException();
}

/// <summary>Línea de letra actual (true) → color de acento; resto → color atenuado.</summary>
public class LyricBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        (Brush)Application.Current.Resources[
            value is true ? "AccentTextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush"];
    public object ConvertBack(object value, Type t, object p, string l) => throw new NotImplementedException();
}

/// <summary>Línea de letra actual (true) → SemiBold; resto → Normal.</summary>
public class LyricWeightConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        value is true ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
    public object ConvertBack(object value, Type t, object p, string l) => throw new NotImplementedException();
}

/// <summary>Texto no vacío → Visible; vacío/null → Collapsed (para botones de acción opcionales).</summary>
public class NotEmptyToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, string l) => throw new NotImplementedException();
}

/// <summary>Estado de salud → color (verde / ámbar / rojo).</summary>
public class HealthBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
    {
        var c = value is Core.HealthStatus s ? s switch
        {
            Core.HealthStatus.Ok      => Color.FromArgb(255, 0x3F, 0xB9, 0x50),
            Core.HealthStatus.Warning => Color.FromArgb(255, 0xFF, 0xB0, 0x5C),
            _                         => Color.FromArgb(255, 0xFF, 0x6B, 0x6B),
        } : Colors.Gray;
        return new SolidColorBrush(c);
    }
    public object ConvertBack(object value, Type t, object p, string l) => throw new NotImplementedException();
}

public class StatusToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, string l)
    {
        var c = value is DownloadStatus s ? s switch
        {
            DownloadStatus.Done        => Color.FromArgb(0x26, 0x16, 0xC6, 0x0A),
            DownloadStatus.Error       => Color.FromArgb(0x26, 0xE8, 0x11, 0x23),
            DownloadStatus.Downloading => Color.FromArgb(0x26, 0x00, 0x78, 0xD4),
            DownloadStatus.Fetching    => Color.FromArgb(0x26, 0x00, 0x78, 0xD4),
            DownloadStatus.Converting  => Color.FromArgb(0x26, 0x74, 0x4D, 0xA9),
            DownloadStatus.Canceled    => Color.FromArgb(0x26, 0xFF, 0xB0, 0x5C),
            _                          => Color.FromArgb(0x18, 0x80, 0x80, 0x80),
        } : Color.FromArgb(0x18, 0x80, 0x80, 0x80);
        return new SolidColorBrush(c);
    }
    public object ConvertBack(object value, Type t, object p, string l) => throw new NotImplementedException();
}
