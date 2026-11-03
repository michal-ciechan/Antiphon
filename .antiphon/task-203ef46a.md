# Review r2 (task 203ef46a): CARD-1677 S1 retention guards, backoff, on-by-default

**Verdict: CLEAN at `dd5837a40ecb438563dfb6d161909e02d433998e`.** No numbered defects; five advisories.
Diff reviewed: `d08bf72b657d9f9fcff61c63f241b0cb52e418a0..dd5837a40ecb438563dfb6d161909e02d433998e`
(subject branch `feat/card-task-5fefd110`; landing owner the original Code task `1296ccbd-436c-44bf-9367-e7db90e789f8`).

## Evidence

- Checkpoint run `20261103-093351-77c6`, `--rows CP-1,CP-3,CP-5` (CP-1 added: CP-3 reuses its build and the
  D-2 witnesses live there). GREEN, wall 9m31s, `unlisted: none`; `validate` → `CHECKPOINT SOURCE VALID source=dd5837a40ecb438563dfb6d161909e02d433998e rows=3`.

```
CHECKPOINT CP-1 commit=dd5837a40ecb438563dfb6d161909e02d433998e build=ok filter=/*/*/(C1677RetentionEngineTests*)|(C1677RetentionInventoryTests*)|(C1677RetentionDispatchTests*)/* executed=53 passed=53 failed=0 skipped=0 trx=/work/worktrees/task-203ef46a/.antiphon/checkpoints/20261103-093351-77c6/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=dd5837a40ecb438563dfb6d161909e02d433998e sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=dd5837a40ecb438563dfb6d161909e02d433998e build=reused filter=/*/*/RunnerWorkspaceServiceTests/* executed=35 passed=35 failed=0 skipped=0 trx=/work/worktrees/task-203ef46a/.antiphon/checkpoints/20261103-093351-77c6/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=dd5837a40ecb438563dfb6d161909e02d433998e sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=dd5837a40ecb438563dfb6d161909e02d433998e build=ok filter=/*/*/(C1677RetentionServerTests*)|(C1677RetentionSettingsTests*)/* executed=66 passed=66 failed=0 skipped=0 trx=/work/worktrees/task-203ef46a/.antiphon/checkpoints/20261103-093351-77c6/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=dd5837a40ecb438563dfb6d161909e02d433998e sourceState=clean buildSource=verified
```

- **Mutants** (detached scratch worktree at dd5837a40, now removed; each build and each run had its own build-slot lease, `UseAppHost=false`):
  - **M-A, the recent-use producer (PC-131, which the Code task did not run).** The `_workspaceActivity.RecordMirror(...)` call in `PhoneHomeCommandDispatcher` WorkspaceMirror was removed.
    - Result: RED. `C1677RetentionDispatchTests` failed 1, passed 7.
    - The failing case was `Recent_use_by_this_runner_keeps_the_tree(mirror-then-retain)`: `response.Verdict should be "kept" but was "removed"`.
    - Without the stamp, a tree reused by a fresh mirror is deleted.
  - **M-B, the config flip.** `server/appsettings.json` was changed to `"Runners": ["server2", "desktop"]`, with no rebuild. The test reads the source JSON.
    - Result: RED. `C1677RetentionSettingsTests` failed 1, passed 13.
    - The failing case was `Shipped_defaults_are_on_for_server2_with_execute`: `should be ["server2"] but was ["server2", "desktop"]`.
  - Both mutations were restored before the worktree was removed.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef d08bf72b6.. -HeadRef dd5837a40`: `commits=3 entries=1 violations=0`.
  The whole-branch range from base `7b920d84a` gives `commits=8 entries=2 violations=0`.
- **Landability:**
  - `git merge-tree --write-tree origin/master(a2584ef6e) HEAD` shows add/add conflicts on the two plan docs only. Master landed them through its own commits.
  - A scratch `git rebase --onto a2584ef6e 7b920d84a` (detached, now removed) was **clean**. It dropped the 2 plan commits as "already upstream" and applied all 6 CARD-1677 commits. A rebase land goes through.
  - `merge-tree origin/rc/2026-11-03 (919d8d42f) HEAD` was clean, tree `b161bcc29`.
- **Builds and runs outside the checkpoint tool, each with a reason:**
  - 1 SessionRunner.Tests build and 1 run (M-A).
  - 1 Antiphon.Tests build and 1 run (M-B).
  - The `dotnet run --project tools/Antiphon.Checkpoints` tool build, which the documented entry point performs itself.
- **Not run:** the whole Unit lane and the full affected integration classes. The brief narrowed this round to the CP rows plus two mutants, with a 50-minute budget. The Code task also deferred them. `ordinaryScopeCompleted` is therefore Interim.
- **PCs:** all of PC-1..PC-135 stay pending for SourceLanding Mutation.

## Verify answers

1. **D-2 recent-use guard.**
   - **Paths that make a tree reachable by a new task:**
     - `WorkspaceMirror` stamps `<WorktreeRoot>/<Name>` under the mutation lock, before `MirrorAsync`. Slot mirrors are skipped, and those are refused `slot_managed` anyway.
     - `Launch` and `LaunchPlatformConstrained` both go through `LaunchAsync`. Both stamp the cwd plus the session id under the same lock, before `StartAsync`.
     - The retain guard runs in phase A under that lock.
     - A -FromTask or -OnAgent re-dispatch reaches the old tree only through Mirror or Launch, so it is stamped. On the server side, `path_shared` also catches an open task whose path is the same.
     - A warm-pool reuse is a live session, so it reads `workspace_in_use`, or the holder scan sees it.
   - **Not stamped:**
     - `VerificationWorkspaceCreate`: the server excludes Mutation and SourceLanding tasks, and create refuses an existing directory.
     - Sessions adopted after a runner restart: covered by the `/proc` holder scan and by the server's `live_session` check.
   - **Path spelling matches:** `IsUnderRoot` requires the exact `_worktreeRoot + "/"` prefix, the same string the stamp uses.
   - **Time source: wall clock** (`TimeProvider.GetUtcNow`). See A-1.
   - **Restart window:** the stamps are in memory only. After a restart, a tree mirrored before it has no stamp.
     - To matter, a retain would need its server reread to happen before the task was reopened, and its send to land after the runner reconnects.
     - An in-flight request dies with its connection. The next tick re-reads the row, which then reads `live_owner` or `path_shared`.
     - So the window is one request's reread-to-send gap (milliseconds) straddling a reconnect. In practice it is nil, and the holder scan plus the dirty and unpushed checks still apply.
   - **Live sessions:** this is any of the runner's own sessions with `Status != "Exited"`, so idle and Blocked sessions whose process is up are covered.
     - A parked or Blocked task with no process is non-terminal, so the server never sends it (`live_owner`).
     - The server also checks `AgentSessions` for live statuses.
2. **D-1 backoff cursor.**
   - The cursor only reorders or skips tasks. `ClassifyAsync` re-runs on a fresh row read right before every send. The request carries no cursor-derived field, and the runner rechecks everything. A stale cursor can only delay a send: at most 24 h, or until the facts fingerprint changes.
   - **No schema change:**
     - Keys are `runner-retention-sent:<32hex>` (54 characters, against a limit of 80).
     - `LastOutcomeKey` is at most about 118 characters, against a limit of 200.
     - Old `runner-retention:<id>` rows are distinct keys. Garbage parses to null, which is treated as never-sent.
     - Nothing prunes or prefix-scans the cursor table.
3. **D-3, tick budget, kill switch.**
   - `Execute=false` never deletes:
     - The preview route forces DryRun (`execute = settings.Execute && previewRunner is null`), and PC-125 is red.
     - `StopReason` stops before the next send.
     - The runner's DryRun never moves anything (R12).
   - **Window after a flip:** one request already in flight. That is a DB reread (milliseconds) plus one retain, up to `RequestTimeoutSeconds`=300 s. The runner's phase B can also finish after the server cancels.
   - **Tick bound:** about 20 min + one 300 s request + 5 s pause, which is under the 30-minute cron. The job never overlaps itself.
4. **The flip.**
   - `server/appsettings.json` ships exactly as stated, and the C# `Runners` default is `[]`. The test binds the real JSON and M-B is red on it.
   - **The desktop cannot be matched:**
     - Runner ids match by ordinal comparison and are not trimmed, so " server2" or "Server2" matches nothing.
     - `Resolve` maps null, `local` and the desktop aliases to the local client. That client is not a `PhoneHomeRunnerClient`, so it reads `runner_not_eligible`.
     - The engine and `workspaceRetentionV1` exist only on a Linux runner.
   - S3 alarm: see A-4.

## Advisories

- **A-1.** `RunnerWorkspaceActivity` uses the wall clock.
  - A backward step keeps stamps longer, which is safe.
  - A forward step of 10 minutes or more expires them early.
  - Suggest `TimeProvider.GetTimestamp`/`GetElapsedTime`.
- **A-2.** Candidates skipped at the reread (`path_changed` or `live_owner`) still consume a `Take(MaxRemovalsPerTick)` slot. This is minor and only slows retention.
- **A-3.** Test hosts that boot the real Program load `server/appsettings.json` (Enabled, Execute, Runners=[server2]). There is no Testing override.
  - Such a host contacts nothing only through `runner_not_eligible`: no Linux phone-home runner named server2 is connected.
  - A test-environment `Enabled=false` would make that explicit.
- **A-4.** The S3 alarm, judged.
  - A missing `Enabled` reads `Off` while the code default is On. That case is effectively right: with no section, `Runners=[]`, so nothing is contacted.
  - The reverse mislabel is real: it ignores `Runners`, so Enabled+Execute with `Runners=[]` reads `Execute` while nothing runs.
  - It is a label only, not safety. Suggest reading the bound options, including `Runners`.
- **A-5.** Round-1 A-1, A-4, A-5 and A-6 remain unaddressed, as the Code report states.
