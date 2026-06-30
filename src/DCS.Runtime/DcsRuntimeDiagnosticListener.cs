using System.Diagnostics;

namespace DCS.Runtime;

/// <summary>
/// Subscribes to Microsoft.Extensions.DependencyInjection DiagnosticSource events and
/// appends JSONL resolution records (ADR-008 Q1 Option B, Q5 Option A).
/// </summary>
public sealed class DcsRuntimeDiagnosticListener : IObserver<KeyValuePair<string, object?>>
{
    private readonly string _logPath;
    private readonly bool _redact;
    private IDisposable? _subscription;

    public DcsRuntimeDiagnosticListener(string logPath, bool redact = false)
    {
        _logPath = logPath;
        _redact = redact;
    }

    public static DcsRuntimeDiagnosticListener Start(string logPath, bool redact = false)
    {
        var listener = new DcsRuntimeDiagnosticListener(logPath, redact);
        listener._subscription = DiagnosticListener.AllListeners.Subscribe(new ListenerObserver(listener));
        return listener;
    }

    public void Dispose() => _subscription?.Dispose();

    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (!string.Equals(value.Key, "Microsoft.Extensions.DependencyInjection", StringComparison.Ordinal))
            return;

        if (value.Value is not DiagnosticListener listener)
            return;

        listener.Subscribe(this);
    }

    public void OnCompleted() { }
    public void OnError(Exception error) { }

    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> evt)
    {
        if (!evt.Key.EndsWith("GetService.Start", StringComparison.Ordinal) &&
            !evt.Key.EndsWith("GetRequiredService.Start", StringComparison.Ordinal))
        {
            return;
        }

        if (evt.Value is not { } payload)
            return;

        var requested = GetProperty(payload, "RequestedType", "ServiceType", "Type");
        if (string.IsNullOrWhiteSpace(requested))
            return;

        var resolved = GetProperty(payload, "ResolvedType", "ImplementationType");
        var evtRecord = new RuntimeResolutionEvent
        {
            RequestedType = _redact ? Redact(requested!) : requested!,
            ResolvedType = string.IsNullOrWhiteSpace(resolved) ? null : (_redact ? Redact(resolved!) : resolved),
            TimestampMs = Environment.TickCount64
        };

        RuntimeLogWriter.AppendJsonl(_logPath, evtRecord);
    }

    void IObserver<KeyValuePair<string, object?>>.OnCompleted() { }
    void IObserver<KeyValuePair<string, object?>>.OnError(Exception error) { }

    private static string? GetProperty(object payload, params string[] names)
    {
        var type = payload.GetType();
        foreach (var name in names)
        {
            var prop = type.GetProperty(name);
            if (prop == null)
                continue;

            var value = prop.GetValue(payload);
            if (value == null)
                continue;

            return value switch
            {
                Type t => t.FullName ?? t.Name,
                _ => value.ToString()
            };
        }

        return null;
    }

    private static string Redact(string typeName) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(typeName)))[..12];

    private sealed class ListenerObserver(DcsRuntimeDiagnosticListener target) : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value) => ((IObserver<KeyValuePair<string, object?>>)target).OnNext(
            new KeyValuePair<string, object?>(value.Name, value));

        public void OnCompleted() { }
        public void OnError(Exception error) { }
    }
}
