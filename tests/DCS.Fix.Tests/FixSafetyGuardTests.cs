using DCS.Analysis;
using DCS.Fix;
using DCS.Parser.CSharp;

namespace DCS.Fix.Tests;

public sealed class FixSafetyGuardTests
{
    private static LeakedRegistration Leak(string id, string name) =>
        new(id, name, "winui", "avalonia", "App.cs", 1);

    [Fact]
    public void LeakedWorsened_true_when_count_increases()
    {
        var before = new AnalysisResult { Leaked = [Leak("a", "IA")] };
        var after = new AnalysisResult { Leaked = [Leak("a", "IA"), Leak("b", "IB")] };
        Assert.True(FixSafetyGuard.LeakedWorsened(before, after));
    }

    [Fact]
    public void LeakedWorsened_true_when_new_node_id_same_count()
    {
        var before = new AnalysisResult { Leaked = [Leak("a", "IA")] };
        var after = new AnalysisResult { Leaked = [Leak("b", "IB")] };
        Assert.True(FixSafetyGuard.LeakedWorsened(before, after));
    }

    [Fact]
    public void LeakedWorsened_false_when_leaks_shrink()
    {
        var before = new AnalysisResult { Leaked = [Leak("a", "IA"), Leak("b", "IB")] };
        var after = new AnalysisResult { Leaked = [Leak("a", "IA")] };
        Assert.False(FixSafetyGuard.LeakedWorsened(before, after));
    }

    [Fact]
    public void BrokenWorsened_true_when_new_chain_appears()
    {
        var before = new AnalysisResult { BrokenChains = [] };
        var after = new AnalysisResult
        {
            BrokenChains = [new BrokenChain("a", "Consumer", "IDep", "Reg.cs", 1)]
        };
        Assert.True(FixSafetyGuard.BrokenWorsened(before, after));
    }

    [Fact]
    public void Verify_rolls_back_file_when_leaked_worsens()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dcs-leaked-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "Reg.cs");
        const string original = "services.AddSingleton<IA, A>();";
        const string updated = "// removed";
        File.WriteAllText(file, updated);

        var patches = new[] { new FilePatch("Reg.cs", original, updated) };
        var before = new AnalysisResult { Leaked = [] };
        var after = new AnalysisResult { Leaked = [Leak("x", "IB")] };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            FixSafetyGuard.VerifyLeakedNotWorsened(before, after, root, patches));

        Assert.Contains("rolled back", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllText(file));

        TryDeleteDirectory(root);
    }

    [Fact]
    public void VerifyApplyGuards_rolls_back_when_broken_worsens()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dcs-broken-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "Reg.cs");
        const string original = "services.AddSingleton<IA, A>();";
        const string updated = "// removed";
        File.WriteAllText(file, updated);

        var patches = new[] { new FilePatch("Reg.cs", original, updated) };
        var before = new AnalysisResult { BrokenChains = [] };
        var after = new AnalysisResult
        {
            BrokenChains = [new BrokenChain("x", "Consumer", "IDep", "Reg.cs", 1)]
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            FixSafetyGuard.VerifyApplyGuards(before, after, root, patches));

        Assert.Contains("BROKEN", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllText(file));

        TryDeleteDirectory(root);
    }

    [Fact]
    public void Duplicate_apply_passes_leaked_guard_on_fixture()
    {
        var root = CreateDuplicateFixtureForGuard();
        try
        {
            var parser = new CSharpStaticParser(new CSharpParseOptions
            {
                AllTargetFrameworks = false,
                TargetFramework = "net8.0"
            });
            var graph = parser.ParseDirectory(root).SingleGraphOrDefault()
                ?? throw new InvalidOperationException("Expected one context graph.");
            var before = new GraphAnalyzer(graph).Analyze();

            var result = FixEngine.ApplyDuplicateFixes(root, graph, before, forceDirtyTree: true);
            var afterGraph = parser.ParseDirectory(root).SingleGraphOrDefault()!;
            var after = new GraphAnalyzer(afterGraph).Analyze();

            FixSafetyGuard.VerifyApplyGuards(before, after, root, result.Patches);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string CreateDuplicateFixtureForGuard()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dcs-fix-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "WinUI"));
        Directory.CreateDirectory(Path.Combine(root, "Avalonia"));

        File.WriteAllText(Path.Combine(root, "DcsFixTest.csproj"), """
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

        File.WriteAllText(Path.Combine(root, "VoiceCloneConsentCoordinator.cs"), """
            namespace DcsFixTest;
            public interface IVoiceCloneConsentCoordinator { }
            public class VoiceCloneConsentCoordinator : IVoiceCloneConsentCoordinator { }
            """);

        File.WriteAllText(Path.Combine(root, "WinUI", "App.xaml.cs"), """
            using Microsoft.Extensions.DependencyInjection;
            using DcsFixTest;
            namespace DcsFixTest.WinUI;
            public static class WinUiApp {
              public static void Register(IServiceCollection services) {
                services.AddSingleton<IVoiceCloneConsentCoordinator, VoiceCloneConsentCoordinator>();
              }
            }
            """);

        File.WriteAllText(Path.Combine(root, "Avalonia", "App.axaml.cs"), """
            using Microsoft.Extensions.DependencyInjection;
            using DcsFixTest;
            namespace DcsFixTest.Avalonia;
            public static class AvaloniaApp {
              public static void Register(IServiceCollection services) {
                services.AddSingleton<IVoiceCloneConsentCoordinator, VoiceCloneConsentCoordinator>();
              }
            }
            """);

        return root;
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
