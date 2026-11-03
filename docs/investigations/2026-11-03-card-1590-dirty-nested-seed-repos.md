# CARD-1590 investigation: dirty nested seed repos block recycle

Date: 2026-11-03. Stage: Investigate (read-only; no product change). Base: 7b920d84aae97139998f940ee2cce3f457bb6e70.
Host: server2 runner mirror (git 2.47.3).

## Verdict

Root cause confirmed. Every observed nested repo sits under the worktree's **gitignored** `.antiphon/`.
The outer worktree's `git status` is therefore clean. The consumer that refuses is the server2 **volume recycle audit**:
it audits every repository on the volume, nested ones included. The CARD-1677 retention engine does not refuse these trees.
By its plan's D-9/V-16, and by the reproduction below, it retires them with the tree.
So CARD-1590 is **not** a gate for enabling CARD-1677. Still fix it: the recycle audit refuses a tree before retention's
`MinTerminalHours` passes, and it refuses any tree that retention keeps for another reason.

## Producers (three families)

| # | Producer | Where the repos are created | Cleanup on success / failure / abort |
|---|---|---|---|
| P1 | `scripts/test-change-aware-release.ps1` (C1313 harness), run **directly** by a delegate with `-ResultsDirectory .antiphon/c1545-base-mode-credit` (task 2dbadf60 R-1 base attribution, commit 27dfcfbf2 `.antiphon/task-2dbadf60.md`: "Raw output: .antiphon/c1545-base-mode-credit.log") | `New-Fx` `:33-35`: `<results>/space <guid>/seed` (git init + 2 local commits). `:67` adds `origin.git` (bare) and `:69` adds `releases λ/checkout` (clone) | The script never deletes its fixture roots. Its `finally` `:373-379` only clears read-only bits and copies `native-*/hop-*/discovery` to `$repoRoot/.antiphon/test-output/c1313-s6a/<guid>`. `New-C487Root` (`scripts/lib/c487-harness.ps1:33-40`) defaults to `$env:TEMP` and never deletes either. **Under TUnit** the caller owns the root: `ScriptHarnessProcess.cs:60` puts it under `Path.GetTempPath()`, and `:167-178` deletes it after success *and* failure. An abort leaves it in `/tmp`, outside the worktree. So the TUnit route is clean and the direct route leaks |
| P2 | Merge delegate's ad-hoc `rc10-selection-harness` scratch (card, 2026-10-27, task b6aeeade) | `.antiphon/rc10-selection-harness/<guid>` | No owner; hand-made scratch |
| P3 (new) | `scripts/test-nightly-failed-first.ps1` archive step, reached by the **TUnit** route (`RcFailedFirstExecutionTests.cs:31-32`) | `:1394` creates `$repo/.antiphon/failfirst/<sha>/portable/<Case>-<guid>`. `:1396-1405` copies every file under the fixture root, including the `.git/` internals of the authority-fixture checkout (`:811` `authority-fixture`, via `New-C599PublishFx`) | Written by design ("Retain the synthetic inputs", `:1391-1392`). Never removed. The result is a working nested repo in the worktree on every run of the three FFAuthorityConservation cases (`:14`) |

The `AD Directory.Build.props` status in the card is the fixture's own doing. It comes from the C1313_ModeCredit fallback loop (`:167-170`):
`read-tree A`, then `update-index --add --cacheinfo ...,Directory.Build.props`. This stages a path that is absent from the working tree,
and the loop leaves it staged. Directory.Build.props is the loop's last path, which is why the card shows that file.

## Measurements (server2, read-only, 2026-11-03)

- 184 `task-*` mirrors in `/work/worktrees`. `/work` is 73 % used (185 G free).
- `find /work/worktrees -mindepth 3 -maxdepth 10` for `.git`, with `bin/obj/node_modules` pruned: **2** nested repos, 0 errors.
  Both are P3:
  `task-e7d4f999/.antiphon/failfirst/1a819f55…/portable/JournalCustodySurvivesInterleaving-853aec3b…/authority-fixture/e680ae31…/checkout/.git` (2026-11-02 14:15) and
  `task-b922ebf8/.antiphon/failfirst/4a0abd68…/portable/JournalCustodySurvivesInterleaving-31f27479…/authority-fixture/dd531f56…/checkout/.git` (2026-11-02 13:55).
  Each nested repo is dirty (`?? .antiphon-release-owned`) and has a local-only commit `policy`.
- **0** C1313 `space */seed` repos and 0 `.antiphon/test-output/c1313-s6a` dirs on the current volume. The 2026-10-26/27 instances
  (2dbadf60, 87928ecc ×6, b6aeeade) went with earlier recycles or hand cleanup.
- For both P3 worktrees, `git status --porcelain --untracked-files=all` at the outer root is **empty**. `--ignored` lists only `!! .antiphon/` and obj dirs.
  The count of finished worktrees that are dirty *at the top level* for this reason is therefore **0**.
- Task status of the two owners was not read: `/api/agent-tasks` rejected `idPrefix`, and no other lookup was tried.

## Reproductions

R1, git semantics (scratch repo, `GIT_CONFIG_GLOBAL=/dev/null`). The nested seed was built with the fixture's exact
`read-tree` + `update-index --add` steps, and its own status reads `AD Directory.Build.props`.

| Nested seed location | outer `status --porcelain --untracked-files=all --ignore-submodules=none` | `git worktree remove` (no `--force`) |
|---|---|---|
| `.antiphon/c1545-base-mode-credit/space 88bf/seed` (ignored) | empty | rc 0, tree and nested repo deleted |
| `scratch/space 88bf/seed` (not ignored) | `?? "scratch/space 88bf/seed/"` | `fatal: … contains modified or untracked files`, rc 128 |

R2, the real harness on its failure path, run directly as delegate 2dbadf60 did:
`TEMP=/tmp build-slot.ps1 -Label c1590-c1313-direct -- pwsh -File scripts/test-change-aware-release.ps1 -Case C1313_ModeCredit -ResultsDirectory <scratch>/results`
with no `-FilterHost`. Exit 1 at `:112` `fixture-discovery-failed`, after `New-Fx`. The slot waited 166 s and was held 10 s.
Residue: `space <guid>/seed/.git` (local commits `baseline`, `candidate`, no remote), `space <guid>/origin.git` and `space <guid>/releases λ/checkout/.git`.
The `finally` also wrote `.antiphon/test-output/c1313-s6a/<guid>/discovery/console.log` into this worktree.
All of it was removed by exact path afterwards.

R3: on Linux with `TEMP` unset, `Join-Path $env:TEMP 'c487-x'` throws "Cannot bind argument to parameter 'Path' because it is null".
A direct run with no `-ResultsDirectory` therefore fails on Linux, which pushes delegates to pass a path. They choose `.antiphon/` because that is the evidence convention.
This is a contributing cause (inference: the delegate's intent was not recorded).

## Answers to the brief

1. **Fixtures and cleanup.** P1 is clean under TUnit after success and failure. An abort leaves its residue in `/tmp`, outside the worktree.
   Run directly, P1 leaves its residue after success, failure and abort, under whatever root the caller gave it. P3 leaves a nested repo inside the worktree on every completed run, by design.
   The `tests/Shared/LiveClaude` / C1545 census and sink-audit fixtures are **not** the producer. The card's "c1545" is only the label that delegate chose for the results directory.
2. **server2 count.** 2 nested repos (both P3). 0 top-level-dirty finished worktrees.
3. **Ignored, not untracked.** The nested repos are ignored, so `git status` stays clean. Adding a `.gitignore` entry is wrong and has no effect: the path is already ignored,
   and the recycle audit walks nested repos regardless (card refusal `repo=worktrees/task-2dbadf60/.antiphon/c1545-base-mode-credit/space …/seed`;
   `docs/docker-stack.md:445`). Inferred from that doc and not reproduced here: a *clean* nested seed would still refuse the recycle audit, because it has local-only commits and no origin (`RecycleUnpublishedWork`/unknown).

## Recommended fix (smallest, per producer)

- **F1 (P3): stop copying Git admin content into the archive.** In the `:1396-1405` loop, skip any file whose relative path has a `.git` segment.
  Record it in `inventory.json` as `kind='git-dir';copied=$false`. Moving the archive to TEMP is not the right fix, because retaining it in the repo is the stated intent (`:1391`).
  A `finally` is not the right fix either, because the archive is meant to outlive the run. It just must not contain a repository.
- **F2 (P1): own the fixture roots.** In the existing `finally` (`:375-379`), after the retained copy and the read-only clear,
  delete each `$script:FixtureRoots` root by its exact path. The retained `native-*/hop-*/discovery` dirs are siblings, not children of `space *`, so they stay.
  Change the `New-C487Root` default to `[IO.Path]::GetTempPath()` so that a direct Linux run without `-ResultsDirectory` lands in TEMP.
  A `finally` covers success and failure. Abort residue stays only where the caller pointed the root.
- **F3 (P2, docs only, optional):** one line in `docs/testing-and-build.md`: scratch or fixture repos go under the session scratchpad or TEMP, never inside the task worktree.

Red-first witnesses (each fails on the base commit):
- **W1 (F1).** Run `RcFailedFirstExecutionTests.JournalCustodySurvivesInterleaving`, or the cheapest of the three FFAuthorityConservation cases.
  Then assert that the archive it created (`.antiphon/failfirst/<HEAD>/portable/<Case>-*`, newer than the test start) contains no path with a `.git` segment.
  The base is red: server2 already holds two such archives with `checkout/.git`.
- **W2 (F2).** Invoke `test-change-aware-release.ps1` directly with a test-owned `-ResultsDirectory`, in two arms:
  (a) failure arm: `C1313_ModeCredit` without `-FilterHost` (exit 1 in about 10 s, reproduced in R2);
  (b) success arm: `C1313_BaselineAuthority`, which needs no FilterHost.
  In both arms, assert that no `space *` directory and no `.git` remains under the results root. The base is red: R2 left 3 repos.
  Also assert that the `c1313-s6a` retained dir still holds `discovery/`. Both arms run through a build slot.

Landing order relative to CARD-1677: **independent, not a prerequisite.** CARD-1677 retires these trees as planned, provided V-16 stays a hard witness.
F1/F2 close the recycle-audit window for trees that are younger than `MinTerminalHours` or that retention keeps for another reason.

## Remaining uncertainties

- The status of the two P3 owner tasks (e7d4f999, b922ebf8) was not looked up. Removing their residue is a matter for the operator or a Debug task. Nothing was deleted here.
- The recycle audit code path that discovers nested repos was not traced line by line. That it does discover them rests on the card's stored refusal and the docker-stack doc.
- P1 abort behaviour (a killed pwsh) was not reproduced. It follows from there being no `finally`/trap on kill.

## Not done, noted

- Fix idea, one line: F1 skips `.git` segments in the failed-first archive copy, and F2 deletes `$script:FixtureRoots` in the C1313 `finally` and defaults `New-C487Root` to `GetTempPath()`.
