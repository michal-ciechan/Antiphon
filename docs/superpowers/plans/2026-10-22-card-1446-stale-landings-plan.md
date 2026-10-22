# CARD-1446 plan: stale landings and sync debt

Plan stage, 2026-10-22. Branch `feat/card-task-7c30c896`, base `d872d06bdb7ed8556a19f7bf5a6e7e06a07ffdb0`
(master `604297b06` carries nothing newer for the files below).
Investigation: [2026-10-22-card-1446-stale-landings-and-sync-debt.md](../../investigations/2026-10-22-card-1446-stale-landings-and-sync-debt.md)
(commit `b3c61b85dfd876a923e6bd35c9dbdb39aec11e56`). Operator authority (2026-10-21): "fine to prune and
cleanup ... just go for it, better to delete more than less".

## Problem in one paragraph

Four different things were read as "stale rows". (A) 145 landings are published with `Cleanup=Refused`:
the designed end state when guarded removal refuses, and nothing has ever retried them because both
producers of cleanup-only land requests are switched off. (B) 5 landings from September sit in a
mid-publication phase after a refusal; four are superseded schema-2 operations and one is a schema-3
`push_rejected` that CARD-0711 deliberately keeps resumable, so no terminal rule was "missed" for it.
(C1) 110 sync debts are `Ready`, which is the paid state; the CARD-1432 classifier miscounted them.
(C2) one `Pending` debt has been retried 2,080 times because the recovery sweep has no attempt cap.
Beyond that, the daily residue sweep writes about 5,000 `Held` candidate rows a day (100,398 so far).
Every fix below is code with tests; the historical rows are closed by the same rules on their first
run after deploy, not by a manual database sweep.

## Ground truth (card / brief assumption vs code at `d872d06bd`)

| # | Assumption | What the code does | Consequence for the plan |
|---|---|---|---|
| G-1 | `push_rejected` at `PushStarted` is a terminal refusal the rule at `AgentTaskLandingProtocol.cs:380-388` misses | **False by design.** `AgentTaskLandTargetRaceTests.C711_PushRejectedWithoutMovementStaysResumable` asserts the operation stays `Active` at `PushStarted`, `Publication=Unconfirmed`, and a second request with the same expected SHA resumes it without re-verifying. The land request itself completes as `LandRefused`. | Do not make `push_rejected` terminal (D-1). Close the *abandoned* resumable operation by age instead (S1). |
| G-2 | `source_changed` / `land_request_identity_conflict` at `TargetAdvanceStarted` need a new terminal arm | Those phases exist only on schema 1/2 (`AgentTaskLandingState.cs:30`, `SchemaVersion != 3`). Every new operation is schema 3 (`LandOperationFactory.cs:88`), and any request on a schema-2 operation is refused `landing_schema_superseded` and marked `Refused` first (`AgentTaskLandingProtocol.cs:101-103,383`). | The arm is unreachable for new rows. No protocol change (D-1). The four schema-2 rows close under the same age rule as G-1. |
| G-3 | `Ready` sync debt is open | `Ready` is set only when desktop and remote both equal `SourceSha` (`SettlementSyncRecoveryService.cs:152-155,174-180`); the sweep selects only Pending/Held (`:36-40`); attention loads only Held/Pending (`AttentionService.cs:3561-3562`). | Nothing in code is wrong. The CARD-1432 rule text and the planned `AntiphonRefProtection` (not yet in code: no such file on master `604297b06`) carry the misread (S3). |
| G-4 | The Pending debt loops forever | Confirmed. `ClaimAsync` bumps `Attempts` with backoff capped at 5 minutes (`:118-119,281`); `SaveAsync` maps `Unavailable`/`LeaseBusy` to Pending (`:186-191`). No attempt or age cap exists. | S2 adds the cap at the claim, before Git runs. |
| G-5 | Enabling `WorktreeResidue:Execute` would clean up the 145 | **Only about 10 of them.** Desktop DB read (read-only, 2026-10-22 01:55 UTC): 127 `ignored_content_preserved`, 7 `evidence_retention_unavailable`, 6 `worktree_removal_incomplete`, 4 `cleanup_additional_budget_expired`, 1 `nested_registration`. The 30 `ignored_content_preserved` rows whose terminal event still carries a `protected:` detail list **only** `.antiphon/<scratch>/...` paths (acceptance logs, probe projects, checkpoint spills); none names a secret or user-local file. The classifier makes every unlisted `.antiphon` path Protected (`WorktreeIgnoredContentClassifier.cs:47-51`), and the registered retention is `RefusingEvidenceRetention` (`Program.cs:464`), so a retry would refuse again. | S4 fixes the two refusal causes first, paces retries, adds a terminal cleanup state, then Execute is flipped (D-5). |
| G-6 | The Publication lane paces its retries | **No.** `PersistCooldownAsync` writes `WorktreeResidueCandidateCursors` only in the SettledTask lane (`WorktreeResidueSweepService.cs:211`), and **nothing reads `NotBefore`**. The Publication lane would re-queue the same 25 oldest operations every day forever. | S4c adds the cooldown reader and a retry count. |
| G-7 | Candidate rows are needed per run | 95,229 of 100,398 rows are `SettledTask Held release_required`: the same verdict for the same ~5,300 terminal tasks, daily. Readers: run detail by `RunId` (`TaskWorktreeRetirementService.cs:217-222`), preview checks by run id (`CardWorktreeCleanupExecutor.cs:182`, `CardWorktreeMirrorCleanupService.cs:35`). No reader needs an unchanged Held row repeated. | S5 writes a Held row only when the verdict changed, and prunes old Held rows. |
| G-8 | The brief's slices (1)-(5) are independent | S1, S2, S3 and S5 are independent. S4 has internal order (retention and classifier before pacing before activation). | Checkpoints are grouped per slice so Code can run them as it commits. |

## Decisions

**D-1. Kind B is "abandoned", not "missed terminal": an age rule in a daily reconciler, no protocol change.**
`AbandonedLandingReconciler.RunAsync` (new) runs from `WorktreeResidueJob.ExecuteAsync` before the sweep,
on every daily run regardless of `WorktreeResidue:Execute` (it is a state correction, not a deletion).
A landing is abandoned when **all** hold:
- `Active`, `Publication == Unconfirmed`, `RemoteConfirmedAt == null`, `!HasPublication`;
- `Phase` is `Verified`, `TargetAdvanceStarted`, `LocalTargetAdvanced` or `PushStarted` (every earlier
  phase already has a terminal arm);
- **not** (`Phase == PushStarted && PushExitCode == 0`): a push that returned 0 but was never confirmed
  is `push_unconfirmed`, which only a human may resolve; the reconciler logs it at Warning and keeps it;
- the task is `Succeeded`, `Failed` or `Canceled`;
- no `AgentTaskLandRequests` row for the task `IsPending`, and the task's `CurrentLandRequestId`
  request (if any) is `Completed`, `Superseded` or `Canceled`;
- `UpdatedAt < now - WorktreeResidue:AbandonedLandingDays` (default 7; 0 disables).

Action: `Publication = Refused`, `_state.Transition(op, Refused, now)` (permitted: `!HasPublication`),
`LastReason` becomes `landing_abandoned:<previous reason>` clipped to 400, and one
`AgentTaskEventType.Warning` event on the task: `landing_abandoned: operation <id:N> refused <reason>
at <phase> on <UpdatedAt:O>; no resume within <days> days; the next -Land starts a fresh operation`.
Under the task row lock (`SELECT ... FOR UPDATE`), one transaction per operation, same as
`RequestCleanupRetryCoreAsync`.
Effect on the 5 rows: all five are 26+ days old, unpublished, tasks Succeeded, requests Completed, and
`fc89bde9` is `push_rejected`, which the protocol raises only for a non-zero push exit
(`AgentTaskLandingProtocol.cs:355`); they become `Refused` on the first daily run. A later explicit `-Land`
replaces a Refused operation (`CanReplaceRefused`, schema 3 needs `ApprovalLandRequestId` and
`ReviewedSourceSha == OriginalSourceSha`, both true for these), re-rebases and re-verifies.
*Rejected:* extending the terminal arm for `push_rejected`. It breaks the CARD-0711 same-SHA resume and
costs a re-verification on every rejected push.
*Rejected:* arms for schema-2 `TargetAdvanceStarted`. Unreachable (G-2); dead code with no witness.
*Rejected:* running the rule in the dispatcher sweep. A 7-day rule does not need a minute cadence; the
daily job already owns landing hygiene.

**D-2. Sync debt: an attempt cap at the claim, mapping to Held with its own reason.**
`Delegation:RunnerSyncDebtMaxAttempts`, default 48, 0 disables (validator: `>= 0`). In `ClaimAsync`'s
Pending branch, after `EpisodeMatches` and `RegistrationGoneAsync`, if `cap > 0 && debt.Attempts >= cap`
then `TerminalAsync(debt, Held, "settlement_sync_attempts_exhausted", due)`, commit, return null: no Git
runs, the sweep's `attempted` count does not include it. The new constant is
`RemoteSettlementSyncReasons.SettlementSyncAttemptsExhausted`. Why 48: with the 1/2/4/5-minute backoff
that is about 3 h 50 min of retries, which covers an evening of back-to-back lands holding the desktop
lease (CARD-1432 saw seven lands over 30 minutes) and ends an 8-day loop within four hours of deploy.
A Held row then costs one restamp an hour (CARD-1136) instead of a Git inspection every five minutes.
Attention (`SettlementSyncDebtAttention`): `Recovery()` gets the sentence for the new reason,
`"Retried <Attempts> times without a fast-forward. Run git pull --ff-only in the desktop worktree, or
retire it."`, and the evidence string gains `; attempts=<n>` for every item.
Effect on 76fe1072: `Attempts` is 2,080, so its next due claim turns it Held without Git. The attention
item changes from "pending (lease contention)" to "held: settlement_sync_attempts_exhausted" and stays
until an operator acts; Held rows are never superseded (CARD-1136 F3d), which this plan does not change.
*Rejected:* mapping `RemoteSettlementSyncState.Unavailable` straight to Held on the first result. A
single inspection exception is often transient (a Git lock or a busy host); one evening of lands would
hold every debt minted in it. The cap applies to every non-terminal result, `LeaseBusy` included.
*Rejected:* an age cap in hours beside the attempt cap. Two knobs for one behaviour; attempts already
measure elapsed retries because the backoff is bounded.

**D-3. Classifier: amend the CARD-1432 rule text now; patch the class if it has landed.**
`AntiphonRefProtection` does not exist yet. S3 edits
`docs/superpowers/plans/2026-10-22-card-1432-repository-maintenance-plan.md` D-5 and the S2 row test
description so the protected set reads "the task of an `AgentTaskSyncDebts` row in **Pending or Held**
(Ready is paid; CARD-1446)" and "the task of an `Active` landing whose `Phase` is not Complete or
Refused **and whose `Cleanup` is not `Abandoned`** (D-4)". It adds one correction line under §2 of
`docs/investigations/2026-10-21-card-1432-desktop-repo-maintenance.md` (the deletion that ran was
conservative; the misread only protected 110 extra tasks). If `server/Application/Services/AntiphonRefProtection.cs`
exists when Code runs, Code also patches its debt clause and the `C1432_protection_rule_rows` Ready row,
and reports that run as an unlisted row with this reason.

**D-4. Kind A: make the two refusal causes retryable, pace the retries, and give cleanup a terminal state.**
Recommendation: **enable `WorktreeResidue:Execute`**, but only after S4a-S4c, because today a retry of
137 of the 145 would refuse again every day (G-5, G-6).
- **S4a `FileSystemEvidenceRetention`** replaces `RefusingEvidenceRetention` at `Program.cs:464`. It
  copies each evidence-class file to `WorktreeCleanup:RetainedEvidenceRoot` (default
  `Path.Combine(GitSettings.WorktreeBasePath, ".retained")`) under `<taskId:N>/<attemptId:N>/<relative>`,
  hashing the source while copying and the destination after, refusing on mismatch
  (`evidence_retention_verify_failed`), on an existing destination with different bytes
  (`evidence_retention_conflict`), and on `MaxRetainedEvidenceBytes` / `MaxRetainedEvidenceFiles`
  (`evidence_retention_budget_bytes` / `evidence_retention_budget_files`; both settings already exist
  at 64 MiB / 500). The gate's second reading (`GuardedWorktreeRemoval.cs:133`) still requires the
  evidence set unchanged, so this is a copy, never a move. Nothing else in the gate changes.
- **S4b classifier default:** add `**/.antiphon/**` to `WorktreeCleanupSettings.DefaultRetainedIgnored`.
  Protected names still win first (`.antiphon/report.md`, `.antiphon/deliverables/**`, `**/.claude/**`,
  `**/appsettings.*.json`, `**/*.user`, `**/*.local.json`, `logs/**`, ...), so an unlisted `.antiphon`
  spill becomes Evidence (retained, then deleted with the tree) instead of Protected (tree kept forever).
- **S4c pacing and terminal state.** New `LandCleanupStatus.Abandoned = 4` (enum stored as int; no
  migration). The Publication lane: skips an operation whose cursor `landing:<opId:N>` has
  `NotBefore > now` (candidate `Held/cooldown`); when it queues a retry it stamps that cursor
  `NotBefore = now + WorktreeResidue:CleanupRetryCooldownDays` (default 7); when the count of this
  operation's earlier `Queued` candidate rows is `>= WorktreeResidue:MaxCleanupRetries` (default 3) and
  `Cleanup == Refused`, it calls `AgentTaskLandService.AbandonCleanupAsync(taskId, opId, count)` and
  writes the candidate as `Refused/cleanup_abandoned`. `AbandonCleanupAsync` (task row lock; requires
  Active, `HasPublication`, `Cleanup == Refused`, no pending request) sets `Cleanup = Abandoned`,
  keeps `LastReason`, and adds an `AgentTaskEventType.LandingCleanup` event: `cleanup abandoned after
  <n> refused retries (<LastReason>); worktree kept at <path>; an explicit -Land retries cleanup`.
  The sweep's selection becomes `Cleanup != Complete && Cleanup != Abandoned`. `RequestCleanupRetryCoreAsync`
  is unchanged (only `Complete` refuses), so an explicit `-Land` on the task is the cleanup-only retry
  path; the protocol's `CleanupAsync` keeps `Abandoned` when the retry refuses again and moves to
  `Complete` when it is clean. `TerminalType`, `FormatOutcome` and `CardWorktreeCleanupExecutor` need
  no logic change (`Abandoned != Complete` already reads as residue).
- **S4d activation (configuration, not code):** set `WorktreeResidue:Execute=true` on the desktop
  AppHost configuration as the commissioned deploy `docs/orchestration-loop.md` already names;
  `MaxActionsPerRun` stays 25, so the 145 drain over about six daily runs. `CardDoneAfterUtc` stays
  null. Flipping Execute also arms the SettledTask lane, which is fail-closed
  (`EvaluateEligibilityAsync`: 95,229 `release_required` holds, 0 authorized), and the legacy Eligible
  path is not reached while the retirement service is registered.

What happens to each residue class, and what is never deleted:

| Residue | After S4 | Never deleted |
|---|---|---|
| `ignored_content_preserved`, `.antiphon` spills only | Retained by copy, byte-verified, then the tree is deleted on the next retry | The retained copy under `.retained/` (no automatic retention in this card; a count/bytes line in the sweep log) |
| `ignored_content_preserved` with a protected name (secret, `.claude`, `appsettings.*.json`, deliverables, `report.md`) | Still refused; after 3 paced retries `Cleanup=Abandoned`, one `LandingCleanup` event, worktree kept | The worktree and every protected file in it |
| `evidence_retention_unavailable` | Retained by copy (S4a), then deleted | The retained copy |
| `worktree_removal_incomplete` (6; directory and registration already gone, set-aside pending) | The retry finishes the set-aside deletion and the branch (`GuardedWorktreeRemoval.cs:56-85`) | Nothing: the set-aside bytes were already set aside by an earlier pass |
| `cleanup_additional_budget_expired` (4) | A fresh budget on retry | Nothing |
| `nested_registration` (1) | Refused until the nested worktree is gone; `Abandoned` after 3 retries | The nested registration's tree |

*Rejected:* flipping Execute alone. G-5/G-6: it re-queues the same 25 operations daily and completes
about 10 of 145.
*Rejected:* a new `LandPhase.CompleteWithResidue`. `GuardedWorktreeRemoval.AuthorityAsync` (`:386`)
and the state machine key on `Phase is CleanupStarted or Complete`; a cleanup status is the smaller
change and every existing `Cleanup != Complete` reader already means "residue".
*Rejected:* moving (not copying) `.antiphon` spills. The gate's second inspection requires the evidence
set unchanged; a move would always read as `ignored_content_changed`.
*Rejected:* deleting protected ignored content under the managed root. CARD-0452/0665 chose refusal for
secrets and user-local files; the data shows the volume is `.antiphon` spills, which S4b handles.

**D-5. Candidate rows: write a Held row only when the verdict changed; prune old Held rows.**
`WorktreeResidueCandidateCursor` gains `LastOutcomeKey` (string, nullable; migration
`AddWorktreeResidueCursorLastOutcome`). In a non-preview run the SettledTask lane (key `task:<id:N>`)
and the Publication lane (key `landing:<opId:N>`) compute `key = "<Lane>|<Outcome>|<ReasonCode>"`; a
`Held` row is written only when the cursor's `LastOutcomeKey` differs or is null, and the cursor is
updated either way. Action rows (`Queued`, `Refused`, `Partial`, `Removed`) are always written; preview
runs always write every row (the operator asked to see them); the Inventory lane (511 rows total) is
unchanged. Run counters (`Candidates`, `Held`, ...) still count every evaluation; the job logs
`candidates=<M> rows=<N> unchanged=<M-N>`. Retention: `Retention:WorktreeResidueHeldRowRetentionDays`
(default 30, 0 disables) in `DataRetentionService.RunOnceAsync` deletes `WorktreeResidueRunCandidates`
with `Outcome in (Held, Preview)` and `EvaluatedAt < cutoff`; run headers and action rows are kept,
so every queued cleanup request keeps its provenance.
*Rejected:* retention alone. 5,300 rows a day for 30 days is still 160,000 rows for no reader.
*Rejected:* dedupe without the cursor column. The previous run's rows would themselves be deduped,
so "unchanged since when" would have no anchor.

**D-6. No session waits for input.** This plan adds no session that waits for input and changes none,
so the CARD-1083 release checklist does not apply.

**D-7. Lane.** `GET /api/runner-defaults` → `globalRunnerId: server2`; `GET /api/session-runners`
lists `server2` (linux, 10 seats) and `desktop` (windows). Every row below uses the test database,
temp Git repositories and `FakeTimeProvider`, so the lane is server2; omit `-Runner` and `-Platform`.

## Slices

### S1: abandoned unpublished landings (D-1)

Files:
- `server/Application/Services/AbandonedLandingReconciler.cs` (new): `Task<int> RunAsync(CancellationToken)`;
  constructor `(AppDbContext, IOptions<WorktreeResidueSettings>, TimeProvider, ILogger<...>)`.
- `server/Application/Settings/WorktreeResidueSettings.cs`: `AbandonedLandingDays` (default 7);
  `WorktreeResidueSettingsValidator`: `>= 0`.
- `server/Infrastructure/Agents/WorktreeResidueJob.cs`: call the reconciler before `RunAsync`; its
  failure is logged and never fails the sweep.
- `server/Program.cs`: `AddScoped<AbandonedLandingReconciler>()`.
- Test: `tests/Antiphon.Tests/Application/AbandonedLandingReconcilerTests.cs` (test DB via
  `TestDbFixture.CreateIsolatedSchemaAsync`, `FakeTimeProvider`, rows seeded directly; no Git).

Commits: **S1a** test file plus a compiling skeleton whose `RunAsync` returns 0 (so CP-1 executes red);
**S1b** the implementation.

### S2: sync debt attempt cap (D-2)

Files:
- `server/Application/Settings/DelegationSettings.cs`: `RunnerSyncDebtMaxAttempts` (default 48) and
  its validator line (`>= 0`).
- `server/Application/Dtos/RemoteSettlementSyncDtos.cs`: `SettlementSyncAttemptsExhausted`.
- `server/Application/Services/SettlementSyncRecoveryService.cs`: the cap in `ClaimAsync`
  (constructor gains `IOptions<DelegationSettings>`; `RunnerSettlementWorld` already registers it).
- `server/Application/Services/SettlementSyncDebtAttention.cs`: recovery sentence and `attempts=`.
- Tests: `tests/Antiphon.Tests/Application/SettlementSyncRecoveryTests.cs` (existing class, three new
  methods using `RunnerSettlementWorld`, `DebtClock`, `SeedAsync`/`DebtAsync`; the exhausted reason is
  written as a string literal so the test compiles before the constant exists), and
  `tests/Antiphon.Tests/Application/SettlementSyncDebtAttentionTests.cs` (new; pure `Build()`; `SettlementSyncDebtPolicyTests` is the existing neighbour).

Commits: **S2a** tests only (they compile against today's code and fail on state); **S2b** implementation.

### S3: classifier text (D-3)

Files: `docs/superpowers/plans/2026-10-22-card-1432-repository-maintenance-plan.md` (D-5 and the
`C1432_protection_rule_rows` bullet), `docs/investigations/2026-10-21-card-1432-desktop-repo-maintenance.md`
(one correction line under §2). Conditional: `server/Application/Services/AntiphonRefProtection.cs` and
`LandRefRetentionTests` if present (reported as an unlisted run, reason D-3). One commit.

### S4: published-with-residue landings (D-4)

- **S4a** `server/Infrastructure/Git/FileSystemEvidenceRetention.cs` (new), `WorktreeCleanupSettings.RetainedEvidenceRoot`
  (nullable; default derived from `GitSettings.WorktreeBasePath`), `Program.cs:464` registration.
  Test: `tests/Antiphon.Tests/Infrastructure/FileSystemEvidenceRetentionTests.cs` (temp directories,
  no DB). Commits: **S4a-test** (test plus a skeleton that returns `evidence_retention_unavailable`),
  **S4a-impl**.
- **S4b** `WorktreeCleanupSettings.DefaultRetainedIgnored` adds `**/.antiphon/**`; `appsettings.json`
  `WorktreeCleanup:RetainedIgnored` mirrors it. Test: one method in the existing `tests/Antiphon.Tests/Application/WorktreeIgnoredContentClassifierTests.cs`,
  executed red today.
  Commits: **S4b-test**, **S4b-impl**.
- **S4c** `server/Domain/Enums/LandingEnums.cs` (`Abandoned = 4`), `WorktreeResidueSettings`
  (`CleanupRetryCooldownDays` 7, `MaxCleanupRetries` 3, validator `>= 0`), `WorktreeResidueSweepService.cs`
  Publication lane (cooldown read, retry count, abandon call, selection), `AgentTaskLandService.AbandonCleanupAsync`,
  `AgentTaskLandingProtocol.CleanupAsync` (keep `Abandoned` on a refused retry). The lane decision is
  a static `PublicationLanePolicy.Decide(op, queuedCount, cursor, now, settings)` returning
  `Cooldown | Queue | Abandon`, so it is unit-testable without the land service.
  Tests: `tests/Antiphon.Tests/Application/WorktreeResiduePublicationLaneTests.cs` (new; pure policy rows
  plus one test-DB test that `AbandonCleanupAsync` writes status, event and refuses a pending request)
  and two methods in `tests/Antiphon.Tests/Application/WorktreeLandingCleanupRetryTests.cs`
  (`LandingProtocolHarness`: an `Abandoned` operation stays `Abandoned` on a refused retry and reaches
  `Complete` on a clean one). Commits: **S4c-test** (tests plus the enum value and empty policy
  returning `Queue`, so the file compiles and the rows fail), **S4c-impl**.
- **S4d** no code. Docs: `docs/orchestration-loop.md` (the `WorktreeResidue:Execute` sentence near line
  2187 gains the S4 prerequisites, the cooldown, the retry cap and `Abandoned`), `docs/testing-and-build.md`
  (the "RefusingEvidenceRetention can preserve residue" sentence near line 362 becomes the copy-based
  retention). Activation is the orchestrator's commissioned deploy after land.

### S5: candidate row dedupe and retention (D-5)

Files: `server/Domain/Entities/WorktreeResidueRun.cs` (`LastOutcomeKey`), migration
`server/Migrations/<stamp>_AddWorktreeResidueCursorLastOutcome.cs` (via `dotnet ef`, see
`docs/testing-and-build.md` "dotnet-ef from the repo tool manifest"), `WorktreeResidueSweepService.cs`
(SettledTask and Publication lanes), `RetentionSettings.WorktreeResidueHeldRowRetentionDays`,
`DataRetentionService.PruneWorktreeResidueHeldRowsAsync` called from `RunOnceAsync`, `appsettings.json`
`Retention` block.
Tests: `tests/Antiphon.Tests/Application/WorktreeResidueCandidateRowTests.cs` (new; test DB; builds
`WorktreeResidueSweepService` with the retirement service the way `WorktreeResidueRegistrationTests`
does; `FakeTimeProvider`): a second non-preview run over unchanged tasks writes 0 new Held rows and the
counters still count; a changed verdict writes one row; a preview run writes every row; retention
deletes only Held/Preview rows older than the cutoff and keeps action rows and run headers.
Commits: **S5-test** (tests plus the entity column and migration so the schema exists; the sweep still
writes every row, so the dedupe row fails), **S5-impl**.

## Verification design

| V | Claim | Test | Goes red when |
|---|---|---|---|
| V-1 | An 8-day-old schema-3 `push_rejected` operation at `PushStarted` with `PushExitCode=1`, task Succeeded, request Completed, becomes `Refused`/`Refused` with `LastReason` prefixed `landing_abandoned:` and one Warning event | `AbandonedLandingReconcilerTests.C1446_abandoned_push_rejected_landing_becomes_refused_after_seven_days` | The reconciler is a no-op or the rule misses `PushStarted` |
| V-2 | Guard rows: `PushExitCode=0` kept (warning logged); 6 days old kept; pending request kept; published operation untouched; schema-2 `TargetAdvanceStarted source_changed` refused; `AbandonedLandingDays=0` does nothing | `AbandonedLandingReconcilerTests.C1446_guard_rows` (`[Arguments]`, 6 rows) | Any guard is dropped |
| V-3 | A due Pending debt with `Attempts >= 48` is Held `settlement_sync_attempts_exhausted`, `NextAttemptAt` +60 min, sweep returns 0 attempted, no Git command recorded | `SettlementSyncRecoveryTests.C1446_PendingDebtAtAttemptCapIsHeldExhaustedWithoutGit` | The cap is missing or runs Git first |
| V-4 | Lease busy from attempt 46: attempts 47 and 48 stay Pending with backoff, the next claim holds; `RunnerSyncDebtMaxAttempts=0` keeps claiming at 500 | `SettlementSyncRecoveryTests.C1446_LeaseBusyRunsToCapThenHolds`, `C1446_ZeroCapKeepsRetrying` | Off-by-one at the cap, or 0 is not honoured |
| V-5 | The Held exhausted item names attempts and the recovery sentence | `SettlementSyncDebtAttentionTests.C1446_ExhaustedHeldDebtNamesAttemptsAndRecovery` | The projection ignores the reason |
| V-6 | Evidence files are copied under `<root>/<task>/<attempt>/<relative>` with matching SHA-256; over-budget, verify-mismatch and conflicting destinations refuse with their reason and leave the source untouched | `FileSystemEvidenceRetentionTests.C1446_copies_and_verifies`, `C1446_refusals` (`[Arguments]`: bytes, files, conflict) | The skeleton still refuses, or a refusal deletes or truncates |
| V-7 | `.antiphon/c820-s3-final/load.jsonl` is Evidence; `.antiphon/report.md`, `.antiphon/deliverables/x` and `server/appsettings.Development.json` stay Protected; `.antiphon/inbox/a.md` stays Disposable | classifier test `C1446_unlisted_antiphon_spill_is_evidence_not_protected` | The default list or precedence changes |
| V-8 | Policy rows: cooldown active → `Cooldown`; 0..2 earlier queued and no cooldown → `Queue`; 3 earlier queued → `Abandon`; `Cleanup=Pending` never abandons | `WorktreeResiduePublicationLaneTests.C1446_policy_rows` | Pacing or the cap is wrong |
| V-9 | `AbandonCleanupAsync` sets `Abandoned`, keeps `LastReason`, writes one `LandingCleanup` event, refuses (`land_request_pending`) while a request is pending, refuses when `Cleanup != Refused` | `WorktreeResiduePublicationLaneTests.C1446_abandon_writes_status_and_event` | The write is partial or unguarded |
| V-10 | An `Abandoned` operation stays `Abandoned` when the explicit retry refuses, and becomes `Complete` when the retry is clean | `WorktreeLandingCleanupRetryTests.C1446_AbandonedStaysAbandonedOnRefusedRetry`, `C1446_AbandonedCompletesOnCleanRetry` | `CleanupAsync` regresses to `Refused` or blocks the retry |
| V-11 | Second unchanged non-preview run writes 0 Held rows and counters still count; a changed verdict writes 1 row; preview writes all | `WorktreeResidueCandidateRowTests.C1446_unchanged_held_rows_are_not_rewritten`, `C1446_changed_verdict_and_preview_write_rows` | Dedupe missing or over-eager |
| V-12 | Retention deletes Held/Preview rows older than the cutoff only | `WorktreeResidueCandidateRowTests.C1446_retention_prunes_only_old_held_rows` | Action rows or run headers are deleted |
| V-13 | No regression in the existing recovery, cleanup-retry and residue suites | CP-5, CP-13, CP-16 | Any changed reader breaks an existing contract |

Mutation targets for the later Mutation stage: the `>=` at the attempt cap; the `PushExitCode == 0`
exclusion; the `UpdatedAt` age comparison; `Abandoned` kept versus `Refused` in `CleanupAsync`;
the `LastOutcomeKey` equality; the retention `Outcome` filter.

### Checkpoints

Lane: server2 (D-7). Build output `bin-c1446-*/` (forward slash). Red rows run through
`scripts/run-checkpoint.ps1` with exit 1 expected; they are not checkpoint-tool rows. Each red row is
run at the named test commit, before the implementation commit exists.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1a | `tests/Antiphon.Tests -> bin-c1446-s1red/` | s1-red | `/*/*/AbandonedLandingReconcilerTests/C1446_abandoned_push_rejected_landing_becomes_refused_after_seven_days` | V-1 witness | **red**: 1 executed, 1 failed (phase still `PushStarted`) | 1 | 7 |
| CP-2 | S1b | `tests/Antiphon.Tests -> bin-c1446-s1/` | s1-green | `/*/*/AbandonedLandingReconcilerTests/*` | V-1, V-2 | all listed, 0 failed/skipped | 7 | 8 |
| CP-3 | S2a | `tests/Antiphon.Tests -> bin-c1446-s2red/` | s2-red | `/*/*/SettlementSyncRecoveryTests/C1446_PendingDebtAtAttemptCapIsHeldExhaustedWithoutGit` | V-3 witness | **red**: 1 executed, 1 failed (row claimed and attempted) | 1 | 7 |
| CP-4 | S2b | `tests/Antiphon.Tests -> bin-c1446-s2/` | s2-green | `/*/*/(SettlementSyncRecoveryTests/C1446*)\|(SettlementSyncDebtAttentionTests/*)` | V-3, V-4, V-5 | all listed, 0 failed/skipped | 4 | 9 |
| CP-5 | S2b | CP-4 | s2-regress | `/*/*/SettlementSyncRecoveryTests/C1082*` | V-13 | all listed, 0 failed/skipped | 10 | 8 |
| CP-6 | S3 | n/a | s3-docs | `pwsh -NoProfile -Command "if ((Select-String -Path docs/superpowers/plans/2026-10-22-card-1432-repository-maintenance-plan.md -Pattern 'Pending or Held').Count -lt 1) { exit 1 }; exit 0"` | D-3 | exit 0 | n/a | 1 |
| CP-7 | S4a-test | `tests/Antiphon.Tests -> bin-c1446-s4ared/` | s4a-red | `/*/*/FileSystemEvidenceRetentionTests/C1446_copies_and_verifies` | V-6 witness | **red**: 1 executed, 1 failed (`evidence_retention_unavailable`) | 1 | 7 |
| CP-8 | S4a-impl | `tests/Antiphon.Tests -> bin-c1446-s4a/` | s4a-green | `/*/*/FileSystemEvidenceRetentionTests/*` | V-6 | all listed, 0 failed/skipped | 4 | 8 |
| CP-9 | S4b-test | `tests/Antiphon.Tests -> bin-c1446-s4bred/` | s4b-red | `/*/*/WorktreeIgnoredContentClassifierTests/C1446_unlisted_antiphon_spill_is_evidence_not_protected` | V-7 witness | **red**: 1 executed, 1 failed (spill classified Protected) | 1 | 7 |
| CP-10 | S4b-impl | `tests/Antiphon.Tests -> bin-c1446-s4b/` | s4b-green | `/*/*/WorktreeIgnoredContentClassifierTests/*` | V-7 | all listed, 0 failed/skipped | 6 | 8 |
| CP-11 | S4c-test | `tests/Antiphon.Tests -> bin-c1446-s4cred/` | s4c-red | `/*/*/WorktreeResiduePublicationLaneTests/C1446_policy_rows` | V-8 witness | **red**: 4 executed, at least 2 failed (empty policy always returns `Queue`) | 4 | 7 |
| CP-12 | S4c-impl | `tests/Antiphon.Tests -> bin-c1446-s4c/` | s4c-green | `/*/*/(WorktreeResiduePublicationLaneTests/*)\|(WorktreeLandingCleanupRetryTests/C1446*)` | V-8, V-9, V-10 | all listed, 0 failed/skipped | 7 | 10 |
| CP-13 | S4c-impl | CP-12 | s4c-regress | `/*/*/(WorktreeLandingCleanupRetryTests/C459*)\|(WorktreeResidueSweepTests/*)\|(WorktreeResidueRecoveryTests/*)` | V-13 | all listed, 0 failed/skipped | 30 | 12 |
| CP-14 | S5-test | `tests/Antiphon.Tests -> bin-c1446-s5red/` | s5-red | `/*/*/WorktreeResidueCandidateRowTests/C1446_unchanged_held_rows_are_not_rewritten` | V-11 witness | **red**: 1 executed, 1 failed (second run writes N rows) | 1 | 7 |
| CP-15 | S5-impl | `tests/Antiphon.Tests -> bin-c1446-s5/` | s5-green | `/*/*/WorktreeResidueCandidateRowTests/*` | V-11, V-12 | all listed, 0 failed/skipped | 3 | 8 |
| CP-16 | S5-impl | CP-15 | s5-regress | `/*/*/(WorktreeResidueRegistrationTests/*)\|(WorktreeRetirementRaceTests/*)\|(CardWorktreeMirrorCleanupTests/*)` | V-13 | all listed, 0 failed/skipped | 20 | 12 |

Notes for Code: `Min` values for regress rows are
floors, not rosters; the TRX roster is the evidence. Every row is linux-only; no Windows row exists.

### Cost

Ordinary Code floor: sum of `EstimatedMinutes` = 126 minutes of builds and runs, plus authoring.
Suggested split if the orchestrator wants parallel Code tasks: {S1, S3}, {S2}, {S4a, S4b}, {S4c, S4d},
{S5}; S4c must land after S4a/S4b only for the activation step, not for its tests.

### Unlisted

None planned. The full `Antiphon.Tests` suite is not part of this card's profile. The conditional
`AntiphonRefProtection` patch (D-3) is reported as an unlisted run with that reason if it applies.

## Rollback

- S1: `WorktreeResidue:AbandonedLandingDays=0` disables the reconciler; already-refused rows stay
  refused (a `-Land` replaces them).
- S2: `Delegation:RunnerSyncDebtMaxAttempts=0` restores unbounded retries; a row already Held stays Held.
- S4a/S4b: re-register `RefusingEvidenceRetention` or override `WorktreeCleanup:RetainedIgnored`
  without `**/.antiphon/**`; retained copies are never deleted by code.
- S4c: `WorktreeResidue:MaxCleanupRetries=0` disables abandonment; `CleanupRetryCooldownDays=0`
  disables pacing. `Abandoned` rows are still explicitly retryable.
- S4d: `WorktreeResidue:Execute=false` returns to inventory-only.
- S5: `Retention:WorktreeResidueHeldRowRetentionDays=0` disables pruning; dedupe has no switch because
  it loses no verdict (counters and changed rows are kept).
