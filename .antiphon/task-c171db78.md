# CARD-1514 CP-8 verification

**Verification incomplete: portable matrix and five loaded repetitions passed; strict-alone certification remains incomplete.**

Host: 4551c5a7852f, Linux/server2, TEMP=/tmp, UseAppHost=false. Assigned branch: feat/card-task-c171db78. Assigned base: 65beb323fdd6f88d0ea478d390be66ea2f8a67e7. Verified source: **b831ee9e150783f943a32a59f82c12b0cde10ad4**, containing final code 829e7b8c55dc1ac995e8ae5e78b64c681da20822. The source-freeze report commit b831ee9e150783f943a32a59f82c12b0cde10ad4 was pushed before the producing build. Later report-only commits do not relabel these receipts. This task changes only this narrative report; no product/test/plan/policy/schema/definition file changed.

Production's shared **200 ms** reader deadline is unchanged. No -Update, native Windows tests, desktop access, live qualification d47848cc access, merge, rebase, tag, release publication or activation occurred. Native CP-10/11 remain outside this brief.

## Matrix

One serial run, one producing build, 38 executed/passed, zero failed/skipped, zero reruns. The checkpoint source validator passed all nine rows at the exact source above. Every build/row held a granted broker lease; no unleased driver ran.

| CP | Executed | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| CP-1 | 8 | 8 | 0 | 0 |
| CP-2 | 6 | 6 | 0 | 0 |
| CP-3 | 6 | 6 | 0 | 0 |
| CP-4 | 1 | 1 | 0 | 0 |
| CP-5 | 3 | 3 | 0 | 0 |
| CP-6 | 10 | 10 | 0 | 0 |
| CP-7 | 2 | 2 | 0 | 0 |
| CP-8 | 1 | 1 | 0 | 0 |
| CP-9 | 1 | 1 | 0 | 0 |

Run 20261031-011948-40e6: GREEN, exit 0. Budget wall 1667.200 s; charged execution 1037.077 s; admission-only union 630.122 s. Execution cap 55 minutes, independent hard-wall cap 100 minutes, broker acquisition cap 45 minutes. Verified producing-build span 293.397 s; actual dotnet build 225.71 s, zero errors / 1,406 inherited warnings. Build admission waited 195 s. No unlisted matrix driver.

The real importer accepted all eleven plan rows unchanged. The ignored execution manifest selected CP-1..9, consolidated their build and After into one build group, preserved each exact filter/group/minimum/expectation, and retained serial execution. Historical provisional coupled row/build ceilings were 40/32/4/2/6/6/4/6/2 minutes; these are inherited loaded-host allowances under CARD-1514/1404, not a measured candidate p95. The initial consolidation refused cross-After reuse before any build/test; that setup refusal is retained in matrix-console.log, with zero test credit. The corrected final run above is the only matrix execution.

## Repetitions and host conditions

| Attempt | Executed / passed / failed / skipped | Test seconds (TRX) | Controller wall seconds | Condition |
|---|---|---:|---:|---|
| CP-8-loaded-01 | 1/1/0/0 | 120.894 | 132.982 | qualified load |
| CP-8-loaded-02 | 1/1/0/0 | 90.581 | 103.415 | qualified load |
| CP-8-loaded-03 | 1/1/0/0 | 71.375 | 79.571 | qualified load |
| CP-8-loaded-04 | 1/1/0/0 | 89.126 | 97.432 | qualified load |
| CP-8-loaded-05 | 1/1/0/0 | 78.777 | 92.764 | qualified load |
| CP-8-alone-01 | 1/1/0/0 | 75.802 | 85.943 | shared host; no alone credit |
| CP-8-alone-02 | 1/1/0/0 | 71.770 | 80.685 | shared host; no alone credit |
| CP-8-alone-03 | 1/1/0/0 | 70.941 | 79.949 | individually qualified; arm incomplete |
| CP-8-alone-04 | 1/1/0/0 | 67.350 | 76.491 | individually qualified; arm incomplete |
| CP-8-alone-05 | 1/1/0/0 | 67.827 | 76.113 | individually qualified; arm incomplete |
| CP-8-alone-06 | 1/1/0/0 | 76.718 | 86.164 | shared host; no alone credit |

Loaded: **5/5 qualifying green**. Alone: **3/10 individually qualifying greens**; latest consecutive streak 0/10; 6 total attempted, all outcomes retained. No all-fifteen-green certification is claimed. A dedicated quiet host window is still required.

Each trial ran the exact CP-8 method in a fresh TUnit process through the checkpoint row tool, reused the fresh verified pack, checked clean source before/after, and retained unchanged assembly/harness/library SHA-256 values before/after. Row names are attempt IDs; an environmentally ineligible green never receives alone credit. No failed test was retried. The controller also tracked a consecutive streak, resetting it on interference; individually qualified results are preserved separately. No ten-run certification is claimed while incomplete.

The observer samples the broker and actual dotnet build/test identities about once per second during each test host. Alone admission additionally requires a stable 15-second quiet interval with no foreign lease, queued driver, build or test. Queue/admission time is retained separately from actual test-host overlap. Host condition evidence is sampled observation, retained in the trial JSON.

Loaded build PID 1856, process start 2026-10-31T01:47:53.6928800Z, was alive with the same start identity throughout all five test-host windows. It rebuilt the real Antiphon.Tests graph under granted lease 79493f73-8f46-491d-a23c-63d268f5bcb5, maxcpucount=1, UseSharedCompilation=false, UseAppHost=false, isolated OutputPath/ArtifactsPath. After trial 5 it was intentionally killed/joined at 2026-10-31T01:57:01.4405683Z (exit 137); no successful load-build claim is made. Its gate held 549 s, waited zero. load-joined.json confirms root absence and no remaining owned dotnet process. The ordinary producing build had already passed.

Other leases observed during loaded trials:

- rc46-narrow: 346390be-3404-4955-abb1-be7894e48d82
- r359-base2: 359a74a3-0669-485f-b9ee-46295079b7c3
- HELPER-ABORT-FIX: cdf3cde4-0e2b-4be5-bdfe-cf023a494141
- r359-migr: 359a74a3-0669-485f-b9ee-46295079b7c3
- c1609-10-tool: 8b5c63b3-0444-4689-8788-626fc6bef065
- HELPER-ABORT-BASE: 8b5c63b3-0444-4689-8788-626fc6bef065

The first attempted alone run passed but overlapped build:bin-c1610-fix3-red and is excluded. Its original receipt/host samples remain. Subsequent interference and admission observations are retained without overriding that history. No other owned build/test ran alongside the alone arm.

TRX method-duration quantiles (nearest rank, small provisional samples): loaded n=5, p50=89.126, p95=120.894, max=120.894 seconds; individually qualified alone n=3, p50=67.827, p95=70.941, max=70.941 seconds. Controller wall includes tool startup, source checks, sampling and teardown. Direct row receipts have unavailable phase subdivisions; these are unknown rather than zero. Matrix CP-8 records startup 10.056 s, test body 81.625 s, teardown 0.740 s, host wall 92.421 s. These script witnesses use no database/container/web-host setup; finer IO/CLI subdivisions remain unknown. No red test/deadline observation justified a source or budget change.

Host-condition observation span: 2026-10-31T01:57:30.904Z through 2026-10-31T03:00:00.869Z. The final quiet-admission cutoff was 03:00 UTC; the controller exited 4 with the host still occupied. This was an environmental admission cutoff, not a failed test or a broker-slot timeout. Last foreign leases: build:bin-r7743-final (7743ab23-676c-4368-ab40-83b5d10cca2e), r7743-mut-build (7743ab23-676c-4368-ab40-83b5d10cca2e), task-7aadc94a-green-build (7aadc94a-fb78-4bb9-b3ea-d7f9f84674d8). A reserved quiet host window is required for completion.

## Definition audit, evidence and cleanup

The real scripts/audit-land-verification-graph.ps1 -Check passed (exit 0): digest 20DE49F6B177E5A238F20D4F8C7F33EAEE6B511A9163CDB902D9136EF06DECB6, 66 inputs, both evaluator passes and canary comparison. -Update never ran. Definition files are unchanged. Tool bootstrap passed in 6.47 s (zero errors / five inherited warnings); audit bootstrap passed in 4.36 s (zero errors/warnings). Both held granted slots.

All trial receipt validations: 11 checked, 11 valid at b831ee9e150783f943a32a59f82c12b0cde10ad4. Matrix validation: CHECKPOINT SOURCE VALID source=b831ee9e150783f943a32a59f82c12b0cde10ad4 rows=9.

Raw ignored evidence root: /work/worktrees/task-c171db78/.antiphon/c171db78-evidence/. Key artifacts: matrix/20261031-011948-40e6/report.json, execution.yaml, imported.yaml, loaded-results.json, loaded-validation.json, loaded-statistics.json, alone-results.json, alone-admission.jsonl, all-validation.json, load-builds.json, load-joined.json, land-check.log and per-attempt TRX/report/console. No generated JSON/TRX/log/archive is committed.

All owned commands and children are joined. Every owned alternate output root was removed by its exact literal path after the last run; cleanup.json verifies absence:

- /work/worktrees/task-c171db78/tools/Antiphon.Checkpoints/bin-c171-tool
- /work/worktrees/task-c171db78/src/Antiphon.HostCleanup/bin-c171-tool
- /work/worktrees/task-c171db78/tools/Antiphon.LandGraphAudit/bin-c171-audit
- /work/worktrees/task-c171db78/tools/Antiphon.LandGraphAudit.Core/bin-c171-audit
- /work/worktrees/task-c171db78/tests/Antiphon.Tests/bin-c171-final
- /work/worktrees/task-c171db78/tests/Antiphon.Tests/bin-c171-load

Source, plan, output custody and artifact provenance are retained in the raw receipts. Final evidence-diff, fetched-master virtual-merge outcome and remote branch tip are in the caller message; no master landability is inferred from these tests.

## Essential unedited checkpoint lines

```text
CHECKPOINT CP-1 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=ok filter=/*/Antiphon.Tests.TestHelpers/(RcManifestJournalTests*)|(RcManifestRecoveryTests*)/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/matrix/20261031-011948-40e6/rows/CP-1/run.trx slot=granted waited=105s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.TestHelpers/RcManifestIntegrationTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/matrix/20261031-011948-40e6/rows/CP-2/run.trx slot=granted waited=45s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.TestHelpers/TestPhaseSummaryTests/C1514T_MergeAndQuality executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/matrix/20261031-011948-40e6/rows/CP-3/run.trx slot=granted waited=30s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.TestHelpers/TestClassificationPolicyTests/C487_G059 executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/matrix/20261031-011948-40e6/rows/CP-4/run.trx slot=granted waited=45s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.Scripts/ReleaseGateAuthorityContractTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/matrix/20261031-011948-40e6/rows/CP-5/run.trx slot=granted waited=45s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.Scripts/RcFullTimingContractTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/matrix/20261031-011948-40e6/rows/CP-6/run.trx slot=granted waited=15s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.TestHelpers/TestPhaseSupervisorTests/* executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/matrix/20261031-011948-40e6/rows/CP-7/run.trx slot=granted waited=150s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.Scripts/PosixHarnessToolTests/Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/matrix/20261031-011948-40e6/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-9 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.Scripts/RcFullWatchdogHarnessTests/HeartbeatExistingContract executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/matrix/20261031-011948-40e6/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-8-loaded-01 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.Scripts/PosixHarnessToolTests/Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/trial-receipts/CP-8-loaded-01-20261031-014817-a955/run.trx slot=granted waited=0s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-8-loaded-02 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.Scripts/PosixHarnessToolTests/Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/trial-receipts/CP-8-loaded-02-20261031-015033-c551/run.trx slot=granted waited=0s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-8-loaded-03 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.Scripts/PosixHarnessToolTests/Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/trial-receipts/CP-8-loaded-03-20261031-015214-ee6c/run.trx slot=granted waited=0s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-8-loaded-04 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.Scripts/PosixHarnessToolTests/Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/trial-receipts/CP-8-loaded-04-20261031-015335-9083/run.trx slot=granted waited=0s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-8-loaded-05 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.Scripts/PosixHarnessToolTests/Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/trial-receipts/CP-8-loaded-05-20261031-015514-5790/run.trx slot=granted waited=0s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-8-alone-01 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.Scripts/PosixHarnessToolTests/Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/trial-receipts/CP-8-alone-01-20261031-020705-b5b9/run.trx slot=granted waited=0s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-8-alone-02 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.Scripts/PosixHarnessToolTests/Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/trial-receipts/CP-8-alone-02-20261031-021850-a98c/run.trx slot=granted waited=0s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-8-alone-03 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.Scripts/PosixHarnessToolTests/Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/trial-receipts/CP-8-alone-03-20261031-025047-746d/run.trx slot=granted waited=0s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-8-alone-04 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.Scripts/PosixHarnessToolTests/Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/trial-receipts/CP-8-alone-04-20261031-025223-95bd/run.trx slot=granted waited=0s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-8-alone-05 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.Scripts/PosixHarnessToolTests/Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/trial-receipts/CP-8-alone-05-20261031-025355-62e5/run.trx slot=granted waited=0s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
CHECKPOINT CP-8-alone-06 commit=b831ee9e150783f943a32a59f82c12b0cde10ad4 build=reused filter=/*/Antiphon.Tests.Scripts/PosixHarnessToolTests/Qualification_timing_completes_without_mkfifo_chmod_or_ln_on_path executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-c171db78/.antiphon/c171db78-evidence/trial-receipts/CP-8-alone-06-20261031-025526-f739/run.trx slot=granted waited=0s dirty=0 source=b831ee9e150783f943a32a59f82c12b0cde10ad4 sourceState=clean buildSource=verified
```

## Artifact digests

```text
2273d82de8326f6e51a365bee5d3f20f5b66ae6ddfa434c7bce055dc07610e38  /work/worktrees/task-c171db78/.antiphon/c171db78-evidence/matrix/20261031-011948-40e6/report.json
a34eb2d76784812784a66b68efd42f75f9d8c0afc1374c70e56c331907621dc8  /work/worktrees/task-c171db78/.antiphon/c171db78-evidence/execution.yaml
f7195975a63b3322a654f825b0e9ae6905a3d0ee990128747480f82209fd6a51  /work/worktrees/task-c171db78/.antiphon/c171db78-evidence/loaded-results.json
de9a22affbc5972f423a1e56c1f7c52e07c440521fb010dcf00c9aee252e8574  /work/worktrees/task-c171db78/.antiphon/c171db78-evidence/alone-results.json
2b9a4be698938aabef267e83100810ea343c937e69c839cc203ed6162da80504  /work/worktrees/task-c171db78/.antiphon/c171db78-evidence/all-validation.json
c3d6c427727f2da7c2e5eaed7365b177cb4a0f6099e7b05e9f5c7be8be16134d  /work/worktrees/task-c171db78/.antiphon/c171db78-evidence/cleanup.json
```

--- next stage ---
next: debug
handoff: Finish strict-alone CP-8 ten-run certification on a reserved quiet host window. Three individually qualifying passes are retained; the required certification is incomplete. Preserve the validated matrix/loaded receipts and the 200 ms product deadline. No task test failed; current blocker is host isolation.
artifact: /work/worktrees/task-c171db78/.antiphon/task-c171db78.md
