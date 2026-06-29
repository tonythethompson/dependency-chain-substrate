namespace DCS.Parser.CSharp.Semantic;

public static class ProjectReferenceClosureOrder
{
    public static IReadOnlyList<ProjectTargetScope> SortTopologically(IReadOnlyList<ProjectTargetScope> scopes)
    {
        var byCsproj = scopes
            .GroupBy(s => s.CsprojPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var scopeById = scopes.ToDictionary(s => s.ScopeId, StringComparer.Ordinal);
        var inDegree = scopes.ToDictionary(s => s.ScopeId, _ => 0, StringComparer.Ordinal);
        var adj = scopes.ToDictionary(s => s.ScopeId, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var scope in scopes)
        {
            foreach (var pref in scope.ProjectReferences)
            {
                var match = byCsproj.Keys.FirstOrDefault(k =>
                    k.Equals(pref, StringComparison.OrdinalIgnoreCase));
                if (match == null) continue;
                var depScope = byCsproj[match];
                if (depScope.ScopeId == scope.ScopeId) continue;
                adj[depScope.ScopeId].Add(scope.ScopeId);
                inDegree[scope.ScopeId]++;
            }
        }

        var queue = new Queue<string>(inDegree.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var result = new List<ProjectTargetScope>();

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            result.Add(scopeById[id]);
            foreach (var next in adj[id])
            {
                inDegree[next]--;
                if (inDegree[next] == 0)
                    queue.Enqueue(next);
            }
        }

        foreach (var scope in scopes)
            if (!result.Any(r => r.ScopeId == scope.ScopeId))
                result.Add(scope);

        return result;
    }
}
