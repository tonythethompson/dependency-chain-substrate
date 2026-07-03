using DCS.Analysis;
using DCS.Parser.Java;
using DCS.Verification;

namespace DCS.Parser.Java.Tests;

[Collection(CorpusGateCollection.JavaSpring)]
[Trait(CorpusGateTraits.CategoryName, CorpusGateTraits.CategoryValue)]
[Trait(CorpusGateTraits.CorpusIdName, CorpusGateTraits.JavaSpring)]
public sealed class SpringPetClinicAnalysisTests
{
    [Fact]
    public void PetClinic_analyze_has_no_leaked_or_broken_chains()
    {
        var path = CorpusPathResolver.ResolveWithDefaults(
            primaryEnvVar: "CORPUS_JAVA_SPRING_PATH",
            legacyEnvVar: "PETCLINIC_PATH",
            defaultLocalPath: string.Empty,
            tempCloneDirName: "corpus-java-spring",
            workspaceRelativeCheckoutPath: PetClinicPin.CheckoutPath);
        if (path == null)
            return;

        var graph = new SpringStaticParser().ParseDirectory(path).ContextGraphs
            .FirstOrDefault(c => c.EntryRoot.FullyQualifiedName.Contains("PetClinicApplication", StringComparison.Ordinal))
            ?.Graph ?? throw new InvalidOperationException("Expected PetClinicApplication context graph.");

        Assert.True(graph.Edges.Count > 0, "Expected constructor wiring edges on PetClinic.");

        var analysis = new GraphAnalyzer(graph).Analyze();

        Assert.Empty(analysis.Leaked);
        Assert.Empty(analysis.BrokenChains);
        Assert.True(analysis.TotalNodes >= 10);
    }
}
