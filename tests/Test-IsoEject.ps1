# Integration test. Mounts only a new, disposable ISO; never ejects existing user media.
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$testExe = Join-Path $PSScriptRoot 'GogDisc.SelfTests/bin/Release/net8.0-windows/GogDisc.SelfTests.exe'
if (-not (Test-Path -LiteralPath $testExe)) { throw 'Build GogDisc.SelfTests in Release first.' }
$fixtureRoot = Join-Path $projectRoot ('artifacts/iso-eject-check-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $fixtureRoot 'source'
$iso = Join-Path $fixtureRoot 'eject-check.iso'
New-Item -ItemType Directory -Path $source -Force | Out-Null
Set-Content -LiteralPath (Join-Path $source 'fixture.txt') -Value 'Disposable ISO eject/remount check.'

Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
public static class IsoTestStream {
    public static void Save(object source, string path) {
        var stream = (IStream)source;
        var count = Marshal.AllocHGlobal(4);
        try {
            using (var output = File.Create(path)) {
                var buffer = new byte[65536];
                while (true) {
                    stream.Read(buffer, buffer.Length, count);
                    var read = Marshal.ReadInt32(count);
                    if (read == 0) break;
                    output.Write(buffer, 0, read);
                }
            }
        } finally { Marshal.FreeHGlobal(count); }
    }
}
'@

$image = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
$image.FileSystemsToCreate = 3
$image.VolumeName = 'EJECT_TEST'
$tree = $image.Root
$tree.AddTree($source, $false)
$result = $image.CreateResultImage()
$stream = $result.ImageStream
[IsoTestStream]::Save($stream, $iso)
foreach ($com in @($stream, $result, $tree, $image)) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($com) }

try {
    foreach ($cycle in 1..3) {
        $mounted = Mount-DiskImage -ImagePath $iso -PassThru
        $volume = $mounted | Get-Volume
        $drive = "$($volume.DriveLetter):\"
        if (-not (Test-Path -LiteralPath (Join-Path $drive 'fixture.txt'))) { throw 'Mounted fixture cannot be read.' }
        & $testExe --eject-test-drive $drive
        if ($LASTEXITCODE -ne 0) { throw 'Launcher eject failed.' }
        if ((Get-DiskImage -ImagePath $iso).Attached) { throw 'ISO backing image is still attached after eject.' }
        # Exclusive access proves the backing file was released, not just the drive letter removed.
        $exclusive = [IO.File]::Open($iso, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $exclusive.Dispose()
        Write-Output "PASS cycle ${cycle}: mounted, read, ejected, detached, and released ISO file."
    }
} finally {
    if ((Get-DiskImage -ImagePath $iso).Attached) { Dismount-DiskImage -ImagePath $iso | Out-Null }
}
Write-Output "PASS repeated ISO remount without reboot. Fixture retained at $iso"
