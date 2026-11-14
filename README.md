# Unveil
*See what's really draining your battery.*

A portable Windows app for IT technicians. Run it on a customer's laptop to find out, with evidence, whether the
battery is worn out or something on the laptop is draining it.

**Version 0.8.1.** All the phases in [plan.md](plan.md) are done. Code signing comes later.

- [Privacy](#privacy): what Unveil reads and writes. No network connections, no telemetry.
- [DEVELOPMENT.md](DEVELOPMENT.md): building, testing and the project layout.
- [plan.md](plan.md): the original spec, with the changes made during the build at the top.

## Requirements
- Windows 10 22H2 or Windows 11, x64 or ARM64
- Nothing to install: `Unveil.exe` is a single self-contained file. It runs as a standard user.

## Get it
Download `Unveil-<version>-win-x64.zip` (or `win-arm64`) from the
[Releases](https://github.com/Z3R0ZIGMA/Unveil/releases) page, unzip it to a USB stick and double-click `Unveil.exe`
on the laptop. Or build it yourself (see [DEVELOPMENT.md](DEVELOPMENT.md)).

Unveil is not code-signed yet, so Windows may show "Windows protected your PC". Click **More info > Run anyway**.
`SHA256SUMS.txt` in each release lets you check the download.

## Use it
- **The dashboard** opens with the verdict (healthy, software/settings issue, battery worn, replace battery, both,
  inconclusive or no battery), battery health, live power draw, recent use on battery, power settings, capacity
  history and the evidence behind the verdict.
- **Run full test:** unplug the charger (or choose "Test while plugged in") and leave the laptop idle for 60 seconds.
  Unveil measures the power draw every 2 seconds and which apps use the most power, then updates the verdict.
  Windows' own processes get plain names ("Desktop Window Manager: draws everything on the screen").
- **Restart as administrator** (one UAC prompt) adds Windows' energy use by app over recent days, the sleep study
  (drain while asleep, and what kept the laptop awake) and **Run energy diagnostics (60 s)**. All read-only.
- **Export report** saves an HTML report for the customer (printable to PDF) and the JSON data. **Copy summary**
  copies a short text summary for the ticket.
- **Since the last check:** export a report on every visit. Next time Unveil finds the latest earlier `unveil-*.json`
  for that laptop (next to Unveil.exe, on the Desktop, in Documents or in Downloads) and shows the change in health,
  capacity, cycles, verdict and power draw. A replaced battery is recognised. **Compare with a report...** picks one by
  hand.
- **Details > Data notes > Save samples** saves the raw data with serial numbers, names, account IDs and Wi-Fi details
  removed, so support for new laptop models can be added.

RMM and security agents (N-able, ConnectWise, NinjaOne, Datto, Kaseya and others) and antivirus scans are labelled when
they use a noticeable share of the power.

Unveil only reads: it never changes settings. Apply the advice yourself, with the customer's agreement.

## unveil.json
Created next to `Unveil.exe` on first run. Every value is optional. Anything missing or invalid falls back to the
default (shown under Data notes), and a broken file is never overwritten.
- `health`: the good/replace thresholds, the cycle-count rating and the capacity-drop rule
- `drain`: the default `laptopType` (thin/standard/performance), `sampleSeconds`, watt limits per laptop type, and the
  dominant-app and idle-CPU rules
- `sleep`, `settings`: the sleep-drain and power-settings thresholds
- `knownOffenders`: extra apps to flag, e.g.
  `{ "category": "Line-of-business app", "advice": "Ask the vendor.", "processes": ["lobclient.exe"] }`

## Privacy
Unveil is a diagnostic tool that a technician runs on a laptop. It reads information about the battery and the
apps that use power, shows it on screen, and writes files only where the technician asks it to.

### The short version
- **Unveil makes no network connections.** It has no telemetry, analytics, crash reporting, update checks, accounts
  or cloud features. Nothing is sent to the developer or anyone else.
- **Everything stays on the laptop** unless the technician saves a report or copies a summary and takes it elsewhere.
- **Unveil changes nothing.** It only reads settings; it never changes power settings, startup apps, drivers or the
  registry.
- **Administrator access is optional.** Unveil runs as a standard user. It asks Windows for administrator access (a UAC
  prompt) only when the technician clicks "Restart as administrator".

### What Unveil reads
All of this is read on the laptop itself, with Windows' own tools and APIs:

| Data | Where it comes from | Why |
|---|---|---|
| Battery make, name, serial number, chemistry, capacities, cycle count, charge and power draw | Windows battery data (WMI) and `powercfg /batteryreport` | Battery health and the verdict |
| Laptop make, model, serial number, BIOS version, Windows build | Windows system information (WMI) | Identifies the laptop in reports and for comparisons |
| Recent battery use (time and energy in use and asleep) and capacity history | `powercfg /batteryreport` | Runtime and capacity trend |
| Power plan and mode, screen brightness, refresh rate, battery saver, screen/sleep timeouts, startup app names, uptime, pending Windows Update restart | Windows power APIs, WMI and the registry | Settings that drain the battery |
| Names and activity (CPU, GPU, disk + network) of running apps and services during the 60-second test | Windows performance counters and the service list | Which apps use the most power |
| *Administrator only:* energy use by app over recent days, the sleep study and Windows energy diagnostics | `powercfg /srumutil`, `/sleepstudy` and `/energy` | Background drain and sleep problems |

Windows' battery report also contains the computer name. Unveil does not show it or put it in any report.

### What Unveil writes, and where
- **`unveil.json`** next to `Unveil.exe`: the thresholds only (no personal data). It is created on first run.
- **Temporary files:** `powercfg` writes its reports to a private folder under `%TEMP%\Unveil`. Unveil deletes the folder
  as soon as it has read them.
- **Reports** (`Export report`): an HTML report and a JSON file, saved only where the technician chooses.
- **Samples** (`Details > Data notes > Save samples`): raw data, saved only where the technician chooses, with
  identifying details removed (see below).
- **Clipboard** (`Copy summary`): a short text summary, when the technician clicks the button.

### What the reports contain
Exported reports (HTML and JSON) and the copied summary are about the laptop, and contain **device identifiers**:
- the laptop and battery serial numbers
- the laptop make and model, BIOS version and Windows build
- the names of apps, services and startup programs
- with administrator checks, the apps and devices Windows reported, sometimes with their file paths
- notes about data Unveil could not read (Windows' error messages)

Unveil removes Wi-Fi network names, MAC addresses, Windows account IDs (SIDs) and user folder names from what Windows
reports and from these notes (`C:\Users\REDACTED\...`). Reports do not contain the computer name, passwords or the
contents of any files.

**For technicians:** treat exported reports as your client's data, handled under your own data-protection
obligations and your agreement with the client. If you save reports on the client's laptop (for example so Unveil can
compare on the next visit), tell the client and delete them when they are no longer needed.

### Comparing with an earlier report
To show "Since the last check", Unveil looks for earlier reports named `unveil-*.json` in these places:
- the folder that holds `Unveil.exe`, and up to two subfolders below it
- the Desktop
- Documents, and one subfolder below it
- Downloads

It only reads those files; it does not open other files or search anywhere else. It keeps only the file name, never
the full path.

### Save samples: what is removed
Samples help add support for new laptop models. Before anything is written, Unveil removes:
- serial numbers, the computer name and report IDs
- user names and user folders in paths, and Windows account IDs (SIDs)
- Wi-Fi network names and MAC addresses

It also leaves out energy history older than 26 hours. Review samples before sharing them anyway: they still describe
the laptop model and the apps on it.

### Questions
Unveil is open source: every line that reads or writes data can be checked in this repository. Please raise
questions or concerns as an issue on GitHub.

## Licence
Unveil is released under the **MIT License**. Copyright (c) 2026 Z3R0. The same text is in [LICENSE](LICENSE).

```
MIT License

Copyright (c) 2026 Z3R0

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
