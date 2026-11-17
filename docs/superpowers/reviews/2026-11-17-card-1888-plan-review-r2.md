# CARD-1888 plan review, round 2

Review task `54b64112`. Ordinary round 2, diff only, blockers only. Final scope for this plan. The subject changes no code and no tests.

Subject task `4f546055-85dd-47fc-b574-a34a66c48122` (Plan, Succeeded). Reviewed source `a7c74ef1c6789dda5705e31ab2c95376a2f18088` on `origin/feat/card-task-4f546055`. Range `1b6c23bed2bf26f41ac3da986611a0241eebc744..a7c74ef1c6789dda5705e31ab2c95376a2f18088`: `84d8ad1cdb1c82ae5bda46f5c5fb9e357bf00ff8` (the amendment) and `a7c74ef1c6789dda5705e31ab2c95376a2f18088` (the import record). The only file is `docs/superpowers/plans/2026-11-17-card-1888-transcript-ingest-probe-plan.md`.

`origin/master` at review time is `300c8a99d0d267a39791410b7540189ab702f251`, the pin the plan names. Code citations below are that tree. CARD-1888 (`110038c1-0d78-46c2-a9d6-7fdf8dfd3319`) is InProgress.

## Verdict

**Clean.** Round-1 D-1 and D-2 are closed. No new blocker.

The skip fires only when the prefix digest matches and three stored facts match: `FirstRowId`, the `Id` of the row at `PrefixStoredMaxSequence` (S), and `count(*)` of rows with `Sequence <= S`. A product insert cannot land at or below the current stored max while the row at S is still there, so a middle delete cannot be refilled under a matching `TopRowId`. Deleting the row at S and refilling that sequence changes `TopRowId`. S2 and S3 are one Merge in section 7, D-9 and F-4.

## D-1. Stored-prefix check

Sequence assignment is `AgentSessionRuntime.cs:1464` and `:1481-1482`:

```
var maxSeq = prelude.MaxSequence ?? 0;
var storedSeq = e.Sequence > maxSeq ? e.Sequence : maxSeq + 1;
maxSeq = storedSeq;
```

`prelude.MaxSequence` is `max("Sequence")` for the session (`TranscriptIngestPrelude.cs:24-25`). The row id is `Guid.NewGuid()` (`AgentSessionRuntime.cs:1486`). The same formula is the only `TranscriptEntries.Add` path (`:1513`, `:1612`, `:1648`). While a row still exists at S, the stored max is at least S, so the next product sequence is at least S+1. An insert at or below S happens only after the row at S is gone.

| Case | Result |
|---|---|
| (a) Delete a middle row | `count(Sequence <= S)` drops. `FirstRowId` and `TopRowId` stay. The count check falls back to the full probe. |
| (b) Delete the row at S and insert a different entry at S | The max drops below S. The next product row takes S (`e.Sequence > max` or `max+1`). Its id is new, so `TopRowId` differs, or no row is at S. W-6 is this case: runner sequence 1200 on an empty session stores 1..1200; after the top delete the max is 1199; `ObserveTranscriptAsync` with runner sequence 1200 stores a new id at 1200 and restores the count. |
| (c) Delete a middle row and insert below the current max | No product insert can do this while the row at S exists. `storedSeq` is either greater than the current max or exactly `max+1`. Refilling the hole would require a writer that inserts at `Sequence <= S` without that rebase. None exists (f). |
| (d) Delete the lowest row | `FirstRowId` changes. The count drops too. Product code does not refill a sequence below the max. |
| (e) Delete a row and insert the same `(Uuid, Kind)` under a new id | A middle hole is not refilled by product code, so the count drops and the probe runs again. If a non-product writer put the same `(Uuid, Kind)` back at a sequence `<= S`, the entry is stored and the skip is still true. If the deleted row is the top, `TopRowId` differs even when the logical entry comes back. |
| (f) In-place `Uuid` or `Kind` change, or an insert at `<= S` that does not rebase | No production statement does either. The only `TranscriptEntries` delete is the whole-session `ExecuteDeleteAsync` at `DataRetentionService.cs:372`. `ProjectCascade.cs:171-175` deletes the session row and cascades every transcript row. There is no `ExecuteUpdate`, `Remove`, or `UPDATE`/`INSERT` SQL against `TranscriptEntries`. The `c1915_*` triggers write `SessionTranscriptSummaries` only (`20261114063858_AddSessionTranscriptSummaries.cs:109-230`). S3's re-grep is in the slice text (plan line 226) and stops the slice on a new identity update or a non-rebasing insert. |

A mismatch drops the memo and uses today's chunk probe. That fails closed.

## D-11. One command

`TranscriptIngestPrelude.ReadAsync` builds one `SqlQueryRaw` (`TranscriptIngestPrelude.cs:58-78`). Part 0 is the `SELECT 0` branch (`:22-29`). The UUID and null-UUID branches are `UNION ALL` on that same string. Extra columns on part 0, with typed nulls on the other branches, stay one command. `C1403_persist_prelude_is_one_command` counts commands tagged `rt.ingest.` or `session-state.identity` and asserts `scope.Count == 1` (`AgentSessionRuntimeTests.Persist.cs:413-447`). Its batch is 12 entries, under the 64-entry floor, so it is neither a record candidate nor a skip candidate. The assertion still holds.

W-1's base replay is the 1,200-entry snapshot plus 5 new uuids: 1,205 keys. Nothing filters the set before `Chunk(512)` (`AgentSessionRuntime.cs:1433-1457`), so the commands are 512 in the prelude, then 512, then 181. Both `1,205 > 5` and `3 != 1` fail. After S3 the prefix check rides in that same prelude command, and W-1/W-2 keep `== 1`.

`IX_TranscriptEntries_AgentSessionId_Sequence` is unique on `(AgentSessionId, Sequence)` (`20260612185721_AddTranscriptEntries.cs:45-49`; still unique at `AppDbContext.cs:1747-1749`). The count is a range of that index. The plan requires `EXPLAIN (ANALYZE, BUFFERS)` of the part-0 count on a 1,200-row test session (plan lines 203-204).

## D-10. Floor of 64

`TranscriptIngestPrefix.MinEntries` is 64 (D-10, rule 4). A smaller batch records no memo and is not a skip candidate when the stored memo is longer, so it takes today's probe. A one-entry `ObserveTranscriptAsync` (`AgentSessionRuntime.cs:413-415`) after a 1,200-entry memo does not replace it: it is under 64 and shorter than the memo.

That append does not make the later count check fail. Product inserts land strictly above the current max, and the skip compares `count(*) WHERE Sequence <= S`, not the whole-session count. Rows above S leave `FirstRowId`, `TopRowId` and the prefix count unchanged. The next snapshot replay still skips. The optimisation keeps applying. A cold batch of 64 or more does pay the one range scan; that is the floor, and it is the cost the plan chose. "A live batch never pays the count" is the under-64 case.

## Foreign key and retention

`SessionRuntimeActions.TranscriptEntryId` is `OnDelete(DeleteBehavior.Restrict)` (`AppDbContext.cs:1712`). `Admit` inserts an action for every pending row (`SessionRuntimeActionCoordinator.cs:42-52`, called from `AgentSessionRuntime.cs:1589-1595`). A delete of the transcript row throws until that action is gone. The plan deletes the affected rows' actions first for W-3, W-5 and W-6 (plan lines 303-305).

`PruneTranscriptsAsync` skips a session that still has any action (`DataRetentionService.cs:363`, and the candidate filter at `:324`). `PruneSessionsAsync` does the same (`:262`) and then deletes the session under `BeginMutationAsync` (`:289-290`). The session delete cascades `TranscriptEntries` (`AppDbContext.cs:1783-1786`). Both are whole-session. Rule 5 clears the memo on that lease.

The plan's `:438` citation is the UserPrompt action phase, not the admit. The witness sentence already covers every affected row. See the advisory.

## Witnesses

| Id | At S2 (digest only) | At S3 (three checks) |
|---|---|---|
| W-4 | Green. No delete. The tail probe still sees a repeated prefix identity. | Green. |
| W-5 | Red. One middle `UserPrompt` (snapshot index 650; indexes 50, 150, … are `UserPrompt`) is skipped with the prefix. 1,199 rows, prompt missing. `FirstRowId` and `TopRowId` would not see it. | Green. The count drops, the memo is dropped, the full probe inserts the prompt again. 1,200 rows. |
| W-6 | Red. The top row is gone and the live refill occupies S, but the prefix is still skipped. 1,200 rows, deleted entry missing, expected 1,201. A count-only check is also red at S2 because S2 has no count. | Green. The count matches and `TopRowId` does not, so the full probe inserts the deleted entry. 1,201 rows. |

W-1 and W-2 are red at base on 1,205 keys and 3 commands, and green once the prefix is omitted from the probe. The counter resets immediately before the replay (plan line 224).

## D-2. One Merge

All three places say S2 and S3 land together in one Merge, cherry-picked in order into one push, and never as separate lands, and that the branch is never landed at S2:

- Section 7 (plan lines 403-407), including the Merge-brief requirement and "A Merge that cannot carry S3 in the same push as S2 stops and reports blocked."
- D-9 (plan lines 279-283), pointing at section 7 and F-4.
- F-4 (plan lines 427-430).

## Checkpoint floors

Counted on this tree, before the six new tests. Each `[Arguments(...)]` is one case. A `[Test]` with none is one case.

| Class | Methods | Cases |
|---|---:|---:|
| `AgentSessionRuntimeTests` | 41 | 48 |
| `WorkingTranscriptRecoveryTests` | 13 | 54 |
| `StandingResumeReceiptReadinessTests` | 6 | 6 |
| `SessionStateCommitTests` | 8 | 8 |
| `SessionStateCacheReadTests` | 2 | 2 |
| `DataRetentionServiceTests` | 64 | 101 |
| `PolicyRefreshServiceTests` | 21 | 24 |

CP-4 Min 124 = 48 + 6 + 54 + 6 + 8 + 2. CP-5 Min 125 = 101 + 24. CP-1 and CP-2 are the six new methods. CP-3 is those six plus the two `C1403_` methods (both single cases). No class name starts with another operand's name.

Re-import at this tip, leased, one-minute load 9.13, `ANTIPHON_LINUX_COMPILER_POLICY=0`, `UseAppHost=false`, `TEMP=/tmp`: exit 0, `imported 5 rows`, no warning line. Manifest `.antiphon/c1888-r2-review-import.yaml`. Three builds (`bin-c1888-red/`, `bin-c1888-memo/`, `bin-c1888-green/`). Ids CP-1..CP-5. `minExecuted` 6/6/8/124/125. Row timeouts 45/45/45/24/24. Estimates sum to 61. Derived total is `2*61+10` = 132 minutes. The manifest's `totalMinutes: 90` is the schema default. The plan forbids a lower `--total-timeout` (A5).

## Advisory

`AgentSessionRuntime.cs:438` is the phase that runs an already-admitted UserPrompt action. The insert is `SessionRuntimeActionCoordinator.Admit` for every pending row. W-6's top row is not a UserPrompt (the 1,200th entry is outside indexes 50, 150, …). The witness text already deletes that row's action. This does not hold the land.

## Verification

No product file was modified. `git diff --check` on `1b6c23bed..a7c74ef1c` exited 0.

`scripts/check-evidence-diff.ps1 -BaseRef 300c8a99d0d267a39791410b7540189ab702f251 -HeadRef a7c74ef1c6789dda5705e31ab2c95376a2f18088`: `EVIDENCE result commits=4 entries=0 violations=0`. The amendment range alone is `commits=2 entries=0 violations=0`.

`scripts/impacted-classes.ps1 -MergeSubset -Range 300c8a99d0d267a39791410b7540189ab702f251..a7c74ef1c6789dda5705e31ab2c95376a2f18088`: `mode=merge-subset`, `migrations=False`, `kinds=` empty. The downgrade selector was not selected. No migration file is in the diff. This plan review does not re-run the fixed rows.

`git fetch origin master` left `origin/master` at `300c8a99d0d267a39791410b7540189ab702f251`. `git merge-tree --write-tree origin/master a7c74ef1c6789dda5705e31ab2c95376a2f18088` exited 0. Tree `8fbc42793a9be0c9a4bec8b9a313b90232b028cb`.

OS-branch witnesses: no OS-selected production behavior changed. No new input wait. Parking stays dormant (CARD-1083). The plan has no positive-control rows. Mutation checks stay waived. The guard batch was not run: the subject changes no code and no tests.

Platform reads, all HTTP 200, with no fleet location recorded: `GET /api/runner-defaults` keys `revision`, `globalRunnerId`, `kindDefaults`, `updatedAt`, `lastReason`, `lastProvenance`, `lastCallerTaskId`, `supportedKinds`, `unresolvedReferences`, and `globalRunnerId=server2`. `GET /api/session-runners` returned three runners. `GET /api/version` reported capabilities `land-v2` and `operator-shutdown-v1`.

Checkpoint import: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1888-r2-plan-import -- dotnet run --project tools/Antiphon.Checkpoints -p:UseAppHost=false -- import --plan docs/superpowers/plans/2026-11-17-card-1888-transcript-ingest-probe-plan.md --out .antiphon/c1888-r2-review-import.yaml`. Lease `5ef353e1-1704-4f2f-9336-222e11b60d10`, waited 0s, held 7s, exit 0. The slot printed its known note that an implicit `dotnet run` build does not receive `-maxcpucount`.

`reviewedSourceClean` is false. The plan table imported. There is no test receipt with verified build provenance because this subject has no tests yet.
