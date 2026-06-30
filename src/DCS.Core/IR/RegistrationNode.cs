using System.Security.Cryptography;
using System.Text;

namespace DCS.Core.IR;

public sealed record RegistrationNode
{
    /// <summary>
    /// Primary graph node key. Alias of <see cref="RegistrationInstanceId"/>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Within-snapshot unique registration site identity (same value as <see cref="Id"/>).
    /// </summary>
    public string RegistrationInstanceId { get; init; } = string.Empty;

    /// <summary>
    /// Legacy field — converged to <see cref="RegistrationInstanceId"/>.
    /// </summary>
    public string InstanceId { get; init; } = string.Empty;

    public ServiceTypeIdentity? ServiceType { get; init; }
    public string DuplicateGroupKey { get; init; } = string.Empty;
    public string CompositionScopeId { get; init; } = string.Empty;
    public TypeResolutionQuality TypeResolutionQuality { get; init; } = TypeResolutionQuality.SyntacticFallback;
    public RegistrationRecognitionQuality RegistrationRecognitionQuality { get; init; } =
        RegistrationRecognitionQuality.SyntaxCandidateUnverified;
    public string RegistrationStatementFingerprint { get; init; } = string.Empty;

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

    /// <summary>
    /// Computes primary graph node identity for a registration site.
    /// </summary>
    public static string ComputeRegistrationInstanceId(
        string projectTargetScopeId,
        string? filePath,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        int registrationOrdinal)
    {
        var key = $"{projectTargetScopeId}:{filePath ?? string.Empty}:{startLine}:{startColumn}:{endLine}:{endColumn}:{registrationOrdinal}";
        return HashKey(key);
    }

    /// <summary>
    /// Computes duplicate group key from composition scope and service type identity.
    /// </summary>
    public static string ComputeDuplicateGroupKey(string compositionScopeId, ServiceTypeIdentity serviceType) =>
        HashKey($"{compositionScopeId}|{serviceType.DuplicateGroupingKey}");

    /// <summary>
    /// Legacy helper — prefer <see cref="ComputeRegistrationInstanceId"/>.
    /// </summary>
    [Obsolete("Use ComputeRegistrationInstanceId for graph node keys.")]
    public static string ComputeId(string fullyQualifiedName) => HashKey(fullyQualifiedName);

    /// <summary>
    /// Legacy helper — converged to registration instance id formula.
    /// </summary>
    public static string ComputeInstanceId(string fullyQualifiedName, string? filePath, int? line) =>
        HashKey($"{fullyQualifiedName}:{filePath ?? string.Empty}:{line?.ToString() ?? string.Empty}");

    private static string HashKey(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
