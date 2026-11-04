# CARD-0824 S4a Review round 1 (task 1dcb8d90): findings

**Outcome: 2 defects (D-1, D-2), neither of them unsafe deletion of work.**

- Subject: Code task `250763e5-1b7a-466b-a6e4-70ac7c7c9bca`, branch `feat/card-task-250763e5`.
- Tested SHA: `bd502f519606fe49e5b36033cf4c9c2f1ea27e12`. The tip `7ead8f066` adds only the report.
- Base: `591fafecb`.
- Safety result: no path removes a dirty, unpushed, foreign or outside-root tree. No path runs Execute while disabled or with invalid settings. No path forces a locked directory.
- D-1: the open-dependent guard misses queued dependents, so their start branch is deleted.
- D-2: the per-tick cost is not bounded, as T-D-7 promises. Liveness and cost only.

## Runs (all on server2, each with its own build-slot lease)

### Checkpoints

Checkpoint run `20261104-004252-0133`, at bd502f519, through the checkpoint tool:

```
CHECKPOINT CP-23 commit=bd502f519606fe49e5b36033cf4c9c2f1ea27e12 build=ok filter=/*/*/(C0824DesktopLegacyReleaseSettingsTests*)|(C0824DesktopLegacyReleasePolicyTests*)/* executed=110 passed=110 failed=0 skipped=0 trx=/work/worktrees/task-1dcb8d90/.antiphon/checkpoints/20261104-004252-0133/rows/CP-23/run.trx slot=granted waited=0s dirty=0 source=bd502f519606fe49e5b36033cf4c9c2f1ea27e12 sourceState=clean buildSource=verified
CHECKPOINT CP-24 commit=bd502f519606fe49e5b36033cf4c9c2f1ea27e12 build=reused filter=/*/*/(TaskWorktreeRetirementTests*)|(WorktreeResidueSweepTests*)/* executed=55 passed=55 failed=0 skipped=0 trx=/work/worktrees/task-1dcb8d90/.antiphon/checkpoints/20261104-004252-0133/rows/CP-24/run.trx slot=granted waited=0s dirty=0 source=bd502f519606fe49e5b36033cf4c9c2f1ea27e12 sourceState=clean buildSource=verified
PHASES CP-23 slotWait=0s build=317.9s startup=8.9s testsWall=100.7s teardown=3.5s
SLOW CLASS C0824DesktopLegacyReleasePolicyTests 321s tests=84
verdict: GREEN exit=0 ; unlisted: none ; source state=clean buildSource=verified
```

- `validate --expected-source-sha bd502f519…`: `CHECKPOINT SOURCE VALID rows=2`.
- CP-25 (Windows-native, LN, Min 6) was not run. It is owed on the desktop lane after the live rc run.

### Other checks

- `check-evidence-diff`:
  - over `591fafecb..bd502f519`: commits=3, violations=0;
  - over `..7ead8f066`: commits=4, entries=1 (the report .md), violations=0.
- Merge-tree against origin/master `81e732dd4`: clean, for both bd502f519 (tree `f15ae123b`) and 7ead8f066. Master's 9 commits since the base touch none of the S4a files (only S1c Program.cs lines elsewhere).
- C1323 census at the merged tree: `scripts/test-rc-platform.ps1 -Case LinuxOnlyWindowsExclusionPolicy` passed 6/6. No Linux-only class, so no policy or pin change is needed (D-9 confirmed).

### Unlisted runs, with reasons

- One `Antiphon.Tests` build in a scratch detached worktree at bd502f519, with env-gated mutants plus 3 probe cases. Never committed.
- 9 method-scoped mutant and probe runs (below), one lease each.
- A read-only SELECT on the desktop DB, to size D-2.
- Scratch worktrees and all bin/obj outputs are deleted.

### Mutants (env-gated, one build; each run is method-scoped)

| Mutant | Result | Killing test |
|---|---|---|
| drop the dirty pre-check | killed 3/6 | Dirty_or_unpushed(tracked-modified / untracked / staged): Held expected, filed instead |
| drop the unpushed pre-check | killed 2/6 | Dirty_or_unpushed(unpushed-commit, origin-branch-deleted) |
| guarded removal ignores ContainsSource | killed 1/3 | Removal_re_proves(origin-rewound-after-filing): Refused expected |
| drop the age floor | killed 2/3 | Age_floor(47h59m, null) |
| drop DirectChildOfRoot | killed 1/15 | Path_escapes(symlink-ancestor): `unregistered` instead of `outside_root`; later guards still hold |
| kill switch read once per tick (Enabled ignored per row) | killed 1/2 | Switch_off(enabled) |
| invalid reload falls back to ON defaults | killed 1/1 | Invalid_reload_fails_closed_then_recovers |
| BindError not validated (D-2 seam) | **survived 8/8** | see A-1 |

## Defects

### D-1: the open-dependent guard misses queued dependents, so their start branch is deleted (T-D-5 / G-21)

- **Where:** `DesktopLegacyReleasePolicyService.Classify` (`dependent_open`, around line 440). It checks only `ParentTaskId` and `WorktreeBaseRef`.
- **Failure:** a Queued task created with `delegate.ps1 -StartRef feat/card-task-X` stores the ref in `WorktreeBaseRequestedRef`. `WorktreeBaseRef` stays null until provisioning (AgentTask.cs:291-304; DelegationWorktreeService.cs:474).
  - The policy therefore files and retires X, deleting X's local branch.
  - A branch-name start ref must resolve locally: StartRefAvailability fetches only full SHAs, and dispatch never fetches. So the dependent's provisioning breaks.
  - The same gap exists for a Queued `RequestedWorktreeBaseMode=Task` / `RequestedWorktreeBaseTaskId=X`.
- **Measured:** probe cases `probe-startref-queued` and `probe-basetask-queued` added to `Server_predicates_keep_the_tree` went red. The outcome was `Removed` where `Held dependent_open` was expected.
- **Why the test missed it:**
  - The `startref-open` witness seeds a Dispatched task with `WorktreeBaseRef`.
  - Its comment ("WorktreeBaseRef is the persisted dispatch start ref") is wrong per the entity docs.
  - The TD named "the delegate request's StartRef persisted on the task".
- **Fix:** also refuse an open task whose `WorktreeBaseRequestedRef` (BranchName-normalised) names the branch, or whose `RequestedWorktreeBaseTaskId == task.Id`. Add both Queued cases to V-9.

### D-2: neither pre-check-held nor absent candidates are capped, backed off or cursor-deduped, so tick cost grows with all task history (T-D-7, G-9, G-58, performance standards)

- **Where:** `RunTickAsync` / `ActAsync`. A pre-check Held row (`directory_absent`, `source_dirty`, `unpushed`, `remote_unverified`, `main_worktree`, …):
  - returns `Acted=false`;
  - stamps no retry cursor;
  - writes with `writeAll:true`, which bypasses the CARD-1446 cursor rule.
- **Failure:** every hour, every such candidate is processed again, first, because "never attempted" is ordered first. Each one costs:
  - a fresh re-read plus `Facts.LoadAsync`, which loads all open tasks and all live sessions: about 7 queries;
  - `git worktree list`, `status`, `rev-parse` and `ls-remote` for trees that exist;
  - one inserted row;
  - a 5 s pause before each candidate once one has acted.
- T-D-7 says "a first enable over hundreds of old trees costs at most MaxReleasesPerTick pre-checks". The probe `Probe_held_backlog_prechecks_are_capped` (8 dirty trees, cap 2) measured `status_calls=8` on each tick, and was red. V-25 itself asserts that the second tick rewrites 273 `directory_absent` rows.
- **Live size (read-only SELECT on the desktop DB, 2026-11-04):**
  - 7,022 tasks have Workspace=Worktree, WorktreePath set and no Complete retirement; 6,620 of them are more than 48 h old;
  - landing never clears WorktreePath;
  - the hourly query also loads the full entities (Result totals 19 MB).
- **Consequence:**
  - about 7k candidate evaluations, about 50k queries and up to about 7k inserted rows per hour, on by default;
  - the 20 min budget and the per-candidate pause make reaching newer eligible trees depend on the held backlog;
  - a due retry is starved behind every never-attempted held row.
  - Safety is unaffected: this errs toward a false keep.
- **Fix:**
  - stamp a recheck cursor for pre-check Held rows (the 1677 KeptRecheck pattern), and dedupe unchanged Held rows through the cursor;
  - treat `directory_absent` as settled until the path reappears;
  - count pre-checks against a per-tick budget;
  - pause only after a real filing;
  - project only the needed columns.
- **Missing test:** a backlog of more than the cap of held or absent candidates, asserting at most the cap pre-checks per tick and a due retry reached.

## Advisories

- **A-1 (guard with no failing test): BindError.**
  - The `"Enabled": null` case is also refused by the shape validator, so the BindError check is masked: its mutant survived.
  - A non-convertible scalar (`"Enabled": "no"`, `"0"`, `"fasle"`) reaches only BindError. Dropping BindError there would keep the shipped-on default.
  - Add such a case to V-3.
- **A-2: kill-switch race.** `StopReason` runs before the 5 s `Task.Delay`, so a switch-off during the pause still lets one more row act. Re-check after the pause.
- **A-3: the dry run misreports an operator-filed release that has no intent.** The pre-check passes and the row says `would_release`, but Execute would hold it as `release_active_other`. Check the caller identity before the `!execute` branch.
- **A-4: unknown keys are not refused.** A misspelled kill switch (`"Enabeld": false`) leaves S4a on. Consider refusing unknown children in the shape validator.
- **A-5: the residue sweep, if ever Execute=true, bypasses S4a's controls.**
  - It retires and reopens system releases outside the S4a kill switch, backoff and MaxRefusedAttempts, because the T-D-4 reopen keys only on the caller identity.
  - Shipped `WorktreeResidue:Execute=false`, so there is no current impact.
- **A-6 (D-8): timeouts.**
  - V-25 `execute` took 58.0 s here, the whole test, against its in-test 60 s wall bound on the first tick. That bound is close.
  - The other 180 s methods measured 39 s at most.
  - p95 re-basing is still owed.
- **A-7: reflog-only commits are not protected.** A branch reset behind an unpushed commit is removed. This is consistent with git and the TD, so it is noted only.
- **A-8: no witness for a reopened T-D-4 attempt on a tree that became dirty between attempts.** It is guarded through the shared Full inspection, but untested.

## Verified sound

- **Settings:**
  - the shipped defaults are on;
  - null, array and object values are refused (through both the shape validator and BindError);
  - an invalid hot reload counts as disabled on every read;
  - ValidateOnStart is set;
  - the job is registered only when Hangfire `ServerEnabled` is true, and the Docker stack sets it false, so it never runs against container paths.
- **Pushed proof:**
  - the pre-check uses a live `ls-remote` and `merge-base`;
  - the guarded removal re-proves with ls-remote, a fetch into a unique pin and ancestry, under the lease;
  - the own branch falls back to the target only when origin lacks the branch;
  - a moved or rewound branch refuses.
- **Name and path checks:**
  - leaf `^card-task-[0-9a-f]{8}\z` equal to the task id;
  - control characters refused first;
  - GetFullPath normalisation, so `..`, trailing dot/space and 8.3 names compare unequal and go to outside_root;
  - a direct child of the root;
  - a reparse point at the leaf refused;
  - main worktree, master/rc and foreign branches refused (`DelegateBranchName.IsOwned`);
  - ws- slot and leased-slot guards.
- **Locked directory:** MoveAside fails, so the outcome is Refused with nothing moved; the T-D-4 reopen is bounded at 5 attempts and never forced. N-1 is owed on Windows.
- **Caller-filed release:** untouched (`release_active_other`; V-26 one command).
- **D-3:** the tracked reload applies to system releases only.
- **D-6 and D-7:** they substitute no-mutating-git for no-git, and do not weaken the witnesses (mutants killed).
- **T-D-6:** a mirror is skipped only for a listed runner.

Commands to rerun: `dotnet run --project tools/Antiphon.Checkpoints -- start --plan docs/superpowers/plans/2026-11-03-card-0824-s4a-test-design.md --expected-source-sha <sha>`, then `wait`.
