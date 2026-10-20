# CARD-1376: dispose Playwright after a cancelled create wait

Repair of Review d4501974, based on 0e969cfe946a5f04d238600fa9a7a4048cc963f9.
Original Code task / landing owner: 5ec48582-e663-41b0-9964-653e055ff2f4.
Master-only and test-only; no image rollout or live container changes.

Extract the existing resource lifetime into a test-only generic seam, first preserving
the defect. Commit and run its deterministic cancelled-create witness before the fix.
Then retain ownership of the creation task in a finally: dispose a successful result
even if it completes after cancellation, and observe faults from abandoned creation
and cleanup tasks. Successful bodies and failed bodies must each dispose exactly once.
Keep the 60 s test, 30 s launch, 5 s DOM and 10 s close deadlines unchanged.

V-1: Cancelled_create_disposes_late_success cancels an incomplete task, observes the
cancelled caller, then completes creation with a disposable probe. The same seam used
by the browser test must dispose exactly once and never invoke the body. Baseline
fails the disposal-count assertion (actual 0, expected 1).

R-1: Successful_body_keeps_resource_until_return_then_disposes and
Failed_body_disposes_resource_and_preserves_exception preserve ownership and exceptions.

V-4: Run the parent plan's CP-8 selection exactly once on the existing S4 candidate image,
sha256:e1199bb6513d62accf75ed5559d9d5d234e10e3baa0112fce8501442e86ee554,
using its matching Playwright 1.63.0 browser and disposable app-user container.
Manual acceptance: no TUnit0015 in the smoke-test build.

The brief's explicit narrow repair selection and parent's no-whole-lane rule apply;
no full Unit/assembly run. The full affected classes are CreatedResourceTests (3)
and ChromiumRuntimeSmokeTests (1). CP-9 is tests-first; CP-10 and CP-8 below are
the final ordinary group, sharing one isolated build in that image. CP-8 retains
the parent's exact method filter and output path. Each uses committed expected-source-sha, fresh TRX and
validated clean source/build receipts. No repetitions after green.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-9 | repair-red | `tests/Antiphon.E2E -> bin-c1376-red/` | cancelled-create-red | `/*/*/CreatedResourceTests/*` | V-1 red, R-1 | 3 executed, 1 disposal assertion failure, 2 passed, 0 skipped | 3 | 6 |
| CP-10 | repair | `tests/Antiphon.E2E -> bin-c1323-s4g/` | cancelled-create-green | `/*/*/CreatedResourceTests/*` | V-1, R-1 | 3 passed, 0 failed/skipped | 3 | 6 |
| CP-8 | repair | `CP-10` | linux-candidate-image-green | `/*/*/ChromiumRuntimeSmokeTests/Chromium_launches_and_renders_local_content*` | V-4 | 1 passed, 0 failed/skipped | 1 | 1 |

### Cost

Ordinary floor 13 minutes including the tests-first row and CP-8's shared-build image run; 40-minute
repair budget includes authoring and tool bootstrap. No new image build is needed.
No named PC IDs: SourceLanding Mutation owns deliberate mutants, red/restore/green
and missing-control discovery after ordinary Review and caller landing.
