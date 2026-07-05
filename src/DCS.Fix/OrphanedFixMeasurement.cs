using DCS.Analysis;
using DCS.Core.IR;
using System.Text;

namespace DCS.Fix;

public sealed record OrphanedFixMeasurementReport(
    int TotalOrphaned,
    int ExplicitWithSite,
    int EligibleForFixPreview,
    IReadOnlyList<OrphanedRegistration> EligibleOrphans,
    IReadOnlyList<OrphanedRegistration> IneligibleOrphans)
{
    public string FormatSummary()
    {
        var builder = new StringBuilder();
        builder.AppendLine("=== Orphaned Fix Measurement ===");
        builder.AppendLine($"Total orphaned:              {TotalOrphaned}");
        builder.AppendLine($"Explicit with file:line:     {ExplicitWithSite}");
        builder.AppendLine($"Eligible for preview fix:    {EligibleForFixPreview}");
        if (TotalOrphaned > EligibleForFixPreview)
        {
            builder.AppendLine(
                "Note: ineligible orphans include composition root, infrastructure, and non-explicit sites.");
        }
        builder.AppendLine();
        builder.AppendLine("Eligible (explicit, non-seed, non-infrastructure):");
        foreach (var orphan in EligibleOrphans)
        {
            var site = orphan.SourceFile != null ? $"{orphan.SourceFile}:{orphan.SourceLine}" : "(unknown)";
            builder.AppendLine($"  - {orphan.DisplayName} @ {site}");
        }

        if (IneligibleOrphans.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine($"Ineligible ({IneligibleOrphans.Count}): inferred/degraded, seeds, or infrastructure");
        }

        return builder.ToString();
    }
}

public static class OrphanedFixMeasurement
{
    public static OrphanedFixMeasurementReport Measure(
        RegistrationGraph graph,
        AnalysisResult analysis)
    {
        var eligible = new List<OrphanedRegistration>();
        var ineligible = new List<OrphanedRegistration>();
        var explicitWithSite = 0;

        foreach (var orphaned in analysis.Orphaned)
        {
            var node = graph.Nodes.FirstOrDefault(n => n.Id == orphaned.NodeId);
            if (node?.SourceLocation?.FilePath != null && node.SourceLocation.Line is > 0 &&
                node.ParserConfidence == Confidence.Explicit)
            {
                explicitWithSite++;
            }

            if (node != null && OrphanedFixEligibility.IsEligible(node, analysis))
                eligible.Add(orphaned);
            else
                ineligible.Add(orphaned);
        }

        return new OrphanedFixMeasurementReport(
            analysis.Orphaned.Count,
            explicitWithSite,
            eligible.Count,
            eligible,
            ineligible);
    }
}

public sealed record OrphanedFixResult(
    IReadOnlyList<OrphanedFixProposal> Proposals,
    IReadOnlyList<FilePatch> Patches,
    bool Applied = false);
