# CARD-0100 S1+S2 repair (F1, F2 of Final Review 3501caaa) — Code b45be219

Branch `feat/card-task-b45be219`, worktree `/work/worktrees/task-b45be219` (runner mirror), continued
from `79fb16126c636be0774cfb2a5cbf88dc1f6415f2` (donor Code 59dcf45c). Original landing owner `b371d29d`.
Source tested tip: `9f71e6493dde1db80aa62016cb34879527625d0e`. Commits added (no rebase, no merge):

- `7362615fecfc7d94c59543e8b801253948ea9794` test: T-30 concurrent ring test (red first)
- `62576fdb752a7ccd3d8b53ab3a1d36888918b91d` fix: graph advisory lock
- `9f71e6493dde1db80aa62016cb34879527625d0e` docs: F2 corrections + test design (tested tip)
- a following docs-only commit stores this report (no source change after the tested tip)

## F1 — fix

`CardService.AddRelationshipAsync`: after the token/archive/resolution checks, a transaction is opened
and `SELECT pg_advisory_xact_lock(hashtext('antiphon.card-relationships.graph'))` taken BEFORE the
duplicate check and the cycle walk; the insert/revision/token save then commits the transaction, which
releases the lock. Same idiom as `HostBudgetService`, `SpecialistRequestService.Qualification`,
`DispatchConcurrencySettingsService.TakeLockAsync`. No execution strategy is configured, so no retry
wrapper is needed.

Deviation from the brief's example: one graph-wide key, not a per-board key. Plan D-4 / T-14 allow
cross-board edges, so a ring spanning two boards would take two different per-board locks and both
adds could still pass. Relationship adds are low-rate operator writes; one key is the smallest sound
mechanism.

Fail-closed: a lock statement failure (connection loss, cancellation) throws before any insert and the
disposed transaction rolls back; it surfaces as the existing unhandled-exception problem (500), not a
409. No lock timeout was added (the neighbouring advisory-lock services also wait unbounded, bounded by
request cancellation). If the caller wants a bounded wait mapped to a 409, that is a follow-up decision.
RemoveRelationshipAsync is unchanged (removing an edge cannot create a cycle).

## F1 — test (T-30)

`CardRelationshipApiTests.Concurrent_adds_closing_a_ring_commit_all_but_one_and_refuse_one_as_a_cycle`
`[Arguments(2)]`, `[Arguments(3)]`. Ring of N cards, card i blocked by card i+1 mod N, N POSTs sent at
once while a test transaction holds `FOR UPDATE` on the N card rows. Each add's plain reads (card,
resolution, duplicate, walk) do not block on those row locks; its save does. The test releases the rows
only when N backends wait on the holder directly or transitively (`pg_blocking_pids`, recursive CTE),
which is reached deterministically in both builds: without the lock, all N have finished their walk and
wait at the save; with it, one waits at the save holding the graph lock and N-1 wait on that lock. An add
completing before release fails the test. Asserts N-1 x 201, N-1 ring rows persisted (so no ring), and
one 409 `card_relationship_cycle`.

Red proof (unlisted run, reason: brief requires red on 79fb16126 for the new test): commit `7362615f`
(test only, production = 79fb16126), slot-gated build `bin-c0100-red/` (0 errors, slot granted
waited=0s) and one filtered run of the method: total 2, failed 2, both at
`statuses.Count(s => s == HttpStatusCode.Created) should be N-1 but was N` (ring 3: `Created, Created,
Created`). TRX: `.antiphon/c0100-f1-red/red.trx` (ignored).

## F2 — documentation corrections

Removed / changed:
1. Plan D-3 "(cards are never hard-deleted in production; test cleanup `ExecuteDelete`s cards)" — deleted;
   replaced by: board and project deletion physically delete the board's cards
   (`ProjectCascade.DeleteBoardsAsync`, `ProjectCascade.cs:127`); deleting either card removes its rows (T-3).
2. `CardService.FindBlockerPathAsync` XML comment: now "the chain from start to target, both included,
   each card blocked by the next" (matches `path.Add(target)`; cited test T-9 three_cycle).
3. Plan D-6 "depth cap 64" and plan row 5 "with a depth cap" — the code has no cap (Review disclosure);
   D-6 now says "no depth cap" and describes the graph lock (T-30).
4. `CardRelationship.cs` summary: deleted "so the row is never rewritten when the blocker closes"
   (no test asserts it).
5. `BoardDtos.cs` `Blockers` comment: "Every BlockedBy row ..." -> "The BlockedBy rows ..." with the
   T-13 test cited.

Checked and left (each tied to a named test or a code citation): plan row 9 "`ArchiveAsync` keeps rows"
(T-10 row present after unarchive), D-7 rows persist on close/reopen (T-11), D-9 rows survive archive
of either side (T-10, T-11), test design line 142 (isolated clone), `CardBlockers` whitelist comment
(cites T-11). `CardRevision.cs` "(null on every other kind)" section headers are the file's existing idiom
on pre-existing columns; left.

Test design: T-30 row, V-3 gains T-30, G-34 / PC-34 (pending Mutation), guards 34, CP-3 now 21
executions over 12 methods (Min 21), PC floor 34.

## Checkpoint run (one flow, at 9f71e6493dde1db80aa62016cb34879527625d0e)

Tool invoked as `dotnet tools/Antiphon.Checkpoints/bin-c0100-red/Antiphon.Checkpoints.dll run --plan
docs/superpowers/plans/2026-10-14-card-0100-test-design.md --rows CP-1,CP-2,CP-3,CP-4 --row-timeout 40m
--total-timeout 150m --expected-source-sha 9f71e649... --serial --max-wait 55s` then `wait`. The tool dll
came from the slot-gated red-proof build (tool source unchanged in the range), so no separate bootstrap
build ran. Note: the launcher was additionally wrapped in `build-slot.ps1` (unnecessary; it held a
lease 65 s, the executor took its own leases without waiting).

```
--- checkpoint report ---
run: 20261014-071933-205f   manifest: /work/worktrees/task-b45be219/.antiphon/checkpoints/20261014-071933-205f/manifest.resolved.yaml
commit: 9f71e6493dde1db80aa62016cb34879527625d0e  branch: feat/card-task-b45be219  worktree: /work/worktrees/task-b45be219  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 9f71e6493dde1db80aa62016cb34879527625d0e state=clean buildSource=verified
CHECKPOINT CP-1 commit=9f71e6493dde1db80aa62016cb34879527625d0e build=ok filter=/*/*/CardRelationshipSchemaTests/* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-b45be219/.antiphon/checkpoints/20261014-071933-205f/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=9f71e6493dde1db80aa62016cb34879527625d0e sourceState=clean buildSource=verified
PHASES CP-1 slotWait=0s build=298.9046347s startup=107.950054s testsWall=1.8870688s teardown=3.0290783s hostWall=112.8662038s
CHECKPOINT CP-2 commit=9f71e6493dde1db80aa62016cb34879527625d0e build=reused filter=/*/*/CardCorrectionApiTests/* executed=12 passed=12 failed=0 skipped=0 trx=/work/worktrees/task-b45be219/.antiphon/checkpoints/20261014-071933-205f/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=9f71e6493dde1db80aa62016cb34879527625d0e sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=0s startup=53.6615827s testsWall=6.0794822s teardown=3.1111457s hostWall=62.8522106s
CHECKPOINT CP-3 commit=9f71e6493dde1db80aa62016cb34879527625d0e build=reused filter=/*/*/CardRelationshipApiTests/* executed=21 passed=21 failed=0 skipped=0 trx=/work/worktrees/task-b45be219/.antiphon/checkpoints/20261014-071933-205f/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=9f71e6493dde1db80aa62016cb34879527625d0e sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=0s startup=52.8075481s testsWall=10.298416s teardown=3.1188302s hostWall=66.2247943s
CHECKPOINT CP-4 commit=9f71e6493dde1db80aa62016cb34879527625d0e build=reused filter=/*/Antiphon.Tests.Application/(CardIdentifierResolutionTests*)|(CardTaskFileRendererTests*)/* executed=30 passed=30 failed=0 skipped=0 trx=/work/worktrees/task-b45be219/.antiphon/checkpoints/20261014-071933-205f/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=9f71e6493dde1db80aa62016cb34879527625d0e sourceState=clean buildSource=verified
PHASES CP-4 slotWait=0s build=0s startup=5.1701715s testsWall=56.0709794s teardown=3.2578523s hostWall=64.4990032s
unlisted: none (the tool ran no other build or test command)
wall: 10m10s  sequential-equivalent: 5m08s  builds: 3  max-concurrent-builds: 1  rows: 4 green 0 red 0 skipped
refused: output-ledger-mismatch; left in place: /work/worktrees/task-b45be219/tests/Antiphon.Tests/bin-c0100-s12/fakegrok.dll
outputs: refused/kept bin-c0100-s12/, bin-c0100-s3/, bin-c0100-s4/
evidence: /work/worktrees/task-b45be219/.antiphon/checkpoints/20261014-071933-205f/report.md
verdict: RED exit=2 reason=refused: output-ledger-mismatch; left in place: /work/worktrees/task-b45be219/tests/Antiphon.Tests/bin-c0100-s12/fakegrok.dll
```

Exit 2 is the known CARD-1236/1242 output-ledger-mismatch (fakegrok.dll) plus tool cleanup refusal
`output-memory-authority-missing`; no test was red. TRX audit: CP-1 3/3 (run 3ef3de1a), CP-2 12/12
(92347f00), CP-3 21/21 (2977d9d8; both `Concurrent_adds_...(2)` and `(3)` Passed), CP-4 30/30 (1da2ef5f).
Total 66 (was 64; +2 T-30 rows). `scripts/validate-checkpoint-receipt.ps1 -Evidence .../report.json
-ExpectedSourceSha 9f71e649... -Rows CP-1..CP-4`: `CHECKPOINT SOURCE VALID source=9f71e649... rows=4`.

Cleanup (literal paths): removed `tools/Antiphon.Checkpoints/bin-c0100-red`, `tests/Antiphon.Tests/bin-c0100-s12`,
`.antiphon/checkpoints/20261014-071933-205f/tool`, and the 27 other `bin-c0100-red` project outputs of the
red-proof build. Kept ignored evidence (TRX, receipts, `builds/bin-c0100-s12` stamp, `.antiphon/c0100-f1-red`).

## Other checks

- `scripts/check-evidence-diff.ps1 -BaseRef 88540babcef8413c662a34a07f2e19f7055ca5d9 -HeadRef HEAD`: exit 0, violations=0.
- `git merge-tree --write-tree origin/master HEAD` with origin/master `15a9071fa50033a5e7910f806180f36e3c92082a`: exit 0, no conflicts.
- No whole-Unit lane (brief). Mutation not run: PC-1..PC-34 pending (PC-34 new, graph lock).
- Out of scope, untouched: CARD-1244 ProjectCascade CardWorktreeCleanups FK; S3..S5.
