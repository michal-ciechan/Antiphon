# CARD-1514 readiness prefilter, Final Review round 4

**Found.** The PostgreSQL prefilter admits a valid supplementary-plane path that its V-3 test model rejects.

Code owner `6e82eb65-4a0a-42fa-b5ed-390b2719e801`. Reviewed `33f0ae07ea3595f3faa8faa3baee4e1962367176..2b22d32e1465a4920d9de82b7958b2285693a247`.

## Blocking test regression

- **Where.** `CLocalePrefilterModel.Admits` reads `pattern[index]` as a UTF-16 code unit. The stored path is already a sequence of scalar values. [WorkingDirectoryIdentityTests.cs](../../../tests/Antiphon.Tests/Application/WorkingDirectoryIdentityTests.cs#L290)
- **Failure.** For stored `C:\𠀀x` and probe `c:/𠀀x/`, the compiled `Same` returned true, the pattern was `c:/𠀀x`, and PostgreSQL 16.16 accepted the path. The compiled model returned false. With the previous `c:/_x` pattern the same model returned true.
- **Why.** The repair makes U+20000 reachable as a literal. The model compares its high surrogate U+D840 with scalar U+20000. The 12 pattern-generation rows check the new spelling, but the database parity corpus covers supplementary case pairs only through Deseret wildcards. A database regression that drops a literal U+20000 row could pass V-3. [Database corpus](../../../tests/Antiphon.Tests/Application/ProjectReadinessTests.PathIdentity.cs#L214)
- **Fix.** Decode pattern tokens as runes, preserving the ASCII escape and wildcard rules. Add a U+20000 path and a distinct same-length supplementary sibling to the shared default/C database corpus. Assert the returned identity for both probes.

## Verified behavior

Windows `Same` uses ordinal equality after invariant lowercasing and separator normalization. The sweep models that rule. For example, the Kelvin sign and K have equal keys, while capital sigma and final sigma have distinct keys. Both consuming services refine database candidates with `Same`. The scalar sweeps, PostgreSQL escape test and both locale matrices passed. PostgreSQL matched a Deseret scalar with exactly one underscore.

The red commit `6927d819624f4049b98e26b2f5f5efad3655eb2a` changed only tests on the previous product source. The Code transcript records 7 failures in 27 cases. Both V-4 methods stopped on 1,003 CJK candidates versus the expected 1. A separate Review query on the same 1,003-row corpus returned 1,003 for the old pattern shared by both probes, 1 for the new CJK pattern, and 2 for the new cased pattern. Both fresh EF witnesses checked the returned IDs.

## Verification record

The five rows in the Code receipt matched the plan filters and minima. Review run `20261123-021118-ec3c` executed the same five rows with 799 passes, 11 rostered skips and 0 failures. All 810 TRX execution identities were distinct. CP-2's 193 cases included 82 path identity, 83 readiness, 15 C-locale, 11 setup and 2 batch cases. The impacted selector added no classes outside CP-2 and CP-3 for this range. Downgrade selector count was 0. All PCs remain pending.

Code's `dotnet run … import` built the checkpoint launcher outside the slot gate. Its ungated `--no-build start` calls launched the checkpoint driver. Review leased the launcher build and repeated the five-row import under a lease. Checkpoint builds and rows had their own leases. The extra leased PostgreSQL and compiled-method checks isolated the missing U+20000 case. The first reflection attempt stopped on a dependency load error. The isolated repeat loaded the checkpoint dependencies and reached the failing comparison.

Evidence-diff found 0 violations in 3 commits. Merge-tree against fetched master `739358378daeaa9dda056ea26e3724231af61b6b` was conflict-free, tree `d05ae2c558adbcbbc56e29cd5bd7899534871afa`. Code's retained report lacks the original TRX files needed for skip revalidation. Its task transcript records the original validator result at event 91. Review validated its own final receipt against the full source SHA.

<details>
<summary>Review checkpoint receipt</summary>

```text
CHECKPOINT CP-1 commit=2b22d32e1465a4920d9de82b7958b2285693a247 build=ok filter=/*/*/ProjectReadinessTests/agent_counts_a_path_matched_agent_on_another_board executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-021118-ec3c/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=2b22d32e1465a4920d9de82b7958b2285693a247 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=2b22d32e1465a4920d9de82b7958b2285693a247 build=reused filter=/*/*/(ProjectReadinessBatchTests*)|(ProjectReadinessCollationTests*)|(ProjectReadinessTests*)|(ProjectSetupServiceTests*)|(WorkingDirectoryIdentityTests*)/* executed=193 passed=193 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-021118-ec3c/rows/CP-2/run.trx slot=granted waited=15s dirty=0 source=2b22d32e1465a4920d9de82b7958b2285693a247 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=2b22d32e1465a4920d9de82b7958b2285693a247 build=reused filter=/*/*/(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(SlowTestRegistryMergeTests*)|(TestFixtureCleanupGuardTests*)|(SlowTestTripwireTests*)|(TestClassificationPolicyTests*)|(TestLaneCategoryGuardTests*)|(NativeLinuxPolicyWitnessTests*)|(TestClassificationGuardTests*)/* executed=85 passed=85 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-021118-ec3c/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=2b22d32e1465a4920d9de82b7958b2285693a247 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=2b22d32e1465a4920d9de82b7958b2285693a247 build=reused filter=/*/*/(AgentAttachHerdrTests*)|(AgentControlServiceIntegrationTests*)|(AgentCreateRaceTests*)|(AgentCreateSupervisionTests*)|(AgentRemoteControlGateTests*)|(AgentServiceIntegrationTests*)|(AgentSessionBackendTests*)|(AgentSessionLaunchFailureTests*)|(AgentSessionServiceIntegrationTests*)|(AgentStartRecoveryTests*)|(AgentSupervisionTests*)|(AgentSystemPromptLaunchTests*)|(AgentTaskPoolTests*)|(BoardServiceIntegrationTests*)|(ContextCompactionAgentTests*)|(DiagnosticsBundleServiceTests*)|(HerdrAlwaysOnChannelParityTests*)|(HerdrLabelFollowConcurrencyTests*)|(HerdrPlacementSettingsTests*)|(HerdrSupervisionBackoffTests*)|(PhoneHomeStandingLaunchTests*)|(ProjectCascadeForeignKeyTests*)|(RemoteExecutableLaunchTests*)|(SessionHealthTests*)/* executed=514 passed=514 failed=0 skipped=10 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-021118-ec3c/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=2b22d32e1465a4920d9de82b7958b2285693a247 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=2b22d32e1465a4920d9de82b7958b2285693a247 build=reused filter=/*/*/WindowsUnitPlatformAdmissionTests/* executed=6 passed=6 failed=0 skipped=1 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261123-021118-ec3c/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=2b22d32e1465a4920d9de82b7958b2285693a247 sourceState=clean buildSource=verified
BUDGET wall=1232.957s execution=1188.257s admissionOnly=44.700s
wall: 20m33s  sequential-equivalent: 8m27s  builds: 1  max-concurrent-builds: 1  rows: 5 green 0 red 0 skipped
outputs: deleted bin-c77d/
verdict: GREEN exit=0
CHECKPOINT SOURCE VALID source=2b22d32e1465a4920d9de82b7958b2285693a247 rows=5
```

</details>
