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

    // ── Consumo en vivo (para la sección Experimental) ──
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME { public uint Low; public uint High; }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    private static ulong ToU(FILETIME f) => ((ulong)f.High << 32) | f.Low;
    private static ulong _pIdle, _pKernel, _pUser; private static bool _cpuPrimed;

    /// <summary>Uso de CPU (0..100). Mantiene estado entre llamadas (~1/seg).</summary>
    public static double CpuUsagePercent()
    {
        if (!GetSystemTimes(out var i, out var k, out var u)) return 0;
        ulong idle = ToU(i), kernel = ToU(k), user = ToU(u);
        double pct = 0;
        if (_cpuPrimed)
        {
            double total = (kernel - _pKernel) + (user - _pUser);
            double idl = idle - _pIdle;
            pct = total > 0 ? (1.0 - idl / total) * 100.0 : 0;
        }
        _pIdle = idle; _pKernel = kernel; _pUser = user; _cpuPrimed = true;
        return Math.Clamp(pct, 0, 100);
    }

    /// <summary>Uso de GPU NVIDIA (0..100) vía nvidia-smi, o -1 si no hay. Llamar en background (~50-100ms).</summary>
    public static double GpuUsagePercent()
    {
        if (!HasNvidiaGpu) return -1;
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                Arguments = "--query-gpu=utilization.gpu --format=csv,noheader,nounits",
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
            })!;
            string outp = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(2000);
            var first = outp.Split('\n').FirstOrDefault()?.Trim();
            if (double.TryParse(first, out double g)) return Math.Clamp(g, 0, 100);
        }
        catch { }
        return -1;
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
