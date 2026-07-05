using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DCS.Parser.CSharp;

/// <summary>
/// Detects DI registrations in mutually exclusive if/else arms (runtime picks one branch).
/// </summary>
internal static class ConditionalRegistrationDetector
{
    public static void ApplyIfElseAnnotation(SyntaxNode registrationCall, IDictionary<string, string> annotations)
    {
        if (annotations.ContainsKey("conditional"))
            return;

        if (!TryGetIfElseBranch(registrationCall, out var branch))
            return;

        annotations["conditional"] = "if_else";
        annotations["conditional_branch"] = branch;
    }

    private static bool TryGetIfElseBranch(SyntaxNode node, out string branch)
    {
        branch = string.Empty;
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            switch (current)
            {
                case IfStatementSyntax ifStmt when ifStmt.Else != null:
                    if (ContainsStatement(ifStmt.Statement, node))
                    {
                        branch = "if";
                        return true;
                    }

                    if (ContainsStatement(ifStmt.Else, node))
                    {
                        branch = "else";
                        return true;
                    }

                    break;

                case ElseClauseSyntax elseClause when elseClause.Parent is IfStatementSyntax { Else: not null }:
                    if (ContainsStatement(elseClause, node))
                    {
                        branch = "else";
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    private static bool ContainsStatement(SyntaxNode container, SyntaxNode target) =>
        container == target || container.DescendantNodes().Contains(target);
}
