using System.Text.Json;

namespace DCS.Runtime;

public static class RuntimeLogWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static void AppendJsonl(string path, RuntimeResolutionEvent evt)
    {
        var line = JsonSerializer.Serialize(evt, Options);
        File.AppendAllText(path, line + Environment.NewLine);
    }

    public static string SerializeJsonl(IEnumerable<RuntimeResolutionEvent> events) =>
        string.Join(Environment.NewLine, events.Select(e => JsonSerializer.Serialize(e, Options)));
}
