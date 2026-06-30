using DCS.Runtime;

namespace DCS.Runtime.Tests;

public sealed class RuntimeLogReaderTests
{
    [Fact]
    public void ParseJsonl_reads_snake_case_events()
    {
        var content = """
            {"requested_type":"IFoo","resolved_type":"FooImpl","lifetime":"Singleton","timestamp_ms":1}
            {"requested_type":"IBar","caller_type":"Host","caller_lifetime":"Singleton","lifetime":"Scoped","timestamp_ms":2}

            """;

        var events = RuntimeLogReader.ParseJsonl(content);

        Assert.Equal(2, events.Count);
        Assert.Equal("IFoo", events[0].RequestedType);
        Assert.Equal("FooImpl", events[0].ResolvedType);
        Assert.Equal("IBar", events[1].RequestedType);
        Assert.Equal("Host", events[1].CallerType);
    }

    [Fact]
    public void SerializeJsonl_round_trips_through_reader()
    {
        var original = new List<RuntimeResolutionEvent>
        {
            new()
            {
                RequestedType = "IService",
                ResolvedType = "ServiceImpl",
                ScopeId = "scope-1",
                Lifetime = "Scoped",
                CallerType = "Program",
                CallerLifetime = "Singleton",
                TimestampMs = 42
            }
        };

        var jsonl = RuntimeLogWriter.SerializeJsonl(original);
        var parsed = RuntimeLogReader.ParseJsonl(jsonl);

        Assert.Single(parsed);
        Assert.Equal("IService", parsed[0].RequestedType);
        Assert.Equal("ServiceImpl", parsed[0].ResolvedType);
        Assert.Equal("scope-1", parsed[0].ScopeId);
        Assert.Equal("Program", parsed[0].CallerType);
    }
}
