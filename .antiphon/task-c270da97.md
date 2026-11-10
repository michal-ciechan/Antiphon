# Review CARD-1784 S1, round 3 (Final, diff only, blockers only): real slot path

Subject: origin/feat/card-task-d705ed31, tip `6fa1d709ecbbe1991404dbecbbf14d6a21930c72`. The tested source is `287fc145771e1850a22d681a1c49e44712d2017f`; the tip adds only `.antiphon/task-d705ed31.md`. Code d705ed31 (Grok) wrote it. The landing owner is the original Code task 3442cc1d.
Round-3 range: `6eddd8fa0c3a6ba9eb226a1683f19f83b3cd0647..6fa1d709ecbbe1991404dbecbbf14d6a21930c72` (red ee4258bbb, fix 287fc1457, report 6fa1d709e). Master: `e06bcb3a0c17f4452db8b31ba92a22694ad3d5ab`.

Verdict: **CLEAN. No blockers.** Round-2 B-5 is fixed on the real path, and the new lock and receipt rules hold under my own probes. The advisories below go to cards and do not hold the land.

## Evidence at the reviewed SHA

| Check | Result |
|---|---|
| CP-1, checkpoint tool run `20261109-233605-966c`, one-row extract `.antiphon/c1784-r3-cp1.md` of the plan row | `CHECKPOINT CP-1 commit=6fa1d709ecbbe1991404dbecbbf14d6a21930c72 build=ok filter=/*/*/PostLandEarlyWarningTests/* executed=20 passed=20 failed=0 skipped=0 slot=granted waited=30s dirty=0 source=6fa1d709ecbbe1991404dbecbbf14d6a21930c72 sourceState=clean buildSource=verified`. `unlisted: none`. `validate --expected-source-sha 6fa1d709...` printed `CHECKPOINT SOURCE VALID ... rows=1`, exit 0. Phases: build 473 s and tests 188 s (SLOW CLASS, 20 tests). Wall was 14m18s with a 45 s slot wait. |
| Guard batch on scratch merge `4458cf92e713c80c44e651b42aa034ec07009e86` (e06bcb3a0 + tip). One build lease (4m43s), then one run lease. | 78/78 passed. TestClassificationPolicyTests 41, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3, **TestLaneCategoryGuardTests 1 (green on master e06bcb3a0)**, TaskPlatformGuidanceTests 5, PostLandEarlyWarningTests 20. |
| `check-os-witnesses.ps1`, base d861149d0, head 6fa1d709e, plan at head. I wrote the declaration, carried over from round 2 with the SHAs updated. | exit 0: `OS-WITNESS complete syntax/inventory`. W-1 (`scripts/post-land-early-warning.ps1:29`) and restartImpact are still `NATIVE pending` for Windows. The new code adds no OS-branch candidate. |
| `check-evidence-diff.ps1 -Repository . -BaseRef d861149d0 -HeadRef 6fa1d709e` | commits=10 entries=1 violations=0, exit 0. The one entry is the Code report markdown. |
| `git merge-tree --write-tree origin/master HEAD` | clean, tree `a47fe3d4f4d9`. |
| `audit-land-verification-graph.ps1 -Check` on the merge, under a lease | exit 0, `approved`. |

Every dotnet build and test ran through `scripts/build-slot.ps1`, one lease per command: the checkpoint tool build, the CP-1 run with its own slot, the merge build, the guard run, and land-graph `-Check`. The probes below ran pwsh and git only. They started no dotnet. PCs stay pending for SourceLanding Mutation. I did not re-run the red commit ee4258bbb, which claims 6 failed.

## Check 1: the real slot path

Setup: a scratch clone at 287fc1457 with `scripts/build-slot.ps1` replaced by a **committed** stub. No `POST_LAND_WARNING_SLOT_SCRIPT` and no shim, so this is the production branch. The stub prints `BUILD SLOT ...`, `42`, `0` and `Build succeeded.` to stdout, plus one stderr line. For shards it writes a fresh TRX to `--results-directory/--report-trx-filename`, and it exits with the code given in its env. Each run used the real tree's plan (23 shards) and `-FileList src/Antiphon.Server/Program.cs`.

| Case | Receipt |
|---|---|
| build 0, shards 0, passing TRX | `state=completed`, `buildExit` is the integer 0, 23 shards each with integer `exit` 0, 1 build plus 23 `--no-build` calls. `new-reds=0`. |
| build 4 | `state=incomplete`, `buildExit` is the integer 4, `new-reds=unknown build=4`, 1 slot call, 0 `--no-build`. |
| build 0, shards 2, failing TRX | `state=completed`, integer shard exit 2, `newReds=["StubTests.Boom"]`. |
| build 0, shards 8, TRX with zero results | `state=incomplete`, `executed=0`, `new-reds=unknown` (A-9 closed). |
| build 0, shards 0, no TRX | `state=incomplete`, `new-reds=unknown no-trx=...`. |

The slot log `<tip>/slot.log` holds all 24 stub stderr lines. The child's `.err` file is empty.

Leak audit, across both scripts:
- Every native or child call is accounted for.
- git and the selector go through `Invoke-PostLandProcess`, which redirects and captures stdout and stderr and returns one object.
- Both `Start-Process` calls are assigned to `$null`, and they sit in the entry script, not in a function return.
- The only `& pwsh` calls are the two in `Invoke-PostLandSlot`, now `*>> $LogPath` and `| Out-Host`.
- There are no command substitutions, `Invoke-Expression` or `& dotnet`/`& git` calls.
- `Push-Location`, `Pop-Location`, `Remove-Item` and `Start-Sleep` emit nothing.
- `.Add` results are `[void]`, `$null =`, or used in a condition.
- `Complete-PostLandOutcome` is assigned to `$null`.

## Check 2: the lock

- **Two starts and a stale lock** (dead pid, `startedAt` 1 h old), launched together. I varied the reclaim delays (0/0, 0/400, 300/0, 0/1500 ms). **4 of 4 trials:** one `started` and one `skipped-busy`, exactly 1 `dotnet build` and 23 shards, no `.reclaim` or `.tmp` leftovers, and the lock released at the end. The 0/1500 trial used the move-aside-and-restore path: B renamed A's fresh lock, saw `busy`, and moved it back, and A still completed.
- **Reused pid.** A live `sleep` pid with `startedAt` an hour before its start is treated as stale. The lane reclaimed it and ran (1 build), and the lock was gone afterwards.
- **Live pid.** The same live pid with `startedAt` after its start gives `skipped-busy`, and the lock is unchanged. With no `startedAt` the result is also `skipped-busy`.
- **Live lane.** A second start 4 s into a running lane gives `skipped-busy`. The lane's lock had pid 3008 and was not reclaimed. Result: 1 build, `completed`.

## Check 3: incomplete runs never become the baseline

I used one receipt root, and the control used a second. T1 (stub v2 commit) failed `StubTests.Boom` and was left `incomplete` with one shard having no TRX. T2 (child commit), failing the same test, then gave `previousTipSha=null` and `new-reds=1`. Control: with T1 `completed` and the same failure, T2 gave `previousTipSha=T1` and `new-reds=0`. So the baseline mechanism works, and it excludes incomplete receipts.

## Advisories (to cards; do not hold the land)

- **A-11. A three-contender reclaim window.** It needs a stale lock plus three near-simultaneous starts. B classifies `stale`, and meanwhile A reclaims and links a fresh lock. B then renames A's lock aside, sees `busy`, and moves it back with `File.Move`, which does not overwrite. If C links a new lock in that window of a few milliseconds, B's move back fails silently (`catch { }`). Then A and C both run, and `lock.json.<pid>.<n>.reclaim` is left behind. Two contenders are safe, which I probed (4/4). Fix: don't move a live lock aside. Check the bytes before the rename, or hold an exclusive `FileShare.None` handle as the lock.
- **A-12. The pid-reuse check compares wall-clock time with .NET's boot-relative `Process.StartTime`.** If the realtime clock steps forward by more than 2 s during a long lane, the live lock would read as stale. This is theoretical on server2.
- **A-13. Shard label.** A zero-executed shard with a fresh TRX is reported under `no-trx=` in the summary line. It should have its own label, such as `empty=`.
- **A-14. CP-1 estimate.** Wall was 14m18s against the plan's 12-minute estimate: build 473 s on a loaded host, tests 188 s for 20 tests (was 121 s for 14). That is under the 30-minute R1 limit. Re-estimate the row.
- Round-2 A-10 (filename-regex doc pins, and the Windows setsid fallback not executed) and A-2 (self-compare census) still stand.

## Scratch

`.../scratchpad/{probe,r-*,cp1*.log,mergebuild.log,guard.log,guardtrx,lgcheck.log,osw.json}` is left in place, under the brief's no `rm -rf` rule. I removed the scratch merge worktree with `git worktree remove`. The checkpoint tool deleted `bin-c1784-s1/`. No probe processes remain.

--- review evidence ---
subjectTaskId: 3442cc1d-9b5a-4b90-bf1b-ea163438e546
reviewedSourceSha: 6fa1d709ecbbe1991404dbecbbf14d6a21930c72
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 3
reviewedRange: 6eddd8fa0c3a6ba9eb226a1683f19f83b3cd0647..6fa1d709ecbbe1991404dbecbbf14d6a21930c72
