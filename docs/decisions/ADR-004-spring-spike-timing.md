# ADR-004: Spring Paper-Spike Timing

**Status:** Accepted
**Date:** 2026-06-28
**Effort:** Medium

---

## Context

The IR is designed for C#/MS.DI. The long-term bet is generalisability to
Spring (Java), Python, and TypeScript DI frameworks. ADR-002 documents the
IR primitives and lists five explicit assumptions (S1–S5) about Spring that
the IR makes without having been tested against Spring.

The question is: when does the Spring paper-spike happen relative to freezing
the IR?

---

## Options

### Option A: Spike before IR freeze (Phase 0)

Run the spike now, during planning, before any implementation. If the spike
finds IR problems, revise the schema before writing any code that depends on it.

**Cost:** a few hours of Spring documentation analysis and IR mapping work.
No code required — the spike is a paper exercise: take 10–15 Spring Boot
registrations from a real open-source project, map them onto the IR primitives
in ADR-002, note every assumption failure.

**Benefit:** prevents baking C#-specific semantics into a schema that will be
expensive to change once the C# parser and serialiser are implemented against it.

### Option B: Spike after IR freeze, before second-language parser

Do the spike when a second-language parser is actually being written. The IR
is stable for C# by then; the spike reveals what revisions are needed to
support Spring.

**Cost:** if the spike requires schema changes, those changes break the C#
serialiser, the analysis layer, any cached IR files, and potentially the diff
engine's identity assumptions — all of which are implemented by this point.

**Benefit:** the spike is concrete (written against real code, not paper).

### Option C: Defer indefinitely

Spike when a user actually requests Spring support.

**Cost:** the IR is designed on sample size one. The probability of needing
a breaking schema change grows with each feature built on an untested
abstraction.

**Benefit:** none that justifies the risk.

---

## Why Option A Is Correct

### The cost asymmetry

The spike is cheap: a few hours of documentation reading and paper mapping.
A post-implementation schema revision is expensive: it breaks the serialiser,
the parser tests, the cached IR files, and the diff engine. The cost ratio
is roughly 4 hours vs several days of regression work.

### The quality of the spike output

Option B's argument that the spike is "more concrete" when written against real
code is weak. The IR assumptions in ADR-002 are specific enough to test against
Spring documentation and Spring Boot open-source examples without writing a
parser. The five assumptions (S1–S5) can each be verified against Spring's
`@Bean`, `@Component`, `@Scope`, `@Conditional`, and `@Autowired` semantics
using documentation and one open-source project.

The spike does not need to produce working code. It needs to produce one of:
- "Assumption holds; no IR change required" — for each of S1–S5
- "Assumption fails; here is the required IR change" — with specific field
  additions, type changes, or enum additions

### The risk of designing on sample size one

The specific risk is not "Spring is different in some general way." The specific
risk is that the `RegistrationNode` schema bakes in:

1. **Type-to-type mapping as the primary registration model.** Spring's
   `@Bean` method produces a named bean (the method name) that may or may not
   map to an interface. `@Component`-scanned beans have a class-to-interfaces
   mapping but the bean token is the class name, not the interface name.

2. **Lifetime as a simple enum.** Spring has six scope types and custom scopes.
   The `Lifetime` enum in ADR-002 covers the common ones but not custom scopes.
   This is an additive extension (new enum value `CUSTOM` with a string field),
   not a breaking change — but it needs to be in the schema before v1 is frozen.

3. **Constructor injection as the primary edge mechanism.** Spring also uses
   `@Autowired` field injection heavily. `DependencyEdge.injection_mechanism`
   already includes `FIELD` — this assumption holds.

4. **Source location as a file+line reference.** Spring beans may be declared
   in XML configuration (legacy). XML-declared beans are a BLIND_SPOT in any
   static parser, but the `SourceRef` structure must support XML paths, not
   just C# file paths. This is an additive extension.

These are the four specific bets being made. The spike validates them.

---

## Spike Definition

**Corpus:** one open-source Spring Boot application with at least 30 bean
definitions. Candidate: Spring PetClinic (well-known, small, diverse patterns).

**Exercise:**
1. List the 10 most interesting/representative bean definitions.
2. Map each to a `RegistrationNode` using the schema from ADR-002.
3. For each assumption S1–S5, evaluate: holds / fails / requires additive
   change / requires breaking change.
4. For each failure: write the minimal IR schema change that fixes it.
5. Update ADR-002 §Cross-Language Validation with findings.
6. If any required change is breaking: revise ADR-002 before closing Phase 0.

**Output:** ADR-002 §Cross-Language Validation filled in with findings and any
schema revisions. Phase 0 is not complete until this section is filled.

**Blocker:** the spike must complete before ADR-002 status changes from
"Accepted (pending Spring spike validation)" to "Accepted."

---

## Decision

**Spike in Phase 0, before IR is frozen.**

Specifically: the spike must complete before Phase 0 is declared done. The
IR is not frozen until the Spring spike finds no breaking changes or the IR
is revised to address them.

The spike is a paper exercise: no Spring parser code, no Java implementation,
no running code. The deliverable is a filled ADR-002 §Cross-Language Validation
section.

---

## Rejected Alternatives

### Spike after IR freeze

Rejected because the cost asymmetry makes this irrational. Post-implementation
schema changes are 10x more expensive than pre-implementation paper validation.

### Defer indefinitely

Rejected. The IR generalisability claim is central to the project's value
proposition beyond C#. Designing an IR that cannot generalise without breaking
changes would require a versioned schema bump and migration work for every
early adopter. Better to get it right once before any IR files are in use.

---

## Assumptions

1. Spring PetClinic (or a similar open-source project) covers enough Spring
   registration patterns to validate the five IR assumptions. If the chosen
   corpus is too simple, add one enterprise-pattern example (e.g., a project
   that uses `@ConditionalOnProperty`, XML config, or custom scope annotations).

2. The spike is a paper exercise. Its findings are correct enough to design
   around even without a running parser. If the paper spike misses a pattern
   that later breaks the IR, that is a known residual risk of the paper method.

---

## What Would Falsify This Decision

Not applicable. The spike is itself the falsification mechanism for ADR-002's
IR assumptions. If the spike finds no problems, it validates ADR-002. If it
finds problems, ADR-002 is revised. The spike cannot be wrong; it can only
be incomplete. Incompleteness risk is mitigated by corpus selection.
