# CARD-0100 S3 Final Review (task d4acf4f9)

Outcome: clean. CP-5 28/28, CP-6 68/68 and CP-7 7/7 ran fresh at 7ebc941b1. Both validators returned VALID. I found no defect under the verdict policy. Mutation was not run, so PC-17..PC-20 are still pending.

- Subject: Code task 981bf35e-5b7c-4efc-84b2-807a25023f5f (carry). The original S3 implementer and landing owner is 5e99ef9f-addc-43a8-a345-28f6a83bce27, and it lands with `-FromTask 981bf35e`.
- Reviewed source: `7ebc941b1d3d16cb57d5075b4b514795fb0fdff0`. The worktree was clean and the branch was not modified.
- `git merge-tree --write-tree origin/master HEAD`: origin/master was freshly fetched at `c24ad5682315a0c9fb6d4a123d0cf8bce4e538c0`. The command exited 0 with tree `6655a7dd21a8a9bbb1fdf5e02cbec97967e857ff` and no conflicts. HEAD already contains that master.

## Checkpoint run (one flow)

The tool bootstrap was one slot-gated build, `tools/Antiphon.Checkpoints/bin-c0100-rv-tool/`, with UseAppHost=false. It held the slot for 13s. CARD-1242 is still InProgress and is not on origin/master, so the run used `--keep-outputs`.

`run --plan docs/superpowers/plans/2026-10-14-card-0100-test-design.md --rows CP-5,CP-6,CP-7 --row-timeout 40m --total-timeout 150m --expected-source-sha 7ebc941b1d3d16cb57d5075b4b514795fb0fdff0 --serial --keep-outputs --max-wait 540s`

```
run: 20261014-101616-db54
source: 7ebc941b1d3d16cb57d5075b4b514795fb0fdff0 state=clean buildSource=verified
CHECKPOINT CP-5 commit=7ebc941b1d3d16cb57d5075b4b514795fb0fdff0 build=ok filter=/*/*/OrchestratorServiceIntegrationTests/* executed=28 passed=28 failed=0 skipped=0 trx=/work/worktrees/task-d4acf4f9/.antiphon/checkpoints/20261014-101616-db54/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=7ebc941b1d3d16cb57d5075b4b514795fb0fdff0 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=7ebc941b1d3d16cb57d5075b4b514795fb0fdff0 build=reused filter=/*/*/AgentTaskPipelineStatusTests/* executed=68 passed=68 failed=0 skipped=0 trx=/work/worktrees/task-d4acf4f9/.antiphon/checkpoints/20261014-101616-db54/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=7ebc941b1d3d16cb57d5075b4b514795fb0fdff0 sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=7ebc941b1d3d16cb57d5075b4b514795fb0fdff0 build=reused filter=/*/*/ExpectationSnapshotTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-d4acf4f9/.antiphon/checkpoints/20261014-101616-db54/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=7ebc941b1d3d16cb57d5075b4b514795fb0fdff0 sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
wall: 6m31s  builds: 3  max-concurrent-builds: 1  rows: 3 green 0 red 0 skipped
outputs: refused/kept bin-c0100-s3/
verdict: GREEN exit=0
```

- Each new method shows `outcome="Passed"` in the fresh TRX: T-15 in CP-5, T-16 in CP-6 and T-17 in CP-7.
- `scripts/validate-checkpoint-receipt.ps1 -Rows CP-5,CP-6,CP-7` returned `CHECKPOINT SOURCE VALID source=7ebc941b1… rows=3` (exit 0).
- `Antiphon.Checkpoints validate --rows CP-5,CP-6,CP-7` returned the same VALID line (exit 0).
- `check-evidence-diff.ps1` returned violations=0 over both ranges: `c24ad5682..HEAD` (6 commits) and `ff4d84df5..HEAD` (11 commits).
- Cleanup: I listed the outputs and checked their creation times (all 10:16 today, this session), then deleted them by literal path: `tests/Antiphon.Tests/bin-c0100-s3`, `tools/Antiphon.Checkpoints/bin-c0100-rv-tool` and `tools/Antiphon.Checkpoints/obj`. The evidence is kept at `.antiphon/checkpoints/20261014-101616-db54/`, which is ignored.

## Fail-safe audit (CHECK 1)

- **Tick:** `CardBlockers.NotBlocked` is one `NOT EXISTS` clause inside the same SQL statement as the existing candidate query (`OrchestratorService.cs:835`). It cannot throw on its own. It fails only when the whole candidate statement fails, for example on a DB error, a timeout or a missing table. In that case `LoadEligibleCandidatesAsync` throws after Reconcile and the tracker sync have already run. `PollTickAsync` then aborts, and `OrchestratorTickHostedService` logs "Orchestrator tick failed" and retries on the next interval.
  - Direction: fail-closed. Nothing is dispatched that tick, and the code never falls back to an unfiltered dispatch. A DB failure in the candidate query already behaves this way today.
  - A persistent stall would need a missing `CardRelationships` table. The S1 migration is already on master, and `Program.cs:1031/1045` applies migrations at startup, so that state is not reachable in a running server. It is not a new reachable fail-stall.
  - Strictly, this design does not meet the brief's wish that "a filter that throws must not stop the whole tick". There is no separate filter that can throw, so this is a disclosure, not a finding.
- **Watchdog** (`ExpectationSnapshotReader.cs:348`): the same single-statement shape. A failure fails the snapshot read, as any DB failure does today.
- **Pipeline:** `LoadOpenBlockersAsync` is a separate query. If it throws, `GET /api/agent-tasks/pipeline` returns the sanitized 500. The route is advisory and dispatches nothing.

## Predicate (CHECK 2)

- **Single definition:** `CardBlockers.Resolved` is the only definition. `IsResolved`, `IsOpenBlocker` and `NotBlocked` are all built from it. S2's `CardService` uses `IsResolved` for the API `resolved` flag and for `OpenBlockerCount` (`:1344`, `:1350`, `:1382`, `:1467`). I grepped for a second blocker definition and found none. The other `Done or Canceled` checks in the codebase belong to unrelated features.
- **Kind:** `IsOpenBlocker` requires `Kind == BlockedBy`, and S2 filters on the same kind. That is the only enum value.
- **Blocker on another board:** the predicate has no board clause, so the blocker counts. In the pipeline `blockedBy`, an identifier is shown without its board (disclosure).
- **Terminal column vs status:** resolution is by `Status` only (Done or Canceled) plus archived. A blocker sitting in a column with `IsTerminal=true` whose `CardStatus` is something else stays open. Its card is then held, which is the fail-closed direction. This is the D-5/A-3 whitelist from S2. Default boards map the terminal column to Done (disclosure).
- **Deleted blocker:** the FK cascade removes the row, so the card is released.
- **Blocker that is itself blocked:** its status is non-terminal, so it is open and the chain stays held until the blocker is terminal. T-17 also shows the open blocker itself staying eligible.
- **Self-edge:** rejected by `CK_CardRelationships_NotSelf`.

## Zero relationships and limits (CHECK 3)

- **Tick:** with no rows, `NOT EXISTS` is always true, so the row set is identical. Ordering is done in memory after the query, and `MaxDispatchesPerTick` is applied in the loop after that. There is no SQL `LIMIT`.
- **Pipeline:** the filter runs over the fully ordered `unstarted` list. `Total` is `candidates.Count`, and `Take(5)` is applied after the filter, so a blocked head card cannot starve the window. With an empty map, `candidates` equals `unstarted`, `Blocked=0` and `BlockedItems=null`. Both are then omitted by `JsonIgnore` (`WhenWritingDefault`/`WhenWritingNull`), and T-16 pins `{"total":0,"items":[]}`.
- **Watchdog:** the filter is in SQL, the remaining filters run in memory, and there is no limit.
- **Performance:** the correlated `NOT EXISTS` on `CardId` is served by the unique index `IX_CardRelationships_CardId_RelatedCardId_Kind`, whose leading column is `CardId`. The pipeline adds one `= ANY(array)` query, and only when there are unstarted Backlog cards.

## Tests (CHECK 4)

- **T-15** dispatches on the first tick if the filter is removed. The S3 Code task showed it red at a818f681 with "should be 0 but was 1". The second phase also catches an always-hold mutation. It would fail under PC-17.
- **T-16** turns red under PC-18 at `Total` (3 instead of 2) and under PC-19 at `Blocked`/`BlockedItems`. Card 3, whose only blocker is Done, guards against over-holding.
- **T-17** turns red under PC-20 at `EligibleBacklog` (5 instead of 4). Its `releasedByArchive` card also guards the archived branch of `Resolved`.
- All expectations are hand-written literals: 2, `[1,3]`, 1, `"CARD-0001"`, 4 and the id set.
- The diff of `origin/master..HEAD` under `tests/` contains additions only. No existing assertion was weakened.

## Docs (CHECK 5)

The new `agent-card-lifecycle.md` section and `orchestration-loop.md` §1 step 4 match the code and the named tests. That covers the whitelist, read-time computation, the one helper behind the API flag and the three reads, top 5 by rank, omission when empty, and the open blocker staying eligible. The "not covered by a test" rows are accurate.

## Disclosures (not findings)

1. The tick and the watchdog fail closed on a DB failure of the shared statement; see the audit above. A filter that degrades on its own would need a separate query.
2. R-3 names `AgentTaskPipelineEndpointTests` (4), but no CP row selects it. It was not rerun here because the brief asks for one flow and the host is loaded. Code 5e99ef9f ran it 4/4 at d76e14e4, and the master merge touched none of the pipeline code. This is a plan gap: R-3 should either name a CP row for it or drop it.
3. The plan said `BacklogRow` would carry `OpenBlockerIdentifiers`. The implementation uses a separate `LoadOpenBlockersAsync` query instead, with the same result.
4. A Canceled blocker and a reopened blocker are not covered at pickup. The shared `Resolved` expression is pinned by S2's `Resolved_follows_…` test.
5. The pipeline's `blockedBy` identifiers carry no board, so a blocker on another board can be ambiguous.
6. The S3 Code task edited `CardBlockers.cs`, an S2 file. It disclosed this scope drift itself.
7. PC-17..PC-20 are pending for SourceLanding Mutation. The whole Unit lane was not run, per the brief.
