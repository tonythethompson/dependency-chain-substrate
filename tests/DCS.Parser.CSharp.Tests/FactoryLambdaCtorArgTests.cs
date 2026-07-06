using DCS.Core.IR;
using DCS.Parser.CSharp;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DCS.Parser.CSharp.Tests;

public sealed class FactoryLambdaCtorArgTests
{
    private static (List<RegistrationNode> nodes, List<BlindSpotReport> spots) Parse(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: "Test.cs");
        var root = tree.GetCompilationUnitRoot();
        var usings = root.Usings
            .Select(u => u.Name?.ToString() ?? string.Empty)
            .Where(u => u.Length > 0)
            .ToList();
        var visitor = new RegistrationPatternVisitor("Test.cs", usings);
        visitor.Visit(root);
        return (visitor.Registrations.ToList(), visitor.BlindSpots.ToList());
    }

    [Fact]
    public void Factory_lambda_traces_only_GetRequiredService_in_service_keys()
    {
        var (nodes, _) = Parse("""
            services.AddSingleton<IFoo>(sp => new FooImpl(
                sp.GetService<IBar>(),
                sp.GetRequiredService<IBaz>()));
            """);
        Assert.Single(nodes);
        var keys = nodes[0].Annotations.GetValueOrDefault("factory_lambda_service_keys");
        Assert.NotNull(keys);
        Assert.DoesNotContain("IBar", keys, StringComparison.Ordinal);
        Assert.Contains("IBaz", keys, StringComparison.Ordinal);
    }

    [Fact]
    public void Block_factory_lambda_traces_GetRequiredService_in_body()
    {
        var (nodes, _) = Parse("""
            services.AddSingleton<ITensorRtRtxProviderBootstrap>(sp =>
            {
                var settings = sp.GetRequiredService<IStudioSettingsService>();
                var paths = sp.GetRequiredService<TrackdubStoragePaths>();
                return TensorRtRtxProviderBootstrapFactory.Create(
                    _ => default,
                    _ => default,
                    (_, _) => default);
            });
            public interface ITensorRtRtxProviderBootstrap { }
            public interface IStudioSettingsService { }
            public sealed class TrackdubStoragePaths { }
            static class TensorRtRtxProviderBootstrapFactory
            {
                public static ITensorRtRtxProviderBootstrap Create(
                    Func<CancellationToken, Task<string?>> a,
                    Func<CancellationToken, ValueTask<string?>> b,
                    Func<bool, CancellationToken, Task<object>> c) => null!;
            }
            """);
        Assert.Single(nodes);
        var keys = nodes[0].Annotations.GetValueOrDefault("factory_lambda_service_keys");
        Assert.NotNull(keys);
        Assert.Contains("IStudioSettingsService", keys, StringComparison.Ordinal);
        Assert.Contains("TrackdubStoragePaths", keys, StringComparison.Ordinal);
    }

    [Fact]
    public void Parameterless_factory_lambda_is_degraded_not_blind_spot()
    {
        var (nodes, _) = Parse("""
            services.AddSingleton<IAmazonSQS>(_ => new AmazonSQSClient());
            """);
        Assert.Single(nodes);
        Assert.Equal(Confidence.Degraded, nodes[0].ParserConfidence);
    }
}
