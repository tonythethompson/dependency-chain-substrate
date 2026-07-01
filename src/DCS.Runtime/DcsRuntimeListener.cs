namespace DCS.Runtime;

/// <summary>
/// Entry point for runtime resolution logging. Uses MS.DI EventSource (see <see cref="DcsRuntimeEventListener"/>).
/// </summary>
public static class DcsRuntimeListener
{
    public static DcsRuntimeEventListener Start(string logPath, bool redact = false) =>
        DcsRuntimeEventListener.Start(logPath, redact);
}
