# Review 8f6e5b17: CARD-1593 S3 atomic-consume plan, design review round 2

Subject task f264f937-96d0-416d-bef6-ad3d6a293ee0, which continues 62fb542c. Branch
`feat/card-task-f264f937`, tip `733f413acc93b79b324ea38972a78aa33bccf117`. Plan:
`docs/superpowers/plans/2026-10-28-card-1593-s3-atomic-consume-plan.md`.

Base is `rc/2026-10-27` `aa6dfd24a`. The plan's touched files are byte-identical at `origin/master`
`fe73cfd2e`: `git diff --stat` on the five services, `CardService`, `SupervisionSettings`,
`SessionReconciliationService`, `AgentSupervisorHostedService` and `tests/Antiphon.Tests/Application/`
is empty. All file:line references below are at that base.

## Verdict

**Not yet approved for Code. There are 4 blocking defects:**
- **Round A** (S3a + S3b): 3 small ones, D-1 to D-3.
- **Round B** (S3d): 1 design decision, D-4.

Round-1 D-1 to D-5 are really fixed in the amended text, but D-3 (S-4) has no witness for its
central mechanism.

**Next stage: a plan amendment.** It is narrow: D-1 to D-3 are wording plus 3 witness
arguments.
- After that, Round A is mechanical enough for a Grok Code task followed by an Opus Review.
- D-4 can be deferred to a Round B amendment. It need not hold Round A.

**What ran:** `scripts/check-evidence-diff.ps1 -Repository . -BaseRef aa6dfd24a… -HeadRef 733f413ac…`
reported commits=4, entries=0, violations=0, exit 0.

**What did not run:** nothing was built or tested. This is a design review, and no claim needed
a build.

I re-counted the cases at base with a static per-partial count of `[Test]`/`[Arguments]`:
- `StandingRestartEscalationTests` 52 (1+2+1+1+22+2+18+4+1);
- `AgentSupervisionTests` 10;
- `CapacityRecoverySupervisionTests` 6.

These match CP-4 and CP-5. CP-4's 14 new cases plus V-S3-11's 1 make the stated S3b total of 15.

## Round-1 defects: verification against current code

| Round 1 | Status | Evidence |
|---|---|---|
| D-1 retained strand | **Fixed** (Round B) | D-S3-3 steps 2 and 4 skip the consume when the wait is null or the feature is off. This matches base `AgentTaskDispatcher.cs:633-635` and `:1756-1757`. The task-only recheck, the `WaitingForHold` gate and the AsNoTracking grant-match gate (`:1764-1771`) are kept. V-S3-6b and V-S3-6c are the witnesses. |
| D-2 Enter-only without spend | **Fixed** (Round B) | The consume goes immediately before `SendInputAsync("\r")` at `SessionMessageQueueService.cs:3426`, after every non-delivering exit (`:3388`, `:3407`, `:3416`, `:3419-3420`). The caller split is correct: `:2701` and `:2684` (the 5-argument `RecoverPointerAsync`, `CodexPointers.cs:128`) sit behind the gate at `:2579`. `:1508` and `:3345`/`:3358` use the 4-argument overload and pass null. |
| D-3 S-4 lost ack | **Fixed in text, unwitnessed** | The procedure is correct. The reservation holds the agent row through `LockAgentAsync` `:549` (`FOR UPDATE`, `:1492-1496`), so a fresh-scope `FOR UPDATE` waits the doubt out. The committed test (pointer, plus session `StartedAt` == the accepted generation) distinguishes the resume arm, where the id is the same and `StartedAt` moves at `:656`. The enqueue arguments are all in scope. **But nothing can go red if the `FOR UPDATE` is dropped:** see D-2 below. |
| D-4 exit table | **Fixed**, with one misclassification (D-1) and a placement gap (D-3) | (a) S-2h, (b) the catch placement, (c) S-T, (d) S-C and (e) the tracker clear are all present. Correct sites: the hold catch is at `AgentSupervisorService.cs:232` and the generic catch at **`:240`** (the plan says `:236`, which is a comment line). The non-reserve `StartAsync` is at **`:535`** (the plan says `:531`). Its filter at `:547-551` lists `standing_resume_*` only, so the outer placement is right. |
| D-5 coverage | **Fixed** | CP-5 runs both whole classes (62). Witnesses exist for the card arm, S-4, `capacity_recovery_suspended` and the remote retained return. V-S3-11 now asserts that nothing is written after the consume. |

## Blocking defects

### D-1 (Round A): a grant race at the fresh-reservation pre-check cancels the wait
**Where:**
- the plan, D-S3-2 "Pre-check" and the D-S3-5 S-2 row (`standing_automatic_fresh_*`);
- the code, `AgentControlService.cs:606-613`.

**Failure:** the plan keeps the pre-check's code `standing_automatic_fresh_capacity_required` but
turns it into the "grant still matches" check. S-2 classifies every `standing_automatic_fresh_*`
as durable, which leads to `ReleaseAgentReservationAsync` cancelling the wait. So a grant that is
revoked or moved between the supervisor's grant-match read (`AgentSupervisorService.cs:500-505`)
and the reservation's pre-check is handled as S-2:
- the wait is `Canceled`;
- the next tick takes the ungated restart.

The same race, when it is detected one step later at the consume, is S-1: no release, and the
reconciler owns the wait. Composition, `ListAsync` (`:527-528`) and the queue-lock wait all sit
inside this window. One way the grant moves is a queue head stamped with the same wait consuming it
(Risk 4).

The plan also does not say which of the current clauses survive:
- `intent.CapacityRecoveryActionKey != wait.ActionKey` (`:610`) **must** go. The supervisor no
  longer pre-stamps the intent (`:521` is deleted), and `AcceptAsync` stamps it only after the
  pre-check. If the clause stayed, every capacity fresh start would throw.
- `wait.State != Admitted` / `OutcomeReason != Start` (`:611-612`) must become the grant check.

**Why it blocks:** this is pacing loss through a race exit, the class D-4 was about. A Code author
would have to decide the classification.

**Fix:**
- A provider grant mismatch at the pre-check throws `capacity_consume_refused` (S-1).
- `standing_automatic_fresh_capacity_required` stays only for the durable mismatches: no unfinished
  wait, `ActionKey != supervisedFresh` key, or `ExecutionKind != spec.Kind`.
- Delete `:610`.
- Add a V-S3-4 argument `precheck-revoked`: revoke at a boundary before `:602`. Assert the wait is
  not `Canceled`, the grant was revoked by the test, `AdmissionCount == 0`, there is no backoff and
  the tracker is clean.

### D-2 (Round A): the S-4 `FOR UPDATE` has no red witness
**Where:** the plan, V-S3-5b and its PC.

**Failure:** `committed` throws from `TransactionCommittedAsync`, after the server commit, so a
lock-free reread also sees "committed". `not-committed` aborts before the commit, so a lock-free
reread also sees "not committed". The PC replaces the whole reread with "assume committed". That
proves a reread exists. It does not prove the reread waits out an in-doubt `COMMIT`, which was the
whole of round-1 D-3. A Code task that drops `FOR UPDATE` passes every listed test.

**Fix:** add V-S3-5b `[Arguments("in-doubt")]`.
1. At `TransactionCommittingAsync` of the reservation, a test connection opens a transaction. It
   takes the agent row `FOR UPDATE` and writes the accepted pointer plus the session row at the
   accepted generation, standing in for the in-doubt commit. The interceptor then throws `XX000`,
   so the reservation does not commit.
2. Poll `pg_stat_activity` until the reread backend waits on a `Lock`.
3. Commit the test transaction.
4. Assert exactly one enqueue with the accepted generation, no backoff, and `AdmissionCount`
   unchanged by S-3.
5. PC: drop `FOR UPDATE`. The reread sees "not committed", so S-3 records a backoff and there are
   zero launches: red.

### D-3 (Round A): the card-arm consume has no placement and no throw rule
**Where:** the plan, D-S3-2 "Card arm", and the code, `AgentControlService.cs:302-326`.

**Failure:** `CardService.SpawnAsync` (`CardService.cs:1601`) does not set the agent pointer.
`StartCoreAsync` sets and saves it after the spawn, at `:322-326`. The plan says only "after
`SpawnAsync` returns" and "a refusal is logged", and says nothing about a **throw**: a transient,
a connection loss or a 40P01 in its own 7->8 scope.

If the consume sits before `:326` and throws:
1. The launched card session is left with `PersistentSessionId` and `Status` unsaved.
2. The exception reaches S-3. `RecordStartFailureAsync` `:384` cannot see the launch as owned,
   because the pointer is stale, so it records a backoff, and the charge consumes.
3. After the re-arm the supervisor regrants and calls `ResolveStartCardAsync`/`SpawnAsync` again
   for an agent whose card already has a running session.

**Fix:**
- Run the consume after the `:326` save, before `:328`.
- Catch every non-cancellation exception, log at Warning, and let the launch stand. This is the
  same under-spend direction as Risk 3.
- Add a V-S3-5c argument `consume-throws`: one spawn, the pointer saved, no backoff,
  `AdmissionCount == 0`.

### D-4 (Round B, S3d): queue-row re-key under the provider lock against the `Sent` transaction
**Where:**
- the plan, the D-S3-7 row L17' ("no 7 -> 4 against this 4 -> 7 forms").
- the code, `CapacityRecoveryService.cs:141` -> `:157` `RecordRevokedGrantAsync` -> `:1188`
  `RearmRevokedGrantOnAsync` -> `:1215` -> `PrepareNextAttemptOnAsync` `:1401-1407`.

**Failure:** L17' rules out the cycle only through `RearmStalledAdmissionsAsync`, which skips
receipt-backed `Admitted` waits. It misses the grant-**expiry** path. `GrantReadyAsync` revokes an
expired grant on an `ActionPending` wait: `IsGrantCandidate`, not revalidation. It then re-keys
every `SessionQueuedMessages` row with the old key, and it does so while holding rank 7. The wait
being consumed at the `Sent` stamp is exactly that `ActionPending` stamped wait. The stamp does set
`SelectedMessageId` (`:983`), but the expiry path does not consult it.

The `Sent` transaction holds the head row (rank 4) and then waits on rank 7. A reconcile tick at
grant age ≥ 120 s holds rank 7 and waits on the head row. D-12 explicitly allows a consume on an
expired-but-unrevoked grant, so this window is reachable. PostgreSQL detects the cycle (40P01),
and the outcome is bounded either way:
- if the flush is the victim, class 2 returns `Nothing`;
- if the reconciler is the victim, `AgentSupervisorHostedService.cs:146-148` share one `try`, so
  that tick's supervisor pass and later sweeps are skipped.

It is still a rank-table violation that the plan denies. The other design in session 0cd4407b
flagged the same risk, and the amendment did not take it up.

**Fix (a design decision for the Round B amendment):** either
- (a) take the re-key out of rank 7: match the queue consume on
  `CapacityWaitId`+`CapacityWaitVersion` rather than on message-key equality, or re-key in a
  separate transaction after the provider commit; or
- (b) accept the detected deadlock explicitly. That means a witness for both victims, and moving
  `ReconcileAsync` into its own `try` so that a 40P01 victim does not skip the supervisor tick.

Correct L17', and the S1 L18 "conforms" note it inherits.

## Advisories (non-blocking)
- **A-1, S-2h (the question in the brief).** There is no re-grant loop.
  - Both hold codes are reachable only by a race. The early returns at
    `AgentSupervisorService.cs:132` and `:147` precede the capacity branch.
  - After the release cancels the wait, the next tick finds no unfinished wait (`:209-212`). The
    ungated path returns at `:132`/`:147` while the hold lasts.
  - A new wait needs a new wall observation, and a dead, held agent cannot produce one.
  - Add S-2h to the tracker-clear rule. The `:585`/`:587` throws roll back and then reach
    `ObserveAsync` on `_db`.
- **A-2.** State two things:
  - where `ReleaseAgentReservationAsync` is called for the codes handled inside
    `StartSupervisedRestartAsync` (after `held == true`, `:570`);
  - that a `standing_resume_*` rethrown at `:569` (`!held`) is S-3, which is base behaviour.
  The catch-order item 3 wording "not already handled" is ambiguous for that case.
- **A-3.** S-3 for a `ModelDisabledException` from `:249`: `RecordStartFailureAsync` (`:385-400`)
  links a hold first, so the charge after it will usually refuse (`not-action-pending`), and nothing
  is spent. That is acceptable. Name it in the S-3 row.
- **A-4, V-S3-11 `supervisor-start` adequacy.** With the agent row held, the path blocks at
  `LockAgentAsync` `:177` (autocommit, before the reservation). The capacity-key assertion therefore
  cannot go red under the stated PC; only the nothing-after-consume half can. Hold the involved
  `AgentSessions` row instead, which is locked first inside the reservation (`:547`).
- **A-5, L16' cross-process note.** The reservation's transferred pending rows (`:822-831`) may
  carry a stamped key that the reconciler re-keys under rank 7. In-process, the reconciler and the
  supervisor run serially on one hosted loop (`:146-148`), so only a second server on the same
  database could collide. That would be a detected 40P01, which S-T handles. Record it in L16'.
- **A-6, Risk 12 and the bound on spending before Enter (the question in the brief).**
  - Spending before Enter is acceptable. It is base-equivalent, because base spends at `:2586`
    before Enter-only.
  - The "bounded by `MaxAttempts`" claim is wrong. After the first consume the wait re-arms as
    revalidation, which never increments. Enter-only also never charges `DeliveryAttempts`
    (`:3380-3477`).
  - So a repeating Enter-failure cycle regrants every 120 s with no attempt bound, exactly as at
    base (round-1 A-3). Correct the text.
- **A-7, S-T.** A `55P03` can repeat for as long as a foreign lock holder persists. With no backoff
  that costs one reservation per tick until D-12. "Cannot repeat on its own" is too strong.
- **A-8, line drift.** Corrections:

  | Plan says | Actual line |
  |---|---|
  | `:236` | `:240` |
  | `:531` | `:535` |
  | `AgentControlService` `:176` | `:177` |
  | `:886` | `:887` |
  | `:806-816` | `:808-816` |
  | `AgentSupervisorService` `:127` | `:126` |
  | `:510-522` | `:512-522` |
- **A-9, evidence path.** The checkpoints run `dotnet exec` and report CHECKPOINT-shaped lines
  under the S1 D-11 reader cap, with no sealed receipts. Re-check whether the cap still applies.
  Without receipts a Code Review cannot report `reviewedSourceClean: true`.
- **A-10, Round C.** The other design releases and reacquires the grant, where this plan renews
  `GrantedAt`. With the cap and default 0, renewal is acceptable. Decide this at the Round C
  amendment, not now.

## Round A readiness

After D-1 to D-3 it is mechanical. These are the exact open decisions, all resolvable in the
amendment:
1. The pre-check classification and which clauses survive (D-1).
2. The `in-doubt` S-4 witness (D-2).
3. The card-arm consume placement and its throw rule (D-3).
4. The release call site and the classification of a rethrown `standing_resume_*` (A-2).

Nothing else needs design. The lock order for L16', L16c and L16f conforms in-process:
- the S-4 reread is a rank-5 leaf whose only wait is on the in-doubt holder;
- it causes no deadlock with an `AgentControlService` start, because queue locks are taken in
  sorted order (`:522`, `:542`) before any database lock.
