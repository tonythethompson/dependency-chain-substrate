using DCS.Core.IR;

namespace DCS.Parser.CSharp;

/// <summary>
/// Upgrades factory registration nodes from BlindSpot to Degraded when DI dependencies
/// are fully traced and resolved, eliminating false-positive broken chains.
/// </summary>
internal static class FactoryNodeConfidenceRefiner
{
    private const int MaxPasses = 12;

    public static List<RegistrationNode> Refine(
        List<RegistrationNode> nodes,
        List<DependencyEdge> edges,
        List<UnresolvedInjection> unresolved)
    {
        var current = nodes;
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var next = RefinePass(current, edges, unresolved);
            if (ReferenceEquals(next, current) || NodesEquivalent(next, current))
                return next;
            current = next;
        }

        return current;
    }

    private static bool NodesEquivalent(IReadOnlyList<RegistrationNode> a, IReadOnlyList<RegistrationNode> b)
    {
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].Id != b[i].Id || a[i].ParserConfidence != b[i].ParserConfidence)
                return false;
        }

        return true;
    }

    private static List<RegistrationNode> RefinePass(
        List<RegistrationNode> nodes,
        List<DependencyEdge> edges,
        List<UnresolvedInjection> unresolved)
    {
        var nodeById = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var factoryUnresolved = unresolved
            .Where(u => u.InjectionMechanism == Mechanism.FactoryParameter)
            .Where(u => string.Equals(u.Reason, "no_matching_registration", StringComparison.Ordinal))
            .GroupBy(u => u.FromRegistrationId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var factoryOutTargets = edges
            .Where(e => e.InjectionMechanism == Mechanism.FactoryParameter)
            .GroupBy(e => e.From, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.To).Distinct(StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);

        var changed = false;
        var refined = nodes.Select(node =>
        {
            if (node.ParserConfidence != Confidence.BlindSpot)
                return node;

            var pattern = node.Annotations.GetValueOrDefault("pattern");
            if (pattern is not ("factory_lambda_shallow" or "factory_lambda"))
                return node;

            if (factoryUnresolved.ContainsKey(node.Id))
                return node;

            if (!node.Annotations.ContainsKey("factory_lambda_service_keys"))
                return Upgrade(node, ref changed);

            if (!factoryOutTargets.TryGetValue(node.Id, out var targetIds) || targetIds.Count == 0)
                return node;

            var allTargetsTraced = targetIds.All(id =>
                nodeById.TryGetValue(id, out var target) &&
                target.ParserConfidence != Confidence.BlindSpot);

            return allTargetsTraced ? Upgrade(node, ref changed) : node;
        }).ToList();

        return changed ? refined : nodes;
    }

    private static RegistrationNode Upgrade(RegistrationNode node, ref bool changed)
    {
        changed = true;
        return node with { ParserConfidence = Confidence.Degraded };
    }
}
