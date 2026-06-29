# ADR-005: Spring Language Parser — Scope and Approach

**Status:** Proposed
**Date:** 2026-06-28
**Effort:** High (Opus-class reasoning required; see AGENTS.md routing matrix)

---

## Context

ADR-004 deferred the Spring parser until after the IR was validated against
Spring patterns via a paper spike. That spike is complete (ADR-002 addendum,
Phase 0). The IR accommodates Spring patterns with five additive extensions and
no breaking changes. Phase 6 is the implementation phase for the Spring parser.

This ADR decides: which AST library, which Spring patterns are in scope for
v1, and how the parser plugs into the existing `IStaticParser` interface.

---

## Questions to Resolve

### Q1: Java AST library

**Option A: JavaParser (javaparser.org)**
- Pure Java, no JDK required for parsing
- Produces a typed AST with visitor support
- Can be called from .NET via a thin Java subprocess with JSON IPC
- Well-maintained, widely used in static analysis tools

**Option B: Tree-sitter (Java grammar)**
- Cross-platform, zero-JVM dependency
- Native bindings exist for .NET (tree-sitter-dotnet)
- Grammar-based: less semantic awareness than JavaParser (no type resolution)
- Faster than JavaParser for large files

**Option C: Regex / string matching**
- Minimal dependency
- Brittle on unusual formatting
- Unacceptable for production use

**Option D: Roslyn analogue — Microsoft.CodeAnalysis.Java (if available)**
- Does not exist as of 2026-06-28

**Likely decision:** Tree-sitter for syntactic extraction (matches the C# parser's
philosophy: syntactic-only, no semantic model). JavaParser as a fallback if
tree-sitter grammar is insufficient for annotation detection.

---

### Q2: Spring patterns in scope for v1

**In scope (EXPLICIT or DEGRADED confidence):**
- `@Bean` methods in `@Configuration` classes → RegistrationNode (EXPLICIT)
- `@Component`, `@Service`, `@Repository`, `@Controller` on concrete classes → RegistrationNode (EXPLICIT)
- `@Autowired` constructor parameters → DependencyEdge CONSTRUCTOR (EXPLICIT)
- `@Autowired` field injection → DependencyEdge FIELD (EXPLICIT)
- `@Scope("singleton" | "prototype" | "request" | "session")` → Lifetime
- Spring Data `Repository<T, ID>` interfaces → RegistrationNode, concrete_impl=null, DEGRADED
- `@Primary` / `@Qualifier` → annotations map entry

**Accepted blind spots (BLIND_SPOT nodes):**
- `@ConditionalOnProperty`, `@ConditionalOnMissingBean`, `@ConditionalOnClass`
- `@Profile`-gated beans
- Spring Boot auto-configuration (`spring.factories` / `AutoConfiguration.imports`)
- `@Import` transitive configuration chains
- Dynamic `@Bean` definitions (method body creates beans conditionally)

**Out of scope for v1 (not even BLIND_SPOT):**
- Spring Integration, Spring Batch (specialised container contexts)
- XML-based Spring configuration (legacy, declining use)
- Kotlin Spring DSL

---

### Q3: Parser interface integration

The existing `IStaticParser` interface in `DCS.Core`:

```csharp
public interface IStaticParser
{
    Task<RegistrationGraph> ParseDirectoryAsync(string rootPath, CancellationToken ct = default);
    Task<RegistrationGraph> ParseCommitAsync(string repoPath, string commitSha, CancellationToken ct = default);
}
```

The Spring parser must implement this interface. Language detection: by file
extension (`.java`) and presence of Spring annotations in imports. A `DCS.Parser.Java`
project follows the same structure as `DCS.Parser.CSharp`.

**Subprocess model (if JavaParser is used):** A thin Java CLI that reads a
file list from stdin and emits JSON IR fragments. The .NET parser spawns it,
collects the JSON, and deserializes. Tradeoff: JVM startup cost (~300ms);
acceptable for batch analysis, painful for per-file IDE use.

**Tree-sitter model (if used):** Native .NET binding; no subprocess; no JVM.
Parse is syntactic only; annotation detection via tree-sitter queries.

---

### Q4: Framework tags for Spring

Spring-specific framework tags to add to `FrameworkBoundary.cs`:

| Tag | Heuristic |
|-----|-----------|
| `spring-mvc` | `@Controller` or `@RestController` on a class, or `import org.springframework.web.*` |
| `spring-data` | `extends Repository<>` or `@EnableJpaRepositories` in file |
| `spring-security` | `import org.springframework.security.*` |
| `spring-core` | `@Component`, `@Service`, `@Bean` without a more specific tag |
| `spring-boot` | `@SpringBootApplication` in file |

---

## Assumptions

1. A JVM is available in the development environment (for JavaParser option).
   The CLI can skip Java files gracefully if no JVM is detected, logging a
   `BLIND_SPOT` for the missing parser.
2. The .NET–JVM subprocess boundary adds <500ms startup latency on a cold run,
   acceptable for batch analysis.
3. Spring annotation patterns are stable enough that syntactic detection
   without semantic resolution covers ≥80% of real-world Spring Boot apps.

---

## What Would Falsify This Decision

- Tree-sitter Java grammar cannot reliably identify `@Bean` method return types
  and parameter injection sites → fall back to JavaParser.
- JVM startup cost exceeds 2 seconds on CI baseline → reconsider subprocess
  model; consider AOT-compiled Java tool.
- More than 20% of Spring PetClinic beans are missed by syntactic detection
  → scope must expand or confidence must be set lower.

---

## Status: Proposed

This ADR will be marked Accepted when Phase 6 begins and the Q1–Q4 decisions
are confirmed by the implementation team.
