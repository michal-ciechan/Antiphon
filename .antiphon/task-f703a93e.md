# Design Review f703a93e (round 2): CARD-1593 capacity admission plan (subject 444ea5d9)

Reviewed: `docs/superpowers/plans/2026-10-27-card-1593-capacity-admission-lock-order-plan.md` at
`ba4bac4e70879794df23a1e2cf985c1f92ba967e`. Base is rc/2026-10-27 `f84228f285dd3e4ad7b5dc8260cbc9f819256f50`.
All line numbers are at the base. The tip contains only the plan, so I ran no tests: there is
nothing to build, and no CP row or V-n witness exists yet.
`scripts/check-evidence-diff.ps1 -Repository . -BaseRef f84228f2… -HeadRef ba4bac4e…` gave
commits=2, entries=0, violations=0 (exit 0).

**Verdict:** the direction still holds: task first, the provider lock last, consume and receipt
in the claim commit, and one rollback helper. The supervisor refutation is correct. Round 2
accepted F1-F4, but the fix introduces one new lock-order defect (F1). Two lifecycle
statements are wrong against the code (F2, F3), and two of the named positive controls cannot
go red (F4). Plan again before Code.

## Priority 1: the supervisor refutation holds (independently verified)

- `TryHandleCapacityWaitAsync` has one caller, `AgentSupervisorService.cs:230`, and
  `RedeemAsync` has one call in this file, `:507`. Every path into `:507` passes
  `await CompleteAsync(false)` at `:226`, which saves and commits (`:349-356`), and then
  `transaction.DisposeAsync()` at `:227`.
- The Herdr agent lock (`HerdrSupervisionStateService.cs:23-26`) joins the ambient transaction,
  so the commit releases it.
- Between `:227` and `:507` there are only unlocked reads (`:473-505`). `RedeemAsync` opens its
  own scope and transaction (`CapacityRecoveryService.cs:245-248`).
- After the redeem, `:520-522` saves through autocommit and holds no provider lock. Then
  `StartSupervisedRestartAsync` -> `AgentControlService.StartAsync` takes only in-process queue
  gates first, then DB locks on its own single connection: sessions, then agent, then an
  `ExecuteUpdate` of the agent's wait row at `:808`. It takes no provider lock.
- The tick (`:77-118`) runs with no outer transaction.
- Conclusion: there is no cross-connection cycle between the supervisor and a standing or
  pinned claim. Any cycle among these locks is a single-database lock graph, so PostgreSQL
  detects it (`40P01`). L16 is a lifecycle leak only, as the plan says.

## Findings (design defects)

### F1: rank 4 (physical agent) before rank 5 (sessions/releases) inverts the order the code chose on purpose (CARD-1190), and S1 adds new explicit 4->5 sequences
- **Where:** D-1 rank table; L7 S1 sequence (`LockReleasedAnswerAsync` gains agent between task
  and release); L1w S1 sequence (agent `FOR UPDATE` then reused session `FOR KEY SHARE`).
- **Code that locks session before agent, deliberately:**
  - `AgentControlService.cs:543-548`. The comment reads: "Sessions before the agent. Launch-failure
    cleanup … already holds that order; the agent lock first deadlocks it (40P01, CARD-1190)".
  - The stop path at `AgentControlService.cs:1276-1283`: session `FOR UPDATE`, then
    owner/agent `FOR UPDATE`.
  - `AgentService.cs:942-950`, in the same shape.
  - `AgentSessionService.cs:410-417`: "Session before agent, matching CloseSessionOnExitAsync".
  - The exit observer at `AgentSessionRuntime.cs:213` (session `FOR UPDATE`) -> `:266-279`
    (agent write).
  - `TerminalRunnerSeatReleaseService.cs:872-878` (task, release, session) -> `:956` (agent
    `ExecuteUpdate`).
- **What the table records instead:** L21 lists `AgentService.cs:943/950` and
  `AgentControlService.cs:1277/1283` as `1->2/4`. They are 5->4 (or 5->2). L20 and L25 record
  the TRSR and exit-observer rows as `3 (+5)` and `5` with no agent write.
- **Failure:**
  - **Warm reuse.** S1 warm reuse locks pool agent X `FOR UPDATE`, then X's live session S
    `FOR KEY SHARE`. A concurrent stop of X (operator stop or idle-pool reclaim through the stop
    path) holds S `FOR UPDATE` and waits for X. That is a cycle, so `40P01`.
  - **Continuation.** The S1 continuation locks task A, then X, then release R, then S
    `FOR UPDATE`. A stop of X holds S and waits for X, so this also deadlocks. At base the
    continuation took S before it wrote X, so these were not cycles.
  - **Where the victim lands.** A victim in the claim's new pre-lock statement is outside D-9,
    which covers only the consume step. It propagates to the tick's generic catch
    (`AgentTaskDispatcher.cs:1120-1130`), and that catch fails a `Queued` task.
  - **Ledger.** The S2 `LockOrderLedger` would also throw on every stop, exit-observer and
    launch-failure path.
- **Fix:**
  - Put sessions and releases before the physical agent: 3 task -> 4 release/session (incl.
    `KEY SHARE`) -> 5 physical agent -> 6 parallel -> 7 provider -> 8 capacity rows.
  - L7 becomes A->R->S->X, which matches TRSR `:872-878`->`:956`.
  - Warm reuse takes S `FOR KEY SHARE` first, then X `FOR UPDATE`, and rechecks both
    `Idle`/`PoolIdleSince` and `X.PersistentSessionId == S`.
  - Cold launch locks no existing session, so it only moves its agent lock's rank.
  - Re-record the 5->4 sites listed above in L21/L25/L20.
  - Note as pre-existing, and detectable, the owner(2)->session pairing against the stop path
    (`:1276-1283` locks session, then owner) for standing placement L1s.
  - Extend D-9: a `40P01`/`55P03` from any S1 pre-lock statement abandons the claim
    (`NotClaimed`, reservation intact) instead of reaching the generic catch.
  - Add a witness: a V-10 case `stop_vs_warm_reuse` (stop holds S, claim reuses X), with no
    `40P01` and the claim either reusing or falling back. Its PC is the plan's current
    X-before-S order, which must produce `40P01`.

### F2: grant expiry is enforced only by the reconciler, not by the gate; c5, D-9 and D-10 misstate it, and V-4 c5 as written cannot pass at S1
- **Where:**
  - D-3 c5 says "the `consume:false` gate sees `GrantExpired` (`:1015-1017`) and revokes the
    grant through `RearmRevokedGrantOnAsync`".
  - D-9 lists "grant expired" as a consume refusal.
  - D-10 says "while a grant is reserved, no other waiter of that provider is granted".
  - V-4 c5 says to "run the gate tick (expiry re-arm)".
- **Code:**
  - `RedeemAsync` (`CapacityRecoveryService.cs:237-301`) never calls `GrantExpired`. It checks
    only the grant id and key, `NextAdmissionAt` and the refusal reason.
  - Expiry is handled only in `GrantReadyAsync` (`:148-158` -> `StillHoldsGrant` `:996-1000` ->
    `RecordRevokedGrantAsync` `:1019-1027` -> `RearmRevokedGrantOnAsync` `:1042-1062`). That
    runs on the reconcile loop (`ReconcileAsync` `:47-55`), independently of the dispatch tick.
- **Failure:**
  - **V-4 c5.** As written, the gate tick does not re-arm, so the launch tick consumes the
    expired but unrevoked grant. `ActionOrdinal` and `ActionKey` do not move, and the planned
    assertion fails at S1. Code then either changes semantics without a plan, or edits the test.
  - **D-10.** The reconciler revokes W during a long server2 preparation. Re-arm sets `DueAt`,
    and grant order uses `DueAt` for `grant-expired` (`:177-179`). So the provider is regranted
    to *other* waiters while W prepares. D-10's "one launch per preparation window" is wrong:
    W can lose its slot and wait one or more further cycles.
- **Fix:**
  - Name the reconciler as the expiry actor.
  - Decide explicitly whether `RedeemOnAsync(consume:true)` refuses an expired grant that has
    not been revoked yet. I recommend that it does not: under the provider lock a matching grant
    is safe to spend. Then drop "grant expired" from D-9.
  - Rewrite V-4 c5: advance past `StalledAdmissionTimeout`, run `GrantReadyAsync` (revoke and
    re-arm), advance past `DueAt`, run `GrantReadyAsync` (regrant), then run the launch tick.
  - Also assert that the post-prep tick does not request preparation again. This is the
    progress guarantee: a completed preparation survives a regrant.
  - Correct D-10's throughput statement.
- **Livelock (Q4):** none, as long as a recorded mirror stays usable across the regrant. Each
  waiter's preparation then completes once and its next grant launches. Code should confirm
  that the `HeldForRemotePrep` result is not invalidated by the extra about 2-4 minutes.

### F3: D-9's "a deterministic failure cannot loop forever" is false
- **Where:** D-9, first bullet. Every exception before the commit is routed to abandon, with
  the task left `Queued` and the reservation intact. That includes the Rule P
  `InvalidOperationException` and any non-transient receipt-save `DbUpdateException`.
- **Failure:**
  - Expiry re-arm never increments `AdmissionCount`. The plan itself says "churn from expiry
    never exhausts a wait", and `AttemptWouldExhaust` reads `AdmissionCount` only.
  - So a deterministic consume failure repeats forever. One example is a claim path that leaves
    a dirty `Agent` entry, which trips the Rule P assertion every time.
  - Each time W is regranted, every tick re-runs the whole claim (worktree add, session
    allocation) and fails again. The provider is held for up to `StalledAdmissionTimeout` per
    cycle. The task never fails, and only a deduped Warning shows it. At base, the same bug
    fails the task.
- **Fix:**
  - Keep `Queued` only for refusals (grant mismatch, wait no longer `ActionPending`) and for
    transient SQLSTATEs (`40P01`, `40001`, `55P03`), plus the lost-ack reread.
  - Fail every other exception through the existing failure path and release the reservation
    (`ReleaseClaimReservationOnAsync`), or bound consecutive consume failures per wait.
  - Make V-14's `ThrowOnReceiptSave` throw a transient-typed fault (or say which class it uses).
  - Add a V-13 argument `deterministic-consume-failure` that expects the bounded outcome.

### F4: two named positive controls cannot go red, and the V-10 PCs are timing-dependent
- **V-8 `claim-agent-row`:** the PC is "consume before the flush (round 1 order)". The S1
  explicit rank-4 agent lock still runs before `RefuseParallelAsync` and before any consume. So
  under that mutation the claim still blocks at the explicit lock, holding neither the parallel
  key nor a provider key, and the test stays green.
  - *Fix:* use PC (a), deleting the explicit agent pre-lock, which turns the parallel-key
    assertion red. For the capacity-key assertion, use PC (b): delete the pre-lock **and** move
    the consume before the flush.
- **V-8 `continuation`:** the argument's test connection holds the **task** row. Dropping the
  S1 agent lock (the PC) still blocks the continuation at the task lock first, so the test stays
  green. The PC text assumes "the test holding the pool agent row", which is not this argument's
  setup.
  - *Fix:* add an argument `continuation-agent-row`, where the test holds pool agent X, and
    attach the PC to it. After F1 the S1 sequence is A->R->S->X, so the argument blocks at X with
    no provider held.
- **V-10 PCs:** "barrier-released at the lock step" does not guarantee that the continuation
  (or reroute) already holds the provider when the claim requests it. The `40P01` then depends
  on timing.
  - *Fix:* add a second boundary right after the non-claim path acquires the provider (inside or
    after `SupersedeTaskWaitsOnAsync`). Release the claim only once `pg_locks` shows that pid
    holding the kind key.

## Answers to the other priorities (advisories)
- **Priority 2 (after the consume):**
  - The design is sound. `RedeemOnAsync` saves on the claim context inside the ambient
    transaction. The claim's `CommitAsync` follows directly (`:6150`->`:6151`, `:5706`->`:5707`),
    so `Dispatched`, the session insert and the receipt commit together.
  - The wait has no FK to `AgentSessions` (snapshot `:11637-11640`), so the receipt takes no
    `KEY SHARE`.
  - The enqueue, the brief queue and `DeliverReuseMessagesAsync` all run after the commit and
    hold no DB lock. The dispatch-warning intents are rows on the same context
    (`DispatchBaseWarningIntentService.cs:18-60`), not another connection.
  - Code must not rely on the existing catch at `:6153`. It is filtered to
    `Worktree && SourceLandingOperationId is null`, and warm reuse `:5706` has no catch at all.
    The D-9 handling needs its own unfiltered try around the consume and the commit.
- **Rule F, gap in the stated invariant:** after rank 6, the cold-launch session insert takes FK
  `KEY SHARE` on `Cards` and `Worktrees` (snapshot `:10385-10400`). That conflicts with the
  `Cards FOR UPDATE` holders: `PipelineHandoffActionService.cs:33/:159`,
  `CardCompletionAcceptanceService.cs:94`, `CardWorktreeCleanupExecutor.cs:80`,
  `CardWorktreeMirrorCleanupService.cs:100`.
  - These lock non-`Queued` tasks after the card, so no cycle forms, only short waits.
  - Cards (card->action->task, L14) are absent from the rank table. Add them as rank 0 and
    state the FK `KEY SHARE` exemption in Rule F, or the S2 ledger will flag it.
  - L22 also says `WorkspaceReservationJournal.cs:253` is "never with ranks 3/7", but the
    cleanup executor takes admission advisory -> card -> task (`CardWorktreeCleanupExecutor.cs:79-85`).
    This is n/c, but the description is wrong.
- **Priority 3 (spot checks):** these are correct to within ±2 lines: L1 `:5530/:5535/:5539/:5543`
  (the plan says `:5532` for the transaction; it is `:5530`), `:6098/:6100/:6150/:6151`,
  `:7447/:7457`, L1w `:8097/:8203/:8231-8235/:8244/:8245/:8247`, L1s `:8400/:8403/:8482/:8502`,
  L3 `:1682/:1685`, L4 `:1826/:1842-1846`, L7 `:3632-3643/:3763/:3845/:3881/:3951/:3997/:4004`,
  L9 `:3792`, L10 `:2949`, L23 `AgentTaskReplyService.cs:121/:1991`, L24 `AppDbContext.cs:163-164`,
  and `FinishParallelHoldAsync` `:8003-8031` (which also serves `FinishParallelReuseAsync` `:7997`).
  - **Cancel (L10):** it conforms, and S1 improves it. A cancel-time supersede now makes the
    claim's consume refuse instead of racing the receipt.
  - **Pre-existing, outside the plan's scope:** cancel writes a `Queued` task with no task lock
    and can overwrite a concurrent `Dispatched`. S2's task lock covers it.
- **Priority 4 (D-9 lost acknowledgement):** if the client errors before the server applies
  `COMMIT`, the reread can see `Queued` and the commit can land later. The task then ends up
  `Dispatched`/`Starting` with no enqueue. Interrupted-launch recovery covers that state, and
  the next claim's `Queued` filter refuses it. This is safe; say so in D-9. `AbandonClaimAsync`
  must also tolerate a throwing rollback after a failed commit.
- **Priority 5 (budget and slice):**
  - **Counts:** V counts check out (17 lifecycle + 12 lock-order = 29), and CP-3's 15+15+6 = 36
    stands. F4 adds 1 lock-order result, and F1/F3 may add 1-2 more, so update `Min`.
  - **Slice:** S1 is still the smallest slice for C508, provided F1 is fixed in it. The
    residual-risk paragraph is accurate for L6/L16/L17 once the F1 deadlocks are removed.
  - **Budget:** the timings are plausible for server2: CP-1 about 15 min including the build,
    and the whole round under 30 min.
  - **Determinism:** the witnesses are deterministic where they wait on `pg_stat_activity`
    (V-8, V-9). V-10 needs the F4 boundary.
