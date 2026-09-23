# CARD-0641: land outcome delivery and truthful waiting diagnostics

Plan date: 2026-09-23; completed 2026-09-24. Plan task: `87eb38d2`.
Code and master inspected at `145f953da426e460f10782a932d05a5e020a4ac9`.
Card `c5ffeacb-cce8-4b1f-87b0-f3e301331234`, board
`8988ca03-7414-47ad-b0b6-51556c701703`, revision count 5, including the receipt and Held-spam additions.
Primary investigation: [land outcome delivery](../../investigations/2026-09-23-card-0641-land-outcome-delivery.md), committed as `25564530`.

The deliverable is five bounded Code rounds. Verification design is folded into this plan because
the dispatch explicitly requests executable checkpoints and `next: code`. These are implementation
decisions, not unresolved defaults requiring a Decide stage. No production changes, restarts, database
writes, builds or test runs were performed in Plan.

## Ground truth

| Card assumption / question | What the inspected code and evidence actually show | Consequence |
|---|---|---|
| An Outcome can remain Queued because the caller is busy. | `AgentTaskLandNotificationService.ReconcileAsync:99-117` probes the repository lease **before** enqueue, for Outcome and Conflict only. A null acquisition returns without saving a reason. The investigation's `2cbed084` was not enqueued for ten minutes during subsequent lands; delivery then took seconds. | Remove repository exclusion from notification enqueue. Keep the session queue's WhenIdle policy. |
| Waiting for the producer's lease release prevents unsafe delivery. | Settlement commits terminal event and immutable notification together. The probe has no producer identity and conflicts with every later land, dispatch admission, settlement and cleanup taking the same OS lock. Notification enqueue performs no repository mutation. | Committed outbox identity is the handoff gate. Another land's lease is irrelevant. |
| Delivered `1cef2473` failed text matching. | Its complete body is in **QueuedUserPrompt**, sequence **73264**, timestamp **2026-09-23 22:29:34.702Z**. Queue row `8e576052-38ed-4397-9f34-265bb2723eca` has attempt 1 and baseline **73258**. The note and queue bodies are equal. The receipt query accepts only UserPrompt; the session queue already accepts submitted QueuedUserPrompt. | Fix transcript-kind selection for land lifecycle notes; do not weaken identity, completeness or floors. |
| Sent or caller acknowledgement should confirm that note. | Queue verdict 8 is `LateConfirmed`; the 23:30:05.315 +01:00 server log records late confirmation. Neither that verdict nor assistant prose is the authoritative receipt. `TranscriptKinds.QueuedUserPrompt` means a submitted `attachment/queued_command`, unlike inert QueueEnqueue/QueueDequeue/QueueRemove. | Use the submitted transcript record itself. Keep housekeeping and Sent-only evidence negative. |
| A queued LandAged should have persisted holder fields. | `AgentTaskLandMonitorService.AddAged` interpolates fields populated only after `RunRequestAsync` attempts admission. A request still in the channel has none. `AgentTaskLandQueue` exposes an unordered active set and no position. Its channel is **global, single-reader**, not one channel per repository. | Expose a truthful channel snapshot; distinguish queue predecessor from repository lease owner. |
| The same Held situation created seven or more notes. | Read-only inspection found **13 Held notifications** for request `82158b95` between 22:49:21Z and 23:00:50Z, alternating writer `6807840d9ef6492faae720cd96eee602` and owner-unknown. `HoldAsync:967-983` treats reason changes and known/null holder flips as new notification episodes. | Deduplicate caller notes separately from diagnostic Held events and progress clocks. |
| A lease can already describe its owner. | `RepositoryMutationLease` owns a FileShare.None stream. `DescribeUnavailableAsync` reports unfinished child journals only. Production registers the provider as a singleton. Known task identities are available at land, dispatch and commit acquisition sites but are not passed to it. | Add advisory live owner attribution; preserve the OS lock as the authority. |
| The 57-minute Aged and 25-minute TaskCompletion delays share the lease gate. | Maxima reproduced: Aged `90b541f6` created 09-13 15:06:42Z, enqueued 16:03:16Z (**3394s**); TaskCompletion `db67d3a4` created 09-18 04:34:30Z, enqueued 04:59:50Z (**1520s**). Kinds Aged and TaskCompletion do **not** enter the probe. Both were first enqueue attempts. | No shared direct lock-gate cause. Do not claim this plan fixes these historical outliers. See the bounded exclusion below. |
| LocalTargetAdvanced to PushStarted took four minutes. | Investigation measured about 24 seconds; most of the roughly five-minute land is verification and repeated inspection. | Land speed remains CARD-0642, outside this plan. |

### Additional evidence and limits

The receipt comparison above was checked with a read-only join of notification, queue and transcript:
`position(regexp_replace(note.Body, '\s', '', 'g') in regexp_replace(prompt.Text, '\s', '', 'g')) > 0`
and `prompt.Sequence > queue.LastDeliveryBaselineSequence` both returned true for sequence 73264.
The wrapper is `<pasted_content ...>`; no fuzzy comparison is needed. The monitor's later Error
is a different UserPrompt containing an alert about the ID, not the original full body.

The retained local server logs begin on September 20, so the September 13/18 enqueue gaps cannot
be attributed from historical scan traces here. The hosted scan processes all unresolved rows
serially and calls `CatchUpTranscriptAsync` for attempted rows; its five-second delay is **after
the whole pass**, not a five-second per-note latency guarantee. Slow transcript pulls, service
downtime or other serial work could delay any kind. That is a separate, unproven hypothesis,
not evidence that Aged/TaskCompletion take the repository gate. If those outliers recur, measure
pass start/end, per-note reconcile and per-destination transcript-pull durations, due-time skips,
process availability, and CreatedAt/EnqueuedAt/SentAt/ConfirmedAt separately. Do not add scanner
parallelism, tune retry/receipt timeouts or widen this card into a throughput project by inference.

## Decisions

- **D-1 — Enqueue committed land notes independently of all repository leases.** Delete the
  Outcome/Conflict probe and remove its constructor dependency. Keep immutable destination/body,
  unique SourceLandNotificationId, due-time retry and `deliverIfIdle: false` plus flush wakeup.
  The ordinary scan and recovery already retry committed debt. Reject a shorter poll interval,
  a second notification file lock, and a probe that tries to identify the producing lease: they
  retain unnecessary coupling or add a second lock protocol. A committed note may enqueue and
  be read before its producer disposes the lease; publication facts are already committed and
  this does not authorize cleanup, dispatch or any Git mutation. There is consequently no
  lock-blocked notification state left to explain; prove the reconciler makes zero lease calls.
  Existing non-lock blockers must keep their saved error codes and due times.

- **D-2 — Accept a complete submitted QueuedUserPrompt for land lifecycle receipt.** Limit this
  extension to non-legacy Held, Aged, Conflict and Outcome notes. Accept either UserPrompt or
  QueuedUserPrompt in the immutable destination above the stored attempt sequence, or within
  the existing timestamp-floor rule when no sequence baseline exists. Require an actual attempt,
  `IsConfirmedBy` and `IsCompleteIn` against the immutable Body. Keep catch-up before declaring
  absence and persist the confirming sequence. No change to PromptSubmissionMatch normalization,
  channel turn ownership, task settlement, transcript parsing, specialist rules or completion
  rendering. DispatchBase, DeliveryFailure, TaskCompletion and LegacyCheckNote retain their
  current UserPrompt receipt contracts. Reject accepting QueueEnqueue/Dequeue/Remove, Sent,
  LateConfirmed alone, assistant acknowledgement, ID/header-only matches, or concatenating
  partial records. The old `C467_V11_RejectFalseReceipts("queued-prompt")` expectation changes
  deliberately for its Outcome fixture; preserve every other negative. Update the land-specific
  paragraph in `docs/session-runtime-invariants.md` to explain this submitted-prompt exception.

- **D-3 — Show the actual in-process queue, not a guessed FIFO from database timestamps.** Add an
  immutable snapshot to AgentTaskLandQueue containing the executing task/request and ordered
  waiting task/request identities. Serialize accepted writes and snapshot bookkeeping together;
  transition waiting to executing in both ReadAllAsync and TryDequeue. Track each accepted channel
  entry, not just task IDs: Release clears the active claim/executing entry, but must not erase an
  unread channel item from the snapshot. Early release followed by requeue of the same task must
  not confuse the old item with the new one. Preserve deduplication and channel order; do not
  reorder or silently drop channel items to make a diagnostic fit. Position is one-based among
  **waiting** requests: B is position 1 when A executes; C is position 2. Include waiting count
  and observed time. A running request has no waiting position. Snapshot order is global because
  the worker is global. A predecessor in another repository is labelled `queue blocker`, never
  asserted to own this repository's lease. Monitor joins snapshot identities to current request/
  task data without changing hold or progress fields. After restart, if a durable pending request
  is not yet replayed, say `queue position=unknown (awaiting replay)` and name a known blocker
  only with evidence; do not report stale DB Running as a live lock owner. Reject moving the
  age clock to dequeue: time waiting is useful information and must remain visible.

- **D-4 — Name known lease owners through advisory singleton state.** Extend
  IRepositoryMutationLease with a typed owner-taking acquisition overload and read-only owner
  lookup. Keep the existing acquisition member and default overload delegation so callers/fakes
  are source compatible. Owner data contains task ID when available, purpose, observed/acquired
  time and a per-acquisition identity. RepositoryMutationLease registers it only after successful
  OS lock acquisition and child-journal checks; disposal clears only that acquisition's record.
  Key by the canonical common directory using the existing platform path rules. Lookup must
  never acquire landing.lock, recover a child or claim safety authority. An untagged owner may
  be described as untagged/in-process; external holders and restart-surviving child journals
  remain explicitly unknown, with the existing journal diagnostic when applicable. Do not infer
  a lease owner merely because a Shared task is active. No owner sidecar or new distributed lease
  protocol is needed for this observed same-server case.

  Tag the land acquisition, dispatcher admission, GatedCommitService acquisition (parse its
  existing antiphon-task trailer before acquisition), and both DelegationWorktreeService
  acquisitions (provisioning and settlement). The held-lease commit overload keeps its caller's
  ownership. Other callers remain compatible and explicitly untagged. When land admission fails,
  use the live lease owner first; otherwise use existing writer checks only as an **admission
  writer**, labelled separately from unknown lease ownership. Unknown must never erase a known
  owner merely for deduplication. A last-known owner shown diagnostically must say last-known.
  No new owner observation can admit a land or override an unavailable OS lease.

- **D-5 — Deduplicate Held caller notes per request and stable owner, independently of events.**
  Add nullable `HoldNotificationOwnerKey` (bounded to 200 characters) to AgentTaskLandRequest.
  Null means no Held note has been created; `unknown` means one was created without identified
  ownership; `task:<full-guid-N>` is a known task anchor. Acquisition IDs, reason codes, statuses
  and timestamps are not owner keys. A task is the same holder across lease reacquisitions and
  writer/lease reason changes. Keep the current diagnostic Held events, HoldEpisode, HeldSince,
  HoldDetail, HeldReleased and warning/error clocks; gate only AddNotification for Held.

  | Existing anchor | Current observation | Caller note | Persisted anchor |
  |---|---|---|---|
  | null | unknown or A | first note | unknown or A |
  | unknown | unknown | none | unknown |
  | unknown | A | none: identity refinement does not prove a holder change | A |
  | A | unknown | none | A |
  | A | A, even after release/rehold or reason/status change | none | A |
  | A | B, different known task | one new note | B |

  Save anchor, any new note and event in the existing task-locked transaction. Retain the anchor
  across admission and terminal completion; a new request starts with null. Never rewrite an
  immutable already-delivered note. On first post-upgrade evaluation of a null anchor, inspect
  existing Held notifications for **that request** under the same task lock: if present, seed an
  anchor from the current defensible owner or unknown and do not mint another first note.
  This conservative adoption can suppress an unprovable pre-upgrade owner change; it prevents
  a deployment replay storm. Reject in-memory-only dedupe, parsing old free-text notes as owner
  authority, deleting historical spam or suppressing changed-holder notes globally.

- **D-6 — Explain waiting without fabricating progress.** Queued LandAged includes request ID,
  requested time, queue position/count, executing predecessor's task/request and status, and
  repository owner if separately known. Receipt LandAged retains the original publication/
  cleanup snapshots and reports queue state/error; enqueue failure codes remain persisted.
  Avoid `holder= ()`, empty reason labels, or claiming a completed publication is still landing.
  No severity threshold, retry count or LandAged clock changes. A real unconfirmed receipt can
  still warn. Confirmation prevents future escalation; historical warning/error events remain.

## Slices and Code rounds

Each round ends with committed/pushed code, its CP lines and remaining scope; it is not the whole
card's completion until S5. Use a verification profile with the named round scope and pending Final
when dispatching partial rounds. Never overlap these rounds on the same source files. Authoring
budgets include fixture work; if a slice exceeds its bound, checkpoint and hand off `next: code`
with the exact remaining work rather than expanding the verification loop.

| Slice / round | Bound | Files and implementation | Tests / exit evidence |
|---|---:|---|---|
| **S1: independent enqueue** | 60 min | `server/Application/Services/AgentTaskLandNotificationService.cs`: remove repository probe/dependency, adjust its construction sites; preserve the existing queue/outbox boundaries. | New `tests/Antiphon.Tests/Application/AgentTaskLandEnqueueTests.cs`; CP-1/2. One producer-to-recipient Outcome and one Conflict survive another live lease. |
| **S2: submitted queued receipts** | 60 min | Same notification service: kind-scoped receipt predicate. `docs/session-runtime-invariants.md`: clarify submitted queued receipt versus housekeeping. No edits to the shared matcher or parser. | New `tests/Antiphon.Tests/Application/AgentTaskLandQueuedReceiptTests.cs`; deliberate expectation update in `AgentTaskLandReceiptTests.cs`; CP-3/4. Historical shape confirms without retyping. |
| **S3: queue visibility and aging** | 75 min | `AgentTaskLandQueue.cs`, `AgentTaskLandMonitorService.cs`; adjust `server/Infrastructure/Orchestration/AgentTaskLandHostedService.cs` only if needed for the dequeue snapshot transition. `docs/orchestration-loop.md`: position semantics. | New `AgentTaskLandQueueVisibilityTests.cs` and `AgentTaskLandQueueAgingTests.cs` in `tests/Antiphon.Tests/Application/`; update the blank-holder golden text in `AgentTaskLandMonitoringTests.cs`; CP-5/6/7. |
| **S4: lease owner attribution** | 80 min | `server/Application/Interfaces/IRepositoryMutationLease.cs`, `server/Infrastructure/Git/RepositoryMutationLease.cs`, `AgentTaskLandService.cs`, `AgentTaskDispatcher.cs`, `GatedCommitService.cs`, `DelegationWorktreeService.cs` (the four services under `server/Application/Services/`). Wire the monitor's owner display as needed. No authority or lock-order changes. | New `tests/Antiphon.Tests/Infrastructure/RepositoryMutationLeaseOwnerTests.cs`; CP-8/9. Observe named owner while the real file lock is held, including Shared admission/commit paths. |
| **S5: durable Held-note dedupe** | 80 min | `server/Domain/Entities/AgentTaskLandRequest.cs`, `server/Infrastructure/Data/AppDbContext.cs`, CLI-generated `server/Migrations/*_AddLandHoldNotificationOwner.cs`, designer and model snapshot; `AgentTaskLandService.cs`; owner documentation in `docs/orchestration-loop.md`. | New `tests/Antiphon.Tests/Application/AgentTaskLandHeldNotificationTests.cs`; CP-10/11. Restart, upgrade, concurrency and rollback preserve one first note and a real owner change still notifies. |

New helpers, if needed, belong in `tests/Antiphon.Tests/TestHelpers/LandOutcomeDeliveryHarness.cs`.
Compose the existing harnesses; do not clone their service registration graphs. No generated
`docs/cards/` edits, client changes, delivery-mode changes, production configuration tuning or Git
verification changes are part of these slices.

For S5, generate the migration through the repository's EF CLI workflow, with the slice's isolated
output and no application of it to the production database. Report generation as a necessary
non-test command. The migration adds a nullable column; adoption is transactional application
logic, not a migration guessing historical owners. The upgrade test applies migrations only to
its owned test schema. Include migration generation time in S5 authoring, not a hidden test run.

## Verification design

### Inspection

Read the relevant bodies in `AgentTaskLandReceiptTests`, `AgentTaskLandNotificationRecoveryTests`
(including H5's legacy recovery), `AgentTaskLandNotificationPersistenceTests`,
`AgentTaskLandMonitoringTests`, `AgentTaskLandHoldVisibilityTests`, `AgentTaskLandIndexLockTests`,
`RepositoryMutationLeaseDescribeTests`, `RepositoryMutationLeaseTests`, and the admission/controlled
landing tests. Read `BridgeQueueHarness`, `LandingProtocolHarness`, `LandingSafetyHarness`,
`ControlledLandingGit`, GatedCommitServiceTests' fixture usage, the notification hosted scanner,
the real queue's receipt selectors and TranscriptKinds documentation.

Use **LandingProtocolHarness** (real services and isolated Postgres, controlled Git) for producer,
hold and transaction boundaries. Attach **BridgeQueueHarness** to the same connection/schema and
caller; use its real SessionMessageQueueService, runtime and fake adapter/runner. Explicitly register
CompletionNoteFlushQueue, notification service and the notification/flush hosted service when a
case exercises scan/wakeup, rather than accidentally relying on Program. The notification helper's
SeedAsync is suitable for matcher adversaries, not sufficient for the producer-to-recipient tests.
Observe committed rows through a fresh DbContext. Use the existing LandDeliveryBoundary and save/
transaction fault hooks at commit, before-enqueue, queue-inserted and receipt-before-save.

Missing setup to add: a two-request controlled land fixture sharing one repository; queue snapshot
observation; configurable submitted transcript kind in the fake caller; owner-tagging lease spy
which forwards both acquisition overloads; and a task-locked Held transaction cut. For S4's provider
tests, use real FileStream exclusion in an owned temporary common directory with controlled Git
identity, not a boolean fake for the OS-lock assertion. No real Git server or paid model is needed.
Use TestDbFixture's isolated schemas and FakeTimeProvider for ages. Any fixture that actually spawns
a child takes the assembly's ProcessSpawnLimit; run no Pty assembly concurrently.

The substitutes prove service/database/queue/receipt behavior; they do not certify a new native
terminal parser, real model comprehension or changed Git publication safety. Those surfaces are
unchanged. Do not launch production Program or connect a test to runner 17204.

### Delivery inventory

| Path | Producer and commit | Destination and durable identity | Recovery | Observable receipt |
|---|---|---|---|---|
| Outcome / Conflict | Real land terminal/conflict path commits event and note in its existing transaction. | Request snapshot ParentSessionId; note.Id -> SourceLandNotificationId -> QueueMessageId; immutable body/digest. | Boot/periodic reconcile; existing-row adoption after lost acknowledgement; independent of held repository lease. | Complete matching submitted prompt above attempt floor, confirming sequence saved; caller fake observed the body. |
| Receipt of previously sent land note | Existing keyed row plus native submitted transcript; no new enqueue. | Same notification, destination and immutable body. | Catch-up and next reconcile after missed stream or failed confirmation save. | UserPrompt or, for D-2's four non-legacy kinds, submitted QueuedUserPrompt. Never merely queue verdict. |
| Queued LandAged | Monitor saves event/note with snapshot-derived diagnostics; original publication fields remain immutable. | Same request destination; separate Aged notification ID. | Ordinary notification outbox and session flush. | Busy caller receives after eligible turn end; already-eligible caller receives through wakeup; full body in transcript. |
| Held | HoldAsync task-lock transaction saves event and, only when D-5 allows, note plus owner anchor. | Request plus stable holder key gates minting; each minted note still owns its normal unique queue key. | Restart reloads anchor; pre-upgrade notes seed it without replay; rollback retains debt. | Same real queue and full submitted prompt; tests count notes as well as actual caller submissions. |

Crash/enqueue cuts are simulated by throwing at the existing durable boundaries and rebuilding the
service scope/provider over the same schema. A fresh observer proves commit/rollback before retry.
An eligible caller and a busy caller are both required. Sent, a queue insert, an event or a flush
ack is never the last assertion in a positive delivery case.

### Proves it works now

All new methods below have the literal prefix **`C641_`**. Names are fixed for checkpoint and
positive-control selection. A parenthesized list is `[Arguments]` expansion; each other method
contributes one TUnit execution. Negative subcases must be separate arguments, not inflated
assertion counts.

| ID | Test class and methods (prefix C641_ on each) | Executions and decisive assertions |
|---|---|---|
| **V-1** | `AgentTaskLandEnqueueTests`: `Outcome_enqueues_and_reaches_an_idle_caller_while_lease_is_busy`; `Conflict_enqueues_and_reaches_a_busy_caller_after_turn_end`; `Recovered_commit_and_lost_insert_ack_use_one_notification_and_queue_row` (before-enqueue, queue-inserted); `Unavailable_destination_records_a_due_retry`; `Concurrent_reconcilers_keep_one_key_and_complete_receipt`. | **6**. Produce the first two through real land paths, acquire B's lease, reconcile A before releasing B, assert keyed AwaitingReceipt, then complete transcript receipt. Idle/busy delivery once each; no lease probe. Each cut recovers the original committed identity exactly once. Unavailable destination persists reason/due time without typing. |
| **V-2** | `AgentTaskLandQueuedReceiptTests`: `Submitted_queued_prompt_confirms_land_lifecycle_note` (Held, Aged, Conflict, Outcome); `Historical_1cef_shape_confirms_without_retyping_and_stops_escalation`; `Receipt_rejects_non_submission_or_incomplete_evidence` (enqueue, dequeue, remove, head-only, head-tail-splice, wrong-session, wrong-identity, unattempted); `Receipt_keeps_sequence_and_timestamp_floors` (old-sequence, equal-sequence, old-time, no-floor); `Other_notification_kinds_keep_UserPrompt_contract` (DispatchBase, DeliveryFailure, TaskCompletion, LegacyCheckNote, legacy-Outcome). | **22**. Complete queued receipt stamps 73264 for the historical-shaped synthetic fixture above 73258; second reconcile has no new input; later monitor adds no Error. Every negative stays unconfirmed. Other kinds and an IsLegacy Outcome with an existing keyed row reject queued evidence, then accept valid UserPrompt using their existing rendering rules. |
| **V-3** | `AgentTaskLandQueueVisibilityTests`: `Snapshot_matches_channel_order_and_running_owner`; `Duplicate_enqueue_and_release_preserve_positions`; `Concurrent_enqueue_snapshot_is_consistent`; `Restart_has_no_invented_running_owner`. | **4**. Compare snapshot identities/order to actual channel dequeue, including rejected duplicate, normal release, early release/requeue with an old unread item, concurrent writers, and empty new instance. No DB/Git required. |
| **V-4** | `AgentTaskLandQueueAgingTests`: `Queued_aged_note_names_running_holder_and_first_position`; `Queued_position_counts_predecessors_and_advances_after_release`; `Different_repository_predecessor_is_queue_blocker_not_lease_owner`; `Unreplayed_request_reports_unknown_position_without_blank_holder`; `Queue_observation_does_not_reset_age_or_attempts`; `Queue_aged_note_reaches_busy_and_idle_caller` (busy, idle). | **7**. A running, B first, C second; remove A then verify changed position. Cross-repository global queue case is accurately labelled. No blank holder, fabricated running owner, age reset or extra warning per threshold. Last method ends at full caller receipt. |
| **V-5** | `RepositoryMutationLeaseOwnerTests`: `Lease_exposes_task_owner_while_os_lock_is_held`; `Releasing_old_lease_cannot_erase_new_owner`; `Unknown_and_journal_only_holds_do_not_invent_an_owner` (external, journal); `Owner_lookup_keeps_exclusion_and_Owns_semantics`; `Shared_dispatch_and_commit_tag_the_lease` (dispatch, commit); `Worktree_settlement_preserves_owner_tag`. | **8**. Real file exclusion remains busy during lookup, exact task/purpose appears through each real service's acquisition, released owner disappears, stale disposal cannot remove B, unowned evidence remains explicit. For journal-only case, no acquisition or cleanup occurs. |
| **V-6** | `AgentTaskLandHeldNotificationTests`: `Reason_flips_for_same_owner_emit_one_note_across_restart`; `Unknown_observations_do_not_change_owner_or_reemit` (unknown-first, known-first); `Different_known_owner_emits_one_additional_note`; `New_request_gets_its_own_first_note`; `Concurrent_holds_commit_one_note_and_dedupe_anchor`; `Rollback_does_not_consume_first_note`; `Upgrade_preserves_existing_held_notification_debt`; `Held_note_reaches_busy_and_idle_caller` (busy, idle). | **10**. Exercise the D-5 table through real HoldAsync, with thirteen reason flips and fresh contexts/provider restart. One note for A, another for B, none on unknown flips; first note per new request. Failed transaction saves neither anchor nor note; retry creates one. Apply additive migration in an owned schema with existing Held debt and prove adoption does not replay it. Last method ends at full caller receipt and unchanged lease exclusion. |

The synthetic historical receipt fixture uses non-sensitive generated task/request/body identities
but preserves the wrapper, transcript kind, sequence relation and full-body comparison from the
measured incident. No live-session transcript or credential store is copied into tests.

### Guards the regression

- **R-1:** V-1 rejects reintroducing a repository prerequisite and protects immutable keyed
  adoption, recorded failure reasons and eligible/busy wakeups. The cut cases assert exactly
  one SourceLandNotificationId row through fresh contexts, not an in-memory count.
- **R-2:** Run the complete existing `AgentTaskLandReceiptTests` (**30** executions) with V-2.
  Preserve truncation, stale floor, wrong destination/identity, spill-pointer rejection,
  catch-up/save recovery, cancellation and retention checks. Only the queued-prompt Outcome
  expectation changes; `C488_ApprovalReceiptNeedsUserPrompt` still rejects Sent-only evidence.
- **R-3:** Run `AgentTaskLandMonitoringTests.C467_V15_*` plus
  `C498_MonitorRotatesTokenEveryPass` (**13** executions). Update blank-holder golden wording
  to the new explicit unknown form; preserve immutable publication/cleanup evidence, threshold
  boundaries and concurrency-token rotation. V-3/4 protect channel order and truthful diagnostics.
- **R-4:** V-5 protects actual OS exclusion and `Owns`, including unknown owner/journal cases;
  tagged metadata cannot become an admission override. The other acquisition overload remains
  usable by existing callers and test fakes. No broad native child-journal matrix is changed.
- **R-5:** V-6 plus the existing
  `AgentTaskLandIndexLockTests.Fresh_lock_holds_as_held_then_ages_to_stale` (**1** execution)
  preserve diagnostic events and HoldEpisode while caller notes deduplicate. The first and
  changed-owner notes are durable delivery obligations, never suppressed because a prior queue
  row is pending. Existing notes are not deleted or rewritten.

### Guard inventory and positive controls

Every row is one independently bypassable changed or relied-on delivery/ownership guard, mapped
1:1 to its PC. Method names below include the C641_ prefix; existing R-2 tests also remain available.
Code runs ordinary V/R and the explicitly listed baseline-red checkpoints. Post-land Mutation
runs each PC with `/*/*/ClassName/ExactMethod`, red assertion, restore, same method green. Parameter
expansions stay within that exact method. No suite/class-wide PC runs and no simultaneous changes
to the same method/file. A compiler/fixture error or zero executions is not a red control.

| Guard / PC | Compiling defect to inject | Exact class.method and expected red assertion |
|---|---|---|
| G-1 / PC-1: no repository prerequisite (D-1) | Restore the old Outcome/Conflict busy-lease return. | `AgentTaskLandEnqueueTests.C641_Outcome_enqueues_and_reaches_an_idle_caller_while_lease_is_busy`: QueueMessageId/receipt missing while B owns lease. |
| G-2 / PC-2: adopt committed keyed insertion | Skip existing-key adoption and return on that branch. | `AgentTaskLandEnqueueTests.C641_Recovered_commit_and_lost_insert_ack_use_one_notification_and_queue_row`: queue-inserted cut never links/confirms original row. |
| G-3 / PC-3: eligible caller wakeup | Omit flush enqueue after newly creating the keyed row. | `AgentTaskLandEnqueueTests.C641_Outcome_enqueues_and_reaches_an_idle_caller_while_lease_is_busy`: no submitted prompt/receipt after the controlled flush boundary. |
| G-4 / PC-4: submitted queued kind allowed (D-2) | Restrict the land receipt predicate to UserPrompt again. | `AgentTaskLandQueuedReceiptTests.C641_Submitted_queued_prompt_confirms_land_lifecycle_note`: Confirmed state absent in all four variants. |
| G-5 / PC-5: extension limited to land lifecycle | Allow QueuedUserPrompt for every note kind. | `AgentTaskLandQueuedReceiptTests.C641_Other_notification_kinds_keep_UserPrompt_contract`: forbidden queued evidence confirms. |
| G-6 / PC-6: sequence floor | Remove `Sequence > floor`. | `AgentTaskLandQueuedReceiptTests.C641_Receipt_keeps_sequence_and_timestamp_floors`: stale/equal sequence incorrectly confirms. |
| G-7 / PC-7: timestamp floor | Remove the timestamp lower bound in the no-sequence arm. | Same exact method as PC-6: old-time incorrectly confirms. Separate mutation/evidence. |
| G-8 / PC-8: immutable destination | Remove the destination-session predicate on receipt candidates. | `AgentTaskLandQueuedReceiptTests.C641_Receipt_rejects_non_submission_or_incomplete_evidence`: wrong-session confirms. |
| G-9 / PC-9: complete immutable body | Bypass IsCompleteIn, retain identity/head match. | Same method as PC-8: head-only or head-tail-splice confirms. |
| G-10 / PC-10: actual delivery attempt | Bypass the `DeliveryAttempts > 0` guard. | Same method as PC-8: unattempted fixture with adversarial floor and matching transcript confirms. |
| G-11 / PC-11: inert queue operations excluded | Add QueueEnqueue to accepted transcript kinds. | Same method as PC-8: enqueue-only evidence confirms. |
| G-12 / PC-12: owner lifecycle | Clear owner by common-directory key on every Dispose, bypass the disposed/acquisition identity guards. | `RepositoryMutationLeaseOwnerTests.C641_Releasing_old_lease_cannot_erase_new_owner`: repeated disposal of A removes B's current owner. |
| G-13 / PC-13: OS exclusion remains authority | Change the lock stream to FileShare.ReadWrite. | `RepositoryMutationLeaseOwnerTests.C641_Owner_lookup_keeps_exclusion_and_Owns_semantics`: second provider acquires during first lease. |
| G-14 / PC-14: reason is not holder identity | Include reason in notification dedupe, reproducing original changed-reason behavior. | `AgentTaskLandHeldNotificationTests.C641_Reason_flips_for_same_owner_emit_one_note_across_restart`: more than one Held note for A. |
| G-15 / PC-15: changed known holder still notifies | Suppress every Held note once any anchor exists. | `AgentTaskLandHeldNotificationTests.C641_Different_known_owner_emits_one_additional_note`: B has no second note. |
| G-16 / PC-16: atomic dedupe/debt | Save owner anchor before opening the note/event transaction. | `AgentTaskLandHeldNotificationTests.C641_Rollback_does_not_consume_first_note`: injected note rollback leaves consumed anchor or retry produces no note. |
| G-17 / PC-17: unknown cannot manufacture a holder change | Replace retained known anchor with unknown and treat unknown-to-known as new holder. | `AgentTaskLandHeldNotificationTests.C641_Unknown_observations_do_not_change_owner_or_reemit`: known-first A/unknown/A emits again. |
| G-18 / PC-18: legacy receipt policy preserved | Remove the IsLegacy exclusion from the queued-prompt extension. | `AgentTaskLandQueuedReceiptTests.C641_Other_notification_kinds_keep_UserPrompt_contract`: legacy-Outcome confirms on forbidden queued evidence. |

Inventory: **18 guards, 18 distinct PCs, 0 unmapped guards, 0 duplicate PC IDs**. Multiple distinct
guards may deliberately use the same adversarial test method. Queue position/wording is diagnostic,
not mutation authority; its red-first and V-3/4 tests are sufficient without an invented safety PC.
Publication, cleanup permission, parser semantics and profile-v1 rendering are unchanged and excluded
from new PCs; R-2 and the scoped D-2 tests retain their relevant boundaries.

### Out of scope

CARD-0642 speed work, full-suite/Unit-lane sweeps, E2E or paid-model launches, production note
resending or row edits, repairing historical alerts, channel turn attribution, broad notification
scanner scheduling, and a cross-process distributed owner registry. The historical Aged/TaskCompletion
outliers have no established shared direct cause with the deleted gate; measure them as described
above before commissioning a separate fix. No claim of a universal five-second delivery SLA.

### Checkpoints

This is the closed list for Code, overriding the generic full Unit-lane recipe for this focused
operator-budgeted work. `Sx-tests` means test-only changes (and inert, source-compatible API scaffolds
if needed to compile S3/S4) committed/pushed before production behavior changes. Red checkpoints
must fail the named behavioral assertion against the old behavior. Their expected exit is 1;
they are evidence, not a green claim. Green checkpoints require zero failed/skipped. Rerun only
the failing CP after a fix, report reruns and reasons; do not add speculative broad runs.

Use `scripts/run-checkpoint.ps1` with the project's isolated OutputPath, fresh ResultsRoot per row/
attempt, the listed Min as `-MinExecuted`, and `-Expect` tokens for each selected class/method.
Commit before each run and record the exact verified SHA. Each build uses its round's same output
so red-to-green rebuilds are incremental; CP-7 alone reuses CP-6 with `-NoBuild` because After is
identical. If API scaffolding is necessary, it may only provide empty/default behavior at the red
commit; zero tests or compile failure does not satisfy a row.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1-tests | `tests/Antiphon.Tests -> bin-c641-r1/` | enqueue-red | `/*/*/AgentTaskLandEnqueueTests/(C641_Outcome_enqueues_and_reaches_an_idle_caller_while_lease_is_busy*)\|(C641_Conflict_enqueues_and_reaches_a_busy_caller_after_turn_end*)` | V-1, R-1 | 2 executed, both fail at missing keyed enqueue/receipt under occupied lease | 2 | 1.1 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c641-r1/` | enqueue-green | `/*/*/AgentTaskLandEnqueueTests/*` | V-1, R-1 | all 6, 0 failed/skipped | 6 | 1.9 |
| CP-3 | S2-tests | `tests/Antiphon.Tests -> bin-c641-r2/` | receipt-red | `/*/*/AgentTaskLandQueuedReceiptTests/C641_Submitted_queued_prompt_confirms_land_lifecycle_note` | V-2, R-2 | 4 executed, all fail at missing receipt | 4 | 1.1 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c641-r2/` | receipt-green | `/*/*/(AgentTaskLandQueuedReceiptTests*)\|(AgentTaskLandReceiptTests*)/*` | V-2, R-2 | all 22 new + 30 existing, 0 failed/skipped | 52 | 1.9 |
| CP-5 | S3-tests | `tests/Antiphon.Tests -> bin-c641-r3/` | queue-age-red | `/*/*/AgentTaskLandQueueAgingTests/C641_Queued_aged_note_names_running_holder_and_first_position` | V-4, R-3 | 1 executed, fails missing holder/position | 1 | 1.1 |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c641-r3/` | queue-age-green | `/*/*/(AgentTaskLandQueueVisibilityTests*)\|(AgentTaskLandQueueAgingTests*)/*` | V-3, V-4, R-3 | all 11, 0 failed/skipped | 11 | 1.5 |
| CP-7 | S3 | CP-6 | monitor-regression | `/*/*/AgentTaskLandMonitoringTests/(C467_V15_*)\|(C498_MonitorRotatesTokenEveryPass*)` | R-3 | all 13, 0 failed/skipped | 13 | 0.4 |
| CP-8 | S4-tests | `tests/Antiphon.Tests -> bin-c641-r4/` | owner-red | `/*/*/RepositoryMutationLeaseOwnerTests/C641_Lease_exposes_task_owner_while_os_lock_is_held` | V-5, R-4 | 1 executed, fails absent owner attribution | 1 | 1.1 |
| CP-9 | S4 | `tests/Antiphon.Tests -> bin-c641-r4/` | owner-green | `/*/*/RepositoryMutationLeaseOwnerTests/*` | V-5, R-4 | all 8, 0 failed/skipped | 8 | 1.9 |
| CP-10 | S5-tests | `tests/Antiphon.Tests -> bin-c641-r5/` | held-red | `/*/*/AgentTaskLandHeldNotificationTests/C641_Reason_flips_for_same_owner_emit_one_note_across_restart` | V-6, R-5 | 1 executed, fails duplicate caller-note count | 1 | 1.1 |
| CP-11 | S5 | `tests/Antiphon.Tests -> bin-c641-r5/` | held-green | `/*/*/(AgentTaskLandHeldNotificationTests*)\|(AgentTaskLandIndexLockTests*)/(C641_*)\|(Fresh_lock_holds_as_held_then_ages_to_stale*)` | V-6, R-5 | all 10 new + 1 existing, 0 failed/skipped | 11 | 1.9 |

Backslashes before pipes in the table are Markdown escapes only. Pass the rendered `|` characters
to TUnit, not literal `\|`. For example, CP-4 uses:

```powershell
pwsh -NoProfile -File scripts/run-checkpoint.ps1 -Name CP-4 -Project tests/Antiphon.Tests -OutputPath bin-c641-r2/ -Filter '/*/*/(AgentTaskLandQueuedReceiptTests*)|(AgentTaskLandReceiptTests*)/*' -MinExecuted 52 -Expect AgentTaskLandQueuedReceiptTests,AgentTaskLandReceiptTests -ResultsRoot .antiphon/c641-cp4-attempt1
```

### Cost

Ordinary Code verification floor: **15 estimated minutes**, about **3 per round**, including the
five required baseline-red builds/runs and five green builds/runs plus one reused-output regression
run. Green roster floor: **101 executions** (6 + 52 + 11 + 13 + 8 + 11); baseline-red roster:
**9 executions** (2 + 4 + 1 + 1 + 1). Counts are TUnit results, not assertions or minutes.
These are planning estimates, not measured claims: cold restore/schema preparation or shared-host
load may exceed them. Report actual wall time; never omit a listed row, shorten safety checks or
raise a timeout to fit. Prepare all tests/edits for a slice before its declared checkpoint, and
give an explicit reason for any additional build/test command.

Code round bounds total **355 minutes** (60 + 60 + 75 + 80 + 80): up to 340 minutes authoring plus
15 minutes verification; no individual round exceeds 90 minutes. Round dispatch ExpectAbout should
use that round's authoring estimate plus its CP time without double-counting the bound.

Post-land PC floor: **54 estimated minutes**, 18 method-scoped red/restore/green cycles at roughly
3 minutes each with incremental isolated builds. Ordinary verification and mutation have separate
budgets; mutation is not squeezed into the three-minute Code allowance. Total verification planning
floor is **69 minutes** across stages, not a measured duration. This avoids five whole-assembly
runs and repeated exploratory rebuilds; no invented numerical full-suite savings is claimed.

Keep the exact inventory of alternate output directories produced by each round. Before settlement,
delete only those producer-owned bin-c641-rN directories after checking their resolved absolute paths
remain inside that Code worktree. Run commands in the foreground and await completion. Do not edit
source during a test run. Record each CP's counts, SHA, TRX path, duration and reruns in the Code report.

## Completion and handoff

Ordinary Review checks the five rounds, not just the last diff: no skipped checkpoint, positive
recipient evidence, exact kind scope, unchanged lease authority and transactional Held dedupe.
Land/deploy through the existing pipeline. After activation, ordinary scanner recovery should
confirm retained `1cef2473` from its existing transcript without a manual resend; inspect that
receipt and absence of new escalation, but do not edit production rows to manufacture acceptance.
Publication of this plan is not server activation or evidence the production defect is fixed.

Next: **Code**, starting S1 with CP-1/2. The caller may dispatch one bounded round at a time; follow
the normal Review/landing ownership rules and carry the exact plan commit in each checkpoint brief.
