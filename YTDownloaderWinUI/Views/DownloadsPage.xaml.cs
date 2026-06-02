using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using YTDownloader.Models;

namespace YTDownloader.Views;

public sealed partial class DownloadsPage : Page
{
    private readonly Core.DownloadManager _manager = App.DownloadManager;

    // Debounce compartido para preview y búsqueda
    private readonly DispatcherTimer _inputTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _searchCts;
    private string _lastPreviewedUrl = string.Empty;
    private string _lastSearchQuery  = string.Empty;
    private bool   _searchMode;

    public DownloadsPage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        DownloadsList.ItemsSource = _manager.Queue;
        _manager.Queue.CollectionChanged += (_, _) => RefreshListState();
        RefreshListState();

        _inputTimer.Tick += async (_, _) => { _inputTimer.Stop(); await OnInputSettledAsync(); };

        if (!_manager.IsReady)
            TxtStatus.Text = "⚠  Faltan yt-dlp.exe o ffmpeg.exe en Assets/";

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Pre-rellenar desde los defaults de Configuración
        SelectComboByContent(CboFormat, Core.AppSettings.Current.DefaultFormat);
        // La calidad se actualiza al cambiar el formato, así que la seteamos después
        SelectComboByContent(CboQuality, Core.AppSettings.Current.DefaultQuality);
        SelectComboByContent(CboSubtitles, Core.AppSettings.Current.DefaultSubtitles);
    }

    private static void SelectComboByContent(ComboBox cbo, string content)
    {
        foreach (ComboBoxItem item in cbo.Items)
        {
            if (item.Content?.ToString() == content)
            { cbo.SelectedItem = item; return; }
        }
        if (cbo.Items.Count > 0) cbo.SelectedIndex = 0;
    }

    // ── Entrada del usuario ────────────────────────────────────

    /// <summary>Recibe una URL de fuera (CLI, protocolo, extensión) y la prepara para descargar.</summary>
    public void PrefillUrl(string url)
    {
        TxtUrl.Text = url;
        TxtUrl.Focus(FocusState.Programmatic);
    }

    private void TxtUrl_TextChanged(object sender, TextChangedEventArgs e)
    {
        string text = TxtUrl.Text.Trim();

        // Botón de limpiar
        BtnClearUrl.Visibility = string.IsNullOrEmpty(text)
            ? Visibility.Collapsed : Visibility.Visible;

        // Icono dinámico: enlace vs búsqueda
        bool looksLikeUrl = Core.PlatformDetector.LooksLikeUrl(text);
        UrlIcon.Glyph = looksLikeUrl
            ? char.ConvertFromUtf32(0xE71B)  // Link
            : char.ConvertFromUtf32(0xE721); // Search

        // Si vacío, colapsar todo y resetear
        if (string.IsNullOrWhiteSpace(text))
        {
            _inputTimer.Stop();
            HideAllPanels();
            _lastPreviewedUrl = string.Empty;
            _lastSearchQuery  = string.Empty;
            return;
        }

        // Reiniciar debounce
        _inputTimer.Stop();
        _inputTimer.Start();
    }

    private async Task OnInputSettledAsync()
    {
        string text = TxtUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        if (Core.PlatformDetector.LooksLikeUrl(text))
        {
            // Modo URL: vista previa normal
            HideSearchPanel();
            await UpdatePreviewAsync(text);
        }
        else if (text.Length >= 3)
        {
            // Modo búsqueda: texto libre → buscar en YouTube
            HidePreviewCard();
            await UpdateSearchAsync(text);
        }
    }

    // ── Vista previa de URL ────────────────────────────────────

    private async Task UpdatePreviewAsync(string url)
    {
        _previewCts?.Cancel();

        var platform = Core.PlatformDetector.Detect(url);

        if (platform == Core.Platform.Spotify)
        {
            if (!Core.AppSettings.Current.IsPlatformEnabled(Core.Platform.Spotify))
                ShowPreviewState(error: "Spotify está desactivado — actívalo en Configuración › Plataformas");
            else if (!_manager.SpotifyReady)
                ShowPreviewState(error: "Spotify: configura tu Client ID y Secret en Configuración");
            else
                ShowPreviewState(error: "Spotify detectado ✓ — pulsa Descargar para añadir las canciones");
            return;
        }

        if (!Core.AppSettings.Current.IsPlatformEnabled(platform))
        {
            ShowPreviewState(error: $"{Core.PlatformDetector.Name(platform)} está desactivada — actívala en Configuración › Plataformas");
            return;
        }

        if (url == _lastPreviewedUrl) return;
        _lastPreviewedUrl = url;

        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;
        ShowPreviewState(loading: true);

        try
        {
            var info = await _manager.GetInfoAsync(url, ct);
            if (ct.IsCancellationRequested) return;

            PreviewTitle.Text    = info.Title;
            PreviewUploader.Text = string.IsNullOrEmpty(info.Uploader)
                ? Core.PlatformDetector.Name(platform)
                : $"{info.Uploader}  ·  {Core.PlatformDetector.Name(platform)}";
            PreviewDuration.Text = string.IsNullOrEmpty(info.Duration) ? "—" : info.Duration;
            if (!string.IsNullOrEmpty(info.Thumbnail))
                PreviewThumb.Source = new BitmapImage(new Uri(info.Thumbnail));
            PreviewQualities.ItemsSource = info.QualityLabels.Count > 0
                ? info.QualityLabels
                : new List<string> { "Audio" };

            ShowPreviewState(info: true);
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!ct.IsCancellationRequested)
                ShowPreviewState(error: "No se pudo analizar el enlace (¿privado o no disponible?)");
        }
    }

    private void ShowPreviewState(bool loading = false, bool info = false, string? error = null)
    {
        PreviewCard.Visibility    = Visibility.Visible;
        PreviewLoading.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        PreviewInfo.Visibility    = info    ? Visibility.Visible : Visibility.Collapsed;
        PreviewError.Visibility   = error != null ? Visibility.Visible : Visibility.Collapsed;
        if (error != null) PreviewErrorText.Text = error;
    }

    // ── Búsqueda en YouTube ───────────────────────────────────

    private async Task UpdateSearchAsync(string query)
    {
        _searchCts?.Cancel();

        if (query == _lastSearchQuery) return;
        _lastSearchQuery = query;

        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        // Mostrar panel con spinner
        SearchPanel.Visibility      = Visibility.Visible;
        SearchLoading.Visibility    = Visibility.Visible;
        SearchLoading.IsActive      = true;
        SearchEmptyText.Visibility  = Visibility.Collapsed;
        SearchResultsList.ItemsSource = null;

        try
        {
            var results = await _manager.SearchAsync(query, 8, ct);
            if (ct.IsCancellationRequested) return;

            SearchLoading.IsActive   = false;
            SearchLoading.Visibility = Visibility.Collapsed;

            if (results.Count == 0)
            {
                SearchEmptyText.Visibility = Visibility.Visible;
            }
            else
            {
                SearchResultsList.ItemsSource = results;
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!ct.IsCancellationRequested)
            {
                SearchLoading.IsActive   = false;
                SearchLoading.Visibility = Visibility.Collapsed;
                SearchEmptyText.Visibility = Visibility.Visible;
                SearchEmptyText.Text = "Error al buscar";
            }
        }
    }

    private void SearchResult_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not SearchResultItem item) return;

        // Rellena el campo con la URL del resultado
        TxtUrl.Text = item.Url;
        TxtUrl.SelectionStart = item.Url.Length;

        // Cierra el panel de búsqueda
        HideSearchPanel();
        _lastSearchQuery = string.Empty;

        // Dispara la vista previa automáticamente
        _inputTimer.Stop();
        _inputTimer.Start();
    }

    // ── Helpers de visibilidad ─────────────────────────────────

    private void HideAllPanels()
    {
        PreviewCard.Visibility = Visibility.Collapsed;
        SearchPanel.Visibility = Visibility.Collapsed;
    }

    private void HideSearchPanel()
    {
        SearchPanel.Visibility = Visibility.Collapsed;
        _searchCts?.Cancel();
    }

    private void HidePreviewCard()
    {
        PreviewCard.Visibility = Visibility.Collapsed;
        _previewCts?.Cancel();
        _lastPreviewedUrl = string.Empty;
    }

    // ── Lista de descargas ─────────────────────────────────────

    private void RefreshListState()
    {
        int count = _manager.Queue.Count;
        TxtCount.Text = count.ToString();
        EmptyState.Visibility    = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadsList.Visibility = count >  0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Descargar ──────────────────────────────────────────────

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        string url = TxtUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!Core.PlatformDetector.LooksLikeUrl(url))
        {
            TxtStatus.Text = "Pega un enlace válido (https://...) o selecciona un resultado de búsqueda";
            return;
        }

        var platform = Core.PlatformDetector.Detect(url);

        if (platform == Core.Platform.Spotify)
        {
            if (!Core.AppSettings.Current.IsPlatformEnabled(Core.Platform.Spotify))
            { TxtStatus.Text = "Spotify está desactivado (Configuración › Plataformas)"; return; }
            if (!_manager.SpotifyReady)
            { TxtStatus.Text = "Configura tus credenciales de Spotify en Configuración"; return; }

            TxtUrl.Text = string.Empty;
            HideAllPanels();
            TxtStatus.Text = "Resolviendo Spotify...";
            try
            {
                var tracks = await _manager.ResolveSpotifyAsync(url);
                foreach (var t in tracks) _manager.AddSpotify(t);
                TxtStatus.Text = $"Spotify: {tracks.Count} canción(es) añadidas a la cola";
            }
            catch (Exception ex) { TxtStatus.Text = $"Spotify: {ex.Message}"; }
            return;
        }

        if (!Core.AppSettings.Current.IsPlatformEnabled(platform))
        {
            TxtStatus.Text = $"{Core.PlatformDetector.Name(platform)} está desactivada (Configuración › Plataformas)";
            return;
        }

        string format    = (string)((ComboBoxItem)CboFormat.SelectedItem).Content;
        string quality   = (string)((ComboBoxItem)CboQuality.SelectedItem).Content;
        string subtitles = (string)((ComboBoxItem)CboSubtitles.SelectedItem).Content;
        bool   playlist  = TglPlaylist.IsOn;

        _manager.AddAndStart(url, format, quality, subtitles, playlist);

        TxtUrl.Text = string.Empty;
        HideAllPanels();
        _lastPreviewedUrl = string.Empty;
        TxtStatus.Text = "Añadida a la cola";
    }

    // ── Controles de barra ─────────────────────────────────────

    private async void BtnPaste_Click(object sender, RoutedEventArgs e)
    {
        var dp = Clipboard.GetContent();
        if (dp.Contains(StandardDataFormats.Text))
            TxtUrl.Text = (await dp.GetTextAsync()).Trim();
    }

    private void BtnClearUrl_Click(object sender, RoutedEventArgs e)
    {
        TxtUrl.Text = string.Empty;
        TxtUrl.Focus(FocusState.Programmatic);
    }

    private void TxtUrl_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            string text = TxtUrl.Text.Trim();
            if (Core.PlatformDetector.LooksLikeUrl(text))
                BtnDownload_Click(sender, new RoutedEventArgs());
            // Si es búsqueda y Enter, simplemente deja que el debounce actúe
        }
        else if (e.Key == VirtualKey.Escape)
        {
            HideAllPanels();
        }
    }

    // ── Acciones de items ──────────────────────────────────────

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DownloadItem item)
            _manager.Cancel(item);
    }

    private void BtnRetry_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DownloadItem item)
            _manager.Retry(item);
    }

    private void BtnClearDone_Click(object sender, RoutedEventArgs e)
    {
        var done = _manager.Queue
            .Where(x => x.Status is DownloadStatus.Done or DownloadStatus.Error or DownloadStatus.Canceled)
            .ToList();
        foreach (var item in done) _manager.Queue.Remove(item);
    }

    private void CboFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboQuality == null) return;
        string format = (string)((ComboBoxItem)CboFormat.SelectedItem).Content;
        bool isAudio   = format is "MP3" or "M4A" or "OGG" or "FLAC" or "WAV" or "OPUS";
        bool isLossless = format is "FLAC" or "WAV";

        CboQuality.Items.Clear();
        string[] options = isLossless
            ? new[] { "Mejor (sin pérdida)" }
            : isAudio
                ? new[] { "320kbps", "256kbps", "192kbps", "128kbps", "64kbps" }
                : new[] { "Mejor", "4K", "2K", "1080p", "720p", "480p", "360p", "240p" };

        foreach (var q in options)
            CboQuality.Items.Add(new ComboBoxItem { Content = q });

        // Intentar restaurar la última calidad usada o el default
        string preferred = Core.AppSettings.Current.DefaultQuality;
        SelectComboByContent(CboQuality, preferred);
    }
}
