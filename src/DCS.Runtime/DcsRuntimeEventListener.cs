using System.Diagnostics.Tracing;
using System.Text.Json;

namespace DCS.Runtime;

/// <summary>
/// Subscribes to <c>Microsoft-Extensions-DependencyInjection</c> EventSource and appends
/// JSONL resolution records (ADR-008; DiagnosticSource path falsified — MS.DI emits EventSource).
/// </summary>
public sealed class DcsRuntimeEventListener : EventListener
{
    private const string EventSourceName = "Microsoft-Extensions-DependencyInjection";

    private readonly string _logPath;
    private readonly bool _redact;
    private readonly Dictionary<string, string> _implementationByService = new(StringComparer.Ordinal);

    private DcsRuntimeEventListener(string logPath, bool redact)
    {
        _logPath = logPath;
        _redact = redact;
    }

    public static DcsRuntimeEventListener Start(string logPath, bool redact = false) => new(logPath, redact);

    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (string.Equals(eventSource.Name, EventSourceName, StringComparison.Ordinal))
            EnableEvents(eventSource, EventLevel.Verbose, EventKeywords.All);
    }

    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        switch (eventData.EventName)
        {
            case "CallSiteBuilt":
                RecordCallSite(eventData);
                break;
            case "ServiceResolved":
                RecordResolution(eventData);
                break;
        }
    }

    private void RecordCallSite(EventWrittenEventArgs eventData)
    {
        var serviceType = eventData.Payload?.ElementAtOrDefault(0)?.ToString();
        var callSiteJson = eventData.Payload?.ElementAtOrDefault(1)?.ToString();
        if (string.IsNullOrWhiteSpace(serviceType) || string.IsNullOrWhiteSpace(callSiteJson))
            return;

        var implementation = TryParseImplementationType(callSiteJson);
        if (!string.IsNullOrWhiteSpace(implementation))
            _implementationByService[serviceType] = implementation!;
    }

    private void RecordResolution(EventWrittenEventArgs eventData)
    {
        var serviceType = eventData.Payload?.ElementAtOrDefault(0)?.ToString();
        if (string.IsNullOrWhiteSpace(serviceType))
            return;

        _implementationByService.TryGetValue(serviceType, out var implementation);

        var evt = new RuntimeResolutionEvent
        {
            RequestedType = _redact ? Redact(serviceType) : serviceType,
            ResolvedType = string.IsNullOrWhiteSpace(implementation)
                ? null
                : (_redact ? Redact(implementation) : implementation),
            TimestampMs = Environment.TickCount64
        };

        RuntimeLogWriter.AppendJsonl(_logPath, evt);
    }

    private static string? TryParseImplementationType(string callSiteJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(callSiteJson);
            if (doc.RootElement.TryGetProperty("implementationType", out var impl))
                return impl.GetString();
        }
        catch (JsonException)
        {
            // Ignore malformed call-site payloads.
        }

        return null;
    }

    private static string Redact(string typeName) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(typeName)))[..12];
}
