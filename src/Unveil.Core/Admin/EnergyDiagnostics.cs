using System.Xml;
using System.Xml.Linq;
using Unveil.Core.Battery;

namespace Unveil.Core.Admin;

public enum EnergySeverity
{
    Error,
    Warning,
    Informational,
}

/// <summary>One type of problem from <c>powercfg /energy</c>, with every occurrence of it.</summary>
public sealed record EnergyFinding
{
    /// <summary>Stable ID for this type of check (the same in every language).</summary>
    public required string Guid { get; init; }

    public EnergySeverity Severity { get; init; }

    /// <summary>Title as Windows wrote it (can be localised: display only, never compare).</summary>
    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    /// <summary>Each occurrence's details as name/value pairs (e.g. "Process Name" = "svchost.exe").</summary>
    public IReadOnlyList<IReadOnlyList<KeyValuePair<string, string>>> Occurrences { get; init; } = [];
}

/// <summary>Parsed <c>powercfg /energy /xml</c> (admin only): the errors and warnings Windows found.</summary>
public sealed record EnergyDiagnostics
{
    public DateTimeOffset? ScanTime { get; init; }

    public TimeSpan? Duration { get; init; }

    public int ErrorCount { get; init; }

    public int WarningCount { get; init; }

    public int InformationalCount { get; init; }

    /// <summary>Errors then warnings, grouped by check type.</summary>
    public IReadOnlyList<EnergyFinding> Findings { get; init; } = [];

    /// <summary>Checks Unveil knows how to explain, by GUID (language-independent).</summary>
    public static class Known
    {
        public const string DisplayTimeoutDisabledOnBattery = "5da972cc-6777-4119-9874-a803a2036f38";
        public const string SleepTimeoutDisabledOnBattery = "fc79a03c-7f75-45db-b4df-c5582adbe2a5";
        public const string UsbNotSuspending = "109f745a-2d80-4358-b63a-0c7a2ae1afea";
        public const string HighProcessorUtilization = "df7b6707-14d3-45ff-9ae6-e65517684d37";
        public const string ProcessWithHighUtilization = "f9dabcaa-e040-411f-8c32-ced54cc665cc";
    }

    public EnergyFinding? Find(string guid) =>
        Findings.FirstOrDefault(f => string.Equals(f.Guid, guid, StringComparison.OrdinalIgnoreCase));
}

public static class EnergyDiagnosticsParser
{
    /// <summary>
    /// Details end up in the app and in exported reports, so Wi-Fi network names, MAC addresses, account SIDs and
    /// user folder names are removed as they are read (README.md, Privacy). The rest (programs, devices) stays.
    /// </summary>
    internal static KeyValuePair<string, string> Private(string name, string value) =>
        new(name, name.Contains("SSID", StringComparison.OrdinalIgnoreCase) && value.Length > 0
            ? SampleScrubber.Redacted
            : SampleScrubber.Text(value, []));

    public static EnergyDiagnostics Parse(XDocument document)
    {
        var root = document.Root;
        if (root is null || root.Name.LocalName != "EnergyReport")
        {
            throw new BatteryReportParseException($"Not an energy report: root element is '{root?.Name.LocalName ?? "(none)"}'.");
        }

        var info = Child(root, "ReportInformation");
        var entries = root.Descendants().Where(e => e.Name.LocalName == "LogEntry").Select(e => new
        {
            Guid = e.Attribute("guid")?.Value ?? "",
            Severity = ParseSeverity(Child(e, "Severity")?.Value),
            Name = Child(e, "Name")?.Value.Trim() ?? "",
            Description = Child(e, "Description")?.Value.Trim() ?? "",
            Details = (IReadOnlyList<KeyValuePair<string, string>>)Children(Child(e, "Details"), "Detail")
                .Select(d => Private(Child(d, "Name")?.Value.Trim() ?? "", Child(d, "Value")?.Value.Trim() ?? ""))
                .Where(kv => kv.Key.Length > 0)
                .ToList(),
        }).ToList();

        var findings = entries
            .Where(e => e.Severity != EnergySeverity.Informational)
            .GroupBy(e => e.Guid.Length > 0 ? e.Guid : e.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var (name, description) = Title(g.First().Name, g.First().Description);
                return new EnergyFinding
                {
                    Guid = g.Key,
                    Severity = g.Min(e => e.Severity),
                    Name = name,
                    Description = description,
                    Occurrences = g.Select(e => e.Details).ToList(),
                };
            })
            .OrderBy(f => f.Severity)
            .ToList();

        return new EnergyDiagnostics
        {
            ScanTime = DateTimeOffset.TryParse(Child(info, "ScanTime")?.Value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal, out var t) ? t : null,
            Duration = Duration(Child(info, "ScanDuration")?.Value),
            ErrorCount = entries.Count(e => e.Severity == EnergySeverity.Error),
            WarningCount = entries.Count(e => e.Severity == EnergySeverity.Warning),
            InformationalCount = entries.Count(e => e.Severity == EnergySeverity.Informational),
            Findings = findings,
        };
    }

    /// <summary>The title is shown before the description, so it never ends in a full stop.</summary>
    public const string UntitledCheck = "Windows energy check";

    /// <summary>
    /// A display title for an entry. Some entries come without a name (seen for "Devices with missing or misconfigured
    /// drivers"): then the description's first sentence becomes the title and the rest stays as the description.
    /// </summary>
    internal static (string Name, string Description) Title(string name, string description)
    {
        name = name.Trim().TrimEnd('.').TrimEnd();
        description = description.Trim();
        if (name.Length > 0)
        {
            return (name, description);
        }

        if (description.Length == 0)
        {
            return (UntitledCheck, "");
        }

        var end = description.IndexOf(". ", StringComparison.Ordinal);
        return end > 0
            ? (description[..end], description[(end + 2)..].Trim())
            : (description.TrimEnd('.'), "");
    }

    /// <summary>
    /// The XML uses English severity words. If a localised Windows ever writes something else, the entry is
    /// shown as a warning rather than hidden.
    /// </summary>
    internal static EnergySeverity ParseSeverity(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "error" => EnergySeverity.Error,
        "informational" or "information" or "info" => EnergySeverity.Informational,
        _ => EnergySeverity.Warning,
    };

    private static TimeSpan? Duration(string? value)
    {
        try
        {
            return value is null ? null : XmlConvert.ToTimeSpan(value.Trim());
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static XElement? Child(XElement? parent, string name) => parent?.Elements().FirstOrDefault(c => c.Name.LocalName == name);

    private static IEnumerable<XElement> Children(XElement? parent, string name) =>
        parent?.Elements().Where(c => c.Name.LocalName == name) ?? [];
}
