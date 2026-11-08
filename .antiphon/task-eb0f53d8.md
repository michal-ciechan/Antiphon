# CARD-1773 Review round 2 (Final, diff only): retryable prompt-stamp index

Verdict: CLEAN. Round 1 B-1 is closed and there are no regressions: 3 rows, 76/76 green at the reviewed SHA. Advisories A-1 and A-2 from round 1 stand and go to cards. next: land.

Subject: origin/feat/card-task-b009716e at tip aa07b33d55df270d7c5b223c8fdadd1c248b40bd (repair task b009716e-461a-4a5d-8728-4ed33881f751, authored by Grok Code). Landing owner: the original Code task d53f3fba-b8da-4504-8ed6-19c9cd34dc1a. Reviewed range: 4f20588c8c7d43cb8852d398098f2aea7ba29892..aa07b33d55. That range has six commits, and aa07b33d5 is report-only.

## B-1 closure

- **Up:** `20261108121426_Card1773TranscriptPromptStampIndex.Up` now calls `AddTranscriptHotPathIndexes.CreateRetryableIndex`. The only helper changes are visibility (`private` to `internal`) and an optional `filter`. With no filter, the `WHERE` clause is left out. All three CARD-0698 calls pass a filter, so their generated SQL is byte-identical; TranscriptHotPathMigrationTests is 11/11. The emitted sequence has three steps, all `suppressTransaction: true`:
  1. A DO block renames `"IX_…_Kind_Sequence"` to `_invalid` only when `NOT indisvalid`. If `_invalid` is already taken by a valid relation, it raises an exception instead.
  2. `DROP INDEX CONCURRENTLY IF EXISTS "…_invalid"`.
  3. `CREATE INDEX CONCURRENTLY IF NOT EXISTS "…" ON "TranscriptEntries" ("AgentSessionId", "Kind", "Sequence")`.
- **Name handling:** the cleanup name is 57 characters, under the 63-character NAMEDATALEN limit. A crash between the create and the history row write re-runs as a no-op.
- **Down:** drops the index and `_invalid` with `DROP INDEX CONCURRENTLY IF EXISTS`, transaction suppressed. It is additive and removes no rows.
- **Witness:** `Retry_replaces_an_interrupted_invalid_index` holds an open writer transaction on TranscriptEntries and sets a 1 s `statement_timeout`. The real concurrent build then fails with 57014, which leaves a true `indisvalid=false` leftover. The test does not edit the catalog. It asserts the leftover is INVALID with no history row, then that the retry produces a valid, ready index with a new OID, no `_invalid`, and the history row written.
  - `Retry_finishes_cleanup_after_invalid_index_rename` covers an interruption between the rename and the drop.
  - The red TRX at 6dc088b62 (`/work/worktrees/task-b009716e/.antiphon/c1773-red/c1773-red.trx`) shows all 3 methods Failed, including `42P07: relation "IX_TranscriptEntries_AgentSessionId_Kind_Sequence" already exists`.
- **Migration order:** origin/master is 7624fb6820a48805c2fb23c9d7e38f324f7cece2 (fetched at review time). Its latest migration is still `20261107104114_HostLoadRetentionCatchup`, so this one still sorts last.
- **Snapshot:** the snapshot diff against base f54741ef3b is only the one `HasIndex(...).HasAnnotation("Npgsql:CreatedConcurrently", true)` hunk. Round 2 did not change the snapshot or the Designer. C0824RefOnlySnapshotMigrationTests and C1514HostLoadMigrationTests are green, which shows no pending model changes.

## Checkpoints

There is no plan `### Checkpoints` table. The rows below are the brief's named classes, run from the ignored extract `.antiphon/c1773-r2-review-plan.md`.

- Run `20261108-153941-c69b`: one cold build `bin-c1773rv2/` (410 s), GREEN exit 0, wall 16m57s.
- `validate --evidence report.json --expected-source-sha aa07b33d55…` printed `CHECKPOINT SOURCE VALID source=aa07b33d55df270d7c5b223c8fdadd1c248b40bd rows=3`.
- `unlisted: none`. The only other build was the checkpoint tool itself, under build-slot lease 52fac930 (7 s).

```
CHECKPOINT CP-1 commit=aa07b33d55df270d7c5b223c8fdadd1c248b40bd build=ok filter=/*/*/(Card1773TranscriptPromptStampMigrationTests*)|(TranscriptHotPathMigrationTests*)/* executed=14 passed=14 failed=0 skipped=0 slot=granted waited=0s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=aa07b33d55df270d7c5b223c8fdadd1c248b40bd build=reused filter=/*/*/(C0824RefOnlySnapshotMigrationTests*)|(C1514HostLoadMigrationTests*)/* executed=12 passed=12 failed=0 skipped=0 slot=granted waited=0s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=aa07b33d55df270d7c5b223c8fdadd1c248b40bd build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)/* executed=50 passed=50 failed=0 skipped=0 slot=granted waited=0s dirty=0 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=289.417s wall=289.457s
CHECKPOINT-TIMING CP-2 waited=0s executed=136.883s wall=136.970s
CHECKPOINT-TIMING CP-3 waited=0s executed=87.119s wall=87.168s
```

The CP-1 TRX names all 3 Card1773 methods (2.5 s, 35.8 s and 13.0 s) and all 11 TranscriptHotPath methods. The new class is in `slow-tests-allowlist.txt` and its `[Timeout]` is 180 s. The 35.8 s method stays well inside that.

The whole Unit lane was not run. This round's commissioned scope is diff-only with named classes, and the Unit lane stays with nightly and rc qualification. There are no V-n, R-n or PC rows; every PC stays pending.

## Publication checks

- `scripts/check-evidence-diff.ps1 -BaseRef f54741ef3b -HeadRef aa07b33d55`: commits=9 entries=1 violations=0. The one entry is the Markdown Code report.
- `git merge-tree --write-tree origin/master HEAD` (7624fb682): exit 0, tree 98a3f74221357448960a59d1ba815f33343b08ac. Nothing was merged.

## Advisories (unchanged, for cards; they do not hold the land)

- A-1: the Canceled short-circuit skips a same-pass late confirm.
- A-2: the index is full rather than partial on the UserPrompt and QueuedUserPrompt kinds.

The green run deleted `bin-c1773rv2/`. Evidence is at `.antiphon/checkpoints/20261108-153941-c69b/report.md` (ignored). The only commit on this branch is this document.
