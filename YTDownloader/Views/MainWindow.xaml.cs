using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using YTDownloader.Core;
using YTDownloader.Models;
using Clipboard = System.Windows.Clipboard;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace YTDownloader.Views;

public partial class MainWindow : Window
{
    private readonly DownloadManager _manager = App.DownloadManager;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            MicaHelper.Apply(this);
            CheckDependencies();
        };

        // Placeholder manual
        TxtUrl.TextChanged += (_, _) =>
            TxtPlaceholder.Visibility = string.IsNullOrEmpty(TxtUrl.Text)
                ? Visibility.Visible : Visibility.Collapsed;

        DownloadsList.ItemsSource = _manager.Queue;
        _manager.Queue.CollectionChanged += (_, _) => RefreshListState();
        RefreshListState();
    }

    private void RefreshListState()
    {
        int count = _manager.Queue.Count;
        TxtCount.Text = count.ToString();
        EmptyState.Visibility    = count == 0 ? Visibility.Visible  : Visibility.Collapsed;
        DownloadScroll.Visibility = count > 0 ? Visibility.Visible  : Visibility.Collapsed;
    }

    private void CheckDependencies()
    {
        if (!_manager.IsReady)
            TxtStatus.Text = "⚠  Faltan yt-dlp.exe o ffmpeg.exe en Assets/";
    }

    // ── Title bar manual ──────────────────────────────────────
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleMaximize();
        else DragMove();
    }

    private void TitleBar_MouseMove(object sender, MouseEventArgs e) { }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void BtnMaximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal : WindowState.Maximized;
        MaxIcon.Text = WindowState == WindowState.Maximized ? "&#xE923;" : "&#xE922;";
    }

    // ── Lógica principal ──────────────────────────────────────
    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
    {
        string url = TxtUrl.Text.Trim();
        if (string.IsNullOrWhiteSpace(url)) return;

        if (!url.Contains("youtube.com") && !url.Contains("youtu.be"))
        {
            TxtStatus.Text = "Pega un enlace válido de YouTube";
            return;
        }

        string format  = ((ComboBoxItem)CboFormat.SelectedItem).Content.ToString()!;
        string quality = ((ComboBoxItem)CboQuality.SelectedItem).Content.ToString()!;

        TxtUrl.Text = string.Empty;
        TxtStatus.Text = $"Añadida: {url[..Math.Min(55, url.Length)]}...";
        BtnDownload.IsEnabled = false;
        try
        {
            await _manager.AddAndStartAsync(url, format, quality);
        }
        finally
        {
            BtnDownload.IsEnabled = true;
            TxtStatus.Text = "Listo";
        }
    }

    private void BtnPaste_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText())
            TxtUrl.Text = Clipboard.GetText().Trim();
    }

    private void TxtUrl_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            BtnDownload_Click(sender, new RoutedEventArgs());
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_manager.OutputFolder);
        Process.Start("explorer.exe", _manager.OutputFolder);
    }

    private void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow { Owner = this };
        win.ShowDialog();
    }

    private void BtnClearDone_Click(object sender, RoutedEventArgs e)
    {
        var done = _manager.Queue
            .Where(x => x.Status is DownloadStatus.Done or DownloadStatus.Error)
            .ToList();
        foreach (var item in done) _manager.Queue.Remove(item);
    }

    private void CboFormat_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboQuality == null) return;
        string format = ((ComboBoxItem)CboFormat.SelectedItem).Content.ToString()!;
        bool isAudio = format is "MP3" or "M4A" or "OGG";

        CboQuality.Items.Clear();
        foreach (var q in isAudio
            ? new[] { "320kbps", "256kbps", "192kbps", "128kbps" }
            : new[] { "Mejor", "4K", "1080p", "720p", "480p", "360p" })
            CboQuality.Items.Add(new ComboBoxItem { Content = q });
        CboQuality.SelectedIndex = 0;
    }
}
