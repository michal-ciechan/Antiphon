# CARD-0074 — The check digest's CAPTURE TIME: plan

**Date:** 2026-08-19
**Status:** planned (not implemented)
**Card:** CARD-0074 (`20fc420a-0b9f-4bce-bcd0-ac6712149ef5`) — a check-in digest carries no capture
time, so one delivered after its task settled reads as a live status report about finished work.
**Precedent:** CARD-0047 slice 2 (`DelegateCheckProbe` — the digest is the FLOOR), CARD-0055
(delivery confirmation matches the STORED `Body` against the transcript), CARD-0025 (the queue
legitimately rewrites a body before it is typed), CARD-0089 (the digest's content, shipped today
`4eb793d`).
**Do not steal:** CARD-0079 owns the interpreter's availability and the note header's
`INTERPRETER DOWN` marker (shipped `43c9d25`). CARD-0091 owns PARKED `Delegation` messages on
settled tasks, and specifically owns the open question of whether `SessionQueuedMessage` needs a
new terminal status — **this plan must not invent one** (§5).

This is a planning document only. Do not write the fix in the Plan pass.

---

## Verdict

**The card is right, the defect is measurable, and it happens to 1 check note in 6.**

The capture time is not missing from the data — `CheckFacts.At` has existed since CARD-0047
(`DelegateCheckProbe.cs:165`) and is passed to `RenderDigest`. It is simply **never printed**.
Every relative age in the digest is already computed against it (`facts.At - message.CreatedAt`,
`facts.At - incident.CreatedAt`), so the anchor is load-bearing everywhere except in the one place
a reader could see it.

Measured against the live database this evening (40 delivered check notes, all of `Origin = Check`):

| Fact | Value |
|---|---|
| Check notes typed into a caller's terminal **after their task had settled** | **7 / 40 (17.5%)** |
| …of those, settled **before the note was even enqueued** | 3 |
| …of those, settled **while the note sat Pending in the queue** | 4 |
| Staleness at the moment of typing | 32 s – 329 s |
| Enqueue → typed delay, all 40 | p50 **0 s**, p90 95 s, max 348 s |

The card's own case is in that data. Task `74bef32b`: note enqueued 14:54:41Z, task settled
14:56:38Z, note typed 14:58:23Z — **105 seconds stale** when the orchestrator read it. (The card
quotes 15:53/15:57 because it was written in BST; the rows are UTC.)

The 3/4 split is the whole design. **There are two independent windows, and closing either one
alone leaves most of the defect standing:**

1. **The interpreter window** — `GatherAsync` to `EnqueueAsync`, bounded by
   `CheckInterpreterWaitSeconds` (60 s default, `DelegationSettings.cs:408`). `RunCheckAsync`
   already refuses to check a settled task (`CheckOutcome.AlreadySettled`, `:135`) but reads the
   status **once, before** the wait. 3 of the 7 settled inside this window.
2. **The queue window** — `EnqueueAsync` to typed. `MessageSendMode.WhenIdle` means the note waits
   for the caller's turn to end; p50 is 0 s but p90 is 95 s and the tail runs to 348 s. 4 of the 7
   settled inside this window.

Neither is a bug in anything. The digest simply describes a moment it never names.

---

## 1. What to decide — the card's four bullets

### B1. Stamp the digest with its capture time and render it as a snapshot — **Yes. Absolute ISO-8601 UTC, on line 1.**

`facts.At` is already there. The change is in `DelegateCheckProbe.RenderDigest`
(`server/Application/Services/DelegateCheckProbe.cs:360`), which today opens straight into
`TASK <shortid>: <title>`.

Absolute, not relative, and **not** "N minutes before you are reading this": that number cannot be
computed where the body is built. The body is frozen at `EnqueueAsync` and typed an unbounded time
later, so any "ago" rendered at build time is itself a stale fact — precisely the defect being
fixed. Use the full date, not `Clock()`'s `HH:mm:ssZ`: a check note can be read the next morning,
and `14:53:07Z` alone does not say which day.

Line 1 also has to tell the reader what to *do* about a delta it cannot state. Something of this
shape, worded at implementation time:

```
CAPTURED 2026-08-17T14:54:41Z — a snapshot of that moment, not of now. Nothing below updates
after capture; if the delta since then matters, read the task row before acting on it.
```

### B2. Reconcile at delivery — **Yes, and reconcile at BOTH windows. Mark, never suppress.**

The card's wording is "the task's status is a cheap lookup at the moment of send", and the two
windows above are two different moments of send:

- **Interpreter window** — re-read `task.Status` in `RunCheckAsync` after `InterpretAsync` returns
  and before `EnqueueAsync` (`AgentTaskCheckService.cs:151-158`). One `AsNoTracking` lookup on a
  row already in scope. Catches 3 of 7.
- **Queue window** — a sweep over still-Pending `Origin = Check` rows whose task has settled since,
  amending the body in place (§2.3). Catches 4 of 7.

**Marking, not suppressing**, per the card. The stale digest is not deleted and its status is not
changed — a banner is prepended and the digest stays underneath it. "It finished while this was in
flight" is the useful half, and the digest remains the evidence a reader who distrusts the banner
falls back to.

The banner must name the three responses the card identifies as destructive, because naming the
state is not the same as forbidding the reaction:

```
SUPERSEDED — captured 2026-08-17T14:54:41Z, but this task SETTLED at 2026-08-17T14:56:38Z, after
capture. It is now Succeeded. Every status/working/elapsed line below is historical. The
completion note is the current answer — do not chase, cancel or re-dispatch this task.
```

### B3. Should a settled task generate a check at all? — **Already answered in code. Nothing to build.**

`RunCheckAsync` returns `CheckOutcome.AlreadySettled` and delivers nothing when the task settled
between the claim and the run (`AgentTaskCheckService.cs:134-141`), and `RunScheduledChecksAsync`
only ever selects `Dispatched`/`Working` rows (`AgentTaskDispatcher.cs:772`). The card's argument
("the final report supersedes it entirely") is already the shipped behaviour.

The only residual is the 60 s interpreter window, and B2 closes it. Note the deliberate asymmetry
there: a task that settles *before* the check runs produces **no note**; one that settles *during*
the check produces a **superseded** note. That is not an inconsistency to tidy — in the second case
the caller is owed an explanation for a check it was scheduled to receive and would otherwise
silently never get.

### B4. Should the fallback say more loudly that it is a fallback? — **Already shipped by CARD-0079. Do not touch it.**

`AgentTaskCheckService.InterpreterDownMarker` (`:613`) puts a literal `INTERPRETER DOWN` in the
note's **first line**, added by `43c9d25`. The card was filed 2026-08-17, two days before that
landed. The `(unverified digest — …)` body line the card calls skimmable now has a header marker
above it, which is exactly the fix the bullet asks for.

Say so in the commit rather than re-doing it. CARD-0089 already carved the header out as CARD-0079's
territory, and this card must respect the same line.

---

## 2. The slice

**ONE Code slice.** No migration, no new enum member, no new column, no new hosted service.

### 2.1 `DelegateCheckProbe.RenderDigest` — print the anchor

`server/Application/Services/DelegateCheckProbe.cs:360`. Prepend the `CAPTURED` line from
`facts.At` before the `TASK` line, with a blank line after it. Add a `Stamp(DateTime)` helper
beside the existing `Clock`/`Duration` helpers (`:592`) rendering full ISO-8601 UTC.

The class's read-only guarantee is unaffected — this reads a field it already holds and adds no
dependency. Nothing else in the digest changes; CARD-0089's incident ages, `×N` collapse and
queue labels shipped this morning and this slice must not disturb them.

### 2.2 `AgentTaskCheckService` — close the interpreter window, and stamp the header

`server/Application/Services/AgentTaskCheckService.cs`.

- **`RunCheckAsync` (`:127`)** — after `InterpretAsync` returns (`:151`) and before
  `EnqueueAsync` (`:157`), re-read the task's `Status` and `CompletedAt` `AsNoTracking`. If it has
  settled since `GatherAsync`, prepend the superseded banner to `body`. Keep the same enqueue and
  the same `CheckOutcome.Delivered` — this is a note that still goes out, marked.
- **`BuildNote` (`:635`)** — add `captured <stamp>` to the `bits` list, beside the existing
  `elapsed` / `session` bits. First-line skimmability is the whole point of that header; a reader
  who reads only line 1 must see the anchor. **Do not touch `HeaderPrefix`, the
  `InterpreterDownMarker` arm, or the `(unverified digest — …)` line** — CARD-0079's.
- **New `internal static string SupersededBanner(AgentTaskStatus, DateTime settledAt, DateTime capturedAt)`** — the
  one place the banner text is built, so the two callers (here and §2.3) cannot drift.
- **New `public const string SupersededMarker`** — the banner's opening token, used for
  idempotency in §2.3.
- **New `public static bool TryParseCheckConversationKey(string?, out Guid taskId)`** — the inverse
  of the existing `ConversationKey(Guid)` (`:189`), which writes `check:{taskId:N}`. It belongs
  directly beside it so the format has one definition.

The banner is prepended, so it lands inside CARD-0055's 200-char head match window
(`PromptSubmissionMatch.MatchWindowChars`). That is correct and needs no accommodation: the matcher
compares against the **stored** body, and the stored body is the banner plus digest, which is
exactly what gets typed.

**Ceiling.** `BuildNote` already fits the note to
`_ptyProfile?.Ceilings.ReplyInlineMaxChars ?? _settings.ReplyInlineMaxChars` (`:665`). Prepending
~300 characters can cross it, so the banner must be added **before** that fit runs, not after.
Losing the digest's tail to make room is the right trade — the full digest is on the timeline in
`AgentTaskEvent.Detail` regardless (`:169-176`), and the banner is worth more to the reader than
the last block of a snapshot it has just been told is historical.

### 2.3 The queue window — a sweep, and a locked amend

**The sweep** — `AgentTaskDispatcher.ReconcileSupersededChecksAsync(ct)`, added to `TickAsync`'s
clock list via the existing `RunSweepAsync` isolation wrapper
(`server/Application/Services/AgentTaskDispatcher.cs:99-141`). That list is already six
independent clocks with a documented rationale for why each is isolated, it already owns the
check *scheduling* half (`RunScheduledChecksAsync`, `:765`), and it fires every
`Delegation:PollIntervalSeconds` — **5 s**, the default, not overridden in `server/appsettings.json`.
Against observed staleness of 32–329 s, a 5 s cadence catches all four measured queue-window cases.

Query: `SessionQueuedMessages` where `Status == Pending && Origin == Check && DeliveryAttempts == 0`,
parse the task id out of `ConversationKey`, join `AgentTasks`, keep the ones where
`AgentTaskService.IsSettled(status)` and `CompletedAt > ` the note's `CreatedAt`. The working set
is tiny — **zero** pending `Check` rows right now, and 38 of the 40 lifetime rows delivered on
their first attempt.

**The amend** — a new `public Task<bool> AmendPendingBodyAsync(Guid sessionId, Guid messageId, string prefix, int ceiling, CancellationToken ct)`
on `SessionMessageQueueService`, in the **exact shape `CancelAsync` already uses** (`:251-278`):
take `GetLock(sessionId)`, open its own scope, re-check `Status == Pending && DeliveryAttempts == 0`
under the lock, prepend, re-fit to the ceiling, `SaveChangesAsync`, release, then
`PublishQueueChangedAsync` so the queue UI reflects it.

**This must go through the per-session lock, and that is the single most important constraint in
this plan.** A read-modify-write from the sweep's own scope races `FlushAsync`: the flush reads
`head.Body` into memory, spills, then stamps `Sent` + `DeliveryAttempts++` +
`LastDeliveryBaselineSequence` in a later `SaveChanges` (`SessionMessageQueueService.cs:643-694`).
An amend landing inside that gap makes the stored body differ from the typed body, which is
exactly the disagreement CARD-0055's confirmation and CARD-0024's completeness check exist to
detect — so the amend would manufacture a `NoTranscriptRecord` or `Truncated` verdict, and
`HandleDeliveryFailureAsync` can kill an always-on session on the former. The lock closes it
completely, and there is no need to invent a mechanism: `CancelAsync`, `SendNowAsync` and the
flush all already serialise on it.

The `DeliveryAttempts == 0` guard is the second half of the same safety. A row that has been typed
once carries a `LastDeliveryBaselineSequence` and is subject to `LateConfirmAttemptedMessagesAsync`
(`:596`), which re-runs the matcher over the **stored** body; amending it after the fact would make
late-confirm search the transcript for banner text that was never typed, fail, and re-type the
message. Cost of the guard, measured: 2 of 40 check notes ever reached a second attempt.

`AmendPendingBodyAsync` takes a string and a ceiling. It learns nothing about tasks — the sweep
decides *what* to say, the queue only applies it safely. No layering is inverted.

---

## 3. Why not the alternatives

- **Rewrite the body inside `FlushAsync`.** The card's literal "at the moment of send". Rejected:
  it puts task-status knowledge inside the queue's hot path, and that path is the most
  safety-critical code in the repo (CARD-0055, CARD-0024, CARD-0025 all live in those forty lines).
  The lock gives the same atomicity from outside.
- **Hook the settle paths directly.** There is no single funnel: `AgentTaskStatus.Succeeded` is
  assigned at three sites in `AgentTaskReplyService` alone (`:387`, `:540`, `:925`), with `Failed`
  and `Canceled` elsewhere. Three-plus hooks that each must be remembered by future work, versus
  one sweep that cannot be forgotten.
- **Suppress the superseded note.** The card rules it out explicitly, and it is the wrong default:
  a caller that scheduled a check and silently receives nothing learns less than one that is told
  the task finished first.
- **Render "N minutes before you read this."** Not computable at build time (§B1). Refreshing it
  from the sweep would mean rewriting every pending check body every 5 s — churn on the same rows
  the flush is contending for, for a number that is stale again immediately.
- **A new `Superseded` status on `SessionQueuedMessage`.** That is CARD-0091's open question,
  verbatim, and it has 21 rows of its own evidence to decide it against. Prejudging it here would
  bind that card to a decision made without its data.

---

## 4. Verification

`tests/Antiphon.Tests/Application/`. Both existing classes already carry the right parallelism
attributes — `DelegateCheckProbeTests` is `[NotInParallel("AgentQueue")]`, `AgentTaskCheckSweepTests`
is bare `[NotInParallel]` (correct: it drives a global sweep — see the CLAUDE.md rule that a keyed
group only serialises against itself).

1. `DelegateCheckProbeTests` — the digest's first line carries `facts.At` as full ISO-8601 UTC, and
   a fixed `TimeProvider` makes it exact. Assert the CARD-0089 blocks are untouched.
2. `AgentTaskCheckSweepTests` — a task that settles between `GatherAsync` and `EnqueueAsync` still
   enqueues, and the enqueued body opens with the marker naming the settle time. The 3/7 case.
3. `AgentTaskCheckSweepTests` — a Pending check note whose task settles afterwards is amended by
   the sweep; run the sweep twice and assert the banner appears **once**. The 4/7 case plus
   idempotency.
4. `AgentTaskCheckSweepTests` — a check note with `DeliveryAttempts == 1` is **not** amended.
5. `AgentTaskCheckSweepTests` — a check note whose task is still `Working` is not amended.
6. A body already at the ceiling still fits after the banner is prepended, and the banner survives
   while the digest tail is what gets trimmed.

Scope assertions to the rows the test created (shared Postgres testcontainer — never assert a
global count).

Run: `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card74/` (forward slash),
filtered to the two classes. Delete the `bin-card74/` directories afterwards. Do not co-schedule
with `Antiphon.Agents.Pty.Tests`.

---

## 5. Out of scope

- **CARD-0091**: PARKED `Origin = Delegation` messages on settled tasks, and whether
  `SessionQueuedMessage` needs a status beyond `Pending`/`Sent`/`Canceled`. Disjoint data — that
  card's own debug pass established all 21 parked rows are `Delegation` with a NULL
  `ConversationKey`, and there are zero parked `Check` rows. Adjacent mechanism, though: if
  CARD-0091 later lands a general "queued message whose task is over" sweep, this card's
  reconcile is its natural special case and should be folded in rather than duplicated.
- **CARD-0079**: the interpreter's availability, the `INTERPRETER DOWN` marker, `HeaderPrefix`,
  the `(unverified digest — …)` line. B4 is already shipped.
- **CARD-0089**: the digest's content — incident ages, the `×N` collapse, tool inputs, queue
  labels. Shipped this morning; this slice adds a line above it and changes none of it.
- The check schedule, the check budget, and `CheckSchedule.NextInterval`.
- The client. `CardThreadCheckDto` already carries the event's own `At`; a stored digest gains its
  capture line for free through `ComposeEventDetail`.

---

## 6. Commit

`fix(delegation): CARD-0074 - stamp the check digest with its capture time and mark a superseded one`
