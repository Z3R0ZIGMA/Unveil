using System.Diagnostics;
using Unveil.Core;
using Unveil.Core.Admin;
using Unveil.Core.Battery;
using Unveil.Core.Config;
using Unveil.Core.Power;
using Unveil.Core.Reports;
using Unveil.Core.SystemInfo;
using Unveil.Core.Verdict;

namespace Unveil.CommandLine;

/// <summary>How the command line was started.</summary>
public sealed record CommandLineContext
{
    /// <summary>Unveil.Cli.exe opened from Explorer: save reports next to the exe, open the report.</summary>
    public bool StartedByDoubleClick { get; init; }

    /// <summary>Print only the 5-8 line summary unless --details is given ("Unveil.exe --cli", for RMM).</summary>
    public bool SummaryByDefault { get; init; }

    /// <summary>Folder with unveil.json (normally the exe's folder).</summary>
    public string ConfigFolder { get; init; } = ConfigLoader.ExeFolder;
}

/// <summary>Command-line mode (plan.md section 7), shared by Unveil.Cli.exe and "Unveil.exe --cli".</summary>
public static class CommandLineRunner
{
    public static int Run(string[] args, CommandLineContext context)
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

            var configSource = options.ConfigFile is { } file ? ConfigLoader.LoadFile(file) : ConfigLoader.Load(context.ConfigFolder);
            var config = configSource.Config;
            config.ApplyKnownOffenders();
            var summaryOnly = options.SummaryOnly ?? context.SummaryByDefault;

            // A double-click cannot pass options: save the report and samples next to the exe and open the report.
            if (context.StartedByDoubleClick)
            {
                options = options with
                {
                    OutFolder = options.OutFolder ?? Path.Combine(context.ConfigFolder, "reports"),
                    Json = true,
                    Html = true,
                    Open = true,
                    SaveReportFolder = options.SaveReportFolder ?? Path.Combine(context.ConfigFolder, "samples"),
                };
            }

            Console.WriteLine("Collecting battery data...");
            var snapshot = BatteryCollector.CreateDefault().Collect(new BatteryCollectionOptions
            {
                SkipWmi = options.SkipWmi,
                SkipBatteryReport = options.SkipReport,
            });

            var settings = config.ToVerdictSettings(options.MachineClass ?? config.DefaultLaptopType);
            DrainTestResult? test = null;
            if (options.Sample && snapshot.HasBattery)
            {
                test = RunDrainTest(options.SampleSeconds ?? config.Drain.SampleSeconds, quiet: summaryOnly);
            }

            AdminData? admin = null;
            if (options.AdminChecks && snapshot.HasBattery)
            {
                if (!summaryOnly)
                {
                    Console.WriteLine();
                    Console.WriteLine(AdminCollector.IsAdministrator()
                        ? $"ADMIN CHECKS: energy history, sleep study{(options.EnergySeconds > 0 ? $" and {options.EnergySeconds} s of energy diagnostics (leave the laptop idle)" : "")}..."
                        : "ADMIN CHECKS: skipped (run from an elevated command prompt to include them).");
                }

                admin = new AdminCollector().Collect(new AdminOptions
                {
                    IncludeEnergyDiagnostics = options.EnergySeconds > 0,
                    EnergyDuration = TimeSpan.FromSeconds(options.EnergySeconds),
                    KeepRaw = options.SaveReportFolder is not null,
                }, new CollectionLog());
            }

            var powerSettings = snapshot.HasBattery ? new PowerSettingsReader().Read(new CollectionLog()) : null;
            var report = ReportDocument.Create(snapshot, test, settings, admin: admin, powerSettings: powerSettings,
                thresholds: config.ToSettingsThresholds());

            if (summaryOnly)
            {
                Console.WriteLine(TextSummary.Create(report));
            }
            else
            {
                ConsoleReport.Write(Console.Out, report);
            }

            foreach (var entry in configSource.Log.Where(e => e.Level != LogLevel.Info))
            {
                Console.WriteLine($"Config: {entry.Message}");
            }

            if (options.OutFolder is not null)
            {
                WriteReports(options, report, config.ToBranding(configSource.Path is { } p ? Path.GetDirectoryName(p) : context.ConfigFolder));
            }

            if (options.SaveReportFolder is not null)
            {
                TryWrite(() => SampleWriter.Save(options.SaveReportFolder, snapshot, Console.Out, admin), options.SaveReportFolder);
            }

            Console.WriteLine($"Exit code: {report.Verdict.ExitCode}");
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

    private static DrainTestResult RunDrainTest(int seconds, bool quiet)
    {
        var live = new WmiLiveBatteryReader();
        var drainTest = new DrainTest(live, new PerfCounterActivitySource());
        var interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected;

        void Say(string line)
        {
            if (!quiet)
            {
                Console.WriteLine(line);
            }
        }

        Say("");
        Say($"DRAIN TEST ({seconds} s)");
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
                Say("  The charger is connected; measuring app activity only.");
            }
        }

        Say("  Preparing...");
        drainTest.WarmUp();
        Say("  Measuring. Leave the laptop idle: do not type or move the mouse.");

        var lastPrinted = -1;
        var progress = new SynchronousProgress<DrainTestProgress>(p =>
        {
            var elapsed = (int)p.Elapsed.TotalSeconds;
            if (elapsed / 10 != lastPrinted / 10 || p.Fraction >= 1)
            {
                lastPrinted = elapsed;
                var now = p.LatestWatts is { } w ? $"{w.ToString("0.0", Formatting.Inv)} W" : "plugged in";
                var avg = p.AverageWatts is { } a ? $", average {a.ToString("0.0", Formatting.Inv)} W" : "";
                Say($"  {elapsed,3} s: {now}{avg}");
            }
        });

        return drainTest.RunAsync(new DrainTestOptions { Duration = TimeSpan.FromSeconds(seconds) }, progress).GetAwaiter().GetResult();
    }

    private static void WriteReports(CliOptions options, ReportDocument report, ReportBranding branding)
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
                HtmlReportWriter.Write(report, htmlPath, branding);
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

    /// <summary>Results are already on screen; a read-only folder must not turn the run into a failure.</summary>
    private static void TryWrite(Action write, string folder)
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
}
