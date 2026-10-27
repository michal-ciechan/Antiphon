# Scripts-lane product repairs — task 356abafc

All three assigned product defects were repaired and pushed separately from fixture changes. Linux verification is green: 20 passed, zero failed, zero skipped across 12 exact method selections. Native Windows verification remains owed; this report does not grant rc qualification or landing approval. Final Review is required.

Assigned base: `9f00008facfc48e75555c08df59887019b3f8dfe` (rc top-up 8). Assigned branch: `feat/card-task-356abafc`. Build and all 12 selections verified `dbca0041f7f91773838a8c6cc4ab434b608ec7a6`; subsequent changes are this report and the cleanup owner's documentation. No branch switch, rebase, target merge, tag, deployment or desktop test run occurred. The caller's standing fix authority covered these repairs and task-branch publication.

## Product behavior and causes

**Unowned bin-* deletion — highest priority.** At the base, `scripts/cleanup-build-junk.ps1:29` selected every `bin-*`, `:38` recursively enumerated matches, and `:49`–`:50` mirrored an empty directory and recursively removed aged matches. Neither a name nor an age proves the producer owns the bytes. The portable red witness deleted its private `bin-private/nested/bin-archive/report.md` containing `irreplaceable`, reproducing the native report's data-loss assertion. Its fixture-only robocopy stand-in permits the old Remove-Item path to run on Linux; all deletion was confined to fixture scratch.

Current `scripts/cleanup-build-junk.ps1:22` preserves outputs without producer creation records. With `-CleanupContext`, `:25` and `:34` reuse `CheckpointOutputLedger.Remove`, whose existing in-memory creation registry, ledger checks and nonrecursive entry removal remain authoritative (`src/Antiphon.HostCleanup/Checkpoints/OwnedOutputLedger.cs:66`, `:86`). An arbitrary disk marker or an explicit path does not import authority. The owned witness uses the real compiled library to create/seal an output, then proves owned removal and preservation of the opaque sibling in the same invocation. Existing tests also prove disk ledgers alone cannot authorize deletion.

Operational change: a scheduled invocation has no producing-process context, so it now logs a skip and retains legacy outputs. This deliberately removes age-only deletion authority. It does not provide cross-process adoption of old outputs. The same-process owned path remains available. The existing age threshold still skips recent eligible outputs.

**Windows state publication contention.** `tools/Antiphon.Checkpoints/State/RunStateStore.cs:35` calls `CheckpointWrite.Text`; `src/Antiphon.HostCleanup/Checkpoints/SafeWrite.cs:189` publishes a completed temporary through `Io.Move`. At the base, `CheckpointNativeIo.cs:92` always used File.Move(overwrite), which the native Windows report reproduced failing with access denied despite readers sharing deletion and the store's five retries.

Current `src/Antiphon.HostCleanup/Checkpoints/CheckpointNativeIo.cs:92` uses File.Replace for an existing Windows overwrite destination and retains File.Move for initial publication, non-overwrites and other platforms. This follows the replacement primitive already used by `scripts/lib/nightly-common.ps1:359`. The state writer's existing bounded retry schedule, read-only refusal, ownership checks and assertions are unchanged. No permission or deadline was loosened.

The portable state witness compiles the exact production Move method with only OS/native File observations modeled. The old method fails under the recorded overwrite-move contention model. The replacement method forces one sharing violation (`0x80070020`), verifies complete old bytes and retained temporary during denial, then verifies complete replacement on retry and the initial Move path. The existing state-publication test independently exercises the actual store's retry and permanent-refusal cases. The Linux continuous-reader test completes 300 writes without a partial document. These controls do not execute Windows ReplaceFile or prove the historical native workload is repaired.

The [Windows ReplaceFile documentation](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-replacefilew) documents its read/write/delete sharing mode for the replaced file. A [dotnet/runtime reproduction](https://github.com/dotnet/runtime/issues/114230) describes the overwrite-move/shared-delete problem; it corroborates the API choice, not attribution to antivirus on this host. Shared-caller scope for Final Review: NativeIo.Move also serves SafeWrite.Bytes/MoveOverwrite, TestRootGuard and the temporary-root index writer. Observed publishers use files within output directories; this shared primitive change merits review beyond the RunStateStore call site.

**Escaped local origins.** At the base, `scripts/lib/nightly-common.ps1:187` used a regex over raw config, selected the first url regardless of remote section, and returned Git's escaped representation. Its ownership comparison then rejected a supplied `C:\Antiphon\origin.git` against raw `C:\\Antiphon\\origin.git`.

Current `scripts/lib/nightly-common.ps1:188` asks Git to decode `remote.origin.url` from the explicit local config file with includes disabled. This retains the ownership check without importing global config or include-file authority. The pure-string witness uses actual Git config for a drive path, a spaces/quote string, a UNC path and HTTPS, and refuses an absent origin. The quote case is a config-string witness, not a claim that quotes form a legal Windows filename. The affected release fixture cases now pass on Linux.

## Fixtures and evidence limits

`scripts/test-scripts-lane-repairs.ps1` contains the four portable witnesses, registered by `tests/Antiphon.Tests/Scripts/ScriptsLaneRepairTests.cs` with the existing process-spawn limiter and bounded shared process runner. `RunStateStoreTests.cs` now joins its reader in finally even when a writer throws; all original assertions remain. These are separate fixture commits, not mixed into product commits.

The first cleanup probe stopped before its assertion because TEMP was unset on Linux; the fixture was corrected before the qualifying data-loss red. A preliminary origin fixture also exposed selection of another remote's first url; the qualifying escaped-path red used the actual origin. A repeated cleanup red occurred before the edit was successfully applied. Those preliminary calls are not additional passing tests or qualification credit; individual phase costs were not retained. The three qualifying red failure messages are retained in the ignored evidence logs and their causes above. No failed run was relabeled green.

## Verification

One product build, through the real host build-slot gate:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label 356abafc-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-debug356abafc/ --nologo
```

Build result: zero errors, 1,198 warnings, 329.39 seconds MSBuild elapsed; slot held 331 seconds. Platform: Debian 12 / linux-x64; SDK 10.0.403; test runtime .NET 9.0.22. No base build was needed to attribute a new failure because all selected tests passed. Warnings were not repaired or assigned new attribution.

Every row below used `scripts/build-slot.ps1` around `dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-debug356abafc/`, followed by `-- --treenode-filter "/*/*/<Class>/<Method>" --report-trx --report-trx-filename results.trx --results-directory <row-root>`. Each row acquired a granted lease. These are direct Debug runs, not checkpoint publication receipts; no build/source stamp or release credit was manufactured.

| Row | Exact class / method | Passed / failed / skipped | Outer wall seconds |
|---|---|---:|---:|
| CP-1 | ScriptsLaneRepairTests / Portable_scripts_lane_witness | 4 / 0 / 0 | 15.25 |
| CP-2 | RunStateStoreTests / readers_never_see_a_partial_file | 1 / 0 / 0 | 13.47 |
| CP-3 | CheckpointStatePublicationTests / state_publication_survives_contention_and_preserves_completed_rows | 1 / 0 / 0 | 36.85 |
| CP-4 | WorktreeRemovalAuthorityTests / C448_V35_BuildJunkScriptPreservesOpaqueWildcardMatches | 1 / 0 / 0 | 10.94 |
| CP-5 | HostCleanupCheckpointCustodyTests / Fresh_owned_output_can_be_removed | 1 / 0 / 0 | 7.92 |
| CP-6 | HostCleanupCheckpointCustodyTests / Disk_ledger_cannot_authorize_generic_delete | 1 / 0 / 0 | 24.59 |
| CP-7 | ReleaseGateRunTests / C599_MasterStateIsolation | 1 / 0 / 0 | 49.61 |
| CP-8 | ReleaseGateRunTests / C599_ParameterHops | 1 / 0 / 0 | 26.85 |
| CP-9 | ChangeAwareReleaseTests / C1313_ParameterHops | 1 / 0 / 0 | 131.07 |
| CP-10 | ChangeAwareReleaseTests / C1313_BaselineAuthority | 1 / 0 / 0 | 185.18 |
| CP-11 | ReleaseGateFollowupTests / C1336_NightlyCandidateIdentity | 1 / 0 / 0 | 142.13 |
| CP-12 | CheckpointWindowsReadModeTests / Windows_reads_do_not_queue_synchronous_io_behind_blocked_workers | 6 / 0 / 0 | 5.63 |

Totals: 20 passed, zero failed/skipped; 649.49 seconds summed row wall time including admission/startup. Counts were independently checked against each row's expected count and TRX counters. No selection or batch exceeded 30 minutes. Per-test TRX durations and phase sidecars are retained; available spans, not zero-valued absent instrumentation, define attribution. Child CLI/filesystem subphases and host-load attribution are unknown; uninstrumented work remains remainder. New witness case bodies measured 0.79–2.66 seconds in this one Linux sample. Their 30-second process boundary is provisional, accounting for startup and the existing 15-second native Git operation boundary; Windows p95 is unmeasured. The bounded collection still owed is one native run of those four arguments. Existing timeouts were not widened.

Evidence root: `/work/worktrees/task-356abafc/.antiphon/debug-356abafc/`: `build.log`, `selected.json`, `runs.jsonl`, `validated-summary.json`, `per-test.csv`, `CP-n/{console.log,results.trx}`, `timing/*.jsonl`, red/green witness logs and `unresolved-classification.log`. Generated evidence stays ignored. The committed table and excerpts preserve essential results if the runner mirror is retired.

Build bytes were hashed before tests and rechecked afterward:

- Antiphon.Tests.dll: `af9d83bc7d8c476529738001ceb4e4f629c75b43ccb7300413e2a7f3aa09379b`.
- Antiphon.HostCleanup.dll: `ab838f5b3dbee953d8f3f3e1cefa5e7c1f8b5e29694a050b21e6b05dee2bac84`.

All 33 `bin-debug356abafc` directories from this build's recorded output inventory are removed at settlement. Other producers' bin-* directories are outside that cleanup.

## Remaining unresolved items

The source evidence is the committed native report on `origin/feat/card-task-fb80be16`, `docs/investigations/2026-10-27-native-rerun-scripts-checkpoint-release-gate.md`. Its historical outcomes/costs remain unchanged. Re-checks here used current source and two additional portable diagnostic controls through a granted build slot; they are separate from the 20 TUnit results.

1. **Staged-copy reader stall: intermittent, initiating phase unresolved.** The initial native failure was `read-stalled: 0 of 69058048 bytes; no progress within 3s`; the staged-copy case passed all three repeats. Current CheckpointFileReader still requests OVERLAPPED with no-follow and sequential flags (`:88`), starts the stall window before open/inspection (`:71`) and applies the read deadline (`:104`). The six portable mode controls pass. Neither these controls nor the historical direct reads identify which open/inspection/read/scheduling phase consumed the native window. No AV, ACL or intrinsic file-size cause is established. No reader fix or timeout change was guessed.
2. **Checkpoint inventory: confirmed diagnostic masking; initiating native exception unresolved.** `scripts/lib/checkpoint-owned-output.ps1:31` replaces a read exception with `output-inventory-failed`. An injected known IOException through the actual seal helper confirms the masking, but does not identify the original Windows exception/path. The native report's repeated zero-test failures and 203-entry inventory remain evidence, not a size/reparse/AV diagnosis. Capture the first failing path and exception inside the native seal before designing a causal repair.
3. **Output pipes: execution/drain timeout with ambiguous root status; handle owner unresolved.** The native `C1313_BaselineAuthority` timeout reported `completion root=True stdout=False stderr=False` after 12 assertions. Contrary to interpreting root=True as successful root exit, `ScriptHarnessProcess.cs:88` prints Task.IsCompleted. `WindowsScriptHarnessProcess.cs:217` can complete that task by cancellation. A portable replay of the exact wait method, with a never-signaled process and canceled token, proves that ambiguity. Its unedited control output is:

```text
PIPE_CLASSIFICATION root=True canceled=True successful=False processSignaled=False: root=True alone does not prove process exit
INVENTORY_CLASSIFICATION forced IOException(forced-inventory-read) => output-inventory-failed: underlying native failure remains unknown
```

The Linux baseline case passes, but neither historical root exit nor a retained descendant is proven by that boolean alone. Native task status/exit code, process/job roster and pipe-handle ownership before cleanup are still needed. No speculative process or pipe fix was applied.

## Native reruns still owed and handoff

Commission native Windows runs on a current build of this branch with an installed PowerShell executable, through the build-slot gate. The task did not operate the desktop. Required focused rows:

- All four arguments of ScriptsLaneRepairTests.Portable_scripts_lane_witness.
- RunStateStoreTests.readers_never_see_a_partial_file and CheckpointStatePublicationTests.state_publication_survives_contention_and_preserves_completed_rows (including its real native contention subcase).
- WorktreeRemovalAuthorityTests.C448_V35_BuildJunkScriptPreservesOpaqueWildcardMatches.
- ReleaseGateRunTests.C599_MasterStateIsolation and C599_ParameterHops; ChangeAwareReleaseTests.C1313_ParameterHops; ReleaseGateFollowupTests.C1336_NightlyCandidateIdentity.

Separate unresolved investigation rows remain CheckpointRecoveryWindowsTests.detached_staged_copy_survives_starter_exit, CheckpointRepeatBuildIsolationTests.repeat_two_then_default_then_three_rebuilds_the_same_project, and ChangeAwareReleaseTests.C1313_BaselineAuthority. Preserve native phase/exception/handle evidence before changing their behavior. Existing ledger/missing-receipt fixture failures in the original report were not repaired here.

Product changes require Final Review. Linux acceptance is green; native verification completeness remains open. Publish to both rc/2026-10-27 and master through normal reviewed landing; a master-only publication is not activation. No operational activation is claimed.

## Pushed source slices

- `060c4bec5ec1a7e3e2c9252d06264829800a9771` — initial cleanup/origin red witnesses.
- `498543972fac1e18c8f19c3defbe956c3eca0d6f` — portable witness correction.
- `c91926eb4538cc19f41c04fd233a6b5f97035e20` — product: require cleanup custody.
- `54841da8d9c1737c1cb0107731e3d21b70569a8f` — product: Git config decoding.
- `556175752f7726ca01fca605aa34b0cce93afb18` — product: isolate local ownership read from execution seams.
- `216a3e0372ee18a08e9397307784d2bf7f704bd3` — state contention red witness.
- `5d0844660b7e94968a6f2b3e208e13371257485b` — product: Windows replacement primitive.
- `dbca0041f7f91773838a8c6cc4ab434b608ec7a6` — fixture registration and reader finally/join.

--- next stage ---
next: review
handoff: Final Review of the three product repairs and shared Windows Move callers; retain the native gate until the focused Windows rows run, and route the three unresolved mechanisms to evidence-led Debug work.
artifact: docs/investigations/2026-10-27-scripts-lane-product-repairs.md
