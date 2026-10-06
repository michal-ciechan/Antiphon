# CARD-1082: settle a complete report on its verdict with desktop sync debt, not Blocked

Date: 2026-10-06. Plan task: `23011504-ec5a-47ed-b95c-64b1cfde7cea` (Frontier, Linux runner mirror).
Inspected source: `ed0f0d920` (origin/master at planning time; the task branch was fast-forwarded to it).
Card: Antiphon CARD-1082, `87dde2fe-3fb0-447a-abb6-080a19e72377`. Assigned branch: `feat/card-task-23011504`.
Status: Plan complete with the verification design folded in. **next: land**, then Code runs the
`### Checkpoints` table as a closed list, slice by slice.

## Outcome and scope

Today a runner-bound task whose report says `done` is settled **Blocked** with `next=decide` and
the text "repair the desktop checkout ... then reply" whenever the settlement sync cannot take the
desktop repository lease inside `Delegation:RunnerSyncBudgetSeconds` (`runner_sync_lease_busy`).
The report is complete, the branch is pushed, nothing is wrong with the work; the only failure is
that the desktop worktree could not be fast-forwarded. The card counted 63 such blocks since
2026-09-24, 25 of them never replied to (150 seat-hours), and every reply costs a fresh model turn
that produces the same report.

After this card:

1. A report whose only sync failure is desktop lease contention settles on **its own verdict**
   (Succeeded, or Failed on a Code no-progress read) with `progressEvidence.remoteSync.state =
   Pending`, the server-observed pushed SHA, and the lease reason. The caller's header keeps the
   report's own `next=`; the warning line says the desktop will be synced later and that no reply
   is owed. The seat is released through the ordinary Succeeded/Failed path.
2. A durable **sync debt** row records the exact source to fast-forward to. A bounded sweep in the
   dispatcher tick (modelled on CARD-1065 S6) retries the fast-forward with persisted backoff and
   ends Ready, Held (with a `runner_sync_*` reason) or Superseded (worktree already retired). It
   never promotes a verdict, mints approval or rewrites settlement evidence.
3. Every existing safety property is kept: Review evidence binds through exactly the paths it binds
   through today (first settlement unchanged; strict repair and recovery refuse an unconfirmed
   witness, and Pending is unconfirmed); land approval still requires verified review evidence and
   resolves the source from origin's exact ref; a Pending settlement never mutates the desktop
   checkout; CARD-1065 parking is unaffected because the task is not Blocked.
4. **Blocked stays** for every refusal and every uncertainty that is not lease contention: dirty or
   sequencing desktop checkout, identity/branch/endpoint mismatch, diverged/rewound/local-ahead
   tips, mirror diverged/publish failed/unavailable, timeout, fetch/merge/postcondition failures,
   missing wiring. A Code task whose fresh objects were never fetched during the budget also stays
   Blocked (its progress read is indeterminate); see D-4 and "Residual gap".

Out of scope: lease contention itself (CARD-0743), lease-free fetching (CARD-0499 rule), parking
activation (CARD-1065 D-9), a runner-attested ancestry wire for Code (follow-up, see D-4).

## Ground truth

| Card/brief assumption | Observed code (file:line at `ed0f0d920`) | Design consequence |
|---|---|---|
| `runner_sync_lease_busy` turns a finished report into Blocked. | Confirmed. `AgentTaskReplyService.SettleAsync` classifies, then `RemoteSyncBlockReason` overrides Succeeded to Blocked (`server/Application/Services/AgentTaskReplyService.cs:864-866`), sets `NextStage=Decide` and "repair ... then reply" (`:907-912`), and writes the Warning "repair it, then reply" (`:941-957`). The reason is minted in `RemoteWorkspaceService.SyncOwnedCheckoutAsync` at three lease points: observation fetch (`server/Application/Services/RemoteWorkspaceService.cs:377-378`), after mirror publish (`:347-348`), lease acquire for the fast-forward (`:403`); `LeaseReason` (`:531-532`); budget `DelegationSettings.RunnerSyncBudgetSeconds` default 120 (`server/Application/Settings/DelegationSettings.cs:639`); the slice/cumulative wait is `WhileLeaseBusyAsync` (`:490-527`) with `RunnerSyncLeaseWaits`. | Change the settlement verdict, not the sync budget or the wait (CARD-1043 D-1, CARD-1065 D-6 already rejected retry-in-settlement). |
| "record `progressEvidence.remoteSync = Pending`". | There is no Pending state: `RemoteSettlementSyncState` is NotApplicable/Synchronized/NoPushedProgress/Refused/Unavailable (`server/Application/Dtos/RemoteSettlementSyncDtos.cs:8-24`). `RemoteSyncEvidence.From` records `ConfirmedSha` only when `Confirmed` (`:85-88`). The evidence JSON is written once per settlement (`TaskCompletionProgressService.cs:160-166`, `AgentTaskReplyService.cs:3445-3450`) and is documented as never reused as fresh evidence (CARD-0657 D-4). | Add `Pending = 5`; keep `Confirmed` false for it; the settlement JSON records Pending immutably, the mutable state lives in the debt row (D-2, D-5). |
| "with the observed/pushed SHA". | At the acquire lease point the result carries `RemoteSha = s` (`RemoteWorkspaceService.cs:403`). At the observation lease point it carries only the baseline (`:377-378`) because `TaskProgressGit.ObserveExactRefCoreAsync` returns `repository_lease_busy` without the SHA (`server/Infrastructure/Git/TaskProgressGit.cs:127-137`) even though `ls-remote` already advertised it (`:112-124`). | Carry the advertised SHA on lease-busy observations and outcomes (D-3). |
| "release the seat through the ordinary Succeeded path". | Release fires for Succeeded/Failed, and for Blocked only through CARD-1065 parking (`AgentTaskReplyService.cs:1131-1135`). The completion obligation predicate splits on `remoteBlock` (`:1106-1112`; `TaskCompletionNotification.Applies` `:34-36` vs `AppliesToRunnerSyncBlock` `:44-45`). | Settling Succeeded/Failed gives the ordinary release and obligation for free; no release code changes. |
| "the reconcile job (or the next land) retries the desktop fast-forward". | CARD-1065 S6's sweep exists but is park-keyed: it selects Pending **parks** (`BlockedTaskSyncRecoveryService.cs:25-27`), requires the park's publication receipt (`:63-78`) and saves only while `task.Status == Blocked` (`:95-97`); `RemoteWorkspaceService.SyncParkedAsync` checks park-episode fields (`:172-184`). | Not reusable for a Succeeded task. Add a sibling debt entity and sweep with the same claim/backoff/save discipline (D-5). |
| "`-Land` already verifies against the pushed branch, so a pending sync must not refuse it". | Confirmed. `LandApproval` requires a full `expectedSourceSha` (`server/Application/Services/LandApproval.cs:58-65`); `AgentTaskLandingProtocol` requires `Resolved`/`RemoteSourceSha`/fingerprint (`:130-158`) and runs `ls-remote` before each mutation (`:650`); neither `AgentTaskLandService.cs`, `AgentTaskLandingProtocol.cs` nor `LandApproval.cs` reads `RemoteSync` or `CompletionProgressEvidenceJson` (grep). | No land change. Prove it with a real-Git land of a Pending owner (V-29) and keep it as a canary (G-12). |
| "the completion note tells the caller 'synced later'; no Reply is owed". | The Decide override in the header applies only to a **Blocked** runner task (`SettledHandoff`, `AgentTaskReplyService.cs:2559-2571`); header bits are built in `DelegationReportFormatter.BuildNote` (`:690-724`). | A Succeeded settlement keeps the report's `next=`; add the pending warning and a `source <S> (desktop-sync=pending)` workspace note (D-7). |
| "keep Blocked only when the runner cannot prove the branch is pushed (dirty mirror, push refused)". | Refusals and uncertainties are typed in `RemoteSettlementSyncReasons` (`RemoteSettlementSyncDtos.cs:59-110`): mirror diverged/publish failed (`RemoteWorkspaceService.cs:329-337`), desktop dirty (`:598`), sequencer (`:591`), diverged/rewound/local-ahead (`:434-452`), identity/branch/endpoint (`:549-580`). | All of these stay Blocked; only `LeaseBusy` with a server-observed tip becomes Pending (D-1). |
| Close as duplicate if CARD-1043 chose "retry the sync inside settlement". | CARD-1043 D-1 "Repair the outcome writer, not the lease budget" and rejected retry-only; its S1-S3 landed (`b79ed3c56`, `53ad18ec7`, `406540e1d`). CARD-1065 D-6 also rejected sleeping in settlement. | Not a duplicate. This card removes the lease-busy *source* of CARD-1043's incident; CARD-1043's repair path stays for other blocks (`AgentTaskReplyService.cs:4640-4647`). |
| "no unverified evidence binding". | First-settlement binding needs Succeeded + usable evidence + authorized subject and only warns on tip mismatch (`ReviewEvidenceBindingService.cs:28-63`; it never reads the Review's sync). Strict repair requires Synchronized/NoPushedProgress with `DesktopAfterSha == ReviewedSourceSha` and `MirrorDirty == false` (`:92-101`); recovery requires stored confirmed sync (`ReviewEvidenceRecoveryService.cs:59-63`). | Pending is not Confirmed, so both strict paths refuse it unchanged (fail-closed); first settlement keeps its exact predicate. V-27/V-28 pin this. |
| "CARD-1065 parking invariants". | Parking owns only Blocked tasks (`TerminalRunnerSeatReleaseService.OwnsAutomaticPath`; sweep `:95`). Three tests manufacture a Blocked precondition with `done` under a held lease: `ReviewEvidenceResettlementTests.BlockAsync` (`tests/Antiphon.Tests/Application/ReviewEvidenceResettlementTests.cs:54-69`), `BlockedTaskParkDeliveryTests.C1065_ParkedReviewReplyBindsFreshEvidence` (`:640-648`), `BlockedTaskSyncRecoveryTests.C1065_LeaseBusyDoesNotRetainPublishedIdleSeat` (`:48-56`). | Those tests opt out through the kill switch so the CARD-1043/1065 paths stay reachable and tested (D-8). |
| Code progress under lease contention. | `EvaluateRemoteAsync` treats every state but Synchronized as Indeterminate (`TaskCompletionProgressService.cs:178-183`) and checks the desktop HEAD (`:190-196`); `RemoteSyncBlockReason` blocks Code on Indeterminate (`AgentTaskReplyService.cs:1995-1998`). Objects of a new S become local only through the observation's fetch under the lease (`TaskProgressGit.cs:118-150`). | Attribute Code progress against S when S's objects are local (D-4); otherwise Code stays Blocked (residual gap, documented). |
| Deliverable and `git=` header for a runner task. | `ResolveDeliverableAsync` returns nothing for an unconfirmed sync (`AgentTaskReplyService.cs:3285-3291`); `TryDescribeGitAsync` says "base unknown" (`:4436-4441`). | Read both from S when its objects are local; otherwise keep today's absence (D-7). |
| Mirror removal at retirement. | `RemoveMirrorAsync` uses `RemoteSync.ConfirmedSha ?? WorktreeBaseSha` as `PublishedSha` (`RemoteWorkspaceService.cs:719-720`); a capable runner refuses removal when its tip is not in that history. | For Pending use the server-observed origin tip (`ObservedSha`) before the baseline (D-6). |
| Counts in the card (63 blocks, 150 seat-hours). | Not re-measured here: the desktop server log is unreachable from the mirror. The code paths above confirm the mechanism; the counts are the card's. | None. |

Platform read on 2026-10-06: `GET /api/runner-defaults` has `globalRunnerId=server2`; `GET
/api/session-runners` lists desktop (windows, capacity 2), server2 (linux, draining, not accepting
work) and server2-temp (linux, capacity 10). Every slice and checkpoint here runs on the Linux lane
with real Git and the isolated PostgreSQL fixture; no `-Runner` pin and no `-Platform` is needed.

## Decisions

### D-1. Settle on the verdict when the only failure is desktop lease contention; on by default behind a kill switch

Add `DelegationSettings.RunnerSyncDebtOnSettlement` (bool, **default true**) and
`DelegationSettings.RunnerSyncDebtAttentionMinutes` (int, default 30, floor 1; validated beside
`RunnerSyncBudgetSeconds`). With the switch off every path below is byte-for-byte today's.

Eligibility is fail-closed and decided once per settlement sync result:

- `State == Unavailable` and `Reason == runner_sync_lease_busy` (the spent budget; `LeaseWaiting`
  still leaves the task open for the next sweep exactly as today), **and**
- `RemoteSha` is a full object id that the server itself observed with `ls-remote` on the task's
  exact owned ref (`refs/heads/feat/card-task-<id8>`), **and**
- the sync reached the lease point: by construction every lease-busy outcome follows a passed
  `ValidateCheckoutAsync` (identity, branch, sequencer, clean) and a non-refused mirror step.

Everything else keeps the Blocked/Decide/"then reply" settlement: all `Refused` states, and every
`Unavailable` reason other than `LeaseBusy`. For the Code role the verdict additionally needs a
determinate progress read (D-4); an indeterminate Code read still blocks.

*Why default on:* the card's Ask is unconditional, and CARD-1077 found that CARD-0667's dormant
path "with no activation date" let the cost grow unmeasured. The switch exists for rollback. This
default is the one call that is the operator's; flipping it is a one-line change in S1.

Rejected: raising `RunnerSyncBudgetSeconds` (multiplies latency; CARD-1043 D-1); retrying inside
settlement (holds the model turn and seat; CARD-1065 D-6); treating every `Unavailable` as pending
(loses the fail-closed desktop guarantees of CARD-0657 D-5); treating desktop dirt as debt (a
Refused state is a human repair, not contention; its test `Refused_sync_never_autosaves_or_releases_workspace`
stays as is); shipping dormant.

### D-2. `Pending` is a typed sync state minted once, before progress evaluation, and recorded immutably

Add `RemoteSettlementSyncState.Pending = 5`. `RemoteSettlementSyncResult.Confirmed` stays
`Synchronized or NoPushedProgress` with a desktop SHA, so Pending is **never** Confirmed.
`RemoteSyncEvidence.From` therefore records `State=Pending`, `ObservedSha=S`, `ConfirmedSha=null`,
`Reason=runner_sync_lease_busy`.

A new pure class `server/Application/Services/SettlementSyncDebtPolicy.cs` owns the rule:
`Classify(task, result, settings)` returns `result with { State = Pending }` when D-1 holds, else
the result unchanged; `BlockReason(task, result, evidence)` is today's `RemoteSyncBlockReason`
body plus one arm: `Pending` blocks only a Code task whose evidence is Indeterminate.
`AgentTaskReplyService.PrepareRemoteAsync` (`:1922-1942`) applies `Classify` to the prepared
result so the progress evaluation, the block decision, the evidence JSON and the debt row all see
one state; `RemoteSyncBlockReason` delegates to the policy. `RemoteWorkspaceService` never returns
Pending (the park sweep's vocabulary is untouched).

Consumers checked: `ReviewEvidenceBindingService.PrepareRepairAsync` refuses Pending with
`review_evidence_sync_unconfirmed`; `ReviewEvidenceRecoveryService` refuses stored Pending with
`stored_sync_unconfirmed`; `MergeBackAsync` returns "left for review" for `Confirmed != true`;
`BlockedTaskSyncRecoveryService` cannot see Pending. All fail closed without edits; V-27/V-28 pin
the first two.

Rejected: mutating the settlement evidence JSON when the sweep succeeds (CARD-0657 D-4 immutability;
CARD-1065 D-6 "does not ... synthesize"); deciding Pending inside `RemoteWorkspaceService` (it has
neither the role policy nor the setting, and the park sweep shares it).

### D-3. Lease-busy sync results carry what the server observed; ancestry is read before the lease

`TaskProgressGit.ObserveExactRefCoreAsync` returns the advertised full SHA in `Sha` on its
`repository_lease_busy` answers (state stays Unavailable; the existing CARD-0499 R16 test keeps
asserting Unavailable and no unlocked fetch). In `RemoteWorkspaceService.SyncOwnedCheckoutAsync`:

1. When the observation is `Present` (S's objects are local and pinned), run the ancestry read
   (`S == b` or `IsAncestorAsync(repo, b, S)`) **before** acquiring the lease. `merge-base
   --is-ancestor` is a read and takes no lease. `false` yields the same `Diverged`/`Rewound`
   refusals as today (so a rewritten branch blocks whatever the lease state); `null` yields
   `InspectionUnavailable`. The existing under-lease checks stay where they are.
2. The acquire lease-busy outcome carries `RemoteSha = S`, `DesktopBeforeSha = l0`,
   `ObservationRef`, `EndpointFingerprint`, the mirror fields, and a new additive
   `bool? SourceDescends = true` on `RemoteSettlementSyncResult`.
3. The observation lease-busy outcome (objects not local) carries `RemoteSha = advertised` and
   `SourceDescends = null`. The after-publish re-observation compares the advertised SHA with
   `mirror.Tip` when the fetch is lease-busy: equal continues, different stays
   `ChangedDuringValidation`.

`LeaseWaiting` semantics, `RunnerSyncLeaseWaits`, the budget and the slice are unchanged.

Rejected: fetching or pinning without the repository lease (CARD-0499 rule; CARD-0743 owns
contention); taking the runner's `WorkspacePublish` relation as ancestry proof (CARD-1065 D-2
rejects runner/report claims as proof; `Relation` is T vs S, not b).

### D-4. Code progress is attributed against the observed S when its objects are local

`TaskCompletionProgressService.EvaluateRemoteAsync` gains one arm for `State == Pending`:

- `SourceDescends == true` and `RevParseCommitAsync(repo, S)` succeeds: run today's claim rules
  with `s = RemoteSha` (claim reachable from S, novel against both captured baselines, S itself
  Primary when unclaimed) and **skip** the desktop-HEAD check, whose premise (the desktop
  represents S) does not hold. The Primary source records `VerifiedSha = S`, `LocalObserved = l0`,
  `RemoteObserved = S`. `S == b` is `NoAttributedProgress` with `runner_no_pushed_progress` /
  `runner_reported_commit_not_pushed`, exactly as NoPushedProgress is today.
- otherwise `Indeterminate(runner_sync_lease_busy)`, exactly as today.

Consequences: a Code task with attributed progress settles Succeeded Pending; a Code task that
pushed nothing settles Failed `CompletedWithoutProgress` (its own verdict, lease or no lease); a
Code task whose new objects never reached the desktop repository stays Blocked. The merge-back gate
is unchanged: `AllowsAutomaticWorkspaceMutation` may be true, but `MergeBackAsync` returns "left
for review" because Pending is not Confirmed, so no desktop mutation happens (G-10).

**Residual gap.** Under a lease held for the whole budget by a long land, a Code task that pushed
new commits is lease-busy at the *observation fetch*, its objects are not local, and it stays
Blocked as today. Reviews, Plans and every no-push task (S == b is always local) are covered, as is
any Code task whose objects were fetched during a free moment of the budget. Covering the rest
needs either a lease-free observation fetch (CARD-0743 territory) or a runner-attested
ancestry-and-claim answer on the `WorkspacePublish` wire (runner + contracts + server). File that
as a follow-up card; measure its size from `AgentTaskEvents` Warning rows containing
`runner_sync_lease_busy` on Code-role tasks after this card activates.

### D-5. A durable debt row and a bounded sweep, modelled on CARD-1065 S6

New entity `server/Domain/Entities/AgentTaskSyncDebt.cs` (table `AgentTaskSyncDebts`): `Id`,
`TaskId`, `Attempt`, `SettlementEventId`, `RunnerId`, `WorktreePath`, `RemoteWorktreePath`,
`RepositoryPath`, `FullRef`, `BaselineSha`, `SourceSha` (= S), `DesktopBeforeSha`,
`EndpointFingerprint`, `State` (`AgentTaskSyncDebtState { Pending, Ready, Held, Superseded }`),
`ReasonCode`, `Attempts`, `NextAttemptAt`, `SourceReadyAt`, `ConfirmedSha`, `Revision`
(concurrency token), `CreatedAt`, `UpdatedAt`. Unique index `(TaskId, Attempt)`; index
`(State, NextAttemptAt)`. Migration `AddAgentTaskSyncDebts` created with the repo-pinned
`dotnet ef migrations add --project server` (CARD-0677); `HasPendingModelChanges()` must be false
(V-13, same check as `BlockedTaskParkStateTests`).

Settlement adds the row to the same tracker as the task and the settlement event whenever the
prepared result is Pending and the status is Succeeded or Failed; `PersistDeliverThenReleaseAsync`
commits it with the settlement (G-11). `NextAttemptAt = now`, `ReasonCode = runner_sync_lease_busy`.

New `server/Application/Services/SettlementSyncRecoveryService.SweepAsync` copies the claim and
save discipline of `BlockedTaskSyncRecoveryService`: at most 32 due rows per pass, ordered by
`NextAttemptAt`; claim under `SELECT ... FOR UPDATE` on the task row with a revision CAS and the
1/2/4/5-minute backoff; Git outside the transaction; one `AgentTaskChanged` dashboard event per
saved result. It calls new `RemoteWorkspaceService.SyncSettledAsync(task, debt, ct)` which refuses
`settlement_sync_source_changed` unless task id, attempt, eligibility, worktree paths, runner,
baseline SHA and full ref still match the row and `SourceSha` is full, then runs
`SyncCoreAsync(task, [debt.SourceSha], inspectMirror: false, singleAttempt: true)`. Passing
`reportedTips = [S]` makes an advanced origin tip `TipNotReported` (Refused): the sweep only ever
fast-forwards to the recorded source (G-8) and never republishes a mirror.

Save: **Ready** when `Confirmed && DesktopAfterSha == S && RemoteSha == S && FullRef == debt.FullRef`
(`SourceReadyAt`, `ConfirmedSha`); **Superseded** (`settlement_sync_superseded_by_retirement`)
when the task's worktree registration is gone (`RetirementReserved`, or `IdentityMismatch` with a
missing `WorktreePath` directory, or a `TaskWorktreeRetirements` row for the attempt); **Held**
for every other `Refused`/`NotApplicable` result, `BaselineUnavailable`, `BranchNotPushed`, and
for a changed episode (`settlement_sync_episode_changed`: attempt, `ProgressBaselineJson` or
`WorktreePath` changed, or status no longer Succeeded/Failed); otherwise **Pending** with the
backoff. The save CAS compares `Attempt`, `ProgressBaselineJson`, `WorktreePath` and status, not
`ConcurrencyToken` (land requests rotate it). The sweep writes only the debt row (G-9): no
`AgentTasks.Status`, no `StageOutcomes`, no completion obligation, no evidence JSON.

Wiring: `AgentTaskDispatcher` gets an optional `SettlementSyncRecoveryService? settlementSync`
constructor argument and `RunSweepAsync("settlement sync debt", d => d.RecoverSettlementSyncAsync(ct))`
beside the `blocked-task sync` sweep (`AgentTaskDispatcher.cs:336`); `server/Program.cs`
registers the service beside `BlockedTaskSyncRecoveryService` (`:412`).

Rejected: reusing `AgentTaskPark` (one blocked episode per row, requires a publication receipt and
`Blocked`); scanning evidence JSON for Pending (not indexable, and the JSON is immutable); landing
the fast-forward as a side effect of `-Land` (the land takes the lease for its own work and
retires the worktree; it needs no desktop fast-forward to succeed).

### D-6. Land, retirement and evidence paths stay as they are; only the published SHA fallback changes

- `-Land`/`POST /land/v2` resolves the source from origin's exact ref and never reads
  `RemoteSync` (ground truth). A Pending owner lands as any Succeeded owner does (V-29); a Pending
  state adds no approval and no refusal (G-12).
- First-settlement Review binding is unchanged (`PrepareFirstSettlementAsync`). Strict repair and
  recovery refuse Pending as a witness (V-27, V-28). Known consequence: a Review whose **first**
  block was a non-lease refusal and whose continuation hits lease-busy settles Succeeded with
  `next=decide` ("Review evidence repair refused: review_evidence_sync_unconfirmed") and unbound
  evidence; the recovery endpoint refuses it too. Commission a fresh Review. This is rare (two
  different block causes in a row) and fail-closed.
- `RemoveMirrorAsync` picks `publishedSha` as `ConfirmedSha ?? (State == Pending ? ObservedSha :
  null) ?? WorktreeBaseSha`: the server-observed origin tip is by definition published, so a
  capable runner removes a mirror whose tip is on origin instead of refusing on the baseline.
- `MergeBackAsync`, `AllowsAutomaticWorkspaceMutation`, `TryCommitOnSettleAsync` are unchanged.

### D-7. Caller-facing text, header, projections, attention and CLI

- Header: the report's own `next=`; workspace note `branch <X> left for review; source <S>
  (desktop-sync=pending)`. `source <S>` has the same meaning as the Synchronized case: the SHA to
  review and land against.
- Warning event and caller line (one `AgentTaskEventType.Warning`, same mechanism as the
  `progress=none` warning): `Runner sync pending: runner_sync_lease_busy. Origin <fullRef> is at
  <S>; the desktop checkout <WorktreePath> will be fast-forwarded by the settlement sync sweep
  (synced later). No reply is needed; Review and -Land use the pushed branch at <S>.` The text
  never contains "then reply".
- Deliverable (`deliverable=`) and `git=` header are read from S when `SourceDescends == true`
  (objects local); otherwise today's absence.
- `GET /api/agent-tasks/{id}` exposes `progressEvidence.remoteSync.state = "Pending"` and a new
  `syncDebt` object (`id`, `state`, `sourceSha`, `confirmedSha`, `reasonCode`, `attempts`,
  `nextAttemptAt`, `sourceReadyAt`) beside `parkSync`.
- Attention: a pure projection `SettlementSyncDebtAttention.Build(debts, tasks, now, settings)`
  called from `AttentionService` beside `BuildRunnerSeatReleaseItemsAsync` (`:237`). **Held** →
  Warning naming task, attempt, source, reason and the matching recovery sentence from the Runner
  sync outcomes section; **Pending** older than `RunnerSyncDebtAttentionMinutes` → Warning "desktop
  sync still pending (lease contention)"; Ready/Superseded/fresh Pending → nothing. Kind
  `AttentionKind.SessionDisagreement`, condition key `settlement-sync-debt:<id>`, as CARD-1065 S6
  does for seat-release debt (no new kind).
- `scripts/delegate.ps1` status rendering prints `Runner sync: Pending runner_sync_lease_busy;
  origin=<S>` and a `Desktop sync debt: <state> ...` line when `syncDebt` is present.
- Docs: `docs/orchestration-loop.md` Runner sync outcomes gains the `runner_sync_lease_busy →
  Pending` paragraph ("synced later", no reply, Held reasons map to the existing recovery
  sentences); `docs/session-runtime-invariants.md` gains a CARD-1082 paragraph; `docs/ops-http.md`
  and `docs/antiphon-api.md` document `syncDebt` and the Pending state; the
  `RunnerBranchContractDocumentationTests` class pins the vocabulary (V-26).

### D-8. Legacy tests that manufacture Blocked with `done` under a held lease opt out explicitly

`RunnerSettlementWorld.CreateAsync` and `RunnerSeatReleaseFixture.CreateAsync` gain
`bool syncDebt = true` (the production default) that sets `RunnerSyncDebtOnSettlement`.
`ReviewEvidenceResettlementTests.IncidentAsync` (and thus every CARD-1043 resettlement test),
`BlockedTaskParkDeliveryTests.C1065_ParkedReviewReplyBindsFreshEvidence` and
`BlockedTaskSyncRecoveryTests.C1065_LeaseBusyDoesNotRetainPublishedIdleSeat` pass
`syncDebt: false` with a one-line comment: they test the CARD-1043/CARD-1065 paths that remain
reachable when the switch is off or when the block is not lease contention. Their assertions are
unchanged, which makes them the kill-switch controls (R-3, R-4). The three CARD-0657 tests that
hold the lease on a Code task with unfetched objects (`Runner_sync_block_commits_the_completion_obligation`,
`Sync_uncertainty_blocks_and_reply_retries`, `Sustained_lease_contention_spans_sweeps_without_holding_up_other_settlements`)
stay Blocked under D-4 and are left as they are (R-2). Any other legacy test that changes verdict
is a plan finding to report, not something to adjust silently.

### D-9. Keep fleet placement dynamic

No `-Runner` or `-Platform` in any brief for this card; Code and Review run where
`GET /api/runner-defaults` sends them (server2-temp today). Every checkpoint row is Linux-capable.

## Implementation slices

Each slice is one 30-60 minute Code task. Slices that touch the same file are sequenced (S1 → S2
→ S3 → S4a → S4b → S5 → S6 → S7); none run in parallel. Commit and push each slice before its
checkpoint group; never rebase or force-push the task branch. Besides its own rows, each slice runs
the existing classes named in its row (lesson from CARD-1065 S5).

| Slice | Budget | Files | Deliverable and named tests |
|---|---|---|---|
| S1: state, policy, settings, fixture knobs | 45 min | `server/Application/Dtos/RemoteSettlementSyncDtos.cs` (`Pending`, `SourceDescends`); `server/Application/Settings/DelegationSettings.cs` (two settings + validation); new `server/Application/Services/SettlementSyncDebtPolicy.cs` (`Classify`, `BlockReason`, `PendingWarning`, `WorkspaceNote`); `tests/Antiphon.Tests/TestHelpers/RunnerSettlementWorld.cs` and `tests/Antiphon.Tests/Application/RunnerSeatReleaseFixture.cs` (`syncDebt` knob); `ReviewEvidenceResettlementTests.cs`, `BlockedTaskParkDeliveryTests.cs`, `BlockedTaskSyncRecoveryTests.cs` (pass `syncDebt: false`, D-8) | New `tests/Antiphon.Tests/Application/SettlementSyncDebtPolicyTests.cs` (7 methods, 17 results); `DelegationLeaseSettingsTests.C1082_RunnerSyncDebtSettingsDefaultOnAndAttentionFloor` (3 arguments). No production behaviour changes yet: the policy is pure and unwired. |
| S2: observation and sync-result enrichment, ancestry before the lease | 60 min | `server/Infrastructure/Git/TaskProgressGit.cs`; `server/Application/Services/RemoteWorkspaceService.cs` (`SyncOwnedCheckoutAsync` only) | `RunnerSettlementSyncTests`: `C1082_AcquireLeaseBusyCarriesObservedTipAndAncestry`, `C1082_ObservationLeaseBusyCarriesAdvertisedTipWithoutAncestry`, `C1082_DivergedTipRefusesBeforeTheLease`; `TaskProgressGitTests.C1082_LeaseBusyObservationReportsAdvertisedTip`. Whole `RunnerSettlementSyncTests` and `C499_R16_LeaseContentionIsUnavailableWithoutAnUnlockedFetch` must stay green. |
| S3: Pending progress evaluation | 45 min | `server/Application/Services/TaskCompletionProgressService.cs` (`EvaluateRemoteAsync`) | `RunnerCompletionProgressTests`: `C1082_PendingWithLocalObjectsAttributesClaimAgainstObservedTip`, `C1082_PendingEqualTipIsNoPushedProgress`, `C1082_PendingWithoutLocalObjectsIsIndeterminate`; whole class stays green. |
| S4a: debt entity, migration, `SyncSettledAsync`, mirror removal fallback | 50 min | New `server/Domain/Entities/AgentTaskSyncDebt.cs`; `server/Infrastructure/Data/AppDbContext.cs` (DbSet + configuration); `server/Migrations/<stamp>_AddAgentTaskSyncDebts.cs` + snapshot via `dotnet ef`; `RemoteWorkspaceService.cs` (`SyncSettledAsync`, `RemoveMirrorAsync` fallback) | `RunnerSettlementSyncTests`: `C1082_SettledDebtSyncOnlyFastForwardsToTheRecordedSource`, `C1082_SettledDebtSyncRefusesAChangedEpisode`; new `tests/Antiphon.Tests/Application/SettlementSyncDebtSchemaTests.C1082_SchemaMigrationMatchesModel`. |
| S4b: sweep service, dispatcher hook, registration | 60 min | New `server/Application/Services/SettlementSyncRecoveryService.cs`; `server/Application/Services/AgentTaskDispatcher.cs`; `server/Program.cs`; `RunnerSettlementWorld.cs` (register the sweep and a `SweepSettlementSyncAsync()` helper) | New Slow class `tests/Antiphon.Tests/Application/SettlementSyncRecoveryTests.cs` (6 methods, 9 results) seeding debt rows directly; registered in `tests/Antiphon.Tests/slow-tests-allowlist.txt` with its reason. `BlockedTaskSyncRecoveryTests` stays green. |
| S5: settlement integration | 60 min | `server/Application/Services/AgentTaskReplyService.cs` (`PrepareRemoteAsync` → `Classify`; `RemoteSyncBlockReason` → policy; pending warning and workspace note; debt row insert; deliverable and `git=` from S when local; no change to `SettledHandoff`, release or obligation code) | `RunnerTaskSettlementTests`: `C1082_ReviewLeaseBusySettlesSucceededWithPendingSyncDebt`, `C1082_CodeLeaseBusyWithLocalObjectsSettlesSucceededPending`, `C1082_CodeLeaseBusyEqualTipFailsOnItsOwnVerdict`, `C1082_DisabledSettingKeepsLeaseBusyBlocked`, `C1082_PendingDebtRecoversThroughTheDispatcherSweep`. Whole `RunnerTaskSettlementTests`, `ReviewEvidenceResettlementTests`, `TerminalRunnerSeatReleaseTests`, `BlockedTaskSyncRecoveryTests`, `BlockedTaskParkDeliveryTests` must stay green (CP-7, CP-8, CP-9). |
| S6: projection, attention, CLI, docs | 50 min | `server/Application/Dtos/AgentTaskDtos.cs`; `server/Application/Services/AgentTaskService.cs`; new `server/Application/Services/SettlementSyncDebtAttention.cs`; `server/Application/Services/AttentionService.cs`; `scripts/delegate.ps1`; `docs/orchestration-loop.md`, `docs/session-runtime-invariants.md`, `docs/ops-http.md`, `docs/antiphon-api.md` | `SettlementSyncDebtPolicyTests.C1082_AttentionWarnsOnHeldAndStalePendingDebt` (4 arguments); `SettlementSyncRecoveryTests.C1082_TaskDetailExposesSyncDebt`; `RunnerBranchContractDocumentationTests.C1082_settlement_sync_debt_is_documented`. |
| S7: fail-closed evidence and land controls | 45 min | Tests only unless a defect appears; a production defect is repaired in its owning slice's files and reported | `ReviewEvidenceResettlementTests.C1082_PendingContinuationRefusesStrictRebind`; `ReviewEvidenceRecoveryTests.C1043_StoredSyncRequired` gains a Pending arm (same method count); new `tests/Antiphon.Tests/Application/SettlementSyncDebtLandingTests.C1082_PendingSyncOwnerLandsOnPushedBranch` on `LandingSafetyHarness` (model: `AgentTaskLandAdoptionTests`). |

After S7 the Code task for the last slice (or Review) runs CP-13, CP-14 and CP-15 at the final
committed SHA so the whole closed list has one certificate.

## Verification design

### Inspection

| Read | Why |
|---|---|
| `AgentTaskReplyService.cs:800-1135` (`SettleAsync`), `:1922-2000` (`PrepareRemoteAsync`, `StillWaitingForLease`, `RecordRemoteEvidenceAsync`, `RemoteSyncBlockReason`), `:2559-2571` (`SettledHandoff`), `:3278-3330` (`ResolveDeliverableAsync`), `:4393-4460` (`TryDescribeGitAsync`), `:4630-4745` (`RecordDelegateStageOutcomeAsync`) | Every settlement seam this card touches or must leave alone. |
| `RemoteWorkspaceService.cs:140-540` (`IsEligible`, `SyncAsync`, `SyncParkedAsync`, `SyncCoreAsync`, `SyncAdmittedAsync`, `SyncOwnedCheckoutAsync`, `WhileLeaseBusyAsync`, `LeaseReason`), `:690-740` (`RemoveMirrorAsync`) | The three lease points and the park sweep's guards to mirror. |
| `TaskProgressGit.cs:55-180`; `TaskCompletionProgressService.cs:60-200, 330-345` | Observation and attribution rules. |
| `BlockedTaskSyncRecoveryService.cs` (entire), `AgentTaskPark.cs`, `AppDbContext.cs:126-155`, `AgentTaskDispatcher.cs:330-340, 7351-7352`, `Program.cs:405-413` | The sibling to copy and its wiring. |
| `ReviewEvidenceBindingService.cs:28-130`, `ReviewEvidenceRecoveryService.cs:45-85`, `LandApproval.cs:50-70`, `AgentTaskLandingProtocol.cs:85-160` | The safety properties that must not move. |
| Tests: `RunnerTaskSettlementTests.cs:95-125, 495-690`, `RunnerSettlementSyncTests.cs:559-700, 904-1100` (`SyncWorld`), `RunnerSettlementWorld.cs`, `RunnerSeatReleaseFixture.cs:130-225, 324-350`, `BlockedTaskSyncRecoveryTests.cs:29-106`, `ReviewEvidenceResettlementTests.cs:20-100`, `TerminalRunnerSeatReleaseTests.cs:128-171`, `LandingSafetyHarness.cs` | Fixtures to extend and the legacy tests whose preconditions this card changes. |

### Proves it works now

| V | Behaviour | Test (class-qualified) |
|---|---|---|
| V-1 | `Classify` turns Unavailable/LeaseBusy with a full server-observed `RemoteSha` into Pending. | `SettlementSyncDebtPolicyTests.C1082_LeaseBusyWithObservedTipClassifiesPending` |
| V-2 | With `RunnerSyncDebtOnSettlement=false` the result is unchanged and settlement is Blocked/Decide with today's text. | `SettlementSyncDebtPolicyTests.C1082_DisabledSettingLeavesLeaseBusyUnavailable`; `RunnerTaskSettlementTests.C1082_DisabledSettingKeepsLeaseBusyBlocked` |
| V-3 | `BlockReason` for Pending: null for non-Code; Code blocks only on Indeterminate. | `SettlementSyncDebtPolicyTests.C1082_BlockReasonForPendingDependsOnRoleAndProgress` (4 results) |
| V-4 | `RemoteSyncEvidence.From(Pending)` has `ObservedSha=S`, `ConfirmedSha=null`, `State=Pending`; `Confirmed` is false. | `SettlementSyncDebtPolicyTests.C1082_PendingEvidenceRecordsObservedNotConfirmed` |
| V-5 | The pending warning says "synced later" and "No reply is needed" and never "then reply". | `SettlementSyncDebtPolicyTests.C1082_PendingWarningSaysSyncedLaterAndNoReply` |
| V-6 | Acquire-lease-busy result carries `RemoteSha=S`, `DesktopBeforeSha=l0`, `SourceDescends=true`; the desktop is untouched. | `RunnerSettlementSyncTests.C1082_AcquireLeaseBusyCarriesObservedTipAndAncestry` |
| V-7 | Observation-lease-busy result carries the advertised S with `SourceDescends=null`; `TaskProgressGit` reports `Sha` on a lease-busy answer and performs no fetch. | `RunnerSettlementSyncTests.C1082_ObservationLeaseBusyCarriesAdvertisedTipWithoutAncestry`; `TaskProgressGitTests.C1082_LeaseBusyObservationReportsAdvertisedTip` |
| V-8 | A diverged pushed tip is `Refused runner_sync_diverged` while the lease is held (not lease-busy). | `RunnerSettlementSyncTests.C1082_DivergedTipRefusesBeforeTheLease` |
| V-9 | Pending with local objects: claim C ∈ S and novel → ProgressObserved, Primary `VerifiedSha=S`. | `RunnerCompletionProgressTests.C1082_PendingWithLocalObjectsAttributesClaimAgainstObservedTip` |
| V-10 | Pending with `S == b` → NoAttributedProgress `runner_no_pushed_progress` (or `runner_reported_commit_not_pushed` with a claim). | `RunnerCompletionProgressTests.C1082_PendingEqualTipIsNoPushedProgress` |
| V-11 | Pending without local objects → Indeterminate `runner_sync_lease_busy`. | `RunnerCompletionProgressTests.C1082_PendingWithoutLocalObjectsIsIndeterminate` |
| V-12 | `SyncSettledAsync` fast-forwards only to the recorded S (advanced tip → `runner_sync_tip_not_reported`); mismatched task/attempt/paths/baseline → `settlement_sync_source_changed`. | `RunnerSettlementSyncTests.C1082_SettledDebtSyncOnlyFastForwardsToTheRecordedSource`, `RunnerSettlementSyncTests.C1082_SettledDebtSyncRefusesAChangedEpisode` |
| V-13 | Migrations apply and `HasPendingModelChanges()` is false. | `SettlementSyncDebtSchemaTests.C1082_SchemaMigrationMatchesModel` |
| V-14 | A due Pending debt fast-forwards the desktop to S, ends Ready with `ConfirmedSha=S`, publishes `AgentTaskChanged`. | `SettlementSyncRecoveryTests.C1082_DueDebtFastForwardsDesktopAndMarksReady` |
| V-15 | An advanced origin tip ends Held `runner_sync_tip_not_reported`; the desktop stays put. | `SettlementSyncRecoveryTests.C1082_AdvancedRemoteTipIsHeldNotFollowed` |
| V-16 | A busy lease returns at once: Pending, `Attempts=1`, `NextAttemptAt=+1 min`, then 2, 4, 5. | `SettlementSyncRecoveryTests.C1082_LeaseBusyDebtBacksOffAndStaysPending` |
| V-17 | Dirty, sequencing and diverged desktop checkouts end Held with their `runner_sync_*` reason. | `SettlementSyncRecoveryTests.C1082_DesktopRefusalsHoldWithReason` (3 results) |
| V-18 | A changed attempt ends Held `settlement_sync_episode_changed`; a retired worktree ends Superseded. | `SettlementSyncRecoveryTests.C1082_ChangedEpisodeOrRetiredWorktreeEndsTheDebt` (2 results) |
| V-19 | Ready leaves `CompletionProgressEvidenceJson`, `StageOutcomes`, `AgentTasks.Status`, obligations and the event count unchanged. | `SettlementSyncRecoveryTests.C1082_ReadyDebtLeavesSettlementEvidenceAndOutcomesImmutable` |
| V-20 | Review, `S == b`, lease held for the budget: Succeeded; `NextStage` is the report's; `RemoteSync.State=Pending`, `ObservedSha=b`, `ConfirmedSha=null`; Warning contains "synced later"; debt row Pending with `SourceSha=b`; the profiled obligation has no `next=decide`; the stage outcome binds `SubjectTaskId`/`ReviewedSourceSha`; the delegate is released as in `Runner_push_settles_success_without_claim`; desktop HEAD is the baseline. | `RunnerTaskSettlementTests.C1082_ReviewLeaseBusySettlesSucceededWithPendingSyncDebt` |
| V-21 | Code, pushed S with objects made local, lease held: Succeeded Pending; progress ProgressObserved `VerifiedSha=S`; header has `source <S> (desktop-sync=pending)` and `git=` counts; no merge command ran; desktop HEAD is the baseline. | `RunnerTaskSettlementTests.C1082_CodeLeaseBusyWithLocalObjectsSettlesSucceededPending` |
| V-22 | Code, no push, lease held: Failed `CompletedWithoutProgress` with `runner_no_pushed_progress`; debt row Pending; worktree retained. | `RunnerTaskSettlementTests.C1082_CodeLeaseBusyEqualTipFailsOnItsOwnVerdict` |
| V-23 | After V-20's settlement and lease release, `AgentTaskDispatcher.RecoverSettlementSyncAsync` ends the debt Ready; the task stays Succeeded with one completion obligation. | `RunnerTaskSettlementTests.C1082_PendingDebtRecoversThroughTheDispatcherSweep` |
| V-24 | `AgentTaskService.GetAsync` exposes `syncDebt` and `progressEvidence.remoteSync.state == Pending`. | `SettlementSyncRecoveryTests.C1082_TaskDetailExposesSyncDebt` |
| V-25 | Attention: Held → Warning; Pending older than the threshold → Warning; fresh Pending and Ready → nothing. | `SettlementSyncDebtPolicyTests.C1082_AttentionWarnsOnHeldAndStalePendingDebt` (4 results) |
| V-26 | The owner docs name `runner_sync_lease_busy`, `Pending`, `syncDebt`, `RunnerSyncDebtOnSettlement`, "synced later". | `RunnerBranchContractDocumentationTests.C1082_settlement_sync_debt_is_documented` |
| V-27 | A Review first blocked by desktop dirt, then continued under a held lease, settles Succeeded Pending with `next=decide` and the repair-refusal prefix; the unbound row is not replaced. | `ReviewEvidenceResettlementTests.C1082_PendingContinuationRefusesStrictRebind` |
| V-28 | The recovery endpoint refuses a stored Pending sync with `stored_sync_unconfirmed`. | `ReviewEvidenceRecoveryTests.C1043_StoredSyncRequired` (Pending arm) |
| V-29 | A Succeeded owner with stored Pending sync and bound Clean review evidence lands through `/land/v2` on real Git. | `SettlementSyncDebtLandingTests.C1082_PendingSyncOwnerLandsOnPushedBranch` |
| V-30 | Settings: `RunnerSyncDebtOnSettlement` defaults true; `RunnerSyncDebtAttentionMinutes` defaults 30 and rejects 0. | `DelegationLeaseSettingsTests.C1082_RunnerSyncDebtSettingsDefaultOnAndAttentionFloor` (3 results) |

### Guards the regression

| R | Negative control | Test |
|---|---|---|
| R-1 | Every non-lease reason and every Refused state is never Pending, at the policy and at settlement (`runner_sync_dependency_unavailable`, dirty desktop). | `SettlementSyncDebtPolicyTests.C1082_OtherReasonsAndStatesAreNeverPending` (7 results), `SettlementSyncDebtPolicyTests.C1082_MissingOrShortObservedTipIsNeverPending` (2); existing `TerminalRunnerSeatReleaseTests.Blocked_report_with_running_runner_requires_a_published_park`, `RunnerTaskSettlementTests.Refused_sync_never_autosaves_or_releases_workspace` |
| R-2 | Code with unfetched objects under a held lease stays Blocked/Decide with the obligation. | existing `RunnerTaskSettlementTests.Runner_sync_block_commits_the_completion_obligation`, `Sync_uncertainty_blocks_and_reply_retries`, `Sustained_lease_contention_spans_sweeps_without_holding_up_other_settlements` |
| R-3 | The CARD-1043 incident and repair path is reachable with the switch off. | whole `ReviewEvidenceResettlementTests` (with `syncDebt: false`) |
| R-4 | CARD-1065 park sync debt and parked-review rebinding are unchanged. | whole `BlockedTaskSyncRecoveryTests`, whole `BlockedTaskParkDeliveryTests` |
| R-5 | Service-level lease semantics (`LeaseWaiting`, `LeaseBusy`, slice per sweep, no mutation while busy) are unchanged. | whole `RunnerSettlementSyncTests`; `TaskProgressGitTests.C499_R16_LeaseContentionIsUnavailableWithoutAnUnlockedFetch` |
| R-6 | The seat release ledger for Succeeded/Failed/Blocked is unchanged. | whole `TerminalRunnerSeatReleaseTests` |
| R-7 | Existing runner progress attribution rules are unchanged. | whole `RunnerCompletionProgressTests` |

### Guard inventory

| G | Decision: guard | PC |
|---|---|---|
| G-1 | D-1: only lease-busy with a server-observed full tip classifies Pending | PC-1 |
| G-2 | D-1: the kill switch restores today's Blocked settlement | PC-2 |
| G-3 | D-2: Pending is never Confirmed | PC-3 |
| G-4 | D-1/D-4: a Code task with an indeterminate progress read still blocks | PC-4 |
| G-5 | D-3: ancestry is checked before the lease; a diverged tip refuses | PC-5 |
| G-6 | D-3: an observation-busy result never claims ancestry | PC-6 |
| G-7 | D-4: progress attribution needs S's objects local | PC-7 |
| G-8 | D-5: the sweep only fast-forwards to the recorded source | PC-8 |
| G-9 | D-5/D-6: the sweep never rewrites settlement evidence | PC-9 |
| G-10 | D-6: a Pending settlement never mutates the desktop checkout | PC-10 |
| G-11 | D-5: the debt row commits with the settlement | PC-11 |
| G-12 | D-6: no Pending refusal exists in land approval | PC-12 |
| G-13 | D-7: Held and stale Pending debt reach attention | PC-13 |
| G-14 | D-7: the report's next stage is kept | PC-14 |
| G-15 | D-6: strict rebind refuses a Pending witness | PC-15 |
| G-16 | D-6: recovery refuses stored Pending | PC-16 |

### Positive controls

Mutation runs break/red/restore/green after land; Code runs ordinary V/R; Review judges guard
independence. Each row is one compiling production defect applied singly; use the exact method
filter, never the class. Zero executions, a build or fixture failure, or a timeout is not red.
Restore source and rebuild before the green run. Controls sharing a file run sequentially.

| PC | Break guard by compiling defect | Exact detecting filter | Expected red assertion |
|---|---|---|---|
| PC-1 | G-1: `Classify` returns Pending for any `Unavailable` result. | `/*/*/SettlementSyncDebtPolicyTests/C1082_OtherReasonsAndStatesAreNeverPending` | `G-1`: Timeout/FetchUnavailable/... arms are still Unavailable, not Pending. |
| PC-2 | G-2: `Classify` ignores `RunnerSyncDebtOnSettlement`. | `/*/*/RunnerTaskSettlementTests/C1082_DisabledSettingKeepsLeaseBusyBlocked` | `G-2`: status is Blocked and the handoff starts with "Runner sync blocked". |
| PC-3 | G-3: `Confirmed` includes `Pending`. | `/*/*/SettlementSyncDebtPolicyTests/C1082_PendingEvidenceRecordsObservedNotConfirmed` | `G-3`: `ConfirmedSha` is null and `Confirmed` is false. |
| PC-4 | G-4: `BlockReason` returns null for every Pending result. | `/*/*/SettlementSyncDebtPolicyTests/C1082_BlockReasonForPendingDependsOnRoleAndProgress` | `G-4`: the Code/Indeterminate arm returns the lease reason. |
| PC-5 | G-5: skip the pre-lease ancestry read. | `/*/*/RunnerSettlementSyncTests/C1082_DivergedTipRefusesBeforeTheLease` | `G-5`: state is Refused `runner_sync_diverged`, not Unavailable lease-busy. |
| PC-6 | G-6: set `SourceDescends = true` on the observation-busy outcome. | `/*/*/RunnerSettlementSyncTests/C1082_ObservationLeaseBusyCarriesAdvertisedTipWithoutAncestry` | `G-6`: `SourceDescends` is null. |
| PC-7 | G-7: evaluate the Pending claim without the local `RevParseCommitAsync(S)` check. | `/*/*/RunnerCompletionProgressTests/C1082_PendingWithoutLocalObjectsIsIndeterminate` | `G-7`: assessment is Indeterminate with `runner_sync_lease_busy`. |
| PC-8 | G-8: `SyncSettledAsync` passes `reportedTips: null`. | `/*/*/SettlementSyncRecoveryTests/C1082_AdvancedRemoteTipIsHeldNotFollowed` | `G-8`: debt is Held `runner_sync_tip_not_reported` and desktop HEAD is unchanged. |
| PC-9 | G-9: the sweep's save also writes a confirmed `RemoteSync` into `CompletionProgressEvidenceJson`. | `/*/*/SettlementSyncRecoveryTests/C1082_ReadyDebtLeavesSettlementEvidenceAndOutcomesImmutable` | `G-9`: the evidence JSON equals its pre-sweep bytes. |
| PC-10 | G-10: `MergeBackAsync` treats `Pending` as confirmed. | `/*/*/RunnerTaskSettlementTests/C1082_CodeLeaseBusyWithLocalObjectsSettlesSucceededPending` | `G-10`: no `merge` command ran and desktop HEAD is the baseline. |
| PC-11 | G-11: skip the debt insert in `SettleAsync`. | `/*/*/RunnerTaskSettlementTests/C1082_ReviewLeaseBusySettlesSucceededWithPendingSyncDebt` | `G-11`: exactly one `AgentTaskSyncDebts` row exists for the task attempt. |
| PC-12 | G-12: insert a refusal in `LandApproval` when the owner's stored sync is Pending. | `/*/*/SettlementSyncDebtLandingTests/C1082_PendingSyncOwnerLandsOnPushedBranch` | `G-12`: the land completes `Landed`. |
| PC-13 | G-13: the attention projection selects only `Pending`. | `/*/*/SettlementSyncDebtPolicyTests/C1082_AttentionWarnsOnHeldAndStalePendingDebt` | `G-13`: the Held arm yields one Warning item. |
| PC-14 | G-14: set `NextStage = Decide` for a Pending settlement. | `/*/*/RunnerTaskSettlementTests/C1082_ReviewLeaseBusySettlesSucceededWithPendingSyncDebt` | `G-14`: `NextStage` equals the report's stage and the snapshot header has no `next=decide`. |
| PC-15 | G-15: `PrepareRepairAsync` accepts `Pending` as a sync witness. | `/*/*/ReviewEvidenceResettlementTests/C1082_PendingContinuationRefusesStrictRebind` | `G-15`: the handoff starts with "Review evidence repair refused" and the row count is 1. |
| PC-16 | G-16: `ReviewEvidenceRecoveryService` accepts stored `Pending`. | `/*/*/ReviewEvidenceRecoveryTests/C1043_StoredSyncRequired` | `G-16`: the Pending arm refuses `stored_sync_unconfirmed`. |

### Out of scope

Lease contention and lease hold time (CARD-0743); lease-free observation fetch (CARD-0499 rule);
parking activation and reclaim (CARD-1065 D-9); runner-attested ancestry for Code (follow-up card
from D-4); desktop-dirt-as-debt; attention noise policy (CARD-1085); client UI rendering (the
client does not render `remoteSync` today).

### Checkpoints

Exactly one isolated build and one filter per row. Counts are TUnit executed results: a method
with N `[Arguments]` rows contributes N. Existing rosters counted at `ed0f0d920`:
`RunnerSettlementSyncTests` 24, `TaskProgressGitTests` 10 (7 methods), `RunnerCompletionProgressTests`
8, `RunnerTaskSettlementTests` 26 (22 methods), `ReviewEvidenceResettlementTests` 9,
`ReviewEvidenceRecoveryTests` 21, `TerminalRunnerSeatReleaseTests` 39 (29 methods),
`BlockedTaskSyncRecoveryTests` 2, `BlockedTaskParkDeliveryTests` 4, `DelegationLeaseSettingsTests`
11 (4 methods), `RunnerBranchContractDocumentationTests` 4. Confirm the TRX roster equals the
expected set, not merely at least `Min`. The table below was validated at planning time with the
checkpoint importer (`import --plan`, tool built through `scripts/build-slot.ps1`): 15 rows, exit
0, no advisory warnings; every derived row timeout is at or under 42 minutes.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1082-cp1/` | policy-settings | `/*/*/(SettlementSyncDebtPolicyTests*)\|(DelegationLeaseSettingsTests*)/*` | V-1, V-2, V-3, V-4, V-5, V-30, R-1 | exact 31 results (17 policy + 11 existing settings + 3 new), 0 failed/skipped | 31 | 8 | |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-c1082-cp2/` | sync-service | `/*/*/RunnerSettlementSyncTests/*` | V-6, V-7, V-8, R-5 | exact 27 results (24 existing + 3 C1082_*), 0 failed/skipped | 27 | 9 | |
| CP-3 | S2 | CP-2 | observation | `/*/*/TaskProgressGitTests/(C1082_*)\|(C499_R16_LeaseContentionIsUnavailableWithoutAnUnlockedFetch*)` | V-7, R-5 | exact 2 results, 0 failed/skipped | 2 | 3 | |
| CP-4 | S3 | `tests/Antiphon.Tests -> bin-c1082-cp4/` | progress | `/*/*/RunnerCompletionProgressTests/*` | V-9, V-10, V-11, R-7 | exact 11 results (8 existing + 3 C1082_*), 0 failed/skipped | 11 | 6 | |
| CP-5 | S4a | `tests/Antiphon.Tests -> bin-c1082-cp5/` | debt-schema-sync | `/*/*/(RunnerSettlementSyncTests*)\|(SettlementSyncDebtSchemaTests*)/C1082_*` | V-12, V-13 | exact 6 results (3 from S2 + 2 S4a sync + 1 schema), 0 failed/skipped | 6 | 7 | |
| CP-6 | S4b | `tests/Antiphon.Tests -> bin-c1082-cp6/` | sweep | `/*/*/(SettlementSyncRecoveryTests*)\|(BlockedTaskSyncRecoveryTests*)/*` | V-14, V-15, V-16, V-17, V-18, V-19, R-4 | exact 11 results (9 new + 2 existing park sweep), 0 failed/skipped | 11 | 12 | true |
| CP-7 | S5 | `tests/Antiphon.Tests -> bin-c1082-cp7/` | settlement | `/*/*/RunnerTaskSettlementTests/*` | V-2, V-20, V-21, V-22, V-23, R-1, R-2 | exact 31 results (26 existing + 5 C1082_*), 0 failed/skipped | 31 | 14 | true |
| CP-8 | S5 | CP-7 | legacy-evidence-park | `/*/*/(ReviewEvidenceResettlementTests*)\|(BlockedTaskSyncRecoveryTests*)\|(BlockedTaskParkDeliveryTests*)/*` | R-1, R-3, R-4 | exact 15 results (9 + 2 + 4), 0 failed/skipped | 15 | 9 | true |
| CP-9 | S5 | CP-7 | legacy-seat-release | `/*/*/TerminalRunnerSeatReleaseTests/*` | R-1, R-6 | exact 39 results (29 methods with their argument rows), 0 failed/skipped | 39 | 10 | true |
| CP-10 | S6 | `tests/Antiphon.Tests -> bin-c1082-cp10/` | projection-docs | `/*/*/(SettlementSyncDebtPolicyTests*)\|(RunnerBranchContractDocumentationTests*)\|(SettlementSyncRecoveryTests*)/*` | V-24, V-25, V-26 | exact 36 results (17 + 4 attention, 4 + 1 docs, 9 + 1 detail), 0 failed/skipped | 36 | 14 | true |
| CP-11 | S7 | `tests/Antiphon.Tests -> bin-c1082-cp11/` | evidence-land-controls | `/*/*/(ReviewEvidenceResettlementTests*)\|(SettlementSyncDebtLandingTests*)/*` | V-27, V-29, R-3 | exact 11 results (9 + 1, 1), 0 failed/skipped | 11 | 9 | true |
| CP-12 | S7 | CP-11 | recovery-controls | `/*/*/ReviewEvidenceRecoveryTests/*` | V-28 | exact 21 results (the Pending arm extends an existing method), 0 failed/skipped | 21 | 8 | true |
| CP-13 | all | `tests/Antiphon.Tests -> bin-c1082-final/` | final-settlement-a | `/*/*/(RunnerTaskSettlementTests*)\|(ReviewEvidenceResettlementTests*)\|(SettlementSyncDebtLandingTests*)\|(ReviewEvidenceRecoveryTests*)/*` | V-2, V-20-V-23, V-27-V-29, R-1-R-3 | exact 63 results (31 + 10 + 1 + 21), 0 failed/skipped | 63 | 12 | true |
| CP-14 | all | CP-13 | final-settlement-b | `/*/*/(TerminalRunnerSeatReleaseTests*)\|(BlockedTaskParkDeliveryTests*)\|(BlockedTaskSyncRecoveryTests*)\|(SettlementSyncRecoveryTests*)/*` | V-14-V-19, V-24, R-1, R-4, R-6 | exact 55 results (39 + 4 + 2 + 10), 0 failed/skipped | 55 | 12 | true |
| CP-15 | all | CP-13 | final-units-git | `/*/*/(SettlementSyncDebtPolicyTests*)\|(DelegationLeaseSettingsTests*)\|(RunnerSettlementSyncTests*)\|(TaskProgressGitTests*)\|(RunnerCompletionProgressTests*)\|(SettlementSyncDebtSchemaTests*)\|(RunnerBranchContractDocumentationTests*)/*` | V-1-V-13, V-25, V-26, V-30, R-1, R-5, R-7 | exact 92 results (21 + 14 + 29 + 11 + 11 + 1 + 5), 0 failed/skipped | 92 | 12 | |

Run each group once its slice is committed, through the checkpoint tool:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1082-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1082-driver/ --nologo
dotnet tools/Antiphon.Checkpoints/bin-c1082-driver/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-06-card-1082-settlement-sync-debt-plan.md --after S1 --expected-source-sha "$(git rev-parse HEAD)"
```

`S4a` and `S4b` are literal After tokens (`--after S4a`); a numeric range does not select them.
The last slice runs `--rows CP-13,CP-14,CP-15` at the final committed SHA. Continue `wait` while the exit
is 75. Exit 4 is a slot timeout: report the row as not run. Preserve every tool-produced
`CHECKPOINT` line. No unlisted build or test loop; a failure-driven rerun is the same `CP-n` with
its reason and commit. Code and Review run `scripts/check-evidence-diff.ps1` over the task range.
Delete only producer-owned `bin-c1082-*` outputs; evidence stays ignored.

### Cost

Ordinary Code V/R floor = **145 minutes**, the sum of the `EstimatedMinutes` column
(8 + 9 + 3 + 6 + 7 + 12 + 14 + 9 + 10 + 14 + 9 + 8 + 12 + 12 + 12). Authoring: about 6.5 hours
across eight slices. `-ExpectAbout` for each Code dispatch is its slice's rows plus its authoring budget.
Mutation (post-land): 16 method-scoped controls, about 90 minutes.

## Activation and rollout

The behaviour activates with the AppHost restart that picks up S5 (no setting change needed).
Before that restart, land or re-settle any task currently Blocked on `runner_sync_lease_busy`
through today's reply path; the new code does not reinterpret historical Blocked rows. The sweep
starts with the first dispatcher tick after S4b. To roll back the verdict change set
`Delegation:RunnerSyncDebtOnSettlement=false` and restart; accepted debt rows keep recovering.
Operators read `syncDebt` on task detail and the `settlement-sync-debt:*` attention items; a Held
item names the `runner_sync_*` reason whose recovery the Runner sync outcomes section already
documents. Measure after a week: count Warning events containing `runner_sync_lease_busy` by role
to size the D-4 residual gap.

## Risks and notes for Code and Review

- **Object locality decides the Code verdict.** In `SyncWorld` the desktop repository has not
  fetched the runner's push; make S local in V-21 with `git fetch <origin> <fullRef>` (objects and
  `FETCH_HEAD` only, no ref) before holding the lease. V-22 needs no fetch (S == b). Do not make
  objects local by pre-pinning through `TaskProgressGit` (that is the observation under test).
- **One state, one place.** `Classify` runs in `PrepareRemoteAsync` only; `TaskCompletionProgressService.PrepareAsync`
  (the direct-evaluation path, `Direct_evaluation_prepares_remote_source`) must still see the raw
  result when settlement did not prepare. If Code finds a second caller that needs Pending, route
  it through the policy, never duplicate the predicate.
- **Additive records.** `RemoteSettlementSyncResult` and `RemoteSyncEvidence` gain trailing
  optional fields; the park sweep, `ReviewRecoveryWorld` and every positional constructor in tests
  stay valid. Do not reorder parameters.
- **`[Arguments]` counts are promises.** CP rows count results; a new test may not add parameter
  expansion without updating this table.
- **Legacy flags are explicit.** The `syncDebt: false` opt-outs in D-8 carry a comment naming
  this card; Review rejects an opt-out added to make an unrelated test pass.
- **Lease timing in tests.** Reuse `controlledSyncClock: true`, `LeaseBusy.First/Next` and the
  `RunnerSyncBudgetSeconds` advance exactly as `Sync_uncertainty_blocks_and_reply_retries` does;
  bound awaits with the existing 30-second guards. A sweep test holds the lease with
  `world.Git.Leases.TryAcquireAsync(world.Git.Desktop, ...)` and asserts the immediate return.
- **Migration discipline.** `dotnet tool restore` then `dotnet ef migrations add AddAgentTaskSyncDebts --project server`;
  never hand-edit the snapshot. V-13 is the proof.
- **No new attention kind, no new event type.** Both would widen client contracts; the Warning
  event and `SessionDisagreement` kind already carry sibling conditions.
