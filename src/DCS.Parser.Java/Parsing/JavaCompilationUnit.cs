using DCS.Core.IR;
using TreeSitter;

namespace DCS.Parser.Java.Parsing;

public sealed class JavaCompilationUnit
{
    public required string FilePath { get; init; }
    public required string ModuleId { get; init; }
    public SourceSetKind SourceSet { get; init; }
    public string? PackageName { get; init; }
    public List<string> Imports { get; init; } = [];
    public List<JavaTypeDeclaration> Types { get; init; } = [];
    public bool HasSyntaxError { get; init; }
    public string Source { get; init; } = string.Empty;
    public Node Root { get; init; } = default!;
}

public sealed class JavaTypeDeclaration
{
    public required string SimpleName { get; init; }
    public required string Kind { get; init; }
    public required Node Node { get; init; }
    public List<JavaAnnotation> Annotations { get; init; } = [];
    public List<string> ExtendsAndImplements { get; init; } = [];
    public List<JavaMethodDeclaration> Methods { get; init; } = [];
    public List<JavaFieldDeclaration> Fields { get; init; } = [];
    public List<JavaConstructorDeclaration> Constructors { get; init; } = [];
    public bool IsInterface { get; init; }
}

public sealed class JavaMethodDeclaration
{
    public required string Name { get; init; }
    public required Node Node { get; init; }
    public string? ReturnTypeText { get; init; }
    public List<JavaParameter> Parameters { get; init; } = [];
    public List<JavaAnnotation> Annotations { get; init; } = [];
    public bool IsStatic { get; init; }
    public string? BodyText { get; init; }
    public int Line { get; init; }
}

public sealed class JavaConstructorDeclaration
{
    public required Node Node { get; init; }
    public List<JavaParameter> Parameters { get; init; } = [];
    public List<JavaAnnotation> Annotations { get; init; } = [];
    public int Line { get; init; }
}

public sealed class JavaFieldDeclaration
{
    public required string Name { get; init; }
    public required Node Node { get; init; }
    public string? TypeText { get; init; }
    public List<JavaAnnotation> Annotations { get; init; } = [];
    public int Line { get; init; }
}

public sealed record JavaParameter(string Name, string TypeText, List<JavaAnnotation> Annotations);

public static class JavaCompilationUnitBuilder
{
    public static JavaCompilationUnit Build(
        string filePath,
        string moduleId,
        SourceSetKind sourceSet,
        string source,
        Node root)
    {
        var package = JavaNodeWalker.FindPackage(root);
        var imports = JavaNodeWalker.FindImports(root);
        var types = new List<JavaTypeDeclaration>();

        foreach (var node in JavaNodeWalker.OfType(root, "class_declaration")
                     .Concat(JavaNodeWalker.OfType(root, "interface_declaration"))
                     .Concat(JavaNodeWalker.OfType(root, "enum_declaration")))
        {
            var nameNode = JavaNodeWalker.ChildOfType(node, "identifier");
            if (nameNode == null)
                continue;

            var decl = new JavaTypeDeclaration
            {
                SimpleName = nameNode.Text,
                Kind = node.Type,
                Node = node,
                IsInterface = node.Type == "interface_declaration",
                Annotations = JavaNodeWalker.FindAnnotations(node),
                ExtendsAndImplements = JavaNodeWalker.GetExtendsAndImplements(node),
                Methods = ExtractMethods(node),
                Fields = ExtractFields(node),
                Constructors = ExtractConstructors(node)
            };
            types.Add(decl);
        }

        return new JavaCompilationUnit
        {
            FilePath = filePath,
            ModuleId = moduleId,
            SourceSet = sourceSet,
            PackageName = package,
            Imports = imports,
            Types = types,
            HasSyntaxError = root.HasError,
            Source = source,
            Root = root
        };
    }

    private static List<JavaMethodDeclaration> ExtractMethods(Node typeNode)
    {
        var methods = new List<JavaMethodDeclaration>();
        foreach (var method in JavaNodeWalker.OfType(typeNode, "method_declaration"))
        {
            var nameNode = JavaNodeWalker.ChildOfType(method, "identifier");
            if (nameNode == null)
                continue;

            var returnType = method.NamedChildren.FirstOrDefault(n => n.Type is "type_identifier" or "scoped_type_identifier" or "generic_type" or "void_type");
            var body = JavaNodeWalker.ChildOfType(method, "block");

            methods.Add(new JavaMethodDeclaration
            {
                Name = nameNode.Text,
                Node = method,
                ReturnTypeText = returnType?.Text,
                Parameters = ExtractParameters(method),
                Annotations = JavaNodeWalker.FindAnnotations(method),
                IsStatic = JavaNodeWalker.IsStatic(method),
                BodyText = body?.Text,
                Line = (int)method.StartPosition.Row + 1
            });
        }

        return methods;
    }

    private static List<JavaConstructorDeclaration> ExtractConstructors(Node typeNode)
    {
        var ctors = new List<JavaConstructorDeclaration>();
        foreach (var ctor in JavaNodeWalker.NamedChildrenOfType(typeNode, "constructor_declaration")
                     .Concat(JavaNodeWalker.OfType(typeNode, "constructor_declaration")))
        {
            ctors.Add(new JavaConstructorDeclaration
            {
                Node = ctor,
                Parameters = ExtractParameters(ctor),
                Annotations = JavaNodeWalker.FindAnnotations(ctor),
                Line = (int)ctor.StartPosition.Row + 1
            });
        }

        return ctors;
    }

    private static List<JavaFieldDeclaration> ExtractFields(Node typeNode)
    {
        var fields = new List<JavaFieldDeclaration>();
        foreach (var field in JavaNodeWalker.OfType(typeNode, "field_declaration"))
        {
            var fieldTypeNode = field.NamedChildren.FirstOrDefault(n =>
                n.Type is "type_identifier" or "scoped_type_identifier" or "generic_type");
            foreach (var declarator in JavaNodeWalker.OfType(field, "variable_declarator"))
            {
                var name = JavaNodeWalker.ChildOfType(declarator, "identifier")?.Text;
                if (name == null)
                    continue;

                fields.Add(new JavaFieldDeclaration
                {
                    Name = name,
                    Node = field,
                    TypeText = fieldTypeNode?.Text,
                    Annotations = JavaNodeWalker.FindAnnotations(field),
                    Line = (int)field.StartPosition.Row + 1
                });
            }
        }

        return fields;
    }

    private static List<JavaParameter> ExtractParameters(Node methodOrCtor)
    {
        var parameters = new List<JavaParameter>();
        var formal = JavaNodeWalker.ChildOfType(methodOrCtor, "formal_parameters");
        if (formal == null)
            return parameters;

        foreach (var param in JavaNodeWalker.NamedChildrenOfType(formal, "formal_parameter")
                     .Concat(JavaNodeWalker.NamedChildrenOfType(formal, "spread_parameter")))
        {
            var typeNode = param.NamedChildren.FirstOrDefault(n =>
                n.Type is "type_identifier" or "scoped_type_identifier" or "generic_type");
            var name = JavaNodeWalker.ChildOfType(param, "identifier")?.Text ?? "arg";
            parameters.Add(new JavaParameter(name, typeNode?.Text ?? "Object", JavaNodeWalker.FindAnnotations(param)));
        }

        return parameters;
    }
}
