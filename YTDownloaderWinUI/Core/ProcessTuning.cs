using System.Diagnostics;

namespace YTDownloader.Core;

/// <summary>
/// Ajustes de prioridad para los procesos auxiliares (yt-dlp, ffmpeg, motor de stems).
///
/// Estas herramientas son intensivas en CPU y, con prioridad normal, compiten de tú a tú con lo
/// que el usuario esté haciendo: el sistema entero se siente lento mientras MediaFy descarga o
/// convierte. Bajarlas a "por debajo de lo normal" hace que sigan aprovechando toda la CPU libre,
/// pero cediendo el paso a la aplicación en primer plano.
/// </summary>
public static class ProcessTuning
{
    /// <summary>Baja la prioridad de un proceso auxiliar recién iniciado. Nunca lanza.</summary>
    public static void RunInBackground(Process proc)
    {
        try
        {
            if (!proc.HasExited) proc.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch { /* el proceso terminó o el sistema no lo permite: irrelevante */ }
    }
}
