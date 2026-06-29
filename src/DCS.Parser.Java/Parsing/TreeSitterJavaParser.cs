using TreeSitter;
using TsParser = TreeSitter.Parser;

namespace DCS.Parser.Java.Parsing;

public sealed class TreeSitterJavaParser : IDisposable
{
    private readonly TsParser _parser;

    public TreeSitterJavaParser()
    {
        _parser = new TsParser(new Language("java"));
    }

    public (Tree Tree, Node Root) Parse(string source)
    {
        var tree = _parser.Parse(source) ?? throw new InvalidOperationException("Tree-sitter returned null tree.");
        return (tree, tree.RootNode);
    }

    public void Dispose() => _parser.Dispose();
}
