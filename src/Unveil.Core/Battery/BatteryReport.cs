namespace Unveil.Core.Battery;

/// <summary>Parsed output of <c>powercfg /batteryreport /xml</c>.</summary>
public sealed record BatteryReport
{
    public string? ReportVersion { get; init; }

    public DateTimeOffset? ScanTime { get; init; }

    public ReportSystemInfo System { get; init; } = new();

    public IReadOnlyList<ReportBattery> Batteries { get; init; } = [];

    public RuntimeEstimates? RuntimeEstimates { get; init; }

    /// <summary>Full charge capacity over time (one entry per day/week). Oldest first.</summary>
    public IReadOnlyList<CapacityHistoryEntry> CapacityHistory { get; init; } = [];

    public IReadOnlyList<UsageEntry> RecentUsage { get; init; } = [];

    /// <summary>Periods spent on battery power. Oldest first.</summary>
    public IReadOnlyList<EnergyDrain> EnergyDrains { get; init; } = [];
}

public sealed record ReportSystemInfo
{
    public string? Manufacturer { get; init; }

    public string? ProductName { get; init; }

    public string? BiosVersion { get; init; }

    public string? BiosDate { get; init; }

    public string? OsBuild { get; init; }

    /// <summary>"Mobile", "Desktop", "Slate" etc.</summary>
    public string? PlatformRole { get; init; }

    /// <summary>True when the machine uses Modern Standby (Connected Standby).</summary>
    public bool? ModernStandby { get; init; }
}

public sealed record ReportBattery
{
    /// <summary>Battery name as Windows reports it; often the model or FRU part number.</summary>
    public string? Id { get; init; }

    public string? Manufacturer { get; init; }

    public string? SerialNumber { get; init; }

    /// <summary>Raw text; the format varies by OEM.</summary>
    public string? ManufactureDate { get; init; }

    /// <summary>Raw chemistry code, e.g. "LIon".</summary>
    public string? Chemistry { get; init; }

    public bool? LongTerm { get; init; }

    /// <summary>True when capacities are relative units, not mWh.</summary>
    public bool IsRelativeCapacity { get; init; }

    public int? DesignCapacity { get; init; }

    public int? FullChargeCapacity { get; init; }

    public int? CycleCount { get; init; }
}

public sealed record RuntimeEstimate(int? Capacity, TimeSpan? ActiveRuntime, TimeSpan? ModernStandbyRuntime);

public sealed record RuntimeEstimates(RuntimeEstimate? AtDesignCapacity, RuntimeEstimate? AtFullChargeCapacity);

public sealed record CapacityHistoryEntry
{
    public DateTimeOffset? Start { get; init; }

    public DateTimeOffset? End { get; init; }

    public int? DesignCapacity { get; init; }

    public int? FullChargeCapacity { get; init; }

    public int? CycleCount { get; init; }

    public bool BatteryChanged { get; init; }

    public TimeSpan? EstimatedDesignActiveTime { get; init; }

    public TimeSpan? EstimatedFullChargeActiveTime { get; init; }

    public HealthResult Health => BatteryHealth.Calculate(DesignCapacity, FullChargeCapacity);
}

public sealed record UsageEntry
{
    public DateTimeOffset? Timestamp { get; init; }

    public TimeSpan Duration { get; init; }

    public bool OnAc { get; init; }

    /// <summary>"Active", "Suspend", "ConnectedStandby", "ReportGenerated" etc.</summary>
    public string? EntryType { get; init; }

    public int? ChargeCapacity { get; init; }

    public int? FullChargeCapacity { get; init; }
}

public sealed record EnergyDrain
{
    public DateTimeOffset? Start { get; init; }

    public DateTimeOffset? End { get; init; }

    public int? StartChargeCapacity { get; init; }

    public int? EndChargeCapacity { get; init; }

    public int? FullChargeCapacity { get; init; }

    public TimeSpan? Duration => Start is { } s && End is { } e && e >= s ? e - s : null;

    /// <summary>Capacity used during the drain (mWh). Null or negative values mean it charged.</summary>
    public int? Used => StartChargeCapacity is { } s && EndChargeCapacity is { } e ? s - e : null;

    /// <summary>Average draw in watts, or null when it cannot be worked out.</summary>
    public double? AverageWatts =>
        Duration is { TotalHours: > 0 } d && Used is > 0 ? Used.Value / d.TotalHours / 1000.0 : null;
}
