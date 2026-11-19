<#
.SYNOPSIS
  Runs the Unveil battery check from N-able N-sight RMM (Script Check or Automated Task).

.DESCRIPTION
  N-sight runs this script as Local System with no screen. It:
    1. downloads Unveil.exe from the Unveil GitHub release into C:\ProgramData\Unveil (only when it has changed),
       and checks it against the release's SHA256SUMS.txt;
    2. runs "Unveil.exe --rmm", which checks the battery, the power settings, the energy history and sleep study,
       and runs the live test (watts only when the laptop is on battery; otherwise app activity only);
    3. saves the full HTML and JSON report in C:\ProgramData\Unveil\Reports (the newest 30 runs are kept);
    4. prints one line for the N-sight dashboard (N-sight shows the first 255 characters) and exits with:
         0     pass: battery healthy, inconclusive, or no battery
         1001  software or settings issue
         1002  battery worn
         1003  replace battery
         1004  battery worn or failing AND software drain
         1010  Unveil failed (the line says why)
         1011  Unveil could not be downloaded and no copy is on the laptop
         1012  Unveil was blocked, or did not finish in time

  Unveil itself makes no network connections; only this script downloads, from the Unveil releases page.
  If the laptop cannot reach github.com, copy Unveil.exe into C:\ProgramData\Unveil (for example with
  Remote Background > File transfer) and run this script with -NoDownload.

.PARAMETER TestSeconds
  Length of the live test in seconds (0 skips it). Default 60.

.PARAMETER EnergyDiagnostics
  Also run Windows' 60-second energy diagnostics (adds about a minute).

.PARAMETER Version
  The Unveil release to use, e.g. 0.9.2. Default "latest". Pin a version to update clients only when you choose.

.PARAMETER NoDownload
  Use the Unveil.exe already in the folder; never download.

.PARAMETER Keep
  How many runs' reports to keep. Default 30.

.PARAMETER ReportOnly
  Always pass (exit 0) when Unveil ran, whatever it found; the line still gives the verdict. For Automated Tasks,
  where you only want the result. Real failures (1010-1012) still fail. Leave it off for Script Checks, so a battery
  or drain problem raises an alert.

.PARAMETER Folder
  Where Unveil.exe and the Reports folder live. Default C:\ProgramData\Unveil.

.EXAMPLE
  N-sight "Command Line" box:  -TestSeconds 60 -EnergyDiagnostics -Version 0.9.2
  Automated Task that should show green when it ran:  -ReportOnly
#>
param(
    [int]$TestSeconds = 60,
    [switch]$EnergyDiagnostics,
    [string]$Version = 'latest',
    [switch]$NoDownload,
    [int]$Keep = 30,
    [switch]$ReportOnly,
    [string]$Folder = (Join-Path $env:ProgramData 'Unveil')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'   # the progress bar makes downloads very slow in Windows PowerShell
$Repo = 'Z3R0ZIGMA/Unveil'

function Finish([int]$Code, [string]$Text) {
    if ($Text.Length -gt 255) { $Text = $Text.Substring(0, 255) }
    Write-Host $Text
    exit $Code
}

$exe = Join-Path $Folder 'Unveil.exe'
$reports = Join-Path $Folder 'Reports'
$resultFile = Join-Path $Folder 'last-result.txt'

try {
    New-Item -ItemType Directory -Force -Path $Folder | Out-Null
} catch {
    Finish 1010 "Unveil: cannot create $Folder ($($_.Exception.Message))"
}

# ------------------------------------------------------------------ 1. get Unveil.exe
if (-not $NoDownload) {
    $arch = $env:PROCESSOR_ARCHITEW6432
    if (-not $arch) { $arch = $env:PROCESSOR_ARCHITECTURE }
    $asset = if ($arch -eq 'ARM64') { 'Unveil-arm64.exe' } else { 'Unveil.exe' }
    $base = if ($Version -eq 'latest') { "https://github.com/$Repo/releases/latest/download" }
            else { "https://github.com/$Repo/releases/download/v$($Version.TrimStart('v'))" }

    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $sumsFile = Join-Path $Folder 'SHA256SUMS.txt'
        Invoke-WebRequest -UseBasicParsing -Uri "$base/SHA256SUMS.txt" -OutFile $sumsFile -TimeoutSec 60
        $line = Get-Content $sumsFile | Where-Object { $_ -match "^([0-9a-fA-F]{64})\s+\*?$([regex]::Escape($asset))\s*$" } | Select-Object -First 1
        if (-not $line) { throw "$asset is not listed in SHA256SUMS.txt" }
        $expected = ($line -split '\s+')[0].ToLowerInvariant()

        $current = if (Test-Path $exe) { (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant() } else { '' }
        if ($current -ne $expected) {
            $download = "$exe.download"
            Invoke-WebRequest -UseBasicParsing -Uri "$base/$asset" -OutFile $download -TimeoutSec 600
            $got = (Get-FileHash $download -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($got -ne $expected) {
                Remove-Item $download -Force -ErrorAction SilentlyContinue
                throw "the downloaded file does not match SHA256SUMS.txt"
            }
            Move-Item $download $exe -Force
        }
    } catch {
        if (-not (Test-Path $exe)) {
            Finish 1011 "Unveil could not be downloaded ($($_.Exception.Message)). Check that github.com is reachable, or copy Unveil.exe to $Folder and use -NoDownload."
        }
        # A copy is already here: use it.
    }
}

if (-not (Test-Path $exe)) {
    Finish 1011 "Unveil.exe is not in $Folder. Copy it there, or run without -NoDownload."
}

# Files downloaded by a browser carry a "from the internet" mark that makes SmartScreen check them. This copy
# was not downloaded by a browser, but remove any mark anyway.
Unblock-File -Path $exe -ErrorAction SilentlyContinue

# ------------------------------------------------------------------ 2. run it
Remove-Item $resultFile -Force -ErrorAction SilentlyContinue
$arguments = @('--rmm', '--test-seconds', $TestSeconds, '--out', "`"$reports`"", '--result', "`"$resultFile`"", '--keep', $Keep)
if ($EnergyDiagnostics) { $arguments += '--energy' }

try {
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -WindowStyle Hidden
    $null = $process.Handle   # needed for ExitCode to be available later in Windows PowerShell
} catch {
    Finish 1012 "Windows or the antivirus blocked Unveil.exe ($($_.Exception.Message)). Allow C:\ProgramData\Unveil in the antivirus policy."
}

$limit = [Math]::Max(0, $TestSeconds) + 240
if ($EnergyDiagnostics) { $limit += 90 }
if (-not $process.WaitForExit($limit * 1000)) {
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    Finish 1012 "Unveil did not finish within $limit seconds and was stopped."
}

# ------------------------------------------------------------------ 3. report back to N-sight
$code = $process.ExitCode
$text = if (Test-Path $resultFile) { (Get-Content $resultFile -Raw).Trim() } else { "Unveil ended with code $code without a result." }

# -ReportOnly: a finding (1001-1004) is a result, not a failure of the task.
if ($ReportOnly -and $code -ge 1001 -and $code -le 1004) { $code = 0 }
Finish $code $text
