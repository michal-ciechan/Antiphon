# CARD-1956: resume after SIGKILL

Date: 2026-11-16. Task: 55d5be7a. Branch: feat/card-task-55d5be7a.

The retirement recovery fix works. Final unique Linux verification scope: **223 passed, 0 failed, 1 platform skip** (224 results). N3 passed with a real SIGKILL and same-operation resume; the public production entry-point witnesses also passed. Full Review by a different company is next; this report does not authorize landing or claim Windows execution.

## Cause and invariant

Merge 248 / CARD-1955 made phase_begin reject every active lifecycle, even after its owner died. That closed unfinished Preview reuse, but also prevented an already-consumed Apply from resuming after SIGKILL/OOM/reboot.

Admission now requires explicit C1008_RESUME=1, an already-consumed receipt for the same operation/project/daemon, an Apply lifecycle, a dead owner identified by PID/startTicks, complete lifecycle evidence and joined helpers. The receipt must be in an existing resume-supported consumed state. Every original incarnation, journal intent, publication, live-state, custody, closure and token check remains in the ordinary production path. A Ready receipt that was never consumed and an unfinished Preview remain refused.

New lifecycle records use schema 2 and name Preview or Apply. Strict schema-1 validation remains supported; resuming an active legacy Apply additionally requires its protected admission record bound to that lifecycle nonce and immutable Preview digest. Missing lifecycle evidence refuses as lifecycle-missing. Refused Applying/PartialRefused admission names -Close as recovery. Close retains its existing absence/census/custody conditions. An unreadable owner stat is unknown and refuses.

## Changes and provenance

Base/current origin/master at start: 48a4615dcb4d67033886a3e31629dc7351ed0852.

- 4428b2b5bfb86600b7c146612478507410aee6fc: committed witnesses before the production repair; also gave the native fixture real phase_begin/phase_end boundaries instead of bypassing lifecycle admission.
- c7356e9a04f422318f42b302ee9ede0e4dc3a26f: production lifecycle repair, recovery runbook, and provisional 120-second bounds for the seven new witnesses. Product code and test bodies verified at this SHA.
- 3b04a2dbfdd9844d675bc949ccaebe785bfc51d4: seven NativeSourceOnly roster entries. This is the only file difference from c7356e9a04f422318f42b302ee9ede0e4dc3a26f; the static verification checks that exact diff. Product/script/test-body results are retained from c7356e9a04f422318f42b302ee9ede0e4dc3a26f. The affected guard selection is rebuilt and rerun at 3b04a2dbfdd9844d675bc949ccaebe785bfc51d4.

Changed files: scripts/c1008-retirement.sh; scripts/fixtures/c1609-retirement-real-cases.mjs; tests/Antiphon.Tests/Scripts/VolumeRetirementReceiptTests.cs; tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs; docs/docker-stack.md; this investigation.

The fence constant is unchanged. No rc/master push or live-host restart/qualification action was performed.

## Red evidence and historical outcomes

1. Unchanged master N3: 1 failed / 0 passed / 0 skipped, 2m06.447s test-run duration. It reported lifecycle-active recovery=new-operation at the initial Apply: the old native fixture had no lifecycle. Its owned Docker session confirmed cleanup=absent.
2. Witness commit 4428b2b5bfb86600b7c146612478507410aee6fc: red-focused selected three new witnesses; all 3 failed assertions, with zero fixture/build/timeout errors. Both public-entry SIGKILL resumes returned exit 2 / RecycleReceiptUnavailable / lifecycle-active. Missing lifecycle reported lifecycle-active instead of lifecycle-missing. The public-entry red was 29.026s.
3. The pre-fix native run after adding lifecycle admission stopped at the separately tracked 5-second fence: final effect elapsed 5510ms, limit 5000ms. This is preserved in red-native-lifecycle and is **not** counted as an assertion-red witness for the resume guard. The production retirement scripts were still identical to base at that test-only commit. Debug 235c38c6 owns the independent fence change.
4. At c7356e9a04f422318f42b302ee9ede0e4dc3a26f, the full guard union found one omission introduced by these new tests: C1210_Roster_closure_matches_source_gates, c1210-unrostered-gate, naming Sigkill_apply_resumes_through_public_entry. Outcome: 165 passed / 1 failed / 1 Windows-only skip. Commit 3b04a2dbfdd9844d675bc949ccaebe785bfc51d4 adds all seven exact source-roster identities. The frozen child runtime roster and pinned policy inputs remain unchanged. Affected guards were rerun; this historical failure is not relabeled as green.

N3 at the repaired source passed in 275.09s total wall (274s leased execution). Its native stage receipts show crash exit=137 after the first deletion and resume exit=0 with three remaining effect controls. Exact names, durable intent/result handling, removed-observed attribution and preservation of main/cache/sentinel volumes were asserted; owned native objects confirmed absent after cleanup.

## Required verification

All 25 VolumeRetirementReceiptTests, 13 VolumeRetirementApplyGateTests, 15 VolumeRetirementHostProofTests and 4 VolumeRetirementDockerTests passed: 57 product/witness results. The native N1/N2 and refusal shards were selected from the same real-case driver that exercises the changed lifecycle boundary. The focused row includes the unchanged round-1 Failed_refused_persist_refuses_active_lifecycle_apply blocker.

The guard union includes all eleven required guard classes plus TestTimingOperationLabelTests and ImpactedClassesSelectorTests. Its final unique scope is 166 passed and one Windows-only skip. After the roster correction, the 86-result affected selection replaces the earlier results of WindowsUnitPlatformAdmissionTests, DerivedImpactIndexTests, TestTimingOperationLabelTests and ImpactedClassesSelectorTests; 81 unaffected guard results are retained. Static validation checks class membership, nonzero counts, result identities, source SHAs, and the unchanged product inputs. Counts below retain every historical row; do not add the two guard rows together as unique coverage.

| Row | Source | Passed / failed / skipped | Execution / slot wait (s) |
|---|---|---:|---:|
| green-focused | c7356e9a04f4 | 8 / 0 / 0 | 229 / 15 |
| green-native-n3 | c7356e9a04f4 | 1 / 0 / 0 | 274 / 0 |
| receipts-1 | c7356e9a04f4 | 4 / 0 / 0 | 60 / 0 |
| receipts-2 | c7356e9a04f4 | 2 / 0 / 0 | 111 / 0 |
| receipts-3 | c7356e9a04f4 | 1 / 0 / 0 | 157 / 0 |
| receipts-4 | c7356e9a04f4 | 1 / 0 / 0 | 192 / 0 |
| receipts-5 | c7356e9a04f4 | 3 / 0 / 0 | 132 / 0 |
| receipts-6 | c7356e9a04f4 | 1 / 0 / 0 | 232 / 0 |
| receipts-7 | c7356e9a04f4 | 2 / 0 / 0 | 201 / 0 |
| receipts-8 | c7356e9a04f4 | 3 / 0 / 0 | 134 / 30 |
| apply-gate | c7356e9a04f4 | 13 / 0 / 0 | 317 / 0 |
| host-proof | c7356e9a04f4 | 15 / 0 / 0 | 61 / 0 |
| native-other | c7356e9a04f4 | 2 / 0 / 0 | 114 / 0 |
| native-refusals | c7356e9a04f4 | 1 / 0 / 0 | 167 / 150 |
| guards | c7356e9a04f4 | 165 / 1 / 1 | 416 / 0 |
| roster-guards | 3b04a2dbfdd9 | 85 / 0 / 1 | 423 / 0 |


The platform skip is WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact: CARD-1210: the child skip-roster audit requires Windows placement. This Linux run does not discharge that native Windows audit; its frozen runtime selection was not changed.

Each test row used --timeout 570s and stayed below 570 seconds of leased execution. Queue time is separate. Every build/test driver used scripts/build-slot.ps1; TUnit used dotnet run, native default output, --no-build and UseAppHost=false matching its producer. Tracked product and test inputs remained frozen during active builds/tests.

Build provenance:
- base build: 48a4615dcb4d67033886a3e31629dc7351ed0852, 0 errors, MSBuild 00:09:06.06; native default output / UseAppHost=false, BUILD SLOT held=552s.
- red-build: 4428b2b5bfb86600b7c146612478507410aee6fc, exit 0, MSBuild 00:09:20.39, total wall 580.73s.
- index-driver-build: c7356e9a04f422318f42b302ee9ede0e4dc3a26f, exit 0, MSBuild 00:00:06.01, total wall 25.40s.
- green-build: c7356e9a04f422318f42b302ee9ede0e4dc3a26f, exit 0, MSBuild 00:10:36.69, total wall 639.64s.
- roster-driver-build: 3b04a2dbfdd9844d675bc949ccaebe785bfc51d4, exit 0, MSBuild 00:00:02.13, total wall 4.76s.
- roster-build: 3b04a2dbfdd9844d675bc949ccaebe785bfc51d4, exit 0, MSBuild 00:10:55.78, total wall 658.56s.

The 10m36.69s and 10m55.78s test-project builds exceeded the requested ten-minute window under the oversubscribed host (CARD-1906); this is an explicit performance overrun, not a qualifying under-ten-minute build claim. All test selections themselves remained bounded. Recorded /proc/loadavg samples include initial 3.16 4.70 4.93 and loaded 49.49 38.11 27.58. Every row receipt retains its starting load, command, source, wall time, TRX per-test durations and phase directory. Expensive native preparation is shared per TUnit session. Unknown phase attribution remains unknown. Single-run duration samples are provisional, not a measured p95; no retry or timeout widening was used to mask a failure. The aggregate requested verification cost and historical failed runs remain visible in these receipts.

The derived-index driver was built first at both source tips. index-first/index-second and roster-index-first/roster-index-second all printed derived-impact-index unchanged. There is no index content change to commit. Pins verified unchanged:

- policyHash: c2e3939821a8fef911600c44be767b951c8212bcb62824cb746e48c5aaba6909
- PinnedInputDigest (recomputed over protected normalized inputs): 2f48120a65280582b586f9369e2eaf6b1bdc464a2362c8e49d434bd852479f60
- LegacyExclusionDigest: 6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a
- C1729: VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3

bash -n passed for c1008-retirement.sh and c590-remote.sh; node --check passed for the real-case fixture. Mutation was waived by the operator on 2026-11-12; no Mutation/PC execution is claimed.

## Rerun and evidence locations

Raw logs, source-bound JSON receipts, fresh TRX and per-test/native phase evidence are ignored under /work/worktrees/ws-01-d2/.antiphon/c1956/<row>/. The evidence is generated and is not committed. The stored summary preserves the essential outcomes and provenance. static-summary.json validates final unique coverage and pins. The final caller report supplies the pushed report tip, evidence-diff and merge-tree result.

Build with the host gate and native default output; run a selected row using its exact filter below:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1956-build -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1956-n3 -- dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/VolumeRetirementDockerTests/Exact_deletion_and_crash_resume_preserve_other_volumes' --timeout 570s --report-trx --report-trx-filename results.trx --results-directory .antiphon/c1956-rerun-n3
```

Exact row filters (tab-separated):

```text
green-focused	/*/*/VolumeRetirementReceiptTests/(Sigkill*)|(Resume_refuses*)|(Missing_lifecycle*)|(Failed_refused*)
green-native-n3	/*/*/VolumeRetirementDockerTests/Exact_deletion_and_crash_resume_preserve_other_volumes
receipts-1	/*/*/VolumeRetirementReceiptTests/(Preview_receipt_is_required_and_consumed)|(Receipt_shape_custody_and_digest_are_strict)|(Proof_directory_custody_matches_the_applying_account)|(Volume_incarnation_and_operation_are_bound)
receipts-2	/*/*/VolumeRetirementReceiptTests/(Freshness_and_policy_version_are_rechecked)|(Legacy_receipts_are_history_only)
receipts-3	/*/*/VolumeRetirementReceiptTests/(Manual_attestation_is_exact_and_cannot_waive_findings)
receipts-4	/*/*/VolumeRetirementReceiptTests/(Manual_attestation_public_entry_preserves_all_guards)
receipts-5	/*/*/VolumeRetirementReceiptTests/(Changed_content_and_remote_proof_refuse)|(Every_delete_rechecks_full_live_guards)|(Lock_order_excludes_supported_writers)
receipts-6	/*/*/VolumeRetirementReceiptTests/(Journal_intents_and_partial_resume_are_exact)
receipts-7	/*/*/VolumeRetirementReceiptTests/(Journal_intents_and_partial_resume_are_exact_replacement)|(Journal_intents_and_partial_resume_are_exact_close)
receipts-8	/*/*/VolumeRetirementReceiptTests/(Ready_publication_requires_file_and_directory_durability)|(Large_ready_rendering_precedes_the_unchanged_final_fence)|(Late_ready_fence_refusal_invalidates_apply_admission)
apply-gate	/*/*/VolumeRetirementApplyGateTests/*
host-proof	/*/*/VolumeRetirementHostProofTests/*
native-other	/*/*/VolumeRetirementDockerTests/(Read_only_namespace_and_host_proof_are_real)|(Replacement_and_concurrent_reference_block_delete)
native-refusals	/*/*/VolumeRetirementDockerTests/Replacement_and_unattributed_absence_refuse_resume
guards	/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)|(TestTimingOperationLabelTests*)|(ImpactedClassesSelectorTests*)/*
roster-guards	/*/*/(WindowsUnitPlatformAdmissionTests*)|(DerivedImpactIndexTests*)|(TestTimingOperationLabelTests*)|(ImpactedClassesSelectorTests*)/*
```

## WHY MISSED

Per the caller's Merge 248 record, that land did not run the native Docker shards. The cheapest existing pre-land row for this regression was N3. Its original shortcut also bypassed phase_begin/phase_end, so it could not demonstrate production lifecycle recovery; its initial missing-lifecycle refusal obscured the later SIGKILL resume failure. The revised N3 now admits actual lifecycle phases, and the new focused witness kills Apply through retire-temp-runner/c1008_recycle before resuming the same operation. Together they expose both the fixture gap and the product regression while retaining the round-1 fail-closed witness.

--- next stage ---
next: review
handoff: Full Review by a different company of the final pushed task tip; preserve the expected Linux Windows-only skip and historical failure/build costs.
artifact: /work/worktrees/ws-01-d2/docs/investigations/2026-11-16-card-1956-resume-after-sigkill.md
