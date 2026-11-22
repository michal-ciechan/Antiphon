# Final review round 1: nightly evidence size normalisation

Verdict: CLEAN. No product defect. Positive controls stay pending. This document is the review record; it does not change the readers.

Subject task `952704a5-c14c-47d1-b4d6-07f9a022dffc`. Reviewed source `14628bbf36ff10b8699cb6d4aa00d97592d8f035` on `feat/card-task-952704a5`, parent and merge-base `5411db8db92428e3db563485d3ee6c86ca3b8d64`. Reviewed range `5411db8db92428e3db563485d3ee6c86ca3b8d64..14628bbf36ff10b8699cb6d4aa00d97592d8f035`. Author company Codex. Reviewer company xAI. The author receipts named in the brief were not in this worktree; the counts below are this review's runs.

The subject diff is three files: `docs/investigations/2026-11-22-why-missed-nightly-golden-directory-size.md`, `scripts/lib/Antiphon.Nightly/TestPhaseFiles.cs`, and `scripts/lib/test-phase-paths.ps1`. `LinuxKind` now returns `unsupported` before it assigns `stat.Size`. The PowerShell Linux arm returns `Size = [int64]0` for every non-regular lstat result. The mode test is unchanged: `S_IFMT` `0xF000`, regular `0x8000` / `32768`, lstat, symlinks not followed. `LinuxStat.Size` remains `long`. `WindowsKind` and the Windows PowerShell body are not in the diff; directories and reparse points there already return size 0.

## Attacks

Regular size stays the 64-bit `st_size`. On this Linux host both readers matched `/usr/bin/stat`: regular file 5 bytes, empty file 0, sparse file `3221225472` (not an int32 truncation). A symlink to the 5-byte file stays `unsupported`, size 0, raw link size 36, and is not followed. A directory stays `unsupported`, size 0, while raw `st_size` is 4096. A fifo and a socket stay `unsupported`, size 0. Classification is the same predicate as before this commit; only the unsupported size changed, from `st_size` to 0.

No stored baseline digest includes per-entry path-identity sizes. `selectionDigest` is the SHA-256 of the canonical JSON of the release Selection. `expectedSkipRosterDigest` is the SHA-256 of sorted `platform`, `test`, `reasonClass`, `reasonPrefix` lines (`NightlyCoverageParts.RosterDigest`). Publish `summaryDigest` is the canonical JSON of profile, policyHash, suites, and exclusions. The protected-input pin `aff88704a0be7c2b989b6149b08976853da2fdf094491fd5baef6a57deae858d` lists nightly-common, policy, discovery, diagnostics, coverage, nightly-core, and two fixtures. It does not list `TestPhaseFiles.cs` or `test-phase-paths.ps1`. `Get-NightlyCoreBuildIdentity` hashes every `.cs` under `Antiphon.Nightly`, so this commit changes that local temp-cache key. That key is not a digest stored on run `3528e6b1`. Qualification timing uses size only after kind is `regular` (sidecar refusal, journal shard cache key, bounded read). Run `3528e6b1` lives on the Windows qualification host and was not recomputed here. Source inspection does not show an unintended baseline invalidation.

The golden `scripts/fixtures/nightly/core-golden/pathIdentity.json` is not in the subject diff. It expects `sub` unsupported size 0 and the regular sizes 7, 0, 200, 64, and 2. `Evidence_files_match_the_goldens` calls `TestPhaseFiles.PathIdentity` and compares that output to the file. It does not call the PowerShell reader, so it is not a Linux self-comparison of the two readers. The comment above the call still says the comparison is against the PowerShell body. That comment is stale. It does not hold the land.

`C1514T_PS51AndRerun` is an existing Windows `Skip.Test` on Linux (`CARD-1514: actual nightly native custody and PowerShell 5.1 rerun require Windows`). It is NotExecuted here. It is not a result of this size change, and it is not a Linux pass of the Windows branch.

This change does not alter an input wait or a release deadline. Parking stays dormant (CARD-1083). The readers sit under `scripts/lib`, so the OS witness census records a pending Windows startup obligation for a later rc pick. The change does not edit AppHost or the restart scripts. A restart is not required to accept this review.

## Selector

`scripts/impacted-classes.ps1 -Range 5411db8db92428e3db563485d3ee6c86ca3b8d64...14628bbf36ff10b8699cb6d4aa00d97592d8f035 -Json` exited 0. Kinds: `nightly-live-lane`. Classes: `LiveLaneRunnerTests`, `NightlyCoreGoldenTests`, derived `TestPhaseSummaryTests`. Keyword, roster, skipped, and owed lists are empty. Windows filters are empty. The migrations globs do not match these files, so the migrations row is absent and the downgrade-caller count is 0. A two-dot diff against master is the wrong tree: the parent is not master. The full derived land suite was not run. The derived class was included in CP-1. `tests/derived-impact-index.json` is not in the diff and was not regenerated.

## Checkpoints

Closed list. CP-1 and CP-2 are one build of `tests/Antiphon.Tests` into `bin-d4e9r1/`. CP-3 is the first probe invocation. CP-4 is the corrected probe after that row had already been recorded.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---|---|
| CP-1 | all | `tests/Antiphon.Tests -> bin-d4e9r1/` | path-classes | `/*/*/(LiveLaneRunnerTests*)\|(NightlyCoreGoldenTests*)\|(TestPhaseSummaryTests*)/*` | R-1 | 0 failed, 37 executed, Evidence_files_match_the_goldens included | 37 | 12 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp;ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | all | CP-1 | guard-batch | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | R-2 | 0 failed, executed at least 81 | 81 | 9 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp;ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | all | n/a | path-probe | `pwsh -NoProfile -File /tmp/d4e9f133-path-probe.ps1` | R-3 | exit 0, oracle cases passed, failed=0 | n/a | 4 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp;ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | all | n/a | path-probe | `pwsh -NoProfile -File /tmp/d4e9f133-path-probe.ps1` | R-3 | exit 0, PROBE failed=0 | n/a | 4 | true | TEMP=/tmp;TMP=/tmp;TMPDIR=/tmp;ANTIPHON_LINUX_COMPILER_POLICY=0 |

Run `20261122-060217-343a` at clean `14628bbf36ff10b8699cb6d4aa00d97592d8f035`, build source verified, one build, verdict RED only because CP-3 exited 1. The script threw on `Start-Process -WindowStyle` before any assertion. That is the review harness, not the readers. CP-1 TRX counters: total 42, executed 41, passed 41, failed 0, notExecuted 1. `Evidence_files_match_the_goldens` passed. The single NotExecuted result is `C1514T_PS51AndRerun`. Class composition of those 42 results: `NightlyCoreGoldenTests` 30, `LiveLaneRunnerTests` 4, `TestPhaseSummaryTests` 8. CP-2 TRX counters: total 84, executed 84, passed 84, failed 0. The nine-class guard floor in the table is 81; the live count is 84.

```
CHECKPOINT CP-3 commit=14628bbf36ff10b8699cb6d4aa00d97592d8f035 build=n/a filter=pwsh -NoProfile -File /tmp/d4e9f133-path-probe.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=14628bbf36ff10b8699cb6d4aa00d97592d8f035 sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-3 waited=0s executed=2.324s wall=2.973s
CHECKPOINT CP-1 commit=14628bbf36ff10b8699cb6d4aa00d97592d8f035 build=ok filter=/*/*/(LiveLaneRunnerTests*)|(NightlyCoreGoldenTests*)|(TestPhaseSummaryTests*)/* executed=41 passed=41 failed=0 skipped=1 trx=/work/worktrees/ws-04-d2/.antiphon/checkpoints/20261122-060217-343a/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=14628bbf36ff10b8699cb6d4aa00d97592d8f035 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=42.968s wall=43.544s
PHASES CP-1 slotWait=0s build=326.0895689s startup=8.7360492s testsWall=32.0133855s teardown=0.6191365s hostWall=41.3685711s
CHECKPOINT CP-2 commit=14628bbf36ff10b8699cb6d4aa00d97592d8f035 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/* executed=84 passed=84 failed=0 skipped=0 trx=/work/worktrees/ws-04-d2/.antiphon/checkpoints/20261122-060217-343a/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=14628bbf36ff10b8699cb6d4aa00d97592d8f035 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=78.543s wall=79.082s
PHASES CP-2 slotWait=0s build=0s startup=17.2614825s testsWall=59.0121116s teardown=0.6304765s hostWall=76.9040709s
```

Run `20261122-061853-e639` is the corrected probe, still at clean `14628bbf36ff10b8699cb6d4aa00d97592d8f035`, no second test build. Verdict GREEN. Console ends `PROBE passed=29 failed=0`. Both readers were loaded from `tests/Antiphon.Tests/bin-d4e9r1/Antiphon.Nightly.dll`. The oracle is `/usr/bin/stat`, not the other reader.

```
CHECKPOINT CP-4 commit=14628bbf36ff10b8699cb6d4aa00d97592d8f035 build=n/a filter=pwsh -NoProfile -File /tmp/d4e9f133-path-probe.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=14628bbf36ff10b8699cb6d4aa00d97592d8f035 sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-4 waited=0s executed=5.188s wall=5.591s
```

## Gates at the reviewed source

Evidence-diff `5411db8db92428e3db563485d3ee6c86ca3b8d64..14628bbf36ff10b8699cb6d4aa00d97592d8f035`: commits=1, entries=0, violations=0, exit 0. The report commit is checked again over the full range and is not a new test run.

`git fetch origin master` moved `origin/master` from the brief's `72a1da4428d8d0dc8b0ecd8ad533cbad75399288` to `565d2239a26144b748cd846aa14954b679b23959`. `git merge-tree --write-tree origin/master HEAD` at `14628bbf36ff10b8699cb6d4aa00d97592d8f035` exited 0 and wrote `1a952d3cb81c8d5d6e0fbea0739a9c793b6bab92`. No conflict was resolved.

Land-graph `-Check` at that same clean source exited 0, `approved`, digest `A9BB27C595EA171463F0ED13725E198AA64385B1744B9E0BA2D211829C65A7CF`, count 80. `-Update` was not run.

## Advisory

`NightlyCoreGoldenTests.Evidence_files_match_the_goldens` still comments that the identity comparison is against the PowerShell body. The assertion is the committed golden file and the C# reader. The comment should be corrected by a docs change. It does not hold the land.
