# CARD-1915 S1–S3 Final Review, round 2

Reviewed source: `919fb8cc35dbf0e0ca34ca290d22f3bf3d16d903` on `feat/card-task-882cae23`.
Landing owner: `8e452527-6112-47e3-9512-b545440805dd`.
Round-2 delta: `15df4e06aa..919fb8cc` (9 files, +600/−32). `a46bd7af` is patch-identical to `15df4e06aa` and is not an ancestor of the reviewed tip.
Verdict: clean. No substantive blocker. Positive controls and native Windows proof stay pending. Nothing was activated.

## Blocker checks

Feature-off append overhead is not a stable double-digit tax. The migration does not install `c1915_insert`. Fresh and migrated databases have `Enabled` false, zero summary rows, and only `c1915_update`, `c1915_delete`, and `c1915_truncate`. Independent probes on `SessionStateScaleFixture` (3,118,500 transcript rows, Testcontainers schema) compared that shape with all four triggers dropped.

The author's harness, re-run by this review (3 interleaved rounds, load about 32 on 24 cores), medians for the installed feature-off function versus all triggers dropped:

| state | 100 singletons | one 1,000-row insert |
|---|---:|---:|
| absent | +9.86% (54.92 ms vs 49.99 ms) | +2.01% (101.19 ms vs 99.20 ms) |
| invalid | +19.47% (45.54 ms vs 38.11 ms) | +8.72% (93.78 ms vs 86.26 ms) |

A cleaner 7-round probe, with no summary delete before the timer and a 1-minute load near 7, medians:

| state | 100 singletons | one 1,000-row insert |
|---|---:|---:|
| absent | −3.42% | +6.99% |
| invalid | −18.91% | +27.53% unpaired |

The +27.53% invalid multi-row cell is bimodal latency landing in different halves of the sorted lists. Paired by round, six of seven invalid multi-row pairs are within about 3 percent or the guards are faster; one pair is +37%. An earlier 3-round probe under load 32 showed the opposite pattern (+59% / +46% absent, negative invalid) and did not reproduce. The default absent path, which is the production feature-off shape, stays single-digit on both the author's harness and the quieter 7-round median.

UPDATE and DELETE of transcript rows still pay the installed guards when they fire. Set-based 1,000-row statements were −3.89% (update) and +1.98% (delete) at the median. One hundred single-row deletes were +64% because `c1915_delete` invalidates the summary; that is the guard's work. One hundred single-row updates had median +109%, inflated by one 4.1 s sample while 14 tasks were runnable. This cost is not the feature-off append blocker.

Stale-valid safety held. A planted valid summary went invalid on gate-off, stayed invalid and unchanged across an append while the trigger was absent, and stayed invalid when the gate came back on. The legacy loader count matched the transcript count. All four transition orders left `Valid` false, with the insert trigger absent while disabled and present while enabled. Eight writers of four inserts each landed 32 rows, the loader read 32, and the summary stayed invalid with the trigger absent. Double on, double off, and concurrent opposite transitions left `Valid` false. Killing the backend after the maintenance update returned, before commit, restored the prior trigger and validity in both directions. It did not leave the trigger installed on a changed summary.

A plain append while `c1915_insert` was absent held zero locks on `SessionTranscriptSummaries`.

The migration is additive: create table, create index, and SQL. It sets `lock_timeout` to 5 seconds and does not rewrite `TranscriptEntries`. `Down` throws `NotSupportedException`. Against a concurrent `ROW EXCLUSIVE` lock on `TranscriptEntries`, gate admission failed with SQLSTATE `55P03` in 1024 ms.

The gate is a raw `UPDATE` of `SessionTranscriptSummaryMaintenance.Enabled`. The migration inserts that singleton with `Enabled` false. No `server/` code outside the migration writes the maintenance row or calls the gate. `SetMaintenanceAsync` exists only on the test fixture. `SessionStateLoader` does not read summaries. The reader flag is not this slice.

## Compose and gates

Scratch compose `7ee4a6c478` is `origin/master` `27a43f25` plus the 29 card commits. The derived-impact driver was built first, then `scripts/regenerate-derived-impact-index.ps1 -NoBuild` changed `tests/derived-impact-index.json`. After that regeneration, `Read-NightlyExecutionPolicy` still matches the stored pin `ecc5d4fabc326645bf8b7e7970cdc22c1a9d195a83e737a66ccf157f9cb0c0c1`, and `PinnedInputDigest` is still `67e50252fffbb65df7cf45d9187bb8a9cdaaec9ef12333103b883c60f313f3b8`.

Leased rows on that tree, each under 10 minutes of test time:

| row | result |
|---|---|
| CP-1 | 15 passed |
| CP-2 | 14 passed |
| CP-3 | 30 passed |
| adjacent | 48 passed |
| migration callers | 19 passed |
| migration chain | 48 passed |
| guards-a | 48 passed |
| guards-b | 32 passed |
| guards-c1 | 28 passed, 1 failed: `C1620_EveryCallSiteOperationLabelIsKept` |
| guards-c2 | 11 passed |
| CP-35 | 2 passed |
| CP-36 | 1 passed |
| CP-32 | 1 passed |
| guard union | 112 passed, 1 skipped, 1 failed: `C1210_Roster_closure_matches_source_gates` |

`C1620` and `C1210` are the named master reds. `C1210` failed on the compose of `27a43f25`. During this review `origin/master` advanced to `21c9f51b`, whose `50d2dfdd12` rosters the CARD-1609 selector-fixture gates. CARD-1915 does not touch that roster. `git merge-tree --write-tree origin/master HEAD` against `21c9f51b` still conflicts only in `tests/derived-impact-index.json`. Land regenerates that file. It is not hand-merged here.

At `919fb8cc`: append plus migration tests 25 passed; append-cost test 1 passed; short guard batch 53 passed. `scripts/check-evidence-diff.ps1` from `8cec1e74` to `919fb8cc`: 29 commits, 0 violations. Downgrade selector: 71 files, 36 derived classes, owed 1 (`AppDbContext.cs`, count 575, pre-existing cap).

Runs were build-slot leases, not checkpoint-tool receipts, so `reviewedSourceClean` is false. Host load was often above the 24-core count. No OS-specific product branch is in the round-2 diff. Windows proof remains pending.

## Advisories

These do not hold the land.

- The brief's +5.29% / +8.60% / +1.70% / +3.47% figures are not stored as a new measured result in the plan at this tip. The independent runs above are the review measurement.
- A direct SQL plant of `Valid=true` while the gate is off would stay stale, because no insert trigger is installed. No production writer does that. Gate transitions themselves invalidate.
- Per-statement transcript updates pay `c1915_update`. Set-based purge statements did not show that tax.
- The index-only merge-tree conflict is the usual regenerate-on-rebase step.

--- review evidence ---
subjectTaskId: 8e452527-6112-47e3-9512-b545440805dd
reviewedSourceSha: 919fb8cc35dbf0e0ca34ca290d22f3bf3d16d903
reviewedSourceClean: false
ordinaryScopeCompleted: Full
reviewRound: 2
reviewedRange: 15df4e06aa..919fb8cc35dbf0e0ca34ca290d22f3bf3d16d903
