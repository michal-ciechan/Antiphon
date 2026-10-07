# CARD-1082 follow-ups: close the S1..S7 review disclosures as pins, small hardenings, docs, or not-needed

Date: 2026-10-07. Plan task: `3cd6c16d-cc73-4f20-8923-8b7e3f3430e3` (Frontier, Linux runner mirror).
Inspected source: `9975d163e34501e0fe66bf1f8486ef1a6413b4d2` (origin/master at planning time; the task
branch `feat/card-task-3cd6c16d` was fast-forwarded to it). Cards: CARD-1113, CARD-1115, CARD-1119,
CARD-1125, CARD-1132 (item 3), CARD-1133, CARD-1136, CARD-1128, all on the Antiphon board.
Status: Plan complete with the verification design folded in. **next: code**, slice by slice, each
Code task running its `### Checkpoints` rows as a closed list.

## Outcome and scope

The CARD-1082 chain is landed on master (S1 `b8f9caaf9`, S2 `3d6d7c450`, S3 `cff20cb13`, S4a
`e8582b604`, S4b `83939cfb3`, S5 `d45150be6` + `215c5f00b` + `3cb1a0aba` + `6ebb1bc73`, S6
`80db50749`, S7 `18ed47f48` + `5dc6b8371` + `1364e3814`; every reviewed SHA was rebased by the
land, so the review SHAs themselves are not ancestors of master). It activates with the next AppHost
restart. Eight disclosure cards remain. This plan reads each against master, decides its class
(test-only pin, small fail-closed or cost-reducing hardening, doc fix, or close-as-not-needed), and
groups the work into five slices ordered by safety value:

1. **F1** policy pins (CARD-1113 items 1-3, CARD-1119 item 2): the only reachable "Pending treated
   as a verdict without evidence" shape, plus three tripwires.
2. **F2** the Blocked lease-busy warning stops saying "pending" (CARD-1133), and a settlement-level
   pin that a Code report with no progress evidence stays Blocked (CARD-1113 item 2 at the seam).
3. **F3** Held sync debt is re-checked slowly and ends Superseded after retirement (CARD-1136 items 2
   and 3), so the attention row and the per-tick Held load are bounded.
4. **F4** the dispatcher pins a remote baseline only from a Present observation (CARD-1115), and a
   Program-level test proves both sync-debt sweeps are injected (CARD-1128).
5. **F5** test and doc pins: the `RemoveMirrorAsync` Pending fallback (CARD-1125), the CLI rendering
   through a stub detail (CARD-1136 item 4), and the chained-operator build-slot pitfall (CARD-1136
   item 1b).

Closed as not needed with evidence (D-6): CARD-1119 item 1 (shared claim rules) and CARD-1132 item 3
(composing the Pending workspace note). CARD-1136 item 1a is already a rule.

Constraints kept throughout: Pending is never Confirmed; settlement evidence is never rewritten; no
approval is minted; no dirty checkout is touched; every production change is fail-closed or
cost-reducing; no new per-tick statement on the desktop (counts stated per change); Land,
review-evidence binding and the CARD-1065 parking invariants are untouched (no card here is about
them).

Platform read on 2026-10-07: `GET /api/runner-defaults` has `globalRunnerId=server2`;
`GET /api/session-runners` lists desktop (windows, capacity 2), server2 (linux, capacity 10,
draining) and server2-temp (linux, capacity 10). Every slice and checkpoint runs on the Linux lane
with real Git and the isolated PostgreSQL fixture; no `-Runner` pin and no `-Platform` is needed.

## Ground truth

| Card assumption | Observed code (file:line at `9975d163e`) | Consequence |
|---|---|---|
| CARD-1113 (1): `Eligible` does not pin `FullRef` to the task's owned ref. | Confirmed. `SettlementSyncDebtPolicy.Eligible` checks switch, `Unavailable`, `LeaseBusy`, `GitObjectId.IsFull(RemoteSha)` only (`server/Application/Services/SettlementSyncDebtPolicy.cs:86-90`). The property holds by construction: `SyncCoreAsync` derives `fullRef` from `OwnedBranch(task.Id)` and refuses `BranchMismatch` otherwise (`server/Application/Services/RemoteWorkspaceService.cs:219-222`, `:155`). | Add the pin as a tripwire (D-1); it is cheap and fail-closed. |
| CARD-1113 (2): `BlockReason(Pending, Code, evidence: null)` returns null. | Confirmed (`SettlementSyncDebtPolicy.cs:45-51`). Reachable: `TryClassifyCompletedWithoutProgressAsync` returns null without writing evidence when `TaskCompletionProgressService` is missing from DI (`server/Application/Services/AgentTaskReplyService.cs:3499-3511`); `RemoteSyncBlockReason` then reads a null evidence (`:2049-2051`) and the task settles Succeeded with a debt row (`:1152-1178`). | A real fail-open under misconfiguration: block with the lease reason (D-1); pin at the policy and at settlement (V-2, V-6). |
| CARD-1113 (3): `PendingWarning`/`WorkspaceNote` accept any state. | Confirmed (`SettlementSyncDebtPolicy.cs:65-84`); the two call sites guard on Pending (`AgentTaskReplyService.cs:967-975`, `:1040-1043`). | Throw on a non-Pending result (D-1): a refactor that drops a guard fails loudly instead of printing "synced later". |
| CARD-1119 (1): the Pending arm duplicates the confirmed arm's claim rules. | Confirmed: `server/Application/Services/TaskCompletionProgressService.cs:187-234` (Pending) and `:244-280` (confirmed) repeat the claim/novelty switch with different inputs (`tip` vs `DesktopAfterSha`, no desktop-HEAD read under Pending). | A refactor with no fail-closed or cost value; close (D-6). Both copies are guarded (`RunnerCompletionProgressTests`, 11 results). |
| CARD-1119 (2): `BlockReason` returns the constant lease reason for Pending + Code + Indeterminate. | Confirmed (`SettlementSyncDebtPolicy.cs:47-49`); the Pending arm can yield `baseline_lineage_broken` or `primary_log_unavailable` (`TaskCompletionProgressService.cs:227-231`). Nothing keys on the string: parking and seat release never read `runner_sync_lease_busy` (grep of `TerminalRunnerSeatReleaseService.cs` and `BlockedTaskPark*.cs`: none). | Carry `evidence.Reason ?? LeaseBusy` (D-1); only the handoff and warning text change. |
| CARD-1125: the Pending -> `ObservedSha` fallback has no test. | Confirmed. `PublishedShaForRemoval` is `ConfirmedSha ?? (Pending ? ObservedSha : null) ?? WorktreeBaseSha` (`RemoteWorkspaceService.cs:802-808`); the guard sends it only to a `WorkspacePublishV1` runner (`:774-790`). Only `PhoneHomeRollingRunnerTests.Retirement_mirror_remove_uses_confirmed_sha_and_keeps_unpublished_tip_as_residue` exercises removal (`tests/Antiphon.Tests/Application/PhoneHomeRollingRunnerTests.cs:185-212`); `RemoteWorktreeMirrorTests` has a runner directory that throws on `Resolve` (`:291`). | Test-only, `RollingWorld` sibling with three arms (D-7). |
| CARD-1132 (3): the Pending workspace note replaces the merge-path note. | Confirmed (`AgentTaskReplyService.cs:1040-1043`). For every Pending result `MergeBackAsync` returns `branch X left for review` at its first gate before any merge command, because Pending is never Confirmed (`:2096-2098`; `RemoteSettlementSyncDtos.cs` `Confirmed`). The S7 event pins in V-20/V-21 (`18ed47f48`, `5dc6b8371`) went red under the S7 review's M4 probe. | Nothing to compose; close (D-6). |
| CARD-1133: Blocked lease-busy settlements record Pending with no debt row and warn "Runner sync pending ... then reply". | Confirmed. `Classify` runs in `PrepareRemoteAsync` before the block (`AgentTaskReplyService.cs:1998`); the warning is built from the state word (`:941-958`); the debt insert requires `remoteBlock is null` (`:1152-1156`); the owner doc even pins "The Blocked warning says Runner sync pending and then reply." (`docs/orchestration-loop.md:1190`, `tests/Antiphon.Tests/Application/RunnerBranchContractDocumentationTests.cs:88`). The bind-refusal recovery warning does not use the state word (`:2074-2083`). | Say "unavailable" for a Pending result that blocks; keep the evidence immutable; update the pinned sentence (D-2). |
| CARD-1136 (2): a Held debt never clears. | Confirmed. The claim read selects `Pending` with a due `NextAttemptAt` only (`server/Application/Services/SettlementSyncRecoveryService.cs:27-29`); Held is written with `NextAttemptAt = null` (`:70-71`, `:139-143`, `:167-175`); `RegistrationGoneAsync` runs only inside a claim (`:75`, `:193-198`); attention warns on every Held row (`server/Application/Services/SettlementSyncDebtAttention.cs:64-71`). Retirement rows are written by `CardWorktreeCleanupExecutor` (`:116-131`) and `TaskWorktreeRetirementService` (`:148-180`), both land-side. | Re-check Held rows slowly from the existing claim read (D-3); do not touch the land-side writers. |
| CARD-1136 (3): the attention loader has no `Take`. | Confirmed (`server/Application/Services/AttentionService.cs:3212-3222`); the CARD-1065 sibling loads every Held/Pending park the same way (`AttentionService.Leaks.cs:11-25`). | No `Take` (a cap hides debt); the bound comes from D-3. |
| CARD-1136 (4): CLI rendering pinned only as source text. | Confirmed (`RunnerBranchContractDocumentationTests.cs:69-73`); `scripts/delegate.ps1:741-763` prints the lines; `DelegateScriptLandStatusTests` runs the script against `LandApiStub` task-status JSON (`tests/Antiphon.Tests/Application/DelegateScriptLandStatusTests.cs:24-60`). | Add a stub-driven arm (D-7). |
| CARD-1136 (1): unleased chained exec; Code report omitted it. | Rule 2 of the checkpoint manifest already requires every unlisted driver to be reported with a reason (`docs/testing-and-build.md`, "Rules"). `build-slot.ps1` cannot see a shell operator that the shell consumed before it started. | (a) already a rule; (b) one doc sentence naming the pitfall, pinned (D-7). |
| CARD-1128: no Program-level proof that the dispatcher receives both sweep services. | Confirmed. Both are optional constructor parameters (`server/Application/Services/AgentTaskDispatcher.cs:193-194`); `RecoverBlockedTaskSyncAsync`/`RecoverSettlementSyncAsync` return 0 when absent (`:7431-7436`); `Program.cs:412-413` registers both scoped and `:470` the dispatcher; `DispatcherSweepLifetimeRegistrationTests` has one method (`tests/Antiphon.Tests/Application/DispatcherSweepLifetimeRegistrationTests.cs:22-33`). | Internal probe plus one factory test (D-5). |
| CARD-1115 (1): baseline remote pins happen whenever `Sha` is not null. | Confirmed (`AgentTaskDispatcher.cs:6742-6743` repair-remote, `:6747-6748` primary-remote). `ObserveExactRefCoreAsync` returns `Unavailable` + the advertised SHA on `repository_lease_busy` (`server/Infrastructure/Git/TaskProgressGit.cs:124-137`). The lease is a `FileShare.None` file lock, so the dispatcher's own held lease makes the nested acquire inside the observation fail (`server/Infrastructure/Git/RepositoryMutationLease.cs:24-60`): every capture of a non-local advertised tip under the claim is lease-busy. `PinBaselineAsync` then runs `check-ref-format`, a lease attempt, `show-ref` and a failing `update-ref` (`TaskProgressGit.cs:221-246`). The progress service already gates its own pins on Present (`TaskCompletionProgressService.cs:542-544`). | Gate both pins on `State == Present` (D-4): cost-reducing and fail-closed. |
| CARD-1115 (2): `remoteTip != source.Remote.Sha` compares without a state gate. | Confirmed at `TaskCompletionProgressService.cs:705-706` and `:878`; `:943` already gates on `Present`. After a lease-busy baseline an unmoved remote compares equal and the evaluation falls through to the local claim check or `no_movement` (fail-closed). | Pin it (V-12); no production change. |
| CARD-1115 (3): is `ProgressRemoteBaseline.Sha` with `State = Unavailable` a contract? | The record has no doc (`server/Application/Dtos/TaskProgressDtos.cs:56-60`). | Document: "advertised on origin, not local; readers gate on State" (D-4). |
| Sweep cost today. | One indexed read per tick on `(State, NextAttemptAt)`; an empty table is one statement (`SettlementSyncRecoveryService.cs:27-29`; `SettlementSyncRecoveryTests.C1082_DueDebtFastForwardsDesktopAndMarksReady`). Attention: one statement, early return on empty (`AttentionService.cs:3214-3218`). | D-3 keeps one claim statement per tick; the Held re-check is folded into it. |
| Existing rosters at `9975d163e`. | `SettlementSyncDebtPolicyTests` 21 results (8 methods), `RunnerTaskSettlementTests` 31 (27), `SettlementSyncRecoveryTests` 10 (7), `RepairSourceDispatchTests` 29 (14), `PhoneHomeRollingRunnerTests` 4 (4), `DelegateScriptLandStatusTests` 19 (3), `DispatcherSweepLifetimeRegistrationTests` 1, `RunnerBranchContractDocumentationTests` 5, `CheckpointManifestDocumentationTests` 7, `RunnerCompletionProgressTests` 11, `ReviewEvidenceResettlementTests` 10, `TerminalRunnerSeatReleaseTests` 39, `BlockedTaskSyncRecoveryTests` 2, `BlockedTaskParkDeliveryTests` 4. | The Checkpoints table counts from these. |

## Card dispositions

| Card | Still true on master? | Class | Slice |
|---|---|---|---|
| CARD-1113 | Yes (three items) | Small production hardening (policy) + pins; item 2 also pinned at settlement | F1, F2 |
| CARD-1115 | Yes (three items) | Cost-reducing hardening (two pin gates) + pins + record doc comment | F4 |
| CARD-1119 | Item 1 yes; item 2 yes | Item 2: production hardening in the policy. Item 1: **close as not needed** (D-6) | F1 |
| CARD-1125 | Yes | Test-only | F5 |
| CARD-1132 (item 3) | Yes, but unobservable | **Close as not needed** (D-6); items 1-2 landed in S7 | none |
| CARD-1133 | Yes | Small production text hardening + doc + pin | F2 |
| CARD-1136 | Items 2, 3, 4 yes; item 1a already a rule; item 1b doc only | Items 2-3: production hardening (sweep); item 4: test-only; item 1b: doc fix | F3, F5 |
| CARD-1128 | Yes | Test + internal read-only probe | F4 |

## Decisions

### D-1. Policy pins are additive fail-closed predicates in `SettlementSyncDebtPolicy`

- `Eligible` additionally requires `result.FullRef == "refs/heads/" + RemoteWorkspaceService.OwnedBranch(task.Id)`
  (ordinal). Production always satisfies it; a foreign, tag or null ref is never Pending.
- `BlockReason` Pending arm for `Role == Code`: `evidence is null` returns `runner_sync_lease_busy`;
  `Indeterminate` returns `evidence.Reason ?? runner_sync_lease_busy`; anything else null. Non-Code
  roles stay null. The non-Pending arms are byte-for-byte today's.
- `PendingWarning` and `WorkspaceNote` throw `ArgumentException` when `result.State != Pending`.
  Throwing is the fail-closed choice: a settlement that would print "synced later" for a non-Pending
  result fails instead of lying to the caller.

Rejected: writing a fallback evidence in `TryClassifyCompletedWithoutProgressAsync` when the
progress service is missing (touches the classifier for a DI-misconfiguration case; CARD-1082 D-2
makes the policy the single decision point); returning `InspectionUnavailable` for null evidence
(the lease reason is the true cause and the Decide handoff already names it).

### D-2. A Blocked settlement's warning never says "pending"; the evidence stays Pending

In `AgentTaskReplyService.SettleAsync` the warning's state word is `"unavailable"` when
`sync.State == Pending` (the Pending classification of a lease-busy result whose debt was not
accepted), otherwise today's lower-cased state. The handoff text ("Runner sync blocked (...)") is
unchanged. The settlement evidence keeps `State = Pending` (written once, CARD-1082 D-2;
CARD-0657 D-4 immutability); the documented meaning "Pending evidence without a debt row means the
task stayed Blocked and a reply is required" is already pinned and stays. The owner-doc sentence
becomes "A Blocked lease-busy settlement's warning says Runner sync unavailable and then reply,
never Runner sync pending; its evidence still records Pending."

Rejected: re-classifying the evidence to Unavailable after the block decision (a second evidence
write inside one settlement; breaks "one state for progress, block, evidence and debt"); making
`Classify` consult the role or progress (it runs before the progress read by design); changing the
bind-refusal recovery text (it does not use the state word).

### D-3. Held debt is re-checked slowly for its registration only, by the existing claim read

- A Held row carries `NextAttemptAt = now + HeldRecheckMinutes` (internal constant 60) instead of
  null. Ready and Superseded keep null.
- The claim read selects `(State == Pending && NextAttemptAt <= now) || (State == Held &&
  (NextAttemptAt == null || NextAttemptAt <= now))`, ordered as today, `Take(32)`. Same index
  `(State, NextAttemptAt)`, one statement per tick: a null `NextAttemptAt` on a Held row (rows
  written before this change) means due now, so no migration.
- `ClaimAsync` for a Held snapshot: lock the task row, re-read, and either `TerminalAsync(Superseded,
  settlement_sync_superseded_by_retirement)` when `RegistrationGoneAsync` (retirement row for the
  attempt, or the worktree directory gone) or reschedule `NextAttemptAt += 60 min` with `Revision + 1`
  and `UpdatedAt`. Never `SyncSettledAsync`, never Git, never `Attempts++`, never a verdict, never the
  task row. `SweepAsync` does not count it as attempted (returns 0 for it, like the episode-changed
  arm). `TerminalAsync`'s CAS compares the snapshot's state, not the constant Pending.
- `SaveAsync`'s Held arm and the episode-changed Held arm set the re-check time. Attention is
  unchanged: Held warns until the row is Superseded, Superseded warns nothing.
- Contract change, stated here so Code does not treat it as weakening: the three assertions
  `NextAttemptAt.ShouldBeNull()` on Held rows (`SettlementSyncRecoveryTests.cs:109`, `:243`, and the
  V-18 attempt arm) become `ShouldBe(now + 60 min)`.
- Cost: a Held row costs one claim transaction per hour (lock, two reads, one update); the per-tick
  statement count is unchanged (one read on an empty or not-due table). Item 3 (no `Take` on the
  attention load) is answered by this bound; a `Take` would hide debt.

Rejected: marking Held rows Superseded from `CardWorktreeCleanupExecutor` or
`TaskWorktreeRetirementService` at retirement time (a write on the land/cleanup path; outside this
bundle's cards and the brief's "Land untouched"); an operator clear endpoint (new API surface and
the row would still be loaded); a second per-tick query for Held rows (new per-tick statement on the
desktop); re-trying the fast-forward for Held rows (Held means a refusal that needs a human; the
sweep must not loop on dirt or divergence).

### F3b. Held supersession is fail-closed (CARD-1136 repair)

D-3's Held arm no longer treats "any retirement row, or the directory missing" as gone. That
predicate superseded a live Held debt after `RevokeAsync` (the row stays, `Active=false`,
`State=Revoked`, directory lives) and after a temporary path absence. Pending fast-forward still
uses `RegistrationGoneAsync` so a retired checkout is not fast-forwarded. The Held re-check uses
`HeldRegistrationPermanentlyGoneAsync` and does not run Git.

Permanently gone, after F3c, means all of the following, read inside the claim: exactly one active
`WorktreeRetirementState.Complete` retirement for the debt's task and attempt; its `WorktreePath`
equals the debt's recorded path; `DirectoryRemovedAt`, `RegistrationRemovedAt` and
`RetirementCompletedAt` are all set at or before the re-check instant and `RetirementCompletedAt`
is not before the debt row's `CreatedAt`; `Directory.Exists` on that path is false; the retirement's
recorded `GitDirectory` is absent; and no current `worktrees/*/gitdir` names the debt path. A
retirement of an earlier incarnation, a registration recreated at the same path, a revoked or
inactive retirement, a Released/Partial/Refused retirement, a Complete retirement whose directory
or git directory is still present, a blank or missing or unreadable path, a lease-busy or
runner-unavailable observation (this check never asks), and any exception from the read stay Held
with `NextAttemptAt` moved forward 60 minutes. Superseded is never written on doubt. The same
SELECT also reads `GitDirectory` and `CommonDirectory`, so the due nonblank Held path stays 6
statements. A blank path still returns before that query. Negative controls:
`C1136_RevokedRetirementKeepsHeldDebtAndReschedules` (live, absent),
`C1136_TemporaryWorktreeAbsenceKeepsHeldDebtAndReschedules`,
`C1136_UncertainHeldRegistrationKeepsHeldDebtAndReschedules` (released-absent, complete-present,
blank-path), `C1136_HeldRecheckReadFailureKeepsHeldDebtAndReschedules`,
`C1136_RecreatedRegistrationKeepsHeldDebtAndReschedules`. The positive V-8 row is one
result: Complete removal of the current registration, including its recorded git directory.
CP-4 and CP-12 rosters are 20. The Held re-check does not enqueue a caller note. The lease-busy caller notes are proved by `PendingSettlementDeliveryTests.C1082_LeaseBusySettlementNoteIsAcceptedComplete`: the profiled Succeeded Pending note and the non-profiled Blocked note, each accepted once for an eligible recipient, a busy recipient, and a crash between enqueue and confirmation.

### F3d. The Held re-check does not supersede

Three reviews each found a new way for a filesystem or path match to supersede a live Held debt. `AgentTaskSyncDebts` has no registration id, and `TaskWorktreeRetirement` has none either. Treating any Complete retirement of the same task attempt as identity would repeat the same-path recreation defect, and adding a column would be a migration. F3d drops supersession from the Held re-check. The re-check only restamps `NextAttemptAt` 60 minutes ahead: no retirement SELECT, no filesystem read, no Git, no fast-forward, and never Ready. An empty table and a not-due Held row stay 1 statement. A due Held re-check is 5 statements: the sweep select, the snapshot, the task lock, the re-read, and the reschedule. Pending recovery, including `RegistrationGoneAsync`, is unchanged. CARD-1136 item 3 stays open: a Held attention row remains for the life of the debt, because the S4b sweep's Ready and Superseded rules apply to Pending rows only. CARD-1148 owns the registration-identity binding. The closed F3d rows are `docs/investigations/2026-10-07-card-1082-f3d-rows.md`.

### D-4. Baseline remote pins require a Present observation; the record documents its `Sha`

`CaptureProgressBaselineAsync` pins `repair-remote` and `primary-remote` only when the observation
is `{ State: Present, Sha: not null }`, the same predicate `TaskCompletionProgressService` already
uses for its own pins. `ProgressRemoteBaseline` gains an XML doc: `Sha` with `State != Present` is
the advertised origin tip, not a local object; readers gate on `State` before pinning, comparing or
attributing against it. No change to `TaskCompletionProgressService`'s compares; V-12 pins their
fail-closed direction.

Rejected: pinning after a `cat-file -e` probe (one more child per capture); dropping `Sha` from
lease-busy observations (CARD-1082 D-3 needs it for settlement).

### D-5. The dispatcher exposes an internal read-only probe; a Program-level test resolves it

`AgentTaskDispatcher` gains `internal (bool BlockedTaskSync, bool SettlementSync) SyncDebtSweepsWired
=> (_blockedTaskSync is not null, _settlementSync is not null);` (no behaviour). A second method in
`DispatcherSweepLifetimeRegistrationTests` resolves `AgentTaskDispatcher` from a factory scope and
asserts both true, and resolves both services from the same scope.

Rejected: reflection on the private fields (brittle; a rename passes silently as a null field);
seeding a debt row into the factory's shared database and sweeping (a write to the shared host, and
the return value is 0 whether or not the service is present).

### D-6. Close CARD-1119 item 1 and CARD-1132 item 3 as not needed

- CARD-1119 item 1: the two arms are deliberately different (no desktop-HEAD read under Pending; a
  different `LocalObserved`), the S3 review enumerated the Pending arm as fail-closed in every
  combination, and both arms are guarded by `RunnerCompletionProgressTests` (R-7 of CARD-1082). A
  shared local function would be a behaviour-neutral refactor with regression risk and no
  fail-closed or cost value, which the brief excludes.
- CARD-1132 item 3: for a Pending result `MergeBackAsync` returns before any merge command
  (`AgentTaskReplyService.cs:2096-2098`), so there is no merge outcome string to hide; the only way
  one could appear is the PC-10 defect, which the landed V-20/V-21 event pins detect (S7 review
  probes M4 and M4+M5 went red). Composing the note would change caller-visible text for no
  observable case.

### D-7. Test-only and doc pins

- CARD-1125: one `[Arguments]` method in `PhoneHomeRollingRunnerTests` on `RollingWorld`, three
  arms, asserting the `PublishedSha` the scripted peer received.
- CARD-1136 item 4: one `[Arguments]` method in `DelegateScriptLandStatusTests` with a stub detail
  carrying `progressEvidence.remoteSync` and `syncDebt`, three arms, asserting the exact lines.
- CARD-1136 item 1b: one sentence in the Build slots section of `docs/testing-and-build.md`, pinned
  by a new method in `CheckpointManifestDocumentationTests`.
- The measured statement counts (30/40/31) stay doc text; the tests keep asserting `debtSql` only.
  An exact-total assertion would pin unrelated statement changes; the review re-measures them.

Rejected: making `PublishedShaForRemoval` internal for a unit test (the `RollingWorld` exercises
the real guard wire and the `WorkspacePublishV1` feature gate).

### D-8. Keep fleet placement dynamic

No `-Runner` or `-Platform` in any brief for this bundle; every row is Linux-capable.

### D-9. Activation per slice

F1, F2, F3 and F4 change server code and activate at the AppHost restart that follows their land
(`GET /api/version` must show the landed SHA). F5 needs no restart: tests, docs and `delegate.ps1`
(read at invocation). No setting changes; no migration.

## Implementation slices

Each slice is one 30-60 minute Code task. F1 and F2 touch different files; F2 and F4 both touch
`AgentTaskReplyService.cs`/`AgentTaskDispatcher.cs` only once each, so the order F1 -> F2 -> F3 ->
F4 -> F5 is by safety value, not by file conflict; F3 and F4 may run in parallel after F2. Commit
and push each slice before its checkpoint group; never rebase or force-push the task branch.

| Slice | Budget | Files | Deliverable and named tests | Restart |
|---|---|---|---|---|
| F1: policy pins (CARD-1113 1-3, CARD-1119 2) | 45 min | `server/Application/Services/SettlementSyncDebtPolicy.cs`; `tests/Antiphon.Tests/Application/SettlementSyncDebtPolicyTests.cs` | Fixture: `Task()` gets `Id = Guid.Parse("abcd1234-0000-0000-0000-000000000000")` so `OwnedBranch` equals the existing `Branch` constant. New `C1113_ForeignFullRefIsNeverPending` (3 arguments: another task's ref, `refs/tags/...`, null), `C1113_PendingCodeWithoutEvidenceBlocksWithLeaseReason` (2: Code, Review), `C1119_PendingCodeIndeterminateCarriesEvaluatedReason` (2: reason present, reason null), `C1113_PendingTextHelpersRefuseANonPendingResult` (2: Unavailable, Synchronized). Existing `C1082_BlockReasonForPendingDependsOnRoleAndProgress` row 4 expects `"progress_read_other"` (the evidence reason) instead of the constant. Roster 30. | yes |
| F2: Blocked warning honesty (CARD-1133) and the settlement-level null-evidence pin (CARD-1113 2) | 40 min | `server/Application/Services/AgentTaskReplyService.cs` (one expression at `:945`); `docs/orchestration-loop.md:1190`; `tests/Antiphon.Tests/Application/RunnerTaskSettlementTests.cs`; `tests/Antiphon.Tests/Application/RunnerBranchContractDocumentationTests.cs`; `tests/Antiphon.Tests/TestHelpers/RunnerSettlementWorld.cs` (only if a `progressService: false` knob is needed) | `Sync_uncertainty_blocks_and_reply_retries` gains: Warning contains `"Runner sync unavailable: runner_sync_lease_busy"` and `"then reply"`, no event contains `"Runner sync pending"`, the caller note contains `"Runner sync unavailable"` and not `"synced later"`. New `C1113_CodeLeaseBusyWithoutProgressServiceStaysBlocked`: a Code report under the held lease with `TaskCompletionProgressService` absent from the world's DI settles Blocked/Decide with the lease reason, no debt row, no `"synced later"`, HEAD at the baseline. `LoopSentences` swaps the "Runner sync pending and then reply" sentence for the D-2 sentence and adds "a Code report with no progress evidence under Pending stays Blocked". | yes |
| F3: Held debt lifecycle (CARD-1136 2-3) | 60 min | `server/Application/Services/SettlementSyncRecoveryService.cs`; `tests/Antiphon.Tests/Application/SettlementSyncRecoveryTests.cs`; `docs/orchestration-loop.md`, `docs/ops-http.md`, `docs/session-runtime-invariants.md` (one Held sentence each); `tests/Antiphon.Tests/Application/RunnerBranchContractDocumentationTests.cs` | New `C1136_HeldDebtEndsSupersededOnceTheWorktreeIsRetired` (2 arguments: retirement-row, directory-gone): seed, advance origin so the first sweep ends Held `runner_sync_tip_not_reported`; retire; a sweep before 60 minutes returns 0 and leaves Held; advance `DebtClock` 60 minutes; sweep returns 0, row Superseded with `settlement_sync_superseded_by_retirement`, `Attempts` unchanged, no Git command recorded, HEAD unchanged, `SettlementSyncDebtAttention.Build` on the row is empty. New `C1136_HeldDebtWithALiveWorktreeStaysHeldAndReschedules`: after 60 minutes with the worktree registered the row stays Held, `NextAttemptAt` moves forward 60 minutes, `Revision` advanced, no Git, sweep returns 0, attention still one Warning. Existing V-14 gains: a table holding only a not-due Held row is still one statement per tick. V-15, V-17, V-18 Held assertions become `NextAttemptAt == now + 60 min`. Roster 13. | yes |
| F4: baseline remote pin gate (CARD-1115) and sweep wiring guard (CARD-1128) | 60 min | `server/Application/Services/AgentTaskDispatcher.cs` (`:6742-6748` gates; internal probe); `server/Application/Dtos/TaskProgressDtos.cs` (doc comment); `tests/Antiphon.Tests/Application/RepairSourceDispatchTests.cs`; `tests/Antiphon.Tests/Application/DispatcherSweepLifetimeRegistrationTests.cs` | New `C1115_LeaseBusyBaselineRecordsTheAdvertisedTipWithoutARemotePin`: `world.Git.BeforeCommand` fails every `cat-file -e` during the dispatch tick, so the repair-source observation inside the held claim lease is `repository_lease_busy` with the advertised owner tip; assert `baseline.RepairSource.Remote` is `Unavailable`/`repository_lease_busy`/`Sha == OwnerSha`, `ProgressPins()` contains `baseline-repair-local` and `baseline-primary-local` and no `*-remote`, `Trace` has no `update-ref` naming `baseline-repair-remote`; then a second world without the hook dispatches with `Present` and the `baseline-repair-remote` pin present (the negative control in the same method). New `C1115_LeaseBusyBaselineUnmovedRemoteIsNoMovementAndMovedRemoteQualifies`: after such a baseline, settle a report with no commits and no claim: evidence `NoAttributedProgress`/`no_movement`; a second world pushes one more owner commit from the second clone and reports its claim: `ProgressObserved` through the remote-origin arm. New `Program_wires_both_sync_debt_sweeps_into_the_dispatcher` (D-5). If the primary branch can be pre-created on origin for the task id, assert the `primary-remote` arm in the first method too; otherwise report it as covered by reading and the repair-remote PC. | yes |
| F5: test and doc pins (CARD-1125, CARD-1136 1b and 4) | 45 min | `tests/Antiphon.Tests/Application/PhoneHomeRollingRunnerTests.cs`; `tests/Antiphon.Tests/Application/DelegateScriptLandStatusTests.cs`; `docs/testing-and-build.md` (Build slots); `tests/Antiphon.Tests/Application/CheckpointManifestDocumentationTests.cs` | New `C1125_RetirementMirrorRemovePublishesTheObservedTipOnlyForPendingEvidence` (3 arguments: pending-observed sends `ObservedSha`; unavailable-observed, a lease-busy `Unavailable` evidence carrying `ObservedSha`, sends `WorktreeBaseSha`; pending-confirmed sends `ConfirmedSha`), the peer answers removed, residue null. New `C1136_StatusPrintsSyncDebtAndPendingLines` (3 arguments: pending-with-debt prints `Runner sync: Pending runner_sync_lease_busy; origin=<S>` and `Desktop sync debt: Pending source=<S> confirmed= reason=runner_sync_lease_busy attempts=2`; pending-without-debt prints the origin line and the "Pending evidence without a debt row" sentence and no debt line; synchronized prints `Runner sync: Synchronized` and neither). Doc sentence: "Never chain a second driver after the gated command with a shell operator: the lease ends when build-slot.ps1 returns, so `-- dotnet build ... && dotnet exec ...` runs the exec unleased and Review flags it." New `CheckpointManifestDocumentationTests.the_chained_operator_pitfall_is_documented`. | no |

After F5 the last Code task (or Review) runs CP-11..CP-15 at the final committed SHA so the whole
closed list has one certificate.

## Verification design

### Inspection

| Read | Why |
|---|---|
| `SettlementSyncDebtPolicy.cs` (entire), `SettlementSyncDebtPolicyTests.cs` (entire) | F1's seam and fixture. |
| `AgentTaskReplyService.cs:850-1060` (settle, warnings, note), `:1140-1185` (debt insert), `:1960-2100` (prepare, block reason, recovery, merge gate), `:3455-3540` (no-progress classifier) | F2's one-line change and why null evidence is reachable. |
| `SettlementSyncRecoveryService.cs` (entire), `SettlementSyncRecoveryTests.cs:30-140, 205-335, 430-520`, `SettlementSyncDebtAttention.cs`, `AttentionService.cs:3205-3222` | F3's claim/save discipline, the assertions that change, the attention bound. |
| `AgentTaskDispatcher.cs:160-215` (constructor), `:4660-4690` (claim lease), `:4975-5000` (capture call), `:6709-6760` (capture), `:7425-7440` (sweep wrappers); `TaskProgressGit.cs:68-160, 221-246`; `RepositoryMutationLease.cs:18-60`; `RepairSourceWorld.cs`, `ControlledTaskProgressGit.cs`, `RepairSourceDispatchTests.cs:20-60, 240-270`; `DispatcherSweepLifetimeRegistrationTests.cs`, `AntiphonWebAppFactory.cs:1-40` | F4's gates, why the nested acquire is busy, the test doubles and the factory. |
| `RemoteWorkspaceService.cs:770-810`, `PhoneHomeRollingRunnerTests.cs:180-215, 475-500`; `scripts/delegate.ps1:735-770`, `DelegateScriptLandStatusTests.cs:1-95`; `docs/testing-and-build.md` Build slots, `CheckpointManifestDocumentationTests.cs` | F5's fixtures and doc pin homes. |

### Proves it works now

| V | Behaviour | Test (class-qualified) |
|---|---|---|
| V-1 | `Classify` turns a lease-busy result on the task's own owned ref into Pending (fixture id matches the ref). | `SettlementSyncDebtPolicyTests.C1082_LeaseBusyWithObservedTipClassifiesPending` |
| V-2 | `BlockReason(Pending, Code, null)` is `runner_sync_lease_busy`; `(Pending, Review, null)` is null. | `SettlementSyncDebtPolicyTests.C1113_PendingCodeWithoutEvidenceBlocksWithLeaseReason` (2) |
| V-3 | `BlockReason(Pending, Code, Indeterminate reason X)` is X; a null reason falls back to the lease reason. | `SettlementSyncDebtPolicyTests.C1119_PendingCodeIndeterminateCarriesEvaluatedReason` (2); `C1082_BlockReasonForPendingDependsOnRoleAndProgress` row 4 |
| V-4 | `PendingWarning` and `WorkspaceNote` throw for a non-Pending result. | `SettlementSyncDebtPolicyTests.C1113_PendingTextHelpersRefuseANonPendingResult` (2) |
| V-5 | A Blocked lease-busy Code settlement warns "Runner sync unavailable: runner_sync_lease_busy ... then reply", never "Runner sync pending"; evidence Pending, no debt, HEAD at baseline, reply path unchanged. | `RunnerTaskSettlementTests.Sync_uncertainty_blocks_and_reply_retries` |
| V-6 | A Code lease-busy settlement with no progress service stays Blocked/Decide with the lease reason and no debt row. | `RunnerTaskSettlementTests.C1113_CodeLeaseBusyWithoutProgressServiceStaysBlocked` |
| V-7 | The owner docs carry the D-2 sentence, the Held re-check sentence and the null-evidence sentence. | `RunnerBranchContractDocumentationTests.C1082_settlement_sync_debt_is_documented` |
| V-8 | A Held re-check never supersedes. A Complete retirement, a recreated path, a revoked retirement, a temporary absence, and an unreadable, empty, symlinked, or moved registration all stay Held. The Held claim contains no filesystem or retirement read. | `SettlementSyncRecoveryTests.C1136_CompleteRetirementWithoutRegistrationIdKeepsHeldDebt` |
| V-9 | A Held debt with a live registration stays Held, reschedules 60 minutes, runs no Git, keeps `Attempts`, still warns. | `SettlementSyncRecoveryTests.C1136_HeldDebtWithALiveWorktreeStaysHeldAndReschedules` |
| V-10 | Held rows carry `NextAttemptAt = now + 60 min`; an empty table and a not-due Held row are one statement per tick. | `SettlementSyncRecoveryTests.C1082_AdvancedRemoteTipIsHeldNotFollowed`, `C1082_DesktopRefusalsHoldWithReason` (3), `C1082_ChangedEpisodeOrRetiredWorktreeEndsTheDebt` (2), `C1082_DueDebtFastForwardsDesktopAndMarksReady` |
| V-11 | A lease-busy baseline observation records the advertised tip with `State = Unavailable` and issues no remote pin; a Present observation still pins. | `RepairSourceDispatchTests.C1115_LeaseBusyBaselineRecordsTheAdvertisedTipWithoutARemotePin` |
| V-12 | After a lease-busy baseline, an unmoved remote evaluates `no_movement`; a moved remote with a claim is `ProgressObserved` through the remote arm. | `RepairSourceDispatchTests.C1115_LeaseBusyBaselineUnmovedRemoteIsNoMovementAndMovedRemoteQualifies` |
| V-13 | The booted Program's dispatcher has both sync-debt sweep services injected. | `DispatcherSweepLifetimeRegistrationTests.Program_wires_both_sync_debt_sweeps_into_the_dispatcher` |
| V-14 | `RemoveMirrorAsync` publishes `ObservedSha` only for Pending evidence without `ConfirmedSha`; a lease-busy Unavailable evidence falls back to `WorktreeBaseSha`; `ConfirmedSha` wins. | `PhoneHomeRollingRunnerTests.C1125_RetirementMirrorRemovePublishesTheObservedTipOnlyForPendingEvidence` (3) |
| V-15 | `delegate.ps1 -Status` prints the Pending origin line, the debt line, and the no-debt sentence from a stub detail. | `DelegateScriptLandStatusTests.C1136_StatusPrintsSyncDebtAndPendingLines` (3) |
| V-16 | `docs/testing-and-build.md` names the chained-operator pitfall. | `CheckpointManifestDocumentationTests.the_chained_operator_pitfall_is_documented` |

### Guards the regression (negative controls)

| R | Negative control (for the fail-closed claim it guards) | Test |
|---|---|---|
| R-1 | A lease-busy result on another task's ref, a tag ref, or a null ref is never Pending (D-1 FullRef pin). | `SettlementSyncDebtPolicyTests.C1113_ForeignFullRefIsNeverPending` (3) |
| R-2 | Non-lease reasons, Refused states, the kill switch, and short or missing tips are unchanged by F1. | existing `C1082_OtherReasonsAndStatesAreNeverPending` (7), `C1082_MissingOrShortObservedTipIsNeverPending` (2), `C1082_DisabledSettingLeavesLeaseBusyUnavailable` |
| R-3 | Succeeded Pending vocabulary and the kill-switch note are unchanged by F2 ("synced later", `desktop-sync=pending`, "Runner sync unavailable" with the switch off). | existing `RunnerTaskSettlementTests.C1082_ReviewLeaseBusySettlesSucceededWithPendingSyncDebt`, `C1082_CodeLeaseBusyWithLocalObjectsSettlesSucceededPending`, `C1082_DisabledSettingKeepsLeaseBusyBlocked` |
| R-4 | Ready and Superseded rows are never due again (D-3 touches Held only). | existing `SettlementSyncRecoveryTests.C1082_DueDebtFastForwardsDesktopAndMarksReady` ("Ready is not due again"), V-18 retired arm |
| R-5 | The sweep still fast-forwards only to the recorded source and leaves settlement evidence immutable (G-8/G-9 of CARD-1082). | existing `C1082_AdvancedRemoteTipIsHeldNotFollowed`, `C1082_ReadyDebtLeavesSettlementEvidenceAndOutcomesImmutable` |
| R-6 | A remote observation that is Unavailable with no SHA still dispatches with an Unavailable baseline and no pin. | existing `RepairSourceDispatchTests.C499_V07b_RemoteFailureStillDispatchesWithAnUnavailableBaseline` |
| R-7 | The `ConfirmedSha` removal arm and the unpublished-tip residue are unchanged. | existing `PhoneHomeRollingRunnerTests.Retirement_mirror_remove_uses_confirmed_sha_and_keeps_unpublished_tip_as_residue` |
| R-8 | Whole settlement and sweep classes stay green at the final SHA. | whole `RunnerTaskSettlementTests`, whole `SettlementSyncRecoveryTests` |
| R-9 | Dispatch, mirror and DI classes stay green at the final SHA. | whole `RepairSourceDispatchTests`, `PhoneHomeRollingRunnerTests`, `DispatcherSweepLifetimeRegistrationTests` |
| R-10 | Unit, CLI, doc and progress classes stay green; the Pending and confirmed arms are unchanged (CARD-1119 item 1 closed). | whole `SettlementSyncDebtPolicyTests`, `DelegateScriptLandStatusTests`, `RunnerBranchContractDocumentationTests`, `CheckpointManifestDocumentationTests`, `RunnerCompletionProgressTests` |
| R-11 | CARD-1043 resettlement, CARD-1065 parking and seat-release ledgers are untouched (the warning site is shared with every runner block). | whole `ReviewEvidenceResettlementTests`, `TerminalRunnerSeatReleaseTests`, `BlockedTaskSyncRecoveryTests`, `BlockedTaskParkDeliveryTests` |

### Guard inventory

| G | Decision: guard | PC |
|---|---|---|
| G-1 | D-1: only the task's own owned ref classifies Pending | PC-1 |
| G-2 | D-1: a Code report with no progress evidence blocks under Pending | PC-2 |
| G-3 | D-1: the evaluated reason reaches the block | PC-3 |
| G-4 | D-1: the Pending text helpers refuse a non-Pending result | PC-4 |
| G-5 | D-2: a Blocked lease-busy warning never says "pending" | PC-5 |
| G-6 | D-3 / F3d: a Held re-check does not supersede and does not read the filesystem | PC-6 |
| G-7 | D-3: a Held re-check never runs Git or counts an attempt | PC-7 |
| G-8 | D-4: a remote baseline pin needs a Present observation | PC-8 |
| G-9 | D-5: the production dispatcher receives both sweep services | PC-9 |
| G-10 | CARD-1082 D-6: only Pending evidence publishes `ObservedSha` for removal | PC-10 |
| G-11 | CARD-1082 D-7: the CLI prints the observed origin for Pending | PC-11 |

### Positive controls

Mutation runs break/red/restore/green after land; Code runs ordinary V/R; Review judges guard
independence. Each row is one compiling production defect applied singly; use the exact method
filter, never the class. Zero executions, a build or fixture failure, or a timeout is not red.
Restore source and rebuild before the green run. Controls sharing a file run sequentially.

| PC | Break guard by compiling defect | Exact detecting filter | Expected red assertion |
|---|---|---|---|
| PC-1 | G-1: drop the `FullRef` predicate from `Eligible`. | `/*/*/SettlementSyncDebtPolicyTests/C1113_ForeignFullRefIsNeverPending` | `G-1`: the foreign-ref result is classified Pending instead of returned unchanged. |
| PC-2 | G-2: the Pending arm returns null for null evidence. | `/*/*/SettlementSyncDebtPolicyTests/C1113_PendingCodeWithoutEvidenceBlocksWithLeaseReason`; `/*/*/RunnerTaskSettlementTests/C1113_CodeLeaseBusyWithoutProgressServiceStaysBlocked` | `G-2`: the Code arm returns null; the settlement is Succeeded with a debt row. |
| PC-3 | G-3: the Indeterminate arm returns the constant lease reason. | `/*/*/SettlementSyncDebtPolicyTests/C1119_PendingCodeIndeterminateCarriesEvaluatedReason` | `G-3`: the reason is `runner_sync_lease_busy` instead of the evidence reason. |
| PC-4 | G-4: remove the state guard from `PendingWarning` and `WorkspaceNote`. | `/*/*/SettlementSyncDebtPolicyTests/C1113_PendingTextHelpersRefuseANonPendingResult` | `G-4`: no `ArgumentException`; a "Runner sync pending" line is returned for an Unavailable result. |
| PC-5 | G-5: use the lower-cased state word for a Pending result that blocks. | `/*/*/RunnerTaskSettlementTests/Sync_uncertainty_blocks_and_reply_retries` | `G-5`: a Warning event contains "Runner sync pending". |
| PC-6 | G-6: the Held re-check supersedes when the recorded path is absent; separately, the claim read selects Pending only. | `/*/*/SettlementSyncRecoveryTests/C1136_HeldDebtWithALiveWorktreeStaysHeldAndReschedules`; `/*/*/SettlementSyncRecoveryTests/C1136_CompleteRetirementWithoutRegistrationIdKeepsHeldDebt`; `/*/*/SettlementSyncRecoveryTests/C1136_TemporaryWorktreeAbsenceKeepsHeldDebtAndReschedules` | `G-6`: a missing path becomes Superseded; a Pending-only claim leaves the due Held row at the old NextAttemptAt. |
| PC-7 | G-7: the Held re-check calls `SyncSettledAsync` and increments `Attempts`. | `/*/*/SettlementSyncRecoveryTests/C1136_HeldDebtWithALiveWorktreeStaysHeldAndReschedules` | `G-7`: a Git command is recorded and `Attempts` moves. |
| PC-8 | G-8: pin `repair-remote` whenever `Sha` is not null. | `/*/*/RepairSourceDispatchTests/C1115_LeaseBusyBaselineRecordsTheAdvertisedTipWithoutARemotePin` | `G-8`: `ProgressPins()` contains `baseline-repair-remote` and `Trace` has its `update-ref`. |
| PC-9 | G-9: register `AgentTaskDispatcher` in `Program.cs` through a factory lambda that passes no `settlementSync`. | `/*/*/DispatcherSweepLifetimeRegistrationTests/Program_wires_both_sync_debt_sweeps_into_the_dispatcher` | `G-9`: `SyncDebtSweepsWired.SettlementSync` is false. |
| PC-10 | G-10: drop the `State == Pending` guard in `PublishedShaForRemoval`. | `/*/*/PhoneHomeRollingRunnerTests/C1125_RetirementMirrorRemovePublishesTheObservedTipOnlyForPendingEvidence` | `G-10`: the unavailable-observed arm sends `ObservedSha` instead of `WorktreeBaseSha`. |
| PC-11 | G-11: print `confirmedSha` instead of `observedSha` on the Pending origin line. | `/*/*/DelegateScriptLandStatusTests/C1136_StatusPrintsSyncDebtAndPendingLines` | `G-11`: the pending-with-debt arm's origin line is missing the observed SHA. |

### Docs sentences with pins

| Doc | Sentence | Pin |
|---|---|---|
| `docs/orchestration-loop.md` (Runner sync outcomes, CARD-1082 paragraph) | Replace "The Blocked warning says Runner sync pending and then reply." with "A Blocked lease-busy settlement's warning says Runner sync unavailable and then reply, never Runner sync pending; its evidence still records Pending." Add "The policy accepts only the task's own owned ref as the observed tip, and a Code report with no progress evidence under Pending stays Blocked." F3d replaces the Held sentence with "A Held debt is re-checked every 60 minutes by restamping NextAttemptAt 60 minutes ahead: the re-check reads no filesystem and matches no path, runs no Git, fast-forwards nothing, and does not supersede, because AgentTaskSyncDebts has no registration id that a complete retirement can be bound to. The attention warning stays until that binding exists." | `RunnerBranchContractDocumentationTests.LoopSentences` (F2, F3, F3d) |
| `docs/session-runtime-invariants.md` (CARD-1082 paragraph) | F3d replaces the Held sentence with "A Held debt is re-checked hourly and stays Held: the re-check does not read the worktree and does not supersede the row." | `RuntimeSentences` (F3d) |
| `docs/ops-http.md` (Settlement sync debt row) | F3d replaces the Held sentence with "Held debt is re-checked hourly and stays Held, so the re-check does not clear its attention row." | `OpsSentences` (F3d) |
| `docs/testing-and-build.md` (Build slots) | "Never chain a second driver after the gated command with a shell operator: the lease ends when build-slot.ps1 returns, so `-- dotnet build ... && dotnet exec ...` runs the exec unleased and Review flags it." | `CheckpointManifestDocumentationTests.the_chained_operator_pitfall_is_documented` (F5) |
| `server/Application/Dtos/TaskProgressDtos.cs` (`ProgressRemoteBaseline`) | XML doc: `Sha` with `State != Present` is the advertised origin tip, not a local object; readers gate on `State`. | reading only (F4) |

### Checkpoints

Exactly one isolated build and one filter per row. Counts are TUnit executed results: a method
with N `[Arguments]` rows contributes N. Rosters at `9975d163e` are in the ground-truth table.
Confirm the TRX roster equals the expected set, not merely at least `Min`. Postgres-backed classes
run serial. No whole-Unit lane. The table was validated at planning time with the checkpoint
importer (`import --plan`, tool built through `scripts/build-slot.ps1`).

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | F1 | `tests/Antiphon.Tests -> bin-c1082fu-cp1/` | policy-pins | `/*/*/SettlementSyncDebtPolicyTests/*` | V-1, V-2, V-3, V-4, R-1, R-2 | exact 30 results (21 existing + 3 + 2 + 2 + 2), 0 failed/skipped | 30 | 6 | |
| CP-2 | F2 | `tests/Antiphon.Tests -> bin-c1082fu-cp2/` | blocked-warning | `/*/*/RunnerTaskSettlementTests/(Sync_uncertainty_blocks_and_reply_retries*)\|(C1113_*)\|(C1082_*)` | V-5, V-6, R-3 | exact 7 results (1 + 1 + 5), 0 failed/skipped | 7 | 10 | true |
| CP-3 | F2 | CP-2 | docs-vocabulary | `/*/*/RunnerBranchContractDocumentationTests/*` | V-7 | exact 5 results, 0 failed/skipped | 5 | 3 | |
| CP-4 | F3 | `tests/Antiphon.Tests -> bin-c1082fu-cp4/` | held-lifecycle | `/*/*/SettlementSyncRecoveryTests/*` | V-8, V-9, V-10, R-4, R-5 | exact 20 results (10 existing + 1 committed removal + 8 uncertain-or-live + 1 recreated registration), 0 failed/skipped | 20 | 14 | true |
| CP-5 | F3 | CP-4 | docs-held | `/*/*/RunnerBranchContractDocumentationTests/*` | V-7 | exact 5 results, 0 failed/skipped | 5 | 3 | |
| CP-4N | F3 | CP-4 | held-revoked | `/*/*/SettlementSyncRecoveryTests/(C1136_RevokedRetirementKeepsHeldDebtAndReschedules*)` | F3b revoked | exact 2 results, 0 failed/skipped | 2 | 4 | true |
| CP-4T | F3 | CP-4 | held-temporary | `/*/*/SettlementSyncRecoveryTests/(C1136_TemporaryWorktreeAbsenceKeepsHeldDebtAndReschedules*)` | F3b temporary absence | exact 1 result, 0 failed/skipped | 1 | 4 | true |
| CP-4U | F3 | CP-4 | held-uncertain | `/*/*/SettlementSyncRecoveryTests/(C1136_UncertainHeldRegistrationKeepsHeldDebtAndReschedules*)` | F3b uncommitted or contradicted | exact 3 results, 0 failed/skipped | 3 | 4 | true |
| CP-4X | F3 | CP-4 | held-read-failure | `/*/*/SettlementSyncRecoveryTests/(C1136_HeldRecheckReadFailureKeepsHeldDebtAndReschedules*)` | F3b read failure | exact 1 result, 0 failed/skipped | 1 | 4 | true |
| RV-POLICY | F3 | CP-4 | regression | `/*/*/SettlementSyncDebtPolicyTests/*` | F3b regression | exact 30 results, 0 failed/skipped | 30 | 3 | true |
| RV-CLI | F3 | CP-4 | regression | `/*/*/DelegateScriptLandStatusTests/*` | F3b regression | exact 19 results, 0 failed/skipped | 19 | 4 | true |
| RV-SYNC | F3 | CP-4 | regression | `/*/*/RunnerSettlementSyncTests/*` | F3b regression | exact 29 results, 0 failed/skipped | 29 | 8 | true |
| RV-SETTLE | F3 | CP-4 | regression | `/*/*/RunnerTaskSettlementTests/*` | F3b regression | exact 32 results, 0 failed/skipped | 32 | 14 | true |
| RV-EVIDENCE | F3 | CP-4 | regression | `/*/*/ReviewEvidenceResettlementTests/*` | F3b regression | exact 10 results, 0 failed/skipped | 10 | 5 | true |
| RV-BLOCKED | F3 | CP-4 | regression | `/*/*/BlockedTaskSyncRecoveryTests/*` | F3b regression | exact 2 results, 0 failed/skipped | 2 | 3 | true |
| RV-ATTENTION | F3 | CP-4 | regression | `/*/*/AttentionServiceTests/*` | F3b regression | exact 164 results, 0 failed/skipped | 164 | 8 | true |
| RV-LIFETIME | F3 | CP-4 | regression | `/*/*/DispatcherSweepLifetimeTests/*` | F3b regression | exact 6 results, 0 failed/skipped | 6 | 3 | true |
| RV-REGISTRATION | F3 | CP-4 | regression | `/*/*/DispatcherSweepLifetimeRegistrationTests/*` | F3b regression | exact 1 result, 0 failed/skipped | 1 | 3 | true |
| RV-MODEL | F3 | CP-4 | regression | `/*/*/AppDbContextModelTests/*` | F3b regression | exact 2 results, 0 failed/skipped | 2 | 3 | true |
| RV-CLASSIFICATION | F3 | CP-4 | regression | `/*/*/TestClassificationGuardTests/*` | F3b regression | exact 1 result, 0 failed/skipped | 1 | 3 | true |
| RV-SLOW | F3 | CP-4 | regression | `/*/*/SlowTestTripwireTests/*` | F3b regression | exact 2 results, 0 failed/skipped | 2 | 3 | true |
| CP-6 | F4 | `tests/Antiphon.Tests -> bin-c1082fu-cp6/` | baseline-pins | `/*/*/RepairSourceDispatchTests/(C1115_*)\|(C499_V07b_RemoteFailureStillDispatchesWithAnUnavailableBaseline*)` | V-11, V-12, R-6 | exact 3 results (2 + 1), 0 failed/skipped | 3 | 8 | true |
| CP-7 | F4 | CP-6 | sweep-wiring | `/*/*/DispatcherSweepLifetimeRegistrationTests/*` | V-13 | exact 2 results (1 existing + 1), 0 failed/skipped | 2 | 5 | true |
| CP-8 | F5 | `tests/Antiphon.Tests -> bin-c1082fu-cp8/` | mirror-removal | `/*/*/PhoneHomeRollingRunnerTests/(C1125_*)\|(Retirement_mirror_remove_uses_confirmed_sha_and_keeps_unpublished_tip_as_residue*)` | V-14, R-7 | exact 4 results (3 + 1), 0 failed/skipped | 4 | 8 | true |
| CP-9 | F5 | CP-8 | cli-status | `/*/*/DelegateScriptLandStatusTests/C1136_*` | V-15 | exact 3 results, 0 failed/skipped | 3 | 5 | true |
| CP-10 | F5 | CP-8 | docs-pitfall | `/*/*/(CheckpointManifestDocumentationTests*)\|(RunnerBranchContractDocumentationTests*)/*` | V-16, V-7 | exact 13 results (8 + 5), 0 failed/skipped | 13 | 3 | |
| CP-11 | all | `tests/Antiphon.Tests -> bin-c1082fu-final/` | final-settlement | `/*/*/RunnerTaskSettlementTests/*` | V-5, V-6, R-3, R-8 | exact 32 results (31 + 1), 0 failed/skipped | 32 | 14 | true |
| CP-12 | all | CP-11 | final-sweep | `/*/*/SettlementSyncRecoveryTests/*` | V-8, V-9, V-10, R-4, R-5, R-8 | exact 20 results, 0 failed/skipped | 20 | 12 | true |
| CP-13 | all | CP-11 | final-dispatch-mirror | `/*/*/(RepairSourceDispatchTests*)\|(PhoneHomeRollingRunnerTests*)\|(DispatcherSweepLifetimeRegistrationTests*)/*` | V-11, V-12, V-13, V-14, R-6, R-7, R-9 | exact 40 results (31 + 7 + 2), 0 failed/skipped | 40 | 14 | true |
| CP-14 | all | CP-11 | final-units-cli-docs | `/*/*/(SettlementSyncDebtPolicyTests*)\|(DelegateScriptLandStatusTests*)\|(RunnerBranchContractDocumentationTests*)\|(CheckpointManifestDocumentationTests*)\|(RunnerCompletionProgressTests*)/*` | V-1-V-4, V-7, V-15, V-16, R-1, R-2, R-10 | exact 76 results (30 + 22 + 5 + 8 + 11), 0 failed/skipped | 76 | 10 | true |
| CP-15 | all | CP-11 | final-legacy | `/*/*/(ReviewEvidenceResettlementTests*)\|(TerminalRunnerSeatReleaseTests*)\|(BlockedTaskSyncRecoveryTests*)\|(BlockedTaskParkDeliveryTests*)/*` | R-11 | exact 55 results (10 + 39 + 2 + 4), 0 failed/skipped | 55 | 15 | true |

Run each group once its slice is committed, through the checkpoint tool:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1082fu-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1082fu-driver/ --nologo
dotnet tools/Antiphon.Checkpoints/bin-c1082fu-driver/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-07-card-1082-followups-plan.md --after F1 --expected-source-sha "$(git rev-parse HEAD)"
```

`F1`..`F5` are literal After tokens (`--after F3`); a numeric range does not select them. The last
slice runs `--rows CP-11,CP-12,CP-13,CP-14,CP-15` at the final committed SHA. Continue `wait` while
the exit is 75. Exit 4 is a slot timeout: report the row as not run. Preserve every tool-produced
`CHECKPOINT` line. No unlisted build or test loop; a failure-driven rerun is the same `CP-n` with
its reason and commit. Code and Review run `scripts/check-evidence-diff.ps1` over the task range.
Delete only producer-owned `bin-c1082fu-*` outputs; evidence stays ignored.

### Cost

Ordinary Code V/R floor = **130 minutes**, the sum of the `EstimatedMinutes` column
(6 + 10 + 3 + 14 + 3 + 8 + 5 + 8 + 5 + 3 + 14 + 12 + 14 + 10 + 15). Authoring: about 4 hours across
five slices. `-ExpectAbout` for each Code dispatch is its slice's rows plus its authoring budget.
Mutation (post-land): 11 method-scoped controls (PC-2 and PC-6 have two detectors each), about
70 minutes.

## Activation and rollout

F1-F4 go live at the AppHost restart after their land; confirm `GET /api/version` shows the landed
SHA. No setting changes, no migration: a Held row written before F3 has `NextAttemptAt = null` and
is re-checked on the first tick after the restart, then rescheduled hourly. F5 is live on land.
After the restart, a Blocked lease-busy task's warning reads "Runner sync unavailable" and its
`syncDebt` stays absent, which is the documented reply-required case. Operators watching
`settlement-sync-debt:*` attention keep a Held row for the life of that debt. The hourly re-check
only restamps NextAttemptAt. F3d dropped path-based supersession because the debt has no
registration id.

## Cards recommended to close as not needed

- **CARD-1119 item 1** (shared claim rules): see D-6. Item 2 lands in F1; close the card when F1 lands.
- **CARD-1132** (remaining item 3): see D-6; items 1-2 landed in S7 (`18ed47f48`, `5dc6b8371`, the
  plan's PC-10 cell). Close now.
- **CARD-1136 item 1a** (report every driver): already rule 2 of the checkpoint manifest; item 1b
  is the F5 doc sentence. Close items 1a/1b with F5, items 2-3 with F3, item 4 with F5.

## Risks and notes for Code and Review

- **F1 fixture.** `SettlementSyncDebtPolicyTests.Task()` has `Id = Guid.Empty` today; the FullRef
  pin requires the id whose `OwnedBranch` is `feat/card-task-abcd1234`. Set it in the fixture, never
  by loosening the pin to a prefix match.
- **F2 DI knob.** If `RunnerSettlementWorld.ConfigureServices` runs before the world registers
  `TaskCompletionProgressService`, add a fixture-only `progressService: false` parameter to
  `CreateAsync` (same shape as `syncDebt`), with a one-line comment naming CARD-1113. Do not remove
  the service for any other test.
- **F2 text.** Only the state word changes at `AgentTaskReplyService.cs:945`; the `MirrorDiverged`
  and `Diverged` recovery sentences and the "then reply" tail are unchanged
  (`TerminalRunnerSeatReleaseTests:143-148` pins the `dependency_unavailable` shape and must stay
  green).
- **F3 query shape.** Keep one `Where` so the claim read stays one statement; V-14's
  interceptor assertion (`AgentTaskSyncDebts`, `NextAttemptAt`, no `UPDATE`) is the cost pin. The
  Held arm of `ClaimAsync` returns null, so `attempted` stays 0 and the V-18 "ends before Git"
  assertions keep their meaning.
- **F3 clocks.** The sweep uses `DebtClock`; advance it 60 minutes for the re-check, as V-16 does
  for the backoff. Seed a Held row by running the sweep against an advanced origin (V-15's shape),
  not by hand-writing `State = Held`, so the re-check time is the production value.
- **F4 lease.** The nested acquire inside the observation fails because the claim holds the
  `landing.lock` file; `BeforeCommand` only has to fail `cat-file -e` to force the observation past
  the local-object check. Clear the hook before the settlement phase of V-12. Do not hold the lease
  from the test (the claim itself would be held).
- **F4 probe.** `SyncDebtSweepsWired` is internal and read-only; it must not be used by production
  code.
- **F5 stub.** Build the `taskStatusBody` with `summary`, `result`, `landing` as the existing C467
  arm does, then add `progressEvidence` and `syncDebt`; `nextAttemptAt` and `sourceReadyAt` may be
  null. Assert whole lines, not substrings of the SHA.
- **`[Arguments]` counts are promises.** CP rows count results; a new test may not add parameter
  expansion without updating this table.
- **No new attention kind, event type, setting or migration.** `HeldRecheckMinutes` is a constant
  on the service, not a `DelegationSettings` member.
