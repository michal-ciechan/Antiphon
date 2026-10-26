Pushed fixture repairs with Windows-input witnesses. Linux baseline: 10 passed, one intentional Windows-only refusal. Focused script checks pass; native Windows group 77 remains unqualified.

Assigned branch: feat/card-task-bf7efe06. Starting and latest fetched master: 9a3e6c7a53d2e98fd8582dd7d0c0ef1cb4be87a9. Final executable/witness source: 448a1e5e753c41928a8059de656bcce5b5ffc595. Subsequent publication changes only this report. The live desktop qualification db2c2088, candidate a13e544e, its group-77 TRX, outputs, native lock and build slots were never accessed or changed.

- NightlyProbeSupervisorTests now uses the existing NativeLinuxGate instead of throwing PlatformNotSupportedException on Windows. The two real Docker methods call that same gate before constructing ProcessStartInfo. Their fixtures require Linux process groups, POSIX paths and an owned in-container Docker daemon; c1008-recycle-real-cases.mjs explicitly refuses other daemon placement before allocating its root. Windows Docker Desktop path translation is therefore outside these fixtures' admitted execution. Linux retains all original assertions, including ten retired-container and 34 rolling-volume outcomes. Windows should report three explicit placement skips, not three failures or native Linux coverage.
- The C1336 wrapper fixture now selects Git Bash from the discovered Git installation on Windows, checking usr/bin/bash.exe and bin/bash.exe. Previously whichever bash appeared first on PATH could select the WSL launcher for Windows-owned script/capture paths. Missing Git Bash now gives an explicit prerequisite refusal. Linux discovery is unchanged. A forced Windows path with spaces and WSL first reproduces the old selection defect on Linux; it does not establish the original desktop's actual PATH.
- Reused sibling c643f462's exact shared C487 repair, original fa8427b7e4a3949d4f9f5f0e83046cca010bc7f7, as e1585adc9a7df0658cae5fdd9425d62b8a706d2f. Completion clears ReadOnly only on files below its owned results root, skips file/directory/root reparse points, and leaves deletion to the parent after join. This addresses Git objects left by the candidate-identity fixture. The current sibling source-driven witness was reused unchanged. A final direct candidate fixture run exercised this integrated completion after all 25 assertions and reported 170 normalized files.
- Added diagnostics for the unqualified native release/status cases: C1313 reports actual/expected bindings and capability refusal results; C1336 reports runtime, shell and child bindings; C1500 reports outer/inner versions, child exit, read/owner statuses and identity mismatches on failure. Conditions and inventories are unchanged. No native-only product fix was invented from Linux greens.

All four sibling reports were read, including c643f462's report when it became available. af2b78c8's admission fixes, ab1cd00f's stronger parent cleanup and 10421b0e's LF fixture fix remain on their own branches; their classes were untouched. The shared C487 repair is included here, so this branch does not require a particular sibling landing order for normal successful completion. ab1cd00f still supplies complementary parent cleanup for exceptions before completion. No production source, timeout, retry, test method inventory, nightly-class-costs.json, live release/tag/deployment action, Unit lane or Review was changed/run. Fixture-only direct landing was explicitly commissioned.

Exactly one isolated project build ran through scripts/build-slot.ps1, with OutputPath=bin-bf7efe06/ and UseAppHost=false: zero errors, 1197 warnings, 239.71 seconds. C# inputs were the original base; only narrative and independent standalone witness files were added while it built. All TUnit invocations reused that DLL with --no-build. SHA-256 before/after execution and before output deletion:

    55053bbb7afde2ae0476983fc402be92984c806134a0f1fd6cc87a979ced3e88

Unedited build/baseline receipts:

    BUILD SLOT granted lease=4455fca3-1a5f-49eb-b8ea-0617a43490d6 waited=0s maxcpucount=6
    BUILD SLOT released lease=4455fca3-1a5f-49eb-b8ea-0617a43490d6 held=240s
    BUILD SLOT granted lease=d4809b8b-b591-41c8-8894-a025e4d01993 waited=0s maxcpucount=6
    Test run summary: Failed! - /work/worktrees/task-bf7efe06/tests/Antiphon.Tests/bin-bf7efe06/Antiphon.Tests.dll (net9.0|x64)
      total: 11
      failed: 1
      succeeded: 10
      skipped: 0
      duration: 24m 36s 532ms
    BUILD SLOT released lease=d4809b8b-b591-41c8-8894-a025e4d01993 held=1480s

The exact baseline filter was:

    /*/*/(ReleaseGateFollowupTests*)|(ChangeAwareReleaseTests*)|(NightlyProbeSupervisorTests*)|(RcQualificationStatusTests*)|(RetiredTempContainerDockerTests*)|(RollingVolumeRecycleDockerTests*)/*

It ran at f6da43a05f97b6c8e52ad47068bca9a97c1e6c4f, whose assigned C#/runtime fixture inputs were identical to base. Fresh TRX definitions/results were joined and checked for all six classes and 11 results.

| Class | Passed / failed / skipped | Summed body seconds |
|---|---:|---:|
| ReleaseGateFollowupTests | 2 / 0 / 0 | 132.390 |
| ChangeAwareReleaseTests | 3 / 0 / 0 | 438.857 |
| NightlyProbeSupervisorTests | 1 / 0 / 0 | 4.055 |
| RcQualificationStatusTests | 2 / 1 / 0 | 7.915 |
| RetiredTempContainerDockerTests | 1 / 0 / 0 | 244.984 |
| RollingVolumeRecycleDockerTests | 1 / 0 / 0 | 645.984 |

The sole baseline failure is C1500_WindowsSharedReadAndPs51: `Native Windows fixture required; never a passing skip.` This is an inherited Linux platform refusal from unchanged base, retained as failed, not evidence of the original Windows cause. No full-selection green is claimed.

Focused results and source bindings:

- At c3b09f04bcbd0e9c994737c972c90110fda7b719, the admission witness compiles each actual method prefix plus NativeLinuxGate, substitutes a nonconstant Windows/Linux decision, and uses an explicit stand-in for the TUnit skip exception. Six decisions pass. Before repair, both Docker methods reach native setup and the supervisor throws a failure exception. No native TUnit skip receipt is claimed.
- At 4cf94681334ca4af07cc02fef28f2e609c92463b, the shell witness executes the actual selection assignment/helper against Windows strings: usr/bin selection, bin fallback, missing-shell refusal and Linux discovery all pass. The committed pre-fix witness selected C:\Windows\System32\bash.exe and failed its expected Git Bash assertion.
- At 4fa76cb5e62e55d82d4346175570292b9a38f77f, C1336_WrapperRef passed 1/1 with nine fixture assertions (2.745s host, 1.645s body); C1313_ParameterHops passed 1/1 with ten fixture assertions (155.102s host, 153.992s body). Both used separate exact-method filters, fresh TRXs, the one compiled DLL and updated runtime scripts. Their leases held 6s and 158s.
- The reused C487 source witness passes on 448a1e5e753c41928a8059de656bcce5b5ffc595: three read-only Git objects normalized, outside attributes and all link boundaries preserved, no deletion before owner join, original row inventory unchanged. Against exact base via -SourceRef it fails at Windows read-only deletion after the original passing completion row. Both outcomes are retained.
- After integrating that helper, the direct C1336_NightlyCandidateIdentity fixture passed all 25 assertions at 448a1e5e753c41928a8059de656bcce5b5ffc595 through the build-slot gate (142s lease). This extra narrow run verifies the newly reused runtime completion, using inert workload seams and real owned Git fixtures; it is not another project build or native TUnit qualification.
- Six changed/reused PowerShell fixtures/witnesses parse with PS7 and remain ASCII. PS7 parsing is not Windows PowerShell 5.1 parsing evidence. The first admission witness attempt produced CS0162 from a constant forced branch; its log is retained as setup error, not assertion-red evidence. The committed correction uses a nonconstant field and produced the intended reds.

The final C# admission edits were not rebuilt into TUnit under the one-build budget. They were compiled/exercised through the source-bound prefix/gate witness. The native owner must freshly compile them. Runtime script inputs and the three admission files are unchanged since their named successful checks except for the separately witnessed shared completion integration above. No CHECKPOINT certificate or reviewedSourceClean assertion is claimed; the brief commissioned direct execution because of the test DLL reader limit.

Host: server2/Linux, Debian 12, SDK 10.0.402, .NET runtime 9.0.21, TUnit 1.44.0.0. The longest selection was the 24m36s baseline, below 30 minutes. TRXs and opt-in phase JSONL retain membership and per-case durations. Docker ledgers retain 477 and 1662 CLI timings and checked absence of their owned containers, volumes, networks and images. Other nested CLI/filesystem spans remain unknown/remainder. These are single-run observations, not measured native p95 or cost-pricing evidence. Existing deadlines were unchanged.

Native obligations remain open. Read antiphon-all-group-77.trx from db2c2088 before attributing its exact seven failures. On the caller's next integrated candidate, use a fresh isolated native build/output and evidence directory, never db2c2088's live workspace, lock or slots:

    pwsh -NoProfile -File scripts/build-slot.ps1 -Label group77-native-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-group77-native/ --nologo

For each table row, run dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-group77-native/ through that host's build-slot.ps1, with `--treenode-filter '/*/*/<ClassName>/*'`, a fresh --report-trx/--report-trx-filename and --results-directory. Preserve normal native apphost staging; Linux's UseAppHost=false is not a native instruction.

| Native row | Expected parent outcomes, pending execution |
|---|---|
| ReleaseGateFollowupTests | 2 passes; installed Git Bash beside Git; verify actual shell/capture paths and candidate cleanup |
| ChangeAwareReleaseTests | 3 passes; inspect new binding/capability diagnostics if the original native failure persists |
| RcQualificationStatusTests | 3 passes; actual Windows PowerShell 5.1 and installed pwsh 7 shared-read observations |
| NightlyProbeSupervisorTests | 1 explicit native-Linux placement skip |
| RetiredTempContainerDockerTests | 1 explicit owned Linux-daemon placement skip |
| RollingVolumeRecycleDockerTests | 1 explicit owned Linux-daemon placement skip |

Expected total is eight Windows executions plus three placement skips. Those skips do not provide Windows Docker/process-tree coverage. Repeat source witnesses through build-slot.ps1 with pwsh -NoProfile -File scripts/fixtures/group77-windows-input-witness.ps1 -Case admission or -Case bash, and scripts/fixtures/c487-readonly-windows-witness.ps1. The original native causes remain unresolved until fresh receipts exist; this task does not qualify, release or activate the rc candidate.

Generated evidence: /work/worktrees/task-bf7efe06/.antiphon/evidence-bf7efe06/. It contains build/driver logs, baseline/wrapper/hops TRXs and joined rows, phase sidecars, both Docker ledgers, source-witness red/green/setup-error logs, final candidate log/results, syntax, output inventory, DLL digest, lease and cleanup records. All owned commands were awaited. All 33 inventoried bin-bf7efe06 roots were removed after the final DLL digest check. Direct fixture results remain inside that ignored evidence root. No owned test/build invocation remains; lease grants/releases are audited. Generated payloads remain ignored.

Evidence history guards pass with zero violations. Fetched origin/master remains 9a3e6c7a53d2e98fd8582dd7d0c0ef1cb4be87a9; source merge-tree is conflict-free, tree 7205ea7acb4c567be30a5f0a29c6a0027004f824. Final report-only guard/merge-tree/remote-tip receipts are recorded separately. No branch switch, rebase, amend, reset, force push or target merge occurred. Every meaningful slice was committed and immediately pushed:

    fdbc9c37f549d37563958410611a6855b478353e baseline report
    0c779f39ba8bdbe3a7ec1a5388ca9f5919e27e30 initial Windows-input witnesses
    f6da43a05f97b6c8e52ad47068bca9a97c1e6c4f witness compilation correction / baseline run
    c3b09f04bcbd0e9c994737c972c90110fda7b719 Linux fixture admission
    4cf94681334ca4af07cc02fef28f2e609c92463b Windows Git Bash selection
    4fa76cb5e62e55d82d4346175570292b9a38f77f native diagnostics
    e1585adc9a7df0658cae5fdd9425d62b8a706d2f reused exact C487 repair from c643f462
    448a1e5e753c41928a8059de656bcce5b5ffc595 reused C487 witness / final executable source

--- next stage ---
next: land
handoff: Land the pushed fixture/witness branch under the commissioned Review waiver; coordinate the identical shared C487 patch with c643f462, and retain all six native rows as incomplete qualification.
artifact: /work/worktrees/task-bf7efe06/.antiphon/task-bf7efe06.md
