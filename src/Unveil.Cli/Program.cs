using Unveil.Cli;
using Unveil.Core.Battery;

// Phase 1 test front end. Exit codes follow plan.md section 7 where they apply:
//   0  battery data collected
//   10 no battery present
//   20 error (bad arguments or unexpected failure)
const int ExitOk = 0;
const int ExitNoBattery = 10;
const int ExitError = 20;

var exitCode = Run(args);

// Started by double-click: keep the window open so the results can be read.
if (ConsoleHost.StartedByDoubleClick)
{
    ConsoleHost.WaitForKey();
}

return exitCode;

static int Run(string[] args)
{
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

        // A double-click cannot pass --save-report, so save samples next to the exe (e.g. on the USB stick).
        var saveFolder = options.SaveReportFolder;
        if (saveFolder is null && ConsoleHost.StartedByDoubleClick
            && Path.GetDirectoryName(Environment.ProcessPath) is { } exeFolder)
        {
            saveFolder = Path.Combine(exeFolder, "samples");
        }

        Console.WriteLine("Collecting battery data...");
        var snapshot = BatteryCollector.CreateDefault().Collect(new BatteryCollectionOptions
        {
            SkipWmi = options.SkipWmi,
            SkipBatteryReport = options.SkipReport,
        });

        ConsoleReport.Write(Console.Out, snapshot);

        if (saveFolder is not null)
        {
            try
            {
                SampleWriter.Save(saveFolder, snapshot, Console.Out);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Results are already on screen; a read-only USB stick must not turn this into a failure.
                Console.WriteLine();
                Console.WriteLine($"Could not save samples to {saveFolder}: {ex.Message}");
            }
        }

        return snapshot.HasBattery ? ExitOk : ExitNoBattery;
    }
#pragma warning disable CA1031 // Top-level handler: report any failure and return the error exit code.
    catch (Exception ex)
#pragma warning restore CA1031
    {
        Console.Error.WriteLine($"Unveil failed: {ex.GetType().Name}: {ex.Message}");
        return ExitError;
    }
}
