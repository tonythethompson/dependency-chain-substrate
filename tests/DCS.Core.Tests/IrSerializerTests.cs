using DCS.Core.IR;
using DCS.Core.Serialization;

namespace DCS.Core.Tests;

public sealed class IrSerializerTests
{
    [Fact]
    public void Serialize_then_deserialize_round_trips_graph_with_node()
    {
        var node = new RegistrationNode
        {
            Id = "abc123",
            DisplayName = "IFoo -> Foo",
            AbstractToken = TypeRef.FromQualifiedName("App.IFoo")
        };
        var graph = new RegistrationGraph { Nodes = [node] };

        var json = IrSerializer.Serialize(graph);
        var roundTripped = IrSerializer.Deserialize(json);

        Assert.NotNull(roundTripped);
        Assert.Single(roundTripped!.Nodes);
        Assert.Equal("abc123", roundTripped.Nodes[0].Id);
        Assert.Equal("App.IFoo", roundTripped.Nodes[0].AbstractToken.FullyQualifiedName);
    }

    [Fact]
    public void Serialize_uses_snake_case_property_names()
    {
        var graph = new RegistrationGraph { SchemaVersion = "1.2.0" };
        var json = IrSerializer.Serialize(graph);
        Assert.Contains("\"schema_version\"", json);
    }

    [Fact]
    public void Deserialize_throws_on_malformed_json()
    {
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => IrSerializer.Deserialize("{not valid json"));
    }

    [Fact]
    public void Deserialize_returns_default_graph_for_empty_object()
    {
        var graph = IrSerializer.Deserialize("{}");
        Assert.NotNull(graph);
        Assert.Empty(graph!.Nodes);
    }

    [Fact]
    public void Deserialize_ignores_missing_schema_version_field()
    {
        const string json = """
            {
              "nodes": [],
              "edges": []
            }
            """;

        var graph = IrSerializer.Deserialize(json);
        Assert.NotNull(graph);
    }

    [Fact]
    public void Deserialize_ignores_blank_schema_version()
    {
        const string json = """
            {
              "schema_version": "",
              "nodes": [],
              "edges": []
            }
            """;

        var graph = IrSerializer.Deserialize(json);
        Assert.NotNull(graph);
    }

    [Fact]
    public void Deserialize_throws_when_schema_version_major_is_not_numeric()
    {
        const string json = """
            {
              "schema_version": "abc.0.0",
              "nodes": [],
              "edges": []
            }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => IrSerializer.Deserialize(json));
        Assert.Contains("Unsupported IR schema_version", ex.Message);
    }

    [Fact]
    public async Task WriteToFileAsync_writes_readable_json_file()
    {
        var graph = new RegistrationGraph();
        var path = Path.Combine(Path.GetTempPath(), $"dcs-ir-{Guid.NewGuid():N}.json");

        try
        {
            await IrSerializer.WriteToFileAsync(graph, path);
            var content = await File.ReadAllTextAsync(path);
            var reloaded = IrSerializer.Deserialize(content);
            Assert.NotNull(reloaded);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void Deserialize_rejects_future_major_schema_version()
    {
        const string json = """
            {
              "schema_version": "2.0.0",
              "parser_version": "0.1.0",
              "nodes": [],
              "edges": []
            }
            """;

        var ex = Assert.Throws<InvalidOperationException>(() => IrSerializer.Deserialize(json));
        Assert.Contains("Unsupported IR schema_version", ex.Message);
    }

    [Fact]
    public void Deserialize_accepts_current_major_schema_version()
    {
        const string json = """
            {
              "schema_version": "1.2.0",
              "parser_version": "0.1.0",
              "nodes": [],
              "edges": []
            }
            """;

        var graph = IrSerializer.Deserialize(json);
        Assert.NotNull(graph);
        Assert.Equal("1.2.0", graph!.SchemaVersion);
    }
}
