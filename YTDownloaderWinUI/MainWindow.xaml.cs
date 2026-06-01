using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
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
    private TaskbarIcon? _tray;
    private bool _reallyExit;

    // VU del MediaPlayer (ligero, no toca samples → cero microcortes)
    private readonly DispatcherTimer _vuTimer = new() { Interval = TimeSpan.FromMilliseconds(70) };
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

        SetupTray();
        AppWindow.Closing += OnClosing;

        // Banner de actualización: se muestra cuando el UpdateService la detecta
        App.Updater.StateChanged += s => DispatcherQueue.TryEnqueue(RefreshUpdateBanner);
        RefreshUpdateBanner();

        // Si se inició como autoarranque con Windows, la ventana no aparece (solo la bandeja)
        if (App.StartedInTray) AppWindow.Hide();

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
                case "organizer": ContentFrame.Navigate(typeof(OrganizerPage)); break;
                case "resources": ContentFrame.Navigate(typeof(ResourcePage));  break;
                case "about":     ContentFrame.Navigate(typeof(AboutPage));     break;
            }
        }
    }

    // ── Bandeja del sistema / segundo plano ────────────────────
    private void SetupTray()
    {
        try
        {
            var menu = new MenuFlyout();
            var open = new MenuFlyoutItem { Text = "Abrir MediaFy" };
            open.Click += (_, _) => ShowFromTray();
            var exit = new MenuFlyoutItem { Text = "Salir" };
            exit.Click += (_, _) => ExitApp();
            menu.Items.Add(open);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(exit);

            _tray = new TaskbarIcon
            {
                ToolTipText = "MediaFy by CG",
                IconSource = new BitmapImage(new Uri("ms-appx:///Assets/logo.ico")),
                ContextFlyout = menu,
                LeftClickCommand = new RelayCommand(ShowFromTray)
            };
            _tray.ForceCreate();
        }
        catch { /* sin bandeja, la app sigue funcionando */ }
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_reallyExit) return;
        args.Cancel = true;           // no cerrar: ir a segundo plano
        AppWindow.Hide();
    }

    private void ShowFromTray()
    {
        AppWindow.Show();
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            SetForegroundWindow(hwnd);
        }
        catch { }
    }

    private void ExitApp()
    {
        _reallyExit = true;
        try { _tray?.Dispose(); } catch { }
        Close();
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private bool _updateBannerDismissed;

    private void RefreshUpdateBanner()
    {
        if (_updateBannerDismissed) { UpdateBanner.Visibility = Visibility.Collapsed; return; }
        var u = App.Updater;
        if (u.State == Core.UpdateState.Available && u.Latest != null)
        {
            UpdateBanner.Visibility = Visibility.Visible;
            UpdateBannerText.Text = $"MediaFy {u.Latest.Version} está disponible.";
            UpdateBannerActionText.Text = "Ver actualización";
        }
        else if (u.State == Core.UpdateState.ReadyToInstall && u.Latest != null)
        {
            UpdateBanner.Visibility = Visibility.Visible;
            UpdateBannerText.Text = $"MediaFy {u.Latest.Version} listo para instalar.";
            UpdateBannerActionText.Text = "Instalar ahora";
        }
        else
        {
            UpdateBanner.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateBanner_Action_Click(object sender, RoutedEventArgs e)
    {
        if (App.Updater.State == Core.UpdateState.ReadyToInstall)
        {
            App.Updater.Install();
            return;
        }
        // Si solo está disponible (aún no descargada), llevar al usuario a Configuración para decidir
        ContentFrame.Navigate(typeof(SettingsPage));
        foreach (var obj in Nav.MenuItems)
            if (obj is NavigationViewItem nvi && (string?)nvi.Tag is null) { /* no-op */ }
        // No hay item de menú con tag "settings" — usar el item built-in Settings:
        Nav.SelectedItem = Nav.SettingsItem;
    }

    private void UpdateBanner_Dismiss_Click(object sender, RoutedEventArgs e)
    {
        _updateBannerDismissed = true;
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    /// <summary>Trae la ventana al frente (la usa la activación entrante).</summary>
    public void BringToFront()
    {
        try
        {
            AppWindow.Show();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            SetForegroundWindow(hwnd);
        }
        catch { }
    }

    /// <summary>Recibe una URL desde la línea de comandos o el protocolo mediafy://.</summary>
    public void HandleIncomingUrl(string url)
    {
        NavigateTo("downloads");
        if (ContentFrame.Content is DownloadsPage dp)
            dp.PrefillUrl(url);
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

    // ── VU meter (MediaPlayer + AudioStateMonitor, ligero, sin glitches) ──
    private void VuTimer_Tick(object? sender, object e)
    {
        App.Playback.TickLevels();
        _vuL = App.Playback.LevelLeft;
        _vuR = App.Playback.LevelRight;
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
