# ADR-005: Spring Language Parser — Scope and Approach

**Status:** Accepted
**Date:** 2026-06-28
**Accepted:** 2026-06-28 (Phase 6 kickoff; Trackdub Phase 5 regression verified)
**Effort:** High (Opus-class reasoning required; see AGENTS.md routing matrix)

---

## Context

ADR-004 deferred the Spring parser until after the IR was validated against
Spring patterns via a paper spike. That spike is complete (ADR-002 addendum,
Phase 0). The IR accommodates Spring patterns with five additive extensions and
no breaking changes. Phase 6 is the implementation phase for the Spring parser.

This ADR decides: which AST library, which Spring patterns are in scope for
v1, and how the parser plugs into the shared parser contract.

---

## Decision

### Q1: Java AST library — **Tree-sitter (primary)**

**Accepted:** Tree-sitter with the Java grammar via .NET bindings, matching the
C# parser's syntactic-only philosophy (no semantic model, no JDK type resolution).

**Rejected for v1 primary path:** JavaParser subprocess (JVM startup cost, IPC
complexity) — retained as **fallback** if tree-sitter cannot reliably detect
`@Bean` return types and `@Autowired` injection sites on Spring PetClinic
(see falsifiers below).

**Rejected:** Regex/string matching; Roslyn-equivalent for Java (does not exist).

### Q2: Spring patterns in scope for v1

**In scope (EXPLICIT or DEGRADED confidence):**
- `@Bean` methods in `@Configuration` classes → `RegistrationNode` (EXPLICIT)
- `@Component`, `@Service`, `@Repository`, `@Controller` on concrete classes → EXPLICIT
- `@Autowired` constructor parameters → `DependencyEdge` CONSTRUCTOR (EXPLICIT)
- `@Autowired` field injection → `DependencyEdge` FIELD (EXPLICIT)
- `@Scope("singleton" | "prototype" | "request" | "session")` → `Lifetime`
- Spring Data `Repository<T, ID>` interfaces → `concrete_impl=null`, DEGRADED
- `@Primary` / `@Qualifier` → `annotations` map entries

**Accepted blind spots (BLIND_SPOT nodes):**
- `@ConditionalOnProperty`, `@ConditionalOnMissingBean`, `@ConditionalOnClass`
- `@Profile`-gated beans
- Spring Boot auto-configuration (`spring.factories` / `AutoConfiguration.imports`)
- `@Import` transitive configuration chains
- Dynamic `@Bean` definitions (conditional method body)

**Out of scope for v1:** Spring Integration/Batch, XML Spring config, Kotlin Spring DSL.

### Q3: Parser interface integration — **`IStaticParser` in `DCS.Core.Parsing`**

```csharp
public interface IStaticParser
{
    RegistrationGraph ParseCommit(string repoPath, string commitSha);
    RegistrationGraph ParseDirectory(string directoryPath);
}
```

`DCS.Parser.Java.SpringStaticParser` implements this interface (scaffold landed
2026-06-28). Structure mirrors `DCS.Parser.CSharp`: LibGit2Sharp blob read,
`ExtractionCache` keyed by SHA + `ParserVersion`, `FrameworkBoundaryModel` for tags.

CLI language routing (`.java` → Java parser) is a follow-up task; Phase 6 gate
is PetClinic IR quality, not multi-language CLI dispatch.

### Q4: Framework tags for Spring — **built into `FrameworkBoundaryModel`**

| Tag | Namespace prefix |
|-----|------------------|
| `spring-mvc` | `org.springframework.web.` |
| `spring-security` | `org.springframework.security.` |
| `spring-data` | `org.springframework.data.` |
| `spring-boot` | `org.springframework.boot.` |
| `spring-core` | `org.springframework.` (lowest priority among Spring tags) |

---

## Implementation sequence (Phase 6)

1. ✅ Scaffold `DCS.Parser.Java` + `IStaticParser` + Spring framework tags
2. Add tree-sitter-java dependency + query-based annotation visitor
3. `@Bean` / `@Configuration` extraction
4. `@Component` stereotype extraction
5. `@Autowired` edge extraction
6. `@Conditional` / auto-config → BLIND_SPOT; Spring Data → DEGRADED
7. Verification against Spring PetClinic

---

## Assumptions

1. Tree-sitter Java grammar reliably exposes annotation nodes and method signatures
   without a semantic model.
2. Spring annotation FQNs in import statements or fully-qualified annotations are
   sufficient for ≥80% of PetClinic beans.
3. Batch analysis tolerates tree-sitter native binding deployment on Windows/Linux CI.

---

## What Would Falsify This Decision

- Tree-sitter cannot reliably identify `@Bean` return types and parameter injection
  sites on PetClinic → **fall back to JavaParser subprocess**.
- More than 20% of PetClinic beans missed by syntactic detection → expand scope or
  lower default confidence.
- Tree-sitter native bindings block CI → reconsider JavaParser AOT JAR or pure Java CLI sidecar.

---

## Rejected Alternatives

### JavaParser as primary
Rejected for v1 due to JVM subprocess latency and IPC complexity; valid fallback.

### Defer `IStaticParser` until Java parser is complete
Rejected — contract needed before parallel parser work proceeds.
