using System.Security.Cryptography;
using System.Text;

namespace DCS.Core.IR;

public sealed record RegistrationNode
{
    /// <summary>
    /// Cross-snapshot identity key: hash(FQN). Stable across commits for the same logical
    /// registration. Used by the diff engine for rename detection and change matching.
    /// Two registrations of the same abstract token in different files share this ID.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Within-snapshot uniqueness key: hash(FQN + filePath + line). Unique per registration
    /// site so duplicate registrations (e.g. same interface in WinUI and Avalonia shells)
    /// are not silently collapsed in analysis. Used by GraphAnalyzer for per-instance tracking.
    /// </summary>
    public string InstanceId { get; init; } = string.Empty;

    public required string DisplayName { get; init; }
    public required TypeRef AbstractToken { get; init; }
    public List<TypeRef> Aliases { get; init; } = [];
    public TypeRef? ConcreteImpl { get; init; }
    public Lifetime Lifetime { get; init; } = Lifetime.Unknown;
    public Scope Scope { get; init; } = Scope.Root;
    public SourceRef? SourceLocation { get; init; }
    public Confidence ParserConfidence { get; init; } = Confidence.Explicit;
    public List<string> FrameworkTags { get; init; } = [];
    public Dictionary<string, string> Annotations { get; init; } = [];
    public List<string> ConditionalOn { get; init; } = [];

    public List<ContextMembership> ContextMemberships { get; init; } = [];
    public string? PrimaryBeanName { get; init; }
    public List<string> BeanAliases { get; init; } = [];
    public TypeRef? ExposedType { get; init; }
    public TypeRef? ImplementationType { get; init; }
    public List<QualifierConstraint> QualifierConstraints { get; init; } = [];
    public bool IsPrimary { get; init; }
    public RegistrationOrigin? Origin { get; init; }
    public string? ModuleId { get; init; }
    public SourceSetKind? SourceSet { get; init; }

    /// <summary>Cross-snapshot identity: hash(FQN). Shared by all registrations of the same abstract token.</summary>
    public static string ComputeId(string fullyQualifiedName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fullyQualifiedName));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    /// <summary>Within-snapshot uniqueness: hash(FQN + ":" + filePath + ":" + line). Unique per registration site.</summary>
    public static string ComputeInstanceId(string fullyQualifiedName, string? filePath, int? line)
    {
        var key = $"{fullyQualifiedName}:{filePath ?? string.Empty}:{line?.ToString() ?? string.Empty}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
