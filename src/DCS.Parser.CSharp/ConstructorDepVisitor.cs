using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DCS.Parser.CSharp;

internal sealed class ConstructorDepVisitor : CSharpSyntaxWalker
{
    private readonly Dictionary<string, List<string>> _constructorDeps = [];

    public IReadOnlyDictionary<string, List<string>> ConstructorDeps => _constructorDeps;

    public override void VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        var className = node.Identifier.Text;
        var ctors = node.Members.OfType<ConstructorDeclarationSyntax>().ToList();

        // Use the constructor with the most parameters (primary injection target)
        var mainCtor = ctors.OrderByDescending(c => c.ParameterList.Parameters.Count).FirstOrDefault();
        if (mainCtor == null)
        {
            base.VisitClassDeclaration(node);
            return;
        }

        var deps = mainCtor.ParameterList.Parameters
            .Where(p => p.Type != null)
            .Select(p => GetTypeName(p.Type!))
            .ToList();

        if (deps.Count > 0)
            _constructorDeps[className] = deps;

        base.VisitClassDeclaration(node);
    }

    private static string GetTypeName(TypeSyntax type) => type switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        QualifiedNameSyntax qn => qn.ToString(),
        GenericNameSyntax gn =>
            $"{gn.Identifier.Text}<{string.Join(", ", gn.TypeArgumentList.Arguments.Select(GetTypeName))}>",
        PredefinedTypeSyntax pt => pt.Keyword.Text,
        NullableTypeSyntax nt => $"{GetTypeName(nt.ElementType)}?",
        _ => type.ToString()
    };
}
