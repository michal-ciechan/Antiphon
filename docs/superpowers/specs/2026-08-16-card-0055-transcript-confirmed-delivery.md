# CARD-0055 — A delivery is Sent only when its UserPrompt record exists

- **Status**: Planned (this document is the plan; nothing here is implemented)
- **Card**: CARD-0055 (`c93aa2c5-379a-4634-b52a-6a651cf9f70b`) — "A queued message is marked Sent
  when the screen merely redrew — so a delivery can sit unsubmitted for hours, or be lost"
- **Date**: 2026-08-16
- **Relates to**: CARD-0024 (verifier cannot see truncation — same verifier trusted for a second
  thing it cannot see), CARD-0047 (whose check-ins this fix demotes from primary completion signal
  to safety net), CARD-0006 (C4 prompt-matching machinery, reused here), CARD-0037 (paste
  placeholder rendering, which the matcher must be immune to), the 2026-08-08 miss that created
  `VerifiedPromptSubmitter`.

## 0. What happens today, and why it is wrong

Two implementations of the same delivery contract exist, and both stop verifying one step too
early:

- `SessionMessageQueueService.DeliverAsync` (server, `server/Application/Services/SessionMessageQueueService.cs:618`)
  — every queued delivery: UI sends, WhenIdle flushes, channel replies, delegation briefs and
  completion notes, launch notes, auto-continue prompts, stranded-watchdog redeliveries, batches.
  Sequence: normalize → size tripwire → type (bracket-wrapped) → `ComposerDeliveryEvidence` →
  20 ms pause → one `\r` → **"output sequence advanced" = Delivered**. No Enter retry at all.
- `VerifiedPromptSubmitter.SubmitAsync` (`src/Antiphon.Agents.Pty/VerifiedPromptSubmitter.cs`) —
  the boot-prompt analogue used by `RunnerClaudeAdapter.SendPromptAsync`. Same contract, plus up
  to 3 Enter presses when output does NOT advance.

"Output advanced" is satisfied by any redraw: a spinner, a status line, the composer re-rendering
the text it is still holding. The measured consequence (task 817682e9, session `cefed08a`, the
card's evidence — not re-derived here):

- `ea2feb92`'s note: Enter swallowed, screen redrew, marked **Sent** at 15:16:20Z; became a
  `UserPrompt` only at 17:00:09Z when the NEXT delivery's Enter pushed it in — 104 minutes.
- `15c9150e`'s note: its Enter submitted the stale `ea2feb92` body; a **new UserPrompt record did
  appear** — with the *wrong* text — output advanced, marked Sent, and its own body died with the
  composer. Never in the transcript at all.

The ground truth — the prompt becoming a `UserPrompt` JSONL record — is observable end-to-end
today with ~sub-second latency: `TranscriptTailer` polls at 300 ms
(`src/Antiphon.SessionRunner/TranscriptTailer.cs:37`), pushes `SessionTranscript` events, and the
server persists `TranscriptEntry` rows (`Kind == TranscriptKinds.UserPrompt`) as they arrive.
Nothing reads it at delivery time.

Note what the 15c9150e shape proves about the fix: **record *arrival* is not enough — the record's
*text* must match the body we typed.** A mere "a new UserPrompt appeared" check would have
false-positived on exactly the measured miss.

## 1. Design decisions

### D1. The verification signal: a text-matched UserPrompt row past a sequence baseline

Before the body is written, capture a per-session baseline: `max(Sequence)` over the session's
`TranscriptEntries` (0 if none — but see the observability gate below). Stored sequences are
arrival-ordered and rebased past the session max (CLAUDE.md, 2026-08-08 bullet), so every entry
ingested *live* after this moment has `Sequence > baseline` — backfill reordering cannot fake or
hide a match. Confirmation = a row with `Kind == UserPrompt`, `Sequence > baseline`, and a **text
match** against the typed body.

**The matcher is CARD-0006's C4 matcher, extracted, not reinvented.** `SessionInputLog`
(`src/Antiphon.SessionRunner/SessionInputLog.cs`) already solves this exact problem in the other
direction ("does a candidate transcript prompt appear in what was typed?") with rules earned
against real Claude: strip ANSI/bracketed-paste wrappers, drop control chars, collapse whitespace
runs (line endings must not matter — the delivery path rewrites them), `MinMatchChars = 12`
(short strings are worthless as identification), `MatchWindowChars = 200` (bounded compare).
Extract `Normalize`, `SkipEscapeSequence` and the two thresholds into a new static
`PromptSubmissionMatch` in `src/Antiphon.SessionRunner.Contracts/` (the server already references
Contracts for `TranscriptKinds`; it does not reference the runner project). `SessionInputLog`
delegates to it, so C4 and delivery confirmation stay in lockstep by construction.

Match rule, direction reversed from C4: `normalizedRecordText.Contains(head200(normalizedBody))`.
Head window rather than tail because a batch body's head is stable framing
(`ChannelPromptFormat.FormatBatch` output is part of the typed body and is recorded verbatim) and
because the pty's known loss mode keeps *tails* (CARD-0027) — a tail match could certify a clipped
body, which is CARD-0024's territory, not this card's.

**Paste placeholders**: on the modern backend a large body renders as `[Pasted text #N +M lines]`
(CARD-0037), but the JSONL user record carries what the *API* receives, which is the full pasted
content — the collapse is composer display only. That premise is already load-bearing for C4
("Claude records a submitted prompt verbatim in its JSONL") but has never been pinned for a
*collapsed paste specifically*; slice 4's headed canary pins it before the matcher is trusted for
large bodies. Contingency if the canary falsifies it: for bodies above the collapse threshold,
fall back to the weak-match arm below.

**Degenerate bodies** (the card's "legitimately no distinct prompt text"): a body whose normalized
form is shorter than `MinMatchChars` (e.g. an auto-continue "Continue.") cannot be identified by
text. Weak-match arm: any new `UserPrompt` row past the baseline that is not a local-command,
task-notification-only... no — keep it simple and honest: any `UserPrompt` with
`Sequence > baseline` counts, logged as a weak confirmation. Weaker than a text match but strictly
stronger than today's "any redraw". Empty bodies never reach `DeliverAsync` (normalized-empty is
dropped at enqueue).

**Observability gate — degrade, never fail, when ground truth is absent** (the echo-probe lesson,
same shape as the existing `TryGetLiveSnapshot` guard at `SessionMessageQueueService.cs:665`):
transcript confirmation runs only when the session has ≥ 1 `TranscriptEntry` at baseline time —
i.e. the transcript is bound and ingesting. A fresh session's launch note (queued before any
transcript exists — CARD-0006) and a session whose bind failed keep today's screen-only behavior,
logged. Codex/Raw sessions are already excluded by `IsClaudeCodeSessionAsync`.

### D2. Window and retry: 30 s, Enter-only re-press every 7 s, max 3 Enters, never re-type

New knobs on `DeliveryVerificationSettings` (`server/Application/Settings/SupervisionSettings.cs`):

| Setting | Default | Why |
|---|---|---|
| `TranscriptConfirmEnabled` | `true` | kill switch, same pattern as `Enabled` |
| `TranscriptConfirmTimeoutSeconds` | `30` | tailer 300 ms + pump ≈ sub-second live; 30× margin, and the card calls 30 s generous |
| `ReEnterIntervalSeconds` | `7` | long enough that a slow-but-successful submit's record usually lands first |
| `SubmitAttempts` | `3` | total Enters, matching `VerifiedSubmitOptions.SubmitAttempts` |

After the (kept) composer-evidence phase and first Enter, one loop replaces the bare
sequence-advance wait: poll (at `PollIntervalMs`) for the matching row; every
`ReEnterIntervalSeconds` without it, write another `\r` (up to `SubmitAttempts` total); at
`TranscriptConfirmTimeoutSeconds`, give up with the new verdict `NoTranscriptRecord`. The
sequence-advance check is kept *inside* the loop as a cheap wedge signal for logging, but it can
no longer produce `Delivered`. Both measured shapes resolve: a swallowed Enter (ea2feb92) gets a
re-press that submits the still-held body; a stale-body submit (15c9150e) produces a record whose
text FAILS the match, so the re-press submits our body and the second record matches.

**Why a re-pressed Enter cannot double-submit** — the card's sharpest question:

1. The retry is Enter-only, never a re-type. If the first Enter actually submitted, the composer
   is empty, and Enter on an empty composer is a no-op — already the documented contract of
   `VerifiedSubmitOptions` ("a re-press on a submitted, empty composer is a no-op"), relied on by
   the boot path since 2026-08-08. It is pinned in-process today; slice 4 adds the fakeclaude
   contract test and a headed real-Claude canary, because this design leans its full weight on it.
2. The per-session queue lock (`GetLock`) serializes deliveries, so during the window the composer
   holds at most *our* body — no other queued body can be standing in it for a re-press to submit.
3. The remaining vector is a **false-negative confirmation followed by a redelivery that
   re-types** — the first submit succeeded but the matcher was blind (ingestion stall, fork,
   text transform). That is closed structurally by D3's late-confirm: no reverted message is ever
   re-typed without first re-running the matcher over everything that arrived since its original
   baseline. The redelivery, not the re-press, is where a duplicate to a human could originate,
   and it checks first.

Residual, accepted: an operator typing into the same composer (RC/shared session) during the
window can interleave; the match then fails and the retry path runs. That window exists today with
no detection at all; with this change it at least ends in an incident instead of a silent `Sent`.

### D3. Failure behavior: revert + incident, late-confirm before any re-type, capped attempts

New verdict `NoTranscriptRecord` ("the submitted prompt never became a transcript record") joins
`DeliveryVerdict`; `HandleDeliveryFailureAsync` handles it with today's machinery — revert the run
to Pending, `DeliveryVerificationFailed` incident — plus three new behaviors:

- **Working-kill guard**: before the always-on kill, check `IsWorkingAsync`. A session that is now
  *working* is evidence the submit may have succeeded with the matcher blind — killing it would
  abort a live turn to fix a bookkeeping doubt. Working ⇒ no kill; the message stays Pending and
  the next turn-end flush late-confirms it. Not-working + always-on ⇒ kill as today (fresh
  composer, watchdog redelivers).
- **Late-confirm before any redelivery**: three columns on `SessionQueuedMessage` (migration
  `AddQueuedMessageDeliveryAttempts`): `DeliveryAttempts int not null default 0`,
  `LastDeliveryStartedAt timestamptz null`, `LastDeliveryBaselineSequence bigint null`.
  `DeliverNextLockedAsync` stamps them before typing. Every path about to deliver a Pending
  message with a prior attempt first re-runs the matcher over `UserPrompt` rows past that stored
  baseline; a match ⇒ mark Sent (`SentAt = now`, logged "late-confirmed, redelivery skipped"),
  publish, move to the next message. This is the anti-duplicate keystone: automatic retry is safe
  *because* the retry looks before it types.
- **Loop stop**: `MaxDeliveryAttempts` (default 3) on `DeliveryVerificationSettings`. A message at
  the cap stays Pending but is excluded from `FlushStrandedQueuesAsync` and turn-end flush
  candidate queries (`DeliveryAttempts < max`), i.e. it **parks for a human** — visible in the
  queue UI, where cancel/re-enqueue already exists (re-enqueueing resets attempts by being a new
  row). The parking incident escalates: **Critical when the agent is channel-bound** (a parked
  channel reply is a human waiting on a dead line — mirror of `TranscriptBindFailed`'s severity
  rule), Error otherwise.

Now-mode deliveries (never persisted; `messageIds == null`) keep today's synchronous surfacing to
the caller — there is no row to park or late-confirm, and a human is on the other end of the API
call.

### D4. Scope: who types into sessions, and who is covered

| Path | Covered? |
|---|---|
| `SessionMessageQueueService.DeliverAsync` — Now-mode sends, WhenIdle immediate-idle, turn-end flush, `FlushSessionAsync` boot nudge, compaction-recovery flush, stranded watchdog, Channel/Delegation batches; carrying UI messages, channel replies, delegation briefs + completion notes, launch notes, auto-continue prompts, CARD-0047 check notes | **Yes — this card.** All funnel through the one `DeliverAsync`. |
| `RunnerClaudeAdapter.SendPromptAsync` → `VerifiedPromptSubmitter` — boot/card prompts at launch | **No, deliberately.** At boot no transcript exists to verify against — the *file is created by the first submit*. Existing backstops: 3 Enter retries on no-output, and CARD-0006 binding itself is a transcript-level confirmation (C4 binds on the boot prompt's text; a never-submitted boot prompt surfaces as `TranscriptBindFailed`, Critical when channel-bound). Follow-up idea recorded on the card: treat a successful C4 bind as the boot prompt's confirmation and retry the Enter when binding times out. |
| `SendInputAsync` raw passthrough (interactive UI/RC keystrokes) | **No, by design** — a human is driving and watching. |
| Codex/Raw sessions | **No, unchanged** — composer contract and JSONL transcript are Claude-specific; they deliver blind today. |

### D5. The open composer-state question does not gate this fix

The card argues transcript verification is correct regardless of WHY the 15:16:20Z Enter failed.
Tested against each candidate state: a collapsed-paste scroll state, a dropdown/dialog eating
Enter, a mode change — in every case the submit either produces the matching record (delivered,
correctly) or does not (caught, retried, reverted — correctly). The argument holds because the
record is the *definition* of submitted: it is what the model receives. The one hazard specific to
re-pressing Enter into an unknown state — activating a dialog's default button — is not a new risk
class (`VerifiedPromptSubmitter` has re-pressed Enter since 2026-08-08) and is bounded by composer
evidence having just verified the body visible, which a full-screen dialog would have failed.
Forensics on `cefed08a`'s `.ansi` capture around 15:16:20Z are still worth one look for the
card's record (they might reveal a *preventable* state), but as a card addendum, not a slice.

## 2. Slices

### Slice 1 — Extract the matcher (independent, land first)

- `src/Antiphon.SessionRunner.Contracts/PromptSubmissionMatch.cs` (new): `Normalize`,
  `SkipEscapeSequence`, `MinMatchChars`, `MatchWindowChars`, and
  `IsConfirmedBy(body, recordText)` implementing D1's head-window containment + weak-match rule.
- `src/Antiphon.SessionRunner/SessionInputLog.cs`: delegate normalization/thresholds to it;
  public API unchanged.
- **Tests**: new `tests/Antiphon.SessionRunner.Tests/PromptSubmissionMatchTests.cs`
  (normalization, head window, containment direction, min-length → weak-match arm, ANSI/paste
  wrapper stripping, CRLF-vs-LF immunity). Existing C4 suites
  (`TranscriptAdoptionSafetyTests`, tailer tests) stay green untouched — the lockstep proof.

### Slice 2 — Transcript confirmation in `DeliverAsync` (needs 1)

- `server/Application/Settings/SupervisionSettings.cs`: the four D2 knobs.
- `server/Application/Services/SessionMessageQueueService.cs`: baseline capture (max sequence +
  ≥1-entry observability gate) before the body write; replace the bare
  `WaitForSequenceAdvanceAsync` verdict with the D2 confirm/re-press loop
  (`WaitForTranscriptConfirmAsync`, polling `TranscriptEntries` through a fresh scope); new
  verdict `NoTranscriptRecord` + `Describe` text; `RunnerClaudeAdapter` untouched.
- **Tests** (extend `tests/Antiphon.Tests/Application/SessionMessageQueueDeliveryVerificationTests.cs`,
  fake runtime + seeded DB, scoped to this test's rows per the shared-Postgres rule):
  swallowed-first-Enter → re-press → exactly one `\r` retry recorded, verdict Delivered;
  **stale-body record with non-matching text is NOT accepted, re-press follows** (the 15c9150e
  pin); no record ever → `NoTranscriptRecord`; zero-entry session degrades to legacy verdicts;
  short body → weak match; batch body confirmed by head window; sequence-advance alone no longer
  yields Delivered.

### Slice 3 — Failure path: late-confirm, attempts cap, kill guard (needs 2)

- Migration `AddQueuedMessageDeliveryAttempts` (`server/Migrations/`), entity columns per D3.
- `SessionMessageQueueService`: stamp attempt metadata in `DeliverNextLockedAsync`; late-confirm
  check before typing any previously-attempted message; attempts-cap filter in
  `FlushStrandedQueuesAsync` + flush candidate queries; `HandleDeliveryFailureAsync` working-kill
  guard + parking escalation (Critical when channel-bound).
- **Tests** (`SessionMessageQueueServiceTests` + delivery-verification suite): late-confirm marks
  Sent with ZERO writes to the runtime (the anti-duplicate pin); cap parks the message and
  excludes it from the watchdog; parked channel-bound ⇒ Critical incident; working session not
  killed on `NoTranscriptRecord`; idle always-on still killed; reverted metadata survives revert.

### Slice 4 — fakeclaude + pty pins and headed canaries (needs 2; 3 not required)

- `src/Antiphon.FakeClaude/`: `ANTIPHON_FAKE_SWALLOW_ENTER=<n>` — swallow the first *n*
  submitting CRs while still redrawing (the measured composer state, modeled); default off.
  Contract test that Enter on an empty fake composer is a no-op.
- `tests/Antiphon.Tests/Application/SessionMessageQueuePtyIntegrationTests.cs`: through the real
  queue against fakeclaude (with `ANTIPHON_FAKE_TRANSCRIPT_PATH`), one swallowed Enter ⇒ exactly
  ONE prompt in the fake's transcript, message Sent; all Enters swallowed ⇒ reverted Pending +
  incident, body appears ZERO times.
- Headed `[Explicit]` canaries (new `ClaudeSubmitConfirmCanaryTests` beside
  `ClaudeVerifiedDeliveryTests`): (a) Enter on real Claude's empty composer is a no-op — no
  turn, no record; (b) a collapsed-paste submission's JSONL `UserPrompt` carries the FULL body,
  not the placeholder — the D1 premise. fakeclaude parity: with
  `ANTIPHON_FAKE_PASTE_PLACEHOLDER=1` the fake's transcript must also carry the full body.

### Slice 5 — Docs and card close (last)

- CLAUDE.md gotcha bullet: "a delivery is Sent only when its UserPrompt record exists" — the
  Sent-on-redraw miss, the late-confirm rule, and that any NEW code path typing into a session
  must confirm against the transcript, not the screen.
- Close CARD-0055 with commit hashes; cross-note CARD-0024 (truncation remains the sibling gap —
  head-window match deliberately does not certify body *completeness*) and CARD-0047 (check
  cadence is now a safety net, per its spec §0).

**Landing order**: 1 → 2 → 3 → 4 → 5. Slice 2 alone already converts both measured misses from
silent `Sent` into delivered-on-retry; slice 3 makes the failure tail safe; slice 4 pins the two
load-bearing assumptions.

## 3. What I could not determine, and what settles it

1. **Real Claude's JSONL text for a collapsed paste** — assumed full body (C4's premise, never
   pinned for the placeholder case). Settled by the slice-4 headed canary (b); contingency is the
   weak-match arm for above-threshold bodies.
2. **Enter-on-empty no-op against real Claude** — documented in `VerifiedSubmitOptions`, believed
   from 2026-08-08 usage, no headed pin found. Settled by slice-4 canary (a). If falsified
   (e.g. an empty Enter produces an empty-turn record), the re-press interval logic must instead
   gate on "composer still shows our body" via a screen snapshot before each re-press.
3. **Why the 15:16:20Z Enter was swallowed** — `.ansi` capture + runner log around that timestamp
   for `cefed08a`. Does not gate (D5); worth a card addendum if the capture still exists.
4. **Whether transcript-confirm should hold the per-session lock for the full 30 s** — it does in
   this design (serialization is what makes re-press safe). Cost: a WhenIdle flush to OTHER
   sessions is unaffected (locks are per-session); the same session's next message waits, which it
   must anyway. If the 30 s tail proves noisy in practice, lower
   `TranscriptConfirmTimeoutSeconds` — do not release the lock mid-confirm.
