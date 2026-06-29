using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DCS.Parser.CSharp.Semantic;

public static class RegistrationApiVerifier
{
    private const string DiNamespace = "Microsoft.Extensions.DependencyInjection";

    public static DCS.Core.IR.RegistrationRecognitionQuality Verify(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        string methodName)
    {
        var symbolInfo = model.GetSymbolInfo(invocation);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        if (symbol == null)
            return DCS.Core.IR.RegistrationRecognitionQuality.SyntaxCandidateUnverified;

        var containingType = symbol.ContainingType;
        if (containingType == null)
            return DCS.Core.IR.RegistrationRecognitionQuality.SyntaxCandidateUnverified;

        var ns = containingType.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (!ns.Equals(DiNamespace, StringComparison.Ordinal) &&
            !ns.StartsWith(DiNamespace + ".", StringComparison.Ordinal))
            return DCS.Core.IR.RegistrationRecognitionQuality.SyntaxCandidateUnverified;

        if (!methodName.Contains("Singleton", StringComparison.Ordinal) &&
            !methodName.Contains("Scoped", StringComparison.Ordinal) &&
            !methodName.Contains("Transient", StringComparison.Ordinal))
            return DCS.Core.IR.RegistrationRecognitionQuality.UnsupportedPattern;

        return DCS.Core.IR.RegistrationRecognitionQuality.VerifiedMicrosoftDI;
    }
}
