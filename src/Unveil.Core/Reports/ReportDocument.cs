using System.Reflection;
using Unveil.Core.Battery;
using Unveil.Core.Power;
using Unveil.Core.Verdict;

namespace Unveil.Core.Reports;

/// <summary>Everything a report contains. This is what the JSON export holds, and what the HTML and text summary render.</summary>
public sealed record ReportDocument
{
    /// <summary>Bump when the JSON shape changes in a way that breaks comparison with older reports.</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>e.g. "Unveil 0.1.0".</summary>
    public string Generator { get; init; } = DefaultGenerator;

    public DateTimeOffset GeneratedAt { get; init; }

    public required BatterySnapshot Snapshot { get; init; }

    public required Verdict.Verdict Verdict { get; init; }

    public UsageSummary Usage { get; init; } = new();

    /// <summary>Null until the live drain test is run.</summary>
    public DrainMeasurement? Drain { get; init; }

    /// <summary>Per-app activity from the drain test; null until it is run.</summary>
    public ActivityResult? Activity { get; init; }

    /// <summary>Which drain thresholds were used, e.g. "Thin and light laptop".</summary>
    public string? DrainProfile { get; init; }

    public static ReportDocument Create(
        BatterySnapshot snapshot,
        DrainTestResult? test = null,
        VerdictSettings? settings = null,
        DateTimeOffset? generatedAt = null)
    {
        settings ??= VerdictSettings.Default;
        return new ReportDocument
        {
            GeneratedAt = generatedAt ?? DateTimeOffset.Now,
            Snapshot = snapshot,
            Verdict = VerdictEngine.Evaluate(snapshot, test?.Measurement, settings),
            Usage = UsageAnalyzer.Summarize(snapshot.Report),
            Drain = test?.Measurement,
            Activity = test?.Activity,
            DrainProfile = test is null ? null : settings.DrainProfileName,
        };
    }

    public static string DefaultGenerator { get; } =
        $"Unveil {typeof(ReportDocument).Assembly.GetName().Version?.ToString(3) ?? "0.0.0"}";

    /// <summary>Base file name for this report, e.g. "unveil-asus-expertbook-b1403cva-20261114-082926".</summary>
    public string FileStem =>
        $"unveil-{Slug(Snapshot.System.DisplayModel ?? Snapshot.System.Manufacturer)}-" +
        GeneratedAt.ToString("yyyyMMdd-HHmmss", Formatting.Inv);

    private static string Slug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var chars = value.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length > 40 ? slug[..40].TrimEnd('-') : slug;
    }
}

/// <summary>Company branding for the HTML report. Comes from unveil.json from Phase 7.</summary>
public sealed record ReportBranding
{
    /// <summary>Shown at the top of the report. Null shows just "Unveil".</summary>
    public string? CompanyName { get; init; }

    /// <summary>PNG/JPG/SVG file embedded in the report. Missing or unreadable files are skipped.</summary>
    public string? LogoPath { get; init; }

    public string? FooterText { get; init; }

    public static ReportBranding Default { get; } = new();
}
