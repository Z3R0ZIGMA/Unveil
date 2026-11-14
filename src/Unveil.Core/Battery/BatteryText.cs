using static Unveil.Core.Formatting;

namespace Unveil.Core.Battery;

/// <summary>Plain-English descriptions shared by the console, HTML report and UI.</summary>
public static class BatteryText
{
    public static string PowerState(BatteryInfo b)
    {
        if (b.PowerOnline is null && b.Charging is null && b.Discharging is null)
        {
            return "Unknown (no live data)";
        }

        if (b.Discharging == true || b.PowerOnline == false)
        {
            return b.DischargeRateMilliwatts is > 0
                ? $"On battery, discharging at {Watts(b.DischargeRateMilliwatts / 1000.0)}"
                : "On battery";
        }

        if (b.Charging == true)
        {
            return b.ChargeRateMilliwatts is > 0
                ? $"AC connected, charging at {Watts(b.ChargeRateMilliwatts / 1000.0)}"
                : "AC connected, charging";
        }

        return "AC connected, not charging";
    }

    public static string Sources(BatteryDataSources s) => s switch
    {
        BatteryDataSources.Wmi | BatteryDataSources.BatteryReport => "WMI + battery report",
        BatteryDataSources.Wmi => "WMI only",
        BatteryDataSources.BatteryReport => "Battery report only",
        _ => "None",
    };

    public static string Chemistry(BatteryInfo b) =>
        b.Chemistry is null ? "Not reported"
        : b.ChemistryCode is { } code && !string.Equals(code, b.Chemistry, StringComparison.OrdinalIgnoreCase)
            ? $"{b.Chemistry} ({code})"
            : b.Chemistry;

    public static string Health(HealthResult h) => h.HealthPercent is { } health
        ? $"{Percent(health)} (wear {Percent(h.WearPercent)})"
        : $"Unknown - {h.UnknownReason}";

    public static string CycleCount(int? cycles) => cycles is { } c ? Number(c) : "Not reported by manufacturer";

    public static string Charge(long? remaining, double? percent, string unit = "mWh") =>
        remaining is null ? "Not reported" : $"{Number(remaining)} {unit}" + (percent is { } p ? $" ({Percent(p)})" : "");
}
