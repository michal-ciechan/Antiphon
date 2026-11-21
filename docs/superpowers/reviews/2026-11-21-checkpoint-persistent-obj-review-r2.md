# CARD-2050 Final Review, round 2

The repair removes the tracked intermediate redirect that caused round 1 B-1. It introduces a blocker in the generated props writer. Hold the land.

| Review coordinate | Value |
|---|---|
| Original Code landing owner | `644e673c-be19-4bcb-beeb-6554ce0277ae` |
| Repair source | `refs/heads/feat/card-task-8a722744` |
| Reviewed range | `03bdadefd9a59ef1c6cad16b70cb0297fe65011f..a74a6e865d8313d55cc207ba8f59da67d6252ba3` |
| Full candidate history for evidence diff | `b83ebfd38d4b6a1b1a5346c34603846977693bd4..a74a6e865d8313d55cc207ba8f59da67d6252ba3` |

## B-2. Cache open can overwrite a file outside its write scope

- **Where** — `CheckpointIntermediateCache.WriteProps` opens the generated props through `SafeWrite.OpenGate` and calls `SetLength(0)` and `Write` on mismatch. Commit `3f33cce8c3ccca4060818937f1f36a61f578b03e` introduced this path.
- **Failure** — A pre-existing hard-linked props entry can cause the tool to truncate and overwrite the shared file. The source guard has not established a build baseline at this point. An outside or ignored alias can escape the later repository checks. This follows from the production call chain. No destructive test was run.
- **Why** — `OpenGate` admits existing paths after containment, symlink/reparse and tracked-path checks. It opens the existing inode for read/write and records its identity without checking that it is a regular file with a single link. The caller's exclusive share mode does not confer write ownership. A stable pre-existing hard link suffices. An I/O error after truncation can leave the other file damaged even if cache open falls back cold.
- **Fix** — Publish canonical bytes from an exclusively created, tool-owned regular file using a confined replacement that leaves the previous inode untouched, or validate regular single-link custody on the opened handle before writing and fall back cold on refusal. Add a red-first production cache-open witness that checks the other file's bytes stay intact. [Cache writer](../../../tools/Antiphon.Checkpoints/Execution/CheckpointIntermediateCache.cs#L65) · [OpenGate](../../../src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs#L403)

## Import and verification boundary

`BuildStep.AppendSharedLayout` sets an absolute `CustomBeforeDirectoryBuildProps` global property on warm-eligible builds. The installed SDK imports it in `Microsoft.Common.props` ahead of the directory-wide props, and the checkpoint graph evaluation, build and no-build launch share the properties. The import selects a per-project directory for worktree-relative paths only. Projects outside the worktree can evaluate the import and use the ordinary isolated artifacts layout for intermediates. Windows-confined checkpoint-tool builds use the same import when eligible. The existing Windows no-`UseAppHost` branch stays intact. The PowerShell fallback and cold tool builds have their ordinary layout.

The props file is opened with `FileShare.None` while it is written and closed before the graph evaluates. There is no read-only attribute or digest check at import time. The worktree gate coordinates checkpoint executors, and the generated props is excluded from the source snapshot and the confinement baseline with other ignored `.antiphon/` content. A different same-account writer can change it between publication and SDK evaluation. The documented verifier runs repository MSBuild targets and test code with that account, detects tracked-source drift, and expressly places a same-account writer on the trusted side of the custody boundary. This race alone adds no execution privilege in that model. B-2 concerns the verifier's own write to a pre-existing file. [Checkpoint tool guarantee and custody model](../../testing-and-build.md#checkpoint-runner-tool)

Git references to `CheckpointIntermediates.props` are the generated path in the writer, its test expectation and documentation. The tracked props is absent. The repair diff changes neither the land-graph manifest, evaluation record and pins nor the derived impact index. The repair author reported two unchanged regenerations.

## Test and OS review

The red-first change `52869622ca36be9ddac4992bc2eeb5a6323cd7d9` compares the production build argument to the new literal repository-relative import path. Its parent implementation supplies the old tracked path, so the assertion can fail. The green test also plants different bytes, opens the real cache and checks the bytes on disk after opening and after bin cleanup. It covers the import argument and cache-opening behavior. The author's MSBuild run supplies the separate observation that the import really redirects the intermediates.

The five new `WindowsOnly` entries each represent one result and use the exact method and reason from the leading `!OperatingSystem.IsWindows()` guard. Their native operations cover NTFS overwrite timing, an out-of-scope DACL and clock changes, CopyFile change time, retry on sharing violations, and the Windows process launch with `UseAppHost` absent. The receipt validator reads the roster from the exact expected commit and uses per-identity limits. The C1210 frozen native Linux census excludes the separate `WindowsOnly` array. No pin change is required. Native Windows execution and PCs remain pending. [Custody tests](../../../tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs#L1278) · [Confined build test](../../../tests/Antiphon.Tests/Checkpoints/CheckpointOutputConfinementTests.cs#L79) · [Receipt validator](../../../scripts/validate-checkpoint-receipt.ps1#L39)

## Warm run cost

The author's required warm receipt `20261121-214513-83cf` reported zero of 48 CoreCompile cache timestamp changes, 67.04 s in the checkpoint build and 29.91 s in MSBuild. The compile result is acceptable. The receipt exceeded the original 60 s estimate in that run. The later qualifying same-ID class receipt `20261121-220239-7e46` took 54.43 s, also with zero changes to those 48 timestamps. The extra repeat started above the load limit and receives no acceptance credit. The non-MSBuild remainder in the required repeat was about 37 s. Its graph and verifier components were not separately measured. File a non-blocking performance follow-up to attribute that remainder before changing the estimate. The roughly 279 s cleanup is separately timed. This round did not commission a new paired benchmark. The new witness shares the build and sets up only a temporary Git fixture.

## Fresh checkpoint execution

The first two rows preserve the prior Review and repair class selection. CP-2 covers checkpoint build confinement, output custody, source binding, receipt validation and the named classification, mechanical contract, platform guidance and layering guards. Its previous measured test wall time was 242 s. The matching impact rows add the exact C1210 method and spawn-limiter rosters in the Pty and SessionRunner projects. CP-3 runs the plan's benchmark anchor once on the shared output. The paired timing measurement stays in the author evidence.

One checkpoint-tool run `20261121-223329-033f` used one isolated build per test entry project. The tool build ran separately through the slot gate to bootstrap the exact candidate. The PowerShell validator returned 0 against the full candidate SHA for all six rows. Independent TRX parsing matched every class filter and exact method and found 291 passes, zero failures and the five expected Windows skips. CP-2 matched all 22 classes, with 289 distinct outcomes. The inherited CP-2 Expect cell says zero skips. The five observed skips qualify under the source-bound Windows placement roster, and this run has no zero-skip native result.

The caller's load condition is an incomplete gate. The outer checkpoint start was at 10.94 and the tool's host snapshot was at 12.87. The 22:40:26 sample was 20.49, about seven seconds before the CP-1 lease. The load wrapper did not consistently intercept the executor's SDK and test launches. It recorded no test-host launches and could not apply the test-host `--timeout 570s` consistently. The checkpoint row override was `--row-timeout 570s`, recorded by the tool as a 10-minute bound. All rows were leased and source-validated. The results retain diagnostic value. The load-qualified Final/Full gate remains incomplete. No additional build or paired benchmark was run to replace these results.

The first test project build recorded 350.75 s, Pty 55.26 s and SessionRunner 56.15 s. CP-2 recorded 4m15s execution. Total wall time was about 22m35s, including 414.53 s of output cleanup. The tool removed all three isolated binary roots without cleanup refusals. Slot wait was 0 s on each row. Phase journals and raw per-case TRX files are under the run directory. Native Windows execution was outside this Linux rerun.

### Checkpoints

| Row | Selection | Passed | Failed | Skipped |
|---|---|---:|---:|---:|
| CP-1 | `CheckpointWarmBuildTests.intermediate_cache_is_shared_by_matching_builds_and_survives_bin_cleanup` | 1 | 0 | 0 |
| CP-2 | Exact 22-class batch in the receipt below | 284 | 0 | 5 |
| CP-3 | `DerivedImpactIndexTests.merge_process_names_the_regenerate_step` | 1 | 0 | 0 |
| CP-4 | `WindowsUnitPlatformAdmissionTests.C1210_Roster_closure_matches_source_gates` | 1 | 0 | 0 |
| CP-5 | `ProcessSpawnLimitTests` in `Antiphon.Agents.Pty.Tests` | 2 | 0 | 0 |
| CP-6 | `ProcessSpawnLimitTests` in `Antiphon.SessionRunner.Tests` | 2 | 0 | 0 |

```text
CHECKPOINT CP-1 commit=a74a6e865d8313d55cc207ba8f59da67d6252ba3 build=ok filter=/*/*/CheckpointWarmBuildTests/intermediate_cache_is_shared_by_matching_builds_and_survives_bin_cleanup executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/ws-04-d2/.antiphon/checkpoints/20261121-223329-033f/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=a74a6e865d8313d55cc207ba8f59da67d6252ba3 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=a74a6e865d8313d55cc207ba8f59da67d6252ba3 build=reused filter=/*/*/(CheckpointOutputConfinementTests*)|(CheckpointConfinementLedgerTests*)|(OutputCleanupTests*)|(CheckpointRepeatConfigurationTests*)|(RowRunnerTests*)|(RunSchedulerTests*)|(CheckpointAppTests*)|(ProcessDriverTests*)|(CheckpointBuildBindingPathTests*)|(CheckpointReceiptSafetyTests*)|(CheckpointTerminalReceiptTests*)|(HostCleanupCheckpointReceiptTests*)|(ProcessSpawnLimitTests*)|(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/* executed=284 passed=284 failed=0 skipped=5 trx=/work/worktrees/ws-04-d2/.antiphon/checkpoints/20261121-223329-033f/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=a74a6e865d8313d55cc207ba8f59da67d6252ba3 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=a74a6e865d8313d55cc207ba8f59da67d6252ba3 build=reused filter=/*/*/DerivedImpactIndexTests/merge_process_names_the_regenerate_step executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/ws-04-d2/.antiphon/checkpoints/20261121-223329-033f/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=a74a6e865d8313d55cc207ba8f59da67d6252ba3 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=a74a6e865d8313d55cc207ba8f59da67d6252ba3 build=reused filter=/*/*/WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/ws-04-d2/.antiphon/checkpoints/20261121-223329-033f/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=a74a6e865d8313d55cc207ba8f59da67d6252ba3 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=a74a6e865d8313d55cc207ba8f59da67d6252ba3 build=ok filter=/*/*/(ProcessSpawnLimitTests*)/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/ws-04-d2/.antiphon/checkpoints/20261121-223329-033f/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=a74a6e865d8313d55cc207ba8f59da67d6252ba3 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=a74a6e865d8313d55cc207ba8f59da67d6252ba3 build=ok filter=/*/*/(ProcessSpawnLimitTests*)/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/ws-04-d2/.antiphon/checkpoints/20261121-223329-033f/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=a74a6e865d8313d55cc207ba8f59da67d6252ba3 sourceState=clean buildSource=verified
```

## Other gates

| Gate | Result |
|---|---|
| Land-graph `-Check` | Exit 0 and approved at load 6.24. Digest `544627DF4CFD73157DA134EE730CA45C3DEC7A5DFAFFF977986D045295D00A33`, count 80. The tool was bootstrapped once in native output under its own lease. The check took a separate lease and used that built tool for the no-restore graph evaluation. No refresh is needed for this candidate. |
| Derived impact index | Recomputed in memory and matched the committed bytes. SHA-256 `6E9872EEC2329B3C0A73E80FD6897C4FAD08D70675F9A6E4DB219513051339C4`. Result lists 31 Windows-only classes and one Linux-only class. The file was left intact. |
| Impact and downgrade selectors | Matched `checkpoint-tool`, `native-linux-gate-roster`, `spawn-limit-rosters` and `test-lane-category`. Downgrade selector count 0, migrations=False. |
| Evidence diff | The documented compiled `antiphon evidence-diff` replacement checked the whole candidate history. 9 commits, 0 evidence entries, 0 violations, exit 0. |
| Merge-tree | Clean against master `0372c6f663cb2b3fe631f047198e626a94c9382e`. Merged tree `75be366ff28fd7fed8a0e5a7b54fb82863c76bfb`. A final fetch advanced master to `4785791e5c728b08e7ee23e8b8faf611af01934d`. That merge-tree also passed, with tree `abb388c7f24df5d0e97a501080f721054a589320`. |

Return B-2 to Code and repeat the exact checkpoint selection on that repaired SHA with a launch-level load gate and the requested test-host timeout.
