using System.Text;

namespace Unveil.Core.Battery;

/// <summary>Decodes battery chemistry codes (e.g. "LION", "LiP") into readable names.</summary>
public static class BatteryChemistry
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LION"] = "Lithium-ion",
        ["LI-I"] = "Lithium-ion",
        ["LI-ION"] = "Lithium-ion",
        ["LIIO"] = "Lithium-ion",
        ["LI"] = "Lithium-ion",
        ["LIP"] = "Lithium polymer",
        ["LIPO"] = "Lithium polymer",
        ["LI-P"] = "Lithium polymer",
        ["LIPOLY"] = "Lithium polymer",
        ["LPOL"] = "Lithium polymer",
        ["LIFE"] = "Lithium iron phosphate",
        ["LFP"] = "Lithium iron phosphate",
        ["NIMH"] = "Nickel-metal hydride",
        ["NICD"] = "Nickel-cadmium",
        ["PBAC"] = "Lead-acid",
        ["RAM"] = "Rechargeable alkaline manganese",
    };

    /// <summary>Returns a readable name, the raw code if it is not recognised, or null if empty.</summary>
    public static string? Describe(string? code)
    {
        var trimmed = code?.Trim().Trim('\0').Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return Names.TryGetValue(trimmed, out var name) ? name : trimmed;
    }

    /// <summary>
    /// <c>BatteryStaticData.Chemistry</c> is four ASCII characters packed into a uint32 (little-endian),
    /// e.g. "LION". Returns the code string, or null when it is 0 or not printable.
    /// </summary>
    public static string? DecodeWmiCode(uint? code)
    {
        if (code is null or 0)
        {
            return null;
        }

        var bytes = BitConverter.GetBytes(code.Value);
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        var chars = bytes.TakeWhile(b => b != 0).ToArray();
        if (chars.Length == 0 || chars.Any(b => b < 0x20 || b > 0x7E))
        {
            return null;
        }

        return Encoding.ASCII.GetString(chars).Trim();
    }
}
