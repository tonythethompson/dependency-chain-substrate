namespace DCS.Analysis;

public sealed class FrameworkBoundaryModel
{
    public static readonly FrameworkBoundaryModel Default = new();

    // Ordered by specificity (more specific prefix wins over broader one)
    private static readonly (string Prefix, string Tag)[] BuiltIn =
    [
        ("Microsoft.Extensions.DependencyInjection.", "msdi"),
        ("Microsoft.Extensions.", "ms-extensions"),
        ("Microsoft.AspNetCore.", "aspnetcore"),
        ("Microsoft.UI.", "winui"),
        ("WinUI.", "winui"),
        ("Avalonia.", "avalonia"),
        ("System.Windows.", "wpf"),
    ];

    private readonly (string Prefix, string Tag)[] _entries;

    public FrameworkBoundaryModel() => _entries = BuiltIn;

    public FrameworkBoundaryModel((string Prefix, string Tag)[] custom) =>
        _entries = [.. custom, .. BuiltIn];

    public string? GetTagForNamespace(string namespaceName)
    {
        foreach (var (prefix, tag) in _entries)
            if (namespaceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return tag;
        return null;
    }

    public string? GetTagForUsing(string usingDirective) =>
        GetTagForNamespace(usingDirective.TrimEnd('.') + ".");

    public bool AreDifferentFrameworks(IEnumerable<string> tagsA, IEnumerable<string> tagsB)
    {
        var setA = new HashSet<string>(tagsA);
        var setB = new HashSet<string>(tagsB);
        setA.ExceptWith(["msdi", "ms-extensions"]); // infrastructure, not boundaries
        setB.ExceptWith(["msdi", "ms-extensions"]);
        return setA.Count > 0 && setB.Count > 0 && !setA.Overlaps(setB);
    }
}
