namespace DCS.Parser.CSharp.Semantic;

public static class CrossTfmProjectReferenceResolver
{
    /// <summary>
    /// Expands an active TFM scope set with portable (or compatible) dependency scopes
    /// referenced via MSBuild project references but not multi-targeting the active TFM.
    /// </summary>
    public static IReadOnlyList<ProjectTargetScope> ExpandWithReferencedScopes(
        IReadOnlyList<ProjectTargetScope> activeScopes,
        IReadOnlyList<ProjectTargetScope> allScopes)
    {
        var byCsproj = allScopes
            .GroupBy(s => s.CsprojPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var included = activeScopes.ToDictionary(s => s.ScopeId, StringComparer.Ordinal);
        var changed = true;

        while (changed)
        {
            changed = false;
            foreach (var scope in included.Values.ToList())
            {
                foreach (var pref in scope.ProjectReferences)
                {
                    if (included.Values.Any(s => s.CsprojPath.Equals(pref, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var compatible = FindCompatibleScope(pref, scope.TargetFramework, byCsproj);
                    if (compatible == null || included.ContainsKey(compatible.ScopeId))
                        continue;

                    included[compatible.ScopeId] = compatible;
                    changed = true;
                }
            }
        }

        return ProjectReferenceClosureOrder.SortTopologically(included.Values.ToList());
    }

    public static ProjectTargetScope? FindScopeForProjectReference(
        string projectReferencePath,
        string consumerTargetFramework,
        IReadOnlyList<ProjectTargetScope> candidateScopes)
    {
        var byCsproj = candidateScopes
            .GroupBy(s => s.CsprojPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        return FindCompatibleScope(projectReferencePath, consumerTargetFramework, byCsproj);
    }

    public static bool IsReferenceCompatible(string providerTfm, string consumerTfm)
    {
        if (string.Equals(providerTfm, consumerTfm, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!consumerTfm.Contains('-', StringComparison.Ordinal))
            return false;

        var portableConsumer = GetPortableTargetFrameworkMoniker(consumerTfm);
        return string.Equals(providerTfm, portableConsumer, StringComparison.OrdinalIgnoreCase);
    }

    internal static string GetPortableTargetFrameworkMoniker(string targetFramework)
    {
        var dash = targetFramework.IndexOf('-', StringComparison.Ordinal);
        return dash > 0 ? targetFramework[..dash] : targetFramework;
    }

    private static ProjectTargetScope? FindCompatibleScope(
        string projectReferencePath,
        string consumerTargetFramework,
        IReadOnlyDictionary<string, List<ProjectTargetScope>> scopesByCsproj)
    {
        if (!scopesByCsproj.TryGetValue(projectReferencePath, out var candidates) || candidates.Count == 0)
            return null;

        var exact = candidates.FirstOrDefault(s =>
            string.Equals(s.TargetFramework, consumerTargetFramework, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact;

        return candidates
            .Where(s => IsReferenceCompatible(s.TargetFramework, consumerTargetFramework))
            .OrderByDescending(s => s.TargetFramework, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }
}
