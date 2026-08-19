# CARD-0024 — Truncation detection for delivered messages: plan

**Date:** 2026-08-19
**Status:** planned (not implemented)
**Card:** CARD-0024 (`149c6211-e5aa-4161-858c-ec0a66e67777`) — delivery verification cannot
detect truncation, only non-delivery
**Incident:** task `c7151848` (2026-08-10). Parent session `da374342` was queued a 5 471-char
report and recorded **379** characters: head `src[0..246]` + tail `src[5339..5470]`, 5 × 1024
bytes dropped from the middle. `ComposerDeliveryEvidence` matched head-or-tail and certified
it. Investigation: `docs/investigations/2026-08-10-mangled-delegate-report-c7151848.md`.
Mechanism: `docs/investigations/2026-08-11-pty-chunk-loss-root-cause-CARD-0027.md`.
**Precedent:** CARD-0055 (`PromptSubmissionMatch.IsConfirmedBy`, head-window identity against
the recipient's `UserPrompt` row) already proves a body was *submitted*. It was written to
leave completeness to this card — `PromptSubmissionMatchTests.A_long_body_confirms_on_its_head_window_alone`
says so in as many words. CARD-0037 moved the clip quantum (inbox 1 024 B typed vs modern
86 400 B pasted) and kept spill-and-pointer as the prevention; this card is the check that
prevention held.

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**This still needs a code change.** Spill-to-file and the backend-conditional ceilings prevent
the original 5 KB *delegation* miss on a healthy modern pty. They do not certify completeness.
CARD-0055's head-window match (`MatchWindowChars = 200`) will mark Sent on every clip shape
that keeps the opening frame — first-chunk-only (CARD-0027, 5 194 B → first 1 026 B) and the
original head+tail splice (5 471 → 379) both pass identity today.

The ground truth is the same row CARD-0055 already waits for. After identity, require the
normalized record to *contain the full normalized body* (whitespace-free arm included, same
as identity). Identity without completeness is a new verdict, not `NoTranscriptRecord`: the
submit happened, re-typing would double-send, killing the session would abort a live turn.
Park immediately, raise an incident, do not retry.

One Code slice.

Spill is not a substitute. It covers delegation briefs/reports/refinements (and Grok briefs
always, CARD-0084). Channel and UI messages are still typed whole, with only the
`OversizedTerminalDelivery` size tripwire in `DeliverAsync` (`SessionMessageQueueService.cs:864`)
— and that tripwire fires on size *before* send, then sends anyway. Inbox
`ReplyInlineMaxChars = 3 000` is three clip-quanta. A machine that fell back to inbox
conhost, or a new clip that sits *under* the ceiling (the 2026-08-11 1 400-byte trials),
produces no size incident and a Sent that looks fine.

## 1. Current shape (verified against the files, 2026-08-19)

### 1.1 Two verifiers, neither sees a splice

| Check | When | What it asks | What a middle-clip does |
|---|---|---|---|
| `ComposerDeliveryEvidence.IsVisible` | pre-Enter, screen | head *or* tail fragment (40 chars) *or* a new `[Pasted text #N +M lines]` index | **Passes.** The 2026-08-10 miss kept both ends. On modern, the placeholder arm passes with no body on screen at all (CARD-0037). |
| `PromptSubmissionMatch.IsConfirmedBy` | post-Enter, `UserPrompt` row | normalized record contains the body's **head 200 chars** (or existence, if `< MinMatchChars`) | **Passes** if the head survived. Tail-only clips fail here (already pinned). First-chunk-only and head+tail clips pass. |

CARD-0055 D1 chose the head window *because* the measured loss mode keeps tails: a tail match
would certify a clipped body. That is this card. Identity and completeness were always
supposed to be two questions.

Bodies whose normalized form is **≤ 200 chars** already require full-body containment
(`TryBuildNeedle` returns the whole string). The gap is only bodies longer than
`MatchWindowChars`, which is exactly when a 1 024-byte clip can keep a matching head and
drop the rest.

`IsConfirmedBy` must not change. C4 (`SessionInputLog`) shares it and asks a different
question ("is this file ours?"). A clipped prompt of ours still identifies the file.

### 1.2 Where confirmation runs

`SessionMessageQueueService.DeliverAsync` (`:835`) → composer evidence → Enter →
`WaitForTranscriptConfirmAsync` (`:957`). That loop's only success is
`TryFindConfirmingRecordAsync` (`:1091`) returning true via `IsConfirmedBy`.
`LateConfirmAttemptedMessagesAsync` (`:655`) and `GraceConfirmAsync` (`:1026`) use the
same matcher. A truncated record that lands after the 30 s window is currently *promoted
to Sent* by late-confirm.

`DeliveryVerdict` is `Delivered | NoComposerEvidence | NoSubmitOutput | NoTranscriptRecord`.
Failure handling (`HandleDeliveryFailureAsync`, `:1172`) reverts to Pending, may kill an
idle always-on session, and parks only at `MaxDeliveryAttempts`. Truncation is none of
those: the terminal is not wedged, the composer is empty of *our* body (it submitted), and
a retry would type a second copy.

Boot (`VerifiedPromptSubmitter`) is out of CARD-0055's transcript-confirm on purpose (no
file yet on a fresh boot). Leave it there.

### 1.3 What the recipient actually stored

`TranscriptNormalizer.FromUser` writes the JSONL user text **verbatim** — no
`…[truncated]` marker (that exists only on tool input/result). `TranscriptEntries.Text`
has no `HasMaxLength`. CARD-0055's headed canary already pinned that a collapsed paste's
JSONL record carries the full body, not the placeholder. Completeness is therefore
comparable against the stored `UserPrompt` row; we do not need to re-open the JSONL.

Grok joins typed lines (CARD-0080: 4 450 sent → 4 389 recorded, exactly the newlines).
Identity already has a whitespace-free `Contains`. Completeness uses the same arm, or
every multi-line Grok delivery looks truncated. Grok has no CARD-0027 clip; the arm is so
the new check does not invent one.

### 1.4 Prevention vs detection

| Path | Prevention today | Residual |
|---|---|---|
| Delegation brief / refinement | Spill above `PtyDeliveryProfile.Ceilings.BriefInlineMaxBytes` (inbox 900 B, modern 43 200 B, Grok 0) | Under-ceiling typed bodies. Inbox 900 B is one chunk; modern is the paste envelope. |
| Delegation report | `FitReport` excerpts above `ReplyInlineMaxChars` (inbox 3 000, modern 14 400) | Inbox 3 000 chars is ~3 chunks and is still typed. |
| Channel / UI / anything else reaching `DeliverAsync` | None. Size tripwire at `SingleWriteMaxBytes` (inbox 1 024 B, modern 86 400 B) raises `OversizedTerminalDelivery` **and types anyway**. | This is the live exposure. |
| Inbox fallback of a "modern" server | `PtyDeliveryProfile` downgrades ceilings when the runner reports `InboxConhost` | Ceilings follow; verification still cannot see a splice under them. |

Do not build a generic spill-to-`.antiphon/inbox/` helper on this card. That is
investigation "Still open" #1, a prevention change, and a different shape. This card is
the check.

## 2. Decisions

### 2.1 Full-body containment, not length and not a tail needle

Length alone false-positives a splice that happens to be long (first-chunk of a 2 KB body
is ~1 024 B, well above any "shortfall ratio") and false-negatives a short splice with
enough JSONL framing to pad the count. A tail needle is the 2026-08-10 miss: both ends
survived. `normalizedRecord.Contains(normalizedBody)`, then the same whitespace-free
`Contains` identity already uses, is the check that fails on every measured clip and
passes on a framed-but-whole record (CARD-0055 already documents that Claude may wrap).

New method on `PromptSubmissionMatch`, **not** a change to `IsConfirmedBy`:

```
IsCompleteIn(body, recordText) -> bool
```

- No needle (`RequiresTextMatch` false) → `true`. Nothing to be incomplete about; the weak
  arm has no completeness claim.
- Spaced `Normalize(record).Contains(Normalize(body))` → `true`.
- Whitespace-free `Contains` when the stripped body still clears `MinMatchChars` → `true`.
- Else `false`.

Slash-command wrappers (`<command-name>/remote-control</command-name>`) already contain
the typed body, so completeness is a no-op there. C4 / `SessionInputLog` do not call this.

### 2.2 New verdict `Truncated`, not `NoTranscriptRecord`

`WaitForTranscriptConfirmAsync` already has the row. Classify it:

| Record vs body | Verdict | Why |
|---|---|---|
| none | keep polling → `NoTranscriptRecord` | CARD-0055, unchanged |
| identity, not complete | **`Truncated`**, stop polling | UserPrompt is written once; waiting will not grow it |
| complete | `Delivered` | |

Do not press Enter again on `Truncated`. The truncated body is already the current turn.

### 2.3 Park immediately. Do not kill. Do not re-type.

Own handler, not `HandleDeliveryFailureAsync`:

1. Revert the run to Pending (it was stamped Sent before the write, same as today).
2. Set `DeliveryAttempts = MaxDeliveryAttempts` so the existing park filter excludes it
   from every automatic redelivery path. No new status, no schema.
3. Raise **`AgentIncidentKind.TruncatedTerminalDelivery`** (new). Warning normally;
   Critical when the agent is channel-bound — same severity rule as parked channel
   replies (CARD-0055) and `ChannelReplyLost` (CARD-0067). Detail carries sent vs
   recorded normalized lengths so the splice is measurable from the timeline.
4. Do **not** kill. The session took a turn; a kill is the CARD-0055 working-guard's
   whole point, applied here even when idle.
5. Publish `AgentChanged` / queue-changed so the parked row is visible.

The card sketched reusing `OversizedTerminalDelivery`. That kind fires on **size before
send**, is Warning, and then the write proceeds. Truncation is **measured after submit**,
parks, and can fire *under* the ceiling (2026-08-11). Same kind would make "we typed
something big" and "we measured a splice" the same row. New kind; leave the size
tripwire alone.

### 2.4 Late-confirm and grace must not promote a splice to Sent

Today `LateConfirmAttemptedMessagesAsync` is identity-only. After this card, a parked
truncated message would be marked Sent on the next flush — undoing the park.

Late-confirm becomes:

- complete → Sent (today's success)
- identity, not complete → run the truncation handler (park + incident); do **not** mark Sent
- no identity → leave Pending (today's miss)

`GraceConfirmAsync` is late-confirm in a loop. A truncated classification during grace
is "handled", not "confirmed": `HandleDeliveryFailureAsync` must not then kill for
`NoTranscriptRecord` on those ids. Return truncated ids from grace separately from
confirmed ids, or classify inside the confirm loop so `Truncated` never reaches the
failure handler as `NoTranscriptRecord`.

### 2.5 No ceiling skip

A body under `SingleWriteMaxBytes` "should never" truncate. Run the check anyway. A hit
under the ceiling is a new mechanism, which is the card's defence-in-depth sentence.
Skipping under the ceiling would recreate the 2026-08-11 four-brief miss (all under
`PtyInlineSafeChars`, no incident).

### 2.6 Do not compare paste-placeholder line counts

CARD-0027 offered `M` in `[Pasted text #N +M lines]` vs the body's line count as a
pre-Enter check. Rejected here: it only exists on collapsed pastes (modern, large
multi-line); inbox typed input has no placeholder; wrap vs source lines is unpinned;
and it is not the signal that measured the original miss. The JSONL user record is.

## 3. The fix — one method, one confirm-loop branch, one handler

### 3.1 Matcher

`src/Antiphon.SessionRunner.Contracts/PromptSubmissionMatch.cs`: add `IsCompleteIn`.
`IsConfirmedBy` / `TryBuildNeedle` / thresholds untouched. Doc-comment on
`IsConfirmedBy` already points at this card; keep it.

### 3.2 Queue

`server/Application/Services/SessionMessageQueueService.cs`:

- `DeliveryVerdict.Truncated` + `Describe`.
- `TryFindConfirmingRecordAsync` returns the matching **text** (or a small result
  type `{ Identity, Complete, Text }`), not `bool`. Both confirm and late-confirm need
  the record to run `IsCompleteIn`.
- `WaitForTranscriptConfirmAsync`: on identity-without-complete, return `Truncated`.
- `Flush` / send-now: `Truncated` → `HandleTruncationAsync`, not
  `HandleDeliveryFailureAsync`.
- `LateConfirmAttemptedMessagesAsync` as §2.4.
- `HandleTruncationAsync` as §2.3. Dedup the incident on the message id so a later
  late-confirm classification does not raise a second row.

### 3.3 Incident kind

`server/Domain/Enums/AgentIncidentKind.cs`: `TruncatedTerminalDelivery = 25` (next
after `DelegateBindRefusalRecovered = 24`). Append-only; do not reuse 14.

## 4. Tests (same slice)

### 4.1 Matcher — `PromptSubmissionMatchTests`

Keep `A_long_body_confirms_on_its_head_window_alone` as the identity pin. Add
completeness cases:

| Shape | Identity | Complete |
|---|---|---|
| 2026-08-10 head+tail (5 471 → 379, ends preserved) | true | **false** |
| First-chunk-only (head 200+ of a long body, rest gone) | true | **false** |
| Tail-only (already identity-false) | false | n/a |
| Whole body | true | true |
| Whole body with framing (`<framing>…</framing>`) | true | true |
| Grok newline-join (CARD-0080 fixture) | true | true |
| The existing 40 KB body vs head+200 `z`s | true | **false** |
| Weak-match `"Continue."` | weak | true (vacuous) |
| `/remote-control` vs its `<command-name>` wrapper | true | true |

### 4.2 Queue — `SessionMessageQueueDeliveryVerificationTests`

A confirming `UserPrompt` that is a clipped prefix of a long queued body:

- verdict is not Delivered; message ends Pending + parked (`Parked == true`)
- `TruncatedTerminalDelivery` incident; Critical if the agent has a `ChatChannel`
- always-on session is **not** killed
- a subsequent flush's late-confirm does **not** mark it Sent
- identity-complete still marks Sent (regression on CARD-0055)

Reuse the existing harness (transcript rows inserted past a stored baseline). No new
schema.

### 4.3 Through the pty (optional in the same slice if cheap, not a second slice)

`SessionMessageQueuePtyIntegrationTests` already pins inbox. With
`ANTIPHON_FAKE_STDIN_CLIP=1` (keep-first) and a body > 1 024 bytes on the **typed**
path (no paste exemption), the fake's transcript record is a prefix and the queue
must park. Skip if the existing integration fixture cannot arm clip without disturbing
neighbours; the matcher + harness tests carry the rule. Do not add a headed canary —
the 2026-08-10 numbers and CARD-0027 probes are the real-Claude pin, already on disk.

## 5. Out of scope

- Changing `IsConfirmedBy`, C4, or `MatchWindowChars`.
- Generic spill for channel/UI messages (investigation still-open #1).
- Raising or lowering `PtyDeliveryProfile` ceilings.
- Boot-prompt path (`VerifiedPromptSubmitter`).
- Codex / OpenCode / Raw (delivery verification already skipped).
- Composer placeholder line-count.
- A new `QueuedMessageStatus`. Parked-Pending is the existing "not good, human looks"
  state.
- CLAUDE.md gotcha: one bullet that CARD-0055's Sent now also requires completeness,
  and that identity-without-completeness parks rather than retries. Same slice.

## 6. What this does not claim

On a healthy modern pty, in-ceiling bodies should never hit `Truncated`. The slice is
still the right size: the check is one `Contains` on a row we already fetched, and
without it a fallback, a flag-off machine, a channel message past the tripwire, or a
new clip under the ceiling is once again a coherent-looking Sent.
