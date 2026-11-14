using System.Globalization;
using System.Reflection;
using Unveil.Core;
using Unveil.Core.Battery;
using Unveil.Core.Reports;
using Unveil.Core.Verdict;

namespace Unveil.CommandLine;

/// <summary>Plain-text output of a battery snapshot. ASCII only so it reads cleanly in any console or RMM log.</summary>
public static class ConsoleReport
{
    private const int LabelWidth = 22;
    private const int MaxHistoryRows = 12;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Write(TextWriter o, ReportDocument doc)
    {
        var s = doc.Snapshot;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        o.WriteLine();
        o.WriteLine($"Unveil {version} - laptop battery check");
        o.WriteLine($"Collected: {s.CollectedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", Inv)}   " +
                    $"Running as: {(s.IsAdministrator ? "administrator" : "standard user")}");

        WriteVerdict(o, doc.Verdict);
        WriteSystem(o, s);
        WriteSettings(o, doc.SettingChecks);

        if (s.HasBattery)
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
        }

        WriteUsage(o, doc.Usage);
        if (doc.Activity is { } activity)
        {
            WriteApps(o, doc, activity);
        }

        if (doc.Admin is { } admin)
        {
            WriteAdmin(o, admin);
        }

        WriteNotes(o, s.Log);

        Section(o, "SUMMARY (copy into a ticket)");
        o.WriteLine(TextSummary.Create(doc));
        o.WriteLine();
    }

    private static void WriteVerdict(TextWriter o, Verdict v)
    {
        var tag = v.Severity switch
        {
            Severity.Good => "[OK]",
            Severity.Warning => "[!]",
            Severity.Danger => "[X]",
            _ => "[i]",
        };

        Section(o, $"VERDICT: {tag} {v.Title.ToUpperInvariant()}");
        o.WriteLine($"  {v.Message}");

        o.WriteLine();
        o.WriteLine("  What to do:");
        foreach (var a in v.Actions)
        {
            o.WriteLine($"   - {a}");
        }

        o.WriteLine();
        o.WriteLine("  Why:");
        foreach (var f in v.Evidence)
        {
            var mark = f.Severity switch
            {
                Severity.Good => "ok",
                Severity.Warning => "! ",
                Severity.Danger => "X ",
                _ => "  ",
            };
            o.WriteLine($"   {mark} {f.Text}");
        }
    }

    private static void WriteApps(TextWriter o, ReportDocument doc, Unveil.Core.Power.ActivityResult activity)
    {
        var drain = doc.Drain;
        Section(o, $"APPS USING THE MOST POWER (drain test, {Formatting.Duration(activity.Duration)})");
        if (drain?.AverageWatts is { } w)
        {
            o.WriteLine($"  Power draw: {Formatting.Watts(w)} average ({Formatting.Watts(drain.MinWatts)} to {Formatting.Watts(drain.MaxWatts)})" +
                        (doc.DrainProfile is { } p ? $", limits for a {p.ToLowerInvariant()}" : ""));
        }
        else
        {
            o.WriteLine("  Power draw: not measured (charger connected)");
        }

        o.WriteLine($"  Whole-machine CPU: {Formatting.Percent(activity.TotalCpuPercent)}" + (activity.GpuMeasured ? "" : "  (GPU not measured)"));
        o.WriteLine();
        o.WriteLine($"  {"App",-34}{"Share",7}{"CPU",8}{"GPU",8}{"Disk+net",11}");
        foreach (var app in activity.Apps.Take(15))
        {
            var name = app.DisplayName + (app.ProcessCount > 1 ? $" ({app.ProcessCount})" : "");
            if (name.Length > 33)
            {
                name = name[..32] + "~";
            }

            var io = app.IoBytesPerSecond >= 1024 * 1024
                ? (app.IoBytesPerSecond / 1024 / 1024).ToString("0.0", Inv) + " MB/s"
                : (app.IoBytesPerSecond / 1024).ToString("0", Inv) + " KB/s";
            o.WriteLine($"  {name,-34}{Formatting.Percent(app.SharePercent, "0"),7}{Formatting.Percent(app.CpuPercent),8}" +
                        $"{(activity.GpuMeasured ? Formatting.Percent(app.GpuPercent) : "-"),8}{io,11}" +
                        (app.Offender is { } off ? $"  ! {off.Category}" : ""));
        }
    }

    private static void WriteSettings(TextWriter o, IReadOnlyList<Unveil.Core.SystemInfo.SettingCheck> checks)
    {
        if (checks.Count == 0)
        {
            return;
        }

        Section(o, "POWER SETTINGS");
        foreach (var c in checks)
        {
            var mark = c.Severity switch { Severity.Good => "ok", Severity.Warning => "! ", _ => "  " };
            o.WriteLine($"  {mark} {c.Name,-28}{c.Value}");
        }
    }

    private static void WriteAdmin(TextWriter o, Unveil.Core.Admin.AdminData admin)
    {
        if (!admin.IsAdministrator)
        {
            Section(o, "ADMIN CHECKS");
            o.WriteLine("  Skipped: energy history, sleep study and energy diagnostics need an elevated prompt.");
            return;
        }

        if (admin.Srum?.Last24Hours is { } day)
        {
            Section(o, $"ENERGY USE BY APP, LAST 24 HOURS{(day.OnBatteryOnly ? " ON BATTERY" : "")} (SRUM)");
            o.WriteLine($"  {Formatting.Percent(day.UnassignedPercent, "0")} of the energy could not be assigned to an app and is left out.");
            o.WriteLine($"  {"App",-44}{"Share",7}  {"Mostly",-8}{"Background",11}");
            foreach (var app in day.Apps.Take(10))
            {
                var name = app.Name.Length > 43 ? app.Name[..42] + "~" : app.Name;
                o.WriteLine($"  {name,-44}{Formatting.Percent(app.SharePercent, "0"),7}  {app.MainComponent,-8}{Formatting.Percent(app.BackgroundPercent, "0"),11}" +
                            (app.Offender is { } off ? $"  ! {off.Category}" : ""));
            }
        }

        if (admin.Sleep is { } sleep)
        {
            Section(o, "SLEEP STUDY");
            o.WriteLine(sleep.JudgedPercentPerHour is { } rate
                ? $"  Asleep on battery: {Formatting.Percent(rate)} of a charge per hour over {Formatting.Duration(sleep.BatterySleepTime)} ({sleep.BatterySessions.Count} sessions of 30 min or more)"
                : $"  Only {Formatting.Duration(sleep.BatterySleepTime)} of sleep on battery: too little to judge (at least {Formatting.Duration(Unveil.Core.Admin.SleepStudy.MinimumJudgedSleep)} needed).");
            foreach (var s in sleep.Sessions.Reverse().Take(5))
            {
                o.WriteLine($"  {Formatting.DateAndTime(s.Start),-17} {Formatting.Duration(s.Duration),8}  {(s.OnAc ? "AC     " : "battery")}" +
                            $"  {(s.PercentPerHour is { } p ? Formatting.Percent(p) + "/h" : "-"),8}  deep sleep {Formatting.Percent(s.HardwareLowPowerPercent, "0"),4}" +
                            (s.TopBlockers.FirstOrDefault() is { } b ? $"  awake: {b.Name}" : ""));
            }
        }

        if (admin.Energy is { } energy)
        {
            Section(o, $"WINDOWS ENERGY DIAGNOSTICS ({energy.ErrorCount} errors, {energy.WarningCount} warnings)");
            foreach (var f in energy.Findings)
            {
                var count = f.Occurrences.Count > 1 ? $" (x{f.Occurrences.Count})" : "";
                o.WriteLine($"  [{(f.Severity == Unveil.Core.Admin.EnergySeverity.Error ? "error" : "warn ")}] {f.Name}{count}");
            }
        }

        foreach (var e in admin.Log.Where(e => e.Level != LogLevel.Info))
        {
            o.WriteLine($"  [{e.Level.ToString().ToLowerInvariant()}] {e.Source}: {e.Message}");
        }
    }

    private static void WriteUsage(TextWriter o, UsageSummary u)
    {
        Section(o, "RECENT POWER USE ON BATTERY (from the battery report)");
        if (u.InUse is null && u.Asleep is null)
        {
            o.WriteLine("  Not enough time on battery in the report period.");
            return;
        }

        Line(o, "In use", u.InUse is { } use
            ? $"{Formatting.Watts(use.AverageWatts)} average over {Formatting.Duration(use.Duration)}"
            : "Not enough data");
        Line(o, "Asleep", u.Asleep is { } sleep
            ? $"{Formatting.Watts(sleep.AverageWatts, "0.00")} average over {Formatting.Duration(sleep.Duration)}" +
              (sleep.PercentPerHour is { } pph ? $" ({Formatting.Percent(pph)} of a full charge per hour)" : "")
            : "Not enough data");
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

    private static void WriteBattery(TextWriter o, BatteryInfo b, int count)
    {
        Section(o, count > 1 ? $"BATTERY {b.Index} of {count}" : "BATTERY");
        var unit = b.IsRelativeCapacity ? "units (relative)" : "mWh";

        Line(o, "Name / part no.", b.Name);
        Line(o, "Manufacturer", b.Manufacturer);
        Line(o, "Serial number", b.SerialNumber);
        Line(o, "Chemistry", BatteryText.Chemistry(b));
        Line(o, "Manufacture date", b.ManufactureDate);
        Line(o, "Design capacity", Capacity(b.DesignCapacity, unit));
        Line(o, "Full charge capacity", Capacity(b.FullChargeCapacity, unit));
        Line(o, "Health", Health(b.Health));
        Line(o, "Cycle count", b.CycleCount?.ToString("N0", Inv) ?? "Not reported by manufacturer");

        Line(o, "Current charge", b.RemainingCapacity is null
            ? null
            : $"{Capacity(b.RemainingCapacity, unit)}{(b.ChargePercent is { } p ? $" ({p.ToString("0.0", Inv)}%)" : "")}");
        Line(o, "Voltage", b.VoltageMillivolts is { } mv ? $"{(mv / 1000.0).ToString("0.00", Inv)} V" : null);
        Line(o, "Power", BatteryText.PowerState(b));
        if (b.Critical == true)
        {
            Line(o, "Charge level", "Critically low");
        }

        Line(o, "Data from", BatteryText.Sources(b.Sources));
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
    public static IEnumerable<int> SampleIndexes(int count, int max)
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
