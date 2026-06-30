using System.Text.Json.Serialization;

namespace DCS.Runtime;

/// <summary>
/// One MS.DI service resolution observed at runtime (ADR-008 Q2).
/// Serialized as JSONL with snake_case property names.
/// </summary>
public sealed record RuntimeResolutionEvent
{
    [JsonPropertyName("requested_type")]
    public required string RequestedType { get; init; }

    [JsonPropertyName("resolved_type")]
    public string? ResolvedType { get; init; }

    [JsonPropertyName("scope_id")]
    public string? ScopeId { get; init; }

    [JsonPropertyName("lifetime")]
    public string? Lifetime { get; init; }

    [JsonPropertyName("caller_type")]
    public string? CallerType { get; init; }

    [JsonPropertyName("caller_lifetime")]
    public string? CallerLifetime { get; init; }

    [JsonPropertyName("timestamp_ms")]
    public long TimestampMs { get; init; }
}
