# CARD-0248 — fix plan: gate settle-anyway on boundary identity, nudge delivery, and a swept-boundary watermark

**Plan pass, 2026-08-31 (task 49728994). Design only; nothing here is built.**

Root cause (diagnosed, not re-litigated here — see
[docs/investigations/2026-08-31-card-0248-nudge-eaten-by-sweep-diagnosis.md](../../investigations/2026-08-31-card-0248-nudge-eaten-by-sweep-diagnosis.md)):
`SettleDeferredReportsAsync`'s predicates are monotonic and the sweep keeps no record of which
turn boundary it already handed to settlement, so it re-fires every `PollIntervalSeconds` (5 s) on
the SAME unchanged boundary. Tick N nudges (`ReportNudgedAt` set); tick N+1 sees `ReportNudgedAt`
non-null and settles — even though the nudge, riding `MessageSendMode.WhenIdle`, had not even been
typed yet (12 m 45 s later in the incident). The settle-anyway contract — "we asked once and it
ended ANOTHER turn unmarked" — is structurally unachievable on a 5-second re-entrant sweep.

Out of scope, deliberately: the phantom-TurnEnd trigger (claude-fable-5 emitting
`stop_reason:"end_turn"` on records that also carry `tool_use`) is **CARD-0282** and has its own
plan. This plan makes settlement robust to *any* re-entry on an unchanged boundary, phantom or
real; where a residual hole depends on phantom boundaries specifically, it is named below and
assigned to CARD-0282 rather than patched here.

## The contract being restored

`ClassifyReportAsync`'s settle-anyway branch (`AgentTaskReplyService.cs:1808-1818`) claims, per
CARD-0159: *"an unmarked live session is nudged once; a second unmarked end settles."* After this
fix that sentence is true by construction:

> A live task settles without a marker only when (1) it was nudged, (2) the nudge was **actually
> typed** into the session (`SessionQueuedMessages.SentAt` non-null), and (3) the turn boundary
> being settled on is a **different, later** boundary that was stored **after** that delivery —
> i.e. the delegate had the nudge in hand and still ended a turn without the closing line.

A dead session (`!sessionLive`) keeps today's behavior: settle without nudging — there is nobody
left to ask.

---

## Component 1 — settle-anyway requires a different boundary than the one nudged

### Schema

Two new nullable columns on `AgentTask` (one EF migration, no backfill — null on every existing
row means "legacy nudge, no boundary recorded", handled below):

```csharp
/// <summary>
/// Transcript Sequence of the TurnEnd boundary the one nudge (CARD-0159) was issued against
/// (CARD-0248). Settle-anyway requires the current boundary to be LATER than this one — the
/// contract is "asked once and it ended ANOTHER turn unmarked", and before this column the
/// same boundary re-entering through the 5 s sweep satisfied it.
/// </summary>
public long? ReportNudgedSequence { get; set; }

/// <summary>
/// The SessionQueuedMessages row carrying the nudge (CARD-0248). Settle-anyway also requires
/// that row's SentAt to be non-null: a WhenIdle nudge can sit queued for many minutes while
/// the delegate is genuinely mid-turn, and settling before it is typed answers a question
/// that was never asked.
/// </summary>
public Guid? ReportNudgeMessageId { get; set; }
```

**Why `Sequence` and not `ApiCallId`:** `Sequence` is per-session, monotonic, always present, and
totally ordered — which the predicate needs, because the test is not "different" but **"strictly
later"** (`currentBoundary.Sequence > ReportNudgedSequence`). `ApiCallId` can be null on some
boundary kinds, is shared by every record of one API response, and supports only equality.
Transcript sequences are append-only per session (every reader in the codebase orders by them;
compaction appends housekeeping records rather than rewriting history), so a stored sequence stays
valid for the life of the session. Warm-pool session reuse is safe: the columns are per-task, and a
task's `AgentSessionId` is fixed while it is Dispatched/Working, so the comparison never crosses
sessions.

### Threading the boundary into `ClassifyReportAsync`

`TurnOutcome` (AgentTaskReplyService.cs:1351) currently carries no boundary identity on the paths
that reach classification. Add one nullable field:

```csharp
private readonly record struct BoundaryFacts(long Sequence, DateTime CreatedAt);

private readonly record struct TurnOutcome(
    string? Report,
    bool UncorrelatedReport,
    bool DeferredForFinalMessage = false,
    bool FinalMessageMissing = false,
    int NarrationDiscardedChars = 0,
    int AbandonedSubagents = 0,
    ApiErrorStubFacts? ApiErrorStub = null,
    InterruptedFacts? Interrupted = null,
    BoundaryFacts? Boundary = null)
```

`ExtractMarkedTurnAsync` populates `Boundary = new(end.Sequence, end.CreatedAt)` on every return
constructed after `end` is resolved (the FinalMessageMissing returns at :1513-1516, the
final-message return at :1530, and the joined fallback at :1536-1538). The static `Nothing` /
`Deferred` values keep `Boundary = null`; neither reaches `ClassifyReportAsync`. `CreatedAt` (the
store time, never the record's backdated `Timestamp` — same CARD-0046 rule the sweep already
follows) is needed by component 2's "stored after delivery" gate.

`ClassifyReportAsync` treats `turn.Boundary is null` on the settle-anyway path defensively: log a
warning and return null (stay Working) rather than settle on a boundary it cannot identify.

### `NudgeForClosingLineAsync` changes

Signature gains the `TurnOutcome` (or just `BoundaryFacts`), and the method:

1. Sets `task.ReportNudgedSequence = turn.Boundary.Sequence` alongside `ReportNudgedAt` in the
   existing pre-enqueue `SaveChangesAsync` (kept first on purpose — if the enqueue throws, the
   recorded nudge still prevents a 5 s re-nudge storm).
2. After `queue.EnqueueAsync(...)` returns, recovers the created message id and writes
   `task.ReportNudgeMessageId` in a second small save. Preferred mechanism: extend
   `SessionMessageQueueService.EnqueueAsync` to surface the created message's id (it already
   builds the row; the returned `SessionQueueDto.Messages` carries `QueuedMessageDto.Id`, so the
   fallback is "the returned message with the highest `Sequence` whose `Body` matches" —
   Delegation-origin messages never coalesce, so the row is always present verbatim). A small
   overload or an out-value is cleaner than the DTO fishing; decide at build time, but the id must
   come from the enqueue itself, never from a later heuristic query.
3. If the enqueue throws (id never recorded): `ReportNudgeMessageId` stays null, which component
   2's gate reads as "never delivered" — the task can then only settle via the dead-session branch
   or fail via the health deadlines. That is the correct failure direction: never settle on
   preamble because our own nudge machinery faulted.

### Legacy rows

A task nudged before this ships has `ReportNudgedAt` non-null but both new columns null. Treat
null `ReportNudgedSequence`/`ReportNudgeMessageId` with a non-null `ReportNudgedAt` as
**gate-satisfied for component 1 and 2** (i.e. today's behavior) so the handful of in-flight
nudged tasks at deploy time cannot strand; every nudge issued after deploy writes both columns.
This is a few-line carve-out with a comment, and it decays to dead code that can be removed later.

## Component 2 — no settlement before the nudge is delivered, plus a response window

### The gate

In `ClassifyReportAsync`, replace the current tail (`:1808-1818`) with:

```csharp
var sessionLive = task.AgentSessionId is Guid sid && await IsSessionLiveAsync(db, sid, ct);
if (sessionLive)
{
    if (task.ReportNudgedAt is null)
    {
        await NudgeForClosingLineAsync(services, db, task, turn, now, ct);
        return null;
    }

    // CARD-0248: "asked once and it ended ANOTHER turn unmarked" — enforced literally.
    // (Legacy carve-out: pre-CARD-0248 nudges with null ReportNudgedSequence skip these gates.)
    var nudge = await LoadNudgeDeliveryAsync(db, task, ct);   // SentAt of ReportNudgeMessageId
    if (nudge.SentAt is not DateTime sentAt)
        return null;                                   // the ask has not happened yet
    if (turn.Boundary is not { } boundary
        || boundary.Sequence <= task.ReportNudgedSequence
        || boundary.CreatedAt <= sentAt)
        return null;                                   // same boundary, or one that predates the ask
    if (turn.FinalMessageMissing
        && now < sentAt + TimeSpan.FromSeconds(_settings.ReportNudgeResponseSeconds))
        return null;                                   // text-less boundary: give the answer time to land
}

var evidence = turn.FinalMessageMissing
    ? AgentTaskReportEvidence.FinalMessageMissing
    : AgentTaskReportEvidence.UnmarkedAfterNudge;
return (AgentTaskStatus.Succeeded, evidence, body, null);
```

Both entry paths — a real transcript-driven `OnTurnEndAsync` and the sweep's re-invocation — pass
through this one place, so the gates hold regardless of who calls.

### Why `boundary.CreatedAt <= sentAt` is load-bearing and not redundant with the sequence check

A new boundary can arrive **between nudge-enqueue and nudge-delivery** (the incident's window was
12 m 45 s; on fable, another phantom `end_turn` lands there easily). Its sequence is later than the
nudged one — the sequence gate alone would let it settle — but the delegate had not yet been asked
anything when it was stored. Requiring the boundary to be stored after `SentAt` is exactly the
investigation's point: "a second boundary arriving seconds later still cannot settle it."

### The response window, and where it applies

New setting:

```csharp
/// <summary>
/// After the closing-line nudge has actually been TYPED (SessionQueuedMessages.SentAt), how
/// long settle-anyway waits before accepting a TEXT-LESS post-nudge boundary as the delegate's
/// non-answer (CARD-0248). 240 s ≈ the measured maximum prompt→response gap (217 s, see
/// ModelWaitDeadlineMinutes) — inside it, the real answer text is very probably still coming.
/// A post-nudge boundary WITH final-message text needs no window: the answer is the answer.
/// </summary>
public int ReportNudgeResponseSeconds { get; set; } = 240;
```

The window deliberately applies **only** to `FinalMessageMissing` boundaries. A post-nudge
boundary whose final message landed (text, no marker) settles immediately as
`UnmarkedAfterNudge` — that is a delegate that answered the nudge without the closing line, the
exact case CARD-0159's second-end contract exists for, and gating it on wall-clock would strand
it: a boundary with text never re-enters through the sweep (arm 1's predicate is "no
AssistantText for the boundary's ApiCallId"), so nothing would ever re-trigger settlement after
the window passed.

For a text-less post-nudge boundary the sweep IS the re-trigger (its predicate stays true), so
waiting is safe: the sweep re-hands the boundary after the watermark interval (component 3) and
the settle fires once `now` crosses `SentAt + window`.

### What the fixed timeline does to the incident, and to CARD-0046's original case

- **The incident (phantom boundary, session mid-turn):** nudge queued at grace expiry; every
  subsequent hand-off returns null (same boundary; then `SentAt` null). The task stays Working —
  which is the truth — until the delegate's real turn ends with the marked report → `Marked`
  settle on the real deliverable. No preamble ever stored.
- **CARD-0046's genuine 1-in-180 case (turn-ending response never writes text, session idle):**
  nudge queued; session is idle so WhenIdle types it within seconds → `SentAt` set → the typed
  nudge is a real UserPrompt and the live TUI answers it. With a marker → `Marked`; without →
  new boundary with text after `SentAt` → `UnmarkedAfterNudge`. The sweep still ends this task —
  just via an answered question instead of scraped narration.
- **Nudge delivered, no answer of any kind:** the nudge's own UserPrompt is now the session's
  last entry, so `ModelWaitDeadlineMinutes` (20 min) owns it and fails the task honestly.
  This is a deliberate behavior change: a task that today would settle `Succeeded` on preamble
  can now instead fail via a health deadline. `Succeeded`-on-preamble was the bug; a loud failure
  naming an unresponsive session is the correct outcome.
- **Residual hole (accepted, named):** a *phantom* text-less boundary appearing after `SentAt`
  can still settle on narration once the response window passes, because no wall-clock window can
  distinguish a phantom from a real dead response. That trigger is CARD-0282's to remove; this
  plan shrinks the exposure from "any 5 s tick after a nudge" to "a phantom boundary that both
  post-dates the nudge's actual delivery and survives a 240 s window".

### Open design question, answered: the nudge stays `WhenIdle`, not `Immediate`

The chicken-and-egg resolves **against** `Immediate`:

1. **Immediate presumes exactly the knowledge this sweep lacks.** The sweep fires when boundary
   evidence says "idle" but that evidence is unreliable — the incident's session was provably
   mid-turn (3 m 36 s into a working turn) at the moment the sweep acted on an "idle" boundary.
   `Immediate` delivery on a false boundary types into a busy composer mid-turn: at best a
   mangled interleaved prompt, at worst an interrupt that cancels the very in-flight turn whose
   report we are waiting for. The failure mode of a mistimed `Immediate` is destroying the
   deliverable; the failure mode of `WhenIdle` is waiting — and waiting is now harmless.
2. **`Immediate` buys nothing when the premise holds.** If the delegate really is idle, `WhenIdle`
   delivers within the queue's next idle flush — seconds. The two modes only diverge when the
   session is busy, which is precisely when `Immediate` is wrong.
3. **The incident's 12 m 45 s delivery delay was not a delivery bug.** The session was genuinely
   busy; holding the nudge was correct. The defect was settling before delivery, and component 2
   removes it — after which delivery latency merely keeps the task Working while the delegate
   works, which is the true state.
4. The one thing `WhenIdle` inherits is that its idle detection also keys off `TurnEnd`
   (`AgentSessionRuntime:344`) and phantom boundaries can flush the queue mid-turn — likely how
   the incident's nudge got typed at 14:07 anyway. Under this design a mid-turn-typed nudge is an
   extra prompt the delegate answers late: noise, not corruption. Making idle detection
   phantom-proof is CARD-0282.

## Component 3 — a swept-boundary watermark in `SettleDeferredReportsAsync`

### Chosen mechanism: in-memory per-session mark with a re-hand interval — not a persisted column

The investigation sketched `AgentTask.DeferredSweptSequence` (persisted, once-per-boundary). This
plan deliberately picks a different shape, because a strict once-per-boundary watermark now
**breaks liveness**: with components 1-2 in place, the SAME text-less boundary legitimately needs
re-handing later — when `SentAt` transitions from null, and again when the response window
expires — and a persisted "already swept" mark would block both, stranding exactly the tasks the
sweep exists to end. Correctness no longer needs exactly-once (the `ClassifyReportAsync` gates
make re-entry inert); what the sweep needs is to stop hammering an unchanged boundary 12 times a
minute. That is rate limiting, not bookkeeping:

```csharp
// AgentTaskDispatcher (singleton hosted service) — no persistence, no migration.
private readonly Dictionary<Guid, SweepMark> _sweepMarks = new();
private readonly record struct SweepMark(
    long BoundarySequence,      // arm 1: latest TurnEnd sequence handed over
    DateTime? LastEntryAt,      // arm 2: the silence anchor handed over
    DateTime LastHandOffUtc);
```

Per session per tick, each arm hands off to `OnTurnEndAsync` only when its observed key changed
(new boundary sequence for arm 1; new `lastEntryAt` for arm 2) **or**
`UtcNow() - LastHandOffUtc >= ReportSweepRehandSeconds`. New setting, default 60:

```csharp
/// <summary>
/// Minimum interval between the deferred-report sweep re-handing an UNCHANGED boundary to
/// settlement (CARD-0248). The sweep's predicates are monotonic, so without this it re-enters
/// settlement every PollIntervalSeconds tick for the life of an affected task — the re-entry
/// channel that ate the CARD-0159 nudge. Correctness never depends on this (settlement's own
/// gates make re-entry inert); it bounds the query load and closes the class. A changed
/// boundary always hands off immediately. <= 0 restores per-tick re-handing (tests).
/// </summary>
public int ReportSweepRehandSeconds { get; set; } = 60;
```

Prune `_sweepMarks` keys not in the tick's `sessions` list (the map self-cleans as tasks settle).
A server restart drops the map — worst case one redundant hand-off per session, absorbed by the
gates. Making it a setting matters for tests: suites that drive multi-step ladders set it to 0 so
back-to-back `SettleDeferredReportsAsync` calls behave; one dedicated test pins the default-on
suppression.

Liveness after the watermark: a text-less post-nudge boundary waiting out the response window is
re-handed within 60 s of the window expiring — well inside every other clock in this file.

---

## The two tests that encode the bug, and their new assertions

Both live in `tests/Antiphon.Tests/Application/AgentTaskDeliveryWatchdogTests.cs`. Harness note:
these suites control time by seeding timestamps into the past (`storedMinutesAgo`), so the new
gates are testable by writing `SentAt`/`CreatedAt` on the seeded rows directly; the harness's
`DelegationSettings` sets `ReportSweepRehandSeconds = 0` except in the dedicated watermark test.

### 1. `a_deferred_settlement_is_swept_after_the_grace_window` (:577-611)

Today it asserts the bug: two back-to-back sweeps, second one settles `Succeeded` /
`FinalMessageMissing` / `Result == "I'll start by reading the spec."` (the preamble). New shape —
first sweep unchanged, second sweep's assertions **inverted**:

```csharp
// sweep 1 — unchanged: the unmarked FinalMessageMissing turn is nudged, not settled
nudged.Status.ShouldBe(AgentTaskStatus.Dispatched);
nudged.ReportNudgedAt.ShouldNotBeNull();
nudged.ReportNudgedSequence.ShouldBe(<seeded TurnEnd sequence>);          // NEW
nudged.ReportNudgeMessageId.ShouldNotBeNull();                            // NEW
// the queued nudge row exists, Origin Delegation, SentAt null
// sweep 2, immediately — THE FLIPPED ASSERTION:
settled.Status.ShouldBe(AgentTaskStatus.Dispatched,
    "CARD-0248: the same boundary that was nudged can never be the settle-anyway boundary, "
    + "and the nudge has not even been delivered");
settled.Result.ShouldBeNull();
settled.ReportEvidence.ShouldBe(AgentTaskReportEvidence.Legacy);   // untouched
// no Completed event exists for the task
```

Its doc-comment changes with it: the sweep's job is now "nudge, then hold until the nudge is
answered or provably ignored", not "end this within two ticks".

### New sibling tests (same seed helper), covering the ladder the old test conflated

- `an_undelivered_nudge_never_settles_the_task` — nudge queued, `SentAt` left null, N sweeps →
  still Dispatched.
- `a_delivered_nudge_with_the_same_boundary_still_does_not_settle` — set the nudge row's
  `SentAt = now - 10 min`, no new transcript rows, sweep → still Dispatched (same boundary).
- `an_unmarked_reply_after_a_delivered_nudge_settles_unmarked_after_nudge` — `SentAt` set, then
  seed a post-`SentAt` turn: AssistantText ("Here is the report…", no marker) + TurnEnd sharing
  its ApiCallId, both `CreatedAt > SentAt`, sequence above the nudged boundary → sweep/OnTurnEnd →
  `Succeeded`, `ReportEvidence == UnmarkedAfterNudge`, `Result` == the post-nudge reply text —
  **never** the preamble.
- `a_marked_reply_after_a_delivered_nudge_settles_marked` — as above with the closing line →
  `Marked` (guards the ordinary path against regression from the new gates).
- `a_textless_boundary_after_a_delivered_nudge_waits_the_response_window` — `SentAt` set, seed a
  post-`SentAt` bare TurnEnd (no text, past FinalMessageGrace): with `SentAt = now - 1 min` →
  not settled; with `SentAt = now - 10 min` (past `ReportNudgeResponseSeconds`) → settles
  `Succeeded` / `FinalMessageMissing`. This is where the OLD test's `FinalMessageMissing`
  assertion migrates to — it is still reachable, but only through delivery + window.
- `an_unchanged_boundary_is_not_rehanded_within_the_rehand_interval` — default
  `ReportSweepRehandSeconds`; two back-to-back sweeps; the second returns a swept count that
  excludes this session (assert via a probe such as no second nudge-warning event / no additional
  `OnTurnEndAsync` observable effect, since the count itself is global across the shared DB).

### 2. `a_task_waiting_on_a_dead_subagent_is_swept_after_the_subagent_grace` (:699-725)

Same two-call shape on the subagent arm. New shape:

- Sweep 1 — unchanged: nudged, still Dispatched (make the currently-conditional mid-assert
  unconditional: `nudged.Status.ShouldBe(Dispatched)` and `ReportNudgedAt.ShouldNotBeNull()` —
  under the fix the first sweep can never have settled it).
- Sweep 2, back-to-back — **flipped**: still Dispatched, `Result` null. The old assertion
  (`Succeeded`, `Result` contains "Four review agents are running in parallel") asserted settling
  the fan-out ANNOUNCEMENT on an unanswered, undelivered nudge — the exact production bug on the
  subagent arm.
- New settling tail (in this test or a sibling
  `an_abandoned_fanout_settles_after_a_delivered_nudge_and_reply`): set the nudge `SentAt`, seed a
  post-`SentAt` unmarked reply turn ("Reviewers never returned; here is what I have…") → sweep →
  `Succeeded` / `UnmarkedAfterNudge`, `Result` == that reply, and the abandoned-subagent warning
  surfaces still fire (`AbandonedSubagents > 0` is re-derived on the new extraction). The
  announcement-only settle remains reachable exclusively through the text-less path after the
  delivered-nudge window, mirroring the final-message ladder above.

Also touched, same file: any test that relies on the second back-to-back sweep invoking
settlement must set `ReportSweepRehandSeconds = 0` in its harness settings (see component 3).

## Build-order for the implementing task

1. Migration: `ReportNudgedSequence` + `ReportNudgeMessageId` on `AgentTasks`.
2. `TurnOutcome.Boundary` + `ExtractMarkedTurnAsync` population (pure threading, no behavior).
3. `NudgeForClosingLineAsync`: record sequence + message id (extend `EnqueueAsync` to surface the
   created id).
4. `ClassifyReportAsync` gates (component 1 + 2, incl. the legacy carve-out) + `ReportNudgeResponseSeconds`.
5. Rewrite the two named tests + add the ladder siblings; run the Application namespace chunk.
6. Sweep watermark + `ReportSweepRehandSeconds` + its dedicated test.
7. Doc updates: `AgentTaskEnums` evidence comments ("a second unmarked end" → "a second unmarked
   end after the delivered nudge"), `SettleDeferredReportsAsync` and `ClassifyReportAsync`
   doc-comments, `docs/orchestration-loop.md` if it states the nudge-once contract.

Steps 1-5 are the fix (component 1 alone ends the incident; 2 closes the delivery race); step 6
is hardening and can land as a follow-up commit in the same slice.
