using System.Diagnostics;
using Unveil.Cli;
using Unveil.Core.Battery;
using Unveil.Core.Power;
using Unveil.Core.Reports;
using Unveil.Core.Verdict;

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
            return ExitCodes.Healthy;
        }

        if (options.Error is not null)
        {
            Console.Error.WriteLine(options.Error);
            CliOptions.PrintUsage(Console.Error);
            return ExitCodes.Error;
        }

        // A double-click cannot pass options: save the report and samples next to the exe and open the report.
        if (ConsoleHost.StartedByDoubleClick && Path.GetDirectoryName(Environment.ProcessPath) is { } exeFolder)
        {
            options = options with
            {
                OutFolder = options.OutFolder ?? Path.Combine(exeFolder, "reports"),
                Json = true,
                Html = true,
                Open = true,
                SaveReportFolder = options.SaveReportFolder ?? Path.Combine(exeFolder, "samples"),
            };
        }

        Console.WriteLine("Collecting battery data...");
        var snapshot = BatteryCollector.CreateDefault().Collect(new BatteryCollectionOptions
        {
            SkipWmi = options.SkipWmi,
            SkipBatteryReport = options.SkipReport,
        });

        var settings = VerdictSettings.Default.For(options.MachineClass);
        DrainTestResult? test = null;
        if (options.SampleSeconds is { } seconds && snapshot.HasBattery)
        {
            test = RunDrainTest(seconds);
        }

        var report = ReportDocument.Create(snapshot, test, settings);
        ConsoleReport.Write(Console.Out, report);

        if (options.OutFolder is not null)
        {
            WriteReports(options, report);
        }

        if (options.SaveReportFolder is not null)
        {
            TryWrite(() => SampleWriter.Save(options.SaveReportFolder, snapshot, Console.Out), options.SaveReportFolder);
        }

        return report.Verdict.ExitCode;
    }
#pragma warning disable CA1031 // Top-level handler: report any failure and return the error exit code.
    catch (Exception ex)
#pragma warning restore CA1031
    {
        Console.Error.WriteLine($"Unveil failed: {ex.GetType().Name}: {ex.Message}");
        return ExitCodes.Error;
    }
}

static DrainTestResult RunDrainTest(int seconds)
{
    var live = new WmiLiveBatteryReader();
    var drainTest = new DrainTest(live, new PerfCounterActivitySource());
    var interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;

    Console.WriteLine();
    Console.WriteLine($"DRAIN TEST ({seconds} s)");
    if (live.Read() is { IsOnBattery: false })
    {
        if (interactive)
        {
            Console.WriteLine("  The charger is connected. Unplug it now to measure the power draw.");
            Console.WriteLine("  Waiting up to 60 s for the charger to be unplugged (press Enter to test while plugged in)...");
            var waited = 0;
            while (waited < 60 && live.Read() is { IsOnBattery: false })
            {
                if (Console.KeyAvailable && Console.ReadKey(intercept: true).Key == ConsoleKey.Enter)
                {
                    break;
                }

                Thread.Sleep(1000);
                waited++;
            }
        }
        else
        {
            Console.WriteLine("  The charger is connected; measuring app activity only.");
        }
    }

    Console.WriteLine("  Preparing...");
    drainTest.WarmUp();
    Console.WriteLine("  Measuring. Leave the laptop idle: do not type or move the mouse.");

    var lastPrinted = -1;
    var progress = new SynchronousProgress<DrainTestProgress>(p =>
    {
        var elapsed = (int)p.Elapsed.TotalSeconds;
        if (elapsed / 10 != lastPrinted / 10 || p.Fraction >= 1)
        {
            lastPrinted = elapsed;
            var now = p.LatestWatts is { } w ? $"{w.ToString("0.0", Unveil.Core.Formatting.Inv)} W" : "plugged in";
            var avg = p.AverageWatts is { } a ? $", average {a.ToString("0.0", Unveil.Core.Formatting.Inv)} W" : "";
            Console.WriteLine($"  {elapsed,3} s: {now}{avg}");
        }
    });

    return drainTest.RunAsync(new DrainTestOptions { Duration = TimeSpan.FromSeconds(seconds) }, progress).GetAwaiter().GetResult();
}

static void WriteReports(CliOptions options, ReportDocument report)
{
    var folder = options.OutFolder!;
    Console.WriteLine();
    Console.WriteLine("REPORT FILES");

    string? htmlPath = null;
    TryWrite(() =>
    {
        Directory.CreateDirectory(folder);
        if (options.Json)
        {
            var jsonPath = Path.Combine(folder, report.FileStem + ".json");
            JsonReportWriter.Write(report, jsonPath);
            Console.WriteLine($"  {jsonPath}");
        }

        if (options.Html)
        {
            htmlPath = Path.Combine(folder, report.FileStem + ".html");
            HtmlReportWriter.Write(report, htmlPath);
            Console.WriteLine($"  {htmlPath}");
        }
    }, folder);

    if (options.Open && htmlPath is not null && File.Exists(htmlPath))
    {
        try
        {
            // Opens the local file in the default browser. No network involved.
            using var _ = Process.Start(new ProcessStartInfo(htmlPath) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Console.WriteLine($"  Could not open the report: {ex.Message}");
        }
    }
}

// Results are already on screen; a read-only folder must not turn the run into a failure.
static void TryWrite(Action write, string folder)
{
    try
    {
        write();
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.WriteLine($"  Could not write to {folder}: {ex.Message}");
    }
}
