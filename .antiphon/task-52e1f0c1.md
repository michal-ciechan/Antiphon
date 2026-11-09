# Review CARD-1792 round 1 (Final, ordinary): trust write mode, shared lock, mkdir pin

Subject: Code task d3a32713-975f-4472-ba3c-281504a3b123 (Grok author), `origin/feat/card-task-d3a32713`
tip `e114e51e4a45d8daa9176e794687e7238063db65`, base `364e271331fc1af83f5b7eabaa956a8c0a289a5f`.
Plan `docs/superpowers/plans/2026-11-09-card-1792-trust-write-advisories.md`.

Verdict: **one blocker (D-1)**, five advisories. Product logic is correct on Linux; the defect is a missing
rc policy disposition for the new Linux-only test.

## Evidence (fresh, this review)

- Checkpoint run `20261109-131005-5ccf` (tool `start --plan`, `--expected-source-sha e114e51e4a`):
  - `CHECKPOINT CP-1 commit=e114e51e4a45d8daa9176e794687e7238063db65 build=ok filter=/*/*/ClaudeProjectTrustTests/* executed=10 passed=10 failed=0 skipped=0 ... slot=granted waited=15s dirty=0 source=e114e51e4a45d8daa9176e794687e7238063db65 sourceState=clean buildSource=verified`
  - `CHECKPOINT CP-2 commit=e114e51e4a45d8daa9176e794687e7238063db65 build=ok filter=/*/*/(Server2SidecarStandingAgentsTests*)|(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)/* executed=82 passed=82 failed=0 skipped=0 ... slot=granted waited=0s dirty=0 source=e114e51e4a45d8daa9176e794687e7238063db65 sourceState=clean buildSource=verified`
  - `unlisted: none`; `verdict: RED exit=7 reason=owner-unverified`: the executor's owner read got
    `http 502` / `timeout after 12s` from the server (13:23:48-13:24:41Z), so output cleanup was refused
    (`refused/kept bin-c1792-pty/, bin-c1792-guards/`). `validate` prints
    `CHECKPOINT SOURCE INVALID reason=cleanup_refused`. Both rows ran green on a clean verified build, but the
    receipt does not validate, so `reviewedSourceClean: false`. Kept outputs removed by exact path afterwards.
- Red commit `8a585f110448ab84c0be55d1bc703ced1c4177cc` (scratch detached worktree, build-slot leased build and
  run): 3 failed, 7 passed. Failures at the intended assertions: mode `ShouldBe(expected)` (line 178), race
  `IsTrusted` (line 208), lock `finishedEarly.ShouldBeFalse()` (line 254). Code's "3 failed, 0 passed" was a
  three-method selection; same three reds.
- Mutants re-run (env-gated, one build, leased build + one lease per run; control 10/10 green):
  - PC-mode, drop `CopyUnixMode`: red at `ClaudeProjectTrustModeTests.cs:24`, expected
    `GroupRead, UserWrite, UserRead`, was `OtherRead, GroupRead, UserWrite, UserRead`.
  - PC-lock, `Acquire` returns without opening `<config>.antiphon-lock`: red at
    `ClaudeProjectTrustTests.cs:224` (`finishedEarly.ShouldBeFalse()`).
  - PC-race and PC-mkdir not re-run (PC-mkdir needs a second ~9 min `Antiphon.Tests` build). All four PCs
    stay pending for SourceLanding Mutation.
- No assertion loosened between the red commit and the tip (the mode assertions only moved into
  `ClaudeProjectTrustModeTests.cs`). `tests/test-execution-policy.json` unchanged (see D-1: it should change).
- `scripts/check-os-witnesses.ps1` with Code's declaration: `OS-WITNESS complete syntax/inventory` (exit 0).
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 364e2713 -HeadRef e114e51e`: commits=5, violations=0.
- `git merge-tree --write-tree origin/master(25b347f4) e114e51e`: `a3224b2626e8203250d6ccdf0373aec6bce82443`, exit 0.

## D-1 (blocker): the Linux-only mode witness has no rc Windows disposition

- **Where:** `tests/Antiphon.Agents.Pty.Tests/ClaudeProjectTrustModeTests.cs:10-14`
  (`Seed_preserves_the_config_file_mode` throws `SkipTestException` when `OperatingSystem.IsWindows()`), with no
  `[NativeLinuxOnly(...)]` marker and no `tests/test-execution-policy.json` row.
- **Failure:** on the Windows rc qualification the `agents-pty` suite launches the case and it reports
  `Skipped`. `ConvertTo-NightlyCoverageVerdict` (`scripts/lib/nightly-coverage.ps1`, skip branch after
  `process-exit`) sets `coverageComplete = $false` for any skipped row, so the Windows Full qualification is
  incomplete from this change alone (the `c1415-launched-skips-stay-incomplete` control in
  `scripts/test-rc-platform.ps1` pins that behaviour).
- **Why:** `docs/release-gates.md` ("A runtime skip alone leaves required coverage incomplete";
  "Rows whose reason starts with `Linux-only by construction:` are Windows-only exclusions for tests whose skip
  is a POSIX mechanism (... Unix modes ...)") requires the marker plus a Windows-only row. Precedent:
  `UnixPtyArgvTests` (class marker + `agents-pty` row), `LinuxCgroupContainmentTests.cs:207` (method marker).
  The brief's "policy unchanged" check guarded against weakening; this is an addition the owner doc requires.
- **Fix:** add `[NativeLinuxOnly("Unix file mode preservation needs Unix file modes")]` on the method (its own
  line, canonical form for the CARD-1686 census) and a method-scoped row
  `{ "suite": "agents-pty", "class": "ClaudeProjectTrustTests", "methods": ["Seed_preserves_the_config_file_mode"], "platform": "windows", "reason": "Linux-only by construction: Unix file modes exist only on Unix. The case remains required on Linux.", "owner": "CARD-1792" }`;
  reseal `policyHash` from the `NativeLinuxPolicyWitnessTests` mismatch line; re-run CP-1, CP-2 and
  `pwsh -NoProfile -File scripts/test-rc-platform.ps1 -Case LinuxOnlyWindowsExclusionPolicy`; add the policy
  file to the plan's CP-2 coverage. Keep the runtime skip.
- **Landing owner:** Code task d3a32713-975f-4472-ba3c-281504a3b123.

## Checks requested by the brief

1. **Mode.** `File.GetUnixFileMode`/`File.SetUnixFileMode` (stat/chmod via the BCL, no shell-out). Temp is a
   per-call `<path>.antiphon-tmp-<guid>` in the same directory; `File.Move(overwrite: true)` is `rename(2)`,
   atomic; the `finally` deletes the temp on every failed attempt. Windows: `CopyUnixMode` returns before any
   Unix API, and `PlatformNotSupportedException` is caught, so no exception. Nothing from the config is
   logged (the runner logs only the outcome and session id). Seed never creates the config (NoConfigFile), so
   there is no new-file default to choose; the lock file is created with the umask default and stays empty.
   The temp is still world-readable (umask default) between `WriteAllText` and `CopyUnixMode`: A-1.
2. **Lock.** In-process `Monitor` per exact path string plus `<config>.antiphon-lock` opened with
   `FileShare.None`, which on Linux .NET is `flock(LOCK_EX|LOCK_NB)`: cross-process among .NET openers,
   released by the kernel on process death (no stale lock after a crash; the leftover file is harmless).
   Monitor is re-entrant, but Seed/Remove never nest. `using var` releases on every path, and `Acquire`
   releases the Monitor on exceptions. Bound: 50 x 25 ms (about 1.25 s) for the file lock; the in-process
   Monitor wait is unbounded. Contention fails closed: Seed returns `Failed`, Remove returns false (but see
   A-2 for how). The read-modify-write is inside the lock, so the lost update between Antiphon writers is
   closed. Not protected: the Claude CLI's own rewrites of `.claude.json`, `scripts/orchestrator-workspace.ps1`
   (desktop operator) and `docker/session-runner-grok/dind-entrypoint.sh`; a Claude write can still be lost
   (rare; ClaudeStartupReadiness still answers the dialog). `DOTNET_SYSTEM_IO_DISABLEFILELOCKING=1` would turn
   the file lock into a no-op (the in-process gate still holds). The call is synchronous in the launch path
   (`SessionRunnerRuntime.cs:712`), so contention can block a thread for about 1.25 s.
3. **V-11 pin.** `InitMkdirTargets` slices from `for d in ` to the first `;`, which is exactly the `mkdir -p`
   list in `docker-compose.server2-server.yml:26`. Dropping `/work/.antiphon` from that list only fails
   (Code's PC-mkdir red at line 69); edits elsewhere in the init command do not. Not re-run here.
4. **Windows.** Owed to qualification. A Windows run exercises: `CopyUnixMode` early return (the replacement
   keeps the default ACL from `MoveFileEx`), `FileShare.None` as a mandatory share mode (sharing violation is an
   `IOException`, retried), `File.Move` overwrite while the Claude CLI may hold the file open (retried once),
   W-race and W-lock executed, W-mode skipped (D-1).

## Advisories (do not hold the land; file as cards)

- **A-1 temp mode window (pre-existing):** the temp is created with the umask default (0644) and receives the
  original mode only after the full JSON is written. Create it with `FileStreamOptions.UnixCreateMode` set to
  the source mode (or chmod the empty file before writing).
- **A-2 unreachable null return:** in `ConfigLock.Acquire` the last attempt's `IOException` fails the
  `when (attempt < Attempts - 1)` filter, goes through the outer `catch` and is rethrown, so
  `Monitor.Exit; return null;` is dead code and the `"config lock failed"` outcome is never produced. It still
  fails closed (Seed `Failed` with the IOException text, Remove false). `DirectoryNotFoundException` and
  `FileNotFoundException` are also retried 50 times.
- **A-3 weak mode witness:** 0640 can be produced by a umask of 027, and on such a host the drop-`CopyUnixMode`
  mutant survives. Use a mode with an execute bit (for example 0750), which no umask on 0666 produces.
- **A-4 plan claim:** "The server-side seed and the runner seed share that lock" is wrong. Only the session
  runner (`ClaudeLaunchTrustSeed.Apply`) calls `Seed` in `src/`; the lock serializes runner threads and
  processes.
- **A-5 orphan temps:** with GUID temp names, a crash between write and rename leaves a full config copy
  (`.claude.json.antiphon-tmp-<guid>`) that nothing sweeps. Before this change the fixed name was reused.

Cleanup: removed `/work/worktrees/task-52e1f0c1/tests/Antiphon.Tests/bin-c1792-guards` and
`/work/worktrees/task-52e1f0c1/tests/Antiphon.Agents.Pty.Tests/bin-c1792-pty` (kept by the refused cleanup)
and the scratch mutant worktree (`git worktree remove`). No product code changed.
