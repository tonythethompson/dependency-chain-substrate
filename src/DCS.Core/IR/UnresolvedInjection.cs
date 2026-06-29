namespace DCS.Core.IR;

public sealed record UnresolvedInjection
{
    public required string Id { get; init; }
    public required string FromRegistrationId { get; init; }
    public required TypeRef DeclaredType { get; init; }
    public Mechanism InjectionMechanism { get; init; } = Mechanism.Constructor;
    public string? ParameterName { get; init; }
    public string Reason { get; init; } = "unresolved";
    public List<string> AmbiguousCandidateIds { get; init; } = [];
}
