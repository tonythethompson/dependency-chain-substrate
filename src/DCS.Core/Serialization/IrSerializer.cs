using System.Text.Json;
using System.Text.Json.Serialization;
using DCS.Core.IR;

namespace DCS.Core.Serialization;

public static class IrSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Serialize(RegistrationGraph graph) =>
        JsonSerializer.Serialize(graph, Options);

    public static RegistrationGraph? Deserialize(string json) =>
        JsonSerializer.Deserialize<RegistrationGraph>(json, Options);

    public static async Task WriteToFileAsync(RegistrationGraph graph, string path, CancellationToken ct = default)
    {
        await using var file = File.CreateText(path);
        await file.WriteAsync(Serialize(graph).AsMemory(), ct);
    }
}
