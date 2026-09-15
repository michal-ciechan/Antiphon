# CARD-0501 Code: implementation and ordinary verification

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

Every handoff a produced check note crosses before it is a prompt in the recipient's transcript,
and what now owns each one. "Owner" is the ordinary test that fails if the handoff breaks.

| # | Handoff | Production site | What is lost if it breaks | Ordinary owner |
|---|---|---|---|---|
| H-1 | facts to note body | `AgentTaskCheckService.RunCheckAsync` then `BuildNote` | a note that cannot be traced to the task it is about | `CheckNoteDeliveryHandoffTests` (header short id); `AgentTaskCheckInterpreterTests` owns the body's content |
| H-2 | note to queue row | `_queue.EnqueueAsync(parentSession, …, Check, ConversationKey(taskId), sourceTaskId)` | correlation: nothing can tie the row back to the checked task | `CheckNoteDeliveryHandoffTests` (`ConversationKey`, `SourceTaskId` on the persisted row) |
| H-3 | queue row to first typing | `DeliverNextLockedAsync` (busy defers, idle types) | a busy recipient typed into mid-turn | `A_check_note_reaches_a_busy_recipient_as_one_whole_prompt_after_recovery` (attempts 0, no inputs while working) |
| H-4 | typed body to submit | `DeliverAsync` and the confirm loop | a swallowed Enter silently reported as delivered | both handoff tests (attempts 1, Pending, zero prompts) |
| H-5 | standing body to Enter-only recovery | retry branch then `EnterOnlyConfirmLockedAsync` | a duplicate retype, or an Enter into a dead composer | `SessionMessageQueueWedgedHeadTests` S2 gate methods; `A_check_note_reaches_an_already_eligible_recipient_as_one_whole_prompt` asserts no retype |
| H-6 | Enter-only failure to attempt charge | `EnterOnlyConfirmLockedAsync` charge | an uncharged cycle repeating forever (the CARD-0501 loop) | `Failed_Enter_only_recovery_charges_an_attempt`, `Failed_Enter_only_recovery_charges_every_row_of_the_batch` |
| H-7 | charge save to failure handler | save, then `HandleDeliveryFailureAsync` in its own scope | a crash losing the charge and unbounding the cycle | `A_crash_between_the_charge_and_the_failure_handler_keeps_the_attempt_charged` |
| H-8 | parked head to the next row | `deliverable` filter plus the F1 composer hold | the parked body riding into the next row's prompt (F1) | `Parked_head_left_in_the_composer_holds_the_next_message` plus its two release arms |
| H-9 | submit to recipient `UserPrompt` | transcript ingestion then verification | a row marked `Delivered` for a prompt the agent never received whole | both handoff tests (`prompts.ShouldBe([row.Body])`, exact) |
| H-10 | terminal task to brief cancellation | `CancelDeadBriefsAsync` | orphans accumulating behind the head | `SessionMessageQueueWedgedHeadTests` S4 methods |

Still not owned by an ordinary test, and deliberately so: the interpreter's own wait/degrade
arms (H-1's body, owned by `AgentTaskCheckInterpreterTests`), a legacy row with BOTH the
generation and the typing timestamp absent, an exception escaping the Enter-only confirmation
path, and a collapsed `[Pasted text #N]` body that shows no head at all (pre-existing, named
out of scope by the plan). Backend-unreachable still defers without charging.

## FollowUp coverage IDs

| ID | Coverage | Class / check |
|---|---|---|
| V-7 | F1 hold, both release arms, previous-generation no-regression twin | `SessionMessageQueueWedgedHeadTests` (`Parked_head_left_in_the_composer_holds_the_next_message`, `Cleared_composer_releases_the_next_message_with_its_exact_body`, `Parked_head_from_a_previous_generation_does_not_hold_the_next_message`, strengthened `Third_failed_Enter_only_recovery_parks_and_unblocks_the_queue`) |
| V-8 | F2 producer-to-recipient handoff, busy and already-eligible | `CheckNoteDeliveryHandoffTests` |
| V-9 | F2 charge/handler crash boundary and multi-row batch charging | `SessionMessageQueueWedgedHeadTests` (`A_crash_between_the_charge_and_the_failure_handler_keeps_the_attempt_charged`, `Failed_Enter_only_recovery_charges_every_row_of_the_batch`) |
