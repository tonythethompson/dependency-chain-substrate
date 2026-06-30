using DCS.Parser.CSharp.Semantic;
using Xunit;

namespace DCS.Parser.CSharp.Tests;

public sealed class ProjectTargetScopeDiscoveryTests
{
    [Fact]
    public void DiscoverScopes_commit_mode_ignores_working_tree_csproj_files()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dcs-scope-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "src"));
            File.WriteAllText(Path.Combine(root, "src", "Extra.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
                </Project>
                """);

            var commitCsprojs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["src/App.csproj"] = """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                    </Project>
                    """
            };
            var sourceFiles = new List<(string path, string content)>
            {
                ("src/App.cs", "namespace App; public class Widget { }")
            };

            var scopes = ProjectTargetScopeDiscovery.DiscoverScopes(
                sourceFiles,
                root,
                new SemanticExtractionOptions { IncludeTests = false },
                commitCsprojs);

            Assert.NotEmpty(scopes);
            Assert.All(scopes, s => Assert.DoesNotContain("Extra", s.CsprojPath, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(scopes, s => s.CsprojPath.Contains("App", StringComparison.OrdinalIgnoreCase));
            Assert.All(scopes, s => Assert.Equal("net8.0", s.TargetFramework));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
