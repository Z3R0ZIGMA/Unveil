using System.Globalization;
using System.Reflection;
using PowerProbe.Core;
using PowerProbe.Core.Battery;

namespace PowerProbe.Cli;

/// <summary>Plain-text output of a battery snapshot. ASCII only so it reads cleanly in any console or RMM log.</summary>
internal static class ConsoleReport
{
    private const int LabelWidth = 22;
    private const int MaxHistoryRows = 12;
    private const int MaxDrainRows = 5;
    private static readonly TimeSpan MinDrainDuration = TimeSpan.FromMinutes(10);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Write(TextWriter o, BatterySnapshot s)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        o.WriteLine();
        o.WriteLine($"PowerProbe {version} - battery data (Phase 1 test build)");
        o.WriteLine($"Collected: {s.CollectedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", Inv)}   " +
                    $"Running as: {(s.IsAdministrator ? "administrator" : "standard user")}");

        WriteSystem(o, s);

        if (!s.HasBattery)
        {
            WriteNoBattery(o, s);
        }
        else
        {
            foreach (var b in s.Batteries)
            {
                WriteBattery(o, b, s.Batteries.Count);
            }

            if (s.Combined is { } total)
            {
                WriteCombined(o, total);
            }
        }

        if (s.Report is { } report)
        {
            WriteReport(o, report);
            WriteHistory(o, report);
            WriteDrains(o, report);
        }

        WriteNotes(o, s.Log);
        o.WriteLine();
    }

    private static void WriteSystem(TextWriter o, BatterySnapshot s)
    {
        Section(o, "SYSTEM");
        Line(o, "Manufacturer", s.System.Manufacturer);
        Line(o, "Model", s.System.DisplayModel);
        if (s.System.SystemFamily is { } family && family != s.System.DisplayModel)
        {
            Line(o, "Family", family);
        }

        Line(o, "Serial number", s.System.SerialNumber);
        Line(o, "BIOS", s.System.BiosVersion);
        Line(o, "Type", s.System.PcSystemTypeName);
    }

    private static void WriteNoBattery(TextWriter o, BatterySnapshot s)
    {
        Section(o, "BATTERY");
        o.WriteLine("  No battery detected.");
        var role = s.Report?.System.PlatformRole;
        o.WriteLine(s.System.PcSystemType is 2 or 8 || role is "Mobile" or "Slate"
            ? "  This machine reports itself as a laptop/tablet, so the battery may be missing,\n" +
              "  disconnected or not recognised by Windows. Check Device Manager > Batteries."
            : "  This looks like a desktop or virtual machine, so that is expected.");
    }

    private static void WriteBattery(TextWriter o, BatteryInfo b, int count)
    {
        Section(o, count > 1 ? $"BATTERY {b.Index} of {count}" : "BATTERY");
        var unit = b.IsRelativeCapacity ? "units (relative)" : "mWh";

        Line(o, "Name / part no.", b.Name);
        Line(o, "Manufacturer", b.Manufacturer);
        Line(o, "Serial number", b.SerialNumber);
        Line(o, "Chemistry", b.Chemistry is null ? null
            : b.ChemistryCode is { } code && !string.Equals(code, b.Chemistry, StringComparison.OrdinalIgnoreCase)
                ? $"{b.Chemistry} ({code})"
                : b.Chemistry);
        Line(o, "Manufacture date", b.ManufactureDate);
        Line(o, "Design capacity", Capacity(b.DesignCapacity, unit));
        Line(o, "Full charge capacity", Capacity(b.FullChargeCapacity, unit));
        Line(o, "Health", Health(b.Health));
        Line(o, "Cycle count", b.CycleCount?.ToString("N0", Inv) ?? "Not reported by manufacturer");

        Line(o, "Current charge", b.RemainingCapacity is null
            ? null
            : $"{Capacity(b.RemainingCapacity, unit)}{(b.ChargePercent is { } p ? $" ({p.ToString("0.0", Inv)}%)" : "")}");
        Line(o, "Voltage", b.VoltageMillivolts is { } mv ? $"{(mv / 1000.0).ToString("0.00", Inv)} V" : null);
        Line(o, "Power", PowerState(b));
        if (b.Critical == true)
        {
            Line(o, "Critical", "YES - Windows flags this battery as critical");
        }

        Line(o, "Data from", Sources(b.Sources));
    }

    private static void WriteCombined(TextWriter o, BatteryTotals t)
    {
        Section(o, $"COMBINED ({t.Count} batteries)");
        Line(o, "Design capacity", Capacity(t.DesignCapacity, "mWh"));
        Line(o, "Full charge capacity", Capacity(t.FullChargeCapacity, "mWh"));
        Line(o, "Health", Health(t.Health));
        Line(o, "Current charge", t.RemainingCapacity is null
            ? null
            : $"{Capacity(t.RemainingCapacity, "mWh")}{(t.ChargePercent is { } p ? $" ({p.ToString("0.0", Inv)}%)" : "")}");
        if (t.DischargeRateMilliwatts is > 0)
        {
            Line(o, "Discharge rate", Watts(t.DischargeRateMilliwatts));
        }
        else if (t.ChargeRateMilliwatts is > 0)
        {
            Line(o, "Charge rate", Watts(t.ChargeRateMilliwatts));
        }
    }

    private static void WriteReport(TextWriter o, BatteryReport r)
    {
        Section(o, "BATTERY REPORT");
        Line(o, "Generated", r.ScanTime?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", Inv));
        Line(o, "Platform", r.System.PlatformRole is null ? null
            : $"{r.System.PlatformRole}{(r.System.ModernStandby is { } ms ? $", Modern Standby: {(ms ? "yes" : "no")}" : "")}");

        if (r.RuntimeEstimates is { } est)
        {
            Line(o, "Est. active runtime", Runtime(est.AtFullChargeCapacity?.ActiveRuntime, est.AtDesignCapacity?.ActiveRuntime));
            if (r.System.ModernStandby == true)
            {
                Line(o, "Est. standby runtime",
                    Runtime(est.AtFullChargeCapacity?.ModernStandbyRuntime, est.AtDesignCapacity?.ModernStandbyRuntime));
            }
        }
    }

    private static void WriteHistory(TextWriter o, BatteryReport r)
    {
        var history = r.CapacityHistory;
        if (history.Count == 0)
        {
            Section(o, "CAPACITY HISTORY");
            o.WriteLine("  No history in the battery report.");
            return;
        }

        Section(o, $"CAPACITY HISTORY ({history.Count} periods, {Date(history[0].Start)} to {Date(history[^1].End)})");
        o.WriteLine($"  {"Period start",-14}{"Full charge",14}{"Design",14}{"Health",10}{"Cycles",9}");

        foreach (var i in SampleIndexes(history.Count, MaxHistoryRows))
        {
            var e = history[i];
            var health = e.Health.HealthPercent is { } h ? $"{h.ToString("0.0", Inv)}%" : "-";
            o.WriteLine(
                $"  {Date(e.Start),-14}{Num(e.FullChargeCapacity),14}{Num(e.DesignCapacity),14}{health,10}" +
                $"{(e.CycleCount is > 0 ? Num(e.CycleCount) : "-"),9}{(e.BatteryChanged ? "  * battery changed" : "")}");
        }

        if (history.Count > MaxHistoryRows)
        {
            o.WriteLine($"  (showing {MaxHistoryRows} of {history.Count} periods, evenly spaced)");
        }
    }

    private static void WriteDrains(TextWriter o, BatteryReport r)
    {
        var drains = r.EnergyDrains
            .Where(d => d.Duration >= MinDrainDuration && d.AverageWatts is not null)
            .ToList();

        Section(o, $"RECENT ON-BATTERY SESSIONS (10 min or longer: {drains.Count})");
        if (drains.Count == 0)
        {
            o.WriteLine("  None in the battery report period.");
            return;
        }

        o.WriteLine($"  {"Start",-18}{"Duration",10}{"Used",14}{"Avg draw",11}");
        foreach (var d in drains.TakeLast(MaxDrainRows))
        {
            o.WriteLine(
                $"  {d.Start?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", Inv),-18}{Span(d.Duration),10}" +
                $"{Num(d.Used) + " mWh",14}{Watts(d.AverageWatts * 1000),11}");
        }

        var average = drains.Sum(d => d.Used!.Value) / drains.Sum(d => d.Duration!.Value.TotalHours) / 1000.0;
        o.WriteLine($"  Average draw across all {drains.Count} sessions: {average.ToString("0.0", Inv)} W");
    }

    private static void WriteNotes(TextWriter o, IReadOnlyList<LogEntry> log)
    {
        Section(o, "NOTES");
        if (log.Count == 0)
        {
            o.WriteLine("  None - all sources read without problems.");
            return;
        }

        foreach (var e in log)
        {
            var tag = e.Level switch
            {
                LogLevel.Error => "error",
                LogLevel.Warning => "warn ",
                _ => "info ",
            };
            o.WriteLine($"  [{tag}] {e.Source}: {e.Message}");
        }
    }

    // ---- formatting helpers ----

    private static void Section(TextWriter o, string title)
    {
        o.WriteLine();
        o.WriteLine(title);
    }

    private static void Line(TextWriter o, string label, string? value) =>
        o.WriteLine($"  {label.PadRight(LabelWidth)}{value ?? "Not reported"}");

    private static string Health(HealthResult h) => h.HealthPercent is { } health
        ? $"{health.ToString("0.0", Inv)}%  (wear {h.WearPercent!.Value.ToString("0.0", Inv)}%)"
        : $"Unknown - {h.UnknownReason}";

    private static string? Capacity(long? value, string unit) => value is null ? null : $"{Num(value)} {unit}";

    private static string Num(long? value) => value?.ToString("N0", Inv) ?? "-";

    private static string Watts(double? milliwatts) =>
        milliwatts is { } mw ? $"{(mw / 1000.0).ToString("0.0", Inv)} W" : "-";

    private static string PowerState(BatteryInfo b)
    {
        if (b.PowerOnline is null && b.Charging is null && b.Discharging is null)
        {
            return "Unknown (no live WMI data)";
        }

        if (b.Discharging == true || b.PowerOnline == false)
        {
            return b.DischargeRateMilliwatts is > 0
                ? $"On battery, discharging at {Watts(b.DischargeRateMilliwatts)}"
                : "On battery";
        }

        if (b.Charging == true)
        {
            return b.ChargeRateMilliwatts is > 0
                ? $"AC connected, charging at {Watts(b.ChargeRateMilliwatts)}"
                : "AC connected, charging";
        }

        return "AC connected, not charging";
    }

    private static string Sources(BatteryDataSources s) => s switch
    {
        BatteryDataSources.Wmi | BatteryDataSources.BatteryReport => "WMI + battery report",
        BatteryDataSources.Wmi => "WMI only",
        BatteryDataSources.BatteryReport => "Battery report only",
        _ => "None",
    };

    private static string? Runtime(TimeSpan? now, TimeSpan? whenNew) => now is null && whenNew is null
        ? null
        : $"{Span(now)} at current full charge ({Span(whenNew)} when new)";

    private static string Span(TimeSpan? t)
    {
        if (t is not { } v)
        {
            return "-";
        }

        if (v.TotalDays >= 1)
        {
            return $"{(int)v.TotalDays} d {v.Hours} h";
        }

        return v.TotalHours >= 1 ? $"{(int)v.TotalHours} h {v.Minutes} m" : $"{v.Minutes} m";
    }

    private static string Date(DateTimeOffset? d) => d?.ToLocalTime().ToString("yyyy-MM-dd", Inv) ?? "?";

    /// <summary>Up to <paramref name="max"/> evenly spaced indexes, always including the first and last.</summary>
    internal static IEnumerable<int> SampleIndexes(int count, int max)
    {
        if (count <= max)
        {
            return Enumerable.Range(0, count);
        }

        return Enumerable.Range(0, max)
            .Select(i => (int)Math.Round(i * (count - 1) / (double)(max - 1)))
            .Distinct();
    }
}
