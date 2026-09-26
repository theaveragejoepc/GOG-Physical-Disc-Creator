namespace GogDisc.Core;

public static class InstallerQueue
{
    public static int Completed(PackageManifest package, string stateRoot)
    {
        var path = Path.Combine(stateRoot, PackageBuilder.SanitizeFileName(package.PackageId) + ".queue.json");
        try { return Math.Clamp(System.Text.Json.JsonSerializer.Deserialize<int>(File.ReadAllText(path)), 0, package.Installers.Count); }
        catch { return 0; }
    }

    public static void SaveCompleted(PackageManifest package, string stateRoot, int count) =>
        JsonFiles.Write(Path.Combine(stateRoot, PackageBuilder.SanitizeFileName(package.PackageId) + ".queue.json"), count);

    public static SetupFamily Combine(SetupFamily baseGame, IEnumerable<string> additionalInstallers)
    {
        var files = baseGame.InstallerFiles.ToList();
        var order = new List<string> { files[0].RelativePath };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetFullPath(baseGame.SetupExecutable) };
        foreach (var path in additionalInstallers)
        {
            if (!seen.Add(Path.GetFullPath(path)))
                throw new InvalidDataException($"Installer selected more than once: {path}");
            var family = SetupFamilyScanner.Scan(path, allowPatch: true);
            var prefix = $"Addons/{order.Count:D3}/";
            order.Add(prefix + family.InstallerFiles[0].RelativePath);
            foreach (var file in family.InstallerFiles)
                files.Add(file with { RelativePath = prefix + file.RelativePath, Sequence = files.Count });
        }
        return new SetupFamily
        {
            SetupExecutable = baseGame.SetupExecutable, FamilyName = baseGame.FamilyName,
            InstallerFiles = files, Extras = baseGame.Extras,
            ExcludedPatches = baseGame.ExcludedPatches.Where(path => !seen.Contains(Path.GetFullPath(path))).ToArray(),
            Installers = order
        };
    }

    public static IReadOnlyList<string> Validate(PackageManifest package)
    {
        var order = package.Installers.Count > 0 ? package.Installers : [package.InstallerRelativePath];
        if (order.Count == 0 || !order[0].Equals(package.InstallerRelativePath, StringComparison.OrdinalIgnoreCase) ||
            order.Distinct(StringComparer.OrdinalIgnoreCase).Count() != order.Count)
            throw new InvalidDataException("Invalid installer order: the base installer must be first, without duplicates.");
        foreach (var path in order)
        {
            SafePaths.ResolveUnderRoot(Path.GetTempPath(), path);
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                !package.Files.Any(file => file.Kind == PackageFileKind.Installer && file.RelativePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"Installer is missing from the verified payload: {path}");
        }
        return order;
    }
}
