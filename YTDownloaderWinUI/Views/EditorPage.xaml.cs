using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using YTDownloader.Core;
using YTDownloader.Models;

namespace YTDownloader.Views;

public sealed partial class EditorPage : Page
{
    private readonly FfmpegService _ffmpeg = new();
    private MediaPlayer? _mp;
    private string? _currentPath;
    private TimeSpan _start = TimeSpan.Zero;
    private TimeSpan _end = TimeSpan.Zero;
    private bool _busy;

    public EditorPage() => InitializeComponent();

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string path && File.Exists(path))
            _ = LoadFileAsync(path);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ReleasePlayer();
    }

    private void ReleasePlayer()
    {
        try { _mp?.Pause(); } catch { }
        if (_mp != null) { _mp.Source = null; }
        Player.SetMediaPlayer(null);
        _mp?.Dispose();
        _mp = null;
    }

    private async Task LoadFileAsync(string path)
    {
        try
        {
            ReleasePlayer();
            _currentPath = path;

            var file = await StorageFile.GetFileFromPathAsync(path);
            _mp = new MediaPlayer { Source = MediaSource.CreateFromStorageFile(file) };
            Player.SetMediaPlayer(_mp);

            bool isAudio = new LibraryFile { Extension = Path.GetExtension(path).ToLowerInvariant() }.IsAudio;
            AudioOverlay.Visibility = isAudio ? Visibility.Visible : Visibility.Collapsed;

            _mp.PlaybackSession.NaturalDurationChanged += (s, _) =>
            {
                var dur = s.NaturalDuration;
                DispatcherQueue.TryEnqueue(() =>
                {
                    _end = dur;
                    TxtEnd.Text = Fmt(_end);
                });
            };

            TxtFileName.Text = Path.GetFileName(path);
            _start = TimeSpan.Zero;
            TxtStart.Text = Fmt(_start);
            TxtTrimStatus.Text = string.Empty;

            EmptyState.Visibility = Visibility.Collapsed;
            PlayerHost.Visibility = Visibility.Visible;
            TrimPanel.Visibility  = Visibility.Visible;
        }
        catch (Exception ex)
        {
            TxtTrimStatus.Text = $"No se pudo abrir: {ex.Message}";
        }
    }

    private async void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        foreach (var ext in LibraryFile.MediaExtensions) picker.FileTypeFilter.Add(ext);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        if (file != null) await LoadFileAsync(file.Path);
    }

    private TimeSpan Position => _mp?.PlaybackSession?.Position ?? TimeSpan.Zero;

    private void BtnMarkStart_Click(object sender, RoutedEventArgs e)
    {
        _start = Position;
        TxtStart.Text = Fmt(_start);
        ValidateTrim();
    }

    private void BtnMarkEnd_Click(object sender, RoutedEventArgs e)
    {
        _end = Position;
        TxtEnd.Text = Fmt(_end);
        ValidateTrim();
    }

    private void ValidateTrim()
    {
        if (_end <= _start)
            TxtTrimStatus.Text = "El fin debe ser posterior al inicio";
        else
            TxtTrimStatus.Text = $"Fragmento: {Fmt(_end - _start)}";
    }

    private async void BtnCut_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _currentPath is null) return;
        if (_end <= _start) { TxtTrimStatus.Text = "El fin debe ser posterior al inicio"; return; }

        // Decide modo de guardado
        bool replace;
        var mode = AppSettings.Current.CutSaveMode;
        if (mode == CutSaveMode.Ask)
        {
            var choice = await AskSaveModeAsync();
            if (choice is null) return; // cancelado
            replace = choice.Value;
        }
        else replace = mode == CutSaveMode.Replace;

        _busy = true;
        BtnCut.IsEnabled = false;
        TxtTrimStatus.Text = "Cortando...";

        string path = _currentPath;
        ReleasePlayer(); // libera el archivo para que ffmpeg pueda escribir

        try
        {
            string result = await _ffmpeg.TrimAsync(path, _start, _end, replace);
            TxtTrimStatus.Text = $"✓ Guardado: {Path.GetFileName(result)}";
            await LoadFileAsync(result); // recarga el resultado
            await ShowSavedDialogAsync(result);
        }
        catch (Exception ex)
        {
            TxtTrimStatus.Text = $"Error: {ex.Message}";
            await LoadFileAsync(path); // recarga el original
        }
        finally
        {
            _busy = false;
            BtnCut.IsEnabled = true;
        }
    }

    private async Task ShowSavedDialogAsync(string resultPath)
    {
        var dlg = new ContentDialog
        {
            Title = "Fragmento guardado",
            Content = $"Se guardó como:\n\n{resultPath}",
            PrimaryButtonText = "Abrir carpeta",
            CloseButtonText = "Aceptar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await dlg.ShowAsync() == ContentDialogResult.Primary)
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{resultPath}\"");
    }

    private async Task<bool?> AskSaveModeAsync()
    {
        var dlg = new ContentDialog
        {
            Title = "Guardar corte",
            Content = "¿Cómo quieres guardar el fragmento recortado?",
            PrimaryButtonText = "Crear copia nueva",
            SecondaryButtonText = "Reemplazar original",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        var r = await dlg.ShowAsync();
        return r switch
        {
            ContentDialogResult.Primary => false,   // copia nueva
            ContentDialogResult.Secondary => true,  // reemplazar
            _ => null
        };
    }

    private static string Fmt(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss\.fff") : t.ToString(@"mm\:ss\.fff");
}
