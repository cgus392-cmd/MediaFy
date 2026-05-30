using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using WinRT;
using DispatcherQueue = Windows.System.DispatcherQueue;
using DispatcherQueueController = Windows.System.DispatcherQueueController;

namespace YTDownloader.Core;

/// <summary>
/// Gestiona el telón de fondo de la ventana vía los controladores de composición,
/// soportando Mica, Mica Alt, Acrílico, Acrílico fino y Ninguno, con sincronización
/// de tema y estado de activación. Permite cambiarlo en vivo.
/// </summary>
public class BackdropManager
{
    private readonly Window _window;
    private DispatcherQueueController? _dqController;
    private ISystemBackdropControllerWithTargets? _controller;
    private SystemBackdropConfiguration? _config;
    private FrameworkElement? _root;
    private bool _hooked;

    public BackdropManager(Window window) => _window = window;

    public void Apply(BackdropKind kind)
    {
        _controller?.Dispose();
        _controller = null;

        if (kind == BackdropKind.None) return;

        EnsureDispatcherQueue();
        EnsureConfig();

        bool isMica = kind is BackdropKind.Mica or BackdropKind.MicaAlt;
        if (isMica && MicaController.IsSupported())
        {
            var c = new MicaController
            {
                Kind = kind == BackdropKind.MicaAlt ? MicaKind.BaseAlt : MicaKind.Base
            };
            c.SetSystemBackdropConfiguration(_config);
            c.AddSystemBackdropTarget(_window.As<ICompositionSupportsSystemBackdrop>());
            _controller = c;
        }
        else if (DesktopAcrylicController.IsSupported())
        {
            var c = new DesktopAcrylicController
            {
                Kind = kind == BackdropKind.AcrylicThin ? DesktopAcrylicKind.Thin : DesktopAcrylicKind.Base
            };
            c.SetSystemBackdropConfiguration(_config);
            c.AddSystemBackdropTarget(_window.As<ICompositionSupportsSystemBackdrop>());
            _controller = c;
        }
    }

    private void EnsureConfig()
    {
        if (_config != null) { UpdateTheme(); return; }

        _config = new SystemBackdropConfiguration { IsInputActive = true };
        _root = _window.Content as FrameworkElement;
        if (_root != null) _root.ActualThemeChanged += (_, _) => UpdateTheme();
        if (!_hooked) { _window.Activated += OnActivated; _hooked = true; }
        UpdateTheme();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (_config != null)
            _config.IsInputActive = e.WindowActivationState != WindowActivationState.Deactivated;
    }

    private void UpdateTheme()
    {
        if (_config == null || _root == null) return;
        _config.Theme = _root.ActualTheme switch
        {
            ElementTheme.Dark  => SystemBackdropTheme.Dark,
            ElementTheme.Light => SystemBackdropTheme.Light,
            _                  => SystemBackdropTheme.Default
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions { public int dwSize; public int threadType; public int apartmentType; }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options,
        [MarshalAs(UnmanagedType.IUnknown)] ref object? dispatcherQueueController);

    private void EnsureDispatcherQueue()
    {
        if (DispatcherQueue.GetForCurrentThread() != null) return;
        if (_dqController != null) return;

        var options = new DispatcherQueueOptions
        {
            dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
            threadType = 2,    // DQTYPE_THREAD_CURRENT
            apartmentType = 2  // DQTAT_COM_STA
        };
        object? controller = null;
        CreateDispatcherQueueController(options, ref controller);
        _dqController = controller as DispatcherQueueController;
    }
}
