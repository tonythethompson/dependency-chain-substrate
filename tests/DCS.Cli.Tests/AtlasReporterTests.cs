using DCS.Cli;
using DCS.Core.IR;

namespace DCS.Cli.Tests;

public sealed class AtlasReporterTests
{
    [Fact]
    public void Print_sorts_registrations_by_file_then_line()
    {
        var graph = new RegistrationGraph
        {
            CommitSha = "abc12345",
            Nodes =
            [
                MakeNode("IB", "B.cs", 20),
                MakeNode("IA", "A.cs", 10),
                MakeNode("IC", "B.cs", 5)
            ]
        };

        using var writer = new StringWriter();
        AtlasReporter.Print(graph, writer);
        var output = writer.ToString();

        var indexA = output.IndexOf("IA", StringComparison.Ordinal);
        var indexC = output.IndexOf("IC", StringComparison.Ordinal);
        var indexB = output.IndexOf("IB", StringComparison.Ordinal);

        Assert.True(indexA >= 0);
        Assert.True(indexC > indexA);
        Assert.True(indexB > indexC);
        Assert.Contains("=== DCS Registration Atlas ===", output);
        Assert.Contains("3 registrations", output);
    }

    private static RegistrationNode MakeNode(string name, string file, int line)
    {
        var instanceId = RegistrationNode.ComputeRegistrationInstanceId("atlas-test", file, line, 0, line, 80, 0);
        return new()
        {
            Id = instanceId,
            RegistrationInstanceId = instanceId,
            InstanceId = instanceId,
            DisplayName = name,
            AbstractToken = TypeRef.FromShortName(name),
            SourceLocation = new SourceRef { FilePath = file, Line = line },
            FrameworkTags = ["avalonia"]
        };
    }
}
