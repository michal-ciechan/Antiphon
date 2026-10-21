# CARD-1389: one owner for a reclaimed run.lock

Master-only. Read against `origin/master` `dcb461ce03c9bd8ea7d0edebfc637af33917776b` (the CARD-1386 reclaim is in that tree). Card read from the Antiphon board on 2026-10-21. The three gaps are still present. This branch does not backport. The closed command rows are in ### Checkpoints.

`GET /api/runner-defaults` returned revision 2. `GET /api/session-runners` returned an eligible Windows runner, an eligible Linux runner, and one draining runner that is not accepting work. Re-read at dispatch. Omit `-Runner`. Pin `-Platform Linux`: the double-reclaim proof is the Linux unlink of an open lock, and Windows `FileShare.Read` already makes that second delete fail.

## Ground truth

Line numbers are at that source SHA.

| Card assumption | What the code does | Smallest change |
|---|---|---|
| Two fresh attempts can both reclaim one dead `run.lock` on Linux. | `Enter-NightlyReclaimedLock` (`scripts/lib/nightly-common.ps1:787`) writes `lock-reclaim.json` (`:799`), then `Remove-Item` (`:805`, errors swallowed at `:807`), then `FileMode.CreateNew` with `FileShare.Read` (`:814-818`). It does not compare the file with the snapshot it was given. A second process that still holds that snapshot unlinks the first process's open file and its own `CreateNew` succeeds. On this host both processes returned `Ok=true`, `Reason=reclaimed`, and the file ended on the second run id. Windows sharing denial is why the same schedule returns `live-owner` there. | Rewrite the bytes through one exclusive open. Do not unlink. |
| A losing attempt must not replace the winner's reclaim record. | `Write-NightlyLockReclaim` (`:757`) runs before `CreateNew`. The lose path (`:825-833`) returns `live-owner` or `lock-held` and does not restore the previous `lock-reclaim.json`. The second process's record in the race above was timestamped before its new lock `startedAt`. | Publish `lock-reclaim.json` only after this attempt holds the new record. |
| A Windows boot-id fallback can make a live lock look rebooted. | `Get-NightlyBootId` (`:579`) returns registry `BootId` (`:585-588`) or, on any failure, `Win32_OperatingSystem.LastBootUpTime` in `o` format (`:592-593`). `Resolve-NightlyStaleLock` reclaims `dead-owner-rebooted` when the host matches and those strings differ, before the live-pid check (`:682-684`). A stubbed pair on this host returned `157` then `2026-01-02T03:04:05.0000000Z`. A planted live pid `424299` with boot id `157` and the same host was reclaimed: `Reason=reclaimed`, json reason `dead-owner-rebooted`, lock run id `thief-run`. | Return only the registry decimal string. An unreadable registry is an empty boot id. |
| Linux stores `parentPid` 0, so only the reboot branch reaches reclaim when a host id exists. | `Get-NightlyParentProcess` (`:499`) reads `Win32_Process` and returns id 0 on failure (`:512`). `New-NightlyLockRecord` stores that 0 (`:735-738`). `parentPid <= 0` refuses before same-boot and legacy reclaim (`:713`). The reboot branch does not look at the parent. This mirror has neither `/etc/machine-id` nor `/var/lib/dbus/machine-id`, so the reboot branch does not fire here without a host-id seam. The race sits inside `Enter-NightlyReclaimedLock`, which that branch calls. | Leave parent lookup unchanged. |

## Decisions

- **D-1 — Exclusive in-place rewrite.** In `Enter-NightlyReclaimedLock`, open the existing `run.lock` with `FileMode.Open`, `FileAccess.ReadWrite`, `FileShare.None`. Read it and require pid, runId, bootId, and hostId to match the snapshot (`Get-NightlyLockFieldPid` / `Get-NightlyLockFieldText`). Write the new record on that stream (`SetLength(0)`, write the ASCII JSON, `Flush`) and keep the stream in `$script:NightlyLockStream`. A second open on this host failed with `IOException` while the first handle was held. A failed open, a missing file, or a different identity does not delete. Re-read once: a readable live pid returns `live-owner`; anything else, including a share-denied read, returns `lock-held`.
  Rejected: re-read, then `Remove-Item`. The compare and the delete are still two steps, so the second process can accept the stale file and then unlink the first process's replacement.
  Rejected: rename `run.lock` to a unique name, then `CreateNew`. After the first process has created the replacement, the second process's rename of that path takes the live file.
  Rejected: a side claim file. A crashed claim holder needs a second stale-claim protocol.
- **D-2 — Publish the reclaim record after the write.** Call `Write-NightlyAtomicJson` for `<StateRoot>\lock-reclaim.json`, and emit `Write-NightlyRunLine`, only after the new bytes are flushed and this attempt still holds the stream. The published `newRunId` equals the lock's `runId`. If that publish throws, write the previous bytes back, flush, close, and return `lock-held`. A contender that lost the open or the identity check does not publish and does not restore someone else's file. `Exit-NightlyExclusiveLock` (`:914`) still does not delete `lock-reclaim.json`.
  Rejected: a unique JSON name per attempt as the only record. CARD-1386 readers use `lock-reclaim.json`.
- **D-3 — One Windows boot source.** Delete the `LastBootUpTime` fallback. Windows boot id is the registry DWORD `BootId` as an invariant decimal string, or empty when that read fails or is absent. Linux stays `/proc/sys/kernel/random/boot_id` only. An empty current boot id does not enter `dead-owner-rebooted`. A live pid then stays `live-owner` (`:685-697`). Keep the reboot-before-live-pid order. CARD-1386 recycled-pid reclaim depends on it.
  Rejected: store a source tag and refuse when the tags differ. Locks already on disk have no tag, and a counter compared with a timestamp would still look like a reboot.
- **D-4 — Leave the rest of the predicate.** Legacy, same-boot, foreign-host, parent-alive, missing-parent, live-owner, the hop path, and `native-run.lock` stay as they are. Do not teach Linux parent lookup in this card.
- **D-5 — Tests call the production functions.** New cases live in `scripts/test-nightly-run.ps1` and run in that process after the existing dot-source of `scripts/lib/nightly-common.ps1`. The race case starts two child `pwsh` processes. The child script stays in the fixture directory. Do not add the cases to the `C487_G0` or `C545_` loops (`scripts/test-nightly-run.ps1:705` and `:712`). `-Case` already dispatches one `Test-<Case>` function. C# class `NightlyReclaimRaceTests`, `[Category("Integration")]`, `[ParallelLimiter<ProcessSpawnLimit>]`, each method `ScriptHarness.RunHarnessCaseAsync("test-nightly-run.ps1", "C1389", ...)`. Assertion names start with `C1389 `. Scripts stay ASCII and use no PowerShell 7-only syntax (`&&`, ternary, null-conditional). `File.Open` and `FileShare` are already used in this file.
- **D-6 — One red witness, then one green method per behaviour.** Commit the three test methods first. Run only the witness below and require that assertion failure. A build failure, a skip, or zero tests is not that red. Then commit the production change and run all three methods. No Unit lane and no unfiltered `test-nightly-run.ps1` run.
- **D-7 — Master first.** `origin/rc/2026-10-20` at `aeaf16647` has this same `Enter-NightlyReclaimedLock` and the same `LastBootUpTime` fallback (`git diff` of `scripts/lib/nightly-common.ps1` against this master tip was empty). `origin/rc/2026-10-15` and `origin/rc/2026-10-18` do not contain the function. After the master land, backport this fix onto `rc/2026-10-20` only. This branch does not do that backport.
- **D-8 — No parked session.** Reclaim either returns or holds the lock. Nothing here waits for input.

## Slices

One Code task. Commit the tests, run the witness, commit the production change, run the three methods. Push after each commit.

| Slice | Files | Budget |
|---|---|---:|
| S1 | `scripts/lib/nightly-common.ps1` (reclaim open, publish-after-own, registry-only boot id). `scripts/test-nightly-run.ps1` (three `-Case` functions). `tests/Antiphon.Tests/Scripts/NightlyReclaimRaceTests.cs`. | 40m |

## Tests

The C# filter for each method is `/*/*/NightlyReclaimRaceTests/<Method>*`. `expectedRows` is the PASS count below.

- **Witness, and the green for the losing record.** `C1389_LoserReclaimRecord`, two rows:
  - `C1389 Record loser does not own the lock`
  - `C1389 Record winner newRunId remains`

  In-process. First `Enter-NightlyReclaimedLock` wins and holds `$script:NightlyLockStream`. Shadow `Remove-Item` so a later call on that lock path throws, and forward every other path to `Microsoft.PowerShell.Management\Remove-Item`. Second call uses the original stale snapshot and a different run id. The lock run id stays the winner. `lock-reclaim.json` `newRunId` stays the winner. The second result is `Ok=false`. Intended red on current master: `newRunId` is the second run id. The write at `:799` happens before `CreateNew`. Detail includes both run ids. Run this method alone before the production change.

- **Green for the Linux double reclaim.** `C1389_DoubleReclaim`, two rows:
  - `C1389 Race one attempt owns the lock`
  - `C1389 Race lock run id is that owner`

  Two child `pwsh` processes. Both receive a snapshot of the stale file taken before either mutates. Process A calls `Enter-NightlyReclaimedLock`, writes its result, and holds the stream. Process B starts after that result file exists and calls the same function with the same snapshot. Exactly one result has `Ok=true`. The lock run id is that process's run id. Intended red on current Linux, from the run on this host: both returned `Ok=true` and the file's run id was `B-run`. This method is not a second pre-fix witness run.

- **Green for one boot source.** `C1389_BootSourcePinned`, three rows:
  - `C1389 Boot registry failure keeps a single source`
  - `C1389 Boot live owner stays live-owner`
  - `C1389 Boot live owner writes no reclaim record`

  In-process. Do not set the `GetBootId` seam. Override `Test-NightlyWindows` to true and `Test-NightlyLinux` to false. First `Get-ItemProperty` returns `BootId` 157. Later calls throw. `Get-CimInstance` returns `LastBootUpTime` `2026-01-02T03:04:05Z`. A call counter on `Get-ItemProperty` must be at least 2; a zero count fails as `stub-not-hit`, which is not the product red. The second boot id is empty or `157`. Plant pid `424299`, boot id `157`, host id `host-current`, seam that pid alive, seam `GetHostId` = `host-current`, no `GetBootId`. `Enter-NightlyExclusiveLock` returns `Reason=live-owner`, the planted run id remains, and `lock-reclaim.json` is absent. Intended red on current master, from the stubbed run on this host: the second id was `2026-01-02T03:04:05.0000000Z` and the live lock was reclaimed with reason `dead-owner-rebooted`.

Do not assert the later fake test phase. Assert the function result, `run.lock`, and `lock-reclaim.json` only.

### Checkpoints

Command rows. The tool records `buildSource=notApplicable`. CP-1 alone, against the test-only commit, is the red witness: `C1389 Record winner newRunId remains` fails and the harness exit is 1. The green run is CP-1 through CP-6 together, against the production commit, all exit 0. No Unit lane. `Expect` is the green rule.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | n/a | loser-record | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C1389_LoserReclaimRecord` | witness | 2 passed, 0 failed, exit 0 | n/a | 4 |
| CP-2 | S1 | n/a | double-reclaim | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C1389_DoubleReclaim` | race | 2 passed, 0 failed, exit 0 | n/a | 6 |
| CP-3 | S1 | n/a | boot-source | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C1389_BootSourcePinned` | boot | 3 passed, 0 failed, exit 0 | n/a | 4 |
| CP-4 | S1 | n/a | dead-owner | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C1386_DeadOwnerReclaimed` | C1386 | 6 passed, 0 failed, exit 0 | n/a | 6 |
| CP-5 | S1 | n/a | live-owner | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C1386_LiveOwnerRefused` | C1386 | 4 passed, 0 failed, exit 0 | n/a | 4 |
| CP-6 | S1 | n/a | ambiguous-owner | `pwsh -NoProfile -File scripts/test-nightly-run.ps1 -Case C1386_AmbiguousOwnerRefused` | C1386 | 6 passed, 0 failed, exit 0 | n/a | 4 |
