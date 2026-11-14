using System.Management;

namespace Unveil.Core.Wmi;

/// <summary>Small helpers for reading WMI safely. Values are copied out before the objects are disposed.</summary>
internal static class WmiQuery
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Runs a WQL query and maps each result. Throws <see cref="ManagementException"/> on WMI errors.</summary>
    public static List<T> Select<T>(string scope, string wql, Func<ManagementBaseObject, T> map, TimeSpan? timeout = null)
    {
        var options = new System.Management.EnumerationOptions
        {
            Timeout = timeout ?? DefaultTimeout,
            ReturnImmediately = true,
            Rewindable = false,
        };

        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql), options);
        using var results = searcher.Get();

        var list = new List<T>();
        foreach (var obj in results)
        {
            using (obj)
            {
                list.Add(map(obj));
            }
        }

        return list;
    }

    public static object? Value(ManagementBaseObject obj, string property)
    {
        try
        {
            return obj.Properties[property].Value;
        }
        catch (ManagementException)
        {
            // Property not present on this OEM's implementation of the class.
            return null;
        }
    }

    public static string? String(ManagementBaseObject obj, string property) =>
        Value(obj, property) is { } v ? Clean(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)) : null;

    public static uint? UInt(ManagementBaseObject obj, string property)
    {
        try
        {
            return Value(obj, property) is { } v ? Convert.ToUInt32(v, System.Globalization.CultureInfo.InvariantCulture) : null;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    public static int? Int(ManagementBaseObject obj, string property)
    {
        try
        {
            return Value(obj, property) is { } v ? Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture) : null;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    public static bool? Bool(ManagementBaseObject obj, string property) =>
        Value(obj, property) is bool b ? b : null;

    /// <summary>Trims a string and drops empty values and common OEM placeholders.</summary>
    public static string? Clean(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim().Trim('\0').Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        return IsPlaceholder(trimmed) ? null : trimmed;
    }

    private static readonly string[] Placeholders =
    [
        "To be filled by O.E.M.",
        "To Be Filled By O.E.M.",
        "Default string",
        "System Serial Number",
        "System Product Name",
        "System manufacturer",
        "Not Applicable",
        "None",
        "N/A",
        "0",
        "00000000",
        "0123456789",
    ];

    private static bool IsPlaceholder(string value) =>
        Placeholders.Any(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase));
}
