# Dependency Chain Substrate — Design Document

Status: PARTIAL. §§6–8 filled from Phase 1 implementation. §§1–5 and §§9–17
backfilled from code + ADRs (Phase 5, 2026-06-28). Rename detection now has a
labelled Trackdub regression pair; future work is broader precision tuning.

Do not fill a section until all upstream sections it depends on are answered.
Do not remove the `> Q:` prompts when filling — replace them with the answer
inline so the question/answer pair stays readable.

---

## 1. Problem & Primary Use Case

> Q: What is the single job-to-be-done v1 must nail? State it in one sentence.

**Answer:** Prove or disprove that a framework migration (e.g. WinUI→Avalonia) has
left leaked or duplicated DI registrations, by statically extracting the registration
graph from arbitrary git commits without requiring the project to build.

> Q: What is the falsifiable value hypothesis? (i.e., "if we run X on Trackdub
>    at commit Y, we will find leakage Z that is known to exist")

**Answer:** If we run `dcs analyze /path/to/trackdub --commit 3c4e374d`, the tool
reports `VoiceCloneConsentCoordinator` registered 2× (WinUI + Avalonia shells) plus
six other duplicate abstract tokens — the known mid-migration parallel-shell state
verified in Phase 1 (186 registrations, exit code 1).

> Q: What failure mode is this tool fixing? Be concrete: what did an agent (or
>    human) do that the tool would have caught?

**Answer:** An agent (or human) declared the WinUI→Avalonia migration "complete" while
WinUI and Avalonia shells still registered the same services in parallel. The full
DI graph was invisible: migrated vs orphaned vs still-wired could not be tracked.
The tool catches this by surfacing `DUPLICATE` (same abstract token in both
frameworks) and `LEAKED` (cross-framework dependency or instance-pass conflict)
with file/line locations — exactly the false "we're good" claim from the origin story.

> Q: What does "correct" mean for this tool? Who decides?

**Answer:** Correct means the tool reproduces known leakage patterns on the Trackdub
ground-truth corpus, documents what it cannot see (`BLIND_SPOT` / `DEGRADED`), and
does not silently drop registrations. The human author (and future CI gate operator)
decides acceptance by comparing CLI output to known migration state; Trackdub is the
reference corpus per `AGENTS.md`.

---

## 2. Goals / Non-Goals / Positioning

> Q: Verifier or general DI visualiser? What is the v1 headline? (See ADR-001
>    and wedge decision in plan-of-plan.)

**Answer:** **Migration verifier** is the v1 headline. Framework Boundary Probe
(leakage + duplicate detection across declared framework tags) is the differentiated
capability. General topology (Registration Atlas, viz) is substrate supporting the
verifier, not the product pitch — per plan-of-plan wedge decision and ADR-001/003.

> Q: Which languages and frameworks are explicitly IN scope for v1?

**Answer:** **Language:** C# only (Roslyn syntactic parser). **DI framework:**
Microsoft.Extensions.DependencyInjection call-site patterns. **Framework boundaries
(built-in):** WinUI, Avalonia, WPF, ASP.NET Core, MS.DI, MS.Extensions
(`FrameworkBoundaryModel.Default`). **Extraction:** static, git blob reading via
LibGit2Sharp. **Delivery:** CLI (`analyze`, `diff`, `dump-ir`, `enrich`, `viz`).

> Q: Which languages and frameworks are explicitly OUT for v1?

**Answer:** Spring/Java (Phase 6, ADR-005 stub), TypeScript, Python (parked).
Runtime/hybrid extraction (Phase 9, ADR-008). IDE extension (Phase 7,
ADR-006 stub). Auto-fix/codemod (Phase 8, ADR-007 stub). Semantic Roslyn /
MSBuildWorkspace (parked — long-term fix for duplicate-ID collision class).
Full extension-method transitive tracing. Decorator chains (`Decorate<T>()`).

> Q: List the explicit non-goals for v1 (no runtime profiling, no auto-fix,
>    no IDE plugin, etc.).

**Answer:** No runtime container inspection in v1 static diff path; optional
runtime enrichment overlay (Phase 9) merges JSONL resolution logs post-run via
`dcs enrich`; no hybrid static+runtime diffs; no
auto-fix or write-back; no IDE/marketplace extension; no semantic type resolution
(no `SemanticModel`); no assembly-scanning body expansion; no factory-lambda
dependency resolution; `--frameworks <json>` for additive custom framework tags;
per-commit disk cache via `--cache-dir` / `--no-cache`; no rename-weight tuning on labelled corpus
(blocked until Trackdub rename pair identified).

> Q: How does this differ from existing tools (dotnet-trace, SonarQube,
>    NDepend, ArchUnitNET)? One sentence per comparison.

**Answer:**
- **dotnet-trace:** Profiles runtime behaviour; cannot analyse mid-migration commits
  that do not build or start.
- **SonarQube:** General code-quality rules; does not model DI registration graphs
  or cross-framework leakage across git history.
- **NDepend:** Rich .NET static analysis; no first-class migration verifier or
  commit-to-commit registration diff tuned for parallel-shell migrations.
- **ArchUnitNET:** Enforces architecture constraints you encode; does not extract
  live DI graphs from `services.Add*` call sites or detect duplicate migration state.

---

## 3. Users & Scenarios

> Q: Who is the primary user? (Concrete persona: role, context, what they
>    know, what they're trying to do mid-migration.)

**Answer:** Solo developer maintaining a desktop app through a WinUI→Avalonia (or
similar) migration. They know C# DI idioms and git; they run shell commands and an
IDE side-by-side. Mid-migration they need to know which registrations are duplicated,
leaked, orphaned, or broken — without trusting agent summaries or manually tracing
every `AddSingleton` across two shell projects.

> Q: Walk through the primary scenario step by step. What does the user type?
>    What does the tool output? What decision does the user make next?

**Answer:**
1. User runs: `dcs analyze /path/to/trackdub --commit 3c4e374d`
2. Stderr: parse progress (`186 registrations, N edges, M blind spots`).
3. Stdout: sections `LEAKED` → `BROKEN CHAINS` → `DUPLICATE REGISTRATIONS` →
   `ORPHANED` → `CYCLES` → `BLIND SPOTS` → `SUMMARY`; each line is
   `[SEVERITY] category: name (location)`.
4. Exit code `1` if any `LEAKED` or `BROKEN` (CI-gate ready).
5. User inspects `DUPLICATE`/`LEAKED` lines, opens cited files, removes WinUI-side
   registrations or fixes cross-framework edges; optionally `dcs viz ... --out graph.html`
   for spatial context.
6. Re-run until exit code `0` or acceptable residual blind spots documented.

> Q: Walk through the secondary scenario: agent-falsification. An agent has
>    claimed "migration complete." How does this tool contradict that claim?

**Answer:** Agent asserts all WinUI services migrated. Operator runs
`dcs analyze --commit <tip>` (or CI runs same on PR). Tool returns exit code `1` with
`[WARN] DUPLICATE: VoiceCloneConsentCoordinator registered 2×` and/or
`[ERROR] LEAKED: ... (winui → avalonia)`. The structured report is objective
evidence the graph still contains parallel-shell migration state — contradicting
"complete" without manual code review.

> Q: What other concrete scenarios must the module specs satisfy?

**Answer:**
- **Drift Scanner:** `dcs diff repo --from A --to B` — shows removed WinUI nodes
  (e.g. MainWindow) when retiring WinUI shell (verified 3c4e374d→316614b8).
- **Migration Diff:** same diff engine; migration-focused interpretation of
  added/removed/renamed registrations across migration commits.
- **Registration Atlas:** `dcs dump-ir` / `--ir-out` produces JSON IR for tooling.
- **Topology Lens:** `dcs viz --out graph.html` — interactive canvas at Trackdub scale.
- **Framework Boundary Probe:** built into `analyze`; tags + leakage rules (§8).
- **CI gate:** non-zero exit on `analyze` (errors) or `diff` (breaking removals).

---

## 4. Extraction Strategy (keystone)

Decision: see `docs/decisions/ADR-001-extraction-strategy.md`.

> Q: What registration call patterns are IN scope for static extraction in
>    v1? (List the concrete C# method signatures.)

**Answer:** Direct MS.DI methods on `IServiceCollection` / `services`:
`AddSingleton`, `AddScoped`, `AddTransient` (and `TryAdd*` variants) with:
- generic two-type form: `AddSingleton<IFoo, FooImpl>()`
- generic self-bind: `AddSingleton<FooImpl>()`
- `typeof` form: `AddSingleton(typeof(IFoo), typeof(FooImpl))` including open generics
- keyed variants: `AddKeyedSingleton<IFoo, FooImpl>("key")` (key in `annotations`)
Implemented in `RegistrationPatternVisitor.KnownRegistrationMethods`.

> Q: What patterns are in a "degraded" state — detectable but with
>    unresolved dependencies? How will the output signal this?

**Answer:** **DEGRADED** `parser_confidence` on the `RegistrationNode`:
- `AddSingleton<IFoo>(new FooImpl(...))` — abstract known, `concrete_impl` null,
  annotation `degraded_reason=instance_arg`
- `Add(new ServiceDescriptor(...))` — ServiceDescriptor ctor pattern
- Unknown argument shapes with resolvable type name
- Extension-method wrappers (`AddLogging()`, etc.) → `BlindSpotReport{pattern:"extension_method"}`
  at call site (registration count unknown; not a node)
CLI: nodes appear in graph with reduced confidence; viz uses alpha 0.5 for degraded nodes.

> Q: What patterns are OUT of scope (explicitly accepted blind spots) for v1?
>    How will the output signal these blind spots rather than silently
>    dropping them?

**Answer:** Per ADR-001 §3 and `RegistrationPatternVisitor`:
- Factory lambdas → `BLIND_SPOT` node + optional `BlindSpotReport`
- Assembly scanning (`Scan`, `RegisterServicesFromAssembly`, …) → `BlindSpotReport{assembly_scanning}`
- Unrecognised `Add*` extension methods → `BlindSpotReport{extension_method}`
- Syntax errors → `BlindSpotReport{syntax_error}`, parser continues
- `#if` blocks — extracted as written; no symbol evaluation
- Decorators (`Decorate<>`) — not detected (no visitor branch)
- Reflection registration — not detected
CLI `BLIND SPOTS` section lists every report; IR `blind_spots[]` array mirrors stdout.

> Q: How does Roslyn semantic vs syntactic parsing trade off for this use
>    case? Which is used, and why?

**Answer:** **Per-`ProjectTargetScope` semantic compilation** (ADR-009, parser 0.2.0).
Each `.csproj` + target framework → in-memory `CSharpCompilation` with closure-aware
reference profile (framework ref packs + DI extension assembly). No `MSBuildWorkspace`
or `dotnet restore`. Git-blob / directory extraction unchanged at the file level;
types resolved via `SemanticModel` / `ISymbol` when compilation succeeds.
Syntactic fallback remains when symbols are error types. Factory lambdas still
BLIND_SPOT. Quality split: resolved edges only; unresolved ctor deps →
`unresolved_injections[]`.

> Q: How does MSBuildWorkspace vs in-memory compilation trade off?

**Answer:** **In-memory compilation per project scope** — not MSBuildWorkspace.
Reference assemblies from installed .NET ref packs (`{pack}/{version}/ref/{tfm}`) +
`Microsoft.WindowsDesktop.App.Ref` for `-windows` TFMs; explicit DI package surface;
project-reference closure adds prior-scope metadata refs with **cross-TFM fallback**
(portable `net10.0` assets satisfy `net10.0-windows…` consumers per MSBuild). Orphan
files (no csproj) get syntactic-only bucket with `project_evaluation_incomplete`.

> Q: How are factory lambdas (`services.AddSingleton<IFoo>(sp => new Foo(sp.GetRequiredService<IBar>()))`)
>    handled? What is surfaced in output?

**Answer:** Detected when first data arg is `LambdaExpressionSyntax` or
`AnonymousMethodExpressionSyntax`. `ShallowFactoryLambdaExtractor` extracts the
created type from:
- expression-bodied lambdas: `sp => new MainWindow(...)`
- block-bodied lambdas: `sp => { return new MainWindow(...); }`
- block-bodied locals: `sp => { var w = new MainWindow(...); return w; }`
When a created type is found, produces one `RegistrationNode` with
`abstract_token` and `concrete_impl` set to that type, `parser_confidence=BLIND_SPOT`,
annotation `blind_spot_reason=factory_lambda_shallow`, plus
`BlindSpotReport{pattern:"factory_lambda_shallow"}`. Generic overloads
(`AddSingleton<IFoo>(sp => ...)`) still use abstract type from type args with
`factory_lambda` blind spot when body deps are not shallow-extractable. No edges
from lambda body. Contributes to `BROKEN_CHAIN` if a consumer depends on
unresolved deps inside lambda. Parser 0.3.1 adds block-bodied shallow extraction
(Phase 11 — Trackdub Avalonia `MainWindow` shell composition). Parser 0.3.2 traces
`GetRequiredService<T>()` calls inside shallow factory bodies as
`factory_lambda_service_keys` and emits `FactoryParameter` dependency edges when
targets resolve (Phase 12).

> Q: How are assembly-scanning registrations
>    (`services.AddServicesFromAssembly(...)`) handled?

**Answer:** Method name matched against `AssemblyScanningMethods` set →
`BlindSpotReport{pattern:"assembly_scanning"}` only (no expanded nodes).
Listed in CLI `BLIND SPOTS` section.

> Q: How are conditional `#if` registrations handled?

**Answer:** No preprocessor evaluation. Both branches appear in source as parsed;
whichever `#if` blocks exist in the blob are walked. Wrong-branch code may produce
phantom registrations or miss active ones — accepted blind spot (same class as
Spring `@Conditional`, ADR-002 S4).

> Q: How are keyed services (`services.AddKeyedSingleton<IFoo>("key", ...)`)
>    handled?

**Answer:** **EXPLICIT** extraction: first arg (key) skipped; types extracted from
generics or `typeof`. `annotations["keyed"]="true"`; string key literal not yet
stored in `annotations["service_key"]` (future polish). Same lifetime/confidence
as non-keyed registrations.

> Q: How are open generics (`services.AddSingleton(typeof(IGeneric<>), typeof(Generic<>))`)
>    handled?

**Answer:** **EXPLICIT** via `typeof` arguments with unbound generic syntax
(`GenericNameSyntax` with empty type args). `TypeRef.is_generic=true`;
`type_arguments` populated when instantiated forms appear.

> Q: How are decorator patterns (`services.Decorate<IFoo, FooDecorator>()`)
>    handled?

**Answer:** **Not handled in v1.** `Decorate` is not in `KnownRegistrationMethods`
and does not match the `Add*` extension blind-spot heuristic unless named `Add*`.
Decorator chains are invisible — accepted gap; Scrutor-style decoration requires
future pattern catalog entry or runtime enrichment (ADR-008).

---

## 5. Shared Abstraction Model (IR)

Decision: see `docs/decisions/ADR-002-ir-identity-model.md`.

> Q: What are the universal IR primitives? Give precise names, field lists,
>    and field types for each.

**Answer:** Implemented in `DCS.Core.IR` (records, JSON snake_case):
- **`RegistrationGraph`:** `schema_version`, `parser_version`, `commit_sha?`,
  `extraction_mode`, `source_language`, `nodes[]`, `edges[]`, `blind_spots[]`,
  `metadata{}`
- **`RegistrationNode`:** `id`, `instance_id`, `display_name`, `abstract_token`,
  `aliases[]`, `concrete_impl?`, `lifetime`, `scope`, `source_location?`,
  `parser_confidence`, `framework_tags[]`, `annotations{}`, `conditional_on[]`
- **`DependencyEdge`:** `id`, `from`, `to`, `injection_mechanism`, `parameter_name?`,
  `parser_confidence`
- **`TypeRef`:** `fully_qualified_name`, `short_name`, `namespace?`, `assembly?`,
  `language`, `is_generic`, `type_arguments[]`
- **`BlindSpotReport`:** `pattern`, `location?`, `description`
- **`SourceRef`:** `file_path`, `line`, `column?`
- Enums: `Lifetime`, `Scope`, `Confidence`, `Mechanism` — see `Enumerations.cs`

> Q: What is the node identity model? (Primary key structure, stability
>    guarantees, cross-version semantics.)

**Answer:** Dual identity (schema **1.2.0**, ADR-002 + ADR-009):
- **`id`** / **`registration_instance_id`** — SHA256(`scopeId:file:line:col:endLine:endCol:ordinal`);
  unique per registration **site** within a snapshot; primary graph node key and edge endpoint.
- **`duplicate_group_key`** — SHA256(`composition_scope_id|service_type.canonical_key`); strict
  DUPLICATE analysis groups on this key when `StrictDuplicateEligibility` is met (resolved type,
  `VerifiedMicrosoftDI`, complete project evaluation).
- **`service_type`** — `ServiceTypeIdentity` with optional `ResolvedTypeIdentity` (assembly-scoped
  metadata name + type arguments) or syntactic fallback display.
- Three quality dimensions on each node: `type_resolution_quality`, `registration_recognition_quality`,
  annotations for `strict_duplicate_eligible`, `project_evaluation_incomplete`, etc.
- **`DependencyEdge.id`** = hash(`from`, `to`, `injection_index`).
- Unresolved constructor parameters → `unresolved_injections[]` (no fallback edges).

> Q: How is rename vs delete+add distinguished during diff?

**Answer:** **Diff engine** (`GraphDiffer`). Hybrid matching (ADR-009): primary key
`duplicate_group_key` when strict-eligible; else file path + `registration_statement_fingerprint`
+ ordinal; line proximity last resort. IR does not encode rename links.

> Q: What is the serialisation schema?

**Answer:** **JSON**, snake_case. Current `schema_version`: **1.2.0**. C# `parser_version`:
**0.2.0** (semantic Roslyn per `ProjectTargetScope`). Policy unchanged (ADR-002).

> Q: What validation was run against Spring to confirm cross-language fit?
>    What revisions were made as a result? (Fill after Spring spike.)

**Answer:** Phase 0 paper-spike against Spring PetClinic patterns (ADR-002 §Findings).
All schema changes were **additive** — no breaking revisions:
1. `RegistrationNode.aliases[]`
2. `Lifetime.APPLICATION`
3. `Mechanism.FACTORY_PARAMETER`
4. `RegistrationNode.conditional_on[]`
5. `TypeRef` structured generics (confirmed sufficient)
6. `concrete_impl=null` documented for Spring Data proxies
Assumptions S1–S5 mapped; auto-config beans expected as `BLIND_SPOT`. IR frozen
for C# implementation; Java parser deferred to Phase 6.

> Q: What are the explicitly accepted IR limitations for languages other than
>    C#/MS.DI?

**Answer:** FastAPI `Depends(fn)` → needs `Mechanism.DEPENDS_FN`, function-token
graph not type-only. NestJS dynamic modules → `BLIND_SPOT`. Spring `@Conditional` /
`@Profile` → `BLIND_SPOT` + annotations. Python dependency-injector often has no
interface abstraction (`abstract_token == concrete_impl`). `TypeRef.assembly` null
outside C#. Multi-interface Spring beans need `aliases[]`. Spring Data repos:
`concrete_impl=null`, `DEGRADED`.

> Q: What does `parser_confidence` look like in the schema? What values and
>    what semantics?

**Answer:** Enum `Confidence` serialised snake_case on nodes and edges:
- **`explicit`** — directly observed at registration call site (generic/`typeof` forms)
- **`inferred`** — derived (e.g. constructor edges when endpoint confidence mixed)
- **`degraded`** — registration detected, fields incomplete (instance arg, ServiceDescriptor)
- **`blind_spot`** — pattern known but body unreadable (factory lambda)
Downstream: `BROKEN_CHAIN` treats edges to `blind_spot` providers as broken;
viz renders alpha 0.3 for blind_spot nodes.

---

## 6. C# Language Parser

**Roslyn API:** Syntactic walk + **semantic enrichment** (Phase 10 / ADR-009).
`ProjectTargetScopeCompilationFactory` builds per-scope `CSharpCompilation`;
`RegistrationPatternVisitor` and `ConstructorDepVisitor` use `SemanticModel` when
available. Parser version **0.2.0**. Default CLI: `--all-target-frameworks` (one
graph per TFM). Override: `--target-framework <tfm>`.

**Workspace loading:** LibGit2Sharp blob read unchanged. Scope discovery reads
`.csproj` metadata (`CsprojMetadataReader`); source membership by project directory.
No repo-wide merged compilation.

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

**Answer:** **`RegistrationNode.Id`** — hash of `abstract_token.fully_qualified_name`
(syntactic FQN). `GraphDiffer` builds `oldById` / `newById` dictionaries (first node
per ID when duplicates exist). **`instance_id` is not used** in cross-snapshot diff.
Edge matching uses **`DependencyEdge.Id`**.

> Q: What is the rename/move detection algorithm? What threshold? What
>    computational complexity?

**Answer:** Implemented in `GraphDiffer.MatchRenames` (ADR-002 Option D):
```
similarity = 0.5 * normalized_levenshtein(display_name)
           + 0.3 * jaccard(outgoing_dep_target_ids)
           + 0.2 * (lifetime match ? 1 : 0)
```
Threshold **0.7** (`RenameThreshold`). Greedy: sort all qualifying pairs by score
descending; assign each old/new node at most once. Complexity **O(R × A)** for
pairwise scoring (R = removed candidates, A = added candidates) plus **O(P log P)**
sort on qualifying pairs. Labelled Trackdub regression: commit
`8fda806d8fced57da178f250e8afa509f9567e3c` detects
`BabelStudioStoragePaths` → `TrackdubStoragePaths` at score 0.78. Broader
precision tuning remains open because that same large technical rename also
contains many same-display-name move matches.

> Q: What change categories does the diff engine produce? (Added, Removed,
>    Renamed, LifetimeChanged, ImplementationChanged, etc.)

**Answer:** **`NodeChangeKind`:** `Added`, `Removed`, `Renamed`, `LifetimeChanged`,
`ImplementationChanged`, `ConfidenceChanged`, `FrameworkTagsChanged`.
**`EdgeChangeKind`:** `Added`, `Removed` (set comparison on edge ID).
In-place ID matches emit modification kinds without Added/Removed.

> Q: What makes a change "meaningful" vs "cosmetic"?

**Answer:** **Breaking / meaningful:** node or edge **Removed** → `HasBreakingChanges`
true, exit code 1. **Renamed** and in-place **ImplementationChanged** /
**LifetimeChanged** are meaningful but not breaking by default. **Cosmetic / noisy:**
namespace-only FQN drift without rename match appears as Removed+Added;
`ConfidenceChanged` or `FrameworkTagsChanged` alone are informational.
Rename detection exists specifically to reduce cosmetic delete+add noise from
namespace moves. No separate "cosmetic" filter — all changes reported.

> Q: How is determinism of diff output guaranteed? (Ordering, stable IDs.)

**Answer:** Node/edge IDs are content hashes (deterministic). `DetectInPlaceChanges`
sorts framework tags before compare. CLI `PrintDiff` lists changes in collection order
(nodes processed in dictionary iteration order — stable for same input graphs).
Greedy rename matching is deterministic given sorted pair scores. Same two
`RegistrationGraph` inputs → same `GraphDiff` structure.

> Q: What is the output format for a diff result?

**Answer:** **Text (stdout):** sections `ADDED`, `REMOVED`, `RENAMED` (with similarity
score), `MODIFIED` (kind uppercased), edge add/remove counts, `SUMMARY`, optional
`NOTE: breaking changes detected`. **JSON (`--ir-out`):** serialised `GraphDiff`
record with snake_case fields (`old_commit`, `new_commit`, `node_changes`, `edge_changes`).
Exit code **1** if `HasBreakingChanges`.

---

## 10. Git Ingestion

> Q: Checkout-per-commit vs git blob reading — which, and why? (See ADR-001
>    for the constraint that rules this out.)

**Answer:** **Git blob reading only** (`CSharpStaticParser.ParseCommit`). Reads each
`.cs` blob from the commit tree via LibGit2Sharp; **no working-tree checkout**.
Rationale (ADR-001): mid-migration commits may not build; checkout pollutes working
tree; enables concurrent/extraction without side effects; required for diff-at-scale.

> Q: What library is used for git object access? (libgit2sharp is the
>    candidate.)

**Answer:** **LibGit2Sharp** — `Repository`, `Commit`, recursive `Tree` walk,
`Blob.GetContentText()` for `.cs` files. Working-directory fallback:
`ParseDirectory` uses `Directory.EnumerateFiles` (excludes `bin/`, `obj/`, `.git/`).

> Q: How is per-commit extraction cached? (Cache key = commit SHA + parser
>    version. Where stored? What invalidation policy?)

**Answer:** **Implemented** (Phase 5). `ExtractionCache` in `DCS.Core.Caching` stores IR JSON at
`{commitSha}_{parserVersion}.json`. Default directory:
`%LOCALAPPDATA%/dependency-chain-substrate/cache/` (Windows) or
`~/.cache/dependency-chain-substrate/` (elsewhere). Override: `--cache-dir <path>`.
Bypass: `--no-cache`. Invalidation: automatic when `CSharpStaticParser.ParserVersion`
changes. Scope: `ParseCommit` only (not `ParseDirectory`). Stderr logs `[DCS] Cache hit for {sha}`.

> Q: How are merge commits handled? (Which parent's tree is extracted?)

**Answer:** Caller passes explicit commit SHA; LibGit2Sharp resolves that commit
object's **own tree** (merge commit snapshot as committed). No automatic parent
selection. For diffs, user chooses `--from` / `--to` SHAs explicitly. Document if
analysing merge commits: result is the merged tree state, not a per-parent union.

> Q: How are missing/deleted files at a given commit handled by the parser?

**Answer:** Only files **present in the commit tree** are parsed. Files deleted at
that commit simply absent — no tombstone nodes. Files added only in later commits
appear only when extracting those commits. No error for "missing" files relative to
another commit; diff engine reports registration Removed/Added accordingly.

---

## 11. Visualisation & Delivery Form Factor

Decision: see `docs/decisions/ADR-003-form-factor.md`.

> Q: What is the minimal text output for Phase 1 CLI? (What fields, what
>    format, what sort order?)

**Answer:** Implemented in `AnalysisReportPrinter` / `AnalysisReportBuilder`. Header: commit,
context, node/edge counts. Sections in order: **LEAKED** (ERROR) → **BROKEN CHAINS** (ERROR) →
**DUPLICATE REGISTRATIONS** (WARN) → **POSSIBLE DUPLICATES** (WARN) → **UNRESOLVED DEPENDENCIES**
(WARN) → **ORPHANED** (WARN) → **CYCLES** (WARN) → **BLIND SPOTS** (WARN). Each finding lists
`file:line` sites; footer **SUMMARY** + **TIERS** breakdown. Default `--verbosity actionable`
hides informational/parser_limit tiers. Progress and context banner on stderr.

> Q: What is the Phase 3 interactive visualisation? What rendering library?

**Answer:** **Self-contained HTML** from `HtmlVizGenerator` (`dcs viz`). No external
CDN dependencies — graph + analysis JSON embedded inline. Rendering: **HTML5 Canvas 2D**
(vanilla JavaScript in generated file). Framework-grouped radial layout; sidebar stats,
legend, click-to-inspect node detail. Trackdub-scale graph verified at 335 nodes
after semantic hardening; synthetic smoke test covers 1,200 nodes / 1,199 edges.

> Q: How is legibility handled at 1000+ nodes? (Aggregation strategy,
>    focus+context, LOD.)

**Answer:** Current implementation:
- **Framework grouping:** nodes clustered by primary `framework_tags[0]` in separate
  circular regions.
- **Zoom LOD:** labels shown only when zoom ≥ 1.5×; node radius scales with zoom;
  edges fade when zoomed out (`HtmlVizGenerator` canvas loop).
- **Focus:** click node → sidebar detail panel with confidence, lifetime, location.
- **Scale status:** 335-node Trackdub graph is behavior-verified; 1,200-node
  synthetic graph is generation-smoke-tested and marked as a large graph in the
  HTML sidebar. Edge bundling, hierarchical collapse, and aggregation thresholds
  remain future UX work if a real corpus exposes readability limits.

> Q: What is the export format from the CLI for downstream consumers?

**Answer:**
- **IR JSON:** `dcs dump-ir`, `dcs analyze --ir-out`, schema 1.2.0 snake_case JSON.
- **Analysis report JSON:** `dcs analyze --format json --report-out report.json`, schema
  `docs/schemas/analysis-report-1.0.json` (v1.0). Findings include `finding_id`, `category`,
  `severity`, `tier` (`actionable` | `informational` | `parser_limit` | `intentional`), and
  `sites[]` with `file_path` + `line`. Separate from IR dump.
  **IDE public API:** ADR-006 — VS Code extension consumes report JSON only; IR 1.2.0 is internal.
  Report compatibility: additive fields within 1.x; breaking changes require major bump.
- **Text report:** `dcs analyze` (default). Sections filtered by `--verbosity`
  (`summary` | `actionable` | `full`). Every WARN/ERROR line cites at least one `file:line`
  site (or tier annotation). `--strict` disables `FindingPolicy` suppressions; `--metrics`
  prints extraction quality on stderr.
- **Multi-context:** `--context all` implies all target frameworks unless
  `--target-framework` is explicit. Text/JSON reports emit all context reports;
  `--ir-out` writes a `ParseResult` bundle rather than a single graph.
- **Diff JSON:** `dcs diff --ir-out` → serialised `GraphDiff`.
- **HTML viz:** `dcs viz --out graph.html` or stdout.
- **Exit codes:** 0 success / 1 errors or breaking diff / 2 usage error.

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

**Answer (MVP slices as shipped):**
- **Registration Atlas:** `dcs atlas` (human-readable sorted listing + framework counts)
  and `dcs dump-ir` / `dcs analyze --ir-out` (JSON IR for tooling).
- **Topology Lens:** `dcs viz` — canvas graph with framework colours, zoom/pan,
  error badges from analysis. No force-directed or Sugiyama layout yet.
- **Path Excavator:** **`dcs path <repo> --to <registration> [--from <registration>]`** —
  shortest dependency path on static edges (constructor + factory-lambda `GetRequiredService`
  traces). Resolves `--from` / `--to` by registration id, display name, or type token.
  Default origin: composition-root seeds (same policy as orphan reachability). Output:
  numbered hops with `file:line`; `--format json` → `PathExcavationReport`. Exit **1** if
  no path, **2** if ambiguous match. **`dcs viz --path-to`** highlights the same path on the
  HTML canvas (Phase 13b).
- **Framework Boundary Probe:** `GraphAnalyzer.FindLeaked` + `FrameworkBoundaryModel`
  — built into `dcs analyze`; built-in WinUI/Avalonia/WPF/ASP.NET tags. Custom
  frameworks additive via `--frameworks <path>` (JSON config).
- **Drift Scanner:** `dcs diff --from --to` — added/removed/renamed/modified nodes
  and edges; breaking-change exit code.
- **Migration Diff:** Same engine as Drift Scanner; migration interpretation is
  user/CI policy on diff output (e.g. expect WinUI node removals on shell retire).

> Q: Map each module to the layers it depends on.

**Answer:**

| Module | Parser | IR | Analysis | Diff | Git | Viz |
|--------|--------|-----|----------|------|-----|-----|
| Registration Atlas | ✓ | ✓ | — | — | ✓ | — |
| Topology Lens | ✓ | ✓ | ✓ | — | ✓ | ✓ |
| Path Excavator | ✓ | ✓ | ✓ | — | ✓ | ✓ (partial) |
| Framework Boundary Probe | ✓ | ✓ | ✓ | — | ✓ | — |
| Drift Scanner | ✓ | ✓ | — | ✓ | ✓ | — |
| Migration Diff | ✓ | ✓ | — | ✓ | ✓ | — |

> Q: What are the module interfaces (inputs, outputs, failure modes)?

**Answer:**

| Module | Input | Output | Failure modes |
|--------|-------|--------|---------------|
| Registration Atlas | repo path ± commit SHA | IR JSON | invalid SHA; parse continues with blind_spots |
| Topology Lens | IR or repo+commit | HTML file | empty graph; large graph browser memory |
| Path Excavator | repo + commit, `--from`/`--to` tokens | path node + edge list (text/json) | no path; ambiguous token; no seeds |
| Framework Boundary Probe | IR + boundary model | LEAKED/DUPLICATE findings in AnalysisResult | false positives on intentional adapters (WARN vs ERROR heuristic) |
| Drift Scanner | repo, from SHA, to SHA | text/JSON diff | false rename matches; duplicate-ID collapse |
| Migration Diff | same as Drift Scanner | same | user mis-selects commit pair |

---

## 13. Cross-Cutting Concerns

> Q: How is determinism enforced across the full pipeline?

**Answer:** Static extraction from fixed commit blob content → deterministic parse.
IDs are SHA256-derived. No random layout in CLI. Viz layout uses deterministic
sort (`groupKeys.sort()`, fixed angle placement) for same graph JSON. No
parallelism that reorders output. Same inputs should yield byte-identical IR JSON
(modulo whitespace if formatting unchanged).

> Q: How are blind spots surfaced in every output format? (CLI, IR, viz.)

**Answer:**
- **CLI:** dedicated `BLIND SPOTS` section listing every `BlindSpotReport`; BLIND_SPOT
  nodes also affect `BROKEN CHAINS`.
- **IR:** top-level `blind_spots[]` plus per-node `parser_confidence=blind_spot`.
- **Viz:** blind-spot nodes drawn at alpha 0.3; sidebar blind-spot count in stats;
  degraded/explicit at 0.5/1.0 alpha.

> Q: What is the logging/diagnostic strategy for parser failures?

**Answer:** Per-file **best-effort**: syntax errors → `BlindSpotReport{syntax_error}`,
continue next file. Progress/diagnostics on **stderr** (`[DCS] Parsing...`, counts,
write paths). No structured log levels yet. Parser never aborts whole repo for one
bad file. Missing type resolution is silent degradation (short names), not fatal.

> Q: What is the performance target for extraction? (Nodes/sec, acceptable
>    latency for a Trackdub-scale codebase.)

**Answer:** **Not formally benchmarked.** ADR-002 assumption: JSON serialisation
< 10s at Trackdub scale; tertiary falsifier at 30s. Phase 3 viz: 186 nodes →
~220KB HTML, acceptable interactive. Phase 5 disk cache targets eliminating
**repeated** extraction cost, not first-parse SLA. Profile before setting nodes/sec target.

> Q: What is the error propagation model (fail-fast vs best-effort with
>    warnings)?

**Answer:** **Best-effort extraction, fail on analysis findings for CI:**
- Parse: continue on file errors; collect blind spots.
- Analyze: always complete; set `HasErrors` for LEAKED/BROKEN.
- CLI exit **1** on analysis errors or breaking diff — not on blind spots alone.
- Usage errors exit **2**. Invalid commit SHA throws before parse (hard fail).

---

## 14. Extensibility / Plugin Contract

> Q: What is the parser interface? (Method signatures, capability
>    negotiation protocol.)

**Answer:** **De facto contract today:** language-specific parsers implement
`IStaticParser` and expose `ParseResult ParseCommit(string repoPath, string sha)` /
`ParseResult ParseDirectory(string path)`. A `ParseResult` contains one or more
`ContextGraph` entries, each with a context id and a complete `RegistrationGraph`.
Capability negotiation remains minimal and is selected by CLI language routing
(`auto`, `csharp`, `java`). Future: formal parser metadata with `ParserVersion`,
`SupportedLanguages[]`, and optional feature flags.

> Q: How does a parser signal "I cannot resolve this pattern"? What goes
>    into the IR for that node?

**Answer:** Two mechanisms:
1. **`BlindSpotReport`** in `graph.blind_spots[]` when no node should be invented
   (assembly scan, extension wrapper, syntax error).
2. **`RegistrationNode`** with `parser_confidence=blind_spot` or `degraded` when partial
   knowledge exists (factory lambda: abstract known, impl null).
Annotations carry `blind_spot_reason` / `degraded_reason` strings for tooling.

> Q: What is the IR contract version? How are breaking schema changes
>    handled? How are additive changes handled?

**Answer:** `RegistrationGraph.schema_version` (**1.2.0**). **Additive:** new optional
JSON fields, new enum values — consumers ignore unknown fields; minor doc bump optional.
**Breaking:** remove/rename required fields or change semantics → major bump;
readers reject unsupported major version (policy in ADR-002; deserialiser does not
yet enforce major-version gate in code — implementation gap still open).

> Q: What is the minimum viable plugin API for a second language parser?

**Answer:** Produce valid `ParseResult` output containing schema 1.2.0
`RegistrationGraph` JSON: set `source_language`, `parser_version`, populate
`nodes`/`edges`/`blind_spots` using shared enums, and provide stable context ids
for each application graph. Map language DI idioms per ADR-002/ADR-005; tag
`framework_tags` appropriately; emit `BLIND_SPOT` for unresolvable patterns. No
changes to analysis/diff/viz are required when the schema and context contract are
honoured.

---

## 15. Phasing / Milestones

See `PLAN.md` for the live tracker. This section documents the architectural
"done" criteria that PLAN.md entries must satisfy.

> Q: What does Phase 1 "done" mean in terms of observable, checkable
>    behaviour on Trackdub? (Not just "tests pass" — what does the tool
>    print, and what is the known-correct answer?)

**Answer:** Run `dcs analyze <trackdub> --commit 3c4e374d`:
- Prints **186 registrations** (approx.), non-zero blind-spot count.
- **`DUPLICATE`:** `VoiceCloneConsentCoordinator` registered 2× plus six other
  duplicate abstract tokens (parallel WinUI+Avalonia shells).
- **`LEAKED`:** instance-pass fires for same-`id` conflicting framework tags
  (schema 1.1.0 dual-identity model).
- **`BROKEN CHAINS`:** constructor deps referencing unresolved types (4 broken at verification).
- Exit code **1**. Unit tests green in `DCS.Parser.CSharp.Tests`, `DCS.Analysis.Tests`.

> Q: What does Phase 2 "done" mean for a specific Trackdub commit pair?

**Answer:** Run `dcs diff <trackdub> --from 3c4e374d --to 316614b8`:
- **`REMOVED`:** WinUI shell nodes including `MainWindow`, `MainWindowViewModel`
  (WinUI retire commits).
- Edge removals mirror removed registrations.
- Exit code **1** (`HasBreakingChanges` on removals).
- Both commits extract via blob read without checkout.

> Q: What is the minimum Phase 3 bar for "legible at full scale"? (Node
>    count, interaction requirement.)

**Answer:** `dcs viz <trackdub> --commit 3c4e374d --out graph.html` produces
self-contained HTML. Browser: canvas renders, zoom/pan works, framework colour
groups visible, LEAKED/BROKEN nodes show error badges, click opens sidebar detail.
No server required. Bar met at Trackdub mid-migration scale and later semantic
Trackdub scale (~335 nodes). A 1,200-node synthetic render smoke test exists;
legibility/aggregation above Trackdub scale is still a UX risk, not a generation
failure.

---

## 16. Risks & Open Questions

> Q: What are the top-5 risks to Phase 1 succeeding? Tag each with the
>    resolver (human decision, spike, implementation discovery).

**Answer:**
1. **Known leakage lives in blind-spot patterns** (factory lambdas, assembly scan) —
   *resolver: Phase 1 verification* — **mitigated:** Trackdub leakage was explicit
   `Add*` calls; static extraction succeeded.
2. **Syntactic FQN collisions suppress LEAKED edge-pass** — *resolver: implementation
   discovery* — **mitigated:** `instance_id` + instance-pass LEAKED (ADR-002 addendum).
3. **Rename detection noise > signal** — *resolver: spike on labelled renames* —
   **partially mitigated:** labelled Trackdub pair locked; broad precision tuning
   still open because large technical renames produce many same-name move matches.
4. **Roslyn parse quality on broken references** — *resolver: implementation discovery*
   — **mitigated:** 186 nodes extracted at mid-migration commit.
5. **IR designed on sample size one fails Spring** — *resolver: Spring paper-spike*
   — **mitigated:** additive extensions only; IR frozen.

> Q: What open questions remain after the four ADRs are closed?

**Answer:**
- ADR-006 IDE integration remains deferred; ADR-005 Spring parser and ADR-007 auto-fix safety are accepted and implemented through their current phases.
- ADR-008 runtime enrichment — **Accepted + Verified** (Phase 9); Trackdub @ pin 89.3% annotated.
- Major-version enforcement in `IrSerializer.Deserialize` is implemented.
- Rename similarity has one labelled Trackdub regression; broader precision tuning remains open.
- Canvas generation is smoke-tested at 1,200 synthetic nodes; real-corpus aggregation behaviour above Trackdub scale remains unproven.
- Second corpus beyond Trackdub for cross-validation (§17).

> Q: What assumptions are load-bearing but unvalidated? (These are
>    falsification targets.)

**Answer:**
- Rename similarity weights 0.5/0.3/0.2 and threshold 0.7 are structurally motivated
  but **not empirically validated** on labelled renames.
- Canvas viz remains legible at **1000+ nodes** without aggregation (scale untested).
- **JSON performance** stays under ADR-002 thresholds at larger repos.
- **Spring Phase 6** mapping holds on real PetClinic parse, not just paper-spike.
- **Intentional adapter** heuristic (abstract F1, impl F2 → WARN not ERROR) is sufficient
  to limit false-positive LEAKED on Trackdub — not exhaustively proven.

---

## 17. Validation / Test Corpus

> Q: What is the specific Trackdub scenario that constitutes Phase 1
>    acceptance? (Commit range, known-leaking registration, expected output.)

**Answer:**
- **Commit:** `3c4e374d` (mid-migration, parallel WinUI+Avalonia shells).
- **Command:** `dcs analyze <TRACKDUB_PATH> --commit 3c4e374d`
- **Expected:** exit code 1; DUPLICATE includes `VoiceCloneConsentCoordinator` 2×;
  six additional duplicate tokens; LEAKED via instance-pass; ≥1 BROKEN chain;
  ~186 registration nodes.
- **Secondary commit pair (Phase 2):** `3c4e374d` → `316614b8` — WinUI shell
  removal visible in diff REMOVED section.
- Trackdub path configured in `.claude/settings.local.json` as `TRACKDUB_PATH` when
  running local verification (not required for unit tests).

> Q: What is the dogfood plan for Phase 3? (Who runs it, against what, by
>    when?)

**Answer:** **Author dogfoods** during active migration work on Trackdub. Phase 3
complete (2026-06-28): generate HTML viz at commit `3c4e374d`, use for spatial
exploration of duplicate/leaked clusters alongside CLI output. Ongoing: re-run
`analyze`/`viz` at migration milestones before declaring shell parity. No external
beta users in v1.

> Q: What second corpus (beyond Trackdub) should be considered for
>    cross-validation?

**Answer:**
- **Spring PetClinic** (or equivalent OSS Spring Boot app) — Phase 6 gate; validates
  cross-language IR and BLIND_SPOT/DEGRADED rates for auto-config.
- **Open-source WinUI or Avalonia sample** with explicit DI — negative control for
  false DUPLICATE/LEAKED on single-framework apps. **Shipped:** `LykosAI/StabilityMatrix` @
  `d97f6ccb` (`csharp-negative-control` corpus leg; analyzes `StabilityMatrix/` subproject).
- **Synthetic minimal repos** in `tests/` — pattern coverage (already used for parser
  unit tests); not a substitute for real migration history.
- Trackdub remains **authoritative** for migration-verifier claims per `AGENTS.md`.
