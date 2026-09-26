using System.IO;
using System.Security.Cryptography;
using System.Text;
using GogDisc.Core;

namespace GogDisc.Packager;

public static class ArtworkKeyStore
{
    public static string DefaultPath => Path.Combine(AppPaths.Root, "steamgriddb-key.dat");

    public static string Load(string path)
    {
        if (!File.Exists(path)) return "";
        var plaintext = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public static void Save(string path, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Enter a SteamGridDB API key first.");
        var plaintext = Encoding.UTF8.GetBytes(key.Trim());
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void Forget(string path) => File.Delete(path);
}
