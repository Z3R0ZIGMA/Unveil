# Unveil — Laptop Battery Diagnostic Tool

*See what's really draining your battery.*

## Changes since the plan (decided during the build)

**Status (version 0.8.2):** all eight phases are done. Code signing is deferred.

- **Who uses it:** a technician runs `Unveil.exe` on the client's laptop. There is no RMM or command-line use.
- **Removed:** CLI mode (section 7, the `--cli` switch, exit codes, `Unveil.Cli.exe`) and report branding (company
  name/logo/footer in sections 6 and 8). The HTML report always says "Unveil".
- **Removed:** the "Find replacement" button (sections 3 and 5). Batteries are ordered from a supplier; Unveil shows the
  laptop model, battery name and any part hint, and makes no outbound connections at all.
- **Deferred:** code signing (Phase 8). The user will handle it later; until then the release README explains the
  SmartScreen warning.
- **Kept from section 8:** `unveil.json` for thresholds and extra known offenders.
- **Comparison (Phase 8):** besides choosing a previous JSON by hand, Unveil finds the latest earlier report for the
  same laptop on its own, and reports a battery replaced since then. It matches on the laptop serial when the battery
  has no serial.
- **Added:** "Save samples" in the app (scrubbed raw data to improve support for new laptop models), power settings
  checks with an idle-apps rule, and RMM/security agents as known battery drainers.
- **Verdict:** a battery at 60-79% gets a softer "Battery worn" verdict; the 10%-in-30-days drop must be sustained over
  the last 3 history entries; "Inconclusive" when health cannot be measured.
- **App names (after Phase 8):** Windows' own processes get plain names and a one-line role ("Desktop Window Manager:
  draws everything on the screen"). Apps with the same name are merged into one row. Service names are read from the
  Windows service list, so they also work as a standard user.
- **Privacy:** The Privacy section of [README.md](README.md#privacy) describes exactly what is read and written. Reports leave out the computer name,
  Wi-Fi network names, MAC addresses, account SIDs and user folder names (section 2 asked only for no telemetry and
  no network calls).
- **Docs:** user guide in README.md; building, testing and ground rules in [DEVELOPMENT.md](DEVELOPMENT.md).
- **Section 1 and 7:** "push it through RMM" no longer applies; Unveil is always run interactively.

## 1. Purpose

When a customer says "my battery doesn't last anymore", a technician needs to answer one question quickly and with evidence:

> **Is the battery worn out (replace it), or is something on the machine draining it (fix the software/settings)?**

Unveil is a portable Windows tool. A tech can run it from a USB stick or push it through RMM. In under two minutes it collects battery health, identity, live power draw and the processes using the most power. It gives a clear verdict and produces a report the tech can hand to the customer.

## 2. Target platform and stack

- **OS:** Windows 10 22H2 and Windows 11 (x64 and ARM64)
- **Language:** C# / .NET 10
- **UI:** WPF, **dark mode only**, single window, dashboard style (see section 5.1 for the colour scheme)
- **Distribution:** self-contained, single-file `Unveil.exe`. No installer, no .NET install required.
- **Libraries:** `System.Management` (WMI) and `System.Diagnostics.PerformanceCounter`. No third-party telemetry and no network calls.
- **Privileges:** runs as a standard user and offers "Restart as Administrator" for the advanced checks. Every feature must degrade gracefully without admin and show what was skipped.

## 3. Data to collect

### 3.1 Battery identity
| Field | Source |
|---|---|
| Manufacturer | WMI `root\wmi` → `BatteryStaticData.ManufactureName`; fallback: battery report XML |
| Battery name / model / part no. | `BatteryStaticData.DeviceName` (often contains the FRU/part number, e.g. Lenovo `5B10…`, Dell `0WRDW…`) |
| Serial number | `BatteryStaticData.SerialNumber` |
| Chemistry | `BatteryStaticData.Chemistry` (decode: LION, LiP, NiMH, etc.) |
| Manufacture date | `BatteryStaticData.ManufactureDate` or battery report |
| Laptop make / model / serial | `Win32_ComputerSystem`, `Win32_ComputerSystemProduct`, `Win32_BIOS` (needed to source a replacement) |

Note: many OEMs do not expose a true replacement part number. Show what is available (laptop model, battery name, any part hint) so the technician can order from their supplier.

### 3.2 Battery health
| Field | Source |
|---|---|
| Design capacity (mWh) | `BatteryStaticData.DesignedCapacity` |
| Full charge capacity (mWh) | `BatteryFullChargedCapacity.FullChargedCapacity` |
| **Health %** | `FullCharge / Design × 100` |
| **Wear %** | `100 − Health` |
| Cycle count | `BatteryCycleCount.CycleCount` (may be 0 or unsupported on some OEMs; show "Not reported") |
| Current charge (mWh and %) | `BatteryStatus.RemainingCapacity` |
| Voltage | `BatteryStatus.Voltage` |
| Charging / discharging rate (mW) | `BatteryStatus.ChargeRate` / `DischargeRate` |
| AC connected | `BatteryStatus.PowerOnline` |
| Multiple batteries | Enumerate every instance (some ThinkPads have two). Show each one plus a combined total. |

**Primary cross-check:** run `powercfg /batteryreport /xml /output <temp>\battery.xml` (no admin needed) and parse:
- Battery details (to fill gaps in WMI)
- **Capacity history** (full charge capacity over time; chart this)
- **Battery life estimates** (design vs. actual runtime)
- Recent usage (drain sessions)

If WMI and the battery report disagree, prefer the battery report and log the discrepancy.

### 3.3 Runtime and drain
- **Live discharge rate.** This works only on battery power. Prompt the tech to unplug AC, then sample `DischargeRate` every 2 s for 60 s and average the samples.
- **Estimated runtime at 100%** = FullChargeCapacity ÷ average discharge rate
- **Estimated runtime if the battery were new** = DesignCapacity ÷ average discharge rate
- Classify the drain rate against the machine class. Defaults, configurable:
  - Ultrabook idle/light: < 8 W normal, 8–15 W elevated, > 15 W high
  - Standard / gaming laptop thresholds go in config

### 3.4 Power-hungry processes
**Live (no admin):**
- Sample all processes for 30–60 s: CPU % (normalised to core count), GPU % (`GPU Engine` perf counters, summed per PID), disk I/O and network I/O.
- Compute a weighted **"Power Impact" score** per process. Show the top 15.
- Group by app (e.g. all `chrome.exe` / `msedge.exe` children combined) and expand for detail.
- Flag known offenders: browsers with many tabs, Teams, OneDrive syncing, Windows Search indexer, antivirus scans, Windows Update / `TiWorker`, crypto miners, RGB/OEM utilities.

**Historical (admin):**
- `powercfg /srumutil /output <temp>\srum.csv /csv` gives per-app energy estimates for recent days from SRUM. Show the top apps by energy use over the last 24 h and 7 days.

**Sleep / overnight drain (admin):**
- `powercfg /sleepstudy /xml` shows Modern Standby sessions with high drain and the top offenders during sleep. This is key for the complaint "it's dead in the morning".

**Energy diagnostics (admin, optional, 60 s):**
- `powercfg /energy /xml`. Surface the Errors and Warnings (USB devices not suspending, timer resolution, power plan problems).

### 3.5 System power configuration
- Active power plan / power mode (Best efficiency / Balanced / Best performance)
- Battery saver on/off and its threshold
- Screen brightness (WMI `WmiMonitorBrightness`)
- Display refresh rate (flag 120 Hz+ on battery)
- Screen and sleep timeouts on battery
- Startup apps count (registry Run keys and the Startup folder)
- Discrete GPU present and active
- Uptime (flag > 7 days without reboot)
- Pending Windows Update activity

## 4. Verdict engine

Combine everything into one plain-English verdict with a traffic-light colour.

| Health % | Label |
|---|---|
| ≥ 80% | Good |
| 60–79% | Worn |
| < 60% | **Replace** |

Additional replace triggers: cycle count above the OEM rating (default 500, configurable), Windows reporting the battery as needing service, or full charge capacity dropping more than 10% within 30 days in the capacity history.

**Drain assessment:** "Elevated" when the discharge rate is above the threshold, or one process or app accounts for more than 25% of the impact score while the machine is supposedly idle.

**Final verdict:**
1. Battery OK + drain normal → "Battery healthy. Runtime is in line with usage."
2. Battery OK + drain elevated → "**Software/settings issue.** Battery is fine; these apps/settings are draining it: …"
3. Battery worn/replace + drain normal → "**Replace battery.** It holds X% of its original capacity."
4. Battery worn/replace + drain elevated → "**Both.** Replace the battery AND address: …"

Every verdict lists the evidence behind it and the recommended actions (e.g. "Set power mode to Balanced", "Disable 6 startup apps", "Teams used 38% of power during the test").

## 5. User interface

Single window with:
- **Header:** laptop make/model/serial, battery name/serial, verdict badge
- **Health card:** big health % gauge, design vs. full-charge capacity, cycle count, chemistry, age
- **Live card:** charge %, AC/battery status, live watts (sparkline), estimated runtime now vs. new
- **Capacity history chart:** from the battery report
- **Power hogs table:** Live tab / Last 24h tab / Sleep tab
- **Settings card:** power plan, brightness, refresh rate, battery saver, startup apps, with warnings highlighted
- **Buttons:** Run Full Test (guided: unplug → 60 s sample → results), Restart as Admin, Export Report, Copy Summary

### 5.1 Theme (dark mode, matches the Unbound app)

Unveil must use the same dark colour scheme as the user's other app, Unbound, so the two tools look like a family. Define these as WPF resources in one `Theme.xaml` ResourceDictionary and use them everywhere (no hard-coded colours in views):

| Key | Hex | Used for |
|---|---|---|
| `Window` | `#1e1f22` | Window background |
| `Panel` | `#2b2d31` | Cards, tables, input backgrounds |
| `Border` | `#3a3d43` | Card borders, dividers, table grid lines |
| `Text` | `#e6e6e6` | Primary text |
| `Muted` | `#8b8f97` | Labels, secondary text, units |
| `Accent` | `#3b82f6` | Primary buttons, links, gauge fill, charts, selection |
| `AccentHover` | `#2563eb` | Button hover / pressed |
| `Danger` | `#ef4444` | "Replace" verdict, errors, high drain |
| `SelectionText` | `#ffffff` | Text on accent / selected rows |

Added for Unveil's traffic-light verdicts (same palette family as the colours above):

| Key | Hex | Used for |
|---|---|---|
| `Success` | `#22c55e` | "Good" health, "Battery healthy" verdict |
| `Warning` | `#f59e0b` | "Worn" health, elevated drain, settings warnings |

Rules:
- Dark mode only. No light theme and no following the Windows theme.
- Dark title bar: use `DwmSetWindowAttribute` with `DWMWA_USE_IMMERSIVE_DARK_MODE` so the window chrome matches.
- Style all standard controls (Button, DataGrid, TabControl, ScrollBar, ToolTip, ComboBox, CheckBox, ProgressBar) so no default light-grey WPF controls show through.
- Rounded corners (6 px) on cards and buttons, subtle `Border` outline, no heavy shadows.
- Font: Segoe UI (Segoe UI Variable on Windows 11); big numbers on the health gauge and watts readout in a larger semibold weight.
- Never use colour alone for status: pair each traffic-light colour with a text label or icon.
- The HTML report uses the same colours on screen, with a print stylesheet that switches to white background and dark text so it prints cleanly.
- App icon: same flat faceted style as Unbound's icon (greys `#B4B9C1` / `#868C96` / `#5C626C`, outline `#3E434B`), with the accent blue used for a battery/probe motif.

## 6. Reports and output

- **HTML report** (single self-contained file, printable to PDF) branded with a configurable company name/logo. It has a customer-friendly summary at the top and the technical detail below.
- **JSON export** of all raw data, for history and comparison
- **Copy Summary:** 5–8 line plain-text summary for pasting into a ticket (e.g. PSA/helpdesk)
- **Comparison:** load a previous JSON for the same battery serial and show the change in health % and capacity

## 7. CLI mode (for RMM / remote use)

```
Unveil.exe --cli [--sample 60] [--admin-checks] [--out C:\Temp\Unveil] [--json] [--html]
```
- No UI. Writes the report files and prints the summary to stdout.
- Exit codes: `0` healthy, `1` software drain, `2` replace battery, `3` both, `10` no battery present, `20` error.

## 8. Configuration

`unveil.json` next to the exe (created with defaults on first run):
- Health thresholds, cycle-count rating, drain thresholds, sample duration
- Company name, logo path, report footer text
- Known-offender process list

## 9. Edge cases to handle

- Desktop or VM with no battery → friendly "No battery detected" message, exit code 10
- WMI battery classes missing or empty → fall back to the battery report XML only
- Cycle count of 0 or missing → "Not reported by manufacturer"
- Design capacity of 0 (some cheap batteries) → health "Unknown", no false "Replace"
- AC connected during the drain test → prompt to unplug and allow "skip"
- Non-English Windows → parse XML outputs only, never localised text output
- powercfg commands time out or fail → log, skip the section, keep going
- ARM64 devices (Snapdragon) → verify the WMI classes and perf counters work

## 10. Project structure

```
Unveil/
  Unveil.sln
  src/Unveil.Core/        # data collection, parsing, verdict engine (no UI)
    Battery/                # WMI + battery report readers
    Power/                  # process sampler, SRUM, sleepstudy, energy parsers
    System/                 # power plan, brightness, startup apps
    Verdict/                # rules engine
    Reports/                # HTML + JSON writers
  src/Unveil.App/         # WPF UI (MVVM)
  src/Unveil.Cli/         # or a --cli switch in App
  tests/Unveil.Core.Tests/  # xUnit, with sample XML/CSV fixtures
  samples/                  # real battery/sleepstudy/srum outputs for tests
```

## 11. Build phases

1. **Core battery data.** WMI readers plus battery report XML parser, health calculation, console output. *Test on 2–3 different OEM laptops.*
2. **Verdict engine + JSON/HTML report.** With unit tests using fixture files.
3. **WPF UI.** Dashboard, health gauge, capacity history chart.
4. **Live drain test + process sampler.** Guided unplug flow, Power Impact scoring, app grouping.
5. **Admin features.** SRUM, sleep study, energy report parsing, Restart as Admin.
6. **Settings checks + recommendations.**
7. **CLI mode, config file, branding, single-file publish** (`dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`, plus `win-arm64`).
8. **Polish.** Comparison with previous reports. (Code signing is deferred: the user will handle it later.)

## 12. Definition of done

- Runs from a USB stick on a clean Windows 11 laptop with no prerequisites
- Full test completes in ≤ 2 minutes and produces a verdict
- HTML report is readable by a non-technical customer
- CLI mode works from an RMM script and returns correct exit codes
- No crashes on a desktop, VM or machine with missing battery data
- All parsing logic covered by unit tests using real sample files

## 13. Out of scope (for now)

- macOS / Linux support
- Fleet dashboard / central server (possible later: upload JSON to a shared location)
- Calibrating or controlling charge thresholds (OEM-specific)
