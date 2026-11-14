namespace Unveil.Core.Battery;

/// <summary>Health of a battery. <see cref="HealthPercent"/> is null when health cannot be trusted.</summary>
/// <param name="HealthPercent">Full charge ÷ design × 100, rounded to 0.1. Can be above 100 for new batteries.</param>
/// <param name="WearPercent">100 − health, never below 0.</param>
/// <param name="UnknownReason">Why health is unknown, when it is.</param>
public sealed record HealthResult(double? HealthPercent, double? WearPercent, string? UnknownReason)
{
    public bool IsKnown => HealthPercent.HasValue;

    public static HealthResult Unknown(string reason) => new(null, null, reason);
}

public static class BatteryHealth
{
    /// <summary>
    /// Above this the design/full-charge values are almost certainly in different units or garbage.
    /// New batteries normally read up to ~110%.
    /// </summary>
    public const double ImplausibleHealthPercent = 130.0;

    /// <param name="designCapacity">Design capacity, normally mWh.</param>
    /// <param name="fullChargeCapacity">Full charge capacity, same units as design.</param>
    /// <param name="isRelativeCapacity">True when the battery reports relative units instead of mWh.</param>
    public static HealthResult Calculate(long? designCapacity, long? fullChargeCapacity, bool isRelativeCapacity = false)
    {
        if (isRelativeCapacity)
        {
            return HealthResult.Unknown("Battery reports relative capacity instead of mWh");
        }

        if (designCapacity is null)
        {
            return HealthResult.Unknown("Design capacity not reported");
        }

        if (designCapacity <= 0)
        {
            return HealthResult.Unknown("Design capacity reported as 0");
        }

        if (fullChargeCapacity is null)
        {
            return HealthResult.Unknown("Full charge capacity not reported");
        }

        if (fullChargeCapacity < 0)
        {
            return HealthResult.Unknown("Full charge capacity is invalid");
        }

        var health = fullChargeCapacity.Value * 100.0 / designCapacity.Value;
        if (health > ImplausibleHealthPercent)
        {
            return HealthResult.Unknown(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Full charge capacity is {health:0}% of design, which is not plausible (units probably differ)"));
        }

        var rounded = Math.Round(health, 1, MidpointRounding.AwayFromZero);
        var wear = Math.Max(0.0, Math.Round(100.0 - rounded, 1, MidpointRounding.AwayFromZero));
        return new HealthResult(rounded, wear, null);
    }
}
