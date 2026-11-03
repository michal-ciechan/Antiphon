# Mutation sweep batch 22: CARD-1668, CARD-1670, CARD-1672, CARD-1665

Task 3af9f716 (Mutation, waived). This was an ordinary worktree sweep on master
`91aa1636816e64bf4cf08aa4b631207f0e9df47a` (L). It ran on server2 (Linux) on 2026-11-03,
from 03:24 to 03:50 UTC.

**These results are NOT CARD-0478 evidence.** Merge-role lands have no land operation id, so
there is no SourceLanding. The orchestrator waived the SourceLanding verification card and ran
this as an ordinary-worktree sweep. No product code was changed and nothing was landed.

## Outcome

- 27 mutants were run: 20 killed, 7 survived. Two extra probes (P1, P2) tested the CARD-1665
  fixture guard. They are not mutants.
- All four cards were covered in the brief's order, well inside the 180-minute budget. Slot waits were 0 to 30 s.
- The baseline was green before any mutant (32/32 and 9/9), and the restored, rebuilt tree at L was green after (32/32 and 9/9).

The survivors:

| Survivor | Card | Class |
|---|---|---|
| M56-WB | CARD-1673 survivor re-run (CARD-1656 A-1) | coverage gap, reachable (binding terms of the conditional update) |
| M56-WR | CARD-1673 survivor re-run (CARD-1656 A-1) | coverage gap, reachable (release-null term) |
| M49-L | CARD-1673 survivor re-run (CARD-1649) | coverage gap, reachable (live-status pre-check) |
| M62-A1c | CARD-1673 survivor re-run (CARD-1662) | coverage gap, reachable (`skipped ??= lastFailure`) |
| M65-X | CARD-1665 | no witness for the fixture's exactly-one guard; it is a test-infrastructure guard, and P1/P2 show what it buys |
| M65-B | CARD-1665 | masked alone: the :9376 topology layer refuses with the same diagnosis; M65-BT was killed |
| M65-T | CARD-1665 | masked alone: the :9051 layer refuses first; M65-BT was killed |

CARD-1670's landed witnesses killed only one of the CARD-1673 survivors: M56-WA, the attempt term. It is the same mutant as CARD-1670 M1.

## Method

- **Env-gated mutants.** Every C# mutant was compiled in behind `B22Mutant.On("<id>")`, which reads
  `B22_MUTANT` (a comma-separated list). The helper was in `server/B22Mutant.cs` and
  `src/Antiphon.SessionRunner/B22Mutant.cs`, both untracked and both deleted afterwards. With the variable unset,
  the gated code behaves as L.
- **Builds.**
  - One gated build per test project: `Antiphon.Tests` (290 s lease, which built and ran the baseline) and `Antiphon.SessionRunner.Tests` (35 s lease, the same).
  - Every round ran `dotnet run --no-build -p:UseAppHost=false` under its own build-slot lease, with native output and `TEMP=/tmp`.
- **The baseline.** It ran on the gated build with `B22_MUTANT` unset. The L-bytes green is the restored rebuild at the end.
- **Script mutants.** The CARD-1665 script mutants (`scripts/c590-remote.sh`) need no build, because the fixture reads the script at run time. They were edited in place for one round, then restored with `git checkout -- scripts/c590-remote.sh` before the next edit. `bash -n` checked each edit.
- **Rounds.** A round never held two mutants with the same witness test. Mutants in one file shared a round only when their witnesses were disjoint and their conditions could not interact:
  - r05: M68-3 (deadline test), M68-4 (trust-key test) and M68-9u (uncertain-transport test).
  - r06: M68-8 (bound test) and M68-9 (the ModalBlocked case).
  - r02: M68-2 (bound test) and M68-7 (modal test).
  - Script and fixture mutants were never mixed, except in P2, which is the combination by design.
- **The Antiphon.Tests filter.** Every round used the same filter (32 tests):
  `/*/*/(RunnerStartupStaleObservationTests*)|(C1656RecordReleaseHardeningTests*)|(PhoneHomeRollingRunnerDrainTests*)|(DataRetentionServiceTests*)|(HostStatsPollServiceTests*)|(VolumeRetirementContentTests*)/(A_stale*)|(A_refusal*)|(An_uncertain*)|(A_modal*)|(A_dialog*)|(The_retry*)|(C1656_*)|(C1670_*)|(C1649_*)|(C1658_*)|(C1662_*)|(C1663_*)|(C1672_*)|(Fixture_setup_runs_once*)|(Temp_recycle_refuses*)`
- **The SessionRunner.Tests filter (9 tests):** `/*/*/Card1653RefusalRetainsHostTests/*`.

| Round | Mutants (B22_MUTANT; script edit) | Total | Failed |
|---|---|---|---|
| r00 baseline | none | 32 | 0 |
| r01 | M68-1, M70-1, M70-4, M1663-6, M65-E | 32 | 9 |
| r02 | M68-2, M68-7, M70-2, M70-5, M65-X | 32 | 4 |
| r03 | M68-5, M70-3, M62-A1c; script M65-B | 32 | 2 |
| r04 | M68-6, M56-WB; script M65-T | 32 | 1 |
| r05 | M68-3, M68-4, M68-9u, M56-WR; script M65-B + M65-T (M65-BT) | 32 | 4 |
| r06 | M68-8, M68-9, M49-L; script P1 (dedent) | 32 | 4 |
| r07 | M68-7b, M65-X; script P1 (dedent), so P2 | 32 | 3 |
| r99 restored | none, rebuilt at L | 32 | 0 |
| s00 baseline | none | 9 | 0 |
| s01 | PC-D2 | 9 | 1 |
| s99 restored | none, rebuilt at L | 9 | 0 |

## CARD-1668: startup stale-observation retry

Code: `server/Infrastructure/Agents/SessionRunner/RunnerTerminalSession.cs` (RTS), `WriteStartupAsync` (:129-165).
The witnesses are in `RunnerStartupStaleObservationTests`: 7 methods, 9 cases.

| PC | file:line (L) | Mutation | Result | Killing test (assertion) |
|---|---|---|---|---|
| M68-1 retry removed | RTS:158 | attempts bound 1 | killed | A_stale_observation_on_the_probe_token_and_on_ctrl_u… (stale-observation escaped); …every_attempt_fails_after_the_bound… (Attempts.Count 3, was 1); A_modal… (Reason modal-blocked, was stale-observation); The_retry_stays… (OCE expected, got ProviderDialogInputRefusedException) |
| M68-2 unbounded | RTS:158 | `attempt >= bound` dropped | killed | …every_attempt_fails_after_the_bound… (should throw ProviderDialogInputRefusedException, did not: readiness ran to its deadline) |
| M68-3 wait ignores deadline | RTS:163 | `Task.Delay(…, CancellationToken.None)` | killed | The_retry_stays_inside_the_callers_deadline (OCE expected, got ProviderDialogInputRefusedException after further attempts) |
| M68-4 snapshot-change guard removed | RTS:149 | `intent != firstIntent` refusal dropped | killed | A_dialog_key_is_not_resent_once_the_dialog_it_answered_has_gone (should throw, did not) |
| M68-5 bound 2 | RTS:158 | constant 3 becomes 2 | killed | …every_attempt_fails_after_the_bound… (Attempts.Count 3, was 2) |
| M68-6 bound 4 | RTS:158 | constant 3 becomes 4 | killed | …every_attempt_fails_after_the_bound… (Attempts.Count 3, was 4) |
| M68-7 dialog re-check removed | RTS:153 | on a retry, the request is built from the new observation without `Request`'s dialog refusal | killed | A_modal_that_appears_during_the_race… (Attempts `[stale]`, was `[stale, modal-blocked]`: the key reached the runner over a modal) |
| M68-7b re-detection removed | RTS:137 | on a retry, the first attempt's intent is reused, not re-detected from the new screen | killed | A_dialog_key_is_not_resent… (should throw, did not) |
| M68-8 new exception type | RTS:160 | exhaustion throws InvalidOperationException | killed | …every_attempt_fails_after_the_bound… (ProviderDialogInputRefusedException expected, got InvalidOperationException) |
| M68-9 other refusal retried | RTS:158 | ModalBlocked also retried | killed | A_refusal_other_than_stale_observation_is_not_retried(modal-blocked) (should throw, did not) |
| M68-9u uncertain result retried | RTS:158 | Unknown also retried | killed | An_uncertain_transport_result_is_not_retried (IOException expected, did not throw) |

The cases ObservationUnknown and GenerationMismatch, from `A_refusal_other_than…`, pin the same condition as M68-9, so no separate mutant was run for them. A new exception of the same type and reason on exhaustion would be equivalent by construction, so it was not run.

## CARD-1670: record-release deleted-task 409 and the short-final-range budget

Code: `server/Application/Services/WorkspaceSlotReleaseService.cs` (WSR) and `DataRetentionService.cs` (DRS).
M1 to M5 come from Code report 3be64a0a.

| PC | file:line (L) | Mutation | Result | Killing test (assertion) |
|---|---|---|---|---|
| M1 (= M56-WA) | WSR:220 | `t.Attempt == attempt` dropped from the conditional update | killed | C1656RecordReleaseHardeningTests.C1670_ReleaseOnATaskWhoseAttemptChangesIsRefusedChanged (should throw ConflictException, did not) |
| M2 | WSR:218 | no transaction around the stamp and the audit event | killed | C1670_AFailedAuditEventRollsBackTheStamp (RemoteWorkspaceSlotReleasedAt should be null, was stamped) |
| M3 | WSR:231 | a missing row maps to task_live | killed | C1670_ReleaseOnATaskDeletedWhileItIsRecordedIsRefusedChanged (Code changed, was task_live) |
| M4 | DRS:517 | nonpositive budget falls back to 1, not 6 | killed | DataRetentionServiceTests.C1670_NonpositiveFailureBudgetFallsBackToSix(0) and (-1) (Thrown 6, was 3) |
| M5 | DRS:573 | budget read only on full ranges (the pre-1670 placement) | killed | C1670_BudgetSpentOnAShortFinalRangeIsReported (FailureBudgetSpent True, was False) |

### The CARD-1673 survivors, re-run against the landed CARD-1670 witnesses

| PC | file:line (L) | Mutation | Result | Killing test (assertion) |
|---|---|---|---|---|
| M56-WA attempt term | WSR:220 | `t.Attempt == attempt` dropped | **killed now** | C1670_ReleaseOnATaskWhoseAttemptChangesIsRefusedChanged (same as M1) |
| M56-WB binding term | WSR:220-221 | runner, slot and generation terms dropped | **SURVIVED** | none |
| M56-WR release-null term | WSR:221 | `RemoteWorkspaceSlotReleasedAt == null` dropped | **SURVIVED** | none |
| M49-L live-status pre-check | WSR:196 | the `HoldingLease.Contains(task.Status)` pre-check removed | **SURVIVED** | none |
| M62-A1c | DRS:577 | `skipped ??= lastFailure` dropped | **SURVIVED** | none |

**M56-WB.** This is a reachable coverage gap. CARD-1670 added the attempt-change witness only. Nothing rebinds the task between the checks and the stamp.

- **Proposed witness:** a sibling of `C1670_ReleaseOnATaskWhoseAttemptChangesIsRefusedChanged`.
  - Its `AfterStuckRecordIsRead` change sets `RemoteWorkspaceSlotGeneration = Generation + 1`, or another `RunnerId` or `RemoteWorkspaceSlotId`, leaving the status Failed.
  - Expect ConflictException `workspace_slot_release_changed`, `RemoteWorkspaceSlotReleasedAt` null and no `workspace_slot_release_recorded` event.
  - The mutant stamps the new binding's lease.

**M56-WR.** This is a reachable coverage gap: a concurrent operator record between the checks and the stamp.

- **Proposed witness:** the change stamps `RemoteWorkspaceSlotReleasedAt` (and nulls `RemoteWorktreePath`), as a racing second record would.
- Expect `workspace_slot_release_changed` and no `workspace_slot_release_recorded` event from this call. Count the events: zero from this call, and only the racer's if the change also writes one.
- The mutant re-stamps the row and writes a second audit event.

**M49-L.** This is still a reachable coverage gap. The analysis from batch 21 stands: the CARD-1656 conditional update gives the same `task_live` code for the stuck attempt.

- **Proposed witness:** in `PhoneHomeRollingRunnerDrainTests.C1649_RecordReleaseStampsOnlyTheStuckAttemptOfATaskThatIsNotLive`, the row `SetAsync(2, AgentTaskStatus.Working)` should assert the code `workspace_slot_release_task_live`.
- Without the pre-check, that row reaches the attempt-mismatch check first and returns `workspace_slot_release_attempt_mismatch`.

**M62-A1c.** This is still a reachable coverage gap.

- Both CARD-1670 budget witnesses fail every attempt. Their ranges are skipped, so `skipped` is already set when the budget check runs.
- **Proposed witness (unchanged from batch 21):**
  - A fault interceptor fails each root range's first two attempts and lets the third succeed. Use `TaskPruneBatchSize = 2`, 10 roots and the default budget 6.
  - After three ranges, FailedAttempts is 6 and SkippedRanges is 0.
  - Assert FailureBudgetSpent and `Passes["Tasks"].Status == Failed`. The mutant reports Completed.

## CARD-1672: the two survivor witnesses

| PC | file:line (L) | Mutation | Result | Killing test (assertion) |
|---|---|---|---|---|
| M1663-6 | server/Infrastructure/Agents/SessionRunner/HostStatsPollService.cs:145 | read the drain state for eligible runners too (the `eligible ? null :` gate dropped) | **killed now** | HostStatsPollServiceTests.C1672_an_eligible_draining_runner_whose_stats_fail_keeps_its_old_reason (row.Reason "unavailable", was "draining") |
| PC-D2 | src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:3879 | CompletePendingAsExited drops the `Exited` early return | **killed now** | Card1653RefusalRetainsHostTests.Completing_a_pending_row_twice_publishes_SessionExited_once (exited 1, was 2); SessionRunner.Tests total 9, failed 1 |

Both of the earlier survivors are now killed.

## CARD-1665: C1008HostFixture setup once, at the top-level trap

Code: `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs` (the fixture, :3391-3396) and
`scripts/c590-remote.sh`. The witnesses are in `VolumeRetirementContentTests`: `Fixture_setup_runs_once_across_audit_traps` and the A-3 vector `Temp_recycle_refuses_a_present_owned_container_before_any_audit`.

| PC | file:line (L) | Mutation | Result | Killing test (assertion) |
|---|---|---|---|---|
| M65-E every-trap insertion | fixture :3396 | setup inserted before every `trap 'ec=$?;` (the pre-1665 `Replace`) | killed | Fixture_setup_runs_once_across_audit_traps (wrapped lines should be 1, was 296) |
| M65-X throw removed | fixture :3394 | the exactly-one check removed (insert at the first match) | **SURVIVED** | none (the real script has exactly one column-0 trap) |
| M65-B :9051 | c590-remote.sh:9051 | `c1008_bind_generation` no longer refuses a present owned temp container | **SURVIVED** (masked) | none: the A-3 vector still sees exit 2 and RecycleContainerStateUnknown from the topology layer |
| M65-T :9376 | c590-remote.sh:9376 | `c1008_owned_mounts` skipped; owned containers get an empty topology | **SURVIVED** (masked) | none: :9051 refuses first |
| M65-BT both | :9051 + :9376 | both refusals removed | killed | Temp_recycle_refuses_a_present_owned_container_before_any_audit (Exit should be 2, was 0: `C1609_RETIREMENT state=Applying`) |

**M65-X, and the question "is there a witness?"** No. No test pins the throw.

- The throw is a test-infrastructure guard. On the current script there is exactly one column-0 `trap 'ec=$?;` (:10477), so removing the throw changes nothing.
- Only a future script edit can trigger it. Two probes show what the throw buys. Both dedent the inner trap at :8700 (in `c1008_preview_restore_traps`) to column 0, which leaves the bash semantics unchanged:
  - **P1, fixture at L:** both witnesses fail at once and loudly: `InvalidOperationException: C1008HostFixture: expected one top-level trap 'ec=$?; line, found 2`.
  - **P2, the same dedent plus M65-X:**
    - The setup is inserted at the first match, inside the function, so it never runs at entry.
    - Both witnesses fail with a misleading diagnosis: `DIAGNOSIS=WrongLane want=host lane=none` (preview Exit 2, and wrapped lines 0).
    - A second column-0 trap placed after :10477 would be silent instead. The setup would still go to the real entry trap, and nothing would fail.
- So the guard's value is a clear failure in place of a misleading or silent one, and it is unpinned.
- **Proposed witness:** extract the insertion into a static helper, for example `C1008HostFixture.InsertSetupAtEntry(string source, string injection)`. Unit-test it:
  - zero column-0 traps throws;
  - two column-0 traps throw;
  - one column-0 trap plus indented traps inserts exactly once, before the column-0 line.
- **Alternative:** record M65-X as accepted. P1 is the manual check that it fires.

**M65-B and M65-T.** These are two independent layers.

- Each mutant is masked by the other layer. The other layer refuses with the same `RecycleContainerStateUnknown` diagnosis, still before the lock, any `run`, any stop or rm and the journal. Every observable the vector asserts is unchanged.
- The pair is pinned: M65-BT was killed. This matches the Code report's own result (9051 alone survived; 9051 plus the topology change went red).
- Through the product, M65-B is unreachable alone while the temp topology model (:6300) has no services, and M65-T is unreachable alone while :9051 stands.
- **Optional layer witnesses (not required):**
  - call `c1008_bind_generation` directly for the temp project with one owned container, and expect the refusal;
  - call `c1008_owned_mounts` with the temp model and one session-runner container, and expect the refusal.

## Restoration and final state

- **Restore.** Every mutated file was restored with `git checkout -- <exact path>`:
  - scripts/c590-remote.sh
  - server/Application/Services/DataRetentionService.cs
  - server/Application/Services/WorkspaceSlotReleaseService.cs
  - server/Infrastructure/Agents/SessionRunner/HostStatsPollService.cs
  - server/Infrastructure/Agents/SessionRunner/RunnerTerminalSession.cs
  - src/Antiphon.SessionRunner/SessionRunnerRuntime.cs
  - tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs

  The two untracked `B22Mutant.cs` helper files were deleted.
- **Rebuild.** Both test projects were rebuilt at L: `Antiphon.Tests` (275 s lease) and `Antiphon.SessionRunner.Tests` (29 s lease).
- **Stale-DLL check.** A UTF-16 search confirmed that no `B22_MUTANT` string is left in any of these DLLs:
  - Antiphon.Server.dll (both copies);
  - Antiphon.Tests.dll;
  - Antiphon.SessionRunner.dll (both copies).
- **Restored green at L:** Antiphon.Tests total 32, failed 0. Antiphon.SessionRunner.Tests total 9, failed 0.
- **Final state.**
  - HEAD was `91aa1636816e64bf4cf08aa4b631207f0e9df47a` before this report commit.
  - Tracked source and index are clean, and no sweep process is still running.
  - The remaining untracked outputs are the gitignored native `bin/` and `obj/` trees and `.antiphon/inbox/`.
- **Leases.** There were 12 build-slot leases: 4 builds that also ran tests, and 8 no-build runs. The logs and TRX files are in the task scratchpad (`logs/run-*.log`, `trx/<round>/`), which is not committed.
