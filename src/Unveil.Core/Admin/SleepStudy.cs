using System.Globalization;
using System.Xml.Linq;
using Unveil.Core.Battery;

namespace Unveil.Core.Admin;

/// <summary>Something that kept the laptop from its lowest power state during a sleep session.</summary>
/// <param name="Name">Device, driver or activity name (shown as-is; can be localised).</param>
/// <param name="Type">e.g. "Fx Device", "Activator", "Audio Activity".</param>
/// <param name="ActiveTime">How long it was active during the session.</param>
/// <param name="ActivePercent">Share of the session it was active, 0-100.</param>
public sealed record SleepBlocker(string Name, string? Type, TimeSpan ActiveTime, double? ActivePercent);

/// <summary>One Modern Standby (sleep) session from the sleep study.</summary>
public sealed record SleepSession
{
    public DateTimeOffset? Start { get; init; }

    public TimeSpan Duration { get; init; }

    public bool OnAc { get; init; }

    /// <summary>mWh used during the session (entry minus exit charge). Negative means it charged.</summary>
    public int? EnergyUsed { get; init; }

    public int? FullChargeCapacity { get; init; }

    /// <summary>Software low-power time ("SW DRIPS"), 0-100.</summary>
    public double? SoftwareLowPowerPercent { get; init; }

    /// <summary>Hardware low-power time ("HW DRIPS"): how much of the session was in true deep sleep, 0-100.</summary>
    public double? HardwareLowPowerPercent { get; init; }

    public IReadOnlyList<SleepBlocker> TopBlockers { get; init; } = [];

    /// <summary>% of a full charge used per hour; null when not on battery or unknown.</summary>
    public double? PercentPerHour =>
        !OnAc && EnergyUsed is { } used && used >= 0 && FullChargeCapacity is > 0 && Duration.TotalHours > 0
            ? used * 100.0 / FullChargeCapacity.Value / Duration.TotalHours
            : null;
}

/// <summary>Parsed <c>powercfg /sleepstudy /xml</c> (admin only).</summary>
public sealed record SleepStudy
{
    public DateTimeOffset? ScanTime { get; init; }

    /// <summary>True on Modern Standby laptops (the only kind with a sleep study).</summary>
    public bool? ModernStandby { get; init; }

    /// <summary>All sleep sessions, oldest first.</summary>
    public IReadOnlyList<SleepSession> Sessions { get; init; } = [];

    /// <summary>Sessions this long or longer on battery are used to judge sleep drain.</summary>
    public static readonly TimeSpan MinimumSession = TimeSpan.FromMinutes(30);

    public IReadOnlyList<SleepSession> BatterySessions =>
        Sessions.Where(s => !s.OnAc && s.Duration >= MinimumSession && s.PercentPerHour is not null).ToList();

    public TimeSpan BatterySleepTime => BatterySessions.Aggregate(TimeSpan.Zero, (t, s) => t + s.Duration);

    /// <summary>
    /// Less sleep on battery than this is too little to judge: charge readings move in coarse steps, so one short
    /// session can show 0 mWh used.
    /// </summary>
    public static readonly TimeSpan MinimumJudgedSleep = TimeSpan.FromHours(2);

    public bool IsEnoughToJudge => AveragePercentPerHour is not null && BatterySleepTime >= MinimumJudgedSleep;

    /// <summary><see cref="AveragePercentPerHour"/> when there is enough sleep to judge, otherwise null.</summary>
    public double? JudgedPercentPerHour => IsEnoughToJudge ? AveragePercentPerHour : null;

    /// <summary>Time-weighted average % of a charge used per hour while asleep on battery.</summary>
    public double? AveragePercentPerHour
    {
        get
        {
            var sessions = BatterySessions;
            var hours = sessions.Sum(s => s.Duration.TotalHours);
            return hours > 0 ? sessions.Sum(s => s.PercentPerHour!.Value * s.Duration.TotalHours) / hours : null;
        }
    }
}

/// <summary>Parses the sleep study XML. Elements are matched by local name; values that are missing become null.</summary>
public static class SleepStudyParser
{
    public const int BlockersPerSession = 5;

    public static SleepStudy Parse(XDocument document)
    {
        var root = document.Root;
        if (root is null || root.Name.LocalName != "SleepStudy")
        {
            throw new BatteryReportParseException($"Not a sleep study: root element is '{root?.Name.LocalName ?? "(none)"}'.");
        }

        var info = Child(root, "ReportInformation");
        var system = Child(root, "SystemInformation");
        var sessions = Children(Child(root, "ScenarioInstances"), "ScenarioInstance")
            .Select(ParseSession)
            .OrderBy(s => s.Start ?? DateTimeOffset.MinValue)
            .ToList();

        return new SleepStudy
        {
            ScanTime = Date(Child(info, "ScanTime")?.Value),
            ModernStandby = Child(system, "ConnectedStandby")?.Value.Trim() switch { "1" => true, "0" => false, _ => null },
            Sessions = sessions,
        };
    }

    private static SleepSession ParseSession(XElement e)
    {
        var duration = Ticks(Attr(e, "Duration"));
        var entry = Int(Attr(e, "EntryRemainingCapacity"));
        var exit = Int(Attr(e, "ExitRemainingCapacity"));
        var used = entry is { } a && exit is { } b ? a - b : Int(Attr(e, "Discharge"));

        return new SleepSession
        {
            Start = Date(Attr(e, "TimeStamp")),
            Duration = duration,
            OnAc = Attr(e, "Ac") == "1",
            EnergyUsed = used,
            FullChargeCapacity = Int(Attr(e, "EntryFullCapacity")) ?? Int(Attr(e, "FullChargeCapacity")),
            SoftwareLowPowerPercent = Percent(Ticks(Attr(e, "LowPowerStateTime")), duration),
            HardwareLowPowerPercent = Percent(Ticks(Attr(e, "HwLowPowerStateTime")), duration),
            TopBlockers = Children(Child(e, "TopBlockers"), "TopBlocker")
                .Select(b => new SleepBlocker(
                    SampleScrubber.Text(Attr(b, "Name") ?? "Unknown", []), // app paths can contain the user folder
                    Attr(b, "Type"),
                    Ticks(Attr(b, "ActiveTime")),
                    double.TryParse(Attr(b, "ScenarioActiveTimePercent"), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : null))
                .OrderByDescending(b => b.ActiveTime)
                .Take(BlockersPerSession)
                .ToList(),
        };
    }

    private static double? Percent(TimeSpan part, TimeSpan whole) =>
        whole > TimeSpan.Zero && part >= TimeSpan.Zero ? Math.Min(100, part / whole * 100) : null;

    private static TimeSpan Ticks(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) && t > 0 ? TimeSpan.FromTicks(t) : TimeSpan.Zero;

    private static int? Int(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    private static DateTimeOffset? Date(string? value) =>
        DateTimeOffset.TryParse(value?.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? d
            : null;

    private static XElement? Child(XElement? parent, string name) => parent?.Elements().FirstOrDefault(c => c.Name.LocalName == name);

    private static IEnumerable<XElement> Children(XElement? parent, string name) =>
        parent?.Elements().Where(c => c.Name.LocalName == name) ?? [];

    private static string? Attr(XElement e, string name) =>
        e.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value is { Length: > 0 } v ? v : null;
}
