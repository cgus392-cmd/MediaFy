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
            DownloadStatus.Converting  => Color.FromArgb(255, 0xB4, 0xA0, 0xFF),
            _                          => Color.FromArgb(255, 0xC0, 0xC0, 0xC0),
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
            _                          => Color.FromArgb(0x18, 0x80, 0x80, 0x80),
        } : Color.FromArgb(0x18, 0x80, 0x80, 0x80);
        return new SolidColorBrush(c);
    }
    public object ConvertBack(object value, Type t, object p, string l) => throw new NotImplementedException();
}
