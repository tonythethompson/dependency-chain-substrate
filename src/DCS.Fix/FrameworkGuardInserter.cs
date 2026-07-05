using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DCS.Fix;

public static class FrameworkGuardInserter
{
    private static readonly HashSet<string> KnownRegistrationMethods = new(StringComparer.Ordinal)
    {
        "AddSingleton", "AddScoped", "AddTransient",
        "TryAddSingleton", "TryAddScoped", "TryAddTransient",
        "AddKeyedSingleton", "AddKeyedScoped", "AddKeyedTransient",
        "TryAddKeyedSingleton", "TryAddKeyedScoped", "TryAddKeyedTransient",
    };

    public static string? TryWrapRegistration(
        string source,
        int line,
        string abstractTokenShortName,
        string guardSymbol)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var startLine = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (startLine != line)
                continue;

            if (!MatchesRegistration(invocation, abstractTokenShortName))
                continue;

            var statement = invocation.Ancestors().OfType<ExpressionStatementSyntax>().FirstOrDefault();
            if (statement == null)
                return null;

            if (statement.Ancestors().OfType<LambdaExpressionSyntax>().Any())
                return null;

            return InsertGuardAroundLine(source, line, guardSymbol);
        }

        return null;
    }

    private static string InsertGuardAroundLine(string source, int line, string guardSymbol)
    {
        var newline = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        var index = line - 1;

        if (index < 0 || index >= lines.Count)
            return source;

        var indent = new string(lines[index].TakeWhile(char.IsWhiteSpace).ToArray());
        lines.Insert(index, $"{indent}#if {guardSymbol}");
        lines.Insert(index + 2, $"{indent}#endif");

        return string.Join(newline, lines);
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

        return false;
    }

    private static bool TypeSyntaxContainsName(TypeSyntax typeSyntax, string name)
    {
        if (typeSyntax is IdentifierNameSyntax id)
            return string.Equals(id.Identifier.Text, name, StringComparison.Ordinal);

        if (typeSyntax is QualifiedNameSyntax qualified)
            return TypeSyntaxContainsName(qualified.Right, name);

        return typeSyntax.ToString().Contains(name, StringComparison.Ordinal);
    }
}
