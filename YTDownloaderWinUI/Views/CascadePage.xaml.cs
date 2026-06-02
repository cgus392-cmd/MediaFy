using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using YTDownloader.Models;

namespace YTDownloader.Views;

public sealed partial class CascadePage : Page
{
    private readonly Core.CascadeManager  _cascade = App.CascadeManager;
    private readonly Core.DownloadManager _dl      = App.DownloadManager;

    // Búsqueda con debounce
    private readonly DispatcherTimer _inputTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private CancellationTokenSource? _searchCts;
    private string _lastQuery = string.Empty;

    public CascadePage()
    {
        InitializeComponent();
        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;

        ItemsList.ItemsSource = _cascade.Items;
        _cascade.Items.CollectionChanged += (_, _) => RefreshState();
        RefreshState();

        _inputTimer.Tick += async (_, _) => { _inputTimer.Stop(); await OnInputSettledAsync(); };
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SelectComboByContent(CboFormat,    Core.AppSettings.Current.DefaultFormat);
        SelectComboByContent(CboQuality,   Core.AppSettings.Current.DefaultQuality);
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

    // ── Estado general ─────────────────────────────────────────

    private void RefreshState()
    {
        int count = _cascade.Items.Count;
        TxtCount.Text = $"{count} / {Core.CascadeManager.MaxItems}";
        EmptyState.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ItemsList.Visibility  = count > 0  ? Visibility.Visible : Visibility.Collapsed;

        BtnAdd.IsEnabled     = _cascade.CanAdd;
        BtnAnalyze.IsEnabled = _cascade.HasAnalyzable && !_cascade.IsRunning;
        BtnStart.IsEnabled   = _cascade.CanStart;
    }

    // ── Input / búsqueda ──────────────────────────────────────

    private void TxtUrl_TextChanged(object sender, TextChangedEventArgs e)
    {
        string text = TxtUrl.Text.Trim();
        BtnClearUrl.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

        bool looksLikeUrl = Core.PlatformDetector.LooksLikeUrl(text);
        UrlIcon.Glyph = looksLikeUrl
            ? char.ConvertFromUtf32(0xE71B)  // Link
            : char.ConvertFromUtf32(0xE721); // Search

        if (string.IsNullOrWhiteSpace(text))
        {
            _inputTimer.Stop();
            SearchPanel.Visibility = Visibility.Collapsed;
            _lastQuery = string.Empty;
            return;
        }

        _inputTimer.Stop();
        _inputTimer.Start();
    }

    private async Task OnInputSettledAsync()
    {
        string text = TxtUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        // Si es URL, no buscar — el usuario verá la URL directo
        if (Core.PlatformDetector.LooksLikeUrl(text))
        {
            SearchPanel.Visibility = Visibility.Collapsed;
            return;
        }

        if (text.Length >= 3)
            await UpdateSearchAsync(text);
    }

    private async Task UpdateSearchAsync(string query)
    {
        _searchCts?.Cancel();
        if (query == _lastQuery) return;
        _lastQuery = query;

        _searchCts = new CancellationTokenSource();
        var ct = _searchCts.Token;

        SearchPanel.Visibility       = Visibility.Visible;
        SearchLoading.Visibility     = Visibility.Visible;
        SearchLoading.IsActive       = true;
        SearchEmptyText.Visibility   = Visibility.Collapsed;
        CascadeSearchHint.Visibility = Visibility.Collapsed;
        SearchResultsList.ItemsSource = null;

        try
        {
            var results = await _dl.SearchAsync(query, 8, ct);
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
                CascadeSearchHint.Visibility  = Visibility.Visible;
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
        TxtUrl.Text = item.Url;
        SearchPanel.Visibility = Visibility.Collapsed;
        _lastQuery = string.Empty;
        TxtStatus.Text = $"Seleccionado: {item.Title} — pulsa Agregar";
    }

    // ── Agregar a la cascada ───────────────────────────────────

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        string url = TxtUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!Core.PlatformDetector.LooksLikeUrl(url))
        {
            TxtStatus.Text = "Selecciona un resultado o pega un enlace válido";
            return;
        }
        var platform = Core.PlatformDetector.Detect(url);
        if (platform == Core.Platform.Spotify)
        { TxtStatus.Text = "Spotify llega en el próximo paso 🎵"; return; }
        if (!Core.AppSettings.Current.IsPlatformEnabled(platform))
        { TxtStatus.Text = $"{Core.PlatformDetector.Name(platform)} está desactivada (Configuración › Plataformas)"; return; }
        if (!_cascade.CanAdd)
        { TxtStatus.Text = "Lista llena (máx. 5) o cascada en curso"; return; }

        string format    = (string)((ComboBoxItem)CboFormat.SelectedItem).Content;
        string quality   = (string)((ComboBoxItem)CboQuality.SelectedItem).Content;
        string subtitles = (string)((ComboBoxItem)CboSubtitles.SelectedItem).Content;

        _cascade.Add(url, format, quality, subtitles);
        TxtUrl.Text = string.Empty;
        SearchPanel.Visibility = Visibility.Collapsed;
        TxtStatus.Text = "Enlace agregado. Cuando termines, pulsa Analizar todo.";
        RefreshState();
    }

    private void BtnClearUrl_Click(object sender, RoutedEventArgs e)
    {
        TxtUrl.Text = string.Empty;
        SearchPanel.Visibility = Visibility.Collapsed;
        TxtUrl.Focus(FocusState.Programmatic);
    }

    private void TxtUrl_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) BtnAdd_Click(sender, new RoutedEventArgs());
        else if (e.Key == VirtualKey.Escape) SearchPanel.Visibility = Visibility.Collapsed;
    }

    // ── Cascade controls ───────────────────────────────────────

    private async void BtnAnalyze_Click(object sender, RoutedEventArgs e)
    {
        BtnAnalyze.IsEnabled = false;
        TxtStatus.Text = "Analizando enlaces...";
        await _cascade.AnalyzeAllAsync();
        TxtStatus.Text = "Análisis completo. Revisa la lista y pulsa Iniciar cascada.";
        RefreshState();
    }

    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        TxtStatus.Text = $"Cascada en curso (umbral {Core.AppSettings.Current.CascadeThreshold}%)...";
        BtnStart.IsEnabled = BtnAnalyze.IsEnabled = BtnAdd.IsEnabled = false;
        await _cascade.StartCascadeAsync();
        TxtStatus.Text = "Cascada finalizada.";
        RefreshState();
    }

    private void BtnRemove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DownloadItem item)
        { _cascade.Remove(item); RefreshState(); }
    }

    private void CboFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboQuality == null) return;
        string format   = (string)((ComboBoxItem)CboFormat.SelectedItem).Content;
        bool isAudio    = format is "MP3" or "M4A" or "OGG" or "FLAC" or "WAV" or "OPUS";
        bool isLossless = format is "FLAC" or "WAV";

        CboQuality.Items.Clear();
        string[] options = isLossless
            ? new[] { "Mejor (sin pérdida)" }
            : isAudio
                ? new[] { "320kbps", "256kbps", "192kbps", "128kbps", "64kbps" }
                : new[] { "Mejor", "4K", "2K", "1080p", "720p", "480p", "360p", "240p" };

        foreach (var q in options)
            CboQuality.Items.Add(new ComboBoxItem { Content = q });

        SelectComboByContent(CboQuality, Core.AppSettings.Current.DefaultQuality);
    }
}
