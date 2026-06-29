namespace DCS.Core.IR;

/// <summary>
/// Service type exposed by a registration — resolved or syntactic fallback.
/// </summary>
public sealed record ServiceTypeIdentity
{
    public ResolvedTypeIdentity? Resolved { get; init; }
    public string? SyntacticDisplay { get; init; }

    public bool IsResolved => Resolved != null;

    public string CanonicalKey => Resolved?.CanonicalKey ?? $"syntactic:{SyntacticDisplay ?? string.Empty}";

    public static ServiceTypeIdentity FromResolved(ResolvedTypeIdentity identity) =>
        new() { Resolved = identity };

    public static ServiceTypeIdentity FromSyntactic(string display) =>
        new() { SyntacticDisplay = display };
}
