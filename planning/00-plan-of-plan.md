# Dependency Chain Substrate — Plan of the Plan

Status: planning scaffolding. This is not the design doc. It defines the design doc's
structure, the cross-cutting concerns, the phasing, the model-routing matrix, and the
gating decisions that must be resolved before the design doc is drafted.

Origin: born from a Trackdub WinUI-to-Avalonia migration where the full DI dependency
graph was invisible, migrated vs orphaned vs still-wired state was untrackable, and
agents falsely claimed completeness. The tool exists to be the thing that contradicts a
false "we're good."

---

## Keystone

The extraction strategy (static vs runtime vs hybrid) is the keystone fork, and the diff
features silently constrain it. Drift Scanner and Migration Diff must compare arbitrary
git commits. Runtime extraction of a historical commit means checking it out and building
it, which is impractical at diff scale and pollutes the working tree. Committing to
diffing as a core feature largely commits the project to static-first extraction. Decide
this consciously and early. Everything downstream sits below it.

---

## Four gating decisions (resolve before drafting the design doc)

1. **Wedge:** migration verifier or general DI visualizer as the v1 headline. The
   verifier is novel and tied to the real pain; the Framework Boundary Probe is the
   differentiated part. General topology is the substrate, not the headline.
2. **Extraction:** static, runtime, or hybrid. The diff features point hard at static-first.
3. **Form factor:** Avalonia desktop, web viewer, CLI-first, or IDE extension. CLI-first
   plus a serialized IR keeps the CI-gate future open and defers the viz ecosystem fork.
4. **Spring paper-spike before IR freeze:** the cheapest insurance against designing the
   abstraction on a sample size of one.

---

## Design doc structure (dependency order)

Order below is roughly design order; each section depends on the ones above. Users and
Positioning come early because they decide what "correct" means, not because of technical
dependency.

1. **Problem & Primary Use Case** — single job-to-be-done v1 must nail; falsifiable value
   hypothesis (reproduce known Trackdub leakage); the failure mode being fixed.
2. **Goals / Non-Goals / Positioning** — verifier vs generic visualizer; languages and
   frameworks explicitly out for v1; explicit non-goals (no runtime profiling, no
   auto-fix, no IDE plugin v1).
3. **Users & Scenarios** — concrete walkthroughs for the solo dev mid-migration; the
   scenarios the module specs must satisfy.
4. **Extraction Strategy (keystone)** — static/runtime/hybrid; handling of factory
   lambdas, assembly scanning, open generics, conditional/`#if` registration, bundling
   extensions, keyed services, decorators; the explicitly accepted blind-spot set.
5. **Shared Abstraction Model (IR)** — universal primitives (registration, edge, lifetime,
   scope, identity); node identity model (load-bearing for diffing); serialization schema
   and versioning; validation against a structurally different second language on paper.
6. **C# Language Parser** — Roslyn semantic vs syntactic; MSBuildWorkspace vs in-memory
   compilation; the concrete catalog of registration patterns in/degraded/out for v1.
7. **Graph Analysis Layer** — precise definitions of orphaned, reachable, leaked, broken
   chain; composition root identification (framework-specific, configurable); cycle
   handling.
8. **Framework Boundary Model** — how a framework is declared (namespace, assembly,
   package); config-driven vs heuristic.
9. **Diff Engine** — node identity across versions; rename and move detection; meaningful
   vs cosmetic change; determinism and stable ordering.
10. **Git Ingestion** — checkout-per-commit vs blob reading (libgit2sharp); per-commit
    extraction caching keyed by SHA.
11. **Visualization & Delivery Form Factor** — desktop vs web vs CLI vs IDE extension;
    graph rendering tech; legibility at 1000+ nodes (aggregation, focus+context, LOD).
12. **Module Specs** — map the six named modules onto the layers; MVP slice of each.
13. **Cross-Cutting Concerns** — see below.
14. **Extensibility / Plugin Contract** — parser interface; capability negotiation (no
    lifetimes, annotation-based scanning); IR contract versioning.
15. **Phasing / Milestones** — explicit "done" per phase.
16. **Risks & Open Questions** — tagged by resolver.
17. **Validation / Test Corpus** — Trackdub as dogfood ground truth.

### Module-to-layer map

- Registration Atlas ≈ IR + parser output
- Topology Lens / Path Excavator ≈ viz layer
- Framework Boundary Probe ≈ analysis layer
- Drift Scanner / Migration Diff ≈ diff engine + git

---

## Cross-cutting concerns and gotchas

1. **Abstraction designed on sample size of one.** Only C#/MS.DI is in hand, but the bet
   is generalization to TS/Python/Java. Semantics diverge hard (Spring annotations +
   component scanning + prototype/request/session lifetimes; most Python has no container
   and no lifetimes). Mitigation: paper parser spike against the most different target
   (Spring) before freezing the IR.
2. **Static extraction cannot see dynamic registration, and that is where bugs hide.**
   Factory lambdas hide dependencies in the body; assembly scanning has no explicit call
   to find. Surface blind spots in output ("detected but dependencies unresolved") rather
   than silently dropping, or the tool reproduces the failure it exists to kill.
3. **Node identity is the hidden spine of diffing.** Naive identity turns a rename/move
   into delete+add and floods the diff with noise. Rename detection is a known-hard
   problem. Design identity with diffing in mind in the IR section, not as an afterthought.
4. **Determinism is a correctness requirement.** Stable IDs, ordering, reproducible output.
5. **IR serialization is load-bearing early.** Both caching and the viz consumer read it.
   Treat its schema as a public contract; stabilize and version before diff or viz.
6. **Roslyn project loading is operationally hostile.** MSBuildWorkspace needs SDK +
   restore and fails confusingly; git blob reading gives source with no built project. The
   collision likely forces in-memory compilation from source.
7. **CI-gate consumer is the automated form of the origin story.** Keep the
   extraction/analysis core transport-agnostic and the viz one consumer of the serialized
   IR, so a CLI/CI gate comes nearly free later. P2 that constrains v1 architecture.
8. **Framework membership needs a taxonomy.** Leakage detection presupposes the tool knows
   which namespace/assembly belongs to which framework. Config plus heuristics; the
   differentiated core of the Boundary Probe.
9. **Scale breaks naive visualization.** Plan aggregation and focus+context from the start.

---

## Phasing

```
Positioning/wedge decision  ─┐
Extraction strategy fork    ─┤ (precede everything technical)
                             ▼
Shared IR + identity + serialization  ──► validated by paper Spring spike
                             ▼
C# parser (concrete, on the IR)
                             ▼
Graph analysis layer (orphan/leakage/reachable)  ──► Boundary taxonomy
                  ┌──────────┴───────────┐
                  ▼                      ▼
        Git ingestion + Diff      Viz + form-factor fork
                  ▼                      ▼
        Drift / Migration Diff    Topology Lens / Path Excavator
```

Thin vertical slice that proves the wedge (Phase 1 "done"): C# static extraction → IR →
leakage/orphan detection → minimal text output, run against Trackdub's real git history,
reproducing the WinUI leakage known to be there. If that slice cannot reproduce known
ground truth, the rest does not matter.

| Phase | Scope | "Done" means |
|-------|-------|--------------|
| 0 | Positioning + extraction fork + IR draft | Decisions written, Spring paper-spike done, IR survives it or is revised |
| 1 | C# parser + analysis + leakage detection, CLI/text output | Reproduces known Trackdub leakage on real commits; blind spots documented |
| 2 | Git ingestion + diff engine | Drift Scanner shows meaningful, low-noise diffs between two Trackdub commits |
| 3 | Viz + form-factor + Topology Lens / Path Excavator | Legible interactive view of Trackdub's graph at full scale |
| 4 | Registration Atlas polish, Boundary Probe config UX, CI-gate consumer | Non-interactive gate runnable in CI |

Deferred but designed-around: second language parser, IDE extension, auto-fix/codemod,
runtime enrichment.

---

## Agent routing matrix

Opus/high-effort rows are the parts to own and integrate directly, not hand to a subagent
to "just execute," because a wrong call there is expensive to unwind.

| Design task | Model | Effort | Why |
|-------------|-------|--------|-----|
| Extraction strategy fork | Opus | High | Irreversible keystone with derived constraints |
| IR + identity model + cross-language validation | Opus | High | Central bet; generalization on sample size one |
| Diff identity / rename-move detection | Opus | High | Known-hard; decides whether diffs are usable |
| Viz / form-factor strategic fork | Opus | Med-High | Ecosystem lock-in; somewhat irreversible |
| Plugin contract + IR versioning | Opus | Med-High | Contract expensive to change once parsers exist |
| Framework boundary taxonomy | Opus consult, Sonnet draft | Med | Subtle at edges, bounded scope |
| Graph analysis definitions | Sonnet | High | Precise-definition work; Opus if reachability gets subtle |
| C# parser pattern catalog | Sonnet | Med-High | Enumeration + judgment; Opus for factory-lambda resolution |
| Git ingestion design | Sonnet | Med | Known patterns (libgit2sharp blob read) |
| Module specs | Sonnet | Med | Synthesis once layers fixed |
| Test corpus / dogfood plan | Sonnet | Med | Concrete and bounded |
| Doc drafting / synthesis | Sonnet | Low-Med | Iteration and formatting |
| Risks / open-questions consolidation | Sonnet | Low | Aggregation |
