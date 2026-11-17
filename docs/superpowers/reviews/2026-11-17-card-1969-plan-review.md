# CARD-1969 plan review, round 1

CLEAN. No blocker. Advisories below do not hold the land.

Reviewed source `804edf5f8015ad823988fc35e8c544b8bc386b03` on `feat/card-task-8626ac28`, plan owner `8626ac28-7edc-4f07-a2f4-afa8f519ab1c`. Base and `origin/master` are `7901e9ba537b757740c2913aa2211d513b0b31f2`. The diff is only `docs/superpowers/plans/2026-11-17-card-1969-stale-working-probe-plan.md` (2 commits, +399). Reviewer is Grok. Mutation stays waived (operator 2026-11-12). Positive controls stay pending. The plan adds no session that waits for input, so the CARD-1083 release rule does not apply.

## (1) Stamp and tail

The whole-array comparisons are `current.Rows.SequenceEqual(before.Rows)` at `AgentSessionRuntime.cs:1102` and `fenced.Rows.SequenceEqual(expectedRecovery.Rows)` at `:1540`. The CheckedWorking path reads at `:1062` and `:1095` and returns at `:1108`. The idle path also reads the fence at `:1534` (`CancellationToken.None`, under the AgentSessions row lock) and the after-read at `:1124`.

Replacing those comparisons with stamp inequality or tail `SequenceEqual` is equivalent for every production writer:

| Writer | Where | What the stamp or tail sees |
|---|---|---|
| Rebased insert | `db.TranscriptEntries.Add` at `:1513`, `:1612`, and `:1648`. `storedSeq` is `e.Sequence > maxSeq ? e.Sequence : maxSeq + 1` (`:1481`). `Id` is `Guid.NewGuid()` (`:1486`). | `MaxSequence` and `TopRowId` change. |
| In-memory stub before insert | `ApplyPersistStub` (`:1803`) mutates the row that `:1648` then inserts. | A new row above the stored max. There is no UPDATE of a stored row. |
| Whole-session transcript delete | `DataRetentionService.PruneTranscriptsAsync` `ExecuteDeleteAsync` at `:372`. | `Count`, `FirstRowId`, and `TopRowId` change. |
| Session delete | `PruneSessionsAsync` deletes the `AgentSessions` row (`:289-290`). `TranscriptEntry` cascades (`AppDbContext.cs:1783-1786`). | The session's rows are gone. Count changes, or the observation is absent. |

Those are the only production `TranscriptEntries.Add` sites. There is no production `ExecuteUpdate`, per-row `Remove`, or raw `UPDATE`/`DELETE`/`INSERT` on `TranscriptEntries`. Reads elsewhere do not write.

F-1 (an in-place update, or a same-`Sequence` replacement below the 64-row tail) is unreachable through those writers. If such a write appeared, stamp and tail equality would treat the revision as unchanged and the idle path could commit a `TurnEnd`. Today's `SequenceEqual` would refuse it. The probe still decides nothing about delivery from a prefix: `UserPrompt` presence below the tail is never inferred. S3's re-grep gate is the stop if a new writer appears. Accept F-1.

## (2) Tagged statement

The plan does not paste the SQL. The required shape is one statement in `TranscriptRecoveryRead.cs`, parameters for the session id and `K`, `SqlQueryRaw` plus `TagWith`, matching `TranscriptIngestPrelude.cs:76-77` (`NpgsqlParameter`, no interpolated session id), comment tag `-- rt.recovery.observe`.

`IX_TranscriptEntries_AgentSessionId_Sequence` (`AppDbContext.cs:1747-1749`) is unique on `(AgentSessionId, Sequence)` and has no included columns. `count(*)` for one session is a range scan of that session's index entries (412,289 for b2f1a869). It is index-only only while the visibility map is all-visible; a dirty map heap-fetches. First and top `Id` are heap fetches because `Id` is the primary key and is not in the index. The tail is 64 heap tuples, including TOAST. One statement is one snapshot, so `TopRowId` is the tail's last row.

10-50 ms is unobserved and optimistic on a dirty map. A few hundred milliseconds is still acceptable against an 8.4-9.2 s read inside a 10 s budget. No migration. The fence still takes the session row lock; the hold drops from the whole read to the count. Acceptable.

## (3) Fallback

W-7 fires when the newest 64 rows contain no `RecoveryActivity` and `Count > 64`. The path then re-reads the whole session and compares arrays, which is today's decision. Queue annotations, `TurnTitle`, `CompactBoundary`, and housekeeping `UserPrompt` rows are non-activity, and a Working session can keep appending them. A session whose newest 64 rows stay non-activity pays today's cost on every probe.

That shape does not change the recovery decision, and it does not apply to b2f1a869 while a runner activity row sits in the newest 64. The 66 `CheckedWorking` probes are the runner-Working path. Residual cost, recorded as an advisory.

## (4) Witnesses

W-1 and W-2 go red at base for the reason the code gives. CheckedWorking materialises every row twice (`:1062`, `:1095`): `2n` against 128, so 600 and 2,400. The command bound is 2, which already passes. The idle path also reads the fence and the after-read, and the after-read includes the new `TurnEnd`: `4n + 1` against 193, so 1,201 and 4,801, and 4 commands against 3.

W-3, W-4, and W-5 assert `Unknown` and no stored `TurnEnd`. They are green at base because `SequenceEqual` sees the edit. They go red only when S2 compares `MaxSequence` and `TopRowId` and omits the count and the tail. W-6 stays green once `TopRowId` is present. W-7 stays green because the fallback is today's read. None of these is a constant or a self-compare.

The checkpoint tool maps a non-zero test exit to `FailedTests` and has no expected-red mode. CP-1 and CP-2 are therefore red receipts at their slice commits. The plan runs them once and does not re-run them after the tip is green. CP-3 is that green tip. CARD-1888 used the same intermediate red. The landed pair is S2 and S3 in one Merge (F-2). Accept that. Code must keep the row bound, so the red is the bound and not a loosened assertion.

Case arithmetic in the Expect column is off. The witness table is 14 cases: W-1 4, W-2 2, W-3 2, W-4 1, W-5 2, W-6 2, W-7 1. The Expect text says 12, and it counts W-3..W-7 as 6 and the CP-2 green set as 7. CP-2's green set is 9 (W-6 has two arguments). CP-3's `54 + 12 = 66` is `54 + 14 = 68`. Existing `WorkingTranscriptRecoveryTests` is 13 methods and 54 cases, so the 54 is right. Min floors 12 and 66 still pass at 14 and 68. The tool does not enforce the Expect prose. Advisory: Code follows the witness table and writes the Expect text as 14 and 68.

`AgentSessionRuntimeTests` is 41 methods and 48 cases, plus `StandingResumeReceiptReadinessTests` 6, which is the CP-4 floor of 54. After CARD-1888 the runtime class is larger and the floor still holds. `StaleWorkingTranscriptWatchdogTests` is 20 methods and 53 cases.

## (5) Checkpoints

The table under `### Checkpoints` has five rows, ids `CP-1`..`CP-5`, one build and one filter each. Builds end in `/`. CP-4 reuses CP-3. Filters have no skip contract. `ANTIPHON_LINUX_COMPILER_POLICY=0` is in the lane prose. CP-4's `\|` is the importer's escaped OR, and each operand is parenthesized and ends in `*`.

Derived row timeouts are `max(15, 3 * EstimatedMinutes)`: 45, 45, 45, 24, 30. CP-1's 15 gives 45, which does not trip the importer warning (`3 * estimate > 45`). A run with `--total-timeout` omitted uses `max(30, 2 * sum + 10)`: 40 minutes for CP-1 or CP-2 alone, 76 minutes for CP-3..CP-5. The manifest default of 90 is not that cap. The ordinary floor in the plan is 63 minutes. This review read the importer and did not re-import.

M-6a's `rows/call ≤ 65` must not become a 65th stamp row that the whole-row counter counts. W-1's 128 is `2 * 64`. The statement should return K data rows with the stamp columns repeated on each row.

## (6) Order

CARD-1888 still edits `AgentSessionRuntime.cs`. `origin/feat/card-task-6ea96bcb` is `90f3b09a4d27e6b9eb9a223a36e904f6561b5ae4`, one test commit after the plan's `2bc851b5cf1010d225f05e8ddc742dcb47ed85c6`. Against master the runtime hunks are at lines 1110, 1358, 1403, 1410, 1433, 1444, 1447, 1449, 1544, 1545, 1566, and 1577. This slice changes the `:1110` `before` argument and the fence comparison at `:1540`. F-6 stays yes: 1888 lands first.

The other named tips do not touch `AgentSessionRuntime.cs` or `TranscriptRecoveryRead.cs`:

| Card | Tip inspected | Overlap |
|---|---|---|
| CARD-1964 | `origin/feat/card-task-cad08e40` `4cfec30c8` (contains `9c0d04cf6`, phone-home pump) | none |
| CARD-1960 | `origin/feat/card-task-aaabade9` `1e82c08ae` (contains `40825c020`, quarantine-release witnesses) | none |
| CARD-1890 | `origin/feat/card-task-b754552c` `c1963c4c0` (desktop CPU investigation) | none |

## (7) Slice 2

`TranscriptPromptSpan` stays on its own plan and its own delivery-evidence review. The card's first step says that read must not get cheaper by assuming a deleted row. It shares no file with this slice. F-5 is right. A slice 2 plan task is already on the card.

## (8) Operator decisions, including M-7

| Decision | Recommendation | Review |
|---|---|---|
| F-1 | Accept the below-tail hole | Yes. Unreachable in production. A real writer of that shape would be a wrong idle commit, and the re-grep gate stops the slice. |
| F-2 | Incomplete S2, one Merge with S3 | Yes. W-3..W-5 are real outcome assertions. Mutation is waived. |
| F-3 | Later card to gate `Due()` on activity age | Yes as a later card. The premise that H-unknown is the weaker fit is stale. See M-7. Do not wait on F-3 to stop the loop below. |
| F-4 | Rotate b2f1a869 when convenient | Yes. A fresh small session fits today's 10 s budget. The rotation is a stop-gap until this slice is activated. |
| F-5 | Prompt span is slice 2 | Yes. |
| F-6 | 1888 lands first | Yes. |

M-7, from the orchestrator: 301 probes of b2f1a869, 235 `Unknown` and 66 `CheckedWorking`. `ProbeAsync` sets `LastSuccess` only when the outcome is not `Unknown` (`StaleWorkingTranscriptRecovery.cs:216`). `Due()` is true while `LastSuccess` is null (`:160`), and the next attempt waits only `PollSeconds` (15). `unknown_freshness` is that null `LastSuccess` (`:182`). The 235 Unknown results are a retry loop.

`ProbeTimeoutSeconds` is 10 (`StaleWorkingTranscriptSettings` and `appsettings.json`). Cancellation returns `Unknown` / `cancelled` (`AgentSessionRuntime.cs:1144-1146`). A whole read in the 8.4-9.2 s band consumes that budget by itself, so the second read and `GetTranscriptAsync` (`:1071`) do not finish. The refinement did not include `Reason`. `cancelled` is the reason that fits this budget. `frontier_missing`, `generation_mismatch`, `persist_failure_retained`, and a real `frontier_changed` stay `Unknown` after the cheaper read.

The stamp and tail remove the cancellation loop, not only the CPU. Each observation becomes an index count of about 412k index tuples plus 64 heap rows. Two of those plus the pull finish inside 10 s: the 66 `CheckedWorking` probes already completed the pull inside an 8.4-9.2 s probe whose cost was the two whole reads. The result is `CheckedWorking`, `LastSuccess` is set, and the next due is `IntervalSeconds` 120. Section 2 ranked H-unknown as the weaker fit from an assumed 30 probes an hour. That ranking is stale. The cheap read is what stops this loop. F-3 remains the separate question of not probing a session that is still streaming.

## Checks at the reviewed SHA

| Check | Result |
|---|---|
| `git merge-tree --write-tree origin/master HEAD` | tree `e1c575a9bf98a8d6e71bd752569e2e128d523f3c`, no conflicts |
| `scripts/check-evidence-diff.ps1` base..HEAD | commits=2, entries=0, violations=0 |
| `scripts/impacted-classes.ps1 -Range -MergeSubset -Json` | kinds empty, migrations false, downgrade count 0. The class list is the always-on merge-subset hand set. This docs diff selects nothing. |
| OS branch witnesses | The diff is the plan markdown only. No production path changed, so there is no OS-selected branch to witness. |
| Guard batch and checkpoint execution | Not run. The subject changes no code or tests, and the witnesses do not exist yet. The brief excludes both. |

`reviewedSourceClean` stays false: this round has no checkpoint-tool receipt. Producer and recipient delivery do not apply.

## Advisories

- **Section 2's H-unknown ranking.** 235 of 301 probes are `Unknown`, so `LastSuccess` stays null. The stamp and tail are what end the cancellation loop. F-3 is still a later scheduling card. An operator reading F-3 as the fix for this loop would wait on the wrong card.
- **Expect arithmetic.** The witness table is 14 cases. CP-1 and CP-2 say 12. CP-2's green count of 7 is 9. CP-3's 66 is 68. Min 12 and Min 66 still pass. Code should set the Expect text to 14 and 68 so a later review does not treat 14 executions as a roster miss.
- **Row shape versus M-6a.** W-1 allows 128 rows, which is two observations of 64. A separate stamp row that the whole-row counter counts would be 130. Return K data rows with the stamp columns repeated.
- **W-7 residual.** A session whose newest 64 rows stay non-activity keeps today's whole read and today's decision. b2f1a869 is covered while runner activity remains in that tail.
