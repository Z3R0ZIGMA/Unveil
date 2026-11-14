<#
.SYNOPSIS
  Builds the release: single-file, self-contained Unveil.exe and Unveil.Cli.exe for win-x64 and win-arm64.

.DESCRIPTION
  Output: dist\Unveil-<version>\win-x64\ and dist\Unveil-<version>\win-arm64\, each with
    Unveil.exe        the app (also "Unveil.exe --cli" for RMM scripts)
    Unveil.Cli.exe    the console version, for typing commands in a Command Prompt
    unveil.json       default settings and branding (edit before copying to the USB stick)
    README.txt        how to use it
  plus SHA256SUMS.txt for the whole release.

  Publish settings (self-contained, single file) live in the .csproj files, not here.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File build\publish.ps1
#>
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$props = [xml](Get-Content (Join-Path $root "Directory.Build.props"))
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$dist = Join-Path $root "dist\Unveil-$version"

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Force $dist | Out-Null

foreach ($rid in "win-x64", "win-arm64") {
    $out = Join-Path $dist $rid
    Write-Host "Publishing $rid..."
    foreach ($project in "src\Unveil.App", "src\Unveil.Cli") {
        & dotnet publish (Join-Path $root $project) -c $Configuration -r $rid -o $out --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish $project ($rid) failed" }
    }

    # Debug symbols are embedded; drop any loose files that are not part of the release.
    Get-ChildItem $out -File | Where-Object { $_.Extension -notin ".exe" } | Remove-Item -Force

    # The default unveil.json comes from the code itself, so it always matches this version.
    if ($rid -eq "win-x64") {
        & (Join-Path $out "Unveil.Cli.exe") --skip-wmi --skip-report --summary | Out-Null
    }
    else {
        Copy-Item (Join-Path $dist "win-x64\unveil.json") $out
    }

    @"
Unveil $version - See what's really draining your battery.

Unveil.exe       Double-click to open the app. Click "Run full test" and unplug the charger.
                 "Restart as administrator" adds energy history, the sleep study and Windows energy diagnostics.
Unveil.Cli.exe   Console version for a Command Prompt:  Unveil.Cli.exe --help
RMM scripts      Unveil.exe --cli --sample 60 --admin-checks --out C:\Temp\Unveil --json --html
                 Exit codes: 0 healthy, 1 software drain, 2 battery worn/replace, 3 both,
                             4 inconclusive, 10 no battery, 20 error.
unveil.json      Settings: company name, logo and footer for the report, thresholds, laptop type,
                 test length and extra known battery-draining apps. Edit it before copying to the stick.

Nothing is sent anywhere: all data stays on the laptop.
"@ | Set-Content (Join-Path $out "README.txt") -Encoding UTF8
}

$sums = Get-ChildItem $dist -Recurse -File | Where-Object Extension -eq ".exe" | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.FullName.Substring($dist.Length + 1).Replace('\', '/')
}
$sums | Set-Content (Join-Path $dist "SHA256SUMS.txt") -Encoding ASCII

Write-Host ""
Write-Host "Release $version in $dist"
Get-ChildItem $dist -Recurse -File | ForEach-Object { "  {0,-34} {1,8:N1} MB" -f $_.FullName.Substring($dist.Length + 1), ($_.Length / 1MB) }
