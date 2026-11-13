# CARD-1871 Review round 2 (diff only): B-1 fix for the CP-4 sort-cost witness

Review task `36bca1ea`. The subject is branch `feat/card-task-5463c5eb` at tip
`39b59824100b95992ccbc3b7b47a9c56815911ed`, authored by Grok Code. This round reviews only
`2349a1938b8ff11829ca6becf3c4719383bf9e52..39b59824100b95992ccbc3b7b47a9c56815911ed`, one commit.
Round 1 is `docs/reviews/2026-11-13-card-1871-round-1-review.md` on `feat/card-task-57edd89d`.
Master is `fac5cc2de89cc82eb39e85cd2bf6c4df18d55ac6`.

**Verdict: CLEAN. B-1 is fixed, and there are no new blockers or regressions.**

## Scope of the diff

The commit changes only two files: the harness case `OrdinalSortAndSingleDigest` in
`scripts/test-rc-boundary.ps1`, and the plan text (V-1871-4, the CP-4 Expect column, and the
performance budget). It does not touch the library `scripts/lib/rc-boundary.ps1`, the prefixes
JSON, or `RcBoundaryChunkIdTests.cs`. Their blob hashes are the same at both SHAs:

- library: `77402503d...`
- prefixes JSON: `b1157e172...`
- C# wrapper: `d5e94e58b...`

## (1) The structural witness goes red on the old sort

Each mutant was a `git archive` scratch copy of the tip with the library's
`Sort-RcBoundaryIdentities` replaced. Each ran case `OrdinalSortAndSingleDigest` once, under its
own build-slot lease, at load average 28 to 32.

| Mutant | Change | Result |
|---|---|---|
| m0 | tip, unchanged (control) | PASS; 0.14 s random, 0.20 s reversed |
| m1 | the old insertion sort with `Compare-RcBoundaryIdentity`, from `0653108a9` | **RED**: `loops=1 compares=1 sorts=0`; the warm-up invoked the comparator 148354 times; both samples exceeded 5 s (11.6 s, 25.0 s); the comparator ran 616308 times |
| m5 | quadratic `for`-loop insertion sort through a renamed comparator function, plus a decoy `[Array]::Sort` on a dummy array | structural check passes; **RED** on the 5 s bound (14.1 s, 20.5 s) |
| m2 | inline quadratic `for`-loop insertion sort (no comparator function), plus a decoy static Sort | PASS; 0.20 s, 0.33 s |
| m3 | `[Array]::Sort($items, [Comparison[object]]{...})` with a PowerShell scriptblock as the per-pair comparator | PASS; 0.34 s, 0.19 s |

Any sort as slow as the one AD-4 targeted is caught. That means a per-pair PowerShell function
call inside a quadratic loop: m1 is caught by the structural check, the runtime counter and the
bound, and m5 by the bound. The 5 s bound is therefore not the only guard: m1 also fails both the
structural AST check and the runtime counter. m2 and m3 get through, but neither is equally slow
at 800 members (advisories AR2-1 and AR2-2).

## (2) The 5 s bound under load

The tip case ran three times in a row, each under its own lease:

| Load average | Random | Reversed | Result |
|---|---|---|---|
| 32.6 | 0.32 s | 0.20 s | PASS |
| 36.4 | 0.23 s | 0.17 s | PASS |
| 39.9 | 0.41 s | 0.26 s | PASS |

In addition, the checkpoint row CP-4 recorded 0.075 s and 0.197 s, and m0 recorded 0.14 s and
0.20 s. The worst sample, 0.41 s, leaves more than 12x headroom below 5 s. The untimed warm-up now
absorbs the first-call cost, which caused the round-1 1.04 s failure.

## (3) Chunk ids are unchanged

The library blob is byte-identical between `2349a1938` and `39b598241` (`77402503d`). Its
inputs, the prefixes JSON and `layers.txt`, are unchanged. Every chunk id, full digest and
membership order is therefore identical by construction. A 900-chunk rerun would compare the
library with itself and could not go red, so I did not repeat it. The 900-chunk old-versus-new
proof from round 1 (`aa36b6555` against `2349a1938`) still holds for this tip.

## (4) Checkpoint run

Run `20261113-084406-9e91` used the tool's `start`/`wait` at commit `39b598241`, on
`feat/card-task-36bca1ea`, which is fast-forward equal to the subject tip.

- Source was clean and `buildSource=verified`. `validate --expected-source-sha 39b59824...`
  printed `CHECKPOINT SOURCE VALID rows=8`.
- The report lists `unlisted: none`. The run made 3 builds, at most one at a time.
- Wall time was 24m33s.

| Row | Result |
|---|---|
| CP-1 | exit 0 |
| CP-2 | exit 0 |
| CP-3 | exit 0 |
| CP-4 | exit 0 (0.075 s, 0.197 s) |
| CP-5 | `RcBoundaryChunkIdTests`: 11 executed, 11 passed |
| CP-6 | guard union: 162 executed, 162 passed, 0 skipped |
| CP-7 | Pty `ProcessSpawnLimitTests`: 2 executed, 2 passed |
| CP-8 | SessionRunner `ProcessSpawnLimitTests`: 2 executed, 2 passed |

CP-6 counts per class, from the TRX:

| Class | Tests |
|---|---|
| TestClassificationPolicyTests | 41 |
| TestClassificationGuardTests | 1 |
| SlowTestRegistryMergeTests | 5 |
| SlowTestTripwireTests | 2 |
| NativeLinuxPolicyWitnessTests | 3 |
| DerivedImpactIndexTests | 27 (559 s, SLOW CLASS) |
| ProcessSpawnLimitTests | 57 |
| TestLaneCategoryGuardTests | 1 |
| MechanicalPcContractTests | 11 |
| TaskPlatformGuidanceTests | 5 |
| C1729LayeringGuardTests | 9 |

Before the run, the DerivedImpactIndex driver and the checkpoint tool were built under their own
build-slot leases. This is the CP-6 precondition (AD-6) and is accounted for as a bootstrap build.

## Other checks

- **ASCII:** the harness, the library and the plan are ASCII-only.
- **Evidence diff:** `check-evidence-diff.ps1 -BaseRef 2349a1938 -HeadRef 39b598241` found
  1 commit and 0 violations. Over `aa36b6555..39b598241` it found 14 commits and 0 violations.
- **Merge-tree against `fac5cc2de`:** exactly the four expected conflicts:
  - add/add on `scripts/lib/rc-boundary.ps1`, `scripts/test-rc-boundary.ps1` and
    `tests/Antiphon.Tests/Scripts/RcBoundaryChunkIdTests.cs`;
  - content on `tests/derived-impact-index.json`.
- **Cherry-pick path:** `git cherry origin/master 39b598241` gives 13 `-` and 14 `+`. Picking the
  14 `+` commits onto a detached scratch tree of `fac5cc2de` gave 0 conflicts. The first attempt
  stopped without conflict markers or changes and succeeded on retry. The four task files are
  byte-identical to the tip.
- **S2 harness on master plus the picks:** all 11 cases passed, 0 failed. The 11 cases ran in one
  loop under one lease; they are pwsh fixture runs, not builds.

## Advisories (cards; they do not hold the land)

- **AR2-1:** V-1871-4 says the sort "invokes no per-pair PowerShell comparator". The structural
  check still accepts a scriptblock `[Comparison[object]]` delegate passed to the static Sort
  (m3, 0.34 s). Its cost is O(n log n) and inside the bound, so AD-4's cost goal holds, but the
  witness is weaker than the plan sentence. Tighten it by requiring the Sort's comparer argument
  to be `[StringComparer]::Ordinal`, or by rejecting any `ScriptBlockExpressionAst` inside
  `Sort-RcBoundaryIdentities`.
- **AR2-2:** the structural check matches only `WhileStatementAst` and the literal command name
  `Compare-RcBoundaryIdentity`. It can be fooled in two ways:
  - an inline quadratic `for` loop plus a decoy static Sort passes (m2, 0.33 s at 800; this
    would grow to tens of seconds at about 8000 members);
  - a renamed comparator plus a decoy Sort is caught only by the wall clock (m5).

  A stronger check would allow only the one key-building `for` loop and reject nested loops and
  calls to library functions.
- **AR2-3:** the runtime comparator counter is installed only when `Compare-RcBoundaryIdentity`
  exists. It does not exist at the tip, so the structural check and the bound are the live guards.
  The counter becomes live only if that function is reintroduced, which is the m1 case.
- These advisories stand: round-1 A-1..A-3 and AD-5, the native Windows PowerShell 5.1 run still
  owed. PCs remain pending for SourceLanding Mutation; the operator waived mutation checks on
  2026-11-12.
