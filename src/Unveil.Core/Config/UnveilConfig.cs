using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Unveil.Core.Power;
using Unveil.Core.Reports;
using Unveil.Core.SystemInfo;
using Unveil.Core.Verdict;

namespace Unveil.Core.Config;

/// <summary>
/// unveil.json, next to the exe (plan.md section 8). Created with the defaults on first run.
/// Every value is optional: anything missing or invalid falls back to the default.
/// </summary>
public sealed record UnveilConfig
{
    public const string FileName = "unveil.json";

    public HealthConfig Health { get; init; } = new();

    public DrainConfig Drain { get; init; } = new();

    public SleepConfig Sleep { get; init; } = new();

    public SettingsConfig Settings { get; init; } = new();

    /// <summary>Extra apps to flag, on top of the built-in list (these win when a name is in both).</summary>
    public IReadOnlyList<OffenderConfig> KnownOffenders { get; init; } = [];

    public static UnveilConfig Default { get; } = new();

    // ------------------------------------------------------------ mapping to the engine

    public MachineClass DefaultLaptopType => Drain.LaptopType?.Trim().ToLowerInvariant() switch
    {
        "standard" => MachineClass.Standard,
        "performance" or "gaming" or "workstation" => MachineClass.Performance,
        _ => MachineClass.ThinAndLight,
    };

    public VerdictSettings ToVerdictSettings(MachineClass laptopType)
    {
        var limits = laptopType switch
        {
            MachineClass.Standard => Drain.Standard,
            MachineClass.Performance => Drain.Performance,
            _ => Drain.ThinAndLight,
        };

        return new VerdictSettings
        {
            GoodHealthPercent = Health.GoodPercent,
            ReplaceHealthPercent = Health.ReplacePercent,
            CycleCountRating = Health.CycleCountRating,
            CapacityDropPercent = Health.CapacityDropPercent,
            CapacityDropWindowDays = Health.CapacityDropWindowDays,
            ElevatedDrainWatts = limits.ElevatedWatts,
            HighDrainWatts = limits.HighWatts,
            DrainProfileName = MachineClasses.Describe(laptopType),
            DominantAppSharePercent = Drain.DominantAppSharePercent,
            IdleCpuPercent = Drain.IdleCpuPercent,
            HighSleepDrainPercentPerHour = Sleep.HighDrainPercentPerHour,
            MinimumDeepSleepPercent = Sleep.MinimumDeepSleepPercent,
        };
    }

    public SettingsThresholds ToSettingsThresholds() => new()
    {
        HighBrightnessPercent = Settings.HighBrightnessPercent,
        HighRefreshRateHz = Settings.HighRefreshRateHz,
        LongScreenOffOnBattery = TimeSpan.FromMinutes(Settings.ScreenOffMinutes),
        LongSleepOnBattery = TimeSpan.FromMinutes(Settings.SleepMinutes),
        ManyStartupApps = Settings.ManyStartupApps,
        LongUptime = TimeSpan.FromDays(Settings.RestartDays),
    };

    public DrainTestOptions ToDrainTestOptions() => new() { Duration = TimeSpan.FromSeconds(Drain.SampleSeconds) };

    /// <summary>Registers the custom known offenders with the process scorer.</summary>
    public void ApplyKnownOffenders() =>
        Power.KnownOffenders.SetCustom(KnownOffenders
            .Where(o => !string.IsNullOrWhiteSpace(o.Category) && o.Processes.Count > 0)
            .Select(o => (o.Category, o.Advice ?? "", (IEnumerable<string>)o.Processes)));
}

public sealed record HealthConfig
{
    public double GoodPercent { get; init; } = 80;

    public double ReplacePercent { get; init; } = 60;

    public int CycleCountRating { get; init; } = 500;

    public double CapacityDropPercent { get; init; } = 10;

    public int CapacityDropWindowDays { get; init; } = 30;
}

public sealed record DrainLimits(double ElevatedWatts, double HighWatts);

public sealed record DrainConfig
{
    /// <summary>"thin", "standard" or "performance": the laptop type selected by default.</summary>
    public string? LaptopType { get; init; } = "thin";

    /// <summary>Length of the live drain test (plan.md: 60 s).</summary>
    public int SampleSeconds { get; init; } = 60;

    public DrainLimits ThinAndLight { get; init; } = new(8, 15);

    public DrainLimits Standard { get; init; } = new(12, 20);

    public DrainLimits Performance { get; init; } = new(20, 35);

    public double DominantAppSharePercent { get; init; } = 25;

    public double IdleCpuPercent { get; init; } = 10;
}

public sealed record SleepConfig
{
    public double HighDrainPercentPerHour { get; init; } = 1.5;

    public double MinimumDeepSleepPercent { get; init; } = 80;
}

public sealed record SettingsConfig
{
    public int HighBrightnessPercent { get; init; } = 80;

    public int HighRefreshRateHz { get; init; } = 120;

    public double ScreenOffMinutes { get; init; } = 10;

    public double SleepMinutes { get; init; } = 30;

    public int ManyStartupApps { get; init; } = 8;

    public double RestartDays { get; init; } = 7;
}

public sealed record OffenderConfig
{
    public string Category { get; init; } = "";

    public string? Advice { get; init; }

    /// <summary>Executable names, with or without ".exe".</summary>
    public IReadOnlyList<string> Processes { get; init; } = [];
}

/// <summary>Where the config came from, for the "Data notes" and the CLI.</summary>
public sealed record ConfigSource(UnveilConfig Config, string? Path, bool Created, IReadOnlyList<LogEntry> Log);

public static class ConfigLoader
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>The folder the exe is in (also right for single-file publish).</summary>
    public static string ExeFolder => System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    /// <summary>
    /// Loads <c>unveil.json</c> from <paramref name="folder"/>. Missing: writes the defaults there (best effort, e.g. not
    /// in Program Files) and uses them. Invalid: logs it and uses the defaults without overwriting the file.
    /// </summary>
    public static ConfigSource Load(string folder) => LoadFile(System.IO.Path.Combine(folder, UnveilConfig.FileName), createIfMissing: true);

    /// <summary>Loads a specific config file. A missing file is only created when <paramref name="createIfMissing"/>.</summary>
    public static ConfigSource LoadFile(string path, bool createIfMissing = false)
    {
        var log = new CollectionLog();

        if (!File.Exists(path) && !createIfMissing)
        {
            log.Warning("Config", $"{path} was not found; using the defaults.");
            return new ConfigSource(UnveilConfig.Default, null, false, log.Entries);
        }

        if (!File.Exists(path))
        {
            try
            {
                File.WriteAllText(path, Serialize(UnveilConfig.Default), new UTF8Encoding(false));
                log.Info("Config", $"Created {UnveilConfig.FileName} with the default settings.");
                return new ConfigSource(UnveilConfig.Default, path, true, log.Entries);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Info("Config", $"No {UnveilConfig.FileName}, and it could not be created here ({ex.Message}); using the defaults.");
                return new ConfigSource(UnveilConfig.Default, null, false, log.Entries);
            }
        }

        try
        {
            var config = JsonSerializer.Deserialize<UnveilConfig>(File.ReadAllText(path), Json) ?? UnveilConfig.Default;
            return new ConfigSource(Validate(config, log), path, false, log.Entries);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            log.Warning("Config", $"{UnveilConfig.FileName} could not be read ({ex.Message}); using the defaults. Fix or delete the file.");
            return new ConfigSource(UnveilConfig.Default, path, false, log.Entries);
        }
    }

    public static string Serialize(UnveilConfig config) => JsonSerializer.Serialize(config, Json);

    /// <summary>Replaces values that make no sense with the defaults, and says so.</summary>
    internal static UnveilConfig Validate(UnveilConfig c, CollectionLog log)
    {
        var d = UnveilConfig.Default;

        T Check<T>(T value, T fallback, bool ok, string name)
        {
            if (ok)
            {
                return value;
            }

            log.Warning("Config", $"{name} = {value} is not valid; using {fallback}.");
            return fallback;
        }

        DrainLimits Limits(DrainLimits? l, DrainLimits fallback, string name) =>
            l is not null && l.ElevatedWatts > 0 && l.HighWatts > l.ElevatedWatts
                ? l
                : Check(l ?? fallback, fallback, false, $"drain.{name}");

        var health = c.Health ?? d.Health;
        var good = Check(health.GoodPercent, d.Health.GoodPercent, health.GoodPercent is > 0 and <= 100, "health.goodPercent");
        var drain = c.Drain ?? d.Drain;
        var sleep = c.Sleep ?? d.Sleep;
        var settings = c.Settings ?? d.Settings;

        return c with
        {
            Health = health with
            {
                GoodPercent = good,
                ReplacePercent = Check(health.ReplacePercent, Math.Min(d.Health.ReplacePercent, good - 1),
                    health.ReplacePercent > 0 && health.ReplacePercent < good, "health.replacePercent"),
                CycleCountRating = Check(health.CycleCountRating, d.Health.CycleCountRating, health.CycleCountRating > 0, "health.cycleCountRating"),
                CapacityDropPercent = Check(health.CapacityDropPercent, d.Health.CapacityDropPercent, health.CapacityDropPercent is > 0 and < 100, "health.capacityDropPercent"),
                CapacityDropWindowDays = Check(health.CapacityDropWindowDays, d.Health.CapacityDropWindowDays, health.CapacityDropWindowDays is > 0 and <= 365, "health.capacityDropWindowDays"),
            },
            Drain = drain with
            {
                SampleSeconds = Check(drain.SampleSeconds, d.Drain.SampleSeconds, drain.SampleSeconds is >= 10 and <= 600, "drain.sampleSeconds"),
                ThinAndLight = Limits(drain.ThinAndLight, d.Drain.ThinAndLight, "thinAndLight"),
                Standard = Limits(drain.Standard, d.Drain.Standard, "standard"),
                Performance = Limits(drain.Performance, d.Drain.Performance, "performance"),
                DominantAppSharePercent = Check(drain.DominantAppSharePercent, d.Drain.DominantAppSharePercent, drain.DominantAppSharePercent is > 0 and <= 100, "drain.dominantAppSharePercent"),
                IdleCpuPercent = Check(drain.IdleCpuPercent, d.Drain.IdleCpuPercent, drain.IdleCpuPercent is >= 0 and <= 100, "drain.idleCpuPercent"),
            },
            Sleep = sleep with
            {
                HighDrainPercentPerHour = Check(sleep.HighDrainPercentPerHour, d.Sleep.HighDrainPercentPerHour, sleep.HighDrainPercentPerHour > 0, "sleep.highDrainPercentPerHour"),
                MinimumDeepSleepPercent = Check(sleep.MinimumDeepSleepPercent, d.Sleep.MinimumDeepSleepPercent, sleep.MinimumDeepSleepPercent is >= 0 and <= 100, "sleep.minimumDeepSleepPercent"),
            },
            Settings = settings with
            {
                HighBrightnessPercent = Check(settings.HighBrightnessPercent, d.Settings.HighBrightnessPercent, settings.HighBrightnessPercent is > 0 and <= 100, "settings.highBrightnessPercent"),
                HighRefreshRateHz = Check(settings.HighRefreshRateHz, d.Settings.HighRefreshRateHz, settings.HighRefreshRateHz > 0, "settings.highRefreshRateHz"),
                ScreenOffMinutes = Check(settings.ScreenOffMinutes, d.Settings.ScreenOffMinutes, settings.ScreenOffMinutes > 0, "settings.screenOffMinutes"),
                SleepMinutes = Check(settings.SleepMinutes, d.Settings.SleepMinutes, settings.SleepMinutes > 0, "settings.sleepMinutes"),
                ManyStartupApps = Check(settings.ManyStartupApps, d.Settings.ManyStartupApps, settings.ManyStartupApps > 0, "settings.manyStartupApps"),
                RestartDays = Check(settings.RestartDays, d.Settings.RestartDays, settings.RestartDays > 0, "settings.restartDays"),
            },
            KnownOffenders = c.KnownOffenders ?? [],
        };
    }
}
