using System.Text.Json;
using System.Text.Json.Serialization;

namespace DCS.Analysis;

public static class AnalysisReportSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(AnalysisReport report) =>
        JsonSerializer.Serialize(report, Options);

    public static string Serialize(MultiContextAnalysisReport report) =>
        JsonSerializer.Serialize(report, Options);

    public static async Task WriteToFileAsync(AnalysisReport report, string path, CancellationToken ct = default)
    {
        await File.WriteAllTextAsync(path, Serialize(report), ct);
    }

    public static async Task WriteToFileAsync(MultiContextAnalysisReport report, string path, CancellationToken ct = default)
    {
        await File.WriteAllTextAsync(path, Serialize(report), ct);
    }
}
