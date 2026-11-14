using System.Management;
using Unveil.Core.Wmi;

namespace Unveil.Core.Battery;

/// <summary>One live reading of charge and power draw, combined across all batteries.</summary>
public sealed record LiveReading
{
    public DateTimeOffset Time { get; init; }

    public int BatteryCount { get; init; }

    public long? RemainingCapacity { get; init; }

    public long? FullChargeCapacity { get; init; }

    public double? ChargePercent { get; init; }

    public bool? PowerOnline { get; init; }

    public bool? Charging { get; init; }

    public bool? Discharging { get; init; }

    /// <summary>Total discharge rate in milliwatts (only meaningful on battery).</summary>
    public int? DischargeRateMilliwatts { get; init; }

    /// <summary>Total charge rate in milliwatts.</summary>
    public int? ChargeRateMilliwatts { get; init; }

    public bool IsOnBattery => PowerOnline == false || Discharging == true;

    /// <summary>Discharge in watts when on battery and the rate is known, else null.</summary>
    public double? DischargeWatts => IsOnBattery && DischargeRateMilliwatts is > 0 ? DischargeRateMilliwatts / 1000.0 : null;
}

public interface ILiveBatterySource
{
    /// <summary>Returns a reading, or null when there is no battery or WMI fails. Never throws.</summary>
    LiveReading? Read();
}

/// <summary>Reads only <c>BatteryStatus</c> and <c>BatteryFullChargedCapacity</c>, so it is cheap enough to poll every 2 s.</summary>
public sealed class WmiLiveBatteryReader : ILiveBatterySource
{
    private const string Scope = @"root\wmi";
    private const uint UnknownCapacity = 0xFFFFFFFF;
    private const int UnknownRate = unchecked((int)0x80000000);

    public LiveReading? Read()
    {
        try
        {
            var status = WmiQuery.Select(Scope, "SELECT * FROM BatteryStatus", o => new WmiBatteryData
            {
                InstanceName = WmiQuery.String(o, "InstanceName") ?? "",
                RemainingCapacity = Known(WmiQuery.UInt(o, "RemainingCapacity")),
                ChargeRate = KnownRate(WmiQuery.Int(o, "ChargeRate")),
                DischargeRate = KnownRate(WmiQuery.Int(o, "DischargeRate")),
                PowerOnline = WmiQuery.Bool(o, "PowerOnline"),
                Charging = WmiQuery.Bool(o, "Charging"),
                Discharging = WmiQuery.Bool(o, "Discharging"),
            });

            var full = WmiQuery.Select(Scope, "SELECT * FROM BatteryFullChargedCapacity", o =>
                (Instance: WmiQuery.String(o, "InstanceName") ?? "", Full: Known(WmiQuery.UInt(o, "FullChargedCapacity"))));

            foreach (var battery in status)
            {
                battery.FullChargedCapacity = full.FirstOrDefault(f =>
                    string.Equals(f.Instance, battery.InstanceName, StringComparison.OrdinalIgnoreCase)).Full;
            }

            return Combine(status, DateTimeOffset.Now);
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>Adds up several batteries into one reading. Null when there are none.</summary>
    public static LiveReading? Combine(IReadOnlyList<WmiBatteryData> batteries, DateTimeOffset time)
    {
        if (batteries.Count == 0)
        {
            return null;
        }

        long? remaining = SumAll(batteries.Select(b => (long?)b.RemainingCapacity));
        long? full = SumAll(batteries.Select(b => (long?)b.FullChargedCapacity));

        return new LiveReading
        {
            Time = time,
            BatteryCount = batteries.Count,
            RemainingCapacity = remaining,
            FullChargeCapacity = full,
            ChargePercent = remaining is { } r && full is > 0 ? Math.Min(100.0, Math.Round(r * 100.0 / full.Value, 1)) : null,
            // AC is connected if any battery says so; discharging/charging if any battery is.
            PowerOnline = AnyTrue(batteries.Select(b => b.PowerOnline)),
            Charging = AnyTrue(batteries.Select(b => b.Charging)),
            Discharging = AnyTrue(batteries.Select(b => b.Discharging)),
            DischargeRateMilliwatts = SumKnown(batteries.Select(b => b.DischargeRate)),
            ChargeRateMilliwatts = SumKnown(batteries.Select(b => b.ChargeRate)),
        };
    }

    /// <summary>How long <paramref name="capacityMWh"/> lasts at <paramref name="watts"/>; null when either is unknown or zero.</summary>
    public static TimeSpan? Runtime(long? capacityMWh, double? watts) =>
        capacityMWh is > 0 && watts is > 0.1 ? TimeSpan.FromHours(capacityMWh.Value / 1000.0 / watts.Value) : null;

    private static uint? Known(uint? value) => value == UnknownCapacity ? null : value;

    private static int? KnownRate(int? value) => value == UnknownRate ? null : value;

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

    private static int? SumKnown(IEnumerable<int?> values)
    {
        var known = values.Where(v => v.HasValue).ToList();
        return known.Count == 0 ? null : known.Sum();
    }

    private static bool? AnyTrue(IEnumerable<bool?> values)
    {
        var known = values.Where(v => v.HasValue).ToList();
        return known.Count == 0 ? null : known.Any(v => v == true);
    }
}
