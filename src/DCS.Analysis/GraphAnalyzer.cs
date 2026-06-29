using DCS.Core.IR;

namespace DCS.Analysis;

public sealed class GraphAnalyzer
{
    private readonly RegistrationGraph _graph;
    private readonly FrameworkBoundaryModel _boundaries;
    private readonly string? _rootOverride;

    public GraphAnalyzer(
        RegistrationGraph graph,
        FrameworkBoundaryModel? boundaries = null,
        string? rootClassOverride = null)
    {
        _graph = graph;
        _boundaries = boundaries ?? FrameworkBoundaryModel.Default;
        _rootOverride = rootClassOverride;
    }

    public AnalysisResult Analyze()
    {
        // Duplicate IDs occur when two registrations share the same short name (no semantic FQN).
        // Take first per ID; duplicates are reported via FindDuplicates() using short name.
        var nodeById = _graph.Nodes
            .GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var inEdges = _graph.Edges
            .GroupBy(e => e.To, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var outEdges = _graph.Edges
            .GroupBy(e => e.From, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var rootId = FindCompositionRoot(nodeById, outEdges);
        var reachable = FindReachable(rootId, outEdges);

        return new AnalysisResult
        {
            Leaked = FindLeaked(nodeById),
            Orphaned = string.Equals(_graph.SourceLanguage, "java", StringComparison.OrdinalIgnoreCase)
                ? []
                : FindOrphaned(nodeById, inEdges, rootId, reachable),
            BrokenChains = FindBrokenChains(nodeById, outEdges),
            Duplicates = FindDuplicates(),
            Cycles = FindCycles(nodeById, outEdges),
            CompositionRootId = rootId,
            TotalNodes = _graph.Nodes.Count,
            TotalEdges = _graph.Edges.Count,
            TotalBlindSpots = _graph.BlindSpots.Count
        };
    }

    private string? FindCompositionRoot(
        Dictionary<string, RegistrationNode> nodes,
        Dictionary<string, List<DependencyEdge>> outEdges)
    {
        if (_rootOverride != null)
        {
            var match = nodes.Values.FirstOrDefault(n =>
                n.DisplayName.Equals(_rootOverride, StringComparison.Ordinal) ||
                n.ConcreteImpl?.ShortName.Equals(_rootOverride, StringComparison.Ordinal) == true);
            if (match != null) return match.Id;
        }

        var priorityFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Program.cs", "Startup.cs", "AppHost.cs", "ServiceRegistration.cs", "DependencyInjection.cs" };

        return nodes.Values
            .OrderByDescending(n =>
            {
                var edgeCount = outEdges.TryGetValue(n.Id, out var edges) ? edges.Count : 0;
                var fileName = Path.GetFileName(n.SourceLocation?.FilePath ?? string.Empty);
                var simpleName = n.ExposedType?.ShortName ?? n.DisplayName;
                var boost = priorityFiles.Contains(fileName) ? 2
                    : simpleName.EndsWith("Application", StringComparison.Ordinal) ? 2
                    : 1;
                return edgeCount * boost;
            })
            .FirstOrDefault()?.Id;
    }

    private static HashSet<string> FindReachable(
        string? rootId,
        Dictionary<string, List<DependencyEdge>> outEdges)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        if (rootId == null) return visited;

        var queue = new Queue<string>();
        queue.Enqueue(rootId);
        visited.Add(rootId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!outEdges.TryGetValue(current, out var edges)) continue;
            foreach (var edge in edges)
                if (visited.Add(edge.To))
                    queue.Enqueue(edge.To);
        }

        return visited;
    }

    private List<LeakedRegistration> FindLeaked(Dictionary<string, RegistrationNode> nodes)
    {
        var leaked = new List<LeakedRegistration>();
        var seenPairs = new HashSet<string>(StringComparer.Ordinal);

        // Pass 1: cross-framework dependency edges (A depends on B, A and B are different frameworks)
        foreach (var edge in _graph.Edges)
        {
            if (!nodes.TryGetValue(edge.From, out var from) ||
                !nodes.TryGetValue(edge.To, out var to))
                continue;

            if (_boundaries.AreDifferentFrameworks(from.FrameworkTags, to.FrameworkTags))
            {
                leaked.Add(new LeakedRegistration(
                    from.Id,
                    from.DisplayName,
                    string.Join(",", from.FrameworkTags),
                    string.Join(",", to.FrameworkTags),
                    from.SourceLocation?.FilePath,
                    from.SourceLocation?.Line));
            }
        }

        // Pass 2: same abstract token (same Id / FQN) registered in multiple framework contexts.
        // This is the primary migration leakage pattern: IFoo registered once in WinUI shell
        // and once in Avalonia shell — both collapse to the same logical Id but have different
        // FrameworkTags. Detected by grouping all instances per Id and checking tag conflicts.
        var instanceGroups = _graph.Nodes
            .GroupBy(n => n.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1);

        foreach (var group in instanceGroups)
        {
            var instances = group.ToList();
            for (var i = 0; i < instances.Count - 1; i++)
            {
                for (var j = i + 1; j < instances.Count; j++)
                {
                    var a = instances[i];
                    var b = instances[j];
                    if (!_boundaries.AreDifferentFrameworks(a.FrameworkTags, b.FrameworkTags))
                        continue;

                    var pairKey = $"{a.InstanceId}:{b.InstanceId}";
                    if (!seenPairs.Add(pairKey)) continue;

                    leaked.Add(new LeakedRegistration(
                        a.Id,
                        a.DisplayName,
                        string.Join(",", a.FrameworkTags),
                        string.Join(",", b.FrameworkTags),
                        a.SourceLocation?.FilePath,
                        a.SourceLocation?.Line));
                }
            }
        }

        return leaked;
    }

    private static List<OrphanedRegistration> FindOrphaned(
        Dictionary<string, RegistrationNode> nodes,
        Dictionary<string, List<DependencyEdge>> inEdges,
        string? rootId,
        HashSet<string> reachable)
    {
        return nodes.Values
            .Where(n =>
                n.Id != rootId &&
                !inEdges.ContainsKey(n.Id) &&
                !reachable.Contains(n.Id))
            .Select(n => new OrphanedRegistration(
                n.Id, n.DisplayName,
                n.SourceLocation?.FilePath,
                n.SourceLocation?.Line))
            .OrderBy(o => o.DisplayName, StringComparer.Ordinal)
            .ToList();
    }

    private List<BrokenChain> FindBrokenChains(
        Dictionary<string, RegistrationNode> nodes,
        Dictionary<string, List<DependencyEdge>> outEdges)
    {
        var broken = new List<BrokenChain>();

        foreach (var node in nodes.Values)
        {
            if (!outEdges.TryGetValue(node.Id, out var edges)) continue;
            foreach (var edge in edges)
            {
                if (!nodes.TryGetValue(edge.To, out var target)) continue;
                if (target.ParserConfidence == Confidence.BlindSpot)
                {
                    broken.Add(new BrokenChain(
                        node.Id, node.DisplayName,
                        target.AbstractToken.ShortName,
                        node.SourceLocation?.FilePath,
                        node.SourceLocation?.Line));
                }
            }
        }

        return broken;
    }

    private List<DuplicateAbstractToken> FindDuplicates() =>
        _graph.Nodes
            .GroupBy(n => n.AbstractToken.ShortName, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => new DuplicateAbstractToken(g.Key, g.Select(n => n.Id).ToList()))
            .OrderBy(d => d.AbstractTokenName, StringComparer.Ordinal)
            .ToList();

    private static List<List<string>> FindCycles(
        Dictionary<string, RegistrationNode> nodes,
        Dictionary<string, List<DependencyEdge>> outEdges)
    {
        var cycles = new List<List<string>>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var inStack = new HashSet<string>(StringComparer.Ordinal);
        var pathList = new List<string>();

        void Dfs(string nodeId)
        {
            if (inStack.Contains(nodeId))
            {
                var startIdx = pathList.IndexOf(nodeId);
                if (startIdx >= 0)
                    cycles.Add(pathList[startIdx..]);
                return;
            }

            if (!visited.Add(nodeId)) return;

            inStack.Add(nodeId);
            pathList.Add(nodeId);

            if (outEdges.TryGetValue(nodeId, out var edges))
                foreach (var edge in edges)
                    Dfs(edge.To);

            pathList.RemoveAt(pathList.Count - 1);
            inStack.Remove(nodeId);
        }

        foreach (var nodeId in nodes.Keys)
            if (!visited.Contains(nodeId))
                Dfs(nodeId);

        return cycles;
    }
}
