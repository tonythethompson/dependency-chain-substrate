using DCS.Analysis;
using DCS.Core.IR;
using DCS.Parser.CSharp;
using DCS.Verification;
using Xunit;
using Xunit.Abstractions;

namespace DCS.Parser.CSharp.Tests;

public sealed class TrackdubSemanticGateTests
{
    private readonly ITestOutputHelper _output;

    public TrackdubSemanticGateTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Trackdub_semantic_extraction_gate()
    {
        var path = TrackdubPin.ResolvePath()
            ?? throw new InvalidOperationException(
                $"Trackdub not found. Set TRACKDUB_PATH or clone to {TrackdubPin.DefaultLocalPath}.");

        var parser = new CSharpStaticParser(new CSharpParseOptions { AllTargetFrameworks = true });
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
        _output.WriteLine(metrics.ToString());

        Assert.True(metrics.SemanticTypeResolutionRate >= 0.45,
            $"semantic_type_resolution_rate too low: {metrics.SemanticTypeResolutionRate:P}");
        Assert.True(metrics.RegistrationApiVerificationRate >= 0.45,
            $"registration_api_verification_rate too low: {metrics.RegistrationApiVerificationRate:P}");
        Assert.True(metrics.ProjectScopeCompletenessRate >= 0.85,
            $"project_scope_completeness_rate too low: {metrics.ProjectScopeCompletenessRate:P}");

        var consent = allNodes.FirstOrDefault(n =>
            n.TypeResolutionQuality == TypeResolutionQuality.Resolved &&
            n.RegistrationRecognitionQuality == RegistrationRecognitionQuality.VerifiedMicrosoftDI &&
            (n.AbstractToken.FullyQualifiedName.Contains("IConsentService", StringComparison.Ordinal) ||
             n.DisplayName.Contains("IConsentService", StringComparison.Ordinal)));
        Assert.NotNull(consent);

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

        var analysis = new GraphAnalyzer(combinedGraph).Analyze();

        Assert.True(analysis.Duplicates.Count >= 1,
            "Expected at least one strict DUPLICATE (cross-shell migration leakage).");
        Assert.Contains(analysis.Duplicates, d =>
            d.AbstractTokenName.Contains("SubtitleExportService", StringComparison.Ordinal) &&
            d.NodeIds.Count >= 2);

        Assert.Contains(analysis.PossibleDuplicates, d =>
            d.AbstractTokenName.Contains("VoiceCloneConsentCoordinator", StringComparison.Ordinal) &&
            d.NodeIds.Count >= 2);
    }
}
