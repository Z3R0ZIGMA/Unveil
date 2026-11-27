# Contributing to Unveil

Thanks for helping. Unveil is a small tool kept by one person, so please open an issue before starting on a big
change.

- **Bugs:** use the bug report form under Issues. Don't attach exported reports: they contain serial numbers.
- **Security problems:** report them privately (see [SECURITY.md](SECURITY.md)), never as an issue.
- **New laptop models:** samples help most. In the app, **Details > Data notes > Save samples** saves the raw
  data without serial numbers, names, account IDs or Wi-Fi details.
- **Code:** [DEVELOPMENT.md](DEVELOPMENT.md) explains building, testing, the layout and the ground rules. The ones
  that matter most:
  - no network calls and no telemetry in Unveil, and it never changes settings on the laptop
  - no personal or customer data in the repository
  - every collector handles its own failures; a missing field never crashes the app
  - `dotnet build` and `dotnet test` must pass (warnings are errors)

By contributing you agree that your work is released under the [MIT licence](LICENSE).
