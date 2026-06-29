using DCS.Analysis;
using DCS.Core.IR;

namespace DCS.Fix;

public sealed record DuplicateFixProposal(
    string AbstractTokenName,
    RegistrationNode Remove,
    RegistrationNode Keep,
    string RelativeFilePath,
    int Line);

public static class DuplicateFixPlanner
{
    public static IReadOnlyList<DuplicateFixProposal> Plan(
        RegistrationGraph graph,
        AnalysisResult analysis,
        string? tokenFilter = null)
    {
        var proposals = new List<DuplicateFixProposal>();
        var duplicates = analysis.Duplicates
            .Where(d => tokenFilter == null ||
                        string.Equals(d.AbstractTokenName, tokenFilter, StringComparison.Ordinal))
            .OrderBy(d => d.AbstractTokenName, StringComparer.Ordinal);

        foreach (var duplicate in duplicates)
        {
            var idSet = duplicate.NodeIds.ToHashSet(StringComparer.Ordinal);
            var instances = graph.Nodes
                .Where(n => idSet.Contains(n.Id))
                .Where(n => n.SourceLocation?.FilePath != null && n.SourceLocation.Line > 0)
                .ToList();

            if (instances.Count < 2)
                continue;

            var remove = SelectRemovalTarget(instances);
            var keep = instances.First(n => n.Id != remove.Id);
            var tokenForRemoval = remove.AbstractToken.ShortName;
            proposals.Add(new DuplicateFixProposal(
                tokenForRemoval,
                remove,
                keep,
                remove.SourceLocation!.FilePath!,
                remove.SourceLocation.Line!.Value));
        }

        return proposals;
    }

    public static RegistrationNode SelectRemovalTarget(IReadOnlyList<RegistrationNode> instances)
    {
        return instances
            .OrderByDescending(n => ConfidenceRank(n.ParserConfidence))
            .ThenBy(n => n.SourceLocation?.FilePath?.Length ?? int.MaxValue)
            .ThenByDescending(n => n.SourceLocation?.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static int ConfidenceRank(Confidence confidence) => confidence switch
    {
        Confidence.Explicit => 0,
        Confidence.Inferred => 1,
        Confidence.Degraded => 2,
        Confidence.BlindSpot => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(confidence), confidence, null)
    };
}
