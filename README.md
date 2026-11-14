# Unveil
*See what's really draining your battery.*

Portable Windows tool that diagnoses laptop battery health and power-hungry apps.

See [plan.md](plan.md) for the full spec. **Current status: Phase 6** (desktop app with the guided drain test, power hogs, administrator checks, and power settings checks with recommendations; plus the console tool).

## Requirements
- Windows 10 22H2 or Windows 11 (x64 or ARM64)
- To build: .NET 10 SDK. To run a published exe: nothing (it is self-contained).

## Build and test
```
dotnet build Unveil.sln
dotnet test Unveil.sln
```

## Run the desktop app
```
dotnet run --project src/Unveil.App
```

Portable single-file `Unveil.exe` (no .NET install needed on the target laptop):
```
dotnet publish src/Unveil.App -c Release -r win-x64 -o publish/app-win-x64
dotnet publish src/Unveil.App -c Release -r win-arm64 -o publish/app-win-arm64
```

**Run full test:** click it, unplug the charger (or choose "Test while plugged in"), then leave the laptop idle for 60 seconds.
Unveil measures the power draw every 2 s and which apps used the most power, then updates the verdict.

**Administrator checks:** click **Restart as administrator** (one UAC prompt). Unveil then also reads Windows' energy history
(energy use by app over the last 24 hours and the days Windows keeps), the sleep study (drain while asleep and what kept
the laptop awake) and, with **Run energy diagnostics (60 s)**, Windows' own power check. All of it is read-only.

Developer aids: `Unveil.exe --screenshot <file.png>` renders the dashboard to an image and exits;
`--simulate-discharge` pretends the laptop is unplugged (7-10 W); `--test-seconds N` runs a short full test before the screenshot;
`--run-energy` runs the energy diagnostics first (admin); `--show-tab all` saves one image per power hogs tab.

## Run the console tool
```
dotnet run --project src/Unveil.Cli
```

Make a portable single-file exe to copy to other laptops:
```
dotnet publish src/Unveil.Cli -c Release -r win-x64 -o publish/win-x64
dotnet publish src/Unveil.Cli -c Release -r win-arm64 -o publish/win-arm64
```

**Easiest way to test a laptop:** double-click `Unveil.Cli.exe`. It shows the verdict, saves the HTML and JSON report to a `reports` folder and scrubbed samples to a `samples` folder next to the exe, opens the report in your browser, and waits for a key press.

Options (when run from a command prompt):

| Option | Purpose |
|---|---|
| `--sample [seconds]` | Run the drain test (default 60 s): power draw on battery plus the apps using the most power |
| `--class thin\|standard\|performance` | Laptop type for the drain limits (8/15, 12/20 or 20/35 W) |
| `--admin-checks` | From an elevated prompt: energy use by app (SRUM), sleep study and 60 s energy diagnostics. Skipped (not an error) without admin |
| `--energy-seconds <n>` | Length of the energy diagnostics (default 60, 0 = skip) |
| `--out <folder>` | Write the report to this folder (JSON and HTML unless one is chosen) |
| `--json` / `--html` | Write only that format |
| `--open` | Open the HTML report when done |
| `--save-report <folder>` | Save a **scrubbed** copy of the raw battery data (serials and computer name removed) for use as test samples in `samples/` |
| `--skip-wmi` | Ignore WMI (tests the battery-report-only fallback) |
| `--skip-report` | Don't run powercfg (tests the WMI-only fallback) |

Exit codes: `0` healthy, `1` software drain, `2` battery worn/replace, `3` both, `4` inconclusive, `10` no battery, `20` error.

## Layout
```
src/Unveil.Core/          data collection, parsing, verdict and reports (no UI)
  Battery/                WMI reader, battery report parser, merge, health, usage summary
  SystemInfo/             laptop make/model/serial; power settings reader and advisor
  Verdict/                verdict engine and capacity trend check
  Power/                  drain test, process sampler, Power Impact scoring, known offenders
  Admin/                  SRUM, sleep study and energy diagnostics parsers, admin collector, sample scrubber
  Reports/                JSON export, HTML report, copy-to-ticket summary
src/Unveil.App/           WPF desktop app (MVVM): Themes/Theme.xaml, Controls/ (gauge, chart, sparkline)
src/Unveil.Cli/           console front end
assets/                   app icon and the script that draws it (make_icon.py)
tests/Unveil.Core.Tests/  xUnit tests + synthetic fixtures
samples/                  real, scrubbed outputs from test laptops
```
