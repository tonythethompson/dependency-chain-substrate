using DCS.Analysis;
using DCS.Core.IR;
using DCS.Parser.CSharp;
using DCS.Verification;
using Xunit;
using Xunit.Abstractions;

namespace DCS.Runtime.Tests;

[Collection(CorpusGateCollection.CsharpMigration)]
[Trait(CorpusGateTraits.CategoryName, CorpusGateTraits.CategoryValue)]
[Trait(CorpusGateTraits.CorpusIdName, CorpusGateTraits.CsharpMigration)]
public sealed class TrackdubRuntimeEnrichmentGateTests
{
    private const double MinAnnotatedNodeRate = 0.50;

    private readonly ITestOutputHelper _output;

    public TrackdubRuntimeEnrichmentGateTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Trackdub_runtime_enrichment_gate()
    {
        var runtimeLogPath = RuntimeFixturePath();
        Assert.True(File.Exists(runtimeLogPath), $"Missing runtime fixture: {runtimeLogPath}");

        var events = RuntimeLogReader.ReadJsonl(runtimeLogPath);
        Assert.NotEmpty(events);

        var trackdubPath = TrackdubPin.ResolvePath();
        if (trackdubPath == null)
            return;

        var graph = BuildAggregateGraph(trackdubPath);
        var analysis = new GraphAnalyzer(graph).Analyze();
        var report = RuntimeGraphEnricher.Enrich(graph, events, analysis);

        var rate = report.AnnotatedNodeCount / (double)graph.Nodes.Count;
        _output.WriteLine(
            $"Runtime enrichment @ {TrackdubPin.CommitSha[..8]}: " +
            $"{report.AnnotatedNodeCount}/{graph.Nodes.Count} annotated ({rate:P1}), " +
            $"{report.TotalResolutionEvents} events, " +
            $"{report.BlindSpotConfirmedNodeIds.Count} blind spots confirmed, " +
            $"{report.OrphanedReclassifiedNodeIds.Count} orphaned reclassified, " +
            $"{report.CaptiveDependencies.Count} captive findings");

        Assert.True(rate >= MinAnnotatedNodeRate,
            $"Expected >= {MinAnnotatedNodeRate:P0} nodes annotated; got {rate:P1}.");

        Assert.Contains(RuntimeGraphEnricher.ResolvedCountKey,
            report.EnrichedGraph.Nodes.SelectMany(n => n.Annotations.Keys));

        if (analysis.Orphaned.Count > 0)
        {
            var orphanIds = analysis.Orphaned
                .Select(o => o.NodeId)
                .ToHashSet(StringComparer.Ordinal);
            var orphansRuntimeConfirmed = report.EnrichedGraph.Nodes.Count(n =>
                orphanIds.Contains(n.Id) &&
                n.Annotations.GetValueOrDefault(RuntimeGraphEnricher.RuntimeConfirmedKey) == "true");

            _output.WriteLine(
                $"Orphan reclassification: {report.OrphanedReclassifiedNodeIds.Count} reclassified; " +
                $"{orphansRuntimeConfirmed}/{analysis.Orphaned.Count} static orphans runtime-confirmed");

            if (orphansRuntimeConfirmed > 0)
            {
                Assert.Equal(orphansRuntimeConfirmed, report.OrphanedReclassifiedNodeIds.Count);
            }
        }
        else
        {
            _output.WriteLine("Orphan reclassification sub-gate N/A: 0 orphaned registrations at pin.");
        }
    }

    private static RegistrationGraph BuildAggregateGraph(string trackdubPath)
    {
        var parser = new CSharpStaticParser(new CSharpParseOptions
        {
            AllTargetFrameworks = true,
            IncludeTests = false
        });
        var result = parser.ParseCommit(trackdubPath, TrackdubPin.CommitSha);

        return new RegistrationGraph
        {
            ParserVersion = CSharpStaticParser.ParserVersion,
            CommitSha = TrackdubPin.CommitSha,
            SourceLanguage = "csharp",
            Nodes = result.ContextGraphs.SelectMany(c => c.Graph.Nodes).ToList(),
            Edges = result.ContextGraphs.SelectMany(c => c.Graph.Edges).ToList(),
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["runtime_gate_contexts"] = string.Join(",", result.ContextGraphs.Select(c => c.ContextId))
            }
        };
    }

    private static string RuntimeFixturePath() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "tests", "fixtures", "corpus", "csharp-migration",
            $"runtime-{TrackdubPin.CommitSha[..8]}.jsonl"));

    [Fact]
    public void Trackdub_runtime_fixture_exists_for_pin()
    {
        var path = RuntimeFixturePath();
        Assert.True(File.Exists(path),
            $"Missing runtime fixture for pin {TrackdubPin.CommitSha[..8]}: {path}. " +
            "Regenerate via tools/TrackdubRuntimeProbe (see ci/README.md).");
    }
}
