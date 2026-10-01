# CARD-0778 readiness control timing audit

At the D-1 review, the worst observed first read was 78.7 ms. A real five-second readiness or harness deadline has 63.5 times that cost; ten seconds has 127 times. A short poll or settle interval is not a deadline on an outcome: it can cause an additional observation, while the five-second maximum bounds that observation. The table covers every test method in the two requested files (17 methods); the card's 22 new methods also include methods in other classes.

| Class and method | Timed path and margin after repair |
|---|---|
| GrokStartupReadinessTests.Captured_idle_frames_stay_ready_while_spinner_redraws | Synchronous fixture replay; no wall-clock wait. |
| GrokStartupReadinessTests.Unknown_or_blocked_current_frames_never_become_ready | Synchronous classification; no wall-clock wait. |
| GrokStartupReadinessTests.Current_frame_overrides_raw_history | Synchronous classification; no wall-clock wait. |
| GrokStartupReadinessTests.Composer_change_or_blocker_restarts_settle | Tracker uses explicit elapsed values. Shared animated adapter helper has a 5 s real maximum (63.5x). |
| GrokStartupReadinessTests.Settle_requires_two_observations_and_elapsed_time | Tracker uses explicit elapsed values. Shared animated adapter helper has a 5 s real maximum (63.5x). |
| RunnerGrokAdapterReadyTests.Spinner_sequence_advance_does_not_prevent_positive_ready | Real adapter maximum 5 s (63.5x); fixture/screens are loaded before the wait. The 60 ms settle is not the deadline. |
| RunnerGrokAdapterReadyTests.Animated_sign_in_blocks_without_input_and_sets_launch_block | Real adapter maximum 5 s (63.5x); current sign-in frame returns immediately. |
| RunnerGrokAdapterReadyTests.Current_trust_is_answered_once_before_positive_ready | Positive trust path maximum 10 s (127x), trust sub-budget 5 s (63.5x); question-only negative path maximum 5 s (63.5x). |
| RunnerGrokAdapterReadyTests.Post_trust_blank_or_sign_in_is_not_ready | Blank/sign-in real maxima 5 s (63.5x); PC-24 held-read trust sub-budget uses a timer-capable controlled clock and a 5 s outer guard. |
| RunnerGrokAdapterReadyTests.One_deadline_covers_reads_trust_and_minimum_age | Late read uses a 5 s JumpClock maximum (63.5x) and explicitly advances past it. Held read, minimum age and trust-budget cases use timer-capable controlled clocks and 5 s outer guards. Zero maximum is synchronous. |
| RunnerGrokAdapterReadyTests.Floor_modal_invalidates_stale_positive_at_minimum_age | Timer-capable controlled clock; three independent 5 s outer guards (63.5x). |
| RunnerGrokAdapterReadyTests.Utc_jump_does_not_advance_monotonic_settle | Timer-capable controlled clock; UTC jump cannot advance the readiness deadline. Real 1 ms delays only schedule continuations; the 5 s outcome-stabilization/outer guards give 63.5x. |
| RunnerGrokAdapterReadyTests.Exit_and_cancellation_stop_without_input | Pre-entry cases are synchronous; between-observation real maximum 5 s (63.5x). |
| RunnerGrokAdapterReadyTests.Cancellation_during_held_read_escapes_without_more_io | Real maximum and cancellation guards 5 s (63.5x); infinite delay is released by caller cancellation. |
| RunnerGrokAdapterReadyTests.Cancellation_during_pending_poll_escapes_without_more_io | Timer-capable controlled clock; outer guard 5 s (63.5x). |
| RunnerGrokAdapterReadyTests.Cancellation_during_held_trust_write_escapes_without_more_io | Real maximum and cancellation guards 5 s (63.5x); infinite write is released by caller cancellation. |
| RunnerGrokAdapterReadyTests.Timeout_captures_last_frame_and_io_failure_preserves_failure | Deadline and unwritable capture arms use a timer-capable controlled clock. Last-frame jump has a 5 s real maximum (63.5x). Snapshot failure preloads `Ready`, advances a controlled poll, asserts two reads and the `SnapshotFailure` terminal outcome, then asserts false. All outer guards are 5 s (63.5x). |

The [matrix](2026-10-01-card-0778-control-matrix.csv) has 136 mutant runs: 17 controls, five idle and three under 24 CPU burners each. Every cell failed on its named outcome assertion; every mutation build compiled; the runner checked an empty tracked diff after each restoration. TUnit uses exit code 2 for one failed test here. The matrix records the implementation SHA used by each run. The final V-12 assertion change was followed by fresh PC-31, PC-41 and PC-44 matrices at `eac2dab80a39a47d47e48dcb75ece49a18982402`.

The first PC-44 matrix exposed an earlier, circular assertion: it compared the captured sequence to the runner's read count after the mutant's forbidden extra read. That assertion was repaired to check the known last observed sequence (`2`), then the read count is asserted separately at `readsAfterFailureDecision`. The repaired PC-44 matrix is 8/8 named red.
