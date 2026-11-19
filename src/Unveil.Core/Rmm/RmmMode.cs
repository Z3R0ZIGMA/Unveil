using System.Globalization;
using System.Text;
using Unveil.Core.Admin;
using Unveil.Core.Battery;
using Unveil.Core.Config;
using Unveil.Core.Power;
using Unveil.Core.Reports;
using Unveil.Core.SystemInfo;
using Unveil.Core.Verdict;
using static Unveil.Core.Formatting;

namespace Unveil.Core.Rmm;

/// <summary>
/// Options for <c>Unveil.exe --rmm</c>, the no-window mode the N-sight script (rmm/Unveil-NSight.ps1) runs as Local
/// System. Unknown options are reported, never guessed.
/// </summary>
public sealed record RmmOptions
{
    /// <summary>Default folder for reports: the same place on every laptop, so the tech can find them.</summary>
    public static string DefaultReportFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Unveil", "Reports");

    /// <summary>Live test length; null = unveil.json's sampleSeconds. 0 skips the test.</summary>
    public int? TestSeconds { get; init; }

    /// <summary>Also run Windows' 60-second energy diagnostics.</summary>
    public bool EnergyDiagnostics { get; init; }

    public string ReportFolder { get; init; } = DefaultReportFolder;

    /// <summary>File the one-line result is written to (the script prints it for N-sight).</summary>
    public string? ResultFile { get; init; }

    /// <summary>How many runs' reports to keep in <see cref="ReportFolder"/>.</summary>
    public int Keep { get; init; } = 30;

    public string? Error { get; init; }

    public static RmmOptions Parse(IReadOnlyList<string> args)
    {
        var options = new RmmOptions();
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            string? Value() => i + 1 < args.Count ? args[++i] : null;

            switch (arg.ToLowerInvariant())
            {
                case "--rmm":
                    break;
                case "--test-seconds":
                    options = int.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out var s) && s <= 600
                        ? options with { TestSeconds = s }
                        : options with { Error = "--test-seconds needs a number of seconds from 0 to 600." };
                    break;
                case "--energy":
                    options = options with { EnergyDiagnostics = true };
                    break;
                case "--out":
                    options = Value() is { Length: > 0 } folder ? options with { ReportFolder = folder } : options with { Error = "--out needs a folder." };
                    break;
                case "--result":
                    options = Value() is { Length: > 0 } file ? options with { ResultFile = file } : options with { Error = "--result needs a file name." };
                    break;
                case "--keep":
                    options = int.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out var k) && k >= 1
                        ? options with { Keep = k }
                        : options with { Error = "--keep needs a number of reports (1 or more)." };
                    break;
                default:
                    options = options with { Error = $"Unknown option: {arg}" };
                    break;
            }

            if (options.Error is not null)
            {
                return options;
            }
        }

        return options;
    }
}

/// <summary>
/// Exit codes for N-sight: 0 is a pass; N-able asks for codes above 1000 for a failure, so the text shows on the dashboard.
/// </summary>
public static class RmmExitCodes
{
    public const int Pass = 0;
    public const int SoftwareDrain = 1001;
    public const int BatteryWorn = 1002;
    public const int ReplaceBattery = 1003;
    public const int Both = 1004;
    public const int Error = 1010;

    /// <summary>Healthy, inconclusive and no battery pass; anything that needs the tech's attention fails.</summary>
    public static int For(VerdictKind kind) => kind switch
    {
        VerdictKind.SoftwareDrain => SoftwareDrain,
        VerdictKind.BatteryWorn => BatteryWorn,
        VerdictKind.ReplaceBattery => ReplaceBattery,
        VerdictKind.Both => Both,
        _ => Pass,
    };
}

/// <summary>The one line N-sight shows (it keeps only the first 255 characters of a script's output).</summary>
public static class RmmSummary
{
    public const int MaxLength = 255;

    public static string Line(ReportDocument doc, string? reportFileName)
    {
        var s = doc.Snapshot;
        var v = doc.Verdict;
        if (!s.HasBattery)
        {
            return Fit([$"{v.Title}: no battery found (desktop, or the battery is not detected)."]);
        }

        var parts = new List<string>();
        var health = s.Combined?.Health ?? s.Batteries[0].Health;
        var cycles = s.Batteries.Select(b => b.CycleCount).FirstOrDefault(c => c is > 0);
        parts.Add($"{v.Title}: " + (health.HealthPercent is { } h ? $"{Percent(h, "0")} health" : "health unknown") +
                  (cycles is { } c ? $", {Number(c)} cycles." : "."));

        if (doc.Drain is { } drain)
        {
            var top = drain.TopApps.FirstOrDefault();
            var app = top is null ? "" : $"; top: {top.Name} {Percent(top.SharePercent, "0")}";
            parts.Add(drain.AverageWatts is { } w
                ? $"Test: {Watts(w)} on battery{app}."
                : $"Test: plugged in, apps only{app}.");
        }

        if (doc.Admin?.Sleep is { JudgedPercentPerHour: { } rate })
        {
            parts.Add($"Sleep: {Percent(rate)}/h.");
        }

        // A report from earlier the same day with no real change says nothing: leave the space for the rest.
        if (doc.Comparison is { Match: ComparisonMatch.SameBattery, Batteries: [{ HealthChange: { } change }, ..] } comparison
            && (comparison.Days >= 1 || Math.Abs(change) >= 0.5))
        {
            parts.Add($"Since {Date(comparison.PreviousGeneratedAt)}: {ReportComparer.Signed(change)} pts.");
        }
        else if (doc.Comparison is { Match: ComparisonMatch.BatteryReplaced } replaced)
        {
            parts.Add($"Battery replaced since {Date(replaced.PreviousGeneratedAt)}.");
        }

        if (reportFileName is not null)
        {
            parts.Add($"Report: {reportFileName}");
        }

        return Fit(parts);
    }

    /// <summary>Adds parts while they fit; the first part is always kept (cut if it must be).</summary>
    private static string Fit(IReadOnlyList<string> parts)
    {
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            var next = sb.Length == 0 ? part : " " + part;
            if (sb.Length + next.Length <= MaxLength)
            {
                sb.Append(next);
            }
            else if (sb.Length == 0)
            {
                sb.Append(part[..MaxLength]);
            }
        }

        return sb.ToString();
    }
}

/// <summary>Keeps the report folder from growing forever when the check runs every day.</summary>
public static class ReportRetention
{
    /// <summary>Deletes all but the newest <paramref name="keep"/> runs (each run is an .html and a .json with the same name).</summary>
    public static int Prune(string folder, int keep, CollectionLog? log = null)
    {
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        var runs = Directory.EnumerateFiles(folder, "unveil-*.*")
            .Where(f => f.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .GroupBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Max(File.GetLastWriteTimeUtc))
            .Skip(Math.Max(1, keep))
            .ToList();

        var deleted = 0;
        foreach (var file in runs.SelectMany(g => g))
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log?.Warning("Reports", $"Could not delete an old report ({Path.GetFileName(file)}): {ex.Message}");
            }
        }

        return deleted;
    }
}

/// <summary>Runs the whole check with no window, for N-sight. Never throws: failures become <see cref="RmmExitCodes.Error"/>.</summary>
public static class RmmRunner
{
    public sealed record Outcome(int ExitCode, string Line, string? HtmlPath);

    public static Outcome Run(RmmOptions options)
    {
        var started = DateTimeOffset.Now;
        try
        {
            if (options.Error is not null)
            {
                return Finish(options, new Outcome(RmmExitCodes.Error, $"Unveil: {options.Error}", null));
            }

            var configSource = ConfigLoader.Load(ConfigLoader.ExeFolder);
            var config = configSource.Config;
            config.ApplyKnownOffenders();

            var snapshot = BatteryCollector.CreateDefault().Collect();
            var settings = config.ToVerdictSettings(config.DefaultLaptopType);
            AdminData? admin = null;
            DrainTestResult? test = null;
            PowerSettings? powerSettings = null;

            if (snapshot.HasBattery)
            {
                // Admin data first: its powercfg runs must not be measured as the laptop's drain.
                admin = new AdminCollector().Collect(new AdminOptions
                {
                    IncludeEnergyDiagnostics = options.EnergyDiagnostics,
                }, new CollectionLog());

                var seconds = options.TestSeconds ?? config.Drain.SampleSeconds;
                if (seconds > 0)
                {
                    var drainTest = DrainTest.CreateDefault();
                    drainTest.WarmUp();
                    test = drainTest.RunAsync(new DrainTestOptions { Duration = TimeSpan.FromSeconds(seconds) })
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                }

                powerSettings = new PowerSettingsReader().Read(new CollectionLog());
            }

            var report = ReportDocument.Create(snapshot, test, settings, admin: admin, powerSettings: powerSettings,
                thresholds: config.ToSettingsThresholds());

            Directory.CreateDirectory(options.ReportFolder);
            if (snapshot.HasBattery
                && PreviousReportFinder.FindLatest([(options.ReportFolder, 0)], report, started) is { } previous)
            {
                report = report with { Comparison = previous.Comparison };
            }

            var htmlPath = Path.Combine(options.ReportFolder, report.FileStem + ".html");
            HtmlReportWriter.Write(report, htmlPath);
            JsonReportWriter.Write(report, Path.ChangeExtension(htmlPath, ".json"));
            ReportRetention.Prune(options.ReportFolder, options.Keep);

            var outcome = new Outcome(RmmExitCodes.For(report.Verdict.Kind), RmmSummary.Line(report, Path.GetFileName(htmlPath)), htmlPath);
            return Finish(options, outcome);
        }
#pragma warning disable CA1031 // Top level of the no-window mode: report any failure as a result line and an exit code.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return Finish(options, new Outcome(RmmExitCodes.Error, $"Unveil failed: {ex.GetType().Name}: {Admin.SampleScrubber.Text(ex.Message, [])}", null));
        }
    }

    /// <summary>Writes the result line where the script can read it, and to standard output.</summary>
    private static Outcome Finish(RmmOptions options, Outcome outcome)
    {
        var line = outcome.Line.Length > RmmSummary.MaxLength ? outcome.Line[..RmmSummary.MaxLength] : outcome.Line;
        try
        {
            if (options.ResultFile is { } file)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
                File.WriteAllText(file, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The exit code still tells N-sight the result.
        }

        try
        {
            Console.Out.WriteLine(line);
        }
        catch (IOException)
        {
        }

        return outcome with { Line = line };
    }
}
