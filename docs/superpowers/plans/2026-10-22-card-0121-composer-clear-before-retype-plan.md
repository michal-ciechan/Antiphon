# CARD-0121: prove the collapsed-paste composer is clear before any re-type

Date: 2026-10-22. Stage: Plan (task `147f83c7`), branch `feat/card-task-147f83c7` from
`4b4138fc5101513df89e7908f52f2537b6940320`. Verification design is included below, so the next
stage is Code. Card: CARD-0121 (`8757828c`), CARD-0103 S3. Evidence base: the CARD-0103 card
(live reproduction 2026-08-20, three runs, ANSI-log replay) and its plan
[2026-08-20-card-0103-boot-prompt-readiness-plan.md](2026-08-20-card-0103-boot-prompt-readiness-plan.md)
§4; nothing below re-derives those measurements.

## Outcome and scope

A queue redelivery of a message typed once into a Claude composer may type the same body again
while the first copy is still standing there as a collapsed `[Pasted text #N +M lines]`
placeholder. The CARD-0103 repro showed the end state: after three attempts the composer held
the brief twice (expanded once, collapsed once), one Enter from a duplicated prompt. CARD-0501
closed the same hole for a body whose whole head is on screen (Enter-only instead of re-type, and
the F1 hold for the next row) and scoped out the collapsed form explicitly, because a placeholder
shows none of the body.

This plan makes a re-type require proof, in this order, at the one place a queue row is re-typed:

1. the transcript says the body landed (late-confirm, exists);
2. the body is standing on screen, whole (head, or now also tail) → Enter only (exists, widened);
3. a paste placeholder appeared on screen after this row's typing began → the composer holds a
   collapsed paste in this generation → Enter only (new; needs one durable int per attempt);
4. the TUI answers an input probe and still shows none of the above → the ConPTY buffer is
   drained and the body is not in the composer → type (new; `ComposerInputProbe`, Claude only);
5. otherwise hold: no type, no attempt charged, no park, no kill, one Warning incident per row
   (new).

The canary the card requires is slice 1: a headed, real-Claude arm that measures what Ctrl+U does
to a standing collapsed placeholder, and what the exact production sequence of step 4 leaves
behind when a placeholder stands. The production policy is correct under every canary outcome
(§D-1 table); the canary decides which branch production takes and what the fakes model.

In scope: `ComposerDeliveryEvidence` gains a whole-tail arm and a public max-placeholder-index
reader; `SessionQueuedMessage.LastDeliveryPasteIndexBefore` with an EF migration; the retry
branch of `DeliverNextLockedAsync`; the F1 hold and the interrupted-Sent Enter-only gate use the
widened standing predicate; one setting; the two fakes; the canary; the invariants doc.

Out of scope, deliberately: `AgentSessionService.SendBootPromptWithRetryAsync` (CARD-0056's
re-type of `/remote-control`, `/rename` and the card work prompt has the same exposure through a
different seam, `IAgentProtocolAdapter.SendPromptAsync`; file a follow-up card at Code time and
leave the CARD-0108 S1 `ComposerMayHoldBody` skip as it is); Grok and Codex retries (their clear
keystroke is unmeasured; follow-up); `EvidenceTimeoutSeconds`, `MaxDeliveryAttempts` and the
CARD-0103 S2 refund (unchanged); the dispatcher's 10-minute never-started watchdog (unchanged,
and it is the only clock on a held brief); a queue-DTO `held` flag (the incident and the log
carry the hold; no client change).

## Ground truth

Verified against `4b4138fc5` on 2026-10-22. Line numbers are from that commit.

| Claim (card, brief, or a tempting shortcut) | Evidence | Consequence |
|---|---|---|
| The retry path re-types the whole body with no look at the composer. | Not quite any more. `SessionMessageQueueService.cs:2573-2590`: when any row in the run has `DeliveryAttempts > 0` in the current generation, a snapshot is read; unavailable → defer; `HeadFragmentIsVisibleWhole` → `EnterOnlyConfirmLockedAsync`; otherwise it falls through to the stamp at `:2609-2619` and `DeliverAsync` types the whole body. | The hole is exactly the fall-through: a collapsed placeholder never satisfies the whole-head test. D-1 inserts steps 3-5 between `:2588` and the stamp. |
| CARD-0501 covers the collapsed placeholder. | Its plan (`2026-09-14-card-0501-…-plan.md`, "Out of scope") names "the pre-existing gap that a collapsed `[Pasted text #N]` body shows no head and therefore never takes the Enter-only arm". | This card is that gap. Nothing in CARD-0501 is undone. |
| Late-confirm runs before every re-type. | `:2309-2318` (`LateConfirmAttemptedMessagesAsync`) precedes the retry branch; it is text-match only, with the CARD-0164 wall-clock arm for null baselines (`:2908-2925`). | Step 1 exists. A pre-first-turn session (the CARD-0103 shape) has no transcript rows, so late-confirm cannot save it; steps 2-5 must. |
| Absence of evidence on screen proves the composer is empty. | False, and the card's measured shape: a deaf TUI retains buffered input and renders it on wake (48.8 s in run 1). `NoComposerEvidence` means the Enter was withheld, not that the body is absent. | A re-type must be gated on a positive signal that the buffer drained: the probe (step 4) or the body's own rendering (steps 2-3). |
| `ComposerDeliveryEvidence.IsVisible` can tell a new placeholder from an old one. | `ComposerDeliveryEvidence.cs:62-99`: the placeholder-index arm compares `PlaceholderIndices(screenAfter)` against the before-screen; `PlaceholderIndices` (`:200-224`, private) parses `asted text #N`. The CARD-0103 ANSI replay returned True for all three attempts. | The index is the identity. The retry has no before-screen, so D-2 records the highest index seen before typing on the row and exposes a public `MaxPlaceholderIndex(screen)`. |
| A placeholder index is monotonic per session. | CARD-0103 run 3: paste 1 → `#1`, paste 2 expanded `#1`, paste 3 → `#2`; `PastePlaceholderModel.cs:59-61` models "N increments per paste and is never reused". After a submit the placeholder stays in scrollback with the same index. | An index greater than the recorded one was minted after our typing began, in this generation, under the per-session lock. A scrollback placeholder from an earlier submit has index ≤ recorded and cannot earn Enter-only. |
| The whole head is on screen whenever a body stands expanded. | Not guaranteed: `ComposerDeliveryEvidence.cs:233-237` records that the settled composer for a huge single line shows only the final wrapped fragment, and run 2 of the repro expanded a 36-line body into a 30-row screen. The replay passed "via head/tail fragments" (windowed). | D-3 widens the standing predicate to whole-head OR whole-tail. Both are task-specific for briefs (the `[antiphon-task:…]` marker opens and closes a brief). |
| `ComposerInputProbe` belongs to launch readiness only. | `ComposerInputProbe.cs`: delegate-based (snapshot fn + write fn), used by `RunnerClaudeAdapter.WaitForReadyAsync` (`RunnerClaudeAdapter.cs:185-196`). The queue already holds the same two primitives: `_runtime.TryGetLiveSnapshotAsync` (`AgentSessionRuntime.cs:2036`) and `_runtime.SendInputAsync` (`:2127`). | Step 4 reuses the helper unchanged. Its Ctrl+U contract is measured for a single typed line (`ClaudeInputProbeCanaryTests`, 2/2 against real Claude 2026-08-20). |
| Ctrl+U on a collapsed placeholder is known. | Unmeasured. The canary file says so (`ClaudeInputProbeCanaryTests.cs:118-123`); fakeclaude models kill-line "for a SINGLE typed line only" (`Program.cs:65-70`, `:381-391`); `FakeAgentProtocolAdapter.SendInputAsync` (`:418-500`) does not handle `\x15` at all and would append it to the composer. | S1 measures it and both fakes learn the measured shape. D-1 is correct whichever way it falls. |
| The in-process fake can show a collapsed paste. | `FakeAgentProtocolAdapter.cs:560-583`: screen = frames/override/raw output plus `"\n> " + composer` (or the CARD-0650 Claude chrome); typed input is echoed verbatim after stripping paste markers (`:489-493`). No placeholder model. | S1 adds `CollapsePastes` (monotonic index, `+M lines`), Ctrl+U, and `MaxPasteIndexInScrollback` (a stale scrollback placeholder) to the fake; every witness in §Verification design is expressible without a pty. |
| The fakeclaude pty integration tests run on server2. | `SessionMessageQueuePtyIntegrationTests.cs:57,163`: `SkipTestException("ConPTY only on Windows")`; `ClSession.SkipIfNotEligible` (`ClSession.cs:11-16`): Windows, `ANTIPHON_HEADED_TESTS=1`, `claude` on PATH. | The Code stage runs on the Windows lane (`-Platform windows`, runner unpinned); everything else in the table also runs there. §Lane. |
| The attempt stamp happens before typing and survives the revert. | `:2609-2619` stamps Sent, `DeliveryAttempts++`, `LastDeliveryStartedAt`, `LastDeliveryGeneration`, `LastDeliveryBaselineSequence` before `DeliverAsync`; `HandleDeliveryFailureAsync` (`:4820-`) reverts status and refunds only the CARD-0103 and CARD-0561 shapes. `SendNowAsync` stamps the same set at `:1090-1095`. | `LastDeliveryPasteIndexBefore` is stamped in both places from one snapshot read taken just before the stamp, and cleared where `LastDeliveryGeneration` is cleared (`:2720-2726`, `:311-314`). |
| The F1 hold and the Enter-only gates share one predicate. | `HeldBackTypingBlocksTheComposerAsync` (`:2049-2088`) and both Enter-only sites (`:2588`, `:3209`) call `HeadFragmentIsVisibleWhole`; the comment at `:2042-2046` says the two must agree. | D-3's `BodyStandsWhole` and D-2's placeholder test replace it at all three sites, so the hold and the recovery still agree. |
| A held session is a session waiting for input. | The retry branch is reached only when the session is idle and queued delivery is admitted; the row stays Pending with attempts kept; the session keeps its runner seat (CARD-1083, parking disabled). | §CARD-1083 checklist. No new waiting state is introduced; the hold is a queue defer like F1. |
| Channel debt orders queue delivery. | No. `SessionRuntimeAction` `channel` debt is per destination (CARD-1434 D-1) and next-message delivery does not wait for it (`OnTurnEndCoreAsync :1621-1623`; `C1435_owed_channel_reply_does_not_hold_next_message_delivery`); the queue's flush returns `FlushResult.Nothing` on a hold exactly as F1 does. | A held Channel-origin row delays only its own reply. The hold's incident stays Warning; the park-at-cap Critical escalation for channel-bound agents is unchanged and unreachable from a hold (no attempt is charged). |
| The probe can run inside a flush. | `DeliverAsync` already waits up to `EvidenceTimeoutSeconds` 15 s plus `TranscriptConfirmTimeoutSeconds` 30 s under the same per-session lock; the stranded sweep ticks every 60 s (`StrandedAgeSeconds`). | D-4 bounds the retry probe at 30 s (one write), half a sweep. A deaf TUI costs 30 s per sweep and no attempt. |

## Decisions

### D-1 Proof-or-hold at the retry branch

In `DeliverNextLockedAsync`, inside the existing `DeliveryAttempts > 0` same-generation branch
(`:2573-2590`), after the whole-body test and before the attempt stamp:

| Observation (same generation, late-confirm found nothing) | Action | Charges |
|---|---|---|
| snapshot unavailable | defer (exists) | none |
| `BodyStandsWhole(screen, body)` | `EnterOnlyConfirmLockedAsync` (exists) | on failure, as CARD-0501 |
| `MaxPlaceholderIndex(screen) > head.LastDeliveryPasteIndexBefore` (recorded non-null) | `EnterOnlyConfirmLockedAsync` | on failure, as CARD-0501 |
| recorded index null and any placeholder on screen | hold | none |
| kind is `ClaudeCode`: probe → `Responsive`, then re-snapshot and re-apply the two rows above | Enter only if standing, else **type** (the existing path from the stamp down) | the typed attempt, as today |
| probe → `NeverAppeared`, `NeverCleared`, `InterruptedByModal` | hold | none |
| kind is not `ClaudeCode` | type (today's behaviour) | as today |

The run is evaluated on its head row (`run[0]`), as the Enter-only gate already is; a batched
Channel run shares one composed body and one typing, so one index covers it.

Why Enter-only for a standing placeholder rather than a clear: it is the existing, tested recovery
for "the body is in the composer", it submits exactly one copy, and
`WaitForTranscriptConfirmAsync` confirms by full text, so a placeholder that turns out to be
something else fails the confirm, charges the attempt and parks at the cap, with the F1 hold
protecting the next row. Rejected: Ctrl+U-then-type (depends on the unmeasured clear and throws
away a copy that was fine).

Why the probe decides the "nothing on screen" case: a Responsive probe proves the ConPTY buffer
is drained up to the token (input is FIFO) and that the TUI renders what it reads, so a body that
had been buffered would now be on screen as a whole head/tail or a new placeholder. Nothing on
screen after a Responsive probe therefore means the body is not in the composer. Rejected:
hold-only without the probe (a genuinely lost paste, for example one an overlay ate before the
CARD-0137 S5 recovery, would hold until the 10-minute watchdog instead of delivering on the next
sweep; the probe restores that liveness for one token write).

Correct under every canary outcome, with a placeholder standing and the probe token typed after it:

| Ctrl+U leaves | Probe result | Re-look sees | Action |
|---|---|---|---|
| placeholder, token gone | Responsive | new index | Enter only, one copy |
| nothing (both cleared) | Responsive | nothing | type, one copy |
| both (token stays) | NeverCleared | n/a | hold |
| the deaf case: token never renders | NeverAppeared | n/a | hold; the next sweep looks again |

### D-2 `LastDeliveryPasteIndexBefore` on the row

`SessionQueuedMessage.LastDeliveryPasteIndexBefore` (`int?`): the highest `[Pasted text #N]`
index on the rendered screen immediately before the attempt typed; `0` when the screen showed
none; null when no snapshot was readable or for rows typed before the migration. Stamped at
`:2609-2619` and `:1090-1095` from one `TryGetLiveSnapshotAsync` read taken just before the stamp
(the retry branch already has `retrySnap`; a first attempt adds one read); cleared with
`LastDeliveryGeneration` in the `BackendUnreachable` refund and the SendNow reset. Migration
`AddQueuedMessageDeliveryPasteIndex`, generated with `dotnet ef`, never hand-authored.
`ComposerDeliveryEvidence.MaxPlaceholderIndex(string screen)` exposes the existing private parser.

Rejected: in-memory per-session state (a server restart inside the retry window turns a
decidable case into a hold, and the queue DTO could not show why); storing the before-screen
(kilobytes per attempt for one integer's worth of information); comparing against the index at
ready time (not recorded anywhere).

### D-3 `BodyStandsWhole`: whole head or whole tail

`ComposerDeliveryEvidence.BodyStandsWhole(screen, body)` = `HeadFragmentIsVisibleWhole` OR the
same contains test on the normalized tail (`FragmentSpan` 40). It replaces
`HeadFragmentIsVisibleWhole` at the three CARD-0501 sites (`:2075`, `:2588`, `:3209`), so the
F1 hold and both Enter-only gates keep agreeing. `HeadFragmentIsVisibleWhole` stays public for
`SubmitEvidence` callers. Rejected: head only (misses an expanded body whose head scrolled out of
the composer box, after which step 4 would type on top of it); windowed matching (CARD-0501's
replayed-history false positive).

### D-4 The retry probe and its budget

`DeliveryVerificationSettings.RetypeProbeTimeoutSeconds` (default 30; 0 disables step 4, in
which case the "nothing on screen" row holds). The probe runs with `PollIntervalMs`, a re-type
interval equal to the budget (one write), `ClearTimeout` 10 s, `ClearAttempts` 3, token
`ComposerInputProbe.TokenFor(sessionId)`, snapshot = `_runtime.TryGetLiveSnapshotAsync` (an
unavailable snapshot inside the probe counts as empty screen, which ends as `NeverAppeared` and a
hold), write = `_runtime.SendInputAsync`, `isBlocked` = the provider overlay/modal detectors
`DeliverAsync` already uses. Rejected: the launch budget of 90 s (a launch has one chance; a
retry has a sweep every 60 s and pays no attempt for a miss).

### D-5 Claude only

Step 4 runs for `AgentKind.ClaudeCode`. Grok and Codex keep today's fall-through re-type; Grok is
the obvious next adopter once its clear keystroke is measured (follow-up card, filed at Code
time). Steps 2-3 and the hold apply to every verified-delivery kind, since they type nothing.

### D-6 The hold

A hold returns `FlushResult.Nothing` from the retry branch: status stays Pending, attempts and
stamps untouched, no park, no kill, no attempt charged. It records one `DeliveryVerificationFailed`
incident at Warning severity on the owning agent, deduped per message id (the same supervisor
path the CARD-0103 refund warning uses), with detail `composer-hold: <reason>` naming the probe
outcome or the null-index case and the three releases. A later sweep re-evaluates from step 1.

Releases: a late-confirm; a new session generation (operator or supervisor restart, or the
dispatcher's never-started watchdog killing the session at `DeliveryFailTimeoutMinutes` 10, after
which `CancelDeadBriefsAsync` cancels the typed brief of the terminal task); a probe that answers
Responsive on a later sweep; a human submitting or clearing the composer in the pane. The hold
itself sets no deadline. Rejected: charging an attempt per hold (a deaf TUI would park a healthy
session in three sweeps, the CARD-0103 S2 defect in a new coat); a new `AgentIncidentKind`
(client mapping for one timeline row); Critical for channel-bound agents (the hold is pre-park;
the Critical escalation stays with the park).

### D-7 The canary and the fakes

`ClaudeInputProbeCanaryTests` gains `Ctrl_u_on_a_collapsed_paste_placeholder_is_measured_and_the_composer_is_shown_empty_or_not`
(headed, `[Explicit]`, `[Category("Card0121")]`, Windows, never sends CR). It bracket-pastes a
40-line body, requires a `[Pasted text #` placeholder (if the paste does not collapse at that
size it prints `CANARY: not-collapsed` and skips the Ctrl+U claims rather than inventing them),
sends one Ctrl+U, snapshots, then runs the production step-4 sequence against a fresh placeholder
(placeholder standing → `ComposerInputProbe.RunAsync` → snapshot) and prints one line per arm:
`CANARY: ctrl-u-on-placeholder=<cleared|standing>`, `CANARY: probe-after-placeholder=<outcome>
leaves=<placeholder|nothing|both>`. Emptiness is shown by the screen (no `Pasted text #`, no body
head or tail) and by a fresh token rendering on an otherwise empty composer row; it is never
shown by pressing Enter, which would submit a junk prompt if the composer were not empty.

The first run is the measurement; its printed lines go into the Code run record. Then both fakes
pin the measured shape: fakeclaude's `PastePlaceholderModel` learns what `\x15` does to a
standing placeholder (and keeps "paste N+1 while a placeholder stands appends a new placeholder",
which is enough to reproduce the stacking the policy prevents), and `FakeAgentProtocolAdapter`
gains `CollapsePastes` (monotonic index, `+M lines`), `\x15` handling with the same shape, and
`ScrollbackText` for a stale placeholder. Until the measurement lands the fakes model the
conservative shape (Ctrl+U clears a typed line, leaves a placeholder), which is the hardest case
for the policy; the final assertion in the canary and the fake's model are set to the measured
value in the same commit.

### D-8 Legacy rows

A row typed before the migration (null index) with any placeholder on screen holds; with no
placeholder it proceeds to step 4. The population is transient (rows inside one retry window at
deploy time).

### D-9 Documentation

`docs/session-runtime-invariants.md`: a new bullet after the CARD-0501 F1 bullet (`:817-831`)
stating the proof-or-hold order, the index rule, the probe rule, the hold's releases and the
CARD-1083 note; one sentence in the CARD-0103 paragraph of
`SupervisionSettings.DeliveryVerificationSettings` docs for the new setting. CARD-0056's claim
that a `SendBootPromptWithRetryAsync` re-type "cannot double-submit" (`SupervisionSettings.cs:491`)
gets a cross-reference to the follow-up card, not a fix.

## CARD-1083 checklist

The plan changes a session that waits for input only in that fewer bodies are typed into it. A
held row leaves the session idle with its runner seat (parking is disabled; no park bound
applies). What releases it: a late-confirm, a new generation, a Responsive probe on a later
sweep, or a human in the pane; after how long: no automatic deadline from the hold. The only
clock remains the dispatcher's never-started watchdog (10 minutes, unchanged), which fails the
task and kills the session, after which the brief is canceled on the next flush.

## Slices

### S1 Canary and fakes (D-7)

- `tests/Antiphon.Agents.Pty.Tests/ClaudeInputProbeCanaryTests.cs`: the new arm. Run once on the
  Windows lane with `ANTIPHON_HEADED_TESTS=1`; record the `CANARY:` lines.
- `src/Antiphon.FakeClaude/PastePlaceholderModel.cs`, `Program.cs`: `\x15` against a standing
  placeholder per the measurement; `tests/Antiphon.Agents.Pty.Tests/FakeClaudeContractTests.cs`
  pins it (`A_collapsed_placeholder_answers_ctrl_u_the_way_real_claude_measured`).
- `tests/Antiphon.Tests/Agents/FakeAgentProtocolAdapter.cs`: `CollapsePastes`, `\x15`,
  `ScrollbackText`; `tests/Antiphon.Tests/Agents/FakeAgentProtocolAdapterTests.cs` (new, small)
  pins the three.
- Red witness: `FakeClaudeContractTests.A_collapsed_placeholder_answers_ctrl_u_the_way_real_claude_measured`
  (red before the model change under either measured shape, because today the fake clears the
  composer buffer and erases the row for any `\x15` outside a paste). Narrow green: the existing
  `ClaudeInputProbeCanaryTests` pair is not rerun; `FakeClaudeContractTests/*` stays green.

### S2 Evidence predicates and the durable index (D-2, D-3)

- `src/Antiphon.Agents.Pty/ComposerDeliveryEvidence.cs`: `BodyStandsWhole`, `MaxPlaceholderIndex`.
  `tests/Antiphon.Agents.Pty.Tests/ComposerDeliveryEvidenceTests.cs`: tail-only standing body is
  whole-standing (red witness: `HeadFragmentIsVisibleWhole` is false for it today); a scrollback
  placeholder's index is read; no placeholder reads 0.
- `server/Domain/Entities/SessionQueuedMessage.cs`, migration `AddQueuedMessageDeliveryPasteIndex`,
  `server/Application/Services/SessionMessageQueueService.cs` stamps and clears,
  `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs` `SeedPendingMessageAsync(pasteIndexBefore:)`.
- `tests/Antiphon.Tests/Application/SessionMessageQueueWedgedHeadTests.cs`:
  `C0121_typed_attempt_records_the_paste_index_before_typing` (red witness: the column is null
  after a typed attempt today), `C0121_backend_unreachable_refund_clears_the_paste_index`.
- The three CARD-0501 call sites move to `BodyStandsWhole`; the existing wedged-head class is the
  narrow green.

### S3 Proof-or-hold (D-1, D-4, D-5, D-6, D-8)

- `SessionMessageQueueService.cs`: the retry branch; `RetypeProbeTimeoutSeconds` in
  `server/Application/Settings/SupervisionSettings.cs`; the hold incident.
- `tests/Antiphon.Tests/Application/SessionMessageQueueWedgedHeadTests.cs` (new partial file
  `SessionMessageQueueWedgedHeadTests.C0121.cs` to keep the class filter): V-1..V-6 below.
- `tests/Antiphon.Tests/Application/SessionMessageQueuePtyIntegrationTests.cs`: V-7, the
  end-to-end witness through the real pty and fakeclaude with `ANTIPHON_FAKE_PASTE_PLACEHOLDER=1`
  and `ANTIPHON_FAKE_DEAF_START_MS` armed past the evidence window (settings for the test:
  `EvidenceTimeoutSeconds` 2, `StrandedAgeSeconds` 0 so a flush retries at once).

### S4 Documentation and regression (D-9)

- `docs/session-runtime-invariants.md`, `SupervisionSettings.cs` doc comments; follow-up cards
  filed for `SendBootPromptWithRetryAsync` and the Grok retry probe; the plan's run record.

## Verification design

Witnesses, all through `BridgeQueueHarness` + `FakeAgentProtocolAdapter` unless named otherwise.
"Typed once" seeds `deliveryAttempts: 1`, same generation, `pasteIndexBefore: 0`, and a null
baseline (the pre-first-turn shape), with `CollapsePastes = true`.

- V-1 (S3, red witness) `C0121_standing_collapsed_placeholder_gets_enter_only_not_a_retype`: the
  composer shows `[Pasted text #1 +39 lines]` for the body; flush; `Inputs` gains exactly one
  `"\r"` and no body bytes; `SubmittedBodies == [body]`; one `UserPrompt`; verdict `Delivered`,
  attempts 1. Today: the body is typed again and `SubmittedBodies` holds it twice.
- V-2 (S3, red witness) `C0121_deaf_composer_holds_instead_of_retyping`: `EchoTypedInputToScreen
  = false` (the token never renders); flush; no body bytes written, no `"\r"`, status Pending,
  attempts 1, `Parked` false, `Killed` false, exactly one `DeliveryVerificationFailed` Warning
  incident whose detail starts `composer-hold:`; a second flush adds no second incident. Today:
  the body is typed again.
- V-3 (S3, green twin) `C0121_responsive_probe_with_nothing_standing_types_once`: empty composer,
  echo on; flush; `Inputs` holds the token, `\x15`, then the body and `"\r"`; `SubmittedBodies ==
  [body]`; `Delivered`. Pins that the hold is not forever.
- V-4 (S3, negative control) `C0121_scrollback_placeholder_at_or_below_the_recorded_index_does_not_earn_enter_only`:
  `ScrollbackText = "[Pasted text #1 +12 lines]"`, `pasteIndexBefore: 1`, composer empty; flush;
  the probe runs and the body is typed once; no bare `"\r"` before the body.
- V-5 (S3) `C0121_legacy_row_with_a_placeholder_on_screen_holds`: `pasteIndexBefore: null`,
  scrollback placeholder; flush; nothing typed, one Warning.
- V-6 (S3, red witness for the F1 side) `C0121_parked_collapsed_placeholder_holds_the_next_message`:
  a row at the cap whose placeholder stands (index 1 > recorded 0) and a fresh next row; flush;
  the next row is not typed (the CARD-0501 `Parked_head_left_in_the_composer_holds_the_next_message`
  shape with a placeholder instead of an expanded body). Today: the next body is typed on top.
- V-7 (S3, end to end, Windows) `SessionMessageQueuePtyIntegrationTests.C0121_three_sweeps_against_a_deaf_collapsing_composer_submit_the_body_once`:
  real runtime → runner → ConPTY → fakeclaude with placeholder and deaf-start models; three
  flushes spaced past the evidence window; after wake, exactly one `UserPrompt` containing the body
  once. Today: the fake receives two pastes and the submitted prompt carries the body twice.
- V-8 (S2) `ComposerDeliveryEvidenceTests.A_body_whose_only_visible_fragment_is_its_whole_tail_stands_whole`
  and `Max_placeholder_index_reads_the_highest_index_on_screen`.
- V-9 (S1) `FakeClaudeContractTests.A_collapsed_placeholder_answers_ctrl_u_the_way_real_claude_measured`;
  the canary arm itself (measurement, printed).

Positive controls for the Code stage's own red-then-green (method-scoped filters):

- PC-1 drop the placeholder-index row of D-1 → V-1 red (body typed twice).
- PC-2 ignore the probe outcome (treat every outcome as Responsive) → V-2 red (body typed).
- PC-3 keep `HeadFragmentIsVisibleWhole` at the F1 site → V-6 red.
- PC-4 skip the `LastDeliveryPasteIndexBefore` stamp → `C0121_typed_attempt_records…` red and
  V-4 flips to a hold.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Agents.Pty.Tests -> bin-c121a/` | canary | `/*/*/ClaudeInputProbeCanaryTests/Ctrl_u_on_a_collapsed_paste_placeholder*` | V-9 | 1 executed, 0 failed; `CANARY:` lines in the run record | 1 | 4 | true | `ANTIPHON_HEADED_TESTS=1` |
| CP-2 | S1 | CP-1 | fake-contract | `/*/*/FakeClaudeContractTests/*` | V-9 | all listed, 0 failed | 49 | 5 | | |
| CP-3 | S1 | `tests/Antiphon.Tests -> bin-c121b/` | fake-adapter | `/*/*/FakeAgentProtocolAdapterTests/*` | S1 fake pins | all listed, 0 failed | 3 | 4 | | |
| CP-4 | S2 | `tests/Antiphon.Agents.Pty.Tests -> bin-c121c/` | evidence | `/*/*/ComposerDeliveryEvidenceTests/*` | V-8 | all listed, 0 failed | 26 | 3 | | |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c121d/` | index-stamp | `/*/*/SessionMessageQueueWedgedHeadTests/*` | S2 witnesses, CARD-0501 pins | all listed, 0 failed | 37 | 6 | | |
| CP-6 | S3 | `tests/Antiphon.Tests -> bin-c121e/` | proof-or-hold | `/*/*/SessionMessageQueueWedgedHeadTests/C0121*` | V-1..V-6 | all listed, 0 failed | 8 | 5 | | |
| CP-7 | S3 | CP-6 | pty-end-to-end | `/*/*/SessionMessageQueuePtyIntegrationTests/C0121*` | V-7 | 1 executed, 0 failed | 1 | 6 | true | |
| CP-8 | S4 | `tests/Antiphon.Tests -> bin-c121f/` | queue-regression | `/*/Antiphon.Tests.Application/(SessionMessageQueueWedgedHeadTests*)\|(SessionMessageQueueInterruptedAttemptTests*)\|(SessionMessageQueueDeliveryVerificationTests*)/*` | CARD-0501, CARD-0103 S2, CARD-0055 pins | all listed, 0 failed | 170 | 14 | true | |
| CP-9 | S4 | `tests/Antiphon.Agents.Pty.Tests -> bin-c121g/` | pty-regression | `/*/*/(ComposerInputProbeTests*)\|(ComposerDeliveryEvidenceTests*)\|(FakeClaudeContractTests*)/*` | S1, S2 pins | all listed, 0 failed | 84 | 5 | | |

`Min` values for CP-2, CP-4, CP-5, CP-8 and CP-9 are floors from the classes' `[Test]` counts at `4b4138fc5` (FakeClaudeContractTests 48, ComposerDeliveryEvidenceTests 24, SessionMessageQueueWedgedHeadTests 35, SessionMessageQueueInterruptedAttemptTests 15, SessionMessageQueueDeliveryVerificationTests 119, ComposerInputProbeTests 9) plus the
methods added here; the Code stage corrects them from its first roster if a count differs. Each
`After` group has its own build; CP-2 and CP-7 reuse their group's build. CP-1 and CP-7 are serial
because they spawn a real pty. Unlisted runs need a stated reason.

### Lane

Windows is needed: CP-1 needs real Claude on ConPTY and CP-7 needs the Windows-only fakeclaude
pty integration path. Dispatch the Code stage with `-Platform windows` and no `-Runner`; the
Windows runner is the desktop (`GET /api/session-runners`, 2026-10-22: desktop windows, capacity 2;
server2 linux is the global default). The S2/S3 migration needs the test Postgres the desktop
lane already has. Do not co-schedule `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests` rows.

### Cost

Nine rows, about 51 minutes of runs plus the canary's one-time measurement; a second canary run
only if the first prints `not-collapsed` (raise the body to 80 lines and rerun the same row).
