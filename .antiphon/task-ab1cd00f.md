Pushed fixture repairs with five inherited Windows source/string reds turned green. Linux baseline: 69 passed, 0 failed, 0 skipped. Native Windows qualification remains pending.

## Changes and attribution

Assigned branch: feat/card-task-ab1cd00f. Fetched starting master/base: 9a3e6c7a53d2e98fd8582dd7d0c0ef1cb4be87a9. Final executable/source-witness tip: b79cd5e7fbfe90cec96d6ba3b62f581eb374e1cb. This report is the only subsequent delta.

- RetiredTempContainerHostTests and RollingProductionMountTests now call the existing C1008HostFixture.RequireNativeLinux from Before(Test), before any host work. The first class had 20 unguarded cases and one guarded case; the second had five unguarded cases and seven guarded cases. Their Bash/flock, POSIX metadata, /tmp and process-group fixtures belong on native Linux. No Docker Desktop drive-path normalization or production deployment behavior was changed. Every assertion and all 33 methods remain.
- EvidenceGitFixture sets core.protectNTFS=false only in its disposable, object-only repository. Deliberately invalid control-character paths must reach the policy under test; these trees are never checked out. This is relevant to both evidence classes, including the approved-fixture trailing-newline near miss. Global/system Git configuration is untouched.
- EvidenceDiffGuardTests normalizes its newly created loose object's attributes before deliberately deleting it. The missing-object assertion is unchanged. Its injection canary now uses a repository-relative leaf and asserts the corresponding owned repository file is absent; the literal PowerShell injection and newline/case/payload assertions remain.
- ScriptHarnessProcess reuses the existing link-aware StartRefGit.DeleteDirectory for owned results, so Windows read-only Git objects do not turn successful mechanical rehearsals into teardown failures. A remaining results directory is still an IOException; the helper's best-effort deletion cannot silently pass. Death confirmation, stream draining, lifecycle budgets, control-directory cleanup and primary-error propagation retain their existing contracts.
- scripts/fixtures/script-groups-windows-witness.ps1 provides the five source/string checks, an optional pinned SourceRef, and a read-only TRX diagnostic reader. scripts/fixtures/script-harness-cleanup-flow-witness.ps1 compiles the complete current helper with an explicit fake owner and its actual deletion helper; only the unused native-owner factory is cut.

The counts match the reported Windows failure pattern: 25 missing-admission cases; eight mechanical cases that create Git repositories, versus two oracle cases without Git objects; and the evidence path/blob cuts. This is an inference, not attribution from the original Windows TRX: those exact native messages were unavailable here. The native reader and rerun rows below retain that obligation. No product source, timeout, retry, test inventory, assertion or nightly-class-costs.json changed. No Unit lane, Review, tag, release script, deployment, desktop connection, native qualification lock or desktop build slot was used. Fixture-only direct land was explicitly commissioned.

The b6d3db3b/65c18d93 reports were read first. The commissioned .antiphon/task-ad00e376.md is absent on its fetched branch; its unrelated db4f734b report is not substituted as mechanical evidence. Sibling af2b78c8's current admission/recovery-object/checkpoint-output repairs and 10421b0e's report/LF repair were read. Their classes/files are untouched. NativeLinuxGate and StartRefGit are reused unchanged.

Git's Windows path check in [Git v2.51.0 mingw.c](https://github.com/git/git/blob/v2.51.0/compat/mingw.c) rejects control characters and embedded colons with core.protectNTFS enabled; [verify_path_internal](https://github.com/git/git/blob/v2.51.0/read-cache.c) applies that check to index entries. The NTFS witness models only the relevant character subset and compiles the actual factory's local configuration statements. It is not execution of Windows git.exe. A preliminary Linux Git trial accepted the same paths even with protectNTFS=true, confirming that flag alone does not simulate the Windows compile-time implementation on Linux.

## Verification and costs

Host: server2/Linux, Debian 12, SDK 10.0.402, .NET runtime 9.0.21, TUnit 1.44.0.0. One isolated project build through scripts/build-slot.ps1, OutputPath=bin-ab1cd00f/, UseAppHost=false: 0 errors, 1196 warnings, 224.41 seconds. Compiled C# and runtime script inputs were identical to the starting base. Only this report and an independent witness were added while that build ran. No fixture source was edited during the long baseline test run.

Unedited receipt lines:

    BUILD SLOT granted lease=9dbdcdaf-8521-4dd9-b635-38830758bc96 waited=0s maxcpucount=6
    BUILD SLOT released lease=9dbdcdaf-8521-4dd9-b635-38830758bc96 held=225s
    BUILD SLOT granted lease=a20b250e-49e9-438e-9139-cbb2154f8e81 waited=0s maxcpucount=6
    Test run summary: Passed! - /work/worktrees/task-ab1cd00f/tests/Antiphon.Tests/bin-ab1cd00f/Antiphon.Tests.dll (net9.0|x64)
      total: 69
      failed: 0
      succeeded: 69
      skipped: 0
      duration: 20m 54s 434ms
    BUILD SLOT released lease=a20b250e-49e9-438e-9139-cbb2154f8e81 held=1257s

The first all-bare OR filter selected zero tests and exited 8. Its log/TRX are retained without execution credit. The corrected documented starred-operand filter selected exactly the five intended classes; fresh TRX definitions/results were joined by testId and checked for all 69 passes.

| Exact class | Baseline passed/failed/skipped | Summed test-body seconds |
|---|---:|---:|
| EvidenceDiffGuardTests | 19/0/0 | 116.538 |
| EvidenceApprovedFixtureGuardTests | 7/0/0 | 100.854 |
| MechanicalPcRehearsalTests | 10/0/0 | 67.210 |
| RetiredTempContainerHostTests | 21/0/0 | 888.523 |
| RollingProductionMountTests | 12/0/0 | 78.559 |

Baseline DLL SHA-256: ab50339272df9fb8b615906a10fcb93146b37c4e90cf1733d8db7820382750fc. It was unchanged after execution and before cleanup. The baseline is not a rebuilt final-tip TUnit assembly. The brief limited this task to one project build; final fixture code was checked through the source-bound witnesses below. Fresh native compilation and execution are still owed. No CHECKPOINT receipt or reviewedSourceClean assertion is claimed; the commissioned direct --no-build fallback was used for the >64 MiB DLL.

Red/green evidence:

1. Witness commit 2f04ece4ea07b0657066249f04cc510ad6d9ed91 recorded the original four reds before repair. b3db8c2276e4ade49b2f3f80390b5e75aaf786ca added the NTFS red. Each was separately leased, compiled successfully where applicable, and exited 1 at its intended assertion/Windows-emulated behavior.
2. The final witness, pinned to exact base 9a3e6c7a53d2e98fd8582dd7d0c0ef1cb4be87a9 with -SourceRef, still yields five intended exit-1 reds and no C# compilation errors. This confirms inherited source, without switching branches, editing a snapshot or rebuilding.
3. Admission green at 9a55b039c3f677a11f1b158d2d21f98f489d371c: both Before(Test) hooks connected; actual NativeLinuxGate body admits forced Linux and refuses forced Windows with its reason (skip exception mapped to PlatformNotSupportedException). This is source/decision evidence, not a native TUnit skip receipt.
4. Blob, literal-path and NTFS greens at fd92dc704ee5729d6c0e8fcb55fc7e99fad6fcee: read-only object normalized before deletion; exact relative injection preserved without drive-qualified Git path; actual local configuration admits the deliberate Windows-invalid object vectors. Each exited 0.
5. Cleanup green at 1dca9e5d909e8241ab2b4158509e2cae5e9d302a: actual cleanup block and unchanged deletion helper execute against an in-memory Windows read-only filesystem, preserving exact paths. Exit 0.
6. Full helper-source flow at 1d93984173a745f446c4bf0cf3e4ae160e421a19: three cases pass (ordinary, uncertain death, primary error). It checks actual owned read-only files, a link to an independently owned foreign sentinel whose bytes/attributes remain unchanged, deletion versus retention, lifecycle completion and exception identity. No native owner is launched. -OmitDeletion changes only the compiled disposable copy; it exits 1 because ordinary cleanup refuses retained results. The first standalone compile omitted nullable context (CS8632); that setup error remains in cleanup-flow.log and is not red evidence. The corrected compile and behavior evidence is cleanup-flow-compiled.log.
7. The diagnostic reader parsed the real 69-result baseline, a clearly synthetic one-failure Windows-path/stack fixture with sentinel redaction, and refused an empty synthetic TRX (exit 1). Synthetic artifacts give no native test credit.

All relevant witness inputs remain unchanged after their named repair commits. Standalone Add-Type compilation is not a second project build. Historical zero-selection/setup failures and negative controls are retained. No timeout or assertion was relaxed. These are single-run Linux costs, not a measured p95. External-call phase attribution beyond per-test/driver durations remains unknown/remainder. Pricing is left to CARD-1577.

## Rerun commands and native obligation

The baseline command reused the single build:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label ab1cd00f-baseline-exact -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-ab1cd00f/ --property:UseAppHost=false -- --treenode-filter '/*/*/(EvidenceDiffGuardTests*)|(EvidenceApprovedFixtureGuardTests*)|(RetiredTempContainerHostTests*)|(RollingProductionMountTests*)|(MechanicalPcRehearsalTests*)/*' --report-trx --report-trx-filename baseline.trx --results-directory .antiphon/evidence-ab1cd00f/baseline-exact
```

For each of admission, blob, literal-path, cleanup, ntfs, run separately:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label script-windows-input -- pwsh -NoProfile -File scripts/fixtures/script-groups-windows-witness.ps1 -Case <case>
```

Expected current source: exit 0. Add -SourceRef 9a3e6c7a53d2e98fd8582dd7d0c0ef1cb4be87a9 for the retained base red (exit 1). Run the full helper-source flow with the same gate and scripts/fixtures/script-harness-cleanup-flow-witness.ps1; expect three passes. -OmitDeletion is the explicit expected-failure control.

The native owner must build the landed pick in a fresh isolated output/check-out and execute separate exact rows with `/*/*/<Class>/*`, fresh TRX and --no-build, through its own build-slot gate. Do not reuse or change db2c2088's live qualification workspace, output or lock. Native builds should stage their normal apphosts; Linux's UseAppHost=false is not a native staging instruction.

| Native class row | Expected disposition, pending execution |
|---|---|
| EvidenceDiffGuardTests | 19 executions, zero failure/skip |
| EvidenceApprovedFixtureGuardTests | 7 executions, zero failure/skip |
| MechanicalPcRehearsalTests | 10 executions, zero failure/skip |
| RetiredTempContainerHostTests | 21 explicit native-Linux placement skips with the existing CARD-1050 reason |
| RollingProductionMountTests | 12 explicit native-Linux placement skips with the existing CARD-1050 reason |
| ScriptHarnessProcessContractTests | 26 executions for the changed shared cleanup helper |

The expected 33 placement skips are not Windows host-contract coverage. Their Linux baseline remains 33/33 executed passes. Admission, git.exe's NTFS behavior and Windows read-only teardown still need native receipts. Read the original group-74/group-75 TRXs first, then the rerun TRXs, with:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label script-native-diagnostics -- pwsh -NoProfile -File scripts/fixtures/script-groups-windows-witness.ps1 -Case diagnostics -TrxPath <existing-or-fresh.trx>
```

The reader prints exact method/display identity, duration, message and stack; it is diagnostic, not a qualification gate. Native qualification remains incomplete until those rows execute. No Windows-green or release/activation claim is made here.

## Publication and cleanup

Evidence root: /work/worktrees/task-ab1cd00f/.antiphon/evidence-ab1cd00f/. It retains build/driver logs, fresh baseline TRX, test-durations.json, class summaries, red/green/negative-control logs, synthetic diagnostic inputs, DLL digest, lease audit and exact output inventory. Generated evidence stays ignored; this individual report and the reusable source witnesses are tracked.

All launched commands were awaited. Before final report publication, 25 grants had 25 matching releases and no missing lease; the final guard has its own release receipt. All 33 producer-inventoried bin-ab1cd00f roots were removed, and the post-cleanup inventory is empty. The final source-check publication did not use those outputs. No task-owned test/script process remains.

At source tip b79cd5e7fbfe90cec96d6ba3b62f581eb374e1cb, check-evidence-diff.ps1 over base..HEAD passed: commits=8 entries=0 violations=0. Fetched origin/master remained 9a3e6c7a53d2e98fd8582dd7d0c0ef1cb4be87a9. git merge-tree --write-tree origin/master HEAD exited 0, tree 24d481df41b5e09cf8423e90c1731c02236e281e. No merge was applied. Final report-only guard/merge-tree receipts are evidence-guard-final.log and merge-tree-final.txt; the completion message supplies the final remote SHA. No rebase, amend, reset, force push or target merge occurred.

All meaningful slices were committed and pushed immediately:

```text
b68e0dcc6f33376d0d7370c879d71f8a4c2866a7 baseline report
2f04ece4ea07b0657066249f04cc510ad6d9ed91 original four red witnesses
b3db8c2276e4ade49b2f3f80390b5e75aaf786ca NTFS red and native diagnostics
9a55b039c3f677a11f1b158d2d21f98f489d371c native-Linux host admission
fd92dc704ee5729d6c0e8fcb55fc7e99fad6fcee evidence fixture repairs
1dca9e5d909e8241ab2b4158509e2cae5e9d302a strict read-only results cleanup
1d93984173a745f446c4bf0cf3e4ae160e421a19 standalone nullable compilation correction
b79cd5e7fbfe90cec96d6ba3b62f581eb374e1cb empty diagnostic input refusal
```

--- next stage ---
next: land
handoff: Land the pushed fixture-only branch under the commissioned Review waiver; retain the native rows above as incomplete qualification and read the original Windows TRX failures before claiming their resolution.
artifact: /work/worktrees/task-ab1cd00f/docs/investigations/2026-10-26-rc-db2c2088-groups-74-75-debug.md
