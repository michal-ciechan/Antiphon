# Review b6b7abdc: CARD-1653 S1, round 1 (Final profile, ran as Interim)

Verdict: **clean, no defects.** Six advisories, none blocking.

- Subject: Code task 9f89bb21, branch `feat/card-task-9f89bb21`.
- Reviewed tip `3ab6fc3840693e96b1a7a11ad24324dc3c5435ee`. Its code tree is the tested `e7467c90034b3bf4c72d84cac6986caec7824cca`; the tip adds only the report.
- Range `faaf94b2d2172775acb701e5073d1e43b6204895..3ab6fc3840693e96b1a7a11ad24324dc3c5435ee`.
- Ordinary scope ran as **Interim**. The whole Unit lane was not run. On server2 it measured about 2 h 15 min wall (memory: unit-lane-shard-plan-server2), which does not fit this round's 90-minute budget, and load average was 42 on 24 cores. Antiphon.Tests does compile against SessionRunnerRuntime, so the lane is not vacuous for this change.

## Checkpoints (checkpoint tool, all at source 3ab6fc384, sourceState=clean, buildSource=verified, `validate` = SOURCE VALID)

| Run | Rows | Result |
|---|---|---|
| 20261102-205003-dc7d (S1 test-design plan, Run A) | CP-1 7/7, CP-2 6/6, CP-3 3/3, CP-14 3/3, CP-15 1/1 | GREEN 20/20 |
| 20261102-205249-2a7b (Run B) | CP-16 1/1 | GREEN |
| 20261102-210156-33da (ignored amendment `.antiphon/rv-b6b7-neighbours.md`) | CP-17 Card1619LinuxStopIntentTests 18/18; CP-18 HerdrAdoptionSweepTests 22/22; CP-19 CompletedCardWorkspaceRemoval+LegacyWorkspaceRemovalGate 110/110; CP-21 Card1619RunnerExit+RunnerAbsenceEvidenceRuntime+RunnerStartupDiagnostics+RunnerCapacityCount 40/40; CP-23 TranscriptAdoptionSafetyTests 45/45 | GREEN 235/235 |

Not rerun:

- Worker CP-20 (PtyHostAdoption/SessionLiveness/RunnerSessionGeneration/GrokRulesAdoption). The worker proved its 7 reds inherited at base, Linux-only by construction.
- Worker CP-22 RunnerCustodyTests. It runs 0 on Linux (Windows-only skip).
- The Windows-native lane. The design hands it to S2's CP-8.

## Unlisted runs (native output, each through build-slot, one lease per build or run)

- **Builds:** the checkpoint tool (needed to drive the runs); LandGraphAudit Release (needed for `-Check -ToolPath`); SessionRunner.Tests native, once plus once per mutant (needed for the brief's V-6 x5 and the mutants).
- **V-6 determinism:** `Card1653FreshLaunchIdentityTests/*` alone 5 times gave 3/3 green each time (15/15). The `fakePtyHost.exe` plus deps.json fix is deterministic.
- **Mutants:** each run one at a time, then restored with `git checkout`; the tree was clean afterwards.
  - M-A: the G2 hello check disabled (`&& _sessionId == Guid.Empty`). V-6 went **RED 3/3**: "should be empty but had ["LaunchMessage"]".
  - M-B: the identity catch disabled (`when (_settings is null)`), so the refusal falls back to the kill/Exited path. **RED 9/13**: V-1 x3 fail at "StandIn.WaitForExit(500) should be False" (the stand-in was killed); V-1b and V-3 fail; Card1636 refusal x4 fail at Status "Starting". V-5, V-3b and the 2 accepting Card1636 tests stay green, as expected.
  - M-C1: dropped `if (_status == "Exited") return false;` in CompletePendingAsExited. **Survived 7/7.** The second sweep never reaches that guard, because the release cleared `_pendingReason` and ReleaseUnprovenHostIfDead returns at the reason check. Once-only is therefore guarded twice for sequential sweeps; C1 only matters in a concurrent race.
  - M-C2: C1 plus dropping `_pendingReason = null`. V-3 went **RED** at "dto.Pending should be null but was pty_host_identity_unproven". The design's PC-14 (an extra publish) remains the count witness for Mutation.

## Other gates

- Land graph `-Check`: approved, count=69, digest D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE (unchanged; -Update not run).
- `check-evidence-diff -BaseRef faaf94b2d -HeadRef HEAD`: commits=5 entries=1 violations=0.
- `merge-tree --write-tree` against origin/master 18bc8a191: clean, tree e987ff463.
- `merge-tree --write-tree` against origin/rc/2026-11-01 10a826b9d: textually clean, tree 59dd5dc36. The slice is master-only, because rc lacks CARD-1636.

## Trace of the brief's priorities

1. **No kill of a refused host.**
   - Identity refusal: the new catch (`SessionRunnerRuntime.cs:2120-2132`) only detaches and registers the held row; no kill, no delete, no CreateAdoptedExited.
   - Held row: KillAsync/KillAllAsync return early (no sidecar, no herdr child, no client; `:4394-4440`). Runtime and session DisposeAsync are detach-only. The vanish sweep only probes. A relaunch while held is refused as "already running" (`:674-701`, HasExited false) before any effect.
   - Fresh-launch refusal: both the session catch (`:3527-3533`) and the runtime catch (`:760`) call TearDownFailedLaunch, then KillHostIfStillOurs. Both are keyed to `_hostPid` from our own launcher and `_hostStartTimeUtc` read right after spawn, plus the PtyHost name check. They never target whoever answered on the pipe; the in-process squatter in V-6 survives. The second teardown is the same key and is idempotent, so it is not the dangerous one.
   - Residual kill paths: see A-1 and A-2.
2. **Nothing sensitive reaches the peer.**
   - The G2 check runs before LaunchAsync; M-A shows the Launch would otherwise go out with the canary env.
   - Adoption sends nothing past hello; V-1 asserts this after PeerClosed.
   - Log lines are fixed tokens plus the session id. The exception message is the fixed code.
3. **Hold and release.**
   - The release is evidence-only, per plan §9 and F-S1-2: host pid dead at the manifest start, a restart that adopts a proving host, or an operator killing the verified pid. KillAsync on the row is a documented no-op.
   - The server incident carries FailureReason=pty_host_identity_unproven and SessionId (V-8). The host pid is on the runner DTO (`GET /sessions`).
   - Pid reuse: the release uses SystemProcessLivenessProbe with a 2-minute start tolerance. A pid recycled within 2 minutes keeps the row held (fail-safe); a later recycle correctly counts as dead. The release kills nothing.
   - SessionExited is published once: CompletePendingAsExited is gated, and the pending clear stops later sweeps. The manifest is deleted only after the dead check.
4. **Test changes.**
   - Card1636 AssertRefusedAsync is not weakened. It keeps the adoption count, nothing past hello, the token and no stranger ids, and adds HostPid, stand-in alive and manifest kept.
   - Deviation (a) is deterministic (above).
   - Deviation (b) is confirmed: `_exitReason` defaults to `PtyExitReason.Unknown` (`:2713`), and base CreateAdoptedExited gives ProcessVanished.
   - Deviation (c): the classes ran in 6-10 s total at 10 s/15 s per-test timeouts, which is fine.

## Advisories (no Review rerun needed)

- **A-1 (hardening).** After a held row is released, a relaunch of the session calls `EnsureExitedHostGoneAsync` → `KillHostIfStillOurs` on `_hostPid` (`:4695-4775`).
  - CreatePendingConflict sets `_retainedHostStartTimeUtc` but leaves `_hostStartTimeUtc` null. The Linux start-time guard at `:4764` therefore short-circuits, and a recycled pid now held by another session's `Antiphon.PtyHos*` host would be killed.
  - server2 `pid_max` is 32768.
  - This is the same exposure as base CreateAdoptedExited rows, including the base refusal row, so S1 does not widen it.
  - One-line fix: set `_hostStartTimeUtc = manifest.HostStartTimeUtc` in CreatePendingConflict, which makes `_retainedHostStartTimeUtc` redundant.
- **A-2 (plan D-3 residual, S2-owned).** A host refused and held now can still be killed on a later runner restart if re-adoption then fails for a non-identity reason. Examples: the connect times out after a squatter left an orphaned path, or a squatter replays the world-readable HostInstanceId and fails at Status. The generic catch then runs `KillPidBestEffort(manifest.HostPid)` (`:2145`), which has no name check for the Host target. The plan keeps this deliberately (D-3, V-5); S2 must close it.
- **A-3 (Mutation note).** PC-22 as designed (skip the session-level teardown) stays green, because the runtime catch (`:760`) tears down the same spawned pid. Mutation must break both teardowns or record PC-22 as covered by the runtime catch.
- **A-4 (S2/S3 scope).** The custody fresh-launch path (VerificationBinding set) skips G2. Its own check requires session, a non-null instance and the custody feature, but not `WaitingForLaunch`, before sending Launch with env. This is unchanged by S1 and belongs to S2/S3.
- **A-5 (low).** ReleaseUnprovenHostIfDead publishes SessionExited before it deletes the manifest. A relaunch that lands in that microsecond window could have its new manifest deleted, and a crash in that window republishes SessionExited on restart. Deleting before completing would close both. The base adoption path has the same order.
- **A-6 (test).** V-1's restart leg does not re-await PeerClosed or re-assert `Requests` empty after the second adoption.

## Not run

- The whole Unit lane (see the top of this report).
- The Windows lane (S2 CP-8).
- All PCs except the three brief mutants. PC-1..PC-32 stay pending for SourceLanding Mutation.
