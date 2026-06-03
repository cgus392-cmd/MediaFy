using System;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using YTDownloader.Core;
using System.IO;
using System.Threading.Tasks;

namespace YTDownloader.Views.Controls;

public sealed partial class StemTrackControl : UserControl
{
    private StemTrack? _track;
    private FfmpegService? _ffmpeg;

    public event Action<StemTrack>? SoloToggled;

    public StemTrackControl()
    {
        this.InitializeComponent();
    }

    public async Task InitializeAsync(StemTrack track, FfmpegService ffmpeg)
    {
        _track = track;
        _ffmpeg = ffmpeg;
        
        TxtName.Text = track.Name;
        SldVolume.Value = track.Volume;
        BtnMute.IsChecked = track.IsMuted;
        BtnSolo.IsChecked = track.IsSoloActiveOnTrack;

        await LoadWaveformAsync();
    }

    private async Task LoadWaveformAsync()
    {
        if (_track == null || _ffmpeg == null) return;

        RingLoading.IsActive = true;

        try
        {
            // Usamos un color acorde a la interfaz (ej. verde menta o el acento)
            string colorHex = "0x22C55E"; 
            string pngPath = await _ffmpeg.GenerateWaveformAsync(_track.FilePath, 800, 60, colorHex);
            
            if (File.Exists(pngPath))
            {
                var bmp = new BitmapImage();
                bmp.SetSource(await Windows.Storage.StorageFile.GetFileFromPathAsync(pngPath).AsTask().ContinueWith(t => t.Result.OpenReadAsync().AsTask().Result));
                ImgWaveform.Source = bmp;
            }
        }
        catch 
        {
            // Si falla la generación, dejamos el espacio vacío
        }
        finally
        {
            RingLoading.IsActive = false;
        }
    }

    private void SldVolume_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_track != null)
        {
            _track.Volume = e.NewValue;
        }
    }

    private void BtnMute_CheckedChanged(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_track != null)
        {
            _track.IsMuted = BtnMute.IsChecked ?? false;
        }
    }

    private void BtnSolo_CheckedChanged(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (_track != null)
        {
            SoloToggled?.Invoke(_track);
        }
    }
}
