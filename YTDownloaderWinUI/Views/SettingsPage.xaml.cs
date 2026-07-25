using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace YTDownloader.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        // Cachea la página: al volver conserva el estado en memoria
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        TxtFolder.Text = App.DownloadManager.OutputFolder;
        PlatformsList.ItemsSource = Models.PlatformOption.All();

        // La restauración se hace en Loaded: los ComboBox aún no están "listos"
        // en el constructor — si se intenta antes, al volver se ven vacíos.
        Loaded += OnLoaded;

        _ = LoadYtDlpVersionAsync();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SelectByTag(CboThreshold, Core.AppSettings.Current.CascadeThreshold.ToString());
        SelectByTag(CboPlayer,    Core.AppSettings.Current.PlayerMode.ToString());
        SelectByTag(CboCut,       Core.AppSettings.Current.CutSaveMode.ToString());
        SelectByTag(CboBackdrop,  Core.AppSettings.Current.BackdropKind.ToString());

        // Preferencias de descarga predeterminadas
        SelectByContent(CboDlFormat,    Core.AppSettings.Current.DefaultFormat);
        SelectByContent(CboDlQuality,   Core.AppSettings.Current.DefaultQuality);
        SelectByContent(CboDlSubtitles, Core.AppSettings.Current.DefaultSubtitles);
        TogPlaylistFolder.IsOn = Core.AppSettings.Current.PlaylistSubfolder;

        TogProtocol.IsOn = Core.UrlProtocol.IsRegistered();
        TogStartup.IsOn  = Core.StartupManager.IsEnabled();

        // ── Actualizaciones ──
        App.Updater.StateChanged += OnUpdateState;
        App.Updater.DownloadProgress += p => DispatcherQueue.TryEnqueue(() => UpdateProgress.Value = p);
        RefreshUpdateUI();

        RefreshYouTubeAuthUI();
    }

    // ── Cuenta de YouTube (cookies + runtime JS) ────────────────────────────
    private void RefreshYouTubeAuthUI()
    {
        string cookies = Core.AppSettings.Current.YouTubeCookiesPath;
        bool hasCookies = !string.IsNullOrWhiteSpace(cookies) && File.Exists(cookies);
        if (hasCookies)
        {
            var when = File.GetLastWriteTime(cookies).ToString("dd/MM/yyyy HH:mm");
            CookiesStatus.Text = $"Configuradas ✓ · importadas el {when}. Si expiran, vuelve a importar.";
            BtnClearCookies.Visibility = Visibility.Visible;
        }
        else
        {
            CookiesStatus.Text = "Sin configurar. Sin cookies, la mayoría de videos fallan (anti-bot de YouTube).";
            BtnClearCookies.Visibility = Visibility.Collapsed;
        }

        NodeStatus.Text = Core.YtDlpService.NodeInstalled
            ? "Detectado ✓ — necesario para resolver los formatos de YouTube."
            : "No detectado. Instala Node.js (nodejs.org) para que las descargas y la reproducción funcionen.";
    }

    private async void OnImportCookies(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".txt");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        try
        {
            string dst = Core.AppSettings.ManagedCookiesPath;
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(file.Path, dst, overwrite: true);
            Core.AppSettings.Current.YouTubeCookiesPath = dst; // dispara guardado
            RefreshYouTubeAuthUI();
        }
        catch (Exception ex)
        {
            var dlg = new ContentDialog
            {
                Title = "No se pudo importar",
                Content = ex.Message,
                CloseButtonText = "Entendido",
                XamlRoot = this.XamlRoot
            };
            try { await dlg.ShowAsync(); } catch { }
        }
    }

    private void OnClearCookies(object sender, RoutedEventArgs e)
    {
        try { if (File.Exists(Core.AppSettings.ManagedCookiesPath)) File.Delete(Core.AppSettings.ManagedCookiesPath); }
        catch { /* archivo bloqueado; ignorar */ }
        Core.AppSettings.Current.YouTubeCookiesPath = string.Empty;
        RefreshYouTubeAuthUI();
    }

    private void OnUpdateState(Core.UpdateState s) => DispatcherQueue.TryEnqueue(RefreshUpdateUI);

    private void RefreshUpdateUI()
    {
        var u = App.Updater;
        string cur = Core.UpdateService.CurrentVersion();
        UpdateProgress.Visibility = u.State == Core.UpdateState.Downloading ? Visibility.Visible : Visibility.Collapsed;
        UpdateActions.Visibility  = (u.State == Core.UpdateState.Available || u.State == Core.UpdateState.ReadyToInstall) ? Visibility.Visible : Visibility.Collapsed;
        BtnCheckUpdate.IsEnabled = u.State is not (Core.UpdateState.Checking or Core.UpdateState.Downloading);

        UpdateStatusText.Text = u.State switch
        {
            Core.UpdateState.Idle            => $"Versión actual: {cur}",
            Core.UpdateState.Checking        => "Comprobando actualizaciones...",
            Core.UpdateState.UpToDate        => $"Estás en la última versión ({cur}) ✓",
            Core.UpdateState.Available       => $"Versión nueva disponible: {u.Latest?.Version}  (tu versión: {cur})",
            Core.UpdateState.Downloading     => $"Descargando MediaFy {u.Latest?.Version}...",
            Core.UpdateState.ReadyToInstall  => $"Lista para instalar MediaFy {u.Latest?.Version}",
            Core.UpdateState.Error           => $"Error: {u.LastError}",
            _                                => $"Versión actual: {cur}"
        };

        if (u.Latest != null)
            LinkReleaseNotes.NavigateUri = !string.IsNullOrEmpty(u.Latest.HtmlUrl) ? new Uri(u.Latest.HtmlUrl) : null;

        BtnDownloadText.Text = u.State == Core.UpdateState.ReadyToInstall ? "Instalar ahora" : "Descargar e instalar";
    }

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        await App.Updater.CheckAsync();
    }

    private async void OnDownloadUpdate(object sender, RoutedEventArgs e)
    {
        var u = App.Updater;
        if (u.State == Core.UpdateState.ReadyToInstall)
        {
            if (!u.IsInstalledLocation())
            {
                var dlg = new ContentDialog
                {
                    Title = "Versión portable",
                    Content = "Estás ejecutando MediaFy en modo portable. Descarga el instalador y reemplaza tus archivos manualmente, o vuelve a descomprimir la nueva versión.",
                    PrimaryButtonText = "Abrir página de la versión",
                    CloseButtonText = "Cerrar",
                    XamlRoot = XamlRoot
                };
                if (await dlg.ShowAsync() == ContentDialogResult.Primary && u.Latest != null)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    { FileName = u.Latest.HtmlUrl, UseShellExecute = true });
                return;
            }
            u.Install();
            // El instalador silencioso cerrará MediaFy y la relanzará al terminar
            return;
        }

        if (u.State == Core.UpdateState.Available)
        {
            bool ok = await u.DownloadAsync();
            // Cuando termine, el botón cambia a "Instalar ahora" automáticamente vía RefreshUpdateUI
        }
    }

    private void OnBackdropChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboBackdrop.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<Core.BackdropKind>((string?)item.Tag, out var kind))
        {
            Core.AppSettings.Current.BackdropKind = kind;
            (App.MainWindow as MainWindow)?.ApplyBackdrop(kind);
        }
    }

    // ── Preferencias de descarga ───────────────────────────────

    private void CboDlFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboDlFormat?.SelectedItem is ComboBoxItem item)
            Core.AppSettings.Current.DefaultFormat = item.Content?.ToString() ?? "MP4";
    }

    private void CboDlQuality_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboDlQuality?.SelectedItem is ComboBoxItem item)
            Core.AppSettings.Current.DefaultQuality = item.Content?.ToString() ?? "Mejor";
    }

    private void CboDlSubtitles_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboDlSubtitles?.SelectedItem is ComboBoxItem item)
            Core.AppSettings.Current.DefaultSubtitles = item.Content?.ToString() ?? "Off";
    }

    private void TogPlaylistFolder_Toggled(object sender, RoutedEventArgs e)
    {
        Core.AppSettings.Current.PlaylistSubfolder = TogPlaylistFolder.IsOn;
    }

    private static void SelectByContent(ComboBox combo, string content)
    {
        foreach (ComboBoxItem ci in combo.Items)
        {
            if (ci.Content?.ToString() == content)
            { combo.SelectedItem = ci; return; }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static void SelectByTag(ComboBox combo, string tag)
    {
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem ci && (string?)ci.Tag == tag)
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    private void OnThresholdChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboThreshold.SelectedItem is ComboBoxItem item &&
            int.TryParse((string?)item.Tag, out int v))
            Core.AppSettings.Current.CascadeThreshold = v;
    }

    private void OnPlayerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboPlayer.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<Core.PlayerMode>((string?)item.Tag, out var m))
            Core.AppSettings.Current.PlayerMode = m;
    }

    private async void OnInstallExtension(object sender, RoutedEventArgs e)
    {
        if (!Core.BrowserDetector.ExtensionAvailable())
        {
            var err = new ContentDialog
            {
                Title = "Extensión no encontrada",
                Content = "La carpeta de la extensión no se encontró junto a MediaFy. Reinstala la app o contacta con el desarrollador.",
                CloseButtonText = "Cerrar",
                XamlRoot = XamlRoot
            };
            await err.ShowAsync();
            return;
        }

        var browser = Core.BrowserDetector.Detect();
        string browserName = browser switch
        {
            Core.Browser.Edge => "Microsoft Edge",
            Core.Browser.Chrome => "Google Chrome",
            Core.Browser.Brave => "Brave",
            Core.Browser.Opera => "Opera",
            _ => "tu navegador"
        };
        string extensionsLabel = Core.BrowserDetector.ExtensionsUrl(browser);

        var panel = new StackPanel { Spacing = 14 };
        panel.Children.Add(Step(1, $"Voy a abrir {browserName} en la página de extensiones ({extensionsLabel})."));
        panel.Children.Add(Step(2, "Activa el interruptor \"Modo de desarrollador\" (esquina superior derecha)."));
        panel.Children.Add(Step(3, "Pulsa \"Cargar descomprimida\" y selecciona la carpeta que MediaFy te abrirá automáticamente."));
        panel.Children.Add(new TextBlock
        {
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            Text = "MediaFy abrirá las dos ventanas (extensiones y la carpeta) para que solo tengas que arrastrar/seleccionar."
        });

        var dlg = new ContentDialog
        {
            Title = "Instalar extensión de MediaFy",
            Content = panel,
            PrimaryButtonText = "Continuar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            Core.BrowserDetector.OpenExtensionsPage();
            await Task.Delay(700); // pequeño respiro para que el navegador abra
            Core.BrowserDetector.OpenExtensionFolder();
        }
    }

    private static UIElement Step(int n, string text)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var num = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
            Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = n.ToString(),
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(num, 0);
        g.Children.Add(num);

        var tb = new TextBlock
        {
            Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(tb, 1);
        g.Children.Add(tb);
        return g;
    }

    private void OnProtocolToggled(object sender, RoutedEventArgs e)
    {
        var sw = (ToggleSwitch)sender;
        bool ok = sw.IsOn ? Core.UrlProtocol.Register() : Core.UrlProtocol.Unregister();
        if (!ok) sw.IsOn = Core.UrlProtocol.IsRegistered();
        Core.AppSettings.Current.UrlProtocolRegistered = Core.UrlProtocol.IsRegistered();
    }

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        var sw = (ToggleSwitch)sender;
        bool ok = sw.IsOn ? Core.StartupManager.Enable() : Core.StartupManager.Disable();
        if (!ok) sw.IsOn = Core.StartupManager.IsEnabled();
        Core.AppSettings.Current.StartWithWindows = Core.StartupManager.IsEnabled();
    }

    private void OnCutChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboCut.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<Core.CutSaveMode>((string?)item.Tag, out var m))
            Core.AppSettings.Current.CutSaveMode = m;
    }

    // Caché de la versión de yt-dlp durante toda la vida de la app: leerla arranca
    // yt-dlp.exe (PyInstaller, se auto-extrae en frío ~1-3s), así que NUNCA debe
    // tocar el hilo de UI ni repetirse.
    private static string? _cachedYtDlpVersion;

    private async Task LoadYtDlpVersionAsync()
    {
        if (_cachedYtDlpVersion != null) { TxtYtDlpVersion.Text = _cachedYtDlpVersion; return; }
        TxtYtDlpVersion.Text = "Comprobando…";

        string result = await Task.Run(() =>
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "Assets", "yt-dlp.exe");
                if (!File.Exists(path)) return "No encontrado en Assets/";
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = path, Arguments = "--version",
                    RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
                })!;
                string v = proc.StandardOutput.ReadToEnd().Trim();
                proc.WaitForExit();
                return $"Versión instalada: {v}";
            }
            catch { return "Error al leer la versión"; }
        });

        _cachedYtDlpVersion = result;
        TxtYtDlpVersion.Text = result;
    }

    /// <summary>Invalida la caché de versión tras actualizar yt-dlp.</summary>
    private static void ClearYtDlpVersionCache() => _cachedYtDlpVersion = null;

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            TxtFolder.Text = folder.Path;
            App.DownloadManager.OutputFolder = folder.Path;
        }
    }

    private async void OnUpdateYtDlp(object sender, RoutedEventArgs e)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "yt-dlp.exe");
        if (!File.Exists(path)) return;
        TxtYtDlpVersion.Text = "Actualizando...";
        try
        {
            await Task.Run(() =>
            {
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = path, Arguments = "-U",
                    RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
                })!;
                proc.WaitForExit();
            });
            ClearYtDlpVersionCache();
            await LoadYtDlpVersionAsync();
        }
        catch { TxtYtDlpVersion.Text = "Error al actualizar"; }
    }
}
