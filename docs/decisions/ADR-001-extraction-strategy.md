# ADR-001: Extraction Strategy

**Status:** Accepted
**Date:** 2026-06-28
**Effort:** High (Opus-class reasoning required; see AGENTS.md routing matrix)

---

## Context

The tool must extract DI registration graphs from a codebase. Three strategies exist:

1. **Static extraction** — parse source or IL without executing the program.
2. **Runtime extraction** — build and run the application, inspect the live DI container.
3. **Hybrid** — static-first with optional runtime enrichment.

This ADR argues each from first principles, then tests whether the diff features
actually constrain the decision the way the plan-of-plan asserts.

---

## The Diff Constraint — Testing Whether It Holds

The plan states: "Committing to diffing as a core feature largely commits the
project to static-first extraction." Let us test this claim rather than accept it.

### What the diff features require

Drift Scanner and Migration Diff must compare dependency graphs across arbitrary
git commits. This requires extraction at two (or more) points in the repository's
history, call them commit A and commit B.

### What runtime extraction requires per commit

To extract via runtime inspection at commit A:

1. The project must **build** at commit A.
2. The built application must **start** (or a harness must bootstrap the DI
   container without starting the full app).
3. A reflection-based or IServiceCollection-sniffing harness must inspect the
   container state before the app processes any requests.
4. The working tree must be at commit A (or a temporary worktree must be created).

### Why each requirement fails for the Trackdub use case

**Requirement 1: the project builds at commit A.**

Mid-migration commits routinely do not build. A WinUI-to-Avalonia migration
introduces broken references, missing assemblies, and type mismatches throughout
the transition. The canonical scenario — "detect leakage during the migration,
not after it" — means the commits of highest interest are the ones least likely
to build. Runtime extraction fails silently or noisily at exactly the commits
where the tool is most needed.

**Requirement 2: the application can start.**

Even if the project builds, running it requires configuration files, database
connections, and external services. Automated harnesses can stub these, but doing
so is build-configuration-aware work that must be re-done for each project. This
is not a general-purpose mechanism.

**Requirement 3: working tree at commit A.**

Checking out historical commits one at a time is linear and sequential. Concurrent
extraction from commits A and B requires either two worktrees or serialisation.
Worktrees are feasible but add infrastructure complexity. More importantly, the
working tree of a long-lived repository being mutated by the tool creates risk
of state pollution (untracked files, staged changes, mixed states).

Git blob reading (reading the source files directly from git's object store
without touching the working tree) eliminates all three of these. It is only
possible with static extraction.

### Conclusion on the constraint

The constraint holds, but with an important nuance: it holds *because of the
mid-migration broken-build property*, not merely because of the multi-commit
access pattern. A project that is always in a buildable state at every commit of
interest would weaken the constraint. Trackdub is not that project; the origin
story is specifically the broken-middle state of a migration.

If the diff feature were removed from v1 scope, the static constraint would
weaken — runtime extraction of the current tip is perfectly viable. But removing
the diff feature removes the tool's differentiating capability and leaves only a
visualiser of what other tools already show.

---

## First-Principles Argument for Each Strategy

### Static extraction

**Strengths:**
- Works on any git blob without checkout or build.
- No side effects on working tree.
- Deterministic: same source produces same IR regardless of runtime state or
  environment configuration.
- Cacheable per commit SHA + parser version without re-execution.
- Handles partial compilation (missing references) gracefully through Roslyn's
  fault-tolerant parsing.

**Weaknesses:**
- Cannot see dynamic registration (factory lambdas, assembly scanning).
- Cannot resolve open generics or conditional compilation states without inference.
- Requires a parser per language rather than a generic harness.
- Roslyn's MSBuildWorkspace requires SDK + restore; git-blob reading requires
  in-memory compilation from source, which loses transitive reference resolution.

**The fatal failure mode to avoid:** silently dropping registrations that cannot
be statically resolved. This would reproduce exactly the invisibility the tool
exists to cure. Mitigation: every unresolvable pattern must produce an explicit
`BLIND_SPOT` or `DEGRADED` node in the IR.

### Runtime extraction

**Strengths:**
- Sees everything the DI container sees: factory lambdas resolved, assembly
  scanning results known, conditional compilation already decided.
- No parser per language — inspect `IServiceCollection` or equivalent directly.
- Accurate lifetime information (Singleton/Scoped/Transient from the registration
  call, not inferred).
- Decorator chains visible.

**Weaknesses:**
- Requires buildable, runnable project. Fails on mid-migration commits.
- Requires working tree checkout or worktree per commit.
- Not cacheable without executing; execution has side effects.
- Container inspection harness is project-specific (bootstrapping DI without
  the full application requires understanding the startup sequence).
- Timing-sensitive: if registrations are conditional on runtime config or
  environment variables, the harness must supply the right environment.
- No git blob access: cannot read historical state without checkout.

**Fatal failure mode:** requires a buildable, runnable project, which is
unavailable for exactly the commits of highest interest.

### Hybrid (static + optional runtime enrichment)

**Strengths:**
- Static base covers all commits; runtime enrichment fills in dynamic
  registrations for the current tip where the project builds.
- Best coverage for the current state view.

**Weaknesses:**
- Two extraction paths with semantic differences. The static IR at historical
  commit A is not structurally equivalent to the runtime-enriched IR at the
  current tip B. Diffing between them introduces category errors: a change
  that appears in the diff may be a real registration change or may be an
  artifact of switching extraction modes.
- This mode inconsistency is worse than static-only because it produces noise
  that is invisible to the user. The tool claims to be diffing two snapshots
  of the same system; it is actually diffing snapshots produced by different
  methods with different coverage characteristics.
- Adds a second implementation path, increasing maintenance burden and the
  surface area for bugs.

**The hybrid trap:** the appeal of hybrid is that it "covers everything." But
coverage without consistency is worse than consistent partial coverage, because
the user cannot reason about what the diff result means.

---

## Decision

**Static-first extraction, version 1.**

Runtime extraction and hybrid modes are deferred to v2+ as an optional
enrichment overlay, clearly labelled as such, and never used for commits that
are being compared in a diff operation.

The specific technical implications:

1. **Parser:** Roslyn in-memory compilation from git blob source. No
   MSBuildWorkspace; no `dotnet restore`. Source is read from git objects via
   libgit2sharp. Reference resolution is best-effort from known framework
   assemblies bundled with the tool.

2. **Blind spots are required output, not optional.** Every pattern that cannot
   be statically resolved produces an explicit node in the IR with a
   `parser_confidence` value of `DEGRADED` or `BLIND_SPOT`. The CLI output must
   report these. Silently dropping them is a correctness failure.

3. **The accepted blind-spot set for v1:**
   - Factory lambda bodies (dependencies hidden inside `sp => new Foo(...)`)
   - Assembly-scanning registrations (`AddServicesFromAssembly`, `Scrutor`)
   - Conditional `#if` blocks (extraction runs against the source as-is; no
     symbol pre-processing)
   - Open generics with computed type arguments
   - Dynamic decorator chains constructed at runtime
   - Reflection-based registration (`RegisterAll`, `RegisterAssemblyTypes`)

4. **Not in the blind-spot set (must be correctly extracted in v1):**
   - All direct `services.AddSingleton/AddScoped/AddTransient<T, TImpl>()` calls
   - Extension method calls that expand to the above (transitively)
   - Keyed service registrations (`AddKeyedSingleton<T>(key, ...)`)
   - Open generic registrations with explicit type arguments (`typeof(IGeneric<>)`)
   - Constructor injection dependencies resolved via semantic model

---

## Rejected Alternatives

### Runtime-only extraction

Rejected because it cannot process mid-migration commits that do not build. This
is not a corner case; it is the primary scenario.

### Hybrid with per-commit mode selection

Rejected because mode inconsistency corrupts diff results. If the tool uses
runtime extraction for the current tip and static for historical commits, the
diff is comparing incompatible snapshots. The user sees noise that is not real
change; worse, they may miss real change that is masked by the mode difference.

### Hybrid with runtime enrichment clearly labelled, static base for all diffs

Valid long-term. Rejected for v1 because it adds a second code path, requires
a runtime harness, and the primary validation case (Trackdub leakage
reproduction) does not require runtime coverage of the specific patterns static
misses. Revisit in v2 if the blind-spot set proves too large to be useful.

---

## Assumptions

1. The primary registration patterns in Trackdub's WinUI-to-Avalonia migration
   are explicit `AddSingleton/AddScoped/AddTransient` calls and extension methods
   that wrap them, not factory lambdas or assembly scanning. If this is wrong,
   static extraction cannot reproduce the known leakage and v1 fails its
   acceptance test.

2. Mid-migration commits in Trackdub do not build. If Trackdub actually builds
   cleanly at all commits of interest, the case for static weakens (though the
   determinism and working-tree-safety arguments still apply).

3. Roslyn can parse source with missing references well enough to resolve
   type identities for explicitly registered types. Missing references produce
   unknown/unresolved types, not parser crashes.

---

## What Would Falsify This Decision

**Primary falsifier:** Run static extraction against Trackdub's known-leaking
commit range. If the known WinUI-leaked registrations are in factory lambdas
or assembly scanning (not explicit calls), static extraction cannot reproduce
ground truth and v1 fails. This check must happen at Phase 1 verification,
before declaring Phase 1 done.

**Secondary falsifier:** If the diff feature is descoped (v1 ships as
"current-state visualiser only, no commit comparison"), the primary constraint
weakens. At that point, reconsider runtime or hybrid for better coverage.

**Tertiary falsifier:** If Roslyn's in-memory compilation of git blob source
produces too many unresolved type identities to be useful (most registrations
appear as `DEGRADED`), static extraction is too lossy. Measure degradation rate
on Trackdub during Phase 1 spiking.
