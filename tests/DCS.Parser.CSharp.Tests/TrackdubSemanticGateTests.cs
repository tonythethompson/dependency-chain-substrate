using DCS.Analysis;
using DCS.Core.IR;
using DCS.Parser.CSharp;
using DCS.Verification;
using Xunit;
using Xunit.Abstractions;

namespace DCS.Parser.CSharp.Tests;

[Collection(CorpusGateCollection.CsharpMigration)]
[Trait(CorpusGateTraits.CategoryName, CorpusGateTraits.CategoryValue)]
[Trait(CorpusGateTraits.CorpusIdName, CorpusGateTraits.CsharpMigration)]
public sealed class TrackdubSemanticGateTests
{
    // Pin b57fc832 (GitHub main): aggregate cross-TFM rate ~57.6%; portable ~59.3%, windows ~55.6%.
    private const double MinSemanticTypeResolutionRate = 0.55;
    private const double MinWindowsSemanticTypeResolutionRate = 0.50;
    private const double MinRegistrationApiVerificationRate = 0.95;
    private const double MinProjectScopeCompletenessRate = 0.80;

    private readonly ITestOutputHelper _output;

    public TrackdubSemanticGateTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Trackdub_semantic_extraction_gate()
    {
        var path = TrackdubPin.ResolvePath();
        if (path == null)
        {
            return;
        }

        var parser = new CSharpStaticParser(new CSharpParseOptions
        {
            AllTargetFrameworks = true,
            IncludeTests = false
        });
        var result = parser.ParseCommit(path, TrackdubPin.CommitSha);

        Assert.True(result.ContextGraphs.Count >= 2,
            $"Expected isolated graphs per TFM; got: {string.Join(", ", result.ContextGraphs.Select(c => c.ContextId))}");

        foreach (var context in result.ContextGraphs)
        {
            Assert.Equal(CSharpStaticParser.ParserVersion, context.Graph.ParserVersion);
            Assert.Equal("true", context.Graph.Metadata.GetValueOrDefault("semantic_parser"));
        }

        var allNodes = result.ContextGraphs.SelectMany(c => c.Graph.Nodes).ToList();
        Assert.True(allNodes.Count >= 280,
            $"Expected post-migration registration count (Avalonia-only + API growth); got {allNodes.Count}");

        var metrics = TrackdubSemanticMetrics.Compute(allNodes);
        _output.WriteLine($"aggregate {metrics}");

        foreach (var context in result.ContextGraphs)
        {
            var contextMetrics = TrackdubSemanticMetrics.Compute(context.Graph.Nodes);
            _output.WriteLine($"{context.ContextId} {contextMetrics}");
        }

        Assert.True(metrics.SemanticTypeResolutionRate >= MinSemanticTypeResolutionRate,
            $"semantic_type_resolution_rate too low: {metrics.SemanticTypeResolutionRate:P}");
        Assert.True(metrics.RegistrationApiVerificationRate >= MinRegistrationApiVerificationRate,
            $"registration_api_verification_rate too low: {metrics.RegistrationApiVerificationRate:P}");
        Assert.True(metrics.ProjectScopeCompletenessRate >= MinProjectScopeCompletenessRate,
            $"project_scope_completeness_rate too low: {metrics.ProjectScopeCompletenessRate:P}");

        var windowsContext = result.ContextGraphs.FirstOrDefault(c =>
            c.ContextId.Contains("windows", StringComparison.OrdinalIgnoreCase));
        if (windowsContext != null)
        {
            var windowsMetrics = TrackdubSemanticMetrics.Compute(windowsContext.Graph.Nodes);
            Assert.True(windowsMetrics.SemanticTypeResolutionRate >= MinWindowsSemanticTypeResolutionRate,
                $"windows semantic_type_resolution_rate too low: {windowsMetrics.SemanticTypeResolutionRate:P}");
        }

        var avaloniaShellBlindSpots = result.ContextGraphs
            .SelectMany(c => c.Graph.BlindSpots)
            .Where(b => b.Location?.FilePath?.Contains("trackdub.app.avalonia/app.axaml.cs", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
        Assert.DoesNotContain(avaloniaShellBlindSpots, b =>
            b.Pattern == "unrecognized_pattern" &&
            b.Description.Contains("AddSingleton", StringComparison.Ordinal));

        var mainWindowShellRegistration = allNodes.Any(n =>
            n.SourceLocation?.FilePath?.Contains("trackdub.app.avalonia/app.axaml.cs", StringComparison.OrdinalIgnoreCase) == true &&
            (n.DisplayName.Contains("MainWindow", StringComparison.Ordinal) ||
             n.AbstractToken.ShortName == "MainWindow" ||
             n.ConcreteImpl?.ShortName == "MainWindow"));
        Assert.True(mainWindowShellRegistration,
            "Expected Avalonia shell MainWindow block factory lambda to register as shallow factory node.");

        var consentSignal = allNodes.Any(n =>
                n.DisplayName.Contains("IConsentService", StringComparison.Ordinal) ||
                n.AbstractToken.FullyQualifiedName.Contains("IConsentService", StringComparison.Ordinal))
            || result.ContextGraphs
                .SelectMany(c => c.Graph.BlindSpots)
                .Any(b => b.Description.Contains("IConsentService", StringComparison.Ordinal));
        Assert.True(consentSignal, "Expected IConsentService registration or factory-lambda blind spot.");

        // WinUI shell retired @ pin b57fc832 — no registrations from legacy Trackdub.App.
        Assert.DoesNotContain(allNodes, n =>
            n.SourceLocation?.FilePath?.Contains("trackdub.app/app.xaml.cs", StringComparison.OrdinalIgnoreCase) == true);

        var voiceCloneSites = allNodes
            .Where(n => n.DisplayName.Contains("VoiceCloneConsentCoordinator", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(voiceCloneSites);
        Assert.All(voiceCloneSites, n =>
        {
            var path = n.SourceLocation?.FilePath?.Replace('\\', '/') ?? string.Empty;
            Assert.Contains("trackdub.app.avalonia/app.axaml.cs", path, StringComparison.OrdinalIgnoreCase);
        });

        var combinedGraph = new RegistrationGraph
        {
            ParserVersion = CSharpStaticParser.ParserVersion,
            CommitSha = TrackdubPin.CommitSha,
            SourceLanguage = "csharp",
            Nodes = allNodes,
            Edges = result.ContextGraphs.SelectMany(c => c.Graph.Edges).ToList(),
            BlindSpots = result.ContextGraphs.SelectMany(c => c.Graph.BlindSpots).ToList(),
            UnresolvedInjections = result.ContextGraphs.SelectMany(c => c.Graph.UnresolvedInjections).ToList()
        };

        var analysisCombined = new GraphAnalyzer(combinedGraph).Analyze();

        // Cross-TFM aggregate may homonym-count VoiceClone (portable + windows graphs); migration debt
        // is cleared when all sites are Avalonia-only (WinUI shell retired).
        var voiceClonePossibleDup = analysisCombined.PossibleDuplicates
            .FirstOrDefault(d => d.AbstractTokenName.Contains("VoiceCloneConsentCoordinator", StringComparison.Ordinal));
        if (voiceClonePossibleDup != null)
        {
            var dupPaths = voiceClonePossibleDup.NodeIds
                .Select(id => allNodes.First(n => n.Id == id).SourceLocation?.FilePath?.Replace('\\', '/') ?? string.Empty)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(dupPaths, p =>
                p.Contains("trackdub.app/app.xaml.cs", StringComparison.OrdinalIgnoreCase));
        }

        Assert.DoesNotContain(analysisCombined.Duplicates, d =>
            d.AbstractTokenName.Contains("SubtitleExportService", StringComparison.Ordinal));
    }
}
