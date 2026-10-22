# CARD-1494 Code task 5db5df66: report

Branch `feat/card-task-5db5df66`, runner mirror `/work/worktrees/task-5db5df66`. Base `cc22379400b5a1bd79034bed0307a8b57f0e1b78`.
Plan: `docs/superpowers/plans/2026-10-22-card-1494-crash-worker-plan.md`.
Investigation: `docs/investigations/2026-10-22-card-1494-crash-worker-flake.md` (Debug 50c720df).

Test-only change: `CheckCompactionFixture.cs`, `CheckCompactionCrashWorker.cs`, `CheckNoteDeliveryHandoffTests.H5.cs`, `CheckCompactionCrashTests.cs` (shared fixture only), and the two new witness classes. There is also one `slow-tests-allowlist.txt` row.

## Commits

- `4613d97db01488131fb3746cfadca6026304273e`: S1, the seam and the red witnesses.
- `74d562b8fae2a34ed76f2007538d7c38dc449a85`: S1 compile fix. The sed had rewritten the `Request` helper's string parameter.
- `dbf1345e4825f37b50e18bc5fac80e71b5d2fc6a`: S1 witness repair. The failure message awaited stderr, so the witness blocked until the child exited.
- `4c4db690b5987a43f1913f66b730948d02112f88`: S2, the fix and the plan.
- `adedcfdbc06fcae1c7654a19bc42821568b5a5b7`: report v1.
- `e14737f69d47cad5fb18ac72e50404843b3c0111`: S2 repair. The busy produce case ends the turn before the scanner starts again, in the original order.
- This report commit (v2). The receipt run starts at this tip.

## Checkpoint run 1 (`20261022-152908-dd89`, at `adedcfdb`, superseded)

- CP-1: 3 of 3 passed.
- CP-3: 9 of 9 passed.
- CP-2: 15 of 20 passed. `Legacy_produced_note_survives_pre_enqueue_worker_death(True)` failed 0/5, deterministically. The new timeout diagnostic showed the cause in every case: the notification was `AwaitingReceipt`, the queued row was `Pending` with 0 attempts, and 12 scans had run.
  - Cause: S2 had moved the readiness wait ahead of the busy `TurnEnd`. The `TurnEnd` the parent writes after the scanner's first scan is never noticed by the child's queue. The original test wrote it 500 ms after spawn, before the child attached, so `busy=True` never exercised a scan while Working.
  - Fixed in `e14737f6` by restoring the original order.
  - Open for the caller: `busy=True` cannot prove delivery after a Working scan in this harness. That is a coverage gap, not a regression.
- No `/tmp/antiphon-c79-crash-*` roots, no `.antiphon/acceptance` under the outputs, and no orphan children were left after the run. The outputs were kept only because a red run keeps them (`still-present`); they were deleted by hand.

## S1 red evidence

One narrow build-slot run each, `bin-c1494r/`, not the checkpoint tool, because a red row cannot produce a valid receipt.

- At `74d562b8`, filter `(CheckCompactionFixtureTests*)|(CheckCompactionWorkerLifetimeTests*)|(TestClassificationGuardTests*)`: 4 executed, 4 failed.
  - `CheckCompactionFixture_root_is_outside_the_build_output` was red for the right reason: `.../bin-c1494r/.antiphon/acceptance/card-0079/crash-... is under the build output`.
  - The two lifetime cases were red for the wrong reason: the witness bug fixed in `dbf1345e`.
  - `TestClassificationGuardTests.Registry_matches_compiled_metadata` was red with unrelated classes only (`BlockedReplyReportRetentionTests`, `DispatcherLeaseSpanTests`, `TestSchemaSnapshotTests` missing or unregistered). Nothing in that list is from this task, and the new `CheckCompactionWorkerLifetimeTests` row is not reported. This red is not in the CP table.
- At `dbf1345e`, filter `/*/*/CheckCompactionWorkerLifetimeTests/*`: 2 executed, 2 failed, both with `IsAlive(pid) should be False` (the child outlived the failed wait). The witnesses' `finally` killed their own children, and no orphan was left (`ps` checked).

## Landability (at `4c4db690`)

- `git merge-tree --write-tree origin/master HEAD` is clean (master `6a2fe6ae3`).
- Per-commit `merge-tree --merge-base` onto `origin/rc/2026-10-20`: all four commits are clean.
- The four fixture/test files are byte-identical on rc at base. `slow-tests-allowlist.txt` differs on rc, but the pick is clean.
- `scripts/check-evidence-diff.ps1` over base..HEAD: violations=0.

The CHECKPOINT lines from the receipt run are in the final report message.
