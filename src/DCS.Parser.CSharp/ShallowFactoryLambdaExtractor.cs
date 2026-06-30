using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DCS.Parser.CSharp;

internal static class ShallowFactoryLambdaExtractor
{
    public static TypeSyntax? TryExtractCreatedType(LambdaExpressionSyntax lambda)
    {
        return lambda.Body switch
        {
            ExpressionSyntax expr => TryExtractTypeFromExpression(expr),
            BlockSyntax block => TryExtractTypeFromBlock(block),
            _ => null
        };
    }

    public static TypeSyntax? TryExtractCreatedType(AnonymousMethodExpressionSyntax lambda)
    {
        if (lambda.Block.Statements.Count == 0)
            return null;

        return TryExtractTypeFromBlock(lambda.Block);
    }

    public static IReadOnlyList<TypeSyntax> TryExtractServiceRequestTypes(LambdaExpressionSyntax lambda) =>
        ExtractServiceRequestTypes(lambda.Body);

    public static IReadOnlyList<TypeSyntax> TryExtractServiceRequestTypes(AnonymousMethodExpressionSyntax lambda) =>
        ExtractServiceRequestTypes(lambda.Block);

    private static IReadOnlyList<TypeSyntax> ExtractServiceRequestTypes(SyntaxNode body)
    {
        var results = new List<TypeSyntax>();
        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            TypeSyntax? typeArg = invocation.Expression switch
            {
                MemberAccessExpressionSyntax
                {
                    Name: GenericNameSyntax { Identifier.Text: "GetRequiredService" or "GetService" } genericName
                } => genericName.TypeArgumentList.Arguments.FirstOrDefault(),
                _ => null
            };

            if (typeArg == null &&
                invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "GetRequiredService" or "GetService" } &&
                invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is TypeOfExpressionSyntax typeOf)
            {
                typeArg = typeOf.Type;
            }

            if (typeArg != null)
                results.Add(typeArg);
        }

        return results;
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

                if (ContainsGetRequiredService(statement))
                    return true;
            }
        }

        return false;
    }

    private static TypeSyntax? TryExtractTypeFromBlock(BlockSyntax block)
    {
        for (var i = block.Statements.Count - 1; i >= 0; i--)
        {
            if (block.Statements[i] is not ReturnStatementSyntax { Expression: { } ret })
                continue;

            var fromReturn = TryExtractTypeFromExpression(ret);
            if (fromReturn != null)
                return fromReturn;

            if (ret is IdentifierNameSyntax idName)
            {
                var fromLocal = FindObjectCreationTypeForVariable(block, idName.Identifier.Text);
                if (fromLocal != null)
                    return fromLocal;
            }
        }

        return null;
    }

    private static TypeSyntax? FindObjectCreationTypeForVariable(BlockSyntax block, string variableName)
    {
        foreach (var stmt in block.Statements)
        {
            if (stmt is not LocalDeclarationStatementSyntax local)
                continue;

            foreach (var variable in local.Declaration.Variables)
            {
                if (variable.Identifier.Text != variableName)
                    continue;

                if (variable.Initializer?.Value is ObjectCreationExpressionSyntax { Type: { } createdType })
                    return createdType;

                if (local.Declaration.Type is TypeSyntax declaredType &&
                    variable.Initializer?.Value is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax)
                    return declaredType;
            }
        }

        return null;
    }

    private static TypeSyntax? TryExtractTypeFromExpression(ExpressionSyntax expression) =>
        expression switch
        {
            ObjectCreationExpressionSyntax { Type: { } type } => type,
            ImplicitObjectCreationExpressionSyntax => null,
            _ => null
        };

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
