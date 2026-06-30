using DCS.Core.IR;

namespace DCS.Analysis;

public sealed record GraphPathResult
{
    public bool Success { get; init; }
    public bool IsAmbiguous { get; init; }
    public string? Error { get; init; }
    public string? FromNodeId { get; init; }
    public string? ToNodeId { get; init; }
    public IReadOnlyList<RegistrationNode> Nodes { get; init; } = [];
    public IReadOnlyList<DependencyEdge> Edges { get; init; } = [];

    public static GraphPathResult Failed(string error, bool ambiguous = false) =>
        new() { Success = false, IsAmbiguous = ambiguous, Error = error };
}

public static class GraphPathFinder
{
    public static IReadOnlyList<string> GetDefaultSeedNodeIds(
        RegistrationGraph graph,
        string? rootOverride = null)
    {
        var nodeById = graph.Nodes
            .GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        return GraphReachabilitySeeds.GetSeedNodeIds(graph, rootOverride, nodeById);
    }

    public static GraphPathResult FindPath(
        RegistrationGraph graph,
        string? fromQuery,
        string toQuery,
        string? rootOverride = null)
    {
        if (string.IsNullOrWhiteSpace(toQuery))
            return GraphPathResult.Failed("Target query is required.");

        var nodeById = graph.Nodes
            .GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        if (nodeById.Count == 0)
            return GraphPathResult.Failed("Graph has no registration nodes.");

        var outEdges = graph.Edges
            .GroupBy(e => e.From, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var (toNodes, toError, toAmbiguous) = ResolveQuery(graph.Nodes, toQuery);
        if (toError != null)
            return GraphPathResult.Failed(toError, toAmbiguous);

        var toIds = toNodes!.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

        List<string> fromIds;
        if (!string.IsNullOrWhiteSpace(fromQuery))
        {
            var (fromNodes, fromError, fromAmbiguous) = ResolveQuery(graph.Nodes, fromQuery);
            if (fromError != null)
                return GraphPathResult.Failed(fromError, fromAmbiguous);
            fromIds = fromNodes!.Select(n => n.Id).ToList();
        }
        else
        {
            fromIds = GraphReachabilitySeeds.GetSeedNodeIds(graph, rootOverride, nodeById);
            if (fromIds.Count == 0)
                return GraphPathResult.Failed("No default path origin (composition root seeds) found.");
        }

        if (fromIds.Any(toIds.Contains))
        {
            var origin = fromIds.First(toIds.Contains);
            var node = nodeById[origin];
            return new GraphPathResult
            {
                Success = true,
                FromNodeId = origin,
                ToNodeId = origin,
                Nodes = [node],
                Edges = []
            };
        }

        var (pathNodeIds, pathEdges) = FindShortestPath(fromIds, toIds, outEdges);
        if (pathNodeIds == null)
            return GraphPathResult.Failed($"No dependency path found to '{toQuery}'.");

        return new GraphPathResult
        {
            Success = true,
            FromNodeId = pathNodeIds[0],
            ToNodeId = pathNodeIds[^1],
            Nodes = pathNodeIds.Select(id => nodeById[id]).ToList(),
            Edges = pathEdges
        };
    }

    internal static (List<RegistrationNode>? nodes, string? error, bool ambiguous) ResolveQuery(
        IReadOnlyList<RegistrationNode> nodes,
        string query)
    {
        var trimmed = query.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return (null, "Query is empty.", false);

        var byId = nodes.Where(n => string.Equals(n.Id, trimmed, StringComparison.OrdinalIgnoreCase)).ToList();
        if (byId.Count == 1)
            return (byId, null, false);
        if (byId.Count > 1)
            return (null, $"Ambiguous registration id '{trimmed}'.", true);

        var byDisplay = nodes
            .Where(n => string.Equals(n.DisplayName, trimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byDisplay.Count == 1)
            return (byDisplay, null, false);
        if (byDisplay.Count > 1)
            return (null, $"Ambiguous display name '{trimmed}' ({byDisplay.Count} registrations).", true);

        var byFqn = nodes
            .Where(n => string.Equals(n.AbstractToken.FullyQualifiedName, trimmed, StringComparison.Ordinal))
            .ToList();
        if (byFqn.Count == 1)
            return (byFqn, null, false);
        if (byFqn.Count > 1)
            return (null, $"Ambiguous fully qualified name '{trimmed}'.", true);

        var byShort = nodes
            .Where(n => string.Equals(n.AbstractToken.ShortName, trimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byShort.Count == 1)
            return (byShort, null, false);
        if (byShort.Count > 1)
            return (null, $"Ambiguous short name '{trimmed}' ({byShort.Count} registrations).", true);

        return (null, $"No registration matched '{trimmed}'.", false);
    }

    private static (List<string>? nodeIds, List<DependencyEdge> edges) FindShortestPath(
        IReadOnlyList<string> fromIds,
        HashSet<string> toIds,
        Dictionary<string, List<DependencyEdge>> outEdges)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var parentNode = new Dictionary<string, string?>(StringComparer.Ordinal);
        var parentEdge = new Dictionary<string, DependencyEdge?>(StringComparer.Ordinal);
        var queue = new Queue<string>();

        foreach (var fromId in fromIds.Distinct(StringComparer.Ordinal))
        {
            if (!visited.Add(fromId))
                continue;
            parentNode[fromId] = null;
            parentEdge[fromId] = null;
            queue.Enqueue(fromId);
        }

        string? goal = null;
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (toIds.Contains(current))
            {
                goal = current;
                break;
            }

            if (!outEdges.TryGetValue(current, out var edges))
                continue;

            foreach (var edge in edges)
            {
                if (!visited.Add(edge.To))
                    continue;
                parentNode[edge.To] = current;
                parentEdge[edge.To] = edge;
                queue.Enqueue(edge.To);
            }
        }

        if (goal == null)
            return (null, []);

        var pathIds = new List<string>();
        var pathEdges = new List<DependencyEdge>();
        for (var cursor = goal; cursor != null; cursor = parentNode.GetValueOrDefault(cursor))
            pathIds.Add(cursor);
        pathIds.Reverse();

        for (var i = 1; i < pathIds.Count; i++)
        {
            var edge = parentEdge[pathIds[i]];
            if (edge != null)
                pathEdges.Add(edge);
        }

        return (pathIds, pathEdges);
    }
}

internal static class GraphReachabilitySeeds
{
    internal static List<string> GetSeedNodeIds(
        RegistrationGraph graph,
        string? rootOverride,
        Dictionary<string, RegistrationNode> nodeById)
    {
        if (!string.IsNullOrWhiteSpace(rootOverride))
        {
            var (matches, error, _) = GraphPathFinder.ResolveQuery(graph.Nodes, rootOverride);
            if (error == null && matches != null && matches.Count > 0)
                return matches.Select(n => n.Id).ToList();
        }

        var priorityFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CompositionRoot.cs", "App.axaml.cs", "App.xaml.cs", "Program.cs", "Startup.cs"
        };

        var seeds = graph.Nodes
            .Where(n =>
            {
                var fileName = Path.GetFileName(n.SourceLocation?.FilePath ?? string.Empty);
                return priorityFiles.Contains(fileName) ||
                       FindingPolicy.IsSecondaryReachabilityRootFile(n.SourceLocation?.FilePath);
            })
            .Select(n => n.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (seeds.Count > 0)
            return seeds;

        var fallback = graph.Nodes
            .OrderByDescending(n => Path.GetFileName(n.SourceLocation?.FilePath ?? string.Empty), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.Id;

        return fallback != null ? [fallback] : [];
    }
}
