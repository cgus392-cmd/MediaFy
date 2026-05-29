using System.Diagnostics;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace YTDownloader;

public sealed partial class SettingsDialog : ContentDialog
{
    public SettingsDialog()
    {
        InitializeComponent();
        TxtFolder.Text = App.DownloadManager.OutputFolder;
        _ = LoadYtDlpVersionAsync();
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
            string ver = (await proc.StandardOutput.ReadToEndAsync()).Trim();
            TxtYtDlpVersion.Text = $"Versión instalada: {ver}";
        }
        catch { TxtYtDlpVersion.Text = "Error al leer la versión"; }
    }

    private async void OnBrowse(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        // Interop: el picker necesita el HWND de la ventana principal (app unpackaged)
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
            TxtFolder.Text = folder.Path;
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (App.MainWindow?.Content is not FrameworkElement root) return;
        var tag = (string)((ComboBoxItem)CboTheme.SelectedItem).Tag;
        root.RequestedTheme = tag switch
        {
            "Light"   => ElementTheme.Light,
            "Default" => ElementTheme.Default,
            _         => ElementTheme.Dark
        };
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

    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        App.DownloadManager.OutputFolder = TxtFolder.Text;
    }
}
