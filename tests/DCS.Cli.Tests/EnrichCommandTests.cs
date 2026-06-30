using DCS.Cli;
using DCS.Core.IR;
using DCS.Core.Serialization;
using DCS.Runtime;

namespace DCS.Cli.Tests;

public sealed class EnrichCommandTests
{
    [Fact]
    public async Task RunEnrich_writes_enriched_ir_with_runtime_annotations()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "dcs-enrich-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var irPath = Path.Combine(tempDir, "static.json");
            var logPath = Path.Combine(tempDir, "runtime.jsonl");
            var outPath = Path.Combine(tempDir, "enriched.json");

            var graph = new RegistrationGraph
            {
                ParserVersion = "test",
                Nodes =
                [
                    new RegistrationNode
                    {
                        Id = "svc",
                        RegistrationInstanceId = "svc",
                        InstanceId = "svc",
                        DisplayName = "IService",
                        AbstractToken = TypeRef.FromShortName("IService"),
                        ParserConfidence = Confidence.Explicit
                    }
                ],
                Edges = []
            };

            await File.WriteAllTextAsync(irPath, IrSerializer.Serialize(graph));
            await File.WriteAllTextAsync(
                logPath,
                RuntimeLogWriter.SerializeJsonl(
                [
                    new RuntimeResolutionEvent
                    {
                        RequestedType = "IService",
                        ResolvedType = "ServiceImpl",
                        Lifetime = "Singleton"
                    }
                ]));

            var exit = await ProgramCommands.RunEnrich(["--runtime-log", logPath, "--out", outPath, irPath]);

            Assert.Equal(0, exit);
            Assert.True(File.Exists(outPath));

            var enriched = IrSerializer.Deserialize(await File.ReadAllTextAsync(outPath));
            Assert.NotNull(enriched);
            Assert.Equal("true", enriched!.Metadata["runtime_enriched"]);
            Assert.Equal("1", enriched.Nodes[0].Annotations[RuntimeGraphEnricher.ResolvedCountKey]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
