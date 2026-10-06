# CARD-1076: remote-prep push budget, non-fencing push journal, lease-free re-arm, queue reasons

Date: 2026-10-06. Plan task: `8181ac16-feda-48c3-9c33-a1f3395b2423` (Frontier, Linux runner mirror).
Inspected source: `fb37c36c10f38da4b04de72496ce9c080e993c85` (origin/master at planning time).
Assigned branch: `feat/card-task-8181ac16`; fast-forward-only from `d985af05b`.
Status: Plan complete with the verification design folded in. **next: land**, then Code
runs the `### Checkpoints` table as a closed list.

## Outcome and scope

On 2026-10-05 three server2 dispatches waited about half an hour with free seats because each
remote-prep `git push` was killed at the five-minute `LandingGit` budget, each live push journal
fenced the repository mutation lease for the whole budget, and every re-arm of a killed push had
to cross that lease again. The pipeline showed `awaitingDispatch` with no holder throughout.
After this card:

1. A remote-prep push runs under its own budget, `Delegation:RemotePrepPushBudgetMinutes`
   (default 20), instead of the five-minute budget every other `LandingGit` command keeps.
2. A live remote-prep push is journaled with its task id and purpose and **does not fence** the
   repository mutation lease. A dead, reused, unknown or unreadable record still fences, and the
   recovery-script sentence is reserved for that case.
3. Remote-prep pushes on one repository run one at a time inside the preparer, so the second
   task's push starts when the first upload is complete and reuses its objects, without the
   dispatcher waiting for either.
4. Re-arming remote preparation for a task whose desktop worktree is already cut and whose
   progress baseline is already captured takes no lease, because that crossing runs no git.
5. `GET /api/agent-tasks/pipeline` names two more queue reasons, `repositoryLease` and
   `remotePrep`, with `heldBy` naming the lease owner, the running land, or the task whose push
   is ahead; the client renders both (and the already-emitted `hostBudget`).

Out of scope, each named by its own card: the land's own "owner unknown" hold text and its
missing `DescribeUnavailableAsync` call (CARD-0809); automatic recovery of a dead journal
(CARD-0648); progress-based (idle) kills instead of wall-clock budgets.

## Ground truth

Read against `fb37c36c1` in the runner mirror; routing facts read live on 2026-10-06 ~06:10Z.

| Card assumption | What the code does | Consequence for the plan |
|---|---|---|
| `LandingGit.ExecuteProcessAsync` kills every git child at a hard five-minute budget and throws `git_timeout`. | `server/Infrastructure/Git/LandingGit.cs:61-116`: `budget.CancelAfter(TimeSpan.FromMinutes(5))` at `:65`; on cancellation the child is killed with its tree (`:103-106`) and `TimeoutException("git_timeout")` is thrown at `:114` when the caller's token is not the cause. `RunAsync(repo, args, ct)` (`:29`) has no budget parameter. A second five-minute budget exists for the read-only byte batch (`RunGitInputBytesAsync`, `:671-674`). | Confirmed. D-1 adds a per-call options overload; the default stays five minutes everywhere else and the byte batch is untouched. |
| Every `push` is journaled under `<common>/antiphon/children` for the life of the child. | `LandingGit.cs:76-78`: `push` is in the mutating set, so `RepositoryChildJournal.BeginAsync` writes a record before `Process.Start`; `journal.Exited` deletes it only after both streams drain (`:107-121`). The record (`RepositoryChildJournal.cs:139-140`) carries schema, common directory, pid, start ticks, `Completed`; no task or purpose. | Confirmed. D-3 adds `Purpose`/`TaskId` to the record and a reader-side non-fencing rule for a live `remote-prep-push`. |
| `RepositoryMutationLease.AcquireAsync` treats any journal file as a fence. | `RepositoryMutationLease.cs:37-42`: `HasUnfinishedAsync` true → dispose `landing.lock`, notify the fence observer, return null. `HasUnfinishedAsync` (`RepositoryChildJournal.cs:104-137`) returns true for a live record (`:124-127`) and for a dead one (`:128-131`); `DescribeUnavailableAsync` (`RepositoryMutationLease.cs:77-86`) always answers with the recovery-script sentence. | Confirmed. D-3 makes a live tagged push skip the fence; everything else keeps today's behaviour, including the `IRepositoryFenceObserver` notification (`AlarmWakeQueue.Fenced`, `server/Application/Services/AlarmWakeQueue.cs:28-33`). |
| The prep push runs with no lease from `RemoteWorkspacePreparer` → `RemoteWorkspaceService.PushBranchAsync`. | `RemoteWorkspacePreparer.cs:127` calls `remote.PushBranchAsync`; `RemoteWorkspaceService.cs:98-117` runs `rev-parse HEAD`, `remote get-url origin`, then `git push -u origin <branch>` through `_git.RunAsync` (`:111-112`) with no lease and no budget. A `TimeoutException` lands in the preparer's catch (`:160-169`) as `The runner could not mirror the task branch (git_timeout); the task stays Queued.`, backs off (base 30 s, cap 900 s, `:171-189`) and the next attempt re-pushes from scratch. | Confirmed, including the misleading "could not mirror" wording for a push failure. D-2 gives the push its budget and tag; the failure sentence is left as is (text pins exist) but the preparer phase is exposed (D-4). |
| The dispatcher takes the lease for every queued task whose mirror is not recorded yet, including a re-arm. | `AgentTaskDispatcher.cs:4546-4553`: `needsLease` excludes only `LaunchesPreparedMirror` (`:4389-4396`, requires `RemoteWorktreePath`). On a re-arm (WorktreePath set, mirror null) the claim path skips the worktree cut (`:4775`), skips baseline capture when `ProgressBaselineJson` is set (`:4845-4848`), and `PrepareRemoteWorkspaceAsync` (`:6163-6212`) performs no git or network work before the claim is committed and `TryBegin` is called (`:4952-4960`). The only git in that crossing is the baseline capture (`:6528-6572`), whose pins acquire the lease non-reentrantly (`TaskProgressGit.cs:216-238`). | Confirmed: a re-arm with a captured baseline runs no git. D-5 exempts exactly that case. |
| `AgentTaskPipelineStatusService.ToQueued` never reads the lease fence or remote-prep holds. | `AgentTaskPipelineStatusService.cs:374-465`: reasons are `sharedCheckoutLease` (in-flight Shared holders), `siblingLandInFlight`, `routingPinNotBefore`, `hostBudget`, `concurrencyCap`, else `awaitingDispatch`. The service already takes `RemoteWorkspacePreparer?` for occupancy (`:46-62`, `:162-173`) and loads sibling lands without Git (`:519-544`). The dispatcher's own hold evidence is the deduplicated `Held` event row (`TraceHeldAsync`, `:1201-1214`) and its stint floor is the last `Dispatched` event (`LoadQueuedHoldIndexAsync`, `:1216-1262`); `DispatchHoldDetails.ClassOf` (`DispatchHoldDetails.cs:167-200`) classifies every hold sentence. | Confirmed. D-6 derives the two new reasons from the latest current-stint `Held` row and never contacts Git. |
| The client can show any reason the server emits. | `client/src/api/agentTasks.ts:467-473` lists five reasons and omits `hostBudget`; `pipelineStageModel.ts:198-224` and `homeTasksModel.ts:236-242,273-300` switch on that union with no default. | Adding members forces the switches to grow (TypeScript non-exhaustive return). D-7 adds the two new reasons and the missing `hostBudget` case. |
| A longer budget alone would have turned the incident into one wait. | The three killed pushes were of three branches sharing the same objects; once one finished (4 m 10 s) the next two took about 38 s (card evidence). Concurrent pushes of the same objects over one uplink split the bandwidth and each would have approached the budget again. | D-4 serializes pushes per repository inside the preparer, off the dispatcher tick, so the first upload is paid once. |
| `RepositoryLeasePurposes` is the place for the push's purpose string. | `IRepositoryMutationLease.cs:35-43` documents those strings as "production acquire sites"; the push acquires nothing. | The tag gets its own `RepositoryChildPurposes.RemotePrepPush` next to the new run options (D-1/D-3). |
| CARD-0817 (Review) changes the same push path. | Its plan changes the runner image credential helper and `PhoneHome` admission; `RemoteWorkspaceService.PushBranchAsync` and `LandingGit` are not in its slices (`docs/superpowers/plans/2026-10-05-card-0817-https-token-push-credential-plan.md`). | No source collision expected; Code checks `git log` on the two files before S2. |

Routing facts (`GET /api/runner-defaults`, `GET /api/session-runners`): global default `server2`,
which is draining; `server2-temp` (linux) accepts, capacity 10, occupied 4; `desktop` (windows)
accepts, capacity 2. Every checkpoint row runs on Linux or Windows (real git, `pwsh` sleep
children, the phone-home test host, vitest), so the Code dispatch needs neither `-Runner` nor
`-Platform`; the default lane is the Linux runner mirror.

## Decisions

- **D-1 Per-call run options on `ILandingGit`.** New
  `LandingGitRunOptions(TimeSpan? Budget = null, RepositoryChildTag? Child = null)` and
  `RepositoryChildTag(Guid? TaskId, string Purpose)` in
  `server/Application/Interfaces/ILandingGit.cs`, plus
  `RepositoryChildPurposes.RemotePrepPush = "remote-prep-push"`. `ILandingGit` gains
  `RunAsync(repository, arguments, LandingGitRunOptions options, ct)` as a **default interface
  method** that forwards to the three-argument `RunAsync`, so no fake breaks. `LandingGit`
  implements it: both public overloads call one private core; `budget.CancelAfter(options.Budget
  ?? DefaultBudget)` with `public static readonly TimeSpan DefaultBudget = TimeSpan.FromMinutes(5)`;
  the journal is begun with the tag when the command is mutating. `FixtureGit`'s three-argument
  override stays the tracing seam; a four-argument override that forwards to the same trace is
  optional.
  *Why:* only the prep push needs a different budget today, and the tag travels on the same
  call. *Rejected:* raising the constant for every command (lands and worktree cuts keep their
  bound); a progress/idle kill (stderr is drained with `ReadToEndAsync` and never exposed; a
  streaming parser is a larger change and the card asks for a budget); a separate
  `RunTaggedAsync` plus `RunWithBudgetAsync` pair (two overloads for one call site).
- **D-2 `Delegation:RemotePrepPushBudgetMinutes`, default 20, floor 1.** `DelegationSettings`
  property with a `DelegationSettingsValidator` rule (`Delegation:RemotePrepPushBudgetMinutes
  must be at least 1.`). `RemoteWorkspaceService.PushBranchAsync` passes
  `new LandingGitRunOptions(TimeSpan.FromMinutes(budget), new RepositoryChildTag(task.Id,
  RepositoryChildPurposes.RemotePrepPush))` for the push only; the two reads before it keep the
  default. The preparer's failure sentence and backoff are unchanged.
  *Why:* the successful push in the incident took 4 m 10 s on this uplink; 20 minutes gives a
  larger pack room without leaving a hung ssh forever. *Rejected:* unbounded (a hung push would
  hold the per-repository push gate indefinitely); growing the budget per attempt (the retry
  re-uploads from scratch, so a short first budget is pure waste).
- **D-3 A live tagged remote-prep push does not fence; everything else still does.**
  `RepositoryChildJournal.ChildRecord` gains `string? Purpose = null, Guid? TaskId = null`
  (schema stays 1; existing positional constructions and the PowerShell reader keep working).
  `HasUnfinishedAsync` skips a record that is well-formed, whose process is **alive**
  (`IsProcessAliveAsync == true`) and whose `Purpose` is in
  `RepositoryChildJournal.LiveNonFencingPurposes` (`{ "remote-prep-push" }`, an explicit
  reader-side list); a dead, reused (`false`), unknown (`null`), completed, torn or unreadable
  record fences exactly as today. `DescribeUnavailableAsync` keeps returning null when nothing
  fences, and when a fencing record carries a tag it names it:
  `unfinished repository child journal under <dir> (purpose=remote-prep-push task=<N-guid>); run
  scripts/recover-repository-children.ps1`. `JournalRecordFinding` gains `Purpose` and `TaskId`;
  `scripts/recover-repository-children.ps1` appends ` purpose=<p> task=<id>` to its
  `retained (<state>)` line when the record has them (state classification unchanged, a live
  record stays retained, exit 3).
  *Why:* the push writes only `refs/remotes/origin/<task-branch>` and the branch's upstream
  config, both under git's own ref/config locks with retry; nothing a land or a worktree cut
  reads-then-writes depends on it, which is the same reasoning CARD-0672 D-1 (I-A) used for the
  mirror launch. Keeping the record keeps crash visibility: an orphaned push after a server
  death is still listed and still blocks the recovery script. *Rejected:* not journaling pushes
  at all (loses that listing); running the push under a tagged lease (CARD-0809 option a: the
  whole upload would then serialize lands and every first crossing, which is this incident's
  shape with a name on it); a writer-side `FencesWhileAlive` flag in the record (the reader,
  not the dead writer, decides what may be ignored).
- **D-4 One remote-prep push at a time per repository, inside the preparer.**
  `RemoteWorkspacePreparer` keeps a `ConcurrentDictionary<string, PushGate>` keyed by the task's
  `RepoPath` normalized like the lease (`Path.TrimEndingDirectorySeparator(Path.GetFullPath)`,
  OS path comparer); `PushGate` is a `SemaphoreSlim(1,1)` plus the holder task id. `RunAsync`
  waits for the gate before `PushBranchAsync` and releases it in `finally` before `MirrorAsync`;
  the wait honours the stopping token and is **not** part of the push budget. The in-flight
  entry becomes mutable (`Phase`: `WaitingForPushTurn | Pushing | Mirroring`; `BehindTaskId`)
  and `public RemotePrepProgress? Progress(Guid taskId)` exposes `(Since, Phase, BehindTaskId)`.
  `IsInFlight`, `InFlightCount`, `WhenIdleAsync` and `TryBegin` keep their contracts: a task
  waiting for its turn is in flight and counts toward the runner's outstanding mirrors.
  *Why:* the dispatcher never waits on this gate (the task is already `HeldForRemotePrep` off
  the tick), the second push reuses the first upload's objects, and a hung push blocks only
  pushes on that repository, for at most the D-2 budget. *Rejected:* concurrent pushes (N
  uploads of one object set over one link); the gate in `RemoteWorkspaceService` (no task
  identity for `heldBy`); keying by normalized origin URL (needs a git call before gating; all
  worktrees of one checkout already share one origin); persisting the phase (restart re-arms
  from the row, as CARD-0633 designed).
- **D-5 A re-arm with a cut worktree and a captured baseline takes no lease.** New
  `AgentTaskDispatcher.RearmsPreparedWorktree(task)`: `RunnerId` set, `Workspace == Worktree`,
  `WorktreePath` set, `RemoteWorktreePath` **null**, `ProgressBaselineJson` non-empty,
  `RepairSourceTaskId` null, `SourceLandingOperationId` null, `VerificationRound != Interim`;
  `needsLease` becomes `... && !LaunchesPreparedMirror(task) && !RearmsPreparedWorktree(task)`.
  `LaunchesPreparedMirror` and its C672 tests are untouched.
  *Why:* the claim path for that row runs no git (ground truth row 5); taking the lease there
  only queues the re-arm behind a land and makes the land yield to it. *Rejected:* dropping the
  baseline guard (a re-arm without a baseline captures one, which pins under the lease);
  widening `LaunchesPreparedMirror` (its name and test matrix are about the mirror launch).
- **D-6 Pipeline reasons `repositoryLease` and `remotePrep` from the dispatcher's own hold
  evidence.** `AgentTaskPipelineStatusService` loads, for the queued ids, the latest `Held` event
  after the task's last `Dispatched` event (the dispatcher's floor rule) in one query, and the
  running pending land requests joined to their holders in one query. Precedence in `ToQueued`:
  `sharedCheckoutLease` → `siblingLandInFlight` → `routingPinNotBefore` → **`repositoryLease`**
  (`ClassOf(detail) == Lease`) → **`remotePrep`** (`ClassOf(detail) == RemotePrep`) →
  `hostBudget`/`concurrencyCap` → `awaitingDispatch`. `heldBy` for `repositoryLease`: the full
  task id parsed from a `LeaseHeldByOwner` sentence (new
  `DispatchHoldDetails.LeaseOwnerTaskId(string reason) : Guid?`, anchored on
  `occupied by task <32 hex> purpose=`), else the holder of a running land whose
  `ScopeResolver.KeyFor(RepoPath, WorkingDirectory)` equals the task's, else empty. `heldBy` for
  `remotePrep`: `_remotePrep?.Progress(task.Id)?.BehindTaskId`, else empty. Holder titles come
  from one `AgentTasks` lookup over the collected ids. `docs/antiphon-api.md` lists the two
  reasons. No DTO shape change.
  *Why:* the projection must not contact Git (`FindOwnerAsync`/`DescribeUnavailableAsync` resolve
  the common directory with a git process per call), and the `Held` row is what the dispatcher
  actually decided on its last tick, deduplicated and already classified. *Rejected:* an
  in-memory waiter lookup only (`RepositoryLeaseWaiters` is keyed by common directory and names
  no owner); a generic `dispatcherHeld` reason (the card asks for these two classes; other hold
  classes stay visible through the task's `Held` detail and the attention ledger); new DTO fields.
- **D-7 Client.** `AgentTaskPipelineQueueReason` gains `'repositoryLease' | 'remotePrep' |
  'hostBudget'`. `QUEUE_REASON_LABEL`: `repositoryLease: 'waiting: repository lease held by'`,
  `remotePrep: 'waiting: remote workspace preparation'`, `hostBudget: 'waiting: runner host at
  its budget'`. `queueReasonLine`: `waiting: repository lease held by task-<short> — <title>` or
  `... held by another process`; `waiting: remote workspace preparation (behind task-<short> —
  <title>)` or `... (branch push and mirror)`; `waiting: runner host at its budget`.
  `compactQueueReason`: `lease ~<short>` / `lease`, `prep ~<short>` / `prep`, `host full`.
  *Why:* the union's switches are non-exhaustive today and `hostBudget` already reaches the
  browser as `undefined`; the two new members make the same edit mandatory, so the third is
  one line in the same places, labelled as such in the slice. *Rejected:* a default branch that
  prints the raw reason (hides the next gap).
- **D-8 Held texts and failure sentences unchanged.** `RemoteMirrorRequested`,
  `RemotePrepBackoff`, the lease sentences and the preparer's warning keep their exact text
  (dedupe and ledger pins depend on them). The push phase is visible through `Progress` and the
  pipeline `heldBy`, not through a new `Held` row per phase.
- **D-9 Defaults a human may change later (not blocking).** The 20-minute budget (D-2) and the
  per-repository gate key (D-4). Both are stated here so Code does not ask.

## Slices (each a 30-60 minute Code task; commit and push after each)

| Slice | Files | Behaviour and tests |
|---|---|---|
| **S1 Run options and the non-fencing tagged journal** | `server/Application/Interfaces/ILandingGit.cs`, `server/Infrastructure/Git/LandingGit.cs`, `server/Infrastructure/Git/RepositoryChildJournal.cs`, `server/Infrastructure/Git/RepositoryMutationLease.cs`, `server/Infrastructure/Git/RepositoryChildJournalInspector.cs`, `scripts/recover-repository-children.ps1`, `tests/Antiphon.Tests/Infrastructure/LandingGitTests.cs`, `tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseTests.cs`, `tests/Antiphon.Tests/Infrastructure/RepositoryChildJournalInspectorTests.cs` | D-1, D-3. New `C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal`: `ScratchGitRepo` with `AddBareOriginAsync`, a `pre-push` hook that sleeps 30 s (the `a_git_timeout_fails_one_task_not_the_tick` hook pattern, executable bit on Unix), `RunAsync(..., ["push","origin","master"], new LandingGitRunOptions(Budget: 2 s), ct)` throws `TimeoutException("git_timeout")` within 15 s, `<common>/antiphon/children` is empty afterwards, and the same push with the three-argument `RunAsync` and no hook succeeds; asserts `LandingGit.DefaultBudget == 5 min`. New `C1076_TaggedPushJournalNamesTaskAndPurposeWhileAlive`: a hook that sleeps 8 s, options with a tag, poll the children directory until the `.json` appears and deserialize it (`Purpose == "remote-prep-push"`, `TaskId == tag`), file gone after exit. New `C1076_LiveTaggedPrepPushChildDoesNotFenceButDeadReusedOrUnknownStillDoes(string state)` with `alive`, `exited`, `reused`, `unknown` (the `C448_C24_StandingJournalFencesAdmissionByStartIdentity` pwsh-sleep pattern, journal begun with the tag through a new internal `BeginAsync(repository, tag, ct)`): `alive` → `TryAcquireAsync` admits and `DescribeUnavailableAsync` is null while the record exists; the other three → refused, and `DescribeUnavailableAsync` contains `recover-repository-children.ps1`, `purpose=remote-prep-push` and the task id. New inspector test `C1076_a_tagged_record_reports_its_purpose_and_task` (tagged → both set; untagged → both null). |
| **S2 Prep push budget, tag and per-repository gate** | `server/Application/Settings/DelegationSettings.cs` (property, validator rule), `server/Application/Services/RemoteWorkspaceService.cs` (`PushBranchAsync`), `server/Application/Services/RemoteWorkspacePreparer.cs` (gate, mutable in-flight entry, `Progress`), `tests/Antiphon.Tests/Application/RemoteWorkspacePreparerTests.cs` (`PushOnlyGit` implements the options overload and records the options; its three-argument `push` branch now throws `NotSupportedException("push must carry run options")`), `tests/Antiphon.Tests/Application/DelegationLeaseSettingsTests.cs` | D-2, D-4. New `C1076_prep_push_carries_the_task_tag_and_the_configured_budget`: rig tweak `RemotePrepPushBudgetMinutes = 7`; after the mirror is recorded the recorded options are `Budget == 7 min`, `Child.Purpose == remote-prep-push`, `Child.TaskId == task`. New `C1076_pushes_on_one_repository_run_one_at_a_time_and_the_waiter_names_the_pusher`: capacity 3, two tasks on the rig's `RepoPath` with their own worktrees and branches (as `C672_three_queued_runner_tasks...` seeds them), `PushOnlyGit` holds the first push on a `TaskCompletionSource` and counts concurrent pushes; after one tick both rows are `HeldForRemotePrep`, `Preparer.Progress(second)` is `WaitingForPushTurn` with `BehindTaskId == first`, the concurrent-push maximum is 1; release → two mirror requests, emit both results, both rows record their mirror, no `Warning` events. New settings test `C1076_RemotePrepPushBudgetMinutes_defaults_to_20_and_rejects_below_1` (`[Arguments(0,false)]`, `[Arguments(1,true)]`, `[Arguments(20,true)]`; the default is 20). |
| **S3 Lease-free re-arm** | `server/Application/Services/AgentTaskDispatcher.cs` (predicate near `:4389`, `needsLease` at `:4546`), `tests/Antiphon.Tests/Application/AgentTaskDispatcherPredicateTests.cs`, `tests/Antiphon.Tests/Application/RemoteWorkspacePreparerTests.cs` | D-5. New `C1076_RearmsPreparedWorktree(string arm, bool expected)` with `rearm` true and `mirror-recorded`, `no-baseline`, `local`, `no-worktree`, `repair-source`, `snapshot`, `interim`, `shared` false. `C672_prepared_task_launches_while_a_land_holds_the_lease` gains arms `rearm` (`RemoteWorktreePath = null`, `ProgressBaselineJson = "{\"schemaVersion\":1}"`): `TickResult.HeldOnLease == 0`, status Queued, last `Held` starts with `RemoteMirrorRequestedPrefix`, no lease sentence, `Waiters.IsEmpty`, one `WorkspaceMirror` request; and `rearm-no-baseline` (`ProgressBaselineJson = null`): refused with `LeaseHeldByOwner`, zero mirror requests. Sequenced after S2 because both edit the same test file. |
| **S4 Pipeline reasons** | `server/Application/Services/AgentTaskPipelineStatusService.cs`, `server/Application/Services/DispatchHoldDetails.cs` (`LeaseOwnerTaskId`), `docs/antiphon-api.md:460`, `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs`, `tests/Antiphon.Tests/Application/DispatchHoldLedgerTests.cs` | D-6. New `C1076_a_dispatcher_lease_hold_is_the_queued_reason_and_names_the_owner(string arm)`: `owner-text` (Held = `LeaseHeldByOwner(owner.Id, "land", t)` → `repositoryLease`, `heldBy == [owner]`), `fenced-text` (`LeaseFenced(...)` → `repositoryLease`, empty `heldBy`), `running-land` (`LeaseHeldByLand(...)` plus a pending Running `AgentTaskLandRequest` on a holder with the same repo key → `heldBy == [holder]`), `previous-stint` (lease Held row older than a later `Dispatched` event → `awaitingDispatch`). New `C1076_remote_preparation_in_flight_or_backing_off_is_the_queued_reason(string arm)`: `in-flight` (`RemoteMirrorRequested`) and `backoff` (`RemotePrepBackoff`) → `remotePrep`; `pin-outranks` (same row plus a dated card pin → `routingPinNotBefore`). New `C1076_a_task_waiting_for_its_push_turn_names_the_pushing_task`: a real `RemoteWorkspacePreparer` on a minimal provider (`AppDbContext`, `SingleRunnerDirectory`-style stub, an `ILandingGit` whose push blocks on a `TaskCompletionSource`, `RemoteWorkspaceService`), `TryBegin(a)` then `TryBegin(b)`, service built with that preparer, b's Held = `RemoteMirrorRequested` → `remotePrep`, `heldBy == [a]`; release and `WhenIdleAsync` in teardown. New `C1076_LeaseOwnerTaskId_parses_only_the_owner_sentence`: the guid for `LeaseHeldByOwner`; null for `LeaseHeldByLand`, `LeaseOccupiedUnknown`, `LeaseUnknownAdmissionWriter`, and for `LeaseFenced("journal of task " + Guid.NewGuid():N)`. |
| **S5 Client and docs** | `client/src/api/agentTasks.ts`, `client/src/features/home/tasks/homeTasksModel.ts`, `client/src/features/orchestrator/pipelineStageModel.ts`, `client/src/features/orchestrator/pipelineStageModel.test.ts`, `client/src/features/home/tasks/homeTasksModel.test.ts`, `client/src/features/home/tasks/TaskCard.test.tsx`, `docs/ops-http.md:135`, `docs/orchestration-loop.md:906-908`, `docs/antiphon-api.md` (if S4 left anything), `tests/Antiphon.Tests/Application/RunnerBranchContractDocumentationTests.cs` | D-7, docs for D-2..D-6. Vitest: `compactQueueReason` cases for `repositoryLease` with and without a holder, `remotePrep` with and without, `hostBudget`; `TaskCard` table rows for the three lines; `homeTasksModel` label test. Docs: the push budget key and default, the tagged non-fencing journal and the recovery sentence reservation, one push at a time per repository, the lease-free re-arm, the two queue reasons. New `C1076_remote_prep_push_contract_is_documented` pins `RemotePrepPushBudgetMinutes`, `remote-prep-push`, `repositoryLease` and `remotePrep` in `docs/orchestration-loop.md` and `docs/ops-http.md`. |

S1 is verified alone (CP-1, CP-2). S2 → S3 → S4 → S5 are sequenced (S2/S3 share a test file;
S4 uses S2's `Progress`; S5 documents all) and verified at the final SHA (CP-3..CP-7). A Code
task may take S2+S3 or S4+S5 as one dispatch when its budget allows; the checkpoint groups do
not change.

## Verification design

Coverage-to-class. Every TUnit row runs in `tests/Antiphon.Tests`. `LandingGitTests`,
`RepositoryMutationLeaseTests`, `RepositoryChildJournalInspectorTests`,
`RepositoryMutationLeaseDescribeTests` and `RepositoryFenceObserverTests` are `Integration`
under `ParallelLimiter<ProcessSpawnLimit>` (real git, `pwsh` sleep children); the preparer,
starvation, projection and pipeline-status classes are `Integration` on isolated PostgreSQL
schemas and the phone-home test host; predicate, ledger, settings and docs-pin classes are
`Unit`. No class is `Slow` and none is added to `tests/Antiphon.Tests/slow-tests-allowlist.txt`.

| ID | Class.Method | Behaviour proven |
|---|---|---|
| V-1 | `LandingGitTests.C1076_RunOptionsBudgetKillsASlowPushAndClearsItsJournal` | D-1: a caller budget of 2 s kills a push stalled in its `pre-push` hook, throws `git_timeout`, leaves no journal; the three-argument overload and `DefaultBudget` (5 min) are unchanged. |
| V-2 | `LandingGitTests.C1076_TaggedPushJournalNamesTaskAndPurposeWhileAlive` | D-3: the live record carries `Purpose`/`TaskId`; it is deleted on exit. |
| V-3 | `RepositoryMutationLeaseTests.C1076_LiveTaggedPrepPushChildDoesNotFenceButDeadReusedOrUnknownStillDoes` (4 results) | D-3: alive tagged → admitted, describe null; exited/reused/unknown → refused, describe names the recovery script, purpose and task. |
| V-4 | `RepositoryChildJournalInspectorTests.C1076_a_tagged_record_reports_its_purpose_and_task` | D-3: inspector findings expose the tag; untagged records read null. |
| V-5 | `RemoteWorkspacePreparerTests.C1076_prep_push_carries_the_task_tag_and_the_configured_budget` | D-2: the production push uses the options overload with the configured minutes and the task tag. |
| V-6 | `RemoteWorkspacePreparerTests.C1076_pushes_on_one_repository_run_one_at_a_time_and_the_waiter_names_the_pusher` | D-4: one push at a time per repository, the waiter's `Progress` names the pusher, both mirrors complete, no warnings. |
| V-7 | `RemoteWorkspacePreparerTests.C672_prepared_task_launches_while_a_land_holds_the_lease` (new arms `rearm`, `rearm-no-baseline`) | D-5: a captured-baseline re-arm never asks for the lease and arms the mirror; a re-arm without a baseline still waits for the lease. |
| V-8 | `AgentTaskDispatcherPredicateTests.C1076_RearmsPreparedWorktree` (9 results) | D-5: the predicate's exact matrix. |
| V-9 | `AgentTaskPipelineStatusTests.C1076_a_dispatcher_lease_hold_is_the_queued_reason_and_names_the_owner` (4 results) | D-6: owner-text, fenced-text, running-land holder, previous-stint floor. |
| V-10 | `AgentTaskPipelineStatusTests.C1076_remote_preparation_in_flight_or_backing_off_is_the_queued_reason` (3 results) | D-6: in-flight and backoff holds, pin precedence. |
| V-11 | `AgentTaskPipelineStatusTests.C1076_a_task_waiting_for_its_push_turn_names_the_pushing_task` | D-4/D-6: `heldBy` from the preparer's `Progress`. |
| V-12 | `DispatchHoldLedgerTests.C1076_LeaseOwnerTaskId_parses_only_the_owner_sentence` | D-6: the parser is anchored on the owner sentence. |
| V-13 | `DelegationLeaseSettingsTests.C1076_RemotePrepPushBudgetMinutes_defaults_to_20_and_rejects_below_1` (3 results) | D-2: default and validator floor. |
| V-14 | vitest `pipelineStageModel.test.ts`, `homeTasksModel.test.ts`, `TaskCard.test.tsx` (new cases) | D-7: compact and long forms for `repositoryLease`, `remotePrep`, `hostBudget`. |
| V-15 | `RunnerBranchContractDocumentationTests.C1076_remote_prep_push_contract_is_documented` | Docs name the key, the purpose and the two reasons. |
| R-1 | `RemoteWorkspacePreparerTests.*` (19 existing results) | CARD-0633/0672 preparer and lease-crossing behaviour unchanged, including `C672_three_queued_runner_tasks_cross_the_lease_once_and_launch_behind_the_next_land`. |
| R-2 | `RepositoryMutationLeaseTests.*` (18 existing) | `C448_C24_StandingJournalFencesAdmissionByStartIdentity` (an untagged live record still fences), `C448_C24_UnreadableJournalStateCannotAdmitAWriter`, `C661_*`, `C666_*`. |
| R-3 | `LandingGitTests.*` (55 existing) | Push destinations, stderr suppression, scope caching unchanged by the overload split. |
| R-4 | `AgentTaskPipelineStatusTests.*` (62 existing across its three partial files) | Existing reasons and precedence; `queued_work_is_awaiting_dispatch_unless_a_live_lease_holds_it` (a stale free-text Held stays `awaitingDispatch`). |
| R-5 | `AgentTaskDispatcherPredicateTests.C672_LaunchesPreparedMirror` (8) | The mirror-launch predicate is untouched. |
| R-6 | `DispatchHoldLedgerTests.*` (22 existing results) | `ClassOf` mapping and ledger sums unchanged. |
| R-7 | `DispatcherRemotePrepStarvationTests.*` (4) | Silent remote tasks still do not delay a local recipient with the push gate in place. |
| R-8 | `RepositoryChildJournalInspectorTests.*` (6), `RepositoryMutationLeaseDescribeTests.*` (1), `RepositoryFenceObserverTests.*` (1) | Inspector states, describe-is-journal-only, one fence notification per fenced acquire/describe. |
| R-9 | `DispatchHoldVisibilityTests.lease_hold_traces_once_per_holder_and_names_the_running_land`, `.lease_fence_is_named_from_the_provider`, `.unknown_lease_holder_is_stable_text`, `.C672_lease_hold_registers_a_waiter_and_dispatch_clears_it`, `.C672_held_aged_carries_the_per_class_wait_ledger` | Lease hold texts, waiter registration and the ledger for local tasks unchanged by D-5. |
| R-10 | `PhoneHomeTaskDispatchProjectionTests.*` (8) | The preparer graph still composes and projects. |
| R-11 | `DelegationLeaseSettingsTests.*` (8 existing) | Existing validator rules. |
| R-12 | `RunnerBranchContractDocumentationTests.*` (3 existing) | Existing doc pins. |

### Positive controls (SourceLanding Mutation, method-scoped, one per behaviour)

| PC | Mutation (restore after) | Red witness |
|---|---|---|
| PC-1 | `HasUnfinishedAsync`: ignore `Purpose` (treat an alive tagged record as a fence). | V-3 `alive` arm (admitted → refused). |
| PC-2 | `HasUnfinishedAsync`: skip any tagged record regardless of liveness. | V-3 `exited` and `reused` arms (refused → admitted). |
| PC-3 | `ExecuteProcessAsync`: always `DefaultBudget`, ignore `options.Budget`. | V-1 (no `git_timeout` inside the bound). |
| PC-4 | `PushBranchAsync`: call the three-argument `RunAsync` for the push. | V-5 (the fake throws) and R-1. |
| PC-5 | Preparer: remove the gate `WaitAsync` (pushes run concurrently). | V-6 (concurrent maximum 2; no `BehindTaskId`). |
| PC-6 | `needsLease`: drop `!RearmsPreparedWorktree(task)`. | V-7 `rearm` arm (`HeldOnLease == 1`). |
| PC-7 | Predicate: drop the `ProgressBaselineJson` guard. | V-8 `no-baseline` arm and V-7 `rearm-no-baseline` arm. |
| PC-8 | Projection: ignore the `Dispatched` floor when picking the latest Held row. | V-9 `previous-stint` arm. |
| PC-9 | Projection: map `DispatchHoldClass.RemotePrep` to `awaitingDispatch`. | V-10 `in-flight` and `backoff` arms. |
| PC-10 | `LeaseOwnerTaskId`: return the first 32-hex run in any sentence. | V-12 (`LeaseFenced("journal of task <guid>")` yields a guid). |
| PC-11 | Validator: accept `RemotePrepPushBudgetMinutes == 0`. | V-13 `[Arguments(0,false)]`. |
| PC-12 | `compactQueueReason`: return `'queued'` for `repositoryLease`. | V-14 (`lease ~<short>` expected). |

Mutation runs after land under the SourceLanding rules (external evidence, no commits).

### Cost

Ordinary Code floor is the `EstimatedMinutes` sum, 56 minutes of checkpoint time, plus
authoring: S1 roughly 60-90 minutes, S2+S3 roughly 90-120 minutes, S4+S5 roughly 90-120
minutes. One checkpoint run per committed group (`--rows CP-1,CP-2` after S1;
`--rows CP-3,CP-4,CP-5,CP-6,CP-7` after S5), each through `scripts/build-slot.ps1`, each with
the exact committed SHA:

```powershell
$planPath = 'docs/superpowers/plans/2026-10-06-card-1076-remote-prep-push-fence-plan.md'
$candidateSha = git rev-parse HEAD
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1076-cp1-2 -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1076-tool/ -- run --plan $planPath --rows CP-1,CP-2 --expected-source-sha $candidateSha --row-timeout 15m --total-timeout 40m
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1076-cp3-7 -- dotnet run --project tools/Antiphon.Checkpoints --property:OutputPath=bin-c1076-tool/ -- run --plan $planPath --rows CP-3,CP-4,CP-5,CP-6,CP-7 --expected-source-sha $candidateSha --row-timeout 20m --total-timeout 70m
```

Delete every `bin-c1076-*` directory before finishing. The tool bootstrap build is the one
explained unlisted build. Repeat-proof budget: at most 3 normal repetitions per unchanged
selection; none after green. No assertion, budget or timeout is loosened to pass; a red row is
fixed and rerun as the same row. CARD-0817 is in Review on the same push path: Code reads
`git log origin/master -- server/Application/Services/RemoteWorkspaceService.cs` before S2 and
rebases nothing (the branch is fast-forward-only).

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1076-a/` | lease-journal | `/*/*/(RepositoryMutationLeaseTests*)\|(RepositoryMutationLeaseDescribeTests*)\|(RepositoryChildJournalInspectorTests*)\|(RepositoryFenceObserverTests*)/*` | V-3, V-4, R-2, R-8 | 29 linux / 31 windows executed (18 lease + 4 V-3, 1 describe, 6 + 1 V-4, 1 fence). Linux skips the two pre-existing Windows-only lease tests; 0 failed | 29 linux / 31 windows | 12 | true |
| CP-2 | S1 | CP-1 | landing-git | `/*/*/LandingGitTests/*` | V-1, V-2, R-3 | exact 57 results (55 existing + 2 C1076_*), 0 failed/skipped | 57 | 8 | true |
| CP-3 | all | `tests/Antiphon.Tests -> bin-c1076-b/` | remote-prep | `/*/*/(RemoteWorkspacePreparerTests*)\|(DispatcherRemotePrepStarvationTests*)\|(PhoneHomeTaskDispatchProjectionTests*)/*` | V-5, V-6, V-7, R-1, R-7, R-10 | exact 35 results (19 existing + 2 C1076_* + 2 new C672 arms, 4, 8), 0 failed/skipped | 35 | 15 | true |
| CP-4 | all | CP-3 | predicates-settings-docs | `/*/*/(AgentTaskDispatcherPredicateTests*)\|(DispatchHoldLedgerTests*)\|(DelegationLeaseSettingsTests*)\|(RunnerBranchContractDocumentationTests*)/*` | V-8, V-12, V-13, V-15, R-5, R-6, R-11, R-12 | exact 55 results (8 + 9, 22 + 1, 8 + 3, 3 + 1), 0 failed/skipped | 55 | 5 | true |
| CP-5 | all | CP-3 | pipeline-reasons | `/*/*/AgentTaskPipelineStatusTests/*` | V-9, V-10, V-11, R-4 | exact 70 results (62 existing across the partial class + 4 + 3 + 1), 0 failed/skipped | 70 | 7 | true |
| CP-6 | all | CP-3 | hold-visibility-lease | `/*/*/DispatchHoldVisibilityTests/(lease_hold_traces_once_per_holder_and_names_the_running_land*)\|(lease_fence_is_named_from_the_provider*)\|(unknown_lease_holder_is_stable_text*)\|(C672_lease_hold_registers_a_waiter_and_dispatch_clears_it*)\|(C672_held_aged_carries_the_per_class_wait_ledger*)` | R-9 | exact 5 methods, 0 failed/skipped | 5 | 4 | true |
| CP-7 | all | n/a | client-vitest | `pwsh -File scripts/test-client.ps1 pipelineStageModel homeTasksModel TaskCard` | V-14 | the three files run with the new cases, 0 failed | n/a | 5 | |

## Risks and notes for Code and Review

- **Overload routing.** `FixtureGit`, `ControlledLandingGit` and every `ILandingGit` fake
  override or implement the three-argument `RunAsync`; the default interface overload forwards
  to it, so they keep working unchanged. `PushOnlyGit` is the one fake that deliberately
  refuses a three-argument push (V-5's red witness); do not copy that into other fakes.
- **Record compatibility.** `ChildRecord` keeps schema 1 with two trailing optional fields; the
  tests that construct it positionally (`RunnerAlarm*`, `RepositoryFenceObserverTests`,
  `ExpectationObservationAdapterTests`, inspector tests) and the PowerShell reader
  (`Get-ChildState`) are unaffected. Do not bump the schema.
- **Timing in V-1/V-2.** Bound the wait on the budget kill at 15 s and the journal poll at the
  hook's sleep; keep the hook `#!/bin/sh` form the existing CARD-0220 test uses (Git for
  Windows ships `sh`). A zero-test or fixture-error run is not evidence for either.
- **Gate accounting.** A task waiting for its push turn is still "in flight" for
  `InFlightCount`, so runner occupancy and the `HeldAged` escalation (warning at 300 s) see it;
  that is intended and the hold text is honest. `WhenIdleAsync` in tests must release the
  blocking push first or it waits forever.
- **Projection cost.** D-6 adds one `AgentTaskEvents` query over the queued ids (Held and
  Dispatched types only) and one small running-land query per `GetAsync`; no per-row queries
  and no Git. Keep the `Dispatched` floor identical to `LoadQueuedHoldIndexAsync`.
- **CARD-0809 remains.** The land path still records "owner unknown" for whatever fences it
  without calling `DescribeUnavailableAsync`; this card removes the live push as a cause but
  does not touch `HoldOnBusyLeaseAsync`. CARD-0648 (dead journal, no auto-recovery) also
  remains; a dead tagged push record fences until the recovery script runs, by design here.
- **CP-1 on Linux.** `C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput` and `C448_V13_WindowsJunctionAndOtherProcessShareTheLease` throw `SkipTestException` off Windows, so the row's executed floor is 29 on Linux and 31 on Windows. The skips are pre-existing; S1 does not remove them.
- **Operator note for the docs slice.** When the pipeline shows `remotePrep` with a holder for
  many minutes, the first task's push is the upload to watch; do not run
  `recover-repository-children.ps1` against it (its record is live and retained) and do not
  restart the server (the push restarts from scratch).
