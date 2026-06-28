# Dependency Chain Substrate — Design Document

Status: SKELETON. Each section has its structural questions inlined as
prompts (marked `> Q:`). Answers are not written yet. Fill sections in
dependency order (see `planning/00-plan-of-plan.md §Design doc structure`).

Do not fill a section until all upstream sections it depends on are answered.
Do not remove the `> Q:` prompts when filling — replace them with the answer
inline so the question/answer pair stays readable.

---

## 1. Problem & Primary Use Case

> Q: What is the single job-to-be-done v1 must nail? State it in one sentence.

> Q: What is the falsifiable value hypothesis? (i.e., "if we run X on Trackdub
>    at commit Y, we will find leakage Z that is known to exist")

> Q: What failure mode is this tool fixing? Be concrete: what did an agent (or
>    human) do that the tool would have caught?

> Q: What does "correct" mean for this tool? Who decides?

---

## 2. Goals / Non-Goals / Positioning

> Q: Verifier or general DI visualiser? What is the v1 headline? (See ADR-001
>    and wedge decision in plan-of-plan.)

> Q: Which languages and frameworks are explicitly IN scope for v1?

> Q: Which languages and frameworks are explicitly OUT for v1?

> Q: List the explicit non-goals for v1 (no runtime profiling, no auto-fix,
>    no IDE plugin, etc.).

> Q: How does this differ from existing tools (dotnet-trace, SonarQube,
>    NDepend, ArchUnitNET)? One sentence per comparison.

---

## 3. Users & Scenarios

> Q: Who is the primary user? (Concrete persona: role, context, what they
>    know, what they're trying to do mid-migration.)

> Q: Walk through the primary scenario step by step. What does the user type?
>    What does the tool output? What decision does the user make next?

> Q: Walk through the secondary scenario: agent-falsification. An agent has
>    claimed "migration complete." How does this tool contradict that claim?

> Q: What other concrete scenarios must the module specs satisfy?

---

## 4. Extraction Strategy (keystone)

Decision: see `docs/decisions/ADR-001-extraction-strategy.md`.

> Q: What registration call patterns are IN scope for static extraction in
>    v1? (List the concrete C# method signatures.)

> Q: What patterns are in a "degraded" state — detectable but with
>    unresolved dependencies? How will the output signal this?

> Q: What patterns are OUT of scope (explicitly accepted blind spots) for v1?
>    How will the output signal these blind spots rather than silently
>    dropping them?

> Q: How does Roslyn semantic vs syntactic parsing trade off for this use
>    case? Which is used, and why?

> Q: How does MSBuildWorkspace vs in-memory compilation trade off? Which is
>    used for git-blob extraction?

> Q: How are factory lambdas (`services.AddSingleton<IFoo>(sp => new Foo(sp.GetRequiredService<IBar>()))`)
>    handled? What is surfaced in output?

> Q: How are assembly-scanning registrations
>    (`services.AddServicesFromAssembly(...)`) handled?

> Q: How are conditional `#if` registrations handled?

> Q: How are keyed services (`services.AddKeyedSingleton<IFoo>("key", ...)`)
>    handled?

> Q: How are open generics (`services.AddSingleton(typeof(IGeneric<>), typeof(Generic<>))`)
>    handled?

> Q: How are decorator patterns (`services.Decorate<IFoo, FooDecorator>()`)
>    handled?

---

## 5. Shared Abstraction Model (IR)

Decision: see `docs/decisions/ADR-002-ir-identity-model.md`.

> Q: What are the universal IR primitives? Give precise names, field lists,
>    and field types for each.

> Q: What is the node identity model? (Primary key structure, stability
>    guarantees, cross-version semantics.)

> Q: How is rename vs delete+add distinguished during diff? Where does this
>    logic live — IR, diff engine, or both?

> Q: What is the serialisation schema? (Format, schema version field,
>    migration policy for schema changes.)

> Q: What validation was run against Spring to confirm cross-language fit?
>    What revisions were made as a result? (Fill after Spring spike.)

> Q: What are the explicitly accepted IR limitations for languages other than
>    C#/MS.DI?

> Q: What does `parser_confidence` look like in the schema? What values and
>    what semantics?

---

## 6. C# Language Parser

> Q: Which Roslyn API surface is used (CSharpSyntaxTree, SemanticModel,
>    ISymbol)? At what granularity are types resolved?

> Q: How is the Roslyn workspace loaded from git blob source (no .csproj build)?

> Q: What is the concrete catalog of registration patterns, their in/degraded/
>    out status, and the Roslyn query for each?

> Q: How are extension methods that wrap registration calls handled?
>    (e.g., `services.AddLogging()` which internally calls `AddSingleton<T>`)

> Q: How are bundling extensions that call other extensions transitively
>    handled?

> Q: What is the error model for parse failures (missing references, partial
>    source, syntax errors)?

---

## 7. Graph Analysis Layer

> Q: Give a precise, unambiguous definition of "orphaned" in this system.

> Q: Give a precise definition of "reachable."

> Q: Give a precise definition of "leaked" (crosses framework boundary with
>    explicit framework type exposed).

> Q: Give a precise definition of "broken chain" (dependency registered but
>    its dependency is not, or is ambiguous).

> Q: How is the composition root identified? Is it framework-specific?
>    Is it configurable?

> Q: How are cycles represented and reported?

> Q: What is the output format for analysis results (text, structured JSON,
>    annotated IR)?

---

## 8. Framework Boundary Model

> Q: How is a framework declared? (Namespace prefix, assembly name, NuGet
>    package ID — which of these, and in what priority order?)

> Q: Is this config-driven, heuristic-driven, or both?

> Q: What is the built-in set of framework boundary declarations for v1?
>    (WinUI, Avalonia, ASP.NET Core, MS.DI itself, etc.)

> Q: What does a "boundary crossing" look like in the IR? How is it
>    distinguished from an intentional abstraction?

---

## 9. Diff Engine

> Q: What is the stable node identity used for diff matching? (References
>    ADR-002 identity model.)

> Q: What is the rename/move detection algorithm? What threshold? What
>    computational complexity?

> Q: What change categories does the diff engine produce? (Added, Removed,
>    Renamed, LifetimeChanged, ImplementationChanged, etc.)

> Q: What makes a change "meaningful" vs "cosmetic"?

> Q: How is determinism of diff output guaranteed? (Ordering, stable IDs.)

> Q: What is the output format for a diff result?

---

## 10. Git Ingestion

> Q: Checkout-per-commit vs git blob reading — which, and why? (See ADR-001
>    for the constraint that rules this out.)

> Q: What library is used for git object access? (libgit2sharp is the
>    candidate.)

> Q: How is per-commit extraction cached? (Cache key = commit SHA + parser
>    version. Where stored? What invalidation policy?)

> Q: How are merge commits handled? (Which parent's tree is extracted?)

> Q: How are missing/deleted files at a given commit handled by the parser?

---

## 11. Visualisation & Delivery Form Factor

Decision: see `docs/decisions/ADR-003-form-factor.md`.

> Q: What is the minimal text output for Phase 1 CLI? (What fields, what
>    format, what sort order?)

> Q: What is the Phase 3 interactive visualisation? What rendering library?

> Q: How is legibility handled at 1000+ nodes? (Aggregation strategy,
>    focus+context, LOD.)

> Q: What is the export format from the CLI for downstream consumers?

---

## 12. Module Specs

> Q: For each of the six named modules, what is the MVP slice for v1?

Modules:
- **Registration Atlas** — IR + parser output consumer
- **Topology Lens** — viz layer, graph layout
- **Path Excavator** — viz layer, path queries
- **Framework Boundary Probe** — analysis layer, boundary detection
- **Drift Scanner** — diff engine + git, commit-to-commit changes
- **Migration Diff** — diff engine + git, migration-focused comparison

> Q: Map each module to the layers it depends on.

> Q: What are the module interfaces (inputs, outputs, failure modes)?

---

## 13. Cross-Cutting Concerns

> Q: How is determinism enforced across the full pipeline?

> Q: How are blind spots surfaced in every output format? (CLI, IR, viz.)

> Q: What is the logging/diagnostic strategy for parser failures?

> Q: What is the performance target for extraction? (Nodes/sec, acceptable
>    latency for a Trackdub-scale codebase.)

> Q: What is the error propagation model (fail-fast vs best-effort with
>    warnings)?

---

## 14. Extensibility / Plugin Contract

> Q: What is the parser interface? (Method signatures, capability
>    negotiation protocol.)

> Q: How does a parser signal "I cannot resolve this pattern"? What goes
>    into the IR for that node?

> Q: What is the IR contract version? How are breaking schema changes
>    handled? How are additive changes handled?

> Q: What is the minimum viable plugin API for a second language parser?

---

## 15. Phasing / Milestones

See `PLAN.md` for the live tracker. This section documents the architectural
"done" criteria that PLAN.md entries must satisfy.

> Q: What does Phase 1 "done" mean in terms of observable, checkable
>    behaviour on Trackdub? (Not just "tests pass" — what does the tool
>    print, and what is the known-correct answer?)

> Q: What does Phase 2 "done" mean for a specific Trackdub commit pair?

> Q: What is the minimum Phase 3 bar for "legible at full scale"? (Node
>    count, interaction requirement.)

---

## 16. Risks & Open Questions

> Q: What are the top-5 risks to Phase 1 succeeding? Tag each with the
>    resolver (human decision, spike, implementation discovery).

> Q: What open questions remain after the four ADRs are closed?

> Q: What assumptions are load-bearing but unvalidated? (These are
>    falsification targets.)

---

## 17. Validation / Test Corpus

> Q: What is the specific Trackdub scenario that constitutes Phase 1
>    acceptance? (Commit range, known-leaking registration, expected output.)

> Q: What is the dogfood plan for Phase 3? (Who runs it, against what, by
>    when?)

> Q: What second corpus (beyond Trackdub) should be considered for
>    cross-validation?
