# ADR-006: IDE Integration Form Factor

**Status:** Accepted
**Date:** 2026-06-28
**Accepted:** 2026-07-03 (Phase 7 kickoff)
**Effort:** High (Opus-class reasoning required; see AGENTS.md routing matrix)

---

## Context

ADR-003 chose CLI-first form factor for v1. Phase 7 extends this to the IDE.
The goal: surface LEAKED, BROKEN, and DUPLICATE diagnostics inline at the
registration call site without leaving the editor.

Prerequisites shipped in Phases 5 and 10b: per-commit disk cache, structured
`analysis-report-1.0.json` output, and file:line sites on actionable findings.

---

## Decisions

### Q1: Extension vs Language Server Protocol (LSP) — **VS Code extension first (Option A)**

Ship a VS Code extension first. The analysis core stays in the CLI; an LSP server
may wrap the same subprocess contract later without changing the report schema.

Rejected for v1: LSP-only (slower VS Code UX), dual extension+LSP (too much surface).

### Q2: Analysis trigger — **On-open + on-save**

Analyze on workspace open (warm disk cache), then re-analyze on each save.
Unsaved edits fall back to working-directory analysis keyed by file mtime.

Rejected for v1: on-keystroke (requires daemon), explicit-command-only (too manual).

### Q3: Analysis scope — **Whole project**

Whole-project extraction per trigger. Per-file incremental invalidation deferred to v2.
Disk cache (Phase 5) makes repeated runs acceptable.

### Q4: Extension ↔ analyzer communication — **CLI subprocess**

Extension spawns:

```text
dcs analyze <workspace-root> --format json --report-out <temp-file> [--cache-dir <dir>]
```

Read and parse the report JSON from the temp file. No daemon, no in-process .NET host.

Cold-start subprocess cost (~100–300ms) is acceptable when cache hits dominate.
Daemon model deferred to v2 if measured on-save latency exceeds 1s on Trackdub-scale repos.

Rejected for v1: persistent daemon, .NET WASM in-process.

### Q5: Diagnostic surface

| Finding | Severity | Display |
|---------|----------|---------|
| LEAKED | error | Red squiggle on `services.Add*`; hover shows leak detail |
| BROKEN | error | Red squiggle; hover shows broken chain |
| DUPLICATE | warn | Yellow squiggle; hover shows duplicate count |
| ORPHANED | warn | Gray squiggle; lower priority |
| possible_duplicate | warn | Yellow squiggle; tier `actionable` only when strict mode |

Map `findings[].sites[]` (`file_path`, `line`) to `vscode.Diagnostic` ranges.
CodeLens above composition-root files: summary node/error counts from `report.summary`.

`FindingPolicy` suppressions apply unless the extension passes `--strict` (user setting).

### Q6: Public API contract — **`analysis-report-1.0.json`, not IR**

The IDE extension is a **consumer of the analysis report schema**, not of
`RegistrationGraph` / IR JSON.

| Artifact | Role | Consumer |
|----------|------|----------|
| `docs/schemas/analysis-report-1.0.json` | **Public IDE/CI contract** | VS Code extension, future LSP, external dashboards |
| IR schema 1.2.0 (`RegistrationGraph`) | Internal extraction interchange | CLI internals, `dump-ir`, diff/viz/fix engines |

**Rationale:** IR fields (`ResolvedTypeIdentity`, context bundles, blind-spot nodes)
change with parser and semantic work. Report findings are stable, actionable, and
already carry `file:line` sites. Binding the extension to IR would couple editor
releases to parser schema churn.

**Report schema compatibility policy** (parallel to ADR-002 IR rules):

- `schema_version` is **major.minor** string; current const: **`1.0`**.
- **Additive changes** (new optional summary counters, new optional finding fields,
  new `metrics` properties): permitted within **1.x**; consumers ignore unknown fields.
- **Breaking changes** (rename/remove required fields, change `category` enum semantics,
  change tier meaning): require **major bump** (`2.0`); extension MUST reject unsupported
  major versions with a clear error.
- New finding `category` values: additive only within 1.x if consumers treat unknown
  categories as informational.

**Freeze gate:** No breaking report-schema changes after the first extension release
without a documented major bump and extension update. IR may continue additive bumps
independently.

**CLI flags the extension depends on** (stable):

- `dcs analyze <path> --format json --report-out <file>`
- Optional: `--cache-dir`, `--strict`, `--context`, `--target-framework`, `--verbosity`

The extension MUST NOT call `dcs dump-ir` or parse raw `RegistrationGraph` JSON.

---

## Assumptions

1. VS Code is the primary editor for the target user (Trackdub team, .NET shops).
2. A 300ms analysis latency on-save is acceptable given disk cache.
3. The extension locates the project root via workspace folder detection.
4. Report schema 1.0 remains the extension contract until a deliberate 2.0 bump.

---

## What Would Falsify This Decision

- Target users are primarily JetBrains (Rider) users → LSP server required in v1.
- CLI subprocess startup exceeds 1s even with cache → daemon model required.
- Actionable findings routinely lack `sites[]` file:line → report schema inadequate;
  revisit before extension ships.
- .NET WASM matures with binary size <2MB → reconsider in-process option.

---

## Implementation (Phase 7)

- New package: `vscode-extension/` (or separate repo) — thin TypeScript shell.
- Subprocess wrapper + JSON schema validation against `analysis-report-1.0.json`.
- Phase 7 gate: LEAKED badge inline on Trackdub mid-migration commit within 5s of
  open; zero false-positive error diagnostics on negative-control corpus
  (`csharp-negative-control`, see `ci/corpus-gates.json`).
