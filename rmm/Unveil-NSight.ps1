<#
.SYNOPSIS
  Runs the Unveil battery check from N-able N-sight RMM (Script Check or Automated Task).

.DESCRIPTION
  N-sight runs this script as Local System with no screen. It:
    0. makes sure C:\ProgramData\Unveil can only be used by SYSTEM and Administrators (a folder anyone could change
       is moved aside, unopened, and a new one made, keeping the earlier reports), so no standard user can swap files
       run as SYSTEM or read the reports;
    1. downloads Unveil.exe from the Unveil GitHub release into C:\ProgramData\Unveil (only when it has changed),
       and checks it against the release's SHA256SUMS.txt before every run; an exe that does not match never runs;
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
         1011  Unveil could not be downloaded, or the copy on the laptop does not match SHA256SUMS.txt
         1012  Unveil was blocked, or did not finish in time

  Unveil itself makes no network connections; only this script downloads, from the Unveil releases page.
  If the laptop cannot reach github.com, copy Unveil.exe into C:\ProgramData\Unveil (for example with
  Remote Background > File transfer) and run this script with -NoDownload.

.PARAMETER TestSeconds
  Length of the live test in seconds (0 skips it). Default 60.

.PARAMETER EnergyDiagnostics
  Also run Windows' 60-second energy diagnostics (adds about a minute).

.PARAMETER Version
  The Unveil release to use, e.g. 0.9.3. Default "latest". Pin a version to update clients only when you choose.

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
  N-sight "Command Line" box:  -TestSeconds 60 -EnergyDiagnostics -Version 0.9.3
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
$sumsFile = Join-Path $Folder 'SHA256SUMS.txt'

# ------------------------------------------------------------------ 0. a folder only SYSTEM and Administrators can use
# N-sight runs this as Local System. C:\ProgramData lets every user create folders and files, so a standard user could
# create the folder first, or drop a file (a DLL, a fake Unveil.exe) next to the exe and have it run as SYSTEM. So the
# folder must belong to SYSTEM/Administrators with no other access (that also keeps the reports private). A folder that
# is not like that is moved aside unopened (it may hold links that point elsewhere) and a new one is made; only plain
# earlier report files are copied over, so "Since the last check" keeps working.
$Trusted = @('S-1-5-18', 'S-1-5-32-544')   # Local System, Administrators

function Test-Elevated {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return ([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-SecureFolder([string]$Path) {
    try {
        $item = Get-Item -LiteralPath $Path -Force
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { return $false }   # a junction or link
        $acl = Get-Acl -LiteralPath $Path
        if (-not $acl.AreAccessRulesProtected) { return $false }                        # inherits ProgramData's access
        if ($Trusted -notcontains $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value) { return $false }
        foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
            if ($Trusted -notcontains $rule.IdentityReference.Value) { return $false }
        }
        return $true
    } catch {
        return $false
    }
}

function New-SecureFolder([string]$Path) {
    $security = New-Object Security.AccessControl.DirectorySecurity
    $security.SetAccessRuleProtection($true, $false)
    foreach ($sid in $Trusted) {
        $rule = New-Object Security.AccessControl.FileSystemAccessRule(
            (New-Object Security.Principal.SecurityIdentifier $sid), 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow')
        $security.AddAccessRule($rule)
    }
    # Created with its permissions in one step, so nobody can add a file in between.
    if ($PSVersionTable.PSVersion.Major -le 5) {
        [void][IO.Directory]::CreateDirectory($Path, $security)                              # Windows PowerShell
    } else {
        [void][IO.FileSystemAclExtensions]::Create((New-Object IO.DirectoryInfo $Path), $security)   # PowerShell 7
    }
}

# Copies earlier reports out of a folder that was moved aside: only plain unveil-*.json/.html files (no links, no
# subfolders), as new files in the new folder, so nothing from the old folder keeps its old permissions.
function Copy-EarlierReports([string]$From, [string]$To) {
    try {
        $old = Get-Item -LiteralPath $From -Force -ErrorAction Stop
        if ($old.Attributes -band [IO.FileAttributes]::ReparsePoint) { return }
        New-Item -ItemType Directory -Force -Path $To | Out-Null
        Get-ChildItem -LiteralPath $From -File -Force |
            Where-Object { $_.Name -match '^unveil-[\w.-]+\.(json|html)$' -and -not ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -and $_.Length -lt 50MB } |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First ([Math]::Max(1, $Keep) * 2) |
            ForEach-Object {
                $target = Join-Path $To $_.Name
                [IO.File]::WriteAllBytes($target, [IO.File]::ReadAllBytes($_.FullName))
                (Get-Item -LiteralPath $target).LastWriteTimeUtc = $_.LastWriteTimeUtc
            }
    } catch {
        # Earlier reports are a convenience: carry on without them.
    }
}

$movedAside = $null
if (Test-Elevated) {
    try {
        if ((Test-Path -LiteralPath $Folder) -and -not (Test-SecureFolder $Folder)) {
            $aside = "$Folder.untrusted-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"
            [IO.Directory]::Move($Folder, $aside)
            $movedAside = $aside
        }
        if (-not (Test-Path -LiteralPath $Folder)) { New-SecureFolder $Folder }
        if (-not (Test-SecureFolder $Folder)) { throw "its permissions could not be set to SYSTEM and Administrators only" }
        if ($movedAside) {
            Copy-EarlierReports (Join-Path $movedAside 'Reports') $reports
            # Best effort: rd removes links themselves and never follows them.
            & $env:ComSpec /c rd /s /q "`"$movedAside`"" 2>$null | Out-Null
        }
    } catch {
        Finish 1010 "Unveil: cannot secure $Folder ($($_.Exception.Message))"
    }
} else {
    # Run by a standard user (a test): nothing runs with more rights than they have, so an ordinary folder is fine.
    try {
        New-Item -ItemType Directory -Force -Path $Folder | Out-Null
    } catch {
        Finish 1010 "Unveil: cannot create $Folder ($($_.Exception.Message))"
    }
}

function Get-ExpectedHash([string]$Asset) {
    if (-not (Test-Path -LiteralPath $sumsFile -PathType Leaf)) { return $null }
    $line = Get-Content -LiteralPath $sumsFile | Where-Object { $_ -match "^([0-9a-fA-F]{64})\s+\*?$([regex]::Escape($Asset))\s*$" } | Select-Object -First 1
    if (-not $line) { return $null }
    return ($line -split '\s+')[0].ToLowerInvariant()
}

$arch = $env:PROCESSOR_ARCHITEW6432
if (-not $arch) { $arch = $env:PROCESSOR_ARCHITECTURE }
$asset = if ($arch -eq 'ARM64') { 'Unveil-arm64.exe' } else { 'Unveil.exe' }

# ------------------------------------------------------------------ 1. get Unveil.exe
$downloadError = $null
if (-not $NoDownload) {
    $base = if ($Version -eq 'latest') { "https://github.com/$Repo/releases/latest/download" }
            else { "https://github.com/$Repo/releases/download/v$($Version.TrimStart('v'))" }

    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $sumsDownload = "$sumsFile.download"
        Invoke-WebRequest -UseBasicParsing -Uri "$base/SHA256SUMS.txt" -OutFile $sumsDownload -TimeoutSec 60
        Move-Item -LiteralPath $sumsDownload -Destination $sumsFile -Force
        $expected = Get-ExpectedHash $asset
        if (-not $expected) { throw "$asset is not listed in SHA256SUMS.txt" }

        $current = if (Test-Path -LiteralPath $exe) { (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant() } else { '' }
        if ($current -ne $expected) {
            $download = "$exe.download"
            Invoke-WebRequest -UseBasicParsing -Uri "$base/$asset" -OutFile $download -TimeoutSec 600
            $got = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($got -ne $expected) {
                Remove-Item -LiteralPath $download -Force -ErrorAction SilentlyContinue
                throw "the downloaded file does not match SHA256SUMS.txt"
            }
            Move-Item -LiteralPath $download -Destination $exe -Force
        }
    } catch {
        # A copy from an earlier run may still be used, but only if it matches the checksums saved with it (below).
        $downloadError = $_.Exception.Message
    }
}

if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    if ($downloadError) {
        Finish 1011 "Unveil could not be downloaded ($downloadError). Check that github.com is reachable, or copy Unveil.exe to $Folder and use -NoDownload."
    }
    if ($movedAside) {
        Finish 1011 "$Folder could be changed by any user, so it was replaced by a locked-down one. Copy Unveil.exe into it again, or run without -NoDownload."
    }
    Finish 1011 "Unveil.exe is not in $Folder. Copy it there, or run without -NoDownload."
}

# Check the exe on every run, not only after a download. With -NoDownload and no checksum file (an exe copied in by
# hand), the folder's permissions are what protect it: only SYSTEM and Administrators can put a file there.
$expected = Get-ExpectedHash $asset
if ($expected) {
    if ((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) {
        Finish 1011 "Unveil.exe in $Folder does not match SHA256SUMS.txt, so it was not run.$(if ($downloadError) { " The download failed: $downloadError" })"
    }
} elseif (-not $NoDownload) {
    Finish 1011 "Unveil could not be checked: no SHA256SUMS.txt$(if ($downloadError) { " ($downloadError)" }). Check that github.com is reachable."
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
