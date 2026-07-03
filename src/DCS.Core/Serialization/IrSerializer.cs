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

    public static RegistrationGraph? Deserialize(string json)
    {
        ValidateSchemaVersion(json);
        return JsonSerializer.Deserialize<RegistrationGraph>(json, Options);
    }

    public static async Task WriteToFileAsync(RegistrationGraph graph, string path, CancellationToken ct = default)
    {
        await using var file = File.CreateText(path);
        await file.WriteAsync(Serialize(graph).AsMemory(), ct);
    }

    private static void ValidateSchemaVersion(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("schema_version", out var schemaVersionElement))
            return;

        var schemaVersion = schemaVersionElement.GetString();
        if (string.IsNullOrWhiteSpace(schemaVersion))
            return;

        var majorText = schemaVersion.Split('.', 2)[0];
        if (!int.TryParse(majorText, out var major))
            throw new InvalidOperationException($"Unsupported IR schema_version '{schemaVersion}'.");

        if (major > 1)
            throw new InvalidOperationException(
                $"Unsupported IR schema_version '{schemaVersion}'. This reader supports major version 1.");
    }
}
