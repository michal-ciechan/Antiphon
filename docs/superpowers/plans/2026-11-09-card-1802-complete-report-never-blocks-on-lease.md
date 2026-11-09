# CARD-1802: a complete runner report never waits on a reply for desktop lease contention

Date: 2026-11-09. Plan task `a31ee409` (Frontier, Linux runner mirror, branch `feat/card-task-a31ee409`).
Inspected source: master `d2ff7e8d4d63d67cf611b2c567c2da97416bd8fa`. Card: Antiphon CARD-1802
(`11475e22-fe3c-4b90-8090-eeddcef43f09`). Investigation:
`docs/investigations/2026-11-09-blocked-complete-tasks-hold-seats.md` on `feat/card-task-12ea19bf`
(commit `d53db64b5`). Prior plan for the same arm: CARD-1109
(`docs/superpowers/plans/2026-10-07-card-1109-unfetched-commits-settlement-plan.md`, never built; see D-10).
Status: plan complete, verification design folded in. **next: code** (S1).

## Outcome and scope

A runner Code task whose complete report settles while the desktop repository mutation lease is busy
for the whole `Delegation:RunnerSyncBudgetSeconds` budget *during the observation fetch* today ends
Blocked/Decide with no `AgentTaskSyncDebts` row, keeps its session (parking off), and asks for the Reply
that standing rule 6 forbids. After this card:

1. That settlement ends **Succeeded or Failed on the report's own verdict**, records
   `progressEvidence.remoteSync.state = Pending` with `sourceDescends` absent and `confirmedSha` null, mints
   the ordinary `AgentTaskSyncDebt` row (`ReasonCode = runner_sync_lease_busy`), tells the caller
   `Runner sync pending ... No reply is needed`, and releases the delegate seat like every other
   Succeeded/Failed settlement. The existing `SettlementSyncRecoveryService` sweep retries the debt; its
   `SyncSettledAsync` path already performs the observation fetch under a free lease before the
   fast-forward, so the sweep clears it to Ready with no Reply and no new model turn.
2. The settlements that still Block are the ones a Reply alone cannot fix: refusals (dirty, diverged,
   rewound, identity, endpoint) and a progress read that could not verify the pushed work. None of them
   carries `runner_sync_lease_busy` as its block reason any more, so rule 6 ("never Reply to a complete
   report Blocked only by runner sync") and the CARD-1082 design sentence agree by construction.
3. Rule 6, the CARD-1082 design paragraph, `docs/ops-http.md`, `docs/session-runtime-invariants.md` and
   `scripts/delegate.ps1` carry one canonical agreement sentence, pinned by a documentation test.
4. No migration, no new column, no new setting. The one-time cleanup of the four live members of this
   class was done by the caller on 2026-11-09 01:14Z (D-9).

Out of scope: lease hold time and contention (CARD-0743, CARD-1479); fetching without the repository
lease (CARD-0499 G-24, CARD-0657 D-3); parking activation (CARD-1083); escalation of long-held Blocked
seats (Card B of the investigation); CARD-1109's runner-attested ancestry as an *attribution upgrade*
(D-10); the handoff/warning wording for refusals.

## Ground truth

| Card/brief assumption | Observed code at `d2ff7e8d4` | Consequence |
|---|---|---|
| The observation fetch needs the desktop lease and a busy lease keeps the tip. | `server/Infrastructure/Git/TaskProgressGit.cs:127-145`: `ls-remote` read the full advertised tip; `cat-file -e` misses; `TryAcquireAsync` fails; `LeaseBusy()` returns `Unavailable("repository_lease_busy")` **with** `advertised`. | Unchanged. The carried tip is what makes a debt row possible without objects. |
| `RemoteWorkspaceService.cs:804` sets `SourceDescends = null`. | `:712-716` waits inside `WhileLeaseBusyAsync` until the cumulative wait reaches `SyncBudget` (`:964-1006`, 10 s slices across deferred-report sweeps); `:802-805` returns `Unavailable(LeaseReason(observeSpent))` with `SourceDescends = null` and **no `DesktopBeforeSha`** although `l0` (`:709`) is in hand. Compare `:853-856`: the acquire-step busy arm passes `l0` and `SourceDescends = true`. | S2 passes `l0` so the debt row carries `DesktopBeforeSha`; `SourceDescends` stays null (it means server-read ancestry). |
| `SettlementSyncDebtPolicy.Classify` marks it Pending. | `:100-109` `Eligible`: `RunnerSyncDebtOnSettlement` on, `Unavailable` + `LeaseBusy`, full `RemoteSha`, the task's own owned ref. Role does not decide. | Unchanged. Pending is already minted for this arm. |
| `TaskCompletionProgressService.cs:268` returns `Unknown(LeaseBusy)`. | `:223-268` Pending arm needs `SourceDescends == true` and a local object; otherwise `Unknown(RemoteSettlementSyncReasons.LeaseBusy)` (Indeterminate). `RevParseCommitAsync` is the local-object gate. | S2 adds the unfetched arm (`Unknown(SourceUnfetched)`) and renames the residual fallback (`Unknown(ProgressUnverified)`). |
| `SettlementSyncDebtPolicy.cs:53-54` blocks. | `:47-54` Pending + Code: `evidence is null` → `LeaseBusy`; `Indeterminate` → `evidence.Reason ?? LeaseBusy`. | S2: `SourceUnfetched` → not blocked; `null`/`LeaseBusy` → `ProgressUnverified`; any other reason unchanged. |
| `AgentTaskReplyService.cs:1248-1250` turns Succeeded into Blocked; `:1567` skips the debt row. | `:1248` `remoteBlock = RemoteSyncBlockReason(...)` (= `BlockReason`, `:2477-2479`); `:1291-1298` Decide handoff "then reply"; `:1566-1594` debt row only when `remoteBlock is null`; `:1558-1561` `shouldRelease` true for Succeeded/Failed, Blocked only through `TerminalRunnerSeatReleaseService.OwnsAutomaticPath`. | No code change in this file: with `BlockReason` null the existing path mints the row (`DesktopBeforeSha = owed.DesktopBeforeSha`), writes `PendingWarning`, and releases. |
| The retry sweep reads only debt rows. | `SettlementSyncRecoveryService.SweepAsync` `:38-45` selects due `AgentTaskSyncDebts`; `ClaimAsync` → `RemoteWorkspaceService.SyncSettledAsync` (`:540-555`) → `SyncCoreAsync(singleAttempt: true)` → `SyncOwnedCheckoutAsync`, which observes **and fetches** the exact ref under a free lease (`:712`), reads ancestry (`:836-845`), acquires (`:846`) and fast-forwards; `SaveAsync` maps Confirmed → Ready, Refused (`Diverged`/`Rewound`/`TipNotReported`) → Held with that reason, still-busy → Pending backoff 1, 2, 4, 5 min, cap `RunnerSyncDebtMaxAttempts` (48). | A debt minted for the unfetched arm is serviced by the sweep as is. The non-Code roles already mint exactly this shape today (`C0824RefOnlyReadTests` state `pending-unknown`, Docs role, `fetchSourceObjects: false`, settles Succeeded). |
| The seat is held because parking is off. | `TerminalRunnerSeatReleaseService.cs:62-78`: `OwnsAutomaticPath` needs `AutomaticEnabled`; `TryHandleTaskAsync` returns `true` without acting for Blocked when `!ParkingEnabled`. `AgentTaskDispatcher.cs:9419-9430` excludes those seats from pool idle retirement. `ReleaseDelegateAsync` (`AgentTaskReplyService.cs:2854-2862`) returns early for Blocked. | Unchanged. The seat is freed because the settlement is no longer Blocked: Succeeded/Failed take the ordinary `PoolDelegateRelease.KillAndVerifyAsync` path (`:2931-2956`). |
| Rule 6 says the sweep clears the block; the design says a reply is required. | `docs/orchestration-loop.md:651` (rule 6) vs `:1866` ("Dirty, diverged, and unfetched Code stay Blocked with no debt row ... Pending evidence without a debt row means the task stayed Blocked and a reply is required"); same sentence at `docs/ops-http.md:179` and `scripts/delegate.ps1:910`; `docs/session-runtime-invariants.md:511` ("Dirty, diverged, and unfetched Code stay Blocked"). Pinned by `RunnerBranchContractDocumentationTests` (`LoopSentences`, `RuntimeSentences`, `OpsSentences`, `:91`) and `StandingPipelinePolicyDocumentationTests.standing_rule_six_never_replies_to_a_sync_blocked_complete_report` (`:204-230`). | S3 rewrites both sides and adds the agreement pin. |
| Tests that should have caught it reply by construction. | `RunnerTaskSettlementTests.Sync_uncertainty_blocks_and_reply_retries` (`:586-646`) manufactures the observation-busy arm and then replies; `Sustained_lease_contention_spans_sweeps_without_holding_up_other_settlements` (`:683-740`) ends Blocked with `AssertNoDebtAsync`; `C1113_CodeLeaseBusyWithoutProgressServiceStaysBlocked` (`:652`) pins the lease reason in the handoff; `SettlementSyncDebtPolicyTests.C1113_*`/`C1119_*` pin `LeaseBusy` as the fallback reason. | S1 re-targets the first to a genuine-unknown read, flips the second's tail, and changes the fallback pins to `ProgressUnverified` (D-8). |
| Kill switch. | `C1082_DisabledSettingKeepsLeaseBusyBlocked`; `RunnerSeatReleaseFixture(syncDebt: false)` in `BlockedTaskSyncRecoveryTests.C1065_*`. With `RunnerSyncDebtOnSettlement = false` `Classify` leaves `Unavailable(LeaseBusy)` and `BlockReason` returns `prepared.Reason`. | Unchanged (D-7). Rule 6 presumes the default-on switch; the doc says so. |
| Live members of the class. | `GET /api/agent-tasks/{id}` on 2026-11-09: `83c6ade8`, `317b5f73`, `0ed344bc`, `2b953d03` are `Canceled` at 01:14:15-01:14:21Z, sessions Stopped, no debt. `GET /api/attention` shows no `SeatIdle` row; the only `BlockedQuestion` is `48473acc` (CARD-1783, a different class). | D-9 records the done cleanup and the procedure for stragglers. |
| Fleet placement. | `GET /api/runner-defaults`: `globalRunnerId = server2`; `GET /api/session-runners`: server2 Linux 10 seats, desktop Windows 2 seats, server2-temp retired. | Every checkpoint row is Linux-capable; briefs carry no `-Runner`/`-Platform` (D-11). |

## Decisions

### D-1. Choose (a): mint the ordinary debt row and settle on the report's verdict; the sweep fetches

The brief's three options:

- **(a) mint a retryable `AgentTaskSyncDebt` row so the existing sweep retries and settles it with no
  reply.** Chosen. Evidence: the sweep's `SyncSettledAsync` already runs the observation fetch under a
  free lease (ground truth row 7); a debt row with no local objects is already produced and serviced for
  non-Code roles (`C0824RefOnlyReadTests` `pending-unknown`); the only thing that made Code different is
  the `BlockReason` arm CARD-1113 added for Indeterminate progress, which exists to fail closed on a read
  that *could not* verify progress, not to park a seat. The one new fact needed is a reason that says
  "the objects are not fetched yet" so the policy can tell it from a genuinely indeterminate read.
- **(b) re-attempt the observation fetch inside the settlement with a bounded backoff.** Rejected as the
  closure. It already exists: `WhileLeaseBusyAsync` retries every `LeaseRetryInterval` in 10 s slices, and
  the deferred-report sweep re-hands the report until the cumulative wait reaches the 120 s budget
  (`Sustained_lease_contention_*`). A longer bound only moves the cliff (a land holds the desktop lease
  for its whole rebase/build/verify, often longer than any sane in-settlement bound) and holds the model
  turn and the seat for the whole wait (CARD-1109 D-2, CARD-1043 D-1, CARD-1065 D-6). The bounded
  backoff that is right for this problem is the debt sweep's: durable, seat-free, 1/2/4/5 min, capped.
- **(c) both.** Rejected: (b) adds nothing once (a) settles the report, and it would make the two
  arms (observation-busy, acquire-busy) wait differently for no gain.

Also rejected: **Blocked now, promoted later by the sweep** (CARD-1082 D-5: the sweep never promotes a
verdict; a Blocked task owes a decision; re-settling from a retained report without a session is new
machinery); **CARD-1109's runner-attested ancestry as the prerequisite** (D-10: it keeps a Blocked
fallback for "no runner answer", "tip not S", "claim not S", so it cannot deliver acceptance (1)/(2)
"never"; it is a fine evidence upgrade on top of this card); **a new debt kind or `ReasonCode`** (the
sweep's `SaveAsync` overwrites `ReasonCode` with the retry result on the first attempt, and the
ops-http sentence "The only debt-creating reason is runner_sync_lease_busy" stays true; the arm is
visible in evidence as `remoteSync.sourceDescends` absent with `confirmedSha` null, in the Pending
warning's unfetched clause (D-5), and in `syncDebt.desktopBeforeSha` being the baseline).

Honesty of the evidence: the progress read for this arm records `Indeterminate` with reason
`runner_sync_source_unfetched`, never `ProgressObserved`. The caller warning therefore carries
`progress=unavailable; reason=runner_sync_source_unfetched` (`TaskCompletionProgressService.ProgressWarning`)
beside `Runner sync pending ... No reply is needed`. What is known at settlement: the task's own owned ref
exists on origin at a full tip S (server-read with `ls-remote`), S is not a local object of the canonical
repository (so S is not b and not any baseline tip present locally: the Contained novelty case is
impossible), and the report is complete. What is not known: that S descends from b (vs rewound/diverged)
and that the report's claimed commit is reachable from S. The sweep settles the first (Held
`runner_sync_rewound`/`runner_sync_diverged` with the existing attention row and `-StartRef` recovery
text); the second stays unverified for this arm, which the warning says. Nothing downstream fails open:
Pending is never Confirmed, `MergeBackAsync` is not reached for Indeterminate progress (workspace note
`desktop-sync=pending`), `RefOnlyCommittedSource` and `PendingLocalTipAsync` require `SourceDescends == true`
and keep returning null, land reads neither field, and Review and `-Land` use the pushed branch at S.

### D-2. What must stay Blocked, and why no block says "lease busy" any more

A complete Code report stays Blocked/Decide (no debt row, session kept, handoff "repair ..., then reply")
when the settlement cannot establish the pushed work by itself and a repair followed by a Reply is the
only way out:

| Arm | Block reason after this card | Why a Reply is the exit |
|---|---|---|
| Refused sync: dirty, diverged, rewound, identity/branch mismatch, endpoint changed/ambiguous, tip not reported, retirement reserved | the refusal reason (unchanged) | The desktop checkout or the branch needs a repair; `-StartRef` recovery for diverged/rewound. |
| Unavailable other than lease: fetch failed, timeout, inspection/postcondition/dependency unavailable, mirror publish failed | the reason (unchanged) | The Git read itself failed; the next report retries it. |
| Pending, Code, progress evidence absent (no `TaskCompletionProgressService` registered) | **`runner_sync_progress_unverified`** (was `runner_sync_lease_busy`) | Wiring fault; fail closed (CARD-1113 item 2). |
| Pending, Code, Indeterminate read with its own reason: `baseline_lineage_broken`, `primary_log_unavailable`, `primary_head_changed`, `ref_only_*`, `claimed_commit_not_novel` family | that reason (unchanged) | The objects were local and the read still could not verify; a repair then a fresh report. |
| Pending, Code, Indeterminate read with a null reason, or whose reason is `runner_sync_lease_busy` (the `RecordRemoteEvidenceAsync` exception fallback at `AgentTaskReplyService.cs:2459-2461` copies `prepared.Reason`) | **`runner_sync_progress_unverified`** (was `runner_sync_lease_busy`) | The read threw or gave no reason; fail closed. |
| Pending, Code, Indeterminate read with reason **`runner_sync_source_unfetched`** | **not blocked** (new) | Nothing to repair: the sweep fetches and fast-forwards. |
| Kill switch `RunnerSyncDebtOnSettlement = false` | `runner_sync_lease_busy` (Unavailable, not Pending; unchanged) | Operator choice; rule 6 states the default-on assumption. |

Rationale for renaming the two fallbacks: rule 6 is phrased on the reason string. After this card a
settlement that is *Pending* and Blocked is blocked by its progress read, so its reason must say so;
leaving `runner_sync_lease_busy` on it would make rule 6 forbid the Reply that is the only exit. The
tests that pinned the fallback (`C1113_PendingCodeWithoutEvidenceBlocksWithLeaseReason`,
`C1119_PendingCodeIndeterminateCarriesEvaluatedReason` null row, `C1113_CodeLeaseBusyWithoutProgressServiceStaysBlocked`)
keep their meaning (Blocked, Decide, no debt row, no "synced later") and change only the expected
string. Rejected: keeping `LeaseBusy` and phrasing rule 6 around "has a debt row" (an orchestrator reads
the handoff, not the debt table); a third `BlockReason` output (the string is the contract).

New constants in `RemoteSettlementSyncReasons` (`server/Application/Dtos/RemoteSettlementSyncDtos.cs`,
appended after `SettlementSyncAttemptsExhausted`):
`SourceUnfetched = "runner_sync_source_unfetched"`, `ProgressUnverified = "runner_sync_progress_unverified"`.

### D-3. The state machine, before and after

Inputs: complete Code report, runner-bound Worktree task, owned ref on origin at full S ≠ b, S not a
local object, desktop lease busy for the whole observation budget, `RunnerSyncDebtOnSettlement = true`.

| Step | Before (`d2ff7e8d4`) | After |
|---|---|---|
| `TaskProgressGit.ObserveExactRefAsync` | `Unavailable("repository_lease_busy")`, tip S carried | same |
| `RemoteWorkspaceService.SyncOwnedCheckoutAsync` `:712-716`, `:802-805` | waits the budget across sweeps (`LeaseWaiting` re-hands), then `Unavailable(runner_sync_lease_busy)`, `RemoteSha = S`, `SourceDescends = null`, `DesktopBeforeSha = null` | same wait; result also carries `DesktopBeforeSha = l0` |
| `SettlementSyncDebtPolicy.Classify` | `Pending` | same |
| `TaskCompletionProgressService.EvaluateRemoteAsync` Pending arm | `Unknown(runner_sync_lease_busy)` (Indeterminate) | `Unknown(runner_sync_source_unfetched)` when `SourceDescends is null`, S full, S ≠ b and S not local; residual fallback `Unknown(runner_sync_progress_unverified)` |
| `SettlementSyncDebtPolicy.BlockReason` | `runner_sync_lease_busy` | `null` for `SourceUnfetched`; `runner_sync_progress_unverified` for null evidence or a null/lease reason; other reasons unchanged |
| `AgentTaskReplyService` `:1248-1250` | status → Blocked; `:1291-1298` NextStage Decide, handoff "then reply"; `:1363` warning "Runner sync unavailable: runner_sync_lease_busy ... then reply" | status stays Succeeded/Failed; report's own `next=` kept; `:1381` warning `PendingWarning` ("Runner sync pending ... synced later ... No reply is needed" plus the unfetched clause, D-5) |
| Debt row `:1566-1594` | skipped (`remoteBlock` not null) | minted: `State Pending`, `ReasonCode runner_sync_lease_busy`, `SourceSha S`, `BaselineSha b`, `DesktopBeforeSha l0`, `NextAttemptAt now` |
| Workspace note `:1450-1453` | none (Blocked) | `branch ... left for review; source S (desktop-sync=pending)` |
| Release `:1558-1561` | Blocked → `TerminalRunnerSeatReleaseService.TryHandleTaskAsync` → returns true, nothing happens (parking off) → session kept, seat held | Succeeded/Failed → `ReleaseDelegateAsync` → `PoolDelegateRelease.KillAndVerifyAsync` → session stopped / pooled, seat free |
| Later | nothing retries; `SeatIdle` and `BlockedQuestion` attention rows; Reply forbidden by rule 6; Cancel by hand | `SettlementSyncRecoveryService.SweepAsync` claims the row at `NextAttemptAt`; `SyncSettledAsync` fetches S under a free lease, reads ancestry, fast-forwards → `Ready` (`settlement_sync_ready`, `confirmedSha = S`); still busy → Pending with backoff; `Diverged`/`Rewound`/`TipNotReported`/dirty → `Held` with that reason and the `settlement-sync-debt:` attention row |

The acquire-step arm (objects fetched, lease busy at `:846`) is unchanged: `SourceDescends = true`,
`ProgressObserved`, Succeeded + debt. S == b never reaches the busy arm (b is local, so `cat-file -e`
answers Present without the lease; `C1082_CodeLeaseBusyEqualTipFailsOnItsOwnVerdict` stays the no-push
negative).

### D-4. The seat: a settled complete report releases its seat even with parking off

Rule: **a settlement that leaves a complete report in a terminal status (Succeeded or Failed), including
every Pending debt-backed settlement, releases the delegate seat in that settlement.** The debt row owns
the only remaining work (the desktop fast-forward) and never needs the session. A Blocked complete report
keeps its session only when its handoff names a repair a Reply must follow (D-2); after this card no such
block is lease contention, so every held seat has an owner: the reader of the Decide handoff, under
`SeatIdle` (CARD-1079) detection, with Cancel as the sanctioned release and no automatic deadline while
parking is off (CARD-1083).

No code change: `shouldRelease` (`AgentTaskReplyService.cs:1558-1561`) is already true for
Succeeded/Failed, and `TerminalRunnerSeatReleaseService` is untouched. The witness is new
(`C1802_LeaseBusyCompleteReportReleasesItsSeatWithParkingOff` in `BlockedTaskSyncRecoveryTests`, on
`RunnerSeatReleaseFixture` with `parking: false, syncRecovery: true, syncDebt: true`, the real desktop
lease held through the budget as `C1065_LeaseBusyDoesNotRetainPublishedIdleSeat` does): after
`SettleAsync("done")` the task is Succeeded, a Pending debt row exists, the fixture agent row is gone or
its session is Stopped (the `PoolDelegateRelease.KillAndVerifyAsync` outcome), `f.Wire.ConditionalCommands`
is 0 (the ordinary release owns it, not the conditional seat path), no `AgentTaskParks` row exists, and the
attention feed has no `seat-idle:` row for the session; then with the lease released and the clock
advanced one minute, `AgentTaskDispatcher.RecoverSettlementSyncAsync` returns 1 and the debt is Ready with
the desktop at the pushed SHA. Mutant M-7 (D-13) proves the rule is load-bearing.

`RunnerSettlementWorld` cannot witness the kill (its task has no `AgentId`, so `ReleaseDelegateAsync`
returns at `:2863`); that is why the seat witness lives on the seat fixture.

### D-5. Caller text

`SettlementSyncDebtPolicy.PendingWarning` (`:62-74`) appends, when `result.SourceDescends is null`:
`The pushed objects are not yet in the desktop repository; the settlement sync sweep fetches them under
the lease before the fast-forward.` The sentence never contains "then reply"; the existing pins
("synced later", "No reply is needed", no "Runner sync pending" on a Blocked settlement) are unchanged.
`WorkspaceNote` is unchanged. The Decide handoff and "Runner sync unavailable" warning texts
(`AgentTaskReplyService.cs:1293-1298`, `:1355-1364`) are unchanged in wording; they now print
`runner_sync_progress_unverified` or the read's reason instead of `runner_sync_lease_busy`.

### D-6. No migration

`AgentTaskSyncDebt` already has every column this arm needs (`SourceSha`, `BaselineSha`, `DesktopBeforeSha`,
`EndpointFingerprint`, `WorkspaceSnapshotJson`); the evidence JSON is additive and already carries
`sourceDescends`; the two new reason strings are constants. Nothing is added to the model, so
`AppDbContextModelSnapshot` is untouched and no migration is sorted after
`20261108121426_Card1773TranscriptPromptStampIndex`. Rejected: a `FetchOwed` flag or a distinct
`ReasonCode` on the row (D-1).

### D-7. Kill switch semantics

With `Delegation:RunnerSyncDebtOnSettlement = false` nothing here is reachable: `Classify` leaves
`Unavailable(runner_sync_lease_busy)`, `BlockReason` returns `prepared.Reason`, the task Blocks with
"then reply" and no debt row, exactly as today (`C1082_DisabledSettingKeepsLeaseBusyBlocked`,
`C1082_DisabledSettingLeavesLeaseBusyUnavailable`, `C1065_LeaseBusyDoesNotRetainPublishedIdleSeat` with
`syncDebt: false`). Rule 6 and the design paragraph say they describe the default.

### D-8. Legacy tests: what changes and what keeps its meaning

| Test | Today | After | Meaning kept |
|---|---|---|---|
| `RunnerTaskSettlementTests.Sync_uncertainty_blocks_and_reply_retries` | observation-busy arm → Blocked → Reply → Succeeded | re-targeted to the genuine-unknown arm: S's objects are fetched into the desktop first (as `C1082_CodeLeaseBusyWithLocalObjectsSettlesSucceededPending` does, so the observation is Present without the lease), the lease is held so the acquire step spends the budget (Pending, `SourceDescends = true`), the report claims a commit C that is the first of two pushed commits, and `world.Git.Git.FailCommand` fails `merge-base --is-ancestor C S` with a non-0/1 exit so `IsAncestorAsync` returns null → `Unknown("primary_log_unavailable")`. Asserts Blocked, Decide, reason `primary_log_unavailable` in the handoff and the "Runner sync unavailable ... then reply" warning, no debt row, HEAD at baseline; then clears the fault, releases the lease, replies, and the fresh report settles Succeeded with `ConfirmedSha == S` | "a progress read the server cannot complete blocks, and a reply retries" |
| `Sustained_lease_contention_spans_sweeps_without_holding_up_other_settlements` | tail: Blocked, Decide, no debt after 120 s | tail: Succeeded, `NextStage != Decide`, evidence Pending with reason `runner_sync_lease_busy` and no `sourceDescends`, one debt row (Pending, `SourceSha = S`), no Blocked event, HEAD at baseline; the four-sweep cadence and the other task's settlement are unchanged | "the lease wait spans sweeps without holding up other settlements, and the budget ends the wait" |
| `C1113_CodeLeaseBusyWithoutProgressServiceStaysBlocked` | handoff contains `runner_sync_lease_busy` | handoff contains `runner_sync_progress_unverified`; everything else unchanged | "no progress evidence fails closed" |
| `SettlementSyncDebtPolicyTests.C1113_PendingCodeWithoutEvidenceBlocksWithLeaseReason` | Code → `LeaseBusy` | Code → `ProgressUnverified` (rename the method's `LeaseReason` suffix to `ProgressUnverifiedReason`) | same |
| `C1119_PendingCodeIndeterminateCarriesEvaluatedReason` | `null → LeaseBusy` | `null → ProgressUnverified`; add row `LeaseBusy → ProgressUnverified` | same |
| `RunnerBranchContractDocumentationTests` arrays | pin the stale sentences | pin the new sentences (S3) | same |
| `BlockedTaskSyncRecoveryTests.C1065_*`, `C1082_DisabledSetting*`, `C0824RefOnlyReadTests`, `SettlementSyncRecoveryTests`, `RunnerSettlementSyncTests`, `TerminalRunnerSeatReleaseTests` | unchanged | unchanged | regression rows |

### D-9. One-time cleanup of tasks already Blocked in this class

Cancel is the sanctioned path; there is no data migration and no server-side re-settlement of an existing
Blocked row (the sweep reads debt rows, a Blocked row of this class has none, and re-settling a retained
report without its session is machinery this card does not build). The four members named by the
investigation were canceled by the caller on 2026-11-09 at 01:14:15-01:14:21Z under the operator's
standing authority ("Clean up the blocked tasks"); `GET /api/agent-tasks/{id}` now reads `Canceled`, sessions
Stopped, no debt; `GET /api/attention` has no `SeatIdle` row. No work was lost: every origin branch equals
its report tip and every commit not on master is a report or docs commit (investigation).

For any straggler before this card activates (run as the caller, token in the header, never printed):

```
curl -fsS "$ANTIPHON_API/api/attention" | node -e '...'   # SeatIdle / BlockedQuestion rows name the task ids
curl -fsS "$ANTIPHON_API/api/agent-tasks/<full-id>"       # progressEvidence.remoteSync.state Pending, syncDebt null, status Blocked, a complete marked report
curl -fsS -X POST -H "X-Antiphon-Task-Token: <caller token>" "$ANTIPHON_API/api/agent-tasks/<full-id>/cancel"
```

Refusals: 409 "has already finished" (already settled), 409 "The task attempt changed before cancellation".
Do not Reply (rule 6) and do not use `-RecoverReviewedSource` (it is for a Review subject). A task whose
`blocked.reason` is its own question (like `2b953d03` was) is a question, not this class; the caller
answers or cancels it on its merits.

### D-10. CARD-1109 is superseded as the closure and kept as an attribution upgrade

CARD-1109 (Review column, plan `next: decide`, D-9 measurement gate, never built) planned to settle this
arm Succeeded only when the runner attests, inspect-only on the `workspacePublishV1` wire
(`PhoneHomeWorkspacePublishRequest.Publish = false`, `PhoneHomeWorkspacePublishResponse.DescendsFromBaseline`
already exists), that its mirror tip equals S and S descends from b, and to keep Blocked otherwise. Its
measurement question is answered by the investigation (59 of 805 server2 Code tasks since 10-15, four
seats held 6-30 h, 25 cancels by hand): the arm is material. Its closure shape is not adopted because it
keeps a Blocked fallback (no runner answer, tip ≠ S, claim ≠ S) that rule 6 then contradicts again. Its
attestation would upgrade this card's `Indeterminate(runner_sync_source_unfetched)` to `ProgressObserved`
with provenance and is compatible with every decision here; the orchestrator should re-scope CARD-1109 to
that upgrade or close it as superseded (a card note, not a code change in this plan).

### D-11. Fleet placement and lane

Server-side C# plus Postgres/Git integration tests. Every checkpoint row runs on the Linux lane where
`GET /api/runner-defaults` sends it (server2 today). No `-Runner` or `-Platform` in any brief. The
`Antiphon.Tests` build uses the worktree's native default output for direct builds and the checkpoint
tool's own `bin-c1802-*` outputs for rows (CARD-1629).

### D-12. Session that waits for input (checklist)

This plan adds no session that waits for input and removes one arm that did. The remaining Blocked arms
(D-2) keep their session as today; what releases them is a Reply after the named repair or a Cancel, and
with parking disabled there is no automatic release deadline (CARD-1083). `SeatIdle` (CARD-1079, Warning at
30 min, Error at 180 min) is the detector; escalation to an owner is Card B of the investigation, not this
card.

### D-13. Mutants per witness

| Mutant | Edit | Witness that goes red |
|---|---|---|
| M-1 | `BlockReason`: treat `SourceUnfetched` like any other Indeterminate reason (return it) | `C1802_UnfetchedOwnedTipUnderPendingDoesNotBlock`; `C1802_LeaseBusyObservationSettlesWithDebtAndNoReply` (Blocked, no debt) |
| M-2 | `EvaluateRemoteAsync`: unfetched arm returns `Unknown(LeaseBusy)` | `C1802_LeaseBusyObservationSettlesWithDebtAndNoReply` (Blocked `runner_sync_progress_unverified`) |
| M-3 | `BlockReason`: null evidence or null/lease reason returns `LeaseBusy` again | `C1802_PendingBlockReasonIsNeverLeaseBusy` rows; `C1113_*`, `C1119_*` |
| M-4 | `RemoteWorkspaceService:805`: drop `l0` | `C1802_LeaseBusyObservationSettlesWithDebtAndNoReply` (`debt.DesktopBeforeSha` must be the baseline) |
| M-5 | `PendingWarning`: drop the unfetched clause, or add "then reply" to it | same witness (warning text pins) |
| M-6 | `AgentTaskReplyService:1566`: skip the debt row when `SourceDescends is null` | same witness (no debt row; sweep returns 0) |
| M-7 | `AgentTaskReplyService:1558-1561`: `shouldRelease` false when `remote.Result.State == Pending` | `C1802_LeaseBusyCompleteReportReleasesItsSeatWithParkingOff` (agent row remains / session Running) |
| M-8 | `EvaluateRemoteAsync`: unfetched arm also fires when `SourceDescends == true` | `Sync_uncertainty_blocks_and_reply_retries` (must stay Blocked `primary_log_unavailable`), `C1082_CodeLeaseBusyWithLocalObjectsSettlesSucceededPending` (must stay `ProgressObserved`) |
| M-9 | docs: restore "Pending evidence without a debt row means the task stayed Blocked and a reply is required." in any of the five places, or drop the canonical sentence from one | `C1802_rule_six_and_the_sync_debt_design_agree` |
| M-10 | rule 6: drop `runner_sync_progress_unverified` or name a reason that is not a `RemoteSettlementSyncReasons` constant | same doc test (constant cross-check) |

## Implementation slices

Each slice is one commit group, implementable in 60-120 minutes, pushed to `feat/card-task-a31ee409`
(or the Code task's own branch) before its checkpoint rows run.

| Slice | Files | Tests | Minutes |
|---|---|---|---|
| **S1: constants and red witnesses** (tests compile, behaviour fails) | `server/Application/Dtos/RemoteSettlementSyncDtos.cs` (two constants only, no behaviour); `tests/Antiphon.Tests/Application/SettlementSyncDebtPolicyTests.cs`; `tests/Antiphon.Tests/Application/RunnerTaskSettlementTests.cs`; `tests/Antiphon.Tests/Application/BlockedTaskSyncRecoveryTests.cs`; `tests/Antiphon.Tests/Application/RunnerBranchContractDocumentationTests.cs` | New `C1802_UnfetchedOwnedTipUnderPendingDoesNotBlock` (Pending Code, Indeterminate `SourceUnfetched` → null); new `C1802_PendingBlockReasonIsNeverLeaseBusy` (`[Arguments]` matrix: evidence null; Indeterminate with null, `LeaseBusy`, `baseline_lineage_broken`, `primary_log_unavailable`, `primary_status_unavailable`, `SourceUnfetched`; Review role → null; asserts the result is never `LeaseBusy` and equals the expected string); `C1113_*`/`C1119_*` expectations → `ProgressUnverified`; new `C1802_LeaseBusyObservationSettlesWithDebtAndNoReply` (modelled on `Sync_uncertainty_blocks_and_reply_retries` and `C1082_CodeLeaseBusyWithLocalObjectsSettlesSucceededPending`: no pre-fetch, lease held through `FinishBusySettleAsync`; asserts Succeeded, `NextStage != Decide`, `FailureCode` null, evidence Pending/`ObservedSha == S`/`ConfirmedSha` null/`SourceDescends` null/assessment Indeterminate reason `SourceUnfetched`, a Warning event containing "Runner sync pending" and "No reply is needed" and the unfetched clause, no event containing "then reply" or "Runner sync unavailable", note contains `desktop-sync=pending`, HEAD at baseline, exactly one debt row with `State Pending`, `SourceSha == S`, `BaselineSha == b`, `DesktopBeforeSha == b`, `ReasonCode LeaseBusy`; then lease released, `DebtClock` +1 min, `SweepSettlementSyncAsync() == 1`, debt Ready with `ConfirmedSha == S`, HEAD == S, settlement evidence JSON unchanged); re-target `Sync_uncertainty_blocks_and_reply_retries` (D-8); flip the `Sustained_*` tail (D-8); new seat witness `C1802_LeaseBusyCompleteReportReleasesItsSeatWithParkingOff` (D-4); new `C1802_rule_six_and_the_sync_debt_design_agree` (D-14) | 90-120 |
| **S2: the fix** | `server/Application/Services/SettlementSyncDebtPolicy.cs:47-54` (`BlockReason` Pending arm), `:62-74` (`PendingWarning` clause); `server/Application/Services/TaskCompletionProgressService.cs:223-268` (unfetched arm before the residual; residual → `ProgressUnverified`); `server/Application/Services/RemoteWorkspaceService.cs:802-805` (pass `l0`) | S1's witnesses go green; `SettlementSyncDebtPolicyTests` whole class; `RunnerTaskSettlementTests` named rows then whole class; `BlockedTaskSyncRecoveryTests`; regression rows (CP-5..CP-9) | 60-90 |
| **S3: docs and the doc contract** | `docs/orchestration-loop.md:651` (rule 6), `:1866` (three sentences, D-14); `docs/ops-http.md:179`; `docs/session-runtime-invariants.md:511`; `scripts/delegate.ps1:910`; `tests/Antiphon.Tests/Application/RunnerBranchContractDocumentationTests.cs` sentence arrays and `:91` | `RunnerBranchContractDocumentationTests` (including the S1 doc test now green), `StandingPipelinePolicyDocumentationTests`, `LandGraphLandRefreshTests`, `DelegateScriptLandStatusTests`; the guard batch | 60 |

### D-14. Exact documentation edits (S3)

Canonical agreement sentence, verbatim in all five places:
`Lease contention alone never leaves Pending evidence without a debt row (CARD-1802).`

- `docs/orchestration-loop.md:651`, rule 6 (keeps every phrase `StandingPipelinePolicyDocumentationTests` pins):
  `6. **Do not Reply to a complete report that is Blocked only by runner sync.** A task whose report is complete and whose desktop sync is pending needs no Reply, even when an older settlement's warning asks for one (CARD-1082; CARD-1472 fixes the server side): Review and `-Land` use the pushed branch, and the settlement sync sweep clears the block. Since CARD-1802 lease contention (`runner_sync_lease_busy`) never Blocks a complete report at all: with Delegation:RunnerSyncDebtOnSettlement on (the default) it settles Succeeded or Failed with a sync-debt row, and the sweep fetches the pushed objects and fast-forwards the desktop (`syncDebt` on task detail goes Ready). Lease contention alone never leaves Pending evidence without a debt row (CARD-1802). A complete report that is Blocked is blocked by something a Reply alone cannot fix: a refused sync (dirty, diverged, rewound, identity) or a progress read that could not verify the pushed work (`runner_sync_progress_unverified`, `baseline_lineage_broken`, `primary_log_unavailable`); its handoff names the repair, and the Reply follows the repair. A Reply reopens the task as a new model turn, and a token-only answer then replaces the retained report with an empty result (Debug fc28850a, [docs/investigations/2026-10-22-report-contract-and-first-prompt-noise.md](investigations/2026-10-22-report-contract-and-first-prompt-noise.md)). Reply only when the report is genuinely missing, and then ask for the full report with its closing token, never just "stop".`
- `docs/orchestration-loop.md:1866`, CARD-1082 paragraph, three sentence replacements:
  `Dirty, diverged, and unfetched Code stay Blocked with no debt row.` → `Dirty, diverged and rewound Code stay Blocked with no debt row; an unfetched owned tip under lease contention is Pending with a debt row, and the sweep fetches its objects under the lease before the fast-forward (CARD-1802).`
  `A Blocked lease-busy settlement's warning says Runner sync unavailable and then reply, never Runner sync pending; its evidence still records Pending.` → `A Blocked Pending settlement's warning says Runner sync unavailable and then reply, never Runner sync pending; its evidence still records Pending, and its block reason is never runner_sync_lease_busy (runner_sync_progress_unverified when the progress read gave no reason).`
  `Pending evidence without a debt row means the task stayed Blocked and a reply is required.` → `Pending evidence without a debt row means the progress read, not the lease, blocked the task; repair what the handoff names, then reply. Lease contention alone never leaves Pending evidence without a debt row (CARD-1802).`
- `docs/ops-http.md:179`: `progressEvidence.remoteSync.state Pending with no syncDebt is the Blocked case: Pending evidence without a debt row means the task stayed Blocked and a reply is required.` → `progressEvidence.remoteSync.state Pending with no syncDebt is the progress-blocked case: the progress read, not the lease, blocked the task; repair what the handoff names, then reply. Lease contention alone never leaves Pending evidence without a debt row (CARD-1802).`
- `docs/session-runtime-invariants.md:511`: `Dirty, diverged, and unfetched Code stay Blocked, and the Blocked warning says then reply.` → `Dirty, diverged and rewound Code stay Blocked, and the Blocked warning says then reply; an unfetched tip under lease contention settles with a debt row and the sweep fetches it. Lease contention alone never leaves Pending evidence without a debt row (CARD-1802).`
- `scripts/delegate.ps1:910`: `Write-Output 'Pending evidence without a debt row: the progress read blocked this task; repair what the handoff names, then reply. Lease contention alone never leaves Pending evidence without a debt row (CARD-1802).'`
- `RunnerBranchContractDocumentationTests`: `LoopSentences` swaps the three sentences; `RuntimeSentences` swaps `Dirty, diverged, and unfetched Code stay Blocked` for `Dirty, diverged and rewound Code stay Blocked`; `OpsSentences` swaps the reply sentence; `:91` pins the new script text.
- New `C1802_rule_six_and_the_sync_debt_design_agree`: slices rule 6 (from `6. **Do not Reply` to `7. **Master lands`) and the CARD-1082 paragraph (from `CARD-1082. Delegation:RunnerSyncDebtOnSettlement` to its paragraph end) out of `docs/orchestration-loop.md`, the `Settlement sync debt (CARD-1082)` row out of `docs/ops-http.md`, the `CARD-1082.` paragraph out of `docs/session-runtime-invariants.md`, and the `Pending evidence without a debt row` `Write-Output` line out of `scripts/delegate.ps1`; asserts the canonical sentence is in all five, the stale sentence `Pending evidence without a debt row means the task stayed Blocked and a reply is required.` is in none, rule 6 names `runner_sync_progress_unverified` and that literal equals `RemoteSettlementSyncReasons.ProgressUnverified`, and the design paragraph says `the sweep fetches`. The unchanged pointers (AGENTS.md:88, `server/Bundles/orchestrator.md:90`, the two skills) keep the prohibition phrase, which stays true.

## Verification design

### V/R rows

| Id | Statement | Test |
|---|---|---|
| V-1 | Pending Code with Indeterminate reason `runner_sync_source_unfetched` is not blocked. | `SettlementSyncDebtPolicyTests.C1802_UnfetchedOwnedTipUnderPendingDoesNotBlock` |
| V-2 | No Pending block reason is `runner_sync_lease_busy`; null evidence and null/lease reasons map to `runner_sync_progress_unverified`; other reasons pass through; Review is never blocked. | `C1802_PendingBlockReasonIsNeverLeaseBusy`, `C1113_PendingCodeWithoutEvidenceBlocksWithProgressUnverifiedReason`, `C1119_*` |
| V-3 | A busy observation lease for the whole budget settles Succeeded with a Pending debt row carrying `DesktopBeforeSha`, "No reply is needed", no "then reply", seat-free; the sweep fetches, fast-forwards and marks Ready without a Reply. | `RunnerTaskSettlementTests.C1802_LeaseBusyObservationSettlesWithDebtAndNoReply`; the `Sustained_*` tail |
| V-4 | A genuine-unknown progress read under Pending stays Blocked/Decide with its own reason and no debt row; a Reply retries and settles. | re-targeted `Sync_uncertainty_blocks_and_reply_retries` |
| V-5 | A complete report under lease contention releases its seat with parking off; the debt is cleared by the dispatcher sweep. | `BlockedTaskSyncRecoveryTests.C1802_LeaseBusyCompleteReportReleasesItsSeatWithParkingOff` |
| V-6 | Rule 6 and the sync-debt design agree (canonical sentence in five places, stale sentence absent, reason literal is a code constant). | `RunnerBranchContractDocumentationTests.C1802_rule_six_and_the_sync_debt_design_agree` |
| R-1 | Every other `SettlementSyncDebtPolicy` behaviour is unchanged. | `SettlementSyncDebtPolicyTests` whole class |
| R-2 | Kill switch off keeps Blocked with the lease reason. | `C1082_DisabledSettingKeepsLeaseBusyBlocked`, `C1082_DisabledSettingLeavesLeaseBusyUnavailable`, `C1065_*` |
| R-3 | No progress service fails closed. | `C1113_CodeLeaseBusyWithoutProgressServiceStaysBlocked` |
| R-4 | Fetched-then-busy arm, equal-tip negative, Review Pending, dispatcher sweep recovery unchanged. | `C1082_*` in `RunnerTaskSettlementTests` |
| R-5 | Sweep, Held, Superseded, cap, RefOnly read states unchanged. | `SettlementSyncRecoveryTests`, `C0824RefOnlyReadTests` |
| R-6 | Resettlement and pending delivery unchanged. | `ReviewEvidenceResettlementTests`, `PendingSettlementDeliveryTests` |
| R-7 | Every pinned doc phrase survives; rule 6 pins hold; the delegate.ps1 status lines are unchanged. | `StandingPipelinePolicyDocumentationTests`, `LandGraphLandRefreshTests`, `DelegateScriptLandStatusTests` |
| R-8 | Policy, mechanical contract, platform guidance and layering guards stay green. | guard batch |

Not run, with reason: `TerminalRunnerSeatReleaseTests` (41 results; `TerminalRunnerSeatReleaseService` is
untouched and `BlockedTaskSyncRecoveryTests` exercises the fixture path this card relies on);
`RunnerSettlementSyncTests` (30; `RemoteWorkspaceService` changes one `Outcome` argument on a path
`RunnerTaskSettlementTests` covers end to end). Review may add either as an unlisted run with this reason.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1 | tests/Antiphon.Tests -> bin-c1802-s1/ | witnesses-red | /*/*/(SettlementSyncDebtPolicyTests*)\|(RunnerTaskSettlementTests*)\|(BlockedTaskSyncRecoveryTests*)\|(RunnerBranchContractDocumentationTests*)/(C1802_*)\|(Sustained_lease_contention_spans_sweeps_without_holding_up_other_settlements*) | V-1, V-2, V-3, V-5, V-6 | red: all listed executed, at least 5 failed (the unfetched policy row, the Code rows of the matrix, the settle witness, the seat witness, the doc pin, the Sustained tail) | 6 | 12 |
| CP-2 | S2 | tests/Antiphon.Tests -> bin-c1802-s2/ | policy-green | /*/*/SettlementSyncDebtPolicyTests/* | V-1, V-2, R-1 | all listed (31 results at d2ff7e8d4 plus the S1 rows), 0 failed/skipped | 31 | 8 |
| CP-3 | S2 | CP-2 | settle-green | /*/*/RunnerTaskSettlementTests/(C1802_*)\|(Sync_uncertainty_blocks_and_reply_retries*)\|(C1113_CodeLeaseBusyWithoutProgressServiceStaysBlocked*)\|(Sustained_lease_contention_spans_sweeps_without_holding_up_other_settlements*)\|(C1082_*) | V-3, V-4, R-2, R-3, R-4 | all listed (9), 0 failed/skipped | 9 | 10 |
| CP-4 | S2 | CP-2 | seat-green | /*/*/BlockedTaskSyncRecoveryTests/* | V-5, R-2 | all listed (4 at d2ff7e8d4 plus C1802), 0 failed/skipped | 5 | 10 |
| CP-5 | S2 | CP-2 | settle-regress-a | /*/*/RunnerTaskSettlementTests/(C1082_*)\|(C1113_*)\|(C1802_*)\|(C788_*)\|(Bind_refusal_recovery_*) | R-3, R-4 | all listed (15 methods; C788 argument rows count as reported), 0 failed/skipped | 15 | 11 |
| CP-6 | S2 | CP-2 | settle-regress-b | /*/*/RunnerTaskSettlementTests/(Unpushed_*)\|(Rebased_*)\|(Force_pushed_*)\|(Runner_*)\|(No_push_*)\|(Unmarked_*)\|(Settlement_sync_*)\|(Sync_uncertainty_*)\|(Sustained_*)\|(Refused_*)\|(Uncorrelated_*)\|(Artifact_*) | R-3, R-4 | all listed (14 methods; CP-5 and CP-6 together are the whole class, 29 methods after S1), 0 failed/skipped | 14 | 11 |
| CP-7 | S2 | CP-2 | debt-regress | /*/*/SettlementSyncRecoveryTests/* | R-5 | all listed (29), 0 failed/skipped | 29 | 15 |
| CP-8 | S2 | CP-2 | refonly-regress | /*/*/C0824RefOnlyReadTests/* | R-5 | all listed (3), 0 failed/skipped | 3 | 6 |
| CP-9 | S2 | CP-2 | resettle-regress | /*/*/(ReviewEvidenceResettlementTests*)\|(PendingSettlementDeliveryTests*)/* | R-6 | all listed (10 + 8), 0 failed/skipped | 18 | 12 |
| CP-10 | S3 | tests/Antiphon.Tests -> bin-c1802-s3/ | docs-contract | /*/*/(RunnerBranchContractDocumentationTests*)\|(StandingPipelinePolicyDocumentationTests*)\|(LandGraphLandRefreshTests*)\|(DelegateScriptLandStatusTests*)/* | V-6, R-7 | all listed (9 + 12 + 15 + 22 = 58), 0 failed/skipped | 58 | 10 |
| CP-11 | S3 | CP-10 | guards | /*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/* | R-8 | all listed (75 at f0e9ea9d31), 0 failed; at d2ff7e8d4 nine lane-xor reds for WriteBehindFileSystemTests (no category) are inherited, confirmed at the base before the row counts | 60 | 8 |

Notes. CP-1 is the red run: it is reported red by design and is not rerun as green; CP-3 is its green
counterpart after S2 and CP-10 the doc test's after S3. A red CP-1 that fails for a build error, a fixture
error or zero tests is not the positive control (CARD-0403). CP-2..CP-9 share one build (same `After`),
CP-11 reuses CP-10. `RunnerTaskSettlementTests` is split into CP-5 and CP-6 by the method prefixes it
already has so that no row derives a timeout above the 45 minute advisory; TestDesign checks the two
rosters sum to the class (29 methods after S1) and moves a method between rows rather than widening an
estimate. If any row is observed over 30 minutes, file the Debug task (R1) and split again by name.
CP-11 at the base shows nine inherited reds (one `lane-xor` for `WriteBehindFileSystemTests`, which
`Antiphon.Tests.csproj` links without a category; flagged by the CARD-1796 Code report): confirm them at
the base commit and report them inherited; a fix that lands first removes the note.
Code runs the table through `dotnet run --project tools/Antiphon.Checkpoints -- run --plan <this file>`
one committed slice group at a time, then `wait` until the exit is not 75; `import --plan` on this file
at plan time: see the Cost block.

### Cost

Ordinary Code floor: 12 + 8 + 10 + 10 + 11 + 11 + 15 + 6 + 12 + 10 + 8 = **113 minutes** of checkpoint
time, plus authoring (S1 90-120, S2 60-90, S3 60). `-ExpectAbout` for a Code dispatch of all three slices:
about 5.5 hours; S1 alone about 2.5 hours. The table was imported with the checkpoint tool at plan time
(`dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll import --plan <this file>`:
11 rows, exit 0, no advisory after the CP-5/CP-6 split).

## Performance budget

- **Selection.** Eleven rows, all `tests/Antiphon.Tests`, three cold builds (`bin-c1802-s1/`, `-s2/`,
  `-s3/`), Linux lane. Categories: `SettlementSyncDebtPolicyTests`, `RunnerBranchContractDocumentationTests`,
  `StandingPipelinePolicyDocumentationTests`, `LandGraphLandRefreshTests` are `Unit`;
  `RunnerTaskSettlementTests`, `SettlementSyncRecoveryTests`, `C0824RefOnlyReadTests`,
  `ReviewEvidenceResettlementTests`, `PendingSettlementDeliveryTests`, `BlockedTaskSyncRecoveryTests` are
  `Integration` + `Slow`; `DelegateScriptLandStatusTests` is `Integration`. Project graph: server +
  `Antiphon.Tests` only; no client, runner or messaging project changes, so no downstream batch beyond
  the guard row.
- **Durations and provenance.** No measured p95 exists for these classes in `tests/test-execution-policy.json`;
  the samples are checkpoint receipts from earlier plans on server2: CARD-1148 CP-17
  (`SettlementSyncRecoveryTests`, 24 results, no build, 14 min, 2026-10-08), CARD-1148 CP-5 (one
  `RunnerTaskSettlementTests` method plus a cold build, 8 min), CARD-1446 CP-5 (`SettlementSyncRecoveryTests/C1082*`,
  10 results, 8 min), CARD-1790 CP-2 (guard batch, 75 results, 8 min including reuse). Derived planning
  figures: cold `Antiphon.Tests` build about 7 min; `Slow` settlement-world tests about 35 s each
  (isolated schema clone plus three Git repositories per world); Unit doc tests under 1 s each. All row
  estimates are **provisional** under R5; the first Code run's `CHECKPOINT-TIMING` lines and TRX durations
  are the measurement, recorded in the task report, and TestDesign/Code refine the estimates from them.
- **Phases.** Build (cold, per `bin-c1802-*`) is reported by the tool separately from execution; slot
  wait is the `BUILD SLOT` line; Postgres is the shared per-run container (`TestDbFixture.Lifecycle`) with a
  cloned schema per world (`CreateIsolatedSchemaAsync`), so no row starts a container; Git fixtures are
  temp repositories created per test; no AppHost, web host or migration run.
- **Timeouts.** No new `[Timeout]`. The new tests use the existing bounded waits of their models
  (`FinishBusySettleAsync` on the controlled `SyncClock`, `WaitAsync(TimeSpan.FromSeconds(10))` as in
  `MintRefOnlyDebtAsync`, `RunnerSettlementSyncTests.SliceReturnGuard` for sweep slices); the controlled
  clock makes the 120 s budget instantaneous. Row timeouts are the importer's `max(15, 3 × EstimatedMinutes)`
  (45 min for CP-7, less for every other row; no import advisory); the total deadline is
  `DeriveTotalMinutes` (2 × 113 + 10 = 236 min) and is not lowered. These are implementation facts, not measured 2× p95; the exception is recorded here under
  R5 with this card as the tracking card until the first measured run replaces the estimates.
- **Over-30-minutes rule.** No row is estimated over 15 minutes. If any row is observed over 30 minutes,
  Code files the Debug task with the receipt (R1) and splits the row by method names.
- **Recording.** `rows/<CP>/run.trx`, `report.json` and the `CHECKPOINT` / `CHECKPOINT-TIMING` lines under
  `.antiphon/` (ignored), quoted unedited in the task report; `scripts/check-evidence-diff.ps1 -BaseRef
  d2ff7e8d4d63d67cf611b2c567c2da97416bd8fa -HeadRef <tip>` before the final report.
- **Setup sharing (R3).** Nothing new is set up per test beyond the existing world/fixture; the new tests
  reuse `RunnerSettlementWorld` and `RunnerSeatReleaseFixture` as built.
- **External calls (R6).** Git child processes through `LandingGit`/`RecordingGit` with the fixture's
  existing cancellation; Postgres through the isolated schema; no HTTP except the seat fixture's in-memory
  `SeatWire`.

## Activation and rollout

Master land, then the ordinary rc port (rule 9: a bug fix for code on the rc line). After the rc tip
activates, the next lease-busy observation settles with a debt row; confirm with one task detail
(`progressEvidence.remoteSync.state Pending`, `syncDebt.state Pending → Ready`) and the absence of new
`Runner sync unavailable: runner_sync_lease_busy` events in `GET /api/agent-tasks/{id}` events. The
investigation's census query (805 server2 Code tasks, one detail GET each) is the before/after
measurement. Nothing is deployed by this plan.

## Risks and notes for Code and Review

- `FailCommand` in the re-targeted `Sync_uncertainty_blocks_and_reply_retries` must match only the
  claim's `merge-base --is-ancestor C S` call: the pre-lease ancestry read is `merge-base --is-ancestor b S`
  and must succeed so `SourceDescends` is true. Use a two-commit push so C ≠ S.
- The seat fixture's `status` argument is used as `C1065_*` uses it; if the Succeeded settlement takes a
  different release path in the fixture than in production (`PoolDelegateRelease.CanRelease`), the fix is
  a fixture knob, reported, never a change to the rule.
- `RecordRemoteEvidenceAsync`'s exception fallback copies `prepared.Reason` (`runner_sync_lease_busy` for a
  Pending result) into the evidence; D-2 maps it to `runner_sync_progress_unverified` at `BlockReason`.
  Do not "fix" the fallback to say unfetched: an exception is not a fetch owed.
- Keep `SourceDescends` null for this arm. `RefOnlyCommittedSource`, `PendingLocalTipAsync` and the
  CARD-1480 base selection read `SourceDescends == true` as "server-read ancestry with local objects".
- The guard batch (CP-9) covers `SlowTestRegistry`/tripwire rules for the new `Slow` methods; if the
  registry needs rows for the new method names, add them in S1 with the tests.
