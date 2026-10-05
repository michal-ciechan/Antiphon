# CARD-1077: why the Blocked-session seat-holding defect (CARD-1065) was not caught earlier

Date: 2026-10-05. Investigate task `6cdcb609-2582-46da-866d-068e1f951ef4` (Frontier, worktree
`feat/card-task-6cdcb609`). Read-only: no production code, no cancel, no reply, no release.
Sources: the Antiphon board (`card.ps1 get` for CARD-1077, 1065, 0667, 0664, 0672, 0811, 1043,
1076 and CARD-0667's revision log), the task API (`GET /api/agent-tasks/{id}` for 1,756 board
tasks created since 2026-09-24, fetched 2026-10-05 22:40Z, plus `/pipeline`, `/attention`,
`/api/hosts`, `/api/session-runners` and `/session-runners/server2/slots`), the two audit reports
(tasks `f23af0f1` and `1fc4f3dd`, read from their stored `result`), git history at `d985af05b`,
and the owner docs and bundles. The desktop server log (`C:\src\Antiphon\server\logs`) is not
reachable from the server2 mirror and was not read; the task event rows carry the same Held,
Blocked and sync lines and were used instead.

## 1. What happened

On 2026-10-05 08:01Z server2 held 10 of 10 seats. Eight were Running, idle sessions of Blocked
tasks; two were real work. Nine dispatches queued behind them that day on `Held: runner
'server2' at capacity 10/10`. The oldest idle session (Code `2c35a27d`, CARD-1013) had been
Blocked since 2026-10-04 00:28Z and was released only by a cancel at 2026-10-05 19:17Z, 42.8
hours later. The audit `f23af0f1` (2026-10-05 09:07Z) found the eight by joining the slot list
to tasks; the audit `1fc4f3dd` (18:58Z) told the operator which to cancel. CARD-1065 was filed
the same day.

The mechanism is three rules that were each correct on their own:

| Rule | Where | Since |
|---|---|---|
| A Blocked delegate keeps its session and agent: "the session is how the conversation continues". | `AgentTaskReplyService.cs:1087-1089`, `:1125-1132`; pinned by `AgentTaskReplyIntegrationTests.a_blocked_delegate_keeps_its_session_and_agent` (`tests/.../AgentTaskReplyIntegrationTests.cs:3728`) | 2026-08-07, commit `92dd18c0f` (worktree isolation); restated 2026-08-08, `2daa5a0be` (warm pool) |
| A runner seat is a live process: `OccupiesCapacity` counts every non-Exited, non-Failed runner session; the host figure counts Created/Starting/Running/Stopping desktop rows. | `RunnerSlotService.cs:20-23`; `HostEndpoints.cs` / `SessionRunnerCatalogue.cs` (audit `f23af0f1`) | 2026-09-24, CARD-0653 `58401ffb8` |
| A seat is owned only by a Dispatched or Working task; anything else is `orphan=true`, and the orphan flag is "selection, never force authority". | `RunnerSlotService.cs:397-401` (open-task predicate), `:73-81` (sweep returns 0 unless the dormant CARD-0667 coordinator is enabled) | 2026-09-24, CARD-0653 |

Together: a task that reports `blocked` keeps a Running process on the runner, the process
counts as a seat, nothing owns it in the orphan sense, and nothing acts on the orphan flag.

Two feeders filled the parked population:

1. `runner_sync_lease_busy` (CARD-0657, 2026-09-24, `d82d3bcc0`). A runner-bound task whose
   report is complete but whose desktop fast-forward cannot take the repository lease within 120 s
   is written Blocked with verdict `done` and told "repair it, then reply to this task for a fresh
   completion report" (`AgentTaskReplyService.cs:954`). The first such block was at 2026-09-24
   23:07Z (Code `2b6ade7e`, CARD-0679), 16 hours after the reason was introduced.
2. Ordinary questions (verdict `blocked`) that nobody answered before leaving.

## 2. Quantification

All figures are from the task event rows of the 1,756 Antiphon-board tasks created from
2026-09-24 (the day seats became countable) to 2026-10-05 22:40Z. A "seat window" runs from a
task's Blocked event to the earlier of its next Replied/Canceled/Failed event and its desktop
session's `endedAt`; a task with no session or whose session had already ended holds no seat.
Blocked episodes on tasks from other boards are not in the sample (163 rows excluded by board
scope), so the server2 counts are a floor.

| Measure | Value |
|---|---|
| Blocked episodes, all runners | 154 (server2 130, server2-temp 10, desktop 14) |
| Episodes that held a server2 or server2-temp seat | 139, 267.4 seat-hours; 58 of them (209.5 seat-hours) on 2026-10-03 to 10-05 |
| Peak idle Blocked seats on server2 | 8 of 10 at 2026-10-05 08:01Z (`2c35a27d`, `ffc43849`, `15d5a0c9`, `bed39f89`, `a073b2d8`, `aa7cf6db`, `b8f22709`, `0c638c88`) |
| Daily peak of idle Blocked seats on server2 | 09-27: 4; 10-03: 5; 10-04: 6; 10-05: 8; every other day 1-2 |
| Longest single windows | `2c35a27d` 42.8 h, `ffc43849` 38.7 h, `15d5a0c9` 38.3 h, `bed39f89` 26.8 h, `3525d6b5` 13.4 h (CARD-0667's 2026-10-01 case), `abea11d8` 13.2 h, `97e2c86f` 13.0 h |
| Finished-but-unsynced (`runner_sync_lease_busy`) | 63 episodes (62 server2, 1 server2-temp); 15 on 10-04, 16 on 10-05 (nine Reviews, four Code, two Plan, one TestDesign on 10-05) |
| Of those, resolved by a caller Reply | 38 (29 re-settled Succeeded, 7 later Canceled, 1 Failed, 1 re-Blocked); median wait under one minute, longest 4.7 h |
| Of those, never replied to | 25, 150.3 seat-hours; 24 ended by a cancel, 1 (`c495f1d8`) still Running at the time of reading |
| Other sync reasons (`runner_sync_diverged`, `runner_sync_identity_mismatch`) | 27 episodes, 18.1 seat-hours |
| Genuine questions on server2 (verdict `blocked`) | 49 episodes, 93.2 seat-hours; median 2 minutes, 90th percentile 4.4 h, longest 38.7 h (`ffc43849`) |
| server2 dispatch holds on capacity (`Held: runner 'server2' at capacity`) | 18 events on 16 tasks; 15 of the 18 while at least one idle Blocked seat was held (09-27: 4 events, 1 with idle seats; 10-04: 3 of 3; 10-05: 11 of 11, against 7-8 idle seats) |
| `HeldAged` escalations naming server2 capacity | 11 (all 2026-10-05 04:48Z-08:59Z, four reaching Error at about 900 s) |

So about 45 per cent of parked seat-time on server2 since 2026-09-24 belonged to tasks that had
finished their work, and the one day server2 was seat-starved for hours (2026-10-05, 04:42Z to
09:16Z) every capacity hold was against seats that were doing nothing. The `hostBudget`
queue reason in `GET /api/agent-tasks/pipeline` is the same condition
(`AgentTaskPipelineStatusService.cs:437-444`); the four capacity holds on 2026-09-27 happened
with the runner genuinely busy (3 of 4 had no idle seat).

## 3. Timeline of what was known

| When | Event | Evidence |
|---|---|---|
| 2026-08-07 | Blocked keeps its session; test pins it as desired. | `92dd18c0f`, test at `AgentTaskReplyIntegrationTests.cs:3728` |
| 2026-09-24 15:34Z | CARD-0667 filed for Failed-settle seat leaks. Its fix line already says "release on EVERY terminal settlement (Failed, Canceled, Blocked-after-report, ...)". | Card revision 1 text; the card's 2026-10-01 appendix says this case was "already named". Create-time text is not revision-logged, so the 09-24 wording rests on that appendix. |
| 2026-09-24 | CARD-0653 lands seats, the slot list, `orphan=true`, operator-only `release-orphans`. CARD-0657 introduces `runner_sync_lease_busy` -> Blocked. First lease-busy block 23:07Z. | `58401ffb8`, `d82d3bcc0`, task `2b6ade7e` |
| 2026-09-25 | 26 Blocked episodes on server2 in one day (10 lease-busy); all short (0.4 seat-hours total). | Episode data |
| 2026-09-27 | First day with 4 idle Blocked seats; first capacity holds. | Episode data; `da9b64d7` held with 3 idle seats |
| 2026-09-28 | CARD-0654 host budgets, `hostBudget` queue reason. | `253338796` |
| 2026-10-01 07:39Z | `3525d6b5` lease-busy Blocked, 13.4 h seat; rollout refused `RunnerBusy`. Appended to CARD-0667 as "the Blocked-after-report case this card already names". | CARD-0667 revision 1 |
| 2026-10-01 | CARD-0667 plan (`1c9373e81`) scopes "Blocked after a completed report" but makes automatic release a dormant flag (`AutomaticEnabled = false`, `TerminalRunnerSeatReleaseService.cs:15`) with activation "a separately commissioned operation" (plan line 386). docs/orchestration-loop.md gains "counting Queued, Dispatched and Working ... but not Blocked ones" (`5d54aff41`). | Plan text; doc line 787 |
| 2026-10-04 | 24 Blocked episodes on server2, 163.4 seat-hours; four Code tasks park for 27-43 h. CARD-1043 filed for the evidence-binding side of lease-busy. | Episode data |
| 2026-10-05 04:42Z-09:16Z | 7-8 idle seats; nine tasks held on capacity; four `HeldAged` Errors. | Held rows |
| 2026-10-05 09:07Z | Audit `f23af0f1` identifies the eight, concludes "legitimate under the current rule", files no card because CARD-0667 exists. | Task result |
| 2026-10-05 18:58Z | Audit `1fc4f3dd` recommends three cancels and one reply; CARD-1065 filed Critical/Now; CARD-1077 filed. | Task result; cards |

The defect was therefore named on a card eleven days before it was treated as one, and the
plan that owned it chose a dormant path. Nothing in between measured how much seat-time
Blocked was consuming, so the choice to stay dormant was made without the number.

## 4. Escape analysis

### Detection gap

The condition was visible in four places and alarmed in none.

- `GET /api/session-runners/server2/slots` showed `orphan=true` on every idle seat from the
  moment each task blocked (open-task predicate at `RunnerSlotService.cs:397-401`). The flag is
  computed on request, read by `scripts/runner-slots.ps1` and the audit, and feeds no attention
  row, incident, metric or log line. `release-orphans` is an operator POST that returns zero
  unless the dormant coordinator is enabled (`RunnerSlotService.cs:73-81`), and the scheduled
  `DiscoverScheduledAsync` is gated by the same flag (`TerminalRunnerSeatReleaseService.cs:73`).
- `GET /api/hosts` and `GET /api/session-runners` report `inFlight 10` and `occupied 10` with no
  breakdown by task status; the pipeline route's `hosts` block is the same number. The
  orchestrator reads exactly these three routes before dispatching
  (docs/orchestration-loop.md:793-800, orchestrator bundle line 84-85, skill line 31-34) and so
  saw a busy host, not eight idle seats.
- The attention feed had one `BlockedQuestion` row per Blocked task, severity Critical, titled
  "Blocked - waiting on a human answer" for every non-quota, non-routing, non-conflict block
  (`AttentionService.cs:378-392`), including the 63 lease-busy blocks whose verdict was `done`.
  The row carries no seat, runner, age threshold or occupancy context, and the feed on
  2026-10-05 22:38Z had 344 rows, 126 of them `LandOutcomeUnconfirmed` from two settled tasks.
- Every age-based condition excludes Blocked: `PastExpectedIdle`, `Overdue` and the open set
  (`AttentionService.cs:172-173`), the leak builders (`AttentionService.Leaks.cs:129, 230`
  treat Blocked as an owner), the check schedule (`AgentTaskDispatcher.cs:3889-3891`), the role
  wall-clock ceiling (`AgentTaskDispatcher.cs:2667`; task `94914cdf` was Blocked 28.5 h and
  failed "29h33m against the 240-minute ceiling" one second after the Reply that resumed it),
  and `CardStalled` (needs nothing open). `DispatchHeld`/`HeldAged` did fire, but they name the
  queued task and the class `runner`, not the idle seats behind it. Host stats keep only
  in-memory 1/5/15/30-minute rollups (docs/ops-http.md:127), so no durable occupancy series
  existed to show "10/10 for five hours".

Missing control: an alarm keyed on a seat (or a host's occupancy minus its Working count)
rather than on a task, with an age threshold. Filed as CARD-1079; the Blocked clock and the
classification as CARD-1081; the feed noise as CARD-1085.

### Test gap

No test states the invariant "a seat belongs only to work". The existing tests pin the parts:

- `a_blocked_delegate_keeps_its_session_and_agent` asserts the behaviour that caused the loss,
  as a desired property (`Stopper.Killed.ShouldBeEmpty()`, `PoolIdleSince.ShouldBeNull`).
- `RunnerSlotRulesTests.Exited_records_do_not_occupy_and_a_warm_pool_is_not_an_orphan` pins
  `OccupiesCapacity` on process status and `IsOrphan` as a pure predicate; nothing asserts what
  happens to an orphan.
- CARD-0667's witnesses (`TerminalRunnerSeatReleaseTests`, 16 results, including
  `Blocked_report_with_running_runner_frees_the_seat`) cover the release path but only with
  `AutomaticEnabled` set; CP-3 of that plan re-runs the four legacy reply tests, including the
  Blocked-keeps-session one, as "compatibility".
- No test or checkpoint bounds the lifetime of a parked session, and the CARD-0667 plan's
  verification table (plan lines 365-380) has no row for "a Blocked task older than N holds no
  seat".

Missing control: an enumerating rules test over `AgentTaskStatus` for seat ownership plus a
per-status integration witness that occupied drops after settlement or park. Filed as
CARD-1080.

### Design gap

Three design decisions compounded:

1. "Blocked keeps everything" was written when a delegate was a local Claude session on the
   desktop with no seat budget (2026-08-07). Seats arrived on 2026-09-24 and the rule was not
   revisited; the CARD-0667 plan revisited it on 2026-10-01 but chose a dormant automatic path
   and no activation date (plan line 386), and CARD-1065's plan on 2026-10-05 still keeps the
   task Blocked and adds publication-gated parking.
2. `runner_sync_lease_busy` makes a finished task Blocked and hands the caller an obligation to
   reply (`TaskCompletionNotification.AppliesToRunnerSyncBlock`). 25 of 63 such obligations were
   never discharged. The reason is purpose-blind wall-clock contention with lands and remote
   prep (CARD-0672 plan line 54, CARD-1076), so the population grows exactly when the host is
   busiest. CARD-1043 covers the evidence side; nothing yet changes the verdict.
3. The process-release invariant in AGENTS.md ("killed, pooled warm, or owned by a standing
   agent") has no time bound and treats a Blocked task as an owner, so an idle Blocked session
   satisfies it indefinitely. The orphan flag was designed as "selection, never force
   authority" (CARD-0653) and nobody was given the authority.

Missing control: a settlement outcome for "complete report, desktop sync pending" that is not
Blocked (filed as CARD-1082), and a bound on how long a parked session may hold a seat, which
CARD-1065 now owns.

### Instruction gap

- Neither bundle nor owner doc says a Blocked session holds a seat. The orchestrator bundle
  (lines 44-50) and docs/orchestration-loop.md (lines 893-900) say what to do with a
  `[task ... blocked]` note (Continue, Reply, or surface `asks:`), never that leaving it costs
  a seat. docs/agent-card-lifecycle.md:101 says "A `Blocked` task counts as open - the card is
  still being worked, by whoever answers the question", and docs/orchestration-loop.md:787
  excludes Blocked from the concurrency gate, which reads as "Blocked is free".
- `server/Bundles/delegate-basics.md` requires pushing each slice and before long test runs
  (lines 19-40) but says nothing about pushing before reporting `blocked`; the 1fc4f3dd audit
  had to inspect each container worktree to confirm nothing was unpushed.
- No stage bundle (`stage-plan.md`, `stage-review.md`, `stage-test-design.md`, `stage-code.md`)
  mentions Blocked at all; a Review of the CARD-0657 or CARD-0667 slices had no prompt to ask
  "what happens to the seat while this task waits".
- The CARD-0811 retrospective trigger would not have fired: it needs a Clean Review row for a
  landed change (docs/orchestration-loop.md:486-520). The two audits are Debug tasks, and the
  first (`f23af0f1`) concluded "legitimate under the current rule, CARD-0667 exists" and filed
  nothing, which is the correct reading of its brief and the wrong outcome for the system.

Missing control: one sentence in each place (filed as CARD-1083) and a second trigger for
design-gap retrospectives (CARD-1084).

## 5. Preventive controls, in priority order

| # | Control | Type | Card | Why this order |
|---|---|---|---|---|
| 1 | Alert on an idle seat, on host occupancy above the Working count, and on any `orphan=true` slot; keep a durable per-host occupancy sample. | detection | CARD-1079 (High/Soon) | Would have fired on 2026-09-27 at the latest and named the seats on 2026-10-04 00:28Z, 33 hours before the audit. Cheapest and independent of CARD-1065's landing. |
| 2 | A complete report with only a desktop sync failure settles on its verdict with sync debt, never Blocked. | design | CARD-1082 (High/Soon) | Removes the feeder that produced 45 per cent of parked seat-time and every Reply-to-resync turn. Interacts with CARD-1043 and CARD-1065; the card says which choice closes it. |
| 3 | Instruction lines: Blocked holds a seat; push before parking; answer or cancel before leaving; read slot orphan flags at capacity; stage checklists ask about waiting sessions. | instruction | CARD-1083 (Normal/Soon) | Prose only, lands in a day, and keeps the orchestrator from re-creating the population before the code controls exist. |
| 4 | Give Blocked a clock and classify sync blocks apart from questions. | detection | CARD-1081 (Normal) | Makes a forgotten question visible on its own age and stops `done` reports looking like human questions. |
| 5 | Invariant test over every status for seat ownership; per-status release witness; rewrite the Blocked-keeps-session test into its bounded form. | test | CARD-1080 (Normal) | Must follow CARD-1065 so the bounded form has something to pin; prevents reintroduction. |
| 6 | Attention feed de-noising (collapse per task, severity filter, summary). | detection | CARD-1085 (Normal) | Control 1 and 4 add rows to a feed that already has 344; without this they are rows 3 and 4 of 350. |
| 7 | Retrospective trigger for audit-found design gaps, with an "older card already named it" check. | process | CARD-1084 (Low) | This retrospective exists only because an operator asked for it; the standing process should ask. |

Already owned elsewhere and linked rather than duplicated: park with pushed WIP and release the
seat (CARD-1065), terminal and orphan release with its dormant activation (CARD-0667), evidence
binding after a lease-busy block (CARD-1043), remote-prep lease contention and the 5-minute push
kill (CARD-0672, CARD-1076), consumer-slot release (CARD-0664).

## 6. Uncertainties

- Seat windows are reconstructed from desktop session `endedAt` and task events, not from the
  runner's own list; a runner process that died earlier than the desktop row would shorten a
  window. The eight at 08:01Z on 2026-10-05 are confirmed by the runner list in `f23af0f1`.
- The sample is the Antiphon board (plus unscoped rows); 163 tasks on other boards were not
  fetched, so server2 seat-hours and the 09-24 to 10-02 peaks are floors.
- The create-time text of CARD-0667 is not revision-logged; that it named Blocked-after-report
  on 2026-09-24 rests on the card's own 2026-10-01 appendix saying so.
- The desktop server log was not read; `HeldAged` and `Held` event rows stood in for it. The
  log would add the `LogHeldAged` ledger lines (`AgentTaskDispatcher.cs:1450-1454`) but no new
  facts about the seats.
- Whether checks ever reach a Blocked task was read from the sweep predicate and from the
  absence of post-Block `Check` events on the eight; 29 tasks did get a Check after a Blocked
  event, all after a Reply made them Working again.

## 7. Not done, noted

- No production code, bundle, doc or card other than the seven new Backlog cards and this file
  was changed. CARD-1077 itself was not edited; the orchestrator may link this artifact there.
- `card.ps1 new` printed a `Join-Path` error after each successful create (line 523, "card files
  NOT WRITTEN: board_not_opted_in"); the cards exist and read back. Worth a Low card if it
  recurs; not filed here because it is outside this retrospective's scope.
- Fix idea for CARD-1082, one line: in `AgentTaskReplyService.SettleAsync`, when
  `remoteBlock.Reason == LeaseBusy` and the runner's publication receipt proves the pushed SHA,
  take the report's verdict and record `remoteSync = Pending` for the reconcile job instead of
  marking Blocked.
