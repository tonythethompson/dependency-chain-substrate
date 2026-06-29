using TreeSitter;

namespace DCS.Parser.Java.Parsing;

public static class JavaNodeWalker
{
    public static IEnumerable<Node> Descendants(Node root)
    {
        yield return root;
        foreach (var child in root.Children)
        {
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    public static IEnumerable<Node> OfType(Node root, string type) =>
        Descendants(root).Where(n => n.Type == type);

    public static Node? ChildOfType(Node node, string type) =>
        node.Children.FirstOrDefault(c => c.Type == type);

    public static IEnumerable<Node> NamedChildrenOfType(Node node, string type) =>
        node.NamedChildren.Where(c => c.Type == type);

    public static string? FindPackage(Node root)
    {
        var pkg = OfType(root, "package_declaration").FirstOrDefault();
        if (pkg == null)
            return null;

        var nameNode = Descendants(pkg).FirstOrDefault(n => n.Type is "scoped_identifier" or "identifier");
        return nameNode?.Text;
    }

    public static List<string> FindImports(Node root) =>
        OfType(root, "import_declaration")
            .Select(ExtractImport)
            .Where(i => i.Length > 0)
            .ToList();

    private static string ExtractImport(Node importDecl)
    {
        var text = importDecl.Text.Trim();
        if (text.StartsWith("import static ", StringComparison.Ordinal))
            text = text["import static ".Length..];
        else if (text.StartsWith("import ", StringComparison.Ordinal))
            text = text["import ".Length..];

        text = text.TrimEnd(';').Trim();
        if (text.EndsWith(".*", StringComparison.Ordinal))
            return text;

        return text;
    }

    public static List<JavaAnnotation> FindAnnotations(Node node)
    {
        var results = new List<JavaAnnotation>();
        foreach (var child in node.Children)
        {
            if (child.Type is "marker_annotation" or "annotation")
                results.Add(ParseAnnotation(child));
            else if (child.Type == "modifiers")
                results.AddRange(child.Children.Where(c => c.Type is "marker_annotation" or "annotation").Select(ParseAnnotation));
        }

        return results;
    }

    public static JavaAnnotation ParseAnnotation(Node annotationNode)
    {
        var name = ExtractAnnotationName(annotationNode);
        var args = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (annotationNode.Type == "annotation")
        {
            foreach (var pair in OfType(annotationNode, "element_value_pair"))
            {
                var key = ChildOfType(pair, "identifier")?.Text ?? "value";
                var valueNode = ChildOfType(pair, "element_value") ?? pair.NamedChildren.LastOrDefault();
                if (valueNode != null)
                    args[key] = ExtractElementValue(valueNode);
            }

            if (args.Count == 0)
            {
                var single = OfType(annotationNode, "element_value").FirstOrDefault();
                if (single != null)
                    args["value"] = ExtractElementValue(single);
                else
                {
                    var literal = OfType(annotationNode, "string_literal").FirstOrDefault();
                    if (literal != null)
                        args["value"] = NormalizeAnnotationValue(literal.Text);
                }
            }
        }

        return new JavaAnnotation(name, args);
    }

    private static string ExtractAnnotationName(Node annotationNode)
    {
        foreach (var n in Descendants(annotationNode))
        {
            if (n.Type is "identifier" or "type_identifier")
                return n.Text.TrimStart('@');

            if (n.Type == "scoped_identifier")
                return n.Text.TrimStart('@').Split('.')[^1];
        }

        var text = annotationNode.Text.TrimStart('@');
        var paren = text.IndexOf('(');
        if (paren >= 0)
            text = text[..paren];
        return text.Contains('.') ? text.Split('.')[^1] : text;
    }

    private static string NormalizeAnnotationValue(string raw)
    {
        var t = raw.Trim();
        if (t.StartsWith('"') && t.EndsWith('"'))
            return t[1..^1];
        return t;
    }

    private static string ExtractElementValue(Node valueNode)
    {
        var literal = OfType(valueNode, "string_literal").FirstOrDefault();
        if (literal != null)
            return NormalizeAnnotationValue(literal.Text);

        return NormalizeAnnotationValue(valueNode.Text);
    }

    public static bool HasModifier(Node typeNode, string modifier) =>
        typeNode.Children.Any(c => c.Type == "modifiers" && c.Text.Contains(modifier, StringComparison.Ordinal));

    public static bool IsStatic(Node memberNode)
    {
        foreach (var child in memberNode.Children)
        {
            if (child.Type == "modifiers" && child.Text.Contains("static", StringComparison.Ordinal))
                return true;
            if (child.Type == "static")
                return true;
        }

        return false;
    }

    public static string? GetTypeIdentifier(Node typeNode)
    {
        foreach (var child in typeNode.NamedChildren)
        {
            if (child.Type is "type_identifier" or "identifier")
                return child.Text;
            if (child.Type is "generic_type" or "scoped_type_identifier")
                return child.Text;
        }

        return Descendants(typeNode)
            .FirstOrDefault(n => n.Type is "type_identifier" or "scoped_type_identifier" or "identifier")
            ?.Text;
    }

    public static List<string> GetExtendsAndImplements(Node typeNode)
    {
        var result = new List<string>();
        foreach (var clause in OfType(typeNode, "superclass"))
            if (GetTypeIdentifier(clause) is { } ext)
                result.Add(ext);

        foreach (var clause in OfType(typeNode, "super_interfaces")
                     .Concat(OfType(typeNode, "extends_interfaces"))
                     .Concat(OfType(typeNode, "interface_type_list")))
        {
            foreach (var type in Descendants(clause).Where(n => n.Type is "type_identifier" or "scoped_type_identifier" or "generic_type"))
                result.Add(type.Text);
        }

        return result;
    }
}

public sealed record JavaAnnotation(string SimpleName, IReadOnlyDictionary<string, string> Arguments)
{
    public string ShortName => SimpleName.Contains('.') ? SimpleName[(SimpleName.LastIndexOf('.') + 1)..] : SimpleName;

    public bool Is(string simpleName) =>
        string.Equals(ShortName, simpleName, StringComparison.Ordinal);
}
