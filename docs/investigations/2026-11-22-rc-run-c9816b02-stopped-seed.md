# Windows Full c9816b02 stopped-run seed

The failed-first seed contains **39 method identities covering 48 observed failed cases**. The stopped run contributed 38 methods and 47 failed cases. One method and its failed case carry forward from run 3528e6b1.

| Field | Value |
| --- | --- |
| Seed | `C:\Antiphon\releases\state-c9816b02\seed-c9816b02-1b0bbe47.json` |
| File SHA-256 | `01e37285882372217e4cd6ea3d3d9a1eb3731ce9cb04abdcf18a7d04d401cc97` |
| Format | `antiphon-nightly-stopped-seed-v1` |
| Created at UTC | `2026-11-22T13:22:49.1037404Z` |
| Source observed at UTC | `2026-11-22T13:15:23.7537821+00:00` |
| Source run | `c9816b02` |
| Source candidate | `rc-2026-12-01-ac8339c754a8` |
| Source SHA | `ac8339c754a84ab53ca07708c091d4e79bd1fb5b` |

## Retained evidence

- `C:\Antiphon\releases\task-c9816b02\supervisor.log:38-39` records 360 TRX at the 13:10:05Z progress poll and an operator stop at 13:15:56Z. The start in `state-c9816b02\last-run.json` was 07:14:10Z.
- The run log directory `C:\Antiphon\releases\candidates\rc-2026-12-01-ac8339c754a8\logs\2026-11-22-0714-c9816b02` contains 363 top-level TRX and no `summary.json`. Its sibling `timing-c9816b02` contains 366 timing records and the `groups-planned-*.json` files used by the exporter. The 11,046 TRX result rows resolve to 10,987 `Passed`, 47 `Failed` and 12 `NotExecuted`, agreeing with the total and failure count in `state-c9816b02\stop-snapshot-failures.txt`.

| Failing TRX stem | Class | Methods | Failed cases |
| --- | --- | ---: | ---: |
| `antiphon-all-group-82` | `AgentReportStoreTests` | 1 | 1 |
| `antiphon-all-group-84` | `PhoneHomeTaskDispatchProjectionTests` | 1 | 1 |
| `antiphon-all-group-123` | `SourceRevisionStampContractTests` | 1 | 1 |
| `antiphon-all-group-134` | `NightlyNativeOwnershipTests` | 2 | 2 |
| `antiphon-all-group-137` | `RcDirtyLaunchOrderTests` | 6 | 6 |
| `antiphon-all-group-138` | `LiveLaneInventoryTests` | 1 | 1 |
| 26 `e2e-all-split-*` | `AgentTaskLandDeliveryE2ETests` | 26 | 35 |

The E2E count includes `C467_RetainedEvidenceCompletesCleanup` from split 18. The other failures are in `C467_V22` through `C467_V32`, `C488_Approval*`, `C488_ReviewToLand*` and `C498_FailureOutcomeReachesCaller`.

Of the [previous seed's](2026-11-22-rc-run-3528e6b1-final-analysis.md#failed-first-seed-and-next-launch) 59 identities, 58 matched at least one `Passed` result by exact suite, fully qualified class and method after stripping parameter suffixes. None of those 58 also failed. The remaining method, `Antiphon.Tests.Infrastructure.CheckpointToolProjectContractTests.csproj_references_only_host_cleanup`, has one `Failed` row in 3528e6b1's `antiphon-all-group-123.trx` and no row under its exact identity in c9816b02. Commit `648ae96a88ed49c6573f85fd9771f41c0133f418` renamed and expanded the test. Its replacement `csproj_references_only_isolated_libraries` passed in c9816b02's `antiphon-all-group-123.trx`. The old identity remains in the seed under the class's current `antiphon/all-group-123` membership. The dispatcher ignores an absent method when the next plan has explicit nodes. [Replacement test](../../tests/Antiphon.Tests/Infrastructure/CheckpointToolProjectContractTests.cs#L25) · [Dispatcher method matching](../../scripts/lib/Antiphon.Nightly/DispatchSchedule.cs#L269)

## Export and validation

The pinned c9816b02 checkout's `scripts/export-nightly-stopped-run-seed.ps1:21` called `StoppedSeedFiles.Export` with the run directory, timing sibling, and exact source run, candidate and SHA. The exporter accepted the run without a summary and created the new file with `FileMode.CreateNew`. Its output had 1,295 chunk rows (362 completed, two started and unfinished, and 931 untouched) and 738 input evidence digests. The two unfinished rows were `antiphon/all-group-142` and `antiphon/all-split-3`. [Exporter](../../scripts/lib/Antiphon.Nightly/StoppedSeedFiles.cs#L254)

The merge carried the one historical row into that newly created file and recalculated the envelope content hash with `StoppedSeedFiles.ContentHash`. Exact pass evidence came from each TRX result joined to its test definition by `testId`. A separate XML census reproduced the final 39 rows and 48 supporting failed cases. The file carries method identities, with case counts taken from the TRX.

The launcher's `PriorFailureHistory.InitializeDispatch` accepted the final file on both the pinned source checkout and master `39ac64c90b1c43d025557398c3ae94caa2d75273`, using the file digest above, the explicit cross-candidate acknowledgement, and the exact source candidate. It returned run `c9816b02`, all 39 methods and an empty fallback reason. On master, passing a 64-character zero digest returned `history-digest-mismatch`. Validation used an empty owned directory argument to keep the intake read-only. [Launcher call](../../scripts/lib/Antiphon.Nightly/NightlyTests.cs#L266) · [Intake](../../scripts/lib/Antiphon.Nightly/PriorFailureHistory.cs#L752) · [Digest and content check](../../scripts/lib/Antiphon.Nightly/StoppedSeedFiles.cs#L85)

## Next Windows Full launch

Append these arguments to the new candidate's normal Windows Full qualification invocation. The seed supplies dispatch ordering only. [Ordering contract](../testing-and-build.md#nightly)

~~~powershell
-PriorFailuresPath 'C:\Antiphon\releases\state-c9816b02\seed-c9816b02-1b0bbe47.json' -PriorFailuresDigest '01e37285882372217e4cd6ea3d3d9a1eb3731ce9cb04abdcf18a7d04d401cc97' -AcceptCrossCandidateSeed -SeedSourceCandidateId 'rc-2026-12-01-ac8339c754a8'
~~~
