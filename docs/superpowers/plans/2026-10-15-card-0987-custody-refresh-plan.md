# CARD-0987: remaining slices after the checkpoint custody repair

Date: 2026-10-15. Plan task: `e2b3297e-09a7-4420-8b05-ba993373fa4d`.
Source inspected: `903a4d15c039a6717ee5d81e7a4f859ed803a0f3` (assigned branch base).
Fetched target: `a30b7f4297400f010ce70188ac99ebe55be51931`; no difference from the base in
`src/Antiphon.HostCleanup`, `tools/Antiphon.Checkpoints`, `tests/Antiphon.Tests/Checkpoints`
or `server/Application/Services/HostCleanupService.cs` at that read.
**Plan refresh complete; TestDesign follows. No implementation or runtime verification is claimed.**

## Authority and change summary

This plan supersedes the remaining-slices plan and checkpoint selection at Git object
`17169b9d47f120b2cb7dcd6172d2c2cf0cfcd632`, paths
`docs/superpowers/plans/2026-10-12-card-0987-remaining-slices-plan.md` and
`docs/superpowers/plans/2026-10-12-card-0987-test-design.md`. Those files are not in this start tree;
the exact Git object was read, not merged. The new plan is a self-contained scope/selection contract.
TestDesign must commit a current method/guard/control ledger into the next Code start tree; the
historical 260-control ledger is input to that work, not an executable acceptance authority after this refresh.

The [CARD-0826 parent](2026-10-01-card-0826-daily-host-cleanup-plan.md) still owns the product
requirements and activation gates. [The S1a preflight](../evidence/2026-10-15-card-0987-s1a-preflight.md)
correctly identified two obsolete deletion oracles and an incomplete I/O footprint. This refresh:

- Changes startup compatibility to preservation of every pre-existing sibling tool, and age cleanup
  to preservation of unowned dead runs. It does not restore the removed deletion behavior.
- Extends S1a through SafeWrite/SafeDelete, both authority mechanisms, native metadata, verified
  receipt handles, parser/report binding and the bounded Git reader. Splits its work into S1a0,
  S1a1 and S1a, with useful virtual policy tests in each slice.
- Retains CP-17/CP-1/CP-2/CP-4 floors **14 / 16 / 54 / 80**. Supplemental custody/receipt/authority
  cases have separate rows; they cannot replace a missing compatibility case.
- Adds a small, explicit set of existing affected-class regression rows. The earlier blanket
  exclusion of physical checkpoint tests is superseded only for these named rows.
- Retains S1b before S2, S7 before S6b, the 60-case service union, Windows separation, all pending
  mutation obligations and all disabled activation defaults. Historical passing/failing outcomes
  in [the status document](../../host-cleanup.md) remain historical.

## Ground truth

Counts below are static source census, not executed results. CARD-0987 was read using
`pwsh -NoProfile -File scripts/card.ps1 get CARD-0987 -Board Antiphon`.

| Card or earlier plan assumes | Code at the inspected source | Required correction |
|---|---|---|
| `next_start_uses_the_shared_veto` removes the dead sibling tool | `CheckpointToolCopyCleanupTests.next_start_preserves_all_pre_existing_sibling_tools` asserts dead, unknown and unrelated tools all remain. `CheckpointApp.CreateRun` exclusively creates the new run and does not sweep old paths. | Rename the port and CP-17/CP-1 selectors; rebind G-204/PC-204 to sibling preservation. |
| `age_clean_cannot_bypass_live_custody` deletes the dead run | Its current body asserts `Deleted` empty, `output-ledger-missing`, and preservation of dead/live/reused/foreign-identity directories. | Retain the method name, change the oracle and G-206/PC-206 binding. Death and age supply no output custody. |
| CARD-1199 must land before S1a | Reviewed landing `e0877982c5dc41990c4e66b80cb47341a1bd0a41` is an ancestor. The above behavior changed in `ef0f0c5a2e34a9ed5d2e08f17845b2227ded2a5d`. | Prerequisite is present. Recheck new overlapping owners at dispatch; do not wait on the stale October 12 task list. |
| File/Directory/clock injection covers tool cleanup | `ToolCopyCleanup.Remove` reaches SafeWrite, SafeDelete, OwnedOutputLedger, ToolCopyAuthority, OutputCustody, ExclusiveDirectory, LedgerStat, ReceiptFile and ConfinedFileReader. SafeDelete uses CheckpointProcessRunner for bounded Git reads. SafeWrite has static creation/content/fresh-root dictionaries. | Instance-owned context with low-level observations and creation registry; production policy remains real. Include the full graph below. |
| Disk inventory is generic deletion authority | OwnedOutputLedger.Check requires current-process creation records. ToolCopyAuthority is a separate run-bound sealed capability; it must never import entries into that generic registry. | Keep the two authority paths distinct, including across a fresh wait context/process. |
| Completed wait needs only phase and dead PID | WaitCommand verifies required state/report/source binding via ReportWriter and ReceiptJson; ReceiptFile uses bounded, regular, single-link, verified-handle reads. | Thread the reader and report writer; retain strict shape/source/digest/terminal-exit checks and documented 2/6/75 outcomes. |
| Existing root marker writes can become general overwrite permission | TestRootGuard.MarkDeleting requires held root gate, matching marker and fresh owner-death check; scope allocation uses SafeWrite.OpenCreate. | Port that exact transition; no generic adoption of foreign control files. |
| Compatibility roster is still 40 | Tool-copy 13 methods/16 results; sweep 21/27; scope 11/11. Target virtual compatibility class is absent. | Port 45 methods/54 results: S1a 14, S1b 16, S2 54. 12 ownership + 14 adapter + 54 compatibility = 80. |
| S2-S5 integration is present | HostCleanup core has no production reference; only Antiphon.Tests references it. Native adapters throw. No HostCleanup/HostMaintenance registration exists in server/runner Program, no cleanup capability/phone-home operations. | All remaining integration slices below are still needed; schema existence is not operational acceptance. |
| Server roster is 56, 34 present | Orchestration 2 schema cases; report 17; maintenance 9; inventory 6 = 34. Missing 14 + 5 + 1 + 6 = 26. | Preserve final 16 + 22 + 10 + 12 = 60. Reuse WorktreeCleanupBacklog kind 56. |
| Client needs a new backlog-owner case | `attentionVisuals.test.ts` already has that case: 24 plain cases plus six parameter rows = 30. | CP-12 is a regression row; no invented additional client case. |
| Fixed host placement belongs in the plan | GET `/api/runner-defaults` returned revision 2; GET `/api/session-runners` returned eligible Linux and Windows lanes. | Resolve current placement at dispatch. No fleet address or host pin is part of this plan. |

Existing regression census: OutputCleanupTests 43, SafeDeleteProcessBoundaryTests 2,
CheckpointReceiptSafetyTests 8, PlanCoverageHandleTests 16, CheckpointToolCopyCleanupTests 16,
CheckpointShadowCustodyTests 12, CheckpointConfinementLedgerTests 16. These are bounded selections,
not permission to run the whole Unit lane or checkpoint namespace.

## Decisions

### D-1. Preserve the landed safeguards and existing feature limits

Startup retains older siblings; age clean retains unrecorded runs. Generic outputs require
instance/process creation custody. Only the exact sealed tool copy can use durable cross-process
authority, with digest, run, creator, directory identity, inventory and entry rechecks. No recursive
fallback, receipt-only generic adoption or age-only deletion is allowed. Rejected: restoring old
positive-delete tests, changing their names while leaving their oracle, or weakening CARD-1199/1242/1254.

Daily cleanup remains repository-lease-free, worktrees inventory-only, strict newest-write age >24h,
10 attempts/10 GiB per storage namespace, no pressure override or session stop. Legacy historical
holds remain held after expiry. Main Hangfire registration uses its existing worker-enabled gate
plus HostCleanup.Enabled; no new main-instance setting or runner timer. Configured namespaces keep
Enabled=false and Execute=false until the parent's per-host qualification and explicit activation.

### D-2. Introduce one explicit context at the observation boundary

Add `CheckpointCleanupContext` containing the I/O port, clocks/delay and `CheckpointCreationRegistry`.
The registry owns append-only created paths, size/hash records and fresh-root timestamps; it is never
populated from a generic on-disk ledger. A fresh virtual context has an empty registry, even over the
same virtual filesystem. A single readonly native process context, composed initially by new tool-owned
`CheckpointCleanupDefaults`, backs legacy static facades so native callers within one process retain
their existing creation custody. No setter, AsyncLocal,
mutable global test override, reset hook or per-call fresh default registry is allowed.

`ICheckpointCleanupIo` is a library-owned primitive contract. It covers directory enumeration,
attributes/link targets, mode/owner/link count/device/inode/ctime/mtime/size/creation identity,
exclusive owner-only mkdir, open/create/append/gate modes, held-file metadata and final path,
bounded/cancellable stream reads, atomic move, individual unlink/nonrecursive rmdir, process
identity observations, environment/path semantics, UTC and monotonic time/delay. Virtual handles
model replacement after open, file sharing and held gates. Timers must advance under the fake clock.
Git observation is a separate primitive request/result port with executable, arguments, deadline,
exit/stdout/stderr/custody facts. Native execution forwards to the existing CheckpointProcessRunner;
no new raw Process.Start path or relaxed Git isolation is introduced.

The port returns facts, bytes and primitive operation results, never `CanDelete`, sealed authority,
parsed trusted receipts or canned cleanup success. SafeWrite/SafeDelete/OutputCustody/ToolCopyAuthority,
ConfinedFileReader's admission/read loop, ReceiptJson and ReportWriter continue to decide policy.
Every admitted path records an attempt before a later veto so a negative test cannot pass by never
reaching its guard. Rejected: a filesystem-only mock (native and static escapes), a fake policy
implementation, or replacing the verified reader with a fake that simply declares bytes trusted.

### D-3. Make dependency direction explicit before extraction

S1a0 adds primitive contracts/native adapters under `src/Antiphon.HostCleanup/Checkpoints/` and a
one-way tool -> HostCleanup reference. The port/context types cannot name tool-owned ReportModel,
RunState, CheckpointApp.Runtime, ProcessIdentity or LedgerStat. Declare the primitive snapshots and
Git request/result interface in `ICheckpointCleanupIo.cs`, with conversion in the existing tool adapters.
Runtime process execution is supplied by composition
at the tool entry; the library cannot reference the executable to obtain it.

S1a0/S1a1/S1a thread instances without moving cleanup policy. S2 moves the shared cleanup closure,
not merely the old four files: ToolCopyCleanup (including ContainedCleanup/ExecutorOwnershipStore),
RunOwnership, TestRootGuard, ProcessIdentity, SafeWrite, SafeDelete, OwnedOutputLedger,
ToolCopyAuthority, OutputCustody, ExclusiveDirectory, CheckpointCleanupDefaults and LedgerStat,
plus generic receipt reading and shape validation and the threaded sweep core. Preserve namespaces, serialized formats and
forwarding entry points; each public type is compiled once. Generic ReceiptJson shape validation
must be separated from its ReportModel-specific checks, which stay in the tool. ReportWriter,
RunStateStore, EvidenceFolder/report generation, manifest/coverage orchestration and process stop
control remain tool/test-owned. Extract only EvidenceFolder.IsExecutorImage into ExecutorImageGuard.

The generic verified reader belongs below both receipt and coverage callers; keep the existing
Coverage.ConfinedFileReader facade forwarding to it. Its native adapter remains replaceable beneath
admission/read policy. SafeDelete's bounded Git primitive is supplied without a reverse dependency:
share the existing `scripts/lib/CheckpointProcessRunner.cs` as a linked implementation in the library
and remove the tool's duplicate compilation in S2, preserving its namespace/API and single type
identity. Do not change that runner's behavior. The library must reference neither tests nor tools.
LifecycleHost retains linked hooks/roster and gains a direct library reference. Rejected: a circular
reference, two compiled copies of public custody types, or moving ReportWriter's entire tool graph.

### D-4. Split the expanded S1a into independently checked policy slices

S1a0 is custody primitives, registry and the complete verified-reader seam; S1a1 is strict
receipt parsing/report binding; S1a completes real callers and
14 compatibility ports. Each has new virtual C987 guards plus existing affected-class regressions;
none is a seam-only slice without a checkpoint. These are separate Code commissions with their
own Review/land, exact source scopes and authoring allowance; the former 55-minute S1a estimate
is withdrawn. Estimate S1a0 120-150, S1a1 75-105, S1a 90-120 minutes including their rows. These are
estimates, not measured timings. Do not commission the expanded graph under the old budget.
If the actual caller census exceeds a commission, commit/push the truthful slice and revise its
remaining selection before continuing; never silently omit a dependency or substitute physical tests
for the virtual ports. S1a0 -> S1a1 -> S1a -> S1b -> S2 remains serial.

### D-5. Keep compatibility floors separate from supplemental coverage

CP-17=14, CP-1=16, CP-2=54 and CP-4=80 stay fixed. New cases use separate classes/rows. The new
physical regression rows exercise native defaults; they do not prove virtual isolation and do not
count toward those floors. Conversely virtual rows do not prove native kernel behavior. Select exact
row IDs; suffixes such as S1a0 work, but an S1-S10 range does not expand them. Rejected: swapping new
guards into the 54 roster, class-wide wildcard additions that hide missing ports, or a whole Unit run.

### D-6. Retain integration order and operator boundaries

S4 provides the daily graph/transport interface; S7 provides real runner local store/outbox/dispatcher.
S5 can use scripted admission transport. S6b requires S5, S6a and S7 in its start tree: receipt delivery
must reach authenticated report GET and public AttentionService GET, including busy/ready recipients,
crash-before-enqueue and lost-ACK status-before-retry. `/api/attention` remains fleet-global; test board
isolation on the feature report route, not an invented attention query parameter. Rejected: canned
serialized success standing in for delivery, or invoking worktree retirement to make daily cleanup work.

The thin shared worker, offline restart/deploy/Windmill wrappers and Windows qualification retain the
parent design. No live schedule edit, deploy, daemon restart or activation belongs to these Code tasks.
No session waiting for input is introduced or changed. Parking remains disabled by default, with no
automatic release deadline (CARD-1083); neither cleanup nor a stalled test is authority to kill a session.

## Slices and exact source scopes

Aliases: `L=src/Antiphon.HostCleanup`, `C=tools/Antiphon.Checkpoints`,
`T=tests/Antiphon.Tests`, `A=server/Application`, `I=server/Infrastructure`,
`R=tests/Antiphon.SessionRunner.Tests`. Brace lists enumerate files, not broad directory permission.
All new tests below are proposed, absent at the inspected source. Existing regression classes are
read/run only; changing their assertions or test selections requires a substantive follow-up.

| Slice | Source scope | Tests and closing rows |
|---|---|---|
| S1a0: custody context | New `L/Checkpoints/{ICheckpointCleanupIo,CheckpointCleanupContext,CheckpointCreationRegistry,CheckpointNativeIo}.cs`; `L/Antiphon.HostCleanup.csproj`, `C/Antiphon.Checkpoints.csproj`; `C/Cleanup/{SafeWrite,SafeDelete,OwnedOutputLedger,OutputCustody,ExclusiveDirectory}.cs`, `C/LedgerStat.cs`; new `C/Cleanup/CheckpointCleanupDefaults.cs`; `C/Coverage/ConfinedFileReader.cs` and the ReceiptFile part of `C/Evidence/ReceiptFile.cs`. Thread the complete verified-reader primitives here, since creation hashes/adoption already need this path; keep real read policy and native forwarding. | New `T/Infrastructure/{HostCleanupCheckpointFixture,HostCleanupCheckpointCustodyTests}.cs`. Virtual facts drive real custody/adoption/preflight policy and real verified reads. CP-18, CP-21, CP-24, CP-22. |
| S1a1: receipt binding | `C/Evidence/ReceiptFile.cs` (ReceiptJson is in this file), `C/Report/ReportWriter.cs`, `C/State/RunStateStore.cs`; thread the parser and report/state entry points into the completed S1a0 reader context. Do not change SourceSnapshot/BuildConfinement semantics. | New `T/Infrastructure/HostCleanupCheckpointReceiptTests.cs`, extend checkpoint fixture. CP-19, CP-26. |
| S1a: real callers and tool authority | `C/Cleanup/{ToolCopyCleanup,OutputCleanup,RunOwnership,TestRootGuard,ProcessIdentity,ToolCopyAuthority}.cs`; `C/{CheckpointApp,Program,RepoPaths}.cs`; `C/Commands/WaitCommand.cs`, `C/Evidence/EvidenceFolder.cs`, `C/Manifest/ManifestLoader.cs`, `C/Execution/{ShadowCopy,CheckpointUsageEvent}.cs`; thread receipt/state overloads from S1a1 at their callers. New `L/Checkpoints/ExecutorImageGuard.cs`; context/native primitive surface as needed by these named callers. Preserve existing source-capture/launch injection; use explicit repo, source/branch and recorded launch in virtual fixtures, never start a build/provider. | New `T/Infrastructure/{HostCleanupCheckpointCompatibilityTests,HostCleanupCheckpointAuthorityTests}.cs`, fixture extension. Exactly 14 tool-only ports plus supplemental authority/isolation cases. CP-17, CP-20, CP-23. |
| S1b: unmoved sweep port | `T/Checkpoints/CheckpointTempRootSweep.cs`; constructor and all file/stream/time/gate paths use the same context, including TestRootGuard.MarkDeleting, post-lock eligibility and nested custody. No move, scope wrapper untouched. | Add the two nested-custody ports to the compatibility class. CP-1=16; a physical-only pass does not close it. |
| S2: scope port, pre-move proof, shared extraction | `T/Checkpoints/{CheckpointTestScope,CheckpointTempRootSweep}.cs`; new `L/Checkpoints/CheckpointTempRootSweep.cs`; shared closure from D-3 moves from `C` to corresponding `L/Checkpoints` files, retaining facades where needed. `C/Evidence/ReceiptFile.cs`, `C/Coverage/ConfinedFileReader.cs`, `C/Report/ReportWriter.cs` split generic versus report-specific validation; `C/Antiphon.Checkpoints.csproj`, `L/Antiphon.HostCleanup.csproj`, `tests/Antiphon.Checkpoints.LifecycleHost/Antiphon.Checkpoints.LifecycleHost.csproj` update compilation/references. | Add 27 sweep + 11 scope executions to compatibility tests and virtual fixture. CP-2 first at committed unmoved scope+ports, then at committed move; CP-25/CP-27 at moved tip check affected native facades; CP-28 reruns the supplemental virtual policies after their move. Scope usage events also pass the context to the threaded CheckpointUsageEvent; scope joins/control remain test-only, marker constant becomes TestRootGuard.MarkerName; startup hook/roster stay test-owned. |
| S3: daily adapter/registry | New `L/Checkpoints/DailyCheckpointAdapter.cs`, `L/ScratchRegistry.cs`; `L/CleanupFamilyRegistry.cs`. Exact creator-resolved scratch path/owner generation, two-level scratch grammar, shared claim/root gate, strict daily age, no lifecycle SweepOnce shortcut or daily truncation. | New `T/Infrastructure/HostCleanupCheckpointAdapterTests.cs` with the parent's 14 V-4 names. CP-3 then CP-4=80, plus CP-13 at the registry-change tip. |
| **S4: server fixtures, read-only Git probe and inventory closure** | `T/Application/HostCleanupFixture.cs` gains `ThrowingRepositoryMutationLease` (both overloads counted), `ForbiddenRetirementProbe`, `RecordingReadOnlyGitProbe`, `ScriptedExistingOwnerStatusProbe`, `ScriptedHostCleanupTransport`, `RecordingRecurringJobManager`; **new** `A/Interfaces/IHostCleanupTransport.cs`; **new** `I/Git/HostCleanupWorktreeProbe.cs` (`GIT_OPTIONAL_LOCKS=0`, `ls-remote` + local objects, never fetch/prune/remove/update-ref); `A/Services/HostCleanupWorktreeInventory.cs` (owner names incl. CARD-1017, `Only_owned_scratch_child` rule); **new** `A/Services/HostCleanupDailyRun.cs` (mixed preview/execute graph over inventory + library executor; no lease type reachable). | `T/Application/HostCleanupWorktreeInventoryTests.cs` gains the six missing names; `Daily_graph_has_no_direct_lease_or_lock_escape` walks reachable daily types for `RepositoryMutationLease` construction, `landing.lock` I/O, retirement calls. CP-5. |
| **S5: scheduling, catch-up, activation and authorization** | **new** `I/Agents/HostCleanupJob.cs`, `I/Agents/HostCleanupMonitorJob.cs`; `I/Agents/HangfireConfiguration.cs` (`AddOrUpdateHostCleanupJobs`, `AddOrUpdateHostCleanupMonitorJob`, 10:00 Europe/London + deterministic 0-30 min host offset, 15-min monitor); **new** `A/Services/HostCleanupScheduler.cs` (per-storage job ids, alias collapse, last-completed-date catch-up, namespace qualification + config digest, weekly-cutover confirmation flag, seven-prefix hold resolution); `A/Settings/{HostCleanupSettings,HostCleanupSettingsValidator}.cs` (hosts/roots/storage ids, `WeeklyBuildJunkDisabledConfirmed`, `Qualified` per namespace); **new** `server/Api/Endpoints/HostCleanupEndpoints.cs` (GET hosts, GET runs/{id} paged, POST preview, POST hold, POST maintenance, POST receipts); `server/Program.cs` (DI + Hangfire registration under `HostCleanup:Enabled`), `server/appsettings.json` (`Enabled=false`, `Execute=false`). | `T/Application/HostCleanupOrchestrationTests.cs` gains the frozen 14 names; keeps the 2 landed schema tests. **new** `T/Application/HostCleanupTestServer.cs` maps only feature/attention/auth routes. CP-6. |
| **S6a: maintenance closure and land participation** | `A/Services/HostMaintenanceService.cs` (bounded-operation syscall barrier hook), `A/Services/AgentTaskLandService.cs` (publish maintenance intent before `TryAcquireAsync` at the current acquisition boundary, finish after; no cleanup invocation). | `T/Application/HostCleanupMaintenanceTests.cs` gains `Land_waits_only_for_bounded_inflight_delete` using `MaintenanceBarriers`. Needs nothing from S5 or S7; dispatchable once S4 has landed and the shared land-service file gate is open. CP-8. |
| **S6b: delivery verification (start tree contains S5, S6a and S7)** | `A/Services/HostCleanupService.cs` (status-before-retry query route, pending-outbox drain entry), **new** `A/Services/HostCleanupReceiptOutbox.cs` (server-side pending receipt recovery), `A/Services/HostCleanupAttentionService.cs` (public recipient through `AttentionService` read path), `A/Services/AttentionService.cs` (append host-cleanup projection), `A/Dtos/AttentionDtos.cs` (no new kind: reuse `WorktreeCleanupBacklog`, kind 56). | `T/Application/HostCleanupReportTests.cs` gains the five missing names (`Post_run_low_space_never_widens_deletion`, the busy/ready recipient pair, `Crash_before_enqueue_recovers_pending_receipt`, `Crash_after_ingestion_before_ack_queries_status`); the four delivery names drive S7's real runner local store/outbox through the receipt route (the historical ledger's production-outbox recipe Q). CP-7, then CP-9 union (60) at the composed tip. |
| **S7: runner contracts, command service, store and transport (precedes S6b)** | `src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs` (`RunnerCapabilityFeatures.HostCleanupV1`), `src/Antiphon.SessionRunner.Contracts/PhoneHomeContracts.cs` (two operations, request/response records), **new** `src/Antiphon.SessionRunner.Contracts/HostCleanupContracts.cs`; **new** `src/Antiphon.SessionRunner/HostCleanupCommandService.cs`, `src/Antiphon.SessionRunner/HostCleanupLocalStore.cs` (protected durable receipt outbox under the runner state root), `src/Antiphon.SessionRunner/HostCleanupWorktreeInventory.cs` (read-only facts); `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs` (admission + routing only), `src/Antiphon.SessionRunner/Program.cs` (DI; no hosted timer), `src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj` (library reference); **new** `I/Agents/SessionRunner/HostCleanupTransport.cs` implementing `IHostCleanupTransport` over `I/Agents/SessionRunner/PhoneHomeRunnerClient.cs`. | **new** `R/HostCleanupFixture.cs` (real dispatcher, `RecordingPhoneHomeRuntime`, `LoopbackHostCleanupWire`, `VirtualRunnerDurableStore`), **new** `R/HostCleanupCommandTests.cs` with the frozen 12 V-8 names. CP-10. |
| **S8: worker, scripts, cutover, smoke evidence, docs** | **new** `tools/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj`, `tools/Antiphon.HostCleanup/Program.cs` (preview/status/execute/register/release/clean verbs); **new** `scripts/host-cleanup.ps1`, `scripts/task-scratch.ps1`, `scripts/host-cleanup-server2.sh`, `scripts/lib/host-cleanup-maintenance.ps1`, `scripts/fixtures/c826-cleanup-shim.ps1`, `scripts/windmill/antiphon-host-cleanup-server2.json` + `.schedule.json`; `scripts/cleanup-build-junk.ps1` (compatibility wrapper, no robocopy/Remove-Item), `scripts/install-cleanup-task.ps1` (refuse install, keep uninstall), `scripts/restart-apphost.ps1`, `scripts/restart-session-runner.ps1`, `scripts/session-runner-restart-health.ps1`, `dev-aspire.ps1`, `scripts/deploy-server2.ps1`, `scripts/verify-card0849-caches.ps1` (tmp volume identity + host free bytes in receipts), `docker/session-runner-grok/Dockerfile`, `docker-compose.server2-runner.yml` (publish worker, settings, no new mount); `scripts/windmill/README.md`, `server/Bundles/stage-code.md`, `server/Bundles/stage-review.md` (self-clean rule), `docs/host-cleanup.md`, relevant sections of `docs/bootstrap.md`, `docs/logs.md`, `docs/testing-and-build.md`, `docs/ops-http.md`. | **new** `T/Scripts/HostCleanupScriptTests.cs`, `T/Scripts/HostCleanupScriptFixture.cs` with the frozen 12 V-9 names; `attentionVisuals.test.ts` is unchanged (the owner-display case already exists; 30 results) and CP-12 reruns it as a regression row. CP-11, CP-12. All scripts ASCII-only. |
| **S9: Linux native adapter** | **new** `L/ICleanupNativeApi.cs`, `L/LinuxCleanupNativeApi.cs`; `L/LinuxCleanupFileSystem.cs` (openat/fstatat/unlinkat semantics over the port, held parent handles, device/inode identity, EACCES partial). | **new** `T/Infrastructure/HostCleanupLinuxTests.cs` with the frozen 6 V-10 names over `HostCleanupNativeFixture(Linux)` added to `T/Infrastructure/HostCleanupFixture.cs`. CP-14. |
| **S10, Windows lane: Windows native adapter and 5.1 wrapper run** | **new** `L/WindowsCleanupNativeApi.cs`; `L/WindowsCleanupFileSystem.cs` (reparse-safe open flags, file-ID/volume identity, long-path handle binding, boot/start tolerance); no script changes beyond what S8 landed. | **new** `T/Infrastructure/HostCleanupWindowsTests.cs` with the frozen 7 V-11 names. CP-15 (6 adapter cases), CP-16 (`Windows_powershell_51_parses_and_runs_wrappers_offline`). Dispatch with `-Platform Windows`. |

Only S5 adds production composition, under disabled settings; earlier slices remain unwired.
S4's new daily graph also needs `server/Antiphon.Server.csproj` to reference the shared library;
include that file in S4's scope. S6a may extend `server/Infrastructure/Data/HostMaintenanceStore.cs`
and `A/Interfaces/IHostMaintenanceStore.cs` only for the bounded-operation barrier; no repository
lease is introduced. S8 adds no client case or enum. The host-specific filenames in S8 are existing
repository artifact conventions, not placement pins or a live destination configuration.

Dependency order: **S1a0 -> S1a1 -> S1a -> S1b -> S2 -> S3 -> S4 -> S7 -> S5 -> S6a -> S6b -> S8 -> S9 -> S10**.
S6a may follow S4 earlier if its shared-file gate opens; S9 needs S3; S10 needs S8 and S9's native
interface. S6b requires all of S5/S6a/S7. Every dispatch starts from landed prerequisites and only
its row's enumerated files. Do not create branch merges to acquire prerequisites.

Re-read scoped active tasks and their actual briefs at dispatch. S1a0/S1a1/S1a/S1b/S2 collide with
any checkpoint cleanup, receipt, confinement or process-reader owner. S5 collides with server/Hangfire
composition; S6a with AgentTaskLandService; S7 with runner contracts/dispatcher/phone-home transport;
S8 with deployment, restart and stage bundles. Prior CARD-1032/1033/1042/1199 statuses from October 12
are historical, not current locks. An empty scope is not absence, and a Review branch is frozen.
Inventory names existing worktree owners including CARD-1017, CARD-0459 and CARD-0692 but calls none.
Integrate a subsequently landed CARD-0692 scratch release entry only after re-reading its actual contract.

## Verification design

This section refreshes scope, required oracles and the closed checkpoint manifest. **Verification is
still a separate next stage.** TestDesign must refresh/freeze the complete method/variant/guard/PC
ledger and copy these selections into its committed artifact before Code. No guard/PC execution,
build, native qualification or claim of green is made by this Plan.

### Required acceptance changes and supplemental guards

| Binding | New required oracle | Rejected mutation for TestDesign to bind |
|---|---|---|
| R-1a, G-204/PC-204 | `next_start_preserves_all_pre_existing_sibling_tools`: fresh run exists, every pre-existing sibling/unrelated tool and its bytes remain; startup performs no sibling delete attempt. | Reintroduce the old sibling sweep/delete on startup; the dead sibling preservation assertion must fail. |
| R-1a, G-206/PC-206 | `age_clean_cannot_bypass_live_custody`: no deletions; missing-ledger refusal; all four directories remain, including dead. | Treat dead+age as authority and bypass the missing-creation/ledger guard; dead-run preservation must fail. |
| S1a0 custody | Generic disk ledger never seeds the registry; root must be exclusively created, owner-only and inside driver-observed build window; entry identity/content stable across reads. | Populate registry from disk, omit one time/owner/link gate, or accept a changed/late entry; recorded delete-attempt/byte assertions fail. |
| S1a1 receipts | Regular single-link retained handle, no-follow/nonblocking open, bounded three-second/64-MiB read; strict shapes and actual markdown/source/terminal binding. | Read by path after verification, omit link/type/size/deadline or required-report checks; named refusal/unchanged-byte assertion fails. |
| S1a tool copy | A fresh context may remove only a valid run-bound sealed tool. Generic unowned outputs remain; no disk authority import. Hashes/directory identity and each entry are rechecked, deletions remain nonrecursive. | Import authority into generic registry, trust only digest, omit final identity/entry check or recursively remove late content; outside/late bytes and refusal oracle fail. |
| S1b/S2 marker transition | Fresh eligibility under held root lock precedes only the matching marker's deleting transition. | Skip fresh owner/marker/gate check or permit arbitrary existing-file overwrite; exact marker/foreign sentinel assertion fails. |

Keep the original 45 compatibility method names and argument rows from the current source, with only
the startup name correction above. CP-17's 11 methods yield 14 results because completed_wait has
four `(exitCode, keepOutputs)` rows. S1b adds nested_live_executor_vetoes_whole_root and
unknown_nested_custody_vetoes_whole_root. S2 adds every current sweep and scope method; do not replace
source assertions with a cleaner but weaker interpretation. S2's mandatory pre-move green and post-move
green are separate SHAs/receipts. Supplemental classes cannot inflate the 54-case compatibility count.

Supplemental virtual roster (proposed; one result per named method unless explicitly stated):

| Class / row | Exact methods and result count | Fixture/production reachability |
|---|---|---|
| HostCleanupCheckpointCustodyTests / CP-18 | 12: `Creation_registries_are_isolated`, `Existing_directory_cannot_gain_creation_authority`, `Disk_ledger_cannot_authorize_generic_delete`, `Build_exit_is_required_for_adoption`, `Custody_window_checks_owner_mode_and_times`, `Links_and_special_entries_are_not_adopted`, `Entry_change_during_read_is_not_adopted`, `Late_or_changed_entries_are_retained`, `Tracked_or_nested_repository_paths_are_retained`, `Git_failure_and_deadline_refuse_before_mutation`, `Individual_deletes_preserve_late_additions`, `Fresh_owned_output_can_be_removed`. | Real SafeWrite/SafeDelete/OwnedOutputLedger/OutputCustody over virtual metadata, handles, streams, Git results and monotonic clock. Each negative variant starts from the same positive removable fixture. No canned removal/refusal result. |
| HostCleanupCheckpointReceiptTests / CP-19 | 8: `Receipt_types_are_checked_before_open`, `Receipt_read_uses_verified_handle`, `Receipt_size_and_deadline_are_bounded`, `Receipt_shape_and_identity_are_required`, `Terminal_receipt_requires_digest_source_and_exit`, `Report_writes_require_verified_readback`, `Receipt_read_errors_do_not_write_or_adopt`, `Valid_terminal_receipt_is_accepted`. | Real ConfinedFileReader/ReceiptFile/ReceiptJson/ReportWriter/RunStateStore. Special file, hardlink, substitution, truncation, cancellation, missing facts and hash/source/exit variants are internal tables, not extra execution counts. Wait's end-to-end exit is additionally covered at S1a. |
| HostCleanupCheckpointAuthorityTests / CP-20 | 16: `Fresh_context_removes_only_sealed_tool` (2 rows: exit 0/1); `Fresh_context_retains_unproven_tool` (10 rows: added, changed, forged-ledger, missing, malformed, foreign, escape, linked, hardlinked, replaced); `Tool_authority_does_not_seed_generic_registry`; `Every_tool_caller_uses_the_injected_context`; `Marker_transition_requires_gate_identity_and_dead_owner`; `Invalid_terminal_receipt_prevents_cleanup`. | Real start/seal -> serialize -> discard context -> fresh wait/cleanup over shared virtual bytes. Mirror current CheckpointShadowCustodyTests oracles, retain evidence/outside bytes, assert no native-port fallback. Invalid-terminal variants drive the real WaitCommand and assert zero cleanup attempts before receipt admission. Caller witness covers create/start/ack, wait/run front doors, stop, clean, evidence wrappers and output cleanup; build/source/launcher observations use existing explicit seams. |

New classes live in Infrastructure, not the Checkpoints namespace; do not change its namespace census
just to count these virtual tests. S1a0 includes verified-handle reading because SafeWrite creation hashes and OutputCustody adoption
already reach it. Its virtual guards use the real reader policy from the first slice; S1a1 adds the
remaining strict object/terminal/report binding. By S1a the positive and negative end-to-end cases
use the entire chain. TestDesign must reject any additional dependency gap; move that particular
guard/dependency earlier, never fake its policy result.

For red-first authoring, commit the failing cases before a production fix; missing-seam compiler
errors, zero selected tests, fixture exceptions and timeouts are not a meaningful red. Existing
behavior ports may already be green: demonstrate new guards with a compiling, exact-method defect
in the separate commissioned SourceLanding Mutation stage. TestDesign retains all PC-1..PC-260
obligations, rebinds PC-204/206 and allocates new controls from PC-261 for the added custody graph.
The final control total/variants/cost are for TestDesign to calculate, not assumed to remain 260.
Preserve the prior recipe weaknesses (competition barriers, cancellation-token use, cross-family
budget and delivery payload witnesses) as obligations, not already-proven claims.

Ordinary requirements remain V-1=22, V-2=20, V-3=12, V-4=14, V-5=16, V-6=22, V-7=10, V-8=12,
V-9=12, V-10=6, V-11=7, V-12=30, V-13=12; R-1=54, R-2 service union=60; R-3 schedule/wrappers,
R-4 producer-to-recipient delivery, R-5 native-adapter seams. Parent named methods/oracles and the
immutable October 12 ledger supply the unchanged bindings. No missing method becomes green by census.

### Execution and evidence contract

Use the checkpoint tool with `run --plan <this artifact> --rows <commissioned CP ids>
--expected-source-sha <full committed SHA>`, then foreground `wait` calls until exit is not 75.
Wrap any launcher build/test driver in `scripts/build-slot.ps1`; the checkpoint row driver takes
its own slot. No raw dotnet test, full Unit lane, provider suite or unlisted build. Slot timeout is
not permission to retry unleased. Each row has one isolated build and one exact filter, no shared
build reuse. Keep source frozen through a run. Preserve CHECKPOINT lines, source/build provenance,
executed/passed/failed/skipped, expected roster, slot/wait and every rerun at its real SHA. Never
claim green with skips or a failed floor. Confirm a suspected inherited failure at the base using
only that exact failing selection, and report that diagnostic exception to the closed list.

The new physical rows are intentional native-default regressions, using their existing owned private
fixtures and process limiters, separate from virtual isolation proof. They require Linux, installed Git
and the existing offline process fixtures; unsupported infrastructure is not a pass. CP-25/CP-27 repeat
only the named affected-class unions after the shared-code move; CP-28 covers the moved virtual policies. No physical sweep/scope/recovery
suite is selected. Their native run behavior remains unclaimed outside this explicit selection.

All TUnit rows set `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` except runner
rows, which need only the latter. New virtual fixtures do not inherit CheckpointTestBase. Native
fallback counters trip on unexpected access; fixture setup must not touch the host filesystem.
Keep generated evidence ignored and remove task-owned alternate outputs through the existing
custody-aware cleanup; retained/unknown outputs are reported, never raw-deleted to make a row green.
Code/Review run `scripts/check-evidence-diff.ps1` over their entire base..HEAD history.

Placement: read runner defaults/catalogue again; omit `-Runner` unless deliberately pinning one host.
Portable authoring needs no `-Platform`; the rows' Linux/Windows prefixes name verification lanes.
Require Linux for CP-21..CP-27 and CP-14; require Windows for CP-15/16, using `-Platform` only when
that OS is needed (`-Platform Any` unpins). No current host identifier or fleet location is embedded.

### Checkpoints

Closed list for the refreshed plan; TestDesign must freeze the same rows with its completed ledger.
Select only the commissioned slice's rows. CP-2 has the sole planned pre/post-move double run;
CP-25/CP-27 are declared native-facade regressions after that move; CP-28 covers its virtual policies. New rows supplement, never reduce,
14/16/54/80. CP-13 moves to S3: the registry change happens there, so run its policy regression there.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-18 | S1a0 | `tests/Antiphon.Tests -> bin-c987-custody/` | linux-custody-virtual | `/*/*/HostCleanupCheckpointCustodyTests*/*` | custody supplement | all 12 named results, 0 failed/skipped | 12 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-21 | S1a0 | `tests/Antiphon.Tests -> bin-c987-custody-existing/` | linux-custody-existing | `/*/*/(OutputCleanupTests*)\|(SafeDeleteProcessBoundaryTests*)/*` | native custody/Git regression | all 43 + 2 results, 0 failed/skipped | 45 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-24 | S1a0 | `tests/Antiphon.Tests -> bin-c987-ledger-existing/` | linux-ledger-existing | `/*/*/CheckpointConfinementLedgerTests*/*` | LedgerStat regression | all 16 results, 0 failed/skipped | 16 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-22 | S1a0 | `tests/Antiphon.Tests -> bin-c987-receipt-existing/` | linux-receipt-existing | `/*/*/(CheckpointReceiptSafetyTests*)\|(PlanCoverageHandleTests*)/*` | native verified-reader regression | all 8 + 16 results, 0 failed/skipped | 24 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-19 | S1a1 | `tests/Antiphon.Tests -> bin-c987-receipt/` | linux-receipt-virtual | `/*/*/HostCleanupCheckpointReceiptTests*/*` | receipt supplement | all 8 named results, 0 failed/skipped | 8 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-26 | S1a1 | `tests/Antiphon.Tests -> bin-c987-binding-existing/` | linux-binding-existing | `/*/*/CheckpointReceiptSafetyTests*/*` | parser/report binding regression | all 8 results, 0 failed/skipped | 8 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-17 | S1a | `tests/Antiphon.Tests -> bin-c987-compat-toolonly/` | linux-compat-toolonly | `/*/*/HostCleanupCheckpointCompatibilityTests*/(wait_uses_custody_not_progress_pid*)\|(next_start_preserves_all_pre_existing_sibling_tools*)\|(stop_validates_generation_before_signaling*)\|(stop_waits_for_confirmed_exit*)\|(age_clean_cannot_bypass_live_custody*)\|(evidence_write_has_no_raw_delete_bypass*)\|(automatic_recovery_preserves_all_evidence*)\|(sweep_never_signals_a_process*)\|(failed_delete_receipt_is_truthful_and_retryable*)\|(completed_wait_removes_only_tool*)\|(already_absent_is_idempotent*)` | R-1a (tool-only 14) | all listed, 0 failed/skipped; 14 executions (11 methods; `completed_wait_removes_only_tool` is 4 rows) | 14 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-20 | S1a | `tests/Antiphon.Tests -> bin-c987-authority/` | linux-authority-virtual | `/*/*/HostCleanupCheckpointAuthorityTests*/*` | authority/caller supplement | all 16 named results, 0 failed/skipped | 16 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-23 | S1a | `tests/Antiphon.Tests -> bin-c987-tool-existing/` | linux-tool-existing | `/*/*/(CheckpointToolCopyCleanupTests*)\|(CheckpointShadowCustodyTests*)/*` | native tool/second-process regression | all 16 + 12 results, 0 failed/skipped | 28 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-1 | S1b | `tests/Antiphon.Tests -> bin-c987-compat-tool/` | linux-compat-toolcopy | `/*/*/HostCleanupCheckpointCompatibilityTests*/(nested_live_executor_vetoes_whole_root*)\|(unknown_nested_custody_vetoes_whole_root*)\|(wait_uses_custody_not_progress_pid*)\|(next_start_preserves_all_pre_existing_sibling_tools*)\|(stop_validates_generation_before_signaling*)\|(stop_waits_for_confirmed_exit*)\|(age_clean_cannot_bypass_live_custody*)\|(evidence_write_has_no_raw_delete_bypass*)\|(automatic_recovery_preserves_all_evidence*)\|(sweep_never_signals_a_process*)\|(failed_delete_receipt_is_truthful_and_retryable*)\|(completed_wait_removes_only_tool*)\|(already_absent_is_idempotent*)` | R-1a | all listed, 0 failed/skipped; 16 executions | 16 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c987-compat-sweep/` | linux-compat-sweep-scope | `/*/*/HostCleanupCheckpointCompatibilityTests*/*` | R-1a, R-1b | all listed, 0 failed/skipped; 54 executions (run once pre-move, once post-move, reported separately) | 54 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-25 | S2 | `tests/Antiphon.Tests -> bin-c987-move-existing/` | linux-moved-facade-existing | `/*/*/(OutputCleanupTests*)\|(CheckpointReceiptSafetyTests*)\|(CheckpointShadowCustodyTests*)\|(SafeDeleteProcessBoundaryTests*)/*` | shared extraction regression | all 43 + 8 + 12 + 2 results, 0 failed/skipped | 65 | 15 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-27 | S2 | `tests/Antiphon.Tests -> bin-c987-move-readers/` | linux-moved-reader-existing | `/*/*/(PlanCoverageHandleTests*)\|(CheckpointConfinementLedgerTests*)/*` | shared reader/stat regression | all 16 + 16 results, 0 failed/skipped | 32 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-28 | S2 | `tests/Antiphon.Tests -> bin-c987-move-policies/` | linux-moved-policies-virtual | `/*/*/(HostCleanupCheckpointCustodyTests*)\|(HostCleanupCheckpointReceiptTests*)\|(HostCleanupCheckpointAuthorityTests*)/*` | shared supplemental policy regression | all 12 + 8 + 16 results, 0 failed/skipped | 36 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-3 | S3 | `tests/Antiphon.Tests -> bin-c987-adapter/` | linux-checkpoint-adapter | `/*/*/HostCleanupCheckpointAdapterTests*/*` | V-4 | all 14 listed executions, 0 failed/skipped | 14 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-4 | S3 | `tests/Antiphon.Tests -> bin-c987-custody-union/` | linux-cleanup-custody-union | `/*/*/(HostCleanupOwnershipTests*)\|(HostCleanupCheckpointAdapterTests*)\|(HostCleanupCheckpointCompatibilityTests*)/*` | V-3, V-4, R-1, R-3 | all listed, 0 failed/skipped; 12 + 14 + 54 | 80 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-13 | S3 | `tests/Antiphon.Tests -> bin-c987-policy/` | linux-cleanup-policy-rerun | `/*/*/(HostCleanupPolicyTests*)\|(HostCleanupExecutionTests*)/*` | V-1, V-2, R-2 (regression after S3 registry change) | all 42 listed executions, 0 failed/skipped | 42 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-5 | S4 | `tests/Antiphon.Tests -> bin-c987-inventory/` | linux-worktree-inventory | `/*/*/HostCleanupWorktreeInventoryTests*/*` | V-13, R-2 | all 12 listed executions, 0 failed/skipped | 12 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-10 | S7 | `tests/Antiphon.SessionRunner.Tests -> bin-c987-runner/` | linux-cleanup-runner | `/*/*/HostCleanupCommandTests*/*` | V-8, R-4 | all 12 listed executions, 0 failed/skipped | 12 | 10 | true | `TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-6 | S5 | `tests/Antiphon.Tests -> bin-c987-orchestration/` | linux-orchestration | `/*/*/HostCleanupOrchestrationTests*/*` | V-5, R-3 | all listed, 0 failed/skipped; 14 + 2 schema | 16 | 12 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-8 | S6a | `tests/Antiphon.Tests -> bin-c987-maintenance/` | linux-maintenance-gate | `/*/*/HostCleanupMaintenanceTests*/*` | V-7, R-2 | all listed, 0 failed/skipped | 10 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-7 | S6b | `tests/Antiphon.Tests -> bin-c987-delivery/` | linux-report-delivery | `/*/*/HostCleanupReportTests*/*` | V-6, R-4 | all listed, 0 failed/skipped; 20 + 2 landed | 22 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-9 | S6b | `tests/Antiphon.Tests -> bin-c987-service/` | linux-cleanup-service-union | `/*/*/(HostCleanupOrchestrationTests*)\|(HostCleanupReportTests*)\|(HostCleanupMaintenanceTests*)\|(HostCleanupWorktreeInventoryTests*)/*` | V-5, V-6, V-7, V-13, R-2, R-3, R-4 | all listed, 0 failed/skipped; 16 + 22 + 10 + 12 | 60 | 15 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-11 | S8 | `tests/Antiphon.Tests -> bin-c987-scripts/` | linux-cleanup-scripts | `/*/*/HostCleanupScriptTests*/*` | V-9, R-3 | all 12 listed executions, 0 failed/skipped | 12 | 10 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-12 | S8 | n/a | linux-attention-client | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/attention/attentionVisuals.test.ts` | V-12, R-4 | 30 = 24 plain + 6 Arguments; 0 failed/skipped | n/a | 2 | true | n/a |
| CP-14 | S9 | `tests/Antiphon.Tests -> bin-c987-linux/` | linux-native-adapter | `/*/*/HostCleanupLinuxTests*/*` | V-10, R-5 | all listed, 0 failed/skipped; Linux only | 6 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-15 | S10 | `tests/Antiphon.Tests -> bin-c987-windows/` | windows-native-adapter | `/*/*/HostCleanupWindowsTests*/(Swapped_junction_preserves_outside_sentinel*)\|(Case_and_path_aliases_cannot_escape_root*)\|(Held_file_returns_partial_and_retains_marker*)\|(File_id_substitution_refuses_delete*)\|(Long_path_uses_bound_handles*)\|(Windows_boot_and_start_uncertainty_is_retained*)` | V-11, R-5 | all listed, 0 failed/skipped; Windows only | 6 | 8 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |
| CP-16 | S10 | `tests/Antiphon.Tests -> bin-c987-ps51/` | windows-ps51-wrappers | `/*/*/HostCleanupWindowsTests*/Windows_powershell_51_parses_and_runs_wrappers_offline*` | V-11, R-3 | 1 result; real powershell.exe 5.1 with recording cmdlets; zero native delete/restart/register | 1 | 6 | true | `C804_ORPHAN_SWEEP_ROOT=c826-disabled;TUNIT_MAX_PARALLEL_TESTS=1` |

## Cost, TestDesign handoff and completion gates

The 17 retained rows total 145 estimated minutes; the eleven added rows total 105. Table total is
250, plus the separately declared pre-move CP-2 invocation (12) = **262 ordinary minutes**.
S1a0 selects 38 minutes, S1a1 16, S1a 26; these are included in D-4's authoring-plus-verification
estimates. S1b has 8; S2 has 57 including both CP-2 runs and CP-25/27/28; S3 has 22 including CP-13.
The other slice row sums are S4 10, S7 10, S5 12, S6a 6, S6b 25, S8 12, S9 6, S10 14.
Red-first/failure reruns add their actual cost; no blanket second full pass is planned. Budget other
Code commissions as authoring + those row sums after TestDesign binds their missing methods; the
old universal 30-60 minute claim does not survive this expanded scope.

The historical Mutation estimate (260 controls x 4 minutes + 30 = 1070 minutes) is a retained
planning baseline, not a current complete budget. TestDesign must price new controls/variants and
publish ordinary and Mutation floors separately and combined. All PCs remain pending for separately
commissioned SourceLanding Mutation after Code, ordinary Review and confirmed land; no PCs ran here.

TestDesign's next artifact must: (1) be committed in the start tree, (2) bind every current method and
variant plus new supplemental roster, (3) update G-204/PC-204 and G-206/PC-206, (4) cover current
marker/custody/receipt/Git/handle dependencies with compiling exact-method defects and named red
assertions, (5) verify slice dependency closure without a fake policy shortcut, and (6) retain every
pending historical control unless explicitly superseded with a reason and replacement. Do not
handoff directly to Code using the absent/stale October 12 verification file.

Activation remains a later operator action under the parent's seven-step sequence: current namespace
inventory/identity, unresolved holds, old weekly deleter cutover confirmation, durable receipt/monitor
path, per-host dry-run/native qualification, independent Review/land and explicit per-namespace enable.
Rollback disables execute and retains custody/evidence. No live setting or service changes were made.
Plan validation consists of source/roster inspection, Markdown/table/path checks and Git checks;
builds 0, tests executed 0, passed 0, failed 0, skipped 0. Restart required: none.
