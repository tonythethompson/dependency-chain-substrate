using DCS.Core.Caching;
using DCS.Core.IR;

namespace DCS.Core.Tests;

public sealed class ExtractionCacheTests
{
    [Fact]
    public void Write_then_TryRead_returns_same_node_count()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"dcs-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cacheDir);

        try
        {
            var instanceId = RegistrationNode.ComputeRegistrationInstanceId("cache-test", "App.cs", 1, 0, 1, 80, 0);
            var graph = new RegistrationGraph
            {
                ParserVersion = "0.1.0-test",
                CommitSha = "abc123",
                Nodes =
                [
                    new RegistrationNode
                    {
                        Id = instanceId,
                        RegistrationInstanceId = instanceId,
                        InstanceId = instanceId,
                        DisplayName = "IFoo",
                        AbstractToken = TypeRef.FromShortName("IFoo")
                    }
                ]
            };

            ExtractionCache.Write(graph, cacheDir);
            var cached = ExtractionCache.TryRead("abc123", "0.1.0-test", cacheDir);

            Assert.NotNull(cached);
            Assert.Single(cached!.Nodes);
            Assert.Equal("abc123", cached.CommitSha);
        }
        finally
        {
            Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public void TryRead_returns_null_on_parser_version_mismatch()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"dcs-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(cacheDir);

        try
        {
            var graph = new RegistrationGraph
            {
                ParserVersion = "0.1.0",
                CommitSha = "def456"
            };

            ExtractionCache.Write(graph, cacheDir);
            var cached = ExtractionCache.TryRead("def456", "0.2.0", cacheDir);

            Assert.Null(cached);
        }
        finally
        {
            Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public void GetCacheFilePath_uses_commit_and_parser_version()
    {
        var path = ExtractionCache.GetCacheFilePath("/tmp/cache", "sha1", "0.1.0");
        Assert.Equal(Path.Combine("/tmp/cache", "sha1_0.1.0.json"), path);
    }
}
