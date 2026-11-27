using System.Runtime.InteropServices;
using Unveil.Core.Updates;

namespace Unveil.Core.Tests.Updates;

/// <summary>"Check for updates": reading the latest release, comparing versions, checksums and swapping the exe.</summary>
public class UpdateTests
{
    private const string Download = "https://github.com/Z3R0ZIGMA/Unveil/releases/download/v0.9.5/";

    private static string Release(string tag = "v0.9.5", bool draft = false, bool prerelease = false, string? sumsUrl = Download + "SHA256SUMS.txt",
        string exeUrl = Download + "Unveil.exe") => $$"""
        {
          "tag_name": "{{tag}}", "draft": {{draft.ToString().ToLowerInvariant()}}, "prerelease": {{prerelease.ToString().ToLowerInvariant()}},
          "html_url": "https://github.com/Z3R0ZIGMA/Unveil/releases/tag/{{tag}}",
          "assets": [
            { "name": "Unveil.exe", "browser_download_url": "{{exeUrl}}" },
            { "name": "Unveil-arm64.exe", "browser_download_url": "{{Download}}Unveil-arm64.exe" },
            { "name": "Unveil-NSight.ps1", "browser_download_url": "{{Download}}Unveil-NSight.ps1" }
            {{(sumsUrl is null ? "" : $$""", { "name": "SHA256SUMS.txt", "browser_download_url": "{{sumsUrl}}" }""")}}
          ]
        }
        """;

    [Fact]
    public void ParseLatest_FindsTheVersionAndTheFilesForThisPc()
    {
        var x64 = AppUpdate.ParseLatest(Release(), "Unveil.exe")!;
        Assert.Equal((new Version(0, 9, 5), "v0.9.5"), (x64.Version, x64.Tag));
        Assert.Equal(Download + "Unveil.exe", x64.AssetUrl);
        Assert.Equal(Download + "SHA256SUMS.txt", x64.SumsUrl);
        Assert.EndsWith("/releases/tag/v0.9.5", x64.PageUrl, StringComparison.Ordinal);

        Assert.Equal(Download + "Unveil-arm64.exe", AppUpdate.ParseLatest(Release(), "Unveil-arm64.exe")!.AssetUrl);
        Assert.Equal("Unveil-arm64.exe", AppUpdate.AssetName(Architecture.Arm64));
        Assert.Equal("Unveil.exe", AppUpdate.AssetName(Architecture.X64));
    }

    [Fact]
    public void ParseLatest_RefusesWhatCannotBeInstalledSafely()
    {
        Assert.Null(AppUpdate.ParseLatest(Release(sumsUrl: null), "Unveil.exe"));          // nothing to check it against
        Assert.Null(AppUpdate.ParseLatest(Release(draft: true), "Unveil.exe"));
        Assert.Null(AppUpdate.ParseLatest(Release(prerelease: true), "Unveil.exe"));
        Assert.Null(AppUpdate.ParseLatest(Release(tag: "latest-build"), "Unveil.exe"));
        // Only this project's own release downloads, never a file somewhere else.
        Assert.Null(AppUpdate.ParseLatest(Release(exeUrl: "https://example.com/Unveil.exe"), "Unveil.exe"));
        Assert.Null(AppUpdate.ParseLatest(Release(exeUrl: "https://github.com/someone-else/Unveil/releases/download/v0.9.5/Unveil.exe"), "Unveil.exe"));
    }

    [Theory]
    [InlineData("0.6.4", "0.6.3.0", true)]
    [InlineData("0.7.0", "0.6.9.0", true)]
    [InlineData("1.0.0", "0.9.9.0", true)]
    [InlineData("0.6.3", "0.6.3.0", false)]
    [InlineData("0.6.2", "0.6.3.0", false)]
    public void IsNewer(string latest, string current, bool newer) =>
        Assert.Equal(newer, AppUpdate.IsNewer(Version.Parse(latest), Version.Parse(current)));

    [Fact]
    public void ExpectedHash_ReadsTheSumsFile()
    {
        const string sums = "e051aea014ef90613947d199b3bf30e9ce559f1ddc7ca5bd9297d3221303e24c  Unveil.exe\n" +
                            "2736F4C2A609FFE3752BB2167B3636CBB7608AF76EE84B66F63C6A7224BF4E56 *Unveil-arm64.exe\r\n" +
                            "not a hash  Unveil-NSight.ps1\n";
        Assert.Equal("e051aea014ef90613947d199b3bf30e9ce559f1ddc7ca5bd9297d3221303e24c", AppUpdate.ExpectedHash(sums, "Unveil.exe"));
        Assert.Equal("2736f4c2a609ffe3752bb2167b3636cbb7608af76ee84b66f63c6a7224bf4e56", AppUpdate.ExpectedHash(sums, "Unveil-arm64.exe"));
        Assert.Null(AppUpdate.ExpectedHash(sums, "Unveil-NSight.ps1"));
        Assert.Null(AppUpdate.ExpectedHash(sums, "Other.exe"));
    }

    [Fact]
    public void Install_SwapsTheExeAndCleanUpRemovesTheOldOne()
    {
        var folder = Path.Combine(Path.GetTempPath(), "unveil-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var exe = Path.Combine(folder, "Unveil.exe");
            File.WriteAllText(exe, "old version");
            File.WriteAllText(exe + ".new", "new version");

            AppUpdate.Install(exe + ".new", exe);
            Assert.Equal("new version", File.ReadAllText(exe));
            Assert.Equal("old version", File.ReadAllText(exe + ".old"));
            Assert.False(File.Exists(exe + ".new"));

            AppUpdate.CleanUpOldVersion(exe);
            Assert.False(File.Exists(exe + ".old"));
            Assert.True(File.Exists(exe));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Install_PutsTheOldExeBackWhenTheNewOneCannotBeMoved()
    {
        var folder = Path.Combine(Path.GetTempPath(), "unveil-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var exe = Path.Combine(folder, "Unveil.exe");
            File.WriteAllText(exe, "old version");
            Assert.ThrowsAny<IOException>(() => AppUpdate.Install(Path.Combine(folder, "missing.new"), exe));
            Assert.Equal("old version", File.ReadAllText(exe));
            Assert.False(File.Exists(exe + ".old"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
