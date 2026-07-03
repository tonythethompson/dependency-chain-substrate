# Test Coverage Audit — DCS Phase 8.1

**Date:** 2026-07-02  
**Test Count:** 143 test methods across 30 test classes  
**Build Status:** ✅ All 156 tests passing  

## Summary

- Total test methods: **143** (XUnit)
- Total test classes: **30**
- Total assertions: ~500+ (estimated)
- Coverage: **Good for main paths**, gaps in **serializers, utilities, edge cases**

## Coverage by Project

| Project | Test Classes | Test Methods | Status |
|---------|-------------|--------------|--------|
| DCS.Analysis | 5 | 24 | 🟡 Serializers missing |
| DCS.Cli | 6 | 18 | 🟡 Parser validation missing |
| DCS.Core | 2 | 5 | 🔴 **Weak** |
| DCS.Diff | 1 | 10 | ✅ Adequate |
| DCS.Fix | 8 | 25 | ✅ Good |
| DCS.Parser.CSharp | 9 | 34 | ✅ Excellent |
| DCS.Parser.Java | 5 | 19 | ✅ Good |
| DCS.Runtime | 4 | 8 | 🟡 Listeners/writers |
| **DCS.Cli** | | | 🟡 CliParserFactory |

## High-Priority Gaps

### 1. **Serialization Edge Cases** 🔴
**Risk:** Data corruption on malformed input

Missing tests:
- `AnalysisReportSerializer` — no error cases for malformed JSON
- `IrSerializer` — no round-trip validation with invalid data
- `ParseResultSerializer` — missing null/empty input tests

**Recommendation:** Add `*SerializerTests.cs` for each serializer with:
- Valid/invalid JSON fixtures
- Round-trip assertions
- Malformed data error handling
- Version compatibility tests

### 2. **CLI Parser Validation** 🔴
**Risk:** Invalid user input crashes or silently fails

Missing tests:
- `CliParserFactory` (121 LOC, 0 tests)
- Exception paths in `CliOptions` parsing (5 throws, 0 test assertions)
- Missing argument validation scenarios
- Conflicting options (e.g., `--no-cache` + `--cache-dir`)

**Recommendation:** Add `CliParserFactoryTests.cs`:
- Valid command line combinations
- Invalid arguments → helpful error messages
- Conflicting option detection
- Default value fallbacks

### 3. **Core Module Under-tested** 🟡
**Risk:** Foundation bugs propagate to all consumers

Current: 2 test classes, 5 methods  
Source: DCS.Core has IR schema, identity logic, extensions

Missing:
- `AnalysisReportModels` — no model validation tests
- `FrameworkTagger` — no tag assignment tests
- `ExtractionQualityMetricsComputer` — no metric calculation tests

**Recommendation:** Add Core model tests (target: 15+ test methods):
- Model construction with invalid states
- Immutability assertions
- Identity hash consistency

### 4. **Runtime Utilities** 🟡
**Risk:** Silent failures in analysis phase

`RuntimeGraphEnricher` (15 public members) — only 8 runtime tests total

Missing:
- Graph enrichment edge cases (null inputs, empty graphs)
- Log reader/writer format validation
- Listener event sequences

### 5. **CLI Integration Scenarios** 🟡
**Risk:** Happy path works, error flows don't

Current: 18 Cli tests (mostly output validation)  
Missing:
- Invalid repo paths → clear error
- Missing frameworks → helpful message
- Fix apply with invalid token → security validation
- Build verification failure modes

**Recommendation:** Expand `EnrichCommandTests.cs` or add `CliIntegrationTests.cs` (10–15 tests)

## Corpus Gate Tests ✅

31 corpus gate tests validate against real codebases:
- Trackdub C# migration (4 gates × 3 test projects)
- Spring PetClinic Java (1 gate)

These cover happy-path and prevent regression. **Not counted in unit test audit.**

## Exception Handling Coverage

**Currently missing `Assert.Throws` patterns:**

| Project | Throws | Assert.Throws | Gap |
|---------|--------|---------------|-----|
| Parser.Java | 2 | 0 | ❌ Error cases |
| Fix | 3+ | 1 | ❌ Mostly uncovered |
| Cli | 5+ | 0 | ❌ Validation errors |

**Recommendation:** Each throw point should have 1+ corresponding test.

## Recommendations (Priority Order)

### 1. **HIGH** — Fix Security-Critical Gaps (1–2 days)
```
- Add CliParserFactoryTests (conflicting args, validation)
- Add serializer error case tests (malformed data)
- Expand Fix exception tests (safety guard validation)
```

### 2. **MEDIUM** — Improve Core Coverage (2–3 days)
```
- Add 10 Core model tests (identity, immutability)
- Expand CLI integration scenarios (error paths)
- Add GraphEnricher edge case tests
```

### 3. **OPTIONAL** — Completeness (future)
```
- Add AnalysisReportSerializer round-trip tests
- Add RuntimeLogReader/Writer format validation
- Property-based testing (QuickCheck style) for parsers
```

## File Structure Recommendations

```
tests/DCS.Cli.Tests/
├── CliParserFactoryTests.cs          (NEW — 15 tests)
├── CliValidationTests.cs              (NEW — 10 tests)

tests/DCS.Core.Tests/
├── CoreModelTests.cs                  (NEW — 15 tests)

tests/DCS.Analysis.Tests/
├── AnalysisReportSerializerTests.cs   (NEW — 10 tests)

tests/DCS.Fix.Tests/
├── FixSafetyGuardExceptionTests.cs    (NEW — 8 tests)

tests/DCS.Runtime.Tests/
├── RuntimeGraphEnricherEdgeCasesTests.cs (NEW — 10 tests)
```

## Success Criteria

- [ ] All throws have ≥1 `Assert.Throws` test
- [ ] Serializers tested with invalid input
- [ ] CLI parser tested with conflicting/invalid args
- [ ] Core models validated for invariants
- [ ] Runtime graph enricher handles null/empty inputs
- [ ] Coverage report shows 80%+ line coverage on critical paths

---

**Next Step:** Prioritize `CliParserFactoryTests` (security+usability) and `SerializerTests` (data integrity).
