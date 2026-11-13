# CARD-1871 Review round 1 (Final): boundary identity hardening AD-1..AD-4

Review task `57edd89d`. The subject is Code owner `e0327e21-07f5-432a-beaf-413d156ead9f`,
branch `feat/card-task-e0327e21`, tip `2349a1938b8ff11829ca6becf3c4719383bf9e52`
(13 commits over `aa36b65553049476f0dcd60bfc061635edc4f2d1`). Master is `cf17942491a7af951aac812da3eb18e2340a5ba7`.

**Verdict: BLOCKER (1). The rest of the review is clean.**

## Blocker B-1: CP-4 is red at the reviewed SHA because its 1 s wall-clock assertion fails under load

- **Where:** `scripts/test-rc-boundary.ps1`, case `OrdinalSortAndSingleDigest`, at
  `Note ($clock.Elapsed.TotalSeconds -lt 1) ...`. The C# wrapper
  `RcBoundaryChunkIdTests.OrdinalSortAndSingleDigest` runs the same case, so the assertion also
  runs in the nightly and rc Unit lanes.
- **Failure:** Checkpoint run `20261113-073402-d9f5` on server2 (load average about 30 on 24
  cores; CP-3 and CP-5's build were running at the same time) recorded
  `CHECKPOINT CP-4 ... exit=1` with `BOUNDARY TIMING count=800 order=random seconds=1.041937`
  and `FAIL RB OrdinalSortAndSingleDigest.contract - 800 random identities exceeded 1s`.
  The same assertion failed in a second, independent run. That run was mutant `M10`, which changes
  only the collision grouping in `Resolve-RcBoundaryChunks` and leaves the timed path untouched.
  Three repetitions at the same load passed in 0.29 s, 0.23 s and 0.50 s (random order) and
  0.07 to 0.15 s (reversed). The random sample is timed first and absorbs the first-call cost.
- **Why:** The limit is roughly 2x the measured p95 at ordinary server2 load and has no
  warm-up. The test therefore fails without any code change: 2 of about 24 runs of this path at
  this SHA failed. Under R5 of
  docs/testing-and-build.md a red that passes alone and recurs is not a flake to move past. It
  would also put a red in rc qualification, especially on Windows PowerShell 5.1, which is slower
  and still owes its native run (AD-5). The plan requires every CP row green, so Final
  cannot pass with CP-4 red.
- **Fix:** Keep the regression witness and make it load-tolerant. Make one untimed warm-up call
  before the timed samples, and raise the bound to one the quadratic sort still breaks by a wide
  margin: the restored insertion sort took 15 to 16 s, so a bound of about 5 s still catches it.
  Alternatively, replace the wall clock with a deterministic witness, such as asserting that
  `Compare-RcBoundaryIdentity` or any per-pair PowerShell comparator is never invoked. Do not
  widen any other budget. Commit the change test-first, then rerun CP-4 and CP-5.

## Verified clean

- **CP rows (one checkpoint-tool run, commit `2349a1938`, source clean, buildSource verified,
  `unlisted: none`):** CP-1, CP-2 and CP-3 exited 0. CP-4 exited 1 (B-1). CP-5
  `RcBoundaryChunkIdTests` ran 11 tests: 11 passed. CP-6, the guard union, ran 162 tests: 162
  passed. Per class: TestClassificationPolicyTests 41, TestClassificationGuardTests 1,
  SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3,
  DerivedImpactIndexTests 27, ProcessSpawnLimitTests 57, TestLaneCategoryGuardTests 1,
  MechanicalPcContractTests 11, TaskPlatformGuidanceTests 5, C1729LayeringGuardTests 9.
  CP-7 (Pty) ran 2 tests: 2 passed. CP-8 (SessionRunner) ran 2 tests: 2 passed. Before the run,
  the DerivedImpactIndex driver and the checkpoint tool were built under build slots (the plan
  lists this as the CP-6 precondition).
- **Mutants:** each was a scratch copy of the tip library run against all 11 cases, with one slot
  lease per mutant. The unmutated baseline passed 11/11. Every one of the ten prior Review
  mutants went red:
  - digest truncation (M01): 6 cases red;
  - skipped collision check (M02): ShortIdCollision and DuplicateMemberships;
  - JSON encoding (M03): 5 cases;
  - digest omits caseKey (M04): 7 cases;
  - membership omits caseKey (M05): CaseKeyChangesIdentity, ShortIdCollision and DuplicateMemberships;
  - first rule wins (M06): TwoBoundariesNeverShare;
  - collision keeps its boundary (M07): ShortIdCollision;
  - omitted length prefix (M08): 5 cases;
  - omitted identity count (M09): 5 cases;
  - full-digest collision grouping (M10): ShortIdCollision and DuplicateMemberships.

  The new mutants went red as well:
  - accept lone surrogates (N1): LoneSurrogatesFailClosed;
  - drop the duplicate-membership check (N2a): DuplicateMemberships ("overlapping memberships
    accepted", "one identity assigned to two boundaries");
  - collision id truncated to 16 hex (N2b): DuplicateMemberships;
  - truthiness-based CaseKeys check (N3): ExplicitCaseKeyCount (all three bypasses);
  - restored insertion sort (N4a): OrdinalSortAndSingleDigest ("exceeded 1s" on both samples);
  - digest computed twice (N4b): "digest calls for two chunks: 4";
  - invariant-culture sort (N4c): "sort is not ordinal by class, method, case".

  One mutant survived, as expected: N2c, which drops the final duplicate-chunk-id check. That check
  is unreachable defence in depth, because membership refusal and full-digest collision ids
  already prevent duplicate ids.
- **(1) Sort order:** the NUL-escaped key (field NUL becomes 0,1 and the separator is 0,0)
  preserves ordinal order and is injective, so it gives the same total order as the old ordinal
  tuple compare. Keys are unique once duplicates are refused, so sort stability does not matter.
  Both versions compare ordinally; neither is culture-sensitive.
  I compared old `aa36b6555` with new `2349a1938` on 900 random chunks (3 seeds, 1 to 39 members).
  The inputs included NUL, control characters, accented letters, U+0130, U+FFFD, surrogate pairs,
  fullwidth letters, shared prefixes and `/` inside method names, and covered
  core/git/unassigned with and without CaseKeys. chunkId, fullDigest, identityKeys order and
  caseKeys order were byte-identical. The pinned ids `b-core-eca0b76b8b1ed195`,
  `b-core-f2e0eae2ff0cf3c2` and `b-core-360a4ef28b10a96c` (the collision fixture) are unchanged.
  One output changes on purpose: a rebuilt collision chunk's id is now `b-unassigned-<64 hex>`
  where it was 16 hex (AD-2). A non-colliding unassigned chunk keeps its 16-hex id.
- **(2) Surrogates:** a lone surrogate throws a terminating `MethodInvocationException` from
  `GetBytes` ("Unable to translate Unicode character \uD800 ..."). The whole `Resolve`/`Get`
  call refuses; there is no partial result and no unassigned chunk. No caller exists yet, so
  nothing handles the throw today (advisory A-1).
- **(3) Duplicates:** one identity in two memberships is refused
  (`boundary-chunk: duplicate membership across chunks`) on the same boundary, across boundaries,
  and when one membership's caseKey is null and the other's is empty. Two identical memberships
  (the same full digest) are refused. The same identity in different suites is accepted.
  `Get-RcBoundaryMembershipKey` is injective (`A/M`+`xy` differs from `A/Mx`+`y`).
- **(4) CaseKeys:** `@('')` with 2 or 3 identity keys is refused with `boundary-chunk: case key
  count`. Passing `@()` is refused, and so is `$null` with 2 keys. With 1 key, `$null` is
  refused through a null-index error (advisory A-2). Omitting CaseKeys keeps the default.
- **(5) Cherry-pick path:** `git cherry origin/master 2349a1938` shows 13 `-` commits (5 S2 and
  8 hardening) and 13 `+` commits. Picking the 13 `+` commits onto a scratch tree of
  origin/master gave **0 conflicts**, and the four task files are identical to the tip.
  A whole-branch `git merge-tree` conflicts as expected: add/add in the three boundary files and
  content in `tests/derived-impact-index.json`.
- **(6) Inert, ASCII, Full unchanged:** only the library, the harness, the C# wrapper and the
  plan changed. Nothing outside the test harness references `rc-boundary.ps1`. No launch,
  selection, Full, migration or OS-branch code changed. Both `.ps1` files are ASCII-only. The
  native Windows PowerShell 5.1 run is still owed (AD-5, orchestrator).
- **Evidence diff:** `check-evidence-diff.ps1` over `aa36b6555..2349a1938` found 13 commits and 0
  violations. The cherry-picked tree against master also found 13 commits and 0 violations.

## Advisories (cards; they do not hold the land)

- **A-1:** surrogate refusal and the throwing encoder surface .NET errors without the
  `boundary-chunk:` prefix. S4a should wrap them in a prefixed refusal, and decide whether a
  malformed identity aborts the plan or becomes an unassigned, dirty chunk.
- **A-2:** `-CaseKeys $null` with a single identity is refused only through "Cannot index into a
  null array". An explicit null check would make that refusal intentional.
- **A-3:** collision ids now run to 77 characters (`b-unassigned-` plus 64 hex). Any S4a
  consumer that pattern-matches chunk ids must accept both lengths.
- PCs remain pending for SourceLanding Mutation. The operator waiver of 2026-11-12 means this
  review does not claim them as passed.
