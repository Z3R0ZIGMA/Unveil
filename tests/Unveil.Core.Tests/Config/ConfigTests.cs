using Unveil.Core.Config;
using Unveil.Core.Power;
using Unveil.Core.Verdict;

namespace Unveil.Core.Tests.Config;

public sealed class ConfigTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "unveil-config-test-" + Guid.NewGuid().ToString("N"));

    public ConfigTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        KnownOffenders.SetCustom([]);
        Directory.Delete(_folder, recursive: true);
    }

    private string File(string json)
    {
        var path = Path.Combine(_folder, UnveilConfig.FileName);
        System.IO.File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void Missing_file_is_created_with_the_defaults()
    {
        var source = ConfigLoader.Load(_folder);

        Assert.True(source.Created);
        Assert.True(System.IO.File.Exists(source.Path));
        Assert.Equal(UnveilConfig.Default.Health, source.Config.Health);

        var reread = ConfigLoader.Load(_folder);
        Assert.False(reread.Created);
        Assert.Equal(60, reread.Config.Drain.SampleSeconds);
        Assert.Equal(500, reread.Config.Health.CycleCountRating);
    }

    [Fact]
    public void Default_file_round_trips_and_reads_like_the_built_in_defaults()
    {
        var json = ConfigLoader.Serialize(UnveilConfig.Default);
        File(json);

        var config = ConfigLoader.Load(_folder).Config;

        Assert.Contains("\"cycleCountRating\": 500", json);
        Assert.Equal(VerdictSettings.Default.For(MachineClass.ThinAndLight), config.ToVerdictSettings(MachineClass.ThinAndLight));
        Assert.Equal(VerdictSettings.Default.For(MachineClass.Performance), config.ToVerdictSettings(MachineClass.Performance));
        Assert.Equal(Unveil.Core.SystemInfo.SettingsThresholds.Default, config.ToSettingsThresholds());
    }

    [Fact]
    public void Partial_file_keeps_defaults_for_everything_else()
    {
        File("""
            {
              // comments are allowed
              "health": { "cycleCountRating": 1000 },
              "drain": { "laptopType": "standard", "sampleSeconds": 90 },
            }
            """);

        var source = ConfigLoader.Load(_folder);
        var c = source.Config;

        Assert.Empty(source.Log);
        Assert.Equal(1000, c.Health.CycleCountRating);
        Assert.Equal(80, c.Health.GoodPercent);
        Assert.Equal(MachineClass.Standard, c.DefaultLaptopType);
        Assert.Equal(TimeSpan.FromSeconds(90), c.ToDrainTestOptions().Duration);
        Assert.Equal(12, c.ToVerdictSettings(c.DefaultLaptopType).ElevatedDrainWatts);
    }

    [Fact]
    public void Custom_drain_limits()
    {
        File("""
            { "drain": { "thinAndLight": { "elevatedWatts": 6, "highWatts": 12 } } }
            """);

        var c = ConfigLoader.Load(_folder).Config;

        Assert.Equal(6, c.ToVerdictSettings(MachineClass.ThinAndLight).ElevatedDrainWatts);
        Assert.Equal(12, c.ToVerdictSettings(MachineClass.ThinAndLight).HighDrainWatts);
    }

    [Fact]
    public void Invalid_values_fall_back_to_defaults_with_a_warning()
    {
        File("""
            { "health": { "goodPercent": 150, "replacePercent": 90, "cycleCountRating": -5 },
              "drain": { "sampleSeconds": 2, "standard": { "elevatedWatts": 20, "highWatts": 10 } } }
            """);

        var source = ConfigLoader.Load(_folder);
        var c = source.Config;

        Assert.Equal(80, c.Health.GoodPercent);
        Assert.Equal(60, c.Health.ReplacePercent); // 90 is not below the good threshold
        Assert.Equal(500, c.Health.CycleCountRating);
        Assert.Equal(60, c.Drain.SampleSeconds);
        Assert.Equal(new DrainLimits(12, 20), c.Drain.Standard);
        Assert.Equal(5, source.Log.Count(e => e.Level == LogLevel.Warning));
    }

    [Fact]
    public void Broken_json_uses_defaults_and_is_not_overwritten()
    {
        var path = File("{ this is not json");

        var source = ConfigLoader.Load(_folder);

        Assert.Equal(UnveilConfig.Default.Health, source.Config.Health);
        Assert.Contains(source.Log, e => e.Level == LogLevel.Warning && e.Message.Contains("could not be read"));
        Assert.Equal("{ this is not json", System.IO.File.ReadAllText(path));
    }

    [Fact]
    public void Missing_explicit_config_file_is_not_created()
    {
        var path = Path.Combine(_folder, "other.json");

        var source = ConfigLoader.LoadFile(path);

        Assert.False(System.IO.File.Exists(path));
        Assert.Contains(source.Log, e => e.Message.Contains("was not found"));
    }

    [Fact]
    public void Custom_known_offenders_are_added_and_win()
    {
        File("""
            { "knownOffenders": [
                { "category": "Customer line-of-business app", "advice": "Ask the vendor about its battery use.", "processes": ["lobclient.exe", "TiWorker"] }
            ] }
            """);

        ConfigLoader.Load(_folder).Config.ApplyKnownOffenders();

        Assert.Equal("Customer line-of-business app", KnownOffenders.Match("LOBClient", 1)?.Category);
        Assert.Equal("Customer line-of-business app", KnownOffenders.Match("TiWorker", 1)?.Category); // overrides the built-in entry
        Assert.Equal("Microsoft Teams", KnownOffenders.Match("ms-teams", 1)?.Category); // built-ins still work
    }
}
