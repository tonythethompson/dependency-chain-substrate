namespace DCS.Core.IR;

/// <summary>
/// Discriminates type identity across assemblies. Metadata symbols use assembly name;
/// source symbols use project target scope id.
/// </summary>
public sealed record AssemblyKey
{
    public required string SimpleName { get; init; }
    public string? Version { get; init; }
    public string? PublicKeyToken { get; init; }
    /// <summary>When true, SimpleName is a ProjectTargetScopeId for source-defined types.</summary>
    public bool IsSourceScope { get; init; }

    public string Canonical => IsSourceScope
        ? $"scope:{SimpleName}"
        : string.IsNullOrEmpty(PublicKeyToken)
            ? SimpleName
            : $"{SimpleName}, Version={Version ?? "0.0.0.0"}, Culture=neutral, PublicKeyToken={PublicKeyToken}";

    public static AssemblyKey FromProjectScope(string projectTargetScopeId) => new()
    {
        SimpleName = projectTargetScopeId,
        IsSourceScope = true
    };

    public static AssemblyKey FromMetadata(string simpleName, string? version = null, string? publicKeyToken = null) => new()
    {
        SimpleName = simpleName,
        Version = version,
        PublicKeyToken = publicKeyToken,
        IsSourceScope = false
    };
}
