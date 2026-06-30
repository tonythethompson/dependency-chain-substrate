using DCS.Analysis;
using DCS.Core.IR;

namespace DCS.Fix;

public sealed record OrphanedFixProposal(
    string DisplayName,
    string RelativeFilePath,
    int Line,
    string NodeId);

public static class OrphanedFixPlanner
{
    public static IReadOnlyList<OrphanedFixProposal> Plan(
        RegistrationGraph graph,
        AnalysisResult analysis,
        string? displayNameFilter = null)
    {
        var proposals = new List<OrphanedFixProposal>();

        foreach (var orphaned in analysis.Orphaned.OrderBy(o => o.DisplayName, StringComparer.Ordinal))
        {
            if (displayNameFilter != null &&
                !string.Equals(orphaned.DisplayName, displayNameFilter, StringComparison.Ordinal) &&
                !orphaned.DisplayName.Contains(displayNameFilter, StringComparison.Ordinal))
            {
                continue;
            }

            var node = graph.Nodes.FirstOrDefault(n => n.Id == orphaned.NodeId);
            if (node == null || !OrphanedFixEligibility.IsEligible(node, analysis))
                continue;

            proposals.Add(new OrphanedFixProposal(
                orphaned.DisplayName,
                node.SourceLocation!.FilePath!,
                node.SourceLocation.Line!.Value,
                node.Id));
        }

        return proposals;
    }
}

internal static class OrphanedFixEligibility
{
    internal static bool IsEligible(RegistrationNode node, AnalysisResult analysis)
    {
        if (node.ParserConfidence != Confidence.Explicit)
            return false;

        if (node.SourceLocation?.FilePath == null || node.SourceLocation.Line is not > 0)
            return false;

        if (!string.IsNullOrEmpty(analysis.CompositionRootId) &&
            string.Equals(node.Id, analysis.CompositionRootId, StringComparison.Ordinal))
        {
            return false;
        }

        return !IsFrameworkInfrastructure(node);
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
}
