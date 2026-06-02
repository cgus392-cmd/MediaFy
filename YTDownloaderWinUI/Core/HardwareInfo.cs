using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace YTDownloader.Core;

/// <summary>Detección ligera de hardware para elegir el modo del motor de IA (GPU/CPU).</summary>
public static class HardwareInfo
{
    /// <summary>True si hay una GPU NVIDIA (driver instalado → nvidia-smi presente).</summary>
    public static bool HasNvidiaGpu =>
        File.Exists(Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"));

    /// <summary>Nombre de la GPU NVIDIA, o cadena vacía si no hay.</summary>
    public static string NvidiaName()
    {
        if (!HasNvidiaGpu) return string.Empty;
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                Arguments = "--query-gpu=name --format=csv,noheader",
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            })!;
            string outp = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(2000);
            return outp.Split('\n').FirstOrDefault()?.Trim() ?? string.Empty;
        }
        catch { return string.Empty; }
    }

    /// <summary>RAM física total en GB.</summary>
    public static double TotalRamGb()
    {
        try
        {
            var m = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(m)) return Math.Round(m.ullTotalPhys / 1073741824.0, 1);
        }
        catch { }
        return 0;
    }

    /// <summary>Modo recomendado para el motor de IA según el hardware.</summary>
    public static (bool useGpu, string label, string detail) Recommend()
    {
        if (HasNvidiaGpu)
        {
            string name = NvidiaName();
            return (true, "GPU (CUDA) — rápido",
                string.IsNullOrEmpty(name) ? "GPU NVIDIA detectada" : name);
        }
        double ram = TotalRamGb();
        string ramTxt = ram > 0 ? $"{ram:0.#} GB de RAM" : "RAM desconocida";
        return (false, "CPU — más lento", $"Sin GPU NVIDIA · {ramTxt}");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
