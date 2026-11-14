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

    public static VerdictSettings Default { get; } = new();
}

/// <summary>One app's share of the measured power impact. Filled by the process sampler (Phase 4).</summary>
public sealed record AppImpact(string Name, double SharePercent);

/// <summary>Result of the live drain test (Phase 4). Null until that test has been run.</summary>
public sealed record DrainMeasurement
{
    public double AverageWatts { get; init; }

    public TimeSpan Duration { get; init; }

    public int SampleCount { get; init; }

    /// <summary>Apps by power impact share, highest first.</summary>
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
