using DCS.Analysis;
using DCS.Parser.CSharp;
using DCS.Verification;

namespace DCS.Parser.CSharp.Tests;

/// <summary>
/// Negative-control corpus: single-framework Avalonia app must not report LEAKED or DUPLICATE.
/// See DESIGN.md §17 and <c>ci/corpus-gates.json</c> leg <c>csharp-negative-control</c>.
/// </summary>
[Collection(CorpusGateCollection.CsharpNegativeControl)]
[Trait(CorpusGateTraits.CategoryName, CorpusGateTraits.CategoryValue)]
[Trait(CorpusGateTraits.CorpusIdName, CorpusGateTraits.CsharpNegativeControl)]
public sealed class CsharpNegativeControlGateTests
{
    [Fact]
    public void StabilityMatrix_primary_shell_has_no_leaked_or_duplicate()
    {
        var path = CorpusPathResolver.ResolveWithDefaults(
            primaryEnvVar: "CORPUS_CSHARP_NEGATIVE_CONTROL_PATH",
            legacyEnvVar: string.Empty,
            defaultLocalPath: string.Empty,
            tempCloneDirName: "corpus-csharp-negative-control",
            workspaceRelativeCheckoutPath: StabilityMatrixPin.CheckoutPath);
        if (path == null)
            return;

        var analyzeRoot = Path.Combine(path, StabilityMatrixPin.AnalyzeSubdirectory);
        if (!Directory.Exists(analyzeRoot))
        {
            throw new InvalidOperationException(
                $"Expected analyze subdirectory '{StabilityMatrixPin.AnalyzeSubdirectory}' under corpus root '{path}'.");
        }

        var result = new CSharpStaticParser(new CSharpParseOptions { IncludeTests = false })
            .ParseDirectory(analyzeRoot);

        var graph = result.ContextGraphs
            .OrderByDescending(c => c.Graph.Nodes.Count)
            .FirstOrDefault()
            ?.Graph ?? throw new InvalidOperationException("Expected at least one context graph.");

        Assert.True(graph.Nodes.Count >= 10,
            $"Expected substantial registration count on primary shell; got {graph.Nodes.Count}.");

        var analysis = new GraphAnalyzer(graph).Analyze();

        Assert.Empty(analysis.Leaked);
        Assert.Empty(analysis.Duplicates);
        Assert.Empty(analysis.BrokenChains);
    }
}
