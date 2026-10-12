# CARD-0987: CARD-0826 remaining slices (S2 extraction, S3 orchestration, S4 transport, S5 operations, native)

Plan task: `40fe843e-210c-4d45-91b9-0f1cbd5b2ca4` (Plan, Frontier, 2026-10-12). Inspected
checkout and fetched `origin/master`: `ce470787aa054ee2d5c0fbdfd7a63a49645d4a96`. **Amended in place by
Plan task `04839ba2-4569-48eb-9870-445321244f5e` (Frontier, 2026-10-12) from
`c7e8ff5bbbad2956ec4a7d5fe37f73cd0b123930`** to resolve the two blockers the frozen
[verification design](2026-10-12-card-0987-test-design.md) (TestDesign task `88ffed25`, findings 6 and 7)
raised against handing S1 to Code: the sweep-seam gap behind two of the 16 tool-copy ports, and the
S7-before-S6 delivery order. The amendment adds D-10..D-12, splits S1 into S1a/S1b and S6 into S6a/S6b,
adds the dependency graph, refreshes the collision map from a live read, and changes only the checkpoint
rows that the split forces (CP-17 added; CP-1, CP-7, CP-8, CP-9 `After` moved). OD-1..OD-5, the frozen
roster (54 / 60 / 30) and every PC binding are unchanged. Parent plan:
[2026-10-01-card-0826-daily-host-cleanup-plan.md](2026-10-01-card-0826-daily-host-cleanup-plan.md)
(frozen D-1..D-7, V-1..V-13, R-1..R-5, PC-1..PC-118). Status owner:
[docs/host-cleanup.md](../../host-cleanup.md). **Plan amended; next: code (first slice S1a; start
condition: CARD-1199's repair has a reviewed landing and the S1a start tree contains it).** The
`## Verification design` below is the frozen TestDesign table with the amendment's forced row changes;
the test design's own table carries the identical rows. No build, test, schedule, deploy or deletion ran
in either Plan task; post-land Mutation was **not** run.
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
| Platform: pin a runner per row. | `GET /api/runner-defaults` revision 2: global default `server2`, no kind overrides. `GET /api/session-runners` at the original read: `desktop` windows capacity 2 occupied 0; `server2` linux capacity 10 occupied 5; `server2-temp` unavailable. Re-read by the amendment (2026-10-12 18:00 UTC): revision 2 unchanged; `server2` occupied 7 of 10 and dispatch-eligible; `desktop` 0 of 2; `server2-temp` stale and draining. | Linux rows name the `linux-` lane and omit `-Runner`; only CP-15/CP-16 (Windows) and the Windows activation dry-run need `-Platform Windows` (pin `-Runner desktop` only if a second Windows host appears). |
| S1's tool-only seams can execute all 16 tool-copy ports (R-1a). | `nested_live_executor_vetoes_whole_root` and `unknown_nested_custody_vetoes_whole_root` (`tests/Antiphon.Tests/Checkpoints/CheckpointToolCopyCleanupTests.cs:13-33`) write a root marker with `TestRootGuard.Write`, mark a nested run with `CheckpointFixtures.MarkRun` (`RunOwnershipStore.Write`), then call `new CheckpointTempRootSweep(sandbox, options: ...)`, `Register` and `SweepOnce`. The sweep (`tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweep.cs`, 442 lines) has 49 static I/O or clock sites: `Directory.*`, `File.*`, `FileStream` for the coordinator gate, cursor state, legacy index and bounded truncation, `FileInfo.Length`, `Stopwatch` for the duration budget and `DateTime.UtcNow` in `RemoveStaleIndexTemps`; only the calendar clock is injectable. Its nested-custody veto in `Eligible` reads `RunOwnershipStore.Read` and `new ToolCopyCleanup(_probe).ObserveExecutor`. No other tool-copy port names the sweep. | Two of the 16 ports cannot run virtually until the sweep itself takes the instance port, which tool seams alone cannot give. D-10 splits S1 into S1a (tool seam, 14 tool-only ports, new row CP-17) and S1b (sweep over the port, no move, the 2 sweep-backed ports, CP-1 = 16). Floors unchanged: CP-1 16, CP-2 54, CP-4 80. |
| S6's five missing report names can close with the scripted transport from S4. | The frozen design binds `Receipt_to_attention_reaches_busy_recipient`, `Receipt_to_attention_reaches_ready_recipient`, `Crash_before_enqueue_recovers_pending_receipt` and `Crash_after_ingestion_before_ack_queries_status` to recipe Q: the producer runs through the runner's real `HostCleanupLocalStore`/outbox (S7) to the authenticated receipt route (delivery path DQ-2); fake serialized success cannot close CP-7/CP-9. `ScriptedHostCleanupTransport` is admissible only for S5 registration/admission. | D-11: S7 precedes delivery verification; S6 splits into S6a (maintenance closure, CP-8, needs nothing from S5/S7) and S6b (delivery verification, CP-7 and the CP-9 union, start tree contains S5, S6a and S7). |
| V-12 is 29 or 30 and S8 may add `shows sustained worktree backlog with its responsible owner`. | `client/src/features/attention/attentionVisuals.test.ts` has 24 plain `it` and one six-row `it.each` (30 results); that title exists at line 147; `WorktreeCleanupBacklog` is already attention kind 56 (test design finding 3). | S8 adds no Vitest case; CP-12 expects 30 as a regression row; S6b reuses kind 56 and introduces no `HostCleanupBacklog` enum name. |
| `After` tokens must be `S<n>`. | `tools/Antiphon.Checkpoints/Manifest/AfterSelector.cs`: `ExpandOne` returns any non-range token verbatim and `IsSelected` compares tokens as strings; only `S<n>-S<m>` ranges expand numerically. | `S1a`, `S1b`, `S6a`, `S6b` are valid `After` tokens (D-12). Select rows with `--rows CP-n`; an `S1-S10` range does not expand to the suffixed names. |

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

### D-10. Resolve the S1 seam gap by splitting S1: the sweep takes the instance port before any port that calls it

The two blocked ports need the still-unmoved sweep to run over the virtual port (ground truth row 14).
The three options in the amendment brief were weighed against the tree:

- *Widen S1 to carry the sweep seam.* S1 was already a 60-minute slice: the port interface, ten tool
  files (57 static sites: `CheckpointApp` 21, `Program` 15, `RunStateStore` 8, `EvidenceFolder` 7,
  `WaitCommand` 6, plus the six `Cleanup/` files), the fixture and 16 ports. Threading the sweep's 49
  sites, replacing `Stopwatch` with the port's monotonic clock and `DateTime.UtcNow` with the injected
  clock adds roughly 25 minutes, so the brief's own rule forces a re-split. **Chosen, as the split below.**
- *A new seam-only slice before S1.* The port interface must exist before the sweep can consume it, and
  a slice whose only regression guard is the physical checkpoint classes selects suites this plan
  excludes; a seam slice with no C987 row has no checkpoint. Rejected as a standalone slice; its content
  is S1b, which carries two ports and CP-1.
- *Re-home the two ports to S2 with a custody row.* CP-1 is frozen at 16. Moving two ports out leaves
  S1 with 14 and either lowers CP-1 or pushes the 16-row to S2's pre-move SHA, where S2 would carry the
  sweep seam, the scope seam, 40 ports, two CP-2 runs and the move: well over 60 minutes. Rejected.

Result: **S1a** = `ICheckpointCleanupIo` (complete from the first commit: file, directory, stream open
modes, attributes, last-write time, length, monotonic timestamp/elapsed, UTC now, process identity),
`CheckpointNativeIo` default, `ExecutorImageGuard`, tool threading, fixture, the 14 tool-only ports,
new row **CP-17 (14)**. **S1b** = the sweep's constructor gains the same port with the native default,
all 49 sites thread through it, `Stopwatch`/`DateTime.UtcNow` go through the port, nothing moves, the 2
sweep-backed ports are added, **CP-1 (16)** runs. The scope wrapper's port (finding 6, last sentence)
stays in S2 because no tool-copy port touches `CheckpointTestScope`. Floors: CP-17 14 (new), CP-1 16,
CP-2 54, CP-4 80; nothing is lowered.

### D-11. S7 precedes delivery verification; S6 splits into maintenance closure (S6a) and delivery verification (S6b)

Finding 7 binds the four delivery names to the runner's real local store/outbox (recipe Q, DQ-2). The
maintenance closure (`HostMaintenanceService` barrier hook, `AgentTaskLandService` intent publication,
`Land_waits_only_for_bounded_inflight_delete`, CP-8) needs nothing from S5 or S7 and collides only with
CARD-1033's land-service slices, so it becomes **S6a** and may be dispatched as soon as S4 has landed
and that gate is open. **S6b** carries the server-side receipt recovery, the status-before-retry route,
the public attention recipient, the five missing report names, CP-7 and the 60-case CP-9 union, and
starts from a tree containing S5, S6a and S7. S5 keeps `ScriptedHostCleanupTransport` for
registration/admission and does not wait for S7. The recommended order S4 -> S7 -> S5 -> S6a -> S6b
follows the frozen design's conservative choice: S7's CARD-1032 gate is the slowest, and S5's Hangfire
gate can open later without idling the pipeline. Rejected: one S6 after S7 (the land-service collision
would then also hold delivery work that never touches that file) and faking serialized success in CP-7
(refused by the frozen design).

### D-12. Suffixed slice names, selection by row id

`S1a`, `S1b`, `S6a`, `S6b` keep every frozen reference to S2..S10 valid (collision tables, guard labels
`S2:`/`S6:`/`S7:`, D-8, scope lines). `AfterSelector` accepts them verbatim (ground truth row 17). Code
dispatches select rows with `--rows CP-n`, as the frozen design already prescribes. Rejected:
renumbering S2..S10 to S3..S12, which touches every frozen cross-reference for no verification gain.

### Operator decisions

| ID | Question | Recommendation (default used below) |
|---|---|---|
| OD-1 | Accept D-1 (54-execution roster, CP-2 floor 80) or hold the frozen 40? | Accept 54. The parent says floors are never lowered; this raises one. |
| OD-2 | Accept the CP-3 split (D-3) with a final 60-case union rerun? | Accept. Same coverage, dispatchable in 30-60 minute tasks. |
| OD-3 | Should the daily job's report list CARD-1017 as an owner before CARD-1033 lands its scheduler? | Yes, name it now; the owner-status probe returns `owner_unavailable` until that owner exposes availability. No call is made either way. |
| OD-4 | Build-slots image recreation (card item d) and smoke disk evidence (item c) are deploy actions. Keep them in S8 as offline-tested script changes only, with the live redeploy left to the next authorized rollout? | Yes. No redeploy from a Code task. |
| OD-5 | Hold expiry for the seven historical server2 prefixes is already past (2026-10-08). Keep them as `hold_expired_review_required` with no disposition change? | Yes. Expiry is review, not permission (parent D-5). |

The amendment needs no new operator decision: D-10..D-12 change slice boundaries and order only, never a
floor, a behaviour, a PC binding or an activation step.

## Landable slices and collision map

Paths: `L=src/Antiphon.HostCleanup/`, `A=server/Application/`, `I=server/Infrastructure/`,
`T=tests/Antiphon.Tests/`, `R=tests/Antiphon.SessionRunner.Tests/`. Every slice lands with the
feature still unwired and `Execute=false`; S5 is the only slice that touches `server/Program.cs`
and it registers under `HostCleanup:Enabled=false`. Each slice commits and pushes before its rows.

| Slice / budget | Files (new marked **new**) | Behaviour and tests |
|---|---|---|
| **S1a / 55 min: instance I/O port and tool-side threading (14 tool-only ports)** | `tools/Antiphon.Checkpoints/Cleanup/{ToolCopyCleanup,OutputCleanup,RunOwnership,TestRootGuard,ProcessIdentity}.cs`; `tools/Antiphon.Checkpoints/{CheckpointApp,Program}.cs`, `Commands/WaitCommand.cs`, `Evidence/EvidenceFolder.cs`, `State/RunStateStore.cs`; **new** `L/Checkpoints/ICheckpointCleanupIo.cs` (filesystem, stream open modes, attributes, last-write time, length, monotonic timestamp/elapsed, UTC clock, process identity: the complete surface both the tool and the sweep need, fixed here so S1b changes no interface), **new** `L/Checkpoints/CheckpointNativeIo.cs` (production default), **new** `L/Checkpoints/ExecutorImageGuard.cs`; `L/Antiphon.HostCleanup.csproj`; `tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj` gains a library reference (one direction only). | No logic moves. Static facades forward to instances; production defaults stay native; no mutable global override. `EvidenceFolder.IsExecutorImage` forwards to `ExecutorImageGuard`. Tests: **new** `T/Infrastructure/HostCleanupCheckpointFixture.cs` (virtual I/O, recording process control, fake delay, fake monotonic clock) and the 14 tool-only ports in **new** `T/Infrastructure/HostCleanupCheckpointCompatibilityTests.cs` (exactly CP-17's named list). The two sweep-backed names are not authored here. CP-17. |
| **S1b / 40 min: the unmoved sweep takes the instance port (2 sweep-backed ports)** | `T/Checkpoints/CheckpointTempRootSweep.cs` (constructor gains `ICheckpointCleanupIo? io` with the native default; all 49 `Directory`/`File`/`FileStream`/`FileInfo`/`Stopwatch`/`DateTime.UtcNow` sites thread through the instance; `RemoveStaleIndexTemps` ages against the injected clock; the coordinator gate, the root lock via `TestRootGuard.TryLock`, cursor state and legacy index open through the port; `Eligible` passes the port to the `ToolCopyCleanup`/`RunOwnershipStore`/`ExecutorOwnershipStore` instance reads); `CheckpointTempSweepAssemblyHook` unchanged. No file moves; `CheckpointTestScope.cs` untouched. | Same behaviour at the native default. Tests: the compatibility class gains `nested_live_executor_vetoes_whole_root` and `unknown_nested_custody_vetoes_whole_root` over the virtual sweep (virtual root and marker, virtual nested run custody, scripted probe). CP-1 (all 16 tool-copy names). Stop condition: CP-1 selecting fewer than 16, or passing only with a physical sandbox, means a site is still static and the row is reported as not run. |
| **S2 / 60 min: scope port, sweep and scope ports (pre-move), then the move** | `T/Checkpoints/CheckpointTestScope.cs` (TempDir/DisposeAsync take the same port: marker create-new stream, empty-root rollback, nested custody read, recursive delete, child observation through the probe; uses `TestRootGuard.MarkerName`), `T/Checkpoints/CheckpointTempRootSweep.cs` (already threaded by S1b; core extracted to **new** `L/Checkpoints/CheckpointTempRootSweep.cs`; wrapper keeps `CheckpointTempSweepAssemblyHook`/roster glue), `tests/Antiphon.Checkpoints.LifecycleHost/Antiphon.Checkpoints.LifecycleHost.csproj` (direct library reference, linked wrapper retained); compatibility test file gains the remaining 29 methods (38 executions: sweep 27, scope 11). | Pre-move: commit the scope port and the ports, run CP-2 green against the unmoved, already-threaded implementation (declared preparatory run). Move, rerun CP-2 identical. `actual_deleted_bytes_are_bounded` truncation stays lifecycle-only. CP-2. |
| **S3 / 45 min: daily adapter and scratch registry** | **new** `L/Checkpoints/DailyCheckpointAdapter.cs` (24-hour gate over shared core, shared root gate, no `SweepOnce` shortcut), **new** `L/ScratchRegistry.cs` (register/status/release records, exact path + owner generation, two-level scratch grammar), `L/CleanupFamilyRegistry.cs` (task-scratch family binds registry records). | **new** `T/Infrastructure/HostCleanupCheckpointAdapterTests.cs`: the frozen 14 V-4 names. `Shared_library_has_no_test_or_tool_dependency` inspects assembly references. CP-3, then CP-4 union (80). |
| **S4 / 60 min: server fixtures, read-only Git probe and inventory closure** | `T/Application/HostCleanupFixture.cs` gains `ThrowingRepositoryMutationLease` (both overloads counted), `ForbiddenRetirementProbe`, `RecordingReadOnlyGitProbe`, `ScriptedExistingOwnerStatusProbe`, `ScriptedHostCleanupTransport`, `RecordingRecurringJobManager`; **new** `A/Interfaces/IHostCleanupTransport.cs`; **new** `I/Git/HostCleanupWorktreeProbe.cs` (`GIT_OPTIONAL_LOCKS=0`, `ls-remote` + local objects, never fetch/prune/remove/update-ref); `A/Services/HostCleanupWorktreeInventory.cs` (owner names incl. CARD-1017, `Only_owned_scratch_child` rule); **new** `A/Services/HostCleanupDailyRun.cs` (mixed preview/execute graph over inventory + library executor; no lease type reachable). | `T/Application/HostCleanupWorktreeInventoryTests.cs` gains the six missing names; `Daily_graph_has_no_direct_lease_or_lock_escape` walks reachable daily types for `RepositoryMutationLease` construction, `landing.lock` I/O, retirement calls. CP-5. |
| **S5 / 60 min: scheduling, catch-up, activation and authorization** | **new** `I/Agents/HostCleanupJob.cs`, `I/Agents/HostCleanupMonitorJob.cs`; `I/Agents/HangfireConfiguration.cs` (`AddOrUpdateHostCleanupJobs`, `AddOrUpdateHostCleanupMonitorJob`, 10:00 Europe/London + deterministic 0-30 min host offset, 15-min monitor); **new** `A/Services/HostCleanupScheduler.cs` (per-storage job ids, alias collapse, last-completed-date catch-up, namespace qualification + config digest, weekly-cutover confirmation flag, seven-prefix hold resolution); `A/Settings/HostCleanupSettings.cs` + validator (hosts/roots/storage ids, `WeeklyBuildJunkDisabledConfirmed`, `Qualified` per namespace); **new** `server/Api/Endpoints/HostCleanupEndpoints.cs` (GET hosts, GET runs/{id} paged, POST preview, POST hold, POST maintenance, POST receipts); `server/Program.cs` (DI + Hangfire registration under `HostCleanup:Enabled`), `server/appsettings.json` (`Enabled=false`, `Execute=false`). | `T/Application/HostCleanupOrchestrationTests.cs` gains the frozen 14 names; keeps the 2 landed schema tests. **new** `T/Application/HostCleanupTestServer.cs` maps only feature/attention/auth routes. CP-6. |
| **S6a / 30 min: maintenance closure and land participation** | `A/Services/HostMaintenanceService.cs` (bounded-operation syscall barrier hook), `A/Services/AgentTaskLandService.cs` (publish maintenance intent before `TryAcquireAsync` at line 475, finish after; no cleanup invocation). | `T/Application/HostCleanupMaintenanceTests.cs` gains `Land_waits_only_for_bounded_inflight_delete` using `MaintenanceBarriers`. Needs nothing from S5 or S7; dispatchable once S4 has landed and the CARD-1033 land-service gate is open. CP-8. |
| **S6b / 45 min: delivery verification (start tree contains S5, S6a and S7)** | `A/Services/HostCleanupService.cs` (status-before-retry query route, pending-outbox drain entry), **new** `A/Services/HostCleanupReceiptOutbox.cs` (server-side pending receipt recovery), `A/Services/HostCleanupAttentionService.cs` (public recipient through `AttentionService` read path), `A/Services/AttentionService.cs` (append host-cleanup projection), `A/Dtos/AttentionDtos.cs` (no new kind: reuse `WorktreeCleanupBacklog`, kind 56). | `T/Application/HostCleanupReportTests.cs` gains the five missing names (`Post_run_low_space_never_widens_deletion`, the busy/ready recipient pair, `Crash_before_enqueue_recovers_pending_receipt`, `Crash_after_ingestion_before_ack_queries_status`); the four delivery names drive S7's real runner local store/outbox through the receipt route (recipe Q). CP-7, then CP-9 union (60) at the composed tip. |
| **S7 / 60 min: runner contracts, command service, store and transport (precedes S6b)** | `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs` (`RunnerCapabilityFeatures.HostCleanupV1`), `PhoneHomeContracts.cs` (two operations, request/response records), **new** `src/Antiphon.SessionRunner.Contracts/HostCleanupContracts.cs`; **new** `src/Antiphon.SessionRunner/HostCleanupCommandService.cs`, `HostCleanupLocalStore.cs` (protected durable receipt outbox under the runner state root), `HostCleanupWorktreeInventory.cs` (read-only facts); `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs` (admission + routing only), `src/Antiphon.SessionRunner/Program.cs` (DI; no hosted timer), `Antiphon.SessionRunner.csproj` (library reference); **new** `I/Agents/SessionRunner/HostCleanupTransport.cs` implementing `IHostCleanupTransport` over `PhoneHomeRunnerClient.cs`. | **new** `R/HostCleanupFixture.cs` (real dispatcher, `RecordingPhoneHomeRuntime`, `LoopbackHostCleanupWire`, `VirtualRunnerDurableStore`), **new** `R/HostCleanupCommandTests.cs` with the frozen 12 V-8 names. CP-10. |
| **S8 / 60 min: worker, scripts, cutover, smoke evidence, docs** | **new** `tools/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj`, `Program.cs` (preview/status/execute/register/release/clean verbs); **new** `scripts/host-cleanup.ps1`, `scripts/task-scratch.ps1`, `scripts/host-cleanup-server2.sh`, `scripts/lib/host-cleanup-maintenance.ps1`, `scripts/fixtures/c826-cleanup-shim.ps1`, `scripts/windmill/antiphon-host-cleanup-server2.json` + `.schedule.json`; `scripts/cleanup-build-junk.ps1` (compatibility wrapper, no robocopy/Remove-Item), `scripts/install-cleanup-task.ps1` (refuse install, keep uninstall), `scripts/restart-apphost.ps1`, `scripts/restart-session-runner.ps1`, `scripts/session-runner-restart-health.ps1`, `dev-aspire.ps1`, `scripts/deploy-server2.ps1`, `scripts/verify-card0849-caches.ps1` (tmp volume identity + host free bytes in receipts), `docker/session-runner-grok/Dockerfile`, `docker-compose.server2-runner.yml` (publish worker, settings, no new mount); `scripts/windmill/README.md`, `server/Bundles/stage-code.md`, `stage-review.md` (self-clean rule), `docs/host-cleanup.md`, relevant sections of `docs/bootstrap.md`, `docs/logs.md`, `docs/testing-and-build.md`, `docs/ops-http.md`. | **new** `T/Scripts/HostCleanupScriptTests.cs`, `T/Scripts/HostCleanupScriptFixture.cs` with the frozen 12 V-9 names; `attentionVisuals.test.ts` is unchanged (the owner-display case already exists; 30 results) and CP-12 reruns it as a regression row. CP-11, CP-12. All scripts ASCII-only. |
| **S9 / 45 min: Linux native adapter** | **new** `L/ICleanupNativeApi.cs`, `L/LinuxCleanupNativeApi.cs`; `L/LinuxCleanupFileSystem.cs` (openat/fstatat/unlinkat semantics over the port, held parent handles, device/inode identity, EACCES partial). | **new** `T/Infrastructure/HostCleanupLinuxTests.cs` with the frozen 6 V-10 names over `HostCleanupNativeFixture(Linux)` added to `T/Infrastructure/HostCleanupFixture.cs`. CP-14. |
| **S10 / 60 min, Windows lane: Windows native adapter and 5.1 wrapper run** | **new** `L/WindowsCleanupNativeApi.cs`; `L/WindowsCleanupFileSystem.cs` (reparse-safe open flags, file-ID/volume identity, long-path handle binding, boot/start tolerance); no script changes beyond what S8 landed. | **new** `T/Infrastructure/HostCleanupWindowsTests.cs` with the frozen 7 V-11 names. CP-15 (6 adapter cases), CP-16 (`Windows_powershell_51_parses_and_runs_wrappers_offline`). Dispatch with `-Platform Windows`. |

### Dependency graph and dispatch order

Arrows are start-tree requirements (the later slice's branch must contain the earlier slice's landed
commits); the collision map adds the external gates.

- S1a -> S1b -> S2 -> S3 -> S4: a strict chain (port, sweep over the port, scope port plus ports plus
  move, daily adapter, server fixtures).
- S4 -> S7 (needs `IHostCleanupTransport` and the fixtures); S4 -> S5 (scripted transport only);
  S4 -> S6a (maintenance only; nothing from S5 or S7).
- {S5, S6a, S7} -> S6b: delivery verification, with the CP-9 union at the composed tip.
- S6b -> S8 (worker and scripts consume the runner command surface and the receipt protocol);
  S3 -> S9 (library seam only); S8 -> S10 (the wrappers must exist for CP-16).

Recommended serial order: **S1a -> S1b -> S2 (pre-move, move) -> S3 -> S4 -> S7 -> S5 -> S6a -> S6b
-> S8 -> S9 -> S10**. S6a may be pulled forward to any point after S4 when its CARD-1033 gate opens
before CARD-1032's runner gate; S9 may run beside S5..S8 (disjoint files). Never run two slices that
share a file concurrently.

Collision map. Live read 2026-10-12 18:00 UTC (`GET /api/agent-tasks?boardId=<Antiphon>`): CARD-1199
repair 10 Code `71060864` Dispatched and repair 7 `44a8eca2` Blocked; CARD-1032 S2 repair `cac7e685`
Succeeded 17:54 UTC with its r2 Review `d8992498` Dispatched; CARD-1033 S3 Code `b113f1fa` Canceled
17:47 UTC pending CARD-1032, and no open CARD-1033 or CARD-1017 task; CARD-1042 Review `defe909e`
Blocked on the desktop. Recheck active task footprints before every dispatch; serialize, never run two
owners of one file concurrently:

| Other work | Shared files with this plan | Gate |
|---|---|---|
| CARD-1199 (Code repair in flight) | The checkpoint tool's build-output cleanup and `--keep-outputs` path: `OutputCleanup.cs`, `CheckpointApp.cs` and `Program.cs` are S1a threading targets; `run-checkpoint.ps1` is the driver every row uses | S1a, S1b and S2 wait for its reviewed landing, then re-read the repaired caller graph before threading. |
| CARD-1032 (InProgress; Review in flight) | `PhoneHomeContracts.cs`, `SessionRunnerContracts.cs`, `PhoneHomeCommandDispatcher.cs`, `PhoneHomeRunnerClient.cs`, runner `Program.cs`, `AppDbContext.cs`/migrations | S7 waits for the in-flight Review and any later runner removal/fence/transport owner on these paths to land; re-read capability/operation allocations first. S5 adds no migration (tables exist). |
| CARD-1033 (InProgress; S3 canceled pending CARD-1032) | `HangfireConfiguration.cs` and `server/Program.cs` (its S17 composition); `AgentTaskLandService.cs` (its S7 and S11); `WorktreeResidueSweepService.cs` (not edited here) | S5 waits for the composition slice's reviewed landing and re-reads its registration block; S6a waits for both land-service slices to land and no newer land-service owner; S6b waits for S5, S6a and S7 of this plan. A Review column is not proof that file owners have landed. |
| CARD-1017 (InProgress; no open task) | None directly; CARD-1032 carries its remote-owner work | Inventory names it as an owner (D-4); no authority transfer. |
| CARD-1042 Review (Blocked, desktop); CARD-1192 S4 and CARD-0955 Code (server2) | CARD-1042 names four evidence test files plus an allowlist; the two Code tasks list empty scope strings | Read each brief footprint before S5, S6a, S6b and S8; defer on any exact-file overlap. An empty scope is not absence. |
| CARD-0692 (Review) | None landed yet; the shared released-scratch seam is a future integration | If it lands first, integrate only its released-scratch entry into S3's registry; no second cleanup stage. |

Scope line for Code dispatches is the parent's two scope lines **plus**
`tests/Antiphon.Tests/Scripts/HostCleanup*,src/Antiphon.HostCleanup/Checkpoints/**,src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs,server/Application/Services/HostCleanupDailyRun.cs,server/Application/Services/HostCleanupScheduler.cs,server/Application/Services/HostCleanupReceiptOutbox.cs,tests/Antiphon.Tests/Application/HostCleanupTestServer.cs,tools/Antiphon.Checkpoints/CheckpointApp.cs,tools/Antiphon.Checkpoints/Program.cs,tools/Antiphon.Checkpoints/Commands/WaitCommand.cs,tools/Antiphon.Checkpoints/Evidence/EvidenceFolder.cs,tools/Antiphon.Checkpoints/State/RunStateStore.cs,scripts/lib/host-cleanup-maintenance.ps1,scripts/session-runner-restart-health.ps1,dev-aspire.ps1`
and **minus** `tests/Antiphon.Tests/Infrastructure/HostCleanupScriptTests.cs` (relocated). Each slice
dispatches with only the subset it edits.

## Verification design

Frozen by TestDesign (task `88ffed25`, source `7df4121900be7c7e7e212caaf60e4e03508c46de`) and amended here
only for the rows the slice split forces; the test design's method ledger binds every name and the 260
controls. Method names are the parent's frozen names unless a ground-truth row changed them. Every
TUnit row sets `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1`, builds to a
forward-slash isolated output, and runs through `scripts/run-checkpoint.ps1` or the checkpoint tool
(`run --plan <this file> --rows <selection> --expected-source-sha <sha>`, then `wait` until exit is
not 75). No row selects a physical checkpoint class, the Unit lane or a provider suite.

### Verification matrix

| ID | Slice | Class (file) | Executions | What it proves | Red proof (case -> mutation -> named failure) |
|---|---|---|---:|---|---|
| R-1a | S1a, S1b | `HostCleanupCheckpointCompatibilityTests` tool-copy ports (16: the 14 tool-only names at S1a via CP-17, all 16 at S1b via CP-1) | 16 | Existing tool-copy custody/evidence/stop contracts survive instance I/O; the two nested-custody vetoes run the sweep over the port | `automatic_recovery_preserves_all_evidence`: delete the report beside the tool in recovery -> evidence hash assertion fails (PC-109); `nested_live_executor_vetoes_whole_root`: ignore the nested live executor in the sweep -> `CompletedRoots == 0` fails (PC-201). |
| R-1b | S2 | same class, sweep (27) + scope (11) ports | 38 | Sweep bounds, root-lock recheck, absent pointer, scope teardown contracts hold before and after the move | `root_delete_count_is_bounded`: ignore MaxRoots -> `CompletedRoots == 2` fails (PC-111); `eligibility_is_rechecked_after_the_root_lock("live")`: skip post-lock recheck -> retained-count assertion fails (new control PC-119). |
| V-4 | S3 | `HostCleanupCheckpointAdapterTests` | 14 | Daily gate stricter than lifecycle; shared root gate; no test/tool dependency; image guard; launch/ack custody | `Daily_age_is_stricter_than_lifecycle_grace`: call `SweepOnce` without the 24-hour gate -> one-hour root deleted, keep assertion fails (PC-56). |
| V-13 | S4 | `HostCleanupWorktreeInventoryTests` (12) | 12 | Zero lease attempts across a full mixed run; graph has no lease/lock escape; execute preserves would-remove worktrees; read-only Git | `Full_run_never_acquires_repository_lease`: insert `TryAcquireAsync` in the daily run -> counter assertion `== 0` fails (PC-19, two overload variants). |
| V-5 | S5 | `HostCleanupOrchestrationTests` (14 + 2 schema) | 16 | Main-only registration, alias collapse, untouched residue/Claude schedules, cutover confirmation, deferred unsupported/offline, one catch-up per day, scoped auth, hold resolution, per-namespace qualification | `Restart_catchup_runs_once_per_local_day`: enqueue every missed day -> dispatch-count `== 1` fails (PC-68). |
| V-6 | S6b | `HostCleanupReportTests` (20 + 2 landed) | 22 | Busy/ready recipient GET, crash-before-enqueue and lost-ack recovery, low-space never widens caps | `Crash_after_ingestion_before_ack_queries_status`: re-execute on lost ack -> execute-count `== 1` fails (PC-84). |
| V-7 | S6a | `HostCleanupMaintenanceTests` (10) | 10 | Land waits only for the bounded in-flight delete; cleanup yields to intent | `Land_waits_only_for_bounded_inflight_delete`: acquire the land lease before drain -> lease-call-at-barrier `== 0` fails (PC-88). |
| V-8 | S7 | `HostCleanupCommandTests` (12) | 12 | Capability refusal, no request roots, bound identity, lost-reply dedup, restart replay without re-delete, no session side effects | `Lost_reply_returns_existing_receipt`: allocate a new claim on duplicate -> receipt-identity assertion fails (PC-16). |
| V-9 | S8 | `HostCleanupScriptTests` (12) | 12 | ASCII, scratch helper rules, wrapper has no remover, installer refuses, host bridge excludes volumes, smoke identities, restart entry points publish/drain, redaction | `Legacy_build_junk_wrapper_uses_shared_worker_only`: restore the robocopy branch -> `robocopy calls == 0` fails (PC-105). |
| V-12 | S8 | `attentionVisuals.test.ts` | 30 | Backlog owner display (existing case; regression row, no new kind) | `shows sustained worktree backlog with its responsible owner`: drop owner from label -> owner substring assertion fails. |
| V-10 | S9 | `HostCleanupLinuxTests` (6) | 6 | Real Linux adapter rejects link swap, mount crossing, path rebuild; EACCES partial; /proc identity | `Swapped_symlink_preserves_outside_sentinel`: follow the link at the native seam -> outside-sentinel/unlink-through-link assertion fails (PC-17). |
| V-11 | S10 | `HostCleanupWindowsTests` (7) | 7 | Reparse-safe opens, alias refusal, sharing-violation partial, file-ID revalidation, long paths, 5.1 wrapper offline | `Swapped_junction_preserves_outside_sentinel`: omit the reparse flag -> required-flag assertion fails (PC-18). |
| R-2 | S4..S6 | union row CP-9 | 60 | Daily job never gains repository authority under Execute across all four services composed | PC-19/PC-22/PC-93 as above, run against the composed union. |
| R-3 | S5, S8 | V-5 schedule snapshots + V-9 wrappers | covered | No second deleter or schedule change | PC-65, PC-66, PC-105, PC-106. |
| R-4 | S6b, S7, S8 | V-6, V-8, V-12 | covered | Delivery reaches the recipient; replay never deletes | PC-82..PC-86, PC-101. |
| R-5 | S9, S10 | V-10, V-11 | covered | Native seam cannot be bypassed by substitution | PC-17, PC-18, PC-117, PC-118. |

New controls beyond the parent's 118 (frozen by the test design, which binds 260 controls in total): **PC-119** post-lock recheck
skipped (`eligibility_is_rechecked_after_the_root_lock`), **PC-120** absent pointer retained in the
index (`absent_root_pointer_is_dropped_by_the_sweep`), **PC-121** receipt list mutated after await
(`Receipt_candidates_are_snapshotted_before_database_await`, landed), **PC-122** stale receipt clears
backlog (`Delayed_complete_inventory_cannot_clear_open_backlog`, landed), **PC-123** scratch grammar
accepts the final path component alone (`Scratch_registration_refuses_foreign_owner_and_late_adoption`).

### Checkpoints

Closed list, frozen by TestDesign and identical to the test design's table after this amendment
(CP-17 added; CP-1 After S1b; CP-8 After S6a; CP-7 and CP-9 After S6b). Rows are ordered by dispatch
prerequisite; CP ids are stable. One isolated build per row (no reuse remains), one exact filter per
row, wildcard class names, `Expect` as the roster rule. Group names carry the lane. Windows rows are
not selectable on Linux and vice versa. CP-2 is the single declared preparatory exception to
once-per-row (pre-move and post-move SHAs, each with its own build, output inventory and receipt).
Min values are floors derived from the rosters; none is below the parent's corresponding floor. Select
rows with `--rows CP-n`; an `S1-S10` range does not expand to `S1a`/`S1b`/`S6a`/`S6b`.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-17 | S1a | `tests/Antiphon.Tests -> bin-c987-compat-toolonly/` | linux-compat-toolonly | `/*/*/HostCleanupCheckpointCompatibilityTests*/(wait_uses_custody_not_progress_pid*)\|(next_start_uses_the_shared_veto*)\|(stop_validates_generation_before_signaling*)\|(stop_waits_for_confirmed_exit*)\|(age_clean_cannot_bypass_live_custody*)\|(evidence_write_has_no_raw_delete_bypass*)\|(automatic_recovery_preserves_all_evidence*)\|(sweep_never_signals_a_process*)\|(failed_delete_receipt_is_truthful_and_retryable*)\|(completed_wait_removes_only_tool*)\|(already_absent_is_idempotent*)` | R-1a (tool-only 14) | all listed, 0 failed/skipped; 14 executions (11 methods; `completed_wait_removes_only_tool` is 4 rows) | 14 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-1 | S1b | `tests/Antiphon.Tests -> bin-c987-compat-tool/` | linux-compat-toolcopy | `/*/*/HostCleanupCheckpointCompatibilityTests*/(nested_live_executor_vetoes_whole_root*)\|(unknown_nested_custody_vetoes_whole_root*)\|(wait_uses_custody_not_progress_pid*)\|(next_start_uses_the_shared_veto*)\|(stop_validates_generation_before_signaling*)\|(stop_waits_for_confirmed_exit*)\|(age_clean_cannot_bypass_live_custody*)\|(evidence_write_has_no_raw_delete_bypass*)\|(automatic_recovery_preserves_all_evidence*)\|(sweep_never_signals_a_process*)\|(failed_delete_receipt_is_truthful_and_retryable*)\|(completed_wait_removes_only_tool*)\|(already_absent_is_idempotent*)` | R-1a | all listed, 0 failed/skipped; 16 executions | 16 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c987-compat-sweep/` | linux-compat-sweep-scope | `/*/*/HostCleanupCheckpointCompatibilityTests*/*` | R-1a, R-1b | all listed, 0 failed/skipped; 54 executions (run once pre-move, once post-move, reported separately) | 54 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c987-adapter/` | linux-checkpoint-adapter | `/*/*/HostCleanupCheckpointAdapterTests*/*` | V-4 | all 14 listed executions, 0 failed/skipped | 14 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S3 | `tests/Antiphon.Tests -> bin-c987-custody-union/` | linux-cleanup-custody-union | `/*/*/(HostCleanupOwnershipTests*)\|(HostCleanupCheckpointAdapterTests*)\|(HostCleanupCheckpointCompatibilityTests*)/*` | V-3, V-4, R-1, R-3 | all listed, 0 failed/skipped; 12 + 14 + 54 | 80 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S4 | `tests/Antiphon.Tests -> bin-c987-inventory/` | linux-worktree-inventory | `/*/*/HostCleanupWorktreeInventoryTests*/*` | V-13, R-2 | all 12 listed executions, 0 failed/skipped | 12 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | S7 | `tests/Antiphon.SessionRunner.Tests -> bin-c987-runner/` | linux-cleanup-runner | `/*/*/HostCleanupCommandTests*/*` | V-8, R-4 | all 12 listed executions, 0 failed/skipped | 12 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S5 | `tests/Antiphon.Tests -> bin-c987-orchestration/` | linux-orchestration | `/*/*/HostCleanupOrchestrationTests*/*` | V-5, R-3 | all listed, 0 failed/skipped; 14 + 2 schema | 16 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S6a | `tests/Antiphon.Tests -> bin-c987-maintenance/` | linux-maintenance-gate | `/*/*/HostCleanupMaintenanceTests*/*` | V-7, R-2 | all listed, 0 failed | 10 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S6b | `tests/Antiphon.Tests -> bin-c987-delivery/` | linux-report-delivery | `/*/*/HostCleanupReportTests*/*` | V-6, R-4 | all listed, 0 failed/skipped; 20 + 2 landed | 22 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | S6b | `tests/Antiphon.Tests -> bin-c987-service/` | linux-cleanup-service-union | `/*/*/(HostCleanupOrchestrationTests*)\|(HostCleanupReportTests*)\|(HostCleanupMaintenanceTests*)\|(HostCleanupWorktreeInventoryTests*)/*` | V-5, V-6, V-7, V-13, R-2, R-3, R-4 | all listed, 0 failed/skipped; 16 + 22 + 10 + 12 | 60 | 15 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | S8 | `tests/Antiphon.Tests -> bin-c987-scripts/` | linux-cleanup-scripts | `/*/*/HostCleanupScriptTests*/*` | V-9, R-3 | all 12 listed executions, 0 failed/skipped | 12 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | S8 | n/a | linux-attention-client | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/attention/attentionVisuals.test.ts` | V-12, R-4 | 30 = 24 plain + 6 Arguments; 0 failed/skipped | n/a | 2 | true | n/a |
| CP-13 | S8 | `tests/Antiphon.Tests -> bin-c987-policy/` | linux-cleanup-policy-rerun | `/*/*/(HostCleanupPolicyTests*)\|(HostCleanupExecutionTests*)/*` | V-1, V-2, R-2 (regression after S3 registry change) | all 42 listed executions, 0 failed/skipped | 42 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-14 | S9 | `tests/Antiphon.Tests -> bin-c987-linux/` | linux-native-adapter | `/*/*/HostCleanupLinuxTests*/*` | V-10, R-5 | all listed, 0 failed/skipped; Linux only | 6 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-15 | S10 | `tests/Antiphon.Tests -> bin-c987-windows/` | windows-native-adapter | `/*/*/HostCleanupWindowsTests*/(Swapped_junction_preserves_outside_sentinel*)\|(Case_and_path_aliases_cannot_escape_root*)\|(Held_file_returns_partial_and_retains_marker*)\|(File_id_substitution_refuses_delete*)\|(Long_path_uses_bound_handles*)\|(Windows_boot_and_start_uncertainty_is_retained*)` | V-11, R-5 | all listed, 0 failed/skipped; Windows only | 6 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | S10 | `tests/Antiphon.Tests -> bin-c987-ps51/` | windows-ps51-wrappers | `/*/*/HostCleanupWindowsTests*/Windows_powershell_51_parses_and_runs_wrappers_offline*` | V-11, R-3 | 1 result; real powershell.exe 5.1 with recording cmdlets; zero native delete/restart/register | 1 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

Direct-row form, when the checkpoint tool is not used (environment set before the test host):

```powershell
$env:C804_ORPHAN_SWEEP_ROOT = 'c826-disabled'
$env:TUNIT_MAX_PARALLEL_TESTS = '1'
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-5 -Project tests/Antiphon.Tests -OutputPath bin-c987-inventory/ -Filter '/*/*/HostCleanupWorktreeInventoryTests*/*' -MinExecuted 12 -Expect HostCleanupWorktreeInventoryTests -ExpectedSourceSha (git rev-parse HEAD) -ResultsRoot .antiphon/c987-checkpoints
```

### Cost (estimates, not measurements)

Ordinary Linux rows (frozen `EstimatedMinutes`): CP-17 8, CP-1 8, CP-2 12, CP-3 6, CP-4 8, CP-5 10,
CP-10 10, CP-6 12, CP-8 6, CP-7 10, CP-9 15, CP-11 10, CP-12 2, CP-13 8, CP-14 6 = **131 minutes**;
Windows rows CP-15 8 + CP-16 6 = **14**; table sum **145**. The declared pre-move CP-2 invocation adds
12: **157** mandatory ordinary minutes. Red-first rounds reuse the same rows and can add up to the same
again. Authoring across S1a..S10 is roughly 10-12 hours of Code time at the stated 30-60 minute
envelopes (twelve Linux dispatches plus one Windows dispatch). Mutation is the frozen design's
**260 controls x 4 minutes + 30 = 1070 minutes**, post-land SourceLanding only.

### Pending Mutation controls (post-land, not run here)

All 260 controls frozen by the test design (the parent's PC-1..PC-118 plus PC-119..PC-260 bound there)
remain pending for SourceLanding Mutation; none was run in this task, in the amendment task, or in the
landed CARD-0826 slice. Post-land Mutation was **not** run. Exact filters are `/*/*/<Class>*/<method>*` with the method
names in the parent's PC table; PC-18/PC-117/PC-118 are Windows-lane mutations, PC-102 is portable.
Keep every cycle method-scoped and restore each mutation before the next.

## Risks, rollout and stop conditions

- **Extraction regressions.** The move in S2 is the riskiest edit. The pre-move CP-2 green run is
  mandatory and is reported as its own run; a move with only a source-count claim is insufficient.
- **Seam before ports.** S1b's CP-1 is the only proof that the sweep runs over the port. A CP-1 that
  selects fewer than 16, or that passes only with a physical sandbox, is reported as not run; S2 does
  not start until CP-1 is green at S1b's committed SHA.
- **Collision with CARD-1032/1033.** Runner contracts, dispatcher, Hangfire registration and land
  service are shared. S5, S6a, S6b and S7 defer while those owners have an in-flight Code or Review
  task, and S1a, S1b and S2 defer while CARD-1199's checkpoint-tool repair is in flight; a 409 or a
  same-file footprint is a defer, not a merge.
- **No activation in Code.** `HostCleanup:Enabled=false` and `Execute=false` stay configured on every
  namespace. Activation steps 1-7 and rollback remain exactly the parent's section; the per-host
  dry-run, the seven-hold disposition, the weekly schedule cutover and the build-slots image
  recreation are operator actions after independent Review and land.
- **Session that waits for input.** This plan adds no session that waits for input; the daily job
  spawns no agent and the runner command returns a receipt or a deferral.
- **Stop conditions.** A row that selects zero tests, a build failure, a fixture exception or a
  timeout is not red-first evidence and is reported as not run. Any lease-counter assertion other
  than zero in CP-5/CP-9 stops the slice until the reachable graph is corrected.
