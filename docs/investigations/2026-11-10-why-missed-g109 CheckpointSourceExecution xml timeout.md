# Why missed: g109 CheckpointSourceExecution xml timeout

rc run 97150a0b (rc/2026-11-19 = ab7af12c699039270af7e22a5d7fd794035bc325), chunk
`antiphon-all-group-109`, native Windows desktop:
`CheckpointSourceExecutionTests.validators_agree_on_xml_character_references` failed at 36.8 s with
`OperationCanceledException`/`TaskCanceledException` from `CheckpointProcessRunner.RunCoreAsync`
via `CheckpointFixtureProcess.Run` in `ValidateCorpusWithScriptAsync`.

## Classification

TEST FIXTURE/TIMING DEFECT, load-sensitive. Not a product defect, not a regression of this cut.

- The test starts ONE `pwsh` that runs `scripts/validate-checkpoint-receipt.ps1` once per corpus
  case: 1 ordinary + 99 character references x 3 contexts (297) + 28 DTD/predefined cases = 326
  validator calls. Its single `CancellationTokenSource(TimeSpan.FromSeconds(30))` covered pwsh
  start, script load and all 326 calls together.
- The 30-s deadline was written by 47922be5c (2026-11-06) for small encoding corpora (a few cases).
  0da4b71ad (CARD-1217) reused it for the 297-case reference corpus ("Keep the existing 30-second
  child deadline"); 72ec6d5e1 added the DTD cases. Work grew about 50x, the budget did not.
- Whole-test durations on the native Windows Full runs (passing, child plus in-process parity loop):
  4e0d1159 28.5 s, 231f573c 27.1 s, dce78ee0 31.1 s, 6591e378 34.1 s, ae84478d 25.9 s,
  0b292fa4 26.2 s; failing 97150a0b 36.8 s (child cancelled at 30 s). The child has run within a
  few seconds of its fixed budget on every Windows run since it landed.
- No change in 56e96eabf..ab7af12c6 touches `scripts/lib/CheckpointProcessRunner.cs`,
  `scripts/validate-checkpoint-receipt.ps1`, `scripts/lib/checkpoint-source.ps1`,
  `scripts/lib/checkpoint-repeat.ps1`, `tests/Antiphon.Tests/Checkpoints/` or `tools/Antiphon.Checkpoints`.
- At 16:17Z the desktop was saturated (91-99% CPU, 42% kernel; group 109 itself runs 190 Checkpoints
  tests with p95 77 s and 1,157 s of process spawn time), so per-call pwsh cost rose past the budget.

## Fix (master)

`CheckpointFixtureProcess.RunStepsAsync` + `CheckpointStepDeadline`: the driver appends one byte to
a progress file per finished case. The child gets 30 s (the old whole-child number) to finish its
first case (pwsh start, validator load, one call), then 10 s between consecutive cases (idle cost is
tens of ms per case). A slow host stretches the run without failing it; a child that stops is
cancelled within one step budget, and the `TimeoutException` names the phase: "before the first
step finished" (startup) or "after k/N steps; stalled in step '<key>'". On success the helper prints
the observed startup and slowest step, so later budgets can be derived from TRX output (R5/R6).
Measured on the same desktop during run 97150a0b (loaded), with the fix: XML corpus child
326/326 steps in 20.1 s, first step 1.23 s (budget 30 s), slowest later step 0.69 s (budget 10 s,
200-ms polling granularity); the encoding corpora 7 and 13 steps in 1.5 s and 2.4 s.
Witnesses: `CheckpointStepDeadlineTests.step_deadline_*` (FakeTimeProvider, deterministic on
Linux) and `step_child_that_never_finishes_a_step_times_out_naming_startup` (real pwsh).

## Introducing commit/card

introducing commit/card: 0da4b71adc7c505e3abe1f38afcc200fa93c824b (CARD-1217), on top of the fixed
deadline from 47922be5c7c85061a78ecb9113f74b55109184b2 (CARD-1217 encoding repair) and the DTD corpus from 72ec6d5e1168e149eb2ab3ac8a4e0d8703a673ee.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- CARD-1217 Code task 8711a7d0 and its Codex Frontier review rounds (report committed in
  87bf11823 `.antiphon/task-8711a7d0.md`), scope Full on server2 Linux, selection included
  CP-SOURCE/CP-REPAIR/CP-100 `/*/Antiphon.Tests.Checkpoints/CheckpointSourceExecutionTests*/*`
  (14 executed, 14 passed at 58cefbae6).

## Outside the selection

why the failing test was outside that selection (lane/category/path): it was INSIDE the class
selection; it was outside the platform and load profile. The review ran on server2 Linux with
`waited=0s` build slots (lightly loaded), where the 326-call child finishes well inside 30 s. The
Windows desktop under a Full qualification run has much slower pwsh process and script costs.
Nothing checked the headroom of an inner child deadline against the measured duration: the R5
2x-p95 rule and the CARD-1563 timeout audit only see `[Timeout]` declarations and row caps, not a
`CancellationTokenSource` inside a fixture helper.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`scripts/validate-checkpoint-receipt.ps1`, `scripts/lib/checkpoint-*.ps1`,
`tests/Antiphon.Tests/Checkpoints/C1217ConditionalSkipFixture.cs` ->
`/*/Antiphon.Tests.Checkpoints/(CheckpointSourceExecutionTests*)|(CheckpointStepDeadlineTests*)/*`

That row only selects the classes; it would have passed on Linux. The check that would have caught
this cause is a headroom rule: a corpus-sized child must use a per-step deadline (as now) or the
Review must compare the inner deadline with the Windows TRX duration of the test.

## Common cause with the other timing failures of run 97150a0b

g76 channel recovery (8-s wait), the profile-16 sampler (25 ms) and this child (30 s fixed) share
one shape: a fixed wall-clock budget over work whose cost scales with host CPU, on a host at 91-99%.
The load is mostly the qualification itself (compilers about 1.3-1.6 cores, test hosts, test
Postgres about 0.9 core), plus standing Antiphon load measured in
`docs/investigations/2026-11-13-card-1886-antiphon-cpu-attribution.md`: Antiphon.Server about
0.95 core, its Postgres 1.3-3.4 cores in `docker stats` (55% of statement time is JIT, CARD-1886),
WSL/Docker vmmem about 1.3 cores, Defender about 0.4 core. What could run less during a
qualification run:

1. Postgres JIT off for the OLTP claim (CARD-1886): about 0.4-0.56 core.
2. Session-state seed/pins and transcript ingest re-probes (CARD-1887, CARD-1888): about 0.5 core.
3. Defender exclusions for the candidate checkout, `bin/obj`, `%TEMP%` test roots: about 0.4 core.
4. No concurrent desktop builds/tests from delegates while a Full run is live (route them to
   server2); this task's own build is one such load.
5. Fewer live desktop sessions during the run (about 0.08 server core each).

These reduce flake frequency; the durable fix for each test is still a condition- or step-based
bound like this one.

## Exemption

same cause as an already written note: none.
