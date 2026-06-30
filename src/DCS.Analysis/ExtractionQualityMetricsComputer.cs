using DCS.Core.IR;

namespace DCS.Analysis;

public static class ExtractionQualityMetricsComputer
{
    public static ExtractionQualityMetrics Compute(IReadOnlyList<RegistrationNode> nodes)
    {
        var syntaxCandidates = nodes
            .Where(n => n.RegistrationRecognitionQuality != RegistrationRecognitionQuality.UnsupportedPattern)
            .ToList();

        var resolved = syntaxCandidates
            .Count(n => n.TypeResolutionQuality == TypeResolutionQuality.Resolved);

        var verified = syntaxCandidates
            .Count(n => n.RegistrationRecognitionQuality == RegistrationRecognitionQuality.VerifiedMicrosoftDI);

        var scopeIds = nodes
            .Select(n => n.CompositionScopeId)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var completeScopes = scopeIds.Count(scopeId =>
            !nodes.Any(n =>
                n.CompositionScopeId == scopeId &&
                n.Annotations.GetValueOrDefault("project_evaluation_incomplete") == "true"));

        var syntaxCount = syntaxCandidates.Count;
        return new ExtractionQualityMetrics
        {
            SemanticTypeResolutionRate = syntaxCount == 0 ? 0 : (double)resolved / syntaxCount,
            RegistrationApiVerificationRate = syntaxCount == 0 ? 0 : (double)verified / syntaxCount,
            ProjectScopeCompletenessRate = scopeIds.Count == 0 ? 0 : (double)completeScopes / scopeIds.Count,
            TotalRegistrations = nodes.Count,
            ResolvedRegistrations = resolved,
            VerifiedRegistrations = verified,
            SyntaxCandidateRegistrations = syntaxCount,
            CompositionScopeCount = scopeIds.Count,
            CompleteCompositionScopeCount = completeScopes
        };
    }
}
