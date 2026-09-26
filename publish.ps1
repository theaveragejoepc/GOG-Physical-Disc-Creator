param(
    [string]$Configuration = 'Release',
    [string]$Version,
    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$projectRoot = $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-cli'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.nuget-packages'
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$artifactRoot = if ($OutputDirectory) {
    if ([IO.Path]::IsPathRooted($OutputDirectory)) {
        [IO.Path]::GetFullPath($OutputDirectory)
    } else {
        [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
    }
} else {
    # Local builds live outside the repository so an internal test build can never be committed or published.
    Join-Path $env:LOCALAPPDATA "GOG Disc Packager\Builds\GOGDiscTool-$stamp"
}
if (Test-Path -LiteralPath $artifactRoot) { throw 'Choose a new output directory; existing folders will not be overwritten or removed.' }
$launcherOutput = Join-Path $artifactRoot 'LauncherPayload'
$packagerTemp = Join-Path ([IO.Path]::GetTempPath()) ("GOGDiscTool-packager-" + [Guid]::NewGuid().ToString("N"))

try {
    New-Item -ItemType Directory -Path $launcherOutput -Force | Out-Null
    New-Item -ItemType Directory -Path $packagerTemp -Force | Out-Null

    dotnet restore (Join-Path $projectRoot 'GOGDiscTool.sln') `
        --configfile (Join-Path $projectRoot 'NuGet.Config')
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }

    $launcherArguments = @(
        'publish', (Join-Path $projectRoot 'src\GogDisc.Launcher\GogDisc.Launcher.csproj'),
        '-c', $Configuration, '--no-restore', '-o', $launcherOutput
    )
    if ($Version) { $launcherArguments += "-p:Version=$Version" }
    & dotnet @launcherArguments
    if ($LASTEXITCODE -ne 0) { throw 'Launcher publish failed.' }
    Get-ChildItem -LiteralPath $launcherOutput -File -Filter '*.pdb' | Remove-Item -Force

    $packagerArguments = @(
        'publish', (Join-Path $projectRoot 'src\GogDisc.Packager\GogDisc.Packager.csproj'),
        '-c', $Configuration, '--no-restore', '-o', $packagerTemp
    )
    if ($Version) { $packagerArguments += "-p:Version=$Version" }
    & dotnet @packagerArguments
    if ($LASTEXITCODE -ne 0) { throw 'Packager publish failed.' }

    Copy-Item -LiteralPath (Join-Path $packagerTemp 'GOG Physical Disc Creator.exe') -Destination $artifactRoot
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $artifactRoot
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $artifactRoot
    New-Item -ItemType Directory -Path (Join-Path $artifactRoot 'docs') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/CUSTOM-BUILD.md') -Destination (Join-Path $artifactRoot 'docs')
    Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $artifactRoot

    foreach ($name in @('CHANGELOG.md', 'CONTRIBUTING.md', 'SECURITY.md')) {
        Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $artifactRoot
    }
    Copy-Item -Path (Join-Path $projectRoot 'docs/*.md') -Destination (Join-Path $artifactRoot 'docs')
    $licenseOutput = Join-Path $artifactRoot 'licenses'
    New-Item -ItemType Directory -Path $licenseOutput -Force | Out-Null
    $assets = Get-Content (Join-Path $projectRoot 'src/GogDisc.Launcher/obj/project.assets.json') -Raw | ConvertFrom-Json
    $runtimeCount = 0
    foreach ($framework in $assets.project.frameworks.PSObject.Properties.Value) {
        foreach ($dependency in $framework.downloadDependencies) {
            if ($dependency.name -notmatch '^Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.win-x64$') { continue }
            $runtimeVersion = ($dependency.version -replace '[\[\] ]', '').Split(',')[0]
            $packagePath = Join-Path (Join-Path $env:NUGET_PACKAGES $dependency.name.ToLowerInvariant()) $runtimeVersion
            $notices = @(Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -in @('LICENSE', 'LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT') })
            if ($notices.Count -eq 0) { throw "Missing runtime license: $packagePath" }
            foreach ($notice in $notices) {
                Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licenseOutput ("$($dependency.name)-$runtimeVersion-" + $notice.Name))
            }
            $runtimeCount++
        }
    }
    if ($runtimeCount -lt 2) { throw 'Expected both .NET and Windows Desktop runtime licenses.' }

    Write-Host "Published GOG Disc Tool to:"
    Write-Host $artifactRoot
    Write-Host "Run 'GOG Physical Disc Creator.exe' from that folder."
}
catch {
    if (Test-Path -LiteralPath $artifactRoot) { Remove-Item -LiteralPath $artifactRoot -Recurse -Force }
    throw
}
finally {
    if (Test-Path -LiteralPath $packagerTemp) { Remove-Item -LiteralPath $packagerTemp -Recurse -Force }
}
