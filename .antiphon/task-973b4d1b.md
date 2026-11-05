Batch 85 is prepared and pushed, but master publication is blocked by required publication-authority execution failures and an incomplete hash-reader run.

## Publication and decision

- Assigned branch: `feat/card-task-973b4d1b`.
- Verified candidate: `0dbd8295a034f8d88f5b98d1e29f14ac82c1890a`.
- Base and last observed remote master: `1527c29fef2737bf3eaa49f2293d82d85402265a`.
- No master push was attempted. Landing/re-pick loops: 0; master push retries: 0. No rc ref, activation, branch reset, rebase, amend, merge or force-push was used.
- Continued under the supplied merge/land authority without requesting another approval. That authority does not waive the newly observed publication failures. A caller-commissioned Debug/Code repair or an explicit decision on the named failed/incomplete checks is needed before landing.
- The brief's inherited-failure exceptions cover the graph and named census check; they do not waive publication-authority failures or incomplete hash-reader verification.
- The full Unit lane was deliberately excluded under the brief's minimal-testing instruction. The source owner's historical incomplete CP-6..CP-26 outcomes are preserved; this report does not relabel them complete.

## Exact picks and patch proof

`git cherry origin/master <source>` selected three repair-stack commits and one TestDesign commit. Every selected `+` commit was picked in order with `cherry-pick -x`; no report-only source commit appeared in that set. Each pick was pushed immediately to the assigned branch. There were no conflicts or implementation repairs.

These are prepared, unlanded commit SHAs. Stable patch IDs match source and prepared commit byte-equivalent patches:

| Prepared SHA | Source SHA | Stable patch ID |
|---|---|---|
| 8747ca113dcef755481ff8e48fc5b0bd03eaded4 | 19b9e69513551259b7c56eec03b77ef3a4cc861c | 621ddd9593daa920351eea47aaf5179d1b75a31c |
| 2b6fbb1e227d36e550c23222dd966cdb98b8f254 | 88ed031df17ad9559d830e7cf120faaaddf05b27 | ab1c8d981b07a3a093e4697cb77706ca60184a52 |
| 2bb8f1a931cea84e1683def56a9baf84c70d1f6e | cc4ab8e31349db759181a2931e8840ec6d79805f | a463ba1cd590e6fd7dfa5aa603ff54c802e7eee8 |
| 0dbd8295a034f8d88f5b98d1e29f14ac82c1890a | e51d45b7c4678be10912b72b4f74b4bd13ac2b04 | a41f3049d74fc0be1f26be70ad5ce12990e967b8 |

Seven deliverable paths changed: `tests/test-execution-policy.json`, `scripts/test-live-lane-run.ps1`, `server/Bundles/stage-test-design.md`, the three repair plans under `docs/superpowers/plans/2026-11-05-*`, and `docs/superpowers/testdesign/2026-11-05-card-0824-s5b-2-testdesign.md`. All plan/TestDesign deliverables were preserved. No C# source, project/build definition, graph record or target pin changed.

## Coupled identities

Batch-base nightly policyHash:
`5c7f5f061c21284a1e7bee0b18ac3cfd6cd7b3a51e36d6db450f5569628aaad4`

Final nightly policyHash:
`0686a97cccbec3d421b2298b47ff64aaa63d923d654252e2ea44bd3d1532487b`

Batch-base live-lane input digest and pin:
`836bbee0716085870363580397052a9f545e072facc92912695a6884278474b7`

Final live-lane input digest and pin:
`f5151a2a95ed14a8de0a50f9a475d0d45853949ce61c1ed83304c04178bfd73f`

The final values equal exactly the new values in `/work/worktrees/task-340f1d0a/.antiphon/task-340f1d0a.md` and its committed repair plan. The report is absent from that source branch's Git tree, so its runner-side file was read directly. `Get-NightlyPolicyHash`, `Read-NightlyExecutionPolicy` and the stored policy hash agree. `scripts/test-live-lane-run.ps1 -PrintInputDigest` equals the executable census pin. The other four protected inputs are unchanged. Exact per-pick patch-ID proof accounts for the batch's entire delta.

## Completed final-tree verification

One serialized native-output solution build on the candidate:

```text
TEMP=/tmp timeout 45m pwsh -NoProfile -File scripts/build-slot.ps1 -Label merge85-final-build -- dotnet build Antiphon.sln -p:UseAppHost=false -m:1 --nologo
1886 Warning(s)
0 Error(s)
Time Elapsed 00:03:09.97
BUILD SLOT granted lease=5d8d3418-fb7b-4426-829e-bc564083e069 waited=150s maxcpucount=6
BUILD SLOT released lease=5d8d3418-fb7b-4426-829e-bc564083e069 held=191s
```

No NETSDK1047 occurred and no second build was run. Every test driver had its own granted build-slot lease. Final runs used `dotnet run --project ... --no-build -p:UseAppHost=false`, native default outputs, `TEMP=/tmp`, explicit bounded outer timeouts, and checkouts outside `/tmp`.

Fresh TRX counters:

| Selection | Passed | Failed | Skipped | Duration |
|---|---:|---:|---:|---|
| Full instruction, classification, slow-test and platform policy sweep | 76 | 0 | 0 | 5m20.430s |
| Final exact generated-output census method | 1 | 0 | 0 | 32.378s |
| Base exact generated-output census method | 1 | 0 | 0 | 14.451s |
| Full ReleaseGateReportDeliveryTests | 2 | 0 | 0 | 2m28.034s |
| Base C599A_ExecutionLedgerMarkers | 1 | 0 | 0 | 5m02.500s |
| Base C599A_ExecutionLedgerJoins | 0 | 1 | 0 | 5m11.161s |
| Base C599A_ExecutionLedger | 1 | 0 | 0 | 4m15.294s |

The 76-test final sweep contains the complete selected classes:

| Class | Passed |
|---|---:|
| MechanicalPcContractTests | 11 |
| TaskPlatformGuidanceTests | 5 |
| RunnerDefaultGuidanceTests | 4 |
| TestClassificationPolicyTests | 39 |
| TestClassificationGuardTests | 1 |
| SlowTestRegistryMergeTests | 5 |
| SlowTestTripwireTests | 2 |
| NativeLinuxPolicyWitnessTests | 3 |
| ReleaseGatePlatformTests | 6 |

All four formerly red witnesses are explicitly present and Passed in the final TRX: `C479_R06_LowNeverDiscoversAndTierRowsStayLow`, `Policy_rows_require_pinned_attribute`, `Markers_do_not_expand_policy_exclusions`, and `LinuxOnlyWindowsExclusionPolicy`. The final policy contains the method-scoped C1677 exclusion and its sweep passed.

The exact census method was `CodexRootExecContractTests.Provider_exec_into_the_runner_names_uid_1654`. It passed at both base and final; the earlier disappearing `bin-*` output failure did not reproduce. No inherited-failure or repaired-census assertion is inferred from these two green results.

## Failed/incomplete publication verification

The final combined selection covered full LiveLaneRunnerTests, ReleaseGatePolicyTests, NightlyScriptsTests, NativeLinuxPolicyWitnessMutationTests, RcFailureHistoryTests, ReleaseGateActivationTests, InterimVerificationReadinessTests and ReleaseGatePublicationAuthorityTests. The E2E policyHash-mentioning delivery class ran separately and passed as above. Support classes NativeLinuxPolicyWitness and C545World are not independently executable test classes.

Three final console-confirmed failures occurred at the unchanged five-minute ScriptHarness execution budget:

```text
failed C599A_ExecutionLedgerMarkers (5m 00s 101ms)
failed C599A_ExecutionLedgerJoins (5m 00s 237ms)
failed C599A_ExecutionLedger (5m 00s 071ms)
```

Each reported `TimeoutException`, with `completion root=False stdout=False stderr=False`. Internal guard variants had made progress; these are execution-budget failures, not demonstrated policy-guard assertion regressions.

- Markers passed at the exact base and timed out at final. This is an observed candidate-only timeout. Host/timing sensitivity and causation are unresolved; it is not called inherited or a proven production regression.
- Joins timed out at both base and final: inherited execution failure confirmed.
- Ledger passed at the exact base and timed out at final. This is a second observed candidate-only timeout; causation remains unresolved.

After the required gate failed, the remaining combined run was cancelled with SIGINT to its verified owned test host PID 9952 at 23:00:38 UTC. The driver joined, exited 1 and released its lease. No final TRX was emitted. No passing counters, Full scope or valid overall receipt are claimed for that interrupted selection. The seven publication methods reported cancelled while waiting for their limiter were PinnedPolicyFields, PinnedPolicyIdentityFields, PublisherTestMode, PinnedPolicySchema, PinnedPolicySuiteFields, PinnedPolicy and PinnedPolicyBlobFields (all prefixed `C599A_`). Other unreported results receive no execution credit.

```text
BUILD SLOT granted lease=e1a934b3-8d5c-4ced-a749-9df520ddd0b9 waited=60s maxcpucount=6
BUILD SLOT released lease=e1a934b3-8d5c-4ced-a749-9df520ddd0b9 held=2220s
```

This selection exceeded 30 minutes and needs performance Debug. Fixture/script execution dominates the observed long child lifetimes; no measured p95 or complete per-phase attribution is available. The checkpoint plan's publication-method estimates remain unproved. No deadline, assertion or retry was weakened, and no green row was rerun. NativeLinuxPolicyWitnessMutationTests is an ordinary synthetic-fixture class; no production PC mutation cycle was performed or credited.

## Base provenance and diagnostics

Base checks ran in detached `/work/worktrees/merge85-base-973b4d1b` at exact base `1527c29fef2737bf3eaa49f2293d82d85402265a`, with the identical final-built test output copied into its native bin location. The assembly therefore resolves its repository root to the actual base checkout. C# test/helper source and build definitions are unchanged across this batch, so this reuses the compiled harness while reading each checkout's own policy/scripts/source. No baseline build was introduced.

Both test DLL copies had SHA-256 `a5842b952ecb5b4bd8dbc3a518c94f82a5a913ba3ce02b2b575903fe5cc1bc0f`. E2E DLL SHA-256 was `b19a04ba237cc634cc4a3da8193b76918ae6b769556bb23417e8fab7ca1a458e`.

The first base census launcher (`dotnet run --no-build`) exited before execution because the unrestored base checkout lacked TUnit package imports and appeared to have OutputType Library. Zero tests ran. A new leased `dotnet exec .../Antiphon.Tests.dll` invocation then executed the exact method and produced the valid baseline TRX. All subsequent exact publication baselines used that same direct-DLL pattern under separate leases. This diagnostic retry did not repeat a green test.

## Graph, evidence and cleanup

`audit-land-verification-graph.ps1 -Check` completed with exit 0 and `approved` at both base and final, using the one final-built native evaluator. Both prechecks produced:

```text
digest=D5B302A94FD3B0C28012C6764A2027B8B8DDE1D04506B3A641DD671EFB6F8C03 count=69
```

There was no inherited graph mismatch to waive, no changed selected definition or target pin, and no `-Update`. Preliminary attempts made before the evaluator existed refused with `built evaluator missing`; they were not approval evidence. The successful checks above followed the completed solution build.

```text
EVIDENCE range base=1527c29fef2737bf3eaa49f2293d82d85402265a head=0dbd8295a034f8d88f5b98d1e29f14ac82c1890a
EVIDENCE result commits=4 entries=0 violations=0 base=1527c29fef2737bf3eaa49f2293d82d85402265a head=0dbd8295a034f8d88f5b98d1e29f14ac82c1890a
```

`git diff --check` passed. All owned test drivers have joined and released their leases. The 104 owned native bin/obj directories were removed using individually typed literal absolute paths; a fresh directory census found none remaining. The detached base worktree was removed at its exact absolute path. Generated logs, JSON, TRX and owned result directories were removed after their essential counters, exact failures, hashes and provenance were recorded here. This Markdown report is the only additional committed deliverable. No tracked mutation needed restoration.

For the diagnostic repair, run each exact failed method as a separate leased row after rebuilding the repaired candidate. Filters are `/*/*/ReleaseGatePublicationAuthorityTests*/C599A_ExecutionLedgerMarkers`, `/*/*/ReleaseGatePublicationAuthorityTests*/C599A_ExecutionLedgerJoins`, and `/*/*/ReleaseGatePublicationAuthorityTests*/C599A_ExecutionLedger`. Preserve the existing base results; do not replay green baseline rows. The interrupted full hash-reader selection still needs complete trustworthy execution evidence.

## Completed driver lease provenance

contract-policy

```text
BUILD SLOT granted lease=4a940dfe-206b-4525-8eac-07d1ee0e2351 waited=330s maxcpucount=6
BUILD SLOT released lease=4a940dfe-206b-4525-8eac-07d1ee0e2351 held=327s
```

final-census

```text
BUILD SLOT granted lease=f7bc9105-f12b-4de8-9567-2d4f3ae8f446 waited=15s maxcpucount=6
BUILD SLOT released lease=f7bc9105-f12b-4de8-9567-2d4f3ae8f446 held=38s
```

base-census-direct

```text
BUILD SLOT granted lease=7bfa8809-72c3-40e7-bc30-30916a4364b5 waited=120s maxcpucount=6
BUILD SLOT released lease=7bfa8809-72c3-40e7-bc30-30916a4364b5 held=15s
```

report-delivery

```text
BUILD SLOT granted lease=177215e1-94c8-41c4-960a-e4e55c1ed2dd waited=61s maxcpucount=6
BUILD SLOT released lease=177215e1-94c8-41c4-960a-e4e55c1ed2dd held=151s
```

base-publication-markers

```text
BUILD SLOT granted lease=79e255bb-6a39-4efb-9a2b-5977226f60e4 waited=406s maxcpucount=6
BUILD SLOT released lease=79e255bb-6a39-4efb-9a2b-5977226f60e4 held=304s
```

base-publication-joins

```text
BUILD SLOT granted lease=4e3db478-087b-449d-a970-71bba1dc3eb1 waited=331s maxcpucount=6
BUILD SLOT released lease=4e3db478-087b-449d-a970-71bba1dc3eb1 held=313s
```

base-publication-ledger

```text
BUILD SLOT granted lease=8cc54a73-9e99-4aa8-91a6-cb1cfb45d6e8 waited=46s maxcpucount=6
BUILD SLOT released lease=8cc54a73-9e99-4aa8-91a6-cb1cfb45d6e8 held=256s
```

--- next stage ---
next: debug
handoff: Diagnose the publication-authority five-minute timeouts, especially the candidate-only Markers and Ledger failures; preserve the inherited Joins result. Obtain complete exact missing verification without replaying valid green rows, then resume Merge from the prepared source picks on the current master. No master publication is authorized by this incomplete verification.
artifact: .antiphon/task-973b4d1b.md
