Landed merge batch 95 on master at 5abd946dd2c235b6e873b0cbb95c738f35216dcf. The solution built with zero errors; all 150 required and conflict-specific tests passed. Two release-gate conflict hunks were combined without losing either fixture family.

Base: 955145f83ab717e3467a06cd192401eb13618129. Assigned branch: feat/card-task-7cb546f2. All eight source commits were selected from the '+' output of git cherry, applied in the commissioned order with cherry-pick -x, and retained as linear commits. Neither subject contained a report-only commit to omit. The red-first source history was preserved; only the final combined tree was built and tested.

Publication used the caller's existing operator authority to continue merge/land and skip a separate review for these test-side branches. The mandatory Git contract required publishing every commit to the assigned task branch, overriding the brief's incompatible local-only branch instruction. Every task-branch update was fast-forward-only. No rebase, reset, amend, merge commit, force-push, rc push or desktop action occurred. The report is committed and pushed to the task branch after the master push.

The final fetch observed unchanged master 955145f83ab717e3467a06cd192401eb13618129. git merge-tree --write-tree origin/master HEAD succeeded with tree d947d43a980e33df0aca3384eacb7aa5715da42c, equal to the candidate tree. The ordinary fast-forward push to refs/heads/master succeeded; git ls-remote confirmed 5abd946dd2c235b6e873b0cbb95c738f35216dcf. Publication attempts: 1. Re-pick/race loops: 0.

Per-commit patch proof uses git show <sha> | git patch-id --stable. A differing ID is explicitly recorded for each conflict adaptation.

| Land SHA | Source SHA | Source patch-id | Land patch-id |
|---|---|---|---|
| a46bc7f5570163b47ba7f8cbcfcc236c21960b6f | db0858bd4bcb4bbefb281adb998704ec34c1901e | 713fe9e13f9965fb77e354313266c668cb593be0 | f6de5d5a02e5e1e4eb4a0a60e6ebfacff6398925 |
| 9d722bd3c482604e573d4134dae3298a44b45be7 | 434bc6d1071f4e03b138d8780463f653d8e46cf9 | 0c0ee8290daec21f887992958b4872bbf035d33f | 0c0ee8290daec21f887992958b4872bbf035d33f |
| 6799d85c0d0f2a877c170b982b883110cff668bf | 142a1acd857c23f59374d02064a1fb60104a910a | 8400ab970b2bd572faf4d417081571421b6d5ac9 | 7cb3391376cd973a7a0e19bcfd27e02abdecf61a |
| e34089c765c31a0290f18cc84cec8cf2e3f894fc | b8e05d04408a8881611341616c1c368b01bcaa09 | 5a21da4e1c05e5dca995c30d34b9903683bfca01 | 5a21da4e1c05e5dca995c30d34b9903683bfca01 |
| e961e7bf1f20026dcb3eabf43e8e4fb61bb3a113 | 93396a85bface28b34dd15659a2c9a3108ca0633 | c1a4d1f6dc59d473989a703c0f1a3ed24c74d09a | c1a4d1f6dc59d473989a703c0f1a3ed24c74d09a |
| 09a82d3b68a259c2273f1be74730fcd16e302ca0 | 0bfaf02006f98c20bd82019950271242972bfe4b | afd27f8e171466097e1679ebe20c6a842a1ea995 | afd27f8e171466097e1679ebe20c6a842a1ea995 |
| 0f18e4a030a5f2fde46486fe92271edb988f113c | 37e4b2639af4b11202075e92bdadf85cdbdf6262 | 48991a60e42f810406f9a57aa3d3dc0edb54f582 | 48991a60e42f810406f9a57aa3d3dc0edb54f582 |
| 5abd946dd2c235b6e873b0cbb95c738f35216dcf | dec066906a1f4656f2f1d334d975df61b07c2817 | df0464e390f4fc2c779e3aedaf4cbfaddc3b3e2c | df0464e390f4fc2c779e3aedaf4cbfaddc3b3e2c |

For the first pick, zero-context patch IDs (git show --format= --unified=0 | git patch-id --stable) are identical: 08036d4563d77a9888f5c565cd77a4017a4ed41e. Only surrounding context changed when the two adjacent fixture imports were retained.

For the third pick, the harness adaptation retains CARD-1731's outer timing finally and calls Complete-C1733FixtureRun inside it. That helper owns the original Stop-C599CardStub/Clear-C599FixtureReadOnly calls and CARD-1733's timing, so cleanup is called once. All other files have identical zero-context patch IDs when the harness and slow allowlist are excluded: 210f1206e9a6b8dc36498002395f69dcc7f9242e. The slow-allowlist addition preserves all four substantive lines; the source patch's additional empty separator was already present on master. No assertion or classification entry was dropped.

The combined harness resolution is:

```powershell
. (Join-Path $here 'fixtures/c1500-status.ps1')
. (Join-Path $here 'fixtures/c1731-snapshot.ps1')
. (Join-Path $here 'fixtures/c1733-allocator.ps1')
# ... unchanged case selection ...
try { & $fn } finally {
    $bodySeconds = $caseWatch.Elapsed.TotalSeconds
    $teardownWatch = [Diagnostics.Stopwatch]::StartNew()
    try { Complete-C1733FixtureRun }
    finally {
        # Existing CARD-1731 timing-record body retained in full.
    }
}
```

The all-cases branch also calls Complete-C1733FixtureRun. The exact snapshot tests and full allocator classes passed on this resolution. Both timing families produced 14 case records, covering the same 14 harness cases.

Verification source for every build/test/audit below: 5abd946dd2c235b6e873b0cbb95c738f35216dcf. Host: server2 runner container dfcc116be984, Debian GNU/Linux 12, linux-x64; SDK 10.0.403, test runtime .NET 9.0.22, TUnit 1.44.0. Each driver ran in the foreground through its own scripts/build-slot.ps1 lease. TEMP=/tmp, native default outputs, and UseAppHost=false were used for the solution build and every --no-build test run. Source stayed frozen throughout execution. No green row was rerun.

| Row | Executed / passed / failed / skipped | Execution time | Classes |
|---|---|---|---|
| Contract | 16 / 16 / 0 / 0 | 4.826 s | MechanicalPcContractTests 11; TaskPlatformGuidanceTests 5 |
| Full policy | 50 / 50 / 0 / 0 | 106.650 s | TestClassificationPolicyTests 39; TestClassificationGuardTests 1; SlowTestRegistryMergeTests 5; SlowTestTripwireTests 2; NativeLinuxPolicyWitnessTests 3 |
| Layering | 9 / 9 / 0 / 0 | 25.815 s | C1729LayeringGuardTests 9 |
| Allocator and conflict witnesses | 14 / 14 / 0 / 0 | 611.511 s | ReleaseTagAllocatorTests 7; C1733AllocatorFixtureTests 5; C599SnapshotTests 2 |
| Landing refusal | 61 / 61 / 0 / 0 | 67.793 s | AgentTaskLandHalfResetTests 61 |

The extra C599SnapshotTests row membership was necessary to verify both sides of the conflict. No other unlisted test selection ran. The known inherited AgentTaskLandBoundaryControlledTests and AgentTaskLandBoundaryTests regressions were excluded as commissioned and were not repaired. This Merge verification does not replace the source plans' deferred Unit qualification or pending SourceLanding positive controls, and does not claim a Full ordinary Review gate or native Windows qualification.

Exact tree filters, one separate dotnet run invocation per row:

```text
/*/*/(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)/*
/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)/*
/*/*/C1729LayeringGuardTests*/*
/*/*/(C1733AllocatorFixtureTests*)|(ReleaseTagAllocatorTests*)|(C599SnapshotTests*)/*
/*/*/AgentTaskLandHalfResetTests*/*
```

Build command: timeout 45m env TEMP=/tmp pwsh -NoProfile -File scripts/build-slot.ps1 -Label merge95-solution -SlotWaitMinutes 20 -- dotnet build Antiphon.sln -p:UseAppHost=false --nologo. It passed: 0 errors, 1961 warnings, 00:04:11.42. All five test invocations used dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter <row-filter> --report-trx --report-trx-filename <row>.trx --results-directory .antiphon/merge95/<row>. Their outer foreground bounds were 40m each (allocator 45m), including finite 20-minute admission allowances; no test's own timeout was changed. These are provisional merge-driver bounds under the caller's explicit generous-timeout brief, not measured p95 claims. The longest row took 10m12s, below the 30-minute Debug trigger.

Layering inventory, copied from the actual executed test:

```text
VIOLATIONS 0 LISTED 168 STALE 0 AMBIGUOUS 3
```

The three existing ambiguity diagnostics name Stage in CascadeService/WorkflowEngine and Context in AntiphonHub. They did not produce a violation or stale row.

Land graph: scripts/audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll, through a separate build-slot lease, exited 0. It used the evaluator built by the single solution build, approved both passes and removed its canary. No -Update ran. No mismatch warning was needed.

```text
digest=D5B302A94FD3B0C28012C6764A2027B8B8DDE1D04506B3A641DD671EFB6F8C03 count=69
phase=pass2
approved
```

The graph reports five existing projects outside the selected solution build: card-0462/Reproduce, c1375-probe, c487-probe, Antiphon.TimeoutAudit.Tests and Antiphon.NativeLinuxMigrationAudit. No selected build definition, target pin, production file, startup path or live-lane protected input changed in this batch. tests/test-execution-policy.json was unchanged: policyHash stayed f2716a2ed5a3f7e06301acfaea9a1dbf89a2f1aaca4ec2e8e75b014dcd5cbdca. PinnedInputDigest stayed 65629ab0a1f1e75c2dff12df4efb8e60b9000e574486c7764d4ba98a76adee4a. No pin refresh was required.

Evidence-history check before publication:

```text
EVIDENCE result commits=8 entries=0 violations=0 base=955145f83ab717e3467a06cd192401eb13618129 head=5abd946dd2c235b6e873b0cbb95c738f35216dcf
```

Raw logs and fixture/phase timings remain ignored under .antiphon/merge95. Per-test outcomes/durations and lease provenance are retained below before deleting the owned bin/obj and raw test-result directories as required. No pre-existing build/output directories existed at admission. No scratch worktree was created. Filesystem/Git/HTTP subphases absent from the fixtures remain unknown; lease wall time and nested phase time are different measurements. No restart or rc activation was requested or performed.

Lease provenance (unedited wrapper lines; admission was 0 seconds for every driver):

```text
BUILD SLOT granted lease=3fe8827d-be68-4adf-9ea3-cb77b5cf2d38 waited=0s maxcpucount=6
BUILD SLOT released lease=3fe8827d-be68-4adf-9ea3-cb77b5cf2d38 held=252s
BUILD SLOT granted lease=250f0d78-fd79-41f6-ac22-98e306bfb13c waited=0s maxcpucount=6
BUILD SLOT released lease=250f0d78-fd79-41f6-ac22-98e306bfb13c held=8s
BUILD SLOT granted lease=59859e89-7dbf-41d2-8258-921240430920 waited=0s maxcpucount=6
BUILD SLOT released lease=59859e89-7dbf-41d2-8258-921240430920 held=110s
BUILD SLOT granted lease=f71af526-c1bb-44ab-8d9c-bb942a7004eb waited=0s maxcpucount=6
BUILD SLOT released lease=f71af526-c1bb-44ab-8d9c-bb942a7004eb held=31s
BUILD SLOT granted lease=e7fde000-39af-4588-9751-f6b1b51dbdf3 waited=0s maxcpucount=6
BUILD SLOT released lease=e7fde000-39af-4588-9751-f6b1b51dbdf3 held=615s
BUILD SLOT granted lease=aa43b15c-4f48-4930-9110-d362efa6f929 waited=0s maxcpucount=6
BUILD SLOT released lease=aa43b15c-4f48-4930-9110-d362efa6f929 held=75s
BUILD SLOT granted lease=c9ef5aaa-3be0-4304-9ce7-3f30e6004bd0 waited=0s maxcpucount=6
BUILD SLOT released lease=c9ef5aaa-3be0-4304-9ce7-3f30e6004bd0 held=27s
```

Per-test audit metadata extracted from the fresh TRX files before cleanup. Every entry belongs to source 5abd946dd2c235b6e873b0cbb95c738f35216dcf on the host recorded above. Duration is the test result duration, not a sum of nested spans.

Row contract; start 2026-11-06T16:45:48.9424176Z; finish 2026-11-06T16:45:53.6533402Z.

| Class | Test | Duration | Outcome |
|---|---|---|---|
| Antiphon.Tests.Application.MechanicalPcContractTests | C479_R06_LowNeverDiscoversAndTierRowsStayLow | 00:00:00.0838474 | Passed |
| Antiphon.Tests.Application.MechanicalPcContractTests | C479_V05_MutationBundleFailsClosedAtLow | 00:00:00.0717976 | Passed |
| Antiphon.Tests.Application.MechanicalPcContractTests | C479_V05b_CodePreparesUnboundCandidates | 00:00:00.0719918 | Passed |
| Antiphon.Tests.Application.MechanicalPcContractTests | C479_V05c_ReviewJudgesAdequacyWithoutMutants | 00:00:00.0721764 | Passed |
| Antiphon.Tests.Application.MechanicalPcContractTests | C479_V06_OwnerDocHoldsTheContract | 00:00:00.0315262 | Passed |
| Antiphon.Tests.Application.MechanicalPcContractTests | C479_V06b_WorkedExamplesAreIllustrative | 00:00:00.0236314 | Passed |
| Antiphon.Tests.Application.MechanicalPcContractTests | C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow(·claude/skills/antiphon-delegate/SKILL·md) | 00:00:00.0431005 | Passed |
| Antiphon.Tests.Application.MechanicalPcContractTests | C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow(docs/orchestration-loop·md) | 00:00:00.0484495 | Passed |
| Antiphon.Tests.Application.MechanicalPcContractTests | C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow(server/Bundles/delegate-basics·md) | 00:00:00.0239364 | Passed |
| Antiphon.Tests.Application.MechanicalPcContractTests | C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow(server/Bundles/orchestrator·md) | 00:00:00.0893982 | Passed |
| Antiphon.Tests.Application.MechanicalPcContractTests | C479_V07_ActiveRecipesRouteBindingHighAndExecutionLow(server/Bundles/README·md) | 00:00:00.0451557 | Passed |
| Antiphon.Tests.Application.TaskPlatformGuidanceTests | Orchestration_guidance_keeps_inheritance_and_task_scoped_unpinning | 00:00:00.0237964 | Passed |
| Antiphon.Tests.Application.TaskPlatformGuidanceTests | Review_guidance_keeps_scope_examples_landing_evidence_and_runner_routes | 00:00:00.0235618 | Passed |
| Antiphon.Tests.Application.TaskPlatformGuidanceTests | Review_guidance_preserves_every_instruction | 00:00:00.1551849 | Passed |
| Antiphon.Tests.Application.TaskPlatformGuidanceTests | Stage_bundles_leave_twenty_characters_below_the_size_cap | 00:00:00.0237495 | Passed |
| Antiphon.Tests.Application.TaskPlatformGuidanceTests | Stage_guidance_omits_platform_unless_needed_and_preserves_runner_routes | 00:00:00.0235132 | Passed |

Row policy; start 2026-11-06T16:46:04.1466268Z; finish 2026-11-06T16:47:50.6136227Z.

| Class | Test | Duration | Outcome |
|---|---|---|---|
| Antiphon.Tests.TestHelpers.SlowTestTripwireTests | allowlist_file_exists_and_has_spawn_lane_entries | 00:00:00.0429022 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Detached_reason_is_rejected(blank) | 00:00:00.8285695 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Detached_reason_is_rejected(eof) | 00:00:01.2625538 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Detached_reason_is_rejected(overwritten) | 00:00:01.0006294 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Header_after_records_is_rejected(between-pairs) | 00:00:01.9167889 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Header_after_records_is_rejected(interrupts-first-pair) | 00:00:01.9904661 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Layout_opt_in_is_scoped(non-repository) | 00:00:01.6285345 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Layout_opt_in_is_scoped(other-assembly) | 00:00:00.0014498 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_ReadRegistry_retains_legacy_layout_behavior | 00:00:00.0014189 | Passed |
| Antiphon.Tests.Infrastructure.SlowTestRegistryMergeTests | C1383_Rebase_preserves_distinct_appended_pairs(common-base) | 00:00:05.3571279 | Passed |
| Antiphon.Tests.Infrastructure.SlowTestRegistryMergeTests | C1383_Rebase_preserves_distinct_appended_pairs(target-only) | 00:00:04.9239975 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Row_without_adjacent_reason_is_rejected(bare-row) | 00:00:02.5505953 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Row_without_adjacent_reason_is_rejected(second-row) | 00:00:01.7386620 | Passed |
| Antiphon.Tests.Infrastructure.SlowTestRegistryMergeTests | C1383_Source_only_attribute_does_not_rescue_first_rebase | 00:00:01.3985292 | Passed |
| Antiphon.Tests.Infrastructure.SlowTestRegistryMergeTests | C1383_Union_collapsed_lines_are_rejected(different-reasons-same-class) | 00:00:05.1265712 | Passed |
| Antiphon.Tests.Infrastructure.SlowTestRegistryMergeTests | C1383_Union_collapsed_lines_are_rejected(same-reason-different-classes) | 00:00:04.9518076 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Valid_reason_pairs_are_accepted( , False) | 00:00:03.2686635 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Valid_reason_pairs_are_accepted( , True) | 00:00:03.2703502 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Valid_reason_pairs_are_accepted( , False) | 00:00:04.0216954 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C1383_Valid_reason_pairs_are_accepted( , True) | 00:00:03.0056273 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G059 | 00:00:01.2319132 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G060 | 00:00:00.6247311 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G061 | 00:00:02.1901840 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G062(duplicate-fqn) | 00:00:01.2256935 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G062(shared-simple) | 00:00:00.6230671 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G063 | 00:00:01.2471527 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G064 | 00:00:04.8420589 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G065 | 00:00:09.4007916 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G066 | 00:00:10.1687428 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G067(inherits) | 00:00:09.3750085 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G067(no-inherits) | 00:00:08.3852697 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G068 | 00:00:01.0340720 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G069(optin) | 00:00:09.4367376 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G069(ordinary) | 00:00:07.9720195 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G070 | 00:00:00.0015650 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G071 | 00:00:00.0002124 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G072 | 00:00:00.0003794 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G073(full) | 00:00:00.9614813 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G073(simple) | 00:00:00.5839596 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G074 | 00:00:00.9592419 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G075 | 00:00:01.0479560 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G076(empty) | 00:00:00.8496363 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G076(malformed) | 00:00:00.9456070 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | C487_G076(missing) | 00:00:00.8178095 | Passed |
| Antiphon.TestSupport.NativeLinuxPolicyWitnessTests | Markers_do_not_expand_policy_exclusions | 00:00:05.1961730 | Passed |
| Antiphon.Tests.TestHelpers.TestClassificationPolicyTests | Missing_slow_class_is_named | 00:00:00.6183154 | Passed |
| Antiphon.TestSupport.NativeLinuxPolicyWitnessTests | Pinned_attribute_contract_matches_native_platform | 00:00:00.8693672 | Passed |
| Antiphon.TestSupport.NativeLinuxPolicyWitnessTests | Policy_rows_require_pinned_attribute | 00:00:05.1960626 | Passed |
| Antiphon.TestSupport.TestClassificationGuardTests | Registry_matches_compiled_metadata | 00:00:02.7792055 | Passed |
| Antiphon.Tests.TestHelpers.SlowTestTripwireTests | unlisted_slow_test_is_a_hit_and_listed_fast_or_allowed_slow_is_not | 00:00:00.0377073 | Passed |

Row layering; start 2026-11-06T16:48:11.7126114Z; finish 2026-11-06T16:48:37.4364989Z.

| Class | Test | Duration | Outcome |
|---|---|---|---|
| Antiphon.Tests.Infrastructure.C1729LayeringGuardTests | Allowed_edges_listed_rows_comments_and_strings_do_not_fire | 00:00:00.0164486 | Passed |
| Antiphon.Tests.Infrastructure.C1729LayeringGuardTests | Allowlist_parses_with_grouped_rows_and_a_mode_header | 00:00:00.0007890 | Passed |
| Antiphon.Tests.Infrastructure.C1729LayeringGuardTests | Ambiguous_simple_names_resolve_by_using_or_are_listed | 00:00:00.0026129 | Passed |
| Antiphon.Tests.Infrastructure.C1729LayeringGuardTests | Layer_of_pinned_files_follows_first_match | 00:00:00.0080837 | Passed |
| Antiphon.Tests.Infrastructure.C1729LayeringGuardTests | Map_covers_every_server_file_exactly_once | 00:00:00.0067343 | Passed |
| Antiphon.Tests.Infrastructure.C1729LayeringGuardTests | Repository_graph_satisfies_the_committed_allowlist | 00:00:00.0030371 | Passed |
| Antiphon.Tests.Infrastructure.C1729LayeringGuardTests | Stale_row_and_unmapped_file_fail_in_strict_mode | 00:00:00.0011836 | Passed |
| Antiphon.Tests.Infrastructure.C1729LayeringGuardTests | Synthetic_core_file_referencing_an_application_type_fails_in_strict_mode | 00:00:00.0092690 | Passed |
| Antiphon.Tests.Infrastructure.C1729LayeringGuardTests | Warn_mode_prints_the_inventory_and_fails_only_on_map_or_parse_errors | 00:00:00.0465405 | Passed |

Row allocator; start 2026-11-06T16:48:53.9820413Z; finish 2026-11-06T16:59:05.3783689Z.

| Class | Test | Duration | Outcome |
|---|---|---|---|
| Antiphon.Tests.Scripts.C1733AllocatorFixtureTests | Boundaries_and_changed_links_are_checked_fresh | 00:00:03.6357371 | Passed |
| Antiphon.Tests.Scripts.ReleaseTagAllocatorTests | C1339_AllocationUtcDay | 00:00:53.9129722 | Passed |
| Antiphon.Tests.Scripts.ReleaseTagAllocatorTests | C1339_ExactRemoteRefs | 00:01:08.6898415 | Passed |
| Antiphon.Tests.Scripts.ReleaseTagAllocatorTests | C1339_PromoteAuthorityHash | 00:01:09.7278250 | Passed |
| Antiphon.Tests.Scripts.ReleaseTagAllocatorTests | C1339_PromoteFreshReservations | 00:02:01.4434567 | Passed |
| Antiphon.Tests.Scripts.ReleaseTagAllocatorTests | C1339_PromoteReservationAndTarget | 00:00:52.9631882 | Passed |
| Antiphon.Tests.Scripts.ReleaseTagAllocatorTests | C1339_PromoteResumedReservations | 00:01:17.2999649 | Passed |
| Antiphon.Tests.Scripts.ReleaseTagAllocatorTests | C1339_RemoteTagAllocation | 00:01:04.6678201 | Passed |
| Antiphon.Tests.Scripts.C599SnapshotTests | Finally_restores_after_a_throwing_mutation | 00:00:17.4628380 | Passed |
| Antiphon.Tests.Scripts.C1733AllocatorFixtureTests | Native_metadata_avoids_provider_operations | 00:00:03.8946301 | Passed |
| Antiphon.Tests.Scripts.C599SnapshotTests | Restore_runs_once_and_only_writes_changed_bytes | 00:00:08.7603974 | Passed |
| Antiphon.Tests.Scripts.C1733AllocatorFixtureTests | Shared_seed_keeps_recipients_and_authority_private | 00:00:32.9793589 | Passed |
| Antiphon.Tests.Scripts.C1733AllocatorFixtureTests | Snapshot_bytes_work_and_restore_failures_are_observed | 00:00:14.7202905 | Passed |
| Antiphon.Tests.Scripts.C1733AllocatorFixtureTests | Throwing_reservation_mutation_restores_the_control | 00:00:16.5062538 | Passed |

Row landing; start 2026-11-06T16:59:23.8077742Z; finish 2026-11-06T17:00:31.5452766Z.

| Class | Test | Duration | Outcome |
|---|---|---|---|
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C1715_Initial_adoption_preserves_untrusted_status_reason | 00:00:00.9561628 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C1715_Post_reset_status_error_preserves_diagnostic(False) | 00:00:00.8070422 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C1715_Post_reset_status_error_preserves_diagnostic(True) | 00:00:01.3188403 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C1715_Resumed_adoption_preserves_untrusted_status_reason | 00:00:01.3583928 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C1715_Resumed_dirty_checkout_preserves_adopt_local_changed | 00:00:01.2271393 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_AuthorityChangesBeforeResetRefuse(fingerprint) | 00:00:00.6482675 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_AuthorityChangesBeforeResetRefuse(owner) | 00:00:00.5822159 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_AuthorityChangesBeforeResetRefuse(remote) | 00:00:00.8055906 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_AuthorityChangesBeforeResetRefuse(review-clean-false) | 00:00:00.8028889 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_AuthorityChangesBeforeResetRefuse(review-clean-null) | 00:00:00.8599169 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_AuthorityChangesBeforeResetRefuse(review-sha) | 00:00:01.1056761 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_AuthorityChangesBeforeResetRefuse(review-superseded) | 00:00:00.6480188 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_CompletedResetIsIdempotent | 00:00:09.4699418 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_ContentChangesBeforeResetRefuse(ignored) | 00:00:02.5756820 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_ContentChangesBeforeResetRefuse(index) | 00:00:01.7075446 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_ContentChangesBeforeResetRefuse(local-pin) | 00:00:03.6985768 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_ContentChangesBeforeResetRefuse(source-pin) | 00:00:02.8048829 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_ContentChangesBeforeResetRefuse(tracked) | 00:00:01.6360257 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_ContentChangesBeforeResetRefuse(untracked) | 00:00:02.0431764 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_CurrentPendingRequestCannotBeStolen | 00:00:10.9816639 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_DirtyFreshRequestAtOldHeadKeepsSourceDirty | 00:00:01.0003892 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FilemodeFalseExecutableAdoptsAndRepairs(False) | 00:00:02.1443371 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FilemodeFalseExecutableAdoptsAndRepairs(True) | 00:00:03.7679164 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FreshRequestRejectsWitnessIdentity(ambiguous) | 00:00:08.3419719 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FreshRequestRejectsWitnessIdentity(common-directory) | 00:00:00.7903001 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FreshRequestRejectsWitnessIdentity(fingerprint) | 00:00:11.4608928 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FreshRequestRejectsWitnessIdentity(local-pin) | 00:00:01.1688955 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FreshRequestRejectsWitnessIdentity(owner) | 00:00:01.0859872 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FreshRequestRejectsWitnessIdentity(ref) | 00:00:01.3374174 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FreshRequestRejectsWitnessIdentity(sha) | 00:00:01.0885803 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FreshRequestRejectsWitnessIdentity(source-identity) | 00:00:11.4651643 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FreshRequestRejectsWitnessIdentity(source-pin) | 00:00:01.1351164 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FreshRequestRejectsWitnessIdentity(worktree) | 00:00:00.9449316 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FreshRequestRepairsPinnedAncestor(False) | 00:00:03.9558933 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FreshRequestRepairsPinnedAncestor(True) | 00:00:05.0131698 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FreshRequestRequiresAncestor | 00:00:01.4059285 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FreshRequestRequiresInterruptedWitness(absent) | 00:00:02.4372132 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FreshRequestRequiresInterruptedWitness(already-adopted) | 00:00:02.3552337 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FreshRequestRequiresInterruptedWitness(not-started) | 00:00:02.4114752 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_FreshRequestRequiresInterruptedWitness(wrong-operation) | 00:00:02.0021192 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_IdentityChangesBeforeResetRefuse(branch) | 00:00:00.6660115 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_IdentityChangesBeforeResetRefuse(head) | 00:00:00.8833237 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_IdentityChangesBeforeResetRefuse(registration) | 00:00:01.1438584 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_IgnoredFileDoesNotHalfResetOrdinaryAdoption | 00:00:04.6434829 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_PostResetMismatchCannotPublish(dirty) | 00:00:00.8086205 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_PostResetMismatchCannotPublish(wrong-head) | 00:00:11.3493952 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_PreRefDirtyRefusalLeavesRefAndCheckoutAtOldTip | 00:00:02.0057043 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_StagedEditSurvivesFreshAndSameRequest(False) | 00:00:04.7955127 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_StagedEditSurvivesFreshAndSameRequest(True) | 00:00:04.0859031 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_StaleIndexSizeCrLfRewriteRefusesBeforeAdoption | 00:00:01.2891407 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_UncertainPriorChildRefuses(live-journal) | 00:00:04.6651743 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_UncertainPriorChildRefuses(live-request) | 00:00:01.3398188 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_UncertainPriorChildRefuses(unknown-journal) | 00:00:01.7222622 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_UncertainPriorChildRefuses(unknown-request) | 00:00:02.2222550 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_UnstagedEditSurvivesFreshAndSameRequest(False) | 00:00:01.4177822 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_UnstagedEditSurvivesFreshAndSameRequest(True) | 00:00:02.7632458 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_UntrackedAndIgnoredArePreserved(False) | 00:00:03.4627620 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C883_UntrackedAndIgnoredArePreserved(True) | 00:00:02.8916556 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C954_IgnoredResidueAllowsHalfResetPublication(False) | 00:00:05.1567385 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | C954_IgnoredResidueAllowsHalfResetPublication(True) | 00:00:05.0867939 | Passed |
| Antiphon.Tests.Application.AgentTaskLandHalfResetTests | Windows_registration_path_matches_git_porcelain | 00:00:00.0021622 | Passed |

CARD-1731 timing for the combined teardown path (actual harness-body seconds; native test launch/outer teardown overhead is outside this body):

| Case | Body seconds | Teardown seconds |
|---|---|---|
| C1339_AllocationUtcDay | 47.249384 | 1.762255 |
| C1339_ExactRemoteRefs | 50.618247 | 12.722096 |
| C1339_PromoteAuthorityHash | 62.39339 | 2.811358 |
| C1339_PromoteFreshReservations | 114.928362 | 1.398254 |
| C1339_PromoteReservationAndTarget | 43.18739 | 4.697387 |
| C1339_PromoteResumedReservations | 71.3561 | 1.539223 |
| C1339_RemoteTagAllocation | 55.619694 | 4.288496 |
| C1731_SnapshotIsolation | 10.433572 | 0.309975 |
| C1731_SnapshotWork | 3.833293 | 0.276693 |
| C1733_BoundariesAndFreshLinks | 0.265025 | 0.065768 |
| C1733_NativeMetadata | 0.161476 | 0.110182 |
| C1733_ReservationFinally | 10.627309 | 0.847102 |
| C1733_SharedSeed | 18.17737 | 3.327015 |
| C1733_SnapshotWork | 7.330502 | 2.102279 |

Cleanup completed after all foreground drivers joined: 104 bin/obj directories matched the task creation inventory and were removed using explicit literal absolute paths; the five raw test-result directories were removed after preserving the metadata above. Final census: zero bin/obj/test-result directories. No pre-existing output or tracked source was deleted. The cleanup and append scripts remain ignored evidence beside their logs.

--- next stage ---
next: done
handoff: Master publication confirmed at 5abd946dd2c235b6e873b0cbb95c738f35216dcf; no decision or approval remains for this merge batch.
artifact: /work/worktrees/task-7cb546f2/.antiphon/task-7cb546f2.md
