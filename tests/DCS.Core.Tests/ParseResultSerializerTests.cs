using DCS.Core.IR;
using DCS.Core.Parsing;

namespace DCS.Core.Tests;

public sealed class ParseResultSerializerTests
{
    [Fact]
    public void Serialize_then_deserialize_round_trips_context_graphs()
    {
        var result = CSharpParseResultFactory.Wrap(new RegistrationGraph());

        var json = ParseResultSerializer.Serialize(result);
        var roundTripped = ParseResultSerializer.Deserialize(json);

        Assert.NotNull(roundTripped);
        Assert.Single(roundTripped!.ContextGraphs);
        Assert.Equal(result.ContextGraphs[0].ContextId, roundTripped.ContextGraphs[0].ContextId);
    }

    [Fact]
    public void Deserialize_throws_on_malformed_json()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => ParseResultSerializer.Deserialize("not json"));
    }

    [Fact]
    public void Deserialize_returns_default_result_for_empty_object()
    {
        var result = ParseResultSerializer.Deserialize("{}");
        Assert.NotNull(result);
        Assert.Empty(result!.ContextGraphs);
    }

    [Fact]
    public void Deserialize_preserves_diagnostics()
    {
        var result = new ParseResult
        {
            Diagnostics = [new ParseDiagnostic { Pattern = "warn", Description = "something odd" }]
        };

        var json = ParseResultSerializer.Serialize(result);
        var roundTripped = ParseResultSerializer.Deserialize(json);

        Assert.NotNull(roundTripped);
        Assert.Single(roundTripped!.Diagnostics);
        Assert.Equal("something odd", roundTripped.Diagnostics[0].Description);
    }

    [Fact]
    public void CSharpParseResultFactory_Wrap_defaults_module_id_to_star()
    {
        var graph = new RegistrationGraph();
        var result = CSharpParseResultFactory.Wrap(graph);

        Assert.Single(result.ContextGraphs);
        Assert.Equal("*", result.ContextGraphs[0].ModuleId);
        Assert.Same(graph, result.ContextGraphs[0].Graph);
    }

    [Fact]
    public void CSharpParseResultFactory_Wrap_honors_explicit_module_id()
    {
        var graph = new RegistrationGraph();
        var result = CSharpParseResultFactory.Wrap(graph, moduleId: "MyModule");

        Assert.Contains("MyModule", result.ContextGraphs[0].ContextId);
        Assert.Equal("MyModule", result.ContextGraphs[0].ModuleId);
    }
}
