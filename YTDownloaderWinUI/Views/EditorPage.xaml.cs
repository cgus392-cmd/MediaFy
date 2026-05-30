using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.ViewManagement;
using YTDownloader.Core;
using YTDownloader.Models;

namespace YTDownloader.Views;

public sealed partial class EditorPage : Page
{
    private readonly FfmpegService _ffmpeg = new();
    private MediaPlayer? _mp;
    private string? _currentPath;
    private string? _wavePath;
    private bool _isVideo;
    private bool _busy;

    private TimeSpan _duration, _start, _end;
    private double _zoom = 1;
    private string? _dragging;       // "start" | "end" | null
    private bool _suppressNum;
    private bool _playingSelection;

    private readonly DispatcherTimer _cursorTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };

    public EditorPage()
    {
        InitializeComponent();
        _cursorTimer.Tick += CursorTimer_Tick;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string path && File.Exists(path))
            _ = LoadFileAsync(path);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cursorTimer.Stop();
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
            _isVideo = new LibraryFile { Extension = Path.GetExtension(path).ToLowerInvariant() }.IsVideo;

            var file = await StorageFile.GetFileFromPathAsync(path);
            _mp = new MediaPlayer { Source = MediaSource.CreateFromStorageFile(file) };
            Player.SetMediaPlayer(_mp);
            AudioOverlay.Visibility = _isVideo ? Visibility.Collapsed : Visibility.Visible;

            _mp.PlaybackSession.NaturalDurationChanged += (s, _) =>
            {
                var dur = s.NaturalDuration;
                DispatcherQueue.TryEnqueue(() =>
                {
                    _duration = dur;
                    _start = TimeSpan.Zero;
                    _end = dur;
                    SetNumbers();
                    NumStart.Maximum = NumEnd.Maximum = dur.TotalSeconds;
                    Redraw();
                });
            };

            TxtFileName.Text = Path.GetFileName(path);
            EmptyState.Visibility = Visibility.Collapsed;
            PlayerHost.Visibility = WavePanel.Visibility = TrimPanel.Visibility = ActionPanel.Visibility = Visibility.Visible;
            TxtTrimStatus.Text = "Generando forma de onda...";

            await GenerateWaveAsync(path);
            _cursorTimer.Start();
            TxtTrimStatus.Text = string.Empty;
        }
        catch (Exception ex)
        {
            TxtTrimStatus.Text = $"No se pudo abrir: {ex.Message}";
        }
    }

    private async Task GenerateWaveAsync(string path)
    {
        try
        {
            DeleteWave();
            var accent = new UISettings().GetColorValue(UIColorType.Accent);
            string hex = $"0x{accent.R:X2}{accent.G:X2}{accent.B:X2}";
            _wavePath = await _ffmpeg.GenerateWaveformAsync(path, 1600, 150, hex);
            WaveImage.Source = new BitmapImage(new Uri(_wavePath));
            Redraw();
        }
        catch { /* sin onda, el resto sigue funcionando */ }
    }

    private void DeleteWave()
    {
        if (_wavePath != null && File.Exists(_wavePath))
            try { File.Delete(_wavePath); } catch { }
        _wavePath = null;
    }

    // ── Mapeo tiempo ↔ pixel ───────────────────────────────────
    private double ViewWidth => WaveScroll.ViewportWidth > 0 ? WaveScroll.ViewportWidth : WaveScroll.ActualWidth;
    private double ScaledWidth => Math.Max(ViewWidth * _zoom, 1);
    private double TimeToX(TimeSpan t) => _duration.TotalSeconds <= 0 ? 0 : t.TotalSeconds / _duration.TotalSeconds * ScaledWidth;
    private TimeSpan XToTime(double x) =>
        TimeSpan.FromSeconds(Math.Clamp(x / ScaledWidth * _duration.TotalSeconds, 0, _duration.TotalSeconds));

    private void Redraw()
    {
        if (_duration.TotalSeconds <= 0) return;
        double w = ScaledWidth;
        WaveCanvas.Width = w;
        WaveImage.Width = w;

        double xs = TimeToX(_start), xe = TimeToX(_end);
        Canvas.SetLeft(StartHandle, xs - 5);
        Canvas.SetLeft(EndHandle, xe - 5);
        Canvas.SetLeft(DimLeft, 0); DimLeft.Width = Math.Max(0, xs);
        Canvas.SetLeft(DimRight, xe); DimRight.Width = Math.Max(0, w - xe);

        UpdateSelLen();
    }

    private void UpdateSelLen() => TxtSelLen.Text = $"Fragmento: {Fmt(_end - _start)}";

    // ── Arrastre de manijas / clic para buscar ─────────────────
    private void StartHandle_PointerPressed(object s, PointerRoutedEventArgs e)
    { _dragging = "start"; WaveCanvas.CapturePointer(e.Pointer); e.Handled = true; }

    private void EndHandle_PointerPressed(object s, PointerRoutedEventArgs e)
    { _dragging = "end"; WaveCanvas.CapturePointer(e.Pointer); e.Handled = true; }

    private void WaveCanvas_PointerPressed(object s, PointerRoutedEventArgs e)
    {
        if (_dragging != null) return;
        double x = e.GetCurrentPoint(WaveCanvas).Position.X;
        if (_mp?.PlaybackSession != null) _mp.PlaybackSession.Position = XToTime(x);
    }

    private void WaveCanvas_PointerMoved(object s, PointerRoutedEventArgs e)
    {
        if (_dragging == null) return;
        double x = e.GetCurrentPoint(WaveCanvas).Position.X;
        var t = XToTime(x);
        if (_dragging == "start")
            _start = TimeSpan.FromSeconds(Math.Min(t.TotalSeconds, _end.TotalSeconds - 0.05));
        else
            _end = TimeSpan.FromSeconds(Math.Max(t.TotalSeconds, _start.TotalSeconds + 0.05));
        SetNumbers();
        Redraw();
    }

    private void WaveCanvas_PointerReleased(object s, PointerRoutedEventArgs e)
    {
        if (_dragging == null) return;
        WaveCanvas.ReleasePointerCapture(e.Pointer);
        _dragging = null;
    }

    // ── Cursor de reproducción ─────────────────────────────────
    private void CursorTimer_Tick(object? s, object e)
    {
        if (_mp?.PlaybackSession is not { } ps || _duration.TotalSeconds <= 0) return;
        Canvas.SetLeft(Cursor, TimeToX(ps.Position));

        if (_playingSelection && ps.Position >= _end)
        {
            _mp.Pause();
            _playingSelection = false;
        }
    }

    // ── Tiempos editables ──────────────────────────────────────
    private void SetNumbers()
    {
        _suppressNum = true;
        NumStart.Value = Math.Round(_start.TotalSeconds, 2);
        NumEnd.Value = Math.Round(_end.TotalSeconds, 2);
        _suppressNum = false;
        UpdateSelLen();
    }

    private void NumStart_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressNum || double.IsNaN(args.NewValue)) return;
        _start = TimeSpan.FromSeconds(Math.Clamp(args.NewValue, 0, Math.Max(0, _end.TotalSeconds - 0.05)));
        Redraw();
    }

    private void NumEnd_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_suppressNum || double.IsNaN(args.NewValue)) return;
        _end = TimeSpan.FromSeconds(Math.Clamp(args.NewValue, _start.TotalSeconds + 0.05, _duration.TotalSeconds));
        Redraw();
    }

    private void ZoomSlider_ValueChanged(object s, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        _zoom = e.NewValue;
        Redraw();
    }

    private void WaveScroll_SizeChanged(object s, SizeChangedEventArgs e) => Redraw();

    private void BtnPlaySelection_Click(object s, RoutedEventArgs e)
    {
        if (_mp?.PlaybackSession is null) return;
        _mp.PlaybackSession.Position = _start;
        _mp.Play();
        _playingSelection = true;
    }

    // ── Abrir / cortar ─────────────────────────────────────────
    private async void BtnOpen_Click(object s, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        foreach (var ext in LibraryFile.MediaExtensions) picker.FileTypeFilter.Add(ext);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSingleFileAsync();
        if (file != null) await LoadFileAsync(file.Path);
    }

    private async void BtnCut_Click(object s, RoutedEventArgs e)
    {
        if (_busy || _currentPath is null) return;
        if (_end <= _start) { TxtTrimStatus.Text = "El fin debe ser posterior al inicio"; return; }

        bool replace;
        var mode = AppSettings.Current.CutSaveMode;
        if (mode == CutSaveMode.Ask)
        {
            var choice = await AskSaveModeAsync();
            if (choice is null) return;
            replace = choice.Value;
        }
        else replace = mode == CutSaveMode.Replace;

        double fadeIn = ChkFadeIn.IsChecked == true ? NumFade.Value : 0;
        double fadeOut = ChkFadeOut.IsChecked == true ? NumFade.Value : 0;
        if (double.IsNaN(fadeIn)) fadeIn = 0;
        if (double.IsNaN(fadeOut)) fadeOut = 0;

        _busy = true; BtnCut.IsEnabled = false;
        TxtTrimStatus.Text = "Cortando...";
        string path = _currentPath;
        _cursorTimer.Stop();
        ReleasePlayer();

        try
        {
            string result = await _ffmpeg.TrimAsync(path, _start, _end, replace, fadeIn, fadeOut, _isVideo);
            TxtTrimStatus.Text = $"✓ Guardado: {Path.GetFileName(result)}";
            await ShowSavedDialogAsync(result);
            await LoadFileAsync(result);
        }
        catch (Exception ex)
        {
            TxtTrimStatus.Text = $"Error: {ex.Message}";
            await LoadFileAsync(path);
        }
        finally
        {
            _busy = false; BtnCut.IsEnabled = true;
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
            ContentDialogResult.Primary => false,
            ContentDialogResult.Secondary => true,
            _ => null
        };
    }

    private static string Fmt(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss\.fff") : t.ToString(@"mm\:ss\.fff");
}
