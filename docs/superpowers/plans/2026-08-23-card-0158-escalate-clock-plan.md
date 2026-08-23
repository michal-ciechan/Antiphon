# CARD-0158 — what "stalled" means for the Debug auto-escalate clock: plan

**Date:** 2026-08-23 · **Card:** CARD-0158 (`bf8acf95-6923-4e1d-8732-bbdeb9fc558e`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** `b834868` (this branch's base = current master). Every line number below was
re-read out of the code on that commit.

**Established fact, not re-derived here:** the Investigate stage (task `859b0611`, Grok,
read-only; findings recorded on the card and in the task's stored report, 2026-08-23). Its census
is taken as ground truth: 29 Debug tasks ever, 13 evaluable at >15 min wall-clock, **zero** loops;
`AutoEscalateStalledAsync` has fired **exactly twice**, both 2026-08-11, both on
**idle-after-a-completed-turn** — a genuine TurnEnd with a real completion message, then ~25
minutes of silence — and CARD-0153's fingerprint detector would have caught **neither** (both were
`working=false`). Nothing here re-investigates.

**Related:** CARD-0153 (the detection-only stall detector this card was filed as a follow-up to;
its plan is the bar this one matches), CARD-0020 (the deadline partition the escalate clock sits
beside), CARD-0003/CARD-0117 (the uncorrelated-report machinery that now owns the shape the clock
actually fired on), CARD-0046 (deferred settlement), CARD-0055/CARD-0056/CARD-0135/CARD-0149 (the
near-miss history that sets this repo's rule on acting from incomplete evidence), CARD-0047
(check-ins — the "is anyone looking at this task" mechanism that did not exist on 2026-08-11).

---

## Verdict up front

1. **"Stalled", as this clock defines it, has never once meant what the name says — and the one
   thing it *has* meant is now owned by a faster, safer mechanism. Disarm the automatic trigger by
   default; keep the mechanism and the manual ladder.** The shipped change is one line of config
   default — remove `EscalateAfterMinutes = 25` from Debug's `RolePolicy` entry
   (`DelegationSettings.cs:261`) — plus the doc comments and tests that pin the new default.
   `EscalateTo = Frontier` **stays**: it is also the target `AgentTaskService.ResolveEscalationTarget`
   (`AgentTaskService.cs:602-609`) resolves for a *manual* escalate, and Test already ships exactly
   this pattern (`EscalateTo = Medium`, no minutes, `DelegationSettings.cs:271`) — a ladder a human
   climbs, not a clock. The sweep code is not deleted: with no role carrying both knobs,
   `AutoEscalateStalledAsync` short-circuits before its first query
   (`AgentTaskDispatcher.cs:339-343`), and an operator who wants the old behaviour back re-arms it
   with one appsettings key (nothing in `server/appsettings.json` overrides `RolePolicy` today, so
   the code default is the live config).

2. **Escalation was the wrong response to the only shape the clock ever caught, and the right
   response has already been built — in pieces, over the two weeks after those firings.**
   Idle-after-done is a *collection* failure, and every path into it now ends somewhere better
   than a Frontier retry (§ "The settlement chain today"). Replayed against current code, both
   2026-08-11 cases produce a Warning incident within seconds of the TurnEnd and a
   Failed-with-a-pointer at the 10-minute delivery watchdog — **15 minutes before the escalate
   clock would have fired**. No new settlement mechanism is needed; the plan's job there is to
   *prove* the coverage with pin tests built from the two real cases (V2, V4).

3. **No working-loop escalation mechanism gets built, and not as a deferral — as a decision.**
   CARD-0153 stays detection-only indefinitely. Escalation is not a softer alternative to a kill;
   it *is* a kill (`RequeueAsync` → `StopDelegateAsync`, `AgentTaskService.cs:622-667`) with a
   bill attached. The revisit criterion is written down in § D3 so this is falsifiable, not vibes:
   three confirmed `TaskProgressStalled` episodes whose human resolution was kill-and-escalate.

4. **The watch-and-see instrument already ships.** CARD-0153's incident stream (zero rows to date)
   is the measurement going forward for the loop shape; the uncorrelated-report incident +
   watchdog arm is the measurement for the idle-after-done shape. Disarming the escalate trigger
   is *not* a structural bet on thin evidence — the evidence on the trigger itself is thin only in
   one direction: it is 2-for-2 harmful, 0-for-2 useful, and its remaining reachable territory is
   a false-positive trap (§ D1, the 88-minute tool call).

**One sentence:** retire the 25-minute auto-escalate trigger (config default, mechanism kept,
manual ladder kept), prove with fixtures built from the two real 2026-08-11 firings that today's
settlement chain handles their shape earlier and better, and leave loop-escalation unbuilt with a
written criterion for when that decision gets reopened.

---

## What the code does today, re-read on `b834868`

### The clock being judged

`AgentTaskDispatcher.AutoEscalateStalledAsync` (`AgentTaskDispatcher.cs:336-396`), first sweep of
the tick (`:163`). Scans roles carrying **both** `EscalateTo` and `EscalateAfterMinutes` — shipped
config: Debug only, High → Frontier at 25 min (`DelegationSettings.cs:261`). Its clock is
`Max(CreatedAt)` over **all** transcript rows (`:362-368`); quiet ≥ 25 min ⇒
`AgentTaskService.EscalateAsync` ⇒ `RequeueAsync` (`AgentTaskService.cs:622-667`): **the session
is killed** (`StopDelegateAsync`), the task requeued one tier up with the old `FailureReason` as
handoff. So the automatic path's action = kill + re-spend, gated on nothing but silence.

### What the two real firings actually did (from the recorded investigation)

| | 2c40e79f (CARD-0006 debug) | 9775fe45 |
|---|---|---|
| Session | `1b78bec1` | `d681178e` |
| Real completion | TurnEnd seq 179, 08:47:31Z — "Done. CARD-0006 did not cause this…" after commit+push | TurnEnd seq 238, 08:50:53Z — "Root cause found, fixed, and committed…" |
| Escalation | 09:12:35Z, quiet 25.07 min | 09:15:55Z, quiet 25.03 min |
| Then | Frontier re-dispatch into the **same worktree**; clipped reporting-contract prompt landed on the dying first session (09:12:51Z / 09:16:11Z); second attempt produced 9–16 rows in ~1 min; both sessions dead ~10:21Z; task rows sat until 2026-08-14 13:02Z when recovery stamped an unattributed-report failure over the stall reason | same |

The work was **finished and pushed** before either escalation — this repo still carries
`.antiphon/task-2c40e79f.md`, the delegate's own written report, committed on 2026-08-11. The
escalations collected nothing, killed the sessions holding the context, spent two Frontier
attempts, and both tasks still ended Failed. That is the clock's entire lifetime record.

### Why that shape cannot recur unnoticed today — the settlement chain, exit by exit

Every way an open task's turn can end now lands somewhere specific. All line numbers current:

| Exit | Mechanism | When |
|---|---|---|
| Marked turn ends with a report | `AgentTaskReplyService.OnTurnEndAsync` → `SettleAsync` (`AgentTaskReplyService.cs:64-148`) | seconds |
| Turn-ending response's text still in flight | deferred; `SettleDeferredReportsAsync` sweeps the grace (CARD-0046, tick `:195`) | ≤ grace (120 s) |
| Turn ends, no text at all | `FailUnreportedTurnAsync` (`:600`) | seconds |
| Turn ends with a report but **no task marker** — the 2026-08-11 shape | `DelegateReportUncorrelated` Warning incident + alert at TurnEnd (`:95-101`, `:165-222`); then the delivery watchdog's second arm **fails the task with a pointer** — "read session X before re-running" — kill withheld if working (`AgentTaskDispatcher.cs:533-564`) | incident: seconds; failure: 10 min (`DeliveryFailTimeoutMinutes`, `DelegationSettings.cs:299`) |
| Brief never landed at all | watchdog arm 1, with bind-refusal and capability-mismatch recovery arms (`:495-531`) | 10 min |
| Session died | `FailDeadSessionTasksAsync` (tick `:174`) | minutes |
| Working, quiet, model owes tokens | phase deadline, 20 min (`TaskDeadlinePolicy.cs:191-194`) | 20 min |
| Working, quiet, local tool running | phase deadline, 90 min (`:198`) | 90 min |
| Working, rows landing, none novel | `TaskProgressStalled` detection, 30 min (CARD-0153, `TaskProgressPolicy.cs`, sweep `AgentTaskDispatcher.cs:1041`) | 30 min |
| Everything else | 240-min ceiling; CARD-0047 check-ins; `PastExpectedIdle` attention row | hint/backstop |

None of this existed on 2026-08-11 except the ceiling. The uncorrelated arm's own doc comment
names those stranded tasks as the reason it was built (`AgentTaskDispatcher.cs:544-549`); CARD-0055
(2026-08-16) then attacked the *root cause* — a marker mangled in delivery now parks with a
Critical-capable incident before the turn ever runs (`Truncated`, CARD-0024), instead of silently
producing an uncorrelatable report; CARD-0117 (2026-08-21) scoped the evidence per task.

### What territory is left for the escalate clock after all that

Work through the table: every idle exit is claimed earlier than 25 min, and the loop shape is
claimed by CARD-0153 at 30. Exactly two shapes remain reachable by a 25-minute quiet clock:

1. **A working session, last row a `ToolCall`, quiet 25–90 min.** The local-execution deadline was
   set at 90 minutes *because the measured healthy maximum is 5 311 s ≈ 88.5 min*
   (`TaskDeadlinePolicy.cs:196-198`). The escalate clock sitting at 25 min inside that window is
   armed to kill a healthy long test run and re-buy it at Frontier — a false positive by the
   repo's own measurements, waiting for the first Debug delegate that runs the full
   `Antiphon.Tests` suite twice. It has not fired here yet only because no evaluable Debug task
   has run a >25-min silent tool (max observed quiet outside the two firings: 9.95 min).
2. **An idle unsettled task that raised no uncorrelated incident** — i.e. a *bug in the settlement
   chain itself*. A bug should fail loudly and preserve the session for reading, not be laundered
   into a Frontier retry that kills the evidence. The 2026-08-11 postscript is the proof: the
   escalation didn't just waste money, it destroyed the sessions that held the answer.

Both remaining shapes are ones where escalation is the wrong action. That — not the sample size —
is the case for disarming.

---

## Design

### D1 — Q1: disarm the automatic trigger; keep the ladder (decision)

The three framed options, judged against the record:

- **Keep as-is** keeps an intervention whose lifetime record is 2 firings, both wrong, whose only
  realized catch is now made unreachable by a 10-minute mechanism that fails-with-a-pointer
  instead of killing-and-re-spending, and whose remaining reachable territory contradicts the
  repo's own measured healthy tool times. "It does real, useful work" was true on 2026-08-11 and
  is not true on `b834868`.
- **Re-key onto `TaskProgressPolicy`** converts the clock into an *automatic kill on fingerprint
  evidence* — precisely what CARD-0153's verdict 1 ruled out, on a signal that has produced zero
  positives ever, for a failure shape (the Debug loop) with zero recorded occurrences. It would
  also, as the investigation notes, have silently un-fired the only two historical escalations —
  a behaviour change nobody would ever observe. Rejected.
- **Split the shapes** is the right *analysis* — idle-after-done and working-loop are genuinely
  different failures — but the split already exists in the codebase: the settlement chain owns the
  first and CARD-0153 owns the second. Building a third mechanism to re-own either would be a
  parallel implementation of a rule that already has a home (the same "fourth implementation of
  IsWorking is a defect" logic).

So the call: **the escalate clock's automatic trigger is retired by default.** Concretely:

- `DelegationSettings.cs:261` becomes
  `["Debug"] = new() { Level = AgentModelLevel.High, EscalateTo = AgentModelLevel.Frontier }`.
- `RolePolicyEntry.EscalateTo`'s doc comment (`:518-540`) is rewritten: it is the **manual
  ladder's configured target** first (what `EscalateAsync` resolves when a human or an explicit
  caller escalates), and the auto-trigger's target only where an operator also sets
  `EscalateAfterMinutes`. The "deliberate, narrow tier bump" paragraph gains the CARD-0158 history:
  fired twice ever, both on idle-after-done now owned by the delivery watchdog's uncorrelated arm,
  disarmed by default 2026-08-23; re-arm only with the knowledge that 25 min sits inside the
  measured 88-min healthy local-execution window.
- `AutoEscalateStalledAsync`'s summary (`AgentTaskDispatcher.cs:330-335`) and the tick comment
  (`:161-163` — "a stalled opus Debug task escalating to fable IS the tier ladder working") are
  updated to say the sweep ships disarmed and why; the tick comment must stop presenting the
  auto-fire as the advertised behaviour.
- The sweep body, `EscalateAsync`, `RequeueAsync`, and the manual `/escalate` surface do not
  change at all.

**Why disarm-by-default rather than delete:** the sweep is 60 lines, costs zero queries when
disarmed, and is the only automatic rung the tier-ladder concept has; an operator running a fleet
where Debug delegates are cheap and unattended may rationally prefer "kill quiet work at 25 min"
— that is a policy choice config should allow. Deleting it forecloses that for no simplification
worth having. What must not survive is it being **armed silently by default** against the
evidence.

### D2 — Q2: idle-after-done is a collection problem, and collection is already fixed (assessment, no build)

The direct fix for "task finished but nothing noticed for 25+ minutes" would be: notice at the
TurnEnd, or fail fast with the work preserved when the report cannot be attributed. Both exist
(§ table above). This plan adds **no new settlement mechanism** — the deliverable for Q2 is the
verification section's V2/V4, which replay the real 2026-08-11 fixtures through today's chain and
pin that the incident and the 10-minute pointer-failure arrive, so the coverage claim this whole
design leans on is a test, not an assertion in a doc.

One residual gap was found while walking the chain, and deliberately **left as a watch item, not a
slice**: the watchdog's uncorrelated arm queries `Status == Dispatched` only
(`AgentTaskDispatcher.cs:427`), so a *Working*-status task (post-`AnswerAsync`, or an API-error
resume) that later strands uncorrelated would today raise the Warning incident but never the
10-minute failure — it would ride to the 240-min ceiling. Widening the arm is **not** obviously
safe: a Working task has already correlated once (the answer carries the marker,
`AgentTaskReplyService.cs:248`), so an uncorrelated turn there is *more* likely a stray human turn
in the delegate's terminal, and auto-failing the task on it is a new false-positive class —
exactly the trade CARD-0117 weighed when it scoped the evidence. Zero occurrences of this shape
exist in the data (both historical cases were Dispatched). It goes on the card as a note, armed by
the incident that already fires; if a Working-status strand ever actually happens, that incident
row is the evidence a future card designs against.

### D3 — Q3: no loop-escalation mechanism; the revisit criterion in writing (decision)

Escalating on a CONFIRMED loop is **not** different enough from a kill to be safe, because it is
a kill — `RequeueAsync`'s first act is `StopDelegateAsync` — plus a re-spend, minus the pause a
human kill gets. The question is therefore CARD-0153 verdict 1's question again, and the answer
does not change with the trigger's colour: fingerprint evidence ("nothing novel for 30 min") is
weaker than the evidence that fired the kills behind CARD-0055/0056/0117/0135/0149, and the
detector has produced zero positives since shipping, so there is not one real episode to design
an intervention against. An automatic intervention designed against zero examples is how this
card's own subject got built in 2026-07 — that is the loop to break.

**Stays detection-only indefinitely, with this reopening criterion** (recorded here and on the
card so "indefinitely" is falsifiable): reconsider an automatic escalate-on-stall only when
**three `TaskProgressStalled` episodes exist whose human resolution was kill-and-redispatch-higher**
(vs steer/compact/wait), giving a real corpus of cases where the automation would have done what
the human did. Until then, the human keeps the trigger; `scripts/checkpoint-task.ps1` (CARD-0153
S4) already makes acting on a stall row loss-free.

### D4 — Q4: what "watch and see" concretely is here

- **Loop shape:** already instrumented — `TaskProgressStalled` incidents with machine-readable
  `FailureReason` (rows/distinct/lastNovel/files). No change; D3's criterion consumes this stream.
- **Idle-after-done shape:** already instrumented — `DelegateReportUncorrelated` incidents +
  watchdog failures whose reason text names the shape. No change.
- **What is *not* kept as watch-and-see:** the armed trigger itself. "Leave it armed and see"
  fails the cost test both ways: if it fires, it kills a session and spends a Frontier attempt on
  evidence its own history says is wrong; and the data it would gather (quiet-clock firings) is
  already fully reconstructable offline from `AgentTaskEvents` + transcripts, which is exactly how
  the investigation measured it. Nothing is learned by letting it act that is not learned by
  replaying it — so it does not get to act.

### D5 — what does not move

- `EscalateAsync` / manual escalation, `RetryAsync`, the escalation event trail, the
  Grok same-model-escalation note. Untouched.
- `TaskProgressPolicy`, `DetectStalledProgressAsync`, all CARD-0153 behaviour and settings.
  Untouched — this card explicitly does not widen detection into intervention.
- `TaskDeadlinePolicy`, both watchdog arms, all settlement paths. Untouched.
- `AgentTaskCheckScheduleTests.running_far_past_the_expected_duration_changes_nothing` — already
  drives `AutoEscalateStalledAsync` and asserts nothing happens on a Code task; stays green
  unmodified.

---

## Slices

| Slice | Content | Depends on |
|---|---|---|
| **S1** | Config default: drop `EscalateAfterMinutes` from Debug's shipped `RolePolicy` entry; rewrite the `RolePolicyEntry.EscalateTo`/`EscalateAfterMinutes` doc comments, `AutoEscalateStalledAsync`'s summary, and the tick's first-sweep comment; rework `AgentTaskStallEscalationTests` (V1, V3, V5) | — |
| **S2** | Historical-fixture pins: the two 2026-08-11 cases as fixtures, replayed against the disarmed clock, the settlement chain, and `TaskProgressPolicy` (V2, V4) | S1 |
| **S3** | Card bookkeeping: append the decision + D2's Working-status watch item + D3's reopening criterion to CARD-0158; no new cards filed | — |

S1 and S3 are independent; S2 builds on S1's settings shape. Each slice is one commit with its
tests.

---

## How we verify this and will not regress it

All in `Antiphon.Tests` (TUnit; `dotnet run --project tests/Antiphon.Tests
--property:OutputPath=bin-0158/ --treenode-filter "/*/Antiphon.Tests.Application/*/*"` — forward
slash, delete `bin-0158` dirs after). Integration tests use `TestDbFixture`; every assertion
scoped to the rows the test made.

### The historical fixtures (S2's foundation)

A test helper (`EscalateClockHistoricalFixture`, in the test project) encodes the two real cases
as *relative* timelines with the absolute source facts in comments, so the tests replay evidence
rather than synthetic guesses:

- **`Fixture_2c40e79f`** — session `1b78bec1`: dense progress rows (the census: ~195 rows over
  44.9 min, max gap 25.07 only at the end), then a `TurnEnd` whose final `AssistantText` is the
  real completion text ("Done. CARD-0006 did not cause this…"), timestamped `now - 25.5min`
  (source: TurnEnd seq 179 08:47:31Z, escalation 09:12:35Z), quiet after. Task: Debug, High,
  Dispatched, dispatched 45 min ago.
- **`Fixture_9775fe45`** — session `d681178e`: same shape (TurnEnd seq 238 08:50:53Z, "Root cause
  found, fixed, and committed…", escalation 09:15:55Z = 25.03 min), 59.9-min run.

Both carry a `DelegateReportUncorrelated`-shaped context: the turn's prompt row does **not**
contain the task marker (that is *why* neither settled — the brief's marker did not survive
2026-08-11 delivery).

### V1 — the behaviour change, pinned from the historical evidence (`AgentTaskStallEscalationTests`, reworked)

1. **`The_2026_08_11_idle_after_done_shape_is_no_longer_escalated`** — `Fixture_9775fe45`,
   **default** `DelegationSettings` → `AutoEscalateStalledAsync` returns 0; no `Escalated` event;
   session not killed; `ModelLevel` still High. **Red on `b834868`** — this is the change.
2. **`a_stalled_task_with_an_escalation_policy_is_bumped_automatically`** (existing, `:31-49`) —
   kept verbatim in behaviour but its harness switches from
   `Options.Create(new DelegationSettings())` (`:125`, whose comment says it pins the *shipped*
   policy) to an explicit opt-in: `RolePolicy["Debug"].EscalateAfterMinutes = 25`. The comment
   flips meaning: the shipped default is now DISARMED; this test pins that the mechanism still
   works for an operator who re-arms it. Same treatment for
   `transcript_progress_resets_the_stall_clock`, `a_task_already_at_the_target_tier_is_not_bumped_again`,
   `a_task_inside_its_window_is_not_touched` (the two no-op tests stay meaningful only under
   opt-in settings; under the default they would pass vacuously).
3. **`the_shipped_default_arms_no_role_at_all`** — construct `new DelegationSettings()`, assert no
   `RolePolicy` entry carries both `EscalateTo` and `EscalateAfterMinutes` (the sweep's own
   arming predicate, `AgentTaskDispatcher.cs:339-341`), and `AutoEscalateStalledAsync` over a
   deliberately poisoned `DbContext`-free harness returns 0 without touching the database — the
   cheapest pin that "disarmed" means zero queries, and the test that goes red if a future role
   entry quietly re-arms the sweep.
4. **`a_quiet_long_local_tool_is_not_escalated_by_default`** — working session, last row
   `ToolCall` 30 min old, task dispatched 40 min ago, default settings →
   `AutoEscalateStalledAsync` 0 **and** `TaskDeadlinePolicy.EvaluateAsync` on the same rows yields
   the LocalExecution verdict, not breached (30 < 90). Pins the false-positive trap of § "What
   territory is left" from both sides: the measured-healthy shape survives the default config,
   and the deadline that legitimately owns it is named in the same test.

### V2 — the settlement chain owns the historical shape (`AgentTaskDeliveryWatchdogTests` additions)

5. **`The_2026_08_11_shape_fails_with_a_pointer_at_ten_minutes_not_an_escalation`** —
   `Fixture_2c40e79f` (Dispatched, real prompt rows present but none carrying the marker,
   `DelegateReportUncorrelated` incident row seeded as `OnTurnEndAsync` writes it) →
   `FailNeverStartedAsync` fails the task; `FailureReason` contains "could not be attributed" and
   the session id; no `Escalated` event; the model level untouched; and — the money assertion —
   the failure fires with the fixture's clock at *dispatch + 10 min + ε*, i.e. 15 minutes before
   the old clock's threshold. (The arm itself is already pinned by the existing watchdog tests;
   this test pins it **against the real historical timeline**, which is what CARD-0158 asked
   for.)
6. **`An_uncorrelated_report_on_a_Working_task_raises_the_incident_and_nothing_else`** — the D2
   watch item pinned as *current* behaviour so the gap is explicit and intentional: same fixture
   but `Status = Working` → watchdog does not fail it, escalate sweep (default settings) does not
   touch it, and the `DelegateReportUncorrelated` incident row is the surviving surface. The test
   comment carries the D2 reasoning and points at the card note, so whoever meets this shape live
   finds the analysis one Ctrl-click away.

### V3 — the fork's rejected arm, documented as a pin (`TaskProgressPolicyTests` addition)

7. **`The_fingerprint_detector_declines_the_2026_08_11_shape_by_design`** — `Fixture_9775fe45`
   through `TaskProgressPolicy.EvaluateAsync` → null (working=false after the TurnEnd). This is
   the investigation's central negative finding as executable documentation: re-keying the
   escalate clock onto the fingerprint detector would have made the only two historical firings
   impossible, and anyone proposing the re-key in the future trips over this test's comment
   first.

### What must stay green (the regression set)

`AgentTaskStallEscalationTests` (reworked, all arms), `AgentTaskDeliveryWatchdogTests`,
`AgentTaskCheckScheduleTests` (drives the sweep directly at `:183` — unaffected, role Code),
`TaskProgressPolicyTests`, `TaskProgressStallSweepTests`, `TaskProgressPolicyFileArmTests`,
`AgentTaskReplyIntegrationTests`, `TaskDeadlinePolicyTests`, `AgentTaskOverdueDeadlineTests`.
Run targeted after S1/S2; one `Antiphon.Tests.Application` namespace chunk at the end.

---

## Cards to file

None. The two follow-ups this design surfaces are deliberately **notes on CARD-0158**, not cards:

1. **D2's Working-status uncorrelated gap** — zero occurrences; instrumented by the existing
   incident; V2's test 6 pins the current behaviour with the reasoning attached. A card with no
   observed instance and no agreed action would be inventory, not work.
2. **D3's reopening criterion** — three `TaskProgressStalled` episodes resolved by a human as
   kill-and-escalate reopens the loop-escalation question with a real corpus. That is a condition,
   not a task.

## Open questions for the operator (non-blocking; defaults chosen above)

- Whether to keep `EscalateTo = Frontier` on Debug at all, now that it only feeds the manual
  ladder. Kept in this plan: `ResolveEscalationTarget` prefers the configured target over
  rung-counting for manual escalates, and Debug→Frontier remains the right manual jump. Removing
  it would change what a human's bare `/escalate` on a Debug task resolves to; that is a separate
  choice from disarming the clock and nothing here forces it.
- Whether the disarm deserves a line in CLAUDE.md's Gotchas. Leaning no — the doc comments on the
  settings and the sweep carry the history, and CLAUDE.md entries are for traps, not changelogs;
  the trap here (re-arming inside the 88-min healthy window) lives in the
  `EscalateAfterMinutes` doc comment where the person re-arming will actually be looking.
