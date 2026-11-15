# Run b7812833 (rc/2026-11-21 @ 64e3d78c487d): pace and finish estimate

Read-only analysis done 2026-11-15 at about 12:51Z. Nothing on the desktop was changed.

## Verdict

- **The pace is normal.** On the 137 chunks that have the same name in the previous run
  34d41b7e, this run took 8.08 h of chunk wall time against 8.50 h last time (ratio **0.95**).
  The median per-chunk ratio is 0.97.
- **The low TRX count is an ordering effect, not a slowdown.** The failed-first seed order ran
  the expensive chunks first: 59 prior-failure chunks (3.53 h), 49 heavy E2E splits, and 85
  heavy antiphon `all-group` chunks. The 770 antiphon `all-profile` chunks (about 14 s each) and
  the session-runner, pty-host and agents-pty profile chunks are all still to come. The previous
  run had only **369** TRX at the 8.3-hour mark. It reached 861 at 12.5 h only because it ran
  619 profile chunks between 17:31Z and 20:10Z.
- **Likely finish: about 20:35Z on 2026-11-15**, with summary.json about 2 minutes later.
  That is about 16.0 h in total; the previous run took 16 h 31 min. Band: low 20:16Z,
  realistic high 22:18Z. The formal slowest-20% high is 04:40Z on 2026-11-16 (see the band
  notes for why this is pathological).
- **The TRX count will catch up soon.** It should pass about 800 by about 15:56Z and be at
  about 890 by the 12.5-hour mark (17:03Z), on or ahead of the previous run's curve.

## Work done versus remaining

The "prev-equivalent" column prices every chunk at its actual wall time in 34d41b7e. It matches
by chunk name; where a name is new, it uses that suite and kind's average from 34d41b7e, for
example 13.9 s for an antiphon profile chunk. The run has 1,236 scheduled chunks, 166 done
(164 TRX), and `antiphon/all-group-136` was active at 12:50Z.

| Suite | Chunks | Done | Planned done (h) | Actual done (h) | Planned remaining (h) | Prev-equivalent remaining (h) |
|---|---:|---:|---:|---:|---:|---:|
| antiphon | 929 | 84 | 14.40 | 5.14 | 19.14 | 6.98 |
| e2e | 76 | 49 | 11.00 | 2.55 | 6.41 | 0.47 |
| session-runner | 130 | 31 | 0.07 | 0.27 | 0.34 | 0.55 |
| pty-host | 53 | 0 | 0 | 0 | 0.02 | 0.03 |
| agents-pty | 29 | 0 | 0 | 0 | 0.11 | 0.06 |
| markdown-pdf | 12 | 0 | 0 | 0 | 0.00 | 0.01 |
| client, scripts | 2 | 2 | n/a (no plan) | 0.14 | 0 | 0 |
| messaging, observability, server-git, resilience, git-in-process | 5 | 0 | 0 | 0 | 0.00 | 0.03 |
| **Total** | **1,236** | **166** | **25.47** | **8.10** | **26.02** | **8.14** |

- **Done:** 8.62 prev-equivalent hours took 8.10 actual hours.
- **Share of the run done:** 51% by prev-equivalent work, 49% by planned cost, but only 13% by
  chunk count.
- **Serial dispatch:** both runs ran one chunk at a time (measured maximum concurrency 1), with
  no idle gaps over 5 minutes. Summed chunk wall time equals elapsed time. The ~10 min setup
  (npm, lint, `dotnet-build` 461 s, planning) finished at 04:44Z.

### Actual time against planned cost (completed chunks)

| Scope | Actual / planned |
|---|---:|
| All completed chunks (aggregate) | 0.312 |
| antiphon (aggregate) | 0.357 |
| e2e (aggregate) | 0.232 |
| Per-chunk median | 0.34 |
| Per-chunk p80 | 2.4 |

The planned cost is poorly calibrated in both directions, so prev-equivalent is the better basis
for the projection:

- **E2E is overpriced.** Its `conservative-bound` price is 1,200 to 1,500 s per split. Real
  splits took 150 to 520 s, and the remaining E2E chunks took 0.47 h in total last run against
  6.41 h planned.
- **Profile chunks are underpriced.** 486 remaining `model` chunks are priced at 0.57 h in total
  (about 4 s each). A profile chunk really costs about 14 s each, which is mostly process
  start-up. That alone is about 1.3 h of underpricing.

### Same chunk names, this run versus 34d41b7e

| Suite | Matched chunks | This run (h) | 34d41b7e (h) | Ratio |
|---|---:|---:|---:|---:|
| antiphon | 84 | 5.14 | 5.67 | 0.91 |
| e2e | 49 | 2.55 | 2.47 | 1.03 |
| session-runner | 2 | 0.25 | 0.24 | 1.04 |
| client / scripts | 2 | 0.14 | 0.13 | 1.07-1.25 |
| **All matched** | **137** | **8.08** | **8.50** | **0.95** |

Sample chunks (wall seconds):

| Chunk | This run | 34d41b7e | Tests now / then | Note |
|---|---:|---:|---|---|
| antiphon all-group-133 | 1,521 | 712 | 444 / 249 | regrouped, prior failure |
| antiphon all-group-110 | 1,055 | 306 | 190 / 284 | slower this run |
| antiphon all-group-132 | 705 | 1,605 | 49 / 439 | regrouped (lighter) |
| antiphon all-group-109 | 460 | 1,161 | 48 / 190 | regrouped (lighter) |
| antiphon all-group-45 | 620 | 670 | 15 / 15 | identical membership |
| antiphon all-group-42 | 399 | 427 | 387 / 387 | identical membership |
| e2e all-split-9 | 517 | 541 | 2 / 2 | prior failure |
| e2e all-split-6 | 468 | 384 | 2 / 2 | prior failure |
| session-runner all-profile-16 | 567 | 566 | 1 / 1 | identical |

Where membership is identical, per-chunk wall time is within ±10%. The big ratios in either
direction come from chunks that were regrouped under the same name, with different test counts.

Ratio by time window (this run / 34d41b7e, matched chunks):

| Window | Chunks | Ratio |
|---|---:|---:|
| 04:00-06:00Z | 15 | 1.35 |
| 06:00-09:00Z | 57 | 1.04 |
| 09:00-11:00Z | 33 | 0.97 |
| 11:00-13:00Z | 32 | 0.69 |

The 59 prior-failure chunks, which ran 04:44-08:47Z, are 1.14x their previous duration. Failing
tests are slower: timeouts and retries inside the failing tests.

## Projection

The basis is the remaining prev-equivalent work of 8.14 h (including all of the active
all-group-136), started at 12:51Z. Add about 2 minutes for summary and report: last run, the
last chunk ended at 03:11:15Z and summary.json was written at 03:13:06Z.

| Band | Ratio applied | Remaining | Last chunk ends | Note |
|---|---:|---:|---|---|
| Low | 0.91 (antiphon matched aggregate) | 7.4 h | **~20:16Z** | |
| **Likely** | **0.95 (all matched aggregate)** | **7.7 h** | **~20:35Z** | Run total ≈ 16.0 h |
| Realistic high | 1.16 (p80 per-chunk ratio) | 9.4 h | ~22:18Z | |
| Formal high (slowest 20%) | 1.94 (weighted slowest-20% ratio) | 15.8 h | ~04:40Z 2026-11-16 | Pathological; see note |

- **The formal high is pathological.** The slowest-20% ratio is driven by regrouped chunks such
  as group-133, which has 444 tests now against 249 then. It is not a whole-run slowdown, so
  applying it to every remaining chunk overstates the risk.
- **The planned-cost cross-check sits inside the band.** Pricing the remaining planned cost at
  this run's per-suite ratios (antiphon 19.14 h × 0.357, e2e 6.41 h × 0.232, others at their
  previous actual time) gives about 9.0 h, a finish of about 21:50Z. That figure is pushed up by
  the E2E overpricing and down by the profile underpricing.

### Expected milestones (likely band, dispatch order)

| By about | What will have run | TRX about |
|---|---|---:|
| 13:05Z | antiphon all-group-136..139 | 170 |
| 13:19Z | antiphon all-split ×13 | 183 |
| 15:56Z | antiphon all-profile ×619 (the cheap block) | 802 |
| 16:30Z | session-runner groups ×5 and profiles ×47, agents-pty ×2, e2e splits 37-40, server-git | 862 |
| 17:36Z | mixed antiphon groups and profiles, session-runner profiles ×9, e2e groups ×2, antiphon splits ×5, git-in-process | ~915 |
| 18:46Z | antiphon groups ×10, e2e splits 41+ ×11, e2e groups ×8, pty-host/agents-pty groups, messaging, observability, resilience | ~955 |
| 19:30Z | antiphon/session-runner profiles, pty-host profiles ×51, agents-pty profiles ×25 | ~1,160 |
| 20:35Z | final antiphon groups 1-11, profiles ×31, markdown-pdf ×12 | 1,236 |

### Suites still ahead

| Suite | Remaining chunks | Planned (h) | 34d41b7e actual for those names (h) | Estimate vs actual |
|---|---:|---:|---:|---|
| e2e (splits and groups) | 27 | 6.41 | 0.47 | Planned is about 14x too high |
| session-runner | 99 | 0.34 | 0.55 | Planned is about 40% too low |
| pty-host | 53 | 0.02 | 0.03 | Close |
| agents-pty | 29 | 0.11 | 0.06 | Close |
| markdown-pdf | 12 | 0.00 | 0.01 | Close |
| antiphon (groups 57, profiles 770, splits 18) | 845 | 19.14 | 6.98 | Planned is about 2.7x too high |

## Is there a slowdown, and why the curve looks bad

- **Concurrency:** none in either run. Chunks run strictly one at a time (maximum overlap 1, gaps
  0.00 h this run and 0.03 h last run). Concurrency did not change.
- **Host contention:** there is no measured effect. Instantaneous CPU load was 81% on 8 logical
  CPUs at 12:51Z, with 8.8 of 31.9 GB free. The process list (names only) shows one
  Antiphon.Tests, 11 Antiphon.PtyHost, 18 node, 12 claude, 12 pwsh and the AppHost, Server and
  SessionRunner. The run-scoped CPU in supervisor-heartbeat (about 1,000-1,350 s per 10 minutes
  since 09:00Z, against about 250 s before) reflects the heavy `all-group` chunks, not foreign
  load. The per-chunk ratio has fallen to 0.69 since 11:00Z. No host-load history file is
  recorded under the log directory.
- **Reordering:** this is the whole explanation. The `operator-seed-order` ran:
  1. the 59 prior-failure chunks (4 h of wall time),
  2. then each lane resumed from its interrupted chunk: e2e splits and antiphon groups 2-135,
  3. leaving the cheap profile tail for the second half.

  The remaining dispatch order is essentially 34d41b7e's own order from 17:17Z onwards.
- **Timeouts and retries:** none visible.
  - Every completed timing file shows `lastPhase=complete` and attempt `-1`. The only incomplete
    one is the active all-group-136.
  - The supervisor log shows no hang suspicion (`hangSuspected=false`, `idleMinutes` at most 5.6).
  - The prior-failure chunks cost 14% more than last time, about 0.4 h, which is expected.
  - Two done chunks have a timing file but no TRX (166 done, 164 TRX).

## Pacing change (only if the pace became abnormal; not recommended now)

No change is warranted: the run is on track to finish in about the same time as 34d41b7e. For
a future run, the cheapest levers are below. All of them change the next run, not this one,
and need the operator.

1. **Profile-chunk launch overhead.** About 1,000 profile chunks cost about 13 s each in process
   start-up, about 3.5-4 h per run.
   - Lever: pack profile leaves into fewer launch groups in `ConvertTo-NightlyProfileLaunchGroups`
     in `scripts/lib/nightly-tests-impl.ps1`.
   - Knobs: `$script:NightlyLaunchTargetSeconds = 900` and
     `$script:NightlyLaunchMaxGroupOperands = 30`, at lines 220-222.
   - Cost: a code change through Code and Review, then a new rc cut and run.
2. **Seed order for operator-visible progress.** Interleaving the cheap profile lanes earlier
   would make the TRX curve look healthier without changing total time.
   - Lever: the order is written by `scripts/lib/nightly-stopped-run-seed.ps1`
     (`operator-seed-order`). It is cosmetic only.
3. **Parallelism.** Neither run used any chunk-level parallelism, and no parallelism setting
   appears in the run's timing data or the supervisor files (`start-supervisor.ps1` was started
   with no parallelism flag).
   - Adding it would be a design change, not a flag.
   - The Antiphon.Tests and Agents.Pty sequencing rule in AGENTS.md argues against it.
4. **Excluding a suite.** Not useful: everything except antiphon accounts for under 1.2 h of
   what remains.

## Method and sources (read-only)

- **This run:**
  - `C:\Antiphon\releases\candidates\rc-2026-11-21-64e3d78c487d\logs\2026-11-15-0434-b7812833\dispatch-schedule.json`:
    1,236 entries with order, suite, chunk and `priorFailure`. It carries no cost.
  - Planned cost (`plannedSeconds`, `pricingBasis`): `groups-planned-<suite>.json` under the
    timing root `...\logs\timing-b7812833d285499188e1fa4680a1fad1`.
  - Actual time: `startedAt`/`completedAt` in `<suite>-<chunk>.timing.json`.
  - Supervisor files: `active-chunk.json`, `setup-phases.json`, and `supervisor.log` and
    `supervisor-heartbeat.json` under `C:\Antiphon\releases\task-ff89ca56`.
- **Previous run:** the same files under
  `C:\Antiphon\releases\candidates\rc-2026-11-20-b559bfba2e9a\logs\2026-11-14-1040-34d41b7e`
  and `...\logs\timing-34d41b7e3ccc4ad4b29ed259749ec847`. All 1,206 of its chunks have timing
  files.
- A chunk counts as done when its TRX exists or its timing file says `lastPhase=complete`.
- All times are UTC; the desktop clock is UTC+0 in November.
