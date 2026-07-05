using DCS.Analysis;
using DCS.Parser.CSharp;
using DCS.Verification;
using Xunit;
using Xunit.Abstractions;

namespace DCS.Parser.CSharp.Tests;

/// <summary>
/// Documents Trackdub broken-chain triage @ pin 5fd8b481 after parser 0.3.5.
/// All 10 pre-triage broken chains were parser-limit factory opacity, not wiring defects.
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
        var result = parser.ParseCommit(path, "5fd8b4814c9142f3980999c178b49adae9e725a6");
        var portable = result.ContextGraphs.FirstOrDefault(c => c.ContextId == "csharp|net10.0");
        Assert.NotNull(portable);

        var analysis = new GraphAnalyzer(portable!.Graph, islandAware: true).Analyze();
        _output.WriteLine($"broken chains: {analysis.BrokenChains.Count}");
        foreach (var broken in analysis.BrokenChains)
            _output.WriteLine($"  {broken.DisplayName} @ {broken.SourceFile}:{broken.SourceLine} -> {broken.MissingDependencyType}");

        Assert.Empty(analysis.BrokenChains);
    }
}
