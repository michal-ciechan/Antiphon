# CARD-1823 S1 Review, round 2 (Final, diff since bbaea14b3): task d3c686cb

Verdict: CLEAN. Round-1 blocker D-1 is fixed. No regressions. One new advisory (A-5) is for a card and does not hold the land.

- Subject: `origin/feat/card-task-36fa6162`, tip `a50d56002453d4cb618f9c05db6dbc0275245471`.
- Red commit: `37457ae3f`.
- Code task: `36fa6162-bdc7-49e6-9b71-f99232570b5d`, which repairs the original S1 Code task `26c1eaa0-f5ee-447e-b284-34031c630e60`.
- Master: `da4f168de5a771adf5dab22336826d19b4ec613b`. Fork point: `263b985a8`.
- Reviewed diff: `bbaea14b3..a50d56002`. It touches `scripts/merge-nightly-seeds.ps1` (proof filter, `fixPresentAtFailure`, `digest=` line and skipped-test sentence), two harness cases plus their `[Test]` rows, and the amendment plan `2026-11-09-card-1823-amendment-1.md`.

## D-1 re-probe (my round-1 probe on the real script at a50d56002)

The temp repo is `root -> fix -> tip -> fix2`, plus a `side` branch from root. `side` carries a commit with the trailer `(cherry picked from commit <fix>)`. The decision is `M1 -> fix`.

| Case | Result |
|---|---|
| (a) fail on tip, green on tip | `open`, failedTests=[M1], fixPresentAtFailure=true |
| (b) fail on tip, later green on the older `fix` commit | `open`, failedTests=[M1], fixPresentAtFailure=true |
| (c) control: fail on root, green on tip (fix new) | `proven`/run-b, containing=fix |
| (d) decision `fix,fix2`, fail on tip, green on fix2 | `proven`, containing=fix2, fixPresentAtFailure=true |
| (d2) same decision, green on tip (fix2 absent) | `open` |
| (e) fail on root, green on unrelated `side` with cherry-pick trailer | `proven`, containing=the pick commit. Acceptable: the remaining SHA is contained by trailer. |
| (e2) fail on the `side` pick, green on tip | `open`, fixPresentAtFailure=true (present by trailer) |
| (e3) fail on tip, later chunk incomplete on tip | `open`; no longer `fixed-pending-proof` |
| (e4) no `-Repo` | `open`; git proof off |
| (e5) `--Proven` on the same-tip green | `proven` (override) |
| (f) shallow clone, missing SHA, non-repo path | exit 2 `repo-sha-unresolved`; never a proof |
| malformed (truncated) seed | exit 2; neither output file written |
| three seeds, input order permuted | identical seed bytes and identical report bytes |

The printed `digest=` was checked on cases c, a and g with the real launch verifier (`Initialize-NightlyDispatchHistory -ExpectedDigest <printed> -AcceptCrossCandidateSeed -SeedSourceCandidateId <newest> -OwnedDirectory`):

- The printed digest equals `Get-NightlyHistoryDigest` and the file SHA-256.
- The verifier accepted it: `FallbackReason=''`, digest equal, and the expected failedTests.
- A wrong digest gave `history-digest-mismatch` and a null history.

Static checks:

- The changed files are ASCII.
- The script uses none of `??`, `?.`, `&&`, `||`, `UtcNow` or `Get-Date`.
- The amendment plan imports alone with 0 warnings: `imported 1 rows`.

## Checkpoint runs (checkpoint tool, `--after S1`, at a50d56002)

Run `20261110-015306-a085`, from the `.antiphon/` extract of parent CP-1/CP-2, amendment CP-8 and the guard batch. `validate --expected-source-sha` gives SOURCE VALID rows=4.

- CP-1: build=ok, executed=7 passed=7 failed=0 skipped=0, sourceState=clean, buildSource=verified.
- CP-2: build=reused, executed=2 passed=2 failed=0 skipped=0.
- CP-8: build=reused, executed=2 passed=2 failed=0 skipped=0. The extract reuses the CP-1 build because the tool warned that the separate `bin-c1823-d1` build was a duplicate. Same commit, same project.
- CP-G guards: executed=90 passed=90 failed=0 skipped=0. Filter: TestClassificationPolicy*, TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire*, NativeLinuxPolicyWitness*, MechanicalPcContract*, TaskPlatformGuidance*, C1729LayeringGuard*, TestLaneCategoryGuard*.

Run `20261110-020508-9e60`, the whole touched class:

- CP-C `/*/*/RcStoppedRunSeedTests/*`: executed=31 passed=31 failed=0 skipped=0, wall 455 s.

## Red/green and mutants (harness cases on `git archive` scratch copies)

- Red commit 37457ae3f fails both new cases for the right reasons:
  - FixPresentAtFailure: `same=proven`, `older=proven`, `held=fixed-pending-proof`, `picked=proven`.
  - PrintsDigest: `printed=False noted=False`.
- The unmutated a50d56002 copy passes both cases.
- PC-D1 (use every decision SHA; `$decisionList` in both Find calls) kills FixPresentAtFailure.
- PC-D1b (keep the filter but drop `continue`) kills FixPresentAtFailure.
- PC-A2 (omit the `digest=` line) kills PrintsDigest with `printed=False`.
- Each mutant leaves the other case green.

These runs are review evidence. The PCs stay pending for the method-scoped SourceLanding Mutation.

## Gates

- `check-evidence-diff.ps1` over `263b985a8..a50d56002`: commits=6, entries=1, violations=0.
- `check-evidence-diff.ps1` over `bbaea14b3..a50d56002`: commits=2, entries=0, violations=0.
- `git merge-tree --write-tree origin/master(da4f168de) HEAD`: exit 0, tree `98aee315a6ecddf32341e5e29df065aaf367e69b`.
- `audit-land-verification-graph.ps1 -Check`: exit 0, `approved`.
- Migration: none. No EF or schema change, so the downgrade selector does not apply.
- Input waits: no wait or parking behaviour changed.

## Advisories (cards, not land holds)

- A-5: the filter checks only the last failing source, as the commissioned rule specifies. A fix that was present at an earlier failure still proves.
  - Probe g:
    1. run-a on tip, which contains the fix, fails M1.
    2. Later, run-b on root, an older candidate without the fix, fails M1.
    3. run-c on tip is green.
  - Result: `proven`/run-c, fixPresentAtFailure=false.
  - This is the same class as D-1, but it needs an older candidate to fail after a newer one. The cost is ordering only: the identity leaves band 0, and the test still runs.
  - Suggested rule: ignore a decision SHA contained in ANY failing source of the identity.
- A-6: containment is one-directional. A decision that names an rc cherry-pick SHA is not seen as present in a failing master source that holds the original commit, because the trailer sits on the pick. In that case a later green on the rc proves.
  - Operator guidance: list the original SHA.
- Round-1 A-3 (no operator doc pointer) and A-4 (5.1 native stderr) stand. A-1 and A-2 are taken in this repair.

## Pending (not discharged here)

- The Windows-native CP-1, CP-2 and CP-8 rows under Windows PowerShell 5.1 and pwsh 7.
- PC-D1, PC-A2 and the parent plan's mutant column go to SourceLanding Mutation.
