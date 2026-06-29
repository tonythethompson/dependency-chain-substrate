using DCS.Core.IR;

namespace DCS.Cli;

internal static class AtlasReporter
{
    public static void Print(RegistrationGraph graph, TextWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine("=== DCS Registration Atlas ===");

        var commitLabel = graph.CommitSha ?? "working directory";
        writer.WriteLine(
            $"Commit: {commitLabel}  |  {graph.Nodes.Count} registrations  |  " +
            $"{graph.Edges.Count} edges  |  {graph.BlindSpots.Count} blind spots");
        writer.WriteLine();

        writer.WriteLine("--- BY FRAMEWORK ---");
        var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var untagged = 0;

        foreach (var node in graph.Nodes)
        {
            var boundaryTags = node.FrameworkTags
                .Where(t => !string.Equals(t, "msdi", StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(t, "ms-extensions", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (boundaryTags.Count == 0)
            {
                untagged++;
                continue;
            }

            foreach (var tag in boundaryTags)
                tagCounts[tag] = tagCounts.GetValueOrDefault(tag) + 1;
        }

        if (tagCounts.Count == 0)
            writer.WriteLine("  (no framework tags)");
        else
        {
            foreach (var (tag, count) in tagCounts.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
                writer.WriteLine($"  {tag}: {count}");
        }

        writer.WriteLine($"  untagged: {untagged}");
        writer.WriteLine();

        writer.WriteLine("--- REGISTRATIONS (sorted by file, line) ---");
        foreach (var node in graph.Nodes.OrderBy(n => n.SourceLocation?.FilePath ?? string.Empty, StringComparer.Ordinal)
                     .ThenBy(n => n.SourceLocation?.Line ?? int.MaxValue)
                     .ThenBy(n => n.DisplayName, StringComparer.Ordinal))
        {
            var abstractName = node.AbstractToken.ShortName;
            var concreteName = node.ConcreteImpl?.ShortName ?? "-";
            var lifetime = node.Lifetime.ToString().ToLowerInvariant();
            var confidence = node.ParserConfidence.ToString().ToLowerInvariant();
            var tags = node.FrameworkTags.Count > 0
                ? $"[{string.Join(", ", node.FrameworkTags)}]"
                : "[]";
            var location = FormatLoc(node.SourceLocation?.FilePath, node.SourceLocation?.Line);

            writer.WriteLine(
                $"  {abstractName} → {concreteName}  {lifetime}  {confidence}  {tags}  {location}");
        }

        writer.WriteLine();
    }

    private static string FormatLoc(string? file, int? line) =>
        file == null ? string.Empty :
        line == null ? $"[{file}]" :
        $"[{file}:{line}]";
}
