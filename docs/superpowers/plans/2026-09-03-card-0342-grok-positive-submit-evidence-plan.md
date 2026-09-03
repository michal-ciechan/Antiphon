# CARD-0342 — Grok queue deliveries need positive post-Enter evidence

**Date:** 2026-09-03

**Plan task:** 284ca95c (Frontier, plan only)

**Card:** CARD-0342 — Grok delegates need the same positive-submit-evidence fix Codex got.

**Related:** CARD-0133 S1/S1b, CARD-0299, CARD-0340 S3, and CARD-0055.

## Decision

Fix this in SessionMessageQueueService, not in ChannelReplyDispatcher or
RunnerGrokAdapter. A Grok delegate brief is queued by
AgentTaskDispatcher.DispatchOneAsync and delivered by DeliverNextLockedAsync after launch
calls FlushSessionAsync. That path stamps its queue row Sent before the first byte, the
intentional crash-safe direction, and its 30-second WaitForTranscriptConfirmAsync decides
whether that stamp remains final.

On the unobservable branch, Grok currently turns any post-Enter output-sequence advance into
sawPositiveSubmit. At the deadline it returns degraded screen-confirmed Sent even when the
brief remains in the composer. Change that branch so a final Grok Sent is allowed only after
positive evidence: the body head was visible in the settled pre-Enter screen, has been absent
for PostEvidenceSettleMs of consecutive rendered-screen snapshots, and is absent in the
deadline snapshot. A matching UserPrompt remains the only transcript-confirmed result.

Sequence advance, Starting session, MCP (0/2), quiet, and a raw OSC title are not submit
evidence. There is deliberately no new Grok Working detector. The successful retry did move
from an idle grok title into responding titles while failed session 4d8712fa retained the
brief and had only two idle grok titles, but raw output is historical and no title contract is
currently measured for queue confirmation. The sustained composer departure already used by
Codex is the safe provider-neutral screen signal.

If the deadline still shows the body, return NoSubmitOutput, do not record
DeliveryUnverified, and revert the row to Pending through the existing failure path. The
pre-type Sent stamp may exist during confirmation, but a final screen-only Sent is no longer
possible for this shape. The existing late transcript check remains before any body retype.
No Grok boot-wedge kill/relaunch is added: this incident does not measure enough to justify a
destructive Grok policy.

## Actual decision point

The live and code evidence rule out the alternative owners:

| Candidate | Verdict |
|---|---|
| AgentTaskDispatcher | Queues the delegate brief as Delegation, but does not type or confirm it. |
| SessionMessageQueueService | Owns the pre-type Sent stamp, Enter writes, 30-second confirmation loop, failure revert, and stranded retry. This is the fix point. |
| ChannelReplyDispatcher | Reads an already-Sent row later to correlate a completed channel turn. It never types or assigns Sent. |
| RunnerGrokAdapter.SendPromptAsync | Named/card-launch path only; it uses VerifiedPromptSubmitter's short output-mark test and creates no queue row. A delegate brief never reaches it. |
| RunnerGrokAdapter.WaitForReadyAsync | Ready, sign-in, and trust gate only; not a submit verdict. |

Session 4d8712fa reached the degraded final verdict roughly 31 seconds before the AppHost
restart. Its brief was Sent with attempt 1 and a null transcript baseline. The restart is
therefore not the cause. Session 2e5c3a22, the bare retry, shows the same startup and MCP
display but a real responding turn. Startup redraw does not discriminate delivery.

## Shared recovery with CARD-0340 S3

Use CARD-0340 S3's one durable attempted-delivery recovery path; do not create a Grok-only
second Sent sweep. The cases are related but not identical:

| Case | Bad state | Owner |
|---|---|---|
| CARD-0342 | The process completes confirmation but falsely treats a visible Grok body as delivered. | The kind-aware queue verdict. |
| CARD-0340 S3 | A hard AppHost death happens after the pre-type Sent stamp and before the process records an outcome. | A persisted attempted-delivery verdict and the stranded sweep. |

CARD-0340 S3's proposed DeliveryVerdict and DeliveryVerdictAt fields are the correct shared
seam. Its recovery order is also the correct CARD-0055 anti-splice rule:

1. Look for the exact transcript match using the stored sequence or wall-clock floor.
2. If the composed body head is still visible on a live idle screen, send Enter only and run the
   existing confirmation loop. Never retype the body.
3. Only when the body is absent may the ordinary Pending path type a fresh body.

Land that S3 foundation before this slice, or implement both in one queue PR. Promote the
private verdict enum to Domain/Enums, clear it when a fresh attempt is stamped, and persist every
known result before a revert or park. The sweep then selects both Sent rows with null verdicts
that are old enough to be interrupted attempts and Pending rows whose latest verdict is
NoSubmitOutput. The latter is the known Grok body-still-visible case. Keep the existing live,
Running, idle, origin, and bounded-window gates; extend FlushStrandedQueuesAsync rather than
adding a hosted sweep.

Group a batch by its shared LastDeliveryStartedAt before testing its body. Reconstruct an
under-ceiling batch through ChannelPromptFormat.FormatBatch; a spilled batch already stores its
common pointer. The helper must resume the whole run, not press Enter for one row and retype the
rest. A missing fresh rendered snapshot is not proof that the composer is empty.

This keeps the normal verdict and the crash verdict separate while using exactly one recovery
mechanism. CARD-0340 retains ownership of interrupted-process rationale; CARD-0342 owns Grok's
positive-evidence predicate. The DeliveryVerdict migration must be generated with dotnet ef, not
hand-authored.

## S1 — durable attempt verdict and shared composer-resume helper

**Files**

- server/Domain/Enums/DeliveryVerdict.cs (new) and
  server/Domain/Entities/SessionQueuedMessage.cs
- generated EF migration and AppDbContextModelSnapshot
- server/Application/Services/SessionMessageQueueService.cs
- server/Application/Settings/SupervisionSettings.cs
- tests/Antiphon.Tests/Application/SessionMessageQueueInterruptedAttemptTests.cs (new)

1. Replace the private queue verdict enum with a domain enum. Add nullable DeliveryVerdict and
   DeliveryVerdictAt to SessionQueuedMessages. A new persisted body attempt clears them alongside
   the Sent stamp. Every normal result, late confirmation, and known failure persists an outcome
   before a status transition. Thus a hard death remains Sent plus null verdict; an observed
   NoSubmitOutput remains identifiable after reverting to Pending.
2. Add CARD-0340 S3's InterruptedAttemptWindowMinutes setting, default 60, and candidate query
   to the existing stranded sweep. Include Pending NoSubmitOutput runs, but leave older rows and
   unrelated pending verdicts on current behaviour.
3. Extract one lock-held transcript-first and visible-body Enter-only helper. It takes a whole
   delivery run, captures a fresh settled output mark before an Enter-only retry, keeps the
   original transcript identity floor, honours Herdr blocked/unreachable, and publishes a queue
   change for Delivered or LateConfirmed results. It never claims a body absent when a snapshot
   cannot be read.
4. Generate the migration through the EF CLI and commit its snapshot.

**Tests**

- Delivered, degraded screen, LateConfirmed, and NoSubmitOutput outcomes persist the right
  verdict/time; a fresh typed attempt clears stale result state.
- An old Sent/null-verdict body visible on screen sends only Enter and can confirm without a body
  write. If the body is absent it returns to ordinary Pending delivery; outside-window rows do
  not change.
- A Pending Grok NoSubmitOutput reaches the same helper: a late UserPrompt wins before input; a
  visible body gets Enter only; working, stopped, blocked, or snapshot-unavailable sessions get
  no input.
- A multi-row batch, including its spilled-pointer form, is recovered as one composed body.

## S2 — Grok's queue verdict requires a settled composer departure

**Files**

- src/Antiphon.Agents.Pty/SubmitEvidence.cs
- server/Application/Services/SessionMessageQueueService.cs
- server/Application/Services/ProviderContractCatalog.cs
- docs/agent-kinds.md and docs/session-runtime-invariants.md
- tests/Antiphon.Agents.Pty.Tests/SubmitEvidenceTests.cs
- tests/Antiphon.Tests/Application/SessionMessageQueueDeliveryVerificationTests.cs
- tests/Antiphon.Tests/Application/SessionMessageQueueGrokPtyIntegrationTests.cs

1. Express the provider facts separately: Codex has its measured immediate Working indication
   or sustained composer departure; Grok has only the latter measured predicate. Do not add a
   title, MCP, quiet, or sequence-advance rule.
2. In WaitForTranscriptConfirmAsync, run CARD-0299's PostEvidenceSettleMs latch and re-latch
   logic for Grok as well as Codex. The head must have been visible before Enter and absent on
   consecutive later snapshots; any reappearance clears the latch. Sequence advance stays a
   diagnostic but cannot make sawPositiveSubmit true for Grok.
3. At the unobservable deadline take a fresh rendered-screen look for Codex and Grok. If the
   Grok body head is present, return NoSubmitOutput regardless of earlier redraws or a transient
   empty frame. The shared S1 recovery owns its retry. If the head remains absent, preserve the
   explicitly degraded Screen result and DeliveryUnverified warning. Transcript confirmation
   still wins immediately, and the loop's existing retries remain Enter-only.
4. Update Grok's DeliveryVerification catalog text and its two owner documents: redraw and
   startup are not proof; transcript or sustained composer departure is. The catalog remains the
   capability source of truth.

**Tests**

- Grok unobservable trailing/startup redraw: body remains visible and no transcript arrives, so
  three Enters produce NoSubmitOutput, a Pending row with the known verdict, and no final Sent or
  DeliveryUnverified incident.
- Grok transient empty frame: one empty/ghost snapshot followed by the body has the same result
  and does not suppress additional Enter presses.
- Grok sustained composer departure: body stays absent for the settle period with no transcript,
  so it remains a degraded Screen result; a matching UserPrompt still wins as transcript proof.
- Claude's advance-based fallback and all current Codex Working, transient-empty, and deadline
  body-visible pins stay unchanged.
- Extend FakeGrok only as needed to swallow Enter while emitting a redraw and retaining the
  composer. The Grok PTY integration test must see Pending rather than final Sent, followed by an
  Enter-only shared recovery with no duplicate body write.

## Ordering, exclusions, and verification

Land S1 before S2, or as one atomic queue PR. S2 must not rely on a second Grok-specific sweep.

Out of scope: RunnerGrokAdapter.SendPromptAsync named/card-launch behaviour; a Grok responding
title detector; Grok ready-gate changes; timeout widening; a blind Grok kill/relaunch; and
changing the documented TranscriptConfirmEnabled emergency switch.

Run these focused suites sequentially with isolated output, then delete generated bin-card0342
directories:

    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0342/ -- --treenode-filter "/*/*/SessionMessageQueueInterruptedAttemptTests/*"
    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0342/ -- --treenode-filter "/*/*/SessionMessageQueueDeliveryVerificationTests/*"
    dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0342/ -- --treenode-filter "/*/*/SessionMessageQueueGrokPtyIntegrationTests/*"
    dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-card0342/ -- --treenode-filter "/*/*/SubmitEvidenceTests/*"

After deployment, a cheap fresh-worktree Grok delegate must end Sent with a matching UserPrompt.
A forced swallowed-Enter fake/canary run must end Pending plus NoSubmitOutput, never final
screen-only Sent, and log Enter-only recovery while the body is still rendered.

## Invariants

- A final Sent is backed by transcript proof, a sustained body departure, or Codex's measured
  Working indicator, never merely a redraw.
- A current body at the deadline defeats a transient earlier empty snapshot.
- A late transcript check happens before retyping. A currently visible body gets Enter only.
- Sent plus null DeliveryVerdict is an unknown interrupted attempt, not proof of delivery.
- This incident establishes no readiness, title, timeout, or destructive-recovery contract.
