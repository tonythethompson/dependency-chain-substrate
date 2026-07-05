using System.Text.Json;
using DCS.Analysis;
using DCS.Parser.CSharp;
using DCS.Verification;
using Xunit;
using Xunit.Abstractions;

namespace DCS.Parser.CSharp.Tests;

/// <summary>
/// Locks Trackdub unresolved/orphan bucket totals @ pin 5fd8b481 (portable net10.0).
/// </summary>
[Collection(CorpusGateCollection.CsharpMigration)]
[Trait(CorpusGateTraits.CategoryName, CorpusGateTraits.CategoryValue)]
[Trait(CorpusGateTraits.CorpusIdName, CorpusGateTraits.CsharpMigration)]
public sealed class TrackdubUnresolvedInventoryTests
{
    private const string InventoryCommit = "5fd8b4814c9142f3980999c178b49adae9e725a6";

    private const int BaselineUnresolved = 91;
    private const int BaselineTrueOrphans = 0;
    private const int BaselineIslandValidOrphans = 10;
    private const int BaselineNodes = 339;
    private const int BaselineDesktopUnresolved = 90;
    private const int BaselineApiUnresolved = 1;
    private const int BaselineLambdaUnresolved = 0;
    private const double BucketTolerance = 0.05;

    private readonly ITestOutputHelper _output;

    public TrackdubUnresolvedInventoryTests(ITestOutputHelper output) => _output = output;

    private static string InventoryFixturePath() =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "tests", "fixtures", "corpus", "csharp-migration",
            "unresolved-inventory-5fd8b481.json"));

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

        var parser = new CSharpStaticParser(new CSharpParseOptions
        {
            TargetFramework = "net10.0",
            IncludeTests = false,
            NoCache = true
        });
        var result = parser.ParseCommit(path, InventoryCommit);
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

        Assert.Equal(BaselineNodes, report.TotalNodes);
        AssertWithinTolerance(BaselineUnresolved, report.Summary.UnresolvedCount, "unresolved_count");
        AssertWithinTolerance(BaselineTrueOrphans, report.Summary.OrphanedCount, "true_orphan_count");

        var islandValid = report.Findings.Count(f =>
            f.Category == FindingCategory.Orphaned && f.Tier == FindingTier.IslandValid);
        AssertWithinTolerance(BaselineIslandValidOrphans, islandValid, "island_valid_orphans");

        var buckets = BucketUnresolvedByIsland(report);
        _output.WriteLine($"island buckets: desktop={buckets.Desktop} api={buckets.Api} lambda={buckets.Lambda} unknown={buckets.Unknown}");

        AssertWithinTolerance(BaselineDesktopUnresolved, buckets.Desktop, "desktop unresolved");
        AssertWithinTolerance(BaselineApiUnresolved, buckets.Api, "api unresolved");
        AssertWithinTolerance(BaselineLambdaUnresolved, buckets.Lambda, "lambda unresolved");
    }

    [Fact]
    public void Inventory_fixture_summary_matches_expected_buckets()
    {
        var json = File.ReadAllText(InventoryFixturePath());
        var report = JsonSerializer.Deserialize<AnalysisReport>(json, AnalysisReportSerializer.Options)!;

        Assert.NotNull(report.CommitSha);
        Assert.StartsWith("5fd8b481", report.CommitSha, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(BaselineUnresolved, report.Summary.UnresolvedCount);
        Assert.Equal(BaselineTrueOrphans, report.Summary.OrphanedCount);

        var unresolved = report.Findings.Where(f => f.Category == FindingCategory.Unresolved).ToList();
        var buckets = CountByIsland(unresolved);
        _output.WriteLine($"fixture buckets: desktop={buckets.Desktop} api={buckets.Api} lambda={buckets.Lambda}");

        AssertWithinTolerance(BaselineDesktopUnresolved, buckets.Desktop, "fixture desktop");
        AssertWithinTolerance(BaselineApiUnresolved, buckets.Api, "fixture api");
        AssertWithinTolerance(BaselineLambdaUnresolved, buckets.Lambda, "fixture lambda");
    }

    private static IslandBucketCounts BucketUnresolvedByIsland(AnalysisReport report)
    {
        var unresolved = report.Findings.Where(f => f.Category == FindingCategory.Unresolved).ToList();
        return CountByIsland(unresolved);
    }

    private static IslandBucketCounts CountByIsland(IReadOnlyList<AnalysisFinding> findings)
    {
        var counts = new IslandBucketCounts();
        foreach (var finding in findings)
        {
            var sitePath = finding.Sites.FirstOrDefault()?.FilePath;
            var island = CompositionIslandAttribution.InferFromFilePath(sitePath);
            switch (island)
            {
                case CompositionIsland.Desktop:
                    counts.Desktop++;
                    break;
                case CompositionIsland.Api:
                    counts.Api++;
                    break;
                case CompositionIsland.Lambda:
                    counts.Lambda++;
                    break;
                default:
                    counts.Unknown++;
                    break;
            }
        }

        return counts;
    }

    private static void AssertWithinTolerance(int expected, int actual, string label)
    {
        var min = (int)Math.Floor(expected * (1 - BucketTolerance));
        var max = (int)Math.Ceiling(expected * (1 + BucketTolerance));
        Assert.InRange(actual, min, max);
    }

    private sealed class IslandBucketCounts
    {
        public int Desktop { get; set; }
        public int Api { get; set; }
        public int Lambda { get; set; }
        public int Unknown { get; set; }
    }
}
