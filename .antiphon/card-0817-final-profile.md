# CARD-0817 supplemental Final profile

Historical execution manifest: the caller's refinement in
`.antiphon/inbox/250ae3ea-0d32-4f5f-9c3f-24a0b4a4acdc.md` stopped further
whole-Unit and baseline work and retained CP-1..CP-6 as this task's closed list.
The runs below had already finished; their failures and timeout remain recorded
in `.antiphon/task-2a876d8a.md`. Unit qualification is incomplete and caller-owned.
This file is retained as provenance, not a request to launch more runs.

Original Code owner: `2a876d8a-7681-47be-94bb-c8cba4abfade`.
Implementation plan: `docs/superpowers/plans/2026-10-05-card-0817-https-token-push-credential-plan.md`.

CP-1 through CP-6 are complete. These are explicitly reported additional checks
required by the stage's Final contract: the whole Unit lane in both changed test
projects and the full affected integration class. They extend the brief's narrower
checkpoint selection; they are not hidden runs or additional implementation scope.
The underlying invariant is that credential release follows repository admission,
while startup, mirror creation and deploy fixture custody remain valid.
The affected integration class is bounded to `RunnerWorkspaceServiceTests` (33
test methods). The shell/image/helper/refresh and shared recycle-fixture classes
are Unit classes, included in the complete Antiphon.Tests Unit lane. No production
host, container, vault or remote repository is an intended test target.

Estimated cost: 21 minutes including two isolated builds. Rows run serially.
No additional repetition or deliberate mutation is requested. Any failures are
reported and diagnosed at the task base before being called inherited; the task's
three repair rounds are already used, so further implementation repairs require
a separate authorization/task. Existing timeouts and assertions stay unchanged.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-7 | Final | `tests/Antiphon.SessionRunner.Tests -> bin-c0817-final-runner/` | runner-unit-final | `/*/*/*/*[Category=Unit]` | Final runner Unit baseline, V-1 through V-4 | whole selected lane, 0 failed | 8 | 4 | true |
| CP-8 | Final | CP-7 | workspace-full-final | `/*/*/RunnerWorkspaceServiceTests/*` | Final full affected integration class, V-5 and R-1 | all 33 methods, 0 failed/skipped | 33 | 2 | true |
| CP-9 | Final | `tests/Antiphon.Tests -> bin-c0817-final/` | unit-final | `/*/*/*/*[Category=Unit]` | Final Unit baseline, V-6 through V-18 and R-2/R-3 | whole selected lane, 0 failed | 343 | 15 | true |
