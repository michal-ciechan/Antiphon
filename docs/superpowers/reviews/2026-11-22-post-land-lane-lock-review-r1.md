# Review: post-land lane lock race fix (round 1)

Review task 229863ff, round 1, Final/Full. Author Codex (Debug task 4714c580). Reviewer Grok.
Subject `66ff06eff83edf4542061f5c9f3f3b3c045ebaf1`, parent `d8a65be7bcecd1eeff9dc2e655083fb95ba770d1`.
CARD-2015 is InProgress. Its card text is a different defect (advisory A-7).

**Verdict: BLOCKER.** One substantive finding (D-1). Next is code. The mutex makes the acquire decision and the owner write one critical section, and a two-process probe of that section holds on this host. The committed suite never starts two acquirers against an absent `lock.json`, so removing the mutex leaves the new test green.

## D-1. The committed test cannot go red for the publication race

Where: `tests/Antiphon.Tests/Scripts/PostLandEarlyWarningTests.cs` `Second_run_refuses_while_the_lane_holds_the_lock`, and the why-missed note `docs/investigations/2026-11-22-why-missed-post-land-lock-test-flake.md` (cheapest-check paragraph). `Stale_lock_reclaim_is_exclusive` is a different interleaving and stays in-process.

Failure: The replacement test starts the second lane only after the first lane has reached its selector. The selector runs after `EnterLock` has returned true and written `started`. Both lanes are `Task.Run` inside one test process (`Settings.LaunchChild`). The second call therefore always observes an existing live owner. Deleting `WaitForLockUpdate` from `EnterLock` and `ExitLock` leaves this test green: the File.Move check-then-rename window is never opened.

The parent test started two `RunAsync` parents together (`Task.WhenAll`). That shape could see the race, and it also admitted the separate timing flake (a winner could finish before the loser acquired, so both wrote `started` even when locking worked). The tip deletes that concurrent start and does not replace it with a two-process probe. The why-missed note says a separate two-entrant acquisition probe checks the initial owner-publication race. No such probe is in the suite.

`Stale_lock_reclaim_is_exclusive` starts two `Task.Run` calls of `EnterLock` in the same process, against a pre-written dead owner, with 200 ms and 800 ms sleeps. It does not launch two OS processes, and it does not publish onto an absent lock.

Why: Standing rule 4. A witness that cannot fail for the defect the change claims to close is a stub. The original production race is two contenders both succeeding at `File.Move` onto an absent destination. The retained author evidence (`/work/retained/4714c580cfae476b892fcec540489e0e/59/.antiphon/4714c580-lock-evidence/`) is ten runs of the sequenced method (checkpoint `20261122-025348-4472`), the stale test, and the derived-index test. It contains no 1,000-trial log and no `driver.sh`. The brief's 1,000 cross-process and 1,000 in-process claims are not committed tests and are not in that evidence.

Fix: Commit a test that starts two OS processes, each calling production `PostLandEarlyWarning.EnterLock` on the same absent path, and asserts exactly one returns true before either releases. One named mutant that removes the mutex around the owner-record update must turn that test red within about 20 trials. Keep the sequenced busy test; it covers a second arrival while a live owner still holds the record.

Scratch witness at the reviewed SHA, production `EnterLock`, two real processes, load under 14, one lease per build and per run. Source was restored to `66ff06eff` afterward (`git status` empty).

| Build | Result |
|---|---|
| Fixed code, lease `bc333bb4-5292-463e-86a5-2a5532e1e834` | `summary trials=40 single=40 doubles=0 other=0`, probe exit 0 |
| Mutex removed from `EnterLock` and `ExitLock` only, run lease `44a5e3c3-1888-4171-a880-c8f7793e6efe` | `summary trials=20 single=18 doubles=2 other=0` (trials 4 and 13), probe exit 1 |

The on-disk probe DLL after that restore is the mutant build. The 40/40 row is the earlier fixed binary, not that DLL.

## Lock behavior (holds)

The original race is Linux `File.Move(source, dest)` without overwrite: the runtime checks that the destination is absent, then `rename(2)` replaces an existing file. Two contenders can both observe absence and both return success. The base two-entrant probe in the why-missed note returned true twice on the first trial. A separate timing flake in the old test let a finished winner release before the loser acquired, which also produced `started, started`. Checkpoint `20261122-024124-65a9` at `d8a65be7`: 2 pass, 8 fail, failures `started, started`.

`WaitForLockUpdate` takes `Mutex(false, "Global\\antiphon-post-land-lock-" + SHA256 hex of Path.GetFullPath)`, uppercasing the path on Windows. `EnterLock` holds it around `EnterLockCore` (read `LockKind`, write the temp, `File.Move`, stale reclaim). `ExitLock` holds it around the owner check and delete. `AbandonedMutexException` is swallowed and the caller keeps the mutex, then reads the owner record. Any other wait failure disposes and throws. The `using` releases the mutex before `RunLane` plans or runs the lane. Exclusion for the rest of the run is the owner file (`pid`, `startedAt`, `runId`). The acquire decision and the publish are in the same critical section. A second arrival after a successful publish sees `busy` and returns false. Same-user processes on this host: 40/40 exactly one winner.

## Stale reclaim (holds)

`LockKind`: absent; ours (same pid and runId); busy (live pid that is not a reuse, or a dead pid with no `startedAt`); stale (unreadable, bad json, no pid, reused pid, or dead owner with `startedAt`). `IsPidReused` is a live pid whose process start is more than 2 seconds after the lock's `startedAt`. There is no age threshold. A live slow holder stays busy. The original holder's process start precedes the lock time, so the 2-second margin does not classify that holder as reused. A dead normal record (pid dead, `startedAt` present) is stale on the next `EnterLock`. Production `ReclaimDelayMs` is `POST_LAND_WARNING_RECLAIM_DELAY_MS`, default 0 via `IntOrZero`. Overlapping lanes would run two builds and two shard sets and let the last writer own the receipt.

## Generated checks

Checkpoint tool run `20261122-031728-0f7f` at `66ff06eff`, sourceState=clean, buildSource=verified, one build, unlisted none, verdict GREEN, exit 0. Wall 18m26s. Build phase on CP-1 was 607s.

| Row | Filter | Result |
|---|---|---|
| CP-1 | `/*/*/PostLandEarlyWarningTests/*` | executed 22, passed 22, failed 0, skipped 0 |
| CP-2 | the five named guard classes | executed 56, passed 56, failed 0, skipped 0 |
| CP-3 | `DerivedImpactIndexTests/committed_index_matches_the_builder` | executed 1, passed 1, failed 0, skipped 0 |

- Evidence-diff `d8a65be7..66ff06eff` via `Antiphon.Scripts.dll evidence-diff`: commits=1, entries=0, violations=0, exit 0. `scripts/check-evidence-diff.ps1` is not in this tree.
- Land-graph `-Check` (no `-Update`): approved. digest `A9BB27C595EA171463F0ED13725E198AA64385B1744B9E0BA2D211829C65A7CF`, count=80, exit 0. Lease `3209c312-a199-4927-b64e-0a5704c658bf`, 1-minute load 11.43. The tool rebuilt a missing DLL in 13s under that lease.
- Pins: `scripts/test-live-lane-run.ps1 -PrintInputDigest` printed `aff88704a0be7c2b989b6149b08976853da2fdf094491fd5baef6a57deae858d`, equal to `$script:PinnedInputDigest`. The subject commit does not touch those inputs.
- Merge-tree of `66ff06eff` against fetched `origin/master` `70c13dfe001c5df6c2108000584da6151ce42e43`: clean, tree `ad6fb51b100abcfd504a5c11ec89c03690906ae1`, exit 0. The brief's master `6f6a789e07f031496ae99e58ee6e563d30837a64` is not an ancestor of the subject.
- Impacted-classes merge-subset: `migrations` false, so the downgrade selector count is 0. Kinds `nightly-live-lane` and `test-lane-category`. The commissioned guard batch is CP-2. The broader merge-subset classes were not part of this round's named rows.
- Diff is three files, +139/−40: the why-missed note, `PostLandLane.cs`, `PostLandEarlyWarningTests.cs`. `LockKind` is unchanged. No positive-control rows. Mutation checks stay waived (operator 2026-11-12); the scratch mutant above is the review attack, and PCs stay pending.

## Advisories (cards, do not hold the land)

- **A-1.** On this host the process was bound to Microsoft.NETCore.App 9.0.23. While `EnterLock` held the mutex, its directory descriptors were `/home/app/.dotnet/corefx/Runtime/SharedMemory/.dotnet` and `.../.dotnet/shm` (mode 0700, owner app). `Global\` selected that tree's `global/` directory. `/tmp/.dotnet/shm` (mode 0777) stayed empty, including with `TMPDIR=/tmp`. No new per-lock inode appeared; exclusion is the 40/40 versus 2/20 result. A different user, HOME, or container that shares the checkout and not HOME does not share that mutex, and the File.Move race returns. The lock file is on the checkout. Staged rollout uses separate checkouts. The production lane is one Linux host.
- **A-2.** The Windows branch uppercases the path and uses the `Global\` prefix. `RunModeAllowed` returns exit 3 before detach when the platform is not linux, so this review did not execute a Windows session. Forcing the platform seam to linux on Windows would be the case where session-local mutexes matter.
- **A-3.** A dead owner with no `startedAt` stays `busy` forever. The normal writer always emits `startedAt`. A hand-written record without it wedges the lane.
- **A-4.** `LockKind` maps `IOException` and `UnauthorizedAccessException` to `stale`. A live owner's record that is briefly unreadable can be reclaimed. This commit does not change that branch.
- **A-5.** The reclaim sleep runs inside the mutex. The production default is 0. A large `POST_LAND_WARNING_RECLAIM_DELAY_MS` blocks other acquirers for that delay.
- **A-6.** CARD-2015's text is still the missing impacted-classes row for `scripts/restart-apphost.ps1` and `scripts/lib/windows-phone-home-env.ps1` onto the graceful-stop tests. This commit does not add that row. Landing the lock fix does not close that selector defect.
