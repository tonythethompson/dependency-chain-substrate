using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DCS.Fix;

public static class RegistrationStatementRemover
{
    private static readonly HashSet<string> KnownRegistrationMethods = new(StringComparer.Ordinal)
    {
        "AddSingleton", "AddScoped", "AddTransient",
        "TryAddSingleton", "TryAddScoped", "TryAddTransient",
        "AddKeyedSingleton", "AddKeyedScoped", "AddKeyedTransient",
        "TryAddKeyedSingleton", "TryAddKeyedScoped", "TryAddKeyedTransient",
    };

    public static string? TryRemove(string source, int line, string abstractTokenShortName) =>
        TryRemoveMany(source, [new RegistrationRemovalRequest(line, abstractTokenShortName)]);

    public static string? TryRemoveMany(string source, IReadOnlyList<RegistrationRemovalRequest> removals)
    {
        if (removals.Count == 0)
            return source;

        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        foreach (var removal in removals.OrderByDescending(r => r.Line))
        {
            if (!TryRemoveOne(ref root, removal.Line, removal.TokenName))
                return null;
        }

        return root.ToFullString();
    }

    private static bool TryRemoveOne(ref SyntaxNode root, int line, string abstractTokenShortName)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var startLine = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (startLine != line)
                continue;

            if (!MatchesRegistration(invocation, abstractTokenShortName))
                continue;

            var statement = invocation.Ancestors().OfType<ExpressionStatementSyntax>().FirstOrDefault();
            if (statement == null)
                return false;

            var updated = root.RemoveNode(statement, SyntaxRemoveOptions.KeepNoTrivia);
            if (updated == null)
                return false;

            root = updated;
            return true;
        }

        return false;
    }

    private static bool MatchesRegistration(InvocationExpressionSyntax invocation, string abstractTokenShortName)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return false;

        if (!KnownRegistrationMethods.Contains(memberAccess.Name.Identifier.Text))
            return false;

        if (memberAccess.Name is GenericNameSyntax { TypeArgumentList.Arguments: var typeArgs })
        {
            foreach (var arg in typeArgs)
            {
                if (TypeSyntaxContainsName(arg, abstractTokenShortName))
                    return true;
            }
        }

        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.Expression is TypeOfExpressionSyntax typeOf &&
                TypeSyntaxContainsName(typeOf.Type, abstractTokenShortName))
                return true;
        }

        return false;
    }

    private static bool TypeSyntaxContainsName(TypeSyntax type, string name)
    {
        var shortName = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;

        var text = type.ToString();
        if (string.Equals(text, name, StringComparison.Ordinal) ||
            string.Equals(text, shortName, StringComparison.Ordinal))
            return true;

        if (text.StartsWith(name + "<", StringComparison.Ordinal) ||
            text.StartsWith(shortName + "<", StringComparison.Ordinal))
            return true;

        return type switch
        {
            IdentifierNameSyntax id => string.Equals(id.Identifier.Text, shortName, StringComparison.Ordinal),
            GenericNameSyntax gn => string.Equals(gn.Identifier.Text, shortName, StringComparison.Ordinal),
            QualifiedNameSyntax qn => string.Equals(qn.Right.ToString(), shortName, StringComparison.Ordinal) ||
                                      TypeSyntaxContainsName(qn.Right, name),
            _ => false
        };
    }
}
