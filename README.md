# Unveil
*See what's really draining your battery.*

A portable Windows app for IT technicians. Run it on a customer's laptop to find out, with evidence, whether the
battery is worn out or something on the laptop is draining it.

**Version 0.8.1.** All the phases in [plan.md](plan.md) are done. Code signing comes later.

- [PRIVACY.md](PRIVACY.md): what Unveil reads and writes. No network connections, no telemetry.
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

## Licence
MIT: see [LICENSE](LICENSE).
