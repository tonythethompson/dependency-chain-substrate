using DCS.Core.IR;
using System.Text.Json.Serialization;

namespace DCS.Analysis;

public sealed record PathExcavationReport
{
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("from_node_id")]
    public string? FromNodeId { get; init; }

    [JsonPropertyName("to_node_id")]
    public string? ToNodeId { get; init; }

    [JsonPropertyName("hop_count")]
    public int HopCount { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("is_ambiguous")]
    public bool IsAmbiguous { get; init; }

    [JsonPropertyName("nodes")]
    public IReadOnlyList<PathExcavationNode> Nodes { get; init; } = [];

    [JsonPropertyName("edges")]
    public IReadOnlyList<PathExcavationEdge> Edges { get; init; } = [];

    public static PathExcavationReport FromResult(GraphPathResult result) =>
        new()
        {
            Success = result.Success,
            FromNodeId = result.FromNodeId,
            ToNodeId = result.ToNodeId,
            HopCount = Math.Max(0, result.Nodes.Count - 1),
            Error = result.Error,
            IsAmbiguous = result.IsAmbiguous,
            Nodes = result.Nodes.Select(PathExcavationNode.FromNode).ToList(),
            Edges = result.Edges.Select(PathExcavationEdge.FromEdge).ToList()
        };
}

public sealed record PathExcavationNode
{
    [JsonPropertyName("registration_id")]
    public required string RegistrationId { get; init; }

    [JsonPropertyName("display_name")]
    public required string DisplayName { get; init; }

    [JsonPropertyName("fully_qualified_name")]
    public string? FullyQualifiedName { get; init; }

    [JsonPropertyName("file_path")]
    public string? FilePath { get; init; }

    [JsonPropertyName("line")]
    public int? Line { get; init; }

    public static PathExcavationNode FromNode(RegistrationNode node) =>
        new()
        {
            RegistrationId = node.Id,
            DisplayName = node.DisplayName,
            FullyQualifiedName = string.IsNullOrEmpty(node.AbstractToken.FullyQualifiedName)
                ? null
                : node.AbstractToken.FullyQualifiedName,
            FilePath = node.SourceLocation?.FilePath,
            Line = node.SourceLocation?.Line
        };
}

public sealed record PathExcavationEdge
{
    [JsonPropertyName("from")]
    public required string From { get; init; }

    [JsonPropertyName("to")]
    public required string To { get; init; }

    [JsonPropertyName("mechanism")]
    public string Mechanism { get; init; } = string.Empty;

    [JsonPropertyName("parameter_name")]
    public string? ParameterName { get; init; }

    public static PathExcavationEdge FromEdge(DependencyEdge edge) =>
        new()
        {
            From = edge.From,
            To = edge.To,
            Mechanism = edge.InjectionMechanism.ToString(),
            ParameterName = edge.ParameterName
        };
}

public static class PathExcavationPrinter
{
    public static void Print(GraphPathResult result, TextWriter writer)
    {
        if (!result.Success)
        {
            writer.WriteLine($"[DCS] Path not found: {result.Error}");
            return;
        }

        writer.WriteLine("=== DCS Path Excavation ===");
        writer.WriteLine($"Hops: {Math.Max(0, result.Nodes.Count - 1)}");
        writer.WriteLine();

        for (var i = 0; i < result.Nodes.Count; i++)
        {
            var node = result.Nodes[i];
            var location = node.SourceLocation;
            var site = location?.FilePath != null
                ? $"{location.FilePath}:{location.Line}"
                : "(unknown site)";
            writer.WriteLine($"{i + 1}. {node.DisplayName} @ {site}");
            if (!string.IsNullOrEmpty(node.AbstractToken.FullyQualifiedName))
                writer.WriteLine($"   {node.AbstractToken.FullyQualifiedName}");
        }

        if (result.Edges.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("--- EDGES ---");
            foreach (var edge in result.Edges)
            {
                var label = string.IsNullOrEmpty(edge.ParameterName)
                    ? edge.InjectionMechanism.ToString()
                    : $"{edge.InjectionMechanism} ({edge.ParameterName})";
                writer.WriteLine($"  {edge.From} -> {edge.To}  [{label}]");
            }
        }
    }
}
