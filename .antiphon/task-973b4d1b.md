Batch 85 landed successfully on master at `0dbd8295a034f8d88f5b98d1e29f14ac82c1890a` under the caller's explicit PUSH decision. Ledger timeouts remain a loud warning; no failed or incomplete verification has been relabelled green.

## Final publication decision and outcome

The final reopened input `.antiphon/inbox/fa3d36d2-8230-47bf-89ca-1cfb28142cea.md` explicitly ordered PUSH, a cheap static reachability check, no further hash-reader run, and a loud inherited-load-sensitivity warning. That instruction superseded the earlier STOP. Publication proceeded under that authority without asking for another approval. The caller stated that a follow-up card will be filed; this delegate did not file one.

- Remote master before publication: `1527c29fef2737bf3eaa49f2293d82d85402265a`.
- New remote master, confirmed with `git ls-remote`: `0dbd8295a034f8d88f5b98d1e29f14ac82c1890a`.
- One plain fast-forward push succeeded on its first attempt: `git push origin 0dbd8295a034f8d88f5b98d1e29f14ac82c1890a:refs/heads/master`.
- Just-in-time `git fetch origin master` confirmed the original base remained current. `git merge-base --is-ancestor origin/master 0dbd8295a034f8d88f5b98d1e29f14ac82c1890a` exited 0, and `git merge-tree --write-tree origin/master 0dbd8295a034f8d88f5b98d1e29f14ac82c1890a` completed without conflict, tree `eff2bf7d73c889f08b827fd4549acbfec566e362`.
- Master-moved re-pick loops: 0. Push retries: 0. No additional build or test ran in this final continuation, and no hash-reader rerun occurred. No rc ref was pushed and no service was activated.
- Only the four source-deliverable commits were published to master. Delegate report commits remain on `feat/card-task-973b4d1b`. No source change, conflict resolution, rebase, reset, amend or branch switch was needed.

### Landed commits and patch proof

| Land SHA | Source SHA | Stable patch ID |
|---|---|---|
| 8747ca113dcef755481ff8e48fc5b0bd03eaded4 | 19b9e69513551259b7c56eec03b77ef3a4cc861c | 621ddd9593daa920351eea47aaf5179d1b75a31c |
| 2b6fbb1e227d36e550c23222dd966cdb98b8f254 | 88ed031df17ad9559d830e7cf120faaaddf05b27 | ab1c8d981b07a3a093e4697cb77706ca60184a52 |
| 2bb8f1a931cea84e1683def56a9baf84c70d1f6e | cc4ab8e31349db759181a2931e8840ec6d79805f | a463ba1cd590e6fd7dfa5aa603ff54c802e7eee8 |
| 0dbd8295a034f8d88f5b98d1e29f14ac82c1890a | e51d45b7c4678be10912b72b4f74b4bd13ac2b04 | a41f3049d74fc0be1f26be70ad5ce12990e967b8 |

### Cheap static ledger reachability check

`rg` and focused source reads followed all three C# methods through `RunCaseAsync`, `ScriptHarness`, their exact PowerShell case functions, the shared publication fixture and the production authority/publisher. This was a read-only check; no test was run.

- `ReleaseGatePublicationAuthorityTests.cs` lines 79, 102 and 113 route the three named cases to `scripts/test-release-gate.ps1`.
- That script's exact functions (lines 2851, 3107 and 3223) each call `New-C599PublishFx -PolicyBytes (Get-C599ABasePolicyBytes)`.
- `Get-C599ABasePolicyBytes` (line 2474) reads unchanged `scripts/fixtures/c599-policy-v2.json`. `New-C599PolicyCheckout` accepts those explicit bytes; its read of the live `tests/test-execution-policy.json` at line 319 is only the null-PolicyBytes fallback, bypassed by these three cases.
- The fixture writes those bytes to its private checkout's `tests/test-execution-policy.json`, commits them, and the production authority reads that pinned blob and computes/validates `policyHash` (`scripts/lib/release-authority.ps1`, `ConvertFrom-ReleaseAuthorityPolicyBytes`, `New-ReleaseGateAuthority` and `Test-ReleaseGateAuthority`). Thus these tests **do read policyHash and a fixture policy at that relative path**; they do not consume the batch's changed policy bytes.
- No live-lane `PinnedInputDigest`/`test-live-lane-run.ps1` read was found in this exercised path. The authority's script-hash list excludes the live-lane runner. The three other changed Markdown plans, TestDesign document and bundle instruction are not read by this ledger path. The tested C# methods/helper, harness, fixture, authority, policy reader and publisher have no diff from the original base.

### LOUD WARNING: inherited load sensitivity; incomplete reader receipt retained

The caller classified these ledger timeout outcomes as inherited load sensitivity and explicitly accepted publication. The recorded results remain: candidate 0 passed / 3 failed; base 1 passed / 2 failed, with unchanged 300s budgets. Ledger alone passed at base in 258.553s and timed out at candidate in 300.136s; Joins and Markers timed out at both. Static reading does not independently prove scheduling causation. No regression or green result is fabricated from this decision.

The full hash-reader selection still has no clean complete receipt. It was not rerun, as explicitly instructed. Retained successful checks are the solution build (0 errors), policy/contract sweep (76 passed, 0 failed, 0 skipped, all four formerly red witnesses green), exact census (1 passed at each tree), report delivery (2 passed), and both approved 69-record land-graph checks. Exact counts, identities, old/new digests and historical receipts follow below. All owned native outputs/results and the detached base checkout were already removed; this final continuation created no test outputs.

## Historical isolated-rerun report

The following report records the earlier STOP before the final PUSH decision above. Its outcomes and costs are preserved.

Batch 85 remained unlanded at that point: the caller's then-current explicit stop condition reproduced for C599A_ExecutionLedger in the requested isolated rerun.

## Current outcome and authority

The reopened input `.antiphon/inbox/b90dfbed-ef7c-4a06-95d5-4e8b0db0fb12.md` ordered one matched three-method invocation at the candidate and one at the base, with nothing else of ours running, and said: **"if they fail ONLY at the candidate on a rerun, STOP and report."** The complete fresh TRXs show that condition for `C599A_ExecutionLedger`. Master was not pushed. The remaining hash-reader run was not started after this stop condition was established.

- Prepared source candidate: `0dbd8295a034f8d88f5b98d1e29f14ac82c1890a`.
- Reopened harness build and candidate run HEAD: `04468ab9e8eccbaf7d62eb19e6146c47ad7dfc3a`. The only tracked difference from the prepared candidate is the previous `.antiphon/task-973b4d1b.md` report; production/test/build-definition files are identical.
- Exact comparison base: `1527c29fef2737bf3eaa49f2293d82d85402265a`.
- Last confirmed remote master: `1527c29fef2737bf3eaa49f2293d82d85402265a`.
- New master SHA: none. Master push attempts/retries: 0. Landing/re-pick loops: 0. No rc publication, activation, rebase, reset, amend, branch switch, conflict repair or source fix occurred.
- Both long-running test drivers completed normally with fresh TRXs and native exit 2. Each held exactly one granted lease, and the candidate invocation exited before the base invocation started. No other owned build/test driver overlapped either comparison.

## Fresh paired result: unchanged 300-second execution budget

The native MTP UID list selected exactly the three requested methods, avoiding the additional methods that an `ExecutionLedger*` prefix would include. Each TRX was checked for a count of 3 and the exact method-name set. The [MTP CLI UID option](https://learn.microsoft.com/en-us/dotnet/core/testing/microsoft-testing-platform-cli-options#platform-options) accepts multiple UIDs; it was used without a simultaneous tree selector.

| Method, prefixed C599A_ | Candidate outcome / seconds | Base outcome / seconds | Existing budget | Classification for this rerun |
|---|---|---|---:|---|
| ExecutionLedger | Failed / 300.1359413 | Passed / 258.5532607 | 300s | **Candidate-only timeout: STOP** |
| ExecutionLedgerJoins | Failed / 300.0370641 | Failed / 300.0855556 | 300s | Timeout occurs at both trees |
| ExecutionLedgerMarkers | Failed / 300.0885283 | Failed / 300.1434734 | 300s | Timeout occurs at both trees |

| Invocation | Total | Passed | Failed | Skipped | TUnit duration |
|---|---:|---:|---:|---:|---|
| Candidate isolated comparison | 3 | 0 | 3 | 0 | 15m10.081s |
| Base isolated comparison | 3 | 1 | 2 | 0 | 14m25.699s |

All five failures were `TimeoutException` from the unchanged ScriptHarness five-minute execution budget. Candidate Ledger reported `completion root=True stdout=False stderr=False`; base Ledger completed successfully. Candidate Joins reported root=False, base Joins root=True, with stdout/stderr incomplete in both failures. Markers reported root=False and stdout/stderr incomplete at both trees. These observations establish the requested stop pattern; causal attribution to policy changes, host scheduling or stream-drain timing remains unresolved.

No execution timeout, assertion, retry policy, fixture or production file was changed. Each named method ran once per tree in this continuation. Earlier results and costs have not been relabelled. In particular, the newly failing base Markers result is recorded alongside its earlier passing baseline in the historical record below.

## Reproduction and provenance

The harness had been cleaned at the end of the initial pass, so one explicitly necessary native-output rebuild was performed before these comparisons:

```text
TEMP=/tmp timeout 40m pwsh -NoProfile -File scripts/build-slot.ps1 -Label merge85-reopen-harness-build -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false -m:1 --nologo
1728 Warning(s)
0 Error(s)
Time Elapsed 00:04:27.57
BUILD SLOT granted lease=cbc8e5d8-1000-43b9-8c29-bccb5d206509 waited=195s maxcpucount=6
BUILD SLOT released lease=cbc8e5d8-1000-43b9-8c29-bccb5d206509 held=268s
```

The candidate used `dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false`; the base used `dotnet exec tests/Antiphon.Tests/bin/Debug/net9.0/Antiphon.Tests.dll`. Both received this same UID argument list, plus `--timeout 18m`, fresh results directories and TRX reporting:

```text
--filter-uid
Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests.1.1.C599A_ExecutionLedgerMarkers.1.1.0
Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests.1.1.C599A_ExecutionLedger.1.1.0
Antiphon.Tests.Scripts.ReleaseGatePublicationAuthorityTests.1.1.C599A_ExecutionLedgerJoins.1.1.0
```

Each outer invocation had a 35m bound, a 15m slot-admission bound and its own `build-slot.ps1` lease. The 18m overall test bound left room for three unchanged 5m case budgets and cleanup; it did not fire. `TEMP=/tmp`; both checkouts were outside `/tmp`.

The detached base checkout read its own scripts and policy. Its native artifact layout was matched to all 72 candidate-owned bin/obj roots using the same built outputs, with no baseline compilation. Both test DLL copies had SHA-256:

```text
8a3b2aa90cb563252238a5cfac5f592164de683b07c18b0299c17924462452c1
```

Both tracked trees were clean after execution. The batch contains no changed C# test/helper source or build definition. Matching the full native artifact layout kept that experimental variable consistent across the two checkouts.

Host samples, with 24 online CPUs, were:

```text
Candidate, 2026-11-05 23:21:46 UTC: loadavg 57.92 40.02 35.14
Base,      2026-11-05 23:39:36 UTC: loadavg 32.76 40.24 36.22
```

These samples show differing host load; they do not prove the cause of any individual timeout. No p95 or complete per-phase cost claim is made.

```text
BUILD SLOT granted lease=853594d6-a021-48e5-8a88-bb56570e4ecf waited=45s maxcpucount=6
BUILD SLOT released lease=853594d6-a021-48e5-8a88-bb56570e4ecf held=914s
BUILD SLOT granted lease=4902548b-867b-4f05-a0a9-0952b21c2ee8 waited=226s maxcpucount=6
BUILD SLOT released lease=4902548b-867b-4f05-a0a9-0952b21c2ee8 held=867s
```

## Retained verification and remaining work

The previously completed solution build had 0 errors. The final-tree instruction/classification/platform policy sweep remains 76 passed, 0 failed, 0 skipped, including all four formerly red witnesses. The exact generated-output census passed once at each tree. Report delivery remains 2 passed, 0 failed. Base and final land-graph checks both approved the same 69-record digest. Their full details, original patch-ID proof, exact source/prepared commit table and old/new digests are preserved below.

The current final-tree identity readers were checked again and still returned exactly:

```text
policyHash=0686a97cccbec3d421b2298b47ff64aaa63d923d654252e2ea44bd3d1532487b
live-lane input digest=f5151a2a95ed14a8de0a50f9a475d0d45853949ce61c1ed83304c04178bfd73f
```

The live-lane census pin equals that digest. No selected build-definition record, graph record or target pin changed. No `land-graph -Update` was used. The interrupted hash-reader selection still lacks a complete final-tree receipt; the reply's conditional STOP prevented another reader invocation and master publication after the paired comparison.

Current cleanup is complete. All 72 recreated native bin/obj directories, the detached base worktree and the generated continuation logs/results were removed using literal absolute paths after preserving the essential counters, durations, provenance and lease receipts here. A final directory census found no bin, obj or TestResults directories in the assigned worktree. All comparison drivers are joined and their leases released; no source restoration was necessary. The final report is the only tracked file updated in this continuation. The initial report remains unedited in Git at `04468ab9e8eccbaf7d62eb19e6146c47ad7dfc3a:.antiphon/task-973b4d1b.md`.

## Historical initial attempt

The following initial-attempt sections preserve prior outcomes, costs and recommendations. The current authority and stop decision are the continuation sections above; the current handoff follows the historical evidence.


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
next: done
handoff: Master fast-forward confirmed at 0dbd8295a034f8d88f5b98d1e29f14ac82c1890a under the caller's explicit PUSH decision. Retain the ledger-timeout warning and incomplete hash-reader receipt; caller owns the promised follow-up card. No rc publication or activation occurred.
artifact: .antiphon/task-973b4d1b.md
