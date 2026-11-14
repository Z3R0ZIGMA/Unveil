# Unveil
*See what's really draining your battery.*

Portable Windows app for technicians: run it on a client's laptop to find out whether the battery is worn out or
something on the machine is draining it.

See [plan.md](plan.md) for the full spec (and the changes made since). **Current status: Phase 7 done** (version 0.7.0).

## Requirements
- Windows 10 22H2 or Windows 11 (x64 or ARM64)
- To build: .NET 10 SDK. To run a release: nothing (Unveil.exe is self-contained).

## Get a release
```
powershell -ExecutionPolicy Bypass -File build\publish.ps1
```
Builds `dist\Unveil-<version>\win-x64\` and `win-arm64\`, each with `Unveil.exe` and a README, plus `SHA256SUMS.txt`.
Copy the folder to a USB stick and double-click `Unveil.exe` on the client's laptop.

## Use it
- **The dashboard** opens with the verdict, battery health, live power draw, recent use on battery, power settings,
  capacity history and the evidence behind the verdict.
- **Run full test:** unplug the charger (or choose "Test while plugged in") and leave the laptop idle for 60 seconds.
  Unveil measures the power draw every 2 s and which apps used the most power, then updates the verdict.
- **Restart as administrator** (one UAC prompt) adds Windows' energy history (energy use by app over recent days), the
  sleep study (drain while asleep and what kept the laptop awake) and **Run energy diagnostics (60 s)**. All read-only.
- **Export report** saves an HTML report for the customer (printable) and the JSON data. **Copy summary** copies a short
  text summary for the ticket.
- **Details > Data notes > Save samples** exports scrubbed raw data (no serials, names, account IDs or Wi-Fi details) so
  new laptop models can be added to the test samples.

RMM and security agents (N-able, ConnectWise, NinjaOne, Datto, Kaseya and others) and antivirus scans are labelled when
they use a noticeable share of the power.

## unveil.json
Created next to Unveil.exe on first run. Every value is optional: anything missing or invalid falls back to the default
(shown under Data notes), and a broken file is never overwritten.
- `health`: good/replace thresholds, cycle-count rating, capacity-drop rule
- `drain`: default `laptopType` (thin/standard/performance), `sampleSeconds`, watt limits per laptop type, dominant-app and idle-CPU rules
- `sleep`, `settings`: the sleep-drain and power-settings thresholds
- `knownOffenders`: extra apps to flag, e.g. `{ "category": "Line-of-business app", "advice": "Ask the vendor.", "processes": ["lobclient.exe"] }`

## Build and test (developers)
```
dotnet build Unveil.sln
dotnet test Unveil.sln
dotnet run --project src/Unveil.App
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
  Admin/                  SRUM, sleep study and energy diagnostics parsers, admin collector, sample export and scrubber
  Config/                 unveil.json model, loader and validation
  Reports/                JSON export, HTML report, copy-to-ticket summary
src/Unveil.App/           Unveil.exe: WPF app (MVVM), Themes/Theme.xaml, Controls/ (gauge, chart, sparkline)
build/publish.ps1         release build into dist/
assets/                   app icon and the script that draws it (make_icon.py)
tests/Unveil.Core.Tests/  xUnit tests + synthetic fixtures
samples/                  real, scrubbed outputs from test laptops
```
