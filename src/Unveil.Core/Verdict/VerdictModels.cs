namespace Unveil.Core.Verdict;

/// <summary>Traffic-light level. UI/report map these to Success / Warning / Danger / Muted.</summary>
public enum Severity
{
    Good,
    Warning,
    Danger,
    Neutral,
}

/// <summary>Health band from plan.md section 4.</summary>
public enum HealthLabel
{
    Good,
    Worn,
    Replace,
    Unknown,
}

/// <summary>Overall battery condition after all replace triggers are applied.</summary>
public enum BatteryCondition
{
    Good,
    Worn,
    Replace,
    Unknown,
}

public enum DrainStatus
{
    /// <summary>The live drain test has not been run.</summary>
    NotTested,
    Normal,
    Elevated,
    High,
}

public enum VerdictKind
{
    Healthy,
    SoftwareDrain,
    BatteryWorn,
    ReplaceBattery,
    Both,
    Inconclusive,
    NoBattery,
}

/// <summary>Thresholds for the verdict. Defaults from plan.md; made configurable via unveil.json in Phase 7.</summary>
public sealed record VerdictSettings
{
    /// <summary>Health at or above this is Good.</summary>
    public double GoodHealthPercent { get; init; } = 80;

    /// <summary>Health below this is Replace; between this and Good is Worn.</summary>
    public double ReplaceHealthPercent { get; init; } = 60;

    /// <summary>Cycle count above this triggers Replace.</summary>
    public int CycleCountRating { get; init; } = 500;

    /// <summary>A sustained drop in full charge capacity larger than this (%) triggers Replace.</summary>
    public double CapacityDropPercent { get; init; } = 10;

    public int CapacityDropWindowDays { get; init; } = 30;

    /// <summary>How many of the most recent history entries must all show the drop.</summary>
    public int CapacityDropSustainedEntries { get; init; } = 3;

    /// <summary>Live drain at or above this is Elevated (ultrabook default).</summary>
    public double ElevatedDrainWatts { get; init; } = 8;

    /// <summary>Live drain above this is High (ultrabook default).</summary>
    public double HighDrainWatts { get; init; } = 15;

    /// <summary>One app above this share of the power impact while idle makes drain Elevated.</summary>
    public double DominantAppSharePercent { get; init; } = 25;

    /// <summary>
    /// ...but only if that app's Power Impact is at least this (about 2.5% of the whole CPU), so a big share of a
    /// near-idle machine does not count as a problem.
    /// </summary>
    public double DominantAppMinimumImpact { get; init; } = 1.5;

    /// <summary>Asleep on battery, using more than this % of a charge per hour counts as high sleep drain (Phase 5).</summary>
    public double HighSleepDrainPercentPerHour { get; init; } = 1.5;

    /// <summary>A sleep session spending less than this % of its time in hardware deep sleep is reported.</summary>
    public double MinimumDeepSleepPercent { get; init; } = 80;

    /// <summary>Name of the drain thresholds in use, for reports.</summary>
    public string DrainProfileName { get; init; } = MachineClasses.Describe(MachineClass.ThinAndLight);

    public static VerdictSettings Default { get; } = new();

    /// <summary>These settings with the drain thresholds for a type of laptop.</summary>
    public VerdictSettings For(MachineClass machineClass)
    {
        var (elevated, high) = MachineClasses.Thresholds(machineClass);
        return this with
        {
            ElevatedDrainWatts = elevated,
            HighDrainWatts = high,
            DrainProfileName = MachineClasses.Describe(machineClass),
        };
    }
}

/// <summary>Type of laptop, which sets what counts as a normal power draw.</summary>
public enum MachineClass
{
    ThinAndLight,
    Standard,
    Performance,
}

public static class MachineClasses
{
    /// <summary>
    /// Elevated / high drain thresholds in watts. Thin and light comes from plan.md; the other two are defaults
    /// until unveil.json (Phase 7) makes them configurable.
    /// </summary>
    public static (double Elevated, double High) Thresholds(MachineClass c) => c switch
    {
        MachineClass.Standard => (12, 20),
        MachineClass.Performance => (20, 35),
        _ => (8, 15),
    };

    public static string Describe(MachineClass c) => c switch
    {
        MachineClass.Standard => "Standard laptop",
        MachineClass.Performance => "Gaming or workstation laptop",
        _ => "Thin and light laptop",
    };
}

/// <summary>One app's part in the drain test, from the process sampler.</summary>
/// <param name="Name">Display name.</param>
/// <param name="SharePercent">Share of the total Power Impact, 0-100.</param>
/// <param name="Impact">Absolute Power Impact score (roughly: 1.5 = 2.5% of the whole CPU).</param>
/// <param name="Category">Known-offender category, if any (e.g. "Windows Update").</param>
/// <param name="Advice">What to do about it, if it is a known offender.</param>
public sealed record AppImpact(string Name, double SharePercent, double Impact = 10, string? Category = null, string? Advice = null);

/// <summary>Result of the live drain test. Null until that test has been run.</summary>
public sealed record DrainMeasurement
{
    /// <summary>Average discharge in watts while on battery; null when the charger stayed connected.</summary>
    public double? AverageWatts { get; init; }

    public double? MinWatts { get; init; }

    public double? MaxWatts { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>Battery readings taken while on battery.</summary>
    public int SampleCount { get; init; }

    /// <summary>True when the charger was connected for some or all of the test.</summary>
    public bool PluggedIn { get; init; }

    /// <summary>Apps by Power Impact share, highest first.</summary>
    public IReadOnlyList<AppImpact> TopApps { get; init; } = [];
}

/// <summary>One piece of evidence behind the verdict.</summary>
public sealed record Finding(Severity Severity, string Text);

public sealed record Verdict
{
    public VerdictKind Kind { get; init; }

    public Severity Severity { get; init; }

    /// <summary>Short badge text, e.g. "Replace battery".</summary>
    public required string Title { get; init; }

    /// <summary>One or two plain-English sentences for the customer.</summary>
    public required string Message { get; init; }

    public BatteryCondition Battery { get; init; }

    public DrainStatus Drain { get; init; }

    /// <summary>Health % used in the message (combined when there are several batteries).</summary>
    public double? HealthPercent { get; init; }

    public IReadOnlyList<Finding> Evidence { get; init; } = [];

    public IReadOnlyList<string> Actions { get; init; } = [];

    /// <summary>CLI exit code (plan.md section 7, plus 4 = inconclusive).</summary>
    public int ExitCode { get; init; }
}

public static class ExitCodes
{
    public const int Healthy = 0;
    public const int SoftwareDrain = 1;
    public const int Battery = 2;
    public const int Both = 3;
    public const int Inconclusive = 4;
    public const int NoBattery = 10;
    public const int Error = 20;
}
