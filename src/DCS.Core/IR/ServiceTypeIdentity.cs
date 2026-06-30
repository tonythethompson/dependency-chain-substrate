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

    /// <summary>
    /// Cross-project duplicate grouping — metadata name only, not scope-specific assembly key.
    /// </summary>
    public string DuplicateGroupingKey
    {
        get
        {
            if (Resolved == null)
                return SyntacticDisplay ?? CanonicalKey;

            if (Resolved.TypeArguments.Count == 0)
                return Resolved.MetadataName;

            var args = string.Join(",", Resolved.TypeArguments.Select(t => t.MetadataName));
            return $"{Resolved.MetadataName}|{args}";
        }
    }

    public static ServiceTypeIdentity FromResolved(ResolvedTypeIdentity identity) =>
        new() { Resolved = identity };

    public static ServiceTypeIdentity FromSyntactic(string display) =>
        new() { SyntacticDisplay = display };
}
