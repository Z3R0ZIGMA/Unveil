using System.Management;
using Unveil.Core.Wmi;

namespace Unveil.Core.SystemInfo;

/// <summary>Laptop make, model and serial — what a tech needs to source a replacement battery.</summary>
public sealed record SystemIdentity
{
    public string? Manufacturer { get; init; }

    /// <summary><c>Win32_ComputerSystem.Model</c>. On Lenovo this is the machine type (e.g. "21AHS0AB00").</summary>
    public string? Model { get; init; }

    /// <summary><c>Win32_ComputerSystemProduct.Version</c>. On Lenovo this is the friendly name (e.g. "ThinkPad T14 Gen 3").</summary>
    public string? ProductVersion { get; init; }

    public string? SystemFamily { get; init; }

    public string? SerialNumber { get; init; }

    public string? BiosVersion { get; init; }

    /// <summary><c>Win32_ComputerSystem.PCSystemType</c>: 1 desktop, 2 mobile, 3 workstation, 8 slate…</summary>
    public int? PcSystemType { get; init; }

    /// <summary>Best human-readable model name, e.g. "ThinkPad T14 Gen 3 (21AHS0AB00)".</summary>
    public string? DisplayModel
    {
        get
        {
            var isLenovo = Manufacturer?.Contains("LENOVO", StringComparison.OrdinalIgnoreCase) == true;
            if (isLenovo && ProductVersion is { } friendly && !string.Equals(friendly, Model, StringComparison.OrdinalIgnoreCase))
            {
                return Model is null ? friendly : $"{friendly} ({Model})";
            }

            return Model ?? ProductVersion ?? SystemFamily;
        }
    }

    public string PcSystemTypeName => PcSystemType switch
    {
        1 => "Desktop",
        2 => "Laptop / mobile",
        3 => "Workstation",
        4 => "Enterprise server",
        5 => "SOHO server",
        6 => "Appliance PC",
        7 => "Performance server",
        8 => "Slate / tablet",
        null => "Unknown",
        _ => $"Other ({PcSystemType})",
    };
}

public interface ISystemIdentitySource
{
    SystemIdentity Read(CollectionLog log);
}

public sealed class WmiSystemIdentityReader : ISystemIdentitySource
{
    private const string Scope = @"root\cimv2";
    private const string Source = "System";

    public SystemIdentity Read(CollectionLog log)
    {
        var cs = First(log, "SELECT Manufacturer, Model, SystemFamily, PCSystemType FROM Win32_ComputerSystem", o => new
        {
            Manufacturer = WmiQuery.String(o, "Manufacturer"),
            Model = WmiQuery.String(o, "Model"),
            Family = WmiQuery.String(o, "SystemFamily"),
            Type = WmiQuery.Int(o, "PCSystemType"),
        });

        var product = First(log, "SELECT Vendor, Name, Version, IdentifyingNumber FROM Win32_ComputerSystemProduct", o => new
        {
            Vendor = WmiQuery.String(o, "Vendor"),
            Name = WmiQuery.String(o, "Name"),
            Version = WmiQuery.String(o, "Version"),
            Serial = WmiQuery.String(o, "IdentifyingNumber"),
        });

        var bios = First(log, "SELECT SerialNumber, SMBIOSBIOSVersion FROM Win32_BIOS", o => new
        {
            Serial = WmiQuery.String(o, "SerialNumber"),
            Version = WmiQuery.String(o, "SMBIOSBIOSVersion"),
        });

        return new SystemIdentity
        {
            Manufacturer = cs?.Manufacturer ?? product?.Vendor,
            Model = cs?.Model ?? product?.Name,
            ProductVersion = product?.Version,
            SystemFamily = cs?.Family,
            SerialNumber = bios?.Serial ?? product?.Serial,
            BiosVersion = bios?.Version,
            PcSystemType = cs?.Type,
        };
    }

    private static T? First<T>(CollectionLog log, string wql, Func<ManagementBaseObject, T> map)
        where T : class
    {
        try
        {
            return WmiQuery.Select(Scope, wql, map).FirstOrDefault();
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            log.Warning(Source, $"Query failed ({wql}): {ex.Message.Trim()}");
            return null;
        }
    }
}
