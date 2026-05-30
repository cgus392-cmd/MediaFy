using System.Runtime.InteropServices;
using Windows.Foundation;
using Windows.Media;
using Windows.Media.Audio;
using Windows.Media.Core;
using Windows.Media.MediaProperties;
using Windows.Media.Render;
using Windows.Storage;
using WinRT;

namespace YTDownloader.Core;

[ComImport]
[Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
unsafe interface IMemoryBufferByteAccess
{
    void GetBuffer(out byte* buffer, out uint capacity);
}

/// <summary>
/// Reproductor global con AudioGraph: reproduce el archivo y, en paralelo, lee las
/// muestras PCM reales para calcular el nivel (pico) de cada canal L/R. VU-meter real.
/// </summary>
public class PlaybackService
{
    private AudioGraph? _graph;
    private MediaSourceAudioInputNode? _input;
    private AudioDeviceOutputNode? _output;
    private AudioFrameOutputNode? _frameOutput;
    private int _channels = 2;

    public string CurrentTitle { get; private set; } = string.Empty;
    public bool HasMedia { get; private set; }
    public bool IsPlaying { get; private set; }
    public double Volume { get; private set; } = 1.0;

    // Niveles reales por canal (0..1), actualizados cada quantum de audio.
    public double LevelLeft { get; private set; }
    public double LevelRight { get; private set; }

    public event Action? Changed;

    public async Task PlayAsync(string path, string title)
    {
        if (!await EnsureGraphAsync()) return;

        // Quita la fuente anterior
        if (_input != null) { try { _input.Dispose(); } catch { } _input = null; }

        var file = await StorageFile.GetFileFromPathAsync(path);
        var src = MediaSource.CreateFromStorageFile(file);
        var res = await _graph!.CreateMediaSourceAudioInputNodeAsync(src);
        if (res.Status != MediaSourceAudioInputNodeCreationStatus.Success) return;

        _input = res.Node;
        _input.OutgoingGain = Volume;
        _input.AddOutgoingConnection(_output);
        _input.AddOutgoingConnection(_frameOutput);
        _input.MediaSourceCompleted += (_, _) =>
        {
            IsPlaying = false;
            LevelLeft = LevelRight = 0;
            Changed?.Invoke();
        };

        CurrentTitle = title;
        HasMedia = true;
        _graph.Start();
        IsPlaying = true;
        Changed?.Invoke();
    }

    private async Task<bool> EnsureGraphAsync()
    {
        if (_graph != null) return true;

        var settings = new AudioGraphSettings(AudioRenderCategory.Media);
        var gr = await AudioGraph.CreateAsync(settings);
        if (gr.Status != AudioGraphCreationStatus.Success) return false;
        _graph = gr.Graph;

        var outRes = await _graph.CreateDeviceOutputNodeAsync();
        if (outRes.Status != AudioDeviceNodeCreationStatus.Success) return false;
        _output = outRes.DeviceOutputNode;

        // Fuerza el nodo de medición a ESTÉREO float, sin importar si la salida es 7.1
        var meterProps = AudioEncodingProperties.CreatePcm(_graph.EncodingProperties.SampleRate, 2, 32);
        meterProps.Subtype = MediaEncodingSubtypes.Float;
        _frameOutput = _graph.CreateFrameOutputNode(meterProps);
        _channels = 2;

        _graph.QuantumStarted += OnQuantum;
        return true;
    }

    private void OnQuantum(AudioGraph sender, object args)
    {
        if (_frameOutput is null) return;
        try
        {
            using AudioFrame frame = _frameOutput.GetFrame();
            ProcessFrame(frame);
        }
        catch { }
    }

    private unsafe void ProcessFrame(AudioFrame frame)
    {
        using AudioBuffer buffer = frame.LockBuffer(AudioBufferAccessMode.Read);
        using IMemoryBufferReference reference = buffer.CreateReference();

        // CsWinRT: obtener el acceso a bytes vía proyección WinRT (no cast directo)
        var byteAccess = reference.As<IMemoryBufferByteAccess>();
        byteAccess.GetBuffer(out byte* bytes, out uint capacity);

        float* data = (float*)bytes;
        int count = (int)(capacity / sizeof(float));
        int ch = _channels <= 0 ? 2 : _channels;

        double peakL = 0, peakR = 0;
        for (int i = 0; i + ch - 1 < count; i += ch)
        {
            double l = Math.Abs(data[i]);
            double r = ch > 1 ? Math.Abs(data[i + 1]) : l;
            if (l > peakL) peakL = l;
            if (r > peakR) peakR = r;
        }

        LevelLeft = Math.Min(1.0, peakL);
        LevelRight = Math.Min(1.0, peakR);
    }

    public void Toggle()
    {
        if (_graph is null) return;
        if (IsPlaying) { _graph.Stop(); IsPlaying = false; LevelLeft = LevelRight = 0; }
        else { _graph.Start(); IsPlaying = true; }
        Changed?.Invoke();
    }

    public void SetVolume(double v)
    {
        Volume = Math.Clamp(v, 0, 1);
        if (_input != null) _input.OutgoingGain = Volume;
    }

    public void Close()
    {
        try { _graph?.Stop(); } catch { }
        if (_input != null) { try { _input.Dispose(); } catch { } _input = null; }
        HasMedia = false;
        IsPlaying = false;
        LevelLeft = LevelRight = 0;
        CurrentTitle = string.Empty;
        Changed?.Invoke();
    }
}
