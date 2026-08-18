# CARD-0061 — Fibonacci check-in backoff: plan (finding: already shipped)

**Status: the DECIDED design is already implemented on master, in full.** This plan therefore
records the verification of that implementation against the card's DECIDED section, and closes the
two questions the card left open, rather than proposing work.

- `e573493` feat(delegation): CARD-0061 — fibonacci check-in ramp from 5m, capped at 60m
- `d3aabe5` feat(delegation): CARD-0061 follow-up — round check-in intervals to human-readable numbers

## Verification against the DECIDED section

| Decided | Shipped | Where |
|---|---|---|
| Fibonacci from a fixed 5-minute first interval; declared duration no longer scales the ramp | Yes — `expectedDurationMinutes` is dead in the arithmetic, kept on the signature so callers don't churn | `server/Application/Services/CheckSchedule.cs` (`NextInterval`) |
| Hard cap 60 minutes (up from 30) | Yes — `CheckMaxIntervalMinutes = 60`, `CheckMinIntervalMinutes = 5` | `server/Application/Settings/DelegationSettings.cs:356-359` |
| Pinned sequence 5, 10, 15, 25, 40, 60, 60 … | Yes — asserted as an explicit table, not a re-derived formula | `DelegationUnitTests.cs` → `CheckScheduleBackoffTests.the_ramp_is_a_fibonacci_sequence_from_the_base_capped_at_the_ceiling` |
| Rounding rule as a separate step (<30 → nearest 5; 30–60 → nearest 10; >60 → 60), not baked into the ramp | Yes — `CheckSchedule.RoundInterval`, its own method with its own test class; rounding provably leaves the shipped sequence unchanged | `CheckScheduleRoundingTests` incl. `the_shipped_ramp_sequence_is_unchanged_by_rounding` |
| Elapsed-time tables for 10m / 25m / 60m expected | Yes — pinned verbatim from the card | `CheckScheduleElapsedTimesTests` (10, 15, 25, 40, 65, 105 · 25, 30, 40, 55, 80, 120 · 60, 65, 75, 90, 115, 155) |

Re-verified 2026-08-18: `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-plan061/ -- --treenode-filter "/*/*/CheckSchedule*/*"` — **14/14 passed**.

## The two questions the plan had to decide

### `CheckMaxCount` stays 10

The card's own elapsed tables settle it. Ten checks under the new ramp reach ~345–395 minutes
elapsed (~6 hours) for every declared duration, with five checks inside the first 90 minutes — the
window a check earns its keep — and an hourly heartbeat after that. The old doubling ramp spent the
same 10-check budget by ~5 hours with only two useful early observations. So the gentler ramp
*improved* total coverage while front-loading it; the card's worry ("a 4-hour task should not go
dark because it used its 10 checks in the first 90 minutes") does not materialise — check #10 on a
default task lands at 345 minutes. A task genuinely longer than ~6 hours goes dark after the budget,
but that was equally true before and is a different card if it ever bites; the budget-spent path
already announces itself ("final check — the 10-check budget is spent",
`AgentTaskCheckService.cs:543`).

### No surface still shows the old 30-minute cap

Audited call sites and displays:

- **Call sites of the ramp/cap** — exactly one consumer: `AgentTaskDispatcher.cs:782-785`
  (budget check + `CheckSchedule.NextInterval`). No other code reads
  `CheckMaxIntervalMinutes` or reimplements the doubling.
- **Config** — no `appsettings*.json` overrides any of the three Check* settings; defaults rule.
- **Client/UI** — nothing in `client/src` displays the interval or cap.
- **Docs** — `docs/orchestration-loop.md` §4 already documents the new ramp, the 60-minute cap and
  the rounding step. The only remaining "30" is in
  `docs/superpowers/specs/2026-08-16-card-0047-delegate-check-ins.md` (lines 182/186/231), which is
  the original CARD-0047 design record — a point-in-time spec, correct for its date, superseded in
  prose by CARD-0061 and by `orchestration-loop.md`. Deliberately left untouched: specs here are
  historical records, not living config docs.

## Remaining action

Only card hygiene: CARD-0061 still sits in **Backlog** with the "Shipped now" section describing the
old doubling ramp. Move it to Done and let this plan + the two commits stand as the record.
