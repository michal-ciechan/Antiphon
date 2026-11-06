# Merge batch 93 completed

Published the test-only report-template read-only cleanup repair to master at `df1fc145181a3bedf444f9113f822f30d3b136f6`. All 101 required final-tree tests passed, with zero failures or skips. No conflict, additional repair, or incomplete gate remains.

## Publication and patch identity

Base and immediately pre-push remote master: `10c3a8dd043326ac3abfefdb113b2fdd3e65a3c9`. Source report tip: `be8e372324053289b58c07f2601d787ed75d818e`. `git cherry origin/master origin/feat/card-task-525e0201` identified two non-report commits; all six report-only commits were omitted. Both picks used `cherry-pick -x`, in order, without conflicts.

| Land SHA | Source SHA | Stable patch ID |
|---|---|---|
| `73d0d0533bb7eb9b10e05ce5efb2d25fb52c51b0` | `b05cc6638fd612b64c15e90f5a1d649cb805e6a3` | `a787474b3eb0a0876e6ca223aad0201775f8216c` |
| `df1fc145181a3bedf444f9113f822f30d3b136f6` | `5aa70e234ecb6364a22c29a8ede0feefcc13dcfb` | `af59f5d1512957fd5ca152ccfa167f14a683ff92` |

Each landed patch ID equals its source patch ID. Only `tests/Antiphon.Tests/TestHelpers/ReportBudgetWorkspace.cs` and `ReportBudgetWorkspacePortabilityTests.cs` changed on master. The template reset now calls the existing `ReadOnlyDirectory.Delete`; the witness exercises read-only loose Git objects. Product code and deadlines are unchanged.

Final fetch observed unchanged master; merge-tree exited 0 with tree `bc603a15ca621581395cc66a9dc4a9580c896860`. Plain fast-forward `git push origin HEAD:refs/heads/master` succeeded. A subsequent `git ls-remote` confirmed the published master SHA. Publication attempts: 1; master-movement/re-pick loops: 0; push retries: 0. No rc ref was pushed and no land-graph update was run.

Standing authority explicitly covered landing this test-only red/green repair without another Review, so no additional approval was requested. The developer-level requirement to push every committed slice overrode the brief's local-branch hold: the two picks were pushed to the assigned task branch before master. No reset, rebase, amend, force-push, branch switch, or target merge occurred. This report is committed only after master publication.

## Verification at df1fc145181a3bedf444f9113f822f30d3b136f6

One serialized `dotnet build Antiphon.sln -p:UseAppHost=false -m:1 -v:minimal`: 0 errors, 1,927 warnings, 203.73 seconds. No NETSDK1047 and no rebuild. An additional narrowly scoped native evaluator build at the base was necessary to perform the required baseline graph evaluation: 0 warnings/errors, 5.14 seconds. Each build and each test run held its own host build-slot lease, all granted with 0-second waits; no unleased run. Native default outputs, Debug, `UseAppHost=false`, and `TEMP=/tmp` throughout.

| Group | Full selected classes | Passed | Failed / skipped | Execution |
|---|---|---:|---|---|
| fixtures | ReportBudgetWorkspacePortabilityTests (4), AgentReportStoreBudgetTests (21), AgentReportStoreLoadTests (1) | 26 | 0 / 0 | 31.255 s |
| policy-A | TestClassificationPolicyTests (39), TestClassificationGuardTests (1), SlowTestRegistryMergeTests (5) | 45 | 0 / 0 | 87.343 s |
| policy-B and CP-1 | SlowTestTripwireTests (2), NativeLinuxPolicyWitnessTests (3), C1729LayeringGuardTests (9) | 14 | 0 / 0 | 18.118 s |
| contracts and CP-92 | MechanicalPcContractTests (11), TaskPlatformGuidanceTests (5) | 16 | 0 / 0 | 5.873 s |

The policy-A and policy-B selections together cover every CP-91 policy class. No green selection was repeated. Each run selected at most three classes, with a trailing `*` on every class name, through `dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false`. Run caps were 5m, 8m, 5m and 3m respectively, provisional diagnostic caps rather than measured p95 or changes to declared test deadlines. Outer bounds included admission separately. Load sampling used `ANTIPHON_REPORT_STORE_MEASUREMENTS=1` and produced 80 samples. Phase recording was enabled on all four groups; TRX retains per-test durations. Unrecorded setup/teardown or external-call phases remain unknown; no invented p95 claim.

`check-evidence-diff.ps1 -BaseRef 10c3a8dd043326ac3abfefdb113b2fdd3e65a3c9 -HeadRef df1fc145181a3bedf444f9113f822f30d3b136f6`: 2 commits, 0 evidence entries, 0 violations. `git diff --check` passed.

## Graph and digests

Base and final `audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll` both approved after the actual differential MSBuild evaluation. Both printed `digest=D5B302A94FD3B0C28012C6764A2027B8B8DDE1D04506B3A641DD671EFB6F8C03 count=69`. There is no inherited mismatch warning. An initial baseline invocation before compiling the evaluator refused for missing DLL; this was resolved by the explicitly recorded tool build and completed baseline check, not credited as success. The selected definition records and target pins did not change.

Contrary to the brief's anticipated digest change, the two selected source commits do not touch any digest input. Base and final recomputation both give:

- `-PrintInputDigest`: `417936f79e8bf453a32f2f91b202ae6435777c3ba21e159d831ca9b5a052c939`, equal to the census pin in `scripts/test-live-lane-run.ps1`.
- `Get-NightlyPolicyHash`: `2c445d46531ccff2a572e27b5e641e984af40c3b4691c67661dd9a4a360eba72`, equal to the stored `tests/test-execution-policy.json` policyHash.

The complete two-path diff proves no other file or digest input changed in this batch. No unnecessary pin rewrite was made.

## Evidence and cleanup

Raw build/graph/run logs, four TRX files, the executed roster and per-class census, phase JSONL, load samples, and output inventory are retained at `/work/evidence/merge-e0a09fa9`. Every owned command was awaited; no background child remains. No desktop or external executor was used. All generated worktree bin/obj directories, the owned card-1605 template/cases and load-output directory were removed with explicitly typed literal absolute paths. A recursive directory census found no remaining bin/obj output, and tracked source was clean before this report. This is a Linux merged-tree verification report; native Windows rc rerun remains the caller's qualification lane. Master publication does not activate the product hosts.

--- next stage ---
next: done
handoff: Master contains the repair; caller may rerun the Windows rc budget group in its existing qualification lane.
artifact: /work/worktrees/task-e0a09fa9/.antiphon/task-e0a09fa9.md
