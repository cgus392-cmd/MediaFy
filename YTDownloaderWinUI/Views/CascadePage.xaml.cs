using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using YTDownloader.Models;

namespace YTDownloader.Views;

public sealed partial class CascadePage : Page
{
    private readonly Core.CascadeManager _cascade = App.CascadeManager;

    public CascadePage()
    {
        InitializeComponent();
        ItemsList.ItemsSource = _cascade.Items;
        _cascade.Items.CollectionChanged += (_, _) => RefreshState();
        RefreshState();
    }

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

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        string url = TxtUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!Core.PlatformDetector.LooksLikeUrl(url))
        {
            TxtStatus.Text = "Pega un enlace válido (https://...)";
            return;
        }
        var platform = Core.PlatformDetector.Detect(url);
        if (platform == Core.Platform.Spotify)
        {
            TxtStatus.Text = "Spotify llega en el próximo paso 🎵";
            return;
        }
        if (!Core.AppSettings.Current.IsPlatformEnabled(platform))
        {
            TxtStatus.Text = $"{Core.PlatformDetector.Name(platform)} está desactivada (Configuración › Plataformas)";
            return;
        }
        if (!_cascade.CanAdd)
        {
            TxtStatus.Text = "Lista llena (máx. 5) o cascada en curso";
            return;
        }

        string format  = (string)((ComboBoxItem)CboFormat.SelectedItem).Content;
        string quality = (string)((ComboBoxItem)CboQuality.SelectedItem).Content;

        _cascade.Add(url, format, quality);
        TxtUrl.Text = string.Empty;
        TxtStatus.Text = "Enlace agregado. Cuando termines, pulsa Analizar todo.";
        RefreshState();
    }

    private void TxtUrl_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) BtnAdd_Click(sender, new RoutedEventArgs());
    }

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
        {
            _cascade.Remove(item);
            RefreshState();
        }
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
