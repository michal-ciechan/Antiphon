# CARD-1653 S1 Code report (task 9f89bb21)

Outcome: S1 implemented red-first; every design witness was red at the red commit for the designed reason and is green at the tested tip. Run A, Run B and the two brief-mandated neighbour classes are green. The land graph check is approved with an unchanged digest. check-evidence-diff is clean. merge-tree against origin/master and against rc/2026-11-01 is clean. The slice is master-only.

- Branch `feat/card-task-9f89bb21`, worktree `/work/worktrees/task-9f89bb21` (server2 runner mirror), Code task 9f89bb21 (landing owner).
- Base `faaf94b2d2172775acb701e5073d1e43b6204895` (plan + test design carried from feat/card-task-053fe121).
- Plan: `docs/superpowers/plans/2026-11-02-card-1653-pty-host-pipe-hardening.md`. Test design: `docs/superpowers/plans/2026-11-02-card-1653-S1-test-design.md`.

## Commits (all pushed)

| SHA | What |
|---|---|
| `befcf2916719ffd572ba6697d9e69c182d752300` | RED: PtyHostStrangerWorld extraction, Card1636 R-1 assertion change, Card1653RefusalRetainsHostTests, Card1653FreshLaunchIdentityTests (+ policy row, policyHash resealed 62f4825a -> 4d386182 via Get-NightlyPolicyHash; the base hash reproduced first), V-8 method, prefixes, compile shim |
| `6ceb7dd7ae78aec4da580a759c1e2ab513f421f1` | GREEN product: typed refusal, hold branch, G2 hello check, D-5 release |
| `a2d3e8f252ac1c54f579d4fb65e17170a9cc74ba` | test fixes found by the first green run (see Deviations) |
| `e7467c90034b3bf4c72d84cac6986caec7824cca` | new classes at a 10 s per-test timeout (design formula); **tested tip** |

## Product change (src/Antiphon.SessionRunner)

- New `PtyHostIdentityRefusedException(code)`, derived from InvalidOperationException, carrying the fixed code only. Constants: `pty_host_identity_mismatch` and `pty_host_identity_unproven`.
- `AdoptCoreAsync` throws the typed refusal. The log line is unchanged.
- The adoption sweep has a new `catch (PtyHostIdentityRefusedException)` ahead of the generic catch. It disposes the session (detach, no Shutdown) and registers `CreatePendingConflict(..., "pty_host_identity_unproven")`. It does not kill, does not register CreateAdoptedExited and does not delete the manifest. It then runs `hostAttempt.Complete(0, "refused")`. Every other failure keeps the old exit path (D-3).
- `CreateWorkspaceConflict` is now `CreatePendingConflict(manifest, settings, events, logger, reason)`. The two workspace callers pass `workspace_ownership_conflict`. The row also keeps the manifest host start and its manifest path.
- Non-custody `StartAsync` checks the hello right after `ConnectAsync` and before `LaunchAsync`. It requires the session id, a non-null instance and `WaitingForLaunch`. On a mismatch it logs `pty_host_identity_mismatch: launch refused for session {id}` and throws the typed refusal. The existing catches tear down the spawned host.
- `MarkVanishedIfDead` gains a D-5 release that applies only to rows with `Pending == pty_host_identity_unproven`. When the manifest host pid is not alive at its recorded start, the row goes to `CompletePendingAsExited("ProcessVanished")`, which publishes once and clears Pending, and the retained manifest is deleted. `CompletePendingAsExited` now returns bool; existing callers ignore it.
- Untouched, as the brief required: AgentTaskDispatcher/Service/ReplyService, delegate.ps1, CreateAgentTaskRequest, AgentTask.cs, migrations. No protocol change and no flag.

## Checkpoints (unedited CHECKPOINT lines)

```
== runA-red.wait
run: 20261102-200313-25b9   manifest: /work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-200313-25b9/manifest.resolved.yaml
CHECKPOINT CP-1 commit=befcf2916719ffd572ba6697d9e69c182d752300 build=ok filter=/*/Antiphon.SessionRunner.Tests/Card1653RefusalRetainsHostTests/* executed=7 passed=1 failed=6 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-200313-25b9/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=befcf2916719ffd572ba6697d9e69c182d752300 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=befcf2916719ffd572ba6697d9e69c182d752300 build=reused filter=/*/Antiphon.SessionRunner.Tests/Card1636AdoptionIdentityTests/* executed=6 passed=2 failed=4 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-200313-25b9/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=befcf2916719ffd572ba6697d9e69c182d752300 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=befcf2916719ffd572ba6697d9e69c182d752300 build=reused filter=/*/Antiphon.SessionRunner.Tests/Card1653FreshLaunchIdentityTests/* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-200313-25b9/rows/CP-3/run.trx slot=granted waited=30s dirty=0 source=befcf2916719ffd572ba6697d9e69c182d752300 sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=befcf2916719ffd572ba6697d9e69c182d752300 build=reused filter=/*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-200313-25b9/rows/CP-14/run.trx slot=granted waited=75s dirty=0 source=befcf2916719ffd572ba6697d9e69c182d752300 sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=befcf2916719ffd572ba6697d9e69c182d752300 build=reused filter=/*/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceRemovalTests/C1032_FenceStartupAdoptionOrders executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-200313-25b9/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=befcf2916719ffd572ba6697d9e69c182d752300 sourceState=clean buildSource=verified
verdict: RED exit=1
== runA-green.wait
run: 20261102-200824-4f7b   manifest: /work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-200824-4f7b/manifest.resolved.yaml
CHECKPOINT CP-1 commit=6ceb7dd7ae78aec4da580a759c1e2ab513f421f1 build=ok filter=/*/Antiphon.SessionRunner.Tests/Card1653RefusalRetainsHostTests/* executed=7 passed=4 failed=3 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-200824-4f7b/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=6ceb7dd7ae78aec4da580a759c1e2ab513f421f1 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=6ceb7dd7ae78aec4da580a759c1e2ab513f421f1 build=reused filter=/*/Antiphon.SessionRunner.Tests/Card1636AdoptionIdentityTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-200824-4f7b/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=6ceb7dd7ae78aec4da580a759c1e2ab513f421f1 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=6ceb7dd7ae78aec4da580a759c1e2ab513f421f1 build=reused filter=/*/Antiphon.SessionRunner.Tests/Card1653FreshLaunchIdentityTests/* executed=3 passed=1 failed=2 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-200824-4f7b/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=6ceb7dd7ae78aec4da580a759c1e2ab513f421f1 sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=6ceb7dd7ae78aec4da580a759c1e2ab513f421f1 build=reused filter=/*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-200824-4f7b/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=6ceb7dd7ae78aec4da580a759c1e2ab513f421f1 sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=6ceb7dd7ae78aec4da580a759c1e2ab513f421f1 build=reused filter=/*/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceRemovalTests/C1032_FenceStartupAdoptionOrders executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-200824-4f7b/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=6ceb7dd7ae78aec4da580a759c1e2ab513f421f1 sourceState=clean buildSource=verified
verdict: RED exit=1
== runA-green2.wait
run: 20261102-201626-3480   manifest: /work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-201626-3480/manifest.resolved.yaml
CHECKPOINT CP-1 commit=a2d3e8f252ac1c54f579d4fb65e17170a9cc74ba build=ok filter=/*/Antiphon.SessionRunner.Tests/Card1653RefusalRetainsHostTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-201626-3480/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=a2d3e8f252ac1c54f579d4fb65e17170a9cc74ba sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=a2d3e8f252ac1c54f579d4fb65e17170a9cc74ba build=reused filter=/*/Antiphon.SessionRunner.Tests/Card1636AdoptionIdentityTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-201626-3480/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=a2d3e8f252ac1c54f579d4fb65e17170a9cc74ba sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=a2d3e8f252ac1c54f579d4fb65e17170a9cc74ba build=reused filter=/*/Antiphon.SessionRunner.Tests/Card1653FreshLaunchIdentityTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-201626-3480/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=a2d3e8f252ac1c54f579d4fb65e17170a9cc74ba sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=a2d3e8f252ac1c54f579d4fb65e17170a9cc74ba build=reused filter=/*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-201626-3480/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=a2d3e8f252ac1c54f579d4fb65e17170a9cc74ba sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=a2d3e8f252ac1c54f579d4fb65e17170a9cc74ba build=reused filter=/*/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceRemovalTests/C1032_FenceStartupAdoptionOrders executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-201626-3480/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=a2d3e8f252ac1c54f579d4fb65e17170a9cc74ba sourceState=clean buildSource=verified
verdict: GREEN exit=0
== runB.wait
run: 20261102-201921-0dbe   manifest: /work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-201921-0dbe/manifest.resolved.yaml
CHECKPOINT CP-16 commit=a2d3e8f252ac1c54f579d4fb65e17170a9cc74ba build=ok filter=/*/Antiphon.Tests.Application/SessionReconciliationServiceTests/Pty_identity_unproven_pending_row_is_held_and_raises_the_pending_incident executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-201921-0dbe/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=a2d3e8f252ac1c54f579d4fb65e17170a9cc74ba sourceState=clean buildSource=verified
verdict: GREEN exit=0
== runA3.wait
run: 20261102-202646-49ab   manifest: /work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-202646-49ab/manifest.resolved.yaml
CHECKPOINT CP-1 commit=e7467c90034b3bf4c72d84cac6986caec7824cca build=ok filter=/*/Antiphon.SessionRunner.Tests/Card1653RefusalRetainsHostTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-202646-49ab/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=e7467c90034b3bf4c72d84cac6986caec7824cca sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=e7467c90034b3bf4c72d84cac6986caec7824cca build=reused filter=/*/Antiphon.SessionRunner.Tests/Card1636AdoptionIdentityTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-202646-49ab/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=e7467c90034b3bf4c72d84cac6986caec7824cca sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=e7467c90034b3bf4c72d84cac6986caec7824cca build=reused filter=/*/Antiphon.SessionRunner.Tests/Card1653FreshLaunchIdentityTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-202646-49ab/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=e7467c90034b3bf4c72d84cac6986caec7824cca sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=e7467c90034b3bf4c72d84cac6986caec7824cca build=reused filter=/*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-202646-49ab/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=e7467c90034b3bf4c72d84cac6986caec7824cca sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=e7467c90034b3bf4c72d84cac6986caec7824cca build=reused filter=/*/Antiphon.SessionRunner.Tests/CompletedCardWorkspaceRemovalTests/C1032_FenceStartupAdoptionOrders executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-202646-49ab/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=e7467c90034b3bf4c72d84cac6986caec7824cca sourceState=clean buildSource=verified
verdict: GREEN exit=0
== runC.wait
run: 20261102-202912-8ddf   manifest: /work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-202912-8ddf/manifest.resolved.yaml
CHECKPOINT CP-17 commit=e7467c90034b3bf4c72d84cac6986caec7824cca build=ok filter=/*/Antiphon.SessionRunner.Tests/Card1619LinuxStopIntentTests/* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-202912-8ddf/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=e7467c90034b3bf4c72d84cac6986caec7824cca sourceState=clean buildSource=verified
CHECKPOINT CP-18 commit=e7467c90034b3bf4c72d84cac6986caec7824cca build=reused filter=/*/Antiphon.SessionRunner.Tests/HerdrAdoptionSweepTests/* executed=22 passed=22 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-202912-8ddf/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=e7467c90034b3bf4c72d84cac6986caec7824cca sourceState=clean buildSource=verified
verdict: GREEN exit=0
```

- Red run `20261102-200313-25b9` @ befcf2916:
  - CP-1 7 executed, 6 failed. V-1 x3 failed at assertion 3, "stand-in killed". V-1b failed at "alive". V-3 failed at `Starting` (base gives Exited). V-3b failed on its identity half. The V-5 control passed.
  - CP-2: 4 refusal cases red at `Status Starting` (base gives Exited).
  - CP-3: 3/3 red with `["LaunchMessage"]` sent.
  - CP-14 and CP-15 green.
- Green runs:
  - `20261102-200824-4f7b` @ 6ceb7dd7a was RED because of test defects only (see Deviations).
  - `20261102-201626-3480` @ a2d3e8f25: Run A 20/20.
  - `20261102-201921-0dbe` @ a2d3e8f25: Run B CP-16 1/1.
  - `20261102-202646-49ab` @ e7467c900: Run A 20/20.
  - `20261102-202912-8ddf` @ e7467c900: CP-17/CP-18 neighbours.
- Every row reported slot=granted. Only the CP-3/CP-14 rows of the red run waited, 30 s and 75 s. CP-16's build took 280 s.
- Unlisted builds and runs, all through build-slot:
  - Two native compile checks of SessionRunner.Tests before the red commit (the first failed on a missing `using`).
  - The checkpoint tool build.
  - A native CP-3 debug build plus run, to diagnose the timeouts (3/3 red, "Process ... is not running").
  - A native build plus run of `Card1653*` with the fixed fixture: 10/10 green.
  - A V-6 red re-proof with the fixed fixture: G2 check temporarily disabled, native build, run 3/3 red at "should be empty but had LaunchMessage", source restored byte-identical before any commit.
  - The LandGraphAudit Release build (native) for `-Check -ToolPath`.
- CP-17/CP-18 come from the ignored amendment table `.antiphon/c1653-s1-neighbours.md` (brief: Card1619LinuxStopIntentTests alone, HerdrAdoptionSweepTests).

## Full affected classes (Final round; ignored amendment `.antiphon/c1653-s1-affected.md`, run `20261102-203435-55fc` @ e7467c900, one build)

```
== runD.wait (affected classes, .antiphon/c1653-s1-affected.md)
run: 20261102-203435-55fc   manifest: /work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-203435-55fc/manifest.resolved.yaml
CHECKPOINT CP-19 commit=e7467c90034b3bf4c72d84cac6986caec7824cca build=ok filter=/*/Antiphon.SessionRunner.Tests/(CompletedCardWorkspaceRemovalTests*)|(LegacyWorkspaceRemovalGateTests*)/* executed=110 passed=110 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-203435-55fc/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=e7467c90034b3bf4c72d84cac6986caec7824cca sourceState=clean buildSource=verified
CHECKPOINT CP-20 commit=e7467c90034b3bf4c72d84cac6986caec7824cca build=reused filter=/*/Antiphon.SessionRunner.Tests/(PtyHostAdoptionTests*)|(SessionLivenessTests*)|(RunnerSessionGenerationTests*)|(GrokRulesAdoptionTests*)/* executed=19 passed=12 failed=7 skipped=2 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-203435-55fc/rows/CP-20/run.trx slot=granted waited=0s dirty=0 source=e7467c90034b3bf4c72d84cac6986caec7824cca sourceState=clean buildSource=verified
CHECKPOINT CP-21 commit=e7467c90034b3bf4c72d84cac6986caec7824cca build=reused filter=/*/Antiphon.SessionRunner.Tests/(Card1619RunnerExitTests*)|(RunnerAbsenceEvidenceRuntimeTests*)|(RunnerStartupDiagnosticsTests*)|(RunnerCapacityCountTests*)/* executed=40 passed=40 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-203435-55fc/rows/CP-21/run.trx slot=granted waited=0s dirty=0 source=e7467c90034b3bf4c72d84cac6986caec7824cca sourceState=clean buildSource=verified
CHECKPOINT CP-22 commit=e7467c90034b3bf4c72d84cac6986caec7824cca build=reused filter=/*/Antiphon.SessionRunner.Tests/RunnerCustodyTests/* executed=0 passed=0 failed=0 skipped=23 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-203435-55fc/rows/CP-22/run.trx slot=granted waited=0s dirty=0 source=e7467c90034b3bf4c72d84cac6986caec7824cca sourceState=clean buildSource=verified reason=zero-tests-executed
CHECKPOINT CP-23 commit=e7467c90034b3bf4c72d84cac6986caec7824cca build=reused filter=/*/Antiphon.SessionRunner.Tests/TranscriptAdoptionSafetyTests/* executed=45 passed=45 failed=0 skipped=0 trx=/work/worktrees/task-9f89bb21/.antiphon/checkpoints/20261102-203435-55fc/rows/CP-23/run.trx slot=granted waited=0s dirty=0 source=e7467c90034b3bf4c72d84cac6986caec7824cca sourceState=clean buildSource=verified
verdict: RED exit=1
```

- CP-19 (CompletedCardWorkspaceRemovalTests full, including its S5 partial, plus LegacyWorkspaceRemovalGateTests): 110/110 green.
- CP-21: 40/40 green. CP-23: 45/45 green.
- CP-20 is RED, 7 of 19. All 7 are **inherited**. They are Linux-red by construction: `cmd.exe`, and `Environment.SystemDirectory` is empty on Linux. The failing tests:
  - PtyHostAdoptionTests: KillAll_kills_every_live_session_and_their_hosts, Running_session_survives_runner_restart_with_buffer_and_input_intact, Exit_while_runner_down_is_collected_on_adoption_with_the_real_exit_code, Dead_host_is_registered_as_exited_ProcessVanished.
  - RunnerSessionGenerationTests: C502_V25_real_runner_exe_advertises_the_capability_echoes_and_refuses_a_mismatched_kill, C502_V27_fast_exit_before_Start_returns_carries_the_generation.
  - SessionLivenessTests: System_probe_reports_an_exited_process_dead.
- Proof that the CP-20 reds are inherited: a detached worktree at base faaf94b2d, a native build through build-slot, and the same filter. It gave total 21, failed 7, succeeded 12, skipped 2, with the **same 7 names**. The worktree was removed afterwards.
- CP-22 RunnerCustodyTests: 23 skipped, 0 executed. The class throws `SkipTestException("Requires Windows and the shipped modern ConPTY backend")` on Linux, so custody launch and adoption are not exercised on server2. S1 leaves the custody paths unchanged (`VerificationBinding is null` guards both new checks).
- Not run: the whole Unit lane. It is about 70 min wall on server2 in three sharded lanes, with roughly 145 known Linux-inherited SessionRunner reds. The brief's closed list plus the classes above cover every class that exercises the changed code. Review/the caller may require it on the windows-native or nightly lane.

## Timeouts (re-derived)

- First green per-test TRX durations: max 2.78 s (V-1 other-instance). Real_host was 2.70 s. All fake bodies were 0.12-1.67 s.
- Formula max(10 s, 2x p95, largest internal bound 5 s + 5 s) = **10 s** for Card1653RefusalRetainsHostTests and Card1653FreshLaunchIdentityTests (applied in e7467c900, green).
- Card1636AdoptionIdentityTests stays at the design's **15 s**. With the Real_host test's default 15 s connect bound, the formula reads 20 s; I did not raise it.

## Deviations from the test design (Review please judge)

1. **V-6 fixture.** `ShadowCopyStore` copies only the host deps closure. The extensionless `fakePtyHost` was dropped, so the background exec failed. At the red commit the runtime won the race against the dying subshell; on the green run it lost (`ArgumentException` at the host start-time read, before connect). The design's probe did not go through the shadow copy. Fix: the copy is now named `fakePtyHost.exe` and listed in the fake deps.json. The test now surfaces the StartAsync exception if the peer never sees the close. Re-proved red without G2 (above).
2. **V-1 assertion 7.** `ExitReason` null is impossible for any live row: the DTO default is `"Unknown"`, the same as the workspace pending row. The test now asserts `"Unknown"`. Base gives `"ProcessVanished"`, so the assertion still discriminates. Status `Starting`, zero SessionExited events and Pending carry the not-exited claim.
3. **Fixture rename.** `RecordingLogger` moved to `PtyHostStrangerWorld.RecordingLogger` (Q-3: no other user).

## Other checks

- `audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Release/net10.0/Antiphon.LandGraphAudit.dll`: `approved`, `count=69`, `digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE` (unchanged; -Update not run).
- `check-evidence-diff.ps1 -Repository . -BaseRef faaf94b2d -HeadRef HEAD`: commits=4 entries=0 violations=0.
- `git merge-tree --write-tree`:
  - origin/master `4bd1473f0`: clean, tree 66369bd8c. Master's 11 new commits touch none of this slice's files.
  - origin/rc/2026-11-01 `10a826b9d`: clean, tree df6520fec. It is textually clean, but rc lacks CARD-1636, so the slice is master-only.
- Census: no unlanded origin branch was re-censused beyond master's range (the plan's GT-13 census stands; master's new commits are CARD-1610 only).

## Pending for Mutation (none run here)

- PC-1..PC-32 all pending (method-scoped SourceLanding Mutation).
- Notes for Mutation:
  - **PC-22 as designed will likely not go red.** `SessionRunnerRuntime.StartAsync`'s own catch also calls `session.TearDownFailedLaunch()` before dispose (`:745-750`). Skipping the session-level teardown leaves G-22 enforced by the runtime-level one. Break both, or record a missing-control finding.
  - PC-28: the exact red method name comes from the witness output at the land SHA (design note).
  - V-5 and V-8 are green at base by design (controls). PC-6 and PC-26/PC-27 own their red.

## Not run / out of scope

- The Windows-native run of the portable classes (design: they ride S2's CP-8).
- Whole Unit lane: see the Full affected classes section.
- No AppHost or restart.
