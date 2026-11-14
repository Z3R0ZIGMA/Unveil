using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Unveil.Core.Battery;

namespace Unveil.Core.Admin;

/// <summary>
/// Saves scrubbed copies of the raw data from a client laptop (battery report, WMI values and, when run as administrator,
/// the energy history, sleep study and energy diagnostics) so they can be added to samples/ as test data.
/// Serial numbers, names, account IDs and network IDs are removed before anything is written.
/// </summary>
public static class SampleExport
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <returns>The files written.</returns>
    public static IReadOnlyList<string> Save(string folder, BatterySnapshot snapshot, AdminData? admin = null)
    {
        Directory.CreateDirectory(folder);
        var stem = $"{Slug(snapshot.System.Manufacturer)}-{Slug(snapshot.System.Model)}-" +
                   snapshot.CollectedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var written = new List<string>();

        if (snapshot.Raw.BatteryReport is { } report)
        {
            var path = Path.Combine(folder, $"{stem}-battery-report.xml");
            SampleScrubber.Xml(BatteryReportScrubber.Scrub(report.Document)).Save(path);
            written.Add(path);
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
        File.WriteAllText(wmiPath, SampleScrubber.Text(JsonSerializer.Serialize(wmi, Json)), new UTF8Encoding(false));
        written.Add(wmiPath);

        if (admin?.Raw is { } raw)
        {
            if (raw.SrumCsv is { } srum)
            {
                var path = Path.Combine(folder, $"{stem}-srum.csv");
                var trimmed = SampleScrubber.TrimSrum(srum, admin.CollectedAt, TimeSpan.FromHours(26));
                File.WriteAllText(path, SampleScrubber.Text(trimmed), new UTF8Encoding(false));
                written.Add(path);
            }

            if (raw.SleepStudy is { } sleep)
            {
                // Per-app power estimates inside each session are bulky and not used by the parser.
                var copy = new XDocument(sleep);
                copy.Descendants().Where(e => e.Name.LocalName is "SrumData" or "SearchIndexerData").ToList().ForEach(e => e.Remove());
                var path = Path.Combine(folder, $"{stem}-sleepstudy.xml");
                SampleScrubber.Xml(copy).Save(path);
                written.Add(path);
            }

            if (raw.Energy is { } energy)
            {
                var path = Path.Combine(folder, $"{stem}-energy.xml");
                SampleScrubber.Xml(energy).Save(path);
                written.Add(path);
            }
        }

        return written;
    }

    private static string? Redact(string? value) => value is null ? null : BatteryReportScrubber.Redacted;

    private static string Slug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var chars = value.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length > 40 ? slug[..40].TrimEnd('-') : slug;
    }
}
