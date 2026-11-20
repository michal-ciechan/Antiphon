# Astra 6.2 selectable fix: Review round 3

Review task `d99e58ac` (Claude Opus; the author is Grok). CARD-1514. This is round 3 of 3. It reviews the diff only and reports blockers only.

- Subject: `origin/feat/card-task-5361412b` at `52ab30e5a86f6fd7e8d93526cf3c8cd7279634d0`. The branch is stacked on rounds 1 and 2 (`2c673f12` -> `a028435d` -> this round).
- Range reviewed: `e00891f6961b10a4cbda2bb9a03f60faf45b0df9..52ab30e5a86f6fd7e8d93526cf3c8cd7279634d0`. Its commits are red `070af154c`, fix `e0c12dcc7`, plan `8b74d20f0` and index `52ab30e5a`.
- Master: `9dbcd3471ef0d46ef96fcf101bd5214253415309`.
- Verdict: **CLEAN**. R2-D-1 is closed. This round found no blockers and no regressions.

## R2-D-1: closed

`TryReuseWarmAgentAsync` pinned pool branch (`server/Application/Services/AgentTaskDispatcher.cs:8919-8935`):

1. Busy first. The alias decline moved below `if (pinned.Status != AgentStatus.Idle || pinned.PoolIdleSince is null) return WaitForAgent;`. A busy pinned pool row now returns `WaitForAgent` for ladder tasks and alias tasks alike, so the CARD-1481 ordering holds for both. Hole (a) is closed. `a_busy_pinned_alias_task_waits_for_its_pool_delegate` witnesses it. The test asserts `WaitForAgent`, an unchanged pin, no session on the task, the row still `Running` and its session still `Running`. Before the fix the early alias return gave `SpawnFresh`, so the witness can go red.
2. Alias history. A FREE pin returns `SpawnFresh` when `MayWarmPool(claimed.RequestedModelAlias)` is false. It also returns `SpawnFresh` when `PoolDelegateRelease.WithRequestedAlias(_db).AnyAsync(t => t.AgentId == pinned.Id)` finds a task. Hole (b) is closed: a ladder task, including a Frontier Review, can no longer inherit a free row whose live process was launched with an alias. `a_ladder_task_pinned_to_alias_history_spawns_fresh` reaches this branch only through alias history. Every earlier check passes, and the pre-fix code returned `Reused`, so the witness can go red.
3. One predicate. The shop query (`AgentTaskDispatcher.cs:8954`) and the desktop warm query (`AgentTaskDispatcher.DesktopMemory.cs:65`) call the same `WithRequestedAlias` helper. The pinned check calls it too.

Other paths I checked, none blocking:

- `ExistingProcessCanTakeAsync`, pinned branch (`DesktopMemory.cs:44-53`). It does not check alias history. It is the read-only memory prediction. A true there for a free alias-history pin is a false positive, and the type comment states that the real `SpawnFresh` recovers it. It was not changed in this range.
- The pool release sweep (`AgentTaskDispatcher.cs:9624`) checks only the newest task's alias before pooling a row warm. A row warmed that way is still unreachable: the shop and the pinned path both exclude alias history. It was not changed in this range.
- `AgentTaskReplyService.cs:2895` checks the replying task's own alias for its own session. It is not a pool reuse decision.
- The standing-agent path returns through `PlaceOnStandingAgentAsync` before any pool check. The diff does not touch it.

## No regression without an alias

With no alias on the claim and no alias history on the row, the new condition is false. Control then reaches `ClaudeEffortMatchesPool` exactly as it did before. The no-alias unpinned path is unchanged. `AgentTaskPoolTests` ran 54/54 green and `CodexDelegateDispatchTests` ran 30/30 green.

## The helper

`WithRequestedAlias(db) => db.AgentTasks.Where(t => t.RequestedModelAlias != null)` is an `IQueryable`:

- In the shop and desktop queries it translates to a correlated `EXISTS` inside the single candidate query. That is not N+1.
- The pinned path adds one `AnyAsync` round trip. It runs only for a FREE pinned pool row that passed every earlier check.
- `!= null` matches `IsNullOrWhiteSpace` for stored rows. `AgentTaskService.ExplicitDelegatableAlias` returns null for blank input and rejects anything that is not a known alias, so the database never holds a blank alias.

## Files and pins

- Six files changed, as claimed: the dispatcher, `DesktopMemory`, `PoolDelegateRelease`, `AgentTaskPoolTests`, `tests/derived-impact-index.json` and the fix2 checkpoint plan.
- `git merge-tree --write-tree origin/master 52ab30e5a` is clean (tree `b3719ce31`).
- On that merged tree (probe commit `c82de6d23`), `scripts/test-live-lane-run.ps1 -PrintInputDigest` prints `15137e48291f1b0c70668a7e967965ed1391f64812cddea271387d48ce1a9365`. That equals the `PinnedInputDigest` master states.
- On the subject tip, CP-8 confirms digest `c0d2a723...` and policy hash `8c376b03...`. This branch edits no pinned input.
- `scripts/regenerate-derived-impact-index.ps1` on the merged tree, under the build slot, prints `derived-impact-index unchanged`.

## Checkpoints

The checkpoint tool ran once. Run `20261120-141220-b970` at `52ab30e5a`, with one build `bin-astra62fix2` reused by every test row. Verdict GREEN exit 0. `validate --evidence report.json --expected-source-sha 52ab30e5a86f6fd7e8d93526cf3c8cd7279634d0` prints `CHECKPOINT SOURCE VALID rows=6`. Every row has `slot=granted`, `dirty=0` and `sourceState=clean`. The test rows also have `buildSource=verified`.

| CP | Result |
|---|---|
| CP-1 witnesses | 2/2 passed, 0 failed, 0 skipped |
| CP-2 `AgentTaskPoolTests` | 54/54 passed |
| CP-3 `CodexDelegateDispatchTests` | 30/30 passed |
| CP-5 nine-class Linux guard batch | 81/81 passed |
| CP-7 `check-evidence-diff` `e00891f6..HEAD` | exit 0 |
| CP-8 pins (digest + policy hash) | exit 0, `pin-ok` |

Rows not run, as the brief directs (SHORT: build once, run the touched classes and the guard batch):

- CP-4, the matrix/complexity/held-chain classes. The diff touches nothing they cover beyond the dispatcher branch, which CP-2 and CP-3 exercise.
- CP-6, `DerivedImpactIndexTests`. I ran the index regeneration directly on the master-merged tree instead (unchanged). The plan's own unlisted driver build exists for that row.

Windows was not observed. Mutation is waived (operator 2026-11-12). PCs stay pending for SourceLanding Mutation.

## Advisories (do not hold the land)

- A-1: `AgentTasks` has no index on `AgentId`. The alias-history `EXISTS`/`AnyAsync` therefore uses another index or a scan. A partial index `(AgentId) WHERE "RequestedModelAlias" IS NOT NULL` would make it cheap if the table grows.
- A-2: alias history is permanent. After a ladder task relaunches the row without an alias (`SpawnFresh` on the same row), that row still never warm-reuses. This is conservative and costs only warmth. A relaunch watermark, meaning alias tasks after the row's latest launch, would restore reuse.
