# Unveil
*See what's really draining your battery.*

Portable Windows tool that diagnoses laptop battery health and power-hungry apps.

See [plan.md](plan.md) for the full spec. **Current status: Phase 7** (version 0.7.0): desktop app, RMM command line, config file and report branding.

## Requirements
- Windows 10 22H2 or Windows 11 (x64 or ARM64)
- To build: .NET 10 SDK. To run a release: nothing (it is self-contained).

## Get a release
```
powershell -ExecutionPolicy Bypass -File build\publish.ps1
```
Builds `dist\Unveil-<version>\win-x64\` and `win-arm64\`, each with `Unveil.exe`, `Unveil.Cli.exe`, `unveil.json` and a
README, plus `SHA256SUMS.txt`. Copy the folder to a USB stick and run it on any Windows 10/11 laptop.

## Use it
- **App:** double-click `Unveil.exe`.
  - **Run full test:** unplug the charger (or choose "Test while plugged in") and leave the laptop idle for 60 seconds.
    Unveil measures the power draw every 2 s and which apps used the most power, then updates the verdict.
  - **Restart as administrator** (one UAC prompt) adds Windows' energy history (energy use by app over recent days), the
    sleep study (drain while asleep and what kept the laptop awake) and **Run energy diagnostics (60 s)**. All read-only.
- **RMM scripts:** `Unveil.exe --cli` prints the 5-8 line summary and returns the exit code:
  ```
  Unveil.exe --cli --sample 60 --admin-checks --out C:\Temp\Unveil --json --html
  ```
- **Command Prompt / PowerShell by hand:** `Unveil.Cli.exe` takes the same options, shows the full details and waits.
  (A window app cannot hold a Command Prompt open, which is why both exes ship.) Double-clicking `Unveil.Cli.exe` saves the
  report to a `reports` folder and scrubbed samples to a `samples` folder next to the exe, and opens the report.

Exit codes: `0` healthy, `1` software drain, `2` battery worn/replace, `3` both, `4` inconclusive, `10` no battery, `20` error.

| Option | Purpose |
|---|---|
| `--sample [seconds]` | Run the drain test (length from unveil.json, default 60 s): power draw on battery plus the apps using the most power |
| `--class thin\|standard\|performance` | Laptop type for the drain limits (defaults 8/15, 12/20, 20/35 W) |
| `--admin-checks` | From an elevated prompt: energy use by app (SRUM), sleep study and energy diagnostics. Skipped (not an error) without admin |
| `--energy-seconds <n>` | Length of the energy diagnostics (default 60, 0 = skip) |
| `--out <folder>` | Write the report to this folder (JSON and HTML unless one is chosen) |
| `--json` / `--html` / `--open` | Write only that format / open the HTML when done |
| `--summary` / `--details` | Print only the summary / the full output |
| `--config <file>` | Use another config file instead of unveil.json next to the exe |
| `--save-report <folder>` | Save a **scrubbed** copy of the raw data (serials, names, network IDs removed) for use as test samples |
| `--skip-wmi` / `--skip-report` | Test the battery-report-only / WMI-only fallbacks |

## unveil.json
Created next to the exe on first run (a release already includes it). Every value is optional: anything missing or invalid
falls back to the default with a note, and a broken file is never overwritten.
- `branding`: `companyName`, `logoPath` (PNG/JPG/SVG up to 1 MB; relative to unveil.json), `footerText` for the HTML report
- `health`: good/replace thresholds, cycle-count rating, capacity-drop rule
- `drain`: default `laptopType` (thin/standard/performance), `sampleSeconds`, watt limits per laptop type, dominant-app and idle-CPU rules
- `sleep`, `settings`: the sleep-drain and power-settings thresholds
- `knownOffenders`: extra apps to flag, e.g. `{ "category": "Line-of-business app", "advice": "Ask the vendor.", "processes": ["lobclient.exe"] }`

## Build and test (developers)
```
dotnet build Unveil.sln
dotnet test Unveil.sln
dotnet run --project src/Unveil.App
dotnet run --project src/Unveil.Cli -- --help
```

Developer aids: `Unveil.exe --screenshot <file.png>` renders the dashboard to an image and exits;
`--simulate-discharge` pretends the laptop is unplugged (7-10 W); `--test-seconds N` runs a short full test before the screenshot;
`--run-energy` runs the energy diagnostics first (admin); `--show-tab all` saves one image per power hogs tab.

## Layout
```
src/Unveil.Core/          data collection, parsing, verdict and reports (no UI)
  Battery/                WMI reader, battery report parser, merge, health, usage summary
  SystemInfo/             laptop make/model/serial; power settings reader and advisor
  Verdict/                verdict engine and capacity trend check
  Power/                  drain test, process sampler, Power Impact scoring, known offenders
  Admin/                  SRUM, sleep study and energy diagnostics parsers, admin collector, sample scrubber
  Config/                 unveil.json model, loader and validation
  Reports/                JSON export, HTML report, copy-to-ticket summary
src/Unveil.CommandLine/   command-line mode shared by both exes (options, console output, samples)
src/Unveil.App/           Unveil.exe: WPF app (MVVM); Program.cs routes --cli to the command line
src/Unveil.Cli/           Unveil.Cli.exe: the console version
build/publish.ps1         release build into dist/
assets/                   app icon and the script that draws it (make_icon.py)
tests/Unveil.Core.Tests/  xUnit tests + synthetic fixtures
samples/                  real, scrubbed outputs from test laptops
```
