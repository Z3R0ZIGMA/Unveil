<#
.SYNOPSIS
  Builds the release: a single-file, self-contained Unveil.exe for win-x64 and win-arm64.

.DESCRIPTION
  Output: dist\Unveil-<version>\win-x64\ and dist\Unveil-<version>\win-arm64\, each with
    Unveil.exe    the app (no .NET install needed on the laptop)
    README.txt    how to use it
  a zip of each folder (Unveil-<version>-<rid>.zip, for the GitHub release) and SHA256SUMS.txt. unveil.json (thresholds) is created next to Unveil.exe on first run.

  Publish settings (self-contained, single file) live in the .csproj, not here.

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
    & dotnet publish (Join-Path $root "src\Unveil.App") -c $Configuration -r $rid -o $out --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish ($rid) failed" }

    # Debug symbols are embedded; drop any loose files that are not part of the release.
    Get-ChildItem $out -File | Where-Object { $_.Extension -ne ".exe" } | Remove-Item -Force

    @"
Unveil $version - See what's really draining your battery.

Double-click Unveil.exe. Nothing to install; all data stays on the laptop.

- Run full test: unplug the charger and leave the laptop idle for 60 seconds. Unveil measures the power
  draw and which apps use the most power, then updates the verdict.
- Restart as administrator: adds energy use by app over recent days, the sleep study and Windows energy
  diagnostics.
- Export report: saves an HTML report for the customer (and the JSON data).
- Copy summary: a short text summary for the ticket.
- Export a report on every visit: next time Unveil finds the earlier .json (next to Unveil.exe, on the
  Desktop, in Documents or in Downloads) and shows how the battery has changed since.
- Details > Data notes > Save samples: scrubbed raw data (no serials, names or network IDs) to improve
  support for new laptop models.

unveil.json is created next to Unveil.exe on first run. Edit it to change the thresholds (health, power
draw limits per laptop type, sleep, settings checks) or to flag extra battery-draining apps.

Privacy: Unveil makes no network connections and sends nothing anywhere. It only reads; it never changes
settings. Exported reports contain the laptop and battery serial numbers: treat them as client data.
Details: https://github.com/Z3R0ZIGMA/Unveil/blob/main/README.md#privacy

If Windows shows "Windows protected your PC", click More info > Run anyway (the app is not code-signed yet).
"@ | Set-Content (Join-Path $out "README.txt") -Encoding UTF8
}

# One zip per architecture, for the GitHub release.
foreach ($rid in "win-x64", "win-arm64") {
    Compress-Archive -Path (Join-Path $dist "$rid\*") -DestinationPath (Join-Path $dist "Unveil-$version-$rid.zip") -Force
}

$sums = Get-ChildItem $dist -Recurse -File | Where-Object { $_.Extension -in ".exe", ".zip" } | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.FullName.Substring($dist.Length + 1).Replace('\', '/')
}
$sums | Set-Content (Join-Path $dist "SHA256SUMS.txt") -Encoding ASCII

Write-Host ""
Write-Host "Release $version in $dist"
Get-ChildItem $dist -Recurse -File | ForEach-Object { "  {0,-26} {1,8:N1} MB" -f $_.FullName.Substring($dist.Length + 1), ($_.Length / 1MB) }
