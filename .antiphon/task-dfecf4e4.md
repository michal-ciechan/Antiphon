# Review CARD-1784 S1, round 2 (Final, diff only): post-land early-warning repair

Subject: origin/feat/card-task-3442cc1d tip `6eddd8fa0c3a6ba9eb226a1683f19f83b3cd0647` (Code 3442cc1d, Grok author).
Round-2 range: `6a97ddc201118b42d665afa8de588cf582ef8cb4..6eddd8fa0c3a6ba9eb226a1683f19f83b3cd0647` (red 60e24bbfb, fix 05a47779c, fix 6eddd8fa0).
Card range: `d861149d0738bef6019e1a437efc69d245512ab9..6eddd8fa0c3a6ba9eb226a1683f19f83b3cd0647` (7 commits). Master `ceec59968e4cebe4aa0da1eb0c148fa8869be3f7`.

Verdict: **1 blocker (B-5), a regression introduced by the B-1 fix.** Round-1 B-2, B-3 and B-4 are resolved. B-1 is resolved only under the test shim. On the real `build-slot.ps1` path, the lane now never runs a shard and never writes a receipt.

## Evidence at the reviewed SHA

| Check | Result |
|---|---|
| CP-1 (checkpoint tool, run `20261109-220108-74a9`, one-row `.antiphon/c1784-r2-cp1.md` extract of the plan row) | `CHECKPOINT CP-1 commit=6eddd8fa0c3a6ba9eb226a1683f19f83b3cd0647 build=ok filter=/*/*/PostLandEarlyWarningTests/* executed=14 passed=14 failed=0 skipped=0 ... slot=granted waited=0s dirty=0 source=6eddd8fa0c3a6ba9eb226a1683f19f83b3cd0647 sourceState=clean buildSource=verified`. Phases: build 362 s, tests 121 s (SLOW CLASS 121 s, 14 tests; row estimate 12 min holds). `validate --expected-source-sha 6eddd8fa...` gives `CHECKPOINT SOURCE VALID ... rows=1`, exit 0. |
| Guard union on scratch merge `328d6ff02bbb05f16dfcb8afa4a3f93ea53f9745` (origin/master ceec59968 + tip). One build lease, then one run lease. | 127/127 passed, 0 failed. TestClassificationPolicyTests 41, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3, **TestLaneCategoryGuardTests 1 (green on current master)**, TaskPlatformGuidanceTests 5, ProcessSpawnLimitTests 55, PostLandEarlyWarningTests 14. Wall 3m10s. |
| `check-os-witnesses.ps1` (base d861149d0, head 6eddd8fa0, plan at head), with a declaration I wrote (Code's ignored JSON is not reachable) | **exit 0**: `OS-WITNESS complete syntax/inventory`. W-1 = `scripts/post-land-early-warning.ps1:29` (`$linuxHost = $IsLinux`), productionType `ProcessStartInfo`, factory `StartPostLandEarlyWarning`, testCase `Non_linux_run_refuses_before_a_slot`, absent/empty/value setupAssertions found in code. Two items are `NATIVE pending`: W-1's Windows absent/empty branch, and restartImpact (`scripts/lib/**` census match; AppHost never loads this library). |
| `check-evidence-diff.ps1 -Repository . -BaseRef d861149d0 -HeadRef 6eddd8fa0` | commits=7 entries=0 violations=0, exit 0. |
| `git merge-tree --write-tree origin/master HEAD` | clean, tree `bf6f03810c1c`. |
| `audit-land-verification-graph.ps1 -Check` on the merge (Release evaluator built under a slot, -Check under a slot) | exit 0, `approved`. |
| Docs-only probe (`-Plan -FileList`) | `docs/testing-and-build.md` -> `run` (pinned by a test). `docs/bootstrap.md` -> `run`. The card's plan doc -> `skipped-docs`. Real-tree plan gives 23 shards with distinct ids (round-1 A-1 duplicates are gone). |

Every build and test driver ran through `scripts/build-slot.ps1`, one lease per command. That covers the checkpoint tool build, the checkpoint run (which takes its own slot), the merge build, the guard run, the LandGraphAudit build and `-Check`. The one exception was a first `-Check` run without a lease, about 20 s, evaluator only, no build. I re-ran it under a lease and report only that run. The probes below ran pwsh and git only, against a committed stub `build-slot.ps1` in a scratch clone. They started no dotnet. PCs stay pending for SourceLanding Mutation. Red/green mutants for the new witnesses were not re-run in this round. The red commit 60e24bbfb claims 8/14 red.

## Blocker

**B-5. The real slot path returns the build's stdout, not its exit code. The lane skips every shard, throws, and writes no receipt.**
- Where: `scripts/lib/post-land-early-warning.ps1`, `Invoke-PostLandSlot`. The non-shim branch runs `& pwsh -NoProfile -NonInteractive -File $slot ... @Command`, which is uncaptured, so every stdout line of build-slot and dotnet becomes function output. Then `return [int]$code`. At the caller, `Invoke-PostLandLane` does `$buildExit = Invoke-PostLandSlot $build`, then `if ($buildExit -eq 0 -and ...)`, then `Format-PostLandSummary ... -BuildExit $buildExit` (an `[int]` parameter). Shards have the same problem: `$shardExit` becomes an array, which goes to `$shard.exit`.
- Failure (probe in a scratch clone at the tip, with `scripts/build-slot.ps1` replaced by a committed stub that prints `BUILD SLOT ...` and `Build succeeded.` and then exits):
  - Build exit 0: `$buildExit` is `Object[]`, so `$buildExit -eq 0` gives `@(0)`, which is `$false`. The shard loop is skipped, and the slot log shows only the `dotnet build` line. Then `Cannot process argument transformation on parameter 'BuildExit'. Cannot convert the "System.Object[]" value ... to type "System.Int32"`, child exit 1, no `<tip>.json` receipt. `last-start.json` is written, so later runs for the next 60 minutes coalesce to `skipped-hourly`.
  - Build exit 4: the same exception and no receipt, so the brief's `new-reds=unknown build=4` receipt never appears on the real path.
  - Minimal repro: `function F { & pwsh -NoProfile -Command "Write-Host line1; exit 0"; return [int]$LASTEXITCODE }; $x = F` gives `type=Object[] count=2 eq0=False`.
- Why: round 1 discarded the result (`$null = Invoke-PostLandSlot $build`), so shards ran. The B-1 fix started using the return value. All five new run-mode witnesses (`Build_exit_...`, `Shard_error_...`, `Stale_trx_...`, `Slow_shard_...`, `Second_run_...`) set `POST_LAND_WARNING_SLOT_SHIM`, which returns before the `& pwsh build-slot.ps1` call. No test reaches the production slot invocation, so CP-1 is 14/14 green while every real lane run on server2 fails. That is the producer-to-recipient gap: the lane's only real producer is build-slot output.
- Fix: keep native output out of the pipeline. For example, use `& pwsh ... -File $slot ... @Command | Out-Host`, or redirect to a per-run log (`*>> $log`), then read `$LASTEXITCODE`. Add a witness that drives the real `Invoke-PostLandSlot` branch against a stub `build-slot.ps1` that writes stdout lines and exits N. Use a test seam for the slot-script path (such as `POST_LAND_WARNING_SLOT_SCRIPT`) or a fixture copy of `scripts/`. Assert three cases: exit 0 runs the shards and gives `completed`, exit 4 gives `incomplete` with `build=4` and no `--no-build`, and a shard exit is an int on the receipt.

## Round-1 blockers re-checked

- **B-1 (shim level): met.** Build exit != 0 gives `state=incomplete`, `buildExit=4`, `new-reds=unknown build=4`, one `dotnet build` and no `--no-build`. The child returns 1; the reserved exit 4 is not reused. `Error`/`Timeout`/`Aborted` count as failures, matching `TrxReport.cs:57`. A stale `<shard>.trx` is removed before each shard and then mtime-checked against shard start minus 2 s, so a shard with no fresh TRX is `no-trx`, which gives `incomplete`. Baseline: `Get-PostLandPrevious` takes only `state == completed`, and `Write-PostLandReceipt` never overwrites a completed receipt with a non-completed one, so an incomplete run never becomes the next tip's baseline. The `Build_exit_...` follow-up plan asserts `previousTipSha` null. On the real path, **B-5 defeats all of this**.
- **B-2: met.** Absent/empty now branch on `OperatingSystem.IsLinux()`. Windows expects exit 3 with `refused=not-linux` and the new message, and no shim file. The `windows` seam value proves the Windows shape on Linux. Executing on Windows stays pending (W-1 native obligation).
- **B-3: met.** The stand-in class and its calls are gone (0 hits). The interpolated literals are rewritten. The checker exits 0 against the real script process (above).
- **B-4: met.** `Enter-PostLandLock` takes the lock before planning: it writes a temp file, then calls `File.Move` without overwrite, which is `link(2)` on Linux and therefore atomic exclusive create. `Exit-PostLandLock` runs in `finally`, including on `exit` and on an exception. Probes:
  - Two parent starts 0.5 s apart: A's marker `started`, B's marker `skipped-busy`, both parents exit 0 and print their own `runId=` marker. The slot log has exactly one `dotnet build`.
  - A killed with `kill -9` mid-build left `lock.json` with a dead pid and `startedAt`. Run C reclaimed it, ran its build, and released the lock.
  - A live lock was not reclaimed: that is B above.
  - The parent waits on `lane-<RunId>.json`. The `RunId` is a 32-hex nonce, and anything else is refused with `bad-run-id`. `lane-started.json` is gone.

## Advisories (to cards; do not hold the land)

- A-6. A stale-lock reclaim race. Two contenders both read `stale`. A deletes the lock and links its own. B's pending `Remove-Item` then deletes A's fresh lock, and B links. Two lanes run. This needs a crashed run plus two near-simultaneous starts. Fix: reclaim by renaming the stale file to a unique name and checking its content before deleting it, or compare the bytes before `Remove-Item`.
- A-7. PID reuse. A dead lane pid that is recycled by a live process keeps the lock `busy` until that process exits. `startedAt` is stored but never compared with the process start time.
- A-8. No test covers stale-lock reclaim. It was verified only by the manual probe above.
- A-9. A non-zero shard exit with a fresh TRX and zero failures (MTP exit 8, zero tests) is `completed`. Consider making `executed == 0` or exit 8 incomplete.
- A-10. Pinned-doc detection is a filename regex over `tests/**/*.cs`. A doc read through directory enumeration is not caught. The run-mode tests on Windows (setsid fallback to `Start-Process pwsh`) were not executed here; the Windows rc lane owns that.
- Round-1 advisories: A-1 is fixed (23 distinct shard ids, and the test asserts distinct ids). A-3 is narrowed (only `docs/**/*.md`, and not when a test pins it). A-4 is fixed (`slowShards` plus per-shard `slow`). A-5 is fixed (actionable message). A-2 (self-compare census) stands.

## Scratch

`/tmp/claude-1654/-work-worktrees-task-dfecf4e4/4d028efe-0371-4942-b7ea-f74678293162/scratchpad/{r1,rr1,rr2,rr3,rrplan,guardtrx}` was left in place (no `rm -rf` per brief). The scratch merge worktree `.../scratchpad/merge` was removed with `git worktree remove`. The checkpoint tool deleted the CP-1 output `bin-c1784-s1/`. No probe processes remain.
