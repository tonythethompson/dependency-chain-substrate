using DCS.Analysis;
using DCS.Core.IR;
using System.Text;

namespace DCS.Fix;

public sealed record BrokenFixMeasurementReport(
    int TotalBroken,
    int EligibleForFixPreview,
    IReadOnlyList<BrokenChain> IneligibleBroken)
{
    public string FormatSummary()
    {
        var builder = new StringBuilder();
        builder.AppendLine("=== Broken Fix Measurement ===");
        builder.AppendLine($"Total broken chains:         {TotalBroken}");
        builder.AppendLine($"Eligible for preview fix:    {EligibleForFixPreview}");
        builder.AppendLine();
        builder.AppendLine("Eligible: simple factory_lambda_shallow with resolved concrete_impl (no service-locator deps).");

        if (IneligibleBroken.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"Ineligible ({IneligibleBroken.Count}): complex factory or missing concrete_impl");
            foreach (var broken in IneligibleBroken.Take(10))
            {
                builder.AppendLine($"  - {broken.DisplayName} → {broken.MissingDependencyType}");
            }
        }

        return builder.ToString();
    }
}

public static class BrokenFixMeasurement
{
    public static BrokenFixMeasurementReport Measure(
        string repoRoot,
        RegistrationGraph graph,
        AnalysisResult analysis)
    {
        var eligibleKeys = BrokenFixPlanner.Plan(repoRoot, graph, analysis)
            .Select(p => p.TargetNodeId)
            .ToHashSet(StringComparer.Ordinal);

        var ineligible = new List<BrokenChain>();
        var seen = new HashSet<(string NodeId, string Missing)>();

        foreach (var broken in analysis.BrokenChains)
        {
            var key = (broken.NodeId, broken.MissingDependencyType);
            if (!seen.Add(key))
                continue;

            var target = graph.Edges
                .Where(e => string.Equals(e.From, broken.NodeId, StringComparison.Ordinal))
                .Select(e => graph.Nodes.FirstOrDefault(n => n.Id == e.To))
                .FirstOrDefault(n =>
                    n?.ParserConfidence == Confidence.BlindSpot &&
                    string.Equals(n.AbstractToken.ShortName, broken.MissingDependencyType, StringComparison.Ordinal));

            if (target == null || !eligibleKeys.Contains(target.Id))
                ineligible.Add(broken);
        }

        return new BrokenFixMeasurementReport(
            analysis.BrokenChains.Count,
            eligibleKeys.Count,
            ineligible);
    }
}

public sealed record BrokenFixResult(
    IReadOnlyList<BrokenFixProposal> Proposals,
    IReadOnlyList<FilePatch> Patches,
    bool Applied = false);
