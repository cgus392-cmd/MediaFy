using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace YTDownloader.Views;

public sealed partial class ResourcePage : Page
{
    private const int MaxPoints = 80;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1000) };

    private readonly List<double> _cpuHist = new();
    private readonly List<double> _memHist = new();
    private readonly List<double> _netHist = new();

    // CPU (GetSystemTimes)
    private ulong _prevIdle, _prevKernel, _prevUser;
    private bool _cpuPrimed;
    // Red
    private long _prevRecv, _prevSent;
    private bool _netPrimed;
    private double _memTotalGB;

    public ResourcePage()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Sample();
        Loaded += (_, _) => { Sample(); _timer.Start(); };
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _timer.Stop();
    }

    private void Graph_SizeChanged(object sender, SizeChangedEventArgs e) => RedrawAll();

    private void Sample()
    {
        // ── CPU ──
        if (GetSystemTimes(out var idleFt, out var kernelFt, out var userFt))
        {
            ulong idle = ToU(idleFt), kernel = ToU(kernelFt), user = ToU(userFt);
            if (_cpuPrimed)
            {
                double total = (kernel - _prevKernel) + (user - _prevUser);
                double idl = idle - _prevIdle;
                double usage = total > 0 ? (1.0 - idl / total) * 100.0 : 0;
                Push(_cpuHist, Math.Clamp(usage, 0, 100));
                CpuPct.Text = $"{usage:F0}%";
            }
            _prevIdle = idle; _prevKernel = kernel; _prevUser = user; _cpuPrimed = true;
        }
        try { CpuProcs.Text = Process.GetProcesses().Length.ToString(); } catch { }

        // ── Memoria ──
        var m = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(m))
        {
            double totalGB = m.ullTotalPhys / 1073741824.0;
            double availGB = m.ullAvailPhys / 1073741824.0;
            double usedGB = totalGB - availGB;
            _memTotalGB = totalGB;
            Push(_memHist, m.dwMemoryLoad);
            MemUsed.Text = $"{usedGB:F1} GB ({m.dwMemoryLoad}%)";
            MemTotal.Text = $"{totalGB:F1} GB";
        }

        // ── Red ──
        long recv = 0, sent = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = ni.GetIPv4Statistics();
                recv += s.BytesReceived; sent += s.BytesSent;
            }
        }
        catch { }
        if (_netPrimed)
        {
            double recvBps = Math.Max(0, recv - _prevRecv);
            double sentBps = Math.Max(0, sent - _prevSent);
            Push(_netHist, recvBps + sentBps);
            NetRecv.Text = FormatRate(recvBps);
            NetSend.Text = FormatRate(sentBps);
        }
        _prevRecv = recv; _prevSent = sent; _netPrimed = true;

        RedrawAll();
    }

    private static void Push(List<double> h, double v)
    {
        h.Add(v);
        if (h.Count > MaxPoints) h.RemoveAt(0);
    }

    private void RedrawAll()
    {
        UpdateGraph(CpuLine, CpuFill, _cpuHist, 100, CpuGraph);
        UpdateGraph(MemLine, MemFill, _memHist, 100, MemGraph);
        double netMax = _netHist.Count > 0 ? Math.Max(_netHist.Max() * 1.15, 64 * 1024) : 64 * 1024;
        UpdateGraph(NetLine, NetFill, _netHist, netMax, NetGraph);
    }

    private static void UpdateGraph(Polyline line, Polygon fill, List<double> hist, double max, Grid host)
    {
        double w = host.ActualWidth, h = host.ActualHeight;
        if (w <= 0 || h <= 0 || hist.Count < 2) return;

        var pts = new PointCollection();
        int n = hist.Count;
        double pad = 3;
        for (int i = 0; i < n; i++)
        {
            double x = (double)i / (MaxPoints - 1) * w;
            double v = Math.Clamp(hist[i] / max, 0, 1);
            double y = pad + (1 - v) * (h - pad * 2);
            pts.Add(new Point(x, y));
        }
        line.Points = pts;

        var fillPts = new PointCollection();
        foreach (var p in pts) fillPts.Add(p);
        fillPts.Add(new Point((double)pts[^1].X, h));
        fillPts.Add(new Point((double)pts[0].X, h));
        fill.Points = fillPts;
    }

    private static string FormatRate(double bytesPerSec)
    {
        double bits = bytesPerSec * 8;
        if (bits >= 1_000_000) return $"{bits / 1_000_000:F1} Mbps";
        if (bits >= 1_000) return $"{bits / 1_000:F0} Kbps";
        return $"{bits:F0} bps";
    }

    private static ulong ToU(System.Runtime.InteropServices.ComTypes.FILETIME ft)
        => ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    // ── P/Invoke ──
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME idleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME kernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME userTime);

    [StructLayout(LayoutKind.Sequential)]
    private class MEMORYSTATUSEX
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

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
}
