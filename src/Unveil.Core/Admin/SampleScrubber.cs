using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Unveil.Core.Admin;

/// <summary>
/// Removes personal data from admin report outputs before they are saved as test samples: the computer name,
/// the Windows user name, user folders in paths, account SIDs, serial numbers and report GUIDs.
/// </summary>
public static partial class SampleScrubber
{
    public const string Redacted = "REDACTED";

    /// <summary>Elements whose whole value is replaced.</summary>
    private static readonly HashSet<string> SensitiveElements = new(StringComparer.Ordinal) { "ComputerName", "SerialNumber" };

    /// <summary>Attributes whose whole value is replaced.</summary>
    private static readonly HashSet<string> SensitiveAttributes = new(StringComparer.Ordinal) { "UserId" };

    public static string Text(string text, IEnumerable<string>? names = null)
    {
        text = Sid().Replace(text, "S-1-5-21-" + Redacted);
        text = MacAddress().Replace(text, Redacted);
        text = UserFolder().Replace(text, "${prefix}" + Redacted);
        foreach (var name in (names ?? DefaultNames()).Where(n => n.Length >= 3))
        {
            text = Regex.Replace(text, Regex.Escape(name), Redacted, RegexOptions.IgnoreCase);
        }

        return text;
    }

    public static XDocument Xml(XDocument document, IEnumerable<string>? names = null)
    {
        var list = (names ?? DefaultNames()).ToList();
        var copy = new XDocument(document);

        // Energy report details such as "SSID: HomeWifi": the network name identifies the customer.
        foreach (var detail in copy.Descendants().Where(d => d.Name.LocalName == "Detail"))
        {
            var label = detail.Elements().FirstOrDefault(c => c.Name.LocalName == "Name")?.Value ?? "";
            if (label.Contains("SSID", StringComparison.OrdinalIgnoreCase)
                && detail.Elements().FirstOrDefault(c => c.Name.LocalName == "Value") is { } value
                && !string.IsNullOrWhiteSpace(value.Value))
            {
                value.Value = Redacted;
            }
        }

        foreach (var e in copy.Descendants())
        {
            var local = e.Name.LocalName;
            if (SensitiveElements.Contains(local) && !string.IsNullOrWhiteSpace(e.Value))
            {
                e.Value = Redacted;
            }
            else if (local == "ReportGuid")
            {
                e.Value = Guid.Empty.ToString();
            }
            else if (!e.HasElements && e.Value.Length > 0)
            {
                e.Value = Text(e.Value, list);
            }

            foreach (var a in e.Attributes())
            {
                a.Value = SensitiveAttributes.Contains(a.Name.LocalName) ? Redacted : Text(a.Value, list);
            }
        }

        return copy;
    }

    /// <summary>Keeps the last <paramref name="recent"/> in full and every <paramref name="olderEvery"/>th older row, so samples stay small.</summary>
    public static string TrimSrum(string csv, DateTimeOffset now, TimeSpan recent, int olderEvery = 40)
    {
        var lines = csv.Split('\n');
        if (lines.Length == 0)
        {
            return csv;
        }

        var cutoff = now - recent;
        var kept = new List<string> { lines[0].TrimEnd('\r') };
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var isRecent = TimeStamp().Match(line) is { Success: true } m
                && DateTimeOffset.TryParse(m.Value, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var t)
                && t >= cutoff;
            if (isRecent || i % olderEvery == 0)
            {
                kept.Add(line);
            }
        }

        return string.Join("\r\n", kept) + "\r\n";
    }

    private static IEnumerable<string> DefaultNames() => [Environment.UserName, Environment.MachineName];

    [GeneratedRegex(@"S-1-5-21-\d+-\d+-\d+-\d+")]
    private static partial Regex Sid();

    /// <summary>Network hardware addresses, e.g. e8:78:29:14:b1:18 or E8-78-29-14-B1-18 (Wi-Fi access points, adapters).</summary>
    [GeneratedRegex(@"\b(?:[0-9A-Fa-f]{2}[:-]){5}[0-9A-Fa-f]{2}\b")]
    public static partial Regex MacAddress();

    [GeneratedRegex(@"(?<prefix>\\Users\\)(?!REDACTED\\|Public\\|Default\\)[^\\""<>,]+", RegexOptions.IgnoreCase)]
    private static partial Regex UserFolder();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}")]
    private static partial Regex TimeStamp();
}
