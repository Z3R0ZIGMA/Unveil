using System.Xml.Linq;
using Unveil.Core.Admin;
using Unveil.Core.Battery;
using Unveil.Core.SystemInfo;

namespace Unveil.Core.Tests.Admin;

public sealed class SampleExportTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "unveil-samples-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Export_writes_scrubbed_files_for_every_source()
    {
        var reportXml = XDocument.Parse("""
            <BatteryReport xmlns="http://schemas.microsoft.com/battery/2012">
              <ReportInformation><ReportGuid>37845d7f-e9dc-45df-affb-69398de4b60a</ReportGuid></ReportInformation>
              <SystemInformation><ComputerName>CLIENT-PC-07</ComputerName></SystemInformation>
              <Batteries><Battery><Id>DELL 0WRDW</Id><SerialNumber>SN-12345</SerialNumber><DesignCapacity>54000</DesignCapacity></Battery></Batteries>
            </BatteryReport>
            """);
        var snapshot = new BatterySnapshot
        {
            CollectedAt = TestData.Now,
            System = new SystemIdentity { Manufacturer = "Dell Inc.", Model = "Latitude 5440", SerialNumber = "SERVICETAG1" },
            Raw = new RawBatteryData
            {
                BatteryReport = new BatteryReportResult(BatteryReportParser.Parse(reportXml), reportXml),
                Wmi = [new WmiBatteryData { InstanceName = "B0", SerialNumber = "SN-12345", UniqueId = "UID-9" }],
            },
        };
        var admin = new AdminData
        {
            IsAdministrator = true,
            CollectedAt = TestData.Now,
            Raw = new AdminRaw
            {
                SrumCsv = "AppId, UserId, TimeStamp\n\\Device\\HarddiskVolume3\\Users\\jsmith\\app.exe, S-1-5-21-1-2-3-1001, 2026-11-14T07:00:00.0000\n",
                SleepStudy = XDocument.Parse("<SleepStudy><SystemInformation><ComputerName>CLIENT-PC-07</ComputerName></SystemInformation><Record UserId=\"jsmith\" /></SleepStudy>"),
                Energy = XDocument.Parse("<EnergyReport><LogEntry><Details><Detail><Name>SSID</Name><Value>Client Home WiFi</Value></Detail><Detail><Name>MAC Address</Name><Value>e8:78:29:14:b1:18</Value></Detail></Details></LogEntry></EnergyReport>"),
            },
        };

        var files = SampleExport.Save(_folder, snapshot, admin);

        Assert.Equal(5, files.Count);
        Assert.All(files, f => Assert.StartsWith("dell-inc-latitude-5440-20261114-", Path.GetFileName(f), StringComparison.Ordinal));
        var all = string.Join("\n", files.Select(File.ReadAllText));
        foreach (var secret in new[] { "SN-12345", "SERVICETAG1", "UID-9", "CLIENT-PC-07", "37845d7f", "jsmith", "1001", "Client Home WiFi", "e8:78:29" })
        {
            Assert.DoesNotContain(secret, all, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("54000", all); // the useful data is kept
    }

    [Fact]
    public void Export_without_admin_data_writes_battery_report_and_wmi_only()
    {
        var snapshot = new BatterySnapshot { CollectedAt = TestData.Now, System = new SystemIdentity { Manufacturer = "ASUSTeK", Model = "X" } };

        var files = SampleExport.Save(_folder, snapshot);

        Assert.Single(files); // no battery report in this snapshot: just the WMI values
        Assert.EndsWith("-wmi.json", files[0], StringComparison.Ordinal);
    }
}
