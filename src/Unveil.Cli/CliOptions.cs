using Unveil.Core.Verdict;

namespace Unveil.Cli;

internal sealed record CliOptions
{
    public bool ShowHelp { get; init; }

    public bool SkipWmi { get; init; }

    public bool SkipReport { get; init; }

    public string? SaveReportFolder { get; init; }

    /// <summary>Folder for the JSON/HTML report.</summary>
    public string? OutFolder { get; init; }

    public bool Json { get; init; }

    public bool Html { get; init; }

    /// <summary>Open the HTML report in the default browser afterwards.</summary>
    public bool Open { get; init; }

    /// <summary>Run the live drain test for this many seconds (null = do not run it).</summary>
    public int? SampleSeconds { get; init; }

    public MachineClass MachineClass { get; init; } = MachineClass.ThinAndLight;

    /// <summary>Run the administrator-only checks (energy history, sleep study, energy diagnostics).</summary>
    public bool AdminChecks { get; init; }

    public int EnergySeconds { get; init; } = 60;

    public string? Error { get; init; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "-h" or "--help" or "/?" or "-?":
                    options = options with { ShowHelp = true };
                    break;
                case "--skip-wmi":
                    options = options with { SkipWmi = true };
                    break;
                case "--skip-report":
                    options = options with { SkipReport = true };
                    break;
                case "--json":
                    options = options with { Json = true };
                    break;
                case "--html":
                    options = options with { Html = true };
                    break;
                case "--open":
                    options = options with { Open = true };
                    break;
                case "--sample":
                    // Optional number of seconds; plan.md uses "--sample 60".
                    var seconds = 60;
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var parsed))
                    {
                        seconds = parsed;
                        i++;
                    }

                    if (seconds is < 10 or > 600)
                    {
                        return options with { Error = "--sample must be between 10 and 600 seconds" };
                    }

                    options = options with { SampleSeconds = seconds };
                    break;
                case "--admin-checks":
                    options = options with { AdminChecks = true };
                    break;
                case "--energy-seconds":
                    if (!TryValue(args, ref i, out var energyText) || !int.TryParse(energyText, out var energySeconds) || energySeconds is < 0 or > 600)
                    {
                        return options with { Error = "--energy-seconds needs a number from 0 (skip) to 600" };
                    }

                    options = options with { EnergySeconds = energySeconds };
                    break;
                case "--class":
                    if (!TryValue(args, ref i, out var cls))
                    {
                        return options with { Error = "--class needs thin, standard or performance" };
                    }

                    MachineClass? machineClass = cls.ToLowerInvariant() switch
                    {
                        "thin" or "thinandlight" or "ultrabook" => MachineClass.ThinAndLight,
                        "standard" => MachineClass.Standard,
                        "performance" or "gaming" or "workstation" => MachineClass.Performance,
                        _ => null,
                    };
                    if (machineClass is null)
                    {
                        return options with { Error = $"Unknown --class '{cls}'. Use thin, standard or performance." };
                    }

                    options = options with { MachineClass = machineClass.Value };
                    break;
                case "--out":
                    if (!TryValue(args, ref i, out var outFolder))
                    {
                        return options with { Error = "--out needs a folder, e.g. --out C:\\Temp\\Unveil" };
                    }

                    options = options with { OutFolder = outFolder };
                    break;
                case "--save-report":
                    if (!TryValue(args, ref i, out var saveFolder))
                    {
                        return options with { Error = "--save-report needs a folder, e.g. --save-report D:\\samples" };
                    }

                    options = options with { SaveReportFolder = saveFolder };
                    break;
                default:
                    return options with { Error = $"Unknown option: {args[i]}" };
            }
        }

        // --json/--html/--open without --out write next to the current folder.
        if (options.OutFolder is null && (options.Json || options.Html || options.Open))
        {
            options = options with { OutFolder = Environment.CurrentDirectory };
        }

        // --out alone writes both formats; --open needs the HTML.
        if (options.OutFolder is not null && !options.Json && !options.Html)
        {
            options = options with { Json = true, Html = true };
        }

        if (options.Open)
        {
            options = options with { Html = true };
        }

        return options;
    }

    private static bool TryValue(string[] args, ref int i, out string value)
    {
        if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = "";
            return false;
        }

        value = args[++i];
        return true;
    }

    public static void PrintUsage(TextWriter output)
    {
        output.WriteLine("""

            Unveil.Cli - laptop battery check (test build)

            Usage:
              Unveil.Cli.exe [--sample [seconds]] [--class thin|standard|performance]
                             [--admin-checks [--energy-seconds N]]
                             [--out <folder>] [--json] [--html] [--open]
                             [--save-report <folder>] [--skip-wmi] [--skip-report]

            Options:
              --sample [seconds]      Run the live drain test (default 60 s): measures the power draw on
                                      battery and which apps use the most power. Leave the laptop idle.
              --class <type>          Laptop type for the drain limits: thin (default, 8/15 W),
                                      standard (12/20 W) or performance (20/35 W).
              --admin-checks          Needs an elevated prompt: energy use by app (SRUM), sleep study
                                      and Windows energy diagnostics (60 s). Skipped if not admin.
              --energy-seconds <n>    Length of the energy diagnostics (default 60, 0 = skip).
              --out <folder>          Write the report to this folder (JSON and HTML unless one is chosen).
              --json                  Write the JSON report (all collected data + verdict).
              --html                  Write the HTML report for the customer.
              --open                  Open the HTML report when done.
              --save-report <folder>  Save a SCRUBBED copy of the raw battery data (serial numbers and
                                      computer name removed) for use as test samples.
              --skip-wmi              Ignore WMI (tests the battery-report-only fallback).
              --skip-report           Do not run powercfg (tests the WMI-only fallback).
              -h, --help              Show this help.

            Double-clicked: saves the report to a "reports" folder and samples to a "samples" folder
            next to the exe, opens the report, and waits for a key press.

            Exit codes: 0 healthy, 1 software drain, 2 battery worn/replace, 3 both,
                        4 inconclusive, 10 no battery, 20 error.
            """);
    }
}
