# Windows Full 79d9e6f9 stopped-run seed

The failed-first seed contains **7 method identities covering 7 observed failed cases**. Run `79d9e6f9` supplied six failures, including one repeat from the prior seed. One renamed method carries forward from run `3528e6b1` through seed `c9816b02`.

| Field | Value |
| --- | --- |
| Seed | `C:\Antiphon\releases\state-79d9e6f9\seed-79d9e6f9-eabe80e4.json` |
| File SHA-256 | `e0ce0bace5af76656db4343aac282bd11f8351c5f12b08045e03e44242357e62` |
| Format | `antiphon-nightly-stopped-seed-v1` |
| Created at UTC | `2026-11-22T20:39:50.2813159Z` |
| Source observed at UTC | `2026-11-22T20:30:58.6120463+00:00` |
| Source run | `79d9e6f9` |
| Source candidate | `rc-2026-12-02-39ac64c90b1c` |
| Source SHA | `39ac64c90b1c43d025557398c3ae94caa2d75273` |

## Retained results

`C:\Antiphon\releases\task-79d9e6f9\supervisor.log:44-45` records 664 TRX at the last progress poll and the operator stop at `2026-11-22T20:33:17.076Z`. `C:\Antiphon\releases\state-79d9e6f9\last-run.json` records the start at `2026-11-22T13:31:04.8677097Z` and incomplete coverage. Its `logDir` was `C:\Antiphon\releases\candidates\rc-2026-12-02-39ac64c90b1c\logs\2026-11-22-1331-79d9e6f9`.

That directory holds 666 top-level TRX files and no `summary.json`. Joining `UnitTestResult.testId` to `TestDefinitions/UnitTest.id` yielded 10,393 rows, with 10,376 `Passed`, 6 `Failed`, and 11 `NotExecuted`. The totals and four failing groups agree with `C:\Antiphon\releases\state-79d9e6f9\stop-snapshot-failures.txt`. [Repository TRX identity reader](../../scripts/lib/Antiphon.Nightly/NightlyTrx.cs#L32)

| TRX in the 79d9e6f9 log directory | Class | Failed method | TRX `testId` |
| --- | --- | --- | --- |
| `antiphon-all-group-134.trx` | `Antiphon.Tests.Scripts.ReleaseGatePlatformTests` | `LinuxOnlyWindowsExclusionPolicy` | `43342bec-b649-bfb9-ea4f-bff6c06f186e` |
| `antiphon-all-group-134.trx` | `Antiphon.Tests.Scripts.WindowsUnitPlatformAdmissionTests` | `C1210_Roster_closure_matches_source_gates` | `d238892d-8a3d-c52b-38b6-416c26b1ab04` |
| `antiphon-all-group-137.trx` | `Antiphon.Tests.Scripts.MergeHighRadiusSubsetTests` | `merge_subset_runs_every_impacted_row_the_batch_diff_selects` | `026bfd80-077e-9595-12f7-656461ce6475` |
| `antiphon-all-group-137.trx` | `Antiphon.Tests.Scripts.MergeHighRadiusSubsetTests` | `merge_subset_without_a_migration_is_exactly_the_fixed_classes` | `1c4c8255-7bd9-b53e-bc9c-6c62d9f92c1a` |
| `antiphon-all-group-138.trx` | `Antiphon.Tests.Scripts.LiveLaneInventoryTests` | `EveryLiveGateHasDisposition` | `f8193508-044b-0828-69f1-2c8c593ad78f` |
| `antiphon-all-group-144.trx` | `Antiphon.TestSupport.NativeLinuxPolicyWitnessTests` | `Policy_rows_require_pinned_attribute` | `9e3e4be9-b628-2513-9b61-7cfcef232d79` |

## Prior seed reconciliation

`dispatch-schedule.json` records the digest `01e37285882372217e4cd6ea3d3d9a1eb3731ce9cb04abdcf18a7d04d401cc97`, which matches `C:\Antiphon\releases\state-c9816b02\seed-c9816b02-1b0bbe47.json`. The prior investigation note is `docs/investigations/2026-11-22-rc-run-c9816b02-stopped-seed.md` at `57581a97cc6b6ccd2fd8b90c86a23c7d19ca64c6` (`origin/feat/card-task-1b0bbe47`).

The merge used ordinal suite, fully qualified class, and method identity after removing parameter suffixes. Of the 39 prior methods, 37 had a `Passed` result and cleared, one failed again (`EveryLiveGateHasDisposition`, counted once), and one had no result under the exact identity. The 37 passing methods cover 46 previously failed cases. They include 26 `Antiphon.E2E.AgentTaskLandDeliveryE2ETests` methods whose 35 formerly failed rows all matched a `Passed` row by TRX `testId`, method and display name in `79d9e6f9`.

The carried identity is `antiphon` / `Antiphon.Tests.Infrastructure.CheckpointToolProjectContractTests.csproj_references_only_host_cleanup`. Its failed case is `a4ec6f3d-10e9-d816-a277-05eb08df7c9c` in `C:\Antiphon\releases\candidates\rc-2026-11-30-bd34a7ff6da9\logs\2026-11-21-0735-3528e6b1\antiphon-all-group-123.trx`. Commit `648ae96a88ed49c6573f85fd9771f41c0133f418` renamed and expanded the witness. The replacement `csproj_references_only_isolated_libraries` passed in `79d9e6f9` group 123 (`6f759d18-f8a0-78f9-c843-ed785572ece5`). The carried row uses the class's current `antiphon/all-group-123` membership. The dispatcher skips absent methods when the destination plan supplies explicit nodes. [Replacement witness](../../tests/Antiphon.Tests/Infrastructure/CheckpointToolProjectContractTests.cs#L25) · [Dispatcher matching](../../scripts/lib/Antiphon.Nightly/DispatchSchedule.cs#L269)

## Export and validation

The previous investigation used `StoppedSeedFiles.Export` through its PowerShell wrapper. This task called the same exporter in the `79d9e6f9` checkout's existing Release assembly, with the exact source identity and the sibling `timing-79d9e6f9` directory. The exporter accepted the stopped evidence and created the seed at the new path with `FileMode.CreateNew`. The merge added the single carried row to that new file and recomputed the envelope hash with `StoppedSeedFiles.ContentHash`. [Exporter](../../scripts/lib/Antiphon.Nightly/StoppedSeedFiles.cs#L254) · [Envelope hash](../../scripts/lib/Antiphon.Nightly/StoppedSeedFiles.cs#L56)

The export contains 1,294 chunk rows, with 665 complete, 2 started and unfinished (`antiphon/all-group-88` and `antiphon/all-group-142`), and 627 untouched. The dispatch schedule has two additional entries for `client/all` and `scripts/all`. An independent file hash check matched all 1,344 evidence digests in the seed. A separate XML read verified each of the seven failed cases against its source TRX. The seed stores method identities and the case counts come from the TRX.

The launcher's `PriorFailureHistory.InitializeDispatch` accepted the final file with the stated SHA-256, cross-candidate acknowledgement, source candidate ID, and current master SHA `ecc9bfaa5bfa91c80aa022bca09f86aa7b693a91` as the destination SHA. It returned run `79d9e6f9`, seven failure identities, and an empty fallback reason. Substituting a 64-character zero digest returned `history-digest-mismatch`. These checks used the stopped candidate's existing Release assembly with an empty owned directory, which exercises the launcher's intake without starting qualification or copying the seed. Git comparison confirmed the intake and stopped-seed reader source files are identical at the stopped candidate and current master. [Launcher call](../../scripts/lib/Antiphon.Nightly/NightlyTests.cs#L295) · [Intake](../../scripts/lib/Antiphon.Nightly/PriorFailureHistory.cs#L752) · [Digest check](../../scripts/lib/Antiphon.Nightly/StoppedSeedFiles.cs#L86)

## Next Windows Full launch

Append these arguments to the new candidate's normal Windows Full invocation. The seed supplies dispatch ordering only. [Ordering contract](../testing-and-build.md#nightly)

~~~powershell
-PriorFailuresPath 'C:\Antiphon\releases\state-79d9e6f9\seed-79d9e6f9-eabe80e4.json' -PriorFailuresDigest 'e0ce0bace5af76656db4343aac282bd11f8351c5f12b08045e03e44242357e62' -AcceptCrossCandidateSeed -SeedSourceCandidateId 'rc-2026-12-02-39ac64c90b1c'
~~~
