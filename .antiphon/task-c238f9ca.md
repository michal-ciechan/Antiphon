# CARD-1677 S1 Review round 1 (task c238f9ca): retention engine and server job

**Outcome: defects found.** No path in this engine deletes a live tree or anything outside its root: every runner safety check I removed alone turned its witness red, and git's own refusals sit behind the rest. But the server selection has a head-of-line starvation bug (D-1). It makes the operator's on-by-default flip ineffective, and the documented `MaxRemovalsPerTick=1` first step would stall retention outright. There is also one TOCTOU window with no witness (D-2), and three safety checks have no killing witness (D-3).

- **Subject:** `feat/card-task-1296ccbd` at tip `d08bf72b657d9f9fcff61c63f241b0cb52e418a0`. The code tip is `dc33c3e42d75ccbe67b8cef704b0a2220619908c`; the tip adds only the report doc. The red commit is `33be9b27d`.
- **Code landing owner:** task `1296ccbd`.

## Verification

**Checkpoint run** `20261103-071217-12e2` at `d08bf72b6`, rows CP-1..CP-6 and CP-12: verdict **GREEN**, every row `slot=granted`, `sourceState=clean`, `buildSource=verified`, 5 builds. `validate --expected-source-sha d08bf72b6…` printed `CHECKPOINT SOURCE VALID rows=7`. Evidence (gitignored) is in `.antiphon/checkpoints/20261103-071217-12e2/`.

| CP | executed | passed | failed | skipped | Min |
|---|---:|---:|---:|---:|---:|
| CP-1 | 46 | 46 | 0 | 0 | 46 |
| CP-2 | 4 | 4 | 0 | 0 | 4 |
| CP-3 | 35 | 35 | 0 | 0 | 35 |
| CP-4 | 3 | 3 | 0 | 0 | 3 |
| CP-12 | 3 | 3 | 0 | 0 | 3 |
| CP-5 | 54 | 54 | 0 | 0 | 54 |
| CP-6 | 1 | 1 | 0 | 0 | 1 |

**Runs outside the checkpoint list.** Each ran under `scripts/build-slot.ps1` with its own lease, native output and `UseAppHost=false`:
- One `tools/Antiphon.Checkpoints` build, to get the tool dll.
- In a git-archive scratch copy, one env-gated build each of `tests/Antiphon.SessionRunner.Tests` (built twice: a second build added two combined mutants) and `tests/Antiphon.Tests`.
- 55 method-scoped mutant and control runs, each on its own lease.

Every output and the scratch tree are deleted. `git clean -ndX` shows nothing outside `.antiphon/`.

**Merge checks:**
- `scripts/check-evidence-diff.ps1 -BaseRef 7b920d84a -HeadRef HEAD`: `commits=5 entries=1 violations=0`.
- `git merge-tree --write-tree origin/master HEAD` (origin/master `59a37c1675862a7ccc8bc7bc36769e2ac50bc0b8`): clean.
- Against the S3 branch `feat/card-task-0f317c85` (`19062e449`): clean.
- Against `rc/2026-11-01` (`10a826b9d`), replaying each commit with `--merge-base`: not portable as is. The plan and design commits apply. `33be9b27d` and `dc33c3e42` conflict, mainly because `src/Antiphon.SessionRunner/WorkspaceSlotSeams.cs` was deleted on rc (modify/delete), along with the contracts, dispatcher, adapter, runner client, docker-stack and policy json. An rc port needs CARD-0824 S1 first.

## Positive controls run by Review: 52 mutants plus 3 controls (44 red)

Each mutant was env-gated (`C238_MUT`) in a scratch copy and run on its method-scoped witnesses. Control runs with no mutant were green: runner 50/50, runner rebuild 5/5, and server 55/55, the server run including my probe.

**Runner: 27 of 33 red.** (`capgate` is runner code but its witnesses are in Antiphon.Tests, so it is counted under server.)

| Mutant (check removed) | Red witness |
|---|---|
| dirty | Dirty_tree_is_kept(tracked, untracked-nested-file, non-ignored-nested-repo) |
| `--untracked-files=no` | Dirty ×4 |
| status failure treated as clean | Dirty(status-unavailable) |
| unpushed | Unpushed ×2 |
| branch absent on origin | Live_origin(deleted-on-origin, decoy) |
| exact ref name to tail match | Live_origin(decoy-tail-match) |
| tip unknown | Live_origin(origin-advanced-unknown-tip) |
| merge-base contains | Live_origin(origin-reset-behind) |
| holder | Holder(holder), Cwd_holder (native) |
| incomplete scan | Holder(scan-unknown) |
| missing /proc treated as complete | Unreadable_proc_root_is_unknown |
| IsUnderRoot and resolved-parent | Confinement(outside-root, nested-under-root) |
| link and resolved-parent | Confinement(symlinked-leaf) |
| leaf == task id | Confinement(leaf-id-mismatch) |
| slot leaf | Slot(ws-leaf) |
| ledger | Slot(ledger-path) |
| use gate / journal | Ownership(use-owner, removal-journal) |
| locked | Ownership(locked) |
| HEAD branch | Repository_and_branch(other-branch) |
| DryRun moves | Dry_run_reports_would_remove |
| no recheck under the lock | Execute_rechecks ×2 |
| cross-device | Cross_device_retiring_root_moves_nothing |
| retiring root inside the root | Retiring_root_inside… |
| a failed move falls through to phase B on the ORIGINAL path | Move_failure_leaves_the_tree_in_place |
| phase B `--force` | Clean_pushed_…_without_force_or_prune, Phase_b_refusal_keeps_the_moved_tree_registered |
| phase B inside the lock | Phase_b_runs_outside_the_mutation_lock |
| capability advertised off Linux | Capability_is_advertised_only_on_linux(False) |

Runner survivors:
- `IsLink` alone and the resolved-parent check alone. Each masks the other; the combined mutant is red, so this is not a defect.
- The registered check and the residue holder scan. Both are defects; see D-3.
- Two residue-pattern loosenings (advisory A-4).

**Server: 17 of 19 red.**

| Check removed | Red case(s) |
|---|---|
| terminal | queued, dispatched, working, blocked |
| age | settling |
| land | land-pending |
| debt | sync-debt-pending, sync-debt-ready, sync-debt-held |
| shared | path-shared |
| session | session-created, session-starting, session-running, session-stopping |
| slot | slot-bound |
| SourceLanding / RepairSource | source-landing, repair-source |
| role | mutation |
| report | report-unpreserved |

- Gates: Enabled (V-31), Execute forced (V-31), bound (V-32), pacing (V-32c), reread (V-33) and capability (no-capability) were each red.
- Clearing the path on every outcome turned `Outcome_recording(kept, lost)` red.

Server survivors:
- The preview route's forced DryRun. This is a defect; see D-3.
- The dispatcher's `RequireRetentionCapability` (`PhoneHomeCommandDispatcher.cs:962`), checked against CP-6 and the V-39 rows. The adapter's own `Retention()` refusal masks it; advisory A-5.

Every PC stays pending for method-scoped SourceLanding Mutation.

## Defects

### D-1: kept candidates starve newer ones; dry run can never see more than 25 trees

- **Where:** `server/Application/Services/RunnerWorktreeRetentionService.cs:124`, `foreach (var candidate in eligible.Take(settings.MaxRemovalsPerTick))`. `ClassifyAsync` (around line 196) has no memory of a runner refusal, and the population is ordered by oldest `CompletedAt`.
- **Failure:** a tree the runner keeps leaves `RemoteWorktreePath` set. That covers dirty, unpushed, `branch_absent_on_origin` (D-14 keeps these forever), a pooled holder, and `retiring_refused`. Such a tree is re-selected and re-sent on every tick. Once the oldest `MaxRemovalsPerTick` eligible tasks are all runner-kept, no newer task is ever sent again.
  - Probe in the scratch copy (`C238_probe_kept_oldest_candidates_starve_newer_ones`): bound 2, two old dirty trees, one newer removable tree, three ticks. It recorded `sent=kept1,kept2,kept1,kept2,kept1,kept2`, and the newer tree was never sent.
  - With the docs' step 3 (`MaxRemovalsPerTick=1`), a single old kept tree stops retention entirely.
  - In dry run nothing ever clears, so every scheduled tick and every preview re-reports the same oldest 25. The plan §8.1 census of the whole mirror cannot be taken.
- **Why:** D-4's "the backlog drains in about 5 ticks" claim fails, and the disk refills to 100 percent. On by default, this is the steady state within days, because failed Code tasks routinely leave dirty trees.
- **Fix:** keep a per-task runner-outcome cursor (the CARD-1446 `WorktreeResidueCandidateCursor`, key `runner-retention:<id>`, holding the last verdict and reason plus `HeadSha`), and use it to deprioritise or back off unchanged runner `Kept` and `RetiringRefused` answers. For example, re-send such a task at most once per `KeptRetryHours` (say 24), and order never-sent or least-recently-sent tasks first. A DryRun `would_remove` must also advance the cursor, so that successive dry ticks cover the whole population.
- **Witness to add:** N+1 eligible tasks, N of them kept by the runner, bound N, two ticks. Assert that the newer task is sent on tick 2, and that a dry-run pair of ticks with bound N covers all N+1.

### D-2: re-dispatch TOCTOU after the server's reread, with no witness (liveness, not data loss)

- **Where:** the server rereads the row (`RunnerWorktreeRetentionService.cs:128`) and sends. The runner (`RunnerWorkspaceRetention.CheckAndMoveAsync`) cannot see the task table. Its only liveness guards are the `/proc` holder scan and the clean and pushed checks. Production installs no `WorkspaceUseGate`, so that gate is a no-op.
- **Failure:** a task is reopened and dispatched to server2 within the roughly 1-3 s between the reread and phase A. All three runner operations (`WorkspaceMirror`, `WorkspaceRetain` phase A, `Launch`) are serialised by `MutationLock`, so the interleavings are:
  - **Mirror, then phase A, then Launch.** Mirror reuses the existing tree at the same SHA. Phase A then sees a clean, pushed tree with no process and moves it. Launch starts with a missing cwd and fails.
  - **Phase A first.** Mirror's `git worktree add -B <branch>` fails with "already used by worktree" while the branch is still checked out in the moved residue. This persists if phase B refused (`retiring_refused`); the residue is cleared only by a later retention request for that task, 12 h after it fails again.
  - No work is lost, because the tree was clean and the branch on origin contains HEAD. The cost is a failed dispatch.
- **Why:** this is the brief's "TOCTOU window without a witness". On by default it runs 25 times every 30 minutes.
- **Fix:** have the runner refuse (`recently_mirrored` or `workspace_in_use`) when either holds:
  - one of its own non-Exited sessions has its cwd at or under the path (`_runtime.List()`);
  - a `WorkspaceMirror` for that path or name was admitted within the last N minutes (for example `LaunchGraceMinutes`, 15).

  Record the stamp in the mirror op under the same lock.
- **Witness to add:** a dispatcher-level test that runs Mirror (reuse) and then Retain(Execute), and expects Kept with the tree in place.

### D-3: three safety checks with no killing witness (mutants survive)

- **Where and what survives:**
  1. **Preview route forced DryRun.** `RunnerWorktreeRetentionService.cs:71`, `var execute = settings.Execute && previewRunner is null`. Dropping the `previewRunner is null` term leaves the whole `C1677RetentionServerTests` class green (45/45). With on-by-default (`Execute=true`) and that term lost, the operator's "always DryRun" preview would remove up to 25 trees.
  2. **Registered in the runner repository.** `RunnerWorkspaceRetention.cs:277`, `if (block is null)`. Dropping it leaves all 50 runner tests green. Git's own `worktree move` refusal masks it in Execute, but DryRun would report `would_remove` for an unregistered tree.
  3. **Residue retry holder scan.** `RunnerWorkspaceRetention.cs:400`, inside `ResidueOrAbsentAsync`, which is plan S-5's "holder scan … checked first". Dropping it leaves all 50 green. Phase B would then delete a residue in which a live process has its cwd; `git worktree remove` does not look at processes.
- **Why:** the brief makes any surviving safety mutant a defect.
- **Fix (tests only):**
  - Preview with `Settings.Execute=true`: assert the request `Mode == DryRun` and that the path is not cleared.
  - A tree moved with plain `mv`, so its porcelain path differs from the request path: assert Kept `not_runner_repository`, including in DryRun.
  - A residue holding a cwd (native class) or a fake `IWorkspaceHolderScan`: assert Kept `live_process` and that `worktree remove` is never called.

## Answers to the brief's questions

**(1) Path safety: sound.**
- The root is the runner's own `<AllowedCwd>/worktrees`; the request path is checked lexically with `IsUnderRoot`, which allows a direct child only and refuses `.`, `..` and `//`.
- `^task-[0-9a-f]{8}$` is lowercase and ordinal, so a trailing dot or a case variant is refused. The leaf must equal the request's `task-<id8>`, and the branch's short id must equal the leaf.
- A link leaf is refused by `IsLink`, and the leaf resolved through `TryResolveFinal` must equal the resolved root joined with the leaf. A symlink pointing outside is refused before any git operation; the combined mutant shows the witness catches it.
- The owning repository must be the runner primary or a `RepositoriesRoot` child, and `git worktree list --porcelain` must contain the exact path (D-3 item 2 is the missing witness for that).
- The move destination is `<AllowedCwd>/worktrees-retiring/<leaf>.<request8>`: runner-built, a sibling of the root and never inside it (`RetiringRootValid` plus the server validator). It must be on the same `st_dev`, and an existing destination keeps the tree.
- It is never a candidate: candidates come from the task table and the leaf regex.
- Phase B is `git -C <owning repo> worktree remove <moved>`, **git semantics, never `rm`**. The path passed is exactly the moved path phase A built, or a `RetiringRoot` entry matching `<leaf>\.[0-9a-f]{8}$` that is a real directory. Git removes only a registered worktree, repeats its own clean check, refuses locked trees and submodules, and deletes ignored content. That deletion is accepted by design in plan D-9, including nested ignored repos, which are counted.

**(2) Checks on each side.**
- Runner-only: dirty, untracked, unpushed, branch absent, exact ref, locked, holder, slot leaf and ledger, registered.
- Server-only, because the runner has no task table: too recent, non-terminal, land pending, sync debt, shared path, slot id, SourceLanding/RepairSource/Mutation, live session, report.
- Slot appears on both sides.
- A re-activation after the reread is D-2. Plan D-7 records that the holder scan skips other-uid processes and does not count them, although D-7 says "their count is recorded" (advisory A-1).

**(3) Phase split: sound and witnessed.** Phase B runs outside the lock (inlock mutant red). Phase A waits for the lock (`Phase_a_waits_for_the_mutation_lock`). A failed move keeps the tree, and the most dangerous variant, phase B on the original path, is red. A crash between A and B leaves `<leaf>.<req8>` in the retiring root. The path stays set (`lost`, so Deferred), the next tick finds the leaf absent and retries phase B on that residue only (`Retain_recovery_at_each_handoff(crash-between-phases)`). The residue is never re-adopted, because Mirror uses `<root>/<name>` only. But the residue keeps the branch checked out, which blocks a re-mirror (D-2).

**(4) On by default.**
- The desktop is never contacted: the capability is advertised only on Linux (witnessed), the server requires a listed phone-home client advertising it (witnessed), and the runner composes the engine only when `PhoneHome.Enabled` and Linux.
- Pacing is real: one at a time (`Second_request_waits_for_the_first_reply`) and a 5 s pause (`Requests_are_paced`, red under the pacing mutant).
- There is **no per-tick time bound**. The worst case is 25 × 300 s plus pauses, about 2 h 7 m, against a 30 min cron and `DisableConcurrentExecution(3600)`; later ticks wait up to 1 h and then fail (advisory A-2).
- A dry-run override while `Execute=true` exists only through the preview route, and its forcing is unwitnessed (D-3 item 1).
- Kill switch: `Enabled=false` is read once per tick (`:61`). An in-flight tick keeps sending its remaining candidates, and the runner finishes an in-flight request regardless. The recurring job is removed only at startup (advisory A-3).

**(5) Concurrency and noise.**
- Scheduled ticks are single-flight through Hangfire's storage lock, which is shared by servers on one database. The preview route is outside that lock but always DryRun.
- Concurrent requests for one task serialise phase A. A second Execute finds the residue and may run a concurrent `git worktree remove` of the same moved tree; one fails as `retiring_refused`, and nothing outside is touched.
- A server restart mid-request is a `lost` row with the path kept, then `already_absent` or a residue retry.
- **No alarm storm.** No attention, Telegram or alert consumer reads `AgentTaskEventType.Warning` generically. The existing readers filter on their own detail prefixes or on parked or interrupted turns, and `AlertSeverity` is a separate system. The event is one per removal, on the task timeline only.
- Logs carry runner paths and up to 500 characters of `git status` output (file names); no secrets.

**(6) Worker deviations.**

| Deviation | Verdict |
|---|---|
| Removal recorded as a Warning event | Acceptable: no consumer pages on it |
| Owner hint stored in `Branch` | Acceptable stopgap: `TaskId` carries the owner and `Branch` is `owner_hint:…`. A dedicated column needs the migration D-8 avoids |
| Unlisted runner writes no row | Acceptable: there is no runner to name |
| Unchanged held rows not rewritten each tick | Fine for held rows. But the same dedupe was never applied to the selection itself, which is D-1 |

## Advisories

- **A-1.** D-7 says the other-uid count is recorded, but `ProcWorkspaceSlotProcessProbe.Probe` counts nothing. Record skipped pids in `HolderScan`.
- **A-2.** Bound the tick: stop sending when `elapsed > Cron period - RequestTimeout`, or pass a tick deadline.
- **A-3.** Re-read `CurrentValue.Enabled` and `Execute` before each send, so that the kill switch stops a running tick.
- **A-4.** V-28 does not pin residue selection. Loosening the regex to `^task-` survives because of ordinal order; an "other residue sorts first" case would pin it.
- **A-5.** The dispatcher capability gate is redundant with the adapter's `Retention()` refusal (it survives). A fake runtime that implements the members but does not advertise the capability would witness it.
- **A-6.** `path_shared` ignores recently terminal tasks on the same path. That is acceptable, because the runner's checks still guard the tree.

## On-by-default flip (follow-up Code commit): exact edits

| File | Line(s) | Edit |
|---|---|---|
| `server/Application/Settings/RunnerWorktreeRetentionSettings.cs` | 15 | `public bool Enabled { get; set; } = true;` |
| same | 18 | `public bool Execute { get; set; } = true;` |
| same | 21 | Keep `Runners = []` in C# and set `["server2"]` in appsettings only. The List binder appends to an initializer, so an initializer gives `server2,server2`, which `Distinct` hides, and no config can then empty the list. |
| same | 6-9, 17, 20 | Doc comments |
| `server/appsettings.json` | 223-225 | `"Enabled": true`, `"Execute": true`, `"Runners": ["server2"]`. Keep `MinTerminalHours` 12; `TempSweep` stays off. |
| `server/Program.cs` | 229 | Comment |
| `src/Antiphon.SessionRunner/Program.cs` | 81-83 | Comment |
| `server/Infrastructure/Agents/RunnerWorktreeRetentionJob.cs` | doc | "does nothing while Enabled is false" stays true; reword "ships" |
| `tests/Antiphon.Tests/Application/C1677RetentionSettingsTests.cs` | 17-41 | `Shipped_defaults_are_off_and_dry_run`: rename it, and flip the six Enabled/Execute/Runners assertions for type and JSON. `Runners` becomes `["server2"]` in JSON and stays empty in C#. `TempSweep` assertions are unchanged. |
| `docs/docker-stack.md` | 715-727 | Replace "It ships off…" and the three steps with: on by default for server2 with Execute; rollback is `Enabled=false` (or `Execute=false` for dry run); the preview route for a census |
| `docs/ops-http.md` | 191 | "(`RunnerWorktreeRetention:Enabled` and `Runners` must name it)": still true; mention the default |
| Plan §D-1/D-13 text and test design V-36 / PC-78..80 | n/a | Note the operator decision of 2026-11-03 |

**Is the flip safe?** On data loss, yes: no path deletes a live or out-of-root tree, and everything the runner refuses is kept. But **flip only together with the D-1 fix**: otherwise on-by-default stalls within days and the disk refills. Fix D-2's guard and D-3's witnesses in the same Code pass. With D-1 fixed, the first enabled tick removes at most 25 trees per 30 minutes, paced 5 s apart.
