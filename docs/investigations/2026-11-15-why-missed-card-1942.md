# CARD-1942: why the TempSweep lock witness missed the changed contract

The repair is test-only. `Sweep_runs_outside_the_mutation_lock` now holds a real
non-slot `WorkspaceMirror` inside the dispatcher's `MutateAsync` semaphore while
the real adapter and scratch TempSweep engine finish a DryRun. The held mirror
creates a fresh workspace after release. Production behavior is unchanged.

## Why the old test stayed green

CARD-1892 commit `282999c2c96f2118b142dd2635a5dbbbc12de730` deliberately removed
Input and ConditionalInput from the workspace mutation semaphore. The old test
waited for `Surface.InputStarted`, completed a sweep, and asserted that Input was
still incomplete. That proves independence from a pending Input; it provides no
evidence that the workspace semaphore was owned.

The unchanged exact method passed 1/1 on current `origin/master`,
`08cc3dcac230658c9bb7f580d32ad91906665fbd`. Its TRX body duration was 0.358 seconds.
This is a vacuous lock witness, not an ordinary product failure. The CARD-1892
[sweep report](https://github.com/michal-ciechan/Antiphon/blob/5ae64d98535261d82a979814e6e522352441edd5/docs/investigations/2026-11-14-card-1892-stale-test-sweep.md) on
`origin/feat/card-task-15406ecc` also recorded the whole class passing 10/10 in
F007b. A green sweep cannot detect a stale ownership premise. The committed
derived index already selected this class for `PhoneHomeCommandDispatcher.cs`;
selection alone was not the missing protection here.

CARD-1926 repaired the sibling slot-custody witness in
`a4449fa6b0e7d20a6192985f48de7b86708548fd` using the existing
`RunnerWorkspaceService.WorkspaceAdmissionForTest` hook. This repair uses that
same owned operation and hook. When concurrency responsibilities move between
operations, the setup of both serialization and independence witnesses needs to
be checked against the actual lock owner, even when their assertions still pass.

## What changed

- Dispatch a valid non-slot mirror against a private local bare origin. Its
  admission hook signals entry and holds the actual dispatcher mutation owner.
- Require a real scratch TempSweep to report `WouldRemove` before releasing that
  owner. Assert the mirror remains pending and its new directory is absent.
  Release it, assert the Result frame and returned path, and require the newly
  created directory to exist.
- Preserve the old Input-independence assertions as the separate
  `Sweep_does_not_wait_for_input` test. Both tests release their gates and join
  every dispatched task in `finally`, including after an assertion fails.
- Retain the class's 20-second timeout and every original five-second wait.
  Keep dispatcher registration state under a test-owned scratch path.
- Regenerate `tests/derived-impact-index.json` with the explicitly built driver;
  the first run printed `changed`, the second `unchanged`. The generated delta
  has its own commit. No selector scripts or hand-maintained impact rules changed.

## Verification and provenance

Task: `b1318d57`. Host: native Linux runner mirror
`/work/worktrees/task-b1318d57`. Generated TRX, logs, mutation patch, source stamps
and per-row `/proc/loadavg` snapshots are gitignored under
`/work/worktrees/task-b1318d57/.antiphon/evidence/card1942/`.

Every build and run acquired its own host build-slot lease with maxcpucount 6.
Rows ran serially, using native default outputs and `UseAppHost=false` on both
builds and no-build runs. No row exceeded ten minutes. The brief's named class
and guard scope was used instead of an assembly-wide run. Broader mutation
checks were waived; the single lock-coupling fault below was used to demonstrate
that the repaired witness detects the actual boundary.

| Evidence file stem | Source | Passed / failed / skipped | Reported run duration | Slot wait |
|---|---|---|---|---|
| `baseline` | master above | 1 / 0 / 0 | 1.016 s | 30 s |
| `fixed-method` | initial repair below | 1 / 0 / 0 | 2.182 s | 75 s |
| `mutant-method` | initial repair + temporary fault | 0 / 1 / 0, intended | 7.311 s | 30 s |
| `class` | initial repair, production restored | 11 / 0 / 0 | 2.604 s | 0 s |
| `input-under-mutant` | final test + temporary fault | 1 / 0 / 0 | 2.194 s | 15 s |
| `final-mutant-method` | final test + temporary fault | 0 / 1 / 0, intended | 7.488 s | 30 s |
| `final-class` | final test, production restored | 11 / 0 / 0 | 3.727 s | 0 s |
| `siblings` | final test, production restored | 216 / 0 / 0 | 86.810 s | 0 s |
| `guard-union` | regenerated-index commit below | 114 / 0 / 1 | 226.213 s | 0 s |

Initial repair: `084fb1d926874486332de263f3e174142a73f7b1`.
Final test: `337319828fd8e16882dea997ebd0c197b7dfd687`.
Regenerated index and guard build/run: `7abcf2dedba763bcff9a250606bace7b56aae4d0`.
The subsequent note commit adds no executable changes.

The temporary fault replaced only the TempSweep dispatch arm with
`await MutateAsync(request, () => TempSweepAsync(request, ct))`. Each fault run
selected only `Sweep_runs_outside_the_mutation_lock`. Both failed at the sweep's
five-second wait with `TimeoutException: The operation has timed out.` The final
failure was at line 105, after mutation admission had already signaled; its body
took 6.410 seconds including setup and cleanup. This was not a build, admission,
zero-test or global-timeout failure. The separate Input test retained the old
logic and passed under the same fault (body 0.440 seconds), demonstrating why
that logic does not detect mutation-lock coupling.

After each fault, the production file was restored, checked with
`git diff --exit-code`, rebuilt, and the class rerun green. No production mutation
is committed. The final green mirror witness body took 1.841 seconds (one sample,
not a p95 or Windows measurement). The original deadlines were not adjusted.

The six runner builds took 35.23, 46.75, 36.93, 33.12, 34.41 and 47.61 seconds;
their slot waits were 30, 45, 0, 15, 0 and 0 seconds respectively. The index driver
build took 4.07 seconds after a 75-second slot wait. Regeneration held its two
leases for 38 and 33 seconds, with no queue wait. The guard-project build took
189.51 seconds with no queue wait. All builds had zero errors; compiler warnings
remain visible in the build logs.

Initial `/proc/loadavg`: `23.49 27.86 25.90 6/4391 24422`. Final class:
`27.53 23.69 22.82 6/4057 2700`; siblings:
`32.75 25.00 23.26 59/4169 5055`; guard union:
`16.51 23.88 24.52 11/3813 24491`. Per-test durations and membership are retained
in TRX. Detailed fixture, filesystem and Git phase attribution is unknown; it is
not reported as zero. Slot wait and build time are separate from test duration.

### Completed class coverage

| Runner class | Passed |
|---|---:|
| C1677TempSweepDispatchTests | 11 |
| C1677RetentionDispatchTests | 14 |
| C1677RetentionEngineTests | 81 |
| C1677RetentionInventoryTests | 3 |
| C1677RetentionNativeProcessTests | 14 |
| C1677TempFamilyRegistryTests | 3 |
| C1677TempSweepNativeTests | 7 |
| C1677TempSweepTests | 26 |
| C0824WorkspaceSlotCustodyTests | 19 |
| PhoneHomeCommandDispatcherTests | 45 |
| NativeLinuxPolicyWitnessTests | 3 |
| TestClassificationGuardTests | 1 |

| Required Antiphon.Tests guard class | Passed / failed / skipped |
|---|---|
| TestClassificationPolicyTests | 42 / 0 / 0 |
| TestClassificationGuardTests | 1 / 0 / 0 |
| SlowTestRegistryMergeTests | 5 / 0 / 0 |
| SlowTestTripwireTests | 2 / 0 / 0 |
| NativeLinuxPolicyWitnessTests | 3 / 0 / 0 |
| DerivedImpactIndexTests | 27 / 0 / 0 |
| MechanicalPcContractTests | 11 / 0 / 0 |
| TaskPlatformGuidanceTests | 5 / 0 / 0 |
| C1729LayeringGuardTests | 11 / 0 / 0 |
| TestLaneCategoryGuardTests | 1 / 0 / 0 |
| WindowsUnitPlatformAdmissionTests | 6 / 0 / 1 |

The one skip is `C1210_Windows_linux_only_outcomes_are_exact`: its child
skip-roster audit requires Windows placement. No Windows qualification is claimed.
C1729 printed `VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`.

The master pins were read and their current digests independently computed:
policyHash `bd6689bbc7ea752f13a69f872ea86a171fc8afc94e90ef9ed0c8d53aab00fbaa`;
PinnedInputDigest `070479b6da9902cc6cbd2b791baf855f01333599f6041cc017c45a8fe0cf9c75`.
Their inputs are unchanged, so no re-pin is needed. No live host, Windows run,
restart, deployment or Docker volume was touched. No product defect was found.

### Rerun selection

Build each project once through `scripts/build-slot.ps1`, using
`dotnet build tests/<Project> -p:UseAppHost=false --nologo`. Then invoke the
following no-build rows through separate leases (append unique TRX paths):

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1942-class -- dotnet run --project tests/Antiphon.SessionRunner.Tests -p:UseAppHost=false --no-build -- --treenode-filter '/*/*/C1677TempSweepDispatchTests/*'
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1942-siblings -- dotnet run --project tests/Antiphon.SessionRunner.Tests -p:UseAppHost=false --no-build -- --treenode-filter '/*/*/(C1677RetentionDispatchTests*)|(C1677RetentionEngineTests*)|(C1677RetentionInventoryTests*)|(C1677RetentionNativeProcessTests*)|(C1677TempSweepTests*)|(C1677TempSweepNativeTests*)|(C1677TempFamilyRegistryTests*)|(C0824WorkspaceSlotCustodyTests*)|(PhoneHomeCommandDispatcherTests*)|(TestClassificationGuardTests*)|(NativeLinuxPolicyWitnessTests*)/*'
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1942-guards -- dotnet run --project tests/Antiphon.Tests -p:UseAppHost=false --no-build -- --treenode-filter '/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)/*'
```

Build `scripts/regenerate-derived-impact-index/Antiphon.DerivedImpactIndex.csproj`
with `UseAppHost=false` before the guard union (its regeneration witness uses the
driver) and before refreshing the index; run its no-build driver
twice through separate build-slot leases and require the second to print
`unchanged`. Test-only standing authority allows this repair to proceed directly
to `next: merge` without an approval pause or ordinary Review.
