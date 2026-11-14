# Privacy

Unveil is a diagnostic tool that a technician runs on a laptop. It reads information about the battery and the
apps that use power, shows it on screen, and writes files only where the technician asks it to.

## The short version
- **Unveil makes no network connections.** It has no telemetry, analytics, crash reporting, update checks, accounts
  or cloud features. Nothing is sent to the developer or anyone else.
- **Everything stays on the laptop** unless the technician saves a report or copies a summary and takes it elsewhere.
- **Unveil changes nothing.** It only reads settings; it never changes power settings, startup apps, drivers or the
  registry.
- **Administrator access is optional.** Unveil runs as a standard user. It asks Windows for administrator access (a UAC
  prompt) only when the technician clicks "Restart as administrator".

## What Unveil reads
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

## What Unveil writes, and where
- **`unveil.json`** next to `Unveil.exe`: the thresholds only (no personal data). It is created on first run.
- **Temporary files:** `powercfg` writes its reports to a private folder under `%TEMP%\Unveil`. Unveil deletes the folder
  as soon as it has read them.
- **Reports** (`Export report`): an HTML report and a JSON file, saved only where the technician chooses.
- **Samples** (`Details > Data notes > Save samples`): raw data, saved only where the technician chooses, with
  identifying details removed (see below).
- **Clipboard** (`Copy summary`): a short text summary, when the technician clicks the button.

## What the reports contain
Exported reports (HTML and JSON) and the copied summary are about the laptop, and contain **device identifiers**:
- the laptop and battery serial numbers
- the laptop make and model, BIOS version and Windows build
- the names of apps, services and startup programs
- with administrator checks, the apps and devices Windows reported, sometimes with their file paths
- notes about data Unveil could not read (Windows' error messages)

Unveil removes Wi-Fi network names, MAC addresses, Windows account IDs (SIDs) and user folder names from what Windows
reports and from these notes (`C:\Users\REDACTED\...`). Reports do not contain the computer name, passwords or the contents of any files.

**For technicians:** treat exported reports as your client's data, handled under your own data-protection
obligations and your agreement with the client. If you save reports on the client's laptop (for example so Unveil can
compare on the next visit), tell the client and delete them when they are no longer needed.

## Comparing with an earlier report
To show "Since the last check", Unveil looks for earlier reports named `unveil-*.json` in these places:
- the folder that holds `Unveil.exe`, and up to two subfolders below it
- the Desktop
- Documents, and one subfolder below it
- Downloads

It only reads those files; it does not open other files or search anywhere else. It keeps only the file name, never
the full path.

## Save samples: what is removed
Samples help add support for new laptop models. Before anything is written, Unveil removes:
- serial numbers, the computer name and report IDs
- user names and user folders in paths, and Windows account IDs (SIDs)
- Wi-Fi network names and MAC addresses

It also leaves out energy history older than 26 hours. Review samples before sharing them anyway: they still describe
the laptop model and the apps on it.

## Questions
Unveil is open source: every line that reads or writes data can be checked in this repository. Please raise
questions or concerns as an issue on GitHub.
