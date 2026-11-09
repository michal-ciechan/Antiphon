# CARD-1792 trust-write advisories

CARD-1778 S2 review `a2845b6c` left two advisories. This slice fixes both.

## A-1 mkdir pin

`Server2SidecarStandingAgentsTests.Orchestrator_instructions_file_is_on_the_shared_work_volume`
asserts `InitMkdirTargets(init)` contains `/work/.antiphon` beside the existing chmod pin.
Dropping that directory from the `mkdir -p` list only must fail the test.

## A-2 atomic write

`ClaudeProjectTrust.WriteAtomic` publishes through a per-call temp name and copies the
original Unix mode onto that temp before `File.Move`. `Seed` and `Remove` hold
`ConfigLock` for the whole read-modify-write: an in-process gate plus
`<config>.antiphon-lock` opened with `FileShare.None`, retried for 1.25 seconds.
The server-side seed and the runner seed share that lock because both call
`ClaudeProjectTrust`. Windows keeps the replacement's default ACL (`CopyUnixMode`
returns before `SetUnixFileMode`).

## Witnesses

| Id | Proves | Mutant that must go red |
|---|---|---|
| W-mode | `Seed_preserves_the_config_file_mode` keeps `0770` | skip `CopyUnixMode` |
| W-race | `Seed_keeps_every_concurrent_project_key` keeps all eight keys | remove `ConfigLock.Acquire` from `SeedCore` |
| W-lock | `Seed_waits_for_the_shared_config_lock` blocks while `<config>.antiphon-lock` is held | stop opening that lock file |

## OS branch

The mode copy is the OS-selected branch (`OperatingSystem.IsWindows` in
`WriteTemp` and `CopyUnixMode`). `WriteTemp` opens the temp with
`FileStreamOptions.UnixCreateMode` set to the source mode before the JSON is
written; `CopyUnixMode` then restores any bits the umask stripped. Windows
returns before either Unix API. The Linux witness is W-mode, executed natively
by CP-1, and it uses `0770` so a umask of 022 or 027 cannot hide a dropped
`CopyUnixMode` after `UnixCreateMode` (an execute bit still means a 0666 create cannot match). The same method keeps its runtime Windows skip, carries
`[NativeLinuxOnly]`, and has a method-scoped `agents-pty` windows exclusion
(owner CARD-1792) so a Windows rc skip is a disposition rather than an
incomplete census. That test lives in `ClaudeProjectTrustModeTests.cs` because
the witness checker cannot lex C# raw strings, and the rest of the class uses
them. No startup path changes.
Runner default at authoring was `globalRunnerId=server2`; this slice pins neither
`-Runner` nor `-Platform`.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | fix | `tests/Antiphon.Agents.Pty.Tests -> bin-c1792-pty/` | trust-write | `/*/*/ClaudeProjectTrustTests/*` | W-mode, W-race, W-lock and the existing seed contract | 10 passed, 0 failed/skipped | 10 | 3 | true |
| CP-2 | fix | `tests/Antiphon.Tests -> bin-c1792-guards/` | guards | `/*/*/(Server2SidecarStandingAgentsTests*)\|(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)/*` | A-1 pin, the Final guard batch, and tests/test-execution-policy.json (CARD-1792 agents-pty windows exclusion) | 0 failed/skipped | 50 | 12 | true |

Code executes these filters once through `scripts/build-slot.ps1` on the worktree's native default output (`UseAppHost=false`), which is the task profile. The cold `bin-c1792-*` cells name the rows for the OS-witness checker; they are not a second build.
