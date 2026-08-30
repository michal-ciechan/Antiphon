# CARD-0264 — Completion notes stranded 33 minutes on the orchestrator's own idle session

**Date:** 2026-08-31 (investigating the 2026-08-30 21:47–22:28Z incident on session `cefed08a`)
**Status:** Root cause found and fully evidenced. Plan only — nothing built yet.

## One-line root cause

A single transcript record lost during the orchestrator's own AppHost restart was backfilled
21 minutes later with an old timestamp but a rebased top-of-session sequence, and
`IsWorkingAsync`'s timestamp override cannot see it — the override compares the **maximum**
activity timestamp (which the turn's final `AssistantText` pins to the exact same instant as its
`TurnEnd`, and equal timestamps keep the sequence verdict) — so the genuinely idle session read
**working** for 33 minutes and every WhenIdle flush path, the stranded-queue watchdog included,
refused to deliver the two `[task done]` notes.

This is a new variant of the known backfill-reordering strand (2026-08-08, the shape the
timestamp override was built to fix). The override is defeated whenever (a) the last turn ended
properly (its `AssistantText` and `TurnEnd` share one timestamp — they come from the same JSONL
line, so this is *always* true of a clean turn end) and (b) at least one backfilled stale record
lands above the `TurnEnd` by sequence. Condition (b) is exactly what any server restart
mid-conversation can produce.

## Evidenced timeline (all times UTC; log file is +01:00)

Sources: `server/logs/antiphon-20260830.log`, `TranscriptEntries` / `SessionQueuedMessages` rows
in Postgres, and the raw JSONL at
`~/.claude/projects/C--src-Antiphon/cefed08a-fd4a-42a0-8c76-0fbf82cf6b20.jsonl`.

| Time (Z) | Event |
|---|---|
| 21:01:40 / 21:10:59 | bd9ef38f (CARD-0245) and caca6ad5 (CARD-0250) dispatched, grok-4.6 |
| 21:20:16, 21:27:04 | Server restarts twice — the orchestrator itself ran `restart-apphost.ps1` mid-turn (deploying CARD-0256 work) |
| 21:27:05.996 | Boot catch-up backfills through `ToolCall` seq 26362 (ts 21:25:53 — the restart command itself). Its `ToolResult` is written to the JSONL at **21:27:06.553** — in the crack between the catch-up query and the live subscription. Live streaming resumes at 21:27:15 (seq 26363). **One record is now missing from the DB.** |
| 21:32:03.954 | Orchestrator's turn ends cleanly: `AssistantText` seq 26405 and `TurnEnd` seq 26406, **both stamped 21:32:03.954** (same JSONL line pair; raw record has `stop_reason: "end_turn"`). Session genuinely idle from here — the transcript is empty until 22:21. |
| 21:46:42 | Check #1 on bd9ef38f captures its digest (interpreter down; degraded 21:47:43). Note: the digest was only 6 min stale at capture-vs-settle — the "over an hour earlier" in the incident report was a Z-vs-BST misread. |
| 21:47:43–44 | Digest (2,430 chars, Origin=Check, WhenIdle) enqueued; the enqueue-time flush sees **idle** (correct at that instant) and **types the body into the composer**. |
| **21:48:01.576** | The delivery's evidence-failure recovery runs `CatchUpTranscriptAsync` — which backfills the missing 21:27:06 `ToolResult` as **seq 26407**, above the `TurnEnd` at 26406 (arrival-order rebase, by design). `IsWorkingAsync` now: activity maxSeq 26407 > end maxSeq 26406 → "working"; override: max activity ts (21:32:03.954, from `AssistantText` 26405) **equals** end ts → sequence verdict stands → **working=true**. The pull that was meant to rescue the delivery *created* the false verdict. |
| 21:48:01.8 | "Overlay dismiss withheld … session is working after transcript pull"; verdict `NoComposerEvidence`, **Enter withheld — 2,430 chars of digest left standing in the live composer.** |
| 21:52:11–12 | caca6ad5 settles Succeeded; `[task caca6ad5 done]` (2,943 chars, Origin=Delegation, WhenIdle) enqueued. Check #2 row canceled at settle. |
| 21:52:32 | bd9ef38f settles Succeeded; `[task bd9ef38f done]` (3,134 chars) enqueued. Check #1 row also canceled (superseded) — **but its typed text stays in the composer; cancellation is queue bookkeeping only.** |
| 21:52 → 22:21 | Nothing delivers. Every flush trigger — turn-end (none coming), enqueue-time, and the stranded-queue watchdog (which **does** cover Delegation-origin rows, `FlushStrandedQueuesAsync`) — gates on `!IsWorkingAsync` (`SessionMessageQueueService.cs:701`). Both brakes share the one poisoned dependency. No attention row exists for this shape. |
| 22:21:01 | The **user** asks "How is 245 and 250 going?" — the orchestrator polls `GET /api/agent-tasks` and finds both Succeeded. |
| 22:25:28 | First turn end since 21:32 → flush → types the caca6ad5 note **after the orphaned digest** → Enter submits both fused as ONE 5,373-char user prompt (2,430 + 2,943 exactly; stored as `UserPrompt` seq 26456). Head-window verification fails (the record starts with the digest); the contains-match late-confirm marks it Sent at 22:26:03. This is the "stale check-in bundled with a re-send of caca6ad5's report" artifact. |
| 22:27:20 | Next turn end → `[task bd9ef38f done]` delivered clean, Sent 22:27:52. |

## Hypotheses from the brief

1. **Session-busyness starvation — REFUTED.** The opposite: the orchestrator was completely idle
   from 21:32 to 22:21. The starvation was a false *working* verdict, not a busy composer.
2. **Grok quota pressure — REFUTED as a cause.** Both tasks ran ~50 min against a 45 min estimate
   and settled normally at 21:52; the entire delay was queue-side, after settlement.
3. **Attention coverage — CONFIRMED GAP.** `AttentionService` surfaces `ParkedMessage` (attempts ≥
   max) and `BriefUndelivered` (Delegation *briefs* on the delegate's session), but a
   Delegation/Check note Pending on the **caller's** session with < max attempts has no signal at
   any age. Follow-up candidate below.

## Primary fix: make the working override row-correlated

`SessionMessageQueueService.IsWorkingBatchAsync` (server, `:2599`) and `isWorking()`
(`client/src/features/agents/transcriptModel.ts:101`) both compare `Max(activity.Ts)` against
`Max(end.Ts)` — group maxima that are uncorrelated with the sequence comparison. Replace the rule
with an **exists** formulation:

> The session is working iff there exists an activity row with `Sequence > lastEndSeq` whose
> timestamp does **not** prove it predates the last end (`Ts == null || Ts >= lastEndTs`).

Why this is right in every known shape:

- **This incident:** the only row above the end is seq 26407 with ts 21:27:06 < 21:32:03 → proven
  stale → idle. ✔
- **2026-08-08 (8 backfilled tool records above the TurnEnd):** all have older timestamps → idle. ✔
- **Same-line pair (`AssistantText` + `TurnEnd`, equal ts):** the text's seq is below the end's →
  never a candidate → idle. ✔ (This is the case the old "equal keeps the sequence verdict"
  comment protected; it needs no timestamp tiebreak at all once the comparison is per-row.)
- **Genuinely working:** any real post-end record has both a higher sequence and a ts ≥ end ts →
  working. ✔
- **Mixed (real new activity + one stale backfilled row above it):** the old rule was right here
  and a naive "use the max-seq row's ts" fix would read false-idle; the exists rule still finds
  the real row → working. ✔ (This is why the fix must be `Any(...)`, not "timestamp of the
  newest-by-sequence row".)
- **Null timestamps** (TurnTitle-only in practice, excluded from activity anyway): cannot prove
  staleness → count as working, preserving today's "missing never overrides" semantics.

Scope of change:

- `server/Application/Services/SessionMessageQueueService.cs` — `IsWorkingBatchAsync`. EF shape:
  keep the `end` per-session query (needs `Max(Sequence)` and the ts of that end — note today it
  takes `Max(Timestamp)` over ends too; for ends this is safe to keep since a later end only
  raises the bar); second query becomes a grouped `Any`-equivalent (e.g. count of activity rows
  with `Sequence > endSeq && (Timestamp == null || Timestamp >= endTs)` joined per session, or a
  two-pass: fetch end pairs, then one `Where` over activity with per-session predicates).
  Watch EF translation cost — this runs on hot paths and in batch.
- `client/src/features/agents/transcriptModel.ts` — same rule in `isWorking()` (single pass:
  track `lastEndSeq`/`lastEndTs`, then a second pass or running check for any qualifying
  activity row).
- **Runner `TranscriptWorkingState` stays untouched** — its mirror is file-ordered and
  deliberately has no override (documented in AGENTS.md).

Tests to pin (all recompute over stored rows, so the fix is retroactive by construction):

- `SessionMessageQueueServiceTests` / working-state tests: the incident shape — clean turn end
  (`AssistantText` + `TurnEnd` sharing one timestamp), then ONE backfilled activity row with
  higher sequence and older timestamp → **idle** (red today).
- The mixed shape: same, plus a genuinely newer activity row → **working** (guards against the
  naive max-seq-row fix).
- Existing 2026-08-08 backfill tests and the client `isWorking` suite must stay green; add the
  incident shape to the client tests too (lockstep rule).

## Secondary findings — file as follow-up cards, not part of the primary fix

1. **Canceled-but-typed composer orphan.** Check #1's body was typed, Enter withheld
   (`NoComposerEvidence`), then the row was canceled at settle — leaving 2,430 chars nobody
   tracked in the live composer. The next flush's Enter submitted it fused with the caca6ad5
   note. CARD-0055's "the per-session queue lock guarantees nothing else is standing in the
   composer" assumption does not survive cancellation of a typed-but-unsubmitted message.
   Candidate direction: on cancel (or before the next delivery), detect a prior attempt that
   typed without submitting (`LastDeliveryStartedAt` set, never confirmed, verdict withheld
   Enter) and clear-or-submit the composer first; clearing must be careful not to eat
   operator-typed text.
2. **Attention row for aging undelivered caller notes.** A Delegation/Check note Pending on the
   caller session past ~10 min deserves an attention row (shape: `BriefUndelivered`'s sibling).
   It would have surfaced this incident 20 minutes before the user's question did.
3. **The event-pump catch-up/subscribe crack.** The record was lost because the boot catch-up ran
   at 21:27:05.996 and the live subscription only saw records from 21:27:13+; anything written in
   between vanished until the next explicit pull. Subscribe-then-catch-up (or a second catch-up
   after the subscription is live) closes the class at its source. Worth its own card — backfill
   rebasing is by design and load-bearing, but every record that avoids backfill is one less
   chance to hit ordering bugs.
4. **Cosmetic:** `AgentTaskCheckService` logs "Check #N … delivered" on enqueue even when the
   delivery attempt failed (`Check #1` logged "delivered" one second after `NoComposerEvidence`).
   And the check digest's `NoComposerEvidence` despite the text demonstrably reaching the
   composer (proven by the later fusion) is unexplained — an overlay was on screen (the overlay
   recovery path fired) and its dismissal was withheld by the same false working verdict; if
   NoComposerEvidence-on-idle recurs after the primary fix, investigate the overlay/placeholder
   evidence path separately.

## Rejected explanations

- Not the channel-reply path (CARD-0067/0233) — no channel rows involved; pure
  Delegation/Check-origin queue traffic.
- Not CARD-0055/0247 single-message confirmation — those mechanisms all *worked* (late-confirm
  correctly rescued both notes once delivery finally ran).
- Not a slow flush cadence — the stranded watchdog swept throughout and correctly covers
  Delegation origin; it was vetoed every sweep by the shared `IsWorkingAsync` verdict.
