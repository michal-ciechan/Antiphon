# CARD-1823 S1 Review, round 1 (Final, Full): task edb215b1

Verdict: BLOCKER (1). The proof rule lets a later green prove an identity even when the failing run already contained the same decision fix. That contradicts plan D-5 and drops known-bad chunks from band 0. Everything else is green.

Subject: `origin/feat/card-task-26c1eaa0` tip `bbaea14b3c720377f5fef78e7c1d7e716bd5be57`. The tested code is `7d8e651854185cbc4c8ab004e0a5b085283e4125`, and the tip adds only the Code report. Landing owner: Code task `26c1eaa0-f5ee-447e-b284-34031c630e60`. Master: `4a600e9af54c297d8527f472e47fdbc1e9b6f652`. Fork point: `263b985a8d708169c3312db6d4e207c28be7e1a8`.

## D-1 (blocker): a fix that was already present at the failure still proves on a later green

- Where: `scripts/merge-nightly-seeds.ps1:318-351`. In the proof loop, `Find-MergeDecisionHit` checks only the later seed's `sourceSha`. Nothing checks whether the last-failed seed's `sourceSha` already contained that decision SHA.
- Failure: I reproduced this with the real script, a temp repo `root -> fix -> tip`, and decision `M1 -> fix`.
  - P1 (same candidate re-run): run-a on `tip` fails M1, then run-b on `tip` shows M1 green. Result: `M1=proven/run-b`, and `failedTests=[]`.
  - P2 (green on an older candidate than the failing one): run-a on `tip` fails M1, then run-b on `fix`, observed later, shows M1 green. Result: `M1=proven/run-b`, and `failedTests=[]`.
  - In both cases the failure happened with the fix already in the tree. The fix did not fix it, or the test is flaky. A chance green still removes the identity from band 0.
- Why it matters: plan D-5 says "a fix that was present and still failed stays `open`". The brief marks false proof as the costly error, and the decision file is cumulative, so fixes that did not work stay in it. The realistic path: rc-N contains fix F and still fails, then rc-N+1 goes green by chance. M1 is then tested late.
- Fix:
  - When `-Repo` is set, drop every decision SHA that is already contained (`Get-MergeContainment`) in the last-failed seed's `sourceSha` before it is used for proof or for `fixed-pending-proof`. With no remaining SHA the identity stays `open`; the report could flag it, for example `fixPresentAtFailure`. `--Proven` stays the explicit override.
  - Add a red-first witness, for example `CumulativeMergeFixPresentAtFailure`, covering P1 and P2. It is red on this tip.
  - The plan's checkpoint table is closed, so the new row goes in a separate plan amendment file rather than an edit of the carried plan (add/add risk).

## Checks run at bbaea14b3 (all green)

- Checkpoint run `20261110-002909-9810` (`--after S1`, `validate --expected-source-sha` gives SOURCE VALID rows=2):
  - `CHECKPOINT CP-1 commit=bbaea14b3c720377f5fef78e7c1d7e716bd5be57 build=ok executed=7 passed=7 failed=0 skipped=0 sourceState=clean buildSource=verified`
  - `CHECKPOINT CP-2 commit=bbaea14b3c720377f5fef78e7c1d7e716bd5be57 build=reused executed=2 passed=2 failed=0 skipped=0 sourceState=clean buildSource=verified`
  - CP-3 to CP-7 belong to S2 to S6 and are out of scope.
- Red at the harness commit `d0f147de0` (Code's TRX, run `20261109-235630-0a33`): 7/7 failed. Six failed with `merge-nightly-seeds.ps1 missing`, and LaunchDefaults failed on the ReadAllBytes of the missing file. These are the right reasons.
- Mutants: harness cases run on `git archive` scratch copies, one build-slot lease per run. The unmutated base copy passed 4/4.
  - Union (collect from `$newest` only) killed `CumulativeMergeUnion` (`methods=M2(canceled)`).
  - Proof rule ignored (`if ($green)`) killed `CumulativeMergeUnfixedGreen` (`status=proven`) and `CumulativeMergeCherryPick` (prose proven).
  - Unstable digest (`createdAtUtc=UtcNow`) killed `CumulativeMergeDigestStable` (`same=False`).
- Real launch verifier, `Initialize-NightlyDispatchHistory` on merged seeds with `Get-NightlyHistoryDigest`, `-AcceptCrossCandidateSeed`, the newest `-SeedSourceCandidateId`, `-OwnedDirectory`:
  - It accepted them (`FallbackReason=''`, union failure present, digest equal).
  - A wrong digest gave `history-digest-mismatch` and a null history.
  - An older candidate id gave `seed-source-candidate-mismatch`.
- Other probes:
  - A CRLF newer seed merged and proved correctly.
  - The repo and output paths contained spaces.
  - Input order swapped gave identical seed and report bytes.
  - A truncated seed gave exit 2 and wrote nothing.
  - A shallow clone missing the fix gave exit 2 (`repo-sha-unresolved`), never a proof.
  - An interrupted later chunk gave `fixed-pending-proof`, kept in failedTests.
  - A class absent from the later seed was not proven.
- Static checks:
  - The script is ASCII.
  - It uses none of `??`, `?.`, `&&`, `||`, or the ternary operator.
  - `$PSScriptRoot` is resolved in the body.
  - The test class carries `[Category("Integration")]` and `ParallelLimiter<ProcessSpawnLimit>`.
- Guard batch: TestClassificationPolicy*, TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire*, NativeLinuxPolicyWitness*, MechanicalPcContract*, TaskPlatformGuidance*, C1729LayeringGuard* and TestLaneCategoryGuard*. It used a leased default-output build (`UseAppHost=false`, 0 errors, 176 s) and a leased run: 78 passed, 0 failed, 0 skipped.
- `check-evidence-diff.ps1` over `263b985a8..bbaea14b3`: commits=4, entries=1 (the Code report), violations=0. The brief's base `4a600e9af` is not an ancestor, so that range gave `base_not_ancestor`.
- `git merge-tree --write-tree origin/master HEAD`: exit 0, tree `9a49efc50f0c7d3d622a0310d684bc3742a2d4fc`.
- `audit-land-verification-graph.ps1 -Check`: exit 0, approved.
- `impacted-classes.ps1 -Range 263b985a8..HEAD` printed an empty filter.

## Advisories (cards, not land holds)

- A-1: A method skipped in a completed later chunk (NotExecuted or Skipped counts as completed, and skipped rows are not exported) reads as green. With a contained fix, a skip therefore proves the identity. This is the schema limit D-4 accepts. A later slice should export per-identity skipped rows, or refuse proof on them.
- A-2: The script prints no digest. The operator has to compute `Get-NightlyHistoryDigest` separately. A printed `digest=` line would remove a manual step.
- A-3: No operator doc or rc-qualification SKILL pointer to the merger exists; the operator steps live only in the plan.
- A-4: The script runs native git with `2>$null` under `$ErrorActionPreference='Stop'`. Under Windows PowerShell 5.1, any git stderr becomes a terminating NativeCommandError. Every such path already ends in exit 2, so this cannot cause a false proof, but the native 5.1 proof should cover it.

## Pending (not discharged here)

- Windows-native CP-1 and CP-2 under Windows PowerShell 5.1 and pwsh 7, on a private tree.
- Every PC (the plan's mutant column) stays pending for the method-scoped SourceLanding Mutation. My scratch mutants are review evidence, not PC discharge.
