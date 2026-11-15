# CARD-0934 rollout Plan canary

## Decisions

D-1. Limit this canary to reading, a twelve-line gate note and an identity report: its acceptance is proving a Plan task starts, runs and reports. Reject deployment, builds and tests because they are outside this bounded canary.

D-2. Preserve the nine-gate order in [the note](2026-11-15-rollout-canary-note.md), including the four-hour drain cap, retirement token expiry and refusal handling. Reject an invented promotion command: the script has no independent promotion phase. Keep a runner accepting throughout.

D-3. The active Plan-stage contract requires a canonical card plan and commit/push; retain the specifically requested note as well and report the deviation from the brief's one-file/no-push restriction. Reject changing product files, merging, landing or executing any rollout phase.

## Ground truth

| Brief assumption | Repository / observed behavior |
|---|---|
| Nine gates map to nine deployment phases. | `scripts/deploy-server2.ps1` exposes five rollout phases; smoke gates are separate verification, gate 3 has no phase, and gate 5 is the wait inside `drain-old`. |
| Promotion changes the runner default. | The owner and `DefaultRunnerRoutingPolicy` describe a drain redirect; the configured preference stays unchanged. Read-only GET `/api/runner-defaults` returned revision 2, global preference `server2`, no per-kind overrides. |
| The task uses temp and a dedicated checkout. | Assignment pins `server2-temp`; cwd is `/work/worktrees/task-7bb75309`, a per-task `task-<id>` directory. GET `/api/session-runners` returned temp as Linux, available, dispatch-eligible and accepting. Hostname is `bfd2093a095a`; CLI is `codex-cli 0.160.0`. |
| Runner build and checkout SHA are interchangeable. | Live GET `/api/session-runners/server2-temp/status` confirms runner build `64e3d78c487d51570f708492ab67b5db6d0de9a9`; initial checkout HEAD is `2972ffa395c77b0776fd429b2d0ca0a0d444baa7`. Keep these identities separate. |

## Slices and existing tests

S-1: author only this canonical plan and `docs/superpowers/plans/2026-11-15-rollout-canary-note.md`; inspect the note for all nine ordered gates and their stop conditions, then report cwd, initial HEAD, hostname and CLI version. No product changes or subsequent implementation slice.

Existing tests affected: none. No migrations, public DTOs/routes, operation labels, remote script functions or statement budgets change; the [CARD-1782 impacted-classes table](../../testing-and-build.md#impacted-classes-card-1782) therefore adds no affected classes.

### Checkpoints

No build/test checkpoint rows: documentation-only canary, executed on the assigned Linux lane. No class/method filters or suffix-collision roster applies. Pin a runner only for the explicitly commissioned host canary; no platform requirement is introduced.

## Verification design

Read the requested note, check that it has at most twelve physical lines and gates 1–9 in order, inspect the final diff for documentation-only scope, and include the task-ref SHA from `git ls-remote` in the final report. No test-design or Code dispatch is needed. This report proves local execution; the caller confirms receipt and task settlement for gate 2. This plan does not add or change any session waiting for input.

## Performance budget

Apply [testing/build performance standards](../../testing-and-build.md#performance-standards). No tests or build run: selections, app/category batches, project graph/downstream coverage, expensive shared setup, per-test isolation and container/data/host/migration/clone/test-body/teardown phases are inapplicable. Admission/build-slot wait is absent. Read-only identity and documentation checks run once; no test timing artifact is generated. Measured p95 is unavailable because this is a single canary rather than a timing sample; no test timeout is added or widened. Future test rows must measure p95, use approximately twice that duration for timeouts, record setup and execution phases separately, and commission Debug for any selection/batch/run over thirty minutes. The optional HTTP reads use a provisional fifteen-second request bound without retries; this is not a measured test budget.
