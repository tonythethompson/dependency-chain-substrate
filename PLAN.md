# Dependency Chain Substrate — Milestone Tracker

Last updated: 2026-06-30 (Phase 13 closed)

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
| Fix classes: ORPHANED removal | ✅ Done | 8.1a preview, 8.1b apply |
| Fix classes: LEAKED (add framework guard) | ⬜ Deferred | Codemod; not the apply safety guard |
| Fix classes: BROKEN (factory → explicit) | ✅ Done | 8.1d — simple shallow factory only |
| LEAKED guard on `--apply` | ✅ Done | 8.1c — re-analyze + rollback if leakage worsens |
| Phase 8 verification | ✅ Done | 2026-06-29 — Fixture + Trackdub optional gate |

**Phase 8 gate:** ✅ CLOSED — `dcs fix --apply` removes one DUPLICATE registration;
preview diff shows only the removed line; analyze shows one fewer duplicate group.

---

## Phase 9 — Runtime Enrichment Overlay

**Done means:** A dev-mode instrumented run annotates the static graph with
which types were actually resolved and whether any lifetime violations fired.

| Task | Status | Notes |
|------|--------|-------|
| ADR-008: Runtime instrumentation approach | ✅ Accepted | DiagnosticSource + JSONL file log; merge via `dcs enrich` |
| Runtime collector — implement | ✅ Done | `DcsRuntimeEventListener`, `RuntimeLogWriter` |
| Static+runtime graph merge | ✅ Done | `RuntimeGraphEnricher` — annotations, blind-spot upgrade, orphaned reclassification |
| Lifetime violation detection (scoped-inside-singleton) | ✅ Done | `CaptiveDependencyFinding` from caller_lifetime + lifetime |
| CLI `dcs enrich` | ✅ Done | `--runtime-log`, `--out`, optional `--frameworks` / `--root` |
| Unit tests | ✅ Done | `DCS.Runtime.Tests`, `EnrichCommandTests` |
| Phase 9 verification | ✅ Done | 2026-07-01 — Trackdub @ pin: 299/335 annotated (89.3%); fixture `runtime-3c4e374d.jsonl`; 0 orphaned at pin |

**Phase 9 gate:** ✅ CLOSED — runtime-annotated IR contains `runtime_resolved_count`; ≥50% nodes annotated on Trackdub aggregate graph; orphan reclassification N/A (0 orphaned at pin).

---

---

## Phase 10 — Semantic Roslyn Type Resolution

**Done means:** Per-project semantic compilation resolves registration and constructor
types; strict DUPLICATE uses `DuplicateGroupKey`; Trackdub CI gate passes at pin
`3c4e374d`.

| Task | Status | Notes |
|------|--------|-------|
| ADR-009: Semantic Roslyn resolution | ✅ Accepted | 2026-06-30 |
| ADR-002 amendment: identity 1.2.0 | ✅ Accepted | 2026-06-30 |
| IR schema 1.2.0 (`ResolvedTypeIdentity`, quality dimensions) | ✅ Done | |
| `ProjectTargetScope` discovery + compilation factory | ✅ Done | |
| `ReferenceProfileProvider` + closure order | ✅ Done | |
| Semantic visitors + unresolved injections | ✅ Done | Parser 0.2.0 |
| GraphAnalyzer strict/possible duplicates | ✅ Done | |
| GraphDiffer hybrid matching | ✅ Done | |
| Fix engine instance-id alignment | ✅ Done | |
| CLI `--target-framework` / `--all-target-frameworks` | ✅ Done | |
| Unit semantic fixtures | ✅ Done | `SemanticResolutionTests` |
| Trackdub mandatory CI gate | ✅ Done | `trackdub-semantic` job green on PR #1 (run 28415499373) |
| DESIGN.md §5 + §6 update | ✅ Done | |
| Phase 10 Verified (Trackdub metrics) | ✅ Done | 2026-06-30 — local gate: 54.4% / 100% / 100% (333 nodes, 6 scopes); VoiceClone file:line in WinUI+Avalonia shells |

**Phase 10 gate:** ✅ CLOSED — `trackdub-semantic` CI green on main (merge `4644863`); metrics at pin `3c4e374d23fe3941ed7ca376775937941973b313`: semantic 54.4%, API verification 100%, scope completeness 100%; VoiceCloneConsentCoordinator possible duplicate with file:line sites.

---

## Phase 10b — Actionable CLI

**Done means:** Structured analysis report with tiered findings, enriched text output,
generalized `FindingPolicy`, parser 0.3.0 instance + shallow factory patterns, and
fixture/Trackdub gates asserting file:line sites.

| Task | Status | Notes |
|------|--------|-------|
| `AnalysisReport` + builder + printer | ✅ Done | Tiers, sites, summary |
| CLI flags (`--verbosity`, `--strict`, `--format json`, etc.) | ✅ Done | |
| `analysis-report-1.0.json` schema | ✅ Done | `docs/schemas/` |
| `FindingPolicy` (replaces `AnalysisNoise`) | ✅ Done | Convention-based reachability roots |
| Parser 0.3.0: instance TryAdd + shallow factory | ✅ Done | |
| `--context all` multi-context summary | ✅ Done | |
| Fixture + golden CLI/JSON tests | ✅ Done | `tests/fixtures/di-patterns/` |
| Trackdub gate file:line assertions | ✅ Done | VoiceClone sites |
| DESIGN.md §11 update | ✅ Done | |

**Phase 10b gate:** ✅ CLOSED — merged PR #1 (`4644863`); `dotnet test` green; Trackdub @ pin report lists VoiceClone homonym sites with `file:line`; `--format json --report-out` validates against `analysis-report-1.0.json`.

**CI contributor note:** The `trackdub-semantic` job checks out the private Trackdub repo at pin `3c4e374d23fe3941ed7ca376775937941973b313` into `dcs-trackdub-pin/` (gitignored locally). Add a GitHub repository secret `TRACKDUB_PAT` with a PAT that can read `tonythethompson/Trackdub`. Locally, set `TRACKDUB_PATH` to your Trackdub clone (see `.claude/settings.local.json`) or clone into `dcs-trackdub-pin/` at the pin SHA.

---

## Phase 11 — Parser fidelity and ground-truth hardening

**Done means:** Trackdub @ pin shows measurably better extraction quality; Avalonia
shell `MainWindow` block factory is recognised (not `unrecognized_pattern`); gate
floors raised from Phase 10c baseline; technical debt cleaned.

| Task | Status | Notes |
|------|--------|-------|
| Investigate `app.axaml.cs` shell composition blind spots | ✅ Done | Root cause: block-bodied `AddSingleton(sp => { return new MainWindow(...); })` |
| `ShallowFactoryLambdaExtractor` block-lambda support | ✅ Done | Parser 0.3.1 |
| Fixture regression (`DiPatternRegistrations` + unit tests) | ✅ Done | Block factory lambda tests |
| Raise `TrackdubSemanticGateTests` metric floors | ✅ Done | Semantic 54%, API 95%, scope 80% |
| Avalonia `MainWindow` shell gate assertion | ✅ Done | No `unrecognized_pattern` at shell site |
| `ComputeId` → `ComputeRegistrationInstanceId` migration | ✅ Done | Test + Java parser helpers |
| `.gitignore` `dcs-trackdub-pin/` | ✅ Done | |
| `TRACKDUB_PAT` contributor docs | ✅ Done | See Phase 10b CI note above |
| DESIGN.md §6 factory-lambda catalog | ✅ Done | Block-bodied shallow extraction |
| Phase 11 Verified (Trackdub metrics) | ✅ Done | 2026-06-30 — 54.6% / 100% / 100% (335 nodes, 6 scopes); MainWindow shallow factory registered |

**Phase 11 gate:** ✅ CLOSED — `dotnet test` green; Trackdub @ pin `3c4e374d23fe3941ed7ca376775937941973b313`: semantic 54.6% (+0.2pp vs Phase 10c baseline 54.4%), API verification 100%, scope completeness 100%; Avalonia `MainWindow` block factory recognised as `factory_lambda_shallow` (not `unrecognized_pattern`); VoiceClone file:line assertions unchanged. **Aspirational +5pp semantic target (~59.4%) deferred** — remaining blind spots are mostly `factory_lambda` / `factory_lambda_shallow` at other sites, not shell composition.

---

## Phase 12 — Semantic resolution hardening (windows TFM + factory deps)

**Done means:** Windows TFM semantic cliff closed via cross-TFM project-reference compilation
closure; factory-lambda `GetRequiredService` deps traced; aggregate + per-context gate floors raised.

| Task | Status | Notes |
|------|--------|-------|
| Investigate windows TFM semantic cliff (15.9% vs 91.8% portable) | ✅ Done | Root cause: portable-only deps not compiled into windows TFM graph |
| `CrossTfmProjectReferenceResolver` scope expansion | ✅ Done | MSBuild-compatible portable→windows ref fallback |
| Fix `ReferenceProfileProvider` ref-pack paths (`{version}/ref/{tfm}`) | ✅ Done | + WindowsDesktop pack for `-windows` TFMs |
| Factory-lambda `GetRequiredService` edge tracing | ✅ Done | Parser 0.3.2; `factory_lambda_service_keys` annotation |
| Per-context gate metrics + raised floors (57% aggregate, 40% windows) | ✅ Done | Actual: 91.6% aggregate, 91.5% windows |
| Phase 12 Verified (Trackdub metrics) | ✅ Done | 2026-06-30 — 91.6% / 100% / 100% (335 nodes, 6 scopes) |

**Phase 12 gate:** ✅ CLOSED — `dotnet test` green; Trackdub @ pin: aggregate semantic **91.6%**, windows TFM **91.5%**, portable **91.8%**; MainWindow + VoiceClone assertions unchanged; cross-TFM compilation closure verified via `CrossTfmProjectReferenceResolverTests`.

---

## Phase 13 — Path Excavator MVP

**Done means:** `dcs path` answers dependency paths on extracted graphs; Trackdub gate
confirms path to Avalonia shell registration; semantic floors locked at Phase 12 levels.

| Task | Status | Notes |
|------|--------|-------|
| `GraphPathFinder` (BFS on dependency edges) | ✅ Done | `DCS.Analysis` |
| `PathExcavationReport` + text/json output | ✅ Done | |
| `dcs path` CLI (`--from`, `--to`, `--format json`) | ✅ Done | |
| Unit tests (`GraphPathFinderTests`) | ✅ Done | |
| Trackdub path gate (`TrackdubPathGateTests`) | ✅ Done | Avalonia VoiceClone by id |
| Raise semantic gate floors to 85%/80% | ✅ Done | |
| DESIGN.md §12 Path Excavator update | ✅ Done | |

**Phase 13 gate:** ✅ CLOSED — `dotnet test` green; `GraphPathFinder.FindPath` succeeds for Avalonia `VoiceCloneConsentCoordinator` @ pin; semantic floors ≥85% aggregate; viz path highlight deferred.

---

## Phase 13b — Viz path highlight

**Done means:** `dcs viz --path-to` highlights the same dependency path as `dcs path` in the HTML canvas.

| Task | Status | Notes |
|------|--------|-------|
| `VizPathHighlight` model + `HtmlVizGenerator` draw pass | ✅ Done | Cyan path edges/nodes; dim non-path |
| `dcs viz --path-to` / `--path-from` CLI flags | ✅ Done | Reuses `GraphPathFinder` |
| Sidebar path panel in HTML | ✅ Done | Hop list |
| Unit tests (`HtmlVizPathHighlightTests`) | ✅ Done | |
| DESIGN.md §12 Path Excavator update | ✅ Done | Viz highlight shipped |

**Phase 13b gate:** ✅ CLOSED — `dotnet test` green; HTML embeds `PATH_HIGHLIGHT` payload matching `dcs path` node/edge ids.

---

## Phase 8.1a — ORPHANED measurement + preview-only fix

**Done means:** `dcs fix --fix-class orphaned --preview` reports FP measurement and unified diff; `--apply` rejected until ADR-007 amendment.

| Task | Status | Notes |
|------|--------|-------|
| `OrphanedFixMeasurement` report | ✅ Done | total / explicit / eligible counts |
| `OrphanedFixPlanner` (explicit, non-root, non-infra) | ✅ Done | Composition root excluded |
| `FixEngine.BuildOrphanedFixes` preview | ✅ Done | No `--apply` in 8.1a |
| CLI `--fix-class orphaned` | ✅ Done | Rejects `--apply` |
| Fixture + Trackdub measurement tests | ✅ Done | `OrphanedFixTests` |
| ADR-007 amendment (orphaned apply gate) | ✅ Accepted | 2026-07-01 — Phase 8.1b |

**Phase 8.1a gate:** ✅ CLOSED (preview) — fixture orphan preview diff; Trackdub measurement documents eligible count.

---

## Phase 8.1b — ORPHANED fix `--apply`

**Done means:** `dcs fix --fix-class orphaned --apply` removes eligible orphaned registrations with DUPLICATE-equivalent git guards.

| Task | Status | Notes |
|------|--------|-------|
| ADR-007 amendment (orphaned apply) | ✅ Accepted | 2026-07-01 — eligibility + git guards |
| `FixEngine.ApplyOrphanedFixes` | ✅ Done | Mirrors duplicate apply path |
| CLI orphaned `--apply` | ✅ Done | `--force` for dirty tree |
| Fixture apply + re-analyze test | ✅ Done | `Apply_removes_orphan_and_analyze_shows_one_fewer_eligible` |
| Trackdub apply verification | ⬜ N/A @ pin | 0 orphaned at `3c4e374d`; fixture gate suffices |

**Phase 8.1b gate:** ✅ CLOSED — fixture apply removes `IOrphanService`; eligible orphan count drops by 1; dirty-tree guard enforced.

---

## Phase 8.1c — LEAKED guard on `--apply`

**Done means:** Duplicate and orphaned `--apply` re-analyze after write; rollback if LEAKED worsens.

| Task | Status | Notes |
|------|--------|-------|
| `FixSafetyGuard` (compare + rollback) | ✅ Done | New leaked node ids or higher count |
| Wire into `dcs fix --apply` | ✅ Done | Duplicate + orphaned |
| Unit + integration tests | ✅ Done | `FixSafetyGuardTests` |

**Phase 8.1c gate:** ✅ CLOSED — duplicate fixture apply passes guard; synthetic worsened LEAKED triggers rollback.

---

## Phase 8.1d — BROKEN fix (factory → explicit)

**Done means:** `dcs fix --fix-class broken` converts eligible `factory_lambda_shallow` blind spots (resolved `concrete_impl`, no `GetRequiredService` in lambda) to explicit `TryAdd*` / `Add*` registrations; apply guards include BROKEN rollback.

| Task | Status | Notes |
|------|--------|-------|
| `BrokenFixPlanner` + `FactoryLambdaToExplicitConverter` | ✅ Done | Roslyn statement replace |
| CLI `dcs fix --fix-class broken` | ✅ Done | `--preview` / `--apply` |
| BROKEN apply guard | ✅ Done | `FixSafetyGuard.VerifyApplyGuards` |
| Unit + integration tests | ✅ Done | `BrokenFixTests` |

**Phase 8.1d gate:** ✅ CLOSED — fixture broken chain cleared after apply; Trackdub @ pin has 0 eligible (complex factories).

---

## Parked (no phase yet)

- TypeScript / Python parsers (Phase 6 Spring first; TS/Python follow same ADR pattern)
