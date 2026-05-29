using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using YTDownloader.Core;

namespace YTDownloader.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => MicaHelper.Apply(this);
        TxtFolder.Text = App.DownloadManager.OutputFolder;
        _ = LoadYtDlpVersionAsync();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private async Task LoadYtDlpVersionAsync()
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "yt-dlp.exe");
            if (!File.Exists(path)) { TxtYtDlpVersion.Text = "No encontrado"; return; }
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = path, Arguments = "--version",
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            })!;
            TxtYtDlpVersion.Text = $"Versión: {(await proc.StandardOutput.ReadToEndAsync()).Trim()}";
        }
        catch { TxtYtDlpVersion.Text = "Error al leer versión"; }
    }

    private void BtnBrowse_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new FolderBrowserDialog { SelectedPath = TxtFolder.Text };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            TxtFolder.Text = dlg.SelectedPath;
    }

    private async void BtnUpdateYtDlp_Click(object sender, RoutedEventArgs e)
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "yt-dlp.exe");
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

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        App.DownloadManager.OutputFolder = TxtFolder.Text;
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();
}
