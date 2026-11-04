# CARD-0824 S4a: review 1dcb8d90 round-1 fixes (task 56e7e9b7)

**Outcome:** D-1 and D-2 are fixed, and so are A-1, A-2, A-3 and A-4. Both closed-list checkpoints are green at the tested SHA. Every new witness was shown red first, either against the unfixed code or against a scratch mutant.

## Where things are

- Branch: `feat/card-task-56e7e9b7`.
- Worktree: `/work/worktrees/task-56e7e9b7`.
- Base: `7ead8f06662f15bff260836f86dd5027b5384820`.
- Original Code task (landing owner): `250763e5-1b7a-466b-a6e4-70ac7c7c9bca`.
- Tested SHA: `07d568a82ded795eeba3762089c4ff585992b7db`. The tip after it adds only this report.
- Plan: `docs/superpowers/plans/2026-11-03-card-0824-s4a-test-design.md`. It is unchanged; Min 109 is a floor, and the run executed 120.

Commits:

| SHA | What it contains |
|---|---|
| `19dd926fac1143352c7880e2b9b3b34f8360981c` | The red witnesses |
| `5a2848f906d21a6cd25eb33a33a189713eb0397b` | The fix, plus the docs/orchestration-loop.md paragraph |
| `07d568a82ded795eeba3762089c4ff585992b7db` | The backlog witness now also pins the backoff, its doubling and the pause rule |

## Checkpoints

Run `20261104-015309-3086`, through the checkpoint tool (`start --rows CP-23,CP-24`, then `wait`). The lines below are unedited:

```
CHECKPOINT CP-23 commit=07d568a82ded795eeba3762089c4ff585992b7db build=ok filter=/*/*/(C0824DesktopLegacyReleaseSettingsTests*)|(C0824DesktopLegacyReleasePolicyTests*)/* executed=120 passed=120 failed=0 skipped=0 trx=/work/worktrees/task-56e7e9b7/.antiphon/checkpoints/20261104-015309-3086/rows/CP-23/run.trx slot=granted waited=15s dirty=0 source=07d568a82ded795eeba3762089c4ff585992b7db sourceState=clean buildSource=verified
CHECKPOINT CP-24 commit=07d568a82ded795eeba3762089c4ff585992b7db build=reused filter=/*/*/(TaskWorktreeRetirementTests*)|(WorktreeResidueSweepTests*)/* executed=55 passed=55 failed=0 skipped=0 trx=/work/worktrees/task-56e7e9b7/.antiphon/checkpoints/20261104-015309-3086/rows/CP-24/run.trx slot=granted waited=15s dirty=0 source=07d568a82ded795eeba3762089c4ff585992b7db sourceState=clean buildSource=verified
PHASES CP-23 slotWait=30s build=291.3483453s startup=15.7796005s testsWall=103.3876042s teardown=6.2187061s hostWall=125.3860616s
verdict: GREEN exit=0 ; unlisted: none (the tool ran no other build or test command)
```

Other checks:

- `validate --expected-source-sha 07d568a8…` printed `CHECKPOINT SOURCE VALID rows=2`.
- `check-evidence-diff`:
  - `7ead8f066..07d568a8`: commits=3, violations=0;
  - `591fafecb..07d568a8`: commits=7, entries=1 (the earlier report), violations=0.
  - Run again over `..HEAD` after this report was committed (see the final message).
- `merge-tree` against origin/master `2b342761f` is clean.

Ordinary V/R:

- CP-23 covers V-1..V-27 and R-3; CP-24 covers R-1 and R-2. All are green.
- CP-25 (Windows-native LN, V-27/N-1) did not run. It is still owed on the desktop lane, as the brief says ("nothing runs on the desktop").

Timing: the new witness `Held_backlog…` took 76.4 s against its `[Timeout(180_000)]`. V-25 `execute` took 43.5 s.

## Unlisted runs (scratch detached worktree in the scratchpad, now removed; one build-slot lease each)

1. **Red proof.** One build at `19dd926f` (red tests on the unfixed code), then one run of the new and changed methods: 35 executed, 10 failed, all of them the intended reds:
   - `Server_predicates(startref-queued, startref-queued-origin, basetask-queued)`: `Removed` where `Held` was expected;
   - `Held_backlog`: 6 pre-checks with cap 2;
   - `First_enable(preview-first, execute)`;
   - `Switch_off_during_the_pause`;
   - `Dry_run_holds(preview, execute-false)`: `Preview` where `Held` was expected;
   - `Non_scalar("Enabeld")`.
2. **Early check of the fix.** One build at `5a2848f9`, then CP-23's two classes: 120/120.
3. **Mutants.** One build at `07d568a8` with env-gated mutants (`C0824_MUT`) in the scratch copy only, never committed, then 13 method-scoped runs. The control run (no mutant) passed 33/33. Every mutant was killed:

| Mutant | Killed tests (assertion) |
|---|---|
| M1 the validator ignores BindError (A-1) | `Non_scalar("Enabled":"no")`, `("Execute":"0")`, `("Enabeld")`: 3/11 red |
| M12 drop `ErrorOnUnknownConfiguration` (A-4) | `Non_scalar("Enabeld")`: 1/11 |
| M2 drop the `WorktreeBaseRequestedRef` dependent check (D-1) | `Server_predicates(startref-queued, startref-queued-origin)`: 2/18 |
| M3 drop the `RequestedWorktreeBaseTaskId` dependent check (D-1) | `Server_predicates(basetask-queued)`: 1/18 |
| M4 a held pre-check does not count against the cap (D-2) | `Held_backlog`: "tick 1: at most the cap in pre-checks" (6 > 2) |
| M5 ignore the held backoff (D-2) | `Held_backlog`: "a held tree inside its backoff is not pre-checked" |
| M6 no doubling of the held backoff | `Held_backlog`, the same assertion (60 min has elapsed) |
| M7 no settling of a missing directory (D-2) | `First_enable(preview-first, execute)` and `Held_backlog` (absent rows take the cap): 3/3 |
| M8 rewrite unchanged `over_cap` rows | `Held_backlog`: "no unbounded row growth" (7 > 4) |
| M9 pause after every pre-check | `Held_backlog`: pauses 4 where 1 was expected |
| M10 read the switch before the pause (A-2) | `Switch_off_during_the_pause`: paused 2, so the second tree was removed |
| M11 caller-release check after the dry-run branch (A-3) | `Dry_run_holds(preview, execute-false)`: 2/2 |

## What changed

The service is `server/Application/Services/DesktopLegacyReleasePolicyService.cs`.

**D-1.** `Classify` → `dependent_open` now also holds when an open task has either of these:

- `WorktreeBaseRequestedRef` naming the branch, in any spelling `BranchName` accepts (bare, `refs/heads/`, `origin/`, `refs/remotes/origin/`);
- `RequestedWorktreeBaseTaskId == task.Id`.

The `OpenTask` projection carries both fields. Three Queued cases were added to V-9. The wrong `startref-open` comment is corrected.

**D-2.** These changes are in `RunTickAsync`, `ActAsync`, `CursorRowAsync` (which replaces `HeldRowAsync`), `HoldAsync`, `RecordCheck` and `CheckState`:

- **Every pre-check counts against `MaxReleasesPerTick`**, whatever its outcome. This is T-D-7 as written. The old `Acted`-only count is gone.
- **Held pre-checks get a recheck cursor.** A Held result of the pre-check or the filing (`source_dirty`, `unpushed`, `remote_unverified`, `release_active_other`, a service ConflictException code, …) records a `desktop-legacy-check:<id>` cursor:
  - the state is stored as `check|held|<repeats>|<fingerprint>|<reason>` in `LastOutcomeKey`;
  - `NotBefore` is `RefusedRecheckMinutes × 2^(repeats-1)`, capped at `RefusedRecheckMaxHours`, the T-D-4 shape: 60 min doubling to 24 h;
  - the fingerprint covers Status, Attempt, CompletedAt, WorktreePath and WorktreeBranch, so a changed task is re-checked at once.
- **A missing directory is settled.** When the task has no active release for its attempt, a missing directory is detected by one `File.GetAttributes` in the classification loop: no DB re-read, no git, no cap. It is recorded once as `check|settled|…|directory_absent` and never re-checked while the fingerprint holds. A directory with a filed release is never settled, because the guarded removal must finish a tree that was set aside (the V-23 crash cases stay green).
- **Order:**
  1. never-checked tasks, oldest `CompletedAt` first;
  2. then due retries and re-checks, least recently evaluated first.
  - A dry run (`Execute=false`) records `checked|would_release`, so its census round-robins instead of re-checking the same oldest 25.
  - An executing pass only resets a held state that already exists; it creates no cursor per filing, which avoids new unique-key inserts in concurrent ticks.
- **Unchanged rows are not rewritten.** An unchanged Held or `over_cap` row is not rewritten on a scheduled tick, and its cursor is not touched. The preview still writes every row.
- **The pause follows only a real filing or removal** (`ActOutcome.Wrote`).

Other changes:

- **A-2.** The switch is read after the pause.
- **A-3.** A caller-filed release is held as `release_active_other` before the pre-check and before the dry-run branch.
- **A-4.** `Bind(options, o => o.ErrorOnUnknownConfiguration = true)` turns an unknown key into a BindError, which is refused. This is fail-closed, and with ValidateOnStart a misspelled key also stops startup, like any invalid value.
- **A-1.** Witness cases only.

Deletion guards are unchanged; this task only adds refusals and bounds.

Two existing tests changed expectations by design:

- **V-25.** The preview now shows 2 `over_cap` and 273 `directory_absent` rows. Before, it showed 275 `over_cap`. In `execute`, the first tick writes the 273 `directory_absent` rows and the second writes only its 2 removals. Before, it rewrote 273 rows, which was the D-2 defect itself.
- **V-24 `throws` and `lease-busy`.** These now run with cap 3, not 2. A failing pre-check now counts against the cap, so with cap 2, C would have moved to the next tick. All assertions are unchanged.

Docs: `docs/orchestration-loop.md` now names the dependent spellings, the pre-check bound, backoff and settling, and the refusal of unknown keys.

## Advisories not taken

- **Projecting only the needed columns** (the 19 MB candidate load): not in the brief's fix list, left as is.
- **A-5** (the residue sweep with Execute=true bypasses S4a): shipped `false`; this is outside this task.
- **A-6:**
  - p95 re-basing is still owed;
  - V-25 `execute` 43.5 s against its 60 s in-test bound;
  - the new backlog test 76 s against 180 s.
- **A-7:** consistent with git, so not changed.
- **A-8** (a reopened T-D-4 attempt on a tree that became dirty): not taken, no witness added.
- **Concurrent ticks.** Two concurrent scheduled ticks that both create the same new held or settled cursor would hit the unique `CandidateKey` index. The same was already true of the pre-existing Held row cursors. Hangfire `DisableConcurrentExecution` prevents it, and the preview writes no cursor. Noted, not changed.

## Pending for Mutation

- **PC-1..PC-68** (the plan's controls) are all pending.
- The new witnesses have no PC rows in the plan:
  - the `Held_backlog…` method;
  - `Switch_off_during_the_pause…`;
  - `Dry_run_holds_a_caller_filed_release` (×2);
  - V-9 `startref-queued`, `startref-queued-origin` and `basetask-queued`;
  - V-3 `Enabled:"no"`, `Execute:"0"` and `Enabeld`.

  Mutation should add controls for them. M1–M12 above can serve as the candidate list (unbound, candidate docs under `docs/superpowers/mutations/`).
