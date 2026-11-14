using System.Diagnostics;
using Unveil.Cli;
using Unveil.Core.Battery;
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

        var report = ReportDocument.Create(snapshot);
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
