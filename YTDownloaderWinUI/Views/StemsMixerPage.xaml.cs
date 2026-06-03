using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using YTDownloader.Core;
using YTDownloader.Views.Controls;
using Microsoft.UI.Xaml;

namespace YTDownloader.Views;

public sealed partial class StemsMixerPage : Page
{
    private StemsMixerEngine? _engine;
    private FfmpegService _ffmpeg = new();
    private string _folderPath = string.Empty;
    private bool _isSliderDragging = false;

    public StemsMixerPage()
    {
        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string folderPath)
        {
            _folderPath = folderPath;
            TxtTitle.Text = "Stems: " + new DirectoryInfo(folderPath).Name.Replace("Stems de ", "");
            
            await LoadMixerAsync();
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _engine?.Dispose();
        _engine = null;
    }

    private async Task LoadMixerAsync()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        TracksPanel.Children.Clear();

        _engine = new StemsMixerEngine();
        _engine.PositionChanged += Engine_PositionChanged;
        _engine.StateChanged += Engine_StateChanged;

        await _engine.LoadStemsAsync(_folderPath);

        SldPosition.Maximum = _engine.Duration.TotalSeconds;

        foreach (var track in _engine.Tracks)
        {
            var control = new StemTrackControl();
            control.SoloToggled += (t) => _engine.ToggleSolo(t);
            TracksPanel.Children.Add(control);
            
            // Inicializar asíncronamente para que empiece a renderizar la onda
            _ = control.InitializeAsync(track, _ffmpeg);
        }

        LoadingOverlay.Visibility = Visibility.Collapsed;
        UpdatePlaybackUI();
    }

    private void Engine_PositionChanged()
    {
        if (_engine == null || _isSliderDragging) return;
        
        DispatcherQueue.TryEnqueue(() =>
        {
            SldPosition.Value = _engine.Position.TotalSeconds;
            UpdatePlaybackUI();
        });
    }

    private void Engine_StateChanged()
    {
        DispatcherQueue.TryEnqueue(UpdatePlaybackUI);
    }

    private void UpdatePlaybackUI()
    {
        if (_engine == null) return;
        
        IconPlayPause.Glyph = _engine.IsPlaying ? "\uE769" : "\uE768";
        TxtTime.Text = $"{_engine.Position:mm\\:ss} / {_engine.Duration:mm\\:ss}";
    }

    public string FormatTime(double value) => TimeSpan.FromSeconds(value).ToString(@"mm\:ss");

    private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
    {
        _engine?.TogglePlayPause();
    }

    private void SldPosition_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        // Solo aplicar si el cambio proviene del usuario interactuando
        if (FocusState != FocusState.Unfocused && _engine != null)
        {
            // Pequeño workaround para detectar si el usuario arrastra
            _isSliderDragging = true;
            _engine.Position = TimeSpan.FromSeconds(e.NewValue);
            _isSliderDragging = false;
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private async void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        if (_engine == null || _engine.Tracks.Count == 0) return;
        
        _engine.Pause();

        var picker = new Windows.Storage.Pickers.FileSavePicker();
        // Obtener el HWND de la ventana principal para WinUI 3
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary;
        picker.FileTypeChoices.Add("Audio MP3", new System.Collections.Generic.List<string>() { ".mp3" });
        picker.SuggestedFileName = TxtTitle.Text + " (Mezcla)";

        var file = await picker.PickSaveFileAsync();
        if (file != null)
        {
            var task = NotificationCenter.Start("Exportando mezcla", "\uE78C");
            try
            {
                await _ffmpeg.ExportMixAsync(_engine.Tracks, file.Path);
                task.Done("Mezcla guardada exitosamente");
            }
            catch (Exception ex)
            {
                task.Fail($"Error al exportar: {ex.Message}");
            }
        }
    }
}
