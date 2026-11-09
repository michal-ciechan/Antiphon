# Merge batch 141 (task b0e54eca) - landed on master

master c599061752fce18db7e7e2ea64cd87d3b365fb02 -> 55d0ca6c84af44b853357cf6860a932d5c5ae12a (plain fast-forward, first try; master had not moved at the pre-push fetch).

## Per-commit table (cherry-pick -x, linear, patch-id equal for every pick)

| Subject | Source | Master commit | patch-id |
|---|---|---|---|
| CARD-1809 | 71c5da5d8 | 77a352d7d test red witness | 0e773a8791f4 |
| CARD-1809 | 55c448a8c | 6fd7687bb test line wraps | 1f5526e9ead4 |
| CARD-1809 | c84f887c1 | 5176b816b docs + skill line | 887ef68f9719 |
| CARD-1808 | e6590fde5 | 7093fb169 test red witnesses | 8a09370c8653 |
| CARD-1808 | 330785b59 | 9c8cd8b0f fix 29 files | 7637a1931a45 |
| CARD-1804 | 68af7d740 | 5b79ea0c9 test red witnesses | 9faba00bc51e |
| CARD-1804 | f33f9f561 | 187ed2589 fix retention | 005c1d2b4711 |
| CARD-1804 | f444af118 | 692bd83f0 docs | f76604a90b6f |
| CARD-1793 | 1c7583744 | 9b159dd8e test red witnesses | abd90f2bb72d |
| CARD-1793 | e1d3925a2 | dc424c0bc test fixture root | 6fb544fb4f6b |
| CARD-1793 | 6a0baee38 | e5f423f83 fix pwsh cache | 9196ac14d0ce |
| land-graph refresh | (scripts/land-graph-land-refresh.ps1) | 55d0ca6c8 manifest + evaluation only | n/a |

Skipped report-only: ff7cab22c (.antiphon/task-6f78ecdb.md), b02f5adc6 (.antiphon/task-84cf7bb4.md). No conflicts, no migrations, no subject excluded.
server/Bundles/stage-review.md unchanged at 2579 bytes. New test classes all carry a lane Category (Unit/Unit/Integration).

## Land-graph refresh (rule 11, own commit 55d0ca6c8)
Pre-refresh -Check refused only "manifest does not match the selected graph" (digest E4F11273..., count 75).
LAND-GRAPH-NOTICE action=refresh
- old-digest=F0AC784A390FF2CCBCC9317BA6B3AA03D42387A7558F326F2BC3CA5B1B95692D
- new-digest=E4F112735EABDB3E5B1DF17A4E521710A71D1E4EA3F1E4863D0F97F1E298444C
- count 75 -> 75; pins blob 7a2586e58bd4768648c6f670f6f2c4432f68936a unchanged
- changed-records: tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj (CARD-1793 Compile link)
- card-note: recorded (CARD-1793)
Final -Check (from the clean tree, -ToolPath Debug LandGraphAudit dll): pass2 approved, exit 0.

## Pins
policyHash computed = stored = 7e54681b56c7925bef082d528cde3285ec579a13d51f34625c8c3aad1ee2dbb0
PinnedInputDigest computed = stored = b499df304358cc5a1ac64adaa6a482fa9dc29a76bd3d9f6bcc65ba9a54c47faf
No re-pin. The brief's quoted 50c33c11.../8510514a... are stale template values (same as batches 139/140); CARD-1777 is not in this batch.

## Checks on the final tree 55d0ca6c8 (all Linux server2, build-slot leased, native output, UseAppHost=false)
- ONE `dotnet build Antiphon.sln -m:1 -p:UseAppHost=false`: 0 errors, 3m53s.
- Antiphon.Tests batched run (51 class patterns: subject classes incl. all 25 CARD-1808 touched test classes, the brief's list, DockerStack*/Server2*Contract, selector classes CheckpointTerminalReceiptTests + ProcessSpawnLimitTests, fixed merge-subset classes incl. 8 land classes): 983 total, 971 passed, 1 failed, 11 skipped (Windows-only/ConPTY skips), 17m33s.
  - Failed: CapabilityScriptTests.issue_does_not_print_the_token_and_dpapi_round_trips (DPAPI Protect "Operation is not supported on this platform"). Rerun alone: 1/1 failed, same reason. Inherited Linux red ([SupportedOSPlatform("windows")] class without a skip; CARD-1808 only changed its listener binding); CARD-1811 owns it.
- Antiphon.SessionRunner.Tests (C1793SessionPwshCacheTests, RunnerChildClaudeTokenInheritanceTests, Card1653FreshLaunchIdentityTests, C1677TempFamilyRegistryTests, ProcessSpawnLimitTests): 12/12.
- SessionRunner guards (TestClassificationGuard*, NativeLinuxPolicyWitnessTests*): 4/4.
- Policy guard batch, own process (TestClassificationPolicy*|TestClassificationGuard*|SlowTestRegistryMerge*|SlowTestTripwire*|NativeLinuxPolicyWitnessTests*): 52/52, 0 failed.
- C1729LayeringGuardTests: 9/9, `VIOLATIONS 0 LISTED 168 STALE 0 AMBIGUOUS 3`.
- A5 pair (ReleaseGatePlatformTests C1686_LinuxOnlyCensus, LinuxOnlyWindowsExclusionPolicy): 2/2.
- E2E ContractSnapshotTests (CP-merge-subset-e2e): 5/5 on Linux.
- impacted-classes -MergeSubset -Range c5990617..55d0ca6c: migrations=False, kinds=checkpoint-tool,contracts,spawn-limit-rosters, no windowsFilters.
- import --plan of the documented Merge rows (selector-adjusted CP-merge-subset-tests filter, messaging row dropped: no migration): imported 5 rows, 0 warnings.
- check-evidence-diff c59906175..55d0ca6c8: commits=12 entries=0 violations=0.

## Owed
- Windows lane: C1793SessionPwshCacheTests and PwshPrivateCacheTests (CARD-1793 report: cross-platform, run on Linux only); CapabilityScriptTests DPAPI path (CARD-1811).
- No rc/* touched; a new rc cut is a separate decision.
