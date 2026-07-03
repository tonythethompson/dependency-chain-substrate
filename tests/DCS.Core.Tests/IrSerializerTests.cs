using DCS.Core.Serialization;

namespace DCS.Core.Tests;

public sealed class IrSerializerTests
{
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
