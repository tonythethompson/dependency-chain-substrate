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
| DESIGN.md §4 (Extraction Strategy) — fill answers | ✅ Done | 2026-06-28 — backfilled from implementation + ADR-001 |
| DESIGN.md §5 (IR + Identity) — fill answers | ✅ Done | 2026-06-28 — backfilled from DCS.Core.IR + ADR-002 |
| DESIGN.md §1-3 (Problem, Goals, Users) — fill answers | ✅ Done | 2026-06-28 — backfilled from plan-of-plan + Trackdub verification |

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
| DESIGN.md §9-10 — fill | ✅ Done | 2026-06-28 — GraphDiffer + CSharpStaticParser |
| Git blob reader (libgit2sharp) — implement | ✅ Done | 2026-06-28 — CSharpStaticParser.ParseCommit |
| Per-commit extraction cache (keyed by SHA) — implement | ✅ Done | 2026-06-28 — ExtractionCache in DCS.Core; `--cache-dir` / `--no-cache` |
| Diff engine + rename detection — implement | ✅ Done | 2026-06-28 — DCS.Diff |
| CLI `diff` command — implement | ✅ Done | 2026-06-28 |
| Phase 2 verification against Trackdub | ✅ Done | 2026-06-28 — diff 3c4e374d→316614b8 correctly shows MainWindow+MainWindowViewModel removed; breaking changes detected |

**Phase 2 gate:** ✅ CLOSED — diff engine verified against Trackdub WinUI retire commits.

---

## Phase 3 — Visualisation + Form Factor

**Done means:** Legible interactive view of Trackdub's graph at full scale.

| Task | Status | Notes |
|------|--------|-------|
| DESIGN.md §11 — fill | ✅ Done | 2026-06-28 — HtmlVizGenerator + CLI |
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
| Registration Atlas polish | ✅ Done | 2026-06-28 — `dcs atlas` command |
| Boundary Probe config UX | ✅ Done | 2026-06-28 — `--frameworks <json>` flag |
| Phase 4 verification | ✅ Done | 2026-06-28 — exit code 1 on analyze 3c4e374d (4 broken chains); exit code 1 on diff 3c4e374d→316614b8 (breaking changes) |

**Phase 4 gate:** ✅ CLOSED — CI gate verified on real Trackdub commits.

---

---

## Phase 5 — Near-Term Enablers + Documentation Backfill

**Done means:** DESIGN.md fully answered; `--frameworks` config works on real
Trackdub; disk cache eliminates redundant re-extraction on repeated CLI runs.

| Task | Status | Notes |
|------|--------|-------|
| DESIGN.md §§1-3 (Problem, Goals, Users) — fill | ✅ Done | 2026-06-28 |
| DESIGN.md §§4-5 (Extraction, IR) — fill | ✅ Done | 2026-06-28 |
| DESIGN.md §§9-10 (Diff Engine, Git Ingestion) — fill | ✅ Done | 2026-06-28 |
| DESIGN.md §§11-17 (Viz, Modules, Cross-cutting, Extensibility, Phasing, Risks, Validation) — fill | ✅ Done | 2026-06-28 |
| `--frameworks <json>` — implement | ✅ Done | 2026-06-28 — FrameworkBoundaryModel.LoadFromJson + CLI wiring |
| Per-commit disk cache — implement | ✅ Done | 2026-06-28 — ExtractionCache; default %LOCALAPPDATA%/dependency-chain-substrate |
| Registration Atlas polish — implement | ✅ Done | 2026-06-28 — `dcs atlas` human-readable listing |
| Rename weight tuning | ⬜ Blocked | Needs a Trackdub commit pair with known renames; unblocked when such a pair is identified |

**Phase 5 gate:** ✅ CLOSED — DESIGN.md fully answered; `--frameworks` on analyze/diff/atlas; disk cache on repeated `analyze --commit`.

---

## Phase 6 — Second Language Parser (Spring)

**Done means:** A Spring Boot project produces a valid IR graph. DUPLICATE and
LEAKED detection work on a real Spring Boot repo with multiple framework contexts.

| Task | Status | Notes |
|------|--------|-------|
| ADR-005: Spring parser scope and approach | ✅ Done | 2026-06-28 — Accepted; tree-sitter primary |
| ParseResult bundle + ContextGraph IR | ✅ Done | 2026-06-29 — Multi-context bundle; schema 1.2.0 |
| Tree-sitter Java parse layer | ✅ Done | 2026-06-29 — TreeSitter.DotNet 1.3.0; JavaCompilationUnit |
| Context discovery (scan, @Import, Spring Data) | ✅ Done | 2026-06-29 — Default repo scan from @SpringBootApplication package |
| @Bean / @Configuration / stereotype extraction | ✅ Done | 2026-06-29 — PrimaryBeanName, aliases, @Scope, FactoryProvenance |
| @Autowired constructor/field edge extraction | ✅ Done | 2026-06-29 — Single-ctor inference; ConservativeEdgeResolver |
| @Conditional / Spring Data → degraded confidence | ✅ Done | 2026-06-29 — Spring Data repos DEGRADED; conditional injections |
| IStaticParser → ParseResult | ✅ Done | 2026-06-29 — C# parser wraps single-graph bundle |
| CLI `--language java` + auto-detect | ✅ Done | 2026-06-29 — analyze/atlas/diff/viz route to SpringStaticParser |
| Phase 6 verification | ✅ Done | 2026-06-29 — PetClinic pinned SHA gate + analysis (no leaked/broken) |

**Phase 6 gate:** ✅ CLOSED — Spring PetClinic IR contains ≥10 singleton nodes,
@Autowired wiring edges present; Spring Data repos show DEGRADED confidence.

---

## Phase 7 — IDE Extension (deferred)

**Status:** Deferred until after Phase 8/9. CLI-first delivery remains the primary
form factor (ADR-003). IDE extension is a thin consumer once fix + analysis stabilize.

**Done means:** A VS Code extension shows inline error badges on DI registration
call sites without leaving the editor.

| Task | Status | Notes |
|------|--------|-------|
| ADR-006: IDE integration form factor | ⬜ Deferred | After Phase 8/9 |
| Extension scaffold | ⬜ Deferred | |
| On-save analysis trigger | ⬜ Deferred | |
| Inline diagnostic decorations (LEAKED, BROKEN, DUPLICATE) | ⬜ Deferred | |
| IR cache reuse within IDE session | ⬜ Deferred | |
| Phase 7 verification | ⬜ Deferred | Inline badge on Trackdub without CLI |

**Phase 7 gate:** LEAKED badge appears inline within 5 seconds of opening
Trackdub in VS Code; no false positive on clean commit.

---

## Phase 8 — Auto-fix / Codemod

**Done means:** `dcs fix` applies at least one safe fix class (DUPLICATE
removal) with a preview diff and rollback via git.

| Task | Status | Notes |
|------|--------|-------|
| ADR-007: Write-back safety model | ✅ Done | 2026-06-29 — Accepted; DUPLICATE-only v1 |
| DCS.Fix — DUPLICATE removal | ✅ Done | 2026-06-29 — Roslyn statement removal + preview/apply |
| CLI `dcs fix` | ✅ Done | 2026-06-29 — `--preview` default, `--apply`, `--token`, `--force` |
| Fix classes: ORPHANED removal | ⬜ Deferred | v1.1 after false-positive measurement |
| Fix classes: LEAKED (add framework guard) | ⬜ Deferred | |
| Phase 8 verification | ✅ Done | 2026-06-29 — Fixture + Trackdub optional gate |

**Phase 8 gate:** ✅ CLOSED — `dcs fix --apply` removes one DUPLICATE registration;
preview diff shows only the removed line; analyze shows one fewer duplicate group.

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

---

## Phase 10 — Semantic Roslyn Type Resolution

**Done means:** Per-project semantic compilation resolves registration and constructor
types; strict DUPLICATE uses `DuplicateGroupKey`; Trackdub CI gate passes at pin
`3c4e374d`.

| Task | Status | Notes |
|------|--------|-------|
| ADR-009: Semantic Roslyn resolution | ✅ Draft (Proposed) | Human sign-off pending → Accepted |
| ADR-002 amendment: identity 1.2.0 | ✅ Draft (Proposed) | |
| IR schema 1.2.0 (`ResolvedTypeIdentity`, quality dimensions) | ✅ Done | |
| `ProjectTargetScope` discovery + compilation factory | ✅ Done | |
| `ReferenceProfileProvider` + closure order | ✅ Done | |
| Semantic visitors + unresolved injections | ✅ Done | Parser 0.2.0 |
| GraphAnalyzer strict/possible duplicates | ✅ Done | |
| GraphDiffer hybrid matching | ✅ Done | |
| Fix engine instance-id alignment | ✅ Done | |
| CLI `--target-framework` / `--all-target-frameworks` | ✅ Done | |
| Unit semantic fixtures | ✅ Done | `SemanticResolutionTests` |
| Trackdub mandatory CI gate | ✅ Done | `trackdub-semantic` job |
| DESIGN.md §5 + §6 update | ✅ Done | |
| Phase 10 Verified (Trackdub metrics) | ⬜ Pending | Blocked on ADR Accepted + CI green on main |

**Phase 10 gate:** `trackdub-semantic` CI job passes; `semantic_type_resolution_rate`,
`registration_api_verification_rate`, and `project_scope_completeness_rate` reported;
VoiceCloneConsentCoordinator strict duplicate detected at pin.

---

## Parked (no phase yet)

- TypeScript / Python parsers (Phase 6 Spring first; TS/Python follow same ADR pattern)
