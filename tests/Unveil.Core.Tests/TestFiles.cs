namespace Unveil.Core.Tests;

internal static class TestFiles
{
    /// <summary>Synthetic fixtures in tests/Unveil.Core.Tests/Fixtures.</summary>
    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>Real, scrubbed outputs from /samples.</summary>
    public static string Sample(params string[] parts) =>
        Path.Combine([AppContext.BaseDirectory, "samples", .. parts]);

    public static string SamplesFolder => Path.Combine(AppContext.BaseDirectory, "samples");
}
