# CARD-0501 Code: implementation and ordinary verification

Latest FollowUp: task `f95bd1fe` corrects round-3 discovery evidence and the R2-C class inventory.
Its source identity, fresh results and pending controls are recorded at the end of this document.

S1-S5 are complete and pushed. All affected cases pass after fixture corrections. Unit has
four failures reproduced at the base commit and one privilege-dependent skip. The caller waived
local cleanup of the 31 producer-owned build directories under CARD-0455; they remain for the
weekly cleanup-build-junk job. No landing, deployment, restart, or deliberate mutation was
performed. Ordinary read-only Review is next.

## Identity and source

- Original Code task / landing owner: `326c349a-dd36-4adb-8ff6-fe533d91a73a`.
- Card: `a8cb0432-431d-4878-b8b6-4553f5324c6c` (CARD-0501); commissioning project: null.
- Branch: `feat/card-task-326c349a`.
- Exact worktree: `C:\Antiphon\worktrees\card-task-326c349a`.
- Fetched/reset base: `eea127b000ad3078fbd324df2573707b07e87ded`.
- Latest built source: `83497db0d27a68939f7484984c8a14f42ffdb59f`.
- Plan: [2026-09-14-card-0501-check-interpreter-wedged-queue-head-plan.md](../superpowers/plans/2026-09-14-card-0501-check-interpreter-wedged-queue-head-plan.md).
- Persistent evidence: `C:\src\Antiphon\.git\antiphon\evidence\326c349a`.
  Worktree copy: `C:\Antiphon\worktrees\card-task-326c349a\.antiphon\c501-326c349a`.

## Changes

- Added provider-neutral whole-head evidence; left the windowed post-submit predicate unchanged.
- Added `SessionQueuedMessage.LastDeliveryGeneration`, CLI-generated migration
  `20260914233700_AddQueuedMessageDeliveryGeneration`, designer and model snapshot.
- Stamped all three persisted typed-write paths and cleared both existing refund paths.
  Both Enter-only entry sites check generation before their retry snapshot, with the specified
  legacy clock fallback. Interrupted old-generation Sent rows revert and retype.
- Failed Enter-only recovery charges every row before failure handling, preserves typing
  metadata, captures the generation before Enter, and identifies the recovery in the incident.
  Successful recovery still finishes the original attempt. Backend-unreachable remains uncharged.
- Added terminal-task brief cancellation when never typed or the old composer is gone;
  human SendNow retains expiry-only cancellation.
- Added six pure tests and 18 integration methods expanding to 26 cases. Extended the shared
  seed helper with current-generation default and explicit legacy-null support. Registered the
  measured PostgreSQL startup cost in the Slow metadata/registry.
- Updated runtime invariants, ops runbook, investigation pointer and plan coverage IDs.

## Commands and executed counts

Builds used `--property:OutputPath=bin-c501-326c349a/` (forward slash). Tests used the same
already-built output with `dotnet run --project <project> --no-build`, the filter below,
`--report-trx --report-trx-filename <file>` and a fresh absolute results directory under the
evidence root's worktree copy. No namespace or full-assembly selection was used. Pty and server
test assemblies ran sequentially. All commands completed before this report.

| Run | Project | Filter | Actual outcome / TRX relative to evidence root |
|---|---|---|---|
| P | Antiphon.Agents.Pty.Tests | `/*/*/(ComposerDeliveryEvidenceTests*)\|(SubmitEvidenceTests*)/*` | 32 passed; `pty/pty.trx` |
| U | Antiphon.Tests | `/*/*/*/*[Category=Unit]` | 2,351 total: 2,346 passed, 4 failed, 1 skipped; `unit/unit.trx` |
| Q | Antiphon.Tests | `/*/*/(SessionMessageQueueWedgedHeadTests*)\|(SessionMessageQueueInterruptedAttemptTests*)\|(SessionMessageQueueDeliveryVerificationTests*)\|(SessionMessageQueueServiceTests*)\|(ParkedMessageSweepServiceTests*)\|(AgentTaskDeliveryWatchdogTests*)/*` | 262 total: 258 passed, 4 failed initially; `queue/queue.trx` |
| W | Antiphon.Tests | `/*/*/SessionMessageQueueWedgedHeadTests/*` | Corrected rerun: 26 passed; `wedged-final/wedged.trx` |
| G | Antiphon.Tests | `/*/*/SessionMessageQueueDeliveryVerificationTests/A_deferred_re_check_of_a_row_sent_without_a_retained_generation_declines_the_recovery_kill` | Corrected rerun: 1 passed; `guard-final/guard.trx` |
| M | Antiphon.Tests | `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata` | 1 failed with only the inherited Herdr category error; `metadata-final/metadata.trx` |
| N | Antiphon.Tests | `/*/*/SessionMessageQueueGrokPtyIntegrationTests/*` | 4 passed, zero skipped; `grok/grok.trx` |

Markdown escapes the OR separators in the table; the actual selectors contain plain `|`,
without backslashes. `inspect-trx.ps1` joins every result to its class/method and requires
nonzero counts; `.trx.methods.json` and `.trx.classes.json` retain the expanded inventories.
All six new pure methods and all 18 integration methods/26 argument cases were checked explicitly.

Build / run source mapping:

- Migration tooling build at `13c508bdacb01d08fdae6a559ab7a50be04875de`; generation used
  `dotnet ef migrations add AddQueuedMessageDeliveryGeneration --project server --no-build`.
  EF used environment `OutputPath=bin-c501-326c349a/` and
  `AppendTargetFrameworkToOutputPath=false`, with runner/optional workers disabled and a
  non-routable design-only database endpoint. The first metadata lookup omitted the append
  override and failed on a nonexistent `net9.0` deps path; no migration was produced by that try.
- P was built at `434a59af7a1c34a360a4f9248a8337684e3f5c8d`; its production/test files never
  changed afterward. U/Q used `2eaa02fe3c914b36799aed029e5e449002a6d659`.
- W/G used `c4b0616b0c8a76478e80635f1944fe995d25a567` after the four fixture corrections.
- M/N used `83497db0d27a68939f7484984c8a14f42ffdb59f`; changes since W/G are only the
  new class's Slow annotation/registry entry and plan documentation. No test body changed.
- Rebuilds were for migration generation, actual fixture fixes, classification metadata, and
  the separate base comparison. No routine per-class rebuilds. Every build succeeded with
  zero errors (server-test builds report 226 repository warnings).

## Every ordinary V/R outcome

| ID | Actual outcome and shared command |
|---|---|
| V-1 | PASS, P: ComposerDeliveryEvidenceTests 24/24, including all six new cases. |
| V-2 | PASS, W: 12 expanded generation/stamp/refund/retry/legacy cases. |
| V-3 | PASS, W: 4 charge/park/kill/working cases. Initial three fixture failures are resolved. |
| V-4 | PASS, W: 10 terminal-task/expiry/SendNow cases. |
| V-5 | PASS: generated nullable timestamp only; fixture migrations execute; `dotnet ef migrations has-pending-model-changes --project server --no-build` reports no changes (`model-check.log`). |
| V-6 | PASS: docs/diff inspection and `git diff --check`; ops inspect/clear routes documented, no live queue mutation. |
| R-1 | INHERITED RED, U: 2,346 pass / 4 fail / 1 skip; all four failures reproduced individually at base; M confirms no new registry error. |
| R-2 | PASS, P: SubmitEvidenceTests 8/8. |
| R-3 | PASS, Q: SessionMessageQueueInterruptedAttemptTests 15/15. |
| R-4 | PASS after correction: Q runs all 114 delivery-verification cases, 113 pass; G reruns the corrected remaining method 1/1. |
| R-5 | PASS, Q: SessionMessageQueueServiceTests 25/25. |
| R-6 | PASS, Q: ParkedMessageSweepServiceTests 9/9. |
| R-7 | PASS, Q: AgentTaskDeliveryWatchdogTests 73/73. |
| R-8 | PASS, N: native Grok 4/4, including swallowed Enter retaining DeliveryAttempts=1. |

Q's failures were test assumptions, not accepted product regressions: the two failure/kill
fixtures needed an observable transcript baseline (unobservable Claude has a preserved degraded
success path), and the working case now pins exactly three allowed Enters rather than incorrectly
expecting one. CARD-0502's no-retained-generation test retains every no-kill assertion and adds
metadata/input assertions at the controlled failure handoff; actual Enter-only now captures a
new token. The handler is internal for that controlled boundary test. No timeout was widened
and no assertion was loosened.

Base was built separately with `--property:OutputPath=bin-c501-base-326c349a/`. Each of these
exact filters executed one failing test at `eea127b000ad3078fbd324df2573707b07e87ded`:

- `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata`
  (`base-classification/base.trx`): unclassified HerdrPaneDisposalEndpointTests.
- `/*/*/ScopedVerificationInstructionTests/C487_G142`
  (`base-stage-order/base.trx`): expects the obsolete Code-to-Mutation stage order.
- `/*/*/TestLaneCategoryGuardTests/every_test_class_is_tagged_unit_xor_integration`
  (`base-lane-category/base.trx`): same missing Herdr category.
- `/*/*/TestClassificationPolicyTests/C487_G068`
  (`base-classification-policy/base.trx`): same missing Herdr category.

The Unit skip is `AgentTuiSecretProtectorTests.Restored_key_file_symlink_is_rejected_without_mutating_target`:
Windows file-symlink privilege is unavailable. Duration checks: Q, W after registering its
measured setup cost, and N have zero unlisted slow rows. U flags 40 rows in unchanged classes;
these performance observations were retained, not blanket-allowlisted or asserted baseline-equivalent.

## Pending SourceLanding Mutation and noticed gaps

All deliberate controls remain **PENDING**, with red/restore/fresh-build/green and discovery
owned by the explicitly commissioned post-land Mutation task:

| PC | Pending tests / variants |
|---|---|
| PC-1 | Pure replayed-marker test; queue replayed-marker method with Pending=False and interrupted Sent=True variants. |
| PC-2 | Previous-generation Pending retype, with the exact body replayed in history. |
| PC-3 | Previous-generation interrupted Sent revert/retype, with the exact body replayed in history. |
| PC-4 | Failed recovery charges; third failed recovery parks/unblocks. |
| PC-5 | Captured-generation kill and Enter-only incident wording. |
| PC-6 | Untyped terminal task (Failed, Canceled, Succeeded variants); three-orphan/live-row mixture. |
| PC-7 | Same-generation terminal-task body remains in recovery. |
| PC-8 | Typed-generation method (Enqueue, SendNow, persisted Immediate variants); the named mutant must fail SendNow. |

No new direct case covers failure charging of a multi-row recovered batch, a legacy row with
both generation and typing timestamp absent, or an exception escaping the Enter-only confirmation
path. Those are discovery gaps, not claims of tested guarantees. The plan's collapsed-paste,
same-generation expired already-typed brief, and post-land live activation observations remain
outside this ordinary battery. Backend-unreachable still defers without charging.

## Cleanup and handoff

Every owned command has exited. The original branch is restored. Final source and evidence report
are committed/pushed; the settlement message supplies the final full SHA including this report.

Automatic approval review rejected both the initial bulk output deletion and the narrower
validated literal-path deletion with only `blocked by policy`. Read-only validation established
31 producer-owned paths inside the exact worktree, with no reparse points. **None was deleted.**
`owned-output-inventory.txt`, `output-cleanup.json`, `final-output-hashes.json` and
`evidence-manifest.json` preserve the evidence. The caller subsequently identified this as the
known CARD-0455 policy issue and explicitly instructed Code to leave all 31 directories as-is
for the weekly cleanup-build-junk job and settle complete. This removes the Code completion
blocker; no further local cleanup is required for this task. Standard `obj/` files remain as usual.

Next: ordinary read-only Review of this diff, V/R evidence, inherited failures and pending PC
inventory. Restart target after authorized publication: **server**. Original landing owner:
`326c349a-dd36-4adb-8ff6-fe533d91a73a`. The caller records the companion obligation, lands that
original Code task after Review, activates the server through the canonical checkout/runbook,
and explicitly commissions SourceLanding Mutation. The manually removed live head is not a
remaining row to delete, and the post-land cancellation count must not be fixed at 65.

---

# FollowUp pass (task `babf4d32`): review defects F1 and F2

Review `b615578c` rejected `fdb56356`. Both defects are fixed on the same branch; nothing was
landed, deployed or restarted. PC execution still belongs to the post-land Mutation task.

## F1 (P1) — a parked head's composer is not empty

**The defect.** Parking the head at `MaxDeliveryAttempts` stops it being re-typed, but skipping a
row is not the same as emptying its composer, and parking deliberately does not restart the
session. The body was still standing there unsubmitted, so the next flush typed the next row's
body straight on top of it: the terminal received `<parked brief><next message>` as ONE prompt,
and the containment matcher then marked the next row `Delivered` for a prompt it never owned.
The earlier regression hid this by advancing the generation and calling `PrimeComposer("")`
before the second flush — a state no production path produces at that moment.

**The fix** (`DeliverNextLockedAsync`, after the `deliverable` filter; helper
`HeldBackTypingBlocksTheComposer`). A flush holds — `FlushResult.Nothing` / `LateConfirmed`,
no attempt charged, nothing parked, no kill — whenever a Pending row it is NOT going to deliver
was typed in the CURRENT generation and the whole head of its body is still on the rendered
screen. That is deliberately the same predicate pair (`DeliveryGenerationChanged` +
`HeadFragmentIsVisibleWhole`) the Enter-only recovery uses to conclude "that body is in the
composer"; the two must agree, or the queue would re-press Enter for a body it simultaneously
believed was gone. An unreadable snapshot also holds, matching
`RecoverDeliveryRunLockedAsync`'s existing "the composer cannot be shown empty" answer.

**Release paths, in the order they normally fire.** The late-confirm that already ran earlier in
the same flush is the ordinary one: a held body that really did land becomes `Sent` and stops
being a held-back row at all — which is also why the "body is in the scrollback, not the composer"
false positive self-resolves rather than wedging the queue. Then `CancelDeadBriefsAsync` for a
terminal task, a demonstrably cleared composer, and a new generation.

**Existing no-kill guards are untouched.** The hold is a defer; it reaches no kill path, and the
regression asserts `Killed == false` and the next row un-parked.

## F2 (P2) — the handoff nothing owned

Three separate gaps, all closed with ordinary coverage.

1. **Producer to recipient.** `AgentTaskCheckInterpreterTests` runs real production but reads the
   result back off the QUEUE ROW (`NotesToCallerAsync`); its caller session has no adapter, so
   nothing is ever typed. The queue suites run real delivery and recovery but hand-seed rows.
   CARD-0501's live failure lived exactly between them. New `CheckNoteDeliveryHandoffTests`
   carries a note produced by the real `AgentTaskCheckService` through the real queue to a
   complete, correlated `UserPrompt`, for both live recipient shapes — BUSY at production time
   and already ELIGIBLE — each finished by the Enter-only recovery after a swallowed first submit.
   The interpretation half stays with `AgentTaskCheckInterpreterTests`; the interpreter is not
   wired here, which is the plain slice-3 digest note — the shape of the stranded rows.
2. **The charge/handler boundary.** The attempt charge is saved before
   `HandleDeliveryFailureAsync` and in a different scope, because the handler reloads the rows and
   recomputes `parked` from the count it reads. A crash in between must cost the attempt; losing
   it would let an Enter-only cycle repeat without bound. `EnterOnlyConfirmLockedAsync` now
   reaches the existing `LandDeliveryBoundary` seam at `"queue-enter-only-charged"` (production
   installs the no-op base class), and the test crashes there twice, then shows the row parked on
   the third cycle with no further Enter.
3. **Multi-row failure charging.** A recovered run can be a CARD-0342 batch: one composed body,
   one Enter, several rows. Every row is charged, not only the head — otherwise the tail could
   re-enter the same cycle forever behind a parked head. This was named as a discovery gap in
   the section above; it is now covered.

## Delivery handoff inventory

**Corrected by the re-review pass (`ceb90cb5`).** The first version of this table stopped at the
recipient and treated the interpretation as somebody else's problem, which is what review F2
rejected: the rows that stranded in the live incident were Check EXECUTION BRIEFS, on the leg
into the interpreter, and that leg had no owner at all here. A produced check note crosses two
delivery legs, and both are inventoried below. "Owner" is the ordinary test that fails if the
handoff breaks.

### Leg I — producing the interpretation and getting its brief into the interpreter

| # | Handoff | Production site | What is lost if it breaks | Ordinary owner |
|---|---|---|---|---|
| I-1 | check to interpretation request | `AgentTaskCheckService.InterpretAsync` then `SpecialistTaskRunner.RunAsync` | no interpretation is ever asked for; every note silently degrades to the digest | `A_dispatched_interpretation_reaches_an_already_eligible_recipient_whole` (the run task exists, pinned to the provisioned interpreter) |
| I-2 | interpretation row that cannot be written | `SpecialistTaskRunner.CreateRunTaskAsync` failing | a failed write silencing the check entirely | `An_interpretation_that_cannot_be_persisted_still_delivers_a_degraded_note_whole` |
| I-3 | Queued run to the interpreter's live session | `AgentTaskDispatcher.TickAsync` then `PlaceOnStandingAgentAsync` | a second session for one standing agent, or a brief that never leaves the queue | `A_dispatched_interpretation_reaches_*` (`Dispatched`, `AgentSessionId == InterpreterSessionId`) |
| I-4 | brief row to the interpreter's typing and recovery | the enqueue's idle delivery, then `EnterOnlyConfirmLockedAsync` | THE live failure: a brief standing unsubmitted in the interpreter's composer forever | `A_dispatched_interpretation_reaches_*` (first submit swallowed, no retype, Enter-only finishes) |
| I-5 | submit to the interpreter's `UserPrompt` | transcript ingestion then verification | a brief marked `Delivered` that the interpreter never received whole | `AssertBriefArrivedWholeAsync` — exactly one prompt, equal to the row body, carrying the interpretation's task marker, at `DeliveryAttempts >= 1` |

### Leg H — the note reaching the recipient

| # | Handoff | Production site | What is lost if it breaks | Ordinary owner |
|---|---|---|---|---|
| H-1 | facts (plus interpretation) to note body | `AgentTaskCheckService.RunCheckAsync` then `BuildNote` | a note that cannot be traced to the task it is about | `CheckNoteDeliveryHandoffTests` (header short id, and the settled reading appearing in the delivered prompt); `AgentTaskCheckInterpreterTests` owns the body's other arms |
| H-2 | note to queue row | `_queue.EnqueueAsync(parentSession, …, Check, ConversationKey(taskId), sourceTaskId)` | correlation: nothing can tie the row back to the checked task | `CheckNoteDeliveryHandoffTests` (`ConversationKey`, `SourceTaskId` on the persisted row) |
| H-2b | queue row that cannot be written | the same enqueue, failing | a half-published check: a timeline row saying the caller was told, and no note | `A_note_whose_queue_row_cannot_be_persisted_fails_cleanly_and_the_retry_delivers` |
| H-3 | queue row to first typing | `DeliverNextLockedAsync` (busy defers, idle types) | a busy recipient typed into mid-turn | `A_dispatched_interpretation_reaches_a_busy_recipient_whole_after_recovery`, `A_digest_note_reaches_a_busy_recipient_as_one_whole_prompt_after_recovery` (attempts 0, no inputs while working) |
| H-4 | typed body to submit | `DeliverAsync` and the confirm loop | a swallowed Enter silently reported as delivered | all four recipient handoff tests (attempts 1, Pending, zero prompts) |
| H-5 | standing body to Enter-only recovery | retry branch then `EnterOnlyConfirmLockedAsync` | a duplicate retype, or an Enter into a dead composer | `SessionMessageQueueWedgedHeadTests` S2 gate methods; the already-eligible handoff tests assert no retype |
| H-6 | Enter-only failure to attempt charge | `EnterOnlyConfirmLockedAsync` charge | an uncharged cycle repeating forever (the CARD-0501 loop) | `Failed_Enter_only_recovery_charges_an_attempt`, `Failed_Enter_only_recovery_charges_every_row_of_the_batch` |
| H-7 | charge save to failure handler | save, then `HandleDeliveryFailureAsync` in its own scope | a crash losing the charge and unbounding the cycle | `A_crash_between_the_charge_and_the_failure_handler_keeps_the_attempt_charged` |
| H-7b | crashed `Sent` row back into recovery | `LoadInterruptedSentRunAsync` then the cap gate in `RecoverDeliveryRunLockedAsync` | F3: recovery spending attempt `MaxAttempts + 1`, and every one after it, because the durable cap was only filtered on the Pending side | `Crashed_Sent_row_at_the_cap_parks_instead_of_Entering_again(Flush\|Sweep)`, `Repeated_sweeps_over_a_capped_crashed_row_add_no_attempts_and_no_Enters`, `One_capped_row_parks_the_whole_interrupted_batch` |
| H-8 | parked head to the next row | `deliverable` filter plus the F1 composer hold | the parked body riding into the next row's prompt (F1) | `Parked_head_left_in_the_composer_holds_the_next_message` plus its two release arms; `A_relaunch_releases_the_queue_behind_a_capped_crashed_row` for the F3 shape |
| H-9 | submit to recipient `UserPrompt` | transcript ingestion then verification | a row marked `Delivered` for a prompt the agent never received whole | all four recipient handoff tests (`prompts.ShouldBe([row.Body])`, exact); the spilled arm reads the file |
| H-10 | terminal task to brief cancellation | `CancelDeadBriefsAsync` | orphans accumulating behind the head | `SessionMessageQueueWedgedHeadTests` S4 methods |
| H-11 | capped row to late-confirm | `LateConfirmAttemptedMessagesAsync`, which runs before the cap gate | a body that really landed staying parked forever because the cap silenced its evidence | `A_capped_crashed_row_whose_body_landed_is_still_late_confirmed` |

Still not owned by an ordinary test, and deliberately so: the interpreter AGENT itself — no real
Claude reads the brief, so the interpretation task is settled directly with a reading, and its
wait/timeout/busy/empty arms stay with `AgentTaskCheckInterpreterTests`; the ROUTED publication
path (`SpecialistRequestService` + `PublishCheckRequestAsync`), which `SpecialistPublicationTests`
owns and which is a different producer from the `SpecialistTaskRunner` one the live incident used;
a legacy row with BOTH the generation and the typing timestamp absent; an exception escaping the
Enter-only confirmation path; and a collapsed `[Pasted text #N]` body that shows no head at all
(pre-existing, named out of scope by the plan). Backend-unreachable still defers without charging.

## FollowUp coverage IDs

| ID | Coverage | Class / check |
|---|---|---|
| V-7 | F1 hold, both release arms, previous-generation no-regression twin | `SessionMessageQueueWedgedHeadTests` (`Parked_head_left_in_the_composer_holds_the_next_message`, `Cleared_composer_releases_the_next_message_with_its_exact_body`, `Parked_head_from_a_previous_generation_does_not_hold_the_next_message`, strengthened `Third_failed_Enter_only_recovery_parks_and_unblocks_the_queue`) |
| V-8 | F2 producer-to-recipient handoff, busy and already-eligible | `CheckNoteDeliveryHandoffTests` |
| V-9 | F2 charge/handler crash boundary and multi-row batch charging | `SessionMessageQueueWedgedHeadTests` (`A_crash_between_the_charge_and_the_failure_handler_keeps_the_attempt_charged`, `Failed_Enter_only_recovery_charges_every_row_of_the_batch`) |

## FollowUp V/R rerun

All runs used one build at `5fb816bf` into `--property:OutputPath=bin-c501fu/` (forward slash),
then `dotnet run --no-build` against that output. Pty and server assemblies ran sequentially,
never concurrently. TRX under `.antiphon/c501-fu/<run>/` in the worktree. The 16 `bin-c501fu`
directories this pass produced were deleted afterwards; the 31 `bin-c501-326c349a` directories
from the original Code task remain under the caller's CARD-0455 waiver and were not touched.

| Run | Project | Filter | Outcome |
|---|---|---|---|
| P | Antiphon.Agents.Pty.Tests | `/*/*/(ComposerDeliveryEvidenceTests*)\|(SubmitEvidenceTests*)/*` | 32/32 pass (`pty/pty.trx`) |
| Q | Antiphon.Tests | `/*/*/(SessionMessageQueueWedgedHeadTests*)\|(SessionMessageQueueInterruptedAttemptTests*)\|(SessionMessageQueueDeliveryVerificationTests*)\|(SessionMessageQueueServiceTests*)\|(ParkedMessageSweepServiceTests*)\|(AgentTaskDeliveryWatchdogTests*)\|(CheckNoteDeliveryHandoffTests*)/*` | 269/269 pass, 7m26s (`queue/queue.trx`). 262 before this pass; the seven added are the three F1 methods, the two F2 methods and the two handoff methods. |
| N | Antiphon.Tests | `/*/*/SessionMessageQueueGrokPtyIntegrationTests/*` | 4/4 pass (`grok/grok.trx`) |
| U | Antiphon.Tests | `/*/*/*/*[Category=Unit]` | 2,351 total: 2,346 pass, 4 fail, 1 skip (`unit/unit.trx`) |

V-1..V-6 and R-2..R-8 are all re-confirmed PASS inside P, Q and N. V-7, V-8 and V-9 pass in Q.

R-1's four failures are the same inherited set the original Code pass reproduced at
`eea127b0`, unchanged in count and in cause. Their messages name only classes and text this
branch never touches: three are `lane-xor` / untagged on
`Antiphon.Tests.Application.HerdrPaneDisposalEndpointTests`
(`Registry_matches_compiled_metadata`, `every_test_class_is_tagged_unit_xor_integration`,
`C487_G068`), and `ScopedVerificationInstructionTests.C487_G142` expects the obsolete
Code-to-Mutation stage order. `git diff --name-only origin/master...HEAD` contains no Herdr,
stage-bundle, classification or lane-category file. The skip is unchanged
(`AgentTuiSecretProtectorTests.Restored_key_file_symlink_is_rejected_without_mutating_target`,
Windows file-symlink privilege).

`Registry_matches_compiled_metadata` also validates the Slow registry: its only error is the
pre-existing Herdr one, so `CheckNoteDeliveryHandoffTests`'s new allowlist entry and its
`Integration`/`Slow` tagging are both accepted. No timeout was widened, no assertion loosened,
and no retry added anywhere in this pass.

# Re-review pass (task `ceb90cb5`): landing blockers F2 and F3

Re-review `2ce2c755` rejected `ac2d49d8`. Both blockers are fixed; the delivery handoff
inventory above is corrected rather than appended to, because its first version was wrong about
what it covered.

## F3 (serious) — the durable attempts cap did not bind interrupted-Sent recovery

`RecoverDeliveryRunLockedAsync` is reached from `DeliverNextLockedAsync` via
`LoadInterruptedSentRunAsync`, which selects rows on `Status == Sent`, `DeliveryVerdict == null`
and the attempt's age — and nothing else. The `DeliveryAttempts < MaxAttempts` filter lives
further down, over the PENDING set, so it never saw these rows at all.

The state that exposes it is the one the FollowUp pass had just documented as deliberate: the
Enter-only charge commits, and `HandleDeliveryFailureAsync` is called afterwards in its own
scope. A crash in between leaves the row `Sent`, verdict `null`, already AT the cap. The next
sweep then pressed Enter again and charged attempt 4 — and the one after that attempt 5, because
nothing in the path was reading the counter. Reproduced from both entry points (direct
`FlushSessionAsync` and the automatic `FlushStrandedQueuesAsync`), 3 -> 4 attempts and extra
Enters, before the fix.

**The fix** (`SessionMessageQueueService.cs`, `RecoverDeliveryRunLockedAsync`): any row of the
recovered run at `DeliveryAttempts >= MaxAttempts` falls through to the existing revert —
`Pending` with attempts kept. That is exactly the parked shape every
`DeliveryAttempts >= MaxAttempts` predicate already reads, so the row becomes visible and parked
in the queue view, is still late-confirmed on every later flush, and is held behind the F1
composer gate so nothing is typed on top of a body that may still be standing there. No verdict
is invented: the crash lost that observation, and stamping one would claim evidence we never had.
The check is all-or-nothing across the run, because a recovered run is ONE composed body under
ONE Enter — a capped head means the tail must not be submitted either.

Three things it deliberately does NOT change: late-confirm still runs first (H-11), an
interrupted row BELOW the cap still gets the Enter-only rescue, and nothing kills.

## F2 — the handoff test proved the wrong path

The first F2 pass disabled the interpreter and delivered a plain digest note. That exercises
`_queue.EnqueueAsync` at the recipient and nothing else, while the rows that stranded in the live
incident were Check EXECUTION BRIEFS on the leg INTO the interpreter — produced by
`SpecialistTaskRunner`, placed by `AgentTaskDispatcher`, and delivered into the standing
interpreter's own session queue. None of that had an owner.

`CheckNoteDeliveryHandoffTests` now runs the real graph: the real `CheckInterpreterProvisioner`
creates the standing interpreter, `AgentTaskCheckService.InterpretAsync` asks
`SpecialistTaskRunner` for a reading, the real `AgentTaskDispatcher.TickAsync` places the
interpretation on the interpreter's live session, and the brief that enqueues there has its first
submit swallowed so the Enter-only recovery is what finishes it. The assertion is the
interpreter's own `UserPrompt`: exactly one, equal to the persisted row body, carrying the
interpretation's task marker, taken after its attempt floor (`DeliveryAttempts >= 1`). The note
that interpretation produces then travels the recipient leg for both live shapes, busy and
already-eligible, through the same recovery.

Both persistence failures are covered: the interpretation row that cannot be written (leg
degrades to `QueueFailed`, the note ships carrying `INTERPRETER DOWN` and the whole digest — over
the brief ceiling, so it ships as a pointer and the test reads the spill file to prove it intact)
and the recipient queue row that cannot be written (`CheckOutcome.DeliveryFailed`, no partial row,
no timeline row claiming the caller was told, and the next run delivers whole).

The two interpreter-off digest cases are kept and renamed (`A_digest_note_reaches_*`): a host
with no interpreter is a real configuration, and that note shape is what the 65 stranded rows on
session `cea73d57` looked like.

## Re-review coverage IDs

| ID | Coverage | Class / check |
|---|---|---|
| V-10 | F3 cap gate on interrupted recovery, both entry points, repeated cycles, mixed batch | `SessionMessageQueueWedgedHeadTests` (`Crashed_Sent_row_at_the_cap_parks_instead_of_Entering_again(Flush\|Sweep)`, `Repeated_sweeps_over_a_capped_crashed_row_add_no_attempts_and_no_Enters`, `One_capped_row_parks_the_whole_interrupted_batch`) |
| V-11 | F3 controls: below-cap rescue, late-confirm past the gate, relaunch release | `Crashed_Sent_row_below_the_cap_still_recovers_by_Enter_only`, `A_capped_crashed_row_whose_body_landed_is_still_late_confirmed`, `A_relaunch_releases_the_queue_behind_a_capped_crashed_row` |
| V-12 | F2 real producer + dispatcher + interpreter-leg receipt, both recipient shapes | `A_dispatched_interpretation_reaches_an_already_eligible_recipient_whole`, `A_dispatched_interpretation_reaches_a_busy_recipient_whole_after_recovery` |
| V-13 | F2 persistence failures on each leg | `An_interpretation_that_cannot_be_persisted_still_delivers_a_degraded_note_whole`, `A_note_whose_queue_row_cannot_be_persisted_fails_cleanly_and_the_retry_delivers` |

Red-then-green for V-10: with `server/Application/Services/SessionMessageQueueService.cs` checked
out at `ac2d49d8` and the new tests unchanged, five methods fail (`h.Adapter.Inputs should be
empty but had …` on four of them, and the relaunch case on the un-reverted `Sent` status); the two
V-11 controls pass on both sides, which is what makes them controls.

## Re-review V/R rerun

`Antiphon.Tests` built once into `--property:OutputPath=bin-c501f3/` and `Antiphon.Agents.Pty.Tests`
into `--property:OutputPath=bin-c501rr/` (forward slashes), then `dotnet run --no-build` against
those outputs. The two assemblies ran sequentially, never concurrently.

| Run | Project | Filter | Outcome |
|---|---|---|---|
| P | Antiphon.Agents.Pty.Tests | `(ComposerDeliveryEvidenceTests*)\|(SubmitEvidenceTests*)` | 32/32 pass |
| Q1 | Antiphon.Tests | `(SessionMessageQueueWedgedHeadTests*)\|(CheckNoteDeliveryHandoffTests*)\|(SessionMessageQueueInterruptedAttemptTests*)` | 59/59 pass, 1m53s (38 wedged-head, up from 31; 6 handoff, up from 2) |
| Q2 | Antiphon.Tests | `(SessionMessageQueueDeliveryVerificationTests*)\|(SessionMessageQueueServiceTests*)\|(ParkedMessageSweepServiceTests*)\|(AgentTaskDeliveryWatchdogTests*)` | 221/221 pass, 5m45s |
| N | Antiphon.Tests | `(SessionMessageQueueGrokPtyIntegrationTests*)\|(AgentTaskCheckInterpreterTests*)\|(SpecialistPublicationTests*)\|(AgentTaskStandingAgentDispatchTests*)` | 57/57 pass, 55s |
| U | Antiphon.Tests | `[Category=Unit]` | 2,351 total: 2,346 pass, 4 fail, 1 skip |

Q was split into Q1/Q2 so each run fits one foreground window; together they are the same 280
methods the FollowUp pass ran as one 269-method Q, plus the eleven added here. N was widened
beyond the previous pass's Grok-only filter to take the three suites this pass's new wiring could
plausibly disturb — the interpreter producer, the routed publication path and standing-agent
dispatch. All three are unchanged and green.

R-1's four failures are the same inherited set, unchanged in count and cause:
`Registry_matches_compiled_metadata` and `every_test_class_is_tagged_unit_xor_integration` name
exactly one offender, `Antiphon.Tests.Application.HerdrPaneDisposalEndpointTests`; `C487_G068` is
the same lane classification; `ScopedVerificationInstructionTests.C487_G142` expects the obsolete
Code-to-Mutation stage order (`Compose(AgentTaskRole.Code)`). `git diff --name-only
origin/master...HEAD` contains no Herdr, stage-bundle, classification or lane-category file. The
one skip is unchanged. The registry check accepting only that one offender is also what confirms
this pass's renamed methods and its `Integration`/`Slow` tagging are still valid.

No timeout was widened, no assertion loosened, no retry added. PC-1…PC-8 and the new F1/F3
controls remain the commissioned post-land Mutation task's work.

# Re-review round 2 (task `0a2217c5`): three findings, one shape

Round 2 reported F3-R2, F2-R2a and F2-R2b as separate defects and asked for the underlying
pattern instead of three patches. They are the same pattern: **every caller that asks "what needs
recovery / discovery / reconciliation" wrote its own partial filter, and the partial filters
disagreed with what the acting code would actually do.**

## The invariant

**Discovery must be a superset of action.** `server/Application/Services/QueueAttention.cs` states
it once, as `Expression`s so the same predicate runs in the database on a discovery query and in
memory on a loaded run. `FlushStrandedQueuesAsync` (both of its queries) and
`LoadInterruptedSentRunAsync` now read it instead of restating it.

The asymmetry inside it is the whole finding, so it is written down rather than merely coded: the
attempts cap gates the `Pending` arms and NOT the interrupted-`Sent` arm. "Parked" is a RESTING
state a row must first be brought **to**; it is not a licence to stop looking at rows that have
not reached it.

## F3-R2 — the root issue

`FlushStrandedQueuesAsync` excluded `DeliveryAttempts >= MaxAttempts` on every arm. Right for a
Pending row: parking means exactly "no automatic retry", and a session whose only pending message
is parked must not be woken for it. Wrong for a crashed `Sent` row at the cap, because
`LoadInterruptedSentRunAsync` has no cap clause **on purpose** — that row still owes a
late-confirm and a revert to the visible parked shape, and the F3 cap gate added in round 1
withholds its Enter regardless.

So a session holding ONE capped crashed `Sent` row and nothing else was never brought into scope.
The row stayed `Sent` forever: not delivered, not parked, therefore also invisible to
`ParkedMessageSweepService`, which only discards `Pending`-at-the-cap. Nothing but an unrelated
direct flush could settle it. That is the live-incident shape — one wedged head, nothing behind
it — and round 1's `Crashed_Sent_row_at_the_cap_parks_instead_of_Entering_again(Sweep)` only
passed because its fixture seeded a second live row to widen the sweep on the wedged row's
behalf. The companion was papering over the defect, not describing the shape.

Widening discovery types nothing and charges nothing: the flush path's own gates (the cap, the F1
composer hold, the generation gate, the working/Starting guards) still decide. And the row it
brings to rest stops matching, so the cost is one extra pass, not a standing one — pinned by
`A_parked_row_stops_being_discovered_once_it_is_at_rest`.

## F2-R2a — the abandoned row that stayed dispatchable

A failed `SaveChangesAsync` does not untrack what it tried to insert; EF only accepts changes on
success. `SpecialistTaskRunner.CreateRunTaskAsync` therefore left the interpretation task `Added`
on the **scoped** context, and the check's own next save through that same context published it.
Measured, not inferred: with the fix reverted, `CountAsync(t => t.AgentId == interpreter)` returns
**1** after the write "failed".

Round 1's assertion read `t.Status != AgentTaskStatus.Queued`, which carved out exactly the row
that survived. It now demands no row at all, and a second test drives the consequence out: a real
`AgentTaskDispatcher.TickAsync` after the failed write must find nothing to place. Left in, the
dispatcher would place a `Queued` run pinned to the standing interpreter on its live session,
enqueue a brief for a check that already shipped its degraded note, bill the result, and occupy
the seat against the next real interpretation (`PlaceOnStandingAgentAsync` allows one task at a
time on a live composer).

## F2-R2b — the window between the reuse path's two commits

`TickAsync` commits `Dispatched`, then `DeliverReuseMessagesAsync` inserts the brief in a separate
commit in a separate scope. Interrupted in between — the enqueue throws, or the process dies — the
durable record is a `Dispatched` task on a live reused session with no brief row of its own. No
queue row means nothing in the queue's own recovery can see it; the delivery watchdog owns it.
Production already handled this (`briefRow` null, `started` false, so the never-started arm
fires); what was missing was the proof, which is what round 2 asked for. Both arms are pinned: a
caught throw leaves a `DeliveryTransportFailed` incident, a crash leaves nothing at all, and
neither may change the verdict. The fixture also keeps the refocus `/compact` that DID commit,
because that housekeeping is the evidence that used to read as "it started".

## The property

`The_sweep_reaches_the_same_resting_state_as_a_direct_flush` is the general test round 2 asked
for. For a session holding ONE row, the automatic sweep and a direct flush must reach the same
`(Status, Attempts, Verdict, bodies typed, submits)`. The sweep only chooses WHICH sessions to
flush; the direct flush is the oracle because it has no discovery filter of its own to be wrong.
Six shapes compare resting outcomes, including parked-`Pending` and the interrupted-window
bound. They do not independently prove candidate exclusion: downstream gates can mask excessive
discovery. The aged `NoSubmitOutputPending` shape also cannot isolate its recovery disjunct.
The round-3 FollowUp below adds direct predicate assertions and fresh-row cases for those claims.

## Positive controls (red then green)

Each was run with only the named production change reverted, then restored.

| PC | Reverted | Test | Red observed |
|---|---|---|---|
| R2-PC-1 | `SessionMessageQueueService.cs` to 92857c4a | `A_lone_capped_crashed_row_is_discovered_and_parked_by_the_sweep` | `row.Status should be Pending but was Sent` |
| R2-PC-2 | same | `The_sweep_reaches_the_same_resting_state_as_a_direct_flush` | 1/6 failed, `(CappedCrashedSent)` only: `Rest { Status = Pending, ... }` vs `Rest { Status = Sent, ... }`. The other five shapes matched the outcome comparison; this did not independently test the Pending discovery guards. |
| R2-PC-3 | `SpecialistTaskRunner.cs` to 92857c4a | `An_abandoned_interpretation_is_never_dispatched_after_its_write_failed` and `An_interpretation_that_cannot_be_persisted_still_delivers_a_degraded_note_whole` | both failed; `CountAsync(AgentId == interpreter) should be 0 but was 1` |
| R2-PC-4 | `AgentTaskDispatcher.cs`, the never-started arm's condition inverted to `started && briefNeverTyped` | `a_reuse_brief_lost_between_the_two_commits_is_failed` | both arguments (True/False) failed |

R2-PC-4 mutates rather than reverts, because F2-R2b is a coverage gap over behaviour that was
already correct; the mutation is what shows the new test has teeth. `git status` was verified
clean after each restore.

## Verification runs (this pass)

| Run | Filter | Result |
|---|---|---|
| R2-A | `(SessionMessageQueueWedgedHeadTests*)`, `(SessionMessageQueueInterruptedAttemptTests*)`, `(CheckNoteDeliveryHandoffTests*)`, `(AgentTaskReuseEnqueueTests*)` | 75/75 pass, 2m05s (`queue.trx`) |
| R2-B | `(AgentTaskDeliveryWatchdogTests*)`, `(ParkedMessageSweepServiceTests*)`, `(SessionMessageQueueDeliveryVerificationTests*)`, `(SessionMessageQueueServiceTests*)` | 223/223 pass, 6m08s (`sweeps.trx`) |
| R2-C | Historical selector also named nonexistent `SpecialistRequestServiceTests` and `AgentTaskCheckServiceTests`; actual owners are `SpecialistTaskRunnerDeadlineTests` and `AgentTaskCheckInterpreterTests` | 49/49 pass: **7** SpecialistTaskRunnerDeadlineTests + **42** AgentTaskCheckInterpreterTests; **0** from each nonexistent class, 46s (`specialist.trx`). Corrected exact-class reruns are recorded below. |
| R2-D | `[Category=Unit]` | 2,351 total: 2,346 pass, 4 fail, 1 skip, 2m06s (`unit.trx`) |

R2-B and R2-C are the widening's blast radius: `ParkedMessageSweepService` is the other reader of
"at the cap", and the specialist/check suites are the other users of the detached context.

R2-D's four failures are the same inherited set as R-1, unchanged in count and cause — the two
guard tests still name exactly one offender,
`Antiphon.Tests.Application.HerdrPaneDisposalEndpointTests`, and none of this pass's new or
renamed methods. TRX artifacts are under `.antiphon/c501r2/`.

No timeout was widened, no assertion loosened, no retry added. One assertion was TIGHTENED
(`t.Status != Queued` to no row at all), which is the F2-R2a fix.

## Not fixed, and why

A capped crashed `Sent` row on a session that is no longer live is still not reached: the sweep
intersects its candidates with `_runtime.ListLiveSessions()`, and a direct flush into a dead
session is not possible either. That bound is older than this card and unchanged by it — recovery
of a dead session's queue belongs to session-end reconciliation, not to a delivery sweep. Named
here so the next reader does not mistake it for part of this fix.

# Round-3 evidence repair (Code FollowUp `f95bd1fe`)

**F1-R3 and F2-R3 are fixed.** Ordinary verification is complete: 399 affected cases passed;
Unit has 2,346 passes, four failures reproduced individually at the exact reviewed base, and
one existing skip. Return to ordinary read-only Review. No production code changed in this pass.

## Source and evidence identity

- Fix base: `92fc5a55c8d03274cc6875ddd37a6e0cd3a18b19`.
- Built/tested implementation: `02940c4d486e4c8619341449a79497aa6984e104`, committed and pushed
  before verification. The final report commit changes documentation only; its full SHA is in
  the task settlement and `final-source.json`.
- Branch: `feat/card-task-326c349a`; exact worktree: `C:\Antiphon\worktrees\card-task-326c349a`.
- Original Code task / landing owner: `326c349a-dd36-4adb-8ff6-fe533d91a73a`.
- Plan: `docs/superpowers/plans/2026-09-14-card-0501-check-interpreter-wedged-queue-head-plan.md`.
- Evidence root: `C:\src\Antiphon\.git\antiphon\evidence\f95bd1fe`.
- Changed files: `SessionMessageQueueWedgedHeadTests.cs`, this report, the plan and
  `docs/session-runtime-invariants.md`. `production-diff.txt` confirms an empty `server/` + `src/`
  diff from the fix base.

## F1-R3: independent discovery evidence

`A_parked_row_stops_being_discovered_once_it_is_at_rest` now executes both production
`QueueAttention` discovery expressions through PostgreSQL. It asserts the candidate row is
included while capped/interrupted Sent, and excluded after the real sweep parks it Pending.
The downstream attempts cap cannot satisfy that candidate-list assertion. The no-input checks
remain, with the old claim that an empty composer made them sufficient removed.

Two additional methods use `StrandedAgeSeconds = 600` and a freshly persisted `CreatedAt`:

- `Fresh_NoSubmitOutput_is_discovered_before_stranded_age_and_delivered_whole`: direct candidate
  inclusion, one Enter, exact submitted body and complete matching persisted `UserPrompt`, Sent /
  Delivered, and attempts still one.
- `Fresh_ordinary_Pending_is_excluded_until_stranded_age`: direct candidate exclusion, zero
  inputs/prompts and an unchanged unattempted Pending row.

All three methods ran with `alwaysOn=false` and `alwaysOn=true`: **six passing expanded cases**.
The original six-shape direct/sweep comparison remains as outcome coverage; its parked and aged
NoSubmitOutput arms are no longer described as independent discovery proof.

## F2-R3: actual coverage owners

The historical R2-C row above is corrected: its 49 passes belonged to two classes, and the two
nonexistent class names contributed zero. This pass reran the real classes separately:

| Behavior | Actual coverage owner / method | Fresh class outcome |
|---|---|---|
| Check production, interpreter success/degradation, persisted reading and caller-note formatting | `AgentTaskCheckInterpreterTests`, including `a_settled_interpretation_replaces_the_digest_in_the_note`, `the_check_event_stores_the_reading_above_the_digest` | **42/42 passed**, `interpreter/run.trx` |
| SpecialistRequestService routing admission and legacy fallback | Same class: `an_undeclared_chain_still_runs_the_legacy_interpreter`, `a_declared_but_unqualified_chain_still_runs_the_legacy_interpreter`, `a_declared_and_qualified_chain_routes_through_the_request_graph` | All three methods passed within those 42 cases |
| SpecialistTaskRunner hold, deadline and cancellation boundaries | `SpecialistTaskRunnerDeadlineTests`, including `Terminal_read_returning_after_deadline_is_late`, `Hold_and_dispatch_race_cancel_only_queued` (both arguments), `Expired_ensure_return_does_not_create_work` | **7/7 passed**, `specialist/run.trx` |
| Failed interpretation persistence, no abandoned dispatch, and complete interpreter/recipient delivery | `CheckNoteDeliveryHandoffTests` | **7/7 passed**, `queue/run.trx` |
| Routed publication rollback and durable identity across two queue instances | `SpecialistPublicationTests.Card0415_V14_publication_rolls_back_and_two_queue_instances_reuse_one_durable_identity` | **1/1 passed**, `native/run.trx` |

These are bounded owners for the affected behaviors, not a claim that all SpecialistRequestService
behavior ran. The producer/recipient distinction and fake interpreter settlement described above
still apply.

## Commands, filters and expanded counts

Built each test project once into `--property:OutputPath=bin-c501-f95bd1fe/`:
`dotnet build tests/<project> --property:OutputPath=bin-c501-f95bd1fe/ --nologo`.
Server build: zero errors / 228 warnings; Pty build: zero errors / one warning. Server tests
finished before building/running Pty tests. The separate base build used its own output/worktree.

Each invocation used:

```powershell
dotnet run --project tests/<project> --no-build --property:OutputPath=bin-c501-f95bd1fe/ -- --treenode-filter '<filter>' --report-trx --report-trx-filename run.trx --results-directory 'C:\src\Antiphon\.git\antiphon\evidence\f95bd1fe\<run>'
```

| Run | Project | Exact filter | Actual per-class outcome |
|---|---|---|---|
| unit | Antiphon.Tests | `/*/*/*/*[Category=Unit]` | 2,351 cases: 2,346 passed / 4 failed / 1 skipped |
| queue | Antiphon.Tests | `/*/*/(SessionMessageQueueWedgedHeadTests*)\|(SessionMessageQueueInterruptedAttemptTests*)\|(CheckNoteDeliveryHandoffTests*)\|(AgentTaskReuseEnqueueTests*)/*` | WedgedHead **51**, InterruptedAttempt **15**, CheckNoteDeliveryHandoff **7**, AgentTaskReuseEnqueue **7**: **80/80 passed** |
| sweeps | Antiphon.Tests | `/*/*/(AgentTaskDeliveryWatchdogTests*)\|(ParkedMessageSweepServiceTests*)\|(SessionMessageQueueDeliveryVerificationTests*)\|(SessionMessageQueueServiceTests*)/*` | Watchdog **75**, ParkedSweep **9**, DeliveryVerification **114**, QueueService **25**: **223/223 passed** |
| interpreter | Antiphon.Tests | `/*/*/AgentTaskCheckInterpreterTests/*` | **42/42 passed** |
| specialist | Antiphon.Tests | `/*/*/SpecialistTaskRunnerDeadlineTests/*` | **7/7 passed** |
| native | Antiphon.Tests | `/*/*/(SessionMessageQueueGrokPtyIntegrationTests*)\|(SpecialistPublicationTests*)\|(AgentTaskStandingAgentDispatchTests*)/*` | Grok **4**, Publication **1**, StandingDispatch **10**: **15/15 passed**, zero skips |
| pty | Antiphon.Agents.Pty.Tests | `/*/*/(ComposerDeliveryEvidenceTests*)\|(SubmitEvidenceTests*)/*` | ComposerDeliveryEvidence **24**, SubmitEvidence **8**: **32/32 passed** |

The table escapes `|` for Markdown; actual selectors contain plain `|`. Each run directory holds
fresh `run.trx`, `run.log`, `execution.json`, `methods.json`, `classes.json` and `duration.log`.
`inspect-trx.py` joins results to definitions, rejects zero/unexpected class counts, and checks
every scoped source test method and Arguments count. `run-check.ps1` preserves each native test
exit (Unit 2; all affected selections 0). No namespace or full-assembly selection was used.
Aggregate ordinary executions: **2,750 cases; 2,745 passed, four failed, one skipped**.

## Every ordinary verification ID

| ID | Actual outcome / shared run |
|---|---|
| V-1 | PASS, pty: 24 ComposerDeliveryEvidence cases including the six original new cases. |
| V-2 | PASS, queue: generation stamps, both gates, refunds and legacy variants. |
| V-3 | PASS, queue: failed Enter charge, parking, captured-generation kill and working guard. |
| V-4 | PASS, queue: terminal-task, composer safety, expiry and human SendNow variants. |
| V-5 | PASS: database fixtures migrate; no pending model changes using the built output (`model-check.log`); generated migration unchanged. |
| V-6 | PASS: runtime/runbook/plan-pointer audit, empty production diff and `git diff --check` (`v6-doc-audit.txt`, `diff-check.log`). |
| V-7 | PASS, queue: held composer and release paths. |
| V-8 | PASS, queue: CheckNoteDeliveryHandoffTests 7/7. |
| V-9 | PASS, queue: persisted charge/handler crash boundary and multi-row charging. |
| V-10 | PASS, queue: interrupted cap, Flush/Sweep, repeated cycles and mixed batch. |
| V-11 | PASS, queue: below-cap rescue, late-confirm and relaunch release. |
| V-12 | PASS, queue: real producer/dispatcher with complete interpreter-leg receipt in both recipient shapes. |
| V-13 | PASS, queue: failed interpretation persistence and failed note enqueue/retry; abandoned row never dispatches. |
| V-14 | PASS, queue: direct pre/post-parking candidate assertions, two arguments. |
| V-15 | PASS, queue: fresh NoSubmitOutput and ordinary Pending, four arguments total. |
| V-16 | PASS, interpreter 42/42 and specialist 7/7, each selected individually. |
| R-1 | INHERITED RED, unit: 2,346 pass / four base-reproduced failures / one skip. |
| R-2 | PASS, pty: SubmitEvidenceTests 8/8. |
| R-3 | PASS, queue: SessionMessageQueueInterruptedAttemptTests 15/15. |
| R-4 | PASS, sweeps: SessionMessageQueueDeliveryVerificationTests 114/114. |
| R-5 | PASS, sweeps: SessionMessageQueueServiceTests 25/25. |
| R-6 | PASS, sweeps: ParkedMessageSweepServiceTests 9/9. |
| R-7 | PASS, sweeps: AgentTaskDeliveryWatchdogTests 75/75. |
| R-8 | PASS, native: SessionMessageQueueGrokPtyIntegrationTests 4/4, zero skips. |

EF ran `dotnet ef migrations has-pending-model-changes --project server --no-build` with
`OutputPath=bin-c501-f95bd1fe/`, `AppendTargetFrameworkToOutputPath=false`, disabled runner,
supervision, channel bridge and Hangfire workers, runner URL `http://127.0.0.1:1` and a design-only
database on port 1. No live queue or live database was modified, and no service was restarted.

### Fresh base reproduction and timing

At exact base `92fc5a55c8d03274cc6875ddd37a6e0cd3a18b19`, a separate build in
`C:\Antiphon\verification\card0501\f95bd1fe-base` used `bin-c501-f95bd1fe-base/`.
Each exact method filter below executed **one failed case**, with an identical error to Unit:

- `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata` (`base-classification`).
- `/*/*/TestClassificationPolicyTests/C487_G068` (`base-classification-policy`).
- `/*/*/TestLaneCategoryGuardTests/every_test_class_is_tagged_unit_xor_integration` (`base-lane-category`).
- `/*/*/ScopedVerificationInstructionTests/C487_G142` (`base-stage-order`).

The first three name unclassified HerdrPaneDisposalEndpointTests; the fourth expects obsolete
Code-to-Mutation ordering. `baseline-comparison.json` records four exact error matches. The Unit
skip remains `AgentTuiSecretProtectorTests.Restored_key_file_symlink_is_rejected_without_mutating_target`.

Duration tripwire unlisted rows at least five seconds: Unit 15, queue 1, interpreter 1,
specialist 1, native 2, sweeps 0, Pty 0. These are unchanged tests/classes; the changed wedged-head
class retains its existing Slow metadata/registry. The queue outlier is
`AgentTaskReuseEnqueueTests.A_grok_reuse_enqueue_queues_only_the_brief` at 27.489s.
No timeout or assertion was loosened, no retry added and no timing baseline equivalence claimed.

## Pending Mutation and remaining coverage bounds

**No deliberate mutant ran in this pass.** Post-land SourceLanding Mutation owns all
red/restore/fresh-build/green cycles and missing-control discovery. Pending inventory:

- **PC-1:** pure replayed marker; queue Pending=false / interrupted Sent=true variants.
- **PC-2:** previous-generation Pending retype, exact body replayed in history.
- **PC-3:** previous-generation interrupted Sent revert/retype, exact body replayed in history.
- **PC-4:** failure charging and third-failure park/unblock; include the inherited multi-row and
  charge/handler crash-boundary coverage when discovering missing controls.
- **PC-5:** captured-generation kill and Enter-only incident wording.
- **PC-6:** untyped terminal Failed/Canceled/Succeeded variants and the orphan/live-row mixture.
- **PC-7:** same-generation terminal-task composer preserved for recovery.
- **PC-8:** Enqueue/SendNow/persisted Immediate stamps; named mutation must fail SendNow.
- **Inherited F1/F3 control families:** remove held-composer hold (parked head); remove interrupted
  cap (Flush/Sweep, repeated sweeps and mixed batch), with below-cap/late-confirm/relaunch contrasts.
- **R2-PC-1:** lone capped crashed Sent discovery/parking.
- **R2-PC-2:** all six direct/sweep shapes; CappedCrashedSent must fail under the narrowed selector.
- **R2-PC-3:** abandoned interpretation exclusion and degraded-note persistence-failure methods.
- **R2-PC-4:** missing reuse brief between commits, incident present/absent (true/false).
- **R3-PC-1:** remove Pending discovery cap; direct post-parking candidate assertion, false/true.
- **R3-PC-2:** remove NoSubmitOutput recovery disjunct; fresh complete-prompt case, false/true.
- **R3-PC-3:** bypass Pending age/recovery condition; fresh ordinary Pending exclusion, false/true.

R3 controls and exact method filters are in the plan. Historical local red/green statements in
earlier sections do not discharge the commissioned post-land obligation. Controls remain
method-scoped; no deliberate mutation is needed before ordinary Review.

Remaining bounds are unchanged: real interpreter reasoning is replaced by direct task settlement;
both missing generation and missing typing timestamp, an exception escaping Enter-only confirmation,
collapsed-paste head absence, and capped crashed Sent on a dead session are not newly covered.
Multi-row charging and the charge/handler crash boundary are covered by prior FollowUps, superseding
the original report's gap for those shapes. F1-R3/F2-R3 leave no known unresolved finding in this scope.

## Cleanup and next stage

All owned builds, tests, model checks and cleanup commands completed. Removed **16** producer-owned
outputs from the original worktree and **15** from the temporary base tree after validating exact
paths, reparse absence and no executable process in those outputs. Removed the clean temporary
base worktree. Inventories, cleanup results and output hashes remain in the external evidence root.
Older output directories from prior tasks, including the original 31 under the caller's waiver,
were not owned by this pass and were left intact. No automatic approval rejection occurred.

Next: **ordinary read-only Review**. Original landing owner remains
`326c349a-dd36-4adb-8ff6-fe533d91a73a`; restart after authorized publication: **server**.
The caller records the companion verification obligation, lands the original Code task after
Review and explicitly commissions SourceLanding Mutation. No land, deploy or restart occurred here.
