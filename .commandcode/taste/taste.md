# Taste (Continuously Learned by [CommandCode][cmd])

[cmd]: https://commandcode.ai/

# csharp
- Use `is` pattern matching over `==` for string comparisons, especially when checking multiple values with `or`. Confidence: 0.85
- Prefer method group references (`.GroupBy(StrictDuplicateGroupKey, ...)`) over lambda expressions (`.GroupBy(n => n.DuplicateGroupKey, ...)`) when the lambda just forwards to a method. Confidence: 0.80
- Include both BCL type names and their C# aliases in type collections (e.g., add both `"String"` and `"string"`, both `"Int32"` and `"int"`). Confidence: 0.75
- Use `StringComparer.Ordinal` consistently on all `HashSet<string>` and LINQ `GroupBy` operations. Confidence: 0.80
- Flatten nested conditionals with guard clauses and early returns instead of deep `if` nesting. Confidence: 0.70

# testing
- Use `[Trait]` attributes for xUnit test categorization with corpus gate identifiers rather than relying on test naming conventions alone. Confidence: 0.75
- Annotate hard-coded assertion values with inline comments referencing the specific commit SHA and context (e.g., `// Parser 0.3.7 @ b57fc832: three broken chains after BlindSpot ctor-edge filter`). Confidence: 0.80
- Use `[Collection]` and `[Trait]` together to organize integration tests by corpus. Confidence: 0.70

# ci-cd
- Use NuGet trusted publisher (OIDC) over API key secrets for NuGet.org package publishing. Confidence: 0.80
- Pin CI corpus fixtures to specific commit SHAs rather than branch names, and reference the pin constants via a shared `TrackdubPin` class. Confidence: 0.75
- Use `if: >` multi-line expression syntax for GitHub Actions conditional job triggers instead of single-line expressions. Confidence: 0.70

# code-style
- Extract parameter objects or add explicit parameters (like `FindingTier tier`) instead of hardcoding enum values inline. Confidence: 0.75
- Bump `ParserVersion` string constant on every meaningful parser change rather than relying on assembly version attributes. Confidence: 0.70
- Use `out var` / `out candidates` directly in `TryGetValue` calls rather than declaring the variable on a separate line. Confidence: 0.70

# commit-messages
- Use conventional commit prefixes like `fix(ci):`, `feat:`, `docs:` with scoped parentheticals for the affected subsystem. Confidence: 0.70
