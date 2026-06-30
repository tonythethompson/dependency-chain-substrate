using DCS.Analysis;
using DCS.Core.IR;
using DCS.Parser.CSharp;
using DCS.Verification;
using Xunit;
using Xunit.Abstractions;

namespace DCS.Parser.CSharp.Tests;

public sealed class TrackdubSemanticGateTests
{
    // Phase 12: raised aggregate floor after cross-TFM compilation closure + ref-pack fix.
    private const double MinSemanticTypeResolutionRate = 0.57;
    private const double MinWindowsSemanticTypeResolutionRate = 0.40;
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
        Assert.True(allNodes.Count >= 150, $"Expected substantial registration count; got {allNodes.Count}");

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

        var voiceCloneSites = allNodes
            .Where(n => n.DisplayName.Contains("VoiceCloneConsentCoordinator", StringComparison.Ordinal))
            .ToList();
        Assert.True(voiceCloneSites.Count >= 2,
            "Expected VoiceCloneConsentCoordinator registrations in multiple shell sites (migration signal).");
        Assert.Equal(voiceCloneSites.Count, voiceCloneSites.Select(n => n.Id).Distinct(StringComparer.Ordinal).Count());

        var shellPaths = voiceCloneSites
            .Select(n => n.SourceLocation?.FilePath?.Replace('\\', '/') ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(shellPaths, p => p.Contains("trackdub.app/app.xaml.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(shellPaths, p => p.Contains("trackdub.app.avalonia/app.axaml.cs", StringComparison.OrdinalIgnoreCase));

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
        var report = AnalysisReportBuilder.Build(combinedGraph, analysisCombined, new AnalysisReportBuildOptions
        {
            Verbosity = ReportVerbosity.Full
        });

        var voiceCloneFinding = report.Findings.FirstOrDefault(f =>
            f.Category == FindingCategory.PossibleDuplicate &&
            f.Title.Contains("VoiceCloneConsentCoordinator", StringComparison.Ordinal));
        Assert.NotNull(voiceCloneFinding);
        Assert.True(voiceCloneFinding.Sites.Count >= 2, "Expected file:line sites for both shell registrations");
        Assert.All(voiceCloneFinding.Sites, s =>
        {
            Assert.False(string.IsNullOrEmpty(s.FilePath));
            Assert.NotNull(s.Line);
        });
        Assert.Contains(voiceCloneFinding.Sites, s =>
            s.FilePath!.Contains("trackdub.app/app.xaml.cs", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(voiceCloneFinding.Sites, s =>
            s.FilePath!.Contains("trackdub.app.avalonia/app.axaml.cs", StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(analysisCombined.Duplicates, d =>
            d.AbstractTokenName.Contains("SubtitleExportService", StringComparison.Ordinal));

        Assert.Contains(analysisCombined.PossibleDuplicates, d =>
            d.AbstractTokenName.Contains("VoiceCloneConsentCoordinator", StringComparison.Ordinal) &&
            d.NodeIds.Count >= 2);
    }
}
