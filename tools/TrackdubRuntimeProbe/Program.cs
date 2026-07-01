using System.Diagnostics;
using DCS.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Trackdub.Composition;

if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
{
    Console.Error.WriteLine("Usage: TrackdubRuntimeProbe --out <dcs-runtime.jsonl> [--trackdub-root <path>]");
    return 2;
}

string? outPath = null;
string? trackdubRoot = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--out" when i + 1 < args.Length:
            outPath = args[++i];
            break;
        case "--trackdub-root" when i + 1 < args.Length:
            trackdubRoot = args[++i];
            break;
    }
}

if (string.IsNullOrWhiteSpace(outPath))
{
    Console.Error.WriteLine("Error: --out is required.");
    return 2;
}

trackdubRoot ??= Environment.GetEnvironmentVariable("TRACKDUB_PATH")
    ?? Environment.GetEnvironmentVariable("CORPUS_CSHARP_MIGRATION_PATH")
    ?? @"A:\Trackdub";

if (!Directory.Exists(trackdubRoot))
{
    Console.Error.WriteLine($"Error: Trackdub root not found: {trackdubRoot}");
    return 2;
}

if (File.Exists(outPath))
    File.Delete(outPath);

var previousCwd = Environment.CurrentDirectory;
Environment.CurrentDirectory = trackdubRoot;

try
{
    using var listener = DcsRuntimeListener.Start(outPath);

    var sw = Stopwatch.StartNew();
    var services = new ServiceCollection();
    services.AddTrackdub();
    var serviceTypes = services
        .Select(d => d.ServiceType)
        .Where(t => t != typeof(IServiceScopeFactory))
        .Distinct()
        .ToList();

    using var provider = services.BuildServiceProvider(new ServiceProviderOptions
    {
        ValidateOnBuild = true,
        ValidateScopes = true
    });

    foreach (var serviceType in serviceTypes)
    {
        try
        {
            provider.GetService(serviceType);
        }
        catch
        {
            // Some open-generic or scoped-only services fail at root; continue.
        }
    }

    using (var scope = provider.CreateScope())
    {
        foreach (var serviceType in serviceTypes)
        {
            try
            {
                scope.ServiceProvider.GetService(serviceType);
            }
            catch
            {
                // Continue best-effort resolution sweep.
            }
        }
    }

    sw.Stop();

    var events = RuntimeLogReader.ReadJsonl(outPath);
    Console.Error.WriteLine(
        $"[probe] events={events.Count} elapsed_ms={sw.ElapsedMilliseconds} cwd={trackdubRoot} out={outPath}");
    return events.Count > 0 ? 0 : 1;
}
finally
{
    Environment.CurrentDirectory = previousCwd;
}
