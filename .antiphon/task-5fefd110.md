# CARD-1677 S1 review round 1 fixes (task 5fefd110): D-1, D-2, D-3, A-2, A-3, and the on-by-default flip

**Outcome: done.** The changes are a red commit, then a green commit.

- **Checkpoints:** CP-1..CP-6 and CP-12 ran GREEN through the checkpoint tool at `b8f6af3df59af81996e3739f9bcdcf887bb056b7`. `validate` printed `CHECKPOINT SOURCE VALID rows=7`.
- **Mutants:** 28 env-gated mutants, every one red on its named witness: 18 old single-check removals and 10 new.
- **Review findings:** D-1, D-2 and D-3 (a, b, c) are fixed or witnessed, and so are advisories A-2 and A-3.
- **Defaults:** they ship ON, as the operator decided.

## Where things are

- **Branch:** `feat/card-task-5fefd110`, continued from `d08bf72b657d9f9fcff61c63f241b0cb52e418a0` (the tip of `feat/card-task-1296ccbd`). Base master is `7b920d84a`.
- **Runner mirror:** `/work/worktrees/task-5fefd110`.
- **Landing owner:** the original Code task `1296ccbd`.
- **Commits:**
  - `afdbff73110a2745b92b32779c4b9e98ae73d010`: the red commit. It holds the witnesses, a compile-only shim (the `TickResult` fields, the new settings left unread, the runner's `recently_mirrored` code and `PhoneHome:RetentionRecentUseMinutes`), and the test-design amendments.
  - `b8f6af3df59af81996e3739f9bcdcf887bb056b7`: the green commit. It holds the fixes, the flip, the docs and the plan notes.
  - This report is committed on top.
- **Plan and test design:**
  - `docs/superpowers/plans/2026-11-03-card-1677-runner-worktree-retention-plan.md`: new D-15, I-10 marked superseded, the enabling section, V-36.
  - `docs/superpowers/plans/2026-11-03-card-1677-test-design.md`: new section "Review c238f9ca round-1 repairs" with V-70..V-80 and G/PC-121..135. The CP-1 Min floor is now 53 and the CP-5 Min floor is 66.
- **No csproj or pins edit.** The land graph was not touched.
- **`scripts/check-evidence-diff.ps1 -Repository . -BaseRef 7b920d84a -HeadRef HEAD`:** `commits=7 entries=1 violations=0`.
- **Landability:**
  - `git merge-tree --write-tree origin/master HEAD` against `a06d64f71` shows an **add/add conflict on the two plan docs only**. Master landed the plan and test design through their own commits, and this branch edits them.
  - A scratch `git rebase --onto origin/master 7b920d84a` (a detached worktree, since removed) is **clean**. It drops the two carried plan commits as "patch contents already upstream" and applies all five CARD-1677 commits. A rebase land therefore goes through.

## What changed

| Item | Fix |
|---|---|
| D-1 (server) | A per-task runner-answer cursor `runner-retention-sent:<id>` lives in `WorktreeResidueCandidateCursors`, with no migration. `LastOutcomeKey` is `send\|kind\|repeats\|fingerprint\|reason` and `NotBefore` holds the backoff end.<br>• A keep (Held or `retiring_refused`) backs the task off for `KeptRecheckMinutes` (60) × 2^(repeats−1), capped at `KeptRecheckMaxHours` (24).<br>• A reason that differs from the previous keep is re-sent on the next tick. Two changes in a row back off, so a flapping reason cannot take a slot every tick.<br>• A change in the task's server facts voids the backoff, and the task is re-sent at once. The facts are hashed: status, `CompletedAt`, path and branch.<br>• Order: never-sent tasks first, oldest first; then retries, least recently sent first.<br>• A DryRun `would_remove` or an unknown outcome never backs off; it only moves the task behind the never-sent ones. Successive dry ticks therefore census the whole population.<br>• A backed-off task gets a Held row, `kept_recheck_pending`, deduplicated by the existing D-8 rule.<br>• `TickResult` and the preview reply now carry `examined`, `eligible`, `backedOff` and `stopped`. |
| D-2 (runner) | `RunnerWorkspaceActivity` is in-memory. Under the mutation lock, the dispatcher stamps every admitted `WorkspaceMirror` (as `<root>/<name>`) and every admitted `Launch` (its cwd plus the session id).<br>Retention phase A, under the same lock, answers Kept in two cases:<br>• `workspace_in_use`: one of the runner's own non-Exited sessions (from `_runtime.List()`) was launched at or under the path.<br>• `recently_mirrored`: a mirror or launch stamp is younger than `PhoneHome:RetentionRecentUseMinutes` (default 10).<br>It draws on what the runner already tracks: the session registry (`List()`) plus the dispatcher's own record of Mirror and Launch. After a runner restart the `/proc` holder scan still keeps a tree in use. |
| D-3 | Three tests:<br>• (a) `Preview_is_dry_run_even_when_execute_is_on`<br>• (b) `Tree_not_registered_at_its_path_is_kept`, DryRun and Execute: a worktree moved with plain `mv`<br>• (c) `Residue_retry_keeps_a_residue_a_live_process_holds`, DryRun and Execute |
| A-2 | `MaxTickMinutes` (default 20): the tick starts no request past it and writes a Deferred row `tick_budget_exhausted`. |
| A-3 | `Enabled` and `Execute` are re-read before every request. If either goes false the tick stops (`tick_stopped_disabled` or `tick_stopped_execute_off`). |
| Flip | See "Exact defaults shipped" below. |

### Exact defaults shipped (operator, 2026-11-03: "Enable this on by default")

- **`server/appsettings.json` → `RunnerWorktreeRetention`:**
  - `Enabled: true`, `Execute: true`, `Runners: ["server2"]`
  - `Cron: "*/30 * * * *"`, `MinTerminalHours: 12`, `MaxRemovalsPerTick: 25`, `PauseBetweenSeconds: 5`, `RequestTimeoutSeconds: 300`
  - New keys: `MaxTickMinutes: 20`, `KeptRecheckMinutes: 60`, `KeptRecheckMaxHours: 24`
  - `WorktreeRoot: "/work/worktrees"`, `RetiringRoot: "/work/worktrees-retiring"`
  - `TempSweep` stays entirely off.
- **The C# type:** `Enabled = true` and `Execute = true`. **`Runners` stays `[]`**, because the binder appends to a list initializer. A witness binds the real JSON and asserts exactly `["server2"]`.
- **Runner:** `PhoneHome:RetentionRecentUseMinutes = 10`.
- **Kill switch and dry run:** `Enabled=false` is the kill switch; it also stops a running tick, and the job is removed at the next start. `Execute=false` gives a dry run, and the preview route is always DryRun. Both are documented in `docs/docker-stack.md`, `docs/ops-http.md` and plan D-15.
- **The S3 alarm** (`AttentionService.Alarms.cs` on master) still reads `RunnerWorktreeRetention:Enabled` and `:Execute`; the key names are unchanged.

## D-1 probe, before and after

`Kept_trees_do_not_starve_newer_removable_ones`: 30 dirty trees older than 5 removable ones, bound 25, two ticks 30 minutes apart.

- **Before** (red commit `afdbff73`, S1 behaviour):
  - Tick 1 sends kept1..25.
  - Tick 2 sends the same oldest 25 again, so the removable trees are never sent. Red with `second should not contain an element satisfying first.Contains(id) but does`.
- **After** (`b8f6af3df`):
  - Tick 1 sends kept1..25.
  - Tick 2 backs off those 25 and sends kept26..30 plus removable1..5. All five are removed.
  - The tick reports `examined=35 eligible=35 backedOff=25`.

## Checkpoints (closed list, one run per committed group)

### Red run `20261103-081641-d95e` at `afdbff73110a2745b92b32779c4b9e98ae73d010`

Verdict RED, as intended for CP-5.

```
CHECKPOINT CP-5 commit=afdbff73110a2745b92b32779c4b9e98ae73d010 build=ok filter=/*/*/(C1677RetentionServerTests*)|(C1677RetentionSettingsTests*)/* executed=66 passed=56 failed=10 skipped=0 trx=/work/worktrees/task-5fefd110/.antiphon/checkpoints/20261103-081641-d95e/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=afdbff73110a2745b92b32779c4b9e98ae73d010 sourceState=clean buildSource=verified
```

**The 10 red cases:**
- `Kept_trees_do_not_starve_newer_removable_ones`
- `Dry_run_census_covers_the_whole_population`
- `Kept_tree_backs_off_until_its_reason_changes`
- `Changed_task_facts_resend_a_backed_off_tree_at_once`
- `Tick_stops_starting_requests_past_its_budget`
- `Switching_off_stops_a_running_tick` (×2)
- `Shipped_defaults_are_on_for_server2_with_execute`
- `Validator_bounds` (×2: the new invalid cases)

`Preview_is_dry_run_even_when_execute_is_on` stays green at the red commit, because the check already exists there. It is a mutant witness (PC-125, red below).

**Runner rows in this run.** CP-1, CP-2, CP-3, CP-4 and CP-12 were `refused: tampered`, with no tests run. My earlier development builds had left `obj/` in `src/Antiphon.Agents.Pty` and `src/Antiphon.PtyHost*`, and the build rewrote those files. I deleted every ignored `bin/` and `obj/` by literal path before the green run.

**Runner red evidence instead.** I ran the same three classes in a `git archive afdbff73` scratch copy (one build lease, one run lease): `total 53 failed 2`. The failures are `Recent_use_by_this_runner_keeps_the_tree(mirror-then-retain)` and `(live-session)`, so the D-2 witness is red at S1 behaviour. Its `window-elapsed` control stayed green.

### Green run `20261103-082523-0aa3` at `b8f6af3df59af81996e3739f9bcdcf887bb056b7`

Verdict GREEN: wall time 18m23s, 5 builds, every row `slot=granted`, `sourceState=clean`, `buildSource=verified`.

| CP | executed | passed | failed | skipped | Min | waited |
|---|---:|---:|---:|---:|---:|---|
| CP-1 | 53 | 53 | 0 | 0 | 53 | 0s |
| CP-2 | 4 | 4 | 0 | 0 | 4 | 60s |
| CP-3 | 35 | 35 | 0 | 0 | 35 | 45s |
| CP-4 | 3 | 3 | 0 | 0 | 3 | 45s |
| CP-12 | 3 | 3 | 0 | 0 | 3 | 45s |
| CP-5 | 66 | 66 | 0 | 0 | 66 | 195s |
| CP-6 | 1 | 1 | 0 | 0 | 1 | 45s |

```
CHECKPOINT CP-1 commit=b8f6af3df59af81996e3739f9bcdcf887bb056b7 build=ok filter=/*/*/(C1677RetentionEngineTests*)|(C1677RetentionInventoryTests*)|(C1677RetentionDispatchTests*)/* executed=53 passed=53 failed=0 skipped=0 trx=/work/worktrees/task-5fefd110/.antiphon/checkpoints/20261103-082523-0aa3/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=b8f6af3df59af81996e3739f9bcdcf887bb056b7 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=b8f6af3df59af81996e3739f9bcdcf887bb056b7 build=ok filter=/*/*/(C1677RetentionServerTests*)|(C1677RetentionSettingsTests*)/* executed=66 passed=66 failed=0 skipped=0 trx=/work/worktrees/task-5fefd110/.antiphon/checkpoints/20261103-082523-0aa3/rows/CP-5/run.trx slot=granted waited=195s dirty=0 source=b8f6af3df59af81996e3739f9bcdcf887bb056b7 sourceState=clean buildSource=verified
CHECKPOINT SOURCE VALID source=b8f6af3df59af81996e3739f9bcdcf887bb056b7 rows=7
```

Evidence is gitignored, in `.antiphon/checkpoints/20261103-082523-0aa3/` (green) and `.antiphon/checkpoints/20261103-081641-d95e/` (red).

## Builds and runs outside the checkpoint list

Each one ran under `scripts/build-slot.ps1`, one lease per build or run, with `UseAppHost=false` and native output.

1. **Development checks before the red commit.** One build and one run each:
   - `tests/Antiphon.SessionRunner.Tests`: Engine and Dispatch classes, 50/50.
   - `tests/Antiphon.Tests`: Server and Settings classes, 66/66.
2. **One `tools/Antiphon.Checkpoints` build** (5 s), to get the tool dll.
3. **Mutant controls.** A `git archive b8f6af3df` scratch copy with env-gated mutants (`C1677_MUT`, inert when unset):
   - One build per project.
   - 31 runs: 28 mutants plus 3 controls. The controls were green: runner Engine 42/42 and Dispatch 8/8, server `C1677RetentionServerTests` 52/52.
4. **The D-2 red proof** at `afdbff73` (above): one build and one run.
5. **Affected integration classes.** The dispatcher's Mirror and Launch paths changed, so I ran these at `b8f6af3df`, with a fresh worktree build per project:
   - Runner, 118/118, 0 skipped. Every class was confirmed in the TRX: `PhoneHomeCommandDispatcherTests` 43, `C0824WorkspaceSlotCustodyTests` 16, `CodexProviderAuthRoutingTests` 1, `RunnerCapacityCountTests` 3, `PhoneHomeConnectionServiceTests` 17, `RunnerAbsenceEvidencePhoneHomeTests` 3, `RunnerWorkspaceBindingTests` 20, `UnixPtyArgvAdmissionTests` 15.
   - Server, 60/60. Classes: `PhoneHomeConnectionTests` 29, `PhoneHomeTaskDispatchProjectionTests` 18, `DelegateBranchDeliveryTests` 7, `PhoneHomeMirrorJoinTests` 6.

**Cleanup.** Every `bin/` and `obj/` I created was deleted, along with the scratch copies, by literal path. `git status --ignored` now shows only `.antiphon/`.

## Mutants (28, each one condition, each red on its named witness)

**Old safety checks, re-run after the change (18, every one a single-check removal):**

| Id | Check removed | Red case(s) |
|---|---|---|
| R1 | dirty | `Dirty_tree_is_kept` (tracked, untracked-nested-file, non-ignored-nested-repo) |
| R2 | unpushed | `Unpushed_commit_is_kept` ×2 |
| R3 | branch absent on origin | `Live_origin` (deleted-on-origin, decoy-tail-match) |
| R4 | merge-base contains HEAD | `Live_origin(origin-reset-behind)` |
| R15 | origin tip known locally | `Live_origin(origin-advanced-unknown-tip)` |
| R5 | live holder | `Holder_keeps_the_tree(holder)` |
| R6 | incomplete scan | `Holder_keeps_the_tree(scan-unknown)` |
| R8 | leaf == task id | `Confinement(leaf-id-mismatch)` |
| R9 | slot leaf | `Slot_paths_are_refused(ws-leaf)` |
| R10 | locked | `Ownership_gates_refuse(locked)` |
| R11 | HEAD is the request branch | `Repository_and_branch(other-branch)` |
| R12 | DryRun never moves | `Dry_run_reports_would_remove_and_mutates_nothing` |
| R13 | cross-device retiring root | `Cross_device_retiring_root_moves_nothing` |
| R14 | a failed move stops | `Move_failure_leaves_the_tree_in_place` |
| S1 | server: terminal status | `Each_ineligible` (queued, dispatched, working, blocked) |
| S5 | server: live session | `Each_ineligible` (session-created, -starting, -running, -stopping) |
| S10 | server: reread before the send | `Reopened_task_is_rechecked_before_its_turn` |
| S11 | server: per-tick bound | `Execute_requests_are_bounded_per_tick` |

**New (10):**

| Id (PC) | Check removed | Red case(s) |
|---|---|---|
| N5 (PC-121) | **backoff removed** | `Kept_trees_do_not_starve_newer_removable_ones`, `Kept_tree_backs_off_until_its_reason_changes` |
| N9 (PC-122) | never-sent first | `Dry_run_census_covers_the_whole_population` |
| N10 (PC-124) | facts fingerprint | `Changed_task_facts_resend_a_backed_off_tree_at_once` |
| N6 (PC-125) | preview forced DryRun (D-3 a) | `Preview_is_dry_run_even_when_execute_is_on` |
| N7 (PC-126) | tick budget | `Tick_stops_starting_requests_past_its_budget` |
| N8 (PC-127) | Enabled re-read | `Switching_off_stops_a_running_tick(enabled)` |
| N1 (PC-133) | registered at the path (D-3 b) | `Tree_not_registered_at_its_path_is_kept` (DryRun: verdict `would_remove`; Execute: reason `retiring_move_failed`) |
| N2 (PC-134) | residue-retry holder scan (D-3 c) | `Residue_retry_keeps_a_residue_a_live_process_holds` (DryRun, Execute) |
| N3 (PC-129) | recent-mirror stamp | `Recent_use(mirror-then-retain)` |
| N4 (PC-130) | live own session | `Recent_use(live-session)` |

Each failure was a Shouldly assertion, not an exception. No mutant survived.

**Not run here and pending for method-scoped SourceLanding Mutation:** PC-123 (a changed reason backs off), PC-128 (Execute re-read), PC-131 and PC-132 (no `RecordMirror` / `RecordLaunch`), PC-135 (C# `Runners` initializer), and the D-15-flipped PC-78..80. Every PC, PC-1..PC-135 including those I ran, stays pending for SourceLanding Mutation.

## Not done or deferred

- **Not run in full:** the whole Unit lane and the PhoneHome Rolling and Remote classes. The memory note puts them at more than 90 minutes on loaded server2, and the brief's closed list is CP-1..CP-6 and CP-12. The full Unit lane is deferred to Final/nightly and is not marked passed.
- **Advisories not addressed:**
  - A-1: other-uid holder count.
  - A-4: residue regex pin.
  - A-5: the dispatcher capability gate masked by the adapter.
  - A-6: `path_shared` and recently terminal tasks.
- **Note.** S3 on master treats an absent `RunnerWorktreeRetention:Enabled` as `Off`. The C# default is now `true`, so a host that dropped the appsettings section would run retention while the alarm said `Off`. The shipped appsettings sets both keys, so the two agree today.
