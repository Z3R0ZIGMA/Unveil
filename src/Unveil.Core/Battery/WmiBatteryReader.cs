using System.Management;
using System.Security.Principal;
using Unveil.Core.Wmi;

namespace Unveil.Core.Battery;

/// <summary>Raw battery values from the <c>root\wmi</c> battery classes, for one battery instance.</summary>
public sealed class WmiBatteryData
{
    public required string InstanceName { get; init; }

    // BatteryStaticData (often fails for a standard user)
    public bool StaticDataAvailable { get; set; }

    public string? ManufactureName { get; set; }

    public string? DeviceName { get; set; }

    public string? SerialNumber { get; set; }

    public string? UniqueId { get; set; }

    /// <summary>Decoded chemistry code, e.g. "LION".</summary>
    public string? Chemistry { get; set; }

    public DateOnly? ManufactureDate { get; set; }

    public uint? DesignedCapacity { get; set; }

    public uint? Capabilities { get; set; }

    // BatteryFullChargedCapacity
    public uint? FullChargedCapacity { get; set; }

    // BatteryCycleCount
    public uint? CycleCount { get; set; }

    // BatteryStatus
    public uint? RemainingCapacity { get; set; }

    /// <summary>Millivolts.</summary>
    public uint? Voltage { get; set; }

    /// <summary>Milliwatts.</summary>
    public int? ChargeRate { get; set; }

    /// <summary>Milliwatts.</summary>
    public int? DischargeRate { get; set; }

    public bool? PowerOnline { get; set; }

    public bool? Charging { get; set; }

    public bool? Discharging { get; set; }

    public bool? Critical { get; set; }

    /// <summary>BATTERY_CAPACITY_RELATIVE: capacities are not in mWh.</summary>
    public bool IsRelativeCapacity => Capabilities is { } c && (c & 0x40000000) != 0;
}

public interface IWmiBatterySource
{
    IReadOnlyList<WmiBatteryData> Read(CollectionLog log);

    /// <summary><c>Win32_Battery.Status</c> for each battery ("OK", "Pred Fail", "Service"...).</summary>
    IReadOnlyList<string> ReadWindowsStatus(CollectionLog log);
}

/// <summary>Reads the four <c>root\wmi</c> battery classes and joins them by InstanceName.</summary>
public sealed class WmiBatteryReader : IWmiBatterySource
{
    private const string Scope = @"root\wmi";
    private const string Source = "WMI";

    // Windows "unknown" sentinels from batclass.h.
    private const uint UnknownCapacity = 0xFFFFFFFF;
    private const uint UnknownVoltage = 0xFFFFFFFF;
    private const int UnknownRate = unchecked((int)0x80000000);

    public IReadOnlyList<WmiBatteryData> Read(CollectionLog log)
    {
        var batteries = new Dictionary<string, WmiBatteryData>(StringComparer.OrdinalIgnoreCase);

        WmiBatteryData For(string? instance)
        {
            var key = instance ?? $"(unnamed {batteries.Count})";
            if (!batteries.TryGetValue(key, out var data))
            {
                data = new WmiBatteryData { InstanceName = key };
                batteries[key] = data;
            }

            return data;
        }

        Query(log, "BatteryStatus", o =>
        {
            var d = For(WmiQuery.String(o, "InstanceName"));
            d.RemainingCapacity = Known(WmiQuery.UInt(o, "RemainingCapacity"), UnknownCapacity);
            d.Voltage = Known(WmiQuery.UInt(o, "Voltage"), UnknownVoltage);
            d.ChargeRate = KnownRate(WmiQuery.Int(o, "ChargeRate"));
            d.DischargeRate = KnownRate(WmiQuery.Int(o, "DischargeRate"));
            d.PowerOnline = WmiQuery.Bool(o, "PowerOnline");
            d.Charging = WmiQuery.Bool(o, "Charging");
            d.Discharging = WmiQuery.Bool(o, "Discharging");
            d.Critical = WmiQuery.Bool(o, "Critical");
        });

        Query(log, "BatteryFullChargedCapacity", o =>
            For(WmiQuery.String(o, "InstanceName")).FullChargedCapacity =
                Known(WmiQuery.UInt(o, "FullChargedCapacity"), UnknownCapacity));

        Query(log, "BatteryCycleCount", o =>
            For(WmiQuery.String(o, "InstanceName")).CycleCount = WmiQuery.UInt(o, "CycleCount"));

        Query(log, "BatteryStaticData", o =>
        {
            var d = For(WmiQuery.String(o, "InstanceName"));
            d.StaticDataAvailable = true;
            d.ManufactureName = WmiQuery.String(o, "ManufactureName");
            d.DeviceName = WmiQuery.String(o, "DeviceName");
            d.SerialNumber = WmiQuery.String(o, "SerialNumber");
            d.UniqueId = WmiQuery.String(o, "UniqueID");
            d.Chemistry = BatteryChemistry.DecodeWmiCode(WmiQuery.UInt(o, "Chemistry"));
            d.ManufactureDate = ParseCimDate(WmiQuery.String(o, "ManufactureDate"));
            d.DesignedCapacity = Known(WmiQuery.UInt(o, "DesignedCapacity"), UnknownCapacity);
            d.Capabilities = WmiQuery.UInt(o, "Capabilities");
        }, staticData: true);

        return batteries.Values.OrderBy(b => b.InstanceName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public IReadOnlyList<string> ReadWindowsStatus(CollectionLog log)
    {
        try
        {
            return WmiQuery.Select(@"root\cimv2", "SELECT Status FROM Win32_Battery", o => WmiQuery.String(o, "Status"))
                .OfType<string>()
                .ToList();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            log.Warning(Source, $"Win32_Battery: query failed: {ex.Message.Trim()}");
            return [];
        }
    }

    private static void Query(CollectionLog log, string className, Action<ManagementBaseObject> apply, bool staticData = false)
    {
        try
        {
            var count = WmiQuery.Select(Scope, $"SELECT * FROM {className}", o =>
            {
                apply(o);
                return 0;
            }).Count;

            if (count == 0)
            {
                log.Info(Source, $"{className}: no instances returned.");
            }
        }
        catch (ManagementException ex) when (ex.ErrorCode is ManagementStatus.InvalidClass or ManagementStatus.NotFound)
        {
            log.Info(Source, $"{className}: class not available on this machine.");
        }
        catch (ManagementException ex) when (staticData)
        {
            log.Info(Source,
                $"{className}: {ex.Message.Trim()}{(IsAdministrator() ? "" : " (running as standard user)")}. " +
                "Using the battery report for design capacity, serial and chemistry.");
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            log.Warning(Source, $"{className}: query failed: {ex.Message.Trim()}");
        }
    }

    private static uint? Known(uint? value, uint unknown) => value == unknown ? null : value;

    private static int? KnownRate(int? value) => value == UnknownRate ? null : value;

    private static DateOnly? ParseCimDate(string? cim)
    {
        if (cim is null)
        {
            return null;
        }

        try
        {
            var date = ManagementDateTimeConverter.ToDateTime(cim);
            return date.Year >= 1990 && date <= DateTime.Now.AddYears(1) ? DateOnly.FromDateTime(date) : null;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or FormatException or ArgumentException)
        {
            return null;
        }
    }

    internal static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
