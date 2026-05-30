using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Media.Casting;
using YTDownloader.Core;
using YTDownloader.Views;

namespace YTDownloader;

public sealed partial class MainWindow : Window
{
    private bool _onLibrary;
    private bool _volSync;
    private BackdropManager? _backdrop;

    // Suavizado del VU (sobre niveles REALES del servicio)
    private readonly DispatcherTimer _vuTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private double _vuL, _vuR;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Title = "MediaFy";
        AppWindow.Resize(new SizeInt32(1000, 740));
        try { AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "logo.ico")); } catch { }

        _backdrop = new BackdropManager(this);
        _backdrop.Apply(AppSettings.Current.BackdropKind);

        ContentFrame.Navigated += (_, e) =>
        {
            _onLibrary = e.SourcePageType == typeof(LibraryPage);
            UpdatePlayersVisibility();
        };
        ContentFrame.Navigate(typeof(HomePage));

        SyncVolumeSliders();
        App.Playback.Changed += OnPlaybackChanged;

        _vuTimer.Tick += VuTimer_Tick;
        _vuTimer.Start();
    }

    /// <summary>Aplica el telón de fondo a toda la ventana en vivo.</summary>
    public void ApplyBackdrop(BackdropKind kind) => _backdrop?.Apply(kind);

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected) { ContentFrame.Navigate(typeof(SettingsPage)); return; }
        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            switch (item.Tag?.ToString())
            {
                case "home":      ContentFrame.Navigate(typeof(HomePage));      break;
                case "downloads": ContentFrame.Navigate(typeof(DownloadsPage)); break;
                case "cascade":   ContentFrame.Navigate(typeof(CascadePage));   break;
                case "library":   ContentFrame.Navigate(typeof(LibraryPage));   break;
                case "editor":    ContentFrame.Navigate(typeof(EditorPage));    break;
                case "about":     ContentFrame.Navigate(typeof(AboutPage));     break;
            }
        }
    }

    /// <summary>Selecciona una sección por su tag (lo usan los accesos rápidos de Inicio).</summary>
    public void NavigateTo(string tag)
    {
        foreach (var obj in Nav.MenuItems)
            if (obj is NavigationViewItem nvi && (string?)nvi.Tag == tag) { Nav.SelectedItem = nvi; return; }
    }

    // ── Reproductor global ─────────────────────────────────────
    private void OnPlaybackChanged() => DispatcherQueue.TryEnqueue(() =>
    {
        GpName.Text = App.Playback.CurrentTitle;
        GpMiniName.Text = App.Playback.CurrentTitle;
        UpdatePlayIcon();
        UpdatePlayersVisibility();
    });

    private void UpdatePlayIcon()
    {
        string g = char.ConvertFromUtf32(App.Playback.IsPlaying ? 0xE769 : 0xE768);
        GpPlayIcon.Glyph = g;
        GpMiniPlayIcon.Glyph = g;
    }

    private void UpdatePlayersVisibility()
    {
        bool media = App.Playback.HasMedia;
        GlobalPlayer.Visibility = media && _onLibrary ? Visibility.Visible : Visibility.Collapsed;
        MiniPlayer.Visibility   = media && !_onLibrary ? Visibility.Visible : Visibility.Collapsed;
        SyncVolumeSliders();
    }

    private void SyncVolumeSliders()
    {
        _volSync = true;
        double v = App.Playback.Volume * 100;
        GpVolume.Value = v;
        GpMiniVolume.Value = v;
        _volSync = false;
    }

    private void Gp_PlayPause_Click(object sender, RoutedEventArgs e) => App.Playback.Toggle();

    private void Gp_Volume_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_volSync || GpVolume is null || GpMiniVolume is null) return;
        App.Playback.SetVolume(e.NewValue / 100.0);
        SyncVolumeSliders();
    }

    private void Gp_Close_Click(object sender, RoutedEventArgs e) => App.Playback.Close();

    // ── VU meter REAL (niveles L/R desde AudioGraph) ──────────
    private void VuTimer_Tick(object? sender, object e)
    {
        double tL = App.Playback.HasMedia ? App.Playback.LevelLeft : 0;
        double tR = App.Playback.HasMedia ? App.Playback.LevelRight : 0;

        // Ataque rápido, caída suave (comportamiento típico de un VU)
        _vuL = tL > _vuL ? tL : _vuL + (tL - _vuL) * 0.25;
        _vuR = tR > _vuR ? tR : _vuR + (tR - _vuR) * 0.25;

        SetBar(GpVuLeft, _vuL);
        SetBar(GpVuRight, _vuR);
        SetBar(GpVuLeftBig, _vuL);
        SetBar(GpVuRightBig, _vuR);
    }

    private static void SetBar(FrameworkElement bar, double level)
    {
        if (bar.Parent is FrameworkElement host && host.ActualWidth > 0)
            bar.Width = Math.Max(0, Math.Min(1, level) * host.ActualWidth);
    }

    // ── Transmitir a dispositivo ───────────────────────────────
    private void Gp_Cast_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new CastingDevicePicker();
            picker.Filter.SupportsAudio = true;
            picker.Filter.SupportsVideo = true;

            var fe = (FrameworkElement)sender;
            var ttv = fe.TransformToVisual(Content);
            var pt = ttv.TransformPoint(new Point(0, 0));
            picker.Show(new Rect(pt.X, pt.Y, fe.ActualWidth, fe.ActualHeight));
        }
        catch
        {
            _ = ShowInfoAsync("Transmitir a dispositivo",
                "La transmisión a dispositivos no está disponible en este equipo o no hay dispositivos compatibles cerca.");
        }
    }

    private async Task ShowInfoAsync(string title, string message)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "Aceptar",
            XamlRoot = Content.XamlRoot
        };
        await dlg.ShowAsync();
    }
}
