using System.Text.Json;

namespace DCS.Runtime;

public static class RuntimeLogReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true
    };

    public static IReadOnlyList<RuntimeResolutionEvent> ReadJsonl(string path)
    {
        var events = new List<RuntimeResolutionEvent>();
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var evt = JsonSerializer.Deserialize<RuntimeResolutionEvent>(line, Options);
            if (evt != null && !string.IsNullOrWhiteSpace(evt.RequestedType))
                events.Add(evt);
        }

        return events;
    }

    public static IReadOnlyList<RuntimeResolutionEvent> ParseJsonl(string content)
    {
        using var reader = new StringReader(content);
        var events = new List<RuntimeResolutionEvent>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var evt = JsonSerializer.Deserialize<RuntimeResolutionEvent>(line, Options);
            if (evt != null && !string.IsNullOrWhiteSpace(evt.RequestedType))
                events.Add(evt);
        }

        return events;
    }
}
