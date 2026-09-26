using System.IO;
using System.Runtime.InteropServices;

namespace GogDisc.Launcher;

internal static class OpticalDriveEjector
{
    public static bool IsOpticalDrive(string? path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path ?? ""));
            return root is not null && new DriveInfo(root).DriveType == DriveType.CDRom;
        }
        catch
        {
            return false;
        }
    }

    public static async Task EjectAsync(string? path)
    {
        if (!IsOpticalDrive(path))
            throw new InvalidOperationException("The active package is not on an optical disc drive.");
        var root = Path.GetPathRoot(Path.GetFullPath(path!))!;
        object? shell = null, drives = null, drive = null;
        try
        {
            // Explorer's Eject detaches mounted ISO images as well as ejecting physical media.
            // A raw IOCTL_STORAGE_EJECT_MEDIA can leave the backing ISO attached and locked.
            var shellType = Type.GetTypeFromProgID("Shell.Application")
                ?? throw new InvalidOperationException("Windows Explorer's eject service is unavailable.");
            shell = Activator.CreateInstance(shellType)!;
            drives = ((dynamic)shell).NameSpace(17); // ssfDRIVES (This PC)
            drive = drives is null ? null : ((dynamic)drives).ParseName(root);
            if (drive is null) throw new IOException("Windows could not find the optical drive.");
            ((dynamic)drive).InvokeVerb("Eject");
        }
        finally
        {
            if (drive is not null) Marshal.FinalReleaseComObject(drive);
            if (drives is not null) Marshal.FinalReleaseComObject(drives);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }

        // Shell verbs return before completion. Keep the launcher available if eject fails or is cancelled.
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (!new DriveInfo(root).IsReady) return;
            await Task.Delay(250);
        }
        throw new IOException("Windows has not finished ejecting the disc. Close any files still in use, then try Eject in File Explorer.");
    }
}
