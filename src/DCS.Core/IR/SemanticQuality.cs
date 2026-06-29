namespace DCS.Core.IR;

public enum TypeResolutionQuality
{
    Resolved,
    SyntacticFallback,
    Error
}

public enum RegistrationRecognitionQuality
{
    VerifiedMicrosoftDI,
    SyntaxCandidateUnverified,
    UnsupportedPattern
}

public static class StrictDuplicateEligibility
{
    public const string AnnotationKey = "strict_duplicate_eligible";

    public static bool IsEligible(RegistrationNode node) =>
        node.TypeResolutionQuality == TypeResolutionQuality.Resolved &&
        node.RegistrationRecognitionQuality == RegistrationRecognitionQuality.VerifiedMicrosoftDI &&
        !string.IsNullOrEmpty(node.CompositionScopeId) &&
        !string.IsNullOrEmpty(node.DuplicateGroupKey) &&
        node.Annotations.GetValueOrDefault("project_evaluation_incomplete") != "true";
}
