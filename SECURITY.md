# Security policy

## Supported versions
Only the latest release gets security fixes. The N-sight script downloads the latest release unless it is pinned with
`-Version`, so update a pinned version when a fix is released.

## Reporting a vulnerability
Please report it privately: on GitHub, **Security > Report a vulnerability** on this repository. Don't open a public
issue. Say what is affected (the app, the N-sight script, the reports), how to reproduce it, and the version.

You should get a reply within a week. Fixes are released as a new version, with the issue described in the release
notes once the release is out.

## How Unveil is meant to run
- The app runs as a standard user and only reads; it asks for administrator rights (UAC) only when the technician
  clicks **Restart as administrator**. It connects to the internet only when the technician clicks **Check for updates**, which downloads the latest
  release from this repository's GitHub releases and uses it only if it matches the release's `SHA256SUMS.txt` (see
  the README's Privacy section).
- From N-able N-sight it runs as Local System through `rmm/Unveil-NSight.ps1`. The script keeps `C:\ProgramData\Unveil`
  usable by SYSTEM and Administrators only (since 0.9.4), checks `Unveil.exe` against the release's `SHA256SUMS.txt`
  before every run, and never runs an exe that does not match.
- Releases are built from this repository by `build/publish.ps1`; each release lists the SHA-256 of every file. The
  exes are not code-signed yet.
