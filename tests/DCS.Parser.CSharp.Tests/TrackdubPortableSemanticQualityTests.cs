using DCS.Parser.CSharp;
using DCS.Verification;
using Xunit;
using Xunit.Abstractions;

namespace DCS.Parser.CSharp.Tests;

/// <summary>
/// Aspirational semantic extraction quality on Trackdub portable TFM — separate from migration pin gates.
/// Not in ci/corpus-gates.json; run locally: dotnet test --filter CorpusId=csharp-migration-quality
/// </summary>
[Collection(CorpusGateCollection.CsharpMigration)]
[Trait(CorpusGateTraits.CategoryName, CorpusGateTraits.CategoryValue)]
[Trait(CorpusGateTraits.CorpusIdName, CorpusGateTraits.CsharpMigrationQuality)]
public sealed class TrackdubPortableSemanticQualityTests
{
    // Baseline @ pin 5fd8b481 portable context (~59.3%). Target: recover toward Phase 12 ~91% as parser improves.
    // Floor raised incrementally as P1 parser work lands (55% → 64% @ parser 0.3.3). Aspirational: 85%.
    private const double MinPortableSemanticRate = 0.64;
    private const double TargetPortableUnresolved = 20;
    private const double AspirationalPortableSemanticRate = 0.85;

    private readonly ITestOutputHelper _output;

    public TrackdubPortableSemanticQualityTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Trackdub_portable_semantic_quality_floor()
    {
        var path = TrackdubPin.ResolvePath();
        if (path == null)
            return;

        var parser = new CSharpStaticParser(new CSharpParseOptions
        {
            TargetFramework = "net10.0",
            IncludeTests = false
        });
        var result = parser.ParseCommit(path, TrackdubPin.CommitSha);
        var portable = result.ContextGraphs.FirstOrDefault(c => c.ContextId == "csharp|net10.0");
        Assert.NotNull(portable);

        var metrics = TrackdubSemanticMetrics.Compute(portable!.Graph.Nodes);
        _output.WriteLine($"portable @ {TrackdubPin.CommitSha[..8]}: {metrics}");
        _output.WriteLine(
            $"aspirational target: {AspirationalPortableSemanticRate:P0} " +
            $"(Phase 12 aggregate was ~91.6% on smaller mid-migration graph @ 3c4e374d)");

        Assert.True(metrics.SemanticTypeResolutionRate >= MinPortableSemanticRate,
            $"portable semantic_type_resolution_rate below floor: {metrics.SemanticTypeResolutionRate:P}");

        var actionableUnresolved = portable!.Graph.UnresolvedInjections.Count(u =>
            DCS.Analysis.FindingPolicy.IsActionableUnresolved(
                u.DeclaredType.ShortName,
                u.DeclaredType.FullyQualifiedName));
        _output.WriteLine($"actionable unresolved: {actionableUnresolved} (aspirational target < {TargetPortableUnresolved})");
    }
}
