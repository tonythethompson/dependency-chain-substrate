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

**Roslyn API:** Syntactic API only for Phase 1 (`CSharpSyntaxTree.ParseText`,
`CSharpSyntaxWalker`). No `SemanticModel` or `ISymbol` — avoids requiring
MSBuildWorkspace and bundled reference assemblies. Type names are the short
names from generic type arguments; namespace resolution uses `using` directives
collected per file. Semantic model deferred to Phase 2 for factory lambda body
analysis and cross-file type resolution.

**Workspace loading:** LibGit2Sharp reads each `.cs` file as a blob from the
git object store at the target commit SHA. Each blob content →
`CSharpSyntaxTree.ParseText(content, path: filePath)`. No compilation object
needed for syntactic walking. Files collected by recursive tree walk of the
commit's tree object, filtered to `*.cs`.

**Pattern catalog:**

| Pattern | Status | Roslyn query |
|---------|--------|-------------|
| `services.AddSingleton<IFoo, FooImpl>()` | EXPLICIT | `InvocationExpressionSyntax` with method name in `AddList`, two generic type args |
| `services.AddScoped<IFoo, FooImpl>()` | EXPLICIT | same |
| `services.AddTransient<IFoo, FooImpl>()` | EXPLICIT | same |
| `services.AddSingleton<FooImpl>()` | EXPLICIT | one generic type arg → AbstractToken = ConcreteImpl |
| `services.AddSingleton(typeof(IFoo), typeof(FooImpl))` | EXPLICIT | two `TypeOfExpressionSyntax` arguments |
| `services.AddSingleton(typeof(IGeneric<>), typeof(Generic<>))` | EXPLICIT | open generic typeof args |
| `services.AddKeyedSingleton<IFoo, FooImpl>("key")` | EXPLICIT | two generic args + string literal → `annotations["service_key"]` |
| `services.TryAddSingleton<IFoo, FooImpl>()` | EXPLICIT | same + `annotations["conditional"]="TryAdd"` |
| `services.AddSingleton<IFoo>(new FooImpl(...))` | DEGRADED | object creation arg — AbstractToken known, ConcreteImpl null |
| `services.Add(new ServiceDescriptor(typeof(IFoo), typeof(FooImpl), ...))` | DEGRADED | ServiceDescriptor ctor pattern |
| `services.AddLogging()`, `services.AddHttpClient()`, etc. | DEGRADED | Any `Add*` call not in known list → `BlindSpotReport{pattern:"extension_method"}` |
| `services.AddSingleton<IFoo>(sp => ...)` | BLIND_SPOT | Lambda arg — AbstractToken=IFoo, deps unresolvable |
| `services.Scan(...)`, `RegisterServicesFromAssembly(...)` | BLIND_SPOT | Assembly scanning call → `BlindSpotReport{pattern:"assembly_scanning"}` |
| XML config, runtime inspection | OUT | Not attempted in v1 |

**Extension methods:** detected at call site only; internals NOT traced in Phase 1.
`services.AddLogging()` → one `BlindSpotReport{pattern:"extension_method",
description:"AddLogging() — internal registrations not traced"}`.

**Bundling extensions:** same as single extension — DEGRADED/BLIND_SPOT at the
outermost call site. Transitive tracing is Phase 2.

**Error model:** syntax error in a file → `BlindSpotReport{pattern:"syntax_error",
location:..., description:message}`, parser continues with remaining files. Missing
references → type names remain as short names (less precise identity, not an error).
Parser never halts on per-file failure; every file produces either nodes or a
BlindSpotReport.

---

## 7. Graph Analysis Layer

**Orphaned:** Node N is orphaned iff `in_degree(N) == 0` (no `DependencyEdge`
has `to == N.id`) AND N is not the composition root AND N is not a
framework-infrastructure node (excluded by default; configurable). Orphans are
nodes nobody depends on — registered but unreachable from the consumer side.

**Reachable from root R:** N is reachable from composition root R iff there
exists a directed path R → ... → N following `DependencyEdge(from→to)` forward
edges. Computed by BFS from R over the directed graph. Nodes not reachable from
any identified root are candidates for orphan or dead-registration reporting.

**Leaked:** Node N is leaked iff `N.framework_tags` contains framework F1, and
there exists `DependencyEdge{from: M, to: N}` where `M.framework_tags` contains
F2 ≠ F1, and both F1 and F2 are declared framework boundaries. Concretely: an
Avalonia service consuming a WinUI type directly (not through an abstraction).
Secondary form: both `WinUI.IFooService` and `Avalonia.IFooService` registered —
duplicate abstract-token short name across frameworks in the same graph.

**Broken chain:** Node N has a broken chain iff there exists a `DependencyEdge`
`{from: N, to: M}` where M has `parser_confidence == BLIND_SPOT`, OR there
exists a constructor parameter type T on `N.concrete_impl` for which no
`RegistrationNode` exists where `abstract_token.short_name == T.short_name`.

**Composition root identification:**
1. Default: file with highest density of `services.Add*()` call sites. Ties:
   alphabetical file path. Files named `Program.cs`, `Startup.cs`, `AppHost.cs`,
   `ServiceRegistration.cs` get 2× weight.
2. Override: `--root=<ClassName>` flag. Specifies the class whose method is the
   composition root.
3. Multiple roots: if multiple files tie or user specifies multiple, analysis
   runs from all roots and unions the reachability set.

**Cycles:** detected by DFS with back-edge identification. Each strongly connected
component of size ≥ 2 is reported as a cycle. Output: `CYCLE: A → B → C → A`.
Cycles do not halt analysis; they are reported and flagged but other analysis
continues. Nodes in cycles are not considered orphaned (they have in-degree > 0).

**Output format:** Phase 1 text to stdout, sections in order:
`SUMMARY` → `LEAKED` → `BROKEN_CHAIN` → `ORPHANED` → `CYCLES` → `BLIND_SPOTS`.
Each finding: one line, `[SEVERITY] category: node_display_name (location)`.
Severity: `[ERROR]` for leaked/broken-chain, `[WARN]` for orphaned/blind-spot.
JSON `AnalysisResult` embedded in IR when `--ir-out` flag used.

---

## 8. Framework Boundary Model

**Framework declaration:** namespace prefix (primary). Assembly name is secondary,
used when namespace is ambiguous. NuGet package ID is metadata only. Priority:
exact namespace prefix match > assembly name match > heuristic short-name prefix.

**Config-driven with built-in defaults.** Built-in set cannot be overridden for
the named frameworks; custom frameworks are additive via `--frameworks <path>`
(JSON config file).

**Built-in set for v1:**

| Tag | Namespace prefixes | Assembly names |
|-----|--------------------|----------------|
| `winui` | `Microsoft.UI.*`, `WinUI.*` | `Microsoft.WindowsAppSDK` |
| `avalonia` | `Avalonia.*` | `Avalonia`, `Avalonia.*` |
| `wpf` | `System.Windows.*` | `PresentationFramework`, `PresentationCore` |
| `aspnetcore` | `Microsoft.AspNetCore.*` | `Microsoft.AspNetCore.*` |
| `msdi` | `Microsoft.Extensions.DependencyInjection.*` | `Microsoft.Extensions.DependencyInjection` |
| `ms-extensions` | `Microsoft.Extensions.*` | `Microsoft.Extensions.*` (lower priority than msdi) |

**Boundary crossing in IR:** a `DependencyEdge{from: M, to: N}` where
`M.framework_tags` and `N.framework_tags` are both non-empty and disjoint —
a node in one framework depending directly on a type in another framework.

**Intentional abstraction vs leak:** if `abstract_token` is in framework F1
but `concrete_impl` is in framework F2, the node may be an intentional adapter
(F2 implements the F1 interface). This is reported as `[WARN]` not `[ERROR]`.
If the consuming node's constructor takes a *concrete* F1 type directly (no
abstraction layer), that is `[ERROR]` leakage.

**Phase 1 leakage heuristic (WinUI→Avalonia migration):**
- Leaked registration: `abstract_token.namespace` starts with `Microsoft.UI.*`
  and the node appears in a graph extracted from code that also contains
  `Avalonia.*` registrations.
- Duplicate abstract token: `Microsoft.UI.IFooService` and `Avalonia.IFooService`
  both registered — same short name, different framework prefix.
- Cross-framework edge: `DependencyEdge{from: AvaloniaNode, to: WinUINode}`.

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
