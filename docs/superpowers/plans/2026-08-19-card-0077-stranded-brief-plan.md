# CARD-0077 — A brief that was never enqueued, and a watchdog that cannot see it: plan

**Date:** 2026-08-20 (filed under the 2026-08-19 card sequence)
**Status:** planned (not implemented)
**Card:** CARD-0077 (`d928ecf1-a23f-42b8-9433-192ad02c8b4c`) — a task handed to a reused session
never gets its brief, and the stranded-brief watchdog cannot see it happen.
**Duplicate pair:** CARD-0029. Resolved below on evidence — same symptom, **different mechanism**,
and CARD-0029's mechanism is already closed by CARD-0041.
**Precedent:** CARD-0003 / CARD-0020 (`FailNeverStartedAsync`, the watchdog this defeats),
CARD-0041 (a manual `CompactBoundary` is a turn END; the raw `/compact` echo and the continuation
prompt are not activity), CARD-0055 (a pre-write sequence baseline as the shape for "since when"),
CARD-0056 (boot confirm past a baseline, with a clock tolerance), CARD-0079 (standing-agent
occupancy — read and checked, unrelated), CARD-0085 (`TryRecoverBindRefusalAsync`, the gate that
keeps this watchdog from killing work it cannot see), CARD-0091 (reuse-compact parking on Grok).
**Evidence:** live Postgres (`antiphon` on 17280) and the current tree, queried 2026-08-20
02:00–03:10Z. 73 reuse dispatches all-time.

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**The card's first bullet is already true in code; its second bullet is the whole defect.**

| Question the card asks | Answer, on this evidence |
|---|---|
| Does the reuse path enqueue the work prompt? | **Yes, and always has.** `DeliverReuseMessagesAsync` enqueues the `/compact` *and* the brief, and has done since the pool shipped (`2daa5a0`); the function has never been edited. **70 of 73** reuse dispatches all-time have a brief row. |
| Did CARD-0079 already close this? | **No.** Its two commits (`3da054c`, `43c9d25`) changed `PlaceOnStandingAgentAsync` occupancy, `AgentControlService` session re-pointing, the `Check` filter in `FailDeadSessionTasksAsync`, and added `CheckInterpreterUnavailable`. Neither `DeliverReuseMessagesAsync` nor `FailNeverStartedAsync` is touched by either. |
| Is CARD-0029 the same defect? | **No — and CARD-0029 is already fixed.** Its brief *was* enqueued (row exists, seq 3) and sat Pending for **45.5 hours** because the manual `/compact` left the session reading Working forever. That is CARD-0041, shipped `3319a37` (2026-08-13 08:19). Its own brief went out at 08-13 08:21. |
| Is the "never enqueued" bug live? | As a **hazard**, yes. As an event, 3 in 73 — and 2 of those 3 are Check tasks inside CARD-0079's documented interpreter outage. One non-Check occurrence exists: `a0914129`, the card's own case. |
| What is structural and reproducible today? | **The watchdog.** `FailNeverStartedAsync` gates on `TranscriptEntries.AnyAsync(t => t.AgentSessionId == sessionId)` — session-scoped, so a reused session is *never* "never started". Confirmed by reading `AgentTaskDispatcher.cs:374`. |
| Should zero tokens raise an incident? | **No.** `AgentTask.TokensIn` is written only at settlement (`AgentTaskReplyService.cs:397`) and by the cost backfill. Every Dispatched task reads 0 — including the healthy one running as I write this. It is the default, not a signal. |
| Should a stranded task release its scope? | **Not as its own mechanism.** Fixing the watchdog bounds the hold to `DeliveryFailTimeoutMinutes` (10). |

## 1. What the data says

### 1.1 The reuse census

Every dispatch that logged `Reused warm delegate …` or `Delivered into standing agent …`, joined to
`SessionQueuedMessages` on the same session for a body carrying that task's own marker:

| | Count |
|---|---|
| Reuse dispatches all-time | 73 |
| With a brief row | 70 |
| **Without a brief row** | **3** |
| …of which non-Check | **1** (`a0914129`) |

The three misses:

- **`a0914129`** — the card's case. Merge/Low, 2026-08-17 15:02:45Z, session `e55b3b86`. Compact row
  enqueued and Sent; **no brief row**; `TokensIn/TokensOut = 0`; Canceled 15:29:57Z.
- **`1d407857`** and **`f978e957`** — both `Role = Check`, both inside CARD-0079's documented
  interpreter outage (08-16 20:02 → 08-17 17:54). `1d407857`'s session `1cb2fadb` is Failed with
  `FailureReason = "Session runner does not know this session (launch failed or the runner
  restarted)."` **Explained by CARD-0079, which is Done.** A Check task enqueues no `/compact`
  (deliberately — `DeliverReuseMessagesAsync` excludes it), so its brief is the *first* enqueue, and
  these two are the same throw-before-any-row shape as `a0914129` with nothing before it.

So: after CARD-0079, **exactly one unexplained reuse dispatch remains in the whole history**, and it
is the one the card was filed on. Nothing like it has happened in the 40 reuse dispatches since.

### 1.2 CARD-0029 is a different bug, and it is closed

`74f4de94`'s three queue rows on session `e77fb0a7` settle it:

| Seq | Created | Sent | Body |
|---|---|---|---|
| 1 | 08-11 10:45:47 | 10:45:56 | `[antiphon-task:530ec4aa]` (the previous task) |
| 2 | 08-11 10:53:09 | 10:53:09 | `/compact This session is being handed NEW, unrelated work…` |
| 3 | 08-11 10:53:10 | **08-13 08:21:33** | `[antiphon-task:74f4de94]` |

The brief **was** enqueued, 1.2 s after the compact — CARD-0029's lead 2 ("check the queued-message
row: still pending, or marked delivered?") was never run, and the card's inference that "the brief
was assembled and enqueued, and the delegate never received it" is correct but its conclusion is
not. It stranded **Pending for 45.5 hours** because the session read Working forever after the
manual `/compact`: CARD-0041's live miss, dated the same day, fixed in `3319a37`. The task then
Succeeded at 08-13 08:25 on its own.

Delivery latency for every reuse brief since is bounded: the worst case in 9 days and 70+ dispatches
is 26 minutes (`6a6b6e95`, 3 attempts); the median is ~165 s, which is just the compact delivering
first. The single unsent one is `49f11348` — CARD-0091's parked brief typed into a usage-capped
Claude corpse, a third distinct thing.

**Recommendation for the duplicate pair:** keep **CARD-0077**, close CARD-0029 as *fixed by
CARD-0041* with a pointer to this section. They are not duplicates; CARD-0029 simply predates the
fix for what it actually was.

### 1.3 The transcript that proves the watchdog is blind

Session `e55b3b86`, every record from the previous task's last turn end onward. Dispatch of
`a0914129` was 15:02:45Z:

| Seq | Kind | Timestamp | What it is |
|---|---|---|---|
| 82 | `TurnEnd` | 14:53:31 | end of the **previous** task |
| 83 | `UserPrompt` | 15:04:31 | the raw typed `/compact This session is being handed NEW…` |
| 84 | `CompactBoundary` | 15:05:15 | `Context compacted (manual)` |
| 85 | `UserPrompt` | 15:05:06 | the synthetic continuation prompt |
| 86 | `UserPrompt` | 15:04:31 | the `<command-name>/compact</command-name>` wrapper |
| 87 | `UserPrompt` | 15:05:15 | `<local-command-stdout>Compacted` |

There is no seq 88. The session had **87** entries when the watchdog looked, so
`TranscriptEntries.AnyAsync(...)` was `true` and the `!started` branch — with all of CARD-0085's
recovery and all of the `briefStatus` evidence text inside it — was unreachable. The second branch
needs a `DelegateReportUncorrelated` incident, which needs a report, which needs a brief. So the
loop `continue`d, every 5 s, for 27 minutes, until a human cancelled the task.

Note also that seqs 83–87 are **five records created after `DispatchedAt`**. A naive "any entry past
a dispatch baseline" test would see them and call the task started. The baseline alone is not
enough — which is the next section.

## 2. The predicate already exists, and settlement already uses it

`AgentTaskReplyService.LoadPromptsInSpanAsync` (`:1435`) already answers exactly the question the
watchdog needs to ask, on exactly the axis it needs:

```csharp
.Where(t => t.AgentSessionId == sessionId
    && t.Kind == TranscriptKinds.UserPrompt
    && (dispatchedAt == null || t.Timestamp == null || t.Timestamp > dispatchedAt))
…
rows.Where(r => !IsHousekeepingPrompt(r.Text, invoked))
```

and `IsHousekeepingPrompt` (`:1464`) drops all four shapes:
`IsLocalCommandRecord` (seq 86, 87), `IsCompactionContinuationPrompt` (85),
`IsRawLocalCommandEcho` (83 — matched against the command names actually read out of wrappers in the
same span, so a real prompt beginning with a slash is not caught), and `IsTaskNotificationPrompt`.

Applied to `e55b3b86` past `DispatchedAt`, that yields **zero** turn prompts. That is the signal, and
it is computed by code settlement already trusts.

Two properties make this the right test rather than a new judgement:

- **It degenerates to today's behaviour on a fresh session.** A brand-new session has no records at
  all, so it has no non-housekeeping prompt past dispatch either. One predicate, both paths — which
  is what the card means by "a dispatch-time baseline is the general fix".
- **It fails in the safe direction.** A brief whose marker was mangled in delivery (the shape that
  produced `DelegateReportUncorrelated`) still writes a `UserPrompt` and still counts as started, so
  the fix cannot kill a delegate that is genuinely working. Matching the task's *marker* would be a
  sharper test and a worse one: this branch **fails the task and kills the session**, and a false
  positive there kills live work.

`IsRawLocalCommandEcho`'s doc comment says "SETTLEMENT ONLY. No working/idle rule may consume it."
The watchdog is not a working/idle rule — it never asks whether the session is working, only whether
this task ever began — so it is inside that scope, not an exception to it. Worth a line in the
comment so the next reader does not have to re-derive it.

## 3. The enqueue hazard, and why its cause is unrecoverable

`DeliverReuseMessagesAsync` (`AgentTaskDispatcher.cs:1635`) awaits two `EnqueueAsync` calls under a
single `try`. The first one is not cheap: `SessionMessageQueueService.EnqueueAsync` **delivers
inline** when the session is live, accepting input and not working (`:219` →
`DeliverNextLockedAsync`), which on a reused idle delegate is always true. On `a0914129` that call
occupied **106 seconds** (compact `CreatedAt` 15:02:45 → `SentAt` 15:04:31). Everything that can
fail in those 106 seconds sits between the compact row and the brief row, and there are two exits,
both silent:

- **A non-OCE throw** is caught, and the log line says
  *"reuse messages are queued but could not be delivered yet"* — which is false in exactly this case.
  The comment above it ("rows are persisted before the runner is probed") is the *spawn* path's
  contract; this function does not hold it.
- **An `OperationCanceledException` escapes the filter entirely** (`when (ex is not
  OperationCanceledException)`). Per CLAUDE.md's own standing rule, an `HttpClient` timeout arrives
  as a `TaskCanceledException` with nothing actually cancelled — and this path makes runner HTTP
  calls (`IsAcceptingInputAsync`, the write). It then passes through `TickAsync`'s identically
  filtered catch and reaches the hosted service as a bare warning naming no task.

I cannot name which of the two fired on 08-17: the server logs for that day have rotated
(`logs/antiphon-*.log` starts at 08-18) and no incident is raised on either exit. **That is part of
the defect, not a gap in this investigation** — a path that loses a delegate's entire brief leaves
no durable evidence of having done so.

The fix is not a third enqueue call. It is to hold the spawn path's contract the comment already
claims: **persist both rows, then flush once.** `FlushSessionAsync` (`:578`) exists for precisely
this and is what the launch path calls after boot; `FlushStrandedQueuesAsync` (`:468`) explicitly
covers delegation briefs on non-always-on sessions as the backstop. Ordering is preserved by
`Sequence`, and the `/compact`-then-brief hazard the card raises is already handled: the queue holds
a per-session lock, delivers one message at a time, and CARD-0041 makes the manual boundary a turn
end so the brief's `WhenIdle` gate opens. CARD-0091 measured the failure mode of that ordering on
Grok (the compact parks after 3 attempts, the brief behind it delivers on the first) — parked rows
are skipped, so a stuck compact does not block the brief either.

## 4. Slice S1 — the watchdog learns "since this task was dispatched" (Code)

One slice. Tier: **sonnet**, standalone, `scopeGlob: server`.

1. **`FailNeverStartedAsync` (`AgentTaskDispatcher.cs:354`)** — replace the `started` test. Instead
   of `TranscriptEntries.AnyAsync(t => t.AgentSessionId == sessionId)`, ask whether the session has
   at least one **non-housekeeping `UserPrompt` with `Timestamp > task.DispatchedAt`**, using the
   same predicate set as `IsHousekeepingPrompt`. Lift that helper (and the `invoked` command-name
   collection it needs) somewhere both services can reach it rather than copying it — a second copy
   of a four-way judgement is a second chance to disagree, which is the reason CARD-0041's rules are
   described as lockstep in the first place.
2. **Keep both existing branches intact.** CARD-0085's `TryRecoverBindRefusalAsync` gate stays first
   inside `!started` and now covers reused sessions too, which it should. The `briefStatus` evidence
   text stays and gets one more arm worth having: `null` currently reads *"no brief was ever queued
   for the session"*, which on a reused session is the wrong sentence — scope that lookup to rows
   created at/after `DispatchedAt` so it says the true thing about *this* task.
3. **Clock tolerance, mirroring CARD-0056.** Stored sequences are arrival-ordered and backfill
   rebases late entries past the session max, so a sequence baseline can be leapfrogged; record
   timestamps survive reordering. Use the timestamp comparison (as `LoadPromptsInSpanAsync` already
   does), treat a null timestamp as "counts as started", and do **not** add a column — there is no
   migration in this slice.
4. **Tests** (`AgentTaskDispatcherTests` / the delivery-watchdog fixtures): a reused session whose
   only post-dispatch records are the five from §1.3 fails at the timeout and names the reuse; the
   same session plus one real `UserPrompt` does not; a fresh session with zero entries still fails
   exactly as today; a session with a `DelegateReportUncorrelated` incident still takes the second
   branch; the CARD-0085 recovery gate still short-circuits before anything destructive.

Optionally in the same slice, since it is four lines and the same file: **split
`DeliverReuseMessagesAsync`'s try so the brief's enqueue cannot be skipped by the compact's
failure**, and correct the log line that currently asserts the messages were queued. If the reviewer
would rather keep the slice to one behaviour, this is the natural S2 — but it is the half that
*causes* the strand, and S1 only makes the strand visible within 10 minutes.

## 5. Deliberately not in scope

- **A zero-token incident.** `TokensIn` is settlement-only; on a Dispatched row it is always 0. An
  incident on that would fire on every healthy task in flight.
- **A scope-release mechanism.** The lane block is real (`a0914129` held `merge,deploy` for 27
  minutes and `TickAsync` reads `ScopeGlob` off every Dispatched/Working row), but it is a
  *consequence* of an unbounded strand. With S1 the strand is bounded by
  `DeliveryFailTimeoutMinutes` = 10, and `FailAsync` releases the scope with the row. Building a
  separate release would add a second authority over the same lock for a window that no longer
  exists. Revisit only if a strand is ever observed that S1's test cannot see.
- **Any change to warm-pool eligibility** (CARD-0029's lead 3 — whether a delegate that just
  reported a failure should be reusable). Real question, different card.
- **The parked reuse-compacts.** CARD-0091 owns that population and its sweep.
- **Grok's missing `/compact` UserPrompt.** CARD-0091 §2 established it; it is why reuse-compacts
  park, and it does not affect this test (the brief's own prompt is what S1 looks for).

## 6. Card housekeeping

- CARD-0029 → close as fixed by CARD-0041, citing §1.2. CARD-0077 survives the pair.
- CARD-0077's first bullet ("enqueue the work prompt on the reuse path") should be re-worded to what
  §3 found — the enqueue exists; the failure window between the two enqueues is what loses it.
- CARD-0077's third bullet (zero tokens) should be struck with the reason in §5, so it is not
  re-proposed.
