using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GogDisc.Core;

// Adapted from TargetFill 2.5.0 Create-SparseFile (ArchivSect1986, MIT).
// See THIRD-PARTY-NOTICES.md. Filenames do not guarantee physical sector placement.
public static class TargetFill
{
    public const long SectorBytes = 2048;

    public static long Fill(string directory, long capacity, long reserve, CancellationToken cancellationToken = default)
    {
        if (capacity <= 0 || reserve < 0 || reserve >= capacity)
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must exceed the filesystem reserve.");
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToArray();
        var used = files.Sum(path => checked((new FileInfo(path).Length + SectorBytes - 1) / SectorBytes * SectorBytes));
        var remaining = capacity - reserve - used;
        if (remaining < 0) throw new InvalidDataException("The completed disc exceeds its capacity after the filesystem reserve.");
        var sectors = remaining / SectorBytes;
        var created = new List<string>();
        try
        {
            Create("00_lead_in_part1_filler.dat", sectors / 2 * SectorBytes);
            Create("zz_lead_out_part2_filler.dat", (sectors - sectors / 2) * SectorBytes);
            return sectors * SectorBytes;
        }
        catch
        {
            foreach (var path in created) File.Delete(path);
            throw;
        }

        void Create(string name, long size)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (size == 0) return;
            var path = Path.Combine(directory, name);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created.Add(path);
            if (!DeviceIoControl(stream.SafeFileHandle, 0x000900C4, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "TargetFill requires a filesystem that supports sparse files (such as NTFS). Disable padding or choose another output drive.");
            stream.SetLength(size);
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, uint inputSize,
        IntPtr output, uint outputSize, out uint returned, IntPtr overlapped);
}
