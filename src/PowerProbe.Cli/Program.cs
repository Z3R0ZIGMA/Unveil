using PowerProbe.Cli;
using PowerProbe.Core.Battery;

// Phase 1 test front end. Exit codes follow plan.md section 7 where they apply:
//   0  battery data collected
//   10 no battery present
//   20 error (bad arguments or unexpected failure)
const int ExitOk = 0;
const int ExitNoBattery = 10;
const int ExitError = 20;

try
{
    var options = CliOptions.Parse(args);
    if (options.ShowHelp)
    {
        CliOptions.PrintUsage(Console.Out);
        return ExitOk;
    }

    if (options.Error is not null)
    {
        Console.Error.WriteLine(options.Error);
        CliOptions.PrintUsage(Console.Error);
        return ExitError;
    }

    Console.WriteLine("Collecting battery data...");
    var snapshot = BatteryCollector.CreateDefault().Collect(new BatteryCollectionOptions
    {
        SkipWmi = options.SkipWmi,
        SkipBatteryReport = options.SkipReport,
    });

    ConsoleReport.Write(Console.Out, snapshot);

    if (options.SaveReportFolder is not null)
    {
        SampleWriter.Save(options.SaveReportFolder, snapshot, Console.Out);
    }

    return snapshot.HasBattery ? ExitOk : ExitNoBattery;
}
#pragma warning disable CA1031 // Top-level handler: report any failure and return the error exit code.
catch (Exception ex)
#pragma warning restore CA1031
{
    Console.Error.WriteLine($"PowerProbe failed: {ex.GetType().Name}: {ex.Message}");
    return ExitError;
}
