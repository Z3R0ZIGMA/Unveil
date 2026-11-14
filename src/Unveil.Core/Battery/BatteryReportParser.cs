using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace Unveil.Core.Battery;

public sealed class BatteryReportParseException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Parses <c>powercfg /batteryreport /xml</c>. Elements are matched by local name so a namespace
/// change between Windows builds does not break parsing. Missing or malformed values become null.
/// </summary>
public static class BatteryReportParser
{
    public const string RootElement = "BatteryReport";

    public static BatteryReport Parse(string xml)
    {
        using var reader = new StringReader(xml);
        return Parse(LoadDocument(reader));
    }

    public static BatteryReport Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return Parse(LoadDocument(reader));
    }

    /// <summary>Loads XML safely (no DTDs). Throws <see cref="BatteryReportParseException"/> when it is not valid XML.</summary>
    public static XDocument LoadDocument(TextReader reader)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        try
        {
            using var xmlReader = XmlReader.Create(reader, settings);
            return XDocument.Load(xmlReader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            throw new BatteryReportParseException($"Battery report is not valid XML: {ex.Message}", ex);
        }
    }

    public static BatteryReport Parse(XDocument document)
    {
        var root = document.Root;
        if (root is null || root.Name.LocalName != RootElement)
        {
            throw new BatteryReportParseException(
                $"Not a battery report: root element is '{root?.Name.LocalName ?? "(none)"}', expected '{RootElement}'.");
        }

        var info = Child(root, "ReportInformation");
        return new BatteryReport
        {
            ReportVersion = Text(info, "ReportVersion"),
            ScanTime = Date(Text(info, "ScanTime")),
            System = ParseSystem(Child(root, "SystemInformation")),
            Batteries = Children(Child(root, "Batteries"), "Battery").Select(ParseBattery).ToList(),
            RuntimeEstimates = ParseRuntimeEstimates(Child(root, "RuntimeEstimates")),
            CapacityHistory = Children(Child(root, "History"), "HistoryEntry")
                .Select(ParseHistoryEntry)
                .OrderBy(e => e.Start ?? DateTimeOffset.MinValue)
                .ToList(),
            RecentUsage = Children(Child(root, "RecentUsage"), "UsageEntry").Select(ParseUsageEntry).ToList(),
            EnergyDrains = Children(Child(root, "EnergyDrains"), "Drain")
                .Select(ParseDrain)
                .OrderBy(d => d.Start ?? DateTimeOffset.MinValue)
                .ToList(),
        };
    }

    private static ReportSystemInfo ParseSystem(XElement? e) => new()
    {
        // ComputerName is deliberately not read: it is not needed and is personal data.
        Manufacturer = Text(e, "SystemManufacturer"),
        ProductName = Text(e, "SystemProductName"),
        BiosVersion = Text(e, "BIOSVersion"),
        BiosDate = Text(e, "BIOSDate"),
        OsBuild = Text(e, "OSBuild"),
        PlatformRole = Text(e, "PlatformRole"),
        ModernStandby = Flag(Text(e, "ConnectedStandby")),
    };

    private static ReportBattery ParseBattery(XElement e) => new()
    {
        Id = Text(e, "Id"),
        Manufacturer = Text(e, "Manufacturer"),
        SerialNumber = Text(e, "SerialNumber"),
        ManufactureDate = Text(e, "ManufactureDate"),
        Chemistry = Text(e, "Chemistry"),
        LongTerm = Flag(Text(e, "LongTerm")),
        IsRelativeCapacity = Flag(Text(e, "RelativeCapacity")) ?? false,
        DesignCapacity = Int(Text(e, "DesignCapacity")),
        FullChargeCapacity = Int(Text(e, "FullChargeCapacity")),
        CycleCount = Int(Text(e, "CycleCount")),
    };

    private static RuntimeEstimates? ParseRuntimeEstimates(XElement? e)
    {
        if (e is null)
        {
            return null;
        }

        static RuntimeEstimate? Estimate(XElement? x) => x is null
            ? null
            : new RuntimeEstimate(
                Int(Text(x, "Capacity")),
                Duration(Text(x, "ActiveRuntime")),
                Duration(Text(x, "ConnectedStandbyRuntime")));

        return new RuntimeEstimates(Estimate(Child(e, "DesignCapacity")), Estimate(Child(e, "FullChargeCapacity")));
    }

    private static CapacityHistoryEntry ParseHistoryEntry(XElement e) => new()
    {
        Start = Date(Attr(e, "StartDate")),
        End = Date(Attr(e, "EndDate")),
        DesignCapacity = Int(Attr(e, "DesignCapacity")),
        FullChargeCapacity = Int(Attr(e, "FullChargeCapacity")),
        CycleCount = Int(Attr(e, "CycleCount")),
        BatteryChanged = Flag(Attr(e, "BatteryChanged")) ?? false,
        EstimatedDesignActiveTime = Duration(Attr(e, "EstimatedDesignActiveTime")),
        EstimatedFullChargeActiveTime = Duration(Attr(e, "EstimatedFullChargeActiveTime")),
    };

    private static UsageEntry ParseUsageEntry(XElement e) => new()
    {
        Timestamp = Date(Attr(e, "Timestamp")),
        // Duration is in 100-nanosecond ticks.
        Duration = long.TryParse(Attr(e, "Duration"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks) && ticks > 0
            ? TimeSpan.FromTicks(ticks)
            : TimeSpan.Zero,
        OnAc = Flag(Attr(e, "Ac")) ?? false,
        EntryType = Attr(e, "EntryType"),
        ChargeCapacity = Int(Attr(e, "ChargeCapacity")),
        Discharge = Int(Attr(e, "Discharge")),
        FullChargeCapacity = Int(Attr(e, "FullChargeCapacity")),
    };

    private static EnergyDrain ParseDrain(XElement e) => new()
    {
        Start = Date(Attr(e, "StartTimestamp")),
        End = Date(Attr(e, "EndTimestamp")),
        StartChargeCapacity = Int(Attr(e, "StartChargeCapacity")),
        EndChargeCapacity = Int(Attr(e, "EndChargeCapacity")),
        FullChargeCapacity = Int(Attr(e, "StartFullChargeCapacity")),
    };

    // ---- helpers ----

    private static XElement? Child(XElement? parent, string name) =>
        parent?.Elements().FirstOrDefault(c => c.Name.LocalName == name);

    private static IEnumerable<XElement> Children(XElement? parent, string name) =>
        parent?.Elements().Where(c => c.Name.LocalName == name) ?? [];

    private static string? Text(XElement? parent, string name) => Clean(Child(parent, name)?.Value);

    private static string? Attr(XElement e, string name) =>
        Clean(e.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value);

    private static string? Clean(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    internal static int? Int(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;

    internal static bool? Flag(string? value) => value switch
    {
        null => null,
        "1" => true,
        "0" => false,
        _ when bool.TryParse(value, out var b) => b,
        _ => null,
    };

    /// <summary>ISO 8601 duration such as "PT3H47M13S" or "P3DT4H14M51S".</summary>
    internal static TimeSpan? Duration(string? value)
    {
        if (value is null)
        {
            return null;
        }

        try
        {
            return XmlConvert.ToTimeSpan(value);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    internal static DateTimeOffset? Date(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var d)
            ? d
            : null;
}
