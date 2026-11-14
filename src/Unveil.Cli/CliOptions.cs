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
              Unveil.Cli.exe [--out <folder>] [--json] [--html] [--open]
                             [--save-report <folder>] [--skip-wmi] [--skip-report]

            Options:
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
