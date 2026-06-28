namespace DCS.Core.IR;

public sealed record DependencyEdge
{
    public required string Id { get; init; }
    public required string From { get; init; }
    public required string To { get; init; }
    public Mechanism InjectionMechanism { get; init; } = Mechanism.Constructor;
    public string? ParameterName { get; init; }
    public Confidence ParserConfidence { get; init; } = Confidence.Explicit;

    public static string ComputeId(string from, string to, int index = 0) =>
        $"{from}:{to}:{index}";
}
