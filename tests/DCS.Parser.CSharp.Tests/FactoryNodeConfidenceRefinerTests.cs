using DCS.Core.IR;
using DCS.Parser.CSharp;
using Xunit;

namespace DCS.Parser.CSharp.Tests;

public sealed class FactoryNodeConfidenceRefinerTests
{
    [Fact]
    public void Upgrades_factory_when_all_service_targets_are_non_blind_spot()
    {
        var provider = Node("provider", "IProvider", Confidence.Explicit);
        var consumer = Node("consumer", "IConsumer", Confidence.BlindSpot, "factory_lambda_shallow",
            keys: "provider-key");
        consumer.Annotations["factory_lambda_service_keys"] =
            provider.ServiceType!.CanonicalKey;

        var nodes = new List<RegistrationNode> { provider, consumer };
        var edges = new List<DependencyEdge>
        {
            new()
            {
                Id = "e1",
                From = consumer.Id,
                To = provider.Id,
                InjectionMechanism = Mechanism.FactoryParameter
            }
        };

        var refined = FactoryNodeConfidenceRefiner.Refine(nodes, edges, []);
        Assert.Equal(Confidence.Degraded, refined.Single(n => n.Id == "consumer").ParserConfidence);
    }

    [Fact]
    public void Leaves_factory_blind_when_service_target_unresolved()
    {
        var consumer = Node("consumer", "IConsumer", Confidence.BlindSpot, "factory_lambda_shallow",
            keys: "missing-key");
        consumer.Annotations["factory_lambda_service_keys"] = "missing-key";

        var refined = FactoryNodeConfidenceRefiner.Refine([consumer], [], [
            new UnresolvedInjection
            {
                Id = "u1",
                FromRegistrationId = consumer.Id,
                DeclaredType = TypeRef.FromShortName("Missing"),
                Reason = "no_matching_registration"
            }
        ]);

        Assert.Equal(Confidence.BlindSpot, refined[0].ParserConfidence);
    }

    [Fact]
    public void Ignores_constructor_no_matching_when_upgrading_factory()
    {
        var provider = Node("provider", "IProvider", Confidence.Degraded);
        var consumer = Node("consumer", "IConsumer", Confidence.BlindSpot, "factory_lambda_shallow",
            keys: "provider-key");
        consumer.Annotations["factory_lambda_service_keys"] = provider.ServiceType!.CanonicalKey;
        consumer = consumer with { ConcreteImpl = TypeRef.FromShortName("ConsumerImpl") };

        var nodes = new List<RegistrationNode> { provider, consumer };
        var edges = new List<DependencyEdge>
        {
            new()
            {
                Id = "e1",
                From = consumer.Id,
                To = provider.Id,
                InjectionMechanism = Mechanism.FactoryParameter
            }
        };

        var unresolved = new List<UnresolvedInjection>
        {
            new()
            {
                Id = "u1",
                FromRegistrationId = consumer.Id,
                DeclaredType = TypeRef.FromShortName("ApplicationLogLevel"),
                ParameterName = "ApplicationLogLevel",
                InjectionMechanism = Mechanism.Constructor,
                Reason = "no_matching_registration"
            }
        };

        var refined = FactoryNodeConfidenceRefiner.Refine(nodes, edges, unresolved);
        Assert.Equal(Confidence.Degraded, refined.Single(n => n.Id == "consumer").ParserConfidence);
    }

    private static RegistrationNode Node(
        string id,
        string name,
        Confidence confidence,
        string? pattern = null,
        string? keys = null)
    {
        var annotations = new Dictionary<string, string>();
        if (pattern != null)
            annotations["pattern"] = pattern;
        if (keys != null)
            annotations["factory_lambda_service_keys"] = keys;

        return new RegistrationNode
        {
            Id = id,
            DisplayName = name,
            AbstractToken = TypeRef.FromShortName(name),
            ServiceType = ServiceTypeIdentity.FromSyntactic(name),
            ParserConfidence = confidence,
            Annotations = annotations
        };
    }
}
