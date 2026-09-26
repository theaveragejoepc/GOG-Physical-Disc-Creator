# Build and test

Use Windows 10/11 x64, PowerShell, Git, and a .NET SDK compatible with `global.json` (8.0.424 or a later SDK permitted by its roll-forward setting). Projects target .NET 8 Windows. No private package feed, signing key, API key, or GOG account is required to build.

```powershell
git clone https://github.com/theaveragejoepc/GOG-Physical-Disc-Creator.git
cd GOG-Physical-Disc-Creator
dotnet build GOGDiscTool.sln -c Release
dotnet run --project tests/GogDisc.SelfTests -c Release
dotnet run --project tests/GogDisc.WizardChecks -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File ./publish.ps1 -Version 1.0.0-beta.1 -OutputDirectory ./artifacts/release
```

Use a new, empty output directory. `publish.ps1` produces a self-contained creator and `LauncherPayload/Launch.exe`, plus documentation and licenses. Keep the payload folder next to the creator. Namespace and solution names retain `GogDisc` for compatibility.

The self-tests use synthetic installer files, never commercial game installers. Wizard checks exercise WPF controls and require an interactive Windows desktop. Optional ISO integration test (mounts only its own disposable ISO):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./tests/Test-IsoEject.ps1
```

The ISO check mounts, reads, ejects and remounts three times, and verifies exclusive access to the detached image. Do not run it concurrently with other optical-media tests. It does not validate physical-drive hardware. Fixtures are retained under ignored `artifacts/`.

GitHub Actions builds and runs the noninteractive regression suite on Windows. UI, account downloads, real game installation, physical controllers and optical hardware require separate testing. No reproducible-byte-for-byte build claim is made.
