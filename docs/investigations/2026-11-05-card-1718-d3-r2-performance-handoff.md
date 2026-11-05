# Required Debug handoff: CARD-1718 historical checkpoint wall over 30 minutes

At 2026-11-05 05:15:35Z, run `20261105-044522-956f` exceeded 30 minutes wall time.
Source: `0b7f9070e95e57b7d265663436478339530f3121` (affected production bytes equal
Review base `249d4654d2410c4a3959a492ee0165e8a75b75f9`). Host: Debian GNU/Linux 12,
24 reported cores. The broker repeatedly reports occupied=4, budget=4. At 05:15:15Z,
host load average was 7.51 / 20.96 / 34.19. This Code delegate cannot sub-delegate;
the caller must commission Debug with the final checkpoint archive and this request.

This is a performance/verification follow-up, not an assertion regression or permission to
raise host budgets, widen timeouts, bypass admission, or repeat a passing selection.
Separate admission-only wall, actual builds, test execution, fingerprint/sealing and teardown.
Inspect current broker holder/queue behavior and competing selection durations using the
documented read-only broker/host surface. Do not change an operator-only host budget.

Exact commissioned selections:

- CP-1: `/*/*/(RunnerWorkspaceIgnoredContentFloorTests*)|(C1677RetentionEngineTests*)/(Disposable_override_cannot_release_a_secret_shaped_name*)|(Ignored_secret_files_survive_admission_on_dry_run_and_execute*)|(Phase_b_keeps_an_ignored_secret_planted_after_the_move*)|(Residue_retry_keeps_an_ignored_secret*)`
- CP-2: `/*/*/(WorktreeIgnoredContentClassifierTests*)|(C1677RetentionServerTests*)/(Both_classifiers_protect_the_secret_floor_under_a_disposable_override*)|(Ignored_secrets_survive_the_real_runner_and_a_lost_reply_replay*)`

At the threshold: CP-1 had 23 genuine expected assertion failures, no fixture/build failure,
10 seconds of actual test-driver execution (fresh TRX; final report supplies exact timing).
Its build slot waited 30 seconds and test slot 150 seconds. CP-2's build slot waited 225 seconds;
the build completed with verified provenance. CP-2 had not started tests: its separate test
lease was still waiting after 540 seconds. Do not describe that wait as a hung test.
The manifest was bounded at 25 minutes charged execution and 45 minutes hard wall.

Evidence roots (archive mapping will be recorded in the final Code report):
`/tmp/task-27e2f0b5-baseline/.antiphon/checkpoints/20261105-044522-956f/`, especially
`report.json`, `report.md`, `executor.log`, `state.history.jsonl`, build logs and row TRX.
The existing round-1 whole-Unit timeout remains a separate CARD-1720 obligation; this exact
historical selection was not a whole-Unit rerun.

The repaired-source run `20261105-045917-ce44` also crossed 30 minutes wall at approximately
05:29:17Z, source `40b176ce1225a40bd8290e77975633f17025c4d2`. Its manifest is the ordinary
three-row table in `docs/superpowers/plans/2026-11-05-card-1718-d3-r2-checkpoints.md`.
CP-1/CP-3 did not execute: the first runner build was provenance-refused because the inherited
Linux nested apphost producer rewrote pre-existing generated obj caches after the source SHA
changed. Tracked source stayed clean. CP-2's build slot waited 600 seconds and its test slot
330 seconds; its test execution started at 05:28:08Z. Thus the threshold is not evidence of a
30-minute test body. Its final report must supply actual execution/teardown and counts.
The refused runner rows were rerun once in fresh detached source-40b176ce1 worktree
`/tmp/task-27e2f0b5-runner-verify`, run `20261105-051349-41b0`, preserving normal provenance
and build slots. Include the four generated obj roots named in the first build log when
investigating repeated-checkpoint/cold-output costs; do not bypass confinement or edit csproj
inputs under this Code brief.
