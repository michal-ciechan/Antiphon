# CARD-0128 S1 — PTY flake measurements

**Date:** 2026-08-21  
**Scope:** instrumentation and measurement only. No timeout, quiet-period, or product behaviour was changed.

## Result

S1 found three members in the CI-candidate solo configuration (A). All also fail in isolation,
so this is not a rotating contention-only cast. They are product defects/regression pins and S1
stops here as required; repair is deliberately deferred to the concrete S2 slices below.

### Instrumentation landed

- `OutputGapTimeline` records the live-buffer length every 25 ms and prints the complete timeline,
  max inter-growth/terminal gap, and `QuietPeriod` whenever the Claude done-detector or
  quiet-after-visible assertions fail.
- `FakeGrokContractTests.Body_then_separate_CR_submits` now names the failing assertion, captures
  the elapsed wait and both rendered-screen/raw-output dumps. Its launch helper always arms
  `ANTIPHON_FAKE_DEBUG_INPUT=1` and both FakeGrok/FakeClaude ready helpers report spawn-to-banner
  latency on a readiness failure.

The known detector tests did **not** fail in A, B, or C, therefore no output-gap failure timeline
was emitted. Their established S1 bucket is **(f) no reproduction in this matrix**, not an
unproven ping-starvation conclusion.

## Matrix

Runner verdicts below come from each saved log's `Test run summary`, not a pipeline exit code.

| Config | Runs | Result |
|---|---:|---|
| A — solo, machine as-is | 5 | A1 276/0/40; A2 276/0/40; A3 275/1/40; A4 274/2/40; A5 276/0/40 (succeeded/failed/skipped) |
| B — solo + six-worker CPU burner | 3 | B1 262/14/40 (14m06s); B2 265/11/40 (10m36s); B3 265/11/40 (10m07s). Burner alive at every run end. |
| C — deliberate concurrent `Antiphon.Tests` + PTY suite | 2 | C1: PTY 272/4/40, core 2084/12/4. C2: PTY 275/1/40, core 2079/17/4. These are saturation data, not A results. |
| D — isolation | 20 each for every A member | FakeClaude CRLF 14/6; FakeGrok CR 18/2; modern marker probe 19/1. D was stopped at the first confirmed bucket-(b) defects, per the task stop rule. |

Logs are under `logs/card-0128/run-<config>-<n>.log`; C additionally preserves each constituent
suite in `run-C-<n>-pty.log` and `run-C-<n>-core.log`.

## A cast: mechanism evidence and buckets

| Member | Observed evidence | Isolation | Bucket |
|---|---|---:|---|
| `FakeClaudeContractTests.SendLineAsync_with_CRLF_multiline_body_submits_as_one_intact_turn` | A3 raw output contains the complete `HEAD … TAIL` body but no `SUBMITTED:` marker. This recurred in 6/20 solo runs (for example D5/D10/D13/D15/D16), after 5.65–8.66s. A separate body then CR delivery is not reliably submitting. | 6/20 failed | **(b)** real product defect; the runner's production `SendLineAsync` contract is the regression pin. |
| `FakeGrokContractTests.Body_then_separate_CR_submits` | A4 and D6/D10 identify the exact failing assertion: submit marker and `Worked for 1.7s` are present, but the idle OSC title is absent after the complete 5s wait. The screen/raw dump rules out body merge/drop for this occurrence. | 2/20 failed | **(b)** real product/output-delivery defect; a terminal output record is being missed. |
| `PtyBracketedPasteContractTests.A_modern_conpty_delivers_the_markers_unchanged` | A4 and D16 throw `InvalidOperationException: no PROBE-SUMMARY under win-x64/OpenConsole`; D16 fails after 33.454s. The modern ConPTY probe sometimes produces no contract result at all. | 1/20 failed | **(b)** real modern-backend product defect; its contract test remains unmodified as the pin. |

## B-only and C-only cast

The following failures were not present in A. They are retained as saturation evidence but are not
claimed fixed or representative of CI. The task's bucket-(b) stop rule precluded their 20×
isolation series; they are deliberately deferred to their owning cards/slices.

| Members | Evidence | Bucket |
|---|---|---|
| B: `Child_emitting_1MB_does_not_OOM_runner`; `Spawn_dispose_loop_does_not_leak_processes`; `A_43KB_bracketed_paste_arrives_whole_with_clipping_armed`; `Deterministic_clipping_gives_identical_survivors_on_three_identical_trials`; `A_gap_between_writes_saves_the_chunk_that_would_otherwise_be_dropped`; `Clipping_is_off_by_default_and_a_two_chunk_body_arrives_whole`; `SendLineAsync_submits_a_turn_and_emits_idle_signal`; four `A_body_that_mangled_live_reaches_a_js_runtime_peer_whole(...)` arguments; `A_peer_that_blocks_between_reads_still_receives_every_byte`; `A_multi_kb_body_arrives_as_several_reads_inside_one_event_loop_turn`; `Slow_draining_child_still_receives_every_byte`; `Text_and_CR_in_one_write_does_not_submit(fakeclaude)`; `A_swallowed_enter_redraws_holds_the_body_and_the_next_enter_submits_it_once`; `The_inbox_conhost_delivers_no_bracketed_paste_markers` | B's valid six-worker CPU load was alive at end of all runs; members time out in fixed real-time wait windows under that load. | **(c)** provisional runaway-bound latency class; deferred because the confirmed A product defects stop this slice. |
| C PTY: `SendLineAsync_submits_a_turn_and_emits_idle_signal`; `Modern_child_first_output_arrives_without_the_da1_stall`; `Resize_after_start_does_not_throw`; `Stdin_round_trip_via_pwsh` | C1 had four, C2 only `Resize...`; C is intentionally known-red concurrent saturation. | **(f)** deferred concurrent-only observations, not a CI claim. |
| C core: all 29 failures in `run-C-1-core.log` and `run-C-2-core.log` (project-scope, managed-secret/profile, hook, turn-complete, and catalogue/import families) | None appeared in A because A runs only the PTY suite; they are external concurrent-saturation observations. | **(f)** deferred concurrent-only observations, not a CI claim. |

## Decision

No timing value may be widened from these data. The A members have passed the stricter test of
failing alone, and all three therefore select product-first fixes. In particular, CARD-0124 S4's
precondition — timing-sensitive detector tests deflaked or `[Explicit]`-gated — is **not met**:
the suite still has reproducible PTY product defects and the detector tests have only an
instrumented no-reproduction result.

