using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unveil.Core.Reports;

/// <summary>JSON export of all collected data plus the verdict, for history and comparison.</summary>
public static class JsonReportWriter
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(ReportDocument document) => JsonSerializer.Serialize(document, Options);

    public static void Write(ReportDocument document, string path) =>
        File.WriteAllText(path, Serialize(document), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    /// <summary>Reads a previously exported report (used for comparison in Phase 8).</summary>
    public static ReportDocument Read(string json) =>
        JsonSerializer.Deserialize<ReportDocument>(json, Options)
        ?? throw new JsonException("The file does not contain an Unveil report.");
}
