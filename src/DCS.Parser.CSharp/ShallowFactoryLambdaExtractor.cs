using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DCS.Parser.CSharp;

internal static class ShallowFactoryLambdaExtractor
{
    public static TypeSyntax? TryExtractCreatedType(LambdaExpressionSyntax lambda)
    {
        var body = lambda.Body;
        ExpressionSyntax? expression = body switch
        {
            ExpressionSyntax expr => expr,
            BlockSyntax block when block.Statements.Count == 1 &&
                                   block.Statements[0] is ReturnStatementSyntax { Expression: { } ret } =>
                ret,
            _ => null
        };

        return expression switch
        {
            ObjectCreationExpressionSyntax { Type: { } type } => type,
            ImplicitObjectCreationExpressionSyntax =>
                null,
            _ => null
        };
    }

    public static TypeSyntax? TryExtractCreatedType(AnonymousMethodExpressionSyntax lambda)
    {
        if (lambda.Block.Statements.Count != 1 ||
            lambda.Block.Statements[0] is not ReturnStatementSyntax { Expression: ObjectCreationExpressionSyntax { Type: { } type } })
            return null;

        return type;
    }

    public static bool UsesGetRequiredService(LambdaExpressionSyntax lambda)
    {
        var body = lambda.Body;
        if (body is ExpressionSyntax expr)
            return ContainsGetRequiredService(expr);

        if (body is BlockSyntax block)
        {
            foreach (var statement in block.Statements)
            {
                if (statement is ReturnStatementSyntax { Expression: { } ret } && ContainsGetRequiredService(ret))
                    return true;
            }
        }

        return false;
    }

    private static bool ContainsGetRequiredService(SyntaxNode node)
    {
        foreach (var invocation in node.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is MemberAccessExpressionSyntax ma &&
                ma.Name.Identifier.Text is "GetRequiredService" or "GetService")
                return true;
        }

        return false;
    }
}
