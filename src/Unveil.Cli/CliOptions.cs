namespace Unveil.Cli;

internal sealed record CliOptions
{
    public bool ShowHelp { get; init; }

    public bool SkipWmi { get; init; }

    public bool SkipReport { get; init; }

    public string? SaveReportFolder { get; init; }

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
                case "--save-report":
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        return options with { Error = "--save-report needs a folder, e.g. --save-report D:\\samples" };
                    }

                    options = options with { SaveReportFolder = args[++i] };
                    break;
                default:
                    return options with { Error = $"Unknown option: {args[i]}" };
            }
        }

        return options;
    }

    public static void PrintUsage(TextWriter output)
    {
        output.WriteLine("""

            Unveil.Cli - battery data (Phase 1 test build)

            Usage:
              Unveil.Cli.exe [--save-report <folder>] [--skip-wmi] [--skip-report]

            Options:
              --save-report <folder>  Also save a SCRUBBED copy of the battery report XML and the raw
                                      WMI values (serial numbers and computer name removed) for use
                                      as test samples.
              --skip-wmi              Ignore WMI (tests the battery-report-only fallback).
              --skip-report           Do not run powercfg (tests the WMI-only fallback).
              -h, --help              Show this help.

            Exit codes: 0 = OK, 10 = no battery detected, 20 = error.
            """);
    }
}
