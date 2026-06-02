using System.IO;
using System.Runtime.InteropServices;
using YTDownloader.Models;

namespace YTDownloader.Core;

/// <summary>Detección de unidades y expulsión segura de extraíbles (Win32).</summary>
public static class DriveService
{
    /// <summary>Unidades listas y relevantes (locales, extraíbles, red).</summary>
    public static List<StorageDrive> GetDrives()
    {
        var list = new List<StorageDrive>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                if (d.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.Network)) continue;
                list.Add(new StorageDrive
                {
                    Root = d.RootDirectory.FullName,
                    Letter = d.Name.TrimEnd('\\'),
                    Label = d.VolumeLabel,
                    Type = d.DriveType,
                    FreeBytes = d.AvailableFreeSpace,
                    TotalBytes = d.TotalSize
                });
            }
            catch { /* unidad no accesible */ }
        }
        return list;
    }

    // ── Expulsión segura (desmontar volumen) ──
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFile(string fileName, uint access, uint share, IntPtr sec,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr h, uint code, IntPtr inBuf, uint inSize,
        IntPtr outBuf, uint outSize, out uint returned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);

    private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;
    private const uint FSCTL_LOCK_VOLUME = 0x00090018;
    private const uint FSCTL_DISMOUNT_VOLUME = 0x00090020;
    private const uint IOCTL_STORAGE_MEDIA_REMOVAL = 0x002D4804;
    private const uint IOCTL_STORAGE_EJECT_MEDIA = 0x002D4808;

    /// <summary>Expulsa con seguridad una unidad extraíble. <paramref name="letter"/> = "D:".</summary>
    public static bool Eject(string letter)
    {
        if (string.IsNullOrWhiteSpace(letter)) return false;
        IntPtr h = CreateFile($@"\\.\{letter}",
            GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == IntPtr.Zero || h.ToInt64() == -1) return false;
        try
        {
            DeviceIoControl(h, FSCTL_LOCK_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
            DeviceIoControl(h, FSCTL_DISMOUNT_VOLUME, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);

            IntPtr buf = Marshal.AllocHGlobal(1);
            Marshal.WriteByte(buf, 0); // PREVENT_MEDIA_REMOVAL = false → permitir retirar
            DeviceIoControl(h, IOCTL_STORAGE_MEDIA_REMOVAL, buf, 1, IntPtr.Zero, 0, out _, IntPtr.Zero);
            Marshal.FreeHGlobal(buf);

            return DeviceIoControl(h, IOCTL_STORAGE_EJECT_MEDIA, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
        }
        catch { return false; }
        finally { CloseHandle(h); }
    }
}
