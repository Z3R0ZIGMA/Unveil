using System.Text;
using Unveil.Core.Battery;
using static Unveil.Core.Formatting;

namespace Unveil.Core.Reports;

/// <summary>The 5-8 line plain-text summary for pasting into a ticket (plan.md section 6, "Copy Summary").</summary>
public static class TextSummary
{
    public static string Create(ReportDocument doc)
    {
        var s = doc.Snapshot;
        var v = doc.Verdict;
        var sb = new StringBuilder();

        sb.AppendLine($"Unveil battery check - {DateAndTime(doc.GeneratedAt)}");

        var laptop = string.Join(" ", new[] { s.System.Manufacturer, s.System.DisplayModel }.Where(x => !string.IsNullOrWhiteSpace(x)));
        sb.AppendLine($"Laptop: {(laptop.Length > 0 ? laptop : "Unknown")}" +
                      (s.System.SerialNumber is { } serial ? $", S/N {serial}" : ""));

        if (s.Batteries.Count == 1)
        {
            sb.AppendLine($"Battery: {BatteryLine(s.Batteries[0])}");
        }
        else if (s.Batteries.Count > 1)
        {
            var combined = s.Combined?.Health.HealthPercent is { } h ? $"{Percent(h)} combined health" : "combined health unknown";
            sb.AppendLine($"Batteries: {s.Batteries.Count}, {combined}; " +
                          string.Join("; ", s.Batteries.Select(b => $"#{b.Index} {BatteryLine(b)}")));
        }

        sb.AppendLine($"Verdict: {v.Title.ToUpperInvariant()} - {v.Message}");

        if (s.Report?.RuntimeEstimates?.AtFullChargeCapacity?.ActiveRuntime is { } runtime)
        {
            var whenNew = s.Report.RuntimeEstimates.AtDesignCapacity?.ActiveRuntime;
            sb.AppendLine($"Runtime: about {Duration(runtime)} on a full charge" +
                          (whenNew is null ? "" : $" ({Duration(whenNew)} when new)"));
        }

        if (doc.Drain is { } drain)
        {
            sb.AppendLine($"Drain test: {Watts(drain.AverageWatts)} average over {Duration(drain.Duration)}");
        }

        sb.Append($"Actions: {string.Join(" ", v.Actions)}");
        return sb.ToString();
    }

    private static string BatteryLine(BatteryInfo b)
    {
        var parts = new List<string>();
        if (b.Name is { } name)
        {
            parts.Add(name);
        }

        parts.Add(b.Health.HealthPercent is { } h
            ? $"{Percent(h)} health ({Number(b.FullChargeCapacity)} / {Number(b.DesignCapacity)} mWh)"
            : "health unknown");
        parts.Add(b.CycleCount is { } c ? $"{Number(c)} cycles" : "cycles not reported");
        return string.Join(", ", parts);
    }
}
