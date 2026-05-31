using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace YTDownloader;

/// <summary>
/// Entry point custom para:
///  - Single-instance: si MediaFy ya está abierto y alguien la lanza con un argumento
///    (p.ej. mediafy://download?url=...), la actividad se reenvía y este proceso sale.
///  - Manejo de protocolo URL: registramos un handler para activaciones de tipo Protocol.
/// </summary>
public static class Program
{
    [System.STAThreadAttribute]
    static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var activatedArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        var keyedInstance = AppInstance.FindOrRegisterForKey("MediaFy.SingleInstance");

        if (!keyedInstance.IsCurrent)
        {
            // Ya hay una instancia abierta: redirigir y salir
            keyedInstance.RedirectActivationToAsync(activatedArgs).AsTask().GetAwaiter().GetResult();
            return 0;
        }

        // Somos la instancia principal: escuchar futuras activaciones
        keyedInstance.Activated += OnActivated;

        Application.Start((p) =>
        {
            var ctx = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(ctx);
            _ = new App();
        });

        return 0;
    }

    private static void OnActivated(object? sender, AppActivationArguments args)
    {
        // Las activaciones entrantes (otra instancia que se redirigió a nosotros)
        // se procesan en el hilo de UI por App
        App.HandleActivation(args);
    }
}
