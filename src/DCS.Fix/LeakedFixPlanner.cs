using DCS.Analysis;
using DCS.Core.IR;

namespace DCS.Fix;

public sealed record LeakedFixProposal(
    string DisplayName,
    string RelativeFilePath,
    int Line,
    string NodeId,
    string ShellTag,
    string GuardSymbol);

public static class LeakedFixPlanner
{
    public static IReadOnlyList<LeakedFixProposal> Plan(
        string repoRoot,
        RegistrationGraph graph,
        AnalysisResult analysis,
        string? displayNameFilter = null)
    {
        var nodeById = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var targetIds = CollectGuardTargetNodeIds(graph, analysis);
        var proposals = new Dictionary<string, LeakedFixProposal>(StringComparer.Ordinal);

        foreach (var nodeId in targetIds.OrderBy(id => id, StringComparer.Ordinal))
        {
            if (!nodeById.TryGetValue(nodeId, out var node))
                continue;

            if (displayNameFilter != null &&
                !string.Equals(node.DisplayName, displayNameFilter, StringComparison.Ordinal) &&
                !node.DisplayName.Contains(displayNameFilter, StringComparison.Ordinal))
            {
                continue;
            }

            if (!LeakedFixEligibility.TryCreateProposal(repoRoot, node, analysis, out var proposal))
                continue;

            proposals[nodeId] = proposal;
        }

        return proposals.Values
            .OrderBy(p => p.RelativeFilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Line)
            .ToList();
    }

    private static HashSet<string> CollectGuardTargetNodeIds(RegistrationGraph graph, AnalysisResult analysis)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var nodeById = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);

        foreach (var leaked in analysis.Leaked)
        {
            ids.Add(leaked.NodeId);

            if (!nodeById.TryGetValue(leaked.NodeId, out var node))
                continue;

            if (!string.IsNullOrEmpty(node.DuplicateGroupKey))
            {
                foreach (var peer in graph.Nodes.Where(n =>
                             string.Equals(n.DuplicateGroupKey, node.DuplicateGroupKey, StringComparison.Ordinal)))
                {
                    ids.Add(peer.Id);
                }
            }

            var abstractName = node.AbstractToken.ShortName;
            if (string.IsNullOrEmpty(abstractName))
                continue;

            foreach (var peer in graph.Nodes.Where(n =>
                         string.Equals(n.AbstractToken.ShortName, abstractName, StringComparison.Ordinal)))
            {
                ids.Add(peer.Id);
            }
        }

        return ids;
    }
}

internal static class LeakedFixEligibility
{
    internal static bool TryCreateProposal(
        string repoRoot,
        RegistrationNode node,
        AnalysisResult analysis,
        out LeakedFixProposal proposal)
    {
        proposal = null!;

        if (node.ParserConfidence is not (Confidence.Explicit or Confidence.Inferred))
            return false;

        if (node.SourceLocation?.FilePath == null || node.SourceLocation.Line is null or <= 0)
            return false;

        var line = node.SourceLocation.Line.Value;

        if (!string.IsNullOrEmpty(analysis.CompositionRootId) &&
            string.Equals(node.Id, analysis.CompositionRootId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryResolveShellTag(node.FrameworkTags, out var shellTag, out var guardSymbol))
            return false;

        if (!ProjectDefineConstantsResolver.DefinesConstant(repoRoot, node.SourceLocation.FilePath, guardSymbol))
            return false;

        proposal = new LeakedFixProposal(
            node.DisplayName,
            node.SourceLocation.FilePath,
            line,
            node.Id,
            shellTag,
            guardSymbol);

        return true;
    }

    private static bool TryResolveShellTag(
        IReadOnlyList<string> frameworkTags,
        out string shellTag,
        out string guardSymbol)
    {
        shellTag = string.Empty;
        guardSymbol = string.Empty;

        var matches = frameworkTags
            .Where(tag => ShellGuardSymbols.ContainsKey(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count != 1)
            return false;

        shellTag = matches[0];
        guardSymbol = ShellGuardSymbols[shellTag];
        return true;
    }

    private static readonly Dictionary<string, string> ShellGuardSymbols =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["winui"] = "WINUI",
            ["avalonia"] = "AVALONIA",
            ["wpf"] = "WPF",
            ["maui"] = "MAUI",
        };
}
