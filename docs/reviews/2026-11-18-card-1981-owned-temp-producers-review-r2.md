# CARD-1981 Final review, round 2: CLEAN

- Subject: `origin/feat/card-task-02a4c375`, tip `79149738ea037b2a901a3ab35a20762e0873bb88` (Grok author).
- Round 2, diff only: `192c8d63b62c8d73e1f1093a3c4c15338b1fb1fe..79149738ea037b2a901a3ab35a20762e0873bb88`, blockers only.
- Round 1: task 8ff0e185, commit `473b4efa4b577b7eae223dca8032117a1437030c` (D-1, D-2).
- Reviewer: Claude Opus 5.5 on server2 runner mirror, task 8b660bbd.

## Verdict

CLEAN. Both round-1 blockers are fixed. No new blocker found.

## D-1: remove-review-scratch.ps1 deletion safety (fixed)

I re-ran the original cases and 18 more adversarial cases on throwaway repositories under
the session scratchpad. I ran the new script at the tip and the old script from
`5398ff1867635444544e392d7e4e5ed225213221`.

| Case | Old script (5398ff18) | New script (79149738) |
|---|---|---|
| A: child symlink to a clean worktree outside the container | exit 0, foreign `README` deleted | exit 1, reparse refusal, foreign worktree and container kept |
| B: linked worktree at `sub/wt` with an uncommitted file | exit 0, uncommitted file deleted | exit 1, nested-git refusal, file and container kept |
| C: child is a dirty worktree of a different repository | n/a | exit 1, git refuses the remove, container kept |
| C2: child is a clean worktree of a different repository | n/a | exit 0, removed through that repository's `git worktree remove` (sanctioned) |
| D: clean worktree child plus a standalone clone child | n/a | exit 1, clone refusal, container kept |
| E: forged gitfile pointing at a main repository `.git` | n/a | exit 1, porcelain does not name the child |
| F: forged gitfile pointing at another worktree's admin directory (dirty, outside) | n/a | exit 1, porcelain does not name the child, outside file kept |
| G: hidden dotfile symlink to a directory outside | n/a | exit 1, reparse refusal |
| H: locked clean worktree child | n/a | exit 1, git refuses without `--force` |
| I: root given as `x/../antiphon-review-…/` with a trailing slash | n/a | normalised; dirty worktree refused by git, kept |
| J: `.git` directly in the container | n/a | exit 1, nested-git refusal |
| K: container is a symlink to a real directory | n/a | exit 1, container reparse refusal |
| L: owner marker is a symlink to an outside file | n/a | exit 1, reparse refusal, outside file kept |
| M, N: temp root is a symlink; worktree registered by its real or link path | n/a | exit 1, porcelain path does not match, kept (fails closed) |
| O: hard link to an outside file | n/a | exit 0; only the link name is unlinked, the outside file keeps its content |
| P: unreadable subdirectory | n/a | exit 1 from enumeration, kept |
| Q: worktree child containing a nested repository (`wt/vendor/x/.git`) | n/a | exit 1, nested-git refusal |
| R: child `.git` copied from another worktree | n/a | exit 1, kept |

No case deleted data outside the container or uncommitted work. I found no third escape. I
cannot test Windows junctions on server2. A junction carries the ReparsePoint attribute, so
the same check applies to it. Linux has no trailing-dot or case-folding aliasing. Long paths
throw, and the script then keeps the container.

## D-2: [ParallelLimiter<ProcessSpawnLimit>] on OwnedTempLeakTests (fixed)

The attribute is now at class level (`tests/Antiphon.Tests/TestHelpers/OwnedTempLeakTests.cs:14`).

## Witnesses and runs at 79149738

- One build-slot build of `tests/Antiphon.Tests` (`UseAppHost=false`, native output): 0 errors.
- One build-slot run of `/*/*/(OwnedTempLeakTests*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)/*`:
  63 total, 63 passed, 0 failed, 0 skipped (OwnedTempLeakTests 10, TestClassificationPolicyTests 42,
  SlowTestRegistryMergeTests 5, NativeLinuxPolicyWitnessTests 3, SlowTestTripwireTests 2,
  TestClassificationGuardTests 1).
- Red at 5398ff18: cases A and B above set up the same scenarios as the two new witnesses, using
  the old script. Each witness asserts that the foreign file still exists, that the exit is
  nonzero and that the refusal text is present, so each would fail at 5398ff18. The author
  recorded the same red run under "Unlisted runs" in the plan. I did not rebuild at 5398ff18.
- `check-evidence-diff` over `192c8d63..79149738`: 2 commits, 0 entries, 0 violations.

## Advisories (not blockers)

- A-1: The child loop validates and removes one child at a time. If a clean linked worktree
  is enumerated before a refused child, it is removed before the refusal, though the
  container is kept. On ext4 the probes always enumerated the clone first. The removal is
  still `git worktree remove` without `--force`, so no uncommitted work is lost. A pre-pass
  that validates every child before any remove would make the script all-or-nothing.
- A-2: On Windows, `git worktree list --porcelain` prints `C:/...` with forward slashes,
  and the script compares that against a backslash path with `-ceq`. Every Windows child
  would therefore be refused. This fails closed and only matters if the script is used on
  Windows.
- A-3: The walk compares `.git` with case sensitivity. On a case-insensitive Windows
  volume, a nested `.GIT` directory would not be detected. This matters on Windows only.

## Landability

`git merge-tree --write-tree origin/master 79149738` against master
`625755ca3ee4929a51d9feaa6771d4dd736ae2b0` has one conflict, in the generated
`tests/derived-impact-index.json`, from round-1 commit 192c8d63 and master's later
regenerations. `docs/docker-stack.md` auto-merges. To resolve it, run
`scripts/regenerate-derived-impact-index.ps1` once during the land or Merge, then
`DerivedImpactIndexTests`. The subject branch does not contain master 625755ca.

## Process note

The load gate was read before each command. The build started at load 10.5. The test run
started at 17.8, above the 16 threshold. That was my error; the run took 72 seconds and
passed.
