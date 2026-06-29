using DCS.Core.IR;
using DCS.Core.Serialization;
using System.Text.Json;

namespace DCS.Core.Parsing;

public static class ParseResultSerializer
{
    public static string Serialize(ParseResult result) =>
        JsonSerializer.Serialize(result, IrSerializer.Options);

    public static ParseResult? Deserialize(string json) =>
        JsonSerializer.Deserialize<ParseResult>(json, IrSerializer.Options);
}

public static class CSharpParseResultFactory
{
    public static ParseResult Wrap(RegistrationGraph graph, string moduleId = "*")
    {
        var contextId = ContextGraph.BuildContextId(moduleId, SourceSetKind.Main, "*");
        return new ParseResult
        {
            ContextGraphs =
            [
                new ContextGraph
                {
                    ContextId = contextId,
                    EntryRoot = TypeRef.FromQualifiedName("*"),
                    ModuleId = moduleId,
                    SourceSet = SourceSetKind.Main,
                    Graph = graph
                }
            ]
        };
    }
}
