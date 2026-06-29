using DCS.Core.IR;

namespace DCS.Verification;

public sealed record SemanticExtractionMetrics(
    double SemanticTypeResolutionRate,
    double RegistrationApiVerificationRate,
    double ProjectScopeCompletenessRate,
    int TotalRegistrations,
    int ResolvedRegistrations,
    int VerifiedRegistrations,
    int SyntaxCandidateRegistrations,
    int CompositionScopeCount,
    int CompleteCompositionScopeCount)
{
    public override string ToString() =>
        $"semantic_type_resolution_rate={SemanticTypeResolutionRate:P1} " +
        $"registration_api_verification_rate={RegistrationApiVerificationRate:P1} " +
        $"project_scope_completeness_rate={ProjectScopeCompletenessRate:P1} " +
        $"(nodes={TotalRegistrations}, scopes={CompositionScopeCount})";
}

public static class TrackdubSemanticMetrics
{
    public static SemanticExtractionMetrics Compute(IReadOnlyList<RegistrationNode> nodes)
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
        return new SemanticExtractionMetrics(
            SemanticTypeResolutionRate: syntaxCount == 0 ? 0 : (double)resolved / syntaxCount,
            RegistrationApiVerificationRate: syntaxCount == 0 ? 0 : (double)verified / syntaxCount,
            ProjectScopeCompletenessRate: scopeIds.Count == 0 ? 0 : (double)completeScopes / scopeIds.Count,
            TotalRegistrations: nodes.Count,
            ResolvedRegistrations: resolved,
            VerifiedRegistrations: verified,
            SyntaxCandidateRegistrations: syntaxCount,
            CompositionScopeCount: scopeIds.Count,
            CompleteCompositionScopeCount: completeScopes);
    }
}
