# CARD-0824 S2 mutation-survivor witnesses (task cb6b4d80, Code, test-only)

Outcome: one witness per Mutation 29664acc survivor. Each was red against an env-gated mutant and green on the
restored code. No product defect was found and no production file changed.

- Branch: `feat/card-task-cb6b4d80`. Worktree: `/work/worktrees/task-cb6b4d80`. Base: master `5449f8f50b9272d6ea468920725724ec88c56d68`.
- Tested source SHA: `4c3e1680f47b8f1c6b2bbce0a6e287c29bcf90e0`. Commits:
  - `aca9799337198b759e4bb9decb0d20a76a70fcaa`: the witnesses.
  - `4c3e1680f47b8f1c6b2bbce0a6e287c29bcf90e0`: the runner test compile fix (Shouldly `NotThrowAsync` returns no value).
  - A report-only commit (this file) follows.
- Files: `tests/Antiphon.Tests/Application/C0824PublishedBranchDeletionTests.cs`, `tests/Antiphon.SessionRunner.Tests/C0824RunnerBranchRefsTests.cs`.
- No policy input changed (neither class is named in a policy/roster file), so policyHash and PinnedInputDigest were not re-recorded.

## Per-survivor kill evidence

Mutants were env-gated edits (`ANTIPHON_M0824=<id>`) in the five source files. There was one mutant build per test
project, then one build-slot lease per run, method-scoped. Afterwards all five files were restored with
`git checkout -- <exact path>`, and a grep for `ANTIPHON_M0824` in server/src/tests found nothing.

| Survivor | Mutant | New witness | Mutant run | Red at |
|---|---|---|---|---|
| PC-1 | `DeleteSourceBranchAfterPublication` defaults true (LandingSettings.cs:48) | `C0824_default_settings_ship_off_with_no_cutoff` | 1/1 failed | `ships-off` (was True) |
| PC-33b | `SettleAsync` ignores dryRun (Service:190) | `C0824_dry_run_writes_no_kept_or_deferred_row` (protected, moved, dependent-open rows; live-run liveness control writes 3 rows/3 events) | 1/1 failed | `dry-run-reported` (kept/kept/deferred instead of would_*); the row-write/event asserts follow |
| PC-31b | drop the `containment_unknown` branch (Service:108) | `C0824_containment_read_error_defers_and_retries` | 1/1 failed | `containment-deferred` (Kept, not Deferred) |
| PC-6c | drop the 64-char `SourceRemoteFingerprint` term (Policy:39) | `C0824_policy_rows(fingerprint-missing)`, `(fingerprint-short)` | 20 run / 2 failed | decision Eligible, not Kept(source_identity_mismatch) |
| PC-D1 | skip the pre-lease dependent check (Service:82) | `C0824_open_dependent_defers_before_lease_and_runner` | 1/1 failed | `no-attempt` (PushAttempts 1). Runner.Calls == 0, lease 0, containment 0 asserted after it |
| PC-38b | drop the `rev-parse --git-dir` exit check (Runner:30) | `C0824_non_repository_directory_refuses_unavailable` (existing empty dir, GIT_CEILING_DIRECTORIES = scratch root) | 1/1 failed | `result.Code` expected_object_missing, not repository_unavailable |
| PC-38 (weak) | drop `Directory.Exists` (Runner:27) | existing `C0824_missing_repository_refuses_unavailable`, now `Should.NotThrowAsync(..., "missing-repository-refused")` | 1/1 failed | named `missing-repository-refused` (PhoneHomeAdmissionException), no longer a bare exception |
| PC-41 (weak) | drop the whole-input length anchor (DelegateBranchName.cs:32) | `C0824_policy_rows(trailing-newline-name)` and runner `C0824_branch_with_trailing_newline_refused` | server 20/1 failed; runner 1/1 failed | server: Eligible, not Kept(protected_branch); runner: outcome `deleted`, not `refused` |

Equivalent mutants (no killing test is possible):
- **PC-6b** (drop `OriginalSourceSha != ReviewedSourceSha`, Policy:39). The brief listed it as a survivor needing a
  row. It is equivalent: `HasPublication` -> `HasIdentity` -> `HasV2ApprovalStatic` already requires
  `ReviewedSourceSha == OriginalSourceSha` for schema 3 (AgentTaskLandingState.cs:147), and schema 1/2 is Kept
  earlier. The added row `C0824_policy_rows(original-source-mismatch)` pins NotEligible(not_published). It ran green
  under the pc6b mutant (20/0), as predicted. It would go red only if the HasPublication equality were dropped (the
  row would become Kept(source_identity_mismatch)).
- **PC-5c, PC-6d, PC-15b**: equivalent, as the sweep reasoned. No test added.
- **PC-2**: the planned integration test cannot go red because the candidate query (Service:55-57) filters out
  unconfirmed rows. The guard is killed by the policy row `C0824_policy_rows(unconfirmed-pushstarted)`, which returns
  `before_delay`, not `not_published`. No change.

## Final verification on 4c3e1680f (checkpoint tool, ignored plan `.antiphon/c0824-s2-survivors-checkpoints.md`)

Run 20261104-102911-398a covered CP-1..CP-4. CP-5 in that run was refused `tampered`, before any test ran. The
build-source check flagged stale native obj caches under src/Antiphon.{Agents.Pty,PtyHost,PtyHost.Protocol,SessionRunner.Contracts}/obj
left by my mutant build (a PtyHost Exec child sits outside the tool's write roots). I deleted all native bin/obj
outputs and re-ran CP-5 alone in run 20261104-110357-4ed2. Green. Unedited lines:

```
CHECKPOINT CP-1 commit=4c3e1680f47b8f1c6b2bbce0a6e287c29bcf90e0 build=ok filter=/*/Antiphon.Tests.Application/C0824PublishedBranchDeletionTests/* executed=60 passed=60 failed=0 skipped=0 trx=/work/worktrees/task-cb6b4d80/.antiphon/checkpoints/20261104-102911-398a/rows/CP-1/run.trx slot=granted waited=225s dirty=0 source=4c3e1680f47b8f1c6b2bbce0a6e287c29bcf90e0 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=4c3e1680f47b8f1c6b2bbce0a6e287c29bcf90e0 build=reused filter=/*/Antiphon.Tests.Scripts/C0824BranchDeletionRecycleAuditTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-cb6b4d80/.antiphon/checkpoints/20261104-102911-398a/rows/CP-2/run.trx slot=granted waited=165s dirty=0 source=4c3e1680f47b8f1c6b2bbce0a6e287c29bcf90e0 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=4c3e1680f47b8f1c6b2bbce0a6e287c29bcf90e0 build=reused filter=/*/Antiphon.Tests.Application/C0824BranchRefsDeliveryTests/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-cb6b4d80/.antiphon/checkpoints/20261104-102911-398a/rows/CP-3/run.trx slot=granted waited=150s dirty=0 source=4c3e1680f47b8f1c6b2bbce0a6e287c29bcf90e0 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=4c3e1680f47b8f1c6b2bbce0a6e287c29bcf90e0 build=reused filter=/*/Antiphon.Tests.Domain/AgentTaskEventTypeLedgerTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-cb6b4d80/.antiphon/checkpoints/20261104-102911-398a/rows/CP-4/run.trx slot=granted waited=150s dirty=0 source=4c3e1680f47b8f1c6b2bbce0a6e287c29bcf90e0 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=4c3e1680f47b8f1c6b2bbce0a6e287c29bcf90e0 build=failed filter=/*/Antiphon.SessionRunner.Tests/C0824RunnerBranchRefsTests/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=90s slot-reason=refused: tampered dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-5 commit=4c3e1680f47b8f1c6b2bbce0a6e287c29bcf90e0 build=ok filter=/*/Antiphon.SessionRunner.Tests/C0824RunnerBranchRefsTests/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/task-cb6b4d80/.antiphon/checkpoints/20261104-110357-4ed2/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=4c3e1680f47b8f1c6b2bbce0a6e287c29bcf90e0 sourceState=clean buildSource=verified
```

The CP-1 TRX lists all 8 new server cases as Passed (60 = 52 before + 4 policy rows + 4 methods). The CP-5 TRX
lists the 2 new runner methods and the modified missing-repository method as Passed (14 = 12 + 2).

Unlisted builds/runs, all through build-slot.ps1 with one lease each and the native default output:
- 2 mutant builds: Antiphon.Tests 7m17s (waited 0s); SessionRunner.Tests 2m25s (waited 90s). The first SessionRunner
  build failed to compile (CS0815) and was fixed in 4c3e1680f.
- 10 method-scoped mutant runs, each with waited 0-225s: pc1, pc33b, pc31b, pcd1, pc6c, pc6b, pc41 (server);
  pc38b, pc38, pc41, plus one unmutated control (runner 3/3 green).

Scope: as the brief asked, I ran only the five S2 classes, not the whole Unit lane. This is a test-only change to two
classes, so there is no shared impact.

- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 5449f8f50 -HeadRef HEAD`: commits=2 entries=0 violations=0, exit 0 (before the report commit).
- `git merge-tree --write-tree origin/master HEAD`: clean. origin/master is still 5449f8f50.
- Push: server2 SSH push was denied (`git@ssh.github.com: Permission denied (publickey)`, no ~/.ssh) on 5 tries
  spread over about 30 minutes, then succeeded.
- PCs pending for SourceLanding Mutation: none of this task's own. These witnesses close survivors PC-1, PC-33b,
  PC-31b, PC-6c, PC-D1 and PC-38b, and strengthen PC-38 and PC-41. Re-running those mutants against the landed tip
  is Mutation's job.
