# Review: post-land lane lock two-process witness (round 2)

Review task 8b13571f, round 2, Final/Full, blockers only. Author Codex (subject task 4714c580). Reviewer Grok.
Subject `8b077d0fee254d9b8b6e5f57be2f5dbd3727e05d` on `origin/feat/card-task-217e685f`.
Reviewed range `66ff06eff83edf4542061f5c9f3f3b3c045ebaf1..8b077d0fee254d9b8b6e5f57be2f5dbd3727e05d` (one commit).
Round 1 is `docs/superpowers/reviews/2026-11-22-post-land-lane-lock-review-r1.md` on `origin/feat/card-task-229863ff`.

**Verdict: CLEAN.** No blocker. Next is land. The land path owns the land-graph record refresh. Advisories do not hold the land. Positive controls stay pending. Mutation checks stay waived (operator 2026-11-12). The mutex-removed red was not re-run.

`scripts/lib/Antiphon.Nightly/PostLandLane.cs` is not in this range. The round adds the two-process witness, the `post-land-lock` harness mode, the Nightly project reference on `tests/Antiphon.ScriptHarnessHost/Antiphon.ScriptHarnessHost.csproj`, the plan, and the why-missed count note. Five files, +224/−6.

## Blocker hunt

### 1. Exactly one winner, and a slow start cannot pass green

`Two_processes_publish_exactly_one_owner` runs 24 trials. Each trial requires `wins == 1`, where a win is a result file whose trimmed text is `true`. Zero or two `true` results append a failure. A ready or result timeout returns `wins=0` and fails the test. That is a false red, not a false green.

Both children call production `PostLandEarlyWarning.EnterLock` on one absent `lock.json` after a shared go file and a peer-armed spin. Neither calls `ExitLock`. The parent writes the release file only after both result files exist, so the loser inspects the winner's live pid. Distinct run ids, so `ours` cannot alias the peer.

Ten checkpoint repetitions at admission load 5.02, one host invocation, all `trials=24 failures=0`:

| Repetition elapsedMs |
|---:|
| 4665, 5646, 5697, 5884, 6207, 6238, 6442, 6818, 6961, 7337 |

Maximum 7337 ms. All under 25 s. The closed-plan runs of the same method were 6723 ms and 6611 ms at admission load 2.44. This host was not at load 12–14 when the rows started. No REFUSING line.

### 2. Helper process and Windows classification

The child is the existing `tests/Antiphon.ScriptHarnessHost` executable, mode `post-land-lock`, argv length 9. The test starts `dotnet` plus `AppContext.BaseDirectory/script-harness-host/Antiphon.ScriptHarnessHost.dll`. It is not an environment switch and it is not a second executable. `PostLandLock` uses files, `Thread.SpinWait`, and production `EnterLock`. It has no `/bin/sh`.

`PostLandEarlyWarningTests` is `[Category("Unit")]` and `[ParallelLimiter<ProcessSpawnLimit>]`. The new method adds no OS branch and no Linux-only attribute, so it does not need a Windows exclusion-roster row. Plan CP-3 executed `ProcessSpawnLimitTests` and `TestLaneCategoryGuardTests` at 58/58/0. Desktop was not executed (advisory A-2).

### 3. Children and temp directories on the green runs

Each trial's `finally` writes the release file when missing, calls `Kill(entireProcessTree: true)` on both processes, and `Directory.Delete`s the trial directory. After the 10/10 run, no `post-land-lock-*` temp directory remained and no `post-land-lock` child from this worktree remained. A red run was not forced.

### 4. Lock path and mutex name

Each trial uses `Directory.CreateTempSubdirectory("post-land-lock-")`. The production mutex name is `Global\antiphon-post-land-lock-` plus the SHA-256 hex of `Path.GetFullPath` of that trial's `lock.json`. A later trial does not reuse the previous path, so a stale mutex or lock file from an earlier trial is not the next trial's name.

## Generated checks

Closed plan `docs/superpowers/plans/2026-11-22-post-land-lock-two-process.md`, checkpoint `20261122-044513-c160`, exit 0, sourceState=clean, buildSource=verified, unlisted none. Admission load 2.44.

```
CHECKPOINT CP-1 commit=8b077d0fee254d9b8b6e5f57be2f5dbd3727e05d build=ok filter=/*/*/PostLandEarlyWarningTests/Two_processes_publish_exactly_one_owner executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/ws-04-d2/.antiphon/checkpoints/20261122-044513-c160/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=8b077d0fee254d9b8b6e5f57be2f5dbd3727e05d sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=8b077d0fee254d9b8b6e5f57be2f5dbd3727e05d build=reused filter=/*/*/PostLandEarlyWarningTests/* executed=23 passed=23 failed=0 skipped=0 trx=/work/worktrees/ws-04-d2/.antiphon/checkpoints/20261122-044513-c160/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=8b077d0fee254d9b8b6e5f57be2f5dbd3727e05d sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=8b077d0fee254d9b8b6e5f57be2f5dbd3727e05d build=reused filter=/*/*/(ProcessSpawnLimitTests*)|(TestLaneCategoryGuardTests*)/* executed=58 passed=58 failed=0 skipped=0 trx=/work/worktrees/ws-04-d2/.antiphon/checkpoints/20261122-044513-c160/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=8b077d0fee254d9b8b6e5f57be2f5dbd3727e05d sourceState=clean buildSource=verified
```

Repeat selection, checkpoint `20261122-050001-a3c1`, exit 0. `--repeat` cannot reuse the closed plan's build, so this is a second `Antiphon.Tests` build (`bin-c217e-r10/`). Admission load 5.02.

```
CHECKPOINT CP-1 commit=8b077d0fee254d9b8b6e5f57be2f5dbd3727e05d build=ok filter=/*/*/PostLandEarlyWarningTests/Two_processes_publish_exactly_one_owner executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/ws-04-d2/.antiphon/checkpoints/20261122-050001-a3c1/rows/CP-1/run.trx slot=granted waited=0s repeat=10 repetitions=10/10 hostInvocations=1 dirty=0 source=8b077d0fee254d9b8b6e5f57be2f5dbd3727e05d sourceState=clean buildSource=verified
```

Named guard batch, checkpoint `20261122-051550-b2d0`, exit 0. Third `Antiphon.Tests` build (`bin-c217e-guard/`) because the tool had reclaimed the earlier outputs and the repeat assembly is a different compilation. Admission load 4.54. The commissioned cap is these five classes, not the merge-subset union below.

```
CHECKPOINT CP-1 commit=8b077d0fee254d9b8b6e5f57be2f5dbd3727e05d build=ok filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)/* executed=56 passed=56 failed=0 skipped=0 trx=/work/worktrees/ws-04-d2/.antiphon/checkpoints/20261122-051550-b2d0/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=8b077d0fee254d9b8b6e5f57be2f5dbd3727e05d sourceState=clean buildSource=verified
```

- Evidence-diff via `scripts/antiphon evidence-diff` (there is no `scripts/check-evidence-diff.ps1`). Range `d8a65be7bcecd1eeff9dc2e655083fb95ba770d1..8b077d0fee254d9b8b6e5f57be2f5dbd3727e05d`: commits=2, entries=0, violations=0, exit 0. Lease `06cd3a6b-259a-4b88-9884-8ed1df3ddbc1`.
- Merge-tree `--write-tree` of HEAD against fetched `origin/master` `5411db8db92428e3db563485d3ee6c86ca3b8d64`: clean, tree `efc322820272bde1b89435453f30ba65ecb960d5`, exit 0. The same command against the brief's master `e3806b6278227b5acb364c6cd530da45e260a031` is also clean, tree `d825379d4ce6bcd0e0107573cfa320a19eaa4c76`. The brief's master is an ancestor of fetched master. Nine commits sit between them; none touch a csproj or the land-graph record.
- OS-witness lint for the round range, exit 0. The new `PostLandLock` mode adds no OS token. Restart impact required=false.
- Impacted-classes `-MergeSubset` for `d8a65be7..8b077d0`: `migrations` false, so the downgrade selector count is 0. Kinds `nightly-live-lane` and `test-lane-category`. The class union includes the land-notification tests, `AppDbContextModelTests`, `LiveLaneRunnerTests`, `NightlyCoreGoldenTests`, `ReleaseGatePlatformTests`, and `TestLaneCategoryGuardTests`. That union was not this round's commissioned batch. `TestLaneCategoryGuardTests` is inside plan CP-3.

## Land-graph

`-Check` on this tip, no `-Update`, lease `69f4f72c-0d40-4030-80a9-4fddc51fb42b`, exit 1:

```
digest=A178B9D5D6599C683A0FF63561F31C54A35B4888CCCD495D86EAC418A18A338F count=80
manifest does not match the selected graph
audit refused: land graph pre-check refused
```

The recorded manifest on this tip and on `origin/master` is still `A9BB27C595EA171463F0ED13725E198AA64385B1744B9E0BA2D211829C65A7CF`, count 80. The same `-Check` on a detached worktree of `5411db8db92428e3db563485d3ee6c86ca3b8d64` printed that digest, count 80, and `approved`, exit 0. Lease `be31bbf6-d569-4223-a479-d5a30163fff1`. The worktree was removed.

The author's account is not what the tree shows. This range adds the Nightly `ProjectReference` to `Antiphon.ScriptHarnessHost.csproj`. Fetched master does not contain that reference, and no master commit since `e3806b62` touches the file. Count stays 80 because Nightly is already in the graph; the new edge changes the digest. Evaluation produced a digest, so this is the record mismatch the landing batch owns. `scripts/land-graph-land-refresh.ps1` refreshes it before land. This review did not run `-Update`.

## Advisories (do not hold the land)

Round 1 A-1 through A-6 still stand. This range does not change `PostLandLane.cs`.

- **A-1.** The mutex lives under the process HOME shared-memory tree, not `/tmp/.dotnet/shm`. A different user or HOME that shares the checkout and not HOME does not share the mutex.
- **A-2.** Windows was not executed. The helper is portable managed code. `RunModeAllowed` still returns exit 3 off Linux for the production lane.
- **A-3.** A dead owner with no `startedAt` stays `busy`.
- **A-4.** An unreadable lock is classified `stale`.
- **A-5.** The reclaim sleep runs inside the mutex. Production default delay is 0.
- **A-6.** CARD-2015's live text is still the missing impacted-classes row for `scripts/restart-apphost.ps1` and `scripts/lib/windows-phone-home-env.ps1`. This witness does not add that row.
- **A-7.** If the second `Process.Start` throws, the first `using` disposes the handle and the trial `finally` has not been entered, so that child is not killed there. `StopProcess` catches only `InvalidOperationException`. Neither path showed a leftover after the green 10/10.
- **A-8.** The new test's `[Timeout(60_000)]` is wider than about twice the measured 7.3 s maximum. The plan has no `## Performance budget` section. That does not force another code round.
