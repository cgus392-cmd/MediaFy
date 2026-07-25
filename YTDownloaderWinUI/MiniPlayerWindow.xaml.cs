using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using YTDownloader.Core;
using Path = System.IO.Path;
using File = System.IO.File;

namespace YTDownloader;

public sealed partial class MiniPlayerWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly PlaybackService _pb = App.Playback;
    private string _mode = "Bar";

    public MiniPlayerWindow()
    {
        InitializeComponent();
        Title = "MediaFy · Mini";

        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsAlwaysOnTop = true;
            p.IsMaximizable = false;
            p.IsMinimizable = false;
            p.IsResizable = true;           // redimensionable
            p.SetBorderAndTitleBar(true, false);
        }
        ExtendsContentIntoTitleBar = true;
        try { AppWindow.IsShownInSwitchers = false; } catch { }
        try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico")); } catch { }

        _pb.Changed += OnPlaybackChanged;
        _timer.Tick += (_, _) => UpdateProgress();
        _timer.Start();
        Closed += (_, _) => { _pb.Changed -= OnPlaybackChanged; _timer.Stop(); };

        ApplyMode(AppSettings.Current.MiniPlayerMode);
        Refresh();
    }

    /// <summary>Aplica la forma del mini-reproductor: "Bar" (barra) o "Square" (cuadrado).</summary>
    private void ApplyMode(string mode)
    {
        _mode = mode == "Square" ? "Square" : "Bar";
        AppSettings.Current.MiniPlayerMode = _mode;

        bool square = _mode == "Square";
        BarLayout.Visibility    = square ? Visibility.Collapsed : Visibility.Visible;
        SquareLayout.Visibility = square ? Visibility.Visible : Visibility.Collapsed;

        int w = square ? 300 : 380;
        int h = square ? 360 : 96;
        AppWindow.Resize(new SizeInt32(w, h));
        SetTitleBar(square ? SqDragArea : BarDragArea);

        // Re-anclar a la esquina inferior derecha tras cambiar de tamaño
        try
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            AppWindow.Move(new PointInt32(area.X + area.Width - w - 24, area.Y + area.Height - h - 48));
        }
        catch { }
    }

    private void LayoutCycle_Click(object sender, RoutedEventArgs e)
        => ApplyMode(_mode == "Bar" ? "Square" : "Bar");

    private void OnPlaybackChanged() => DispatcherQueue.TryEnqueue(Refresh);

    private void Refresh()
    {
        string title = string.IsNullOrEmpty(_pb.CurrentTitle) ? "MediaFy" : _pb.CurrentTitle;
        string artist = _pb.HasMedia
            ? (string.IsNullOrEmpty(_pb.CurrentArtist) ? "Reproduciendo" : _pb.CurrentArtist)
            : "Nada en reproducción";
        string playGlyph = _pb.IsPlaying ? char.ConvertFromUtf32(0xE769) : char.ConvertFromUtf32(0xE768);

        MiniTitle.Text = title;  SqTitle.Text = title;
        MiniArtist.Text = artist; SqArtist.Text = artist;
        MiniPlayIcon.Glyph = playGlyph; SqPlayIcon.Glyph = playGlyph;

        string? cover = _pb.CurrentCover;
        if (!string.IsNullOrEmpty(cover) && (File.Exists(cover) || cover.StartsWith("http")))
        {
            var bmp = new BitmapImage(new Uri(cover));
            MiniCover.Source = bmp; SqCover.Source = bmp;
            MiniCover.Visibility = SqCover.Visibility = Visibility.Visible;
            MiniIcon.Visibility = SqIcon.Visibility = Visibility.Collapsed;
        }
        else
        {
            MiniCover.Visibility = SqCover.Visibility = Visibility.Collapsed;
            MiniIcon.Visibility = SqIcon.Visibility = Visibility.Visible;
        }
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        var s = _pb.Player.PlaybackSession;
        double v = (s != null && _pb.HasMedia && s.NaturalDuration.TotalSeconds > 0)
            ? Math.Clamp(s.Position.TotalSeconds / s.NaturalDuration.TotalSeconds * 1000, 0, 1000)
            : 0;
        MiniProgress.Value = v;
        SqProgress.Value = v;
    }

    private void Play_Click(object sender, RoutedEventArgs e) { _pb.Toggle(); Refresh(); }
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
