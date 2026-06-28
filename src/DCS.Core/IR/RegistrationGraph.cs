namespace DCS.Core.IR;

public sealed record RegistrationGraph
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public string ParserVersion { get; init; } = "0.1.0";
    public string? CommitSha { get; init; }
    public string ExtractionMode { get; init; } = "static";
    public string SourceLanguage { get; init; } = "csharp";
    public List<RegistrationNode> Nodes { get; init; } = [];
    public List<DependencyEdge> Edges { get; init; } = [];
    public List<BlindSpotReport> BlindSpots { get; init; } = [];
    public Dictionary<string, string> Metadata { get; init; } = [];
}
