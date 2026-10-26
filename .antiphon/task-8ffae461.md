EvidenceDiffGuardTests did not reproduce on Linux: 19 passed, 0 failed, 0 skipped. No product or fixture fix was justified; the historical Windows failure remains unclosed.

Assigned branch: feat/card-task-8ffae461. Tested source and fetched origin/rc/2026-10-26: bedea1c03cd788d83b2ff54c40bfb603927bd69c. No branch switch, rebase, reset, target merge, desktop access, native lock, tag or release action occurred. The caller's existing debug/fix authority was sufficient; no additional approval was requested.

Only this report is changed. Test assertions, product source, fixtures, deadlines and retries are unchanged.

Findings and classification:

- The reported one-case failure in antiphon-all-group-74 has no failing method or exception available in this Linux checkout. Its TRX is desktop-only. Classification: unresolved native failure; neither Windows path semantics nor load-induced timing is established.
- Compared the class, EvidenceGitFixture, check-evidence-diff.ps1 and scripts/lib/evidence-policy.ps1 against the failed candidate 105792f7d22f297746cdb338976f52e424b76fa7: no changes. Thus this pass does not credit a later rc repair.
- Checked the earlier a043b0a4 work at ffe9525531e2c6087a5395b43527c01bf460a8b5. That is its report commit; the reader implementation fix is 281542f75b74848819ac2340402ff9e2167ec5bd. It adds overlapped Windows handles to CheckpointFileReader and checkpoint-file-reader.ps1. This class calls neither reader. Its guard reads Git subprocess pipes; fixture snapshot reads are ordinary File.ReadAllBytes and event JSON uses Get-Content. There is no evidence connecting this failure to that specific zero-byte verified-reader stall. No duplicate reader repair was made.
- Existing Windows fixture repairs in 1fc698beeaa695f3f44045919c77ba325b423c22 are already in the failed candidate: core.protectNTFS=false for object-only invalid paths, a relative shell canary, and clearing a loose object's read-only attribute before deleting it. All 19 cases, including literal paths and missing-object refusal, passed here. No new Windows-string witness was added because no Windows-semantics cluster was established.

Verification (Debian 12 linux-x64, .NET 9.0.22, SDK 10.0.403):

- One isolated build: 0 errors, 1196 warnings, 227.31 seconds. Warning attribution was not separately measured. Host build slot granted, waited=0s, held=228s.
- Exact filter /*/*/EvidenceDiffGuardTests/*: executed=19 passed=19 failed=0 skipped=0; test-host duration 97.860 seconds. Host build slot granted, waited=0s, held=101s.
- Tested source stayed clean at the SHA above throughout build and execution. Direct --no-build execution was explicitly required by the brief because the test DLL exceeds the checkpoint reader's 64 MiB cap. No unrelated suite or repeat run was needed.
- Per-test durations and roster are in .antiphon/8ffae461-evidence/linux/evidence-diff.trx. IO/CLI/setup/teardown phase attribution is unavailable/unknown, not claimed zero; aggregate durations are one sample, not measured p95. Slowest case: Refuses_failed_or_malformed_git_results, 14.380469 seconds.

Linux commands executed, each in the foreground and fully awaited:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-8ffae461-build -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-8ffae461/ --property:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label debug-8ffae461-EvidenceDiffGuardTests -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-8ffae461/ --property:UseAppHost=false -- --treenode-filter '/*/*/EvidenceDiffGuardTests/*' --report-trx --report-trx-filename evidence-diff.trx --results-directory .antiphon/8ffae461-evidence/linux
```

Ignored raw evidence is local to /work/worktrees/task-8ffae461/.antiphon/8ffae461-evidence/: build.log, test.log, linux/evidence-diff.trx, generated HTML, and output-inventory.txt. These generated files are not committed and may disappear when the mirror is retired; this report preserves the actionable result.

Native rerun row (pending, zero native execution credit): EvidenceDiffGuardTests, exact filter /*/*/EvidenceDiffGuardTests/*, expected 19 cases. After the caller's existing qualification releases the native lane, rebuild the chosen rc tip once in a fresh isolated output, then run this row. Do not take the currently owned native lock or interrupt the supervisor.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label g74-native-build -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-8ffae461-native/ --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label g74-native-evidence-diff -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-8ffae461-native/ -- --treenode-filter '/*/*/EvidenceDiffGuardTests/*' --report-trx --report-trx-filename evidence-diff-native.trx --results-directory .antiphon/8ffae461-native-rerun
```

Use a fresh results directory if that destination exists. Retain the original Windows failing method/exception and the rerun TRX to determine closure. A Linux pass does not close the native qualification failure.

Landing context: fetched origin/master=b6db2de3dd0c0bad2ba30823d01882ca6fc9388e. The rc merge-tree check passed. Master merge-tree reports add/add conflicts in .antiphon/task-af2b78c8.md and docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md; both reproduce against the assigned base, independent of this report. Do not describe the whole branch as master-landable. No implementation needs landing from this task; this is a report-only publication.

--- next stage ---
next: land
handoff: Report-only tip; no reproduced cluster and no code fix. Retain the unresolved Windows failure and arrange the exact 19-case native rerun when its existing lane is free. Any rc/master report publication must account for the inherited master conflicts; this report grants no native closure or release credit.
artifact: /work/worktrees/task-8ffae461/.antiphon/task-8ffae461.md
