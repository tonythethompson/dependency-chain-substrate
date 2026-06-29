using DCS.Analysis;
using DCS.Core.Parsing;
using DCS.Parser.Java;

namespace DCS.Parser.Java.Tests;

public sealed class SpringPetClinicAnalysisTests
{
    private static string? ResolvePetClinicPath()
    {
        var env = Environment.GetEnvironmentVariable("PETCLINIC_PATH");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return env;

        var cloneDir = Path.Combine(Path.GetTempPath(), "dcs-petclinic-pin");
        return Directory.Exists(cloneDir) ? cloneDir : null;
    }

    [Fact]
    public void PetClinic_analyze_has_no_leaked_or_broken_chains()
    {
        var path = ResolvePetClinicPath();
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
