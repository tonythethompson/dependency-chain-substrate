using DCS.Core.Parsing;
using DCS.Parser.CSharp;

namespace DCS.Parser.CSharp.Tests;

public sealed class ParserPathExclusionTests
{
    [Fact]
    public void ParseDirectory_excludes_agent_worktree_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dcs-path-excl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, ".claude", "worktrees", "copy"));

        File.WriteAllText(Path.Combine(root, "DcsPathExcl.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.1" />
              </ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(root, "Registrations.cs"), """
            using Microsoft.Extensions.DependencyInjection;
            namespace App;
            public static class Registrations
            {
                public static void Configure(IServiceCollection services) =>
                    services.AddSingleton<IMain, MainImpl>();
            }
            public interface IMain { }
            public class MainImpl : IMain { }
            """);

        File.WriteAllText(Path.Combine(root, ".claude", "worktrees", "copy", "Registrations.cs"), """
            using Microsoft.Extensions.DependencyInjection;
            namespace App;
            public static class Registrations
            {
                public static void Configure(IServiceCollection services) =>
                    services.AddSingleton<ICopy, CopyImpl>();
            }
            public interface ICopy { }
            public class CopyImpl : ICopy { }
            """);

        try
        {
            var parser = new CSharpStaticParser(new CSharpParseOptions
            {
                AllTargetFrameworks = false,
                TargetFramework = "net8.0",
                IncludeTests = false
            });

            var result = parser.ParseDirectory(root);
            var graph = result.SingleGraphOrDefault()
                ?? throw new InvalidOperationException("Expected one context graph.");

            Assert.Contains(graph.Nodes, n => n.DisplayName == "IMain");
            Assert.DoesNotContain(graph.Nodes, n => n.DisplayName == "ICopy");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
