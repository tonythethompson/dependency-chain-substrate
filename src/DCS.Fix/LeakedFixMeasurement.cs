using DCS.Analysis;
using DCS.Core.IR;
using System.Text;

namespace DCS.Fix;

public sealed record LeakedFixMeasurementReport(
    int TotalLeaked,
    int EligibleForFixPreview,
    IReadOnlyList<LeakedRegistration> IneligibleLeaked)
{
    public string FormatSummary()
    {
        var builder = new StringBuilder();
        builder.AppendLine("=== Leaked Fix Measurement ===");
        builder.AppendLine($"Total leaked findings:       {TotalLeaked}");
        builder.AppendLine($"Eligible for preview fix:    {EligibleForFixPreview}");
        builder.AppendLine();
        builder.AppendLine(
            "Eligible: explicit/inferred registration with file:line, single shell tag, " +
            "and matching DefineConstants in project.");
        return builder.ToString();
    }
}

public static class LeakedFixMeasurement
{
    public static LeakedFixMeasurementReport Measure(
        string repoRoot,
        RegistrationGraph graph,
        AnalysisResult analysis)
    {
        var proposals = LeakedFixPlanner.Plan(repoRoot, graph, analysis);
        var eligibleNodeIds = proposals.Select(p => p.NodeId).ToHashSet(StringComparer.Ordinal);

        var ineligible = analysis.Leaked
            .Where(l => !eligibleNodeIds.Contains(l.NodeId))
            .ToList();

        return new LeakedFixMeasurementReport(
            analysis.Leaked.Count,
            proposals.Count,
            ineligible);
    }
}

public sealed record LeakedFixResult(
    IReadOnlyList<LeakedFixProposal> Proposals,
    IReadOnlyList<FilePatch> Patches,
    bool Applied = false);
