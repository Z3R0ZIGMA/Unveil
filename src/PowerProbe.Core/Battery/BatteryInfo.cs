namespace PowerProbe.Core.Battery;

[Flags]
public enum BatteryDataSources
{
    None = 0,
    Wmi = 1,
    BatteryReport = 2,
}

/// <summary>One battery, with WMI and battery report data merged (battery report wins on conflicts).</summary>
public sealed record BatteryInfo
{
    /// <summary>1-based position.</summary>
    public int Index { get; init; }

    public BatteryDataSources Sources { get; init; }

    public string? WmiInstanceName { get; init; }

    // ---- identity ----
    public string? Manufacturer { get; init; }

    /// <summary>Battery name; often the model or FRU/part number.</summary>
    public string? Name { get; init; }

    public string? SerialNumber { get; init; }

    /// <summary>Raw chemistry code, e.g. "LIon".</summary>
    public string? ChemistryCode { get; init; }

    /// <summary>Readable chemistry, e.g. "Lithium-ion".</summary>
    public string? Chemistry => BatteryChemistry.Describe(ChemistryCode);

    /// <summary>yyyy-MM-dd when it could be read, otherwise the raw OEM text.</summary>
    public string? ManufactureDate { get; init; }

    // ---- health ----
    public long? DesignCapacity { get; init; }

    public long? FullChargeCapacity { get; init; }

    /// <summary>True when capacities are relative units rather than mWh.</summary>
    public bool IsRelativeCapacity { get; init; }

    /// <summary>Null when not reported (0 is treated as not reported).</summary>
    public int? CycleCount { get; init; }

    public HealthResult Health => BatteryHealth.Calculate(DesignCapacity, FullChargeCapacity, IsRelativeCapacity);

    // ---- live status (WMI only) ----
    public long? RemainingCapacity { get; init; }

    /// <summary>Remaining ÷ full charge × 100, capped at 100.</summary>
    public double? ChargePercent { get; init; }

    /// <summary>Millivolts.</summary>
    public long? VoltageMillivolts { get; init; }

    /// <summary>Milliwatts.</summary>
    public int? ChargeRateMilliwatts { get; init; }

    /// <summary>Milliwatts.</summary>
    public int? DischargeRateMilliwatts { get; init; }

    public bool? PowerOnline { get; init; }

    public bool? Charging { get; init; }

    public bool? Discharging { get; init; }

    /// <summary>Windows flags the battery as critical (very low or failing).</summary>
    public bool? Critical { get; init; }
}

/// <summary>Combined figures when there is more than one battery.</summary>
public sealed record BatteryTotals
{
    public int Count { get; init; }

    public long? DesignCapacity { get; init; }

    public long? FullChargeCapacity { get; init; }

    public long? RemainingCapacity { get; init; }

    public double? ChargePercent { get; init; }

    public int? ChargeRateMilliwatts { get; init; }

    public int? DischargeRateMilliwatts { get; init; }

    public required HealthResult Health { get; init; }
}
