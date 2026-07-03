using DCS.Core.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DCS.Fix;

public static class FactoryLambdaToExplicitConverter
{
    private static readonly HashSet<string> KnownRegistrationMethods = new(StringComparer.Ordinal)
    {
        "AddSingleton", "AddScoped", "AddTransient",
        "TryAddSingleton", "TryAddScoped", "TryAddTransient",
    };

    public static bool TryBuildReplacement(
        string source,
        int line,
        RegistrationNode node,
        out string replacementStatement)
    {
        replacementStatement = string.Empty;

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var startLine = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (startLine != line)
                continue;

            if (!TryParseRegistrationInvocation(invocation, out var methodName, out var receiverExpression, out var lambdaBody))
                continue;

            if (lambdaBody != null && UsesServiceLocator(lambdaBody))
                return false;

            replacementStatement = BuildReplacement(receiverExpression, methodName, node);
            return !string.IsNullOrEmpty(replacementStatement);
        }

        return false;
    }

    public static string? TryConvert(string source, int line, RegistrationNode node)
    {
        if (!TryBuildReplacement(source, line, node, out var replacement))
            return null;

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var startLine = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (startLine != line)
                continue;

            if (!TryParseRegistrationInvocation(invocation, out _, out _, out _))
                continue;

            var statement = invocation.Ancestors().OfType<ExpressionStatementSyntax>().FirstOrDefault();
            if (statement == null)
                return null;

            var replacementExpr = SyntaxFactory.ParseExpression(replacement.TrimEnd(';'))
                .WithTriviaFrom(statement.Expression);
            var replacementStmt = SyntaxFactory.ExpressionStatement(replacementExpr)
                .WithTriviaFrom(statement);

            var updated = root.ReplaceNode(statement, replacementStmt);
            return updated.NormalizeWhitespace().ToFullString();
        }

        return null;
    }

    private static string BuildReplacement(string receiverExpression, string methodName, RegistrationNode node)
    {
        var abstractName = TypeDisplayName(node.AbstractToken);
        var concreteName = TypeDisplayName(node.ConcreteImpl!);
        var usesTryAdd = methodName.StartsWith("TryAdd", StringComparison.Ordinal);
        var lifetime = node.Lifetime switch
        {
            Lifetime.Scoped => "Scoped",
            Lifetime.Transient => "Transient",
            _ => "Singleton"
        };

        if (!usesTryAdd && node.Annotations.ContainsKey("conditional"))
            usesTryAdd = string.Equals(node.Annotations["conditional"], "try_add", StringComparison.Ordinal);

        var prefix = usesTryAdd ? "TryAdd" : "Add";
        var method = $"{prefix}{lifetime}";

        if (!string.Equals(abstractName, concreteName, StringComparison.Ordinal))
            return $"{receiverExpression}.{method}<{abstractName}, {concreteName}>();";

        return $"{receiverExpression}.{method}<{concreteName}>();";
    }

    private static string TypeDisplayName(TypeRef typeRef) =>
        string.IsNullOrWhiteSpace(typeRef.FullyQualifiedName)
            ? typeRef.ShortName
            : typeRef.FullyQualifiedName;

    private static bool TryParseRegistrationInvocation(
        InvocationExpressionSyntax invocation,
        out string methodName,
        out string receiverExpression,
        out SyntaxNode? lambdaBody)
    {
        methodName = string.Empty;
        receiverExpression = string.Empty;
        lambdaBody = null;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        methodName = memberAccess.Name.Identifier.Text;
        if (!KnownRegistrationMethods.Contains(methodName))
            return false;

        receiverExpression = memberAccess.Expression.ToString();
        lambdaBody = invocation.ArgumentList.Arguments
            .Select(a => a.Expression)
            .FirstOrDefault(e => e is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax);

        return true;
    }

    private static bool UsesServiceLocator(SyntaxNode lambdaBody)
    {
        foreach (var invocation in lambdaBody.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is MemberAccessExpressionSyntax ma &&
                ma.Name.Identifier.Text is "GetRequiredService" or "GetService")
            {
                return true;
            }
        }

        return false;
    }
}
