# CARD-0824 S4a Review round 2 (task 4ed13efe): D-1/D-2 fixes

**Verdict: Clean at tested SHA `07d568a82ded795eeba3762089c4ff585992b7db`.** No D-n. Six advisories.

- Subject: `feat/card-task-56e7e9b7`. Range reviewed: `bd502f519606fe49e5b36033cf4c9c2f1ea27e12..07d568a82ded795eeba3762089c4ff585992b7db`. The tip `cd1697f33` adds only the Code report.
- Landing owner: Code task `250763e5-1b7a-466b-a6e4-70ac7c7c9bca`.

## Checkpoints

Run `20261104-020939-2236`, done with the checkpoint tool (`start --rows CP-23,CP-24`, then `wait`). Lines unedited:

```
CHECKPOINT CP-23 commit=07d568a82ded795eeba3762089c4ff585992b7db build=ok filter=/*/*/(C0824DesktopLegacyReleaseSettingsTests*)|(C0824DesktopLegacyReleasePolicyTests*)/* executed=120 passed=120 failed=0 skipped=0 trx=/work/worktrees/task-4ed13efe/.antiphon/checkpoints/20261104-020939-2236/rows/CP-23/run.trx slot=granted waited=0s dirty=0 source=07d568a82ded795eeba3762089c4ff585992b7db sourceState=clean buildSource=verified
CHECKPOINT CP-24 commit=07d568a82ded795eeba3762089c4ff585992b7db build=reused filter=/*/*/(TaskWorktreeRetirementTests*)|(WorktreeResidueSweepTests*)/* executed=55 passed=55 failed=0 skipped=0 trx=/work/worktrees/task-4ed13efe/.antiphon/checkpoints/20261104-020939-2236/rows/CP-24/run.trx slot=granted waited=0s dirty=0 source=07d568a82ded795eeba3762089c4ff585992b7db sourceState=clean buildSource=verified
verdict: GREEN exit=0 ; unlisted: none (the tool ran no other build or test command)
```

Other checks:

- `validate --evidence .../report.json --expected-source-sha 07d568a8...` printed `CHECKPOINT SOURCE VALID rows=2`.
- `check-evidence-diff`:
  - `bd502f519..07d568a82`: commits=4, violations=0;
  - `591fafecb..cd1697f33`: commits=8, violations=0.
- `git merge-tree --write-tree origin/master(2b342761f) 07d568a82` is clean (tree `c91860b6f`). The tip `cd1697f33` is clean too.
- Linux-only constructs: none in the five changed test files (I grepped for NativeLinuxOnly, IsLinux, GetUnixFileMode, /bin/sh and chmod). I did not run the C1323 census harness itself.
- CP-25 (Windows-native LN, N-1..N-3) did not run and is owed on the desktop lane.

## Unlisted runs (one build-slot lease each; mutants are exact-path sed edits restored with `git checkout --`)

Each mutant had one native build (`-p:UseAppHost=false`) and one `--no-build` run with the same property.

| Mutant | Edit | Result |
|---|---|---|
| M2 + M7c (one build) | M2 drops the `WorktreeBaseRequestedRef` dependent check. M7c drops the top-loop settled `RecordCheck` | M2 **killed**: `Server_predicates(startref-queued)` and `(startref-queued-origin)` were red (`Removed` where `Held` was expected), 2/21 failed. M7c **survived**: `Held_backlog` and both `First_enable` cases stayed green |
| M4 | `prechecks++` counted only when the outcome wrote, filed or retired | **killed**: `Held_backlog` ("at most the cap in pre-checks") and `First_enable(preview-first)`, 2/3 red |
| M7a | the cheap missing-directory branch disabled (`false &&`) | **killed**: 3/3 red (`over_cap` count, `directory_absent` count, `prechecked`) |

Afterwards, `git clean -fdX` removed exactly the 72 bin/obj directories, after a dry run that listed only those. The tree is clean.

## What the diff does (brief items 1-4)

1. **Only refusals and bounds were added.**
   - No deletion guard changed.
   - `release_active_other` moved ahead of the pre-check, so a dry run also holds.
   - Recheck and held paths now write rows through the cursor rule.
   - D-1: `BranchName` (bare, `refs/heads/`, `origin/`, `refs/remotes/origin/`) is applied to `WorktreeBaseRequestedRef` exactly as it is to `WorktreeBaseRef`.
   - `RequestedWorktreeBaseTaskId == task.Id` is added.
   - The open set is Queued, Dispatched, Working and Blocked.
   - Follow-up continuations store a SHA, so they do not depend on the branch.
2. **Progress.**
   - Never-checked trees come first (oldest `CompletedAt`), then due trees in LRU order.
   - Every pre-check takes a cap slot.
   - Every checked tree leaves the never-checked set: Held gets a backoff; a missing directory is settled; a removal completes; a refusal or throw gets a retry cursor. A lease-busy deferral stays first but uses only 1 slot per tick.
   - So every candidate is reached.
   - The fingerprint (Status, Attempt, CompletedAt, path, branch) changes only when the task row changes, so a reset cannot hot-loop.
   - A settled record is keyed to the task id plus that fingerprint. A new task cannot reuse the path, because the leaf is its own id8 and `path_shared` applies.
3. **A-4.** `ErrorOnUnknownConfiguration` is the only use in the repository.
   - The CARD-1677 `RunnerWorktreeRetention` precedent ignores unknown keys, but a ValidateOnStart failure there stops startup on an invalid value.
   - So an S4a typo is the same failure class as an invalid value: fail-closed at startup, and disabled (not a crash) on hot reload through `ReadSettings`.
   - I judge this acceptable for a setting that ships ON.
   - The shipped `appsettings.json` keys all match the properties.
   - The shape validator is unchanged and still refuses arrays, objects and null.
4. **The changed tests.**
   - V-24 `throws`/`lease-busy`: the cap goes from 2 to 3, and the assertions are unchanged.
   - V-25: the over_cap count goes from 275 to 2, plus 273 `directory_absent` rows, and the second tick writes only its 2 removals.
   - Both match the D-2 intent. No `AssertRetired`/`AssertIntact`/snapshot assertion was loosened.

## Advisories (owner: Code 250763e5 chain / Mutation)

- **A-1. The backoff for filing refusals never doubles.**
  - Where: `ActAsync` records `RecordCheck(..., "passed")` whenever a check cursor exists. That makes the state `Checked` with repeats 0. A later `ConflictException`, through `HoldAsync`, then records repeats=1, which is 60 min.
  - Effect: a tree whose pre-check passes but whose filing is refused (`mutation_excluded`, `source_landing_excluded`, `repair_source_excluded`, `handoff_pending`, `artifact_unpreserved`, ...) gets a full pre-check, ls-remote included, every 60 min forever. The docs promise "doubling to 24 h".
  - Bound: the cap and the LRU order, so there is no starvation.
  - Fix: keep the previous Held repeats across a passed pre-check until a removal.
- **A-2. The settled record itself is unpinned (M7c survives).**
  - Without it, a missing directory is stat'd every tick instead of once. That costs no git, no cap and no row rewrite.
  - Mutation should pin the `desktop-legacy-check:` settled cursor, or accept M7c as near-equivalent.
- **A-3. `BranchName` misses some spellings.** It does not normalise `heads/<b>` or suffixed revisions (`<b>^{commit}`, `<b>~0`).
  - This holds for both base fields, so it is a pre-existing shape.
  - A queued `-StartRef` in those spellings loses its local base. Dispatch then refuses; origin keeps the work.
- **A-4. A settled missing directory hides a reappearing directory** for the same task while its facts are unchanged.
  - This is the safe direction: nothing is deleted, the tree just leaks.
- **A-5. Drain time.** At the shipped cap of 25 and the hourly cron, the first pass over 7,022 candidates takes at most ceil(7022/25) = **281 ticks, about 11.7 days**.
  - Only trees whose directory exists count; missing directories cost nothing.
  - During the drain, a newly eligible tree waits behind every older present never-checked tree. After the drain, new trees go first while fewer than 25 per hour become eligible.
  - Steady-state rechecks of held trees run about every H/600 days (H = held count at the 24 h cap).
  - At the validator maximum of 100 the drain is about 70 h.
  - Judgment: the drain is bounded and errs on the safe side; slow but acceptable. Consider a temporary operator cap of 100 for the first drain.
- **A-6. Docs.** `docs/orchestration-loop.md` does not say that an unknown key stops startup (ValidateOnStart). One sentence would help operators.

## Not applicable to this diff

There is no session delivery, no queue, no transcript and no input-wait change. The only wait is the in-tick pause (`PauseBetweenSeconds` ≤ 300, inside `MaxTickMinutes` ≤ 55).
