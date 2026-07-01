using DCS.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace DCS.Runtime.Tests;

public sealed class DcsRuntimeDiagnosticListenerTests
{
    private sealed class ProbeService;

    [Fact]
    public void Start_captures_ms_di_resolution_events()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dcs-runtime-{Guid.NewGuid():N}.jsonl");
        try
        {
            using var listener = DcsRuntimeListener.Start(path);
            var services = new ServiceCollection();
            services.AddSingleton<ProbeService>();
            using var provider = services.BuildServiceProvider();
            provider.GetRequiredService<ProbeService>();

            var events = RuntimeLogReader.ReadJsonl(path);
            Assert.NotEmpty(events);
            Assert.Contains(events, e => e.RequestedType.Contains("ProbeService", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
