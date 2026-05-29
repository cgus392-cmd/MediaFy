using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using YTDownloader.Models;

namespace YTDownloader;

public sealed partial class MainPage : Page
{
    private readonly Core.DownloadManager _manager = App.DownloadManager;

    public MainPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        DownloadsList.ItemsSource = _manager.Queue;
        _manager.Queue.CollectionChanged += (_, _) => RefreshListState();
        RefreshListState();
        CheckDependencies();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Registra el title bar con la ventana para drag y doble-click nativo
        if (App.MainWindow is MainWindow mw)
            mw.SetTitleBarElement(AppTitleBar);
    }

    private void RefreshListState()
    {
        int count = _manager.Queue.Count;
        TxtCount.Text = count.ToString();
        EmptyState.Visibility    = count == 0 ? Visibility.Visible  : Visibility.Collapsed;
        DownloadsList.Visibility = count >  0 ? Visibility.Visible  : Visibility.Collapsed;
    }

    private void CheckDependencies()
    {
        if (!_manager.IsReady)
            TxtStatus.Text = "⚠  Faltan yt-dlp.exe o ffmpeg.exe en Assets/";
    }

    private async void BtnDownload_Click(object sender, RoutedEventArgs e)
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

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(_manager.OutputFolder);
        Process.Start("explorer.exe", _manager.OutputFolder);
    }

    private async void BtnSettings_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SettingsDialog { XamlRoot = XamlRoot };
        await dlg.ShowAsync();
    }

    private void BtnClearDone_Click(object sender, RoutedEventArgs e)
    {
        var done = _manager.Queue
            .Where(x => x.Status is DownloadStatus.Done or DownloadStatus.Error)
            .ToList();
        foreach (var item in done)
            _manager.Queue.Remove(item);
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
