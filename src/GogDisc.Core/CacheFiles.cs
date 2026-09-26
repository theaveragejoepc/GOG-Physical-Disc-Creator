using System.Security.Cryptography;
using System.ComponentModel;

namespace GogDisc.Core;

public static class CacheFiles
{
    public static void Copy(string source, string destination) => RetryFileAccess(() => CopyOnce(source, destination));

    // Antivirus scanning and overlapping starts can briefly lock a newly cached EXE.
    public static void RetryFileAccess(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { action(); return; }
            catch (Exception ex) when (attempt < 5 && (ex is UnauthorizedAccessException ||
                ex is IOException io && (io.HResult & 0xffff) is 5 or 32 or 33 ||
                ex is Win32Exception win32 && win32.NativeErrorCode is 5 or 32 or 33))
            {
                Thread.Sleep(200 * (attempt + 1));
            }
        }
    }

    private static void CopyOnce(string source, string destination)
    {
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase))
        {
            MakeWritable(destination);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            MakeWritable(destination);
            if (FilesMatch(source, destination)) return;
        }

        // Publish a complete, writable file. A second start must never see a partial EXE.
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporary);
            MakeWritable(temporary);
            // Another process may have completed the same copy while we were writing.
            if (File.Exists(destination) && FilesMatch(temporary, destination)) return;
            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary)) { MakeWritable(temporary); File.Delete(temporary); }
        }
    }

    private static bool FilesMatch(string first, string second)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length) return false;
        using var firstStream = File.OpenRead(first);
        using var secondStream = File.OpenRead(second);
        return SHA256.HashData(firstStream).AsSpan().SequenceEqual(SHA256.HashData(secondStream));
    }

    private static void MakeWritable(string path)
    {
        if (!File.Exists(path)) return;
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }
}
