using System.Security.Cryptography;
using System.Text;

namespace DCS.Core.IR;

public sealed record ResolvedTypeIdentity
{
    public required AssemblyKey AssemblyKey { get; init; }
    public required string MetadataName { get; init; }
    public List<ResolvedTypeIdentity> TypeArguments { get; init; } = [];

    public string CanonicalKey =>
        $"{AssemblyKey.Canonical}|{MetadataName}|{string.Join(",", TypeArguments.Select(t => t.CanonicalKey))}";

    public static string ComputeHash(string canonicalKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalKey));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    /// <summary>Display projection for CLI — not the canonical identity.</summary>
    public string ToDisplayName()
    {
        var baseName = MetadataName;
        var plusIdx = baseName.LastIndexOf('+');
        var shortName = plusIdx >= 0 ? baseName[(plusIdx + 1)..] : baseName;
        var tickIdx = shortName.IndexOf('`');
        if (tickIdx >= 0) shortName = shortName[..tickIdx];
        return shortName;
    }
}
