using PowerProbe.Core.Battery;

namespace PowerProbe.Core.Tests.Battery;

public class BatteryChemistryTests
{
    [Theory]
    [InlineData("LION", "Lithium-ion")]
    [InlineData("LIon", "Lithium-ion")]
    [InlineData("Li-I", "Lithium-ion")]
    [InlineData("LiP", "Lithium polymer")]
    [InlineData("LIPO", "Lithium polymer")]
    [InlineData("NiMH", "Nickel-metal hydride")]
    [InlineData("NICD", "Nickel-cadmium")]
    [InlineData("PbAc", "Lead-acid")]
    [InlineData(" LION\0", "Lithium-ion")]
    public void Known_codes_are_described(string code, string expected)
    {
        Assert.Equal(expected, BatteryChemistry.Describe(code));
    }

    [Fact]
    public void Unknown_code_is_returned_as_is()
    {
        Assert.Equal("XYZ", BatteryChemistry.Describe("XYZ"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_code_is_null(string? code)
    {
        Assert.Null(BatteryChemistry.Describe(code));
    }

    [Fact]
    public void Wmi_code_is_four_packed_ascii_characters()
    {
        // "LION" little-endian: 'L'=0x4C 'I'=0x49 'O'=0x4F 'N'=0x4E
        const uint lion = 0x4E4F494C;
        Assert.Equal("LION", BatteryChemistry.DecodeWmiCode(lion));
    }

    [Fact]
    public void Wmi_code_shorter_than_four_characters_is_decoded()
    {
        // "LiP" + NUL
        const uint lip = 0x0050694C;
        Assert.Equal("LiP", BatteryChemistry.DecodeWmiCode(lip));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x01020304u)]
    public void Wmi_code_zero_or_unprintable_is_null(uint code)
    {
        Assert.Null(BatteryChemistry.DecodeWmiCode(code));
    }
}
