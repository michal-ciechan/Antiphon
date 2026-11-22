# Project readiness path prefilter: repair after Review 0d322ed5 round 2

Code task 77d83f5d repairs the two Review round 2 findings on Code task c37a1eef (`fed28c75eadba0269ed6bd6802dfd3421c6fd52a`), cherry-picked onto master `624d4d5b8`. Review round 3 covers only the diff from `fed28c75e`.

## Findings and decision

1. **Correctness.** `rtrim(replace(col,'\','/'),'/') ILIKE @p ESCAPE '!'` folds case with the database's ctype. On a UTF-8 database with locale C it folds only ASCII, so it dropped Windows pairs that `WorkingDirectoryIdentity.Same` accepts (stored `c:\café`, probe `C:/CAFÉ`).
2. **Performance.** On en_US.utf8, ILIKE across a full Agents scan cost about 1.7 times the master predicate `lower(replace(col,'/','\')) = @key`.

Decision: option (a) of the brief, made collation-independent, plus a cheap prefix guard.

```sql
lower(replace(col, '\', '/') COLLATE "C") LIKE @pattern || '%' ESCAPE '!'
AND lower(rtrim(replace(col, '\', '/'), '/') COLLATE "C") LIKE @pattern ESCAPE '!'
```

- `COLLATE "C"` exists on every PostgreSQL server. Under it, `lower` folds only A-Z, whatever the database locale is, and `LIKE` compares code points. `_` matches exactly one code point.
- `CandidatePattern` turns the probe's loose spelling into the pattern. Every ASCII character becomes its ASCII lower case, and `!`, `%` and `_` are escaped. `_` replaces every non-ASCII code point, and every ASCII character that some non-ASCII code point lower-cases to under `ToLowerInvariant` (`k`, the lower case of the Kelvin sign; the set is computed from the runtime). `ToLowerInvariant` maps each code point to exactly one code point, so two spellings with equal `Key`s have loose spellings with the same number of code points. Those spellings agree after ASCII lower-casing wherever the pattern keeps a literal, so the prefilter admits every pair `Same` accepts.
- The trimmed match implies the first operand, because trailing slashes only extend a trimmed match. The first operand exists so that `rtrim`, the costliest step per row, runs only on rows that share the probe's prefix. PostgreSQL orders the cheaper qual first and stops at the first false qual.
- Rejected: (b) a stored computed key would need a migration and would still depend on a database fold. (c) Loading every non-null path into the process gives up the bounded SQL selection the Review asked to keep.

## EXPLAIN ANALYZE (PostgreSQL 16.16, 100,001 agents, LEFT JOIN profiles, one match)

These numbers come from a scratch `postgres:16` container on loaded server2, limited to 2 CPUs. The `Agents` and `AgentTuiProfiles` tables mirror the readiness query's columns, with 100,000 mixed Windows, UNC and POSIX paths plus the probe row. All three predicates are prepared statements, run interleaved, 11 rounds each. The new predicate is the exact EF-generated text. Every run returned rows=1.

| Database | master predicate (median / min ms) | fed28c75e ILIKE | this repair |
|---|---|---|---|
| en_US.utf8 | 83.9 / 78.9 | 241.2 / 159.2 | 36.6 / 26.8 |
| C (UTF-8) | 64.5 / 38.8 | 92.2 / 85.3 | 27.4 / 26.2 |

A probe whose prefix matches 22,500 rows (`C:\Antiphon\worktrees`, 7 rounds) gave medians of 84.6 / 169.3 / 40.0 ms on en_US.utf8 and 43.9 / 91.9 / 37.8 ms on C. A component profile on the C database (minimums over 9 rounds) put the cost in `rtrim`: the trimmed LIKE alone took 79.5 ms, while the untrimmed LIKE took 44.7 ms and the master predicate 42.2 ms. The plan is a sequential scan of `Agents`, the same as before, with the filter order shown in `candidate_prefilter_runs_in_postgres_as_one_filtered_scan`.

## Verification

- V-1: the original witness `agent_counts_a_path_matched_agent_on_another_board`.
- V-2: the C-locale witnesses in `ProjectReadinessCollationTests`, run on an isolated database created with `LC_COLLATE`/`LC_CTYPE` C and dropped afterwards. They cover readiness and `AgentService.FindProjectForWorkingDirectoryAsync` for six non-ASCII case variants, a fixture sanity check, and the prefilter property. All 13 were red at `b75c02b39` and are green after the fix.
- V-3: the non-ASCII case-fold matrix (é/É, ü/Ü, Greek including final sigma, Turkish İ/ı, ß/ẞ, Kelvin, Angstrom, Deseret). On both databases, the rows the database admits must equal the in-process C-collation model's admissions, which must include every pair `Same` accepts. In process, an exhaustive sweep covers every invariant case-fold pair of code points, and the one-code-point lemma is checked over all scalar values.
- R-1: the batched guards.
- R-2: the impacted classes from Review round 2. Their ten Linux skips are now listed in `WindowsUnitSkipRoster.WindowsOnly`, so `scripts/validate-checkpoint-receipt.ps1` can qualify them.
- R-3: the roster's consumer class.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | repair | `tests/Antiphon.Tests -> bin-c77d/` | original | `/*/*/ProjectReadinessTests/agent_counts_a_path_matched_agent_on_another_board` | V-1 | 1 passed, 0 failed/skipped | 1 | 15 | true |
| CP-2 | repair | CP-1 | identity | `/*/*/(ProjectReadinessBatchTests*)\|(ProjectReadinessCollationTests*)\|(ProjectReadinessTests*)\|(ProjectSetupServiceTests*)\|(WorkingDirectoryIdentityTests*)/*` | V-2, V-3 | all listed, 0 failed/skipped | 183 | 6 | true |
| CP-3 | repair | CP-1 | guards | `/*/*/(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(SlowTestRegistryMergeTests*)\|(TestFixtureCleanupGuardTests*)\|(SlowTestTripwireTests*)\|(TestClassificationPolicyTests*)\|(TestLaneCategoryGuardTests*)\|(NativeLinuxPolicyWitnessTests*)\|(TestClassificationGuardTests*)/*` | R-1 | all listed, 0 failed/skipped | 85 | 6 | true |
| CP-4 | repair | CP-1 | impacted | `/*/*/(AgentAttachHerdrTests*)\|(AgentControlServiceIntegrationTests*)\|(AgentCreateRaceTests*)\|(AgentCreateSupervisionTests*)\|(AgentRemoteControlGateTests*)\|(AgentServiceIntegrationTests*)\|(AgentSessionBackendTests*)\|(AgentSessionLaunchFailureTests*)\|(AgentSessionServiceIntegrationTests*)\|(AgentStartRecoveryTests*)\|(AgentSupervisionTests*)\|(AgentSystemPromptLaunchTests*)\|(AgentTaskPoolTests*)\|(BoardServiceIntegrationTests*)\|(ContextCompactionAgentTests*)\|(DiagnosticsBundleServiceTests*)\|(HerdrAlwaysOnChannelParityTests*)\|(HerdrLabelFollowConcurrencyTests*)\|(HerdrPlacementSettingsTests*)\|(HerdrSupervisionBackoffTests*)\|(PhoneHomeStandingLaunchTests*)\|(ProjectCascadeForeignKeyTests*)\|(RemoteExecutableLaunchTests*)\|(SessionHealthTests*)/*` | R-2 | 0 failed; the 10 Linux skips are roster-listed | 514 | 12 | true |
| CP-5 | repair | CP-1 | roster | `/*/*/WindowsUnitPlatformAdmissionTests/*` | R-3 | 0 failed; any skip is roster-listed | 5 | 4 | true |

CP-4 and CP-5 may record roster-listed skips. Qualify them with `pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence <report.json> -ExpectedSourceSha <tip>`. The checkpoint tool's `validate` verb refuses every skip, so rows with skips do not count for that verb.
