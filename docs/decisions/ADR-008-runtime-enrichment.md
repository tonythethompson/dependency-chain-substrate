# ADR-008: Runtime Enrichment Overlay

**Status:** Proposed
**Date:** 2026-06-28
**Effort:** High (Opus-class reasoning required; see AGENTS.md routing matrix)

---

## Context

The tool's current extraction is purely static: what is registered, not what
is actually resolved at runtime. Static analysis has known blind spots:
factory lambdas, conditional registrations, assembly scanning. Runtime data
fills these gaps. The enrichment overlay blends a runtime call log with the
static IR, reclassifying BLIND_SPOT and ORPHANED nodes based on observed
resolution.

A second motivation: lifetime violation detection. A scoped service resolved
inside a singleton's constructor is a captive dependency bug. Static analysis
can only flag this if the lifetimes are explicit; runtime data makes it certain.

---

## Questions to Resolve

### Q1: Instrumentation approach

**Option A: IServiceProvider wrapper (decorator pattern)**
- Wrap the host's `IServiceProvider` with a recording decorator
- Intercept every `GetService<T>()` / `GetRequiredService<T>()` call
- Log: type requested, type resolved, calling type (via stack frame), timestamp
- Advantages: no framework changes; works with any MS.DI host
- Disadvantages: stack-frame capture is expensive; misses lazy resolution via
  `Lazy<T>` or `IServiceScopeFactory`

**Option B: DiagnosticSource / EventSource**
- MS.DI emits `Microsoft.Extensions.DependencyInjection` DiagnosticSource events
  (available since .NET 5) for service resolution
- Subscribe via `DiagnosticListener`; zero wrapper code in user app
- Events include: `IServiceProvider.GetService.Start` (type requested, provider ID)
- Advantages: framework-native; no decorator plumbing
- Disadvantages: events are undocumented / semi-internal; payload schema may change

**Option C: OpenTelemetry instrumentation**
- MS.DI OTel instrumentation package exists (`Microsoft.Extensions.DependencyInjection.Otel`)
  as of .NET 9 preview
- Emits spans for service resolution
- Advantages: standard; composable with existing OTel pipelines
- Disadvantages: adds OTel dependency; span overhead higher than DiagnosticSource;
  availability depends on .NET version

**Option D: ETW / EventPipe**
- .NET runtime emits GC/JIT/type-load events via EventPipe
- Could infer service resolution from type construction events
- Very indirect; high noise; requires dotnet-trace or custom EventPipe consumer

**Likely decision:** Option B (DiagnosticSource) for dev-mode; Option A as
fallback if DiagnosticSource payload is insufficient. OTel support in v2 after
.NET 9 OTel integration matures.

---

### Q2: Data collected

Minimum viable runtime log (per resolution event):

```json
{
  "requested_type": "IFoo",
  "resolved_type":  "FooImpl",
  "scope_id":       "abc123",
  "lifetime":       "Singleton",
  "caller_type":    "BarService",
  "timestamp_ms":   12345
}
```

Used to:
1. Reclassify BLIND_SPOT nodes where `resolved_type` is now known
2. Reclassify ORPHANED nodes where `caller_type` resolves them at runtime
3. Detect captive dependency: `lifetime=Scoped` but `caller_type` is Singleton
4. Count resolution frequency (hot paths)

Privacy consideration: type names may reveal business logic or sensitive class
names. The runtime log must be treated as sensitive and never leave the dev
machine without explicit export. A `--redact` flag replaces short names with
opaque hashes in the log before export.

---

### Q3: Merge strategy

The static IR and runtime log are two separate documents. Merge rules:

| Static node | Runtime event | Merged result |
|-------------|---------------|---------------|
| BLIND_SPOT | resolved | BLIND_SPOT → EXPLICIT (runtime-confirmed), concrete_impl updated |
| ORPHANED | resolved via caller | ORPHANED cleared; edge added (INFERRED, runtime source) |
| EXPLICIT | not resolved | Keep as-is; add annotation `resolved_count=0` |
| EXPLICIT | resolved N times | Add annotation `resolved_count=N` |
| Missing from static | resolved | New node added, confidence=INFERRED, source=runtime |

Merge is non-destructive: the original static IR is preserved; the enriched
graph is a separate document (`--output enriched-ir`).

---

### Q4: Performance overhead target

Runtime log collection must not affect production deployments. Two modes:

**Dev mode (default):** DiagnosticSource enabled; full resolution logging;
overhead target <5% on warm path. Acceptable in dev/staging.

**Prod mode (`--runtime-log=off`):** No instrumentation; static-only analysis.
Zero overhead. Default for prod.

The enrichment NuGet package is opt-in; it does not auto-activate.

---

### Q5: Collection mechanism

**A: In-process log to file**
- Resolution events written to `dcs-runtime.jsonl` in the app's working directory
- `dcs enrich <path-to-ir> --runtime-log dcs-runtime.jsonl` merges post-run
- Simple; works offline

**B: In-process log to named pipe / socket**
- Real-time streaming to a DCS daemon
- Higher integration complexity; enables live updates in IDE

**Likely decision:** Option A (file-based log) for v1; daemon streaming in v2
alongside the IDE extension.

---

## Assumptions

1. DiagnosticSource events for MS.DI service resolution are stable across
   .NET 6–9 (to be validated; events are semi-internal).
2. Developer environments can tolerate <5% overhead during enrichment runs.
3. Type names in runtime logs are not subject to PII regulations in the
   projects this tool is applied to (valid for open-source projects; note the
   risk for enterprise users in the output).

---

## What Would Falsify This Decision

- DiagnosticSource payload changes between .NET versions → IServiceProvider
  wrapper (Option A) becomes the stable approach.
- Overhead exceeds 5% on Trackdub startup → throttle event collection (sample
  1-in-N resolutions for hot paths).
- Type names in runtime logs create legal/compliance issues for enterprise
  adopters → `--redact` must be the default, not opt-in.

---

## Status: Proposed

This ADR will be marked Accepted when Phase 9 begins.
