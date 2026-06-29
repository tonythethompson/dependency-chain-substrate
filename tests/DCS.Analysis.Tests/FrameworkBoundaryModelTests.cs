using DCS.Analysis;
using DCS.Core.IR;

namespace DCS.Analysis.Tests;

public sealed class FrameworkBoundaryModelTests
{
    [Fact]
    public void Default_model_detects_winui_avalonia_as_different()
    {
        var model = FrameworkBoundaryModel.Default;
        Assert.True(model.AreDifferentFrameworks(["winui"], ["avalonia"]));
        Assert.False(model.AreDifferentFrameworks(["winui"], ["winui"]));
    }

    [Fact]
    public void LoadFromJson_adds_custom_prefix_before_built_in()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "custom-frameworks.json");

        var model = FrameworkBoundaryModel.LoadFromJson(path);
        var tags = FrameworkTagger.InferTags(model, ["Microsoft.Maui.Controls"]);

        Assert.Contains("maui", tags);
    }

    [Fact]
    public void Custom_framework_enables_cross_boundary_leak_detection()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "custom-frameworks.json");
        var model = FrameworkBoundaryModel.LoadFromJson(path);

        var mauiNode = MakeNode("IMauiService", ["maui"]);
        var avaloniaNode = MakeNode("IAvaloniaService", ["avalonia"]);
        var graph = new RegistrationGraph
        {
            Nodes = [mauiNode, avaloniaNode],
            Edges =
            [
                new DependencyEdge
                {
                    Id = DependencyEdge.ComputeId(mauiNode.Id, avaloniaNode.Id),
                    From = mauiNode.Id,
                    To = avaloniaNode.Id
                }
            ]
        };

        var result = new GraphAnalyzer(graph, model).Analyze();
        Assert.Contains(result.Leaked, l => l.DisplayName == "IMauiService");
    }

    [Fact]
    public void LoadFromJson_rejects_built_in_tag_override()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dcs-fw-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "frameworks": [
                { "tag": "winui", "namespace_prefixes": ["Custom.WinUI."] }
              ]
            }
            """);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => FrameworkBoundaryModel.LoadFromJson(path));
            Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadFromJson_rejects_prefix_overlap_with_built_in()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dcs-fw-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """
            {
              "frameworks": [
                { "tag": "custom-ui", "namespace_prefixes": ["Microsoft.UI."] }
              ]
            }
            """);

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => FrameworkBoundaryModel.LoadFromJson(path));
            Assert.Contains("overlaps built-in", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FrameworkTagger_tags_avalonia_using_via_default_model()
    {
        var tags = FrameworkTagger.InferTags(
            FrameworkBoundaryModel.Default,
            ["Avalonia.Controls"],
            TypeRef.FromShortName("IFoo"));

        Assert.Contains("avalonia", tags);
    }

    private static RegistrationNode MakeNode(string shortName, string[] frameworks) =>
        new()
        {
            Id = RegistrationNode.ComputeId(shortName),
            InstanceId = RegistrationNode.ComputeInstanceId(shortName, "Test.cs", 1),
            DisplayName = shortName,
            AbstractToken = TypeRef.FromShortName(shortName),
            FrameworkTags = [.. frameworks]
        };
}
