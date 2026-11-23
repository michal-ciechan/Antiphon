# Round 3 review of the project readiness path prefilter

**Hold landing.** The C-locale false negative is fixed. The repair can now send every row of a large table to EF for a lookup that has one match.

## Performance blocker

**Where.** `WorkingDirectoryIdentity.CandidatePattern` turns each non-ASCII scalar into `_`. `ProjectSetupService.GetReadinessAsync` materializes the matching agents and their profiles at line 138. `AgentService.FindProjectForWorkingDirectoryAsync` materializes the matching projects at line 1309. Both apply `Same` afterwards.

**Failure.** With 100,000 paths from `C:/repos/00000` through `C:/repos/99999` and one `C:/repos/支付服务组`, the compiled helper produced `c:/repos/_____` for the last path. PostgreSQL returned all 100,001 candidates. Master and the previous reviewed predicate each returned one. A numeric control probe returned one row with both the previous and repaired predicates.

| Candidate for the five-character Unicode leaf | Rows returned | C median, ms | en_US.utf8 median, ms |
|---|---:|---:|---:|
| Master | 1 | 44.0 | 67.9 |
| Previous reviewed ILIKE | 1 | 75.6 | 126.2 |
| Repair | 100,001 | 87.2 | 89.9 |

These are five interleaved PostgreSQL 16.16 EXPLAIN ANALYZE measurements per predicate and locale after warm-up, on a synthetic temporary table, with two CPUs. Each plan used one sequential scan. Times cover database execution only. For the numeric control the repair took 39.0 ms on C and 38.2 ms on en_US.utf8.

**Why.** None of the five Han characters has a case variant in the numeric directory names. The SQL prefix and length checks both admit every one of those names, so each row also pays for the trimmed comparison. Both production lookups then buffer the entire result before the single exact match is known. The author's one-match benchmark cannot catch this increase in rows returned.

**Fix.** Keep Unicode literals when their invariant case class is a singleton. Use a selective comparison for the remaining case classes that admits every spelling accepted by `Same`. Add a database assertion on the candidate count for the mixed-script example, alongside the C-locale positive witnesses.

## Correctness and provenance

The change uses `EnumerateRunes()`, giving a supplementary character one SQL wildcard. The compiled helper accepted the supplementary and decomposed case pairs and distinguished NFC from NFD, dotted I from its expanded spelling, and the drive root from its drive-relative spelling. Null, empty and blank directories did not match. Both patterns appear as separate bound parameters in EF's SQL, with a constant collation and escape. Both lookup directions use the shared filter and exact comparison.

Code's transcript event 103 records 15 executed, 13 assertion failures and two passes at test-only commit `b75c02b3992e637ab3c496dcc4fb0b9deb94de9b`. The C-locale matrix identified 30 missed pairs. Code's five-row green receipt `20261123-001028-0872` validated at `33f0ae07ea3595f3faa8faa3baee4e1962367176`. Its earlier CP-4 run had two diagnostics cleanup foreign-key failures. Code did not establish their status on base.

The previous reviewed SHA is `fed28c75eadba0269ed6bd6802dfd3421c6fd52a`. Its patch is identical at `6822608d7` on the recut branch. The repair delta contains six files and one production helper. The shared investigation note preserves Groups 90 and 97 and the readiness section. Evidence-diff from `624d4d5b8b730d0f1f4f6835215f60ea1412961e` found zero violations in eight commits. The downgrade census found 60 caller files and selected zero migration rows.

The repair source is `77d83f5d-eeba-433e-9ae6-ec8ff25b4a4c`. The original landing owner is `c37a1eef-de84-4500-bc2a-b80193467d85`.

## Verification

Fresh checkpoint run `20261123-004537-1119` validated at the reviewed SHA with one leased build and five leased rows. The rows executed 1, 183, 85, 514 and 6 tests respectively, all passing. CP-4 also recorded ten roster-qualified Linux skips and CP-5 one. The fresh TRX files contain all five identity classes, all ten guard classes and all 24 impacted classes named by the plan, including six C-locale parameter rows in each service direction. The independent SQL diagnostic and compiled-helper probe used build slots. The first SQL invocation stopped at a PostgreSQL locale-reporting query before measurement; the rerun read the locale from `pg_database`.

The ordinary gate is incomplete. The repair's impact selector names eight project/class checks absent from the Code plan and the commissioned short rerun: `TestTimingOperationLabelTests`, `CardFilePrivacyGitTests`, `CardFilePrivacyPathTests`, `RcBridgeProbePlatformTests` and `RunCheckpointSourceScriptTests` in Antiphon.Tests, plus `ProcessSpawnLimitTests` in Antiphon.Tests, Antiphon.Agents.Pty.Tests and Antiphon.SessionRunner.Tests. The native Windows `AgentSessionServiceIntegrationTests` evidence carried from round 2 also remains pending. Collect the missing selections at the eventual candidate SHA. Positive controls remain pending.

The merge-tree against master `6c7a56fe154b934f1d4550f165f7b863521fd522` resolved without conflicts to `6a67781e8c274d8219ea613ba6cde1fef3f181ab`.

<details>
<summary>Checkpoint receipt lines</summary>

```text
CHECKPOINT CP-1 commit=33f0ae07ea3595f3faa8faa3baee4e1962367176 build=ok filter=/*/*/ProjectReadinessTests/agent_counts_a_path_matched_agent_on_another_board executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/ws-11/.antiphon/checkpoints/20261123-004537-1119/rows/CP-1/run.trx slot=granted waited=180s dirty=0 source=33f0ae07ea3595f3faa8faa3baee4e1962367176 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=33f0ae07ea3595f3faa8faa3baee4e1962367176 build=reused filter=/*/*/(ProjectReadinessBatchTests*)|(ProjectReadinessCollationTests*)|(ProjectReadinessTests*)|(ProjectSetupServiceTests*)|(WorkingDirectoryIdentityTests*)/* executed=183 passed=183 failed=0 skipped=0 trx=/work/worktrees/ws-11/.antiphon/checkpoints/20261123-004537-1119/rows/CP-2/run.trx slot=granted waited=60s dirty=0 source=33f0ae07ea3595f3faa8faa3baee4e1962367176 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=33f0ae07ea3595f3faa8faa3baee4e1962367176 build=reused filter=/*/*/(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(SlowTestRegistryMergeTests*)|(TestFixtureCleanupGuardTests*)|(SlowTestTripwireTests*)|(TestClassificationPolicyTests*)|(TestLaneCategoryGuardTests*)|(NativeLinuxPolicyWitnessTests*)|(TestClassificationGuardTests*)/* executed=85 passed=85 failed=0 skipped=0 trx=/work/worktrees/ws-11/.antiphon/checkpoints/20261123-004537-1119/rows/CP-3/run.trx slot=granted waited=15s dirty=0 source=33f0ae07ea3595f3faa8faa3baee4e1962367176 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=33f0ae07ea3595f3faa8faa3baee4e1962367176 build=reused filter=/*/*/(AgentAttachHerdrTests*)|(AgentControlServiceIntegrationTests*)|(AgentCreateRaceTests*)|(AgentCreateSupervisionTests*)|(AgentRemoteControlGateTests*)|(AgentServiceIntegrationTests*)|(AgentSessionBackendTests*)|(AgentSessionLaunchFailureTests*)|(AgentSessionServiceIntegrationTests*)|(AgentStartRecoveryTests*)|(AgentSupervisionTests*)|(AgentSystemPromptLaunchTests*)|(AgentTaskPoolTests*)|(BoardServiceIntegrationTests*)|(ContextCompactionAgentTests*)|(DiagnosticsBundleServiceTests*)|(HerdrAlwaysOnChannelParityTests*)|(HerdrLabelFollowConcurrencyTests*)|(HerdrPlacementSettingsTests*)|(HerdrSupervisionBackoffTests*)|(PhoneHomeStandingLaunchTests*)|(ProjectCascadeForeignKeyTests*)|(RemoteExecutableLaunchTests*)|(SessionHealthTests*)/* executed=514 passed=514 failed=0 skipped=10 trx=/work/worktrees/ws-11/.antiphon/checkpoints/20261123-004537-1119/rows/CP-4/run.trx slot=granted waited=30s dirty=0 source=33f0ae07ea3595f3faa8faa3baee4e1962367176 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=33f0ae07ea3595f3faa8faa3baee4e1962367176 build=reused filter=/*/*/WindowsUnitPlatformAdmissionTests/* executed=6 passed=6 failed=0 skipped=1 trx=/work/worktrees/ws-11/.antiphon/checkpoints/20261123-004537-1119/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=33f0ae07ea3595f3faa8faa3baee4e1962367176 sourceState=clean buildSource=verified
```

</details>
