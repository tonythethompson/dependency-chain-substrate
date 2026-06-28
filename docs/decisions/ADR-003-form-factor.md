# ADR-003: Form Factor

**Status:** Accepted
**Date:** 2026-06-28
**Effort:** Medium-High (see AGENTS.md routing matrix)

---

## Context

The tool must be delivered in some form. The candidates are:

1. **Avalonia desktop app** — native Windows/Linux/Mac GUI application.
2. **Web viewer** — browser-based, either static-generated or with a dev server.
3. **CLI-first with serialised IR** — command-line tool, structured output,
   separate viewer consumers.
4. **IDE extension** — VS Code / Rider extension, inline visualisation.

This decision determines the distribution model, the CI-gate viability, the
development timeline, and which future form factors are still open.

---

## Constraints

### C1: CI-gate is a P2 architectural constraint

The plan-of-plan states: "Keep the extraction/analysis core transport-agnostic
and the viz one consumer of the serialised IR, so a CLI/CI gate comes nearly
free later. P2 that constrains v1 architecture."

A CI gate must:
- Run headless (no GUI required).
- Be scriptable (accept arguments, produce structured exit codes and output).
- Run in a Docker container or standard CI runner without special OS dependencies.

### C2: Phase 1 acceptance test is a CLI operation

Phase 1 done = "run against Trackdub's real git history and reproduce known
WinUI leakage." This is a command to run against a git repository, not an
interactive session. It must be automatable.

### C3: Viz ecosystem lock-in is a risk

Choosing an Avalonia desktop app for v1 binds the viz rendering, the
distribution format, and the UI paradigm. If the primary user turns out to prefer
a web-based view, the Avalonia investment is wasted. Deferring the viz consumer
decision keeps options open.

### C4: The Trackdub migration is the dogfood use case, not a demo

The tool author is the primary user. The author works in a shell and an IDE.
A CLI is immediately usable without installation ceremony.

---

## First-Principles Argument

### Avalonia desktop app

**For:**
- Native rendering, best performance at 1000+ nodes with a proper scene graph.
- No browser sandbox limitations.
- Avalonia targets the migration use case (ironic fit with the project origin).

**Against:**
- Not headless. CI gate requires a separate CLI path or the Avalonia rendering
  stack in headless mode (possible but added complexity).
- Distribution requires a packaged app installer or self-contained executable.
  This is overhead before v1 is validated.
- Forces the graph rendering library decision (SkiaSharp-based Avalonia Canvas
  vs a third-party graph lib) at v1, before the scale requirements are proven.
- Cannot be the primary form factor for Phase 1 (extraction + CLI output) —
  Phase 1 has no GUI component. Building Avalonia infrastructure for Phase 3
  before Phase 1 is validated is premature.

**Verdict:** correct for Phase 3 viz consumer; wrong as the primary v1 form
factor.

### Web viewer

**For:**
- Universally accessible; any OS, any device.
- Rich graph rendering ecosystem (D3, Cytoscape, Sigma.js, vis.js).
- Shareable: generate a static HTML file from the IR, share it with a
  stakeholder.

**Against:**
- Requires a build step (JS bundler) even for a simple viewer.
- Headless CI gate needs the extraction to run separately and produce the IR;
  the viewer is a consumer but not the gate itself. This is actually the
  correct split, but it means the "web viewer" is never the gate.
- Browser sandbox limits access to large graph files (memory) and local
  git repositories.
- More complex to generate fully offline/static output from a .NET CLI tool.

**Verdict:** valid as a Phase 3 viz consumer (generate a self-contained HTML
file from the serialised IR). Wrong as the primary v1 form factor for the
same reason as Avalonia.

### CLI-first with serialised IR

**For:**
- Phase 1 acceptance test is a CLI command. Phase 1 can be validated with no
  GUI work.
- CI gate is the CLI. No additional form factor work needed to support CI.
- Serialised IR (JSON to a file or stdout) is consumed by any downstream
  viz consumer without coupling.
- Transport-agnostic core: extraction and analysis do not know or care about
  the UI.
- Development sequence is natural: CLI first → Phase 1 done → diff CLI →
  Phase 2 done → viz consumer added → Phase 3 done.
- Immediate utility to the author without distribution ceremony: build and run.

**Against:**
- Text output is not the best UX for graph exploration. A 1000-node
  registration graph in text is unreadable.
- Requires a downstream viewer for the interactive use case.

**Assessment of the against:** the text-output limitation is a Phase 3
concern, not a Phase 1 concern. Phase 1 needs "list orphaned registrations" and
"show leakage," not "render graph interactively." The limitation is real but
deferred.

### IDE extension

**For:**
- Highest leverage if distributed broadly: integrates where developers already
  work.
- Can annotate code inline (e.g., gutter icon for registered types).

**Against:**
- IDE extension SDKs (VS Code API, JetBrains Platform) are large, poorly
  documented, and brittle across versions.
- An extension cannot easily run the full extraction pipeline inline without
  either bundling a server or spawning an external process.
- Distribution requires marketplace publishing and review.
- Wrong focus for v1: the goal is to validate the extraction + analysis core.
  Building distribution infrastructure before the core is proven is backwards.

**Verdict:** correct for v3+; wrong for v1.

---

## Decision

**CLI-first with a serialised IR as the primary delivery form for v1.**

The CLI:
- Takes a path to a git repository and optional commit ref(s) as arguments.
- Produces either a text report (stdout) or a JSON IR file (flag: `--ir-out`).
- Returns a non-zero exit code if leakage is detected (enabling CI gating).
- Is the only consumer for Phase 1 and Phase 2.

The serialised IR:
- Is the integration point for all downstream consumers.
- Is versioned (see ADR-002).
- Is the input to the Phase 3 viz consumer, whatever form that takes.

Form factor for Phase 3 viz:
- Decision deferred until Phase 2 is done and the IR format is stable.
- The leading candidate is a self-contained HTML file generated from the IR
  (web viewer, no server required), because it is shareable and cross-platform
  without distribution work.
- Avalonia desktop app remains a valid choice if the scale or interactivity
  requirements prove that the web rendering stack cannot handle them.
- This decision is deliberately not made now. The Phase 3 viz ADR will be
  written when Phase 2 is complete.

---

## Rejected Alternatives

### Avalonia desktop as primary v1 form factor

Rejected because Phase 1 requires no GUI, and building the GUI before the core
is validated inverts the priority order. The core (extraction + analysis) must
be proven against Trackdub before investing in rendering.

### Web viewer as primary v1 form factor

Rejected for the same reason: the web viewer is a viz consumer, not the tool
itself. The tool is the extraction + analysis pipeline. Coupling the primary
delivery form to the viz layer ties Phase 1 to Phase 3.

### IDE extension as primary v1 form factor

Rejected because it adds distribution complexity before the core is validated,
and the primary use case (CI-gate, bulk diff across commit range) does not fit
the IDE interaction model.

---

## Assumptions

1. The primary Phase 1 use case (reproduce Trackdub leakage) is satisfied by
   text output and a non-zero exit code. If the user needs to navigate the
   graph interactively to understand the leakage, Phase 1 defers to Phase 3
   before the acceptance test can be meaningful.

2. The CLI's extraction-to-JSON output is fast enough that users don't need
   an incremental streaming UI. If Trackdub extraction takes >60 seconds, add
   progress output but keep the CLI-first model.

3. The form factor decision does not preclude adding a GUI later. The IR is
   the API contract; the GUI is one consumer of it.

---

## What Would Falsify This Decision

**Primary falsifier:** The primary users (once we have them beyond the author)
require interactive graph navigation before they can use the tool at all.
If text output + JSON IR are insufficient for the Phase 1 acceptance test to be
meaningful, advance the viz form factor into Phase 1 scope.

**Secondary falsifier:** CI integration requires a container-compatible binary
with no .NET runtime dependency. If the .NET CLI tool cannot be packaged
self-contained without SDK installation, reconsider distribution strategy.
