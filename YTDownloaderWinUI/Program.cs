using System.Runtime.InteropServices;

namespace YTDownloader;

public static class Program
{
    [DllImport("Microsoft.WindowsAppRuntime.Bootstrap.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MddBootstrapInitialize(uint majorMinorVersion, string? versionTag, ulong minVersion);

    [DllImport("Microsoft.WindowsAppRuntime.Bootstrap.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern void MddBootstrapShutdown();

    [STAThread]
    static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // 0x00010006 = Windows App SDK 1.6
        int hr = MddBootstrapInitialize(0x00010006, null, 0);
        if (hr != 0)
            throw new InvalidOperationException($"Bootstrap.Initialize falló: 0x{hr:X8}");

        Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        MddBootstrapShutdown();
    }
}
