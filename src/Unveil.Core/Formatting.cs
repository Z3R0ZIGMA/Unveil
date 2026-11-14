using System.Globalization;

namespace Unveil.Core;

/// <summary>Shared, culture-independent formatting for reports, summaries and verdict text.</summary>
public static class Formatting
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Number(long? value) => value?.ToString("N0", Inv) ?? "-";

    public static string Percent(double? value, string format = "0.0") =>
        value is { } v ? v.ToString(format, Inv) + "%" : "-";

    public static string Watts(double? watts, string format = "0.0") =>
        watts is { } w ? w.ToString(format, Inv) + " W" : "-";

    public static string Capacity(long? mWh) => mWh is null ? "-" : Number(mWh) + " mWh";

    /// <summary>"3 h 17 m", "2 d 18 h", "45 m".</summary>
    public static string Duration(TimeSpan? span)
    {
        if (span is not { } t)
        {
            return "-";
        }

        if (t.TotalDays >= 1)
        {
            return $"{(int)t.TotalDays} d {t.Hours} h";
        }

        if (t.TotalHours >= 1)
        {
            return $"{(int)t.TotalHours} h {t.Minutes} m";
        }

        return t.TotalMinutes >= 1 ? $"{t.Minutes} m" : $"{Math.Max(0, (int)Math.Round(t.TotalSeconds))} s";
    }

    public static string Date(DateTimeOffset? date) => date?.ToLocalTime().ToString("yyyy-MM-dd", Inv) ?? "-";

    public static string DateAndTime(DateTimeOffset? date) => date?.ToLocalTime().ToString("yyyy-MM-dd HH:mm", Inv) ?? "-";
}
