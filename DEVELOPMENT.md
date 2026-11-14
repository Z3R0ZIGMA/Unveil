# Developing Unveil

## Requirements
- Windows 10 22H2 or Windows 11
- .NET 10 SDK
- Python with PySide6, only to redraw the app icon (`assets/make_icon.py`)

## Build, test, run
```
dotnet build Unveil.sln
dotnet test Unveil.sln
dotnet run --project src/Unveil.App
```
`dotnet test` builds only the Core library and its tests. Run `dotnet build src/Unveil.App` before taking screenshots
of the app.

## Release
```
powershell -ExecutionPolicy Bypass -File build\publish.ps1
```
This builds `dist\Unveil-<version>\`:
- `win-x64\` and `win-arm64\`, each with `Unveil.exe` and a `README.txt`
- a zip of each, for the GitHub release
- `SHA256SUMS.txt`

The version comes from `Directory.Build.props`. The publish settings (self-contained, single file, both runtimes) live
in `src/Unveil.App/Unveil.App.csproj`, never in a `.pubxml`.

## Checking the UI without clicking around
`Unveil.exe --screenshot <file.png>` renders the whole dashboard, with real data, to a PNG and exits. Add any of:
- `--simulate-discharge`: pretend the laptop is unplugged and drawing 7-10 W
- `--test-seconds N`: run a short full test first (no unplug wait)
- `--run-energy`: run the energy diagnostics first (needs administrator)
- `--show-tab all`: save one image per power hogs tab (`file-tab0.png` ...)

The window title bar is not in the screenshot.

## Layout
```
src/Unveil.Core/          data collection, parsing, verdict and reports (no UI code)
  Battery/                WMI reader, battery report parser, merge, health, usage summary, scrubber
  SystemInfo/             laptop make/model/serial; power settings reader and advisor
  Verdict/                verdict engine and capacity trend check
  Power/                  drain test, process sampler, Power Impact, app names, Windows process names, known offenders
  Admin/                  energy history (SRUM), sleep study and energy diagnostics; admin collector; sample export and scrubber
  Config/                 unveil.json model, loader and validation
  Reports/                JSON export, HTML report, ticket summary, comparison with an earlier report
src/Unveil.App/           Unveil.exe: WPF app (MVVM without libraries), Themes/Theme.xaml, Controls/ (gauge, charts)
tests/Unveil.Core.Tests/  xUnit tests and synthetic fixtures
samples/                  real, scrubbed outputs from test laptops
build/publish.ps1         release build
assets/                   the app icon and the script that draws it
```

## Ground rules
- **No network calls and no telemetry**, ever. See [PRIVACY.md](PRIVACY.md), and keep it accurate when you change
  what Unveil reads or writes.
- **Read only.** Unveil never changes settings on the laptop.
- **Degrade gracefully.** Every collector catches its own failures, writes them to the `CollectionLog` and carries on.
  A desktop, a VM or missing battery data must never crash the app. `powercfg` calls always have a timeout.
- **Standard user first.** Administrator-only data is optional and is reported as skipped, not as an error.
- **Parse XML/CSV only.** Never parse `powercfg`'s console text: it is translated on non-English Windows.
- **Never assume a field is present.** Battery data differs a lot between Dell, HP, Lenovo, ASUS and others.
- **Runtime libraries:** `System.Management` (WMI) and `System.Diagnostics.PerformanceCounter` only.
- **Theme:** dark only. Every colour is a resource in `Themes/Theme.xaml`, and colour is never the only sign of a
  status.
- Don't name a namespace `Unveil.Core.System`: it hides the `System` namespace.

## Samples and personal data
`samples/` holds real outputs from test laptops, used by the tests. Never commit serial numbers, computer names, user
names, account SIDs, Wi-Fi names or MAC addresses. Save new samples with the app:
**Details > Data notes > Save samples** writes scrubbed copies (`SampleExport` + `SampleScrubber`).
The `SamplesAreScrubbed` test checks every sample; never weaken it.

## Testing elevated
`powercfg /srumutil`, `/sleepstudy` and `/energy` need administrator rights. Start the app elevated with
`Start-Process <path>\Unveil.exe -Verb RunAs`.

## Where the rules came from
[plan.md](plan.md) is the original spec. "Changes since the plan" at its top lists what changed during the build.
[CLAUDE.md](CLAUDE.md) holds the detailed rules learned in each phase (battery data quirks, verdict thresholds, Power
Impact, SRUM formats, power settings sources).
