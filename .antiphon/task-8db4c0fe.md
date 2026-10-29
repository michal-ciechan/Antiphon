# CARD-1603 plan re-review, round 2 (task 8db4c0fe)

Subject: plan task `c404bdc3-beaf-499d-b9a9-bbeeaed5a122`, branch `feat/card-task-c404bdc3`, tip
`22a7a05cd6c9149e8d4be7e67cf5a373cfc2d6c5`,
`docs/superpowers/plans/2026-10-29-card-1603-stop-capacity-grant-cycling-plan.md`.
I read the product code at the inspected base `f8bddb6ef` (= `origin/master`) and at S3-A tip `6564c5757`.
`SessionMessageQueueService.cs` and `CapacityRecoveryPolicy.cs` are byte-identical at those two refs.

**Verdict: needs one more plan revision.**

- The standing half, the roster, the classification and budget, the lock order, the CAS and the
  dependency ordering are sound.
- The queue half still has one producer that production cannot reach. I introduced that producer
  myself in round 1, and I correct it here.
- The plan also leaves a parked-selection cycle unfixed.
- The new demand check in outstanding-grant validation can strand later input.

**Checks run:**

- `check-evidence-diff.ps1 -BaseRef d553e7010 -HeadRef 22a7a05cd`: 2 commits, 0 violations.
- `git merge-tree --write-tree origin/master 22a7a05cd`: clean. The only changed file is the plan.
- No builds or tests were run. The subject is a plan, so there is nothing executable.

## Defects

### D-1 (major): the parked-stale producer is unreachable for Channel/Scheduled, and the parked selection still cycles

**Where:**

- Ground truth row 2, and D-2's "Existing selected Pending ... keep their semantics".
- V-1's `parked-stale` arm.
- The CARD-1603 / S3 Risk 8 replacement wording.

**Why the producer is unreachable:**

- `CancelParkedIfStaleAsync` (`SessionMessageQueueService.cs:1391`) has one production caller,
  `ParkedMessageSweepService.ScanAsync` (`ParkedMessageSweepService.cs:49-102`).
- That caller filters `Origin ∈ {Delegation, System, Check, Supervision}`, plus `SourceTaskId == null`
  and no active task.
- A Channel or Scheduled row is therefore never parked-stale-canceled. Only a human Drop cancels it.
- I listed this canceller as a real producer in round 1 (b7762633 D-1). That was wrong.
- V-1's parked arm "drives failed attempts to the existing cap then calls CancelParkedIfStaleAsync" on
  a Channel row. That is a test-only transition, the same class of defect as round 1's
  `StampQueueActionAsync`.

**Unfixed cycle:** the state the arm builds just before its cancel really happens in production, and
nothing ends it.

1. Selected X has failed `MaxAttempts` deliveries and is still Pending and keyed. The wait is Admitted
   with `SelectedMessageId`, which counts as a receipt.
2. The flush's `deliverable` filter (`:2417-2419`, `DeliveryAttempts < MaxAttempts`) hides X from
   `ApplyCapacityHoldAsync` and from redeem.
3. `RearmStalledAdmissionsAsync` sets `NeedsRevalidationGrant`. Grant, no flush delivery, D-12 expiry.
4. `RearmRevokedGrantOnAsync` (revalidation branch: version++, `DueAt`) re-grants.
5. This repeats forever with no admission spent. The provider is reserved 120 s per cycle until a
   human drops X.

D-2 keeps this shape as a receipt, so the plan does not fix it. The demand predicate ("Pending
Channel/Scheduled row ... unkeyed, or linked to this wait") also counts an unselected parked row, or a
row with a future `HoldUntil`, as demand. The same no-delivery grant then cycles before redeem
(re-keyed, count 0).

**Fix:**

- Replace the `parked-stale` arm with a real automatic producer. The best one is schedule
  supersession:
  - `ScheduleService.cs:298` calls `CancelPendingBySourceScheduleAsync`, and through it
    `CancelPendingIfUntypedAsync`.
  - This cancels the held (possibly selected, untyped) Scheduled copy when the schedule fires again.
    It then enqueues a fresh copy, which also exercises later-input delivery.
- Add `CancelPendingBySourceScheduleAsync` to Ground truth.
- Define queue demand with the flush's deliverability rules: `DeliveryAttempts < MaxAttempts`, and
  `HoldUntil` null or due. Excluding `LegacyUnclassified` maintenance is optional.
- Decide the parked-selected case. Either:
  - treat a selected Pending row at the attempts cap as non-demand (no revalidation grant until a human
    Drop or Send-now); or
  - scope it out explicitly, file a card for it, and state its unbounded cost.
- Add a V-12 arm `parked-only`, plus a V-1 or V-11 arm for the parked-selected disposition.

### D-2 (moderate): demand loss in outstanding-grant validation reuses a revoke path that strands the wait

**Where:** D-2 says "Add one shared queue-demand predicate to GrantReadyAsync, both its candidate query
before Take(batch) and outstanding-grant validation". The plan never gives the disposition.

**How today's revoke path strands the wait:**

- A failed validation goes to `RecordRevokedGrantAsync` (`CapacityRecoveryService.cs:1282-1302` at
  S3-A).
- For a grant that has not expired, an ActionPending/Ready/Admitted wait becomes `Deferred` with
  `OutcomeReason ??= "grant-revoked"`. Any earlier reason survives, for example `grant-expired` or
  `new-wall-wave`.
- No path in `CapacityRecoveryService` returns Deferred to Ready. The only transitions to Ready are
  WaitingForHold→Ready (`:100`, `:1391`, `:1406`) and `PrepareNextAttemptOnAsync`.
- GrantReadyAsync candidates exclude Deferred.
- D-2's cleanup re-readies only Deferred waits whose reason is exactly `grant-revoked`, and only when
  they have a canceled selection.

**Scenario:**

1. Wait W is granted (ActionPending, not expired). Its only held row Y is still unselected.
2. A human drops Y (`CancelAsync`), or a schedule refire supersedes it.
3. The next GrantReadyAsync finds no demand, revokes the grant, and W becomes Deferred.
4. Later input Z arrives. `ApplyCapacityHoldAsync` finds W unfinished but not granted, so Z is held.
5. W is never a candidate again, and the cleanup path ignores W because it has no canceled selection.
6. Z waits for a human prompt.

At base, W expires, is re-armed Ready and is re-granted, so Z would be delivered. This is a regression
the revision introduced.

**Fix:**

- Specify the outcome when an outstanding grant loses demand. Either:
  - clear only the grant and leave the wait Ready (or re-arm it through `RearmRevokedGrantOnAsync`)
    with no Deferred state; or
  - don't add demand to outstanding validation, and let D-12 expiry re-arm it once. The candidate
    filter then stops the re-grant.
- Add a V-12 or V-11 guard arm `granted-then-demand-lost`: Drop unselected Y while granted, then
  enqueue Z. Assert that the same wait delivers Z on a later grant. Its PC routes revocation through
  today's Deferred branch.

## Advisories

- **A-1: V-1 before-redeem arm.** A natural production path exists, so a SaveChanges interceptor is not
  needed. When `pending[0]` is a non-held head row, for example Ui or Supervision, the redeem at
  `:2578-2589` is skipped. The selected X then stays keyed and unredeemed after the flush, and a human
  Drop follows. The interceptor's crash boundary is legitimate, but the natural path is the stronger
  producer-to-recipient witness. Consider both.
- **A-2: V-1 spill arm.** It is a real production cancel. `HoldSpillBodyForDeliveryAsync` at `:2675`
  runs after selection (`:2575`) and redeem (`:2586`), so the plan's count of 1 at S3-A is right. Say
  where an owned pointer with missing bytes comes from in production, such as a legacy row or lost
  staged bytes, since the test seeds it.
- **A-3: roster.** `WallRerouteDispatchTests` seeds a LiveSession wait with a Queued TaskId and drives a
  dispatcher tick. That is exactly the "independently redeemable" exemption the new predicate must
  keep. Add it or justify leaving it out. The other unlisted capacity callers seed QueuedTask waits
  (`AgentTaskDispatchBaseGuardTests.Capacity`, `ReceiptFailureDeliveryTests`, and WallReroute
  `:161-182`/`:271+`), which the predicate does not touch.
- **A-4: wording (item 4).** In the replacement text:
  - replace "parked-stale cancellation (CancelParkedIfStaleAsync)" with "schedule supersession
    (CancelPendingBySourceScheduleAsync)";
  - add one sentence on the parked-selected disposition chosen under D-1.

  The rest is accurate against the code: re-key with no spend before redeem, receipt revalidation
  after redeem, an end through `ObserveTranscriptAsync`, and the Delegation-only brief expiry.

## Item-by-item verification

1. **Round-1 closure.**
   - D-1 is partially closed. Drop before and after redeem and the spill arm are real; parked-stale is
     not (new D-1).
   - D-2 is closed by retaining the wait and adding the demand predicate (D-2 and V-11/V-12), except
     for the new D-2.
   - D-3 is closed. CP-3..CP-8 are added, and CP-4 is 24.
   - D-4 is closed:
     - D-7 makes both classes Slow and allowlisted, with no NativeLinuxGate and no roster entry;
     - CP-2 is 10 minutes.
   - A-3 is Risk 7, A-4 is D-4 and Risk 8, A-5 is the Performance budget's Final Unit-lane paragraph,
     and A-6 is Risk 8 and V-4's semaphore-hold measurement. All closed.
2. **Retained wait.**
   - With X detached, an unselected held Y (unkeyed, NoteHeader Held) is stamped on the next grant.
     `ApplyCapacityHoldAsync` (`:5557-5573`) chooses rows whose key is null or the granted key, and
     the rotated action key cannot match X. Later input wakes the same wait the same way. Both hold.
   - "No cycling while nothing is held" holds only once D-1 (deliverable demand) and D-2 (revoke
     disposition) are fixed.
   - **Lock order** is semaphore → message `NOWAIT` → provider try-lock → wait rows. Nothing in it
     blocks. `PrepareNextAttemptOnAsync` (provider → message UPDATE) can wait briefly on X's row lock,
     but the cleanup's provider try-lock then fails and rolls back, so there is no deadlock.
   - **Restart mid-cleanup:** everything is in one transaction plus an in-memory gate, so a crash rolls
     back and the next tick rediscovers.
   - **Standing half:** unchanged from round 1 and sound. S3-A stamps `SessionId` and
     `SessionStartedAt` in the reservation (`CapacityRecoveryService.cs:396-399`), and
     `ConsumeAdmittedStartAsync` is at `AgentControlService.cs:731` and `:839`.
3. **Roster.**
   - Source counts at `6564c5757`:

     | Checkpoint | Count |
     |---|---|
     | CP-3 | 6+12+8+6 = 32 |
     | CP-4 | 24 (2 files) |
     | CP-5 | 52 (9 partials) |
     | CP-6 | 15+12+1 = 28 |
     | CP-7 | 28 (4 partials; the brief's 26 omitted Recovery's 2) |
     | CP-8 | 5+4+3+17 = 29 |

   - That makes 193 existing results, plus 27+18 new = 238. The arithmetic and the V-row sums (4+8+5+1+1+4+4 and
     4+7+4+2+1) are exact.
   - No suffix collisions: each prefix matches only its own class. Each row has one build and one exact
     filter, and the OR operands are parenthesized with a trailing `*`.
   - The obligations are stated: Slow classification, the R1 performance Debug (>30 min selection), and
     the Final Unit lane, which is separate and incomplete without it.
4. **Wording:** see A-4.
5. **Dependency:** correct. `6564c5757` is not an ancestor of `origin/master` (`f8bddb6ef`). Code is gated
   on S3-A landing, and S3-A still owes its Full Review.
6. **New defects from this revision:** D-2. D-1 carries round-1's error forward.
