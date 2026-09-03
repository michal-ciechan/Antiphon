# CARD-0336 — triage the 16 non-Postgres Antiphon.Tests failures

**Plan pass, 2026-09-03.** Sources: CARD-0336, CARD-0238 verification at `95ad1dd5` (`docs/superpowers/plans/2026-09-03-card-0238-postgres-testcontainer-verification.md`), current master `db63063c` (CARD-0335 plan-only, no production change). Isolation re-run of all 16 named tests against `db63063c` with `tests/Antiphon.Tests/bin-c336/Antiphon.Tests.exe`. No production code is changed by this plan.

The CARD-0238 connection-exhaustion bug is closed and is not in this set. Do not raise `max_connections`, add a pool semaphore, or re-open CARD-0238.

## 1. Reproduce

The card allowed a filtered re-run instead of another 25-minute full suite. Each of the 16 names was executed as its own TUnit process (fresh Postgres testcontainer per invocation) at `db63063c`:

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c336/
tests/Antiphon.Tests/bin-c336/Antiphon.Tests.exe --treenode-filter "/*/*/<Class>/<Test>" --output Detailed
```

`ModelAvailabilityTests` and `ModelAvailabilityDispatcherTests` were also run as whole classes (8/8 and 3/3).

| # | Test | Isolation | Assertion (when red) |
|---|---|---|---|
| 1 | `Probe_cancellation_kills_the_tree_and_redacts_secret_canaries` | pass | — |
| 2 | `Late_process_start_after_the_one_second_deadline_is_reaped` | pass | — |
| 3 | `last_activity_uses_transcript_after_dispatch_and_falls_back_otherwise` | **fail** | `LastActivityAt` `…28.1991642Z` vs `…28.1991640Z` |
| 4 | `a_git_timeout_fails_one_task_not_the_tick` | pass | — |
| 5 | `a_failed_worktree_add_leaves_no_registration_branch_or_directory` | pass | — |
| 6 | `A_brief_inside_the_ceiling_arrives_whole` | **fail** | `FitBriefForTyping` returned a pointer (`YOUR BRIEF IS NOT IN THIS MESSAGE`), not `HEAD-MARKER` |
| 7 | `the_final_message_missing_incident_is_raised_once_per_session` | **fail** | `ReportNudgeMessageId` is null in `MarkNudgeDeliveredAsync` |
| 8 | `AgentSessionService_kill_force_kills_after_grace_period` | **fail** | `attempt.Phase` stayed `StreamingTurn`, expected `Failed` |
| 9 | `AgentSessionService_kill_marks_active_attempt_canceled_and_disposes_adapter` | **fail** | `attempt.Phase` stayed `StreamingTurn`, expected `Canceled` |
| 10 | `arm_0_while_live_on_turn_end_in_progress_enqueues_one_parent_note` | pass | — |
| 11 | `A_fable_hold_skips_fable_and_dispatches_sonnet_on_the_same_tick` | pass | — |
| 12 | `A_manual_fable_hold_skips_queued_fable_and_dispatches_sonnet` | pass | — |
| 13 | `ListAvailable_omits_held_aliases_and_keeps_the_rest` | pass | — |
| 14 | `Require_throws_model_disabled_with_available_list` | pass | — |
| 15 | `Dispatch_records_an_informational_warning_and_never_refuses_on_a_low_reading` | **fail** | status `Queued`, expected `Dispatched` |
| 16 | `Assembly_guard_and_factory_override_disable_the_Hangfire_worker` | pass | — |

The original clustering (ModelAvailability 11–14; kill/grace 2, 8–9; Hangfire 16; rest unclustered) is **not** the isolation grouping. Tests 2, 8 and 9 are not one timing flake. Tests 11–14 are not CARD-0335.

This pass did **not** re-run the full 3,893-test suite. The 11 isolation-green names are still allowed to be red under full-suite contention; they are not production defects until a post-fix full run says otherwise.

## 2. Existing cards — do not file 16 fixes

| Card | Status | Relation |
|---|---|---|
| **CARD-0335** | InProgress, plan-only at `db63063c` | **Not the cause of 11–14.** Those four pass in isolation on current code. AutoDetected/null expiry is a production hold bug; these tests failed in the full suite because they read the **shared** `ModelAvailabilityHolds` table / run a **global** `TickAsync`. Execute CARD-0335 on its own plan. Do not fold these tests into it. After CARD-0335 lands, 13 (`until: null` AutoDetected) stays held via `HitAt + fallback`, so it should remain green. |
| CARD-0319 | Done | KillAsync now writes on an isolated `AppDbContext` and reloads the tracked **session**. It does **not** reload tracked `RunAttempt`s. Tests 8–9 are that leftover. |
| CARD-0098 | Review | A *different* test (`An_edit_snapshots_importance_urgency_and_due_and_maintains_UrgentSince`) showed the same timestamptz truncation. Do not expand CARD-0098. Test 3 is the same class of comparison. |
| CARD-0304 | Review | Owns `LastActivityAt`. Test 3 is not a pipeline-product bug; the round-trip is 200 ns of Postgres microsecond truncation. |
| CARD-0298 | Review | Owns `HangfireStartupSafetyTests`. Test 16 is green in isolation; full-suite red is the shared `AntiphonWebAppFactory` `ListCalls` counter. Fix here (S6) rather than a new card. |
| CARD-0297 | Review | `GitWorkspaceService` harness. The quota test already calls `AddDelegationWorktreeGraph`. Not the cause of 15. |
| CARD-0248 | Done | Test 7's helper is the CARD-0248 nudge → deliver → later text-less TurnEnd path. Do not weaken settle-anyway gates. |
| CARD-0280 | Backlog | Frozen-clock factory. Test 7 finished in 2.9 s (not a hang). Do not block this card on CARD-0280. |
| CARD-0308, CARD-0128, CARD-0208, CARD-0050 | Review/Done | Process-tree kill / Pty.Tests flake / SessionRunner.Tests flake / fakeclaude flake. Tests 1–2 pass in isolation; different assemblies or already-serialised `ProcessSpawnLimit`. |
| CARD-0238 | Done | Out of scope. |

Do not open one card per failing name. CARD-0336 execute is the one follow-up.

## 3. Clusters (by root cause)

### A — Isolated kill does not sync the caller's RunAttempt (tests 8, 9) — production

`AgentSessionService.KillAsync` (`server/Application/Services/AgentSessionService.cs:834-880`) opens a fresh context, transitions the attempt there, then `SyncTrackedSessionAfterIsolatedKillAsync` reloads only `ChangeTracker.Entries<AgentSession>()`.

Isolation evidence: `adapter.Killed` / `adapter.Disposed` / `session.Status` already match (session reload works). `attempt.Phase` stays `StreamingTurn` because the test's tracked attempt was never reloaded. This is not a grace-period flake: both tests fail in ~1 s.

Production risk: a later `SaveChanges` on the caller's context can write the stale `StreamingTurn` row back over the kill transition — the same class of clobber CARD-0319 isolated the session for.

Fix: reload or detach tracked `RunAttempt`s for that `sessionId` in the same sync helper. Keep the tests' assertions on the caller-tracked entities; they are the pin.

### B — timestamptz microsecond truncation (test 3) — test comparison

`AgentTaskPipelineStatusTests` uses `CreateIsolatedSchemaAsync` (not shared-table pollution). Expected `2026-09-03T05:14:28.1991642Z`, actual `…1991640Z`. Npgsql `timestamptz` stores microseconds; 2 extra 100 ns ticks round to 0.

`LoadLastActivityAsync` (`AgentTaskPipelineStatusService.cs:442-476`) is doing the right thing (`Timestamp ?? CreatedAt`, after dispatch). Do not change the production query.

Fix: truncate seeded timestamps to microseconds, or compare with a 1 µs tolerance. Same helper can be reused if CARD-0098's `An_edit_snapshots_…` is still red; that test is not in this 16.

### C — brief-ceiling fixture vs grown contract (test 6) — test

`A_brief_inside_the_ceiling_arrives_whole` sets `DelegationSettings.BriefInlineMaxBytes = 1024` and expects `FitBriefForTyping` to return the brief (`HEAD-MARKER`). It returned `BuildBriefPointer` (`YOUR BRIEF IS NOT IN THIS MESSAGE`). The test's own comment says the reporting contract used to floor around 915 bytes; it now exceeds 1024 even for a 40-byte goal.

This is not a ConPTY delivery regression (it fails before `DeliverAsync`). Do not raise the shipped 900-byte inbox ceiling. Raise the *test's* synthetic ceiling above `Encoding.UTF8.GetByteCount(BuildBrief(...))` for that fixture, or pin the contract size and fail with the measured byte count when it grows. Keep the first assertion (shipped 900 still spills).

### D — Codex quota dispatch never leaves Queued (test 15) — harness

`Dispatch_records_an_informational_warning_and_never_refuses_on_a_low_reading` seeds a Queued Codex task and a 3% usage sample, then `TickAsync`. Isolation: status stays `Queued`. The harness does **not** seed a warm Codex agent (contrast `ModelAvailabilityDispatcherTests.SeedWarmAgentAsync`) and does not `DrainOtherQueued`. Policy still says a low reading must warn, not refuse (`SubscriptionQuotaPolicy` warning text is the assertion's expected substring).

Fix the harness: isolated schema, warm Idle Codex agent (or the same pool-create path the dispatcher actually uses), drain other queued rows, then assert `Tick` result counts plus the Warning event. If it still stays Queued, the skip/failure reason on `AgentTaskEvents` is the next fact — do not change the gate to make the test pass.

### E — first OnTurnEnd does not record a nudge id (test 7) — harness vs CARD-0248 path

`the_final_message_missing_incident_is_raised_once_per_session` seeds a split turn with narration + **bare** TurnEnd (`finalMessage: null`), advances 121 s (`FinalMessageGraceSeconds` default 120), then `SettleTextlessAfterNudgeAsync` which requires `ReportNudgeMessageId` after the first `OnTurnEndAsync`. Isolation: that id is null. Session is seeded `Running`. The test finished in 2.9 s, so this is not a CARD-0222 frozen-clock hang.

`NudgeForClosingLineAsync` only sets `ReportNudgeMessageId` when `SessionMessageQueueService.EnqueueAsync` fires `onCreated`. A bare thinking TurnEnd is also the CARD-0046 "wait for the text record" shape. Execute must trace that first `OnTurnEnd` (does it no-op, nudge without an id, or skip because grace/boundary identity?). Restore a recorded nudge so the once-per-session incident pin is real. Do not drop the CARD-0248 deliver-then-later-boundary gates.

### F — full-suite-only (tests 1, 2, 4, 5, 10, 11–14, 16)

Green alone, red in CARD-0238's 25 m 27 s run. Two sub-groups:

**F1. Shared-store readers (11–14).** `ModelAvailabilityTests` / `ModelAvailabilityDispatcherTests` are `[NotInParallel]` with no group key, but tests *without* that attribute still write `ModelAvailabilityHolds` and Queued tasks. `ListAvailable` / `Require` / `TickAsync` are fleet-wide. Another test holding `opus` or `grok-4.6` makes 13–14 fail; another Queued task or held sonnet makes 11–12 fail. Put these on `CreateIsolatedSchemaAsync` (the pipeline-status pattern). This is the opposite of CARD-0335.

**F2. Shared factory counter (16).** `HangfireStartupSafetyTests` uses `[ClassDataSource<AntiphonWebAppFactory>(Shared = SharedType.PerTestSession)]` — the same singleton as 16 other classes. It asserts `_factory.SessionRunner.ListCalls.ShouldBe(0)` and `ZombieCensus.Calls.ShouldBe(0)`. Isolation (only consumer) is 0; a full session is not. Keep the env-var / `IBackgroundProcessingServer` null / no Hangfire hosted-service assertions. Drop the process-wide call-count pins, or snapshot before/after this test.

**F3. Leave until a post-fix full run (1, 2, 4, 5, 10).** Process-spawn / git-timeout / arm-0 race. Already serialised (`ProcessSpawnLimit`, `[NotInParallel("RunnerProcessProbe")]`, `[Timeout(30_000)]`, `[NotInParallel]`). Do not widen timeouts. If they fail the post-S1–S6 full suite, they are contention flakes and get a targeted re-run at this commit vs a quiet machine, not a retry loop.

## 4. Implementation slices (recommended order)

| Slice | Cluster | Files | Work |
|---|---|---|---|
| **S1** | A (8, 9) | `server/Application/Services/AgentSessionService.cs`; `AgentSessionServiceIntegrationTests.cs` | Reload/detach tracked `RunAttempt`s for the killed session in the CARD-0319 sync helper. Re-run the two kill tests. |
| **S2** | B (3) | `AgentTaskPipelineStatusTests.cs` (and a tiny helper if one already exists for DateTime round-trip) | Seed or compare at microsecond precision. Re-run `last_activity_uses_transcript_after_dispatch_and_falls_back_otherwise`. |
| **S3** | C (6) | `DelegationBriefCeilingPtyTests.cs` | Measure `BuildBrief` UTF-8 bytes for the 40-byte-goal fixture; set the synthetic ceiling above it; keep the shipped-900-spills assertion. Re-run that test. |
| **S4** | D (15) | `SubscriptionQuotaGateTests.cs` | Isolated schema + warm Codex agent + drain; keep the "warn, do not refuse" assertions. Re-run that test. |
| **S5** | E (7) | `AgentTaskReplyIntegrationTests.cs` (and only production `AgentTaskReplyService` if the first OnTurnEnd is a real miss, not a fixture miss) | Make the first OnTurnEnd record a nudge id for this fixture. Do not loosen CARD-0248. Re-run that test. |
| **S6** | F1+F2 (11–14, 16) | `ModelAvailabilityTests.cs`, `ModelAvailabilityDispatcherTests.cs`, `HangfireStartupSafetyTests.cs` | Isolated schema for availability/dispatcher; Hangfire call-count pins only for this test's delta. Re-run those five tests. |
| **S7** | F3 verify | the 16 names, then optionally the full suite | After S1–S6, re-run all 16 filtered. A full `Antiphon.Tests` watched run is the only way to close F3; it is ~25–28 minutes (`scripts/run-tests-watched.ps1`). Do not kill it before ~35 minutes (Gotcha #74). |

S1 is the only production behaviour change and should land first. S2–S6 are test/harness. S7 is evidence, not a code slice.

## 5. Verification

TUnit, not `dotnet test`. Class or test-name filters, not a namespace. Alternate output while daemons hold `bin/`:

```powershell
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c336/ -- --treenode-filter "/*/*/AgentSessionServiceIntegrationTests/AgentSessionService_kill_*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c336/ -- --treenode-filter "/*/*/AgentTaskPipelineStatusTests/last_activity_uses_transcript_after_dispatch_and_falls_back_otherwise"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c336/ -- --treenode-filter "/*/*/DelegationBriefCeilingPtyTests/A_brief_inside_the_ceiling_arrives_whole"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c336/ -- --treenode-filter "/*/*/SubscriptionQuotaGateTests/Dispatch_records_an_informational_warning_and_never_refuses_on_a_low_reading"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c336/ -- --treenode-filter "/*/*/AgentTaskReplyIntegrationTests/the_final_message_missing_incident_is_raised_once_per_session"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c336/ -- --treenode-filter "/*/*/ModelAvailabilityTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c336/ -- --treenode-filter "/*/*/ModelAvailabilityDispatcherTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c336/ -- --treenode-filter "/*/*/HangfireStartupSafetyTests/Assembly_guard_and_factory_override_disable_the_Hangfire_worker"
```

TUnit accepts only one `--treenode-filter`. Delete `bin-c336` directories afterwards (`Get-ChildItem -Recurse -Depth 2 -Directory -Filter bin-c336`). Forward slash on `OutputPath`.

## 6. Non-goals

- CARD-0238 / container capacity / connection-pool patches.
- Implementing CARD-0335 in this card.
- Widening kill grace, git-timeout, or pty timeouts.
- Filing 16 cards, or treating isolation-green names as full-suite green without S7.
- Changing Manual or AutoDetected hold semantics, Hangfire worker-on in tests, or CARD-0248 settle-anyway.
