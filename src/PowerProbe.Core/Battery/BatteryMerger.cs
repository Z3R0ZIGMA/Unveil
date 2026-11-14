using System.Globalization;

namespace PowerProbe.Core.Battery;

/// <summary>
/// Combines WMI and battery report data per battery. Pure logic, no I/O.
/// Matching order: serial number, then battery name, then position (when the counts are equal).
/// When both sources have a value and they differ, the battery report wins and the difference is logged.
/// </summary>
public static class BatteryMerger
{
    private const string Source = "Merge";

    public static IReadOnlyList<BatteryInfo> Merge(
        IReadOnlyList<WmiBatteryData> wmi,
        IReadOnlyList<ReportBattery> report,
        CollectionLog log)
    {
        var pairs = Pair(wmi, report, log);
        return pairs.Select((p, i) => Build(i + 1, p.Wmi, p.Report, log)).ToList();
    }

    public static BatteryTotals? Combine(IReadOnlyList<BatteryInfo> batteries)
    {
        if (batteries.Count < 2)
        {
            return null;
        }

        var design = SumAll(batteries.Select(b => b.DesignCapacity));
        var full = SumAll(batteries.Select(b => b.FullChargeCapacity));
        var remaining = SumAll(batteries.Select(b => b.RemainingCapacity));
        var unknown = batteries.FirstOrDefault(b => !b.Health.IsKnown);

        return new BatteryTotals
        {
            Count = batteries.Count,
            DesignCapacity = design,
            FullChargeCapacity = full,
            RemainingCapacity = remaining,
            ChargePercent = Percent(remaining, full),
            ChargeRateMilliwatts = SumAny(batteries.Select(b => b.ChargeRateMilliwatts)),
            DischargeRateMilliwatts = SumAny(batteries.Select(b => b.DischargeRateMilliwatts)),
            Health = unknown is not null
                ? HealthResult.Unknown($"Health of battery {unknown.Index} is unknown")
                : BatteryHealth.Calculate(design, full),
        };
    }

    private static List<(WmiBatteryData? Wmi, ReportBattery? Report)> Pair(
        IReadOnlyList<WmiBatteryData> wmi,
        IReadOnlyList<ReportBattery> report,
        CollectionLog log)
    {
        var matched = new Dictionary<WmiBatteryData, ReportBattery>();
        var unusedReport = report.ToList();

        // 1. Serial number, 2. battery name.
        foreach (var w in wmi)
        {
            var r = unusedReport.FirstOrDefault(r => SameKey(w.SerialNumber, r.SerialNumber))
                    ?? unusedReport.FirstOrDefault(r => SameKey(w.DeviceName, r.Id));
            if (r is not null)
            {
                matched[w] = r;
                unusedReport.Remove(r);
            }
        }

        // 3. Position, when what is left lines up one-to-one.
        var unmatchedWmi = wmi.Where(w => !matched.ContainsKey(w)).ToList();
        if (unmatchedWmi.Count > 0 && unmatchedWmi.Count == unusedReport.Count)
        {
            for (var i = 0; i < unmatchedWmi.Count; i++)
            {
                matched[unmatchedWmi[i]] = unusedReport[i];
            }

            unusedReport.Clear();
        }
        else if (unmatchedWmi.Count > 0 && unusedReport.Count > 0)
        {
            log.Warning(Source,
                $"WMI reports {wmi.Count} battery(s) and the battery report {report.Count}; " +
                "some could not be matched and are shown separately.");
        }

        var pairs = wmi.Select(w => ((WmiBatteryData?)w, matched.GetValueOrDefault(w))).ToList();
        pairs.AddRange(unusedReport.Select(r => ((WmiBatteryData?)null, (ReportBattery?)r)));
        return pairs;
    }

    private static BatteryInfo Build(int index, WmiBatteryData? w, ReportBattery? r, CollectionLog log)
    {
        var label = $"Battery {index}";
        var design = Prefer(label, "design capacity", "mWh", r?.DesignCapacity, w?.DesignedCapacity, log);
        var full = Prefer(label, "full charge capacity", "mWh", r?.FullChargeCapacity, w?.FullChargedCapacity, log);
        var cycles = Prefer(label, "cycle count", "", Positive(r?.CycleCount), Positive(w?.CycleCount), log);

        // Charge % uses WMI's own full charge figure: it was read at the same moment as remaining capacity.
        var remaining = (long?)w?.RemainingCapacity;
        var liveFull = (long?)w?.FullChargedCapacity ?? full;

        return new BatteryInfo
        {
            Index = index,
            Sources = (w is null ? BatteryDataSources.None : BatteryDataSources.Wmi)
                      | (r is null ? BatteryDataSources.None : BatteryDataSources.BatteryReport),
            WmiInstanceName = w?.InstanceName,
            Manufacturer = r?.Manufacturer ?? w?.ManufactureName,
            Name = r?.Id ?? w?.DeviceName,
            SerialNumber = r?.SerialNumber ?? w?.SerialNumber,
            ChemistryCode = r?.Chemistry ?? w?.Chemistry,
            ManufactureDate = NormaliseDate(r?.ManufactureDate)
                              ?? w?.ManufactureDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DesignCapacity = design,
            FullChargeCapacity = full,
            // Either source saying "relative" is enough: better "Unknown" than a false verdict.
            IsRelativeCapacity = (r?.IsRelativeCapacity ?? false) || (w?.IsRelativeCapacity ?? false),
            CycleCount = cycles is null ? null : (int)cycles.Value,
            RemainingCapacity = remaining,
            ChargePercent = Percent(remaining, liveFull),
            VoltageMillivolts = w?.Voltage,
            ChargeRateMilliwatts = w?.ChargeRate,
            DischargeRateMilliwatts = w?.DischargeRate,
            PowerOnline = w?.PowerOnline,
            Charging = w?.Charging,
            Discharging = w?.Discharging,
            Critical = w?.Critical,
        };
    }

    /// <summary>Battery report value if present, else WMI. Logs when both exist and differ.</summary>
    private static long? Prefer(string label, string field, string unit, long? report, long? wmi, CollectionLog log)
    {
        if (report is not null && wmi is not null && report != wmi)
        {
            var u = unit.Length > 0 ? $" {unit}" : "";
            log.Warning(Source, string.Create(CultureInfo.InvariantCulture,
                $"{label}: {field} differs - WMI {wmi:N0}{u}, battery report {report:N0}{u}. Using the battery report."));
        }

        return report ?? wmi;
    }

    private static long? Positive(long? value) => value is > 0 ? value : null;

    private static bool SameKey(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static double? Percent(long? part, long? whole) =>
        part is { } p && whole is > 0 ? Math.Min(100.0, Math.Round(p * 100.0 / whole.Value, 1)) : null;

    private static long? SumAll(IEnumerable<long?> values)
    {
        long total = 0;
        foreach (var v in values)
        {
            if (v is null)
            {
                return null;
            }

            total += v.Value;
        }

        return total;
    }

    private static int? SumAny(IEnumerable<int?> values)
    {
        var present = values.Where(v => v.HasValue).ToList();
        return present.Count == 0 ? null : present.Sum();
    }

    private static readonly string[] IsoDateFormats = ["yyyy-MM-dd", "yyyy/MM/dd", "yyyyMMdd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ"];

    /// <summary>Unambiguous dates become yyyy-MM-dd; anything else is kept as the raw OEM text.</summary>
    internal static string? NormaliseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTime.TryParseExact(raw.Trim(), IsoDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : raw.Trim();
    }
}
