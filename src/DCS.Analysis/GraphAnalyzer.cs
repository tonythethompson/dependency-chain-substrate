using DCS.Core.IR;

namespace DCS.Analysis;

public sealed class GraphAnalyzer
{
    private readonly RegistrationGraph _graph;
    private readonly FrameworkBoundaryModel _boundaries;
    private readonly string? _rootOverride;
    private readonly FindingPolicyOptions _policy;
    private readonly bool _islandAware;

    public GraphAnalyzer(
        RegistrationGraph graph,
        FrameworkBoundaryModel? boundaries = null,
        string? rootClassOverride = null,
        FindingPolicyOptions? policy = null,
        bool islandAware = false)
    {
        _graph = graph;
        _boundaries = boundaries ?? FrameworkBoundaryModel.Default;
        _rootOverride = rootClassOverride;
        _policy = policy ?? FindingPolicyOptions.Default;
        _islandAware = islandAware;
    }

    public AnalysisResult Analyze()
    {
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
        var legacyReachable = FindReachableFromRoots(
            _islandAware ? FindLegacyReachabilitySeeds(nodeById) : FindReachabilitySeeds(nodeById),
            outEdges);
        var reachable = _islandAware
            ? legacyReachable
            : FindReachableFromRoots(FindReachabilitySeeds(nodeById), outEdges);
        var islandReachableByIsland = _islandAware
            ? BuildIslandReachable(nodeById, outEdges)
            : new Dictionary<CompositionIsland, HashSet<string>>();
        var islandSummaries = _islandAware
            ? BuildIslandSummaries(nodeById, inEdges, islandReachableByIsland)
            : [];
        var (orphaned, islandValidOrphans) = string.Equals(_graph.SourceLanguage, "java", StringComparison.OrdinalIgnoreCase)
            ? ([], [])
            : FindOrphaned(nodeById, inEdges, rootId, reachable, islandReachableByIsland, _islandAware);

        var actionableBlindSpots = FindingPolicy.ActionableBlindSpots(_graph.BlindSpots, _policy).Count();
        var actionableUnresolved = CountActionableUnresolved();

        return new AnalysisResult
        {
            Leaked = FindLeaked(),
            Orphaned = orphaned,
            IslandValidOrphans = islandValidOrphans,
            BrokenChains = FindBrokenChains(nodeById, outEdges),
            Duplicates = FindStrictDuplicates(),
            PossibleDuplicates = FindPossibleDuplicates(),
            Cycles = FindCycles(nodeById, outEdges),
            CompositionRootId = rootId,
            TotalNodes = _graph.Nodes.Count,
            TotalEdges = _graph.Edges.Count,
            TotalBlindSpots = actionableBlindSpots,
            TotalUnresolvedInjections = actionableUnresolved,
            IslandSummaries = islandSummaries
        };
    }

    private int CountActionableUnresolved() =>
        _graph.UnresolvedInjections.Count(u =>
            FindingPolicy.IsActionableUnresolved(
                u.DeclaredType.ShortName,
                u.DeclaredType.FullyQualifiedName,
                _policy));

    private static List<string> FindReachabilitySeeds(Dictionary<string, RegistrationNode> nodes) =>
        CollectReachabilitySeeds(nodes, FindingPolicy.IsSecondaryReachabilityRootFile);

    private static List<string> FindLegacyReachabilitySeeds(Dictionary<string, RegistrationNode> nodes) =>
        CollectReachabilitySeeds(nodes, FindingPolicy.IsLegacyReachabilityRootFile);

    private static List<string> CollectReachabilitySeeds(
        Dictionary<string, RegistrationNode> nodes,
        Func<string?, bool> isRootFile)
    {
        var seeds = FindCompositionRootCandidates(nodes);
        var secondary = nodes.Values
            .Where(n => isRootFile(n.SourceLocation?.FilePath))
            .Select(n => n.Id);

        return seeds.Concat(secondary).Distinct(StringComparer.Ordinal).ToList();
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
        {
            "Program.cs", "Startup.cs", "AppHost.cs", "ServiceRegistration.cs",
            "DependencyInjection.cs", "CompositionRoot.cs", "App.axaml.cs", "App.xaml.cs"
        };

        return nodes.Values
            .OrderByDescending(n =>
            {
                var fileName = Path.GetFileName(n.SourceLocation?.FilePath ?? string.Empty);
                var filePath = n.SourceLocation?.FilePath?.Replace('\\', '/').ToLowerInvariant() ?? string.Empty;
                var boost = priorityFiles.Contains(fileName) ? 4
                    : filePath.Contains("compositionroot", StringComparison.Ordinal) ? 4
                    : 1;
                return boost;
            })
            .FirstOrDefault()?.Id;
    }

    private static List<string> FindCompositionRootCandidates(Dictionary<string, RegistrationNode> nodes)
    {
        var priorityFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CompositionRoot.cs", "App.axaml.cs", "App.xaml.cs", "Program.cs", "Startup.cs"
        };

        var candidates = nodes.Values
            .Where(n =>
            {
                var fileName = Path.GetFileName(n.SourceLocation?.FilePath ?? string.Empty);
                return priorityFiles.Contains(fileName);
            })
            .Select(n => n.Id)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (candidates.Count > 0)
            return candidates;

        var fallback = nodes.Values
            .OrderByDescending(n => Path.GetFileName(n.SourceLocation?.FilePath ?? string.Empty), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()?.Id;
        return fallback != null ? [fallback] : [];
    }

    private static HashSet<string> FindReachableFromRoots(
        IReadOnlyList<string> rootIds,
        Dictionary<string, List<DependencyEdge>> outEdges)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rootId in rootIds)
        {
            foreach (var id in FindReachable(rootId, outEdges))
                visited.Add(id);
        }

        return visited;
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

    private List<LeakedRegistration> FindLeaked()
    {
        var leaked = new List<LeakedRegistration>();
        var seenPairs = new HashSet<string>(StringComparer.Ordinal);
        var nodeById = _graph.Nodes
            .GroupBy(n => n.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        foreach (var edge in _graph.Edges)
        {
            if (!nodeById.TryGetValue(edge.From, out var from) ||
                !nodeById.TryGetValue(edge.To, out var to))
                continue;

            if (_boundaries.AreDifferentFrameworks(from.FrameworkTags, to.FrameworkTags))
            {
                leaked.Add(new LeakedRegistration(
                    from.Id, from.DisplayName,
                    string.Join(",", from.FrameworkTags),
                    string.Join(",", to.FrameworkTags),
                    from.SourceLocation?.FilePath,
                    from.SourceLocation?.Line));
            }
        }

        var instanceGroups = _graph.Nodes
            .Where(n => !string.IsNullOrEmpty(n.DuplicateGroupKey))
            .GroupBy(n => n.DuplicateGroupKey, StringComparer.Ordinal)
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

                    var pairKey = $"{a.Id}:{b.Id}";
                    if (!seenPairs.Add(pairKey)) continue;

                    leaked.Add(new LeakedRegistration(
                        a.Id, a.DisplayName,
                        string.Join(",", a.FrameworkTags),
                        string.Join(",", b.FrameworkTags),
                        a.SourceLocation?.FilePath,
                        a.SourceLocation?.Line));
                }
            }
        }

        return leaked;
    }

    private static (List<OrphanedRegistration> TrueOrphans, List<OrphanedRegistration> IslandValid) FindOrphaned(
        Dictionary<string, RegistrationNode> nodes,
        Dictionary<string, List<DependencyEdge>> inEdges,
        string? rootId,
        HashSet<string> reachable,
        Dictionary<CompositionIsland, HashSet<string>> islandReachableByIsland,
        bool islandAware)
    {
        var candidates = nodes.Values
            .Where(n =>
                n.Id != rootId &&
                !inEdges.ContainsKey(n.Id) &&
                !reachable.Contains(n.Id) &&
                !IsFrameworkInfrastructure(n))
            .Select(n =>
            {
                var island = CompositionIslandAttribution.InferFromFilePath(n.SourceLocation?.FilePath);
                var islandValid = islandAware &&
                    island != CompositionIsland.Unknown &&
                    island != CompositionIsland.External &&
                    islandReachableByIsland.TryGetValue(island, out var islandReach) &&
                    islandReach.Contains(n.Id);
                return new OrphanedRegistration(
                    n.Id, n.DisplayName,
                    n.SourceLocation?.FilePath,
                    n.SourceLocation?.Line,
                    island,
                    islandValid);
            })
            .ToList();

        var trueOrphans = candidates
            .Where(o => !islandAware || !o.IsIslandValid)
            .OrderBy(o => o.DisplayName, StringComparer.Ordinal)
            .ToList();
        var islandValid = candidates
            .Where(o => islandAware && o.IsIslandValid)
            .OrderBy(o => o.DisplayName, StringComparer.Ordinal)
            .ToList();

        return (trueOrphans, islandValid);
    }

    private static Dictionary<CompositionIsland, HashSet<string>> BuildIslandReachable(
        Dictionary<string, RegistrationNode> nodes,
        Dictionary<string, List<DependencyEdge>> outEdges)
    {
        var lookup = new Dictionary<CompositionIsland, HashSet<string>>();
        foreach (var island in new[] { CompositionIsland.Desktop, CompositionIsland.Api, CompositionIsland.Lambda })
        {
            var seeds = nodes.Values
                .Where(n => FindingPolicy.IsIslandReachabilitySeedFile(n.SourceLocation?.FilePath, island))
                .Select(n => n.Id)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (seeds.Count == 0)
                continue;

            lookup[island] = FindReachableFromRoots(seeds, outEdges);
        }

        return lookup;
    }

    private static List<CompositionIslandSummary> BuildIslandSummaries(
        Dictionary<string, RegistrationNode> nodes,
        Dictionary<string, List<DependencyEdge>> inEdges,
        Dictionary<CompositionIsland, HashSet<string>> islandReachableByIsland)
    {
        var summaries = new List<CompositionIslandSummary>();
        foreach (var (island, islandReachable) in islandReachableByIsland)
        {
            var islandNodes = nodes.Values
                .Where(n => CompositionIslandAttribution.InferFromFilePath(n.SourceLocation?.FilePath) == island)
                .ToList();

            var zeroInDegree = islandNodes
                .Where(n => !inEdges.ContainsKey(n.Id) && !IsFrameworkInfrastructure(n))
                .ToList();
            var islandValid = zeroInDegree.Count(n => islandReachable.Contains(n.Id));
            var trueOrphans = zeroInDegree.Count(n => !islandReachable.Contains(n.Id));

            summaries.Add(new CompositionIslandSummary
            {
                Island = island,
                SeedCount = nodes.Values.Count(n =>
                    FindingPolicy.IsIslandReachabilitySeedFile(n.SourceLocation?.FilePath, island)),
                ReachableCount = islandReachable.Count,
                OrphanedCount = zeroInDegree.Count,
                IslandValidCount = islandValid,
                TrueOrphanCount = trueOrphans
            });
        }

        return summaries;
    }

    private static bool IsFrameworkInfrastructure(RegistrationNode node)
    {
        var name = node.DisplayName;
        if (string.IsNullOrEmpty(name))
            return false;

        return name.StartsWith("ILogger", StringComparison.Ordinal) ||
               name.StartsWith("IHostEnvironment", StringComparison.Ordinal) ||
               name.StartsWith("IConfiguration", StringComparison.Ordinal) ||
               name.StartsWith("IServiceProvider", StringComparison.Ordinal) ||
               name.StartsWith("IHttpClientFactory", StringComparison.Ordinal) ||
               name.StartsWith("IStringLocalizer", StringComparison.Ordinal) ||
               name.StartsWith("IOptions", StringComparison.Ordinal);
    }

    private static List<BrokenChain> FindBrokenChains(
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
                if (target.ParserConfidence != Confidence.BlindSpot)
                    continue;

                if (IsParameterlessShallowFactory(target))
                    continue;

                broken.Add(new BrokenChain(
                    node.Id, node.DisplayName,
                    target.AbstractToken.ShortName,
                    node.SourceLocation?.FilePath,
                    node.SourceLocation?.Line));
            }
        }

        return broken;
    }

    private static bool IsParameterlessShallowFactory(RegistrationNode target) =>
        string.Equals(
            target.Annotations.GetValueOrDefault("pattern"),
            "factory_lambda_shallow",
            StringComparison.Ordinal) &&
        !target.Annotations.ContainsKey("factory_lambda_service_keys");

    private List<DuplicateAbstractToken> FindStrictDuplicates() =>
        _graph.Nodes
            .Where(StrictDuplicateEligibility.IsEligible)
            .GroupBy(StrictDuplicateGroupKey, StringComparer.Ordinal)
            .Where(g => g.Count() > 1
                && !FindingPolicy.IsIntentionalTryAddOverride(g.ToList(), _policy)
                && !FindingPolicy.IsRedundantTryAddDuplicate(g.ToList(), _policy)
                && !FindingPolicy.IsMutuallyExclusiveIfElseBranch(g.ToList(), _policy)
                && !(_islandAware && FindingPolicy.IsCrossCompositionIslandDuplicate(g.ToList())))
            .Select(g => new DuplicateAbstractToken(
                g.First().DisplayName,
                g.Select(n => n.Id).ToList(),
                IsStrict: true))
            .OrderBy(d => d.AbstractTokenName, StringComparer.Ordinal)
            .ToList();

    private string StrictDuplicateGroupKey(RegistrationNode node)
    {
        if (!_islandAware)
            return node.DuplicateGroupKey;

        var island = CompositionIslandAttribution.InferFromFilePath(node.SourceLocation?.FilePath);
        return $"{node.DuplicateGroupKey}|{CompositionIslandAttribution.ToAnnotationValue(island)}";
    }

    private List<DuplicateAbstractToken> FindPossibleDuplicates() =>
        _graph.Nodes
            .GroupBy(n => n.AbstractToken.ShortName, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Where(g =>
            {
                var distinctKeys = g
                    .Select(n => n.DuplicateGroupKey)
                    .Where(k => !string.IsNullOrEmpty(k))
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                return distinctKeys == 0 || distinctKeys > 1;
            })
            .Select(g => new DuplicateAbstractToken(
                g.Key,
                g.Select(n => n.Id).ToList(),
                IsStrict: false))
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
