# CARD-1871 boundary identity advisory repairs

Code/landing owner `e0327e21`; full task base
`aa36b65553049476f0dcd60bfc061635edc4f2d1`. Final verification.
Implements AD-1..AD-4 from Review `20d47839`, reviewed source `aa36b6555`.
The parent scope is [S2 verification](2026-11-13-card-1836-s2-verification.md).
No launch integration, packing, persistence, migration or Full-selection change.

## Verification design

Each AD has a committed test-first witness, its narrow CP row red, then a
committed repair and the same row green. CP-1..CP-4 are script-level witnesses;
CP-2 of the parent is retained as CP-5 here, executing every boundary method
through TUnit and fresh TRX. No self-comparison or constant guard suffices.

- V-1871-1 / AD-1: high and low lone surrogates in class, method and case key
  refuse through both public construction paths; valid supplementary Unicode
  and U+FFFD remain accepted and distinct.
- V-1871-2 / AD-2: colliding already-unassigned chunks retain distinct full
  digest keys. Duplicate suite/identity/case across memberships refuses, even
  across boundaries; distinct suites and cases remain accepted. Duplicate
  memberships no longer return two clean chunks. Collision outputs use the
  full digest in their id so a short-id dictionary cannot erase cases.
- V-1871-3 / AD-3: explicitly bound empty, null and single-empty CaseKeys
  arrays must match membership count; omitted keys retain their default.
- V-1871-4 / AD-4: 800 deterministic shuffled and reversed identities sort
  ordinally (class, method, case), retain pinned ids and compute one digest per
  constructed chunk. Sort-RcBoundaryIdentities uses one static ordinal Array.Sort
  and invokes no per-pair PowerShell comparator. One untimed warm-up precedes the
  timed samples. Each timed sample stays under 5 seconds. The 1 second bound was
  B-1: it failed at 1.04 seconds under ordinary load, while the insertion sort
  it guards against took 15-16 seconds.
- R-1871-1: all original seven cases, parent V-5..V-8, unchanged.
- R-1871-2: guards, policy/classification, layering and impacted-class union
  over the full task diff. Native process roster rows remain parent obligations.
- R-1871-3: evidence-diff, source/provenance validation, OS declaration,
  merge-tree and manual inertness/ASCII checks.

CP-6 precondition (AD-6): build the out-of-solution
`scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj`
under a build slot with `-p:UseAppHost=false` before the guard row. This is an
explicitly accounted bootstrap build, as is building the checkpoint tool.
If the index test is red, regenerate and commit only the derived index in a
separate slice, then rerun the red row. Resolve impacted classes again at HEAD.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | AD1 | n/a | surrogates | `pwsh -NoProfile -File scripts/test-rc-boundary.ps1 -Case LoneSurrogatesFailClosed` | V-1871-1 | one contract, exit 0 after repair | n/a | 1 |
| CP-2 | AD2 | n/a | duplicate-collision | `pwsh -NoProfile -File scripts/test-rc-boundary.ps1 -Case DuplicateMembershipsAndUnassignedCollisions` | V-1871-2 | one contract, exit 0 after repair | n/a | 1 |
| CP-3 | AD3 | n/a | case-count | `pwsh -NoProfile -File scripts/test-rc-boundary.ps1 -Case ExplicitCaseKeyCount` | V-1871-3 | one contract, exit 0 after repair | n/a | 1 |
| CP-4 | AD4 | n/a | sort-cost | `pwsh -NoProfile -File scripts/test-rc-boundary.ps1 -Case OrdinalSortAndSingleDigest` | V-1871-4 | one contract, no per-pair comparator, both timings <5s, exit 0 after repair | n/a | 1 |
| CP-5 | Final | `tests/Antiphon.Tests -> bin-c1871-boundary/` | boundary | `/*/*/RcBoundaryChunkIdTests/*` | V-1871-1..4 R-1871-1 parent V-5..V-8 | every method, 0 failed | 11 | 4 |
| CP-6 | Final | CP-5 | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(ProcessSpawnLimitTests*)\|(TestLaneCategoryGuardTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/*` | R-1871-2 | each class nonzero, 0 failed | 11 | 12 |
| CP-7 | Final | `tests/Antiphon.Agents.Pty.Tests -> bin-c1871-pty/` | pty-roster | `/*/*/ProcessSpawnLimitTests/*` | R-1871-2 parent CP-12 | nonzero, 0 failed | 1 | 2 |
| CP-8 | Final | `tests/Antiphon.SessionRunner.Tests -> bin-c1871-runner/` | runner-roster | `/*/*/ProcessSpawnLimitTests/*` | R-1871-2 parent CP-13 | nonzero, 0 failed | 1 | 2 |

## Performance budget

One process per harness method; no DB, host, provider, browser or migration.
The existing harness 90s execution/10s cleanup and 120s method budgets remain
unchanged, provisional under CARD-1836. CP-4 times only library calls, excluding
host startup; one untimed warm-up precedes the two timed samples, and the safety
bound is 5 seconds. Harness budgets are unchanged. Deterministic shuffled and
reversed 800-member samples are separately reported. Retain per-method durations
in TRX, build/execution/slot times and
membership in checkpoint receipts; uninstrumented nested spans are unknown.
One cold Antiphon.Tests build is shared by CP-5/6. Parent measured guard cost
was 662s, largely DerivedImpactIndexTests; 12 minutes is a one-sample estimate,
not p95. No row/selection is expected over 30 minutes. No timeout is widened.
No repetitions are required after green; red/green and final are separate
source selections. The Unit lane remains nightly/rc-owned.

## Pending controls and native acceptance

PC-V-5..V-8 and the parent's hardening controls remain pending for caller-
commissioned SourceLanding Mutation. PC-1871-1 (replacement UTF8 fallback),
PC-1871-2a (drop duplicate check), PC-1871-2b (truncate collision output key),
PC-1871-3 (truthiness count check), PC-1871-4a (insertion sort), and
PC-1871-4b (duplicate digest) are pending there too. Mutation owns deliberate
mutants and missing-control discovery, including the ten Review variants.
The brief's historical operator mutation waiver does not claim them passed.
AD-5 native Windows PowerShell 5.1 reserved-character/library parsing and the
parent Windows startup probe/rehearsal remain owed by the orchestrator.
No OS-selected production behavior changes; restart target: none.
Parent V-1..V-4 are historical, V-9..V-28 and R-1/R-2 deferred to S3..S10.
