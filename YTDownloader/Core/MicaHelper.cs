using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace YTDownloader.Core;

public static class MicaHelper
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE     = 38;
    private const int DWMWA_MICA_EFFECT             = 1029;

    public static void Apply(Window window)
    {
        var source = PresentationSource.FromVisual(window) as HwndSource;
        if (source == null) return;

        IntPtr hwnd = source.Handle;

        // Fondo transparente para que Mica se vea
        window.Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));

        // Título oscuro (coincide con el tema oscuro del sistema)
        int dark = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        // Mica (DWMSBT_MAINWINDOW = 2)
        int mica = 2;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int));
    }
}
