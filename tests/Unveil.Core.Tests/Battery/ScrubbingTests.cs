using System.Text.RegularExpressions;
using System.Text.Json;
using System.Xml.Linq;
using Unveil.Core.Battery;

namespace Unveil.Core.Tests.Battery;

public class ScrubbingTests
{
    [Fact]
    public void Scrub_removes_serials_computer_name_and_guid()
    {
        var doc = XDocument.Parse("""
            <BatteryReport xmlns="http://schemas.microsoft.com/battery/2012">
              <ReportInformation><ReportGuid>37845d7f-e9dc-45df-affb-69398de4b60a</ReportGuid></ReportInformation>
              <SystemInformation><ComputerName>TECH-LAPTOP-01</ComputerName></SystemInformation>
              <Batteries>
                <Battery><SerialNumber>ABC123</SerialNumber><DesignCapacity>50000</DesignCapacity></Battery>
                <Battery><SerialNumber></SerialNumber></Battery>
              </Batteries>
            </BatteryReport>
            """);

        var scrubbed = BatteryReportScrubber.Scrub(doc);
        var text = scrubbed.ToString();

        Assert.DoesNotContain("TECH-LAPTOP-01", text);
        Assert.DoesNotContain("ABC123", text);
        Assert.DoesNotContain("37845d7f", text);
        Assert.Contains("<DesignCapacity>50000</DesignCapacity>", text);
        Assert.True(BatteryReportScrubber.IsScrubbed(scrubbed));
        Assert.False(BatteryReportScrubber.IsScrubbed(doc));
        Assert.Contains("ABC123", doc.ToString()); // original untouched
    }

    /// <summary>
    /// Guard rail: nothing in /samples may contain a real serial number or computer name.
    /// If this fails, re-save the sample with `Unveil.Cli --save-report`.
    /// </summary>
    [Fact]
    public void SamplesAreScrubbed()
    {
        Assert.True(Directory.Exists(TestFiles.SamplesFolder), "samples folder was not copied to the test output");

        var xmlFiles = Directory.GetFiles(TestFiles.SamplesFolder, "*.xml", SearchOption.AllDirectories);
        Assert.NotEmpty(xmlFiles);
        foreach (var file in xmlFiles)
        {
            Assert.True(BatteryReportScrubber.IsScrubbed(XDocument.Load(file)), $"{Path.GetFileName(file)} contains unscrubbed personal data");
        }

        foreach (var file in Directory.GetFiles(TestFiles.SamplesFolder, "*.json", SearchOption.AllDirectories))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var value in SensitiveJsonValues(json.RootElement))
            {
                Assert.True(value is null or BatteryReportScrubber.Redacted, $"{Path.GetFileName(file)} contains an unscrubbed serial/ID: {value}");
            }
        }

        // Every sample (XML, CSV, JSON): no account SIDs, no real user folders, no real UserId attributes.
        var all = Directory.GetFiles(TestFiles.SamplesFolder, "*", SearchOption.AllDirectories);
        Assert.Contains(all, f => f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase));
        foreach (var file in all)
        {
            var text = File.ReadAllText(file);
            var name = Path.GetFileName(file);
            Assert.False(Regex.IsMatch(text, @"S-1-5-21-\d"), $"{name} contains a user account SID");
            var folder = Regex.Match(text, @"\\Users\\(?!REDACTED\\|Public\\|Default\\)[^\\""<>,]+\\", RegexOptions.IgnoreCase);
            Assert.False(folder.Success, $"{name} contains a user folder: {folder.Value}");
            var userId = Regex.Match(text, @"UserId=""(?!REDACTED"")[^""]+""");
            Assert.False(userId.Success, $"{name} contains a user ID: {userId.Value}");
        }
    }

    private static readonly string[] SensitiveJsonProperties = ["SerialNumber", "UniqueId", "ComputerName"];

    private static IEnumerable<string?> SensitiveJsonValues(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject())
                {
                    if (SensitiveJsonProperties.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        yield return p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.ToString();
                    }
                    else
                    {
                        foreach (var v in SensitiveJsonValues(p.Value))
                        {
                            yield return v;
                        }
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var v in SensitiveJsonValues(item))
                    {
                        yield return v;
                    }
                }

                break;
        }
    }
}
