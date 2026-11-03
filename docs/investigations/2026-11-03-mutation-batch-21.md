# Mutation sweep batch 21: CARD-1662, CARD-1656, CARD-1666, CARD-1652

Task b842c9bf (Mutation, waived). This was an ordinary worktree sweep on master
`7dc9e1168a2b4f714a4e3e29ac99e2aad485524c` (L). It ran on server2 (Linux) on 2026-11-03.

**These results are NOT CARD-0478 evidence.** Merge-role lands have no land operation id, so
there is no SourceLanding. The orchestrator waived the SourceLanding verification card and ran
this as an ordinary-worktree sweep. No product code was changed and nothing was landed.

## Outcome

- 50 mutants were run: 41 killed, 9 survived.
- All four cards were covered in the brief's order, within the 180-minute budget.
- The baseline at L was green before any mutant, and the restored tree at L was green after.

The survivors:

| Survivor | Card | Class |
|---|---|---|
| M56-WA, M56-WB, M56-WR | CARD-1656 | coverage gap, reachable (conditional-update WHERE terms) |
| M49-L | CARD-1649 | coverage gap, reachable (the refusal code changes) |
| M62-A1c | CARD-1662 | coverage gap, reachable |
| M66-F | CARD-1666 | coverage gap, reachable |
| M66-R | CARD-1666 | unreachable alone (masked by the coordinator gate); its reachable variant M66-R2 was killed |
| M52-PC3c | CARD-1652 | output-equivalent (the IO window only) |
| M52-PC3d | CARD-1652 | coverage gap; the witness exercises another site, and that site's mutant PC-3e was killed |

## Method

- **Env-gated mutants.** Every mutant was compiled into the build behind
  `B21Mutant.On("<id>")`, which reads `B21_MUTANT` (a comma-separated list of ids). Each test run
  set the round's ids. With `B21_MUTANT` unset, the gated code behaves as L.
  - One gated build served each test project: `Antiphon.Tests` once, and `Antiphon.SessionRunner.Tests` twice. The second SessionRunner build added PC-3e and PC-3f.
  - This replaced one build per mutant. Slot waits on loaded server2 were 5 to 17 minutes per lease, so one build per mutant would not have fit the budget.
- **Leases.** Every build and every run took its own build-slot lease (`scripts/build-slot.ps1`) with native output and `-p:UseAppHost=false`. Runs used `dotnet run --no-build` with the same property.
- **Rounds.** Each round held at most one mutant per file, and the mutants in a round had different witness methods.
  - RunnerStateAlarmExclusion mutants and RunnerAlarmCoordinator mutants never shared a round, because the exclusion witnesses go through the real coordinator.
  - Each failure was attributed through its class and method to the round's mutant for that file.
  - Rounds r12 to r15 held only one CARD-1656/1649 mutant each, and ran only the C1656 class and the C1649 method.
- **Antiphon.Tests filter.** Rounds r01 to r11 used one filter, the union of every witness method (40 tests):
  - classes: DataRetentionServiceTests, C1656RecordReleaseHardeningTests, PhoneHomeRollingRunnerTests, RunnerStateAlarmExclusionTests, RunnerAlarmCoordinatorTests, RunnerAlarmDeliveryTests, AlarmSettingsValidatorTests
  - methods: `C1658_*`, `C1662_*`, `C1656_*`, `C1649_*`, `C1663_*`, `C1666_*`, plus every method of the four alarm classes
- **SessionRunner.Tests filter (7 tests):**
  - classes: WindowsShellGuardAuditTests, RemoteControlConditionalInputTests, SessionBufferBoundsTests, PtyHostAdoptionTests
  - methods: the three audit methods, `C514_Input*`, `Live_buffer_stays*`, `Buffer_endpoint*`, `Exit_while_runner_down_is_collected_on_adoption_with*`
- **CARD-1652 PC-1.** These mutants edit a scanned source file. The audit reads source at run time, so they needed no rebuild. Each ran alone against `WindowsShellGuardAuditTests/*`.
- **Restoration.** Each mutated file was restored with `git checkout -- <exact path>`, and the two untracked `B21Mutant.cs` helper files were removed.
  - PC-1b's `tests/Shared/HerdrTestProcess.cs` edit was still in place when the first gated SessionRunner build compiled. That edit is semantically identical: the guard is spelled as `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)`.
  - The edit was restored before any SessionRunner round ran.

## Baseline at L (green, before mutation)

- **Antiphon.Tests:** total 40, failed 0. The slot lease was held 83 s.
- **Antiphon.SessionRunner.Tests:** total 7, failed 0.

## CARD-1662: retention task prune failure budget and audit cutoff

Code: `server/Application/Services/DataRetentionService.cs` (DRS) and `AuditService.cs`. The CARD-1658 controls come from Code report 24291915.

| PC | file:line (L) | Mutation | Result | Killing test (assertion) |
|---|---|---|---|---|
| PC-1 | DRS:544 | outbound protection loaded once per pass | killed | C1658_TaskProtectedBetweenBatchesSurvives (roots[3] should survive) |
| PC-2 | DRS:557 | 1 attempt instead of 3 before a skip | killed | C1658_PoisonRootRangeIsSkippedAfterThreeFailures (Thrown 3); C1662_FailureBudget… (Thrown 6, was 5) |
| PC-3a | DRS:757 | usage-sample `Take(size)` becomes unbounded | killed | C1658_UsageSampleBacklogPrunesInBoundedBatches (each statement touches at most 2 rows) |
| PC-3b | DRS:870 | residue Held/Preview `Take(size)` becomes unbounded | killed | C1658_ResidueHeldRowBacklogPrunesInBoundedBatches (rows at most 2) |
| PC-3c | AuditService.cs:313 | audit archive `Take(batchSize)` becomes unbounded | killed | C1658_AuditArchiveBacklogNullsInBoundedBatches (rows at most 2) |
| PC-4 | DRS:807 (`PruneRowBatchesAsync` loop) | per-sweep row batch cap ignored | killed | all three C1658_*Backlog tests (RemainingAsync(old) should be 1) |
| PC-5 | DRS:804 (`PruneRowBatchesAsync`) | row batch command timeout not applied | killed | all three C1658_*Backlog tests (Timeout == 45) |
| A-1 | DRS:576 | budget check removed | killed | C1662_FailureBudgetStopsTaskPassForTheSweep (Thrown 6) |
| A-1b | DRS:576 | `>=` becomes `>` | killed | C1662_FailureBudget… (Thrown 6) |
| A-1c | DRS:579 | `skipped ??= lastFailure` dropped | **SURVIVED** | none |
| A-1d | DRS:575 | cursor not advanced before the budget stop | killed | C1662_FailureBudget… (Cursor should be roots[3]); C1658_Poison… (surviving) |
| A-2 | DRS:778 | audit cutoff recomputed per batch | killed | C1662_AuditCutoffStaysFixedAcrossBatches (the late record keeps FullContent) |

**A-1c survivor.** This is a reachable coverage gap.

- When the budget is spent by ranges that fail once or twice and then succeed, no range is skipped and `skipped` stays null.
- Without `skipped ??= lastFailure`, a pass that stopped early reports **Completed**, not Failed. The CARD-1662 contract and the RetentionSettings doc say it reports Failed.
- The only witness, C1662_FailureBudget…, fails every attempt, so its ranges are skipped and `skipped` is already set.
- **Proposed witness:**
  - A fault interceptor fails each root range's first two attempts and lets the third succeed. Use `TaskPruneBatchSize = 2` and 10 roots.
  - After three ranges, FailedAttempts is 6 and SkippedRanges is 0. Assert FailureBudgetSpent, `Passes["Tasks"].Status == Failed` and the exception type chain.
  - The mutant reports Completed.

## CARD-1656 (and CARD-1649 controls): record-release

Code: `server/Application/Services/WorkspaceSlotReleaseService.cs` (WSR).

| PC | file:line (L) | Mutation | Result | Killing test (assertion) |
|---|---|---|---|---|
| A-1 WHERE attempt (M56-WA) | WSR:219 | `t.Attempt == attempt` dropped from the conditional update | **SURVIVED** | none |
| A-1 WHERE binding (M56-WB) | WSR:219-220 | runner, slot and generation terms dropped | **SURVIVED** | none |
| A-1 WHERE release (M56-WR) | WSR:220 | `RemoteWorkspaceSlotReleasedAt == null` dropped | **SURVIVED** | none |
| A-1 WHERE status (M56-WS) | WSR:221 | `!HoldingLease.Contains(t.Status)` dropped | killed | C1656_ReleaseIsNeverStampedOnATaskThatGoesLiveWhileItIsRecorded (should throw ConflictException) |
| A-1 result (M56-U) | WSR:225 | `stamped != 1` ignored | killed | C1656_ReleaseIsNever… (should throw) |
| 409 mapping (M56-409) | WSR:228 | task_live and changed codes swapped | killed | C1656_ReleaseIsNever… (refused.Code should be workspace_slot_release_task_live) |
| A-3 control (M56-CC) | WSR:168 | Control not refused | killed | C1656_RequesterRefusesFormatAndSeparatorCharacters (U+0007 got non-400) |
| A-3 format (M56-CF) | WSR:168 | Format not refused | killed | C1656_RequesterRefuses… (right-to-left override) |
| A-3 line separator (M56-CL) | WSR:168 | LineSeparator not refused | killed | C1656_RequesterRefuses… (line separator) |
| A-3 paragraph separator (M56-CP) | WSR:168 | ParagraphSeparator not refused | killed | C1656_RequesterRefuses… (paragraph separator) |
| A-3 supplementary plane (M56-CA) | WSR:168 | categories read per UTF-16 char, not per rune | killed | C1656_RequesterRefuses… (astral tag) |
| A-4 case (M56-T) | WSR:170 | token compared `Ordinal` | killed | C1656_RequesterRefusesTheOperatorTokenInAnyCase (400 expected) |
| CARD-1649 token check (M56-TX) | WSR:170 | token check removed | killed | C1656_RequesterRefusesTheOperatorTokenInAnyCase; C1649_RecordReleaseStampsOnlyTheStuckAttemptOfATaskThatIsNotLive (400 expected) |
| CARD-1649 attempt check (M49-A) | WSR:199 | stuck prefix without `task attempt N` | killed | C1649_RecordRelease… ("a new attempt is a live lease": Conflict expected) |
| CARD-1649 live-status (M49-L) | WSR:195 | live-status pre-check removed | **SURVIVED** | none |

**WHERE-term survivors (M56-WA, M56-WB, M56-WR).** These are reachable coverage gaps.

- The only race witness makes the task go Working. Nothing changes the attempt, the binding or the release between the checks and the stamp.
- The `workspace_slot_release_changed` (ChangedCode) branch is not witnessed anywhere.
- **Proposed witnesses.** Add three variants of `GoLiveWhileStuckRecordIsRead` that fire on the stuck-record read and then make a different change from another connection:
  - (a) Set `Attempt = 2`, status still Failed. Expect ConflictException `workspace_slot_release_changed`, `RemoteWorkspaceSlotReleasedAt` still null and no `workspace_slot_release_recorded` event. Under M56-WA the mutant stamps attempt 2's lease.
  - (b) Set `RemoteWorkspaceSlotGeneration = 4`, or another `RunnerId`. Expect the same refusal. M56-WB stamps the new binding.
  - (c) Stamp `RemoteWorkspaceSlotReleasedAt`, as a concurrent operator record would. Expect the changed refusal and exactly one recorded event. M56-WR stamps a second time and writes a second audit event.

**M49-L survivor.** This is a reachable coverage gap, not an equivalent mutant.

- For a task in Dispatched, Working or Blocked on the stuck attempt, the CARD-1656 conditional update refuses with the same `workspace_slot_release_task_live` code, so C1649's three-state loop stays green.
- But C1649's row "attempt 2, Working" asserts no code (`code: null`). Without the pre-check, that row reaches the attempt-mismatch check first and returns `workspace_slot_release_attempt_mismatch` instead of `task_live`.
- **Proposed witness:** assert `workspace_slot_release_task_live` on that row (PhoneHomeRollingRunnerDrainTests.cs, the `SetAsync(2, AgentTaskStatus.Working)` line).

## CARD-1666: draining alarm bound

Code: `server/Application/Services/RunnerStateAlarmExclusion.cs` (RSAE) and `RunnerAlarmCoordinator.cs` (RAC). The validator is in `server/Application/Settings/AlarmSettingsValidator.cs`.

| PC | file:line (L) | Mutation | Result | Killing test (assertion) |
|---|---|---|---|---|
| bound (M66-B) | RSAE:38 | `age <= bound` becomes `age < bound` | killed | C1666_a_draining_runner_offline_past_the_bound_raises_with_a_drain_overran_note (Episodes empty at exactly 30 min) |
| no-start fail-safe (M66-S) | RSAE:35 | null DrainedAt returns null | killed | C1666_a_draining_runner_with_no_drain_start_fails_safe_and_raises (single episode expected) |
| future-start fail-safe (M66-F) | RSAE:35 | DrainedAt later than now returns null | **SURVIVED** | none |
| retired-unbounded (M66-R) | RSAE:32 | `!= "draining"` becomes `is null` (retired gets a bound) | **SURVIVED** (masked) | none |
| retired-unbounded, end to end (M66-R2) | RSAE:32 + RAC:72 | M66-R plus the coordinator asks DrainOverran for any exclusion | killed | C1663_a_retired_or_draining_runner_raises_no_alarm (Episodes must not contain the retired runner) |
| eligibility gate (M66-G) | RAC:73 | `!snapshot.Eligible` dropped | killed | C1666_a_draining_runner_that_reconnects_is_told_it_accepts_no_new_work (Header `[runner r4 draining]`) |
| reachable-draining wording (M66-W) | RAC:248 | the standing sentence is always "not dispatch-eligible" | killed | C1666_…reconnects… (Body contains "not accepting new work"); C1666_a_retired_or_draining_close_note_reaches_the_caller_as_a_user_prompt(draining, True) |
| overran sentence (M66-N) | RAC:347 | overran sentence not appended | killed | C1666_…offline_past_the_bound… ("drain overran"); C1666_…no_drain_start…; C1666_…reconnects… (EndsWith) |
| close note, retired (M66-DR) | RAC:77 | ResolveAsync skipped when retired | killed | a_draining_or_retired_runner_never_raises_and_a_disabled_entry_is_skipped (single retired close note); C1666_close_note(retired, False) (not delivered) |
| close note, draining (M66-DD) | RAC:77 | ResolveAsync skipped when draining | killed | C1666_…reconnects… (single close note); C1666_close_note(draining, True) (not delivered) |
| validator (M66-V) | AlarmSettingsValidator.cs:12 | RunnerDrainMaxMinutes positivity check removed | killed | defaults_validate_and_nonpositive_values_are_named (result.Failed) |

**M66-F survivor.** This is a reachable coverage gap. A drain start in the future, for example from clock skew between hosts, is meant to fail safe, but no witness covers it.

- **Proposed witness:** apply `RunnerState(Draining: true, DrainedAt: T0 + 10 min)` to an offline runner. Evaluate at T0 and at T0 + 180 s. Expect one raised episode and a note containing "drain start is unknown".
- A smaller alternative is a unit assertion: `RunnerStateAlarmExclusion.Overran(state, now, 30)` is not null when `DrainedAt > now`.

**M66-R survivor.** This mutant is unreachable on its own.

- RAC:72 calls `DrainOverran` only when `Excluded` returned `"draining"`, and `Reason` returns `"retired"` first for a retired row. So a retired runner never reaches `Overran` through the product.
- The retired-unbounded behaviour is pinned end to end: M66-R2, which also opens the coordinator gate, was killed.
- **Optional unit witness:** `Overran` on a retired state with an old DrainedAt returns null.

## CARD-1652: WindowsShellGuardAuditTests and the converted live-child tests

| PC | file:line (L) | Mutation | Result | Killing test (assertion) |
|---|---|---|---|---|
| PC-1a | tests/Antiphon.SessionRunner.Tests/RemoteControlConditionalInputTests.cs:15 | `Cmd` back to `Path.Combine(Environment.SystemDirectory, "cmd.exe")` | killed | Every_command_interpreter_literal_is_behind_a_windows_guard (flags `RemoteControlConditionalInputTests.cs:15`); total 3, failed 1 |
| PC-1b | tests/Shared/HerdrTestProcess.cs:34 | guard spelled `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` | killed | Every_command… (flags `HerdrTestProcess.cs:35`); total 3, failed 1 |
| PC-2a | WindowsShellGuardAuditTests.cs:107 | guard window 0 (same line only) | killed | Scan_flags_an_unguarded_literal_and_accepts_guarded_or_commented_ones (ternary probe); Every_command… (real ternary sites flagged) |
| PC-2b | WindowsShellGuardAuditTests.cs:122 | comments treated as code | killed | Scan_flags… (comment probe); Every_command… |
| PC-2c | WindowsShellGuardAuditTests.cs:130 | quote tracking removed (`//` in a string cuts the line) | killed | Scan_flags… (URL probe should have a single item) |
| PC-2d | WindowsShellGuardAuditTests.cs:107 | guard window unbounded (from line 0) | killed | Scan_flags… (far-guard probe should have a single item) |
| PC-3a | src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:1663, 1684, 1703 | all three conditional-input generation checks removed | killed | C514_Input_uses_microsecond_accepted_generation (refused.Outcome GenerationMismatch) |
| PC-3b | SessionRunnerRuntime.cs:4103 | live-buffer trim disabled | killed | Live_buffer_stays_bounded_and_keeps_the_newest_output (raw.Length at most Cap*2) |
| PC-3c | SessionRunnerRuntime.cs:4952 | ANSI log tail seek window disabled | **SURVIVED** (equivalent in output) | none |
| PC-3d | SessionRunnerRuntime.cs:3767 | `CreateAdoptedExited` exit code becomes null | **SURVIVED** (not reached) | none |
| PC-3e | SessionRunnerRuntime.cs:4650 | the live host's exit code becomes null | killed | Exit_while_runner_down_is_collected_on_adoption_with_the_real_exit_code (ExitCode 7) |
| PC-3f | SessionRunnerRuntime.cs:4978 | final `text[^cap..]` trim removed | killed | Buffer_endpoint_returns_only_the_tail_of_a_large_ansi_log (buffer.Length at most Cap) |

**PC-3c.** This mutant is equivalent on the endpoint's output.

- The final `text[^cap..]` trim bounds the result whatever the window does.
- The window bounds only the bytes read. That is IO cost, and no test can see it without IO accounting.
- PC-3f, the trim, is the output control, and it was killed.

**PC-3d.** This is a coverage gap.

- The adoption witness's host is still alive at adoption and reports the real exit through the host Exited path (:4650, PC-3e, killed). It never reaches `CreateAdoptedExited`, the host-gone path that reads the exit from the manifest.
- **Proposed witness:**
  - Write a manifest with a recorded `ExitCode` (for example 7) and `ExitReason "ProcessExited"`, and a dead host pid.
  - Run `AdoptOrphanedHostsAsync`.
  - Expect `dto.ExitCode == 7`. The mutant gives null.

## Restoration and final state

All mutated files were restored with `git checkout -- <exact path>`, and `server/B21Mutant.cs` and `src/Antiphon.SessionRunner/B21Mutant.cs` were removed.

- **Rebuild.** Both test projects were rebuilt at L, `Antiphon.Tests` (265 s lease) and `Antiphon.SessionRunner.Tests` (49 s lease). A UTF-16 search confirmed that no `B21_MUTANT` gate is left in Antiphon.Server.dll or in either copy of Antiphon.SessionRunner.dll.
- **Restored green at L:**
  - Antiphon.Tests: total 40, failed 0 (same filter as the baseline).
  - Antiphon.SessionRunner.Tests: total 7, failed 0.
- **Final state:**
  - HEAD is `7dc9e1168a2b4f714a4e3e29ac99e2aad485524c` before this report commit.
  - Tracked source and index are clean.
  - No sweep process is still running.
  - The remaining untracked outputs are the gitignored native `bin/` and `obj/` trees (74 directories) and `.antiphon/inbox/`.
- **Leases.** There were 34 build-slot leases: 7 builds and 27 runs. Slot waits were 0 to 17 minutes. The run logs and TRX files are in the task scratchpad (`logs/run-*.log`, `trx/<round>/`), which is not committed.
