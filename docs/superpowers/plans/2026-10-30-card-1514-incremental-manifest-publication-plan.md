# Incremental executed-manifest publication

Task `bb0b355c`; base `17a39b9f05781743082c2ad225dbc4f60f2afa7e`.
CARD-1514 names the existing timing workstream; no new card identity is inferred.
Status: skeleton, investigation and verification design in progress. Product code is unchanged.

## Decisions

- D-1: Target cumulative publication after owner return. The measured opportunity is approximately 109.5 minutes of a 1378.1-minute run, not all 114.2 minutes of the original timestamp interval.
- D-2: Preserve per-test accounting, frozen release authority, ledger and receipt meaning, and recoverable completed-chunk evidence. Do not change test selection, concurrency, process ownership or unexplained historical exit tails.
- D-3: Verification design is included in this plan. Independent plan Review by a different company's model precedes implementation, as commissioned.

## Ground truth

| Assumption | Observed fact |
|---|---|
| Tests linger for about 20 seconds | The native investigation locates the dominant delay after owner return; the harness rewrites `tests.log` later. |
| Publication is constant per chunk | The executed manifest is cumulative; a 240-chunk prefix with 21,512 outcomes costs 24.147–31.239 seconds to publish, versus 0.020 seconds warm with an empty prefix. |
| All 114.2 minutes can be saved | Approximately 109.5 minutes is surrounding publication/bookkeeping. Five historical pre-log tails remain unexplained and outside scope. |

## Mechanism and cost

Read in full: `docs/investigations/2026-10-30-post-test-20s-exit-delay.md`.
Current-source call graph, consumer inventory, operation model and integration ordering are being checked before choosing the replacement.

## Slices

Pending: bounded writer/recovery design, consumer integration, regression and native replay slices with exact files and test names.

## Performance budget

Apply `docs/testing-and-build.md#performance-standards` and `docs/test-performance-standards.md`.
This Plan executes no product tests. Checkpoint import will be validated through a slot-gated isolated tool bootstrap. Implementation checkpoints will carry explicit lane, phase/setup ownership, provisional duration provenance, bounded p95 collection and approximately 2x-p95 timeout derivation. Native Windows replay remains OWED; two historical publication observations are not a p95.

## Verification design

Required: deterministic production-path operation/byte-count RED witness independent of wall time, byte-equivalent compatibility artifacts at fixed inputs, kill/recovery/refusal cases, reader/liveness regressions, classification/census/OS inventory and native scale replay. Checkpoint table pending source roster inspection.

## Source findings (completed)

References below are to base `17a39b9f05781743082c2ad225dbc4f60f2afa7e`; re-anchor on the implementation base after S1 integration.

| Brief assumption | Ground truth / consequence |
|---|---|
| All prior TRX files are rehashed each chunk | `test-phase-evidence.ps1:1336-1352` reads/parses and hashes only the current TRX. Prior results are copied from JSON; incremental TRX hashing cannot remove the dominant cost. |
| Only the latest row is rewritten | `test-phase-evidence.ps1:1370` reads/parses the entire prior JSON; `:1377-1381` reconstructs every retained chunk and each outcome (`:1298-1303`); `:1393` serializes/replaces the entire document. `:97-156` recursively serializes objects and escapes strings character-by-character in PowerShell. |
| The log mtime measures child exit | `nightly-tests-impl.ps1:1211-1226` stops the owner timer before publication, but the run timer afterwards. `:1235-1239` then rereads/rewrites this chunk's log with the invocation stamp. This log rewrite is proportional to the current log, not the preceding manifest. Keep it. |
| The executed manifest is the coverage ledger | It is a timing diagnostic. `nightly-coverage.ps1:999,1117,1161,1191` validates discovered/terminal UIDs and TRX directly. `release-authority.ps1:629-664,808-878` separately reads `authority/execution-plan.json`, `authority/ledger.json`, original evidence and their digests. No reader of `groups-executed.json` exists there. |
| The current publication guarantees resumable execution | `Write-TestPhaseTextAtomic:159-175` writes UTF-8 without BOM to a sibling temp then Move/Replace: an intact previous or current snapshot survives an ordinary process kill. There is no explicit Flush(true)/directory sync and no demonstrated power-loss guarantee. The publisher catches failures and writes `phase-summary-failed`; it does not change the test verdict. No automatic resume/skip-from-this-manifest exists in the inspected launcher. Recoverable diagnostic evidence is required; release reuse still requires its existing custody checks. |
| A public reader requires a fresh flat manifest each chunk | The only production direct reader found is `ConvertTo-TestPhaseSummaryRows:1485-1492`, reached by `Invoke-TestPhaseRunSummary:1726` and `scripts/summarize-test-phases.ps1`. `Complete-TestPhaseLaunch:1411-1414` requests a summary explicitly; the qualification loop calls the writer directly and summarizes once at `nightly-tests-impl.ps1:1494`. Test consumers are `C1514NativeProbe.cs` and `TestPhaseSupervisorTests.cs`. No external fleet reader is proven absent. |
| Current chunk verdict fields are final | Publication precedes current-chunk coverage validation; the fields supplied at `nightly-tests-impl.ps1:1220` are aggregate values at that point. Preserve these bytes; correcting them is a separate behavior change. |

For chunk k with current outcomes r[k], prior manifest bytes B[k-1], and current TRX/log bytes T[k]/L[k], the current publication does O(T[k] + B[k-1] + B[k]) work: parse current TRX/hash, parse prior JSON, normalize all prior outcome rows, recursively serialize all rows, write/replace all bytes. With roughly fixed row sizes and r[k], B[k] grows linearly in k and total publication is quadratic. No repeated hashing of previous TRX is in this call. Final summarization separately rereads all TRX once (`test-phase-evidence.ps1:1495-1500`); that linear end-of-run work stays.

Native observations: warm empty prefix 0.020 s; 240 prior chunks / 21,512 outcomes 24.147 and 31.239 s; cold empty 0.415 s. A descriptive two-point model is `t(R) = 0.020 + beta*R` seconds with beta approximately 0.00112-0.00145 seconds/prior outcome, for this host/data shape only. Equivalently 0.1005-0.1301 seconds per prior chunk at the observed prefix mean 89.6 outcomes/chunk. This is not a fitted causal coefficient or p95; row string lengths, runtime/cache/load and serialization versus IO contributions are unmeasured. Historical cumulative manifest ended at 385 chunks / 1,712,308 bytes; late one-test chunks cost about 27 s despite trivial current work.

The measured opportunity is 6570.297 s = 109.505 min, about 7.95% of 1378.1 min. New total is approximately `1378.1 - 109.5 + new_publication_minutes`, not a promised 114-minute reduction. If journal plus one projection measured <=2 min, the corresponding estimate would be >=107.5 min saved and <=1270.6 min total at identical workload/load. This <=2 min is a target to test, not observed evidence. Do not add the speed-up plan's profile-packing estimate: both remove the same repeated publication. Historical 41.564/30.868/4.573/3.117/3.060 s pre-log tails remain unresolved and get no saving credit.

## Candidate decision (completed)

- D-4: Use immutable, atomic per-publication journal records and materialize the legacy `groups-executed.json` once at finalization or on an explicit snapshot request. This removes historical-row work from each chunk while retaining recoverable evidence and exact final serialization. An ordinary write must touch only the current TRX/result plus constant-size journal metadata.
- D-5: A single JSONL append is rejected for this slice: an append is not a portable transactional record boundary; a kill can leave a partial tail, and concurrent/read recovery needs a framing/commit protocol. One temp-file-to-final-name rename per record gives the needed process-kill commit boundary with the existing filesystem pattern.
- D-6: Faster serialization alone is rejected as the primary fix: it may improve the coefficient but leaves whole-prefix parse/write work quadratic. Incremental hashing is rejected because old evidence is not rehashed here. In-memory-only caching/end-only publication is rejected because a killed harness loses completed evidence.
- D-7: Keep publication diagnostic and preserve the existing verdict/failure-marker contract. A journal or projection error must never manufacture success, conceal missing evidence, modify release custody, or overwrite a valid old snapshot with a falsely complete view. No new release authority is derived from timing files.
- D-8: Do not change tests.log stamping, heartbeat thresholds, watchdogs, process owner, exclusions, timing quality joins, receipt schema or policy hash as part of this fix. The known final-summary cost is not moved into each chunk.
