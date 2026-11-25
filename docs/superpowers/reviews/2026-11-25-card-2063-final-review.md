# CARD-2063 Final Review, round 1

Return to Code. The native Linux witness reproduced the original failure and passed at the reviewed tip. Three findings prevent landing.

| Where | Failure | Why | Fix |
|---|---|---|---|
| `src/Antiphon.Server.Git/LinuxOwnedGitMembers.cs` lines 81, 94, 116–122 and 142, `OwnedGitProcess.cs` line 276 | Cleanup can report `Joined` while a previously identified foreign-UID child is still alive. | Each scan creates a new member list. A denied signal can leave that child alive. After root exit and reparenting, the same child can have stable PID, start ticks, new parent and disjoint credentials. The denied environment read then leaves `unknown` clear, the root chain is gone, and the empty probe authorizes `Joined`. The new descendant test takes a single observation with the root present. | Preserve positive identity across cleanup passes until death or replacement is proved. Where ancestry can disappear before the first observation, establish invocation membership independently of current parent and UID. Add a two-pass regression that keeps the same unreadable child alive through root exit and also preserves the unrelated-UID success case. |
| `tests/test-execution-policy.json` lines 3 and 1529, `scripts/test-live-lane-run.ps1` line 28 | The edited Windows exclusion leaves the policy hash and live-lane input digest stale. Release selection refuses the policy. | The policy still declares `259e9888703af5fb7826351aeff12923e56d5d275de9967ab956c4de81e7deda`. Its canonical hash is `22c197130da98fb5c0e95e60e16535486fd506de9f9585ecfe95344fcdfa5b60` at the tip. The protected-input digest also matches its pin on the candidate base and differs at the tip. | Recompute `policyHash` with `scripts/antiphon live-lane-emit-policy-hash --policy-root .`. After updating the policy, recompute the live-lane pin with `pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest`. |
| `tests/Antiphon.Tests/Infrastructure/LinuxOwnedGitProcessTests.cs` line 158 | The timing recorder emits `omitted` for `foreign Git`. | The label is outside the allowed vocabulary. `C1620_EveryCallSiteOperationLabelIsKept` fails on this literal and passes on the candidate base. | Use the existing `spawn` operation label for the second process, or register a specific label with the timing recorder. |

The first finding follows from the cleanup and census code. The journal's production marker probe passes PID 0, so an unreadable candidate still holds that fence. The rooted adapter is exercised by the real native test. No production caller selects that adapter at this SHA. All positive controls, including the blind-skip mutation, remain pending for SourceLanding Mutation.

## Verification

The reviewed owner is `61a4ed85-aa4a-463b-b496-095d50588d66`. The range is `b3c8a192e0d89599e1a83956a4464991e3481500..6cf76ae77065145a350c647478ef6689c3f1cf03`. The original Debug brief named the witness and class and supplied no checkpoint table or CP identifiers. The Review manifest maps those selections, the Final guards and the three filters from `impacted-classes` to eight rows. The selector matched six kinds, zero migration/downgrade rows and zero Windows filters.

| Row | Selection | Passed | Failed | Skipped |
|---|---|---:|---:|---:|
| CP-1 | Linux census | 43 | 0 | 0 |
| CP-2 | Six requested guard classes | 102 | 0 | 1 |
| CP-3 | Remaining standing guards | 27 | 1 | 0 |
| CP-4 | Server Git contracts | 14 | 0 | 0 |
| CP-5 | Impacted classes, first group | 155 | 23 | 1 |
| CP-6 | Impacted classes, second group | 79 | 12 | 1 |
| CP-7 | Impacted classes, third group | 47 | 20 | 0 |
| CP-8 | Linux-only census and exclusion policy | 1 | 1 | 0 |
| **Total** | **45 classes, 525 executed** | **468** | **57** | **3** |

Run `20261124-233338-ddb7` took 27m 29s. Both builds and all eight rows obtained slots. The eight fresh TRXs contained 528 distinct result execution IDs. All three skips named Windows-only work. CP-1 executed all four reparenting arguments, all seven permission-denial arguments and the native other-UID Git witness. The validator returned `CHECKPOINT SOURCE INVALID reason=report_failed` for the whole run. All timestamped receipts and filter details are retained in `/work/review-evidence/7280f7d0-c0e4-4168-a360-a1bce7541fdf`.

The pre-fix witness at `5669ee72aea3a74b7e1fef55ff5d677ab6e74fba` executed once and failed at `Joined`, with `rootExited=True members=Unknown/0 reason=Deadline signals=1 duration=00:00:06.0015273`. The class passed at the tip. Five targeted candidate-base checks passed, with zero failures and skips. They cover the policy hash, the live-lane input digest, the timing label and the release-cut result. Both source builds and all three historical test invocations obtained slots. The checkpoint CLI had a separate gated bootstrap build. The card and impact queries used the repository's `scripts/antiphon` command launcher, which bootstraps its CLI.

`DerivedImpactIndexTests.committed_index_matches_the_builder` passed. No index update is indicated on this tip. On the eventual landing tree run `scripts/antiphon regenerate-derived-impact-index` and include the generated file if its bytes change. `scripts/antiphon evidence-diff` reported four commits, zero evidence entries and zero violations. `git merge-tree --write-tree` succeeded against fetched target `ac6f7a636a11fb870adde7579b6eca0a88e0f35c`, producing `46774511fba8cffeb9fe547ccc52de5f13d52a32`.

The failed aggregate receipt also refused validation when restricted to CP-2. To supply the commissioned six-class receipt, the standalone CP-2 row reused the exact source-stamped build and ran 102 tests successfully with the one rostered Windows skip. Its validator printed `CHECKPOINT SOURCE VALID source=6cf76ae77065145a350c647478ef6689c3f1cf03 rows=1`. Source qualification for the complete Review remains false.

## Checkpoint records

Full run `20261124-233338-ddb7`

```text
CHECKPOINT CP-1 commit=6cf76ae77065145a350c647478ef6689c3f1cf03 build=ok filter=/*/*/LinuxOwnedGitProcessTests/* executed=43 passed=43 failed=0 skipped=0 trx=/work/worktrees/ws-12/.antiphon/checkpoints/20261124-233338-ddb7/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=6cf76ae77065145a350c647478ef6689c3f1cf03 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=6cf76ae77065145a350c647478ef6689c3f1cf03 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(DerivedImpactIndexTests*)|(C1729LayeringGuardTests*)|(WindowsUnitPlatformAdmissionTests*)|(NativeLinuxPolicyWitnessMutationTests*)/* executed=102 passed=102 failed=0 skipped=1 trx=/work/worktrees/ws-12/.antiphon/checkpoints/20261124-233338-ddb7/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=6cf76ae77065145a350c647478ef6689c3f1cf03 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=6cf76ae77065145a350c647478ef6689c3f1cf03 build=reused filter=/*/*/(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(TestLaneCategoryGuardTests*)|(TestFixtureCleanupGuardTests*)/* executed=28 passed=27 failed=1 skipped=0 trx=/work/worktrees/ws-12/.antiphon/checkpoints/20261124-233338-ddb7/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=6cf76ae77065145a350c647478ef6689c3f1cf03 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=6cf76ae77065145a350c647478ef6689c3f1cf03 build=reused filter=/*/*/(AntiphonCliLauncherTests*)|(C1733AllocatorFixtureTests*)|(ChangeAwareReleaseTests*)|(ImpactedClassesSelectorTests*)|(LiveLaneCensusTests*)|(LiveLaneRunnerTests*)|(NightlyCoreGoldenTests*)|(NightlyRunParityTests*)|(NightlyScriptsTests*)|(QualificationTimingTests*)/* executed=178 passed=155 failed=23 skipped=1 trx=/work/worktrees/ws-12/.antiphon/checkpoints/20261124-233338-ddb7/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=6cf76ae77065145a350c647478ef6689c3f1cf03 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=6cf76ae77065145a350c647478ef6689c3f1cf03 build=reused filter=/*/*/(RcFailedFirstExecutionTests*)|(RcProjectSelectionTests*)|(RcRunEvidenceTests*)|(RcStoppedRunSeedTests*)|(ReleaseCutTests*)|(ReleaseGateAuthorityContractTests*)|(ReleaseGateFollowupTests*)|(ReleaseGatePublicationAuthorityTests*)|(ReleaseGateRunTests*)|(ReleaseGateStatusTests*)/* executed=91 passed=79 failed=12 skipped=1 trx=/work/worktrees/ws-12/.antiphon/checkpoints/20261124-233338-ddb7/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=6cf76ae77065145a350c647478ef6689c3f1cf03 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=6cf76ae77065145a350c647478ef6689c3f1cf03 build=reused filter=/*/*/(ReleasePromoteTests*)|(ReleasePublishTests*)|(ReleaseSelectionHopSeedTests*)|(ReleaseTagAllocatorTests*)|(ServerGitProcessTests*)|(TestSelectionTests*)|(TestTimingOperationLabelTests*)|(TimeoutAuditTests*)/* executed=67 passed=47 failed=20 skipped=0 trx=/work/worktrees/ws-12/.antiphon/checkpoints/20261124-233338-ddb7/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=6cf76ae77065145a350c647478ef6689c3f1cf03 sourceState=clean buildSource=verified
CHECKPOINT CP-8 commit=6cf76ae77065145a350c647478ef6689c3f1cf03 build=reused filter=/*/*/ReleaseGatePlatformTests/(C1686_LinuxOnlyCensus*)|(LinuxOnlyWindowsExclusionPolicy*) executed=2 passed=1 failed=1 skipped=0 trx=/work/worktrees/ws-12/.antiphon/checkpoints/20261124-233338-ddb7/rows/CP-8/run.trx slot=granted waited=0s dirty=0 source=6cf76ae77065145a350c647478ef6689c3f1cf03 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=6cf76ae77065145a350c647478ef6689c3f1cf03 build=ok filter=/*/*/(GitIndexLockTests*)|(ServerGitAssemblyTests*)/* executed=14 passed=14 failed=0 skipped=0 trx=/work/worktrees/ws-12/.antiphon/checkpoints/20261124-233338-ddb7/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=6cf76ae77065145a350c647478ef6689c3f1cf03 sourceState=clean buildSource=verified
```

Standalone six-class receipt

```text
CHECKPOINT CP-2 commit=6cf76ae77065145a350c647478ef6689c3f1cf03 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(DerivedImpactIndexTests*)|(C1729LayeringGuardTests*)|(WindowsUnitPlatformAdmissionTests*)|(NativeLinuxPolicyWitnessMutationTests*)/* executed=102 passed=102 failed=0 skipped=1 trx=/work/review-evidence/7280f7d0-c0e4-4168-a360-a1bce7541fdf/six-guards/CP-2-20261125-000223-6294/run.trx slot=granted waited=0s dirty=0 source=6cf76ae77065145a350c647478ef6689c3f1cf03 sourceState=clean buildSource=verified
CHECKPOINT SOURCE VALID source=6cf76ae77065145a350c647478ef6689c3f1cf03 rows=1
```
