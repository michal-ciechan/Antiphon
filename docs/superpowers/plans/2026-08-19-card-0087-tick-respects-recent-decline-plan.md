# CARD-0087 — Tick respects a declined spawn: plan

**Date:** 2026-08-19
**Status:** planned (not implemented)
**Card:** CARD-0087 (`525ef4ca-8f89-4d0b-84d6-231ae4e83890`) — moving a card into an
active column with spawn suppressed still gets auto-dispatched by the tick a minute
later
**Incident:** 2026-08-19. `card.ps1 move` of CARD-0082 and CARD-0083 to In Progress
(no `-Spawn`) printed "moved into an active column; NO agent was started" as
designed. Within ~30–60 s `OrchestratorTickHostedService` spawned real Claude
sessions anyway (`4e0edb45`, `4893e4f3`), both failed fast, and left empty
`feat/card-CARD-0082` / `feat/card-CARD-0083` worktrees.

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**Durable hold, not a recency window.** Persist
`Card.AutoDispatchHeldAt` when a human lands a card in an active column and
explicitly declines to start work. `OrchestratorService.LoadEligibleCandidatesAsync`
must skip rows where that timestamp is set, the same way it already skips
`ArchivedAt != null`.

A "recent-manual-move-with-no-spawn" check is the same marker plus a TTL. The live
miss is the operator's intent ("file this here, do not start it"), not a race
against `PollIntervalSeconds` (30). CARD-0082/0083 were meant to sit in In Progress
as plans. Any window the tick can out-wait re-creates the miss; any window longer
than a planning session is a hold pretending not to be one. Recency also has
nowhere to look today: `SpawnSuppressed` is a response DTO field only
(`MoveCardResult`), and `CardRevisionKind.Move` does not record the spawn flag.

One Code slice (entity + migration + write/clear + eligibility filter + tests).
No UI badge in this slice.

## 1. Current shape (verified 2026-08-19)

### 1.1 Two independent paths

| Path | What it does | Knows about Spawn=false? |
|---|---|---|
| `CardService.MoveAsync` (`CardService.cs:241-289`) | Moves, then spawns only if `request.Spawn`. Else sets `spawnSuppressed` on the **result** and returns. | Yes — for this call only |
| `OrchestratorTickHostedService` (`:27-37`) every `PollIntervalSeconds` (30, `OrchestratorSettings.cs:6` / `appsettings.json`) | `PollTickAsync` → `LoadEligibleCandidatesAsync` (`OrchestratorService.cs:481-525`) | No |

`LoadEligibleCandidatesAsync` today (`:492-499`):

```
BoardColumn.IsActive && !IsTerminal
ArchivedAt == null
OwnerSessionId == null
no AgentSession in Starting/Running
RetrySchedule missing or due
```

That is the entire eligibility rule. A bookkeeping move satisfies it the moment
`SaveCardWriteAsync` returns.

CARD-0051 (spec `2026-08-17-card-0051-card-api-and-cli-ergonomics.md` D4; the card
text calls this CARD-0040's family) already made spawn **opt-in on the move
endpoint**. It did not close the gap: the tick is a second writer. The CLI even
tells the operator to re-run with `-Spawn` (`scripts/card.ps1:385-386`), i.e. the
shipped contract is "do not start work", not "do not start work until the next
tick".

### 1.2 Who already opted in

The UI always sends `spawn: true` (`MoveMenu.tsx:72-75`, `CardThreadPanel.tsx:358`).
Human drag/approve still starts a session immediately. The tick is not that UX.

The tick's real customers, and they stay eligible:

- Cards **seeded** in an active column (`OrchestratorServiceIntegrationTests.CreateGraph`
  / `AddCard` at `:1195-1214`) — the existing dispatch tests.
- External-tracker sync that materialises a card in an active state
  (`PollTick_syncs_external_issue_into_card_and_dispatches_it`).
- A card whose session died and whose `RetrySchedule` is due — hold was never set
  (they asked to start).

`SpawnAsync` (`CardService.cs:514`) is the explicit start: it claims via
`TryClaimCardAsync` and enqueues. That is how a held card begins work.

### 1.3 Reopen is the same hole

`ReopenAsync` (`:407-445`) calls `ApplyColumnMove` directly so it cannot spawn
(CARD-0054). `CardCorrectionIntegrationTests` already pins reopen-into-active
starts nothing (`:1090-1102`). The tick will still pick that card up, for the
same reason as a Spawn=false move. Hold on reopen-into-active is in this slice;
it is not a second card.

## 2. Decisions

| Option | Decision | Why |
|---|---|---|
| Recency window on the latest move | **Reject** | Delays the live miss; does not encode "do not start". Needs a persisted marker anyway (`Move` revisions have no spawn flag). Window vs 30 s tick is a race the incident already lost. |
| Infer decline from `CardRevisions` | **Reject** | Extra join on every tick; still no spawn bit on the row; same TTL temptation. |
| Disable auto-dispatch entirely (tick only retries) | **Reject (this card)** | Would kill the seeded/synced work-queue the existing orchestrator tests pin. Hold is the decline, not a product rewrite. |
| Durable `AutoDispatchHeldAt` on `Card` | **Take** | Same shape as `ArchivedAt`: nullable timestamp, eligibility filter, set at the moment of intent, cleared by an explicit start. |
| Backfill sitting unowned In Progress cards | **Reject** | Those rows are the tick's work queue. Hold is set at decline time. The 2026-08-19 sessions already fired. |
| UI "held" badge / third "release hold so the tick may pick it up" verb | **Out of this slice** | Un-hold is `POST /spawn` or a later move with `Spawn: true`. DTO field is enough for a later badge. |
| Tick honouring "Grok for everything" | **Out of this card** | Compounding factor on CARD-0087 (`PrepareStartRequestAsync` uses `_agentRegistry.Settings.DefaultDefinition`). Separate from eligibility. |

## 3. The slice (one Code)

### 3.1 Column

`Card.AutoDispatchHeldAt` (`DateTime?`), PascalCase column, no extra index
(`ArchivedAt` has none). Doc comment: set when a card is placed in an active
column and work was declined; `LoadEligibleCandidatesAsync` skips it; cleared by
`SpawnAsync` and by a move off an active column.

Migration `AddCardAutoDispatchHeldAt`. **Stop the AppHost first** — the running
process holds `server` file locks (`AGENTS.md` / `CLAUDE.md`).
`.\stop-server.ps1` (or `scripts/restart-apphost.ps1` after the add).

`AppDbContext` Card block (`:795-866`): add the property next to `ArchivedAt`.
`CardDto` last-with-default, same as `ArchivedAt` (`BoardDtos.cs:68-70`);
`BoardService.ToCardDto` (`:217-263`) maps it. Client `CardDto` in
`client/src/api/boards.ts` lockstep. No badge, no new endpoint.

### 3.2 Write / clear (not `ApplyColumnMove`)

Keep spawn knowledge out of `ApplyColumnMove`. CARD-0051 put it in `MoveAsync`
on purpose.

**Set** `AutoDispatchHeldAt = UtcNow()`:

- `MoveAsync` when `targetColumn.IsActive && card.OwnerSessionId is null && !request.Spawn`
  (the existing `spawnSuppressed = true` arm at `:277-282`). Do it **before**
  `SaveCardWriteAsync` so the hold lands in the same write as the move — otherwise
  a tick between the two saves can still claim.
- `ReopenAsync` when the resolved target `IsActive` and the card is unowned, after
  `ApplyColumnMove` mutates the in-memory card and **before** `SaveCardWriteAsync`.

**Clear** to null:

- `SpawnAsync`, before `TryClaimCardAsync`. Explicit start always lifts the hold,
  including the SpawnAsync-from-backlog path that moves into active itself.
- `MoveAsync` when the target is **not** active (Backlog / Review / terminal). Next
  active-column entry is a new decision.

Do **not** touch `CardLifecycleTransitions.TryMoveToReview`. A stale timestamp on
a Review card is excluded by `IsActive` already.

Fail-closed default: `AutoDispatchHeldAt` stays null, so existing CreateGraph /
tracker-sync cards keep dispatching.

### 3.3 Eligibility

`LoadEligibleCandidatesAsync`, with the other durable filters:

```
.Where(c => c.AutoDispatchHeldAt == null)
```

That is the whole tick change. Do not add a time comparison. Do not read revisions.

### 3.4 Tests

All assertions **scoped to the card the test created** (shared Postgres). Do not
assert `result.Dispatched == 0` as the only proof — that counter is assembly-global
(`OrchestratorServiceIntegrationTests` class comment).

1. **Write path** — sibling of
   `Moving_into_an_active_column_without_asking_starts_nothing_and_reports_that`
   (`BoardServiceIntegrationTests.cs:259`). After the existing assertions: stored
   `AutoDispatchHeldAt` is not null. Then `SpawnAsync` on that card: hold is null
   and a session is claimed.
2. **Tick skip** — `OrchestratorServiceIntegrationTests`, `[NotInParallel]` class.
   Seed an active unowned card **with** `AutoDispatchHeldAt = now` (direct on the
   entity is enough here — the write path is test 1). `PollTickAsync`. This card
   has zero `AgentSessions`. Leave
   `Orchestrator_dispatches_eligible_card_through_agent_session_service` untouched:
   it is the no-hold control.
3. **Reopen** — extend
   `Reopen_defaults_to_the_backlog_column_and_never_spawns`
   (`CardCorrectionIntegrationTests.cs:1057`; the into-active arm is `:1090-1102`):
   after reopen-into-active, `AutoDispatchHeldAt` is set.
4. **API surface** — the existing
   `CardCorrectionApiTests` spawn-suppressed PATCH (`:155-169`) can assert
   `result.Card.AutoDispatchHeldAt` is not null if the DTO is wired; do not add a
   headed BoardE2E.

Do not widen timeouts. Do not start a real Claude.

### 3.5 One-line docs in the same slice

- `scripts/card.ps1` header + the `spawnSuppressed` print (`:385-386`): the tick
  will not pick this card up either; `-Spawn` or `POST /spawn` starts it.
- `AGENTS.md` CARD-0051 bullet: same sentence.

No CLAUDE.md gotcha. No new spec file.

## 4. Out of scope

- Recency / TTL on the hold.
- Backfill.
- UI badge, hold-release verb, or changing UI `spawn: true`.
- Stopping the tick from dispatching un-held active cards.
- DefaultDefinition / "Grok for everything" (compounding, not this defect).
- Closing the card. This plan lands; a Code slice implements.

## 5. What the Code agent runs

Stop AppHost/server before `dotnet ef migrations add`. Alternate output path,
**forward slash**, then delete the `bin-card0087/` directories:

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0087/ -- --treenode-filter "/*/*/*Moving_into_an_active_column_without_asking*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0087/ -- --treenode-filter "/*/*/OrchestratorServiceIntegrationTests/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0087/ -- --treenode-filter "/*/*/*Reopen_defaults_to_the_backlog_column_and_never_spawns*"
```

Orchestrator class is `[NotInParallel]` — run it as a class, do not co-schedule
another PollTick sweeper.

## 6. Commit

`fix(orchestrator): CARD-0087 - hold auto-dispatch when a move into an active column declines spawn`
