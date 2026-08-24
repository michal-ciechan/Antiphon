# CARD-0164 — unobservable-baseline delivery: transcript-first confirm from zero, a real herdr advance signal, CARD-0055 unweakened — plan

**Date:** 2026-08-24 · **Card:** CARD-0164 (`271d81a0-5695-4a13-9455-4e3b7864eae8`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** `feat/card-task-1b02176e` @ `432c95d` (= master after CARD-0160 S2 + CARD-0161
S3 + CARD-0162 S4). Every file:line below was re-read out of the code on that commit. Two live
measurements marked **measured (this pass)** were taken 2026-08-24 against the operator's live
herdr 0.8.2 through the same named-pipe NDJSON framing `HerdrClient` uses (read-only probe on an
existing pane + one scratch workspace, created and destroyed by the probe; herdr auto-removed the
emptied workspace).

**Established facts, not re-derived here:**
- The Investigate stage (task `32a55043`, findings on the card 2026-08-24), which CORRECTED the
  filed hypothesis: Mode:Now does NOT skip the CARD-0161 B3 refresh — both Mode:Now and WhenIdle
  share `DeliverAsync` → `TryGetLiveMetadata` → runner `GET /sessions/{id}` →
  `RefreshHerdrSurfaceAsync`. The real root cause: a fresh herdr session has ZERO
  `TranscriptEntries` at baseline, so CARD-0055 degrades to the screen-only verdict
  (`WaitForSequenceAdvanceAsync`, `SessionMessageQueueService.cs:1697`), and herdr's own
  `pane.revision` — which herdr `LastSequence` is folded from — measurably does NOT advance across
  a full turn (stuck at 3 through idle→working→done with a real reply landing). Verdict is
  `DeliveryVerdict.NoSubmitOutput` (`:1463`), surfaced to Mode:Now callers as a 409
  (`EnqueueAsync:219-221`). Reframed precisely as: **any delivery whose baseline is
  `Observable == false`** — not Mode:Now-specific, not first-message-specific.
- CARD-0161 S3 plan (`2026-08-23-card-0161-herdr-s3-delivery-adapter-plan.md`): the single-GET
  `pane.get` refresh (decision 4's runner fix — which made herdr `LastSequence` *readable*, not
  *moving*); `blocked` defers and only defers; herdr `agent_status` is never delivery evidence
  (decision 6); no herdr branch in `DeliverAsync`'s verdict logic (§1's prohibition).
- CARD-0162 S4 plan: herdr events/status are verification triggers, never evidence; the only
  status-driven side effects are the incident row and the blocked-exit `FlushIfIdleAsync` nudge.
- CARD-0055/0024/0056/0103 delivery discipline (CLAUDE.md): Sent requires a matching, COMPLETE
  `UserPrompt` record; retries are Enter-only; late-confirm before any re-type; pull the transcript
  before anything destructive; a wall-clock tolerance is what tells a resumed conversation's copied
  history from fresh evidence (`AgentSessionService.BootConfirmClockTolerance`, `:520`).

**Related:** CARD-0055 (the verdict machinery this card must extend without weakening), CARD-0056
(the wall-clock-floor precedent copied here), CARD-0103 (the pre-first-turn refund — the sibling
"fresh session, screen signal lies" fix, untouched), CARD-0161 (B3 — necessary but not
sufficient), CARD-0006 (why zero transcript rows is a DESIGNED state, not an error), CARD-0141
(why Enters near modals are guarded), CARD-0162 (status never evidence).

---

## Verdict up front — the twelve decisions

1. **Unobservable-baseline policy: transcript-first, screen fallback retained.** When
   `baseline.Observable == false`, the delivery no longer skips the confirm loop — it runs a
   variant that polls for the FIRST matching `UserPrompt`/`QueuedUserPrompt` row (sequence floor 0)
   guarded by a **wall-clock floor** (the CARD-0056 shape), with periodic
   `CatchUpTranscriptAsync` pulls inline. A confirming complete row → `Delivered` (the false
   negative closes). At the deadline with no row: sequence advanced → `Delivered` under exactly
   today's degraded screen-only meaning; nothing at all → `NoSubmitOutput`, exactly today's
   verdict. No new verdicts; no verdict gets easier to earn. §3.
2. **Herdr advance signal: a runner-owned content-delta counter, folded into herdr
   `LastSequence`.** `HerdrPaneChild` bumps a monotonic counter whenever the stripped visible
   `pane.read` text differs from the last read (revision still folded via `Math.Max` — it can only
   add). Measured this pass: an unchanged pane's `pane.read` is byte-identical across repeated
   reads (no false bump), and real output changes the text while `revision` stays 0. REJECTED:
   an Antiphon write-counter (self-evidence — it would certify our own keystroke and weaken the
   check to nothing) and `agent_status` transitions (S3 decision 6's pin: herdr status is never
   delivery evidence). §4.
3. **Scope: backend-agnostic in the queue.** The unobservable-baseline policy is one code path for
   all delivery backends — no herdr branch in verdict logic (S3 §1's prohibition holds). The only
   herdr-specific piece is the runner-side counter (decision 2). This also upgrades the pty fresh
   -session shape (today: a redraw marks the launch note Sent) to a real confirm whenever rows
   arrive. §3, §5.
4. **Launch-time transcript marker (investigation direction c): REJECTED.** It fabricates evidence
   rather than confirming it, pollutes every transcript consumer (three lockstep working-rule
   implementations, turn-response windows, channel dispatch), and converts bind-failure from
   degraded-but-delivering into deterministic delivery failure. CARD-0006's "zero rows reads idle"
   is a designed invariant, not a gap. §6.
5. **Mode:Now: a bounded grace-confirm before any 409** — for `NoSubmitOutput` /
   `NoTranscriptRecord` only (never `NoComposerEvidence`, where Enter was withheld; never
   `Truncated`/`ForbiddenBody`), reusing `PostFailureConfirmGraceSeconds` (20 s) and the same
   floor discipline (sequence floor when observable, wall-clock floor when not). It can only turn
   a failure into a success on the same positive evidence `GraceConfirmAsync` demands. This also
   closes a pre-existing sibling gap: Mode:Now has no message row, so `HandleDeliveryFailureAsync`
   's grace (`:1958`, gated on `messageIds`) never ran for it even on observable sessions. §7.
6. **WhenIdle's first flush: same slice by construction (same `DeliverAsync` path), PLUS the
   late-confirm extension this pass found necessary:** a message attempted with a NULL stored
   baseline is today skipped by `LateConfirmAttemptedMessagesAsync` (`:1049`) and **re-typed —
   the current behavior on a fresh herdr session is a DOUBLE-DELIVERY, not just a false 409**
   (§2, the new finding). The fix: a wall-clock-floor arm — text-match-only, completeness
   enforced — keyed on `LastDeliveryStartedAt`. §5.
7. **Herdr `revision`: re-measured, and Antiphon must not depend on it.** This pass confirmed the
   investigation on 0.8.2: `revision` stays 0/flat through typed input, submitted commands, and
   rendered output on both a scratch pane and (investigation) a real Claude turn. It stays folded
   (`Math.Max` costs nothing; a future herdr that fixes it can only add advances) but nothing may
   *require* it to move. Not escalated upstream as blocking. §4.
8. **`WaitForTranscriptConfirmAsync`'s sequence-advance wedge log: kept.** Decision 2 makes it
   meaningful again on herdr for free; no code change beyond the counter. §4.
9. **Tests:** `FakeHerdrServer` grows scriptable sticky-`revision` + per-read text sequences;
   runner pins the counter's bump/no-bump/monotonic contract; server pins the headline
   false-negative fix (red without it) AND the never-weaken set — no row + no advance still fails,
   an old-timestamp row never confirms, identity-without-completeness from zero still parks as
   `Truncated`, and `PromptSubmissionMatch` is byte-untouched. §9.
10. **`GetSnapshot`/`GetAsync` divergence: dissolved.** Both fold the same child-owned counter
    after decision 2; pinned by an interleaving runner test. (The remaining cosmetic difference —
    `GetAsync` costs one extra `pane.read` — is measured cheap: 2–5 ms idle, S3 M5.) §4.
11. **CARD-0162 interaction: none, pinned.** `agent_status` may not nudge, satisfy, or accelerate
    delivery confirmation. The ONLY status read on the delivery path remains the S3 blocked
    Enter-withhold (`:1551`), which the unobservable loop's re-presses inherit by sharing the loop
    body. §8.
12. **Caller contract: 409 kept for true failures.** After decisions 1+5 a Mode:Now 409 means
    either "Enter withheld, nothing submitted" or "no record and no screen change through a 30 s
    pulled window plus a 20 s pulled grace". The residual false-negative window — a record landing
    after the grace — is stated, incident-recorded, and has no automatic recovery on Mode:Now (no
    row to late-confirm); callers wanting durable delivery keep WhenIdle. §7.

---

## Live measurements (this pass, 2026-08-24)

Raw named pipe (`%APPDATA%\herdr\herdr.sock`, one NDJSON request per connection — `HerdrClient`
framing), herdr 0.8.2. M2's furniture (workspace `w9`, one pane) was created and destroyed by the
probe.

| # | Measurement | Result |
|---|---|---|
| M1 | **Idle-pane read stability** — 6 × `pane.read {source:"visible", strip_ansi:true}` over 3 s on an untouched pane | Text **byte-identical** across all 6 reads (SHA-256 equal), `revision` flat. A content-delta counter has no idle false-bump source. |
| M2 | **Content delta vs revision** — scratch pane: read → `pane.send_text "echo CARD0164-PROBE-OUTPUT"` → read → `send_keys ["enter"]` → read → `pane.get` | Typed echo changed the text hash (len 8→35); Enter+output changed it again (35→65, output string present). **`revision` stayed 0 through all of it**, on both `pane.read` and `pane.get`. The screen is the signal; revision is not. |

Corroborating (investigation, same day, cited not re-run): a real Claude herdr turn left
`revision` at 3 through idle→working→done while the reply rendered and the `UserPrompt` reached
the transcript; a shell pane's revision stayed 0 while echo output appeared.

## 1. What exists on `432c95d`, restated precisely (so the diff stays small)

`DeliverAsync` (`SessionMessageQueueService.cs:1282`) already: normalizes/wraps, refuses forbidden
bodies, sizes against per-session ceilings (S3), takes composer evidence over polled `pane.read`
(works — S3 M1/M3), captures the transcript baseline (`CaptureTranscriptBaselineAsync:1248`,
`Observable = any stored row`), and routes: observable → `WaitForTranscriptConfirmAsync` (`:1490`,
CARD-0055's loop — Enter-only re-presses, herdr blocked-withhold at `:1551`, Truncated on
identity-without-completeness, `NoTranscriptRecord` at deadline); unobservable →
`WaitForSequenceAdvanceAsync` (`:1697`) → `Delivered` on any `LastSequence` advance, else
`NoSubmitOutput`. For herdr sessions `LastSequence` is the folded `pane.get`/`pane.read`
`revision` (`RunnerSession.RefreshHerdrSurfaceAsync`, `SessionRunnerRuntime.cs:660-671`;
`GetSnapshot:1395-1416`), which measurably never moves — so the unobservable arm fails
deterministically on herdr, and `TypeLocalCommandAsync`'s advance check (`:2548`) and the confirm
loop's wedge log (`:1527-1533`) are equally blind. For pty sessions `LastSequence` is a real
per-output-chunk counter (`:1573`) and the arm works.

The failure paths: Mode:Now (`EnqueueAsync:163-228`) throws 409 via `HandleDeliveryFailureAsync`
(null `messageIds` → no grace-confirm ever, `:1958`); queued flushes revert to Pending, and
`LateConfirmAttemptedMessagesAsync` (`:1039`) **skips any attempt whose stored baseline is null**
(`:1049` — "no floor, a match would prove nothing"), so the next flush re-types.

**CARD-0164 is therefore NOT a new verdict, a new matcher, or a herdr delivery branch.** It is:
(a) a stronger confirmation path for the baseline-unobservable case that ends in exactly today's
verdicts, (b) one runner-side counter that makes the existing screen signal true on herdr,
(c) a wall-clock floor (CARD-0056's, transplanted) everywhere a sequence floor doesn't exist,
and (d) pins. Anything in the build that touches `PromptSubmissionMatch`, relaxes what counts as
confirmation on an OBSERVABLE baseline, or adds a herdr conditional to verdict classification is
off the map and must stop.

## 2. The new finding this pass: the WhenIdle shape is a double-delivery, not a 409

Traced on `432c95d`, by construction: a WhenIdle flush on a fresh herdr session stamps
`LastDeliveryBaselineSequence = null` (`DeliverNextLockedAsync:961`), types the body, Enter
submits, Claude takes the turn — and the verdict is still `NoSubmitOutput` (sticky sequence).
`HandleDeliveryFailureAsync` reverts the row to Pending (grace runs only for `NoTranscriptRecord`).
The turn our own delivery started then ENDS → `OnTurnEndAsync` → `DeliverNextLockedAsync` →
late-confirm **skips the row** (null baseline, `:1049`) → **the body is typed a second time.**
Attempt 2 sees an observable baseline and confirms, so the queue looks healthy while the agent
received the brief twice. Herdr sessions are never channel-bound (S2 refusals), so no human chat
gets the duplicate — but a delegation brief delivered twice is a real defect, and it upgrades this
card from "self-healing false negative" to "false negative with a double-type behind it". The fix
is decision 6's late-confirm arm plus decision 1 (which makes attempt 1 confirm in the first
place); the pin is §9's test (iv).

## 3. Decision 1 & 3 — the unobservable-baseline confirm, in the existing loop

`DeliverAsync` changes routing only: `confirmTranscript` becomes `verify &&
_verification.TranscriptConfirmEnabled` (dropping the `baseline.Observable` gate at `:1379`), and
`WaitForTranscriptConfirmAsync` branches internally on `baseline.Observable`. Unchanged: the
`verify == false` blind path, the `TranscriptConfirmEnabled == false` screen-only path, the local
-command arm, composer evidence, the 20 ms gap, one submitting Enter.

**Inside the loop, when `Observable == false`:**

- **Floor:** sequence floor 0 (all rows) **plus a wall-clock floor** `confirmFrom = UtcNow() −
  UnobservableBaselineConfirmClockToleranceSeconds`, captured in `DeliverAsync` where the baseline
  is captured — BEFORE the body write, mirroring `SendBootPromptWithRetryAsync:560` — and threaded
  in. A candidate row must have `Timestamp != null && Timestamp >= confirmFrom`; a record with no
  timestamp is not evidence (CARD-0056's rule verbatim, and its reason: a `--resume`'s copied
  history and a late-binding tailer's backfill keep ORIGINAL timestamps while backfill rebases
  their sequences past any sequence floor — only the wall clock tells them from fresh evidence).
  New setting `DeliveryVerificationSettings.UnobservableBaselineConfirmClockToleranceSeconds`,
  default 30, doc citing `BootConfirmClockTolerance` (`AgentSessionService.cs:520`). The tolerance
  also covers `QueuedUserPrompt` rows, whose stored timestamp is the composer-enqueue time — at
  most the 15 s evidence window before Enter, inside 30 s.
- **Match:** the same `TranscriptConfirm.Classify` → `PromptSubmissionMatch.IsConfirmedBy` +
  `IsCompleteIn`, untouched. Identity+complete → `Delivered`. Identity without completeness →
  `Truncated` (park, incident, no kill, no re-type — CARD-0024's arm, now reachable from zero).
  The weak arm (body under `MinMatchChars`) → any timestamped row past `confirmFrom` counts —
  strictly stronger than the redraw signal it replaces, same relative strength as the observable
  weak arm. (Stated residual, shared with the observable weak arm today: an operator typing into
  the visible herdr pane during the window could satisfy it; text-match bodies are immune.)
- **Pulls:** `_runtime.CatchUpTranscriptAsync` (the no-side-effects half — NEVER
  `SyncTranscriptAsync`, whose turn-boundary flush re-enters the queue while the caller holds the
  per-session lock; `AgentSessionRuntime.cs:422-435` documents exactly this) at most every
  `max(1000, PollIntervalMs)` ms, interleaved with the existing per-`PollIntervalMs` DB checks.
  The fresh-session ingestion path was measured live (the repro: record on disk ~2 s after Enter;
  S3 M1: +1.7 s), but CARD-0055's 45 s store-stall is the reason pulls are load-bearing, not
  optional.
- **Re-presses:** the loop's existing Enter-only schedule (`ReEnterIntervalSeconds`/
  `SubmitAttempts`) runs unchanged, including the S3 herdr blocked-withhold (`:1551`) — shared by
  construction, not duplicated. This ADDS re-press recovery to a path that today has none (a
  swallowed first Enter on a fresh session is currently unrecoverable); safety is the same
  documented contract (empty-composer no-op + per-session lock), unchanged by observability.
- **At the deadline** (`TranscriptConfirmTimeoutSeconds`, 30 s): if `sawSequenceAdvance` (the
  loop already tracks it, `:1527-1533`) → **`Delivered`**, logged loudly as the degraded
  screen-only verdict — exactly the claim today's unobservable path makes, made no sooner and on
  no less evidence; else → **`NoSubmitOutput`** — today's verdict, now with 30 s of pulled
  transcript behind it instead of blindness. `NoTranscriptRecord` is deliberately NOT produced
  from the unobservable branch: its post-verdict grace (`HandleDeliveryFailureAsync:1958`) would
  re-pull what this loop already pulled, and its meaning ("the submitted prompt never became a
  record") presumes a bound transcript the session may not have.

**Why the fallback stays (and the scope is all backends):** a session whose transcript never
binds (CARD-0006's designed degraded state — `TranscriptBindFailed` incident, session runs with
NO transcript) produces no rows ever. Removing the screen fallback would convert every delivery
to such a session from degraded-Delivered into a hard failure — a regression the never-weaken
constraint cuts both ways against. Cost, stated: on bind-failed sessions the degraded `Delivered`
now arrives at the 30 s deadline instead of at first redraw (~0.5 s). Accepted: bind-failure is
rare, already Critical-alerted when channel-bound, and correctness of the common case outranks
latency of the flagged-broken one. A short-circuit ("return early on advance if the runner says
no tailer is attached") was considered and REJECTED for S-slice scope: it needs a new DTO field
for an optimization with no failing measurement behind it (S3 decision 5's principle). On healthy
fresh sessions the row confirms in ~2 s — faster than herdr's current 30 s failure and ~1.5 s
slower than pty's current redraw pass, in exchange for a real verdict.

## 4. Decisions 2, 7, 8, 10 — the herdr advance signal

**`HerdrPaneChild` owns one monotonic `_contentSequence`** (under its own state, exposed alongside
status): on every screen read — `ReadScreenAsync` (`HerdrPaneChild.cs:209`) and a `pane.read`
added to `RefreshStatusAsync` (`:147`) — compare the stripped visible text (ordinal, full string —
no hashing subtleties; the screen is a few KB and M1 pins byte-stability) against the last-seen
text; differ → increment. `revision` keeps folding in via `Math.Max` (decision 7: it can only add;
nothing may require it). Both read paths use IDENTICAL `pane.read` params (`source:"visible",
strip_ansi:true, lines:null`) so path interleaving cannot fabricate a delta (decision 10's pin).
`RunnerSession.RefreshHerdrSurfaceAsync` and `GetSnapshot` fold the child's counter into
`_lastSequence` exactly as they fold revision today — no wire, DTO, or server change: the existing
`GET /sessions/{id}` → `TryGetLiveMetadata` plumbing carries it.

Consumer sweep (build must re-verify on its commit): `WaitForSequenceAdvanceAsync` (the point),
the confirm-loop wedge log (decision 8 — meaningful again, no change), `TypeLocalCommandAsync:2548`
(local TUI commands to herdr sessions currently false-fail `NotAdvanced` by the same mechanism —
healed for free), `RunnerBufferDto.LastSequence` (display), adoption/exit events (herdr hardcodes
`LastSequence: 0` — untouched), reconciliation (CARD-0056 deliberately requires no advancement —
untouched). Nothing anywhere treats a LARGER LastSequence as a kill or settlement signal, so a
counter that advances more than revision did is strictly de-escalating.

**Rejected alternatives, with the failure they'd introduce:**
- *Antiphon-side bump on `WriteAsync`/`send_keys`:* certifies our own keystroke — an Enter into a
  dead pane would still "advance" and the fallback verdict would become unconditionally
  `Delivered`. That is a weakening, and exactly the kind the hard constraint forbids.
- *`agent_status` transition to `working` as advance evidence:* violates S3 decision 6 / S4's
  one-directional pin (herdr's detection is the same screen-heuristic class as ours; S1's false
  `agent_prompt_stalled` is the standing proof). Status keeps exactly one delivery-path power:
  withholding an Enter.
- *Trusting `revision` after a herdr upgrade:* nothing may depend on it until a measurement says
  it tracks content; the fold means a fixed herdr just makes the counter advance sooner.

## 5. Decision 6 — late-confirm's wall-clock arm (the double-type fix)

`LateConfirmAttemptedMessagesAsync` (`:1039`) gains one arm where it today `continue`s (`:1049`):
a message with `DeliveryAttempts > 0`, `LastDeliveryBaselineSequence == null`, and
`LastDeliveryStartedAt is { } started` is matched against rows with `Sequence > 0 && Timestamp !=
null && Timestamp >= started − tolerance` (same new setting). Everything else is byte-identical
to the existing arm: text-match-only (`RequiresTextMatch` — short bodies keep re-delivering;
duplicating an auto-continue is cheap, silently dropping a human's message is not), completeness
enforced (identity-without-completeness → the CARD-0024 truncation park, never Sent), Sent stamped
with zero terminal writes. The old comment's claim ("no floor, so a match would prove nothing")
is superseded, not deleted — rewrite it to say the floor is now the attempt's own wall clock, and
why that floor is sound (CARD-0056's argument: copied history and backfill keep original
timestamps). `GraceConfirmAsync` and `SendNowAsync`'s pre-check inherit the arm for free (both
call this method). `LastDeliveryStartedAt` already exists and is stamped on every attempt
(`:485`, `:960`) — no migration.

With decision 1 in place this arm is a second line of defense (attempt 1 should confirm inline);
it exists because the double-type shape (§2) must be impossible even when the inline confirm
loses its race — e.g. a Truncated-classified splice, or ingestion beyond the 30 s window.

## 6. Decision 4 — no launch-time transcript marker, and why (rejected direction c)

Seeding a synthetic `TranscriptEntry` at launch would flip `Observable` to true and route fresh
sessions into the standard confirm loop — superficially the smallest fix. Rejected on four
grounds, each sufficient:
1. **It fabricates ground truth instead of confirming against it.** `Observable` means "a real
   ingestion pipeline has demonstrably delivered rows for this session"; a seeded row asserts that
   about a pipeline that may never bind (CARD-0006's C1–C4 can legitimately end in NO transcript).
   Decision 1 gets the same confirm strength from real rows, when they exist, without the lie.
2. **Blast radius:** every transcript consumer sees the seed — the three lockstep working-rule
   implementations, `ExtractTurnResponseAsync`'s sequence windows, channel follow-up dispatch,
   the UI timeline. Each would need an exclusion, in lockstep, forever (the CARD-0041 lesson about
   how exclusion lists rot is one bullet up in CLAUDE.md).
3. **It makes bind-failure strictly worse:** with a seed, a bind-failed session's deliveries enter
   the full confirm loop, produce `NoTranscriptRecord`, and fail deterministically — today they
   degrade and deliver. Decision 1 keeps the degraded arm precisely for this.
4. It doesn't even shorten the path: the confirm loop would still be waiting for the FIRST real
   row, exactly as decision 1 has it, since the seed itself can never text-match a delivery.

## 7. Decisions 5 & 12 — Mode:Now's grace, and the 409 contract

**Grace (decision 5):** in `EnqueueAsync`'s Now arm (`:216-222`), before
`HandleDeliveryFailureAsync`/throw, for verdicts `NoSubmitOutput` and `NoTranscriptRecord` only:
a bounded pull-and-recheck loop — `CatchUpTranscriptAsync` + `TryFindConfirmingRecordAsync` with
the delivery's own floor (its baseline sequence when observable; sequence 0 + the wall-clock floor
when not) — for `PostFailureConfirmGraceSeconds` (20 s, existing knob). Complete match → success
(return the queue DTO, no incident, log the late verify). Identity-without-completeness →
`HandleTruncationAsync` + the existing Truncated 409. Nothing → today's path exactly (incident +
409). `NoComposerEvidence` is deliberately excluded: Enter was withheld, nothing can have
submitted, and a grace there would wait 20 s to learn nothing. `SendNowAsync` needs no new code —
its verdicts flow through `HandleDeliveryFailureAsync` WITH message ids, where the existing grace
(extended by §5's arm for null-baseline rows) already runs.

**Contract (decision 12):** the 409 stays, and stays meaning "delivery could not be verified" —
never softened to 200-with-warning (a Mode:Now caller is often a script gating its next step on
the send having landed; an unverified success is the CARD-0055 disease reintroduced at the API).
After this card, a false 409 requires the record to still be missing after ~50 s of pull-based
looking (30 s inline + 20 s grace); the incident row (`DeliveryVerificationFailed`) remains the
audit trail. Stated residual: Mode:Now persists no row, so a record landing after the grace has
no late-confirm to flip it — a caller that retries a genuinely-false 409 double-sends. That
residual exists today with a far larger window; this card shrinks it and documents it. Callers
needing at-most-once semantics keep WhenIdle (durable row + late-confirm).

## 8. Decision 11 — the CARD-0162 boundary, restated as a prohibition

No arm of this card may consume `agent_status`, `SessionAgentStatus` events, or any S4 surface as
delivery evidence — not to confirm, not to shorten a window, not to choose a verdict. The single
permitted status read on the delivery path remains the S3 blocked Enter-withhold, which the
unified loop keeps. Conversely, S4's blocked-exit `FlushIfIdleAsync` nudge continues to trigger
flushes whose deliveries then confirm exactly as above — triggers and evidence stay disjoint
vocabularies. Pinned by §9 test (viii).

## 9. Verification / test design

Server tests in `Antiphon.Tests` (TUnit; shared-Postgres rules — every assertion scoped to rows
the test made; sticky sequence simulated via the existing test-adapter seam, whose
`GetDeltaSequenceOrDefault` the test simply never advances; rows inserted directly into
`TranscriptEntries` stand in for ingestion, since the loop's DB poll reads them regardless of
whether the `CatchUpTranscriptAsync` seam pulled anything). Runner tests in
`Antiphon.SessionRunner.Tests` against `FakeHerdrServer`.

- **`FakeHerdrServer` extensions:** `pane.read`/`pane.get` serve a scriptable `revision` that can
  be pinned STICKY (the measured 0.8.2 truth becomes the fake's default, so no future test can
  accidentally lean on revision moving), and a scriptable per-read text sequence (same text until
  the script advances it).
- **`HerdrRunnerSessionTests` additions (runner):**
  (i) sticky revision + changed read text → `LastSequence` advances (via both `GetSnapshot` and
  the single-session GET); (ii) sticky revision + identical text across N reads → NO advance
  (M1's no-false-bump, pinned); (iii) interleaving `GetSnapshot` and `GetAsync` reads of the same
  text never fabricates a delta and the counter is monotonic (decision 10); (iv) revision moving
  WITHOUT a text change still advances (the fold kept); (v) pty sessions untouched (no herdr
  calls, `_lastSequence` still the output-event counter).
- **`SessionMessageQueueDeliveryVerificationTests` — CARD-0164 cases (server):**
  (i) **The headline false-negative pin:** unobservable baseline + sequence that NEVER advances +
  a matching COMPLETE `UserPrompt` row inserted mid-window with a fresh timestamp → verdict
  `Delivered`, queued row `Sent`. RED without the fix (today: `NoSubmitOutput`).
  (ii) **The never-weaken pin (mirroring S3/S4's own):** unobservable baseline + sticky sequence +
  NO row ever → still `NoSubmitOutput`; the row reverts to Pending, is NOT Sent, attempts charge.
  AND: a matching row whose `Timestamp` predates `confirmFrom` (the resume-history/backfill
  shape) does NOT confirm — the delivery still fails. AND: a row with a NULL timestamp does not
  confirm. Together: no input state that fails today can pass except through a timestamped,
  matching, complete transcript record.
  (iii) Identity-without-completeness from zero → `Truncated`: parked at `MaxDeliveryAttempts`,
  `TruncatedTerminalDelivery` incident, no kill, never Sent — including via the late-confirm arm.
  (iv) **The double-type pin:** a message attempted once with a null stored baseline whose
  matching complete row exists (fresh timestamp) is late-confirmed `Sent` on the next flush with
  ZERO terminal writes — RED without §5 (today it re-types). And its negative: an old-timestamp
  match still re-delivers.
  (v) Weak arm from zero: a sub-`MinMatchChars` body confirms on any fresh-timestamped row and
  does NOT confirm on an old-timestamped one; null-baseline late-confirm still refuses weak
  bodies entirely.
  (vi) Screen fallback preserved: unobservable + no row + sequence DOES advance → `Delivered` at
  the deadline (the bind-failed no-regression pin), logged as degraded.
  (vii) Mode:Now grace: a failing `NoSubmitOutput` whose record lands during the grace loop →
  success, no incident, no 409; grace expiring empty → 409 + incident exactly as today;
  `NoComposerEvidence` gets no grace.
  (viii) **The status-prohibition pin (decision 11):** a status flip to `working`/`done` during
  the unobservable window, with no transcript row, changes nothing — still `NoSubmitOutput`; and
  the herdr blocked-withhold still suppresses re-presses inside the unobservable loop.
  (ix) Observable-baseline behavior byte-identical: the existing CARD-0055/0024/0161 cases run
  untouched (no edits to those tests is itself the pin), and `PromptSubmissionMatchTests` is not
  modified at all.
- **Probe PR1 (build-time, live, S3-style):** the card's original repro re-run through the
  production path — fresh Herdr agent, first `POST /messages` Mode:Now → expect 200 with the
  transcript-confirmed record; then a WhenIdle first flush on a second fresh session → exactly ONE
  `UserPrompt` with the body (the double-type absent). Results recorded in the build commit and
  appended to this plan's measurement table.

## 10. Out of scope

Boot/`VerifiedPromptSubmitter` prompts (CARD-0055's boot scope-out and CARD-0056's resume
late-confirm stand — a fresh boot still has no transcript file and no queue); any
`PromptSubmissionMatch` change; observable-baseline confirm semantics (timing, floors, verdicts —
byte-identical); pulls inside the OBSERVABLE confirm loop (grace owns that, post-verdict, by
design); `agent_status` in any evidentiary role; herdr `revision` upstream fix; ceilings,
`PtyBackend`, S3's blocked gate, S4's pump; a runner "tailer attached" DTO field (the rejected
latency optimization, §3); UI changes.

## 11. Build order

1. **B1 — runner content-delta counter:** `HerdrPaneChild._contentSequence`, `RefreshStatusAsync`
   grows the `pane.read`, folds threaded; `FakeHerdrServer` sticky-revision + text scripting;
   `HerdrRunnerSessionTests` (i)–(v). Ships alone: heals `TypeLocalCommandAsync`, the wedge log,
   and today's screen fallback for herdr sessions even before B2.
2. **B2 — the unobservable confirm loop (server):** routing change in `DeliverAsync`, the
   `Observable == false` branch of `WaitForTranscriptConfirmAsync` (wall-clock floor, pulls,
   deadline fallback), `UnobservableBaselineConfirmClockToleranceSeconds`. Queue cases (i), (ii),
   (iii), (v), (vi), (viii), (ix).
3. **B3 — late-confirm wall-clock arm (server):** the null-baseline arm in
   `LateConfirmAttemptedMessagesAsync`. Queue case (iv).
4. **B4 — Mode:Now grace (server):** the pre-409 pull-and-recheck in `EnqueueAsync`'s Now arm.
   Queue case (vii).
5. **B5 — live smoke + docs:** probe PR1 (both repro shapes) against the operator's herdr;
   CLAUDE.md gotcha line under the CARD-0162 entry (unobservable baseline: transcript-first with
   a wall-clock floor, herdr advance = runner content delta never `revision`, late-confirm covers
   null-baseline attempts, Mode:Now grace before 409 — and the never-weaken rule restated); close
   CARD-0164 with the measured results.

Slices are independently shippable; nothing observable changes for observable-baseline deliveries
at any point, and B1 alone changes herdr behavior only in the direction of signals that exist.
