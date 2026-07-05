using DCS.Analysis;
using DCS.Core.IR;
using DCS.Fix;
using DCS.Parser.CSharp;

namespace DCS.Fix.Tests;

public sealed class DuplicateFixPlannerTests
{
    [Fact]
    public void SelectRemovalTarget_prefers_lower_confidence()
    {
        var explicitNode = MakeNode("IFoo", "Explicit.cs", 1, Confidence.Explicit);
        var degraded = MakeNode("IFoo", "Explicit.cs", 2, Confidence.Degraded);

        var target = DuplicateFixPlanner.SelectRemovalTarget([explicitNode, degraded]);
        Assert.Equal(degraded.InstanceId, target.InstanceId);
    }

    [Fact]
    public void SelectRemovalTarget_prefers_shorter_path_when_confidence_ties()
    {
        var shell = MakeNode("IFoo", "App.xaml.cs", 1, Confidence.Explicit);
        var root = MakeNode("IFoo", "Composition/ServiceRegistration.cs", 5, Confidence.Explicit);

        var target = DuplicateFixPlanner.SelectRemovalTarget([shell, root]);
        Assert.Equal(shell.InstanceId, target.InstanceId);
    }

    private static RegistrationNode MakeNode(string name, string file, int line, Confidence confidence)
    {
        var instanceId = RegistrationNode.ComputeRegistrationInstanceId("fix-test", file, line, 0, line, 80, 0);
        return new RegistrationNode
        {
            Id = instanceId,
            RegistrationInstanceId = instanceId,
            InstanceId = instanceId,
            DisplayName = name,
            AbstractToken = TypeRef.FromShortName(name),
            SourceLocation = new SourceRef { FilePath = file, Line = line },
            ParserConfidence = confidence
        };
    }
}

public sealed class RegistrationStatementRemoverTests
{
    private const string Source = """
        using Microsoft.Extensions.DependencyInjection;

        public static class AppRegistrations
        {
            public static void Configure(IServiceCollection services)
            {
                services.AddSingleton<IVoiceCloneConsentCoordinator, VoiceCloneConsentCoordinator>();
                services.AddSingleton<IOther, Other>();
            }
        }
        """;

    [Fact]
    public void Removes_registration_line_by_line_number_and_token()
    {
        var updated = RegistrationStatementRemover.TryRemove(Source, line: 7, "IVoiceCloneConsentCoordinator");
        Assert.NotNull(updated);
        Assert.DoesNotContain("VoiceCloneConsentCoordinator", updated);
        Assert.Contains("IOther", updated);
    }

    [Fact]
    public void Removes_TryAddSingleton_with_factory_lambda()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.TryAddSingleton<DeviceAffinitySettings>(_ => DeviceAffinitySettings.Load());
                }
            }

            public sealed class DeviceAffinitySettings
            {
                public static DeviceAffinitySettings Load() => new();
            }
            """;

        var updated = RegistrationStatementRemover.TryRemove(source, line: 7, "DeviceAffinitySettings");
        Assert.NotNull(updated);
        Assert.DoesNotContain("TryAddSingleton<DeviceAffinitySettings>", updated);
        Assert.Contains("public static void Configure", updated);
    }

    [Fact]
    public void TryRemoveMany_removes_multiple_lines_without_reformatting_unrelated_code()
    {
        const string source = """
            using Microsoft.Extensions.DependencyInjection;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddSingleton<IFirst, First>();
                    services.AddSingleton<ISecond, Second>();
                }
            }
            """;

        var updated = RegistrationStatementRemover.TryRemoveMany(
            source,
            [
                new RegistrationRemovalRequest(7, "IFirst"),
                new RegistrationRemovalRequest(8, "ISecond")
            ]);

        Assert.NotNull(updated);
        Assert.DoesNotContain("IFirst", updated);
        Assert.DoesNotContain("ISecond", updated);
        Assert.Equal(source.Split('\n').Length - 2, updated.Split('\n').Length);
    }

    [Fact]
    public void TryRemoveMany_preserves_tight_preview_diff_for_single_removal()
    {
        var updated = RegistrationStatementRemover.TryRemove(Source, line: 7, "IVoiceCloneConsentCoordinator");
        Assert.NotNull(updated);

        var diff = UnifiedDiffFormatter.Format("Reg.cs", Source, updated);
        Assert.Contains("@@ -7,1 +7,0 @@", diff);
        Assert.DoesNotContain("StemTempCleanup", diff);
    }

    [Fact]
    public void Removes_lambda_host_AddSingleton_registration()
    {
        const string source = """
            using Amazon.SQS;
            using Microsoft.Extensions.DependencyInjection;

            public static class LambdaHost
            {
                public static ServiceProvider Build()
                {
                    var services = new ServiceCollection();
                    services.AddSingleton<IAmazonSQS, AmazonSQSClient>();
                    return services.BuildServiceProvider();
                }
            }
            """;

        var updated = RegistrationStatementRemover.TryRemove(source, line: 9, "IAmazonSQS");
        Assert.NotNull(updated);
        Assert.DoesNotContain("IAmazonSQS", updated);
        Assert.Contains("ServiceCollection", updated);
    }
}

public sealed class FixEngineIntegrationTests
{
    [Fact]
    public void Apply_removes_duplicate_and_analyze_shows_one_fewer()
    {
        var root = CreateDuplicateFixture();
        try
        {
            var parser = new CSharpStaticParser(new CSharpParseOptions
            {
                AllTargetFrameworks = false,
                TargetFramework = "net8.0"
            });
            var graph = parser.ParseDirectory(root);
            var registrationGraph = graph.SingleGraphOrDefault()
                ?? throw new InvalidOperationException("Expected one context graph.");
            var before = new GraphAnalyzer(registrationGraph).Analyze();
            Assert.Single(before.Duplicates);

            FixEngine.ApplyDuplicateFixes(root, registrationGraph, before, forceDirtyTree: true);

            var afterGraph = new CSharpStaticParser(new CSharpParseOptions
            {
                AllTargetFrameworks = false,
                TargetFramework = "net8.0"
            }).ParseDirectory(root).SingleGraphOrDefault()!;
            var after = new GraphAnalyzer(afterGraph).Analyze();
            Assert.Empty(after.Duplicates);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string CreateDuplicateFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dcs-fix-{Guid.NewGuid():N}");
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
            // Best-effort cleanup on Windows file locks.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
