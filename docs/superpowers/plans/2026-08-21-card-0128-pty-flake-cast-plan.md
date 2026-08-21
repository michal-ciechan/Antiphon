# CARD-0128 — deflake `Antiphon.Agents.Pty.Tests`: measure the rotating cast before touching a window

**Date:** 2026-08-21 · **Status:** plan (S1 not yet run — this doc designs the measurement; the fix
slices are deliberately contingent on what S1 finds). Method is CARD-0050's (.NET flake cast) and
CARD-0069's (client flake cast): **measure first, never widen a deadline on a guess.** Read
`2026-08-19-card-0050-dotnet-flake-cast-plan.md` before executing S1 — its reproduction protocol,
classification buckets, and "runaway bound vs scenario-gated window" distinction are used verbatim
here.

## What is known, and what is deliberately not yet concluded

**The card's evidence (2026-08-21, two independent investigations):**

- CARD-0124's CI feasibility pass ran the suite **solo, on an idle machine**, twice: 2 failures,
  then 1 — `ClaudeDetectorsTests.DoneDetector_returns_false_under_continuous_output`
  (`ClaudeDetectorsTests.cs:60`) both times ("done should be False but was True").
- Same-day re-verification: that test passed 3/3 **in isolation**, but a full-suite run failed a
  *different* test — `FakeGrokContractTests.Body_then_separate_CR_submits`
  (`FakeGrokContractTests.cs:104`; the card's `:128` is the test's last line — which of its four
  asserts actually failed was not recorded, and S1 must capture it).

A rotating cast that passes in isolation is CARD-0050's exact signature: contention removes the
latency, not the defect. But note the configuration difference from CARD-0050, because it changes
what S1 must reproduce: **CARD-0050's repro was the concurrent double-suite run; this flake fires
with the suite running solo** on a machine whose "idle" still includes the always-on session-runner,
AppHost, watchdog, Docker, and any live agent sessions. The CI-relevant configuration — and
therefore S1's primary configuration — is the solo run.

**Code-reading context gathered for this plan (facts, not conclusions):**

- `DoneDetector_returns_false_under_continuous_output` asserts that
  `WaitForQuietAfterVisibleAsync(QuietPeriod: 2s, MaxWait: 3s)` returns false while a `cmd` batch
  loops `echo noisy-%random%` + `ping -n 1 127.0.0.1 > nul`. **Each loop iteration spawns a
  `ping.exe` process.** CARD-0050 measured cold process starts taking **>6s** under saturation. If
  one ping spawn stalls >2s, the child *genuinely* goes quiet for the whole quiet period, the
  detector *correctly* reports done, and the test fails with the detector working exactly as
  specified — the test's own load generator would be violating the test's premise. This is the
  leading hypothesis, and it is only a hypothesis until S1 shows the output-gap timeline.
- The sibling `WaitForQuiet_returns_false_under_continuous_output`
  (`WaitForQuietAfterVisibleTests`) has the same shape. `PtyBackendEnvGuard`'s doc comment records
  that on the **modern** backend both tests fail deterministically (a continuously-echoing cmd loop
  reads as QUIET for 2s) — the suite runs inbox via the guard, so that is not this flake, but it is
  independent evidence that multi-second gaps in ConPTY delivery of a cmd loop's output are a real
  phenomenon, not a hand-wave.
- `FakeGrokContractTests.Body_then_separate_CR_submits` writes the body, `Task.Delay(25)`, then a
  lone `\r` — the raw two-write shape, **not** `EchoGatedSubmit` (its FakeClaude twin was moved onto
  the echo-gated helper in CARD-0050 S3; the Grok twin was not). That is defensible on Grok's
  measured contract — fakegrok treats a trailing `\r` on a merged burst as Enter anyway
  (`Program.cs` "every \r is Enter — including one trailing a text burst"), so a sub-12ms burst-gap
  merge should be *harmless* here, unlike FakeClaude. If S1 sees the submit assert fail, the
  mechanism is therefore probably NOT the CARD-0050 S3 merge race but something else: fake process
  starvation (its input pump polls on `Thread.Sleep(3)` and drains only after a 12ms quiet gap),
  the 45s ready banner, or the two turn-end waits (5s each). Which assert fails decides which.
- This exact test is **already named in CARD-0050 S5** as a rotating member seen under concurrent
  load and deliberately not chased. S1 finishes that deferred work.
- Every process-spawning class in the suite already carries `[ParallelLimiter<ProcessSpawnLimit>]`
  (1-wide lane, pinned by `ProcessSpawnLimitTests`), so spawn tests never overlap each other — but
  they fully overlap the suite's *non*-spawning tests, TUnit's own parallelism, and the machine's
  background services. The lane bounds spawn concurrency; it does not buy a spawn test CPU.

**What this plan refuses to pre-conclude:** whether either failure is (a) the test's load/scenario
generation collapsing under contention, (b) a real product defect the flake is correctly reporting
(CARD-0050 found one of these — the runner relaunch pipe race — and widening would have buried it),
or (c) a genuinely too-thin runaway bound. The card's "do NOT just widen quiet-period timeouts" is
already codified in CARD-0050's distinction: a **runaway bound** (success path returns early;
widening costs nothing) may be widened once the mechanism is established; a **scenario-gated
window** (widening merely delays the same false verdict) may not — it has to be restructured.

## S1 — measurement: reproduce, instrument, classify. No fixes in this slice.

S1's deliverable is a findings report (`docs/investigations/2026-MM-DD-card-0128-pty-flake-measurements.md`)
containing the full failure inventory, per-member mechanism evidence, and a bucket assignment for
every member — plus an updated S2 section in this plan. S1 ships **instrumentation code only**
(better failure messages / gap timelines), never behavior changes, never timeout changes.

### 1. Instrument before running (the failure must self-diagnose)

Staring at pass/fail counts cannot distinguish "child went quiet" from "detector misfired". Land
these first, as test-side diagnostics (CARD-0050 S1's `.timing` sidecar pattern):

- **Output-gap timeline for the quiet-period tests.** In `ClaudeDetectorsTests` (both done-detector
  tests) and `WaitForQuietAfterVisibleTests`, sample `(elapsedMs, liveBufferLength)` on a ~25ms
  cadence for the duration of the detector wait (a small helper task; `PtyAgentRunner.SnapshotText`
  / buffer length are already thread-safe). On failure, the assertion message prints the max
  inter-growth gap and the full timeline. This single number answers the load-bearing question:
  gap ≥ QuietPeriod ⇒ the child really stopped producing (scenario starvation — bucket a);
  gap < QuietPeriod with `done == true` ⇒ the detector returned quiet while output was arriving
  (product defect — bucket b, and the test just became the regression pin).
- **Per-assert identification in `FakeGrokContractTests.Body_then_separate_CR_submits`.** The four
  waits already carry distinct `because` strings; additionally dump `runner.SnapshotText()` and
  elapsed-per-wait on failure so the report can say *which* 5s window missed and what the screen
  held. Arm `ANTIPHON_FAKE_DEBUG_INPUT=1` for this class during S1 runs so fakegrok's own burst
  log is captured.
- **Wall-clock stamps on launch helpers.** `LaunchReadyFakeAsync` (Grok and Claude variants):
  record spawn→banner latency into the failure message. CARD-0050's spawn-latency class predicts
  this is where solo-run contention shows up first.

These diagnostics are permanent (they make the *next* natural flake self-reporting, CARD-0050 S4's
principle) — not scaffolding to delete after S1.

### 2. Run matrix

Build once to an alternate output path (daemons hold `bin/`): `--property:OutputPath=bin-c128/`
(**forward slash**; delete all `bin-c128` dirs when done, per CLAUDE.md). Run via
`dotnet run --project tests/Antiphon.Agents.Pty.Tests --property:OutputPath=bin-c128/ --no-build`
equivalents; tee every run's full console output to `logs/card-0128/run-<config>-<n>.log` (the
verdict line must survive output capping — CARD-0069's lesson; never read a Bash pipeline's exit
code).

| Config | What | Runs | What it establishes |
|---|---|---|---|
| **A — solo, machine as-is** | full suite alone, always-on services running as normal | ≥5 consecutive | The CI-candidate configuration and the one CARD-0124 measured flaking. Primary failure-rate baseline. Suite is 2m36s–3m41s, so this is ~15–20 min. |
| **B — solo + controlled CPU load** | full suite alone, with a deliberate CPU burner (e.g. `pwsh` spinning cores-2 busy loops) | ≥3 | Amplifies A if the mechanism is CPU starvation; a flat result here vs A is itself informative. **Verify the load process is still alive at run end** — CARD-0069's external-load experiment was voided by a dead burner and had to be recorded as inconclusive. |
| **C — concurrent double-run** | `Antiphon.Tests` + `Antiphon.Agents.Pty.Tests` simultaneously | ≥2 | CARD-0050's cheap, reliable saturation repro ("still rotates FakeClaude under saturation"). Maximum amplification; expect a superset of A's cast. Never report C failures as if they were A failures — CLAUDE.md forbids co-scheduling as a *normal* configuration precisely because this is a known-red shape. |
| **D — isolation** | each test that failed anywhere in A–C, alone, ×20 | per member | Confirms contention-dependence (expected green; a member that fails in isolation is a plain bug, not a flake — different track). |

Record per-run: failed tests + their assert/diagnostic output, per-test durations for the cast
members (TUnit prints per-test timing; keep the logs), and machine load context (what the always-on
stack was doing — a delegate build running concurrently is worth a note, per CARD-0069's
"merge agents always test in the worst cache state" finding).

### 3. Classify every cast member (CARD-0050's buckets)

Each test that failed anywhere in the matrix gets exactly one:

- **(a) Scenario starvation** — the test's own load/scenario generation violated its premise under
  contention (the ping-spawn hypothesis lives here). Fix = restructure the scenario, not the window.
- **(b) Real product defect** the flake is correctly reporting. Fix = product; the test is the pin.
  Do not touch its timings.
- **(c) Runaway bound too thin** — success returns early, the bound only catches hangs. Widening is
  sanctioned *with the measured latency in the commit message* (CARD-0050 widened probe timeouts
  5s→30s on exactly this justification).
- **(d) Scenario-gated window** — widening delays the same false verdict. Must be restructured
  (evidence-gated like `EchoGatedSubmit`, or moved off the real ConPTY — see S2 options).
- **(e) Teardown/fragility** — assertions passed, cleanup failed.
- **(f) No reproduction in the matrix** — ship self-diagnosing instrumentation (already done in
  step 1 for the known members) and record it as unestablished; never guess.

**S1 exit criteria:** every member observed in A (the CI configuration) has a bucket with mechanism
evidence (timeline/log excerpt, not narrative); the findings doc exists; this plan's S2 is rewritten
from "contingent shapes" to concrete slices. Members seen *only* in C may be recorded and deferred
(CARD-0050 S5 explicitly left concurrent-only rotation unfixed; the bar for this card is the solo
configuration, because that is what CI would run).

## S2+ — concrete product-first slices selected by S1

S1 is committed in `docs/investigations/2026-08-21-card-0128-pty-flake-measurements.md`. It found
three bucket-(b) members in configuration A, each reproducing in isolation. These are product
repairs, not timeout work; their existing tests remain unmodified regression pins.

### S2a — make `PtyAgentRunner.SendLineAsync` submit CRLF bodies deterministically

Trace the body/CR writes through the ConPTY/fakeclaude boundary and replace the fixed 20ms
body→CR assumption only with positive composer-delivery evidence. Preserve the production
two-write contract and prove the CRLF test green in repeated isolation and solo-suite runs. Do not
widen its 5s observation window.

**Designed:** `2026-08-22-card-0128-s2a-sendline-evidence-gate-plan.md` — mechanism traced (the
writer's 20ms clock vs ConPTY's ~14ms delivery jitter vs the receiver's 12ms burst window; the
CARD-0050 S3 `EchoGatedSubmit` analysis, never applied to the production primitive), fix shape
(delegate-based gate helper, tail-or-placeholder evidence, 20ms settle after evidence, bounded
fallback, single CR, no retry), and the deterministic deaf-start mechanism pin.

### S2b — preserve FakeGrok's idle OSC output through the PTY capture path

Trace the write of `IdleTitle` to the runner's raw buffer/screen and repair the loss or ordering
fault that leaves `SUBMITTED` and `Worked for 1.7s` present but drops the OSC title. Keep all four
named assertions and the S1 screen/raw dump on failure. No echo-gated-submit change is authorised:
S1 proved the submit itself succeeds in the failing shape.

### S2c — repair the modern ConPTY no-summary failure

Instrument `ConPtyProbe.RunAsync` sufficiently to identify whether OpenConsole fails to start,
the child does not write, or capture loses `PROBE-SUMMARY`; repair that product path and retain
`A_modern_conpty_delivers_the_markers_unchanged` unchanged as the regression pin. No probe timeout
widening is authorised.

### S2d — defer load-only and concurrent-only rotation

After S2a–c are green, re-run the B and C members recorded by S1. Any window widening requires
fresh per-member latency proof that it is a runaway bound; the C-only members remain data, not the
CI acceptance bar.

**S-final — the re-verification bar (fixed now, so it cannot be negotiated down later):**
- **5 consecutive green solo runs** (configuration A) on the machine as-is — the CI-candidate bar.
- **3 consecutive green sequential pair-runs** (`Antiphon.Tests` then `Agents.Pty.Tests`) — the
  configuration CARD-0050 S5 proved and CLAUDE.md documents.
- One concurrent double-run (C) executed and its result **recorded honestly** in the findings doc,
  pass or fail — it is data, not a gate (S5's concurrent bar was never met and this card does not
  claim it).
- Report to CARD-0124: whether S4's precondition ("timing-sensitive detector tests deflaked or
  `[Explicit]`-gated") is met, so the non-blocking scheduled Windows workflow can start gathering
  its N-green promotion data.

## Constraints carried from the repo's standing rules

- Never co-schedule `Antiphon.Tests` and `Antiphon.Agents.Pty.Tests` except as the deliberate
  saturation experiment (config C), clearly labeled as such.
- `--property:OutputPath=bin-c128/` with a forward slash; sweep all `bin-c128` dirs afterwards
  (`Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-c128 | Remove-Item -Recurse -Force`).
- Do not touch measured contracts: the DA1 3s stall floor (`ModernPtyDa1Tests`), delivery ceilings
  (`PtyDeliveryCeilingsTests`), the 12ms `ANTIPHON_FAKE_BURST_MS` (CARD-0050 S3 judged the
  [15,19]ms margin too thin to tune — re-tuning it requires new measurement, not this card's).
- `ProcessSpawnLimit` stays; any new spawning class S1/S2 adds takes the attribute
  (`ProcessSpawnLimitTests` enforces it).
- Headed/`[Explicit]` canaries stay out of every run in the matrix (they are 40 of the 316,
  skipped by default; the flake lives in the default set).
- A failure in the lane is a real defect unless it also fails at the base commit (stash and
  re-run) — no informal flaky-list may be reconstituted.

## Slicing rationale (one paragraph, for the record)

S1 is measurement-only because both prior flake casts proved the fix shape is unknowable in
advance: CARD-0050's cast contained a real product race that timeout-widening would have buried,
one dominant latency class where widening *was* correct, a teardown bug, and a non-reproducer —
four different fix shapes from one symptom; CARD-0069's cast turned out to be one global budget
with zero headroom, where the right fix was policy consolidation, not per-test tweaks. Pre-guessing
here would either bake in the ping-starvation hypothesis (plausible, unproven) or default to the
widening the card explicitly forbids. S2's decision table is written now so that S1's findings
select a fix mechanically rather than reopening the argument.
