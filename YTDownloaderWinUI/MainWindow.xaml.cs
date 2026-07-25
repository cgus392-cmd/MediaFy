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

        // Banner del portapapeles: el watcher dispara cuando hay URL válida
        App.Clipboard.UrlDetected += url => DispatcherQueue.TryEnqueue(() => ShowClipboardBanner(url));

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
        // El VU solo se ejecuta mientras haya reproducción (se arranca en OnPlaybackChanged)

        // Centro de notificaciones
        NotifList.ItemsSource = Core.NotificationCenter.Tasks;
        Core.NotificationCenter.Changed += () => DispatcherQueue.TryEnqueue(RefreshNotifBadge);
        RefreshNotifBadge();
    }

    private void RefreshNotifBadge()
    {
        int active = Core.NotificationCenter.ActiveCount;
        NotifBadge.Value = active;
        NotifBadge.Visibility = active > 0 ? Visibility.Visible : Visibility.Collapsed;
        NotifEmpty.Visibility = Core.NotificationCenter.Tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Notif_Clear_Click(object sender, RoutedEventArgs e) => Core.NotificationCenter.Clear();

    /// <summary>Aplica el telón de fondo a toda la ventana en vivo.</summary>
    public void ApplyBackdrop(BackdropKind kind) => _backdrop?.Apply(kind);

    /// <summary>Navega a la página de Configuración (usado por el anuncio de novedades).</summary>
    public void OpenSettings()
    {
        ContentFrame.Navigate(typeof(SettingsPage));
        Nav.SelectedItem = Nav.SettingsItem;
    }

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
                case "resources":    ContentFrame.Navigate(typeof(ResourcePage));     break;
                case "experimental": ContentFrame.Navigate(typeof(ExperimentalPage)); break;
                case "about":        ContentFrame.Navigate(typeof(AboutPage));         break;
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
        // Limpieza ordenada antes de terminar
        try { _tray?.Dispose(); _tray = null; } catch { }
        try { App.Playback.Close(); } catch { }
        try { App.Clipboard.Disable(); } catch { }

        // Garantiza la muerte del proceso y de TODOS sus hijos (yt-dlp/ffmpeg):
        // WinUI 3 + MediaPlayer + bandeja suelen dejar el proceso vivo si solo cerramos la ventana.
        try { System.Diagnostics.Process.GetCurrentProcess().Kill(entireProcessTree: true); }
        catch { Environment.Exit(0); }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private bool _updateBannerDismissed;
    private string _clipboardSuggestedUrl = string.Empty;

    // ── Banner del portapapeles ────────────────────────────────
    private void ShowClipboardBanner(string url)
    {
        _clipboardSuggestedUrl = url;
        ClipboardUrlText.Text = url.Length > 80 ? url[..77] + "..." : url;
        ClipboardBanner.Visibility = Visibility.Visible;
    }

    private void ClipboardBanner_Download_Click(object sender, RoutedEventArgs e)
    {
        ClipboardBanner.Visibility = Visibility.Collapsed;
        if (!string.IsNullOrEmpty(_clipboardSuggestedUrl))
        {
            NavigateTo("downloads");
            if (ContentFrame.Content is Views.DownloadsPage dp)
                dp.PrefillUrl(_clipboardSuggestedUrl);
        }
    }

    private void ClipboardBanner_Dismiss_Click(object sender, RoutedEventArgs e)
        => ClipboardBanner.Visibility = Visibility.Collapsed;

    // ── Drag & drop sobre toda la ventana ──────────────────────
    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems) ||
            e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.WebLink) ||
            e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Soltar para descargar / abrir en MediaFy";
            e.DragUIOverride.IsCaptionVisible = true;
            e.DragUIOverride.IsContentVisible = true;
            e.DragUIOverride.IsGlyphVisible = true;
        }
        else e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        var def = e.GetDeferral();
        try
        {
            // 1) ¿Soltaron un enlace web?
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.WebLink))
            {
                var uri = await e.DataView.GetWebLinkAsync();
                HandleIncomingUrl(uri.ToString());
                return;
            }
            // 2) ¿Texto que parezca URL?
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
            {
                string t = (await e.DataView.GetTextAsync()).Trim();
                if (Core.PlatformDetector.LooksLikeUrl(t)) { HandleIncomingUrl(t); return; }
            }
            // 3) ¿Archivos? → abrir en el Editor si es media, o llevar a Biblioteca si no
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                foreach (var it in items)
                {
                    if (it is Windows.Storage.StorageFile sf)
                    {
                        string ext = System.IO.Path.GetExtension(sf.Path).ToLowerInvariant();
                        if (Array.IndexOf(Models.LibraryFile.MediaExtensions, ext) >= 0)
                        {
                            ContentFrame.Navigate(typeof(Views.EditorPage), sf.Path);
                            return;
                        }
                    }
                }
                // Si nada era media, simplemente navega a Biblioteca
                NavigateTo("library");
            }
        }
        catch { }
        finally { def.Complete(); }
    }

    private void RefreshUpdateBanner()
    {
        var u = App.Updater;
        bool mandatory = u.Latest?.Mandatory == true;

        // Una actualización obligatoria no se puede descartar: se ignora el "cerrar" previo
        // y se oculta el botón de cierre.
        if (_updateBannerDismissed && !mandatory) { UpdateBanner.Visibility = Visibility.Collapsed; return; }
        UpdateBannerDismiss.Visibility = mandatory ? Visibility.Collapsed : Visibility.Visible;

        if (u.State == Core.UpdateState.Available && u.Latest != null)
        {
            UpdateBanner.Visibility = Visibility.Visible;
            UpdateBannerText.Text = mandatory
                ? $"Actualización obligatoria: MediaFy {u.Latest.Version}. Debes actualizar para seguir usando la app correctamente."
                : $"MediaFy {u.Latest.Version} está disponible.";
            UpdateBannerActionText.Text = mandatory ? "Actualizar ahora" : "Ver actualización";
        }
        else if (u.State == Core.UpdateState.ReadyToInstall && u.Latest != null)
        {
            UpdateBanner.Visibility = Visibility.Visible;
            UpdateBannerText.Text = mandatory
                ? $"Actualización obligatoria lista: MediaFy {u.Latest.Version}."
                : $"MediaFy {u.Latest.Version} listo para instalar.";
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
        if (App.Playback.HasMedia && !_vuTimer.IsEnabled) _vuTimer.Start();
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

    private MiniPlayerWindow? _miniPlayer;
    private void Gp_PopOut_Click(object sender, RoutedEventArgs e)
    {
        if (_miniPlayer is null)
        {
            _miniPlayer = new MiniPlayerWindow();
            _miniPlayer.Closed += (_, _) => _miniPlayer = null;
            _miniPlayer.Activate();
        }
        else
        {
            _miniPlayer.Activate(); // ya abierto: traerlo al frente
        }
    }

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
        // Detener el VU cuando ya no hay audio y las barras decayeron (ahorra CPU en reposo)
        if (!App.Playback.HasMedia && _vuL < 0.01 && _vuR < 0.01) _vuTimer.Stop();
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
