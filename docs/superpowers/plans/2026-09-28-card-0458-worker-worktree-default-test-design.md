# CARD-0458: worker workspace defaults — TestDesign

Date: 2026-09-28. Task: `3f4700b0`. Inspected source:
`7ca550cec42852b9659078420707b6bda0128cc3` (also the remote master observed this session).
Card: `9cc8b283-126b-4fc5-9937-811aadc5e959` on Antiphon.
The [original plan](2026-09-14-card-0458-worker-worktree-default-plan.md) is the complete,
verbatim `## Design` section retrieved with `card.ps1 get CARD-0458 -Board Antiphon -Json`.
No copy existed in the inspected local/remote-tracking histories; the original Plan task's
branch was absent from `ls-remote`. The copy was committed first as `26932a91c`.

**Deliverable status: S1/S2 oracles are reconciled for Code. S3/S4 descriptions and CP-9 onward
below record the original candidate and require a later TestDesign amendment before those slices.**
This document designs tests; it adds no tests, application changes or live project settings.
All execution counts below are planned rosters, not measured passes.

## Policy conflict found at the current source

### Approved policy and acceptance reconciliation, 2026-09-28

The caller chose to preserve CARD-0644. Its card is InProgress, yet the current code and
`WorktreeDefaultAdmissionTests` already ship Worktree for a fresh omitted workspace, refusal
outside Git, and Worktree for a separate orchestrator directory. The [plan's reconciliation]
(2026-09-14-card-0458-worker-worktree-default-plan.md) is now normative. These replacements
supersede conflicting original candidate oracles in the sections below:

| Case | Reconciled oracle |
|---|---|
| AC-1 | Fresh explicit Worktree, Shared and ReadOnly win; existing-agent explicit Worktree still refuses and live explicit ReadOnly survives. |
| AC-2 | Project Worktree beats a configured global Shared; dispatch uses a separate worktree. No cleanliness probe. |
| AC-3 | Null project override inherits a configured global Shared. This is an explicit configuration case, never a claim about the shipped initializer. |
| AC-4 | Null override inherits shipped global Worktree. |
| AC-5 | Dirty source plus inherited or project Worktree remains Worktree; committed content is isolated and uncommitted files are absent. No fallback warning. |
| AC-6 | Explicit Worktree on a dirty source remains Worktree. |
| AC-7 | Fresh Deploy, Commit and Merge inherit project/global mode; existing-agent pins stay on their checkout. |
| AC-8 | Omitted live follow-up stays on its checkout; explicit ReadOnly is preserved. |
| AC-9 | Omitted Worktree outside Git refuses with `workspace_default_not_git` before insert. Explicit Shared remains available. |
| AC-10 | No cleanliness probe exists; guard that project/global resolution has no Git status process call. |
| AC-11 | Fresh orchestrators use project/global mode, including a distinct directory; non-Git Worktree refuses. |
| AC-12 | New tasks persist and expose mode/source plus authoritative repoPath/projectId; historic source is null. DirtySource, NoRepository and Orchestrator are not produced under this policy. |
| AC-13 | Shared-only lease text distinguishes Explicit, Project, Global and Pinned sources; no DirtySource branch. |
| AC-14 | Delegate echo uses the actual created response fields and never invents a project name from a path. |
| AC-15 | Project create/update/list/detail and Settings expose configured and effective values; null PUT preserves and Inherit clears. Setup modal creates through `/api/projects/setup`, so its request and service must pass the field to `ProjectService.CreateAsync`. |
| AC-16 | Migration only adds nullable columns. Global initializer is Worktree, task entity's historical initializer stays Shared; S1/S2 do not alter admission. |
| AC-17 | Advice is confined to a proven shared-only hold; area contention and unknown holder scope retain neutral labels. |
| AC-18 | `project.ps1 set` extends the existing verb; one switch never overwrites the other setting or other project fields. |

For S1/S2, CP-1 through CP-8 are the closed manifest under the Final profile. In AC-15, the new-project UI test
targets the setup POST and its existing wizard, not an imaginary direct create modal.
In AC-16, PC-16c changes Worktree to Shared and must fail the initializer assertion.
All S3/S4 method names, result floors and PC variants below are **provisional** wherever
they encode an overridden oracle; a later TestDesign must replace them before CP-9 runs.
The 62-variant estimate and 57-result resolution floor are historical candidate estimates,
not commitments under the reconciled policy. This task runs no PCs; all remain pending
method-scoped SourceLanding Mutation.

The card's September 14 ground truth is historical. CARD-0644 has since implemented
[a different default policy](2026-09-23-card-0644-worktree-default-plan.md).
Do not delete its tests or call their failures pre-existing failures to make this plan pass.

| Conflict | Current production evidence | Original CARD-0458 expectation affected |
|---|---|---|
| Fresh omitted workspace already means Worktree for every role. | `AgentTaskService.EffectiveRequestedWorkspace`; `WorktreeDefaultAdmissionTests.FreshWorkerUsesWorktree`. | D-2 Shared global initializer, D-3 role exceptions, AC-3, AC-7, AC-16's claim that deployment changes nothing. |
| A different orchestrator directory is not isolation. | `WorktreeDefaultAdmissionTests.FreshOrchestratorInOtherRepoUsesWorktree`. | D-3 step 3 and AC-11 would restore Shared for a separate directory. |
| Omitted Worktree outside Git refuses before insert. | `AgentTaskService.CreateAsync`, code `workspace_default_not_git`; `WorktreeDefaultAdmissionTests.NonGitDefaultRefusesBeforeInsert`. | AC-9 and the non-repo arm of AC-11 require successful Shared fallback. |
| Existing-agent selection refuses explicit Worktree; explicit ReadOnly is preserved. | Live follow-up normalization and final pin check; `WorktreeDefaultContinuationTests.ExplicitWorktreePinRefuses` and `LiveFollowUpKeepsCwd`. | AC-1 must be limited to fresh requests; AC-8 means omitted workspace, not every explicit mode. |
| Structured selectors accept omission when effective mode is Worktree. | `WorktreeDefaultAdmissionTests.DefaultWorktreeAdmitsStructuredBases`; `DelegateScriptWorkspaceDefaultTests.StartRefAcceptsOmittedWorktree`. | D-3's historical explicit-Worktree-only SourceLanding rule is no longer true. |
| Retired follow-up preserves the predecessor's frozen tip. | `WorktreeDefaultContinuationTests.RetiredWorktreeCutsAtPriorTip` and `FrozenTipSurvivesPriorRefMovement`. | A new default/dirty probe must not silently redirect this continuation into Shared. |
| The project set surface already exists. | `scripts/project.ps1 set -CommitOnSettle`; `ProjectConfig` and `CommitOnSettleScriptTests`. | S2 extends this surface; it must not replace the verb or clear commit policy. |

**Decision PD-1:** recommended disposition is return to Plan to reconcile CARD-0458 with
CARD-0644, preserving the current fresh-process/non-Git/orchestrator rules unless the caller
deliberately chooses to reverse them. Decide also whether dirty-source fallback and public
Deploy/Commit/Merge exceptions still belong. The global initializer cannot simultaneously be
Shared and leave current fresh-task behavior unchanged at S3. Historical saved task modes
can and must remain unchanged under either policy.

The following is a complete **original-plan candidate**, using D-1 through D-8 as the
oracles, with the explicit clarifications below. It is suitable for review of that choice;
it is not permission to reverse CARD-0644. If preservation is selected, Plan must amend the
affected oracles, controls, regression expectations and checkpoint roster before Code.
S1/S2 can add dormant storage and project surfaces with zero dispatch change; S3 is the
behavior boundary. S6 is excluded. H-1/H-2/H-3 are operator writes, not this implementation.

Candidate clarifications needed to make the original cases unambiguous:

- Fresh explicit modes on a Git repo are AC-1. Existing-agent and non-Git explicit-Worktree
  refusals remain admission guards, not default overrides. Live omitted follow-up reports
  `Pinned`; explicit ReadOnly reports `Explicit`.
- Worker Deploy/Commit/Merge or an existing-agent pin, with no workspace, reports `Pinned`.
  Worker no-repo fallback reports `NoRepository`; historical orchestrator own-directory
  resolution reports `Orchestrator`, and its no-repo fallback reports `NoRepository`.
- Global-default dirty fallback uses the same behavior as project-default dirty fallback;
  its diagnostic says `Global default`, not the misleading `Project default`.
- A parent task's null ProjectId remains null. A target directory never supplies a project.
- The create echo needs the actual repository and commissioning project. The current
  `AgentTaskCreatedDto`/`ScopeOverlapDto` do not provide them. Within S3's already-listed
  `AgentTaskDtos.cs`, expose `repoPath` and nullable `projectId` with the new mode/source,
  or provide equivalent authoritative response data before finalizing this candidate.
  `project.ps1 set` accepts a GUID; never infer a project name from a path. Test a null-project
  response without emitting a fictitious `project.ps1 set null` command.
- D-5's `-Worktree would run it now` advice is restricted to a shared-checkout-only hold.
  Intersecting scopes, concurrency caps, dependencies and routing-pin waits keep their
  existing distinct messages. This advice does not promise to bypass other admission gates.

Two small file-list corrections are necessary within the original slices. S1 needs the
production mappings in `server/Infrastructure/Data/AppDbContext.cs`, not just a migration
and snapshot. S4 needs `AgentTaskPipelineStatusService.cs`, `AgentTaskPipelineDtos.cs` and
`client/src/api/agentTasks.ts`: today the same `sharedCheckoutLease` reason represents both
true scope contention and mere shared-checkout serialization, while the holder DTO drops
`Overlap.Areas`. Add nullable `areas` to each holder and preserve it in that projection so
the client can withhold isolation advice if **any** holder has intersecting areas. An older
response with the property absent is unknown, not affirmative no-intersection evidence;
retain its neutral existing label. Add the one exact pipeline projection test below rather
than testing an invented client-only field. These are documented coverage gaps in the old
file list, not production changes made by this task.

## Verification design

### Fixtures and observation boundaries

Use `TestDbFixture.CreateIsolatedSchemaAsync` for migrations and service tests. Scope every
row assertion by test-owned IDs; no global row-count assertions against the shared database.
Migration tests downgrade **only their isolated schema** to the immediately preceding
migration, insert legacy rows using SQL that does not mention the new columns, then migrate
forward. Inserting new-model rows after migration does not prove upgrade compatibility.

Use `ScratchGitRepo` with an initial commit and a test-owned worktree root. The cleanliness
fixture commits `.gitignore`, `a-tracked.txt` and `b-tracked.txt` first. It then makes one staged
and one unstaged tracked edit and two top-level untracked files `c-new.txt`, `d-new.txt`.
Mixed case counts are exactly two tracked paths and two untracked paths, not the number of
index/worktree status characters. Three diagnostic paths are the first three emitted by the
probe; assert the fourth is absent. Ignored `bin/`, `obj/`, `node_modules/` are separate cases.
Assert scratch Git status/HEAD after creation so neither auto-commit nor copying source edits
can masquerade as the fallback. No operations touch the real checkout's worktree state.

Exercise `AgentTaskService.CreateAsync`, reload the task using a fresh DbContext, and inspect
its own Created event. Add one actual dispatch to AC-2 using the existing `RepairSourceWorld`
pattern: real Git provisioner, recorded fake runner launch, unique branch/path and launch cwd.
Register Git dependencies using `DelegationTestServices`; never boot a real provider. HTTP
assertions use `AntiphonWebAppFactory` with its production-runner guard/refusing runner.
The cleanliness process seam is an external-I/O seam in `DelegationWorkspaceResolver`, not a
mock of the final workspace decision. Use real Git for clean/dirty/ignored coverage and a
controllable process seam for failure/timeout/cancellation. Its test records executable,
arguments, cwd and deadline, can block until cancellation, and records disposal/termination.
Do not sleep for ten real seconds just to assert the budget; assert the ten-second deadline
and drive the timeout through the seam, then assert the distinct timeout outcome. Dispose and
await owned children; a timeout assertion must not leave a process behind.

Script tests launch the real `scripts/delegate.ps1` through `DelegateScriptRunner`, with the
task token cleared and a loopback recording API. Project script tests reuse the process/stub
pattern from `CommitOnSettleScriptTests`, with a stateful GET/PUT store and a second GET to
prove persistence. A canned PUT response that simply repeats the requested value is not a
round-trip test. All process-spawning classes take the assembly-local
`[ParallelLimiter<ProcessSpawnLimit>]`. Tag each class Unit xor Integration; mark/register
Slow only when required by the existing tripwire. Use real/offset clocks for queue code.

Client tests use the established MSW/render helpers and the existing three test files.
Observe the submitted HTTP body and visible label; do not test a duplicate resolver.
`agent-task-detail.json` gains source data, and the production TypeScript DTO must expose it
where consumed. A legacy null source still deserializes. The client build checks these types.

### File and class roster

New paths below are proposed Code-stage files, not files created by TestDesign. Keep the
original plan's named S2/S3/S4 files. Test support additions stay in their owning slice.

| Slice | Path / exact class | Lane and planned result floor |
|---|---|---|
| S1 | `tests/Antiphon.Tests/Migrations/WorkerWorkspaceDefaultStorageTests.cs` / `WorkerWorkspaceDefaultStorageTests` | Unit, 6 |
| S1 | `tests/Antiphon.Tests/Migrations/WorkerWorkspaceDefaultMigrationShapeTests.cs` / `WorkerWorkspaceDefaultMigrationShapeTests` | Unit, 2 |
| S1 | `tests/Antiphon.Tests/Migrations/WorkerWorkspaceDefaultMigrationTests.cs` / `WorkerWorkspaceDefaultMigrationTests` | Integration, 2 |
| S2 | `tests/Antiphon.Tests/Application/ProjectWorkerWorkspaceDefaultTests.cs` / `ProjectWorkerWorkspaceDefaultTests` | Integration, 17 |
| S2 | `tests/Antiphon.Tests/Scripts/ProjectScriptWorkspaceTests.cs` / `ProjectScriptWorkspaceTests` | Integration/process, 9 |
| S2 | `client/src/features/settings/ProjectConfig.test.tsx` | 7 new Vitest results, plus existing cases |
| S3 | `tests/Antiphon.Tests/Application/AgentTaskWorkspaceDefaultTests.cs` / `AgentTaskWorkspaceDefaultTests` | Integration/process, 57 |
| S4 | `tests/Antiphon.Tests/Application/SharedWriterLeaseProjectionTests.cs` / `SharedWriterLeaseProjectionTests` | Unit, 7 new results + 6 existing = 13 |
| S4 | `tests/Antiphon.Tests/Application/DelegateScriptWorkspaceTests.cs` / `DelegateScriptWorkspaceTests` | Integration/process, 23 |
| S4 | `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs` / existing `AgentTaskPipelineStatusTests` | Integration, one new parameterized method / 3 results |
| S4 | `client/src/features/home/tasks/homeTasksModel.test.ts`, `client/src/features/orchestrator/pipelineStageModel.test.ts` | 6 new Vitest results in each file, plus existing cases |
| S5 | Original S5 documentation paths and `scripts/delegate.ps1` comments | Documentation consistency review and whitespace check; no behavioral test invented for prose |

All C# method names below are exact. A parameter tuple is a separate `[Arguments]` result
unless explicitly called an internal assertion matrix. Suffix-free method filters with `*`
select all argument rows. Names in this document are the execution roster contract.

### AC-1 — explicit modes override every default

`AgentTaskWorkspaceDefaultTests.C458_ExplicitWorkspaceOverridesEveryDefault` (18 results):
project `{null, Shared, Worktree}` x global `{Shared, Worktree}` x explicit
`{Worktree, Shared, ReadOnly}`. Fresh Worker/Plan in a clean Git repo; assert persisted mode
equals the explicit value, source `Explicit`, and zero cleanliness probes.

**PC-01:** move default resolution ahead of/around the explicit-mode return in
`AgentTaskService.ResolveWorkspace` so explicit Shared/ReadOnly are overwritten by Worktree.
The contradictory-default rows must fail on persisted mode/source, not admission or setup.

### AC-2 — clean project Worktree default

`AgentTaskWorkspaceDefaultTests.C458_CleanProjectDefaultUsesWorktree` (1): project Worktree,
global Shared, Worker/Plan, omitted mode. Assert Worktree/Project, one clean probe, no dirty
warning, then real dispatch creates `feat/card-task-<id>` at a separate cwd/branch. Its fake
runner's recorded cwd must equal the new worktree; the caller's HEAD/ref stay unchanged.

**PC-02a:** ignore the project override and return the global Shared value; mode assertion
fails. **PC-02b:** keep the row Worktree but make dispatch pass the source checkout as launch
cwd; the recorded-cwd assertion fails. A DTO-only assertion would miss this control.

### AC-3 — inherited global Shared

`AgentTaskWorkspaceDefaultTests.C458_NullProjectDefaultUsesSharedGlobal` (2): one task has a
project whose field is null; the other has null ProjectId while its target repo belongs to
an unrelated Worktree-configured project. Global Shared; both persist Shared/Global and
probe zero times. This describes the candidate policy, not current CARD-0644 behavior.

**PC-03:** hard-code the inherited/default arm to Worktree. Assert mode differs, not just
an absent source string.

### AC-4 — inherited global Worktree

`AgentTaskWorkspaceDefaultTests.C458_NullProjectDefaultUsesWorktreeGlobal` (2): same two
project-identity shapes with global Worktree and clean Git source. Assert Worktree/Global,
one probe and no warning.

**PC-04:** hard-code the inherited arm to Shared. Both rows must fail on the saved mode.

### AC-5 — dirty default falls back with accurate evidence

`AgentTaskWorkspaceDefaultTests.C458_DirtyDefaultFallsBackWithCountsAndPaths` (4):
project default with tracked-only `(2,0)`, untracked-only `(0,2)`, mixed `(2,2)`, and global
Worktree with mixed `(2,2)`. Assert Shared/DirtySource, repo path, counts, first three paths,
`Running Shared instead`, `-Worktree`, `-Shared`, and the warning about files missing from
the isolated branch. Preserve source status/HEAD and create no worktree at this boundary.
`C458_IgnoredOnlyCheckoutKeepsDefaultWorktree` (1) adds only ignored output after the clean
commit; assert Worktree/Project, zero dirty paths and no dirty warning.

**PC-05a:** suppress the dirty fallback; the first method fails on mode. **PC-05b:** run
status with `--untracked-files=no`; its untracked-only row fails on mode and mixed rows on
counts. **PC-05c:** add `--ignored` and treat `!!` paths as dirt; the ignored-only method
fails. **PC-05d:** report tracked count as zero while retaining fallback; the first method's
tracked-count assertion fails. These are separate controls, not one combined mutation.

### AC-6 — explicit Worktree on dirty source

`AgentTaskWorkspaceDefaultTests.C458_ExplicitWorktreeKeepsDirtySourceIsolated` (1): use the
mixed fixture, project Worktree, explicit Worktree. Assert Worktree/Explicit, zero probes,
no fallback warning, unchanged dirty source. Dispatch through the scratch Git provisioner;
the worktree has committed tracked content and neither untracked file.

**PC-06:** remove the default-only condition around the cleanliness check/fallback.
The saved mode/probe-count assertion fails; do not mutate Git to throw an unrelated error.

### AC-7 — in-place roles and standing pins

`AgentTaskWorkspaceDefaultTests.C458_InPlaceRolesAndStandingPinsStayShared` (5): omitted mode,
project Worktree and clean source; rows are fresh Worker Deploy, Commit, Merge, Worker/Plan
with standing `agent` name, and Worker/Plan with `agentId`. Pin rows seed an authorized agent
with a distinct cwd. Assert Shared/Pinned, no probe, and preserved pinned cwd/identity.
The three public-role rows intentionally conflict with current CARD-0644 and need PD-1.

**PC-07a:** remove the Deploy/Commit/Merge role exception; those rows fail on mode.
**PC-07b:** let project-default resolution overwrite an existing-agent selection after
pin resolution; the two pin rows fail on mode/cwd (not an expected successful refusal).

### AC-8 — live OnAgent follow-up

`AgentTaskWorkspaceDefaultTests.C458_LiveFollowUpUsesExistingCheckout` (1): seed a live
predecessor/session and follow it with no explicit mode under project Worktree. Assert
Shared/Pinned, same agent and cwd, same follow-up reference, no worktree/probe. A retired
predecessor is a separate regression, never substituted for the live fixture.

**PC-08:** after follow-up normalization, reapply the project Worktree default as if this
were a fresh task. The same-cwd/mode assertions must fail. Keep the fixture live.

### AC-9 — non-repository fallback

`AgentTaskWorkspaceDefaultTests.C458_NonRepositoryDefaultFallsBackToShared` (1): authorized
temporary directory outside every Git repo, Worker/Plan, project Worktree, omitted mode.
Assert successful creation, RepoPath null, Shared/NoRepository, warning containing `is not
a git repository`, and no cleanliness call. This is an intentional change from today's 422.

**PC-09:** retain the `workspace_default_not_git` throw for this defaulted path; the test
fails because create should succeed under the candidate. Do not weaken explicit-Worktree
non-repo refusal.

### AC-10 — uncertain probe preserves isolation

`AgentTaskWorkspaceDefaultTests.C458_ProbeFaultKeepsWorktreeWithWarning` (2): seam returns
timeout or a nonzero Git exit under project Worktree; assert Worktree/Project and
`could not verify that <repo> is clean`. A failure is not empty/clean evidence.
`C458_ProbeUsesPorcelainNormalAndTenSecondBudget` (1): invoke the real probe boundary through
the recording process seam; assert `git status --porcelain --untracked-files=normal`, resolved
RepoPath as cwd (not the requested subdirectory), deadline ten seconds, awaited process cleanup.
`C458_CallerCancellationDoesNotCreateTask` (1): caller cancels while probe is pending; assert
cancellation propagates, no new task/event for the test's ID, and child cleanup completes.

**PC-10a:** turn a timeout/nonzero exit into Dirty and Shared; the fault method fails on
mode. **PC-10b:** return Worktree without the warning on faults; its warning assertion fails.
**PC-10c:** remove the probe's ten-second deadline; the budget method fails on the recorded
deadline (without waiting indefinitely). **PC-10d:** catch caller cancellation as a probe
fault and continue inserting; the cancellation method fails on cancellation/row assertions.

### AC-11 — historical orchestrator rules

`AgentTaskWorkspaceDefaultTests.C458_OrchestratorKeepsHistoricalResolution` (3): no explicit
mode; separate authorized directory gives Shared/Orchestrator; inherited Git cwd gives
Worktree/Orchestrator; inherited non-Git cwd gives Shared/NoRepository with warning. Set
Worker defaults oppositely where possible and assert no Worker-default cleanliness probe.
This is the old plan's meaning of unchanged, and contradicts current CARD-0644.

**PC-11a:** run orchestrators through the Worker project-default branch; inherited-Git row
with project Shared fails. **PC-11b:** remove the distinct-directory Shared rule; its row
fails against Worktree. **PC-11c:** suppress the non-Git warning; the non-Git row fails.

### AC-12 — persisted, event and wire provenance agree

`AgentTaskWorkspaceDefaultTests.C458_WorkspaceProvenanceSurvivesCreateReloadAndDetail` (7):
construct one genuine resolution for each source `Explicit`, `Project`, `Global`,
`DirtySource`, `NoRepository`, `Pinned`, `Orchestrator`. Do not seed the expected source
directly. Compare the create DTO, fresh DB reload, serialized create/detail HTTP payloads,
and the task's Created event with the literal expected mode/source for that row. The project
row must include `Worktree (project default)`; other rows must identify their corresponding
cause. Assert `workspace`/`workspaceSource` string properties, real `repoPath` and `projectId`.
Also deserialize a historical detail with null source in the same test's internal matrix.

**PC-12a:** omit assigning `task.WorkspaceSource`; fresh reload fails. **PC-12b:** remove
the workspace/source suffix from the Created event; event assertion fails. **PC-12c:** map
created DTO source to null; create wire assertion fails. **PC-12d:** map detail DTO source
to null while retaining persisted/create values; detail wire assertion fails.

### AC-13 — Held explanations preserve the lease decision

In existing `SharedWriterLeaseProjectionTests`, add
`C458_SharedOnlyHeldTextExplainsWorkspaceSource` (5): queued source Explicit, Project, Global,
DirtySource, historical null. Same-repo Shared holder, no intersecting scope; first assert
Serialise and blocking holder identity, then exact D-5 text. Source clauses are `you passed
-Shared`, `nothing asked to isolate this task`, or `it fell back to Shared because <repo>
had uncommitted changes`; null uses the neutral default clause. Assert `Not a dependency`,
both remediation choices and the holder title/short ID. Add
`C458_IntersectingScopeTextRemainsContention` (2): same area for Shared/Shared Serialise and
Worktree/Shared Warn; assert the old intersection/rebase sentences and no no-dependency advice.

**PC-13a:** always choose the default source clause; Explicit and DirtySource rows fail.
**PC-13b:** use the new no-intersection sentence even when Areas is non-null; the contention
method fails. **PC-13c:** change no-intersection Shared/Shared policy to Allow; the source
method fails on Serialise before formatting. This detects accidentally removing the lock.

### AC-14 — real delegate script wire and echo

New `DelegateScriptWorkspaceTests` on `DelegateScriptRunner`:

| Exact method | Results | Input and assertion |
|---|---:|---|
| `C458_OmittedWorkspaceStaysOffWire` | 1 | No workspace switches: one POST and no `workspace` property. Server response supplies Worktree/Project; print `workspace: worktree (project default)`. |
| `C458_ExplicitWorkspaceSwitchesRoundTrip` | 3 | Each of Worktree/Shared/ReadOnly posts its exact mode; no default inference in the script. |
| `C458_EchoUsesResolvedWorkspaceAndSource` | 7 | Responses for all seven source values from AC-12; assert matching mode and cause, including Shared/DirtySource for an omitted request. |
| `C458_SharedWaitEchoExplainsSourceAndHolderCount` | 10 | Five AC-13 source shapes x one/two holders. Assert D-5 sentence, actual repo and project GUID, total running count N, first holder ID/title and correct source clause. |
| `C458_IntersectingWaitEchoKeepsContentionMessage` | 1 | Areas non-null; preserve old serialise/warn messages using an internal assertion matrix; do not promise immediate isolation. |
| `C458_LegacyAndUnscopedResponseDoesNotInventProject` | 1 | Internal matrix: old response lacking new properties and new response with null ProjectId. Exit zero, preserve warnings, no fabricated mode/project/default-setting command. |

**PC-14a:** always put `workspace=Shared` in omitted POST bodies; omission method fails.
**PC-14b:** omit the explicitly selected property; explicit method fails. **PC-14c:** print
the request's switch/default instead of the server's resolved values; source-echo method's
DirtySource row fails. **PC-14d:** restore the old `will wait behind` branch; the ten-result
wait method fails on reason, remediation and count. **PC-14e:** treat every overlap as a
shared-only hold; contention method fails. **PC-14f:** derive project from the caller's cwd
when response ProjectId is null; legacy/unscoped method fails on the invented command.

### AC-15 — project value, null-preserve, Inherit and validation

New `ProjectWorkerWorkspaceDefaultTests`:

| Exact method | Results | Oracle |
|---|---:|---|
| `C458_CreateStoresAndReportsProjectDefault` | 6 | Create value omitted/Shared/Worktree x two globals. Fresh DB and DTO retain nullable configured value; effective field is stored-or-global. |
| `C458_UpdateNullPreservesStoredDefault` | 2 | Seed Shared/Worktree; PUT explicit JSON null, then PUT omitting field. Both preserve stored value and unrelated fields (two requests inside each result). |
| `C458_UpdateInheritClearsStoredDefault` | 2 | Two globals, opposing stored override. PUT literal `Inherit`; persisted value becomes null; effective response changes to global. |
| `C458_UpdateRejectsReadOnlyWithoutChangingProject` | 1 | Real HTTP PUT `ReadOnly` is 422 with field-specific Problem Details; fresh DB proves workspace and other fields unchanged. Internal negative controls also cover unknown value and create ReadOnly. |
| `C458_ListAndDetailResolveEffectiveDefault` | 6 | Three stored values x two globals through GET list and detail; assert configured and effective fields from production mapping. |

In `ProjectConfig.test.tsx` (first three names) and `ProjectSetupModal.test.tsx` (the fourth),
add exact Vitest names:

- `C458 displays inherited workspace from effective default` (`it.each`, 2 globals).
- `C458 changing worker workspace submits selected value` (`it.each`, Shared/Worktree/Inherit,
  3 results); use literal `Inherit`, not null, to clear an existing override.
- `C458 unrelated save omits worker workspace` (1); omit the field, preserve commit policy/env.
- `C458 new project submits worker workspace` (1); interact with `ProjectSetupModal` and assert
  the `/api/projects/setup` POST body, then verify its service passes the value to project create.

**PC-15a:** assign null on an absent/null update; preservation method fails.
**PC-15b:** interpret Inherit as leave-unchanged; clear method fails.
**PC-15c:** allow ReadOnly through project validation; 422/no-change method fails.
**PC-15d:** compute effective as global unconditionally; list/detail override rows fail.
**PC-15e:** discard the create field; create method's configured rows fail.
**PC-15f:** serialize UI Inherit as null; the selected-value Inherit row fails on PUT body.
**PC-15g:** label inherited Worktree as Shared in `ProjectConfig`; inherited-display row fails.

### AC-16 — additive migration and historical compatibility

`WorkerWorkspaceDefaultStorageTests` (Unit):

- `C458_DefaultsAreWorktreeAndHistoricalSourceIsNull` (1): settings initializer Worktree;
  entity Project default null, task source null, legacy task entity mode Shared. S1/S2 keep
  current fresh API Worktree admission.
- `C458_GlobalDefaultValidatorAllowsOnlyWritableModes` (4): Shared and Worktree succeed;
  ReadOnly and an undefined numeric enum value fail on the workspace option. Use the real
  `DelegationSettingsValidator`, not a test-local allowed-values list.
- `C458_EfMappingsStoreNullableTextWithStableWorkspaceOrdinals` (1): inspect production EF
  mappings for nullable text project/source columns, string conversions and unchanged
  WorkspaceMode numeric values Shared=0, Worktree=1, ReadOnly=2. Assert no new DB default.

`WorkerWorkspaceDefaultMigrationShapeTests` (Unit):

- `C458_UpAddsOnlyNullableWorkspaceColumns` (1): actual generated migration has exactly two
  AddColumn operations, `Projects.DefaultWorkerWorkspace` and `AgentTasks.WorkspaceSource`,
  nullable text, no default/defaultSql/backfill/alter/drop. Match the generated migration's
  real timestamp; the historical `2026091xxxxxxx` is not a filename to hand-create.
- `C458_DownDropsOnlyTheAddedColumns` (1): exactly those two DropColumn operations.

`WorkerWorkspaceDefaultMigrationTests` (Integration):

- `C458_UpgradePreservesLegacyRowsAndAddsNullColumns` (1): pre-migration project and task
  rows include each saved mode, Queued/Working/terminal states, ProjectId (including null),
  refs/paths and nondefault project metadata. Apply migration. Raw SQL and fresh EF reads
  prove all originals unchanged and new fields null; no model/snapshot drift. Under S3 the
  configured global Shared case uses AC-3; never re-resolve legacy queued tasks here.
- `C458_DownThenUpPreservesLegacyWorkspace` (1): isolated migration down/up round-trip;
  legacy modes/IDs survive, columns disappear/reappear, source/default null after re-add.

**PC-16a:** give the new project column a Worktree default; shape and legacy-upgrade tests
fail. **PC-16b:** add an UPDATE resetting legacy `AgentTasks.Workspace`; upgrade test fails
on the seeded non-Shared modes. **PC-16c:** change shipped global initializer to Shared;
storage-default method fails. **PC-16d:** permit ReadOnly in the global validator; validator
method fails. **PC-16e:** remove the EF string conversion for project/source; mapping method
fails on provider type. **PC-16f:** omit WorkspaceSource from Down; down-shape and round-trip
column assertions fail. Controls must compile; a broken migration constructor is not red
evidence. To avoid duplicate phase runs, PC-16a uses the shape method, PC-16b the upgrade
method, and PC-16f the Down shape method as their exact Mutation targets.

### AC-17 — home rail and pipeline labels

In **each** existing `homeTasksModel.test.ts` and `pipelineStageModel.test.ts`, add these
exact Vitest names (the file path disambiguates matching names):

- `C458 shared checkout wait names holder and isolation remedy` (`it.each`, one/two holders,
  2 results). Expect D-5 `waiting: sharing the shared-checkout slot with task-<id> - <title>`
  plus ` (+1)` only for two holders and `; -Worktree would run it now`.
- `C458 shared checkout wait without holders remains readable` (1): no undefined title/ID
  or negative extra count; neutral shared-checkout label.
- `C458 real contention never promises immediate isolation` (1): sharedCheckoutLease with
  heldBy Areas non-null retains a contention label, no `would run it now`.
- `C458 other queue reasons retain their labels` (1): internal matrix for sibling land,
  concurrency cap, routing-pin time and awaitingDispatch; exact existing outputs.
- `C458 historical Held event does not override a current queue reason` (1): historical
  Held plus current awaitingDispatch; render the current reason, not stale text.

Test the public production `queueReasonFor` and `compactQueueReason`, not copied functions.
An equality against a constant declared by the implementation is not an oracle.
The no-holders test also checks a legacy holder without an `areas` property as an internal
assertion matrix: it must not promise immediate isolation. The real-contention test also
checks mixed holders (first `areas: null`, second intersecting) to prevent first-holder-only
logic from hiding contention.

`AgentTaskPipelineStatusTests.C458_HolderProjectionPreservesScopeContention` (3): use the
real status service with test-owned queued/running rows for no intersection, intersection,
and mixed holders. Assert actual queue reason and serialized holder `areas` values from
`Overlap.Areas`; null is emitted for positively known no-intersection. Do not manually seed
the DTO. This closes the server-to-client evidence gap identified above.

**PC-17a:** restore the old home-rail formatter; first home test fails. **PC-17b:** restore
the pipeline's old `behind ...` formatter; first pipeline test fails. **PC-17c:** always use
`heldBy.length` as the extra-holder count; first home test's two-holder row fails.
**PC-17d:** remove Areas discrimination from the isolation advice; real-contention test
fails in each file. Mutate each file separately for this last control (two variants).
**PC-17e:** map every holder's Areas to null in `AgentTaskPipelineStatusService.ToQueued`;
the projection method's intersecting and mixed rows fail on serialized values.

### AC-18 — project.ps1 set round-trips without collateral writes

New `ProjectScriptWorkspaceTests`:

- `C458_SetDefaultWorkerWorkspaceRoundTrips` (6): value Shared/Worktree/Inherit x project
  exact name/GUID. Run the real script against a stateful stub that validates the route,
  records GET then PUT and stores the body using null-preserve semantics. A second read
  must expose the new stored/effective values. Assert output uses returned values; Worktree
  can be the global value while Inherit clears the configured one.
- `C458_SetWorkspacePreservesOtherProjectFields` (1): seeded name, Git URL, local path,
  base branch, constitution, booleans, visibility, launch env and CommitOnSettle survive.
  Null-preserve optional fields may be omitted; no stale unrelated setting is overwritten.
- `C458_SetCommitPolicyPreservesWorkspace` (1): existing `-CommitOnSettle Off` remains
  accepted; workspace key omitted and stored Worktree retained.
- `C458_InvalidWorkspaceNeverPuts` (1): internal matrix of ReadOnly and unknown value;
  nonzero exit and no PUT. This complements server-side rejection rather than replacing it.

**PC-18a:** translate Inherit to JSON null; round-trip clear rows retain the old value and
fail. **PC-18b:** send the old GET workspace instead of the switch value; round-trip change
rows fail. **PC-18c:** replace the PUT's copied baseBranch with an empty value; preservation
method fails on its sentinel branch. **PC-18d:** always set workspace to Inherit on a
CommitOnSettle-only update; the independent-policy method fails.

### Additional resolution guards and existing regression roster

The following five methods belong to `AgentTaskWorkspaceDefaultTests` and its floor of 57:

| Guard | Exact method (results) | Observable assertion / control |
|---|---|---|
| R-1 project precedence in the other direction | `C458_ProjectSharedBeatsGlobalWorktree` (1) | Shared/Project, zero probes. **PC-19:** consult global before a non-null project override; saved mode fails. |
| R-2 project provenance after hoisting | `C458_CommissioningProjectWinsOverDirectoryProject` (3) | Parent with project A, parent with null project, and card-bound session A whose owning agent belongs to B; work in B's authorized repo. Configure A=Worktree, B=Shared, global=Shared. Assert captured project identity and matching mode/source. **PC-20:** derive the setting project from resolved RepoPath; A rows fail, and null-parent identity must remain null. |
| R-3 limited probe invocation | `C458_ProbeRunsOnlyForDefaultWorktree` (1) | Internal matrix of explicit modes, global/project Shared, pins and role exclusions: zero process calls; ordinary default Worktree: one. **PC-21:** run probe before explicit/pin/default filtering; zero-call assertion fails. |
| R-4 explicit live ReadOnly | `C458_LiveFollowUpExplicitReadOnlyIsPreserved` (1) | ReadOnly/Explicit, same live cwd. **PC-22:** normalize every live follow-up to Shared; mode assertion fails. |
| R-5 frozen queued decision | `C458_QueuedTaskRetainsResolvedModeAfterSettingChange` (1) | Create Worktree/Project, change project's setting to Shared, dispatch; keep saved mode/source and worktree cwd. **PC-23:** recalculate default at dispatch; saved-mode/launch assertion fails. |

R-6 runs all six existing `AgentTaskProjectScopeTests` and the retained continuation tests
`RetiredWorktreeCutsAtPriorTip`, `FrozenTipSurvivesPriorRefMovement`,
`UnavailablePriorTipRefuses`, `LiveFollowUpKeepsCwd`, `StandingAndRoutingPinsKeepCwd`,
`ExplicitWorktreePinRefuses`, `ContinuationRetainsCardContextAndPolicy` (minimum seven
continuation results; report actual argument expansion). These retain caller scope, exact
continuation tip and existing-agent guards. No role/concurrency bypass is introduced.

R-7 runs existing `DelegateScriptWorkspaceDefaultTests` (6 methods), preserving switch
conflict refusal and structured explicit modes from the current script, and the existing
`CommitOnSettleScriptTests.Project_set_CommitOnSettle_puts_the_value` (3 results),
`Project_set_PUT_carries_the_GET_fields_unchanged` (1), and
`Commit_on_settle_scripts_are_ascii_only` (1). They protect the existing set verb and scripts.

R-0 runs current `WorktreeDefaultAdmissionTests` (6 methods) at S2 to prove dormant S1/S2
did not change dispatch. Before S3, PD-1 must explicitly account for its conflicting fresh,
non-Git, orchestrator and structured-base tests, plus `RetiredSharedGetsFreshWorktree`.
These are not quietly dropped coverage: they are the named policy conflicts that prevent
dispatching Code from this candidate. A reconciled Plan must put the selected contracts in
the S3 roster and update its floor; until then the table below is conditional.

R-8 is the entire Unit lane after S3 and again after message changes at S4. R-9 is the final
client type/build check. R-10 is S5 review of the original plan's five documentation surfaces:
script header, delegate skill, orchestration owner, API owner and AGENTS index. Review checks
resolution precedence, Inherit semantics, source provenance, shared-only wait wording and
the chosen PD-1 policy agree. It must not repeat superseded claims about caps, cleanup,
authentication or worktree deployment as current facts. Documentation checks need no new
test class. Optional S6 would require an amended roster and manifest, not an unlisted run.

### Positive-control execution and cost

PCs here are proposed mutations of production decision, persistence, wire or formatting
logic. They are not edits to tests/expected strings. Every control has the named test above;
the intended failing assertion is stated there. Run all argument results of that method and
retain which ones discriminate. A fixture exception, build failure, timeout of the driver,
or zero executed tests is not an intended red.

There are **62 independent mutation variants**: PC-01 (1), 02 (2), 03 (1), 04 (1), 05 (4),
06 (1), 07 (2), 08 (1), 09 (1), 10 (4), 11 (3), 12 (4), 13 (3), 14 (6), 15 (7), 16 (6),
17 (6, including one PC-17d per file), 18 (4), and PC-19 through PC-23 (one each).
Most workspace controls share `AgentTaskService` or the probe; execute serially.

Code performs ordinary checkpoints only, then separate ordinary Review. After confirmed
publication, the caller commissions SourceLanding Mutation with a same-board verification
companion and the exact O/L/R identities from the landing receipt. Review may pass with
these PCs pending; it must retain all IDs and variants in the handoff. Mutation checks the
exact managed snapshot and uses the copied `run-checkpoint.ps1`/build-slot driver outside
the snapshot for baseline, compiling mutation/red, exact restoration/fresh build/green.
Use `/*/*/<Class>/<ExactMethod>*`, `-Expect <Class>.<ExactMethod>` and that method's result
floor above. Save separate output/results for every phase outside committed source.
Refresh restored source timestamps; never reuse mutated assemblies as green evidence.
For client controls use the real wrapper, one file and exact Vitest name:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c458-pc17a-red -- pwsh -NoProfile -File scripts/test-client.ps1 src/features/home/tasks/homeTasksModel.test.ts -t '^C458 shared checkout wait names holder and isolation remedy' -JsonResultPath <external-evidence>/pc17a-red.json
```

Require the wrapper's actual exit and nonzero named results: baseline/green zero, red
nonzero with the intended assertion. The wrapper changes cwd to `client`; its selectors
and the checkpoint rows below therefore use `src/...` consistently.
This is post-land work, not another Code checkpoint loop. No production/test repairs or
evidence commits in the SourceLanding tree; findings return to a separate repair task.

### Cost

Ordinary checkpoint floor: **76 minutes**, the sum of `EstimatedMinutes` below; add an
estimated **180 minutes authoring** for Code (`ExpectAbout` floor **256 minutes**).
Mutation execution floor: **372 minutes** (62 variants x 6 minutes for baseline/red/green
builds and selected results); add **60 minutes** discovery, triage, restoration and evidence
for a **432-minute Mutation** estimate. Combined execution floor is **448 minutes**
(76 + 372); combined Code + Mutation estimate is **688 minutes** (256 + 432), excluding
separate Review and operator activation. These are budgeting estimates, not measured runtime
or ceilings; actual cold builds/schema setup may take longer. Revise them with PD-1 if the
roster changes. `Min` below is executed TUnit results, never minutes or assertion counts.

### Execution contract

After PD-1 is reconciled and the manifest updated in Plan, use one checkpoint-tool
run per committed group, in dependency order: S1, S1-S2, S1-S3, S1-S4, S1-S5. S3 depends on
S1; S4 depends on S3; S5 is last. All rows are serial to avoid a reused output racing its
producer and to keep shared client log paths independent. Use the CARD-0723 tool, not ad hoc
build/test loops:

```powershell
# One tooling bootstrap, only if the tool is not already built:
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c458-checkpoint-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c458-tool/ --nologo
dotnet run --no-build --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c458-tool/ -- run --plan docs/superpowers/plans/2026-09-28-card-0458-worker-worktree-default-test-design.md --rows CP-1,CP-2
# Later committed groups: CP-3,CP-4,CP-5,CP-6,CP-7,CP-8; CP-9,CP-10,CP-11,CP-12;
# CP-13,CP-14,CP-15,CP-16,CP-17; CP-18,CP-19.
# If run/wait returns 75, continue the same run:
dotnet run --no-build --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c458-tool/ -- wait <run-id> --max-wait 50s
```

Use `--rows`, not a cumulative `--after S1-Sn`: the current AfterSelector also selects all
earlier eligible groups, which would repeat closed-list rows. The tool's row drivers acquire
their own build slots. The one bootstrap build is tooling overhead explicitly listed here,
not a product/test checkpoint; run/wait use `--no-build` and hold no extra outer lease while
waiting for the row drivers. Slot timeout is reported, never bypassed. The checkpoint tool
leases non-TUnit command rows itself; a nested build-slot wrapper would request a second
slot and can starve the run. Each result records CP, commit,
build status, exact filter, executed/passed/failed/skipped counts and fresh TRX path, plus
reruns. Require every listed method/argument row and zero failures/skips in feature classes;
Unit lane reports actual expanded counts and existing skips separately. A table floor alone
does not prove roster completeness. No tests/builds have been executed by TestDesign.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c458-s1/` | storage-shape | `/*/*/(WorkerWorkspaceDefaultStorageTests*)\|(WorkerWorkspaceDefaultMigrationShapeTests*)/*` | AC-16 | all 8 planned results, 0 failed/skipped | 8 | 5 | true |
| CP-2 | S1 | CP-1 | migration-upgrade | `/*/*/WorkerWorkspaceDefaultMigrationTests/*` | AC-16 | both methods executed, 0 failed/skipped | 2 | 3 | true |
| CP-3 | S1-S2 | `tests/Antiphon.Tests -> bin-c458-s2/` | project-surface | `/*/*/(ProjectWorkerWorkspaceDefaultTests*)\|(ProjectScriptWorkspaceTests*)/*` | AC-15, AC-18 | all 26 planned results, 0 failed/skipped | 26 | 6 | true |
| CP-4 | S1-S2 | CP-3 | dormant-dispatch | `/*/*/WorktreeDefaultAdmissionTests/*` | R-0, AC-16 | all 6 existing methods, 0 failed/skipped | 6 | 4 | true |
| CP-5 | S1-S2 | n/a | project-client | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/ProjectConfig.test.tsx src/features/settings/ProjectSetupModal.test.tsx` | AC-15 | all existing tests and 7 new results, CLIENT TESTS EXIT CODE 0, 0 failed | n/a | 3 | true |
| CP-6 | S1-S2 | CP-3 | storage-and-surface-unit | `/*/*/*/*[Category=Unit]` | AC-15, AC-16, Final Unit lane | entire Unit lane, 0 failed; report existing skips | 8 | 5 | true |
| CP-7 | S1-S2 | CP-3 | affected-integrations | `/*/*/(ProjectServiceTests*)\|(ProjectSetupServiceTests*)\|(CommitOnSettleScriptTests*)/*` | AC-15, AC-18, Final integration classes | every method and argument row in all three existing classes, 0 failed/skipped | 3 | 4 | true |
| CP-8 | S1-S2 | n/a | project-client-build | `npm --prefix client run build` | AC-15, Final client build | TypeScript and Vite exit 0, current bundle produced | n/a | 4 | true |
| CP-9 | S1-S3 | `tests/Antiphon.Tests -> bin-c458-s3/` | workspace-resolution | `/*/*/AgentTaskWorkspaceDefaultTests/*` | AC-1 through AC-12, R-1 through R-5 | all 57 planned results, 0 failed/skipped | 57 | 8 | true |
| CP-10 | S1-S3 | CP-9 | caller-scope | `/*/*/AgentTaskProjectScopeTests/*` | R-6 | all 6 existing methods, 0 failed/skipped | 6 | 2 | true |
| CP-11 | S1-S3 | CP-9 | continuation | `/*/*/WorktreeDefaultContinuationTests/(RetiredWorktreeCutsAtPriorTip*)\|(FrozenTipSurvivesPriorRefMovement*)\|(UnavailablePriorTipRefuses*)\|(LiveFollowUpKeepsCwd*)\|(StandingAndRoutingPinsKeepCwd*)\|(ExplicitWorktreePinRefuses*)\|(ContinuationRetainsCardContextAndPolicy*)` | R-6 | all 7 named methods and argument rows, 0 failed/skipped | 7 | 4 | true |
| CP-12 | S1-S3 | CP-9 | resolution-unit | `/*/*/*/*[Category=Unit]` | R-8, AC-16 | entire Unit lane, >=8 executed, 0 failed; report existing skips | 8 | 5 | true |
| CP-13 | S1-S4 | `tests/Antiphon.Tests -> bin-c458-s4/` | workspace-messages | `/*/*/(SharedWriterLeaseProjectionTests*)\|(DelegateScriptWorkspaceTests*)\|(DelegateScriptWorkspaceDefaultTests*)/*` | AC-13, AC-14, R-7 | all 42 planned/existing results, 0 failed/skipped | 42 | 6 | true |
| CP-14 | S1-S4 | CP-13 | queue-holder-projection | `/*/*/AgentTaskPipelineStatusTests/C458_HolderProjectionPreservesScopeContention*` | AC-17 | all 3 planned argument rows, 0 failed/skipped | 3 | 2 | true |
| CP-15 | S1-S4 | CP-13 | project-script-regression | `/*/*/CommitOnSettleScriptTests/(Project_set_CommitOnSettle_puts_the_value*)\|(Project_set_PUT_carries_the_GET_fields_unchanged*)\|(Commit_on_settle_scripts_are_ascii_only*)` | AC-18, R-7 | 3 methods, 5 argument-expanded results, 0 failed/skipped | 5 | 2 | true |
| CP-16 | S1-S4 | CP-13 | messages-unit | `/*/*/*/*[Category=Unit]` | AC-13, R-8 | entire Unit lane, >=21 executed, 0 failed; report existing skips | 21 | 5 | true |
| CP-17 | S1-S4 | n/a | queue-labels | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/home/tasks/homeTasksModel.test.ts src/features/orchestrator/pipelineStageModel.test.ts` | AC-17 | both files, all existing tests and 12 new results, CLIENT TESTS EXIT CODE 0, 0 failed | n/a | 3 | true |
| CP-18 | S1-S5 | n/a | client-contract-build | `npm --prefix client run build` | AC-12, AC-15, AC-17, R-9 | TypeScript and Vite exit 0, current bundle produced | n/a | 4 | true |
| CP-19 | S1-S5 | n/a | documentation-diff | `git diff --check 7ca550cec42852b9659078420707b6bda0128cc3 HEAD -- AGENTS.md .claude/skills/antiphon-delegate/SKILL.md docs/orchestration-loop.md docs/antiphon-api.md scripts/delegate.ps1 docs/superpowers/plans/2026-09-14-card-0458-worker-worktree-default-plan.md docs/superpowers/plans/2026-09-28-card-0458-worker-worktree-default-test-design.md` | R-10 | exit 0; separate Review records five-surface semantic audit | n/a | 1 | true |
