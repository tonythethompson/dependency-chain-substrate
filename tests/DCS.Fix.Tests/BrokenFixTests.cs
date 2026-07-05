using DCS.Analysis;
using DCS.Core.IR;
using DCS.Fix;
using DCS.Parser.CSharp;

namespace DCS.Fix.Tests;

public sealed class BrokenFixPlannerTests
{
    [Fact]
    public void Plan_skips_factory_with_service_locator()
    {
        var root = CreateBrokenFixture(includeComplexDep: true);
        try
        {
            var graph = ParseGraph(root);
            var analysis = new GraphAnalyzer(graph).Analyze();
            var proposals = BrokenFixPlanner.Plan(root, graph, analysis);
            Assert.Empty(proposals);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Plan_skips_parameterless_degraded_factory_targets()
    {
        var root = CreateBrokenFixture(includeComplexDep: false);
        try
        {
            var graph = ParseGraph(root);
            var analysis = new GraphAnalyzer(graph).Analyze();
            Assert.Empty(analysis.BrokenChains);
            Assert.Empty(BrokenFixPlanner.Plan(root, graph, analysis));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Plan_proposes_simple_factory_conversion()
    {
        var target = new RegistrationNode
        {
            Id = "dep",
            DisplayName = "IDepService",
            AbstractToken = TypeRef.FromShortName("IDepService"),
            ConcreteImpl = TypeRef.FromShortName("DepServiceImpl"),
            ServiceType = ServiceTypeIdentity.FromSyntactic("IDepService"),
            ParserConfidence = Confidence.BlindSpot,
            Annotations = new Dictionary<string, string> { ["pattern"] = "factory_lambda_shallow" },
            SourceLocation = new SourceRef { FilePath = "Registrations.cs", Line = 7 }
        };
        var consumer = new RegistrationNode
        {
            Id = "consumer",
            DisplayName = "IConsumer",
            AbstractToken = TypeRef.FromShortName("IConsumer"),
            ServiceType = ServiceTypeIdentity.FromSyntactic("IConsumer"),
            ParserConfidence = Confidence.BlindSpot,
            SourceLocation = new SourceRef { FilePath = "Registrations.cs", Line = 8 }
        };

        var root = CreateBrokenFixture(includeComplexDep: false);
        try
        {
            File.WriteAllText(Path.Combine(root, "Registrations.cs"), """
                using Microsoft.Extensions.DependencyInjection;
                namespace DcsBrokenFixTest;
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.TryAddSingleton<IDepService>(sp => new DepServiceImpl());
                        services.TryAddSingleton<IConsumer>(sp =>
                            new ConsumerImpl(sp.GetRequiredService<IDepService>()));
                    }
                }
                """);

            var graph = new RegistrationGraph
            {
                Nodes = [target, consumer],
                Edges =
                [
                    new DependencyEdge
                    {
                        Id = "e1",
                        From = consumer.Id,
                        To = target.Id,
                        InjectionMechanism = Mechanism.FactoryParameter
                    }
                ]
            };
            var analysis = new AnalysisResult
            {
                BrokenChains =
                [
                    new BrokenChain(consumer.Id, consumer.DisplayName, target.DisplayName,
                        consumer.SourceLocation?.FilePath, consumer.SourceLocation?.Line)
                ]
            };

            var proposals = BrokenFixPlanner.Plan(root, graph, analysis);
            Assert.Single(proposals);
            Assert.Equal("IDepService", proposals[0].TargetDisplayName);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Apply_converts_eligible_factory_via_synthetic_broken_chain()
    {
        var root = CreateBrokenFixture(includeComplexDep: false);
        try
        {
            File.WriteAllText(Path.Combine(root, "Registrations.cs"), """
                using Microsoft.Extensions.DependencyInjection;
                namespace DcsBrokenFixTest;
                public static class Registrations
                {
                    public static void Configure(IServiceCollection services)
                    {
                        services.TryAddSingleton<IDepService>(sp => new DepServiceImpl());
                        services.TryAddSingleton<IConsumer>(sp =>
                            new ConsumerImpl(sp.GetRequiredService<IDepService>()));
                    }
                }
                """);

            var dep = new RegistrationNode
            {
                Id = "dep",
                DisplayName = "IDepService",
                AbstractToken = TypeRef.FromShortName("IDepService"),
                ConcreteImpl = TypeRef.FromShortName("DepServiceImpl"),
                ServiceType = ServiceTypeIdentity.FromSyntactic("IDepService"),
                ParserConfidence = Confidence.BlindSpot,
                Annotations = new Dictionary<string, string> { ["pattern"] = "factory_lambda_shallow" },
                SourceLocation = new SourceRef { FilePath = "Registrations.cs", Line = 7 }
            };
            var consumer = new RegistrationNode
            {
                Id = "consumer",
                DisplayName = "IConsumer",
                AbstractToken = TypeRef.FromShortName("IConsumer"),
                ServiceType = ServiceTypeIdentity.FromSyntactic("IConsumer"),
                ParserConfidence = Confidence.BlindSpot,
                SourceLocation = new SourceRef { FilePath = "Registrations.cs", Line = 8 }
            };
            var graph = new RegistrationGraph
            {
                Nodes = [dep, consumer],
                Edges =
                [
                    new DependencyEdge
                    {
                        Id = "e1",
                        From = consumer.Id,
                        To = dep.Id,
                        InjectionMechanism = Mechanism.FactoryParameter
                    }
                ]
            };
            var before = new AnalysisResult
            {
                BrokenChains =
                [
                    new BrokenChain(consumer.Id, consumer.DisplayName, dep.DisplayName,
                        consumer.SourceLocation?.FilePath, consumer.SourceLocation?.Line)
                ]
            };

            var result = FixEngine.ApplyBrokenFixes(root, graph, before, forceDirtyTree: true);
            Assert.Single(result.Proposals);
            Assert.Contains("TryAddSingleton<IDepService, DepServiceImpl>", File.ReadAllText(Path.Combine(root, "Registrations.cs")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Plan_preserves_original_registration_receiver_for_builder_services()
    {
        var root = CreateBrokenFixture(includeComplexDep: false, useBuilderServicesReceiver: true);
        try
        {
            var dep = new RegistrationNode
            {
                Id = "dep",
                DisplayName = "IDepService",
                AbstractToken = TypeRef.FromShortName("IDepService"),
                ConcreteImpl = TypeRef.FromShortName("DepServiceImpl"),
                ServiceType = ServiceTypeIdentity.FromSyntactic("IDepService"),
                ParserConfidence = Confidence.BlindSpot,
                Annotations = new Dictionary<string, string> { ["pattern"] = "factory_lambda_shallow" },
                SourceLocation = new SourceRef { FilePath = "Registrations.cs", Line = 14 }
            };
            var consumer = new RegistrationNode
            {
                Id = "consumer",
                DisplayName = "IConsumer",
                AbstractToken = TypeRef.FromShortName("IConsumer"),
                ServiceType = ServiceTypeIdentity.FromSyntactic("IConsumer"),
                ParserConfidence = Confidence.BlindSpot,
                SourceLocation = new SourceRef { FilePath = "Registrations.cs", Line = 15 }
            };
            var graph = new RegistrationGraph
            {
                Nodes = [dep, consumer],
                Edges =
                [
                    new DependencyEdge
                    {
                        Id = "e1",
                        From = consumer.Id,
                        To = dep.Id,
                        InjectionMechanism = Mechanism.FactoryParameter
                    }
                ]
            };
            var analysis = new AnalysisResult
            {
                BrokenChains =
                [
                    new BrokenChain(consumer.Id, consumer.DisplayName, dep.DisplayName,
                        consumer.SourceLocation?.FilePath, consumer.SourceLocation?.Line)
                ]
            };

            var proposals = BrokenFixPlanner.Plan(root, graph, analysis);
            var proposal = Assert.Single(proposals);
            Assert.StartsWith("builder.Services.TryAddSingleton<", proposal.ReplacementStatement, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Apply_preserves_original_registration_receiver_for_builder_services()
    {
        var root = CreateBrokenFixture(includeComplexDep: false, useBuilderServicesReceiver: true);
        try
        {
            var dep = new RegistrationNode
            {
                Id = "dep",
                DisplayName = "IDepService",
                AbstractToken = TypeRef.FromShortName("IDepService"),
                ConcreteImpl = TypeRef.FromShortName("DepServiceImpl"),
                ServiceType = ServiceTypeIdentity.FromSyntactic("IDepService"),
                ParserConfidence = Confidence.BlindSpot,
                Annotations = new Dictionary<string, string> { ["pattern"] = "factory_lambda_shallow" },
                SourceLocation = new SourceRef { FilePath = "Registrations.cs", Line = 14 }
            };
            var consumer = new RegistrationNode
            {
                Id = "consumer",
                DisplayName = "IConsumer",
                AbstractToken = TypeRef.FromShortName("IConsumer"),
                ServiceType = ServiceTypeIdentity.FromSyntactic("IConsumer"),
                ParserConfidence = Confidence.BlindSpot,
                SourceLocation = new SourceRef { FilePath = "Registrations.cs", Line = 15 }
            };
            var graph = new RegistrationGraph
            {
                Nodes = [dep, consumer],
                Edges =
                [
                    new DependencyEdge
                    {
                        Id = "e1",
                        From = consumer.Id,
                        To = dep.Id,
                        InjectionMechanism = Mechanism.FactoryParameter
                    }
                ]
            };
            var before = new AnalysisResult
            {
                BrokenChains =
                [
                    new BrokenChain(consumer.Id, consumer.DisplayName, dep.DisplayName,
                        consumer.SourceLocation?.FilePath, consumer.SourceLocation?.Line)
                ]
            };

            var result = FixEngine.ApplyBrokenFixes(root, graph, before, forceDirtyTree: true);
            Assert.Single(result.Proposals);
            var registrations = File.ReadAllText(Path.Combine(root, "Registrations.cs"));
            Assert.Contains("builder.Services.TryAddSingleton<", registrations);
            Assert.DoesNotContain("services.TryAddSingleton<", registrations);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Apply_throws_when_working_tree_dirty()
    {
        var root = CreateBrokenFixture(includeComplexDep: false);
        try
        {
            var graph = ParseGraph(root);
            var analysis = new GraphAnalyzer(graph).Analyze();
            File.WriteAllText(Path.Combine(root, "dirty.txt"), "x");

            Assert.Throws<InvalidOperationException>(() =>
                FixEngine.ApplyBrokenFixes(root, graph, analysis));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static RegistrationGraph ParseGraph(string root)
    {
        var parser = new CSharpStaticParser(new CSharpParseOptions
        {
            AllTargetFrameworks = false,
            TargetFramework = "net8.0"
        });
        return parser.ParseDirectory(root).SingleGraphOrDefault()
            ?? throw new InvalidOperationException("Expected one context graph.");
    }

    private static string CreateBrokenFixture(bool includeComplexDep, bool useBuilderServicesReceiver = false)
    {
        var root = Path.Combine(Path.GetTempPath(), $"dcs-broken-fix-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        File.WriteAllText(Path.Combine(root, "DcsBrokenFixTest.csproj"), """
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

        File.WriteAllText(Path.Combine(root, "Services.cs"), """
            namespace DcsBrokenFixTest;

            public interface IDepService { }
            public sealed class DepServiceImpl : IDepService { }

            public interface IConsumer { }
            public sealed class ConsumerImpl : IConsumer
            {
                public ConsumerImpl(IDepService dep) { }
            }
            """);

        var depRegistration = includeComplexDep
            ? """
              services.TryAddSingleton<IDepService>(sp =>
                  new DepServiceImpl(sp.GetRequiredService<IServiceProvider>()));
              """
            : """
              services.TryAddSingleton<IDepService>(sp => new DepServiceImpl());
              """;

        var registrations = useBuilderServicesReceiver
            ? $$"""
            using Microsoft.Extensions.DependencyInjection;

            namespace DcsBrokenFixTest;

            public sealed class AppBuilder
            {
                public IServiceCollection Services { get; } = new ServiceCollection();
            }

            public static class Registrations
            {
                public static void Configure(AppBuilder builder)
                {
                    {{depRegistration.Replace("services.", "builder.Services.")}}
                    builder.Services.TryAddSingleton<IConsumer>(sp =>
                        new ConsumerImpl(sp.GetRequiredService<IDepService>()));
                }
            }
            """
            : $$"""
            using Microsoft.Extensions.DependencyInjection;

            namespace DcsBrokenFixTest;

            public static class Registrations
            {
                public static void Configure(IServiceCollection services)
                {
                    {{depRegistration}}
                    services.TryAddSingleton<IConsumer>(sp =>
                        new ConsumerImpl(sp.GetRequiredService<IDepService>()));
                }
            }
            """;

        File.WriteAllText(Path.Combine(root, "Registrations.cs"), registrations);

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
