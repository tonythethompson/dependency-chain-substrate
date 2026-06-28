using System.Security.Cryptography;
using System.Text;

namespace DCS.Core.IR;

public sealed record RegistrationNode
{
    public required string Id { get; init; }
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

    public static string ComputeId(string fullyQualifiedName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(fullyQualifiedName));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
