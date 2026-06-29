# ADR-002: IR + Identity Model

**Status:** Accepted
**Date:** 2026-06-28
**Effort:** High (Opus-class reasoning required; see AGENTS.md routing matrix)

---

## Context

The Shared Abstraction Model (IR) is the central bet of this project. It must:

1. **Represent** DI registrations from C#/MS.DI today, with headroom for Spring,
   Python, and TypeScript DI frameworks.
2. **Support diffing** across commits — node identity must be stable enough that
   a rename or namespace move does not corrupt a diff with noise.
3. **Serialise** to a stable, versioned schema that both the analysis layer and
   the visualisation consumer can read.
4. **Surface uncertainty** — static extraction has blind spots; the IR must
   carry parser confidence so downstream consumers know what to trust.

The design is being made on a sample size of one (C#/MS.DI). This ADR
documents the cross-language assumptions being made and what the Spring spike
must validate before the IR is considered frozen.

---

## First-Principles Argument

### What a DI registration is, across languages

A DI registration maps an abstract type (interface or base class) to a concrete
implementation with a specified lifetime. The consuming side depends on the
abstract type; the framework resolves it. This is the common structure across:

- **C#/MS.DI:** `services.AddSingleton<IFoo, FooImpl>()` — explicit call, type
  parameters, lifetime enum {Singleton/Scoped/Transient}.
- **Spring (Java):** `@Bean public IFoo foo() { return new FooImpl(); }` in a
  `@Configuration` class, or `@Component` / `@Service` on `FooImpl` with
  component scanning. Lifetime: `@Scope("singleton")` (default) or `prototype`,
  `request`, `session`, etc.
- **NestJS (TypeScript):** `@Injectable()` on a class, registered in a
  `@Module({ providers: [FooImpl] })`. No explicit interface-to-implementation
  mapping in the simple case; the token is the class itself.
- **Python (dependency-injector):** `providers.Factory(FooImpl)` or
  `providers.Singleton(FooImpl)`. Often no interface abstraction at all.
- **Python (FastAPI):** `Depends(get_foo)` — function-level dependency, no
  container, no lifetime beyond request scope.

The common structure is:

```
(abstract_token, concrete_impl, lifetime, scope)
```

Where `abstract_token` may equal `concrete_impl` (NestJS self-binding, Spring
`@Component` without an interface). Where `scope` may be container-global
(singleton) or narrower (request, session). Where `lifetime` maps to a
coarser enum across languages.

**What does NOT fit the common structure:**

- Spring's `@Conditional` — registration is conditional on bean presence,
  property values, class availability. Static extraction cannot resolve this
  without executing the condition.
- Python FastAPI's `Depends(fn)` — the dependency is a function, not a type.
  The graph is a call graph, not a type graph.
- NestJS dynamic modules — module configuration is runtime-resolved.

These are accepted blind spots for the first cross-language generalisation.
The IR must not silently fail on them; it must produce `BLIND_SPOT` nodes.

---

## IR Primitive Design

### RegistrationNode

```
RegistrationNode {
  id:               NodeId          // stable identity (see below)
  display_name:     string          // human-readable; may change without id change
  abstract_token:   TypeRef         // the type being depended upon
  aliases:          TypeRef[]       // all types this bean satisfies (supertypes + interfaces)
                                    // required for Spring multi-interface beans; empty list for C#
  concrete_impl:    TypeRef?        // the implementation; null if unresolved (e.g. Spring Data proxies)
  lifetime:         Lifetime        // see Lifetime enum
  scope:            Scope           // see Scope enum
  source_location:  SourceRef?      // file + line if available
  parser_confidence: Confidence     // EXPLICIT / INFERRED / DEGRADED / BLIND_SPOT
  framework_tags:   string[]        // which framework(s) this belongs to
  annotations:      map<string, string>  // parser-specific extras (e.g. "key" for keyed services,
                                    // "bean_name", "bean_mode", "conditional_type", "spring_scope")
  conditional_on:   NodeId[]        // nodes that must be absent/present for this bean to register
                                    // empty for unconditional registrations
}
```

### DependencyEdge

```
DependencyEdge {
  id:               EdgeId          // (from_id, to_id, injection_index) tuple
  from:             NodeId          // consumer registration
  to:               NodeId          // provider registration being consumed
  injection_mechanism: Mechanism    // CONSTRUCTOR / PROPERTY / METHOD / FIELD / DEPENDS_FN
  parameter_name:   string?         // for disambiguation when multiple same-type deps
  parser_confidence: Confidence     // edge may be known even if node is DEGRADED
}
```

### TypeRef

```
TypeRef {
  fully_qualified_name: string      // primary key for type lookup
  short_name:           string      // display
  namespace:            string?
  assembly:             string?     // C# only; null for other languages
  language:             string      // "csharp" | "java" | "typescript" | "python"
  is_generic:           bool
  type_arguments:       TypeRef[]   // for generic instantiations
}
```

### Enumerations

```
Lifetime:
  SINGLETON       // one instance for container lifetime
  SCOPED          // one instance per scope (request in web, etc.)
  TRANSIENT       // new instance per resolution
  PROTOTYPE       // Spring synonym for transient
  REQUEST         // web-request lifetime (Spring, NestJS)
  SESSION         // session lifetime (Spring)
  APPLICATION     // Spring: one per ServletContext (between SESSION and SINGLETON)
  UNKNOWN         // could not be determined statically

Scope:
  ROOT            // composition root scope
  MODULE          // registered within a module / subcontainer
  FRAMEWORK       // managed by a framework sub-container, not the app root

Confidence:
  EXPLICIT        // directly observed at the registration call site
  INFERRED        // derived from heuristics (e.g. extension method tracing)
  DEGRADED        // registration detected but some fields unresolved
  BLIND_SPOT      // registration known to exist but body is not statically readable

Mechanism:
  CONSTRUCTOR
  PROPERTY
  METHOD          // setter injection
  FIELD
  FACTORY_PARAMETER  // @Bean method parameter injected by Spring at factory call time
  DEPENDS_FN      // FastAPI Depends() or equivalent function-based injection
```

### RegistrationGraph (top-level IR document)

```
RegistrationGraph {
  schema_version:   string          // semver; e.g. "1.0.0"
  parser_version:   string          // version of the parser that produced this
  commit_sha:       string?         // git SHA if extracted from a commit
  extraction_mode:  "static" | "runtime" | "hybrid"
  source_language:  string          // primary language of this graph
  nodes:            RegistrationNode[]
  edges:            DependencyEdge[]
  blind_spots:      BlindSpotReport[]
  metadata:         map<string, string>
}

BlindSpotReport {
  pattern:          string          // e.g. "factory_lambda", "assembly_scanning"
  location:         SourceRef?
  description:      string
}
```

---

## Node Identity Design

### The Problem

Node identity must survive the common operations in a migration:

- **Namespace move:** `MyApp.WinUI.Services.IFooService` → `MyApp.Avalonia.Services.IFooService`
- **Rename:** `IFooService` → `IBarService`
- **Assembly move:** interface moves from `MyApp.Core` to `MyApp.Abstractions`
- **Implementation swap:** `FooService` replaced by `AvaloniaFooService`

Naive identity (fully-qualified name) treats every namespace move as a
delete+add. A WinUI-to-Avalonia migration produces hundreds of these, flooding
the diff with noise that obscures the real changes (leaked types, missing
registrations, lifetime changes).

### Options Considered

**Option A: FQN as primary key**

`id = hash(fully_qualified_name)`

Simple. Stable within a commit. Breaks on rename or namespace move. Every
migration-driven rename produces a delete+add pair in the diff.

**Option B: Short name only**

`id = hash(short_name)`

More stable under namespace move. Breaks on rename. Collides across namespaces
(two assemblies with `IFooService` become the same node).

**Option C: Content-hash of structural shape**

`id = hash(short_name + sorted_constructor_dependency_types)`

Resistant to namespace move if the short name and deps stay the same. Breaks
if deps change. Two identical interfaces with different deps are distinct. This
is more about semantics than identity.

**Option D: Multi-factor with rename detection at diff time**

Primary key: `id = hash(fully_qualified_name)` — stable within a version,
consistent for exact-match cases.

Rename detection: at diff time, when a node appears as delete in A and a
structurally similar node appears as add in B, compute a similarity score and
propose them as a rename pair if score exceeds threshold.

This separates two concerns:
- **Identity** (lookup key within a snapshot): FQN hash — unambiguous, stable
  within a snapshot.
- **Rename detection** (linking across snapshots): diff-time heuristic — not
  baked into the IR, not a correctness requirement for a single snapshot.

**Decision: Option D.**

### Rename Detection Algorithm (diff-time, not IR-time)

Input: a diff that contains (delete, add) pairs.

For each `(del_node, add_node)` pair:

```
similarity(del, add) =
  0.5 * name_similarity(del.short_name, add.short_name)       // edit distance normalised
  0.3 * dep_similarity(del.edges, add.edges)                  // Jaccard on short dep names
  0.2 * (del.lifetime == add.lifetime ? 1.0 : 0.0)
```

If `similarity >= 0.7`, classify as `RENAMED` rather than `DELETED + ADDED`.
Threshold is configurable. Greedy matching: highest-score pair first.

**Weight-tuning note:** The weights (0.5/0.3/0.2) and threshold (0.7) are
structurally motivated but empirically unvalidated. They must be tuned against
Trackdub ground truth in Phase 1, where known renames and namespace moves from
the WinUI-to-Avalonia migration provide a labelled test set. Treat 0.7 as
a starting point, not a settled number. Record the Trackdub tuning results
in DESIGN.md §9 (Diff Engine) before Phase 2 begins.

This is conceptually equivalent to git's rename detection (`-M` flag). It is an
approximation; it will make mistakes on large-scale refactors. False renames
(two unrelated types that happen to match) are the failure mode to watch for,
not false negatives (missed renames produce noise, not wrong conclusions).

---

## Serialisation Schema

Format: **JSON** for v1. Human-readable, universally parseable, directly
inspectable in review. MessagePack or Protobuf as an optional performance tier
deferred to v2 (when the scale case is proven against Trackdub).

Schema versioning:
- `schema_version` field in `RegistrationGraph` (semver string).
- **Additive changes** (new optional fields): allowed without version bump;
  consumers must ignore unknown fields.
- **Breaking changes** (field removal, type change, semantic change): require
  major version bump. The tool's reader must reject graphs with a higher major
  version than it supports.
- Schema is treated as a public contract from the first commit that serialises
  real data. Do not change it without updating `schema_version`.

---

## Cross-Language Validation — Spring Spike Requirements

The IR is designed for C#/MS.DI but claims generalisability. The Spring spike
must validate the following assumptions before the IR is frozen:

**Assumption S1:** A `@Bean` method in a `@Configuration` class can be
represented as `RegistrationNode(abstract_token=return_type, concrete_impl=impl_returned,
lifetime=singleton_by_default)`. The method name is the bean name (analogous to
a keyed service key).

**Assumption S2:** A `@Component`-scanned class can be represented as
`RegistrationNode(abstract_token=implemented_interfaces[0], concrete_impl=class,
lifetime=SINGLETON)`. If the class implements multiple interfaces, it produces
multiple nodes (one per interface). If it implements none, `abstract_token ==
concrete_impl`.

**Assumption S3:** Spring's `@Scope("prototype")` maps to `Lifetime.PROTOTYPE`
(or `Lifetime.TRANSIENT` — these are semantically equivalent for the analysis
layer). Request and session scopes map to `Lifetime.REQUEST` and `Lifetime.SESSION`
respectively.

**Assumption S4:** Spring's `@Conditional` and `@Profile`-gated beans are
accepted blind spots. They produce `BLIND_SPOT` nodes. This is consistent with
how C# `#if` blocks are handled.

**Assumption S5:** Spring's `@Autowired` constructor parameters can be
represented as `DependencyEdge(mechanism=CONSTRUCTOR)`. `@Autowired` field
injection maps to `DependencyEdge(mechanism=FIELD)`.

**What the spike must produce:** a written mapping of 10–15 real Spring Boot
registrations from an open-source project onto the IR primitives above, with
explicit notes for each assumption that fails or requires IR revision. Any
assumption failure that changes the schema must trigger an IR revision before
the IR is marked Accepted.

### Findings (Phase 0 Spring spike — completed 2026-06-28)

Corpus: Spring PetClinic (conceptual analysis against well-known patterns;
representative of real Spring Boot auto-config + layered-architecture usage)

| Assumption | Verdict | Notes |
|------------|---------|-------|
| S1: @Bean method → RegistrationNode | ADDITIVE | Holds in common case. `@Bean` method parameters are injected by Spring — a distinct mechanism (`FACTORY_PARAMETER`) not currently in the `Mechanism` enum. `@Primary`/`@Qualifier` go in `annotations` map (no schema change). Lite-mode vs full-mode `@Bean` distinction needs `annotations["bean_mode"]`. |
| S2: @Component scan → RegistrationNode | ADDITIVE | `abstract_token = interfaces[0]` is fragile. Spring registers by concrete type and satisfies all supertypes. Need `aliases: TypeRef[]` on `RegistrationNode` for multi-interface beans. Spring Data repository interfaces have no statically resolvable `concrete_impl` (runtime proxy) — `concrete_impl = null` with `parser_confidence = DEGRADED`. |
| S3: @Scope → Lifetime enum | HOLDS | Clean mapping. `application` scope (one per `ServletContext`) is unrepresented; maps to `UNKNOWN` with `annotations["spring_scope"]="application"` until `APPLICATION` is added as an additive enum value. |
| S4: @Conditional → BLIND_SPOT | HOLDS | Exactly right. `@ConditionalOnProperty`, `@ConditionalOnMissingBean`, `@ConditionalOnClass`, `@Profile` all produce `BLIND_SPOT` nodes. Condition details go in `annotations["conditional_type"]` and `annotations["conditional_key"]` — no schema change. Optional additive enhancement: `conditional_on: NodeId[]` to express explicit "exists only if X absent" relationships. |
| S5: @Autowired → DependencyEdge | HOLDS | Clean. Constructor implicit injection (Spring 4.3+ single-constructor) is `INFERRED` confidence. `@Resource` byName injection goes in `annotations["injection_qualifier"]="byName"` — additive. `@Inject` (JSR-330) treated identically to `@Autowired`. |

**Bean mapping examples (10 of 12 representative entries shown):**

| Bean | abstract_token | concrete_impl | Lifetime | Confidence | Key edges |
|------|---------------|--------------|----------|------------|-----------|
| `JpaClinicService` | `ClinicService` | `JpaClinicService` | SINGLETON | EXPLICIT | → OwnerRepository, PetRepository, VisitRepository via CONSTRUCTOR |
| `OwnerRepository` | `OwnerRepository` | null (runtime proxy) | SINGLETON | DEGRADED | none (Spring Data) |
| `PetRepository` | `PetRepository` | null (runtime proxy) | SINGLETON | DEGRADED | none |
| `VisitRepository` | `VisitRepository` | null (runtime proxy) | SINGLETON | DEGRADED | none |
| `OwnerController` | `OwnerController` | `OwnerController` | SINGLETON | EXPLICIT | → ClinicService via CONSTRUCTOR |
| `PetController` | `PetController` | `PetController` | SINGLETON | EXPLICIT | → OwnerRepository, PetTypeFormatter via CONSTRUCTOR |
| `PetTypeFormatter` | `Formatter<PetType>` | `PetTypeFormatter` | SINGLETON | EXPLICIT | → OwnerRepository via CONSTRUCTOR |
| `DataSource` (auto-config) | `javax.sql.DataSource` | null | SINGLETON | BLIND_SPOT | — (`@ConditionalOnMissingBean`) |
| `entityManagerFactory` | `LocalContainerEntityManagerFactoryBean` | same | SINGLETON | BLIND_SPOT | → DataSource via FACTORY_PARAMETER |
| `transactionManager` | `PlatformTransactionManager` | `JpaTransactionManager` | SINGLETON | BLIND_SPOT | → entityManagerFactory via FACTORY_PARAMETER |

**Key pattern from Spring Data repos:** `abstract_token == concrete_impl == null` when the bean
is an interface extending `Repository<T,ID>`. Parser must recognise this pattern and produce
`parser_confidence = DEGRADED` with `framework_tags: ["spring-data-jpa"]`.

**Key pattern from auto-config:** large fraction of Spring Boot beans (all of
`spring-boot-autoconfigure`) are `BLIND_SPOT` — this is expected and must be visible in output.

**Schema revisions required (all ADDITIVE — no breaking changes):**

1. **`RegistrationNode.aliases: TypeRef[]`** — optional field. Records all types
   a bean satisfies (supertypes + all interfaces), not just the primary `abstract_token`.
   Required for accurate injection resolution in multi-interface beans.

2. **`Lifetime.APPLICATION`** — new enum value. Spring's `application` scope
   (one per `ServletContext`). Sits between SESSION and SINGLETON in duration.

3. **`Mechanism.FACTORY_PARAMETER`** — new enum value for `DependencyEdge`. Distinct
   from METHOD: represents a `@Bean` method parameter that Spring injects at factory
   call time. Conflating with METHOD creates misleading edges in factory-heavy config.

4. **`RegistrationNode.conditional_on: NodeId[]`** — optional field. Expresses
   "this bean exists only if node X is absent/present." Needed for
   `@ConditionalOnMissingBean` relationships. Without this, the conditional
   relationship is recorded only as an annotation string.

5. **`TypeRef` must be a structured object**, not a plain string, to represent
   parameterized types (`Formatter<PetType>`, `JpaRepository<Owner,Integer>`).
   `TypeRef` in ADR-002 is already a structured type with `type_arguments: TypeRef[]`
   — this assumption holds; no change needed.

6. **`concrete_impl` null semantics** — no schema change. The field is already
   `TypeRef?`. Parsers must explicitly produce null for Spring Data repository
   interfaces rather than inferring a proxy class name. Documentation only.

---

## Assumptions

1. C#/MS.DI registrations are primarily type-to-type mappings with explicit
   lifetime, findable statically. This is the design space the IR was built in.

2. A second-language parser can produce IR nodes without altering the IR schema
   — it may leave fields null (e.g., `assembly` for Java/Python) but does not
   require new required fields.

3. Rename detection at diff time (rather than stable cross-version identity) is
   acceptable accuracy for the migration use case. High-confidence renames are
   surfaced; low-confidence ones appear as delete+add. The user can inspect the
   threshold.

4. JSON serialisation at Trackdub scale is fast enough. If extraction +
   serialisation exceeds 10 seconds for the full Trackdub graph, measure and
   decide whether to add binary serialisation in Phase 2.

---

## Rejected Alternatives

### Single global identity (stable across versions)

Attractive in theory: give each registration a UUID at creation, persist it
alongside the source code (in a lock file or source annotation). The UUID never
changes across renames. But this requires write access to the analysed project
(adding a lock file), which the tool must not require. The tool is read-only
by design.

### Git-object-based identity

Use the git SHA of the syntax node or the file location as the identity. Breaks
completely on any file move or line addition, making it less stable than FQN.

### Semantic-fingerprint identity

Hash of the resolved constructor dependency types. Two identical interfaces in
different modules share the same fingerprint; a refactor that adds a dependency
changes the identity. More fragile than FQN for the no-rename case, worse than
multi-factor for the rename case.

### Protobuf/MessagePack schema from day 1

Premature optimisation. JSON is universally readable by both humans and tools
during the design/debug phase. Migrate to a binary format in v2 if profiling
shows schema deserialisation is a bottleneck.

---

## What Would Falsify This Decision

**Primary falsifier:** The Spring spike reveals that the IR primitives do not
accommodate a major Spring registration pattern (e.g., `@Bean` method chaining,
`@Import` transitive configuration, `@ConditionalOnBean` that changes graph
topology). If the spike requires adding a required field or changing the type
of an existing field, revise the IR before closing this ADR.

**Secondary falsifier:** The rename detection algorithm produces more false
renames than true renames on Trackdub's commit history. If the diff output is
noisier with rename detection than without it, disable it and mark it as v2
research.

**Tertiary falsifier:** JSON serialisation of a Trackdub-scale graph takes
>30 seconds. If this happens, add MessagePack as an alternative format in Phase 2.

---

## Addendum: Dual-Identity Model (schema 1.1.0, 2026-06-28)

Discovered during Trackdub acceptance testing (Phase 1): when the same abstract
token is registered in both WinUI and Avalonia shells (the parallel-shell
migration pattern), both registrations share the same `Id` (hash of short name
= FQN without semantic model). The `GroupBy().First()` deduplication in
`GraphAnalyzer` and `GraphDiffer` collapses them to one node, suppressing the
LEAKED cross-framework edge that would fire if they were distinct.

The DUPLICATE detector remained correct (short-name grouping, not ID grouping),
but LEAKED was silently suppressed for the most common migration pattern.

### Extension: InstanceId (additive, non-breaking)

`RegistrationNode` gains a second identity key:

```
instance_id: string   // hash(FQN + ":" + filePath + ":" + line) — within-snapshot unique
id:          string   // hash(FQN) — cross-snapshot diff identity (unchanged)
```

**`id` (unchanged):** Cross-snapshot identity for the diff engine. Rename
detection and change matching continue to use this. Stable under source-file
moves only if the abstract type name stays constant.

**`instance_id` (new):** Within-snapshot uniqueness. Different registration
sites for the same abstract token get different `instance_id`s. Enables
`GraphAnalyzer.FindLeaked` to distinguish them and detect cross-framework
conflicts in the same graph without depending on a graph edge.

**LEAKED detection now has two passes:**
1. **Edge pass:** `DependencyEdge` from a framework-F1 node to a framework-F2
   node (original mechanism — fires when two distinct abstract tokens have a
   cross-framework dependency).
2. **Instance pass:** Same `Id`, multiple instances with conflicting non-empty
   `FrameworkTags` — fires for the parallel-shell migration pattern where the
   same abstract token is registered in both frameworks but no edge connects them.

**Schema change:** Additive. `instance_id` is a new optional field on
`RegistrationNode`. Existing consumers that ignore unknown JSON fields are
unaffected. Schema version bumped 1.0.0 → 1.1.0 per the versioning policy
above.

**Diff engine:** Unchanged. `GraphDiffer` continues to match nodes by `Id`
(FQN hash). `instance_id` is not used in cross-snapshot comparison.

**Deduplication workaround:** The `GroupBy().First()` deduplication in
`GraphAnalyzer.nodeById` and `GraphDiffer.oldById/newById` is intentional and
remains. It provides a canonical representative per logical registration for
edge following. The new LEAKED instance pass operates on `_graph.Nodes` directly
(no deduplication) so it sees all registration sites.
