using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerProbe.Core.Battery;

namespace PowerProbe.Cli;

/// <summary>
/// Saves scrubbed copies of the raw data (battery report XML + WMI values) so they can be used
/// as test samples. Serial numbers and the computer name are removed before anything is written.
/// </summary>
internal static class SampleWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void Save(string folder, BatterySnapshot snapshot, TextWriter output)
    {
        Directory.CreateDirectory(folder);
        var stem = $"{Slug(snapshot.System.Manufacturer)}-{Slug(snapshot.System.Model)}-" +
                   snapshot.CollectedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        output.WriteLine();
        output.WriteLine("SAVED SAMPLES (scrubbed: serial numbers and computer name removed)");

        if (snapshot.Raw.BatteryReport is { } report)
        {
            var path = Path.Combine(folder, $"{stem}-battery-report.xml");
            BatteryReportScrubber.Scrub(report.Document).Save(path);
            output.WriteLine($"  {path}");
        }
        else
        {
            output.WriteLine("  No battery report to save.");
        }

        var wmiPath = Path.Combine(folder, $"{stem}-wmi.json");
        var wmi = new
        {
            snapshot.IsAdministrator,
            System = snapshot.System with { SerialNumber = Redact(snapshot.System.SerialNumber) },
            Batteries = snapshot.Raw.Wmi.Select(w => new
            {
                w.InstanceName,
                w.StaticDataAvailable,
                w.ManufactureName,
                w.DeviceName,
                SerialNumber = Redact(w.SerialNumber),
                UniqueId = Redact(w.UniqueId),
                w.Chemistry,
                w.ManufactureDate,
                w.DesignedCapacity,
                w.Capabilities,
                w.FullChargedCapacity,
                w.CycleCount,
                w.RemainingCapacity,
                w.Voltage,
                w.ChargeRate,
                w.DischargeRate,
                w.PowerOnline,
                w.Charging,
                w.Discharging,
                w.Critical,
            }),
            snapshot.Log,
        };
        File.WriteAllText(wmiPath, JsonSerializer.Serialize(wmi, Json), new UTF8Encoding(false));
        output.WriteLine($"  {wmiPath}");
    }

    private static string? Redact(string? value) => value is null ? null : BatteryReportScrubber.Redacted;

    private static string Slug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var sb = new StringBuilder();
        foreach (var c in value.Trim().ToLowerInvariant())
        {
            sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        }

        var slug = sb.ToString();
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-');
        return slug.Length > 40 ? slug[..40].TrimEnd('-') : slug;
    }
}
