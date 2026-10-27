# Native Windows rerun: scripts, checkpoint and release gate

Task `fb80be16`, 2026-10-27. Evidence-only investigation; no product or test source changes. This report qualifies the tested lane, not the exact rc tree.

## Outcome

Initial native selections completed: 34/34 classes; 655 passed, 51 failed, 251 skipped (957 total). Controlled/repeat runs are separate, not added to those counts.

Of the 17 historical failures whose stored message contained `read-stalled`, 15 now pass on the first native run. The state-store test now reaches a different failure (Windows replacement contention). The staged recovery test still stalled initially; its repeat outcomes appear below. The OVERLAPPED reader change is active, but complete elimination of reader stalls is not established.

## Provenance and execution limits

- Assigned branch: `feat/card-task-fb80be16`; initial source `718bd29f4a1504c64c491d607d31403fcb87dae2`. The primary build and test runs verify `281431340b1c78aca392f703701f4f853703e70f` (only the preliminary investigation Markdown differs).
- Fetched `origin/rc/2026-10-27` = `d1d090f766451797a3bd9b30c9d2966f2f8c034c`. The assigned branch was created off master, not that rc tip. The assigned-branch contract prohibits switching/rebasing/merging the target. Complete tree comparison (`source-differences.txt`) differs only in Markdown plus `src/Antiphon.Agents.Pty/ClaudeScreen.cs` and `tests/Antiphon.Agents.Pty.Tests/ClaudeDialogObservationTests.cs`; requested tests, checkpoint implementation, scripts and build configuration match rc. This is not exact-rc qualification.
- Original read-only evidence: `C:\Antiphon\releases\candidates\rc-2026-10-25-105792f7d22f\logs\2026-10-26-0946-8f055f87`. The old checkout was not touched. Parsed original TRX rows are retained in `prior-counts.json` and `prior-failures.json`.
- Final whole-run digest fetched from `origin/feat/card-task-7e719c57`, SHA `c7ad9e901c6be169fb5a0f43be8e210211e9081b`; copy `prior-digest.md`.
- Fresh evidence root (gitignored): `C:\Antiphon\worktrees\card-task-fb80be16\.antiphon\native-fb80be16`. Every selection has `<Class>-<attempt>/console.log`, `results.trx`, and exact failed-test message, stack and stdout/stderr in `failures.log`. `runs.jsonl` binds source, filter, counts and elapsed seconds.
- Windows 10.0.19045, SDK 10.0.300, test runtime .NET 9.0.22. Before the build: 8,935 MiB available memory, no matching Antiphon.Tests/testhost/vstest leftovers, native qualification lock absent.
- One successful primary native build: `build-2.log`, 6m47.61s, 1,196 warnings, zero errors. The preceding command was rejected before compilation (`build.log`, MSB1041) because PowerShell split the unquoted `-nodeReuse:false`; the corrected invocation quoted it.
- Primary `Antiphon.Tests.dll`: 69,058,048 bytes, final SHA256 `BF514AB5BCA9D1AE02EBC1A3801DBFC857FE5FCCBB3FF19C095B5205A4F8F210`, matching all three earlier diagnostic copies. The separate HostCleanup reader assembly has SHA256 `66AD433D0799CDD897CD86BE637F1E44CD9BA464D78FE9DB21164556B562F54B`. Requested direct no-build execution avoids the checkpoint wrapper's >64 MiB rejection but supplies no checkpoint build stamp.
- Every outer build/test and native diagnostic driver used the shared build-slot gate. A repeat fixture prints `slot=skipped` / `-NoSlot` only for its nested checkpoint child while its outer class already holds a slot; no unleased outer retry was used. Independent class invocations share the host gate; no Pty test assembly ran here.
- Minimal scratch C# diagnostic executables were compiled under the gate, separately from the single primary product build. One scratch compiler response-file syntax attempt failed, then the corrected diagnostic succeeded. These are not extra product builds.
- Caller authority already covered reruns and investigation; no approval pause. No live AppHost or runner restart, release publication, tag, or external message was performed. All deletion exercised by this task was confined to its own fixture roots and outputs.

## Reproduction commands

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label fb80be16-native-build -- dotnet build tests/Antiphon.Tests '--property:OutputPath=bin-fb80be16/' --nologo '-nodeReuse:false'
pwsh -NoProfile -File scripts/build-slot.ps1 -Label fb80be16-<Class>-initial -- dotnet run --project tests/Antiphon.Tests --no-build '--property:OutputPath=bin-fb80be16/' -- --treenode-filter '/*/*/<Class>/*' --report-trx --report-trx-filename results.trx --results-directory <fresh-evidence-directory>
```

Installed-PowerShell controls prepend `C:\Program Files\PowerShell\7;` to the child PATH only. The serialized OutputCleanup control adds `--maximum-parallel-tests 1`. Counts below include skipped outcomes; a passing class with skips does not exercise those skipped platforms.

## Class comparison

P/F/S = passed/failed/skipped. “Fixed by rc/2026-10-27” is the requested comparison label: the old failure no longer reproduces against the matching lane source. It does not prove attribution to one commit or cover the whole rc tree. Fix design is deferred by the Investigate-stage contract.

| Class | Original P/F/S | Native initial P/F/S | Classification / cause |
|---|---:|---:|---|
| CheckpointRecoveryWindowsTests | 9/6/0 | 9/6/0 | Fixture: immutable ledger resealed (4), missing receipt (1); intermittent reader stall (1). |
| CheckpointConfinementLedgerTests | 16/3/1 | 20/0/1 | Fixed by rc/2026-10-27. |
| OutputCleanupTests | 35/3/11 | 35/3/11 | Unresolved Git timeouts/custody failures; repeat and serial controls below. |
| CheckpointBuildBindingPathTests | 0/3/0 | 3/0/0 | Fixed by rc/2026-10-27. |
| BaselineComparerTests | 6/2/0 | 8/0/0 | Fixed by rc/2026-10-27. |
| CheckpointTempUsageTests | 6/2/0 | 9/0/0 | Fixed by rc/2026-10-27. |
| CheckpointAdmissionContractTests | 2/1/0 | 3/0/0 | Fixed by rc/2026-10-27. |
| CheckpointAppTests | 2/1/0 | 3/0/0 | Fixed by rc/2026-10-27. |
| C1307CliCancellationTests | 0/5/0 | 5/0/0 | Fixed by rc/2026-10-27. |
| RerunPolicyTests | 2/4/0 | 6/0/0 | Fixed by rc/2026-10-27. |
| EvidenceFolderTests | 4/2/0 | 6/0/0 | Fixed by rc/2026-10-27. |
| EvidenceDiffGuardTests | 18/1/0 | 19/0/0 | Fixed by rc/2026-10-27. |
| PlanCoverageBindingTests | 4/2/0 | 6/0/0 | Fixed by rc/2026-10-27. |
| CheckpointRepeatBuildIsolationTests | 0/1/0 | 0/1/0 | Unresolved output-inventory-failed; zero child tests, underlying exception masked. |
| CheckpointToolSourceTests | 1/1/0 | 2/0/0 | Fixed by rc/2026-10-27. |
| RunStateStoreTests | 0/1/0 | 0/1/0 | Product: concurrent Windows replacement fails despite share-delete and retries. |
| ScriptHarnessWindowsOwnershipTests | 9/1/0 | 10/0/0 | Fixed by rc/2026-10-27. |
| C1008PlatformContractTests | 4/2/0 | 4/2/0 | Verification setup: required checkpoint source stamp absent (2). |
| ReleaseGateRunTests | 7/2/0 | 0/9/0 | Fixture resolver: WindowsApps alias; installed control exposes product wrong-origin (2). |
| RunCheckpointRepeatScriptTests | 2/2/0 | 4/0/0 | Fixed by rc/2026-10-27. |
| CardRelationshipScriptTests | 6/1/0 | 6/1/0 | Fixture: raw stderr phrase spans a Windows rendering line break. |
| WindowsUnitPlatformAdmissionTests | 3/2/0 | 4/1/0 | Verification setup: C1210 child requires absent source stamp. |
| ChangeAwareReleaseTests | 2/1/0 | 0/3/0 | Fixture resolver; installed control exposes wrong-origin and unresolved pipe-drain timeout. |
| RcQualificationStatusTests | 2/1/0 | 1/2/0 | Fixture resolver; installed control fails only directory timestamp inventory. |
| ReleaseGatePlatformTests | 2/1/0 | 0/3/0 | Fixture resolver; installed control 3/0/0: fixed by rc/2026-10-27 after resolver bypass. |
| ReleaseGateFollowupTests | 1/1/0 | 0/2/0 | Fixture resolver; installed control below. |
| HostCleanupCheckpointExtractionTests | 2/1/0 | 3/0/0 | Fixed by rc/2026-10-27. |
| WorktreeRemovalAuthorityTests | 8/1/0 | 8/1/0 | Product: unowned bin-* directory is recursively destroyed. |
| AmServiceDeployReadinessTests | 5/1/0 | 6/0/0 | Fixed by rc/2026-10-27. |
| RollingVolumeRecycleScriptTests | 22/4/4 | 21/5/4 | Fixture: Windows path passed to WSL bash (3), CRLF regex (1), trace sharing (1). |
| GitWorkspaceServiceIndexLockTests | 2/1/0 | 4/0/0 | Fixed by rc/2026-10-27. |
| LandNonCodeSkipTests | 92/12/0 | 101/3/0 | Environment: SDK record mismatch; fixture: trailing-dot Git path and bypassed .cmd shim. |
| RemoteScriptContractTests | 258/7/234 | 259/6/234 | Fixture: Windows census paths (2), WSL shell/chown boundary (4); native witness below. |
| LandGraphEvaluationTests | 91/1/1 | 90/2/1 | Environment: SDK record mismatch; unresolved 120-second real-solution evaluation timeout. |

## Repeat and environment controls

| Class | Attempt | P/F/S | Exit | Wall seconds including slot wait |
|---|---|---:|---:|---:|
| CheckpointRecoveryWindowsTests | repeat1 | 10/5/0 | 2 | 175.62 |
| OutputCleanupTests | repeat1 | 22/16/11 | 2 | 245.2 |
| ReleaseGateRunTests | installed | 7/2/0 | 2 | 185.66 |
| CheckpointRepeatBuildIsolationTests | repeat1 | 0/1/0 | 2 | 350.94 |
| CheckpointRecoveryWindowsTests | repeat2 | 10/5/0 | 2 | 523.96 |
| ChangeAwareReleaseTests | installed | 1/2/0 | 2 | 979.03 |
| OutputCleanupTests | repeat2 | 34/4/11 | 2 | 212.53 |
| OutputCleanupTests | serial | 38/0/11 | 0 | 1502.69 |
| RcQualificationStatusTests | installed | 2/1/0 | 2 | 1462.52 |
| CheckpointRepeatBuildIsolationTests | repeat2 | 0/1/0 | 2 | 1560.56 |
| ReleaseGatePlatformTests | installed | 3/0/0 | 0 | 1421.67 |
| CheckpointRecoveryWindowsTests | repeat3 | 10/5/0 | 2 | 1419.8 |
| ReleaseGateFollowupTests | installed | 1/1/0 | 2 | 219.68 |
| OutputCleanupTests | repeat3 | 20/18/11 | 2 | 124.91 |
| CheckpointRepeatBuildIsolationTests | repeat3 | 0/1/0 | 2 | 147.15 |

The per-run console preserves `BUILD SLOT waited` and `held` durations. Queue time is not test execution time. One initial class (RollingVolumeRecycleScriptTests) executed for 24m30.485s; broad reruns are costly and were not used to disguise failures.

## Confirmed mechanisms and limits

### Recovery fixture custody and completion evidence

`tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:379` creates and seals the tool directory; `:389` adds payload and seals it again. `src/Antiphon.HostCleanup/Checkpoints/CheckpointCreationRegistry.cs:23` records content with `TryAdd`, retaining the first ledger hash. `src/Antiphon.HostCleanup/Checkpoints/OwnedOutputLedger.cs:37` rewrites the disk ledger; `:75` checks that ledger against its creation record. Four recovery cases consequently refuse `output-ledger-mismatch` before reaching their intended cleanup/locked-delete assertion. This is a fixture/contract mismatch with the append-only custody rule, not evidence that unrecorded deletion should be admitted. The .NET 9 production-API witness in `net9-reader-ledger-witness-2.log` reports clean first seal/check and a mismatch immediately after payload + reseal.

`done_live_executor_retains_image` constructs only a done state and executor identity (`tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:89`); `tools/Antiphon.Checkpoints/Commands/WaitCommand.cs:82` requires the report receipt after executor death. The native log explicitly says `refused: receipt-missing: ...\\report.md`; returning 2 is the existing required-receipt gate, while the fixture expects 0 at `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:95`.

### Remaining verified-reader stall

The staged-copy recovery case initially failed with `receipt-read-refused: ...\\staged\\Antiphon.Tests.dll: read-stalled: 0 of 69058048 bytes; no progress within 3s`. `src/Antiphon.HostCleanup/Checkpoints/CheckpointFileReader.cs:71` starts the deadline before open/inspection; `:88` supplies OVERLAPPED; `:104` synchronously waits for each asynchronous read; `:117` emits the observed diagnostic. The change is present and active: the exact compiled production `CheckpointNativeIo.OpenRead`/`OpenStream` path reports `IsAsync=True` with flags `48200000`, versus False with old flags `08200000`, under both .NET 9.0.22 and .NET 10.0.14. Six direct reads of fresh 69,058,048-byte DLL copies on .NET 9 took 106–380 ms, with metadata verification taking 0–33 ms. These observations disprove an absent flag and an intrinsically too-large file as explanations. They do not establish which host scheduling/filter/open/read phase consumed the historical deadline. No AV or ACL attribution is proven. Repeat outcomes are recorded separately.

### Windows atomic replacement contention

`tests/Antiphon.Tests/Checkpoints/RunStateStoreTests.cs:27` deliberately reads with `FileShare.ReadWrite | FileShare.Delete`; its 300-write loop fails at `:44`. `tools/Antiphon.Checkpoints/State/RunStateStore.cs:28` permits five contention retries; `:35` calls `CheckpointWrite.Text`, which uses `Io.Move(... overwrite: true)` at `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:189`; the native implementation is `src/Antiphon.HostCleanup/Checkpoints/CheckpointNativeIo.cs:92`. The exact native failure is `UnauthorizedAccessException: Access to the path is denied.` at that move.

The independent .NET 9 witness uses the same reader share mask. Raw File.Move completes 300 writes without a continuous reader (505 ms), but fails on the second replacement with a reader, HResult `80070005`. The production store completes 300 writes without a continuous reader (198,302 ms), but fails after 16 writes with a continuous reader (25,848 ms), despite its existing retries. The payload remains Archive, not ReadOnly. The mechanism is Windows concurrent replacement contention; a permission/ACL change is not established. This is a product Windows-semantics failure under the state writer's actual reader workload. The test also lacks a finally that joins its reader when writing throws (`tests/Antiphon.Tests/Checkpoints/RunStateStoreTests.cs:41`–`:49`); the test-process exit bounds that residual worker in these runs.

### PowerShell resolution prevents the release harness from launching

`tests/Antiphon.Tests/Scripts/ScriptHarnessProcess.cs:191` chooses the first existing pwsh.exe on PATH. This host resolves the Store alias at `C:\Users\lndco\AppData\Local\Microsoft\WindowsApps\Microsoft.PowerShell_8wekyb3d8bbwe\pwsh.exe` before the regular `C:\Program Files\PowerShell\7\pwsh.exe` (Archive, 301,360 bytes). `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:63` then refuses any WindowsApps path or reparse point. The fixture has a separate alias-skipping resolver at `tests/Antiphon.Tests/Scripts/ScriptHarnessProcessFixture.cs:28`, explaining why the Windows ownership class passes while ordinary release harness classes fail before running their assertions. The initial errors explicitly name this refusal. Controlled reruns only prepend the installed directory in the child environment; machine PATH and source remain unchanged.

### Escaped Git configuration breaks local-origin admission

`scripts/test-release-gate.ps1:1093` constructs a native backslash origin path and clones it. `scripts/lib/nightly-common.ps1:182` reads the raw config with a regular expression, returning Git's escaped backslashes, then `:238` compares it directly with the unescaped RemoteUrl. `origin-witness.log` shows supplied `C:\...\origin.git`, raw-reader `C:\\...\\origin.git`, normal Git reader `C:\...\origin.git`, and ordinal-ignore-case equality False. This confirms a product helper's Windows Git-config parsing defect for local-path origins; it does not establish a failure of HTTPS production origins. The affected release fixture assertions stop at `ownership / wrong-origin` before qualification work starts.

### Card error rendering

`scripts/card.ps1:278` emits the server detail and code through Write-Error. The Windows PowerShell concise renderer inserts a line break between `Relationship route` and `unavailable`. `tests/Antiphon.Tests/Scripts/CardRelationshipScriptTests.cs:170` searches raw stderr for the contiguous phrase. `card-error-witness.log` reconstructs the same loopback 404 response through the real script with both available PowerShell executables; both retain the detail and `code relationship_route_missing`, and both fail the contiguous-string search. This is a fixture assertion about display wrapping, not missing API error data. Full unmodified stderr is saved in `card-error-alias.stderr` and `card-error-installed.stderr`.

### Missing checkpoint source stamp

`tests/Antiphon.Tests/Scripts/C1008PlatformContractTests.cs:128`–`:133` requires `checkpoint-build-source.json` beside the outer compiled output; `tests/Antiphon.Tests/Scripts/WindowsUnitPlatformAdmissionTests.cs:438`–`:449` validates the equivalent binding. The requested direct dotnet-build/no-build route produces no such checkpoint stamp. C1008's two audits and C1210's child audit therefore refuse before their intended child selections. The initial source is clean, but clean source alone is not a build binding. This reproduces a runner/verification setup gap; those child audits remain unexecuted, and no stamp was manufactured to claim otherwise.

### Opaque wildcard deletion

`tests/Antiphon.Tests/Infrastructure/WorktreeRemovalAuthorityTests.cs:20` creates `bin-private/nested/bin-archive/report.md` containing `irreplaceable`, ages the parent and runs the cleanup script only against its private fixture. Its assertion at `:38` finds the file missing. `scripts/cleanup-build-junk.ps1:29` selects every `bin-*`; `:38` enumerates matches and `:49` mirrors an empty directory over each old match before `:50` recursively deletes it. There is no producer-owned inventory check on that path. This is a reproduced product data-loss defect on Windows, not a test-harness admission failure. All deletion exercised here was confined to test-owned scratch.

### SDK and index-path provenance

The installed SDK is 10.0.300; `server/Application/Services/LandVerificationBuildGraph.evaluation.txt:2` records 10.0.402. `scripts/audit-land-verification-graph.ps1:110` resolves the current SDK and `:139` refuses an unequal three-line evaluation record. `tests/Antiphon.Tests/Application/LandGraphEvaluationScriptTests.cs:123` eventually checks the actual repository, so a matching tiny fixture does not remove that host-record mismatch. Separately, `tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs:2406` seeds a trailing-dot path through `git update-index --cacheinfo` at `:2832`; native Git's path rejection can stop the fixture before its intended audit. Native outcomes below distinguish these mechanisms rather than attributing every landing failure to the SDK.

### Rolling-volume fixture boundaries

The 30-result class takes 1,476.14 seconds including its slot wrapper; 21 pass, five fail and four are native-Linux skips. Three failures (`C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance`, `C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts`, `C1371_Post_removal_count_drift_does_not_abort`) reach `tests/Antiphon.Tests/Scripts/C1361CensusProbes.cs:830`–`:834`, which starts bare `bash` and passes an absolute Windows path to the script. PATH resolves that name to `C:\Windows\System32\bash.exe`, the WSL launcher. The error is exit 127 and `/bin/bash: C:UserslndcoAppDataLocalTemp...probe.sh: No such file or directory`. The separately streamed host-side probe can pass through `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:8850`'s explicit wsl.exe route; that does not make the local Windows-file argv valid. These are native-path/fixture-shell defects, not failed remote census assertions.

`C1353_Wrapper_waits_for_a_bound_land_then_proceeds` receives `RECYCLE_LAND_WAIT runner=server2 id=11111111-1111-1111-1111-111111111111 polls=0`, but `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:525` counts with a multiline `polls=\\d+$` regex that does not consume Windows `\\r` before `\\n`. The test's count is zero despite the visible line; its sibling `tests/Antiphon.Tests/Scripts/C1364BoundLandWaitProofTests.cs:103` explicitly trims carriage returns. This is a CRLF assertion defect.

`C1008_Busy_routed_and_land_in_flight_refuse` throws a native sharing violation in `File.ReadAllText(trace.jsonl)` at `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:423`, while its child writes that trace. File.ReadAllText does not request reciprocal write sharing; this is a fixture reader/writer sharing conflict. The fixture joins its child in the finally at `:428`. The assertion did not establish a production rollout failure.

### Cases still needing causal evidence

OutputCleanup's initial three failures are `git-timeout`, including a refusal list containing that value instead of `tracked`. The class declares Unit but no process limiter (`tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:13`), while its helper `:1624` and numerous bodies start Git. This is a scheduling hypothesis, not a proven timeout root cause. The repeat and serialization controls are recorded separately above; their native findings are discussed below.

CheckpointRepeatBuildIsolation initially builds the small FilterHost successfully but refuses `output-inventory-failed`, executing zero child tests. `scripts/lib/checkpoint-owned-output.ps1:31` catches all read failures and erases the underlying path/exception; `:25` uses the same verdict for link detection. `Read-CheckpointBytes` at `checkpoint-file-reader.ps1:193` also retains its 64 MiB document bound. The generic verdict alone cannot distinguish a stalled read, an oversize entry or a link. No source-level cause is confirmed until that first underlying refusal is captured. The exact CHECKPOINT lines and zero counts remain in the failure log.



## Additional native evidence and unresolved questions

### OutputCleanup timing and process custody

Initial, repeat1 and repeat2 produced respectively 3, 16 and 4 failures, with changing test names. Most are `git-timeout`; repeat1 also throws `InvalidOperationException: process custody unavailable`. `scripts/lib/CheckpointProcessRunner.cs:68` and `:79` synchronously wait on an asynchronous child/configuration operation under one deadline. At `:127` the process starts before `:132` assigns it to a Job Object; `:134` erases the native reason into the observed generic custody exception. A quick child exit before assignment and worker scheduling are live hypotheses, not confirmed causes. The serialized control and third repeat are reported in the result table. Neither additional retries nor longer timeouts were introduced. Resolving this needs the native assignment error, child exit state and phase/pipe/worker timing at the first failed operation.

The serialized control completed 38 passed / 0 failed / 11 skipped in 1m34.932s of test execution (1,502.69s including its long slot wait). Every runnable test passed without source changes. This supports sensitivity to concurrency; changing host load between controls prevents treating it as a fully isolated proof of one scheduling mechanism.

The third parallel repeat is 20 passed / 18 failed / 11 skipped, again mostly Git timeouts with a custody exception. It additionally reports missing `run-stop/state.json` at `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:718`: the preceding `RunStateStore.Write` return value at `:717` is ignored, although `tools/Antiphon.Checkpoints/State/RunStateStore.cs:35`–`:37` can return a refusal without creating the file. This failure occurs before the stop command is invoked; it is not evidence of stop deleting tracked state. The exact ignored refusal was not logged and must be captured before assigning its cause. Some Shouldly output strings are truncated by the assertion library; the unedited TRX error is retained rather than reconstructed text being substituted for it.

### A passing class whose baseline environment differed

`AmServiceDeployReadinessTests.RetriesUntilHttpReadyAsync` now passes, but the original message was `fatal: path 'scripts/deploy-am-service.ps1' exists on disk, but not in '10fe51b0f668a272d621a935c27e53313b8de1c8'`. This worktree's `git ls-tree` for that same commit returns blob `3a7f8565a47c174daaaa1d28eac6a4b2347d996e` for the path. The comparison is a native pass, not proof that a readiness product change fixed the earlier clone/object failure.

### ChangeAware release control reaches an additional failure

With installed PowerShell selected, `C1313_ParameterHops` reaches the confirmed escaped-local-origin refusal. Separately, `C1313_BaselineAuthority` times out after 5m01s with `completion root=True stdout=False stderr=False`, after 12 passing assertion rows ending at `baseline-ancestor-required`. The harness expects 14. `tests/Antiphon.Tests/Scripts/ScriptHarnessProcess.cs:141` reports the root and drain task states together. The root process has exited while both redirected drains remain incomplete; the descendant or retained handle responsible was not captured. This is an unresolved process/pipe lifecycle failure, not proof of a baseline authority assertion failing. The original run passed this case. It has not been called a flake without three case-specific repeats.

### Landing fixture entry points

The native `C1390_MissingNulFailsUnderNonInvariantCulture` failure reports `tree=166 supplied=165` yet audit exit zero. `tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs:2572` creates `bin/git.cmd` and prepends its directory to PATH. The audit's `scripts/audit-land-verification-precheck.ps1:1542` uses `ProcessStartInfo.FileName='git'` with `UseShellExecute=false`. That executable resolution selects real `git.exe`, bypassing the `.cmd` shim; the intended malformed byte stream never reaches the parser. This is a fixture interception failure, not evidence that the parser accepts a truncated tree.

The native `C1409_NormaliserAndItemOperationTablesHaveWitnesses` failure is `error: Invalid path 'server/fixtures/notes.md.'`, exit 128, at `git update-index --cacheinfo` in `tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs:2832`. The trailing-dot entry is specified at `:2406`. The fixture cannot construct that index entry on this Windows Git before testing the audit. This failure is independent of the SDK evaluation record.

The actual native `C1385_RegistryMatchesNarrowGraph` failure at `tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs:365` reports `digest=124B52D47DD3232B60F4AEAF083B5E50ACC68DEDDE374AB2ECD6D43E543DC055 count=65` followed by `audit refused: evaluation record mismatch: SDK`. Thus the recorded-SDK incompatibility is observed, not only inferred. The earlier run stopped at a digest comparison; the current run reaches the SDK gate.

`LandGraphEvaluationTests.C1483_S5_CheckRequiresEvaluationRecord` independently reaches the same exact `evaluation record mismatch: SDK` at `tests/Antiphon.Tests/Application/LandGraphEvaluationScriptTests.cs:123`, after its tiny-repository record cases. That establishes the SDK cause for this failure, while the separate real-solution evaluator timeout below remains unresolved.

### Unedited checkpoint provenance excerpt

`CheckpointRepeatBuildIsolationTests-repeat1/failures.log` retains this exact child verdict (the outer class held a build slot):

```text
refused: output-inventory-failed
CHECKPOINT CP-repeat-2 build=failed project=tests/Antiphon.Checkpoints.FilterHost outputPath=bin-c885-script-c723-01a17c4a80ac7ea29f74ad59414523f3-sequence-2/ exit=2 slot=skipped waited=0s
MSBUILD PROPERTY AntiphonCheckpointRepeat=2
MSBUILD PROPERTY AntiphonCheckpointRepeatProject=C:\Antiphon\worktrees\card-task-fb80be16\tests\Antiphon.Checkpoints.FilterHost\Antiphon.Checkpoints.FilterHost.csproj
MSBUILD PROPERTY ArtifactsPath=C:/Antiphon/worktrees/card-task-fb80be16/tests/Antiphon.Checkpoints.FilterHost/bin-c885-script-c723-01a17c4a80ac7ea29f74ad59414523f3-sequence-2/artifacts
MSBUILD PROPERTY UseArtifactsOutput=true
CHECKPOINT CP-repeat-2 commit=281431340b1c78aca392f703701f4f853703e70f build=failed filter=/*/*/FilterHostTests*/* executed=0 passed=0 failed=0 skipped=0 trx=C:\Users\lndco\AppData\Local\Temp\c723-01a17c4a80ac7ea29f74ad59414523f3\results-2\CP-repeat-2-20261027-073702-9171\run.trx slot=skipped waited=0s dirty=0 source=281431340b1c78aca392f703701f4f853703e70f sourceState=clean buildSource=unknown repeat=2 repetitions=0/2 hostInvocations=1
CHECKPOINT CP-repeat-2 EXIT CODE: 2
```

The primary build's FilterHost payload has no file exceeding 5,253,528 bytes; the shell reader's 64 MiB bound alone does not explain that payload. The exact repeat output was cleaned by the fixture's finally. Capturing the first exception/path inside its seal operation remains necessary; a generic inventory refusal cannot establish an AV, ACL, file-size or stalled-read cause.

A read-only inventory captured while repeat2 was running (`repeat2-output-inventory.json`) contains 203 entries, none reporting a link target or ReparsePoint attribute; its largest file is likewise 5,253,528 bytes. This narrows the oversize/reparse hypotheses for the observed tree, but is not a handle-bound proof of what the seal encountered later.

### Native status and CRLF witnesses

`status-witness.log` reconstructs `Test-C1500_StatusDoesNotMutate` using the real fixture and status entrypoint, preserving the original inventory calculation and saving both inventories outside the observed root. The real-entrypoint and no-action-command assertions pass; the inventory assertion fails. Only `coord` and `state` directory timestamps differ: `coord` 639286858659423213 → 639286858659463206 ticks; `state` 639286858659273212 → 639286858659353203 ticks. Every file hash and file timestamp is identical. `scripts/fixtures/c1500-status.ps1:99` inventories directory timestamps as well as files, immediately after fixture lock replacements at `:110`. `scripts/lib/rc-qualification-status.ps1:34` opens read-only and its finally at `:60` disposes the stream; no writer helpers are loaded. This narrows the failure to directory metadata, not changed status input bytes. A no-status-call inventory control is recorded separately before assigning the remaining mechanism.

The same gated witness launches installed PowerShell with the production-style `Write-Host` line and reads its raw redirected pipe: `CRLF raw=RECYCLE_LAND_WAIT runner=server2 id=11111111-1111-1111-1111-111111111111 polls=0<CR><LF>` and `CRLF originalRegexCount=0`. This directly confirms the rolling-volume CRLF reconstruction. TRX XML newline normalization was not used as evidence of original carriage returns.

The shell reader also read all 139 files in the primary FilterHost output with `Read-CheckpointBytes`, `FILTERHOST_READ ok=139 bad=0`. That read-only control does not reproduce the exact repeat output's masked refusal.

`status-noop-control.log` removes the status invocation entirely: it constructs the same fixture, takes consecutive inventories, waits 100 ms and inventories again. All three cycles report `immediateEqual=False delayedEqual=False`; only `state`/`coord` directory ticks differ and every file hash/timestamp remains equal. Thus the status command is not required to produce this failure. This confirms a fixture false positive from assuming directory metadata is immediately stable after its own replacement/backup-deletion setup (`scripts/lib/nightly-common.ps1:372`–`:374`). The precise Windows/PowerShell metadata-refresh subphase was not instrumented; product input mutation is disproven for this witness.

### WSL Docker-model shell mismatch

Four of RemoteScriptContractTests' six failures are reconstructed from the same shell boundary. `scripts/fixtures/c1192-workspace-contract.sh:315`–`:316` defines and exports a Bash `chown` function. Its `exec_in` chooses `sh` for `/bin/sh` at `:582` and `model_mount_exec` executes that interpreter at `:2465`. An exported Bash function is not a dash function. The gated native control records:

```text
WSL uid=1000
WSL sh=/usr/bin/dash
BASH type=function
SH type=chown is /usr/bin/chown
SH chown exit=1
STUB_CHOWN -R 1654:1654 -- /tmp/fb80be16-chown.s8q99G/sample
BASH chown exit=0
chown: changing ownership of '/tmp/fb80be16-chown.s8q99G/sample': Operation not permitted
```

The control uses only its own `/tmp/fb80be16-chown.*` scratch, removes it, and invokes no Docker daemon. This is a fixture shell/privilege mismatch on the actual WSL environment, not a production Docker chown failure.

- `C1191_Lock_probe_is_created_before_uid_handoff` reports `handoff-snapshot=1`, proving the probe existed, but `handoff-code=2` and `handoff-diagnosis=FixtureRaceWriteFailed`. The real product helper invokes `/bin/sh -c chown` at `scripts/c590-remote.sh:4047`; materialization refuses its nonzero result at `:4202`.
- `C1192_Race_programs_and_feed_share_the_work_volume` reports the same `race-code=2` / `FixtureRaceWriteFailed`; subsequent volume facts are zero because materialization stops at that boundary.
- `C1192_Npm_seed_install_and_refill_use_work_volume` reports `npm-work-volume=1`, `npm-hash-ok=1`, `npm-code=2`, and no refill/offline invocation. The `/bin/sh` chown at `scripts/c590-remote.sh:4486` refuses as `FixtureNpmOfflineSetupFailed` before network creation at `:4489`.
- `C1333_Npm_network_create_failure_reports_FixtureNpmNetworkFailed` receives `FixtureNpmOfflineSetupFailed` for the same reason; its intended network-create fault is never reached.

The other two Remote failures are the already-confirmed `tests/Antiphon.Tests/Scripts/C1361CensusProbes.cs:830` bare-bash Windows-path invocation, exit 127. `C1192_Race_barriers_observe_volume_state`, which failed in the old run, now passes. The final native class is 259 passed / 6 failed / 234 native-Linux skips in 19m08.463s.

### Reader repeat verdict

All three complete Recovery repeats are 10 passed / 5 failed. The staged-copy case passes in each; the same four ledger fixture failures and one missing-receipt failure remain. The initial reader stall is therefore intermittent (one failure in four native class runs), with its underlying stalled phase still unconfirmed. The active asynchronous-handle and direct-read controls rule out an absent reader fix, not every possible host/runtime scheduling stall.

### Graph evaluator has an additional unresolved timeout

`C1483_S4_RealRepositoryApproves` fails at `tests/Antiphon.Tests/Application/LandGraphEvaluationProbeTests.cs:100` after `LAND_GRAPH_EVALUATION ... elapsedMs=120363 exit=2`. Its output is `evaluation-failed: evaluating Antiphon.sln exceeded 120 s`. `tools/Antiphon.LandGraphAudit/Program.cs:19` defines that solution budget and `:78`–`:80` waits for the evaluation task and returns the observed failure. This is separate from the confirmed SDK-record mismatch: the evaluator has started evaluating the real solution. Four preceding `not-built:` rows are expected exclusions, not the reported failure. The active project/import/phase at expiry was not captured, so host load, SDK evaluation cost and an evaluation stall remain live hypotheses. This new timeout is not labelled a flake without three method-specific repeats; no timeout was widened.

### Native class execution cost

`LandNonCodeSkipTests` completed 101 passed / 3 failed in 36m28.414s (2,389.52s including its slot wait). At the 30-minute threshold a read-only process snapshot (`land-long-run-processes.json`) showed fresh Git children under its known test host, including a receive-pack fixture operation. That establishes continued work, not a dead process, but does not identify a p95 cost distribution or qualify the class against the performance standard. A targeted performance investigation is needed before treating that duration as acceptable.

The two final classes were queued as separate, individually filtered invocations while LandNonCode was running. After recording LandNonCode's TRX, the initial driver deliberately hit its existing-directory refusal for RemoteScriptContractTests (driver exit 1); the independently queued class owns that directory. This prevented duplicate class execution. Individual test exit codes/counts come from their own records, not that coordinator exit.

### Installed-PowerShell release comparison

The installed controls finish as ReleaseGateRun 7/2/0, ChangeAwareRelease 1/2/0, RcQualificationStatus 2/1/0, ReleaseGatePlatform 3/0/0 and ReleaseGateFollowup 1/1/0. The last failure is `C1336_NightlyCandidateIdentity`, with downstream `{ExitCode:3, Phase:ownership, Refusal:wrong-origin}` at `tests/fixtures/card1336-release-gate-followups.ps1:214`, matching the independently proved escaped-Git-config mechanism. ReleaseGatePlatform's historical native failure no longer reproduces once the resolver admits installed PowerShell.

## Completion and remaining uncertainty

All 34 requested initial classes and all three repeats of each selected suspect class completed. There are 49 unique nonempty TRX invocations (34 initial, 9 repeats, 5 installed-PowerShell controls, 1 serialized control); their per-result outcomes were checked against every `runs.jsonl` row and failure-log count. Every row retains tested source `281431340b1c78aca392f703701f4f853703e70f`. Initial totals are 655 passed / 51 failed / 251 skipped, compared with original 623 / 80 / 251; the current selections contain three more total test cases. Seventeen classes have no initial native failures, and ReleaseGatePlatform also passes its installed-PowerShell control.

RepeatBuildIsolation fails with the same masked `output-inventory-failed` in the initial run and all three repeats; it is persistent, not classified as a flake. The final repeat ends at 2026-10-27 08:45:41.879 UTC. The successful primary build started at approximately 06:43:56 UTC; execution alone therefore exceeded two hours, and the full investigation exceeded the brief's 120-minute budget. Long shared-slot waits and the 36-minute LandNonCode class contributed. All owned invocations were awaited before publication; no unleased retry or timeout increase was used.

After the final run, the test DLL hash matched all three earlier diagnostic copies. All 33 owned `bin-fb80be16` directories were resolved beneath this worktree, checked for reparse points and removed with native PowerShell. `cleanup-owned-output.log` ends `OUTPUT_CLEANUP removed=33 remaining=0`. The final process census found zero task-matching Antiphon.Tests, dotnet or WSL processes. Generated evidence and scratch witnesses remain ignored; no product or test source was changed.

The investigation is complete as an evidence deliverable, but these causes remain unconfirmed and determine the next stage:

1. Capture the first file/path/exception hidden by `Seal-CheckpointFreshOutput` during RepeatBuildIsolation. The 64 MiB bound and reparse hypotheses are not established by the observed payload inventory.
2. Capture phase and scheduler timing at the intermittent staged-copy reader deadline. Three repeats pass; the asynchronous handle change is verified, while the initial stall remains real.
3. Capture native Job Object assignment errors, Git config/command/pipe timings and the ignored initial state-write refusal under the parallel OutputCleanup workload. Serialization is a passing control, not a full causal trace.
4. Identify the process/handle retaining ChangeAwareRelease stdout/stderr after root exit. The timeout did not identify the owner and was not rerun three times as a claimed flake.
5. Capture the active project/import and phase timings when real-solution graph evaluation reaches 120 seconds. SDK-record mismatch is confirmed for the separate record checks, not proven as this evaluator timeout's cause.

Coverage limits remain: the three stamp-dependent child audits were refused before execution, skipped Linux selections are not Windows proof, and the assigned tree differs from the exact rc tree as described above. Confirmed product/fixture mechanisms can move independently to Plan; no design hedge is being chosen for unresolved mechanisms here.




## Failed-test index

These are the exact failed test names in the fresh TRX files, grouped by invocation. Each linked local evidence directory contains unedited full error text and stack in `failures.log`; source paths and line numbers are in those stacks. Repeats are retained even when their failure set changes.

### CheckpointRecoveryWindowsTests / initial

Evidence: `CheckpointRecoveryWindowsTests-initial/failures.log` (6 failures).

- `native_identity_keeps_live_then_removes_dead` — ShouldAssertException: Remove(run, cleanup).Outcome; `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:45`.
- `done_live_executor_retains_image` — ShouldAssertException: await WaitAsync(run, TimeSpan.FromSeconds(1)); `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:95`.
- `starter_death_before_launch_reclaims_partial_copy` — ShouldAssertException: Remove(run).Outcome; `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:113`.
- `detached_staged_copy_survives_starter_exit` — IOException: receipt-read-refused: C:\Users\lndco\AppData\Local\Temp\c723-01a17c206c7d7968b4e5a6815d8da7d2\staged\Antiphon.Tests.dll: read-stalled: 0 of 69058048 bytes; no progress within 3s; `src/Antiphon.HostCleanup/Checkpoints/ReceiptFile.cs:39`.
- `locked_tool_delete_is_reported_then_retried` — ShouldAssertException: Remove(run).Outcome; `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:252`.
- `launch_unknown_survives_until_late_ack` — ShouldAssertException: Remove(run).Outcome; `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:237`.

### OutputCleanupTests / initial

Evidence: `OutputCleanupTests-initial/failures.log` (3 failures).

- `refuses_unsafe_build_ids_and_keeps_tracked_files` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `nested_repository_tracked_output_is_owner_checked_and_kept` — ShouldAssertException: result.Refusals; `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:531`.
- `never_deletes_bin_obj_workspace_or_foreign_bin_star` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.

### CheckpointRepeatBuildIsolationTests / initial

Evidence: `CheckpointRepeatBuildIsolationTests-initial/failures.log` (1 failures).

- `repeat_two_then_default_then_three_rebuilds_the_same_project` — ShouldAssertException: process.ExitCode; `tests/Antiphon.Tests/Checkpoints/CheckpointRepeatBuildIsolationTests.cs:55`.

### RunStateStoreTests / initial

Evidence: `RunStateStoreTests-initial/failures.log` (1 failures).

- `readers_never_see_a_partial_file` — UnauthorizedAccessException: Access to the path is denied.; `src/Antiphon.HostCleanup/Checkpoints/CheckpointNativeIo.cs:92`.

### C1008PlatformContractTests / initial

Evidence: `C1008PlatformContractTests-initial/failures.log` (2 failures).

- `C1050_Windows_remote_outcomes_are_exact` — ShouldAssertException: File.Exists(stampPath); `tests/Antiphon.Tests/Scripts/C1008PlatformContractTests.cs:133`.
- `C1050_Windows_rolling_outcomes_are_exact` — ShouldAssertException: File.Exists(stampPath); `tests/Antiphon.Tests/Scripts/C1008PlatformContractTests.cs:133`.

### ReleaseGateRunTests / initial

Evidence: `ReleaseGateRunTests-initial/failures.log` (9 failures).

- `C599_CreditIdentity` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.
- `C599_CreditVerdicts` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.
- `C599_SharedLock` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.
- `C599_ParameterHops` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.
- `C599_MasterStateIsolation` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.
- `C599_CloneOwnership` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.
- `C599_SelectionHopExitCodes` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.
- `C599_ReportLane` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.
- `C599_Continuation` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.

### CardRelationshipScriptTests / initial

Evidence: `CardRelationshipScriptTests-initial/failures.log` (1 failures).

- `Block_fails_loudly_when_relationships_returns_404` — ShouldAssertException: run.Stderr; `tests/Antiphon.Tests/Scripts/CardRelationshipScriptTests.cs:170`.

### WindowsUnitPlatformAdmissionTests / initial

Evidence: `WindowsUnitPlatformAdmissionTests-initial/failures.log` (1 failures).

- `C1210_Windows_linux_only_outcomes_are_exact` — ShouldAssertException: launch.SourceVerified; `tests/Antiphon.Tests/Scripts/WindowsUnitPlatformAdmissionTests.cs:358`.

### ChangeAwareReleaseTests / initial

Evidence: `ChangeAwareReleaseTests-initial/failures.log` (3 failures).

- `C1313_ModeCredit` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.
- `C1313_BaselineAuthority` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.
- `C1313_ParameterHops` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.

### RcQualificationStatusTests / initial

Evidence: `RcQualificationStatusTests-initial/failures.log` (2 failures).

- `C1500_StatusIdentityAndObservation` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.
- `C1500_StatusDoesNotMutate` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.

### ReleaseGatePlatformTests / initial

Evidence: `ReleaseGatePlatformTests-initial/failures.log` (3 failures).

- `C1323_PlatformPolicy` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.
- `C1323_PlatformExecution` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.
- `C1415_SkipExclusions` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.

### ReleaseGateFollowupTests / initial

Evidence: `ReleaseGateFollowupTests-initial/failures.log` (2 failures).

- `C1336_NightlyCandidateIdentity` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.
- `C1336_WrapperRef` — InvalidOperationException: ScriptHarness requires a real pwsh.exe path, not an App Execution Alias; set ExecutablePath to the installed PowerShell executable.; `tests/Antiphon.Tests/Scripts/WindowsScriptHarnessProcess.cs:67`.

### WorktreeRemovalAuthorityTests / initial

Evidence: `WorktreeRemovalAuthorityTests-initial/failures.log` (1 failures).

- `C448_V35_BuildJunkScriptPreservesOpaqueWildcardMatches` — ShouldAssertException: File.Exists(path); `tests/Antiphon.Tests/Infrastructure/WorktreeRemovalAuthorityTests.cs:38`.

### CheckpointRecoveryWindowsTests / repeat1

Evidence: `CheckpointRecoveryWindowsTests-repeat1/failures.log` (5 failures).

- `starter_death_before_launch_reclaims_partial_copy` — ShouldAssertException: Remove(run).Outcome; `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:113`.
- `native_identity_keeps_live_then_removes_dead` — ShouldAssertException: Remove(run, cleanup).Outcome; `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:45`.
- `done_live_executor_retains_image` — ShouldAssertException: await WaitAsync(run, TimeSpan.FromSeconds(1)); `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:95`.
- `launch_unknown_survives_until_late_ack` — ShouldAssertException: Remove(run).Outcome; `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:237`.
- `locked_tool_delete_is_reported_then_retried` — ShouldAssertException: Remove(run).Outcome; `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:252`.

### OutputCleanupTests / repeat1

Evidence: `OutputCleanupTests-repeat1/failures.log` (16 failures).

- `deletes_only_paths_recorded_for_this_run` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `windows_set_clock_and_foreign_dacl_are_not_adopted` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `stop_refuses_foreign_state_without_writing` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/RunOwnership.cs:56`.
- `safe_write_refuses_tracked_linked_unrecorded_and_git_failure` — InvalidOperationException: process custody unavailable; `scripts/lib/CheckpointProcessRunner.cs:136`.
- `windows_copied_old_change_time_is_adopted` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `nested_repository_inside_an_owned_output_is_refused` — InvalidOperationException: process custody unavailable; `scripts/lib/CheckpointProcessRunner.cs:136`.
- `smoke_detach_refuses_tracked_foreign_and_linked_targets` — ShouldAssertException: trackedOut.ToString(); `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:1064`.
- `unknown_lifecycle_report_after_seal_is_refused_and_kept` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `evidence_under_antiphon_survives_and_dry_run_says_would_delete` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `seal_of_a_root_this_process_did_not_create_adopts_nothing` — ShouldAssertException: OwnedOutputLedger.Seal(output); `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:334`.
- `red_keeps_unless_clean_on_red` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `cleanup_refusal_exits_2_without_claiming_deleted` — InvalidOperationException: process custody unavailable; `scripts/lib/CheckpointProcessRunner.cs:136`.
- `never_deletes_bin_obj_workspace_or_foreign_bin_star` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `build_refuses_tracked_output_before_the_driver` — ShouldAssertException: output.ToString(); `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:921`.
- `nested_repository_tracked_output_is_owner_checked_and_kept` — ShouldAssertException: result.Refusals; `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:531`.
- `tracked_build_stamp_is_not_deleted_or_replaced` — ShouldAssertException: binding.Write(); `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:613`.

### ReleaseGateRunTests / installed

Evidence: `ReleaseGateRunTests-installed/failures.log` (2 failures).

- `C599_MasterStateIsolation` — ShouldAssertException: result.ExitCode; `tests/Antiphon.Tests/Scripts/ScriptHarness.cs:25`.
- `C599_ParameterHops` — ShouldAssertException: result.ExitCode; `tests/Antiphon.Tests/Scripts/ScriptHarness.cs:25`.

### CheckpointRepeatBuildIsolationTests / repeat1

Evidence: `CheckpointRepeatBuildIsolationTests-repeat1/failures.log` (1 failures).

- `repeat_two_then_default_then_three_rebuilds_the_same_project` — ShouldAssertException: process.ExitCode; `tests/Antiphon.Tests/Checkpoints/CheckpointRepeatBuildIsolationTests.cs:55`.

### RollingVolumeRecycleScriptTests / initial

Evidence: `RollingVolumeRecycleScriptTests-initial/failures.log` (5 failures).

- `C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance` — ShouldAssertException: run.Exit; `tests/Antiphon.Tests/Scripts/C1361CensusProbes.cs:822`.
- `C1353_Wrapper_waits_for_a_bound_land_then_proceeds` — ShouldAssertException: waitLines; `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:533`.
- `C1008_Busy_routed_and_land_in_flight_refuse` — IOException: The process cannot access the file 'C:\Users\lndco\AppData\Local\Temp\c1008-wrapper-yz1kbbss.el2\trace.jsonl' because it is being used by another process.; `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs:423`.
- `C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts` — ShouldAssertException: failures; `tests/Antiphon.Tests/Scripts/C1361CensusProbes.cs:247`.
- `C1371_Post_removal_count_drift_does_not_abort` — ShouldAssertException: failures; `tests/Antiphon.Tests/Scripts/C1361CensusProbes.cs:314`.

### CheckpointRecoveryWindowsTests / repeat2

Evidence: `CheckpointRecoveryWindowsTests-repeat2/failures.log` (5 failures).

- `done_live_executor_retains_image` — ShouldAssertException: await WaitAsync(run, TimeSpan.FromSeconds(1)); `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:95`.
- `native_identity_keeps_live_then_removes_dead` — ShouldAssertException: Remove(run, cleanup).Outcome; `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:45`.
- `starter_death_before_launch_reclaims_partial_copy` — ShouldAssertException: Remove(run).Outcome; `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:113`.
- `launch_unknown_survives_until_late_ack` — ShouldAssertException: Remove(run).Outcome; `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:237`.
- `locked_tool_delete_is_reported_then_retried` — ShouldAssertException: Remove(run).Outcome; `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:252`.

### ChangeAwareReleaseTests / installed

Evidence: `ChangeAwareReleaseTests-installed/failures.log` (2 failures).

- `C1313_BaselineAuthority` — TimeoutException: ScriptHarness test-change-aware-release.ps1 case C1313_BaselineAuthority exceeded 00:05:00 execution budget. completion root=True stdout=False stderr=False; `tests/Antiphon.Tests/Scripts/ScriptHarnessProcess.cs:141`.
- `C1313_ParameterHops` — ShouldAssertException: result.ExitCode; `tests/Antiphon.Tests/Scripts/ScriptHarness.cs:25`.

### OutputCleanupTests / repeat2

Evidence: `OutputCleanupTests-repeat2/failures.log` (4 failures).

- `windows_set_clock_and_foreign_dacl_are_not_adopted` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `deletes_only_this_runs_bin_names` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `evidence_under_antiphon_survives_and_dry_run_says_would_delete` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `nested_repository_inside_an_owned_output_is_refused` — ShouldAssertException: result.Refusals; `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:653`.

### RcQualificationStatusTests / installed

Evidence: `RcQualificationStatusTests-installed/failures.log` (1 failures).

- `C1500_StatusDoesNotMutate` — ShouldAssertException: result.ExitCode; `tests/Antiphon.Tests/Scripts/ScriptHarness.cs:25`.

### CheckpointRepeatBuildIsolationTests / repeat2

Evidence: `CheckpointRepeatBuildIsolationTests-repeat2/failures.log` (1 failures).

- `repeat_two_then_default_then_three_rebuilds_the_same_project` — ShouldAssertException: process.ExitCode; `tests/Antiphon.Tests/Checkpoints/CheckpointRepeatBuildIsolationTests.cs:55`.

### LandNonCodeSkipTests / initial

Evidence: `LandNonCodeSkipTests-initial/failures.log` (3 failures).

- `C1390_MissingNulFailsUnderNonInvariantCulture` — ShouldAssertException: ran.Code; `tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs:2613`.
- `C1409_NormaliserAndItemOperationTablesHaveWitnesses` — InvalidOperationException: table row N-12 failed: process.ExitCode; `tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs:2147`.
- `C1385_RegistryMatchesNarrowGraph` — ShouldAssertException: process.ExitCode; `tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs:365`.

### RemoteScriptContractTests / initial

Evidence: `RemoteScriptContractTests-initial/failures.log` (6 failures).

- `C1333_Npm_network_create_failure_reports_FixtureNpmNetworkFailed` — ShouldAssertException: Fact(output, "npm-diagnosis"); `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:1126`.
- `C1192_Npm_seed_install_and_refill_use_work_volume` — ShouldAssertException: Fact(output, "refill-volume-source"); `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:1100`.
- `C1192_Race_programs_and_feed_share_the_work_volume` — ShouldAssertException: Fact(output, "race-boundary"); `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:1054`.
- `C1191_Lock_probe_is_created_before_uid_handoff` — ShouldAssertException: Fact(output, "probe-before-handoff"); `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs:256`.
- `C1361_Host_census_tolerates_open_status_changes_between_passes` — ShouldAssertException: run.Exit; `tests/Antiphon.Tests/Scripts/C1361CensusProbes.cs:368`.
- `C1361_Host_census_tolerates_open_status_changes_between_list_and_detail` — ShouldAssertException: run.Exit; `tests/Antiphon.Tests/Scripts/C1361CensusProbes.cs:368`.

### LandGraphEvaluationTests / initial

Evidence: `LandGraphEvaluationTests-initial/failures.log` (2 failures).

- `C1483_S4_RealRepositoryApproves` — ShouldAssertException: run.ExitCode; `tests/Antiphon.Tests/Application/LandGraphEvaluationProbeTests.cs:100`.
- `C1483_S5_CheckRequiresEvaluationRecord` — ShouldAssertException: real.ExitCode; `tests/Antiphon.Tests/Application/LandGraphEvaluationScriptTests.cs:123`.

### CheckpointRecoveryWindowsTests / repeat3

Evidence: `CheckpointRecoveryWindowsTests-repeat3/failures.log` (5 failures).

- `native_identity_keeps_live_then_removes_dead` — ShouldAssertException: Remove(run, cleanup).Outcome; `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:45`.
- `done_live_executor_retains_image` — ShouldAssertException: await WaitAsync(run, TimeSpan.FromSeconds(1)); `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:95`.
- `starter_death_before_launch_reclaims_partial_copy` — ShouldAssertException: Remove(run).Outcome; `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:113`.
- `launch_unknown_survives_until_late_ack` — ShouldAssertException: Remove(run).Outcome; `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:237`.
- `locked_tool_delete_is_reported_then_retried` — ShouldAssertException: Remove(run).Outcome; `tests/Antiphon.Tests/Checkpoints/CheckpointRecoveryWindowsTests.cs:252`.

### ReleaseGateFollowupTests / installed

Evidence: `ReleaseGateFollowupTests-installed/failures.log` (1 failures).

- `C1336_NightlyCandidateIdentity` — ShouldAssertException: result.ExitCode; `tests/Antiphon.Tests/Scripts/ScriptHarness.cs:25`.

### OutputCleanupTests / repeat3

Evidence: `OutputCleanupTests-repeat3/failures.log` (18 failures).

- `junction_outside_the_isolated_root_is_refused` — InvalidOperationException: process custody unavailable; `scripts/lib/CheckpointProcessRunner.cs:136`.
- `seal_of_a_root_this_process_did_not_create_adopts_nothing` — ShouldAssertException: OwnedOutputLedger.Seal(output); `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:334`.
- `never_deletes_bin_obj_workspace_or_foreign_bin_star` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `windows_copied_old_change_time_is_adopted` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `stop_refuses_foreign_state_without_writing` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/RunOwnership.cs:56`.
- `deletes_only_this_runs_bin_names` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `stop_refuses_to_overwrite_tracked_state_json` — FileNotFoundException: Could not find file 'C:\Users\lndco\AppData\Local\Temp\c723-01a17c867e857ce692aa4bef2cf19463\run-stop\state.json'.; `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:718`.
- `windows_sharing_violation_retries_then_adopts` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `refuses_unsafe_build_ids_and_keeps_tracked_files` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `nested_repository_inside_an_owned_output_is_refused` — ShouldAssertException: result.Refusals; `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:653`.
- `build_entry_over_the_receipt_limit_is_adopted_at_seal_and_removed_on_green` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `unknown_lifecycle_report_after_seal_is_refused_and_kept` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `red_keeps_unless_clean_on_red` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `windows_set_clock_and_foreign_dacl_are_not_adopted` — IOException: refused: git-timeout; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:394`.
- `safe_write_refuses_tracked_linked_unrecorded_and_git_failure` — ShouldAssertException: SafeWrite.Text(tracked, "changed", SafeWrite.Under(trackedRoot)); `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:1146`.
- `run_refuses_tracked_latest_before_writing` — ShouldAssertException: output.ToString(); `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:856`.
- `referenced_project_tracked_output_is_refused_before_the_build` — ShouldAssertException: rowOutput.ToString(); `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:961`.
- `nested_repository_tracked_output_is_owner_checked_and_kept` — ShouldAssertException: result.Refusals; `tests/Antiphon.Tests/Checkpoints/OutputCleanupTests.cs:531`.

### CheckpointRepeatBuildIsolationTests / repeat3

Evidence: `CheckpointRepeatBuildIsolationTests-repeat3/failures.log` (1 failures).

- `repeat_two_then_default_then_three_rebuilds_the_same_project` — ShouldAssertException: process.ExitCode; `tests/Antiphon.Tests/Checkpoints/CheckpointRepeatBuildIsolationTests.cs:55`.

## Not done, noted

No fix designed or implemented; confirmed mechanisms need Plan, while masked inventory errors, Git timing/custody and pipe retention need further causal evidence.

--- next stage ---
next: investigate
handoff: Resolve the masked checkpoint inventory exception, intermittent reader/Git timeouts and retained output pipes from the recorded native cases; route already confirmed Windows product and fixture mechanisms separately to Plan.
artifact: docs/investigations/2026-10-27-native-rerun-scripts-checkpoint-release-gate.md
