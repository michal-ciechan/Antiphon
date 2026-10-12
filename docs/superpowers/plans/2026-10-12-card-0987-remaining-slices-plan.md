# CARD-0987: CARD-0826 remaining slices (S2 extraction, S3 orchestration, S4 transport, S5 operations, native)

Plan task: `40fe843e-210c-4d45-91b9-0f1cbd5b2ca4` (Plan, Frontier, 2026-10-12). Inspected
checkout and fetched `origin/master`: `ce470787aa054ee2d5c0fbdfd7a63a49645d4a96`. Parent plan:
[2026-10-01-card-0826-daily-host-cleanup-plan.md](2026-10-01-card-0826-daily-host-cleanup-plan.md)
(frozen D-1..D-7, V-1..V-13, R-1..R-5, PC-1..PC-118). Status owner:
[docs/host-cleanup.md](../../host-cleanup.md). **Plan complete; next: test-design.** This
dispatch did not fold TestDesign into Plan: the `## Verification design` below is a proposal that
TestDesign must freeze (bind exact names, recount rosters) before any Code dispatch. No build,
test, schedule, deploy or deletion ran in this task; post-land Mutation was **not** run.

## Outcome and remaining scope

CARD-0826 landed only a partial, deliberately unwired slice (reviewed landing owner Code task
`863ee584`, tested production source `c20f26028dd72c8d2f3b14258b12534e0ce86be2`). This plan covers
**only** what is still absent and does not re-plan landed work. The parent's frozen authority
(D-1..D-7) is unchanged: worktrees stay inventory-only, the daily graph never takes a repository
lease, deletion stays `Execute=false` on every namespace until the per-host dry-run and explicit
operator activation in the parent's activation section are evidenced, and no floor is lowered.

Remaining work, grouped as this plan's slices:

| Parent slice | Landed on master | Absent (this plan) |
|---|---|---|
| S1 | `src/Antiphon.HostCleanup` virtual policy/executor core (13 files, 587 lines); V-1 22 / V-2 20 / V-3 12 green at `a9cc7715`. | Native adapters still throw `NotSupportedException`; `ICleanupNativeApi` seam absent (goes to S9/S10 below). |
| S2 | Nothing. `tools/Antiphon.Checkpoints/Cleanup/*` unchanged; sweep still lives in `tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweep.cs`. | Extraction, 14 adapter cases, compatibility ports, image guard, scratch registry/tool/helper, real lifecycle callers. |
| S3 | Entities, `AddHostCleanup` migration, `HostCleanupService` (receipt ingest/read/publish), `HostCleanupAttentionService`, `HostMaintenanceService`/store, `HostCleanupWorktreeInventory` classifier, DTOs, settings; 34 of 56 CP-3 cases green. | 14 orchestration cases, 5 report delivery cases, `Land_waits_only_for_bounded_inflight_delete`, 6 inventory cases; jobs, endpoints, auth, read-only Git probe, transport interface, DI/Hangfire registration, hold resolution, public attention recipient, land/restart participation. |
| S4 | Nothing in runner or contracts. | Capability, contracts, command service, local store/outbox, dispatcher/transport, packaging. |
| S5 | `dev-aspire.ps1` ASCII normalization; client attention kinds 53/54/55 mapped (29 Vitest green). | `host-cleanup.ps1`, `task-scratch.ps1`, weekly wrapper/installer refusal, Windmill host definition, maintenance participation in restart/deploy wrappers, smoke disk evidence, docs/bundles. |
| Native | Nothing. | Parent CP-6 (Linux) and CP-7 (Windows) rows, per-host dry-runs, activation. |
| Mutation | Nothing. | PC-1..PC-118 (119 variants) plus the two landed receipt controls. |

## Ground truth

Each row states what the card or parent plan assumes versus what the inspected source does. These
corrections bind the slices below; TestDesign re-verifies them at its own freeze SHA.

| Assumption (card / parent plan) | Inspected source at `ce470787` | Consequence |
|---|---|---|
| R-1 compatibility roster is 40 executions (37 methods): sweep 15, scope 9, tool-copy 16. | `CheckpointTempRootSweepTests` has 21 `[Test]` methods, two with `[Arguments]` (2 and 6 rows): **27 executions**. `CheckpointTempScopeTests` has 11 methods: **11**. `CheckpointToolCopyCleanupTests` unchanged: 13 methods, **16**. Current census is **54 executions / 45 methods**. The six new sweep methods and two new scope methods came from CARD-0820 contention guards and the absent-pointer/teardown commits (`1b30bbe15`, `041d95986`, `f2820ad11`). | D-1: port all 54. The parent CP-2 floor rises from 66 to **80** (12 + 14 + 54), carried by CP-4 below. Floors are never lowered; raising is required by the "do not lower floors" rule applied to new source. |
| `dev-aspire.ps1` carries 351 non-ASCII bytes; S5 must normalize it. | Landed at `2327795cc`; every script in the parent's ASCII list has zero non-ASCII bytes today. | `Daemon_and_autostart_scripts_are_ascii` is a pure guard with no inherited red. PC-102 stays valid. |
| Parent CP-3 roster is 56; 34 present. | Present: 2 schema tests (not in the frozen 14), 15 of 20 report cases plus 2 landed regressions, 9 of 10 maintenance, 6 of 12 inventory. Missing: all 14 orchestration, `Post_run_low_space_never_widens_deletion`, `Receipt_to_attention_reaches_busy_recipient`, `Receipt_to_attention_reaches_ready_recipient`, `Crash_before_enqueue_recovers_pending_receipt`, `Crash_after_ingestion_before_ack_queries_status`, `Land_waits_only_for_bounded_inflight_delete`, `Full_run_never_acquires_repository_lease`, `Daily_graph_has_no_direct_lease_or_lock_escape`, `Would_remove_worktree_is_preserved_in_execute_mode`, `Would_remove_worktree_is_preserved_in_dry_run`, `Read_only_git_never_fetches_or_mutates_refs`, `Only_owned_scratch_child_is_deleted`. | 26 cases to author. Completed CP-3 roster is **60** (56 frozen + 4 landed supplemental). The parent's single 56-case row is split below into narrow per-slice rows so each Code task fits 30-60 minutes; the union still equals the full roster. |
| Existing worktree owners are CARD-0692 (post-land), CARD-0459 (residue sweep), CARD-0665/0670 rules. | CARD-1017 (InProgress), CARD-1032 (InProgress) and CARD-1033 (Review) added a completed-card worktree executor family: `CardWorktreeCleanupExecutor`, `CardWorktreeCleanupScheduler`, mirror cleanup service (CARD-1032 S7), and an inert mirror-binding migration `20261012075920_AddTaskWorktreeMirrorBinding`. | The inventory report's "existing owner" vocabulary must include `CARD-1017` as an owner name; the daily job still never calls it. Collision map below serializes shared files. |
| `HostCleanupSettings` has no main-instance flag; D-1 says "main-only" registration. | `server/Program.cs` registers recurring jobs behind `HangfireSettings` (worker enabled flag at line 920 and the registration block at lines 1063-1081); no `IsMain` concept exists. Secondary instances already skip recurring registration by that same setting. | D-2: "main-only" is implemented as "register only where the existing Hangfire worker is enabled"; no new instance-role setting. `Main_only_registers_daily_jobs_and_monitor` asserts against that gate. |
| Runner capability `hostCleanupV1` exists or is trivially added. | `RunnerCapabilityFeatures` (SessionRunnerContracts.cs:141) has no cleanup feature; `PhoneHomeOperation` has no cleanup operation; dispatcher admission is in `PhoneHomeCommandDispatcher.DispatchAsync`. | S7 adds the feature constant, two operations (preview, execute) and the request/response records; capability absence must refuse before any root observation (V-8). |
| Task scratch is `/tmp/claude-1654/<task>`. | Actual Linux layout is `/tmp/claude-1654/<worktree-dirname>/<session-id>/scratchpad` (this session: `/tmp/claude-1654/-work-worktrees-task-40fe843e/4df4cff2-.../scratchpad`). | Scratch registry records the exact resolved path from the creator; the family grammar is the two-level layout, never the final component alone. |
| A transport interface `IHostCleanupTransport` exists for stubs. | Absent. `HostCleanupServerFixture` has only `HostCleanupRecordingEvents`; no `ThrowingRepositoryMutationLease`, `ForbiddenRetirementProbe`, `RecordingRecurringJobManager`, `ScriptedHostCleanupTransport`. | S4 authors those fakes in the existing Application fixture file before the orchestration service. |
| `HostCleanupScriptTests` uses an `Infrastructure` fixture. | Script tests live under `tests/Antiphon.Tests/Scripts/` (`BuildSlotScriptTests`, `RunnerDrainScriptTests`, `CapabilityScriptTests`) with the assembly-local `ParallelLimiter<ProcessSpawnLimit>`. | S8 places `HostCleanupScriptTests.cs` and its fixture under `tests/Antiphon.Tests/Scripts/`; the parent's `Infrastructure/HostCleanupScriptFixture.cs` path is corrected. Scope line updated accordingly. |
| Windmill definitions for the weekly build-junk schedule are checked in. | `scripts/windmill/` holds nightly/readiness/release definitions only; the weekly build-junk and Claude cleanup schedules exist only as comments in the scripts. | The cutover test (V-9 `Weekly_schedule_cutover_is_single_owner_and_reversible_to_dry_run`) proves the offline receipt protocol; the live schedule is operator activation step 3, never a test. |
| CARD-0835 must land first. | CARD-0835 is Done. The checkpoint tool gained repeat tooling (CARD-0885, `scripts/lib/checkpoint-repeat.ps1`) and the executor-exit re-read (CARD-1071). | No ordering dependency remains; S1/S2 extraction must thread instance I/O through those newer call sites as well. |
| `C804_ORPHAN_SWEEP_ROOT` disables the assembly sweep hook. | Still true: `CheckpointTempRootSweep.cs:391` returns on any non-null value. | Every TUnit row keeps `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1`. |
| Platform: pin a runner per row. | `GET /api/runner-defaults` revision 2: global default `server2`, no kind overrides. `GET /api/session-runners`: `desktop` windows capacity 2 occupied 0; `server2` linux capacity 10 occupied 5; `server2-temp` unavailable. | Linux rows name the `linux-` lane and omit `-Runner`; only CP-15/CP-16 (Windows) and the Windows activation dry-run need `-Platform Windows` (pin `-Runner desktop` only if a second Windows host appears). |

## Decisions

Each decision is stated under its default; the operator can overturn any of them before Code.

### D-1. Port the current 54-execution compatibility census, not the frozen 40

The source grew after the freeze (ground truth row 1). Porting only the 40 would move
`CheckpointTempRootSweep` while leaving the CARD-0820 root-lock contention guards, the absent-pointer
drop and the scope teardown contracts un-ported; the extraction could regress exactly the behaviour
that was added last. The new `HostCleanupCheckpointCompatibilityTests` class therefore has **45
methods / 54 executions**, keeping each source method name, its `[Arguments]` rows and its
assertion. The old physical classes stay untouched and unselected. Rejected: freezing at 40 and
filing the delta as a follow-up (leaves a known blind spot through a move), or editing the physical
classes to inject I/O (they are the read-only source contract).

### D-2. "Main-only" scheduling means "where the existing Hangfire worker runs"

No instance-role setting exists (ground truth row 5). The daily per-host jobs and the 15-minute
monitor register inside the same `if` block as `AddOrUpdateWorktreeResidueJob`, through two new
`HangfireConfiguration` methods, gated additionally by `HostCleanup:Enabled` (default false).
Rejected: a new `Instance:IsMain` setting (a second source of truth for the same gate) and a
runner-side timer (D-1 of the parent).

### D-3. Split CP-3 into per-slice rows; keep the union at 60 and never reuse a green row for changed code

The parent's one 56-case CP-3 row cannot fit a 30-60 minute Code task when 26 cases are still to be
authored across four services. Rows CP-5..CP-8 below each select one class or one behaviour group,
and CP-9 reruns the whole 60-case union once at the composed tip. Rejected: keeping a single row
(forces one 3-4 hour Code task) and dropping the union rerun (loses the cross-service lease/graph
proof that only the full mixed run gives).

### D-4. Worktree inventory names CARD-1017 as an existing owner; the daily job still calls nothing

Reports group eligible-but-unremoved worktrees by `CARD-0692`, `CARD-0459` and now `CARD-1017`
(completed-card executor). The read-only owner-status probe reads each owner's latest availability
once per run. The daily graph never enqueues, triggers or invokes any of the three. Rejected:
treating CARD-1017's Done-card executor as the daily job's deletion path (contradicts the operator's
confirmed lock-free decision and would acquire the repository lease transitively).

### D-5. The runner transport is one phone-home operation pair plus one HTTP preview route, sharing one command service

`HostCleanupCommandService` is the only entry point; the phone-home dispatcher and the runner's
local HTTP surface both call it with the same typed request. Preview is pure; execute requires the
capability, the bound host/store identity and an explicit run ID from the server. Rejected: a
runner-local scheduler (parent D-1) and a `docker exec` host path (parent D-1).

### D-6. The host worker is `tools/Antiphon.HostCleanup`, a thin command over the shared library, shipped in the runner image

`scripts/host-cleanup.ps1`, `scripts/task-scratch.ps1`, the weekly wrapper and the Windmill host
bridge all invoke that one executable. It has no kill API, no Git mutation and no test dependency.
Rejected: a PowerShell-only implementation (cannot share the planner/executor and would duplicate
policy) and running the test assembly as the worker (forbidden by the parent).

### D-7. Native adapters land behind the same `ICleanupFileSystem` seam with a recording native-call port; kernel qualification stays operational

`LinuxCleanupFileSystem`/`WindowsCleanupFileSystem` implement handle-relative, no-follow observation
and deletion over an injectable `ICleanupNativeApi`. CP-14/CP-15 drive the real adapter code with a
recording virtual native API; no selected test deletes a real path. Real-kernel behaviour is
activation step 5 on operator-authorized private fixtures. Rejected: declaring kernel semantics
proven by the fake rows (explicitly forbidden by the parent's Out of scope).

### D-8. Windows work is two rows and one activation step, dispatched separately on the Windows lane

CP-15 (adapter, 6 cases) and CP-16 (PowerShell 5.1 wrapper offline run, 1 case) need a Windows
host. They are their own Code task (S10) after the Linux slices land, so no Linux slice waits on the
desktop. Rejected: folding Windows rows into Linux tasks (a platform row on the wrong OS is not a
passing row).

### D-9. Slice size and lane

Every slice below is budgeted at 30-60 minutes including its checkpoint rows. Linux slices run on
the default lane (`server2`); do not pass `-Runner`. S10 passes `-Platform Windows`. Each Code
task dispatches with `-Worktree` and the slice's exact scope line.

### Operator decisions

| ID | Question | Recommendation (default used below) |
|---|---|---|
| OD-1 | Accept D-1 (54-execution roster, CP-2 floor 80) or hold the frozen 40? | Accept 54. The parent says floors are never lowered; this raises one. |
| OD-2 | Accept the CP-3 split (D-3) with a final 60-case union rerun? | Accept. Same coverage, dispatchable in 30-60 minute tasks. |
| OD-3 | Should the daily job's report list CARD-1017 as an owner before CARD-1033 lands its scheduler? | Yes, name it now; the owner-status probe returns `owner_unavailable` until that owner exposes availability. No call is made either way. |
| OD-4 | Build-slots image recreation (card item d) and smoke disk evidence (item c) are deploy actions. Keep them in S8 as offline-tested script changes only, with the live redeploy left to the next authorized rollout? | Yes. No redeploy from a Code task. |
| OD-5 | Hold expiry for the seven historical server2 prefixes is already past (2026-10-08). Keep them as `hold_expired_review_required` with no disposition change? | Yes. Expiry is review, not permission (parent D-5). |

## Landable slices and collision map

Paths: `L=src/Antiphon.HostCleanup/`, `A=server/Application/`, `I=server/Infrastructure/`,
`T=tests/Antiphon.Tests/`, `R=tests/Antiphon.SessionRunner.Tests/`. Every slice lands with the
feature still unwired and `Execute=false`; S5 is the only slice that touches `server/Program.cs`
and it registers under `HostCleanup:Enabled=false`. Each slice commits and pushes before its rows.

| Slice / budget | Files (new marked **new**) | Behaviour and tests |
|---|---|---|
| **S1 / 60 min: instance I/O seams in the checkpoint tool** | `tools/Antiphon.Checkpoints/Cleanup/{ToolCopyCleanup,OutputCleanup,RunOwnership,TestRootGuard,ProcessIdentity}.cs`; `tools/Antiphon.Checkpoints/{CheckpointApp,Program}.cs`, `Commands/WaitCommand.cs`, `Evidence/EvidenceFolder.cs`, `State/RunStateStore.cs`; **new** `L/Checkpoints/ICheckpointCleanupIo.cs` (filesystem + clock + process-identity instance ports), **new** `L/Checkpoints/ExecutorImageGuard.cs`; `L/Antiphon.HostCleanup.csproj`; `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj` gains a library reference (one direction only). | No logic moves yet. Static facades forward to instances; production defaults stay native; no mutable global override. `EvidenceFolder.IsExecutorImage` forwards to `ExecutorImageGuard`. Tests: **new** `T/Infrastructure/HostCleanupCheckpointFixture.cs` (virtual I/O, recording process control, fake delay) and the first 16 ports (tool-copy class) in **new** `T/Infrastructure/HostCleanupCheckpointCompatibilityTests.cs`. CP-1. |
| **S2 / 60 min: sweep and scope ports (pre-move), then the move** | `T/Checkpoints/CheckpointTempRootSweep.cs` (core extracted to **new** `L/Checkpoints/CheckpointTempRootSweep.cs`; wrapper keeps `CheckpointTempSweepAssemblyHook`/roster glue), `T/Checkpoints/CheckpointTestScope.cs` (uses `TestRootGuard.MarkerName`), `tests/Antiphon.Checkpoints.LifecycleHost/Antiphon.Checkpoints.LifecycleHost.csproj` (direct library reference, linked wrapper retained); compatibility test file gains the remaining 29 methods (38 executions). | Pre-move: commit ports, run CP-2 green against the unmoved implementation (declared preparatory run). Move, rerun CP-2 identical. `actual_deleted_bytes_are_bounded` truncation stays lifecycle-only. CP-2. |
| **S3 / 45 min: daily adapter and scratch registry** | **new** `L/Checkpoints/DailyCheckpointAdapter.cs` (24-hour gate over shared core, shared root gate, no `SweepOnce` shortcut), **new** `L/ScratchRegistry.cs` (register/status/release records, exact path + owner generation, two-level scratch grammar), `L/CleanupFamilyRegistry.cs` (task-scratch family binds registry records). | **new** `T/Infrastructure/HostCleanupCheckpointAdapterTests.cs`: the frozen 14 V-4 names. `Shared_library_has_no_test_or_tool_dependency` inspects assembly references. CP-3, then CP-4 union (80). |
| **S4 / 60 min: server fixtures, read-only Git probe and inventory closure** | `T/Application/HostCleanupFixture.cs` gains `ThrowingRepositoryMutationLease` (both overloads counted), `ForbiddenRetirementProbe`, `RecordingReadOnlyGitProbe`, `ScriptedExistingOwnerStatusProbe`, `ScriptedHostCleanupTransport`, `RecordingRecurringJobManager`; **new** `A/Interfaces/IHostCleanupTransport.cs`; **new** `I/Git/HostCleanupWorktreeProbe.cs` (`GIT_OPTIONAL_LOCKS=0`, `ls-remote` + local objects, never fetch/prune/remove/update-ref); `A/Services/HostCleanupWorktreeInventory.cs` (owner names incl. CARD-1017, `Only_owned_scratch_child` rule); **new** `A/Services/HostCleanupDailyRun.cs` (mixed preview/execute graph over inventory + library executor; no lease type reachable). | `T/Application/HostCleanupWorktreeInventoryTests.cs` gains the six missing names; `Daily_graph_has_no_direct_lease_or_lock_escape` walks reachable daily types for `RepositoryMutationLease` construction, `landing.lock` I/O, retirement calls. CP-5. |
| **S5 / 60 min: scheduling, catch-up, activation and authorization** | **new** `I/Agents/HostCleanupJob.cs`, `I/Agents/HostCleanupMonitorJob.cs`; `I/Agents/HangfireConfiguration.cs` (`AddOrUpdateHostCleanupJobs`, `AddOrUpdateHostCleanupMonitorJob`, 10:00 Europe/London + deterministic 0-30 min host offset, 15-min monitor); **new** `A/Services/HostCleanupScheduler.cs` (per-storage job ids, alias collapse, last-completed-date catch-up, namespace qualification + config digest, weekly-cutover confirmation flag, seven-prefix hold resolution); `A/Settings/HostCleanupSettings.cs` + validator (hosts/roots/storage ids, `WeeklyBuildJunkDisabledConfirmed`, `Qualified` per namespace); **new** `server/Api/Endpoints/HostCleanupEndpoints.cs` (GET hosts, GET runs/{id} paged, POST preview, POST hold, POST maintenance, POST receipts); `server/Program.cs` (DI + Hangfire registration under `HostCleanup:Enabled`), `server/appsettings.json` (`Enabled=false`, `Execute=false`). | `T/Application/HostCleanupOrchestrationTests.cs` gains the frozen 14 names; keeps the 2 landed schema tests. **new** `T/Application/HostCleanupTestServer.cs` maps only feature/attention/auth routes. CP-6. |
| **S6 / 45 min: delivery and maintenance closure** | `A/Services/HostCleanupService.cs` (status-before-retry query route, pending-outbox drain entry), **new** `A/Services/HostCleanupReceiptOutbox.cs` (server-side pending receipt recovery), `A/Services/HostMaintenanceService.cs` (bounded-operation syscall barrier hook), `A/Services/AgentTaskLandService.cs` (publish maintenance intent before `TryAcquireAsync` at line 475, finish after; no cleanup invocation), `A/Services/HostCleanupAttentionService.cs` (public recipient through `AttentionService` read path), `A/Services/AttentionService.cs` (append host-cleanup projection), `A/Dtos/AttentionDtos.cs` (no new kinds; `HostCleanupBacklog` reuses 56 if already allocated, else add). | `T/Application/HostCleanupReportTests.cs` gains the five missing names; `T/Application/HostCleanupMaintenanceTests.cs` gains `Land_waits_only_for_bounded_inflight_delete` using `MaintenanceBarriers`. CP-7, CP-8, then CP-9 union (60). |
| **S7 / 60 min: runner contracts, command service, store and transport** | `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs` (`RunnerCapabilityFeatures.HostCleanupV1`), `PhoneHomeContracts.cs` (two operations, request/response records), **new** `src/Antiphon.SessionRunner.Contracts/HostCleanupContracts.cs`; **new** `src/Antiphon.SessionRunner/HostCleanupCommandService.cs`, `HostCleanupLocalStore.cs` (protected durable receipt outbox under the runner state root), `HostCleanupWorktreeInventory.cs` (read-only facts); `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs` (admission + routing only), `src/Antiphon.SessionRunner/Program.cs` (DI; no hosted timer), `Antiphon.SessionRunner.csproj` (library reference); **new** `I/Agents/SessionRunner/HostCleanupTransport.cs` implementing `IHostCleanupTransport` over `PhoneHomeRunnerClient.cs`. | **new** `R/HostCleanupFixture.cs` (real dispatcher, `RecordingPhoneHomeRuntime`, `LoopbackHostCleanupWire`, `VirtualRunnerDurableStore`), **new** `R/HostCleanupCommandTests.cs` with the frozen 12 V-8 names. CP-10. |
| **S8 / 60 min: worker, scripts, cutover, smoke evidence, docs** | **new** `tools/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj`, `Program.cs` (preview/status/execute/register/release/clean verbs); **new** `scripts/host-cleanup.ps1`, `scripts/task-scratch.ps1`, `scripts/host-cleanup-server2.sh`, `scripts/lib/host-cleanup-maintenance.ps1`, `scripts/fixtures/c826-cleanup-shim.ps1`, `scripts/windmill/antiphon-host-cleanup-server2.json` + `.schedule.json`; `scripts/cleanup-build-junk.ps1` (compatibility wrapper, no robocopy/Remove-Item), `scripts/install-cleanup-task.ps1` (refuse install, keep uninstall), `scripts/restart-apphost.ps1`, `scripts/restart-session-runner.ps1`, `scripts/session-runner-restart-health.ps1`, `dev-aspire.ps1`, `scripts/deploy-server2.ps1`, `scripts/verify-card0849-caches.ps1` (tmp volume identity + host free bytes in receipts), `docker/session-runner-grok/Dockerfile`, `docker-compose.server2-runner.yml` (publish worker, settings, no new mount); `scripts/windmill/README.md`, `server/Bundles/stage-code.md`, `stage-review.md` (self-clean rule), `docs/host-cleanup.md`, relevant sections of `docs/bootstrap.md`, `docs/logs.md`, `docs/testing-and-build.md`, `docs/ops-http.md`. | **new** `T/Scripts/HostCleanupScriptTests.cs`, `T/Scripts/HostCleanupScriptFixture.cs` with the frozen 12 V-9 names; `attentionVisuals.test.ts` gains `shows sustained worktree backlog with its responsible owner` if the backlog kind is new. CP-11, CP-12. All scripts ASCII-only. |
| **S9 / 45 min: Linux native adapter** | **new** `L/ICleanupNativeApi.cs`, `L/LinuxCleanupNativeApi.cs`; `L/LinuxCleanupFileSystem.cs` (openat/fstatat/unlinkat semantics over the port, held parent handles, device/inode identity, EACCES partial). | **new** `T/Infrastructure/HostCleanupLinuxTests.cs` with the frozen 6 V-10 names over `HostCleanupNativeFixture(Linux)` added to `T/Infrastructure/HostCleanupFixture.cs`. CP-14. |
| **S10 / 60 min, Windows lane: Windows native adapter and 5.1 wrapper run** | **new** `L/WindowsCleanupNativeApi.cs`; `L/WindowsCleanupFileSystem.cs` (reparse-safe open flags, file-ID/volume identity, long-path handle binding, boot/start tolerance); no script changes beyond what S8 landed. | **new** `T/Infrastructure/HostCleanupWindowsTests.cs` with the frozen 7 V-11 names. CP-15 (6 adapter cases), CP-16 (`Windows_powershell_51_parses_and_runs_wrappers_offline`). Dispatch with `-Platform Windows`. |

Collision map (recheck active task footprints before every dispatch; serialize, never run two
owners of one file concurrently):

| Other work | Shared files with this plan | Order |
|---|---|---|
| CARD-1032 (InProgress) | `PhoneHomeContracts.cs`, `SessionRunnerContracts.cs`, `PhoneHomeCommandDispatcher.cs`, `PhoneHomeRunnerClient.cs`, runner `Program.cs`, `AppDbContext.cs`/migrations | S7 waits for its in-flight runner slice to land; S5 adds no migration (tables exist), so only the DI/Hangfire files collide with CARD-1033 S17. |
| CARD-1033 (Review) | `HangfireConfiguration.cs`, `server/Program.cs`, `AgentTaskLandService.cs`, `WorktreeResidueSweepService.cs` (not edited here) | S5 and S6 dispatch after CARD-1033's current Review lands or is parked; re-read its registration block before adding ours. |
| CARD-1199 repair (Code, Blocked, server2) and reviews on CARD-1196/1042 | Unknown scope (empty scope string in the listing) | Read each task's brief footprint before S5/S6/S8; defer on any exact-file overlap. |
| CARD-0692 (Review) | None landed yet; shared released-scratch seam is a future integration | If it lands first, integrate only its released-scratch entry into S3's registry; no second cleanup stage. |

Scope line for Code dispatches is the parent's two scope lines **plus**
`tests/Antiphon.Tests/Scripts/HostCleanup*,src/Antiphon.HostCleanup/Checkpoints/**,src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs,server/Application/Services/HostCleanupDailyRun.cs,server/Application/Services/HostCleanupScheduler.cs,server/Application/Services/HostCleanupReceiptOutbox.cs,tests/Antiphon.Tests/Application/HostCleanupTestServer.cs,tools/Antiphon.Checkpoints/CheckpointApp.cs,tools/Antiphon.Checkpoints/Program.cs,tools/Antiphon.Checkpoints/Commands/WaitCommand.cs,tools/Antiphon.Checkpoints/Evidence/EvidenceFolder.cs,tools/Antiphon.Checkpoints/State/RunStateStore.cs,scripts/lib/host-cleanup-maintenance.ps1,scripts/session-runner-restart-health.ps1,dev-aspire.ps1`
and **minus** `tests/Antiphon.Tests/Infrastructure/HostCleanupScriptTests.cs` (relocated). Each slice
dispatches with only the subset it edits.

## Verification design

Proposed for TestDesign to freeze. Method names are the parent's frozen names unless a ground-truth
row changed them; TestDesign binds every name against the then-current tree and recounts. Every
TUnit row sets `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1`, builds to a
forward-slash isolated output, and runs through `scripts/run-checkpoint.ps1` or the checkpoint tool
(`run --plan <this file> --rows <selection> --expected-source-sha <sha>`, then `wait` until exit is
not 75). No row selects a physical checkpoint class, the Unit lane or a provider suite.

### Verification matrix

| ID | Slice | Class (file) | Executions | What it proves | Red proof (case -> mutation -> named failure) |
|---|---|---|---:|---|---|
| R-1a | S1 | `HostCleanupCheckpointCompatibilityTests` tool-copy ports (16) | 16 | Existing tool-copy custody/evidence/stop contracts survive instance I/O | `automatic_recovery_preserves_all_evidence`: delete the report beside the tool in recovery -> evidence hash assertion fails (PC-109). |
| R-1b | S2 | same class, sweep (27) + scope (11) ports | 38 | Sweep bounds, root-lock recheck, absent pointer, scope teardown contracts hold before and after the move | `root_delete_count_is_bounded`: ignore MaxRoots -> `CompletedRoots == 2` fails (PC-111); `eligibility_is_rechecked_after_the_root_lock("live")`: skip post-lock recheck -> retained-count assertion fails (new control PC-119). |
| V-4 | S3 | `HostCleanupCheckpointAdapterTests` | 14 | Daily gate stricter than lifecycle; shared root gate; no test/tool dependency; image guard; launch/ack custody | `Daily_age_is_stricter_than_lifecycle_grace`: call `SweepOnce` without the 24-hour gate -> one-hour root deleted, keep assertion fails (PC-56). |
| V-13 | S4 | `HostCleanupWorktreeInventoryTests` (12) | 12 | Zero lease attempts across a full mixed run; graph has no lease/lock escape; execute preserves would-remove worktrees; read-only Git | `Full_run_never_acquires_repository_lease`: insert `TryAcquireAsync` in the daily run -> counter assertion `== 0` fails (PC-19, two overload variants). |
| V-5 | S5 | `HostCleanupOrchestrationTests` (14 + 2 schema) | 16 | Main-only registration, alias collapse, untouched residue/Claude schedules, cutover confirmation, deferred unsupported/offline, one catch-up per day, scoped auth, hold resolution, per-namespace qualification | `Restart_catchup_runs_once_per_local_day`: enqueue every missed day -> dispatch-count `== 1` fails (PC-68). |
| V-6 | S6 | `HostCleanupReportTests` (20 + 2 landed) | 22 | Busy/ready recipient GET, crash-before-enqueue and lost-ack recovery, low-space never widens caps | `Crash_after_ingestion_before_ack_queries_status`: re-execute on lost ack -> execute-count `== 1` fails (PC-84). |
| V-7 | S6 | `HostCleanupMaintenanceTests` (10) | 10 | Land waits only for the bounded in-flight delete; cleanup yields to intent | `Land_waits_only_for_bounded_inflight_delete`: acquire the land lease before drain -> lease-call-at-barrier `== 0` fails (PC-88). |
| V-8 | S7 | `HostCleanupCommandTests` (12) | 12 | Capability refusal, no request roots, bound identity, lost-reply dedup, restart replay without re-delete, no session side effects | `Lost_reply_returns_existing_receipt`: allocate a new claim on duplicate -> receipt-identity assertion fails (PC-16). |
| V-9 | S8 | `HostCleanupScriptTests` (12) | 12 | ASCII, scratch helper rules, wrapper has no remover, installer refuses, host bridge excludes volumes, smoke identities, restart entry points publish/drain, redaction | `Legacy_build_junk_wrapper_uses_shared_worker_only`: restore the robocopy branch -> `robocopy calls == 0` fails (PC-105). |
| V-12 | S8 | `attentionVisuals.test.ts` | 29 or 30 | Backlog owner display (only if a new kind is added) | `shows sustained worktree backlog with its responsible owner`: drop owner from label -> owner substring assertion fails. |
| V-10 | S9 | `HostCleanupLinuxTests` (6) | 6 | Real Linux adapter rejects link swap, mount crossing, path rebuild; EACCES partial; /proc identity | `Swapped_symlink_preserves_outside_sentinel`: follow the link at the native seam -> outside-sentinel/unlink-through-link assertion fails (PC-17). |
| V-11 | S10 | `HostCleanupWindowsTests` (7) | 7 | Reparse-safe opens, alias refusal, sharing-violation partial, file-ID revalidation, long paths, 5.1 wrapper offline | `Swapped_junction_preserves_outside_sentinel`: omit the reparse flag -> required-flag assertion fails (PC-18). |
| R-2 | S4..S6 | union row CP-9 | 60 | Daily job never gains repository authority under Execute across all four services composed | PC-19/PC-22/PC-93 as above, run against the composed union. |
| R-3 | S5, S8 | V-5 schedule snapshots + V-9 wrappers | covered | No second deleter or schedule change | PC-65, PC-66, PC-105, PC-106. |
| R-4 | S6, S7, S8 | V-6, V-8, V-12 | covered | Delivery reaches the recipient; replay never deletes | PC-82..PC-86, PC-101. |
| R-5 | S9, S10 | V-10, V-11 | covered | Native seam cannot be bypassed by substitution | PC-17, PC-18, PC-117, PC-118. |

New controls beyond the parent's 118 (TestDesign assigns final IDs): **PC-119** post-lock recheck
skipped (`eligibility_is_rechecked_after_the_root_lock`), **PC-120** absent pointer retained in the
index (`absent_root_pointer_is_dropped_by_the_sweep`), **PC-121** receipt list mutated after await
(`Receipt_candidates_are_snapshotted_before_database_await`, landed), **PC-122** stale receipt clears
backlog (`Delayed_complete_inventory_cannot_clear_open_backlog`, landed), **PC-123** scratch grammar
accepts the final path component alone (`Scratch_registration_refuses_foreign_owner_and_late_adoption`).

### Checkpoints

Closed proposed list; TestDesign freezes it. One isolated build or an explicit reuse per row, one
exact filter per row, wildcard class names, `Expect` as the roster rule. Group names carry the lane.
Windows rows are not selectable on Linux and vice versa. Min values are floors derived from the
rosters above; none is below the parent's corresponding floor.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c987-compat-tool/`| linux-compat-toolcopy | `/*/*/HostCleanupCheckpointCompatibilityTests*/(nested_live_executor_vetoes_whole_root*)\|(unknown_nested_custody_vetoes_whole_root*)\|(wait_uses_custody_not_progress_pid*)\|(next_start_uses_the_shared_veto*)\|(stop_validates_generation_before_signaling*)\|(stop_waits_for_confirmed_exit*)\|(age_clean_cannot_bypass_live_custody*)\|(evidence_write_has_no_raw_delete_bypass*)\|(automatic_recovery_preserves_all_evidence*)\|(sweep_never_signals_a_process*)\|(failed_delete_receipt_is_truthful_and_retryable*)\|(completed_wait_removes_only_tool*)\|(already_absent_is_idempotent*)` | R-1a | all listed, 0 failed; 16 executions | 16 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c987-compat-sweep/`| linux-compat-sweep-scope | `/*/*/HostCleanupCheckpointCompatibilityTests*/*` | R-1a, R-1b | all listed, 0 failed; 54 executions (run once pre-move, once post-move, reported separately) | 54 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c987-adapter/`| linux-checkpoint-adapter | `/*/*/HostCleanupCheckpointAdapterTests*/*` | V-4 | all listed, 0 failed | 14 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S3 | CP-3 | linux-cleanup-custody-union | `/*/*/(HostCleanupOwnershipTests*)\|(HostCleanupCheckpointAdapterTests*)\|(HostCleanupCheckpointCompatibilityTests*)/*` | V-3, V-4, R-1, R-3 | all listed, 0 failed; 12 + 14 + 54 | 80 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S4 | `tests/Antiphon.Tests -> bin-c987-inventory/`| linux-worktree-inventory | `/*/*/HostCleanupWorktreeInventoryTests*/*` | V-13, R-2 | all listed, 0 failed | 12 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S5 | `tests/Antiphon.Tests -> bin-c987-orchestration/`| linux-orchestration | `/*/*/HostCleanupOrchestrationTests*/*` | V-5, R-3 | all listed, 0 failed; 14 + 2 schema | 16 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S6 | `tests/Antiphon.Tests -> bin-c987-delivery/`| linux-report-delivery | `/*/*/HostCleanupReportTests*/*` | V-6, R-4 | all listed, 0 failed; 20 + 2 landed | 22 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S6 | CP-7 | linux-maintenance-gate | `/*/*/HostCleanupMaintenanceTests*/*` | V-7, R-2 | all listed, 0 failed | 10 | 4 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | S6 | `tests/Antiphon.Tests -> bin-c987-service/`| linux-cleanup-service-union | `/*/*/(HostCleanupOrchestrationTests*)\|(HostCleanupReportTests*)\|(HostCleanupMaintenanceTests*)\|(HostCleanupWorktreeInventoryTests*)/*` | V-5, V-6, V-7, V-13, R-2, R-3, R-4 | all listed, 0 failed; 16 + 22 + 10 + 12 | 60 | 15 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | S7 | `tests/Antiphon.SessionRunner.Tests -> bin-c987-runner/`| linux-cleanup-runner | `/*/*/HostCleanupCommandTests*/*` | V-8, R-4 | all listed, 0 failed | 12 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | S8 | `tests/Antiphon.Tests -> bin-c987-scripts/`| linux-cleanup-scripts | `/*/*/HostCleanupScriptTests*/*` | V-9, R-3 | all listed, 0 failed | 12 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | S8 | n/a| linux-attention-client | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/attention/attentionVisuals.test.ts` | V-12, R-4 | 29 existing (+1 if a backlog kind is added); 0 failed | n/a | 2 | true | n/a |
| CP-13 | S8 | `tests/Antiphon.Tests -> bin-c987-policy/`| linux-cleanup-policy-rerun | `/*/*/(HostCleanupPolicyTests*)\|(HostCleanupExecutionTests*)/*` | V-1, V-2, R-2 (regression after S3 registry change) | all listed, 0 failed | 42 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-14 | S9 | `tests/Antiphon.Tests -> bin-c987-linux/`| linux-native-adapter | `/*/*/HostCleanupLinuxTests*/*` | V-10, R-5 | all listed, 0 failed; Linux only | 6 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-15 | S10 | `tests/Antiphon.Tests -> bin-c987-windows/`| windows-native-adapter | `/*/*/HostCleanupWindowsTests*/(Swapped_junction_preserves_outside_sentinel*)\|(Case_and_path_aliases_cannot_escape_root*)\|(Held_file_returns_partial_and_retains_marker*)\|(File_id_substitution_refuses_delete*)\|(Long_path_uses_bound_handles*)\|(Windows_boot_and_start_uncertainty_is_retained*)` | V-11, R-5 | all listed, 0 failed; Windows only | 6 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | S10 | CP-15 | windows-ps51-wrappers | `/*/*/HostCleanupWindowsTests*/Windows_powershell_51_parses_and_runs_wrappers_offline*` | V-11, R-3 | 1 result; real powershell.exe 5.1 with recording cmdlets; zero native delete/restart/register | 1 | 4 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

Direct-row form, when the checkpoint tool is not used (environment set before the test host):

```powershell
$env:C804_ORPHAN_SWEEP_ROOT = 'c826-disabled'
$env:TUNIT_MAX_PARALLEL_TESTS = '1'
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-5 -Project tests/Antiphon.Tests -OutputPath bin-c987-inventory/ -Filter '/*/*/HostCleanupWorktreeInventoryTests*/*' -MinExecuted 12 -Expect HostCleanupWorktreeInventoryTests -ExpectedSourceSha (git rev-parse HEAD) -ResultsRoot .antiphon/c987-checkpoints
```

### Cost (estimates, not measurements)

Ordinary Linux rows: 8 + 12 + 6 + 6 + 10 + 12 + 10 + 4 + 15 + 10 + 10 + 2 + 8 + 6 = **119 minutes**
(CP-2 is run twice, pre- and post-move: +12). Windows rows: **12 minutes**. Red-first rounds reuse
the same rows and can add up to the same again. Authoring across S1..S10 is roughly 9-11 hours of
Code time at the stated 30-60 minute envelopes (ten dispatches plus two Windows rows). Mutation
remains the parent's 119 variants plus five new controls at about 4 minutes each: approximately
**496 + 30 = 526 minutes**, post-land SourceLanding only.

### Pending Mutation controls (post-land, not run here)

All of PC-1..PC-118 (119 prescribed variants, including PC-19's two overload variants) remain pending
for SourceLanding Mutation; none was run in this task or in the landed CARD-0826 slice. New controls
PC-119..PC-123 above need commissioning. Exact filters are `/*/*/<Class>*/<method>*` with the method
names in the parent's PC table; PC-18/PC-117/PC-118 are Windows-lane mutations, PC-102 is portable.
Keep every cycle method-scoped and restore each mutation before the next.

## Risks, rollout and stop conditions

- **Extraction regressions.** The move in S2 is the riskiest edit. The pre-move CP-2 green run is
  mandatory and is reported as its own run; a move with only a source-count claim is insufficient.
- **Collision with CARD-1032/1033.** Runner contracts, dispatcher, Hangfire registration and land
  service are shared. S5, S6 and S7 defer while those owners have an in-flight Code or Review task;
  a 409 or a same-file footprint is a defer, not a merge.
- **No activation in Code.** `HostCleanup:Enabled=false` and `Execute=false` stay configured on every
  namespace. Activation steps 1-7 and rollback remain exactly the parent's section; the per-host
  dry-run, the seven-hold disposition, the weekly schedule cutover and the build-slots image
  recreation are operator actions after independent Review and land.
- **Session that waits for input.** This plan adds no session that waits for input; the daily job
  spawns no agent and the runner command returns a receipt or a deferral.
- **Stop conditions.** A row that selects zero tests, a build failure, a fixture exception or a
  timeout is not red-first evidence and is reported as not run. Any lease-counter assertion other
  than zero in CP-5/CP-9 stops the slice until the reachable graph is corrected.
