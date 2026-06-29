namespace DCS.Core.IR;

public sealed record ConditionalInjection
{
    public required string Id { get; init; }
    public required string FromRegistrationId { get; init; }
    public required TypeRef DeclaredType { get; init; }
    public Mechanism InjectionMechanism { get; init; } = Mechanism.Constructor;
    public string? ParameterName { get; init; }
    public List<string> CandidateRegistrationIds { get; init; } = [];
    public List<Dictionary<string, string>> ConditionMetadata { get; init; } = [];
    public string? ResolvedUnconditionalTargetId { get; init; }
}
