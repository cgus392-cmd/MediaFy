using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
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
        App.Playback.QueueChanged += () => DispatcherQueue.TryEnqueue(() =>
        {
            if (QueueList?.ItemsSource != null) QueueList.SelectedIndex = App.Playback.QueueIndex;
        });

        Core.DiagnosticsService.Updated += OnHealthUpdated;
        RunStartupHealthAsync();

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
        // El guardado de ajustes es diferido: forzarlo aquí para no perder el último cambio.
        try { AppSettings.Current.SaveNow(); } catch { }

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
        UpdateMiniCover();
        GpPrev.IsEnabled = LyricsPrev.IsEnabled = App.Playback.HasPrev;
        GpNext.IsEnabled = LyricsNext.IsEnabled = App.Playback.HasNext;
        if (App.Playback.HasMedia && !_vuTimer.IsEnabled) _vuTimer.Start();

        // Letra: si cambió la canción, invalida la actual y refresca si el panel está abierto.
        string lyricKey = App.Playback.CurrentTitle + "|" + App.Playback.CurrentArtist;
        if (lyricKey != _lyricKey)
        {
            _lyrics = null; _lyricIndex = -1;
            if (_lyricsOpen) _ = LoadLyricsForCurrentAsync();
        }
    });

    // Carátula difuminada de fondo del mini-reproductor (frosted glass sobre la portada).
    private string? _miniCoverShown;
    private void UpdateMiniCover()
    {
        string? cover = App.Playback.CurrentCover;
        if (cover == _miniCoverShown) return; // ya cargada, evitar recrear el bitmap
        _miniCoverShown = cover;

        bool ok = !string.IsNullOrEmpty(cover) &&
                  (cover.StartsWith("http") || System.IO.File.Exists(cover));
        if (ok)
        {
            try
            {
                MiniCardCover.Source = new BitmapImage(new Uri(cover!));
                MiniCardCover.Opacity = 1;
                MiniCoverFrost.Visibility = Visibility.Visible;
                return;
            }
            catch { /* url/imagen inválida → caer al vidrio translúcido */ }
        }
        MiniCardCover.Source = null;
        MiniCardCover.Opacity = 0;
        MiniCoverFrost.Visibility = Visibility.Collapsed;
    }

    private void UpdatePlayIcon()
    {
        string g = char.ConvertFromUtf32(App.Playback.IsPlaying ? 0xE769 : 0xE768);
        GpPlayIcon.Glyph = g;
        GpMiniPlayIcon.Glyph = g;
        LyricsPlayIcon.Glyph = g;
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
        LyricsVolume.Value = v;
        _volSync = false;
    }

    private void Gp_PlayPause_Click(object sender, RoutedEventArgs e) => App.Playback.Toggle();
    private void Gp_Prev_Click(object sender, RoutedEventArgs e) => App.Playback.Previous();
    private void Gp_Next_Click(object sender, RoutedEventArgs e) => App.Playback.Next();

    // ── Vista de cola ──────────────────────────────────────────
    private void Queue_FlyoutOpened(object? sender, object e)
    {
        // La ObservableCollection se refleja en vivo; solo enlazamos una vez y marcamos la actual.
        if (QueueList.ItemsSource is null) QueueList.ItemsSource = App.Playback.Queue;
        QueueList.SelectedIndex = App.Playback.QueueIndex;
    }

    private void Queue_ItemClick(object sender, ItemClickEventArgs e)
    {
        int idx = App.Playback.Queue.IndexOf((Core.QueueItem)e.ClickedItem);
        if (idx >= 0) App.Playback.PlayAt(idx);
    }

    private void QueueRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Core.QueueItem item) return;
        int idx = App.Playback.Queue.IndexOf(item);
        if (idx >= 0) App.Playback.RemoveAt(idx);
    }

    // ── Estado del sistema (diagnóstico) ───────────────────────
    private async void RunStartupHealthAsync()
    {
        Core.DiagnosticsService.RefreshLight(); // instantáneo → pinta el semáforo ya
        // Prueba real de YouTube 1×/día (throttled) — caza rupturas como el cambio anti-bot.
        if (DateTime.UtcNow - Core.AppSettings.Current.LastHealthCheckUtc > TimeSpan.FromHours(24))
            await Core.DiagnosticsService.RunFullAsync();
    }

    private void OnHealthUpdated() => DispatcherQueue.TryEnqueue(() =>
    {
        UpdateHealthIcon();
        RefreshHealthList();
    });

    private void UpdateHealthIcon()
    {
        var overall = Core.DiagnosticsService.Overall;
        HealthIcon.Glyph = char.ConvertFromUtf32(overall switch
        {
            Core.HealthStatus.Ok      => 0xEC61,
            Core.HealthStatus.Warning => 0xE7BA,
            _                         => 0xEA39,
        });
        HealthIcon.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(overall switch
        {
            Core.HealthStatus.Ok      => Windows.UI.Color.FromArgb(255, 0x3F, 0xB9, 0x50),
            Core.HealthStatus.Warning => Windows.UI.Color.FromArgb(255, 0xFF, 0xB0, 0x5C),
            _                         => Windows.UI.Color.FromArgb(255, 0xFF, 0x6B, 0x6B),
        });
    }

    private void RefreshHealthList()
    {
        if (HealthList is null) return;
        HealthList.ItemsSource = null;
        HealthList.ItemsSource = Core.DiagnosticsService.LastResults;
        HealthSummary.Text = Core.DiagnosticsService.Overall switch
        {
            Core.HealthStatus.Ok      => "Todo en orden.",
            Core.HealthStatus.Warning => "Hay avisos que conviene revisar.",
            _                         => "Hay un problema que atender.",
        };
    }

    private async void Health_FlyoutOpened(object? sender, object e)
    {
        RefreshHealthList();
        if (DateTime.UtcNow - Core.AppSettings.Current.LastHealthCheckUtc > TimeSpan.FromHours(24))
            await Core.DiagnosticsService.RunFullAsync();
    }

    private async void Health_Recheck_Click(object sender, RoutedEventArgs e)
    {
        HealthSummary.Text = "Comprobando…";
        Core.AppSettings.Current.LastHealthCheckUtc = DateTime.MinValue; // fuerza la prueba pesada
        await Core.DiagnosticsService.RunFullAsync();
    }

    private void Health_Action_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Core.HealthCheck hc) return;
        switch (hc.ActionKey)
        {
            case "import-cookies":
                OpenSettings();
                break;
            case "update-ytdlp":
                hc.Detail = "Actualizando yt-dlp…";
                RefreshHealthList();
                _ = Task.Run(async () =>
                {
                    await App.DownloadManager.UpdateYtDlpAsync();
                    Core.DiagnosticsService.RefreshLight();
                });
                break;
        }
    }

    // ── Letra sincronizada (vista inmersiva, karaoke) ──────────
    private List<Core.LyricLineVm>? _lyrics;
    private string _lyricKey = "";
    private int _lyricIndex = -1;
    private bool _lyricsOpen;
    private readonly List<TextBlock> _lyricBlocks = new();
    private double _lyricScale = 1.0;
    private bool _lyricCentered = true;

    private void Lyrics_OpenOverlay(object sender, RoutedEventArgs e)
    {
        _lyricsOpen = true;
        LyricsOverlay.Visibility = Visibility.Visible;
        LyricsAlignIcon.Glyph = char.ConvertFromUtf32(_lyricCentered ? 0xE8E9 : 0xE8E4);
        SetLyricsBg();
        _ = LoadLyricsForCurrentAsync();
    }

    private void Lyrics_CloseOverlay(object sender, RoutedEventArgs e)
    {
        _lyricsOpen = false;
        LyricsOverlay.Visibility = Visibility.Collapsed;
    }

    private void SetLyricsBg()
    {
        string? cover = App.Playback.CurrentCover;
        bool ok = !string.IsNullOrEmpty(cover) && (cover.StartsWith("http") || System.IO.File.Exists(cover));
        if (ok)
        {
            try
            {
                var bmp = new BitmapImage(new Uri(cover!));
                LyricsBgImage.Source = bmp;          // fondo difuminado
                LyricsBgImage.Opacity = 1;
                LyricsCover.Source = bmp;            // caratula del panel derecho
                LyricsCover.Visibility = Visibility.Visible;
                LyricsCoverIcon.Visibility = Visibility.Collapsed;
                return;
            }
            catch { /* url/imagen invalida */ }
        }
        LyricsBgImage.Source = null; LyricsBgImage.Opacity = 0;
        LyricsCover.Source = null;
        LyricsCover.Visibility = Visibility.Collapsed;
        LyricsCoverIcon.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// En ventanas estrechas no caben los dos paneles: se oculta el de la caratula para que la
    /// letra siga siendo legible (los controles siguen disponibles en la barra del reproductor).
    /// </summary>
    private void LyricsOverlay_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        LyricsNowPlaying.Visibility = e.NewSize.Width < 820 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task LoadLyricsForCurrentAsync()
    {
        string title = App.Playback.CurrentTitle;
        string artist = App.Playback.CurrentArtist;
        string key = title + "|" + artist;
        LyricsOverlayTitle.Text = string.IsNullOrEmpty(title) ? "Letra" : title;
        LyricsOverlayArtist.Text = artist;
        SetLyricsBg();

        if (!App.Playback.HasMedia) { ShowLyricsStatus("Nada en reproducción."); return; }

        if (_lyrics != null && _lyricKey == key) { BuildLyricsBlocks(); return; }

        _lyricKey = key; _lyrics = null; _lyricIndex = -1;
        ShowLyricsStatus("Buscando letra…");

        double dur = App.Playback.Player?.PlaybackSession?.NaturalDuration.TotalSeconds ?? 0;
        var lines = await Core.LyricsService.FetchAsync(
            App.Playback.CurrentPath, title, artist, dur > 0 ? dur : null);
        if (_lyricKey != key) return; // cambió la canción mientras buscábamos

        _lyrics = lines;
        if (lines is { Count: > 0 })
        {
            BuildLyricsBlocks();
            string src = Core.LyricsService.LastProviderUsed ?? "";
            LyricsSource.Text = Core.LyricsService.LastHadWords
                ? $"Letra por palabra · {src}"
                : $"Letra · {src}";
            LyricsSource.Visibility = Visibility.Visible;
        }
        else
        {
            LyricsSource.Visibility = Visibility.Collapsed;
            ShowLyricsStatus("No encontramos letra sincronizada para esta canción.");
        }
    }

    private void ShowLyricsStatus(string message)
    {
        LyricsStatus.Text = message;
        LyricsStatus.Visibility = Visibility.Visible;
        LyricsStack.Children.Clear();
        _lyricBlocks.Clear();
        _lyricIndex = -1;
    }

    private LinearGradientBrush? _lyricFill;   // relleno karaoke de la línea actual

    private static SolidColorBrush WhiteBrush() =>
        new(Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));

    private void BuildLyricsBlocks()
    {
        LyricsStatus.Visibility = Visibility.Collapsed;
        LyricsStack.Children.Clear();
        _lyricBlocks.Clear();
        _lyricIndex = -1;
        _lyricFill = null;
        if (_lyrics is null) return;

        double maxW = LyricsScroll.ActualWidth - 96;
        if (maxW < 200) maxW = 620;
        var halign = _lyricCentered ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        var talign = _lyricCentered ? TextAlignment.Center : TextAlignment.Left;

        LyricsStack.Children.Add(new Border { Height = 300 }); // espaciador para centrar la 1ª línea
        foreach (var line in _lyrics)
        {
            var tb = new TextBlock
            {
                Text = string.IsNullOrEmpty(line.Text) ? "♪" : line.Text,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = talign,
                HorizontalAlignment = halign,
                MaxWidth = maxW,
                FontSize = 26 * _lyricScale,
                Margin = new Thickness(0, 10, 0, 10),
                Opacity = 0.35,
                Foreground = WhiteBrush(),                 // atenuada vía Opacity
                RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(),
            };
            LyricsStack.Children.Add(tb);
            _lyricBlocks.Add(tb);
        }
        LyricsStack.Children.Add(new Border { Height = 300 });
        SyncLyrics();
    }

    // Degradado que "llena" la línea actual de izquierda a derecha al ritmo (karaoke aproximado).
    private static LinearGradientBrush MakeFillBrush()
    {
        var full = Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF); // cantado
        var dim  = Windows.UI.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF); // aún por cantar
        var b = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0.5),
            EndPoint   = new Windows.Foundation.Point(1, 0.5),
        };
        b.GradientStops.Add(new GradientStop { Color = full, Offset = 0 });
        b.GradientStops.Add(new GradientStop { Color = full, Offset = 0 });
        b.GradientStops.Add(new GradientStop { Color = dim,  Offset = 0 });
        b.GradientStops.Add(new GradientStop { Color = dim,  Offset = 1 });
        return b;
    }

    private void SyncLyrics()
    {
        var lines = _lyrics;
        if (!_lyricsOpen || lines is null || lines.Count == 0 || _lyricBlocks.Count != lines.Count) return;

        var pos = App.Playback.Player?.PlaybackSession?.Position ?? TimeSpan.Zero;
        int idx = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Time <= pos) idx = i;
            else break;
        }

        // Cambio de línea: animar la que sale (atenuar) y la que entra (resaltar + centrar).
        if (idx != _lyricIndex)
        {
            if (_lyricIndex >= 0 && _lyricIndex < _lyricBlocks.Count)
            {
                var prev = _lyricBlocks[_lyricIndex];
                prev.Foreground = WhiteBrush();
                AnimateBlock(prev, false);
            }
            _lyricIndex = idx;
            if (idx >= 0)
            {
                var cur = _lyricBlocks[idx];
                _lyricFill = MakeFillBrush();
                cur.Foreground = _lyricFill;
                AnimateBlock(cur, true);
                CenterLyric(cur);
            }
        }

        // Barrido karaoke: mueve el borde del relleno sobre la línea actual.
        if (idx >= 0 && _lyricFill != null)
        {
            double p = FillFraction(lines, idx, pos);
            _lyricFill.GradientStops[1].Offset = p;
            _lyricFill.GradientStops[2].Offset = p;
        }
    }

    /// <summary>
    /// Cuánto de la línea actual ya se cantó (0..1). Si la fuente trae tiempos por palabra, el
    /// barrido los sigue de verdad (karaoke real); si no, se reparte la línea proporcionalmente
    /// entre su inicio y el de la siguiente.
    /// </summary>
    private static double FillFraction(List<Core.LyricLineVm> lines, int idx, TimeSpan pos)
    {
        var line = lines[idx];
        var words = line.Words;

        if (words is { Count: > 0 })
        {
            int total = 0;
            foreach (var w in words) total += w.Text.Length;
            if (total == 0) return 1;

            int prefix = 0;
            foreach (var w in words)
            {
                if (pos < w.Start) return (double)prefix / total;      // aún no llega a esta palabra
                var end = w.Start + w.Duration;
                if (pos < end)
                {
                    double inside = w.Duration.TotalMilliseconds > 0
                        ? (pos - w.Start).TotalMilliseconds / w.Duration.TotalMilliseconds
                        : 1;
                    return (prefix + Math.Clamp(inside, 0, 1) * w.Text.Length) / total;
                }
                prefix += w.Text.Length;
            }
            return 1;
        }

        double start = line.Time.TotalSeconds;
        double lineEnd = (idx + 1 < lines.Count) ? lines[idx + 1].Time.TotalSeconds : start + 4;
        return lineEnd > start ? Math.Clamp((pos.TotalSeconds - start) / (lineEnd - start), 0, 1) : 1;
    }

    // Transición suave de una línea entre estado normal (atenuada, escala 1) y actual (brillante, escala 1.08).
    private static void AnimateBlock(TextBlock tb, bool current)
    {
        if (tb.RenderTransform is not ScaleTransform scale) { scale = new ScaleTransform(); tb.RenderTransform = scale; }
        var sb = new Storyboard();
        void Tween(DependencyObject target, string prop, double to)
        {
            var a = new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(300),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true,
            };
            Storyboard.SetTarget(a, target);
            Storyboard.SetTargetProperty(a, prop);
            sb.Children.Add(a);
        }
        Tween(tb, "Opacity", current ? 1.0 : 0.35);
        Tween(scale, "ScaleX", current ? 1.08 : 1.0);
        Tween(scale, "ScaleY", current ? 1.08 : 1.0);
        sb.Begin();
    }

    private void CenterLyric(TextBlock tb)
    {
        try
        {
            tb.UpdateLayout();
            double y = tb.TransformToVisual(LyricsStack).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            double target = y - LyricsScroll.ViewportHeight / 2 + tb.ActualHeight / 2;
            LyricsScroll.ChangeView(null, target, null, false); // scroll animado
        }
        catch { }
    }

    private void Lyrics_FontSmaller(object sender, RoutedEventArgs e) => AdjustLyricScale(-0.1);
    private void Lyrics_FontBigger(object sender, RoutedEventArgs e)  => AdjustLyricScale(+0.1);
    private void AdjustLyricScale(double d)
    {
        _lyricScale = Math.Clamp(_lyricScale + d, 0.7, 1.8);
        foreach (var tb in _lyricBlocks) tb.FontSize = 26 * _lyricScale;
        if (_lyricIndex >= 0 && _lyricIndex < _lyricBlocks.Count) CenterLyric(_lyricBlocks[_lyricIndex]);
    }

    private void Lyrics_ToggleAlign(object sender, RoutedEventArgs e)
    {
        _lyricCentered = !_lyricCentered;
        LyricsAlignIcon.Glyph = char.ConvertFromUtf32(_lyricCentered ? 0xE8E9 : 0xE8E4);
        var ha = _lyricCentered ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        var ta = _lyricCentered ? TextAlignment.Center : TextAlignment.Left;
        foreach (var tb in _lyricBlocks) { tb.HorizontalAlignment = ha; tb.TextAlignment = ta; }
    }

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
        // Este tick es lo único que corre continuamente mientras suena música: mantenerlo barato.
        // Solo se actualiza la UI realmente visible — antes se recalculaba el VU y la barra de
        // progreso incluso con el reproductor oculto, gastando layout para nada.
        // (El fundido ya NO depende de este timer: tiene el suyo en PlaybackService.)
        if (GlobalPlayer.Visibility == Visibility.Visible)
        {
            App.Playback.TickLevels();
            _vuL = App.Playback.LevelLeft;
            _vuR = App.Playback.LevelRight;
            SetBar(GpVuLeftBig, _vuL);
            SetBar(GpVuRightBig, _vuR);
        }
        else { _vuL = _vuR = 0; }

        if (MiniPlayer.Visibility == Visibility.Visible || _lyricsOpen) UpdateSeekUI();
        if (_lyricsOpen) SyncLyrics();

        // Detener el tick cuando ya no hay audio (ahorra CPU en reposo)
        if (!App.Playback.HasMedia && _vuL < 0.01 && _vuR < 0.01) _vuTimer.Stop();
    }

    // ── Timeline de la canción (barra de progreso seekable del mini-reproductor) ──
    private bool _seeking;
    private int _shownElapsed = -1, _shownTotal = -1;   // caché para no reescribir textos cada tick

    private void UpdateSeekUI()
    {
        if (_seeking) return; // no pisar la barra mientras el usuario arrastra
        var s = App.Playback.Player?.PlaybackSession;
        double pos = s?.Position.TotalSeconds ?? 0;
        double dur = s?.NaturalDuration.TotalSeconds ?? 0;
        double frac = dur > 0 ? Math.Clamp(pos / dur, 0, 1) : 0;

        bool mini = MiniPlayer.Visibility == Visibility.Visible;
        if (mini) SetSeekFill(MiniSeekFill, frac);
        if (_lyricsOpen) SetSeekFill(LyricsSeekFill, frac);

        // Los textos solo cambian una vez por segundo: evitar reescribirlos en cada tick
        // (cada escritura invalida el layout del bloque de texto).
        int ps = (int)pos, ds = (int)dur;
        if (ps != _shownElapsed)
        {
            _shownElapsed = ps;
            string t = FmtTime(pos);
            if (mini) MiniElapsed.Text = t;
            if (_lyricsOpen) LyricsElapsed.Text = t;
        }
        if (ds != _shownTotal)
        {
            _shownTotal = ds;
            string t = FmtTime(dur);
            if (mini) MiniTotal.Text = t;
            if (_lyricsOpen) LyricsTotal.Text = t;
        }
    }

    /// <summary>Devuelve el relleno que corresponde a la pista de progreso indicada.</summary>
    private FrameworkElement? FillFor(FrameworkElement track) =>
        ReferenceEquals(track, MiniSeekTrack)   ? MiniSeekFill :
        ReferenceEquals(track, LyricsSeekTrack) ? LyricsSeekFill : null;

    private static void SetSeekFill(FrameworkElement fill, double frac)
    {
        if (fill.Parent is FrameworkElement host && host.ActualWidth > 0)
            fill.Width = Math.Clamp(frac, 0, 1) * host.ActualWidth;
    }

    private static string FmtTime(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds)) return "0:00";
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                                 : $"{t.Minutes}:{t.Seconds:00}";
    }

    private void Seek_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement track) return;
        _seeking = true;
        track.CapturePointer(e.Pointer);
        SeekToPointer(track, e);
    }

    private void Seek_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!_seeking || sender is not FrameworkElement track) return;
        SeekToPointer(track, e);
    }

    private void Seek_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (sender is FrameworkElement track) track.ReleasePointerCapture(e.Pointer);
        _seeking = false;
    }

    private void SeekToPointer(FrameworkElement track, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        double w = track.ActualWidth;
        if (w <= 0 || !App.Playback.HasMedia) return;
        double x = e.GetCurrentPoint(track).Position.X;
        double frac = Math.Clamp(x / w, 0, 1);
        var s = App.Playback.Player?.PlaybackSession;
        double dur = s?.NaturalDuration.TotalSeconds ?? 0;
        if (dur > 0) App.Playback.Seek(TimeSpan.FromSeconds(frac * dur));

        // Feedback inmediato sobre la barra que se esta arrastrando (hay una en el mini-reproductor
        // y otra en la vista de letra).
        var fill = FillFor(track);
        if (fill != null) SetSeekFill(fill, frac);
    }

    private static void SetBar(FrameworkElement bar, double level)
    {
        if (bar.Parent is not FrameworkElement host || host.ActualWidth <= 0) return;
        double w = Math.Clamp(level, 0, 1) * host.ActualWidth;
        // Escribir Width invalida el layout: hacerlo solo si el cambio es perceptible.
        if (Math.Abs(bar.Width - w) > 0.5) bar.Width = w;
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
