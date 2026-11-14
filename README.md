# Unveil
*See what's really draining your battery.*

Portable Windows tool that diagnoses laptop battery health and power-hungry apps.

See [plan.md](plan.md) for the full spec. **Current status: Phase 2** (battery data, verdict engine, JSON/HTML report; console front end).

## Requirements
- Windows 10 22H2 or Windows 11 (x64 or ARM64)
- To build: .NET 10 SDK. To run a published exe: nothing (it is self-contained).

## Build and test
```
dotnet build Unveil.sln
dotnet test Unveil.sln
```

## Run (Phase 1 console)
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
  SystemInfo/             laptop make/model/serial
  Verdict/                verdict engine and capacity trend check
  Reports/                JSON export, HTML report, copy-to-ticket summary
src/Unveil.Cli/           console front end
tests/Unveil.Core.Tests/  xUnit tests + synthetic fixtures
samples/                  real, scrubbed outputs from test laptops
```
