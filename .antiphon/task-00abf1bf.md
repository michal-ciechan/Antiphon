# CARD-0824 S1 review round 1 fix (task 00abf1bf)

Outcome: D-1, D-2 and D-3 of review 3a354688 are fixed on `feat/card-task-00abf1bf`, which continues from
`41e63343b6449c6a97ee7c8df3bfe52da0354ea9`. Every witness was red at the tip and is green after the fix. The
three requested mutants went red and came back green when restored. Run A and run B are both GREEN. The
land graph still matches (approved, 69, same digest). Both flags stay off. Nothing was landed. PC-1..PC-74
were not run.

Landing owner: original Code task `cf298805-851e-4b69-bc3e-ed5ae03816d1`. Plan/evidence:
`docs/superpowers/plans/2026-11-02-card-0824-per-slot-worktrees-plan.md` §3.5b,
`docs/superpowers/plans/2026-11-02-card-0824-S1-review-fix-checkpoints.md`, `docs/session-runtime-invariants.md`.

## Commits (branch feat/card-task-00abf1bf, worktree /work/worktrees/task-00abf1bf)

- `6ab40058b16af33eb16356cec6c06814376170e4`: red witnesses, plus only the constants they name.
- `95b8ff5dd9689c1d4f88b6f32fab87bff4a005cc`: moves the D-3 restart witness into the custody class (it had
  landed in the trailing helper class, TUnit0038).
- `fa2531861329f57505886dd03e24db1888033692`: the fix and docs. This is the tested source.
- This report commit follows it and changes nothing except this file.

## Fix per defect

- **D-1** `RunnerWorkspaceSlotPool.ProveOwnership` is a read-only check. A slot passes only if all of these hold:
  - Its path is `<worktrees>/<slot leaf>` by canonical path (`TryResolveFinal`).
  - There is no reparse point or symlink at the directory, at `.git`, at the registration, at `<primary>/.git`
    or at `.git/worktrees`.
  - `.git` is a file that names a registration directly under `<primary>/.git/worktrees`, so the primary
    `.git` is refused.
  - The registration's `gitdir` back-pointer canonicalizes to this directory's `.git`, so a sibling is refused.
  - The owner stamp in the registration names this slot id and directory generation. The stamp is now written
    at creation and at re-home, and rewritten at every lease.

  Where the proof runs:
  - As a preflight in release, before any git command in the slot.
  - In lease, through `CheckFreeAsync`/`PreflightFreeAsync`.
  - In cold reset and retirement, through `PreflightFreeAsync`.
  - Again at the start of `ResetForLeaseAsync` and `ResetForFreeAsync`, immediately before the destructive
    commands.
  - After the lease verify.

  Registration file deletes (`logs/HEAD`, `ORIG_HEAD` and the rest) use the proven registration path, not
  `rev-parse --git-dir`. Any failure quarantines with nothing run. A linked slot path is not `worktree lock`ed,
  because the lock would land on the link target. The old parent comparison is now part of the proof.
- **D-2** `ColdResetAsync` runs `PreflightFreeAsync` before `git clean -ffdx`. That preflight covers ownership,
  the full free invariant and the HEAD-reflog unpushed check (no fetch, which can only make it stricter).
  Precise rule: the cold reset removes keep-list native output only. An untracked non-ignored path, an ignored
  path outside the keep-list (late `.antiphon/` evidence, for example) or a commit outside origin quarantines
  the directory with its bytes in place, the same as a lease. The brief's wording ("ignored files are what cold
  reset removes") holds for the only ignored output a Free slot normally has, because warm reset removes every
  other ignored path. I chose lease parity over wiping all ignored files. That is open question 1.
- **D-3, design (i): release on the old runner first.** In `RebindDrainingAsync`, a queued task that still has a
  slot id and no release stamp first sends the same idempotent `WorkspaceSlotRelease` to its current (old)
  runner. If the answer is `released`, `already_free` or `quarantined`, the release is stamped, the path is
  cleared and the rebind proceeds. Any other result (a refusal, a lost reply or no transport) keeps the task
  on the old runner with its slot. It then gets a `RunnerDraining` hold whose detail is
  `Held: workspace_slot_release_pending: ...`; the detail is stable, so it dedupes and held age escalates it.
  The release is retried every tick.

  I chose (i) over refusing the re-arm because it reuses the reconciler's delivery, moves the task as soon as
  the slot is safe, and adds no new path. Because the pending state lives in the task row (old runner id, slot
  id, generation, no release stamp), a server restart recovers it.

  Runner restart: `RecoverAsync(null)` keeps every Leased slot. The runner cannot tell whether the task is
  terminal; the server owes the release. The reconciler releases the slot once the task is terminal, and the
  drain path releases it before a move.

  Other D-3 changes:
  - The reconciler stops after 3 `slot_unknown` (409) answers per slot and generation and records
    `workspace_slot_release_stuck`. It never stamps a release that did not happen.
  - `RemoveMirrorAsync` now skips only an unreleased slot. A released slot id next to a newer path (a
    per-task mirror on a runner without slots) is removed as usual. Without this, a task redirected to such a
    runner would leak that mirror at retirement.
  - The enablement gate is written into the invariants doc and plan §3.5b.

## Witnesses: red at the tip (source 41e63343b plus the tests at 95b8ff5dd), green at fa2531861

These ran through build-slot outside the checkpoint table. They were not part of the tool's runs. The reason
was to prove each witness red before the fix.

- `C0824_ReleaseProvesOwnershipBeforeAnyDestructiveGit` (primary, sibling, symlink; CP-8): 3/3 red. Primary
  and sibling failed at the outcome assertion (:258). Symlink failed because the sibling's
  HEAD/reflog/refs/files changed (:260).
- `C0824_ColdResetQuarantinesWhatALeaseWould` (untracked, ignored-evidence, reflog-only-commit; CP-9): 3/3 red.
- `C0824_ReconcilerBoundsSlotUnknownRetries` (CP-4): red. The request count was not bounded (:274).
- `C0824_DrainRebindReleasesTheOldSlotFirst` (released, old-runner-refuses; CP-21): 2/2 red. The old runner
  received no release (:418).
- `C0824_RestartKeepsLeasedSlotForTheServersRelease` (CP-2): green at the tip by design. It documents the
  recovery action, and the tip already kept leases.

## Mutants (Mutation stage owns the formal PCs; these were requested in the brief)

- M-D1, ownership proof short-circuited to "owned": the D-1 witness went 3/3 red. Restored: 3/3 green.
- M-D2, cold-reset preflight moved after the clean: the D-2 witness went 3/3 red. Restored: 3/3 green.
- M-D3, old-runner release skipped (`&& false`): the D-3 drain witness went 2/2 red. Restored: 2/2 green.

All three ran in a detached scratch worktree at fa2531861, one lease per group: `7dd857bf` held 65s and
`4b8c2e70` held 347s, both waited 0s. Restoration was `git checkout --`; the restored builds are the green
results above.

## Checkpoints (tool, plan = the review-fix amendment, expected SHA = fa2531861329f57505886dd03e24db1888033692)

Run A, `20261102-094059-8781`, GREEN exit 0. Source state clean, buildSource verified, unlisted none, 3 builds.
Every row was slot=granted, waited=0s.

| Row | Class | Passed / executed |
|---|---|---|
| CP-1 | ResetTests (V-12r) | 10/10 |
| CP-2 | Custody | 16/16 |
| CP-3 | ProcessScan | 3/3 |
| CP-6 | Binding | 20/20 |
| CP-7 | Overflow | 9/9 |
| CP-8 | Rehome | 14/14 |
| CP-9 | ColdReset | 12/12 |
| CP-10 | NativeRehome | 1/1 |
| CP-15 | NativeLinuxPolicy | 3/3 |
| CP-16 | WorkspaceService | 35/35 |

Run B, `20261102-094454-3618`, GREEN exit 0. Wall 10m10s, unlisted none. Every row was slot=granted, waited=0s.

| Row | Class or method | Passed / executed |
|---|---|---|
| CP-4 | Server (V-12) | 23/23 |
| CP-5 | Preparer | 27/27 |
| CP-17 | TerminalRunnerSeatRelease method | 1/1 |
| CP-18 | PhoneHomeConnection method | 1/1 |
| CP-19 | Migration | 2/2 |
| CP-20 | DelegateScriptKind | 46/46 |
| CP-21 | PhoneHomeRollingRunnerTests, full class | 32/32 |

CP-21 is a new row. The full class is the integration class affected by `RebindDrainingAsync` and
`RemoveMirrorAsync`.

No failures, no reruns, nothing skipped. V-12 and V-12r (flag-off identity) are green.

## Gates

- Land graph: `-Check` only, evaluator built through build slot `e6f2a7da`. Result: approved, count 69,
  digest `D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE`, unchanged. The canary was removed
  and `bin-land-audit` was deleted.
- `check-evidence-diff`: base `41e63343b` to `fa2531861` gave commits=3, violations=0. The whole-branch range
  `5d37396b3` to `fa2531861` gave commits=18, entries=1 (the earlier report), violations=0.
- merge-tree: clean against `origin/master` `10c8c2471633c7c0212294afc57348878021e183` (tree `69ae8b674c40`)
  and against `origin/rc/2026-11-01` `3e39e0534f19` (tree `002e364396a0`). The slice stays master-only.

## Open questions

1. D-2 rule: should a cold reset wipe ignored files outside the keep-list (the brief's literal wording) or
   quarantine them (lease parity, which is what is implemented)?
2. D-3: when the old runner answers `slot_unknown` to a drain-path release, the task is held (conservative).
   `slot_unknown` from the runner the slot belongs to means no lease exists there. Treating that answer as
   released would let the task move.
3. Other paths that rewrite `RunnerId` for a slot-bound task (outside the drain re-arm) were not audited in
   this round.
4. Not done, per the brief: whole Unit lane, the 74 PCs (Mutation), and V-42 server-restart and V-56, which
   remain deferred from S1.
