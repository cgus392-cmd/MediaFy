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
        TxtFolder.Text = App.DownloadManager.OutputFolder;

        // Restaura el umbral de cascada guardado
        int threshold = Core.AppSettings.Current.CascadeThreshold;
        foreach (ComboBoxItem ci in CboThreshold.Items)
            if ((string?)ci.Tag == threshold.ToString()) { ci.IsSelected = true; break; }

        _ = LoadYtDlpVersionAsync();
    }

    private void OnThresholdChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CboThreshold.SelectedItem is ComboBoxItem item &&
            int.TryParse((string?)item.Tag, out int v))
            Core.AppSettings.Current.CascadeThreshold = v;
    }

    private async Task LoadYtDlpVersionAsync()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "yt-dlp.exe");
            if (!File.Exists(path)) { TxtYtDlpVersion.Text = "No encontrado en Assets/"; return; }
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = path, Arguments = "--version",
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            })!;
            TxtYtDlpVersion.Text = $"Versión instalada: {(await proc.StandardOutput.ReadToEndAsync()).Trim()}";
        }
        catch { TxtYtDlpVersion.Text = "Error al leer la versión"; }
    }

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
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = path, Arguments = "-U",
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            })!;
            await proc.WaitForExitAsync();
            await LoadYtDlpVersionAsync();
        }
        catch { TxtYtDlpVersion.Text = "Error al actualizar"; }
    }
}
