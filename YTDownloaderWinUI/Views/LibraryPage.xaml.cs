using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using YTDownloader.Models;

namespace YTDownloader.Views;

public sealed partial class LibraryPage : Page
{
    public ObservableCollection<LibraryFile> Files { get; } = new();

    public LibraryPage()
    {
        InitializeComponent();
        FilesList.ItemsSource = Files;
        Loaded += (_, _) => LoadFiles();
    }

    private void LoadFiles()
    {
        Files.Clear();
        string folder = Core.AppSettings.Current.OutputFolder;
        try
        {
            if (Directory.Exists(folder))
            {
                var found = new DirectoryInfo(folder)
                    .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                    .Where(f => LibraryFile.MediaExtensions.Contains(f.Extension.ToLowerInvariant()))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Select(LibraryFile.From);
                foreach (var f in found) Files.Add(f);
            }
        }
        catch { }

        TxtCount.Text = Files.Count.ToString();
        EmptyState.Visibility = Files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FilesList.Visibility  = Files.Count > 0  ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e) => LoadFiles();

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        string folder = Core.AppSettings.Current.OutputFolder;
        Directory.CreateDirectory(folder);
        Process.Start("explorer.exe", folder);
    }

    // ── Reproducir ─────────────────────────────────────────────
    private async void BtnPlay_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string path || !File.Exists(path)) return;

        var mode = Core.AppSettings.Current.PlayerMode;
        if (mode == Core.PlayerMode.Ask)
        {
            var chosen = await AskPlayerModeAsync();
            if (chosen is null) return;
            mode = chosen.Value;
        }

        if (mode == Core.PlayerMode.Integrated)
            await App.Playback.PlayAsync(path, Path.GetFileNameWithoutExtension(path));
        else
            OpenWithWindows(path);
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string path && File.Exists(path))
            Frame.Navigate(typeof(EditorPage), path);
    }

    private async Task<Core.PlayerMode?> AskPlayerModeAsync()
    {
        var remember = new CheckBox { Content = "Recordar mi elección", Margin = new Thickness(0, 12, 0, 0) };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "¿Con qué quieres reproducir este archivo?", TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(remember);

        var dlg = new ContentDialog
        {
            Title = "Reproducir",
            Content = panel,
            PrimaryButtonText = "Reproductor integrado",
            SecondaryButtonText = "Otro programa…",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var r = await dlg.ShowAsync();
        Core.PlayerMode? choice = r switch
        {
            ContentDialogResult.Primary => Core.PlayerMode.Integrated,
            ContentDialogResult.Secondary => Core.PlayerMode.System,
            _ => null
        };
        if (choice is not null && remember.IsChecked == true)
            Core.AppSettings.Current.PlayerMode = choice.Value;
        return choice;
    }

    private static void OpenWithWindows(string path) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = "rundll32.exe",
            Arguments = $"shell32.dll,OpenAs_RunDLL {path}",
            UseShellExecute = false
        });

    private void BtnReveal_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string path && File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string path || !File.Exists(path)) return;
        var dlg = new ContentDialog
        {
            Title = "Eliminar archivo",
            Content = $"¿Seguro que quieres eliminar \"{Path.GetFileName(path)}\"? Esta acción no se puede deshacer.",
            PrimaryButtonText = "Eliminar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
        {
            try { File.Delete(path); } catch { }
            LoadFiles();
        }
    }
}
