# Unveil
*See what's really draining your battery.*

Portable Windows tool that diagnoses laptop battery health and power-hungry apps.

See [plan.md](plan.md) for the full spec. **Current status: Phase 1** (battery data collection, console output).

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

Options:

| Option | Purpose |
|---|---|
| `--save-report <folder>` | Also save a **scrubbed** copy of the battery report XML and WMI values (serials and computer name removed) for use as test samples in `samples/` |
| `--skip-wmi` | Ignore WMI (tests the battery-report-only fallback) |
| `--skip-report` | Don't run powercfg (tests the WMI-only fallback) |

Exit codes: `0` OK, `10` no battery detected, `20` error.

## Layout
```
src/Unveil.Core/          data collection and parsing (no UI)
  Battery/                WMI reader, battery report parser, merge + health calculation
  SystemInfo/             laptop make/model/serial
src/Unveil.Cli/           console front end
tests/Unveil.Core.Tests/  xUnit tests + synthetic fixtures
samples/                  real, scrubbed outputs from test laptops
```
