# ADR-006: IDE Integration Form Factor

**Status:** Proposed
**Date:** 2026-06-28
**Effort:** High (Opus-class reasoning required; see AGENTS.md routing matrix)

---

## Context

ADR-003 chose CLI-first form factor for v1. Phase 7 extends this to the IDE.
The goal: surface LEAKED, BROKEN, and DUPLICATE diagnostics inline at the
registration call site without leaving the editor. The form factor decision
(extension vs LSP server vs other) drives the maintenance burden, the
cross-editor reach, and the analysis latency model.

---

## Questions to Resolve

### Q1: Extension vs Language Server Protocol (LSP) vs both

**Option A: VS Code extension only**
- Fastest to ship; deepest VS Code integration (decorations, CodeLens, hover)
- Only works in VS Code; zero benefit in JetBrains/Neovim users
- Extension spawns CLI subprocess; no shared process with other extensions

**Option B: LSP server (language-agnostic)**
- Works in any LSP-compatible editor (VS Code, JetBrains, Neovim, Emacs)
- LSP defines `textDocument/publishDiagnostics` — maps directly to LEAKED/BROKEN output
- More complex: LSP server manages lifecycle, document sync, workspace state
- No LSP equivalent for CodeLens-style registration-count overlays without
  custom protocol extension (`workspace/executeCommand`)

**Option C: VS Code extension + LSP server (extension hosts the LSP server)**
- Extension handles VS Code-specific UX (CodeLens, hover, status bar)
- LSP server handles analysis and talks to other editors too
- Most future-proof; most work

**Likely decision:** Option A first (VS Code extension), designed so the
analysis core is an independent library that an LSP server could wrap later.
The CLI already provides the analysis core; the extension is a thin shell.

---

### Q2: Analysis trigger model

**On-save:** Run full analysis when the file is saved. Latency: O(seconds) for
large projects. Acceptable if cache eliminates re-extraction of unchanged files.

**On-keystroke (incremental):** Too expensive without a persistent analysis
server. Not viable without a daemon process.

**Explicit command:** User runs "DCS: Analyze" command. No latency surprise.
Feels manual.

**On-open + on-save (hybrid):** Analyze on workspace open (warm cache), then
re-analyze on each save. Cache hit after first run eliminates the latency.
Cache key = commit SHA for git-tracked files; file mtime for unsaved changes.

**Likely decision:** On-open + on-save with disk cache (Phase 5 prerequisite).

---

### Q3: Analysis scope

**Whole project per save:** Simple. Correct. Expensive without cache.

**Changed files only (incremental):** Requires tracking which registrations
are in which files and invalidating only affected graph subsets. Significant
complexity; deferred to v2.

**Likely decision:** Whole project, with disk cache making it fast enough.

---

### Q4: Communication between extension and analyzer

**Option A: CLI subprocess per analysis**
- Extension spawns `dcs analyze <path> --output json` on each trigger
- Simple; no daemon; no port management
- Cold start: 100–300ms .NET startup time on each run (acceptable with cache)

**Option B: Persistent daemon (named pipe or TCP)**
- Extension connects to a running DCS daemon on a well-known port/pipe
- Daemon holds the warm graph in memory; incremental updates fast
- Complex: daemon lifecycle (start, stop, crash recovery, version mismatch)

**Option C: WebAssembly / in-process**
- Compile DCS analysis core to WASM; run inside the extension host process
- Eliminates subprocess cost; no daemon
- .NET WASM compilation is experimental; binary size large (~10MB)

**Likely decision:** Option A (subprocess) for v1; daemon model deferred to
v2 if latency proves unacceptable.

---

### Q5: Diagnostic surface

What the extension shows per registration call site:

| Finding | Display |
|---------|---------|
| LEAKED | Red squiggle on the `services.Add*` call; hover shows "Leaked: X→Y" |
| BROKEN | Red squiggle; hover shows "Broken chain: missing T" |
| DUPLICATE | Yellow squiggle; hover shows "Duplicate: same abstract token N×" |
| ORPHANED | Gray squiggle (lower severity); hover shows "Orphaned: no consumers" |

CodeLens above each composition root file: "DCS: N nodes, M errors".

---

## Assumptions

1. VS Code is the primary editor for the target user (Trackdub team, .NET shops).
2. A 300ms analysis latency on-save is acceptable given disk cache.
3. The extension can locate the project root via workspace folder detection.

---

## What Would Falsify This Decision

- Target users are primarily JetBrains (Rider) users → LSP server is required
  in v1, not v2.
- CLI subprocess startup time exceeds 1 second even with cache → daemon model
  required.
- .NET WASM matures and drops binary size to <2MB → reconsider in-process option.

---

## Status: Proposed

This ADR will be marked Accepted when Phase 7 begins.
