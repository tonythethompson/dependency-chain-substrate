using System.Text.Json;
using System.Text.Json.Serialization;

namespace DCS.Analysis;

public sealed class FrameworkBoundaryModel
{
    internal static readonly (string Prefix, string Tag)[] BuiltIn =
    [
        ("Microsoft.Extensions.DependencyInjection.", "msdi"),
        ("Microsoft.Extensions.", "ms-extensions"),
        ("Microsoft.AspNetCore.", "aspnetcore"),
        ("Microsoft.UI.", "winui"),
        ("WinUI.", "winui"),
        ("Avalonia.", "avalonia"),
        ("System.Windows.", "wpf"),
        ("org.springframework.web.", "spring-mvc"),
        ("org.springframework.security.", "spring-security"),
        ("org.springframework.data.", "spring-data"),
        ("org.springframework.boot.", "spring-boot"),
        ("org.springframework.", "spring-core"),
    ];

    internal static readonly HashSet<string> BuiltInTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "msdi", "ms-extensions", "aspnetcore", "winui", "avalonia", "wpf",
        "spring-mvc", "spring-data", "spring-security", "spring-boot", "spring-core"
    };

    public static readonly FrameworkBoundaryModel Default = new();

    private readonly (string Prefix, string Tag)[] _entries;

    public FrameworkBoundaryModel() => _entries = BuiltIn;

    public FrameworkBoundaryModel((string Prefix, string Tag)[] custom) =>
        _entries = custom.Length > 0 ? [.. custom, .. BuiltIn] : BuiltIn;

    public IReadOnlyList<(string Prefix, string Tag)> Entries => _entries;

    public static FrameworkBoundaryModel Create(string? configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            return Default;

        return LoadFromJson(configPath);
    }

    public static FrameworkBoundaryModel LoadFromJson(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Framework config not found: {path}");

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<FrameworksConfigFile>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Could not parse framework config: {path}");

        if (config.Frameworks == null || config.Frameworks.Count == 0)
            throw new InvalidOperationException("Framework config must contain a non-empty \"frameworks\" array.");

        var custom = new List<(string Prefix, string Tag)>();

        foreach (var entry in config.Frameworks)
        {
            if (string.IsNullOrWhiteSpace(entry.Tag))
                throw new InvalidOperationException("Each framework entry must have a non-empty \"tag\".");

            if (BuiltInTags.Contains(entry.Tag))
                throw new InvalidOperationException(
                    $"Framework tag \"{entry.Tag}\" is reserved; built-in tags cannot be overridden.");

            if (entry.NamespacePrefixes == null || entry.NamespacePrefixes.Count == 0)
                throw new InvalidOperationException(
                    $"Framework \"{entry.Tag}\" must declare at least one \"namespace_prefixes\" entry.");

            foreach (var rawPrefix in entry.NamespacePrefixes)
            {
                if (string.IsNullOrWhiteSpace(rawPrefix))
                    throw new InvalidOperationException($"Framework \"{entry.Tag}\" has an empty namespace prefix.");

                var prefix = NormalizePrefix(rawPrefix);

                foreach (var (builtInPrefix, _) in BuiltIn)
                {
                    if (PrefixesOverlap(prefix, builtInPrefix))
                        throw new InvalidOperationException(
                            $"Custom prefix \"{prefix}\" for tag \"{entry.Tag}\" overlaps built-in prefix \"{builtInPrefix}\".");
                }

                foreach (var (existingPrefix, existingTag) in custom)
                {
                    if (string.Equals(existingTag, entry.Tag, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (PrefixesOverlap(prefix, existingPrefix))
                        throw new InvalidOperationException(
                            $"Custom prefix \"{prefix}\" for tag \"{entry.Tag}\" overlaps prefix \"{existingPrefix}\" (tag \"{existingTag}\").");
                }

                custom.Add((prefix, entry.Tag));
            }
        }

        return new FrameworkBoundaryModel([.. custom]);
    }

    public string? GetTagForNamespace(string? namespaceName)
    {
        if (string.IsNullOrEmpty(namespaceName))
            return null;

        foreach (var (prefix, tag) in _entries)
        {
            if (string.IsNullOrEmpty(prefix))
                continue;
            if (namespaceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return tag;
        }

        return null;
    }

    public string? GetTagForUsing(string? usingDirective)
    {
        if (string.IsNullOrWhiteSpace(usingDirective))
            return null;

        return GetTagForNamespace(NormalizePrefix(usingDirective.TrimEnd('.')));
    }

    public bool AreDifferentFrameworks(IEnumerable<string> tagsA, IEnumerable<string> tagsB)
    {
        var setA = new HashSet<string>(tagsA);
        var setB = new HashSet<string>(tagsB);
        setA.ExceptWith(["msdi", "ms-extensions"]);
        setB.ExceptWith(["msdi", "ms-extensions"]);
        return setA.Count > 0 && setB.Count > 0 && !setA.Overlaps(setB);
    }

    private static string NormalizePrefix(string prefix)
    {
        var trimmed = prefix.Trim();
        return trimmed.EndsWith('.') ? trimmed : trimmed + ".";
    }

    private static bool PrefixesOverlap(string a, string b) =>
        a.StartsWith(b, StringComparison.OrdinalIgnoreCase) ||
        b.StartsWith(a, StringComparison.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class FrameworksConfigFile
    {
        public List<FrameworkConfigEntry>? Frameworks { get; set; }
    }

    private sealed class FrameworkConfigEntry
    {
        public string Tag { get; set; } = string.Empty;

        [JsonPropertyName("namespace_prefixes")]
        public List<string>? NamespacePrefixes { get; set; }
    }
}
