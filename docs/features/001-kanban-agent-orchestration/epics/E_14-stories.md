# E14 — Migrate test suites from xUnit to TUnit + FluentAssertions to Shouldly

> **Status:** `[x]` **Closed 2026-05-15.** Migration was completed in-place during the early E01 work — by the time this epic was opened for execution, all three test projects (`Antiphon.Tests`, `Antiphon.E2E`, `Antiphon.Agents.Pty.Tests`) already referenced `TUnit 1.44.0` + `Shouldly 4.*` only, and the codebase contained zero `[Fact]`, `[Theory]`, `using Xunit`, `using FluentAssertions`, `SkippableFact`, or `.Should()` calls. Doc cleanup performed; verification run green. Stories below are kept for historical traceability.
>
> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked · `[-]` dropped.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** swap test framework from xUnit → [TUnit](https://github.com/thomhurst/TUnit) and assertion library from FluentAssertions → Shouldly across all test projects so subsequent epics gain native async cancellation tokens, source-generated discovery, built-in skip, faster CI, and a permissively-licensed assertion library.

**Why early:** suite is small at end of E01 (RingBuffer + PtyAgentRunner + headed claude tests only). Migration is cheap now, expensive once E02–E07 add hundreds of tests. Run **between E01-S01 (initial proof on xUnit) and the bulk of E02**.

**Covers:** NFR-testability — keep tests fast, cancellation-correct, AOT-friendly.

---

## Stories

- **E14-S01** `[x]` Spike: stand up empty `Antiphon.Tunit.Spike` project on TUnit. Confirm xUnit + TUnit can co-exist in same `dotnet test` solution run (or document migration must be all-at-once). *Resolved: migration done all-at-once; coexistence not needed.*

- **E14-S02** `[x]` Migrate `Antiphon.Tests` (server unit tests).
  - `[Fact]` → `[Test]`, `[Theory]+[InlineData]` → `[Test]+[Arguments]`.
  - `IAsyncLifetime` → `[Before(Test)]`/`[After(Test)]` (or class equivalents).
  - `[Trait("Category","X")]` → `[Category("X")]`.
  - `Microsoft.AspNetCore.Mvc.Testing` + `Testcontainers.PostgreSql` wire correctly via TUnit hooks.

- **E14-S03** `[x]` Migrate `Antiphon.E2E`. Per-attribute swap as S02; long-running flows use TUnit-supplied `CancellationToken` parameter.

- **E14-S04** `[x]` Migrate `Antiphon.Agents.Pty.Tests`.
  - Attribute swap.
  - `Xunit.SkippableFact` + `Skip.IfNot(...)` replaced with `throw new TUnit.Core.Exceptions.SkipTestException(...)` (centralised in `ClSession.SkipIfNotEligible()`).
  - Manual `Task.Delay` budgets converted to per-test `CancellationToken` parameters.

- **E14-S05** `[-]` ~~Migrate `Antiphon.Agents.Pty.Headed.Tests`.~~ **Dropped:** no separate headed test project exists. Headed scenarios live inside `Antiphon.Agents.Pty.Tests`, gated at runtime by `ClSession.SkipIfNotEligible()` (env var `ANTIPHON_HEADED_TESTS=1`). See `tests/Antiphon.Agents.Pty.Tests/TEST-STRATEGY.md` §7.

- **E14-S06** `[x]` Update CI invocation, docs, and per-project test strategy files.
  - TUnit uses `dotnet run --project tests/<X>` + `--treenode-filter` syntax (documented in root `CLAUDE.md` Gotchas).
  - `tests/Antiphon.Agents.Pty.Tests/TEST-STRATEGY.md` rewritten to reflect TUnit/Shouldly.

- **E14-S07** `[x]` Remove xUnit + FluentAssertions + SkippableFact NuGet refs across solution.
  - All three test csprojs reference only `TUnit` + `Shouldly` (+ project-specific test deps like `Microsoft.Playwright`, `Testcontainers.PostgreSql`).
  - `rg` sweep over `src/` + `tests/` for `Xunit`, `FluentAssertions`, `SkippableFact`, `.Should()` returns 0 code matches.

---

## Acceptance

- ✅ All previously-passing tests still pass under TUnit.
- ✅ `dotnet run --project tests/<X>` reports 0 failures (modulo known infrastructure-dependent test that needs live DB).
- ✅ No `Xunit.*` or `xunit.runner.*` PackageReference remains.
- ✅ `tests/Antiphon.Agents.Pty.Tests/TEST-STRATEGY.md` updated.

---

## Out of Scope

- Adding new test coverage. This epic is mechanical migration only (framework + assertion library swap).
- Source-generator AOT publish validation (separate epic if/when we publish AOT binaries).
