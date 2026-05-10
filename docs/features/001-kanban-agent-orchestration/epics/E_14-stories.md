# E14 — Migrate test suites from xUnit to TUnit

> **Status legend:** `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked.
> **Index:** [05-epics.md](../05-epics.md) · **Reqs:** [01-requirements.md](../01-requirements.md)

**Goal:** swap test framework from xUnit → [TUnit](https://github.com/thomhurst/TUnit) across all test projects so subsequent epics gain native async cancellation tokens, source-generated discovery, built-in skip, and faster CI.

**Why early:** suite is small at end of E01 (RingBuffer + PtyAgentRunner + headed claude tests only). Migration is cheap now, expensive once E02–E07 add hundreds of tests. Run **between E01-S01 (initial proof on xUnit) and the bulk of E02**.

**Covers:** NFR-testability — keep tests fast, cancellation-correct, AOT-friendly.

---

## Stories

- **E14-S01** `[ ]` Spike: stand up empty `Antiphon.Tunit.Spike` project on TUnit. Confirm xUnit + TUnit can co-exist in same `dotnet test` solution run (or document migration must be all-at-once).
  - Work items:
    - Add `TUnit` PackageReference (latest stable). Confirm Microsoft.NET.Test.Sdk compatibility.
    - Write 1 trivial passing TUnit test. Run via `dotnet test`.
    - Write 1 trivial TUnit `[Skip]` test, confirm reported skipped not failed.

- **E14-S02** `[ ]` Migrate `Antiphon.Tests` (server unit tests).
  - Work items:
    - Replace `[Fact]` → `[Test]`, `[Theory]+[InlineData]` → `[Test]+[Arguments]`.
    - Replace `IAsyncLifetime` → `[Before(Test)]`/`[After(Test)]` (or class equivalents).
    - Replace `[Trait("Category","X")]` → `[Category("X")]`.
    - Verify `Microsoft.AspNetCore.Mvc.Testing` + `Testcontainers.PostgreSql` still wire (lifecycle hook order).

- **E14-S03** `[ ]` Migrate `Antiphon.E2E`.
  - Work items:
    - Same per-attribute swap as S02.
    - Confirm long-running E2E flows still respect framework-supplied cancellation token (replace manual `TimeSpan` waits where reasonable).

- **E14-S04** `[ ]` Migrate `Antiphon.Agents.Pty.Tests` (rename of PtySpike.Tests after E01 promotion).
  - Work items:
    - Swap attributes.
    - Replace `Xunit.SkippableFact` + `Skip.IfNot(...)` with TUnit `[Skip]` / `[SkipException]` pattern.
    - Convert manual `Task.Delay` timeouts to per-test `CancellationToken` parameters.

- **E14-S05** `[ ]` Migrate `Antiphon.Agents.Pty.Headed.Tests`.
  - Work items:
    - Same as S04.
    - Use `[DependsOn]` to chain `cl --version` smoke before heavier prompts (replaces ad-hoc Skip-on-API-down probe).

- **E14-S06** `[ ]` Update CI invocation, docs, and per-project test strategy files.
  - Work items:
    - `dotnet test` filter syntax differs — update any pipeline scripts.
    - Update `tests/Antiphon.Agents.Pty.Tests/TEST-STRATEGY.md` to reflect TUnit attributes.
    - Update root `AGENTS.md` / `CLAUDE.md` if test-running guidance present.

- **E14-S07** `[ ]` Remove xUnit + FluentAssertions + SkippableFact NuGet refs across solution. Final cleanup PR.
  - Work items:
    - **Switch assertion library FluentAssertions → Shouldly** across all test projects. Reason: FluentAssertions v8 licensing requires paid subscription for commercial use; Shouldly is permissive (BSD-3) and idiomatic.
    - Mechanical rewrite: `x.Should().Be(y)` → `x.ShouldBe(y)`, `x.Should().Contain(y)` → `x.ShouldContain(y)`, `x.Should().BeTrue("...")` → `x.ShouldBeTrue("...")`, `x.Should().BeLessThan(t)` → `x.ShouldBeLessThan(t)`.
    - Replace `using FluentAssertions;` → `using Shouldly;`.
    - Search-and-destroy stragglers: `using Xunit;`, `[Fact]`, `[Theory]`, `xunit.runner.*`, `FluentAssertions` package refs, `Xunit.SkippableFact` refs.
    - Verify no remaining `.Should()` calls (FluentAssertions extension method) — would silently compile if any FluentAssertions ref leaked back in.

---

## Acceptance

- All previously-passing tests still pass under TUnit.
- `dotnet test <solution>` reports 0 failures, 0 errors. Skipped count matches prior xUnit runs (env-gated headed tests).
- No `Xunit.*` or `xunit.runner.*` PackageReference remains.
- `tests/*/TEST-STRATEGY.md` updated.

---

## Out of Scope

- Adding new test coverage. This epic is mechanical migration only (framework + assertion library swap).
- Source-generator AOT publish validation (separate epic if/when we publish AOT binaries).
