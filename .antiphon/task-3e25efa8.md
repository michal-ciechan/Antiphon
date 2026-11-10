# Merge 3e25efa8: cut rc/2026-11-15

Outcome: rc/2026-11-15 is cut at master pin ac6d00022e940ee3b90584c7d313567b4491c498.
The candidate id is rc-2026-11-15-ac6d00022e94. This task landed nothing. No tag,
publish, promote, or host activation was done. The desktop owns the
release-candidate.ps1 journal and CheckoutRoot; that script was not run here.

The report commit that contains this file is only on feat/card-task-3e25efa8.
refs/heads/rc/2026-11-15 and refs/heads/master stay at the pin.

## Pin

ac6d00022e940ee3b90584c7d313567b4491c498

HEAD, origin/master, and the new rc ref were this SHA immediately before the push
and again on the recheck before this report. 162 commits since the previous cut
7bee43614495293afbae1b0f4add019d11dacb9c.

## Prechecks

A1. `git ls-remote origin refs/heads/rc/2026-11-15` was empty immediately before
the push. The push created the ref. Recheck before this report:

```
ac6d00022e940ee3b90584c7d313567b4491c498 refs/heads/feat/card-task-3e25efa8
ac6d00022e940ee3b90584c7d313567b4491c498 refs/heads/master
ac6d00022e940ee3b90584c7d313567b4491c498 refs/heads/rc/2026-11-15
```

The feat SHA in that block is the pin. The caller progress line is the feat ref
after this report commit.

A2. `git cherry <pin> origin/<rc>` had no '+' line. A '+' absent from master would
have stopped the cut.

| Ref | SHA | Cherry |
|---|---|---|
| rc/2026-11-01 | 10a826b9d61f2c2cdfa29a43cac59fe360c99ea9 | 24 lines, all '-', plus=0. The line is not an ancestor of the pin. Every patch is already on the pin by patch-id. |
| rc/2026-11-03 | 919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd | ancestor, 0 lines |
| rc/2026-11-04 | f32aed42e58a4f7aed9b012d39c4bf4ae90a24a0 | ancestor, 0 lines |
| rc/2026-11-05 | 1470340559889c2985ea722173181ea25c51b47c | ancestor, 0 lines |
| rc/2026-11-06 | 5a95ae7a5611eedb56c3bea023bb7a4b4fb6683a | ancestor, 0 lines |
| rc/2026-11-07 | 1038d07e66a54b630a1bc1d394923bc7049b73dd | ancestor, 0 lines |
| rc/2026-11-08 | d164e93ae47a79ed175beeb9367212f7a7d5d1d5 | ancestor, 0 lines |
| rc/2026-11-09 | 0e7aef0379a073c206e8398dcaed539bc33eaf9b | ancestor, 0 lines |
| rc/2026-11-10 | 74a8004f405bc54666fea33889839f0ff4d62ec7 | ancestor, 0 lines |
| rc/2026-11-11 | 3364197bf1cb38081fa27dd3bfc1e4f3e55ccd4e | ancestor, 0 lines |
| rc/2026-11-12 | 5fef2c23b89414b8818aaf40db9ad738bce9524c | ancestor, 0 lines |
| rc/2026-11-13 | 653f1145de5b72c2617a018cd606e0e8ba3cfe6e | ancestor, 0 lines |
| rc/2026-11-14 | 7bee43614495293afbae1b0f4add019d11dacb9c | ancestor, 0 lines |

No remote ref rc/2026-11-02.

A3. Recomputed at the pin, and again immediately before the push. Valid only for
ac6d00022e940ee3b90584c7d313567b4491c498.

- policyHash stored = computed = fdc043758617a5e8c21735126539c559faf9dad95abe5f1122a6d5780ee9df4d
- Read-NightlyExecutionPolicy accepted that hash, schema 4.
- PinnedInputDigest stored (scripts/test-live-lane-run.ps1 line 25) = printed = 4bc09f3840a39436be6ca4035ea0946c01f96ae1883d0a7b4e5513132dbf74ec

A4. `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` at the pin.
The wrapper rebuilt the missing tool (12 s), then approved phase=pass2.
digest=3ED4A38CAA52947D313A1BAD14BA15203DD7F261A219BBC20704CB13E329EB46 count=75.
Exit 0, wall 29 s. Canary path
`/work/worktrees/antiphon-landgraph-canary-a58793ccb1ef4fec978c6fb19ad25039`
was absent after the run. `-Update` was not used.

A5. `scripts/test-rc-platform.ps1`

- `-Case LinuxOnlyWindowsExclusionPolicy`: 6 passed, 0 failed. Lease d22b1c06, held 7 s.
- `-Case C1686_LinuxOnlyCensus`: 62 passed, 0 failed. Lease a81aff67, held 24 s.

A6.

- `scripts/test-rc-coverage-admission.ps1`: 8 passed, 0 failed. Lease 1a2860dd, held 4 s. Cases: separate-method-workflows, class-scope-rejected-by-assertion, source-roster, exact-windows-dispositions, linux-retains-native, portable-siblings, coverage-sweep, required-skips-stay-incomplete.
- `scripts/test-expected-platform-skips.ps1 -Case Verdict`: 14 passed, 0 failed. Lease 1a98d37f, held 3 s.
- `scripts/test-nightly-core.ps1 -Case CoreMatchesFallback`: 18 passed, 0 failed. Lease 164a1017, held 26 s.

Name check. `Test-ReleaseGateCandidateRef -Ref rc/2026-11-15 -Sha ac6d00022e940ee3b90584c7d313567b4491c498` after the push: Ok=True, Kind=long-lived, CandidateId=rc-2026-11-15-ac6d00022e94, Ref=rc/2026-11-15, CutUtc=2026-11-15. release-cut.ps1, release-candidate.ps1, and the nightly rc profile accept `^rc/YYYY-MM-DD$` and a real date. An impossible month refuses with reason ref-date (the witness is rc/2026-13-01). Nothing refuses rc/2026-11-15 for being a later calendar date. release-candidate.ps1 journals an existing remote tip and refuses a linked worktree; it was not executed. The desktop run-start task owns CheckoutRoot `C:\Antiphon\releases\checkout-<id8>`.

## Cut

```
git push origin ac6d00022e940ee3b90584c7d313567b4491c498:refs/heads/rc/2026-11-15
```

Result: `* [new branch] ac6d00022e940ee3b90584c7d313567b4491c498 -> rc/2026-11-15`.
Plain push. The rc ref was not pushed again.

## Tree verification

One whole-solution build, one lease, native output, UseAppHost=false, TEMP=/tmp:

`pwsh -NoProfile -File scripts/build-slot.ps1 -Label sln-ac6d0002 -- dotnet build Antiphon.sln -p:UseAppHost=false`

- Lease e0d9c208-a4be-4363-b0fc-19dcd29d9590, waited 0 s, maxcpucount=6, held 238 s.
- Build succeeded. 0 Error(s). 2207 Warning(s). Time Elapsed 00:03:58.00.
- No NETSDK1047, so -m:1 was not used.

Smoke rows are `--no-build` with the same UseAppHost=false, one lease each. Every named group passed. Two earlier combined rc rows exited 124 because the agent cap (720 s, then 600 s) killed them before the serial ProcessSpawnLimit plus the 19-minute failed-first tests could finish. Those caps are not product failures. The split reruns below are the evidence.

| Row | Result | Wall | Lease held |
|---|---|---|---|
| b1-guards (NativeLinuxPolicyWitness, TestClassificationPolicy, TestClassificationGuard, SlowTestRegistryMerge, SlowTestTripwire, MechanicalPcContract, TaskPlatformGuidance, TestLaneCategoryGuard, C1729LayeringGuard) | 78/0/0 | 99 s | 97 s, 463b92cc |
| b2-roster (ExpectedPlatformSkipRoster, NightlyCore, LiveLaneRunner) | 6/0/0 | 58 s | 57 s, 1fe8a0a8 |
| b3-spawn (ProcessSpawnLimit, Antiphon.Tests copy) | 57/0/0 | 14 s | 13 s, 9ac078cf |
| b4-checkpoints (CheckpointColdBuild, CheckpointTerminalReceipt) | 13/0/0 | 196 s | 194 s, d8580f02 |
| b4-watcher (RcQualificationWatcherScript) | 2/0/0 | 153 s | 136 s, 7ab0e9e4 (waited 15 s) |
| b4-ff-exec (RcFailedFirstExecution) | 5/0/0 | 730 s | 669 s, 4c288c27 (waited 60 s) |
| b4-ff-plan (RcFailedFirstPlanner) | 2/0/0 | 559 s | 558 s, 39355a47 |
| b4-seed (RcStoppedRunSeed, cumulative seed merge) | 31/0/0 | 593 s | 592 s, 39d04d59 |
| b5-mig-a | 33/0/0 | 114 s | 112 s, b0c37a7a |
| b5-mig-b | 32/0/0 | 348 s | 347 s, f029bb97 |
| b5-mig-c (includes AppDbContextModelTests) | 26/0/0 | 114 s | 113 s, 5f6c61bb |
| b6-lease-git (DispatcherLeaseSpan, TaskProgressGit, AgentTaskAutoTitle, RepositoryMaintenance) | 41/0/0 | 50 s | 49 s, f5f343dd |
| b7-reports-land (SiblingToolReportBoundary, TaskReportHousekeeping, LandGraphLandRefresh) | 52/0/0 | 93 s | 92 s, 84ef3603 |
| b8-runner (SessionRunner.Tests C1818GcOwnerRobustness, C1817RunnerRepositoryMaintenance) | 24/0/0 | 17 s | 15 s, cca7c013 |
| b9-e2e (ContractSnapshotTests) | 5/0/0 | 105 s | 104 s, bcd3a6c2 |
| b10-hang (`test-nightly-tests.ps1 -Case Group4CompletedHang`) | 25/0, harness 0 | 7 s | 6 s, 55b11a73 |
| b10-trx (`-Case Group4NativeTrxReader`) | 27/0, harness 0 | 9 s | 8 s, 2ef7cbdc |

C1729LayeringGuardTests passed inside b1-guards. A passing `Repository_graph_satisfies_the_committed_allowlist` asserts an empty unlisted set, an empty stale set, no ambiguous violations, and Passes. That is VIOLATIONS 0 and STALE 0. The inventory line's LISTED and AMBIGUOUS totals were not captured; the green row was not rerun to print them.

The whole `test-nightly-tests.ps1` script was not run. ProcessSpawnLimitTests in other assemblies were not run.

## Changes since rc/2026-11-14 (7bee43614..ac6d00022)

162 commits. Classification by paths: PRODUCT 50 (any path under server/, src/, client/, or Antiphon.AppHost/), tests only 56, scripts/tools only 13, docs only 14, mixed non-product 29.

Migrations hosts will apply at startup since rc/2026-11-14: none. `git diff --name-only 7bee43614..pin -- server/Migrations` is empty, and server/Infrastructure/Data is empty on the same range. Confirmed again immediately before the push.

The four operator commits that arrived on master after the rc/2026-11-14 cut are ancestors of this pin: 25b347f49 perf(land-tests), d15459456 and 364e27133 perf(live-lane), 99e2ffa35 land-graph re-record.

PRODUCT-touching commits (newest first). Several red-witness commits also edit a product path and are included in the 50.

- 36b1ae6ae, edc100dda: CARD-1459 trimmed desktop HEAD compare; null push exit, failed refusal, exhausted debt.
- 0b73eb850: CARD-1476 failed baseline pin on the claim.
- df1248539, 4c5eb59b4, 58454a7a7: CARD-1818 packed-refs lock, gc owner and stop list.
- d376ff9fa, a53b892d3, 3de464f58, 6f25823c2, ac32f0f76, 0e8fb2c80, 2a7454ccf: CARD-1822 refuse userinfo after a prefixed special scheme, a scheme-shaped prefix, and mailto passwords.
- be3924c48, 181d47b33: CARD-1821 sibling phantom does not floor a newer housekeeping final; skip sibling end_turn.
- 1fd035b12, 2eaf2bc4a: CARD-1450 keep latest and refused recovery pins.
- 70ee2f023: CARD-1451 S2 amendment and three advisories.
- a938e96a5: CARD-1454 key machine-turn debt by the chat.
- 060319704, 3b5ed3c22: CARD-1695 one lexical segment under AllowedCwd/agents.
- 6bc444da8, b70beaa90, 1163386c2, d8deb8b16: CARD-1509 refuse userinfo on every scheme and before send.
- 0f9d50a12: perf(git-inprocess) ls-files -z, ls-files --stage -z, ls-tree --full-tree.
- ceec59968, 2d8d85d7e: CARD-1803 page a blocked seat past the hour bound.
- 922a32730: CARD-1473 skip sibling end_turn that share a response tool.
- 23683f9bf: CARD-1520 keep the per-entry cleanup refusal reason.
- 95e85733a: CARD-1513 block a queued rewalk.
- eb1bff5cd, d18bf2e00: CARD-1817 bound runner repository gc.
- 2f7722f54: CARD-1441 disable auto gc on runner non-inspection git.
- 1d4a76c9f: CARD-1453 land git reads suppress optional locks.
- d861149d0, ff5490965: CARD-1771 bind HTTP runner reads to the HTTP JSON contract.
- 93acc0b54, 4069c9f48, 861ce4fce, 78e6f36e5, 25b63d097, f1ce1944e: CARD-1813 pwsh cache.
- 0cb7bfa7f, 99e2ffa35: land-graph record refresh. Records, not a runtime migration.
- 324f66da3: CARD-1815 treat a 57P01 tick-lock break as success.
- 2b5c9620d, 7c6d7f1c6: CARD-1792 Windows rc exclusion and config mode.
- 85bee975c, d3ad4fad6: CARD-1812 mask tick-lock halves; drop a failed retention lock.

Scripts, tests, and docs that the Windows gate needs, also on this pin:

- ac6d00022, 3a81fc86f: trx summary tag and grace.
- c4389a1c9, 21d07912a, 035194494: completed-but-hung capture.
- c2c28776e, a173a87a9: rostered Windows skips count as expected coverage; live-lane digest reseal.
- aa9d558f9, 7e98aa88e, 97d506524, 72fada665: CARD-1823 cumulative seed merge (`merge-nightly-seeds.ps1`).
- a240da0bb: group 130 E2E roster.
- 3025c89e7 and the CARD-1819 series: Windows-only classes stay off Linux filters.
- ca952f100: CARD-1792 policyHash and PinnedInputDigest reseal.
- f5541c4a1 / 965ab0803 / 65d94c686 area: land-graph C# audit and a single solution build of LandGraphAudit.
- 1651d9859: auto-title diagnose-row poll 30 s wall budget (group 86).
- 1b6e15064, 617f9acd4: Windows g87 and g86 failure notes.

CARD-1546 S2+S3 and CARD-1820 are not in master and are not part of this cut.

## Cleanup

After this report commit, ignored build outputs under tests, src, server, tools, scripts, samples, and Antiphon.AppHost are removed with `git clean -f -dX` on those named paths.

--- next stage ---
next: deploy: the Windows failed-first run commission
handoff: host activation is a separate decision
artifact: .antiphon/task-3e25efa8.md
