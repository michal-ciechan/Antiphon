# CARD-0824 S3 Windows slot lease: Review round 1 (Final)

- Review task: 7951bfb5. Subject: Code task de01ed8b, `origin/feat/card-task-de01ed8b`.
- Reviewed range: `d57edfc2c54007d293ec3096941ebbb91fc6c9da..3e1f426e196950114c54796a55b9cff4df60726a` (14 commits).
- Spec: `docs/superpowers/plans/2026-11-19-desktop-hot-worktree-slots-plan.md` S3 (D-2, D-3, D-4, D-7, D-8, D-9, D-10, D-12).
- Host: server2 runner mirror (Linux). Nothing ran on the desktop.

## Verdict

CLEAN. No blockers. The advisories below should go to cards. A-1 must be fixed before S4 lists `desktop-win`.

## Blocker checks

| # | Check | Result |
|---|---|---|
| 1 | Can the probe kill or close anything? | No. The diff has no `RmShutdown`, `TerminateProcess`, `Process.Kill`, `taskkill`, `Stop-Process` or recursive `Directory.Delete`. The only `Kill` is in the test `Rm_lock_defers_release`, which kills its own `powershell.exe` child. `RmStartSession` failure returns before a session exists. Every path after that runs `RmEndSession` in `finally`. `OpenProcess` handles are closed in `finally`, and `Process` objects are disposed. The session key buffer is `StringBuilder(64)`, which is at least the 33 characters `CCH_RM_SESSION_KEY+1` needs. `RmGetList` uses the two-call pattern. `ERROR_MORE_DATA` on the second call, a `needed` above 256, or more than 64 failed paths each return an incomplete scan (fail-closed). `RM_PROCESS_INFO` matches the Win32 layout (256/64 `ByValTStr`, `CharSet.Unicode`). The probe uses no unsafe memory. |
| 2 | Default off | A non-Windows runner, or a `null` runner, takes the unchanged `_roleGate.Evaluate` path. With default settings (`WindowsSlotRoles=none`, `WindowsSlotRunnerIds=[]`), a Windows runner gets `windows_roles_none` and the legacy mirror. `DelegationWorktreeService` creates the sentinel only when the slot flag is on, the runner is Windows, the runner advertises `workspaceSlotV1`, and `DecideListed` admits; with defaults it keeps the card-task checkout. On the runner, the Windows probe is chosen only when `OperatingSystem.IsWindows()`. An empty `RcFencePath` means no fence. Legacy `task-<id>` git still calls `RunnerWorkspaceService.GitAsync` directly. Two things change on server2, both allowed by the plan: slot git gains `-c core.longpaths=true` (a no-op on Linux), and the pool now reads `Proc.Scan`, which is incomplete only when `/proc` cannot list the runner itself. |
| 3 | Role and id admission | An empty list refuses (`windows_runner_not_listed`). The catalogue id `desktop` refuses even when listed. The global `*` never reaches a Windows runner. `Mutation` and SourceLanding are refused by `Admits`. Ids are read from `IOptionsMonitor.CurrentValue`. An invalid live value refuses with `windows_roles_invalid`, or skips the sentinel. Clearing the list does not touch a live lease: release is runner-side and keyed on the recorded slot id, so the lease releases normally at settlement, and new tasks go legacy. See A-1 for the window between checkout and preparation. |
| 4 | Qualification fence | `Held()` returns false only for an empty path or for `FileNotFound`/`DirectoryNotFound`. Any other exception returns true. A read error therefore keeps cold waiting, which is fail-safe. Every cold wipe is fenced: the lease (`coldRequested && !Held()`), the release, and `ColdResetAsync` (read once, before the mutation, and recorded as pending). `MarkScheduleAsync` only marks pending. Cold wipes touch only free slots or the slot being leased. A leased slot only gets a pending mark. |
| 5 | `core.longpaths` | `SlotGitAsync` puts `-c core.longpaths=true` before the subcommand. Every pool git call goes through it, including `RepositoryGitAsync` (worktree add/remove/lock/unlock/prune and fetch), checkout, clean, reset paths, update-ref and the loss diff. No `_service.GitAsync` call is left in the pool. Retention skips slot directories. |
| 6 | D-4 `RecordPathAsync` | `replaceCheckout` is computed from the row read `FOR UPDATE`. It is true only when `slotId != null` and `WorktreePath` starts with `windows-slot-pending:`. server2 never writes that sentinel, so server2 keeps its path. The false branch ran on Postgres: `RemoteWorkspaceReadinessTests` 19/19 (`CaptureMirrorBindingAsync` → `RecordPathAsync`) and `C0824WorkspaceSlotServerTests` 25/25. The true branch did not run on Postgres (A-7). `RemoteWorkspaceReadiness` otherwise keeps its earlier behaviour, which does not matter while the defaults are off. |
| 7 | Reseal and refresh | Recomputed from the tree. `policyHash` = `8c376b03c4e012e3700de15e8b99bdbe851187bce4d3c5defe2b6b6df1f782e2` (Get-NightlyPolicyHash) and `PinnedInputDigest` = `378bc01cf82a42140c72ebb4ca84f0302d7064b2de06d6dd6e22857eb5fc2c4c` (`-PrintInputDigest`); both match. `LegacyExclusionDigest` is held by NativeLinuxPolicyWitnessTests, green in both suites. `audit-land-verification-graph.ps1 -Check` exit 0, `approved` (digest 09EE5597…, count 80). |

## Runs at 3e1f426e1

| Run | Result |
|---|---|
| Checkpoint run `20261119-155240-9437` (CP-2, CP-3, CP-4) | GREEN. CP-2 executed 1, skipped 1 (`Rm_lock_defers_release`, gated), as planned. CP-3 executed 2. CP-4 executed 2. `validate` refuses the whole receipt as `row_failed` because of the planned skip. `--rows CP-3,CP-4` validates. |
| SessionRunner.Tests: all C0824* classes, NativeLinuxPolicyWitnessTests, TestClassificationGuardTests | 120 total, 119 passed, 1 skipped (the gated Rm method), 0 failed. Includes ProcessScan 3, NativeRehome 1, TimeoutBudget 20, RunnerBranchRefs 14, ColdReset 13 (with the fence test), Custody 19, Overflow 12, Rehome 18, Reset 12. |
| Antiphon.Tests: C0824Windows*, C0824WorkspaceSlot*, RemoteWorkspaceReadiness, CardWorktreeMirrorBinding, guard batch, DerivedImpactIndexTests | 184 total, 183 passed. One environmental red: `DerivedImpactIndexTests.regenerate_script_reports_changed_then_unchanged` needs the regenerator tool built, and a fresh mirror does not have it. After `dotnet build scripts/regenerate-derived-impact-index`, the class passed 27/27. |
| Antiphon.Tests: historical C0824RefOnly*, Branch*, Desktop*, Published*, Transport* | 371 total, 359 passed, 11 skipped, 1 failed. The failure is inherited: `C0824RefOnlyIdentityTests.Mode_round_trips_with_legacy_default` hits `NotSupportedException` from `AddLandVerificationFailure.Down`. That migration came from CARD-1995 (c6a1e23c7) and is already in base d57edfc2c. The subject touches neither migrations nor this test. |
| check-evidence-diff over base..tip | 14 commits, 0 violations |

## Merge with master

- Against the brief's master `edd710f39`, the only conflict is `tests/derived-impact-index.json`.
- Against current `origin/master` `59ca3ef94`, there is one more conflict: `scripts/test-live-lane-run.ps1` `PinnedInputDigest`. Master resealed it after the nightly-common/nightly-coverage changes (d86d0f394).
- The replay must:
  - regenerate `tests/derived-impact-index.json`;
  - recompute `PinnedInputDigest` on the merged tree with `-PrintInputDigest`, because the policy JSON (branch) and the nightly libs (master) are both protected inputs.
- Unchanged on the merged tree:
  - `policyHash` and `LegacyExclusionDigest`, because master did not touch the policy JSON or the witness;
  - the land-graph record, because master changed no csproj, props, targets, sln or global.json since base. Confirm it with `-Check` on the replay.

## Advisories (cards; they do not hold the land)

- A-1 (before S4): stranded sentinel.
  - Cause: the decision is made twice. `DelegationWorktreeService` decides at checkout. `ResolveSlotDecisionAsync` decides again at preparation.
  - Trigger: the allow-list or roles change between the two decisions. Clearing the list, the D-12 rollback, is one such change. A failed runner describe, or invalid live options at the second decision, has the same effect.
  - Effect: the task carries `windows-slot-pending:<id>` but gets a legacy mirror. `RecordPathAsync` replaces the sentinel only when `slotId != null`, so `WorktreePath` stays a non-path for the life of the task.
  - Fix: one of the following.
    - Make the preparation decision honour the recorded sentinel.
    - Write the legacy mirror path into `WorktreePath` when the task holds the sentinel.
    - Refuse and re-prepare.
- A-2: a runner whose catalogue `Platform` and capabilities platform are both null takes the server2 global role gate. A Windows runner with `workspaceSlotV1` and no reported platform would therefore inherit `*`. Phone-home reports the platform, so this is unlikely.
- A-3: the Windows-only test `Rm_lock_defers_release` starts its lock holder with `WorkingDirectory = lease.Path`. The cwd census then defers on its own, so the test does not isolate `RmGetList`. Start the child outside the slot.
- A-4: the `TryReadCwd` PEB offsets (0x20, 0x38) and the UNICODE_STRING layout are for x64 only. Guard with `IntPtr.Size == 8` or document that the runner is x64.
- A-5: a same-bitness process that exits between `OpenProcess` and `ReadProcessMemory` makes the census incomplete. That defers the release, and after `LingerRetries` it quarantines and re-homes a healthy slot on a busy desktop. Also, `FailedOpens` opens every file in the slot, including bin/obj and node_modules, on every release and every free check. Measure both at S4.
- A-6: the comment on `ProcWorkspaceSlotProcessProbe.Holders` still says "unchanged fail-open slot path". The pool now uses `Scan` on Linux too.
- A-7: the D-4 true branch (sentinel replaced by the slot path) did not run against Postgres. Add a Postgres witness.
- A-8: the fence treats `DirectoryNotFoundException` as clear. A lock on an unmounted or missing volume therefore fails open. The planned `C:` path is fine.
- A-9: plan row CP-2 expects a skip, which `validate` refuses as `row_failed`. Split CP-2 so that Linux runs only `Incomplete_scan_defers_release`, and Windows runs both methods.
- Inherited: `C0824RefOnlyIdentityTests.Mode_round_trips_with_legacy_default` has been red since the CARD-1995 migration added an irreversible Down. File a Debug card if one does not exist.
