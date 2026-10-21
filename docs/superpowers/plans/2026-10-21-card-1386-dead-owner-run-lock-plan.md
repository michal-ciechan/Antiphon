# CARD-1386: reclaim a dead-owner qualification run.lock

Master-only. Read against `326c91c7759522751213bf2c1bd24a1a9a1cb66e`. Card read from the Antiphon board on 2026-10-21. Verification design is in this plan; next is code.

The defect is still present. One narrow PowerShell call, with no seams, planted a JSON `run.lock` whose owner pid `99999991` and parent pid `99999992` were both absent. `Enter-NightlyExclusiveLock` returned `Ok=False`, `Reason=lock-held`, `OwnsLock=False`, and the file still contained `dead-owner-run`. The same call with the current process as owner returned `Reason=live-owner` and left that file in place.

## Ground truth

Line numbers are at that source SHA.

| Card assumption | What the code does | Smallest change |
|---|---|---|
| A qualification attempt leaves `state-<task>\run.lock` after its owner and parent have exited. | `Enter-NightlyExclusiveLock` (`scripts/lib/nightly-common.ps1:563`) writes `pid`, `processStartedAt`, `startedAt`, `runId`, `parentPid`, `parentStartedAt` (`:604-611`). `Exit-NightlyExclusiveLock` (`:644`) deletes the file only when this process still owns it and the recorded pid and run id match. A dead owner never reaches that delete. `27e994927` only points the G026 retry at a fresh state root (`scripts/test-nightly-run.ps1:381-382`) and says the lock-file lifecycle is separate. | Reclaim on the next fresh acquire. Do not rely on the exited process cleaning up. |
| A fresh attempt must reclaim that dead lock and record why. | On `CreateNew` failure, a live owner pid returns `live-owner` (`:638`). Any other existing file returns `lock-held` (`:640`) and is not removed. `Invoke-AntiphonNightlyRunCore` (`scripts/lib/nightly-run-impl.ps1:288-295`) turns that into exit 3, phase `lock`. There is no boot id, host id, or reclaim record. | Prove the predicate below, write `lock-reclaim.json`, then replace the file. |
| A live owner must still refuse. | `Test-C487_G007` (`scripts/test-nightly-run.ps1:139`) plants this process's pid at ages 239 and 241 minutes and requires `live-owner` with the planted run id kept. Age is not a reclaim. | Leave that refusal and its reason token. Do not run G007 in this card's checkpoints. |
| An owner that cannot be proved dead stays fail-closed. | A null or unreadable record, pid `0`, and a dead owner whose parent is still alive all fall through to `lock-held`. CARD-0614 already flags unreadable records as fail-closed. | Keep those refusals. Do not delete the file. |
| The shared native lock might be the same defect. | It is a different file. `Get-ReleaseGateNativeLockPath` (`scripts/lib/release-gate.ps1:310`) is `<CoordinationRoot>\native-run.lock`. `Enter-ReleaseGateNativeLock` (`:385`) has no parent pid. A dead owner pid returns `stale-shared-lock` (`:444`) and the run records `deferred-busy` (`nightly-run-impl.ps1:310-335`). The 2026-10-19 re-gate left the candidate `state\run.lock` (pids 119604 and 283472) and recorded the shared native lock as clear (`docs/investigations/2026-10-19-rc-regate.md:186`). The per-task lock is checked first, so it alone blocks the fresh attempt. | Do not change the native lock in this card. |

## Decisions

- **D-1 — Reclaim only the per-task state lock.** The file is `<StateRoot>\run.lock` from `Get-NightlyLockPath`. The qualification state root is the task directory the run was given; this plan does not hard-code that directory. Reject reclaiming `native-run.lock`. Its stale refusal is the existing deferred-busy signal, and the incident's shared lock was already clear.
- **D-2 — Identity, not age.** Never reclaim because a timestamp is old. G007 stays true. Reject the CARD-0124 four-hour replacement.
- **D-3 — Predicate.** Evaluate it only on the fresh `CreateNew` failure path. The continue/hop path (`:574-601`) is unchanged and must not reclaim. Order:

  1. Unreadable or null record: refuse `lock-held`.
  2. Proven reboot: recorded `hostId` and current host id both non-empty and equal, recorded `bootId` and current boot id both non-empty and different. Reclaim with `dead-owner-rebooted` even when the recorded pid is now alive.
  3. Owner pid alive: keep today's self-reacquire (`same pid` and `same run id`) and otherwise refuse `live-owner`. A different `processStartedAt` on a live pid is still `live-owner`. Do not steal it.
  4. Owner pid `<= 0`: refuse `lock-held`.
  5. Both host ids non-empty and different, or the record has a host id and the current host id is empty: refuse `lock-held`.
  6. The record has a boot id and the current boot id is empty: refuse `lock-held`.
  7. Parent pid `<= 0`, or the parent pid is alive: refuse `lock-held`.
  8. Owner and parent both absent (both pids `> 0` and not alive):
     - both boot ids non-empty and equal, and both host ids non-empty and equal: `dead-owner-same-boot`;
     - recorded boot id and host id both empty: `dead-owner-legacy`;
     - otherwise refuse `lock-held`.

  Windows `BootId` is a small integer and can collide across machines, so same-boot and reboot both require a matching host id. Legacy records have neither id. They are the locks already on disk (the re-gate file and the narrow run above) and reclaim only when both pids are absent. A legacy file cannot prove host; `StateRoot` is local to the attempt, not a shared network lock. New records store both ids.
- **D-4 — Record the reason before deleting.** Write `<StateRoot>\lock-reclaim.json` with `Write-NightlyAtomicJson` before removing `run.lock`. Fields: `reason`, `at`, `previousPid`, `previousParentPid`, `previousRunId`, `previousProcessStartedAt`, `previousBootId`, `previousHostId`, `newRunId`, `bootId`, `hostId`. Empty string for an absent previous id. `Exit-NightlyExclusiveLock` must not delete this file. If the write fails, do not delete `run.lock`; return `lock-held`. Then delete and `CreateNew` once. On success return `Ok=true`, `Reason=reclaimed`, `OwnsLock=true`. If the retry loses the race, re-read once: live pid returns `live-owner`; anything else returns `lock-held` without a second delete. Also emit `Write-NightlyRunLine` with the reason token and previous run id. The JSON file is the durable record; the line is not.
- **D-5 — Readers.** `Get-NightlyBootId` and `Get-NightlyHostId` honor seams `GetBootId` and `GetHostId` when set. Otherwise Windows reads registry DWORD `HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters` value `BootId` as a decimal string, then `Win32_OperatingSystem.LastBootUpTime` in invariant UTC `o`. Linux reads `/proc/sys/kernel/random/boot_id`. Host id is Windows `HKLM:\SOFTWARE\Microsoft\Cryptography\MachineGuid`, else Linux `/etc/machine-id` then `/var/lib/dbus/machine-id`. Unreadable means empty. Do not substitute the machine name. This mirror has a readable boot id file and no `machine-id`; empty host id correctly fail-closes the reboot arm and does not block legacy reclaim. Scripts stay ASCII and parseable on Windows PowerShell 5.1. Use `RuntimeInformation.IsOSPlatform` rather than `$IsWindows`.
- **D-6 — Tests override liveness.** `Write-C487Seams` (`scripts/lib/c487-harness.ps1:94-97`) treats every positive pid as alive. The new cases must overwrite their own seam file after `New-RunFx`. Do not change that default, or existing cases start treating dead pids as live. Do not add the new cases to the unfiltered `C487_G0` / `C545_` loop (`scripts/test-nightly-run.ps1:486-493`); `-Case` already runs one function and passes `ExpectedRows 0`.
- **D-7 — Placement.** `GET /api/runner-defaults` returned revision 2. `GET /api/session-runners` returned an eligible Windows runner, an eligible Linux runner, and one draining runner that is not accepting work. Re-read at dispatch. These checkpoints use seams, so they need no OS and no host pin. Omit `-Runner` and `-Platform`.
- **D-8 — Master first, then an rc backport.** The same `lock-held` return is on `origin/rc/2026-10-20` at `5acbd708d640c9d457c272ec4fb3801122e806fe` (`nightly-common.ps1:640`) and on `origin/rc/2026-10-18` at `81cfb267a0af5c0ddb65e8b238af355067bee9cc` (`:634`). This branch does not backport. After the master land, the caller schedules the rc line separately.

## Slices

One Code task. Commit the three tests first, run CP-1, and require the dead-owner method to fail for the reason in V-1. Then commit the production change and run CP-1 through CP-3. Push after each commit.

| Slice | Files | Budget |
|---|---|---:|
| S1 | `scripts/lib/nightly-common.ps1` (readers, predicate, reclaim record, new lock fields). `scripts/test-nightly-run.ps1` (three `-Case` functions only). `tests/Antiphon.Tests/Scripts/NightlyDeadOwnerLockTests.cs`. | 50m |

No session waits for input. The run either acquires, reclaims, or returns; it does not park a seat.

## Verification design

Operator scope is one red witness and one narrow green per behaviour. No Unit lane, no namespace filter, no unfiltered `test-nightly-run.ps1` run.

The C# class is `NightlyDeadOwnerLockTests`, `[Category("Integration")]`, `[ParallelLimiter<ProcessSpawnLimit>]`. Each method calls `ScriptHarness.RunHarnessCaseAsync("test-nightly-run.ps1", "C1386", ...)`. Assertion names below are the `PASS` names. `expectedRows` equals that count. The harness prefix match requires each name to start with `C1386 `.

- **V-1 / dead owner (the red witness).** `C1386_DeadOwnerReclaimed`, six rows:
  - `C1386 DeadOwner legacy leaves lock phase`
  - `C1386 DeadOwner legacy records dead-owner-legacy`
  - `C1386 DeadOwner legacy replaces run id`
  - `C1386 DeadOwner same-boot records dead-owner-same-boot`
  - `C1386 DeadOwner reboot reclaims recycled pid`
  - `C1386 DeadOwner reboot records dead-owner-rebooted`

  Legacy plant: JSON pid and parentPid both positive, seam reports both dead, no `bootId` or `hostId` on the file. Same-boot plant: both ids match the seam and both pids are dead. Reboot plant: same host id, different boot id, seam reports the owner pid alive. Each expects phase not `lock`, an empty refusal, `lock-reclaim.json` `reason` equal to the token, and the planted run id gone from `run.lock`. Intended red on current master: legacy and same-boot stay `lock-held` with the planted run id and no reclaim file; reboot stays `live-owner`. A build failure, a skip, or zero tests is not this red.
- **V-2 / live owner.** `C1386_LiveOwnerRefused`, four rows:
  - `C1386 LiveOwner refusal is live-owner`
  - `C1386 LiveOwner keeps planted run id`
  - `C1386 LiveOwner writes no reclaim record`
  - `C1386 LiveOwner start mismatch stays live-owner`

  Same boot and host. First plant uses a pid the seam reports alive. Second plant is also alive but `processStartedAt` is `2001-01-01T00:00:00.0000000Z`. Both refuse `live-owner`, keep the planted run id, and write no `lock-reclaim.json`. This is already green on current master. It guards a reclaim that steals a live pid.
- **V-3 / ambiguous owner.** `C1386_AmbiguousOwnerRefused`, six rows:
  - `C1386 Ambiguous parent-alive refuses lock-held`
  - `C1386 Ambiguous parent-alive keeps the lock`
  - `C1386 Ambiguous foreign-host refuses lock-held`
  - `C1386 Ambiguous foreign-host keeps the lock`
  - `C1386 Ambiguous missing-parent refuses lock-held`
  - `C1386 Ambiguous missing-parent keeps the lock`

  All three plants have a dead owner pid. Parent-alive uses a live parent and matching boot and host. Foreign-host uses a dead parent, matching boot, and a different host id. Missing-parent omits `parentPid` (JSON, so it is absent) with matching boot and host. Each refuses `lock-held`, keeps the planted run id, and writes no reclaim file. Already green on current master. It goes red if a fix reclaims any of these.

`Invoke-FxRun` is the fresh attempt. Do not assert that the later fake test phase exits 0. Assert lock phase, refusal, `run.lock`, and `lock-reclaim.json` only.

### Checkpoints

The checkpoint tool adds `UseAppHost=false` on Linux; do not put it in this table. One build. CP-2 and CP-3 reuse it. Run `dotnet run --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-21-card-1386-dead-owner-run-lock-plan.md --rows CP-n --expected-source-sha <full-sha>`. Bootstrap that tool build through `scripts/build-slot.ps1` only when it is not already built; do not wrap `run-checkpoint.ps1` or the checkpoint run in a second slot. The red CP-1 invocation is the witness run against the test-only commit. The green invocation of all three rows is the post-fix run. `Expect` is the green rule.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1386/` | dead-owner | `/*/*/NightlyDeadOwnerLockTests/C1386_DeadOwnerReclaimed*` | V-1 | all listed, 0 failed/skipped | 1 | 15 |
| CP-2 | S1 | CP-1 | live-owner | `/*/*/NightlyDeadOwnerLockTests/C1386_LiveOwnerRefused*` | V-2 | all listed, 0 failed/skipped | 1 | 4 |
| CP-3 | S1 | CP-1 | ambiguous-owner | `/*/*/NightlyDeadOwnerLockTests/C1386_AmbiguousOwnerRefused*` | V-3 | all listed, 0 failed/skipped | 1 | 4 |
