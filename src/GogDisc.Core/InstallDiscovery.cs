using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace GogDisc.Core;

public static class InstallDiscovery
{
    private static readonly string[] UninstallRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    public static InstallState? Discover(PackageManifest package)
    {
        var saved = InstallStateStore.Load(package.PackageId);
        // Older versions saved GalaxyClient.exe from GOG-created desktop shortcuts.
        if (saved is not null && IsUseful(saved) && CanPlay(saved) && saved.PlayWorkingDirectory is not null) return saved;

        var configuredNames = package.InstallDetectionNames.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
        var names = (configuredNames.Length > 0 ? configuredNames : [package.Title]).Distinct().ToArray();
        if (saved is not null &&
            FindPlayTarget(names, saved.InstallLocation, null) is { } repairedTarget)
        {
            ApplyLaunch(saved, repairedTarget);
            InstallStateStore.Save(saved);
            return saved;
        }
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (var rootName in UninstallRoots)
                {
                    using var root = baseKey.OpenSubKey(rootName);
                    if (root is null) continue;
                    foreach (var subName in root.GetSubKeyNames())
                    {
                        using var entry = root.OpenSubKey(subName);
                        var displayName = entry?.GetValue("DisplayName") as string;
                        if (displayName is null || !names.Any(name => FuzzyContains(displayName, name))) continue;
                        var installLocation = entry?.GetValue("InstallLocation") as string;
                        var displayIcon = CleanDisplayIcon(entry?.GetValue("DisplayIcon") as string);
                        var uninstall = (entry?.GetValue("QuietUninstallString") ?? entry?.GetValue("UninstallString")) as string;
                        var play = FindPlayTarget(names, installLocation, displayIcon);
                        var state = new InstallState
                        {
                            PackageId = package.PackageId,
                            Title = package.Title,
                            InstallLocation = Directory.Exists(installLocation) ? installLocation : Path.GetDirectoryName(play?.FileName),
                            PlayTarget = play?.FileName,
                            PlayArguments = play?.Arguments,
                            PlayWorkingDirectory = play?.WorkingDirectory,
                            UninstallCommand = uninstall,
                            InstalledAt = DateTimeOffset.Now
                        };
                        if (IsUseful(state))
                        {
                            InstallStateStore.Save(state);
                            return state;
                        }
                    }
                }
            }
            catch { }
        }

        var shortcut = FindShortcut(names);
        if (shortcut is null) return null;
        var shortcutState = new InstallState
        {
            PackageId = package.PackageId,
            Title = package.Title,
            PlayTarget = shortcut.FileName,
            PlayArguments = shortcut.Arguments,
            PlayWorkingDirectory = shortcut.WorkingDirectory,
            InstallLocation = shortcut.WorkingDirectory,
            InstalledAt = DateTimeOffset.Now
        };
        InstallStateStore.Save(shortcutState);
        return shortcutState;
    }

    public static InstallState SaveManualTarget(PackageManifest package, string target, string? installLocation = null)
    {
        var launch = Path.GetExtension(target).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            ? ResolveShortcut(target) : IsDirectGameExecutable(target) && !IsEmulator(target) ? DirectLaunch(target) : null;
        if (launch is null)
            throw new InvalidDataException("Choose the game executable or its direct game shortcut. DOSBox games need their configured shortcut; Galaxy shortcuts are not supported.");
        var current = InstallStateStore.Load(package.PackageId) ?? new InstallState { PackageId = package.PackageId, Title = package.Title };
        ApplyLaunch(current, launch);
        current.InstallLocation = string.IsNullOrWhiteSpace(installLocation) ? Path.GetDirectoryName(current.PlayTarget) : Path.GetFullPath(installLocation);
        current.InstalledAt = DateTimeOffset.Now;
        InstallStateStore.Save(current);
        return current;
    }

    private static bool IsUseful(InstallState state) =>
        (!string.IsNullOrWhiteSpace(state.PlayTarget) && File.Exists(state.PlayTarget)) ||
        (!string.IsNullOrWhiteSpace(state.UninstallCommand) && !string.IsNullOrWhiteSpace(state.InstallLocation));

    private static ProcessStartInfo? FindPlayTarget(string[] names, string? installLocation, string? displayIcon) =>
        FindShortcut(names, installLocation) ?? (FindGameExecutable(names, installLocation, displayIcon) is { } exe ? DirectLaunch(exe) : null);

    private static ProcessStartInfo DirectLaunch(string exe) => new(Path.GetFullPath(exe))
        { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(exe))! };

    private static void ApplyLaunch(InstallState state, ProcessStartInfo launch)
    {
        state.PlayTarget = launch.FileName;
        state.PlayArguments = launch.Arguments;
        state.PlayWorkingDirectory = launch.WorkingDirectory;
    }

    public static bool CanPlay(InstallState? state) => IsDirectGameExecutable(state?.PlayTarget) &&
        (!IsEmulator(state!.PlayTarget!) || !string.IsNullOrWhiteSpace(state.PlayArguments));

    private static bool IsEmulator(string path) => Path.GetFileNameWithoutExtension(path).StartsWith("dosbox", StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileNameWithoutExtension(path).Equals("scummvm", StringComparison.OrdinalIgnoreCase);

    public static string? FindGameExecutable(string[] names, string? installLocation, string? displayIcon = null)
    {
        if (string.IsNullOrWhiteSpace(installLocation) || !Directory.Exists(installLocation)) return null;
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installLocation)) + Path.DirectorySeparatorChar;
        if (IsDirectGameExecutable(displayIcon) && !IsEmulator(displayIcon!) && Path.GetFullPath(displayIcon!).StartsWith(root, StringComparison.OrdinalIgnoreCase)) return displayIcon;

        var tokens = names.SelectMany(NormalizedTokens).Where(token => token.Length >= 3).Distinct().ToArray();
        try
        {
            return Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.AllDirectories)
                .Where(path => IsDirectGameExecutable(path) && !IsEmulator(path))
                .OrderByDescending(path => tokens.Count(token => Normalize(Path.GetFileNameWithoutExtension(path)).Contains(token)))
                .ThenBy(path => path.Count(character => character == Path.DirectorySeparatorChar))
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private static ProcessStartInfo? FindShortcut(string[] names, string? installLocation = null)
    {
        var roots = new[]
        {
            installLocation,
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs)
        };
        foreach (var root in roots.Where(Directory.Exists))
        {
            try
            {
                foreach (var shortcut in Directory.EnumerateFiles(root!, "*.lnk", SearchOption.AllDirectories))
                {
                    if (!names.Any(name => FuzzyContains(Path.GetFileNameWithoutExtension(shortcut), name))) continue;
                    var target = ResolveShortcut(shortcut);
                    if (target is not null && (string.IsNullOrWhiteSpace(installLocation) ||
                        Path.GetFullPath(target.FileName).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(installLocation)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) return target;
                }
            }
            catch { }
        }
        return null;
    }

    public static ProcessStartInfo? ResolveShortcut(string shortcut)
    {
        object? shell = null, link = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return null;
            shell = Activator.CreateInstance(shellType)!;
            link = ((dynamic)shell).CreateShortcut(Path.GetFullPath(shortcut));
            string target = ((dynamic)link).TargetPath;
            string arguments = ((dynamic)link).Arguments;
            string directory = ((dynamic)link).WorkingDirectory;
            if (!IsDirectGameExecutable(target) || (IsEmulator(target) && string.IsNullOrWhiteSpace(arguments))) return null;
            return new ProcessStartInfo(target, arguments) { UseShellExecute = true,
                WorkingDirectory = string.IsNullOrWhiteSpace(directory) ? Path.GetDirectoryName(target)! : directory };
        }
        catch { return null; }
        finally
        {
            if (link is not null) Marshal.FinalReleaseComObject(link);
            if (shell is not null) Marshal.FinalReleaseComObject(shell);
        }
    }

    private static string? CleanDisplayIcon(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var cleaned = value.Trim().Trim('"');
        var index = cleaned.LastIndexOf(',');
        if (index > 2 && int.TryParse(cleaned[(index + 1)..], out _)) cleaned = cleaned[..index].Trim('"');
        return Environment.ExpandEnvironmentVariables(cleaned);
    }

    private static bool IsUtilityExecutable(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("setup", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("patch", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("crash", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("report", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GalaxyClient.exe", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GalaxyClientService.exe", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GalaxyCommunication.exe", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("GalaxyUpdater.exe", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("steam.exe", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("EpicGamesLauncher.exe", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDirectGameExecutable(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path) &&
        Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) && !IsUtilityExecutable(path);

    private static bool FuzzyContains(string candidate, string expected)
    {
        var candidateTokens = NormalizedTokens(candidate).ToHashSet();
        var expectedTokens = NormalizedTokens(expected).ToArray();
        // The first game may omit "1" in its shortcut, but sequel numbers must match.
        var numbers = expectedTokens.Where(token => token.All(char.IsDigit)).ToArray();
        if (numbers.Length == 1 && numbers[0] == "1" && !candidateTokens.Any(token => token.All(char.IsDigit)))
            expectedTokens = expectedTokens.Where(token => token != "1").ToArray();
        else if (!candidateTokens.Where(token => token.All(char.IsDigit)).ToHashSet().SetEquals(numbers)) return false;
        return expectedTokens.Length > 0 && expectedTokens.All(candidateTokens.Contains);
    }

    private static IEnumerable<string> NormalizedTokens(string value) =>
        Regex.Split(Normalize(value), @"\s+").Where(token => token.Length > 0);

    private static string Normalize(string value) =>
        Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();
}

public static class ProcessCommands
{
    public static ProcessStartInfo ForGame(InstallState state)
    {
        if (!InstallDiscovery.CanPlay(state)) throw new InvalidDataException("The game needs a configured launch shortcut.");
        return new ProcessStartInfo(state.PlayTarget!, state.PlayArguments ?? "") { UseShellExecute = true,
            WorkingDirectory = state.PlayWorkingDirectory ?? Path.GetDirectoryName(state.PlayTarget!)! };
    }

    public static ProcessStartInfo ForLocalLauncher(string executable, string packagePath, string discRoot)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (var argument in new[] { "--local", "--package", packagePath, "--disc-root", discRoot })
            start.ArgumentList.Add(argument);
        return start;
    }

    public static ProcessStartInfo FromRegisteredCommand(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        string fileName;
        string arguments;
        if (command.StartsWith('"'))
        {
            var closing = command.IndexOf('"', 1);
            if (closing < 0) throw new InvalidDataException("Invalid registered command.");
            fileName = command[1..closing];
            arguments = command[(closing + 1)..].Trim();
        }
        else
        {
            var exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exe < 0) throw new InvalidDataException("Registered command has no executable.");
            fileName = command[..(exe + 4)].Trim();
            arguments = command[(exe + 4)..].Trim();
        }
        return new ProcessStartInfo(fileName, arguments) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(fileName) ?? "" };
    }
}
