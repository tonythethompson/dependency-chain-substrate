using DCS.Core.IR;
using DCS.Core.Parsing;
using DCS.Core.Serialization;
using DCS.Parser.Java;
using DCS.Verification;

namespace DCS.Parser.Java.Tests;

public sealed class SpringPetClinicIntegrationTests
{
    private static string? ResolvePetClinicPath()
    {
        var env = Environment.GetEnvironmentVariable("PETCLINIC_PATH");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env))
            return env;

        if (!string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
            return null;

        var cloneDir = Path.Combine(Path.GetTempPath(), "dcs-petclinic-pin");
        if (!Directory.Exists(Path.Combine(cloneDir, ".git")))
        {
            if (Directory.Exists(cloneDir))
                Directory.Delete(cloneDir, recursive: true);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"clone {PetClinicPin.RepositoryUrl} \"{cloneDir}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start git clone.");
            proc.WaitForExit();
            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"git clone failed: {proc.StandardError.ReadToEnd()}");

            var checkout = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"checkout {PetClinicPin.CommitSha}",
                WorkingDirectory = cloneDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var checkoutProc = System.Diagnostics.Process.Start(checkout)
                ?? throw new InvalidOperationException("Failed to start git checkout.");
            checkoutProc.WaitForExit();
            if (checkoutProc.ExitCode != 0)
                throw new InvalidOperationException($"git checkout failed: {checkoutProc.StandardError.ReadToEnd()}");
        }

        return cloneDir;
    }

    [Fact]
    public void PetClinic_structural_gate()
    {
        var path = ResolvePetClinicPath();
        if (path == null)
        {
            return; // optional locally
        }

        var result = new SpringStaticParser().ParseDirectory(path);
        var graph = result.ContextGraphs.FirstOrDefault(c =>
            c.EntryRoot.FullyQualifiedName.Contains("PetClinicApplication", StringComparison.Ordinal))
            ?? Assert.Single(result.ContextGraphs);

        var nodes = graph.Graph.Nodes;
        Assert.True(nodes.Count(n => n.Lifetime == Lifetime.Singleton) >= 10,
            $"Expected >=10 singleton registrations, got {nodes.Count}");

        Assert.Contains(nodes, n =>
            n.ContextMemberships.Any(m => m.State == ReachabilityState.StaticallyReachable) &&
            n.Origin is RegistrationOrigin.Stereotype or RegistrationOrigin.BeanMethod);

        var repo = nodes.FirstOrDefault(n =>
            n.Origin == RegistrationOrigin.SpringData &&
            n.ExposedType?.ShortName?.Contains("Repository", StringComparison.Ordinal) == true);
        if (repo != null)
        {
            Assert.Null(repo.ImplementationType);
            Assert.Equal(Confidence.Degraded, repo.ParserConfidence);
        }

        Assert.True(
            graph.Graph.Edges.Count > 0 ||
            graph.Graph.ConditionalInjections.Count > 0 ||
            graph.Graph.UnresolvedInjections.Count > 0 ||
            result.Diagnostics.Count > 0 ||
            graph.Graph.BlindSpots.Count > 0,
            "Expected wiring edges, conditional/unresolved injections, or diagnostics/blind spots.");

        Assert.True(graph.Graph.Edges.Count > 0,
            $"Phase 6 gate requires @Autowired wiring edges; got {graph.Graph.Edges.Count}.");

        var pass1 = ParseResultSerializer.Serialize(result);
        var pass2 = ParseResultSerializer.Serialize(new SpringStaticParser().ParseDirectory(path));
        Assert.Equal(pass1, pass2);
    }
}
