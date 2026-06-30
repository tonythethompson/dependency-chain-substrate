using DCS.Analysis;
using DCS.Core.IR;

namespace DCS.Runtime;

public sealed record CaptiveDependencyFinding(
    string ScopedServiceType,
    string CaptiveSingletonType,
    int EventCount);

public sealed record RuntimeEnrichmentReport
{
    public required RegistrationGraph EnrichedGraph { get; init; }
    public IReadOnlyList<CaptiveDependencyFinding> CaptiveDependencies { get; init; } = [];
    public IReadOnlyList<string> RuntimeDiscoveredTypes { get; init; } = [];
    public IReadOnlyList<string> OrphanedReclassifiedNodeIds { get; init; } = [];
    public IReadOnlyList<string> BlindSpotConfirmedNodeIds { get; init; } = [];
    public int AnnotatedNodeCount { get; init; }
    public int TotalResolutionEvents { get; init; }
}

public static class RuntimeGraphEnricher
{
    public const string ResolvedCountKey = "runtime_resolved_count";
    public const string ResolvedTypeKey = "runtime_resolved_type";
    public const string RuntimeConfirmedKey = "runtime_confirmed";
    public const string UpgradedFromKey = "runtime_upgraded_from";

    public static RuntimeEnrichmentReport Enrich(
        RegistrationGraph staticGraph,
        IReadOnlyList<RuntimeResolutionEvent> events,
        AnalysisResult? staticAnalysis = null)
    {
        var eventsByRequested = events
            .GroupBy(e => NormalizeTypeName(e.RequestedType), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var nodeMatches = staticGraph.Nodes.ToDictionary(
            n => n.Id,
            n => MatchEvents(n, eventsByRequested),
            StringComparer.Ordinal);

        var enrichedNodes = staticGraph.Nodes.Select(node =>
        {
            var matched = nodeMatches[node.Id];
            if (matched.Count == 0)
                return node;

            var annotations = new Dictionary<string, string>(node.Annotations, StringComparer.Ordinal)
            {
                [ResolvedCountKey] = matched.Count.ToString(),
                [RuntimeConfirmedKey] = "true"
            };

            var resolvedType = matched
                .Select(e => e.ResolvedType)
                .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            if (!string.IsNullOrWhiteSpace(resolvedType))
                annotations[ResolvedTypeKey] = resolvedType!;

            if (node.ParserConfidence == Confidence.BlindSpot)
                annotations[UpgradedFromKey] = "blind_spot";

            RegistrationNode enriched = node with { Annotations = annotations };

            if (node.ParserConfidence == Confidence.BlindSpot &&
                !string.IsNullOrWhiteSpace(resolvedType))
            {
                enriched = enriched with
                {
                    ParserConfidence = Confidence.Inferred,
                    ConcreteImpl = enriched.ConcreteImpl ?? TypeRef.FromShortName(ShortName(resolvedType!))
                };
            }

            return enriched;
        }).ToList();

        var orphanedIds = staticAnalysis?.Orphaned
            .Select(o => o.NodeId)
            .ToHashSet(StringComparer.Ordinal) ?? [];

        var orphanedReclassified = enrichedNodes
            .Where(n => orphanedIds.Contains(n.Id) &&
                        n.Annotations.TryGetValue(RuntimeConfirmedKey, out var confirmed) &&
                        confirmed == "true")
            .Select(n => n.Id)
            .ToList();

        var blindSpotConfirmed = enrichedNodes
            .Where(n => n.Annotations.TryGetValue(UpgradedFromKey, out var from) && from == "blind_spot")
            .Select(n => n.Id)
            .ToList();

        var knownTypes = new HashSet<string>(
            staticGraph.Nodes.SelectMany(TypeNames),
            StringComparer.OrdinalIgnoreCase);

        var runtimeDiscovered = events
            .SelectMany(e => new[] { e.RequestedType, e.ResolvedType, e.CallerType })
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!)
            .Where(t => !knownTypes.Contains(NormalizeTypeName(t)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var enrichedGraph = staticGraph with
        {
            Nodes = enrichedNodes,
            Metadata = new Dictionary<string, string>(staticGraph.Metadata, StringComparer.Ordinal)
            {
                ["runtime_enriched"] = "true",
                ["runtime_event_count"] = events.Count.ToString(),
                ["runtime_annotated_nodes"] = enrichedNodes.Count(n => n.Annotations.ContainsKey(ResolvedCountKey)).ToString()
            }
        };

        return new RuntimeEnrichmentReport
        {
            EnrichedGraph = enrichedGraph,
            CaptiveDependencies = DetectCaptiveDependencies(events),
            RuntimeDiscoveredTypes = runtimeDiscovered,
            OrphanedReclassifiedNodeIds = orphanedReclassified,
            BlindSpotConfirmedNodeIds = blindSpotConfirmed,
            AnnotatedNodeCount = enrichedNodes.Count(n => n.Annotations.ContainsKey(ResolvedCountKey)),
            TotalResolutionEvents = events.Count
        };
    }

    private static List<RuntimeResolutionEvent> MatchEvents(
        RegistrationNode node,
        IReadOnlyDictionary<string, List<RuntimeResolutionEvent>> eventsByRequested)
    {
        var matches = new List<RuntimeResolutionEvent>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in TypeNames(node))
        {
            if (!seenNames.Add(name))
                continue;

            if (eventsByRequested.TryGetValue(name, out var list))
                matches.AddRange(list);
        }

        return matches;
    }

    private static IEnumerable<string> TypeNames(RegistrationNode node)
    {
        yield return NormalizeTypeName(node.DisplayName);
        yield return NormalizeTypeName(node.AbstractToken.ShortName);
        if (!string.IsNullOrWhiteSpace(node.AbstractToken.FullyQualifiedName))
            yield return NormalizeTypeName(node.AbstractToken.FullyQualifiedName);
        if (node.ConcreteImpl != null)
        {
            yield return NormalizeTypeName(node.ConcreteImpl.ShortName);
            if (!string.IsNullOrWhiteSpace(node.ConcreteImpl.FullyQualifiedName))
                yield return NormalizeTypeName(node.ConcreteImpl.FullyQualifiedName);
        }
    }

    private static string NormalizeTypeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var name = value.Trim();
        if (name.StartsWith("global::", StringComparison.Ordinal))
            name = name["global::".Length..];

        var genericIdx = name.IndexOf('<', StringComparison.Ordinal);
        if (genericIdx >= 0)
            name = name[..genericIdx];

        var lastDot = name.LastIndexOf('.');
        return lastDot >= 0 ? name[(lastDot + 1)..] : name;
    }

    private static string ShortName(string fullyQualifiedOrShort) => NormalizeTypeName(fullyQualifiedOrShort);

    private static List<CaptiveDependencyFinding> DetectCaptiveDependencies(
        IReadOnlyList<RuntimeResolutionEvent> events)
    {
        var findings = new Dictionary<string, CaptiveDependencyFinding>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in events
                     .Where(e => IsScoped(e.Lifetime) && IsSingleton(e.CallerLifetime))
                     .GroupBy(e => $"{e.RequestedType}|{e.CallerType}", StringComparer.OrdinalIgnoreCase))
        {
            var sample = group.First();
            var key = $"{sample.RequestedType}|{sample.CallerType}";
            findings[key] = new CaptiveDependencyFinding(
                sample.RequestedType,
                sample.CallerType ?? "(unknown)",
                group.Count());
        }

        return findings.Values
            .OrderBy(f => f.ScopedServiceType, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsScoped(string? lifetime) =>
        string.Equals(lifetime, Lifetime.Scoped.ToString(), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(lifetime, "scoped", StringComparison.OrdinalIgnoreCase);

    private static bool IsSingleton(string? lifetime) =>
        string.Equals(lifetime, Lifetime.Singleton.ToString(), StringComparison.OrdinalIgnoreCase) ||
        string.Equals(lifetime, "singleton", StringComparison.OrdinalIgnoreCase);
}
