using System.Xml.Linq;

namespace Unveil.Core.Battery;

/// <summary>
/// Removes personal data from a battery report so it can be kept as a test sample:
/// computer name, battery serial numbers and the report GUID.
/// </summary>
public static class BatteryReportScrubber
{
    public const string Redacted = "REDACTED";

    /// <summary>Elements whose text is replaced (when not empty).</summary>
    public static readonly IReadOnlyList<string> SensitiveElements = ["ComputerName", "SerialNumber", "ReportGuid"];

    /// <summary>Returns a scrubbed copy; the input is not changed.</summary>
    public static XDocument Scrub(XDocument report)
    {
        var copy = new XDocument(report);
        foreach (var element in copy.Descendants().Where(e => SensitiveElements.Contains(e.Name.LocalName)).ToList())
        {
            if (!string.IsNullOrWhiteSpace(element.Value))
            {
                element.Value = element.Name.LocalName == "ReportGuid"
                    ? Guid.Empty.ToString()
                    : Redacted;
            }
        }

        return copy;
    }

    /// <summary>True when no sensitive element holds a real value.</summary>
    public static bool IsScrubbed(XDocument report) =>
        report.Descendants()
            .Where(e => SensitiveElements.Contains(e.Name.LocalName))
            .All(e => string.IsNullOrWhiteSpace(e.Value) || e.Value == Redacted || e.Value == Guid.Empty.ToString());
}
