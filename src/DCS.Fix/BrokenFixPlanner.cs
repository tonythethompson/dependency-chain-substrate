using DCS.Analysis;
using DCS.Core.IR;

namespace DCS.Fix;

public sealed record BrokenFixProposal(
    string ConsumerDisplayName,
    string TargetDisplayName,
    string RelativeFilePath,
    int Line,
    string TargetNodeId,
    string ReplacementStatement);

public static class BrokenFixPlanner
{
    public static IReadOnlyList<BrokenFixProposal> Plan(
        string repoRoot,
        RegistrationGraph graph,
        AnalysisResult analysis,
        string? targetFilter = null)
    {
        var nodeById = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var proposals = new Dictionary<string, BrokenFixProposal>(StringComparer.Ordinal);

        foreach (var broken in analysis.BrokenChains.OrderBy(b => b.DisplayName, StringComparer.Ordinal))
        {
            if (targetFilter != null &&
                !string.Equals(broken.MissingDependencyType, targetFilter, StringComparison.Ordinal) &&
                !broken.MissingDependencyType.Contains(targetFilter, StringComparison.Ordinal))
            {
                continue;
            }

            var target = graph.Edges
                .Where(e => string.Equals(e.From, broken.NodeId, StringComparison.Ordinal))
                .Select(e => nodeById.GetValueOrDefault(e.To))
                .FirstOrDefault(n =>
                    n?.ParserConfidence == Confidence.BlindSpot &&
                    string.Equals(n.AbstractToken.ShortName, broken.MissingDependencyType, StringComparison.Ordinal));

            if (target == null || proposals.ContainsKey(target.Id))
                continue;

            if (!BrokenFixEligibility.IsEligible(repoRoot, target, out var replacement))
                continue;

            proposals[target.Id] = new BrokenFixProposal(
                broken.DisplayName,
                target.DisplayName,
                target.SourceLocation!.FilePath!,
                target.SourceLocation.Line!.Value,
                target.Id,
                replacement);
        }

        return proposals.Values.OrderBy(p => p.TargetDisplayName, StringComparer.Ordinal).ToList();
    }
}

internal static class BrokenFixEligibility
{
    internal static bool IsEligible(
        string repoRoot,
        RegistrationNode target,
        out string replacementStatement)
    {
        replacementStatement = string.Empty;

        if (target.ParserConfidence != Confidence.BlindSpot)
            return false;

        if (!string.Equals(
                target.Annotations.GetValueOrDefault("pattern"),
                "factory_lambda_shallow",
                StringComparison.Ordinal))
        {
            return false;
        }

        if (target.ConcreteImpl == null || string.IsNullOrWhiteSpace(target.ConcreteImpl.ShortName))
            return false;

        if (target.SourceLocation?.FilePath == null || target.SourceLocation.Line is not > 0)
            return false;

        var absolutePath = Path.IsPathRooted(target.SourceLocation.FilePath)
            ? target.SourceLocation.FilePath
            : Path.Combine(repoRoot, target.SourceLocation.FilePath);

        if (!File.Exists(absolutePath))
            return false;

        var source = File.ReadAllText(absolutePath);
        return FactoryLambdaToExplicitConverter.TryBuildReplacement(
            source,
            target.SourceLocation.Line.Value,
            target,
            out replacementStatement);
    }
}
