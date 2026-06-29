# Dependency Chain Substrate — Milestone Tracker

Last updated: 2026-06-28

---

## Phase 0 — Positioning + Extraction Fork + IR Draft

**Done means:** All four gating decisions written as ADRs (Accepted). Spring
paper-spike complete. IR either survives the spike or is revised.

| Task | Status | Notes |
|------|--------|-------|
| Scaffold docs (AGENTS.md, DESIGN.md, PLAN.md, decisions/) | ✅ Done | 2026-06-28 |
| ADR-001: Extraction strategy | ✅ Done | 2026-06-28 — Static-first accepted |
| ADR-002: IR + identity model | ✅ Done | 2026-06-28 — Multi-factor identity |
| ADR-003: Form factor | ✅ Done | 2026-06-28 — CLI-first accepted |
| ADR-004: Spring paper-spike timing | ✅ Done | 2026-06-28 — Spike before IR freeze |
| Spring paper-spike (IR compatibility validation) | ✅ Done | 2026-06-28 — No breaking changes; 5 additive extensions folded into ADR-002 |
| DESIGN.md §4 (Extraction Strategy) — fill answers | ⬜ Not started | After ADR-001 accepted |
| DESIGN.md §5 (IR + Identity) — fill answers | ⬜ Not started | After ADR-002 + spike |
| DESIGN.md §1-3 (Problem, Goals, Users) — fill answers | ⬜ Not started | Parallelisable with above |

**Phase 0 gate:** ✅ CLOSED — Spring spike complete, IR frozen, ADR-002 Accepted.

---

## Phase 1 — C# Parser + Analysis + Leakage Detection

**Done means:** Reproduces known Trackdub WinUI leakage on real commits. Blind
spots documented in output.

| Task | Status | Notes |
|------|--------|-------|
| DESIGN.md §6 (C# parser) — fill | ✅ Done | 2026-06-28 |
| DESIGN.md §7 (Graph analysis) — fill | ✅ Done | 2026-06-28 |
| DESIGN.md §8 (Framework boundary) — fill | ✅ Done | 2026-06-28 |
| Roslyn static parser — implement | ✅ Done | 2026-06-28 — DCS.Parser.CSharp |
| IR serialiser — implement | ✅ Done | 2026-06-28 — DCS.Core.Serialization |
| Graph analysis layer — implement | ✅ Done | 2026-06-28 — DCS.Analysis |
| CLI text output — implement | ✅ Done | 2026-06-28 — DCS.Cli `analyze` command |
| Phase 1 verification against Trackdub | ✅ Done | 2026-06-28 — 186 registrations at commit 3c4e374d; VoiceCloneConsentCoordinator 2× (WinUI+Avalonia) and 6 other duplicates correctly detected as leaked migration state |

**Phase 1 gate:** ✅ CLOSED — leakage detected on real Trackdub mid-migration commit. Primary signal: DUPLICATE registrations (same abstract token in both shells). LEAKED now also fires via instance-pass (schema 1.1.0 dual-identity model, see ADR-002 addendum).

---

## Phase 2 — Git Ingestion + Diff Engine

**Done means:** Drift Scanner shows meaningful, low-noise diffs between two
Trackdub commits.

| Task | Status | Notes |
|------|--------|-------|
| DESIGN.md §9-10 — fill | ⬜ Not started | |
| Git blob reader (libgit2sharp) — implement | ✅ Done | 2026-06-28 — CSharpStaticParser.ParseCommit |
| Per-commit extraction cache (keyed by SHA) — implement | ⬜ Not started | In-memory; file cache deferred |
| Diff engine + rename detection — implement | ✅ Done | 2026-06-28 — DCS.Diff |
| CLI `diff` command — implement | ✅ Done | 2026-06-28 |
| Phase 2 verification against Trackdub | ✅ Done | 2026-06-28 — diff 3c4e374d→316614b8 correctly shows MainWindow+MainWindowViewModel removed; breaking changes detected |

**Phase 2 gate:** ✅ CLOSED — diff engine verified against Trackdub WinUI retire commits.

---

## Phase 3 — Visualisation + Form Factor

**Done means:** Legible interactive view of Trackdub's graph at full scale.

| Task | Status | Notes |
|------|--------|-------|
| DESIGN.md §11 — fill | ⬜ Not started | |
| Visualisation consumer — implement | ✅ Done | 2026-06-28 — DCS.Viz self-contained HTML |
| CLI `viz` command — implement | ✅ Done | 2026-06-28 |
| Aggregation / focus+context / LOD | ✅ Done | 2026-06-28 — framework-grouped layout + zoom LOD |
| Phase 3 verification | ✅ Done | 2026-06-28 — 220KB self-contained HTML generated for 186-node Trackdub mid-migration graph; canvas render, zoom/pan, framework groups, error badges |

**Phase 3 gate:** ✅ CLOSED — viz verified at Trackdub scale (186 nodes).

---

## Phase 4 — Polish + CI Gate

**Done means:** Non-interactive gate runnable in CI.

| Task | Status | Notes |
|------|--------|-------|
| CI-gate consumer — implement | ✅ Done | 2026-06-28 — exit code 1 on errors; `analyze` is CI-ready |
| Registration Atlas polish | ⬜ Not started | DESIGN.md §12 module spec |
| Boundary Probe config UX | ⬜ Not started | `--frameworks <json>` flag |
| Phase 4 verification | ✅ Done | 2026-06-28 — exit code 1 on analyze 3c4e374d (4 broken chains); exit code 1 on diff 3c4e374d→316614b8 (breaking changes) |

**Phase 4 gate:** ✅ CLOSED — CI gate verified on real Trackdub commits.

---

---

## Phase 5 — Near-Term Enablers + Documentation Backfill

**Done means:** DESIGN.md fully answered; `--frameworks` config works on real
Trackdub; disk cache eliminates redundant re-extraction on repeated CLI runs.

| Task | Status | Notes |
|------|--------|-------|
| DESIGN.md §§1-3 (Problem, Goals, Users) — fill | ⬜ Not started | Answers derivable from implementation + ADRs |
| DESIGN.md §§4-5 (Extraction, IR) — fill | ⬜ Not started | |
| DESIGN.md §§9-10 (Diff Engine, Git Ingestion) — fill | ⬜ Not started | |
| DESIGN.md §§11-17 (Viz, Modules, Cross-cutting, Extensibility, Phasing, Risks, Validation) — fill | ⬜ Not started | |
| `--frameworks <json>` — implement | ⬜ Not started | Allow caller to override default framework boundary config; see ADR-001 §future |
| Per-commit disk cache — implement | ⬜ Not started | File cache keyed by SHA+parserVersion; eliminates re-extraction on repeated runs |
| Registration Atlas polish — implement | ⬜ Not started | Complete §12 module spec per DESIGN.md |
| Rename weight tuning | ⬜ Blocked | Needs a Trackdub commit pair with known renames; unblocked when such a pair is identified |

**Phase 5 gate:** DESIGN.md has no unfilled `> Q:` prompts; `dcs diff --frameworks` works on Trackdub; repeated `dcs analyze` on same commit skips re-extraction.

---

## Phase 6 — Second Language Parser (Spring)

**Done means:** A Spring Boot project produces a valid IR graph. DUPLICATE and
LEAKED detection work on a real Spring Boot repo with multiple framework contexts.

| Task | Status | Notes |
|------|--------|-------|
| ADR-005: Spring parser scope and approach | ⬜ Not started | Key decisions: JavaParser vs tree-sitter; which Spring patterns are in scope |
| DCS.Parser.Java — scaffold | ⬜ Not started | After ADR-005 accepted |
| @Bean / @Configuration pattern extraction | ⬜ Not started | |
| @Component scan pattern extraction | ⬜ Not started | |
| @Autowired constructor edge extraction | ⬜ Not started | |
| @Conditional / Spring Data → BLIND_SPOT | ⬜ Not started | |
| Framework tags for Spring MVC / Spring Data / Spring Security | ⬜ Not started | |
| Phase 6 verification | ⬜ Not started | Against Spring PetClinic or equivalent open-source project |

**Phase 6 gate:** Spring PetClinic IR contains ≥10 correctly typed nodes with
SINGLETON lifetime; @Autowired edges present; Spring Data repos show DEGRADED
confidence; auto-config beans show BLIND_SPOT.

---

## Phase 7 — IDE Extension

**Done means:** A VS Code extension shows inline error badges on DI registration
call sites without leaving the editor.

| Task | Status | Notes |
|------|--------|-------|
| ADR-006: IDE integration form factor | ⬜ Not started | Key decision: VS Code extension vs LSP server; trigger model; analysis scope |
| Extension scaffold | ⬜ Not started | After ADR-006 accepted |
| On-save analysis trigger | ⬜ Not started | |
| Inline diagnostic decorations (LEAKED, BROKEN, DUPLICATE) | ⬜ Not started | |
| IR cache reuse within IDE session | ⬜ Not started | |
| Phase 7 verification | ⬜ Not started | Inline badge visible on VoiceCloneConsentCoordinator in Trackdub without running CLI |

**Phase 7 gate:** LEAKED badge appears inline within 5 seconds of opening
Trackdub in VS Code; no false positive on clean commit.

---

## Phase 8 — Auto-fix / Codemod

**Done means:** `dcs fix` applies at least one safe fix class (DUPLICATE
removal) with a preview diff and rollback via git.

| Task | Status | Notes |
|------|--------|-------|
| ADR-007: Write-back safety model | ⬜ Not started | Key decisions: fix scope, Roslyn SyntaxRewriter vs string replacement, preview model, rollback strategy |
| Fix classes: DUPLICATE removal | ⬜ Not started | After ADR-007 accepted |
| Fix classes: ORPHANED removal | ⬜ Not started | |
| Fix classes: LEAKED (add framework guard) | ⬜ Not started | |
| Preview diff before apply | ⬜ Not started | |
| Phase 8 verification | ⬜ Not started | `dcs fix` removes one Trackdub WinUI duplicate; git diff shows only the removed line |

**Phase 8 gate:** Single DUPLICATE registration removed with correct syntax
edit; no other lines changed; `dcs analyze` shows one fewer duplicate afterward.

---

## Phase 9 — Runtime Enrichment Overlay

**Done means:** A dev-mode instrumented run annotates the static graph with
which types were actually resolved and whether any lifetime violations fired.

| Task | Status | Notes |
|------|--------|-------|
| ADR-008: Runtime instrumentation approach | ⬜ Not started | Key decisions: IServiceProvider wrapper vs DiagnosticSource vs OpenTelemetry; merge strategy; perf overhead target |
| Runtime collector — implement | ⬜ Not started | After ADR-008 accepted |
| Static+runtime graph merge | ⬜ Not started | |
| Lifetime violation detection (scoped-inside-singleton) | ⬜ Not started | |
| Phase 9 verification | ⬜ Not started | Trackdub dev run annotates ≥50% of static nodes as "resolved"; lifetime violation surfaced if any exist |

**Phase 9 gate:** Runtime-annotated IR contains `resolved_count` per node;
at least one static ORPHANED node reclassified as "resolved at runtime";
overhead <5% on Trackdub startup.

---

## Parked (no phase yet)

- TypeScript / Python parsers (Phase 6 Spring first; TS/Python follow same ADR pattern)
- Semantic Roslyn upgrade (replace syntactic short-name FQN with full resolved FQN — eliminates duplicate-ID collision class entirely; requires MSBuildWorkspace or in-memory compilation)
