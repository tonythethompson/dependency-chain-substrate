using DCS.Analysis;
using DCS.Core.IR;
using DCS.Fix;
using Xunit;

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

    private static RegistrationNode MakeNode(string name, string file, int line, Confidence confidence) =>
        new()
        {
            Id = RegistrationNode.ComputeId(name),
            InstanceId = RegistrationNode.ComputeInstanceId(name, file, line),
            DisplayName = name,
            AbstractToken = TypeRef.FromShortName(name),
            SourceLocation = new SourceRef { FilePath = file, Line = line },
            ParserConfidence = confidence
        };
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
}

public sealed class FixEngineIntegrationTests
{
    [Fact]
    public void Apply_removes_duplicate_and_analyze_shows_one_fewer()
    {
        var root = CreateDuplicateFixture();
        try
        {
            var graph = new DCS.Parser.CSharp.CSharpStaticParser().ParseDirectory(root);
            var registrationGraph = graph.SingleGraphOrDefault()
                ?? throw new InvalidOperationException("Expected one context graph.");
            var before = new GraphAnalyzer(registrationGraph).Analyze();
            Assert.Single(before.Duplicates);

            FixEngine.ApplyDuplicateFixes(root, registrationGraph, before, forceDirtyTree: true);

            var afterGraph = new DCS.Parser.CSharp.CSharpStaticParser().ParseDirectory(root).SingleGraphOrDefault()!;
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
        Directory.CreateDirectory(Path.Combine(root, "WinUI"));
        Directory.CreateDirectory(Path.Combine(root, "Avalonia"));

        File.WriteAllText(Path.Combine(root, "WinUI", "App.xaml.cs"), """
            using Microsoft.Extensions.DependencyInjection;
            public static class WinUiApp {
              public static void Register(IServiceCollection services) {
                services.AddSingleton<IVoiceCloneConsentCoordinator, VoiceCloneConsentCoordinator>();
              }
            }
            """);

        File.WriteAllText(Path.Combine(root, "Avalonia", "App.axaml.cs"), """
            using Microsoft.Extensions.DependencyInjection;
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
