using System.Text.Json;
using DCS.Analysis;
using DCS.Parser.CSharp;
using DCS.Verification;
using Xunit;

namespace DCS.Parser.CSharp.Tests;

/// <summary>
/// Locks Trackdub unresolved/orphan summary totals @ pin b57fc832 (portable net10.0).
/// Expected values: tests/fixtures/corpus/csharp-migration/unresolved-inventory-{pin}.json (summary section).
/// </summary>
/// Optional quality gate — not in ci/corpus-gates.json (local Trackdub vs GitHub pin can diverge on summary buckets).
[Collection(CorpusGateCollection.CsharpMigration)]
[Trait(CorpusGateTraits.CategoryName, CorpusGateTraits.CategoryValue)]
[Trait(CorpusGateTraits.CorpusIdName, CorpusGateTraits.CsharpMigrationQuality)]
public sealed class TrackdubUnresolvedInventoryTests
{
    private const double SummaryTolerance = 0.05;

    private static string InventoryFixturePath() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "tests", "fixtures", "corpus", "csharp-migration",
            $"unresolved-inventory-{TrackdubPin.CommitSha[..8]}.json"));

    private static AnalysisReport LoadFixture()
    {
        var json = File.ReadAllText(InventoryFixturePath());
        return JsonSerializer.Deserialize<AnalysisReport>(json, AnalysisReportSerializer.Options)!;
    }

    [Fact]
    public void Unresolved_inventory_fixture_exists()
    {
        Assert.True(File.Exists(InventoryFixturePath()),
            $"Missing inventory fixture: {InventoryFixturePath()}");
    }

    [Fact]
    public void Trackdub_unresolved_inventory_gate_matches_baseline()
    {
        var path = TrackdubPin.ResolvePath();
        if (path == null)
            return;

        var expected = LoadFixture();

        var parser = new CSharpStaticParser(new CSharpParseOptions
        {
            TargetFramework = "net10.0",
            IncludeTests = false,
            NoCache = true
        });
        var result = parser.ParseCommit(path, TrackdubPin.CommitSha);
        var portable = result.ContextGraphs.FirstOrDefault(c => c.ContextId == "csharp|net10.0");
        Assert.NotNull(portable);

        var analyzer = new GraphAnalyzer(portable!.Graph, islandAware: true);
        var analysis = analyzer.Analyze();
        var report = AnalysisReportBuilder.Build(portable.Graph, analysis, new AnalysisReportBuildOptions
        {
            Verbosity = ReportVerbosity.Full,
            IncludeMetrics = true,
            IslandAware = true
        });

        Assert.Equal(expected.TotalNodes, report.TotalNodes);
        AssertWithinTolerance(expected.Summary.UnresolvedCount, report.Summary.UnresolvedCount, "unresolved_count");
        AssertWithinTolerance(expected.Summary.OrphanedCount, report.Summary.OrphanedCount, "orphaned_count");
        AssertWithinTolerance(expected.Summary.BrokenCount, report.Summary.BrokenCount, "broken_count");
        AssertWithinTolerance(expected.Summary.DuplicateCount, report.Summary.DuplicateCount, "duplicate_count");
    }

    [Fact]
    public void Inventory_fixture_matches_pin()
    {
        var report = LoadFixture();
        Assert.NotNull(report.CommitSha);
        Assert.StartsWith(TrackdubPin.CommitSha[..8], report.CommitSha, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("csharp|net10.0", report.ContextId);
    }

    private static void AssertWithinTolerance(int expected, int actual, string label)
    {
        var min = (int)Math.Floor(expected * (1 - SummaryTolerance));
        var max = (int)Math.Ceiling(expected * (1 + SummaryTolerance));
        Assert.True(actual >= min && actual <= max,
            $"{label}: expected {expected} ±{SummaryTolerance:P0} ({min}-{max}), actual {actual}");
    }
}
