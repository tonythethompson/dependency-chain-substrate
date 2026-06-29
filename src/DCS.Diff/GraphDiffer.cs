using DCS.Core.IR;

namespace DCS.Diff;

public sealed class GraphDiffer
{
    private const double RenameThreshold = 0.7;
    private const double NameWeight = 0.5;
    private const double DepJaccardWeight = 0.3;
    private const double LifetimeWeight = 0.2;

    public GraphDiff Diff(RegistrationGraph oldGraph, RegistrationGraph newGraph)
    {
        var oldNodes = oldGraph.Nodes;
        var newNodes = newGraph.Nodes;

        var nodeChanges = new List<NodeChange>();
        var matchedNewIds = new HashSet<string>(StringComparer.Ordinal);
        var matchedOldIds = new HashSet<string>(StringComparer.Ordinal);

        var oldOutEdges = BuildOutEdgeIndex(oldGraph.Edges);
        var newOutEdges = BuildOutEdgeIndex(newGraph.Edges);

        var oldByDupKey = GroupByDuplicateKey(oldNodes);
        var newByDupKey = GroupByDuplicateKey(newNodes);

        foreach (var (dupKey, oldGroup) in oldByDupKey)
        {
            if (!newByDupKey.TryGetValue(dupKey, out var newGroup))
                continue;

            MatchWithinDuplicateKey(oldGroup, newGroup, matchedOldIds, matchedNewIds, nodeChanges);
        }

        foreach (var oldNode in oldNodes)
        {
            if (matchedOldIds.Contains(oldNode.Id)) continue;
            if (newNodes.Any(n => n.Id == oldNode.Id))
            {
                var newNode = newNodes.First(n => n.Id == oldNode.Id);
                matchedOldIds.Add(oldNode.Id);
                matchedNewIds.Add(newNode.Id);
                nodeChanges.AddRange(DetectInPlaceChanges(oldNode, newNode));
            }
        }

        var removedCandidates = oldNodes
            .Where(n => !matchedOldIds.Contains(n.Id))
            .ToList();
        var addedCandidates = newNodes
            .Where(n => !matchedNewIds.Contains(n.Id))
            .ToList();

        var renames = MatchRenames(removedCandidates, addedCandidates, oldOutEdges, newOutEdges);
        var renamedOldIds = new HashSet<string>(renames.Select(r => r.OldNode!.Id), StringComparer.Ordinal);
        var renamedNewIds = new HashSet<string>(renames.Select(r => r.NewNode!.Id), StringComparer.Ordinal);

        nodeChanges.AddRange(renames);

        foreach (var node in removedCandidates.Where(n => !renamedOldIds.Contains(n.Id)))
            nodeChanges.Add(new NodeChange(NodeChangeKind.Removed, node, null));

        foreach (var node in addedCandidates.Where(n => !renamedNewIds.Contains(n.Id)))
            nodeChanges.Add(new NodeChange(NodeChangeKind.Added, null, node));

        var oldEdgeIds = oldGraph.Edges.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var newEdgeIds = newGraph.Edges.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var edgeChanges = new List<EdgeChange>();
        var oldEdgeById = oldGraph.Edges.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var newEdgeById = newGraph.Edges.ToDictionary(e => e.Id, StringComparer.Ordinal);

        foreach (var id in oldEdgeIds.Where(id => !newEdgeIds.Contains(id)))
            edgeChanges.Add(new EdgeChange(EdgeChangeKind.Removed, oldEdgeById[id], null));

        foreach (var id in newEdgeIds.Where(id => !oldEdgeIds.Contains(id)))
            edgeChanges.Add(new EdgeChange(EdgeChangeKind.Added, null, newEdgeById[id]));

        return new GraphDiff
        {
            OldCommit = oldGraph.CommitSha,
            NewCommit = newGraph.CommitSha,
            NodeChanges = nodeChanges,
            EdgeChanges = edgeChanges
        };
    }

    private static Dictionary<string, List<RegistrationNode>> GroupByDuplicateKey(IReadOnlyList<RegistrationNode> nodes) =>
        nodes
            .Where(n => !string.IsNullOrEmpty(n.DuplicateGroupKey))
            .GroupBy(n => n.DuplicateGroupKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

    private static void MatchWithinDuplicateKey(
        List<RegistrationNode> oldGroup,
        List<RegistrationNode> newGroup,
        HashSet<string> matchedOldIds,
        HashSet<string> matchedNewIds,
        List<NodeChange> nodeChanges)
    {
        foreach (var oldNode in oldGroup)
        {
            if (matchedOldIds.Contains(oldNode.Id)) continue;

            var candidates = newGroup
                .Where(n => !matchedNewIds.Contains(n.Id))
                .Where(n => SameFilePath(oldNode, n))
                .Where(n => n.RegistrationStatementFingerprint == oldNode.RegistrationStatementFingerprint)
                .ToList();

            if (candidates.Count == 1)
            {
                var newNode = candidates[0];
                matchedOldIds.Add(oldNode.Id);
                matchedNewIds.Add(newNode.Id);
                nodeChanges.AddRange(DetectInPlaceChanges(oldNode, newNode));
                continue;
            }

            if (candidates.Count > 1)
            {
                var best = candidates
                    .OrderBy(n => LineDistance(oldNode, n))
                    .First();
                matchedOldIds.Add(oldNode.Id);
                matchedNewIds.Add(best.Id);
                nodeChanges.AddRange(DetectInPlaceChanges(oldNode, best));
            }
        }
    }

    private static bool SameFilePath(RegistrationNode a, RegistrationNode b) =>
        string.Equals(
            a.SourceLocation?.FilePath?.Replace('\\', '/'),
            b.SourceLocation?.FilePath?.Replace('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    private static int LineDistance(RegistrationNode a, RegistrationNode b)
    {
        var la = a.SourceLocation?.Line ?? 0;
        var lb = b.SourceLocation?.Line ?? 0;
        return Math.Abs(la - lb);
    }

    private static IEnumerable<NodeChange> DetectInPlaceChanges(
        RegistrationNode oldNode, RegistrationNode newNode)
    {
        if (oldNode.Lifetime != newNode.Lifetime)
            yield return new NodeChange(NodeChangeKind.LifetimeChanged, oldNode, newNode);

        if (oldNode.ConcreteImpl?.FullyQualifiedName != newNode.ConcreteImpl?.FullyQualifiedName)
            yield return new NodeChange(NodeChangeKind.ImplementationChanged, oldNode, newNode);

        if (oldNode.ParserConfidence != newNode.ParserConfidence)
            yield return new NodeChange(NodeChangeKind.ConfidenceChanged, oldNode, newNode);

        var oldTags = oldNode.FrameworkTags.OrderBy(t => t).ToList();
        var newTags = newNode.FrameworkTags.OrderBy(t => t).ToList();
        if (!oldTags.SequenceEqual(newTags))
            yield return new NodeChange(NodeChangeKind.FrameworkTagsChanged, oldNode, newNode);
    }

    private static List<NodeChange> MatchRenames(
        List<RegistrationNode> removed,
        List<RegistrationNode> added,
        Dictionary<string, List<string>> oldOutEdges,
        Dictionary<string, List<string>> newOutEdges)
    {
        if (removed.Count == 0 || added.Count == 0) return [];

        var pairs = new List<(double score, RegistrationNode old, RegistrationNode newNode)>();
        foreach (var oldNode in removed)
            foreach (var newNode in added)
            {
                var score = ComputeSimilarity(oldNode, newNode, oldOutEdges, newOutEdges);
                if (score >= RenameThreshold)
                    pairs.Add((score, oldNode, newNode));
            }

        pairs.Sort((a, b) => b.score.CompareTo(a.score));

        var usedOld = new HashSet<string>(StringComparer.Ordinal);
        var usedNew = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<NodeChange>();

        foreach (var (score, oldNode, newNode) in pairs)
        {
            if (usedOld.Contains(oldNode.Id) || usedNew.Contains(newNode.Id)) continue;
            usedOld.Add(oldNode.Id);
            usedNew.Add(newNode.Id);
            result.Add(new NodeChange(NodeChangeKind.Renamed, oldNode, newNode, score));
        }

        return result;
    }

    private static double ComputeSimilarity(
        RegistrationNode a, RegistrationNode b,
        Dictionary<string, List<string>> aOutEdges,
        Dictionary<string, List<string>> bOutEdges)
    {
        var nameSim = NormalizedLevenshtein(a.DisplayName, b.DisplayName);
        var aDeps = aOutEdges.TryGetValue(a.Id, out var ad) ? ad : [];
        var bDeps = bOutEdges.TryGetValue(b.Id, out var bd) ? bd : [];
        var depJaccard = JaccardSimilarity(aDeps, bDeps);
        var lifetimeSim = a.Lifetime == b.Lifetime ? 1.0 : 0.0;
        return NameWeight * nameSim + DepJaccardWeight * depJaccard + LifetimeWeight * lifetimeSim;
    }

    private static double NormalizedLevenshtein(string a, string b)
    {
        if (a.Equals(b, StringComparison.Ordinal)) return 1.0;
        var maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 1.0;
        return 1.0 - (double)LevenshteinDistance(a, b) / maxLen;
    }

    private static double JaccardSimilarity(List<string> a, List<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 1.0;
        var setA = new HashSet<string>(a, StringComparer.Ordinal);
        var setB = new HashSet<string>(b, StringComparer.Ordinal);
        var intersection = setA.Count(x => setB.Contains(x));
        var union = setA.Count + setB.Count - intersection;
        return union == 0 ? 1.0 : (double)intersection / union;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                dp[i, j] = a[i - 1] == b[j - 1]
                    ? dp[i - 1, j - 1]
                    : 1 + Math.Min(dp[i - 1, j - 1], Math.Min(dp[i - 1, j], dp[i, j - 1]));
        return dp[a.Length, b.Length];
    }

    private static Dictionary<string, List<string>> BuildOutEdgeIndex(List<DependencyEdge> edges) =>
        edges
            .GroupBy(e => e.From, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.To).ToList(),
                StringComparer.Ordinal);
}
