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

    // Vista previa con debounce
    private readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private CancellationTokenSource? _previewCts;
    private string _lastPreviewedUrl = string.Empty;

    public DownloadsPage()
    {
        InitializeComponent();
        DownloadsList.ItemsSource = _manager.Queue;
        _manager.Queue.CollectionChanged += (_, _) => RefreshListState();
        RefreshListState();

        // Restaura la concurrencia guardada
        int saved = _manager.MaxConcurrent;
        foreach (ComboBoxItem ci in CboConcurrent.Items)
            if ((string)ci.Content == saved.ToString()) { ci.IsSelected = true; break; }

        _previewTimer.Tick += async (_, _) => { _previewTimer.Stop(); await UpdatePreviewAsync(); };
        TxtUrl.TextChanged += (_, _) => { _previewTimer.Stop(); _previewTimer.Start(); };

        if (!_manager.IsReady)
            TxtStatus.Text = "⚠  Faltan yt-dlp.exe o ffmpeg.exe en Assets/";
    }

    // ── Vista previa (Fase 3) ──────────────────────────────────
    private static bool IsYouTubeUrl(string url) =>
        url.Contains("youtube.com/watch", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase) ||
        url.Contains("youtube.com/shorts", StringComparison.OrdinalIgnoreCase);

    private async Task UpdatePreviewAsync()
    {
        string url = TxtUrl.Text.Trim();

        // Cancela cualquier análisis previo en curso
        _previewCts?.Cancel();

        if (string.IsNullOrWhiteSpace(url))
        {
            PreviewCard.Visibility = Visibility.Collapsed;
            _lastPreviewedUrl = string.Empty;
            return;
        }

        if (!IsYouTubeUrl(url))
        {
            ShowPreviewState(error: "No parece un enlace de YouTube válido");
            return;
        }

        if (url == _lastPreviewedUrl) return; // ya analizado
        _lastPreviewedUrl = url;

        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;
        ShowPreviewState(loading: true);

        try
        {
            var info = await _manager.GetInfoAsync(url, ct);
            if (ct.IsCancellationRequested) return;

            PreviewTitle.Text = info.Title;
            PreviewUploader.Text = string.IsNullOrEmpty(info.Uploader) ? "Desconocido" : info.Uploader;
            PreviewDuration.Text = string.IsNullOrEmpty(info.Duration) ? "—" : info.Duration;
            if (!string.IsNullOrEmpty(info.Thumbnail))
                PreviewThumb.Source = new BitmapImage(new Uri(info.Thumbnail));
            PreviewQualities.ItemsSource = info.QualityLabels.Count > 0
                ? info.QualityLabels
                : new List<string> { "Audio" };

            ShowPreviewState(info: true);
        }
        catch (OperationCanceledException) { /* reemplazado por otro análisis */ }
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
        PreviewInfo.Visibility    = info ? Visibility.Visible : Visibility.Collapsed;
        PreviewError.Visibility   = error != null ? Visibility.Visible : Visibility.Collapsed;
        if (error != null) PreviewErrorText.Text = error;
    }

    private void RefreshListState()
    {
        int count = _manager.Queue.Count;
        TxtCount.Text = count.ToString();
        EmptyState.Visibility    = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        DownloadsList.Visibility = count >  0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        string url = TxtUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!url.Contains("youtube.com") && !url.Contains("youtu.be"))
        {
            TxtStatus.Text = "Pega un enlace válido de YouTube";
            return;
        }

        string format  = (string)((ComboBoxItem)CboFormat.SelectedItem).Content;
        string quality = (string)((ComboBoxItem)CboQuality.SelectedItem).Content;

        // No bloquea: arranca en segundo plano y permite varias en paralelo
        _manager.AddAndStart(url, format, quality);

        TxtUrl.Text = string.Empty;
        TxtStatus.Text = "Añadida a la cola";
    }

    private async void BtnPaste_Click(object sender, RoutedEventArgs e)
    {
        var dp = Clipboard.GetContent();
        if (dp.Contains(StandardDataFormats.Text))
            TxtUrl.Text = (await dp.GetTextAsync()).Trim();
    }

    private void TxtUrl_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
            BtnDownload_Click(sender, new RoutedEventArgs());
    }

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

    private void CboConcurrent_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboConcurrent.SelectedItem is ComboBoxItem item &&
            int.TryParse((string)item.Content, out int n))
            _manager.MaxConcurrent = n;
    }

    private void CboFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboQuality == null) return;
        string format = (string)((ComboBoxItem)CboFormat.SelectedItem).Content;
        bool isAudio = format is "MP3" or "M4A" or "OGG";

        CboQuality.Items.Clear();
        string[] options = isAudio
            ? new[] { "320kbps", "256kbps", "192kbps", "128kbps" }
            : new[] { "Mejor", "4K", "1080p", "720p", "480p", "360p" };

        foreach (var q in options)
            CboQuality.Items.Add(new ComboBoxItem { Content = q });
        CboQuality.SelectedIndex = 0;
    }
}
