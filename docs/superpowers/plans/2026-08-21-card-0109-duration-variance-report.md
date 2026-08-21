# CARD-0109 — Delegate build duration variance vs `-ExpectAbout`

**Date:** 2026-08-21
**Status:** Investigation complete — recommendation below, no code changed
**Card:** CARD-0109 (`7f222f91-35fb-4402-ad68-cfb9b3bd9241`)
**Evidence:** the live Antiphon board database (`antiphon-postgres`, 58 Code/opus dispatches), the
delegate/orchestration source and docs, and four wall-clock measurements taken on this machine today

---

## Verdict

**The estimates are not biased, and inflating them is measurably the wrong fix. What is missing is a
*second term*: the verification cost, which is fixed, large, and knowable from the brief before
dispatch.**

Three findings carry the recommendation:

1. **`-ExpectAbout` has no prescribed heuristic anywhere in the repo.** Five files mention it; every
   one describes what the number *does*, none says how to *pick* it. It is a guess each time — that
   should be named, not continued.
2. **On the day the card is about, the median build finished at 0.98× its estimate.** The estimator's
   *centre* is right. What is wide is the *spread* (0.27×–2.56×). And the one time estimates *were*
   inflated across the board — the 08-18 CARD-0084 series, average estimate 188 minutes — the median
   ratio collapsed to **0.11×**. That is the card's "don't just raise the numbers" instinct confirmed
   with data, not asserted.
3. **The dominant predictable cost is not tooling and not model speed — it is which test assemblies
   the change forces you to run.** Measured today: the full `Antiphon.Tests` assembly is **~12
   minutes** and genuinely exceeds a single 10-minute foreground window; a build that must also
   re-verify pre-existing red at the base commit pays that **twice**, ≈25 minutes, *before any
   authoring time at all*. A build that touches no shared surface pays **none** of it. That single
   binary distinction explains the extreme ends of the card's own table.

So: keep the estimates honest and centred, add the verification term explicitly, report the number as
a **band**, and fix two concrete inefficiencies found on the way (§5).

---

## Evidence read

| Source | What it settled |
|---|---|
| `scripts/delegate.ps1:67-75, :188` | What `-ExpectAbout` is documented to be, and that omitting it sends nothing |
| `.claude/skills/antiphon-delegate/SKILL.md:82, :138-164` | The only caller-facing guidance: "declare the honest duration", no method |
| `docs/orchestration-loop.md:145-153` | Same, from the orchestrator's side |
| `server/Domain/Entities/AgentTask.cs:132-146` | "A HINT, NEVER A DEADLINE" — nothing fails or escalates off it |
| `server/Application/Services/AgentTaskService.cs:101`, `Settings/DelegationSettings.cs:398` | Resolution and the default of 10 |
| `server/Application/Services/AttentionService.cs:346-376, :447-453` | The *one* real consequence — and its mid-turn exclusion |
| `server/Application/Services/AgentTaskDispatcher.cs:466, :483`; `AgentTaskReplyService.cs:532` | The 10-minute watchdog that fabricates false durations in the data |
| `server/Bundles/delegate-basics.md:26-30` | "re-run **the failure** there" — singular; and no verification protocol anywhere in any bundle |
| `AgentTasks` table, live DB | 58 Code/opus/ClaudeCode succeeded rows with estimate, actual, tokens, cost, goal text |
| Four wall-clock measurements, this machine, 2026-08-21 | Build floor, TUnit startup floor, Application chunk, full assembly |

---

## 1. Is `-ExpectAbout` set from any real signal? (card question 1)

**No. There is no heuristic to follow, and the number has almost no consequence.**

### 1.1 Nothing in the repo says how to choose it

A repo-wide search for `ExpectAbout` returns five files. Every one documents the *effect*:

- `scripts/delegate.ps1:67-72` — *"Roughly how long you expect this to take... It is a HINT, NEVER A
  DEADLINE: nothing fails, escalates or cancels a task for running past it, so declare an honest
  number rather than padding it."*
- `SKILL.md:162-164` — *"Declare the honest duration: padding it just delays the first check, and it
  doesn't buy the delegate more time to run."*
- `AgentTask.cs:138-146` — *"It is a HINT, NEVER A DEADLINE... All it does is decide when the FIRST
  check-in happens."*

"Declare the honest duration" is an instruction to be sincere, not a method. Nothing anywhere maps
scope, slice count, file count, or brief content to minutes. **The orchestrator is guessing, and the
docs never claimed otherwise — the card's suspicion is correct.**

### 1.2 The number has exactly two consequences, and the second one is disarmed for the case that matters

1. It schedules the **first check-in** (`AgentTask.NextCheckAt`). The ramp after that is fixed —
   5, 10, 15, 25, 40, 60… — and does **not** scale with the estimate (`SKILL.md:138-143`). So a
   90-minute estimate buys one late first check and nothing else.
2. It sets the `PastExpectedIdle` attention threshold, `max(2 × expected, expected + 30m)`
   (`AttentionService.cs:447-453`).

The second one looks like it makes the estimate matter. It does not, for overruns:

```csharp
// AttentionService.cs:346-356 — THE exclusion lives here: a session that is mid-turn
// is not listed, however far past the estimate it has run.
var working = await SessionMessageQueueService.IsWorkingAsync(...);
if (!working) { /* only then raise PastExpectedIdle */ }
```

**A delegate that is still working is never flagged, at any multiple of its estimate.** Every overrun
in the card's table was a working delegate. So none of them produced any signal anywhere. The
estimate's only live use is "when does the first progress note arrive" — which is why over-padding is
*worse* than under-estimating: it delays the one thing the number actually buys you.

### 1.3 Measured: the estimate carries almost no information about duration

Over the 24 comparable Code/opus builds since check-ins shipped (cohort defined in §2.1):

| Predictor of actual duration | Pearson r | r² | Known before dispatch? |
|---|---|---|---|
| **`-ExpectAbout` estimate** | **0.338** | **0.11** | yes |
| Length of the `-Goal` brief | 0.594 | 0.35 | **yes** |
| Output tokens the delegate spent | 0.731 | 0.53 | no |

**The brief's own length predicts duration three times better than the estimate does.** On the 08-20
cohort alone (n=9) the estimate's correlation is *negative*, −0.498 — though at n=9 that is
noise-prone and I would not lean on it.

This is the crux of question 1: the number is not merely a guess, it is a guess that is
out-predicted by a quantity sitting in the same dispatch call.

---

## 2. What the data actually says about variance

### 2.1 Two data hazards that must be handled first

Anyone re-running this analysis will get wrong answers without these.

**(a) A 10-minute watchdog fabricates durations.** Sixteen rows have `CompletedAt − DispatchedAt`
between 10.05 and 10.25 minutes — an implausible spike. They are not real durations. The dispatcher
settles a task at 10 minutes when its session cannot be correlated
(`AgentTaskDispatcher.cs:466, :483`), and the recovery path writes `Result = "Recovered from an
unbound session; work is at <transcript>"` with **`Status = Succeeded`**
(`AgentTaskReplyService.cs:532`). The delegate may have run for an hour after that stamp. Eight of
these fall inside 2026-08-20 alone (CARD-0075 S1, CARD-0020 ×2, CARD-0099 S1/S2, CARD-0101 ×3) —
earlier, glitched dispatches of tasks that were then re-dispatched successfully. **Filter
`Result NOT LIKE 'Recovered from an unbound session%'` or every conclusion is wrong.**

> Worth noting on its own: the board shows those tasks as *Succeeded in 10 minutes*. Anyone reading
> durations off the delegations board — human or agent — is reading fiction for those rows.

**(b) Everything before 2026-08-16 15:20 used the default.** `-ExpectAbout` only became usable when
CARD-0047 shipped check-ins (08-16 14:17). All 16 earlier Code/opus rows carry `est = 10`, the
`DelegationSettings.DefaultExpectedMinutes` default. Their ratios (up to **13.9×**) measure nothing
but "the parameter was not passed".

Cohort used throughout: `Role=Code, ModelLevel=High, AgentKind=ClaudeCode, Status=Succeeded`,
dispatched ≥ 2026-08-16 15:00Z, watchdog artifacts excluded → **34 rows**, of which 24 excluding the
08-18 anomaly below.

### 2.2 Accuracy by day — the estimator's centre is fine

| Day | n | avg est | avg actual | **median ratio** | range |
|---|---|---|---|---|---|
| 08-16 | 5 | 43m | 34m | 0.81× | 0.55–0.99 |
| 08-17 | 10 | 44m | 36m | 0.80× | 0.45–1.36 |
| **08-18** | 10 | **188m** | **19m** | **0.11×** | 0.03–0.26 |
| **08-20** | 9 | 68m | 76m | **0.98×** | 0.27–2.56 |

Read the last row carefully. **On the day this card is about, the median build landed within 2% of
its estimate.** The card's framing — "some finish at a quarter of budget, others run 2.3× over" — is
about the *tails*, and both tails are real, but there is no central bias to correct.

### 2.3 The 08-18 row is the card's own argument, already run as an experiment

The CARD-0084 Grok series was estimated at 120, 180, 240, 360 minutes and finished in 26, 20, 13, 35.
Ten dispatches, median **0.11×**, worst **0.03×**. Somebody already tried "when unsure, pad it", at
scale, and the result was a set of numbers that conveyed nothing — a 360-minute estimate on a
35-minute task is not a cautious estimate, it is a non-estimate. **This is the empirical backing the
card demanded before anyone raises numbers across the board. Do not.**

### 2.4 Correction to the card's table

The card lists CARD-0103 S1+S2 as *45m estimate → 48m56s actual → 1.09×*. The dispatch record says
`ExpectedDurationMinutes = 60`, so the true ratio is **0.82×** — an under-run, not an over-run. The
actual durations in the card's table otherwise match the DB well. The headline range 0.27×–2.56×
stands.

### 2.5 Correction to the card's aside about Plan dispatches

The card says Plan/fable dispatches "ran consistently fast and close to estimate". Half right. Over
42 Plan/fable rows in the same window: average estimate **29.3m**, average actual **12.4m**, median
ratio **0.42×**. The *durations* are the most consistent of any cohort (sd of ratio 0.32, range
2.7–39m); the *estimates* are more than double them. Plan work is the cohort most in need of smaller
numbers, not larger.

---

## 3. Are full-suite regression re-runs redundant? (card question 2)

**The chunking is real and unavoidable. The redundancy is somewhere else, and it is worth ~11 minutes
a build.**

### 3.1 Measured today, on this machine

| Measurement | Result |
|---|---|
| Cold build of `Antiphon.Tests` to a fresh `--property:OutputPath=bin-card0109/` | **105 s** |
| Warm rebuild, nothing changed | **65 s** |
| `dotnet run` a filter matching **zero** tests (MSBuild + host start + discovery) | **88 s** |
| `Antiphon.Tests` `Application` namespace: 1414 tests | **545 s wall** (7m44s execution) |
| `Antiphon.Tests` **whole assembly** (~1930 tests) | **killed at the 600 s cap — did not finish** |

Extrapolating from the `Application` share (1414 / ~1930 ≈ 73%): **the full assembly is ≈ 12 minutes.**

**So CARD-0106's report was right.** Its line *"Regression, chunked (the full assembly exceeds a
single 10-min run)"* is a correct statement of fact, verified independently today. That build's six
invocations were not excess caution. (Five other 08-20 builds *do* report a single whole-assembly
total — 1903, 1911, 1922, 1923, 1964 tests — so it sometimes squeaks under the wire depending on
machine load. It is a coin-flip against the window, which is the worst possible place for a
mandatory step to sit.)

### 3.2 The real redundancy: full-suite re-runs at the base commit

Every build that finds red must prove it is inherited. `server/Bundles/delegate-basics.md:26-27` says:

> *"Stash your changes, or check out the base commit, and **re-run the failure** there."*

**The failure. Singular. Targeted.** Two protocols are visibly in use on 08-20:

- **Targeted (correct).** CARD-0103: *"checked out base commit `ea70830` and re-ran that suite — same
  3/6 failures, same message"*. CARD-0106: *"the identical three fail the same way at base `4eb381c`
  in a clean throwaway worktree"*. Cost: ~1–2 minutes.
- **Whole-assembly (wasteful).** The CARD-0101 continuation: *"`Antiphon.Tests` @ base `aa1c8f1` |
  1878 total, 33 failed — identical set"*. CARD-0099 S3: *"Verified against the base commit
  `2755613`: **1924 total**, 4 failed, the same four test names"*. Cost: **~12 minutes**, to confirm
  three or four already-named test methods.

Re-running 1,900 tests to check four is roughly **11 wasted minutes per occurrence**, and it happened
at least twice in nine builds. Both of those builds are in the card's overrun set.

### 3.3 Root cause: the delegate is told no verification protocol at all

Searching `server/Bundles/` for any test-running guidance — *full suite*, *targeted*,
*treenode-filter* — returns **nothing**. `AGENTS.md`, `docs/orchestration-loop.md` and the delegate
SKILL likewise contain no "run the full suite once, then targeted re-runs" rule.

The card asks whether build agents are following this session's established discipline. **They cannot
be: it was never written down anywhere a delegate can see it.** `delegate-basics.md` covers output
paths, backgrounding, committing and pre-existing red — but says nothing about how much to run or how
often. Every build invents its own protocol, and the spread between "targeted base re-run" and "whole
assembly twice" is ~11 minutes of pure, invisible variance in a 45-minute budget.

That is the single most actionable finding in this report, and it is a documentation fix.

---

## 4. Should live-CLI work get a wider estimate, and is there a tooling floor? (card question 3)

### 4.1 Live/headed work is already priced correctly on the mean — its *spread* is what is wider

Classifying the 24-build cohort by whether the brief calls for live/headed work against a real
external CLI:

| Class | n | avg estimate | avg actual | median ratio | **sd of ratio** |
|---|---|---|---|---|---|
| Live/headed CLI | 8 | 63.1m | **62.3m** | 0.85× | **0.62** |
| Pure in-repo | 16 | 47.8m | 44.4m | 0.80× | 0.49 |

The estimator is *already* charging live work ~32% more (63 vs 48 minutes) and the average actual
lands within **48 seconds** of it. **Adding a fixed "+N minutes for live CLI" would over-correct a
term that is already right.** What differs is dispersion — sd 0.62 vs 0.49, and the worst live
overrun is 2.29×. The correct response to a wider distribution with a correct mean is to **report a
wider band, not to move the centre.**

### 4.2 There is no meaningful tooling floor

The floor is **65–90 seconds per verification cycle** (§3.1) — MSBuild evaluation, host start,
discovery — before a single test runs. Real but small. Two checks confirm it is not what inflates
builds:

- The card asked for a deliberately tiny build as a control. One already exists: **CARD-0084 S1,
  "normalize Windows line endings in Grok tests" — 5.2 minutes end to end, 11.7k output tokens.** A
  whole dispatch, launch to report, in five minutes. There is no 20-minute floor.
- The card's own instinct about CARD-0107 S1 (24 minutes for a substantial adapter) was right, and
  §4.3 explains why it was even faster than it looks.

**The large fixed cost is not tooling. It is the shared-assembly regression protocol — ~25 minutes
when triggered, ~0 when not.** That is the term missing from estimates.

### 4.3 The actual predictor: which test assemblies the change forces

The two extremes of the card's table are explained by one binary fact available at dispatch time.

**CARD-0107 S1 — 90m estimate, 24m actual, 0.27×.** Its report: *"129/129 tests pass in
`Antiphon.Messaging.Tests` (67 new Slack tests + 62 pre-existing, untouched)"* and, decisively,
*"**Nothing constructs `SlackChannelAdapter` yet** — the project is compiled and tested but not wired
into the host."* A greenfield, unwired project has **no shared-surface exposure**: no full
`Antiphon.Tests` run, no base-commit re-verification, ~25 minutes of mandatory verification simply
not incurred. It was not a fast build; it was a build that skipped the expensive half legitimately.

**CARD-0099 S2 (2.29×) and the CARD-0101 continuation (2.56×)** both touched shared launch/session
surface, both ran the full assembly, and both re-verified at base — one of them by re-running all
1,878 tests.

So the estimate should be built from two terms:

```
estimate  =  verification floor  +  authoring time
```

**Verification floor**, from what the brief touches — knowable before dispatch:

| What the change touches | Floor |
|---|---|
| A new or isolated assembly only, not wired in (`Antiphon.Messaging.Tests`, `SessionRunner.Tests`) | **~5 m** |
| One namespace of `Antiphon.Tests`, no shared surface | **~10 m** |
| `server/Application`, launch/session/queue, or anything cross-cutting → full assembly | **~15 m** (chunked) |
| …and red is plausible, so a base-commit re-verification is likely | **+5 m targeted** (not +12 m — see §5.1) |

**Authoring time**, from brief size — the regression over the 24-build cohort is
`minutes ≈ 6.3 + 10.7 × (brief kilochars)`, and bucketed it behaves like this:

| Brief length | n | median actual | observed range |
|---|---|---|---|
| 2.5–4.5k chars | 14 | 33 m | 20–45 m |
| 4.5–6k chars | 6 | 78 m | 24–115 m |
| > 6k chars | 3 | 59 m | 58–120 m |

Note what happens above ~4.5k characters: the median rises, but the **range explodes** (24–115). Long
briefs are not just longer jobs, they are *less predictable* jobs. That is the honest signal to
report — and it is the answer to "which builds are likely to need discovery time": **the long-brief
ones, regardless of whether they mention live CLI work.**

### 4.4 What does *not* predict overruns — tested and rejected

I tried to find a textual tell in the brief for the discovery-heavy builds, since that is what the
card hoped for. Classifying the cohort by contingent language (*measure … then*, *root-cause*,
*diagnose*, *remaining items*, *continuation*, *retry*) gives:

| Class | n | median ratio | mean ratio | over 1.0× |
|---|---|---|---|---|
| Contingent | 12 | 0.81× | 1.03× | 3 / 12 |
| Bounded | 12 | 0.86× | 0.91× | 2 / 12 |

**That does not separate.** Nor does the live/headed classifier (§4.1). The three big overruns
(CARD-0099 S2, CARD-0101 continuation, CARD-0108) share an obvious property *in hindsight* — each
found a real defect the brief did not know about — but no pre-dispatch text feature isolates them
from the builds that didn't. This is the card's second option, and it is now tested rather than
assumed: **the discovery overruns are genuinely unpredictable from the brief.**

---

## 5. Recommendation

### 5.1 Two concrete fixes (real minutes, low risk)

**(a) Put a verification protocol in `server/Bundles/delegate-basics.md`.** It is the one document
every delegate is handed, it already covers output paths and pre-existing red, and it is silent on
this. Proposed addition, in the bundle's existing voice:

> - RUN THE FULL SUITE ONCE, THEN TARGET. `Antiphon.Tests` is ~12 minutes and does not reliably fit
>   one 10-minute foreground window — chunk it by namespace (`--treenode-filter
>   "/*/Antiphon.Tests.Application/*/*"`). After a fix, re-run only what you touched. When you verify
>   that red is pre-existing, **re-run the failing tests at the base commit, not the assembly** —
>   confirming four known test names costs one minute targeted and twelve full.

The second sentence alone recovers ~11 minutes on any build that finds inherited red, and it makes
`delegate-basics.md:26-27`'s existing "re-run the failure" operational instead of ambiguous.

**(b) Stop the 10-minute watchdog from writing fiction into the duration record.** A task settled by
`AgentTaskReplyService.cs:532` reads as *Succeeded in 10 minutes* on the board and in every query.
Sixteen rows are affected, eight of them from a single day. Whatever else changes, durations should
not be silently wrong — this needs its own card (§6).

### 5.2 The estimation heuristic

Replace the single gut number with two terms and a band:

1. **Verification floor** from §4.3's table — driven by which assemblies the change forces, which is
   knowable from the brief. This is the term that is missing today, and it is the largest fixed cost
   in the system (0 to ~25 minutes).
2. **Authoring time** from brief size — ~10 minutes per 1,000 characters of `-Goal`, +6.
3. **Report it as a band, not a point.** Historical dispersion for this cohort is roughly **0.6×–1.4×
   for briefs under 4.5k characters** and **0.3×–2.6× above it**. Say the band out loud in the
   dispatch note.

Worked against the card's own table: CARD-0107 S1 → isolated new assembly (~5m) + 4.8k brief (~58m)
≈ 60m, band 20–130 — the actual 24m is inside it, where the flat 90m point estimate was simply wrong.
CARD-0099 S2 → full assembly + base re-run (~20m) + 4.9k brief (~59m) ≈ 79m, band 25–170 — the actual
103m is inside it, where the 45m point estimate had no room for its own verification step. **Both of
the card's headline outliers stop being outliers.**

### 5.3 And say what the number is, when reporting it

Given §1.2 — nothing escalates, and a working delegate is never flagged however far over it runs —
`-ExpectAbout` should be described to callers as **"when do you want the first progress note, and
what band do you expect"**, not as a budget. A ratio in a report ("83m against 75m") reads as a
variance against plan; it is not one, and treating it as one is what produced the 08-18 series. The
honest report line is *"finished in 83m; band was 45–105m"* — and an actual outside its band is the
only thing worth explaining.

---

## Deliberately not in scope

- **Changing any code.** Plan-only dispatch; nothing was edited. The two fixes in §5.1 are proposals.
- **Re-litigating CARD-0099 S2 / CARD-0101 / CARD-0108.** The card rules these settled: real defects
  found, time well spent. Nothing here contradicts that; §4.4 only tests whether they were *knowable
  in advance*, and concludes they were not.
- **Codex/Grok/fable cohorts.** The card scopes to Code/opus. The Plan/fable correction in §2.5 is
  offered only because the card asserts something about it that the data does not support.
- **Per-test performance work on `Antiphon.Tests`.** The 12-minute assembly is a fact this report
  measures and prices; making it faster is a different, much larger piece of work.

---

## Hazards for whoever acts on this

- **The `bin-card0109/` measurement artifacts are deleted** (13 directories, all removed; the
  trailing-space check is clean). Anyone repeating §3.1 must use a forward slash on `OutputPath` and
  clean up after — see `CLAUDE.md`.
- **The 12-minute figure is machine- and load-dependent.** The `Application` chunk measured 545s wall
  today with the always-on stack running. Five 08-20 builds fitted the whole assembly under 600s.
  Treat ~12 minutes as the planning number, not a constant.
- **n is small at the tails.** The 08-20 cohort is 9 builds; the live/headed split is 8 vs 16. The
  day-level medians and the §4.3 buckets are sound; do not push the correlations further than §1.3
  states them.
- **§2.1(a) applies to any future analysis of this table.** Without the watchdog filter, 16 rows of
  fiction sit in the middle of the distribution.
