Batch 83 landed on master at 534e3e66aa061e88eff4658fb5a668b576d009d1. All requested checks completed: 285 tests passed, the three explicitly accepted inherited failures were the only reds, and none were skipped.

Base: 388ff482046a62f813a251208bfeabe7c21808ea. Landing loop count: 1; re-pick loops: 0; push retries: 0. The final fetch still named the original base. The ordinary fast-forward push succeeded and git ls-remote confirmed the new master. No rc ref was pushed and no running service was activated. Standing authority covered continuing the merge and land without another permission request.

The four branches were processed in the requested order using git cherry against origin/master, followed by cherry-pick -x for each content commit. Each pick was pushed to feat/card-task-a3a1bbee. The two report-only commits b45390d13737abfd8b4fe3bac4a82d7f53bbf30c and c2a89688c81408c97b8df8ee2d11bb6a83479346 were skipped. Mixed code/report commits were preserved intact. Plan and mutation deliverables were retained, consistent with earlier batches. No conflict or source repair was needed. CARD-1723 remains query-only; this batch adds no migration.

Every landed patch ID equals its source patch ID, computed independently with git show --format= and git patch-id --stable:

| Land SHA | Source SHA | Stable patch ID |
|---|---|---|
| 527029d2ed2adf1179a37074f840b61f55ad569c | 5ad2a941c520a2d5175840af58706b7a6bb305bc | 069995e3b95aa5265dd3d33f33f56731efa4b34f |
| 502257c9a3f340fd9c4773c7504ed3ee976774ce | 13c26a2ee0043ff8d5f968eff4e03c1ba08e1efb | cfe99a3496b623a05b5831837711824566c8a461 |
| db2dd1b4615fcb7a3d260c2fde1922edd9bfae7c | 3caf83cb1a0befc099c8cbd271aa88d277aefaac | ad3c3b21b92df71567c99fabf59137b0934ebd2b |
| 84a480040994a596992d38028b260671b2b4f95f | c06daa462dd9a4bd0bc60efa83fd33d020e4a7fb | 986472d0f394f04eea1a543301757808691cde2b |
| 399f6d58a6a122528cce903d2e1c360f599119b1 | 071a10d1ce3ff8b30543fe0f0cd44332c798bf94 | 7a0804077f7f03705941a186af2fe3ad7acbedc3 |
| 58ee29fd378a5a59767ccdce13320230a1159174 | 720e9a6b56601356cb8ff0f08e9f436c169463c8 | 6204cba1ae89f56614ee2679c2bdb6db06bc8aa4 |
| 4cd02a4b5baf047982116845958066f9ec431aa6 | 1b8bbf695b61d77db0cda8d80c50d06030aae668 | 15e00c90bb2fe15a1bc712e933db9759d3ccc18e |
| 721ea90312afb6ea19fc2efd1fd872d5b7948957 | 7aa5aac5fd994f3e6943d26dcc35f1ef2f861d39 | cbd6c97984359c7ef86a2d63376be8992b146bc4 |
| 91a649074e888596eaf18c3f312bd016ea948abc | 1e5e9d6f9629d3ce5c05e6f14a6154dee181be6b | 847f163ebe9d8fcaedc97d7b0941671394947691 |
| cff07f97eb570e84b67f1e4fbb271124f40b40bd | 018dc40f6716da355c1e3bc0bbd72b2a499f1d6d | 0a0fb6a2dc63b4bacd0d20dc23d5181cee7ef0ff |
| 6a8738b4fe4314f6662cb5558b92af5ec82cfda2 | 750af1ab99b13cd78bc4b14b5017f0e36eb7833c | 558a9b32ac8f4b1a9422dc1c3fa2749ea112011d |
| 534e3e66aa061e88eff4658fb5a668b576d009d1 | e33acee6b3d26391b293b21a072f9485f574b754 | 9f7411ff5968a89b26db8b53ffcf6b8223a8213c |

The final-tree evidence guard reported commits=12, entries=2, violations=0 over base..534e3e66aa061e88eff4658fb5a668b576d009d1. git diff --check passed. git merge-tree --write-tree origin/master HEAD exited 0 and returned 490a4ab2517ba7caf2218938313b0599727d4cb6, equal to the candidate tree.

Both base and final tree produced these identical protected hashes:

- scripts/test-live-lane-run.ps1 -PrintInputDigest: 836bbee0716085870363580397052a9f545e072facc92912695a6884278474b7
- Get-NightlyPolicyHash: 5c7f5f061c21284a1e7bee0b18ac3cfd6cd7b3a51e36d6db450f5569628aaad4

The real scripts/audit-land-verification-graph.ps1 -Check approved both base and final tree. Each printed digest=D5B302A94FD3B0C28012C6764A2027B8B8DDE1D04506B3A641DD671EFB6F8C03 count=69, phase=pass2, approved. This matches the base's committed manifest and evaluation record. -Update was never used. The initial base invocation matched the precheck digest but refused because the audit tool was not built; a justified, separately leased native-output tool bootstrap passed with zero warnings/errors, then the complete base audit passed. That bootstrap waited 45 seconds for its slot and held it for 8 seconds.

The single final solution build used TEMP=/tmp, a host lease, native output, UseAppHost=false, and -m:1. It passed with 0 errors and 1,853 warnings in 10m58.33s. Its slot wait was 0 seconds and lease held time was 659 seconds. No NETSDK1047 rebuild was needed. Every test run below used that exact source/build, --no-build --no-restore, the same UseAppHost=false property, a separate host lease, an explicit 20m test timeout, and an outer 30m limit. Tests ran sequentially on the runner; no desktop execution occurred.

| Group | Selection | Total | Passed | Failed | Skipped | Test duration | Lease held |
|---|---|---:|---:|---:|---:|---|---|
| a | MechanicalPcContractTests*, TaskPlatformGuidanceTests* | 16 | 15 | 1 expected | 0 | 5.840s | 12s |
| b | TestClassificationPolicy*, TestClassificationGuard*, SlowTestRegistryMerge*, SlowTestTripwire*, NativeLinuxPolicyWitnessTests* | 50 | 48 | 2 expected | 0 | 83.887s | 89s |
| c | RunSchedulerTests*, CheckpointCallerBudgetTests* | 32 | 32 | 0 | 0 | 6.301s | 9s |
| d | C0824WorkspaceSlotCustodyTests* | 19 | 19 | 0 | 0 | 10.720s | 13s |
| e | GhostSeatReleaseTests*, TaskSupervisionVisibilityTests*, RunnerSlotEndpointTests* | 51 | 51 | 0 | 0 | 49.121s | 52s |
| f | ChannelBridge*, ChannelInboundRecoveryTests*, ChannelInputWithheldIncidentTests* | 120 | 120 | 0 | 0 | 207.245s | 210s |

Group d ran tests/Antiphon.SessionRunner.Tests; the other groups ran tests/Antiphon.Tests. Combined filters used /*/*/(ClassA*)|(ClassB*)/* with a star after every class name. The policy sweep covered all four requested classification/registry/tripwire prefixes and the full NativeLinuxPolicyWitnessTests class, including both known-red policy methods.

The only failed tests were:

- Antiphon.Tests.Application.MechanicalPcContractTests.C479_R06_LowNeverDiscoversAndTierRowsStayLow: composed TestDesign instructions lack the expected "binding required after Code" phrase.
- Antiphon.TestSupport.NativeLinuxPolicyWitnessTests.Policy_rows_require_pinned_attribute: the accepted inherited retention-method reverse-coverage gap.
- Antiphon.TestSupport.NativeLinuxPolicyWitnessTests.Markers_do_not_expand_policy_exclusions: the same accepted inherited reverse-coverage gap.

Both policy diagnostics named C1677RetentionServerTests.Ignored_nested_repository_secret_survives_real_transport and Ignored_secrets_survive_the_real_runner_and_a_lost_reply_replay. These match the brief's three expressly accepted master failures. The held repair 19b9e69513551259b7c56eec03b77ef3a4cc861c was not picked. No policy hash or instruction repair was made.

Rerun template after rebuilding the same code tip:

```sh
TEMP=/tmp timeout 40m pwsh -NoProfile -File scripts/build-slot.ps1 -Label merge83-solution -- dotnet build Antiphon.sln -p:UseAppHost=false --nologo -m:1
TEMP=/tmp timeout 30m pwsh -NoProfile -File scripts/build-slot.ps1 -Label merge83-selection -- dotnet run --project tests/Antiphon.Tests --no-build --no-restore -p:UseAppHost=false -- --treenode-filter '/*/*/(ClassA*)|(ClassB*)/*' --timeout 20m --report-trx --report-trx-filename run.trx --results-directory /tmp/merge83-selection
```

Use the exact class groups above and the SessionRunner project for d, one lease per run. Full logs, patch-ID receipts, and the parsed six-group summary are under /work/worktrees/task-a3a1bbee/.antiphon/merge83/. Owned bin/obj roots and six TRX/result directories were removed after verification. No scratch worktree remains from this task. This report is a separate report-only descendant on the assigned branch, not part of the published master code tip.

--- next stage ---
next: done
handoff: Batch 83 is published to master; only the three expressly accepted inherited failures remain. RC publication and activation remain outside this merge.
artifact: /work/worktrees/task-a3a1bbee/.antiphon/task-a3a1bbee.md
