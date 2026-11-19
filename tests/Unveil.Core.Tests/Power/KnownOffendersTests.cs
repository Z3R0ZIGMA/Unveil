using Unveil.Core.Power;

namespace Unveil.Core.Tests.Power;

public class KnownOffendersTests
{
    [Theory]
    [InlineData("ms-teams", "Microsoft Teams")]
    [InlineData("OneDrive", "OneDrive syncing")]
    [InlineData("SearchIndexer", "Windows Search indexer")]
    [InlineData("MsMpEng", "Antivirus scan")]
    [InlineData("TiWorker", "Windows Update")]
    [InlineData("xmrig", "Crypto miner")]
    [InlineData("iCUE", "RGB / OEM utility")]
    [InlineData("TiWorker.exe", "Windows Update")]
    [InlineData("iscanruntime", "Management / security agent")]
    [InlineData("winagent", "Management / security agent")]
    [InlineData("msp-agent-core", "Management / security agent")]
    [InlineData("BASupSrvc", "Management / security agent")]
    [InlineData("NinjaRMMAgent", "Management / security agent")]
    [InlineData("LTSVC", "Management / security agent")]
    [InlineData("EPHost", "Antivirus scan")]
    [InlineData("EPSecurityService", "Antivirus scan")]
    [InlineData("ManagedAntivirus", "Antivirus scan")]
    [InlineData("AdobeCollabSync", "Adobe Acrobat sync")]
    [InlineData("AnyDesk", "Remote access tool")]
    [InlineData("TeamViewer", "Remote access tool")]
    [InlineData("rdpclip", "Remote access tool")]
    [InlineData("AsusSystemAnalysis", "RGB / OEM utility")]
    [InlineData("AsusCcSystemAnalysis", "RGB / OEM utility")]
    public void Known_names_are_matched_case_insensitively(string name, string category)
    {
        Assert.Equal(category, KnownOffenders.Match(name, 1)?.Category);
    }

    [Fact]
    public void Unknown_apps_are_not_offenders()
    {
        Assert.Null(KnownOffenders.Match("notepad", 1));
    }

    [Fact]
    public void Browser_is_only_flagged_with_many_processes()
    {
        Assert.Null(KnownOffenders.Match("chrome", KnownOffenders.ManyBrowserProcesses - 1));

        var offender = KnownOffenders.Match("msedge", 40);
        Assert.Equal("Browser with many tabs", offender?.Category);
        Assert.Contains("40 processes", offender!.Advice);
    }
}

public class AppNamesTests
{
    [Theory]
    [InlineData("SnippingTool.exe", "SnippingTool")]
    [InlineData("  Google Chrome ", "Google Chrome")]
    [InlineData("Windows Explorer", "Windows Explorer")]
    [InlineData(null, null)]
    public void Descriptions_are_cleaned(string? description, string? expected)
    {
        Assert.Equal(expected, AppNames.Clean(description));
    }
}
