DONE — integrated and pushed all four branches; four full-solution builds passed. The Linux union has a confirmed inherited Windows-only refusal and one transient cleanup failure, with the exact cleanup case subsequently passing.

Assigned branch: feat/card-task-2cfa8643. Verified executable/test source: d92454cff84d30729df8a92e0cbf5460d2e7442d. The completion receipt supplies the subsequent report-only tip. Initial fetched origin/master and required fast-forward-only base: 3d9d2567609f5f901f7c149d68b63aae3ba8d181. The branch is a clean, linear descendant of that base. No target merge, rebase, amend, reset or force push occurred.

Imported branch tips, in commissioned order:

- af2b78c8: c291efde7ffaefedecc049095dec52f480666d5c.
- ab1cd00f: 7b13239a869cad38f6b7a4a7b16ea8f25a71e987.
- c643f462: 23c3cd2ac394681d92c979dde44f2a947c9d0be2.
- bf7efe06: 479fe008b333977c7a737bc3120a80c51f8b1e4f.

All 32 nonempty imported commits were pushed individually. No textual conflict or compiler error occurred, so there is no compiler diagnostic to attribute to an imported branch. Four already-applied duplicates produced the exact Git diagnostic `The previous cherry-pick is now empty, possibly due to conflict resolution.` and were skipped:

- c643f462: c921867bd6a165b58aa4e39d5ac7e15b39d4afec (C578 witness) and 70b510a1bbd56cad38565f7abfc50c1a13561316 (unique C578 output). Their canonical copies came from af2b78c8; its additional runtime/output diagnostics remain.
- bf7efe06: e1585adc9a7df0658cae5fdd9425d62b8a706d2f (C487 cleanup) and 448a1e5e753c41928a8059de656bcce5b5ffc595 (cleanup witness). Their canonical copies came from c643f462.

Both complementary cleanup layers remain: C487 clears ReadOnly on owned nonlinked files during completion; ScriptHarnessProcess uses the existing link-aware StartRefGit deletion after joining, and still throws if results remain. Supplemental, deletion and diff fault-cut assertions remain; retirement host-case splitting preserves all assertions and changes the class count from 13 to 16. CARD-1577 pricing was not changed. No additional source repair was necessary.

One isolated whole-solution Debug build ran after each branch. Round two freshly compiled ab1cd00f's final admission/cleanup edits, and round four freshly compiled bf7efe06's final C# admission edits. All used scripts/build-slot.ps1, the same task-owned forward-slash OutputPath, and UseAppHost=false on Linux:

    pwsh -NoProfile -File scripts/build-slot.ps1 -Label integration-2cfa8643-round4 -- dotnet build --configuration Debug --property:OutputPath=bin-integration-2cfa8643/ --property:UseAppHost=false --nologo -v:q

| Round | Built source | Warnings / errors | MSBuild elapsed |
|---|---|---:|---:|
| 1 | 87999292deb2a543e2bced3dc1169b887bda0ff6 | 1250 / 0 | 00:03:44.84 |
| 2 | 73a853be755bcc675ef67ecdd8f2c8ab2a41d091 | 1250 / 0 | 00:03:31.01 |
| 3 | a5aaba12e0dfd9fc901b9d3cad79921e4efca534 | 1250 / 0 | 00:03:34.19 |
| 4 | d92454cff84d30729df8a92e0cbf5460d2e7442d | 1250 / 0 | 00:03:32.33 |

Unedited build-slot provenance:

```text
BUILD SLOT granted lease=8e220ade-48f1-4123-a7a7-69e409a4ee2a waited=0s maxcpucount=6
BUILD SLOT released lease=8e220ade-48f1-4123-a7a7-69e409a4ee2a held=226s
BUILD SLOT granted lease=f77cb046-c7b0-46bd-b8c0-f6b03d93cd8d waited=0s maxcpucount=6
BUILD SLOT released lease=f77cb046-c7b0-46bd-b8c0-f6b03d93cd8d held=212s
BUILD SLOT granted lease=3b7da43c-8e93-4f14-b412-fba8e16e5167 waited=0s maxcpucount=6
BUILD SLOT released lease=3b7da43c-8e93-4f14-b412-fba8e16e5167 held=215s
BUILD SLOT granted lease=d70b9f32-18ce-4a71-bae0-73808d4c3666 waited=0s maxcpucount=6
BUILD SLOT released lease=d70b9f32-18ce-4a71-bae0-73808d4c3666 held=213s
```

The deduplicated union ran once as four exact class-OR selections, with a trailing wildcard on every operand, in two supervised local lanes. Every driver acquired its own host slot; each assembly retained ProcessSpawnLimit. Selected fixtures use per-invocation temporary repositories/results/Docker resources and no shared TestDbFixture or real Program host. No source was edited during any test run. Direct compiled-DLL execution avoided the commissioned checkpoint reader's >64 MiB DLL problem. DLL SHA-256 before/after execution: 320783a0f319432b86c23df467c5bf23da0a9936cd0bb37e6205a2896b673b5b. No outer CHECKPOINT certificate or reviewedSourceClean claim is made.

| Group | Driver exit | Driver wall seconds |
|---|---:|---:|
| groups72-73 | 2 | 912.572 |
| groups74-75 | 0 | 1066.002 |
| group76 | 0 | 1289.083 |
| group77 | 2 | 1606.461 |

Validated TRX definitions/results and the independent class census: 23 classes, exactly 201 cases, 196 passed, 2 failed, 3 skipped. No unexpected class or zero selection received credit. These original outcomes are preserved:

| Class | Passed | Failed | Skipped | Sum of body seconds |
|---|---:|---:|---:|---:|
| C1008PlatformContractTests | 4 | 0 | 2 | 2.953 |
| CardRelationshipScriptTests | 7 | 0 | 0 | 12.383 |
| ChangeAwareReleaseTests | 3 | 0 | 0 | 520.077 |
| EvidenceApprovedFixtureGuardTests | 7 | 0 | 0 | 76.841 |
| EvidenceDeletionGuardTests | 6 | 0 | 0 | 192.816 |
| EvidenceDiffGuardTests | 19 | 0 | 0 | 105.362 |
| EvidenceSupplementalDeletionGuardTests | 10 | 0 | 0 | 52.226 |
| MechanicalPcRehearsalTests | 10 | 0 | 0 | 85.946 |
| NightlyProbeSupervisorTests | 1 | 0 | 0 | 3.689 |
| RcProjectSelectionTests | 4 | 0 | 0 | 35.196 |
| RcQualificationStatusTests | 2 | 1 | 0 | 9.422 |
| ReleaseGateFollowupTests | 2 | 0 | 0 | 159.721 |
| ReleaseGateRunTests | 8 | 0 | 1 | 110.376 |
| ReleaseTagAllocatorTests | 7 | 0 | 0 | 984.647 |
| RetiredTempContainerDockerTests | 1 | 0 | 0 | 255.068 |
| RetiredTempContainerHostTests | 21 | 0 | 0 | 740.738 |
| RetiredTempContainerScriptTests | 16 | 0 | 0 | 178.859 |
| RollingProductionMountTests | 12 | 0 | 0 | 51.033 |
| RollingVolumeRecycleDockerTests | 1 | 0 | 0 | 653.219 |
| RunCheckpointRepeatScriptTests | 4 | 0 | 0 | 51.361 |
| RunCheckpointScriptTests | 25 | 1 | 0 | 482.571 |
| ScriptHarnessCancellationTests | 5 | 0 | 0 | 37.796 |
| ScriptHarnessLinuxOwnershipTests | 21 | 0 | 0 | 52.312 |

The two failures and their attribution:

1. RcQualificationStatusTests.C1500_WindowsSharedReadAndPs51: `Native Windows fixture required; never a passing skip.` Confirmed at the exact initial master in a detached checkout: fresh isolated base project build passed, then the exact method executed 1 case and failed with the same refusal. This remains a failed Linux case, not a passing skip or Windows evidence. Per the brief, it does not block this integration handoff.

2. RunCheckpointScriptTests.C589_SlotTimeout: all five actual fixture assertions passed, followed by `cleanup: death confirmation: No such process : '/proc/1146/stat'`. The failing results/control paths remain retained by the fixture: /tmp/c589-nightly-8846c6a13ade48f89012532c72aa27a6 and /tmp/antiphon-script-control-8846c6a13ade48f89012532c72aa27a6. Exact native method reruns at master and the unchanged integrated tip each passed 1/1. The spontaneous race did not reproduce in the base exact-method run. Independently, an isolated assembly-load witness invoked each actual compiled LinuxScriptHarnessProcess.ConfirmDeadAsync using its existing IdentityReader fault seam and an owned, joined /bin/true supervisor: FileNotFoundException and DirectoryNotFoundException controls completed, while the same ESRCH-shaped IOException escaped in both binaries. The Linux owner source blob is identical at base and tip: 2c5ca623fd1c89e71db02853e37ef13459ff68b9. This confirms inherited exception-handling behavior; injected error evidence is distinct from a spontaneous base native failure. No cleanup, assertion, timeout or retry policy was changed. The original union is not relabeled all-green.

The attribution-only base project build used OutputPath=bin-integration-base-2cfa8643/, through the host gate: 1196 warnings, 0 errors, 00:03:46.01. Base DLL SHA-256 and the 33 exact output roots are retained in base-provenance.json. This extra build was needed to attribute verification failures; it is separate from the commissioned four integration rounds. The detached base and all its outputs were removed after its children exited.

Preserved uncredited attempts: the first combined base method selector admitted the full checkpoint class; I sent SIGINT to its verified owned test-host PID and joined it through slot release (aborted, 6 completed passes, 162s held), then replaced it with three separate exact-method runs. Two initial owner-witness setup attempts failed during module initialization because PowerShell had an incompatible System.Text.Json assembly loaded; they are setup failures, not assertion reds. An isolated AssemblyLoadContext corrected the witness. All attempt logs remain stored.

Both real Docker fixtures passed checked resource cleanup: ten retired-container outcomes and 34 rolling-volume outcomes, with 477 and 1665 recorded CLI timings respectively. Their original evidence JSON files were copied and byte-verified into this task's evidence root. The longest class group was 26m43.655s; all four groups remained below 30 minutes. Concurrent union wall time is distinct from summed body/CLI spans. Per-case TRX durations, phase JSONL, source/group identity, load samples and unavailable phase attribution are retained. Uninstrumented external spans remain unknown/remainder, not zero. These are single observations, not measured p95.

Windows static inspection covered changed OS branches, platform gates, project/TFM conditions, linked-source dependencies, namespaces/APIs, duplicate members and line endings. All 27 changed C#/PowerShell source files are ASCII with LF working bytes; no project file, Windows-only preprocessor path or Windows target framework changed. The changed C# uses the common net9.0 assembly, including OperatingSystem-dependent bodies compiled on Linux. No unresolved compile blocker was found. Existing Windows-only ownership implementation and the integration-1 LifecycleHost dependency repairs remain intact.

Windows execution is still owed. The original four tracked task reports retain their complete native rows, witnesses and prerequisites. In particular: use a fresh native build/output and normal apphost staging; verify actual Git Bash resolution, git.exe NTFS vectors, read-only cleanup and PowerShell 5.1 shared-read behavior. C1050 native child audits require Antiphon.Tests.exe and a trusted checkpoint-build-source.json; an ordinary build alone is not evidence of that stamp. Explicit Linux-placement skips do not qualify Windows Docker/process behavior. PS7/string/injected-error checks do not substitute for native receipts. The original desktop group failures were not read here. Live Windows qualification db2c2088, its workspace, database, lock and build slots were untouched. No native tests, category-wide Unit lane, Review, Git/release tags, rc pick, deployment or activation was performed.

Latest fetched origin/master advanced during verification to d78b580951d34bb6907d92cfbbba695553e4033c with three unrelated split-8 recovery-budget commits. It is not contained in this task branch; the clean-descendant statement applies to the initially fetched base above. Read-only git merge-tree --write-tree origin/master HEAD exits 0, tree e0261f54b217042d8a0e96578a6c8b0181620ee7. No target changes were merged or rebased into the task. The normal landing rebase remains required. Source-tip evidence guard passed: commits=32, entries=8, violations=0. Final report-only guard, ancestry, merge-tree and remote-tip receipts accompany settlement.

Generated evidence is ignored at /work/worktrees/task-2cfa8643/.antiphon/evidence-2cfa8643/: build/slot logs, groups-planned.json/groups-executed.json, all original and exact-method TRXs, case-results.json, case-durations.csv, class-summary.json, phase sidecars, copied Docker ledgers, static-source-final.json, source/assembly witness logs, load samples, inventories and cleanup records. Essential source and build provenance is recorded above. All owned commands were awaited. Cleanup removed 49 integration output roots and 36 source-bound checkpoint output roots; remaining=0, active matching task build/test executors=0, final DLL digest matched before removal. Tracked source was clean before this report-only publication.

After a fresh isolated build, the relevant exact reruns use the same host gate and compiled DLL with fresh TRX/results directories:

    pwsh -NoProfile -File scripts/build-slot.ps1 -Label integration-exact -- dotnet exec tests/Antiphon.Tests/bin-integration-2cfa8643/Antiphon.Tests.dll --treenode-filter '/*/*/RunCheckpointScriptTests/C589_SlotTimeout' --report-trx --report-trx-filename run.trx --results-directory .antiphon/integration-exact-fresh

Use /*/*/RcQualificationStatusTests/C1500_WindowsSharedReadAndPs51 for the native Windows case. Exact union selectors/arguments are retained in groups-executed.json; no broad suite rerun is needed for this report-only commit.

--- next stage ---
next: land
handoff: Land the pushed integration owner under the commissioned fixture-only waiver, using its exact reported SHA and normal rebase onto newer master. Preserve both original Linux failures, exact rerun attribution and all pending native qualification rows.
artifact: /work/worktrees/task-2cfa8643/.antiphon/task-2cfa8643.md
