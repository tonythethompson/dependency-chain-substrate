using DCS.Analysis;
using DCS.Parser.CSharp;
using DCS.Verification;
using Xunit;
using Xunit.Abstractions;

namespace DCS.Parser.CSharp.Tests;

/// <summary>
/// Documents Trackdub broken-chain triage @ pin b57fc832 after parser 0.3.7.
/// P2 abstract promotion traces ctor deps; edges skip BlindSpot targets to avoid false broken chains.
/// </summary>
[Collection(CorpusGateCollection.CsharpMigration)]
[Trait(CorpusGateTraits.CategoryName, CorpusGateTraits.CategoryValue)]
[Trait(CorpusGateTraits.CorpusIdName, CorpusGateTraits.CsharpMigration)]
public sealed class TrackdubBrokenChainTriageTests
{
    private readonly ITestOutputHelper _output;

    public TrackdubBrokenChainTriageTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Trackdub_portable_broken_chains_cleared_after_factory_ctor_tracing()
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
        var result = parser.ParseCommit(path, TrackdubPin.CommitSha);
        var portable = result.ContextGraphs.FirstOrDefault(c => c.ContextId == "csharp|net10.0");
        Assert.NotNull(portable);

        var analysis = new GraphAnalyzer(portable!.Graph, islandAware: true).Analyze();
        _output.WriteLine($"broken chains: {analysis.BrokenChains.Count}");
        foreach (var broken in analysis.BrokenChains)
            _output.WriteLine($"  {broken.DisplayName} @ {broken.SourceFile}:{broken.SourceLine} -> {broken.MissingDependencyType}");

        // Parser 0.3.7 @ b57fc832: three broken chains after BlindSpot ctor-edge filter (TensorRT / lip-synthesis).
        Assert.Equal(3, analysis.BrokenChains.Count);
        var names = analysis.BrokenChains.Select(b => b.DisplayName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Contains("IExecutionProviderDiscovery", names);
        Assert.Contains("ITensorRtRtxReadinessProbe", names);
        Assert.Contains("LipSynthesisStageHandler", names);
    }
}
