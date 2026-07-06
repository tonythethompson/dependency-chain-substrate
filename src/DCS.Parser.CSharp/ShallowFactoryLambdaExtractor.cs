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
        ExtractServiceRequestTypes(lambda.Body, requiredOnly: false);

    public static IReadOnlyList<TypeSyntax> TryExtractRequiredServiceRequestTypes(LambdaExpressionSyntax lambda) =>
        ExtractServiceRequestTypes(lambda.Body, requiredOnly: true);

    public static IReadOnlyList<TypeSyntax> TryExtractServiceRequestTypes(AnonymousMethodExpressionSyntax lambda) =>
        ExtractServiceRequestTypes(lambda.Block, requiredOnly: false);

    public static IReadOnlyList<TypeSyntax> TryExtractRequiredServiceRequestTypes(AnonymousMethodExpressionSyntax lambda) =>
        ExtractServiceRequestTypes(lambda.Block, requiredOnly: true);

    private static IReadOnlyList<TypeSyntax> ExtractServiceRequestTypes(SyntaxNode body, bool requiredOnly)
    {
        var results = new List<TypeSyntax>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void AddType(TypeSyntax? typeArg)
        {
            if (typeArg == null)
                return;
            var key = typeArg.ToString();
            if (seen.Add(key))
                results.Add(typeArg);
        }

        foreach (var invocation in body.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            AddType(TryExtractServiceTypeFromInvocation(invocation, requiredOnly));
        }

        foreach (var creation in body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            foreach (var arg in creation.ArgumentList?.Arguments ?? default)
                AddType(TryExtractServiceTypeFromExpression(arg.Expression));
        }

        foreach (var creation in body.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>())
        {
            foreach (var arg in creation.ArgumentList?.Arguments ?? default)
                AddType(TryExtractServiceTypeFromExpression(arg.Expression));
        }

        foreach (var local in body.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            foreach (var variable in local.Declaration.Variables)
            {
                if (variable.Initializer?.Value is not InvocationExpressionSyntax initInvocation)
                    continue;

                TypeSyntax? typeArg = TryExtractServiceTypeFromInvocation(initInvocation, requiredOnly);

                if (typeArg == null)
                    continue;

                AddType(typeArg);

                var varName = variable.Identifier.Text;
                foreach (var creation in body.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
                {
                    foreach (var arg in creation.ArgumentList?.Arguments ?? default)
                    {
                        if (arg.Expression is IdentifierNameSyntax { Identifier.Text: var id } && id == varName)
                            AddType(typeArg);
                    }
                }

                foreach (var implicitCreation in body.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>())
                {
                    foreach (var arg in implicitCreation.ArgumentList?.Arguments ?? default)
                    {
                        if (arg.Expression is IdentifierNameSyntax { Identifier.Text: var id } && id == varName)
                            AddType(typeArg);
                    }
                }
            }
        }

        return results;
    }

    private static TypeSyntax? TryExtractServiceTypeFromInvocation(InvocationExpressionSyntax invocation, bool requiredOnly)
    {
        if (requiredOnly && !IsGetRequiredServiceInvocation(invocation))
            return null;

        if (!requiredOnly && !IsServiceResolutionInvocation(invocation))
            return null;

        TypeSyntax? typeArg = invocation.Expression switch
        {
            MemberAccessExpressionSyntax
            {
                Name: GenericNameSyntax { Identifier.Text: "GetRequiredService" or "GetService" } genericName
            } => genericName.TypeArgumentList.Arguments.FirstOrDefault(),
            _ => null
        };

        if (typeArg == null &&
            IsServiceResolutionInvocation(invocation) &&
            invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression is TypeOfExpressionSyntax typeOf)
        {
            typeArg = typeOf.Type;
        }

        return typeArg;
    }

    private static bool IsGetRequiredServiceInvocation(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "GetRequiredService" };

    private static bool IsServiceResolutionInvocation(InvocationExpressionSyntax invocation) =>
        invocation.Expression is MemberAccessExpressionSyntax { Name.Identifier.Text: "GetRequiredService" or "GetService" };

    private static TypeSyntax? TryExtractServiceTypeFromInvocation(InvocationExpressionSyntax invocation) =>
        TryExtractServiceTypeFromInvocation(invocation, requiredOnly: false);

    private static TypeSyntax? TryExtractServiceTypeFromExpression(ExpressionSyntax expression) =>
        expression switch
        {
            InvocationExpressionSyntax invocation => TryExtractServiceTypeFromInvocation(invocation),
            MemberAccessExpressionSyntax member => TryExtractServiceTypeFromExpression(member.Expression),
            _ => expression.DescendantNodes().OfType<InvocationExpressionSyntax>()
                .Select(TryExtractServiceTypeFromInvocation)
                .FirstOrDefault(t => t != null)
        };

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
            InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax or QualifiedNameSyntax,
                    Name.Identifier.Text: not ("GetRequiredService" or "GetService")
                }
            } invocation when invocation.Expression is MemberAccessExpressionSyntax { Expression: { } typeExpr } =>
                typeExpr as TypeSyntax,
            _ => null
        };

    public static bool ContainsImplicitObjectCreation(LambdaExpressionSyntax lambda)
    {
        if (lambda.Body is ImplicitObjectCreationExpressionSyntax)
            return true;

        if (lambda.Body is ExpressionSyntax expr && expr is ImplicitObjectCreationExpressionSyntax)
            return true;

        return lambda.Body.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>().Any();
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
