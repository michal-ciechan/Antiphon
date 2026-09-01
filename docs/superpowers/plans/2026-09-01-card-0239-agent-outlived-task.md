# CARD-0239 — flag standing agents that outlived their task

- **Date:** 2026-09-01
- **Card:** CARD-0239 "Review: should Antiphon detect/flag per-task standing agents that outlive their task"
- **Sources:** the card's Verdict section (Grok task `0e44e0c9`, read-only investigation, 2026-09-01); the 2026-08-29 Gym Stat incident description on the card; code cited inline. The diagnosis is settled — this plan designs the implementation the verdict already chose.

## Decision

Ship **`AttentionKind.AgentOutlivedTask = 20`** — a Warning-only, self-clearing, read-time
projection in `AttentionService`, the exact shape of `CardStalled` (AttentionService.cs:311) and
`CardlessDetailsNoPrompt` (AttentionService.cs:880). Two detection arms behind one shared
exclusion set:

- **Arm 1 (live idle)** — the original incident: agent latched Running, session Running, not
  mid-turn, no open task, newest transcript entry older than **8 hours**.
- **Arm 2 (leftover identity)** — the CARD-0295 cohort: no live session, one-off shape (worktree
  cwd, or sole agent on its same-named zero-card board), no open task, untouched for **2 days**.

Detection only, the CARD-0153 rule: the row exists to be seen. Nothing stops, kills, retires or
archives anything. No new hosted sweep (`GET /api/attention` is polled every 15 s,
client/src/api/attention.ts:152), no new incident kind (`AgentIncidentKind` 43 stays free for
CARD-0292's paper reservation), no new column, no migration. Thresholds are **hardcoded
constants**, not a settings class — rationale in "Thresholds" below.

Card questions 2 (create-time `remoteControlName` hardening) and 3 ("scoped to one task" flag)
are explicitly out of scope; the verdict answered 3 with "not needed — intent is inferable from
shape", and 2 is a separate card if anyone still wants it.

## Ground truth

| Fact | Where |
|---|---|
| `AttentionKind` is append-only; last member `CardlessDetailsNoPrompt = 19`, so next is 20 | server/Application/Dtos/AttentionDtos.cs:10-11, :154 |
| `AttentionItemDto` already carries `AgentId`/`SessionId`; `OpenAgent` action + `/agents?agent=` click-through exist | AttentionDtos.cs:212-226, :189; client/src/features/attention/attentionVisuals.ts:232 |
| Read-time projections with nothing stored are the established pattern | `BuildCardStalledAsync` AttentionService.cs:311, `BuildCardlessDetailsNoPromptItemsAsync` :880 |
| The shared busy verdict is `SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct)`, `internal static`, already called twice from AttentionService | SessionMessageQueueService.cs:2601-2603; AttentionService.cs:530, :584 |
| `AgentStatus.Running` is a lifecycle latch ("has a live session"), NOT "mid-turn" — busy is the separate transcript-derived bool | server/Domain/Enums/AgentStatus.cs:7-13 |
| Agent → current session is `Agent.PersistentSessionId` (string GUID, "D"); the resolve pattern exists | server/Domain/Entities/Agent.cs:95; AttentionService.cs:901-916 |
| Exclusion fields: `AlwaysOn` :32, `IsPoolDelegate` :132, `BoardId` :98, `WorkingDirectory` :10, `CreatedAt`/`UpdatedAt` :99-100 | server/Domain/Entities/Agent.cs |
| Channel binding is `ChatChannel.AgentId` (null = unmapped) | server/Domain/Entities/ChatChannel.cs:27 |
| Open-task statuses: Queued=0, Dispatched=1, Working=2, Blocked=3; `AgentTask.AgentId` exists | server/Domain/Enums/AgentTaskEnums.cs:53-63; server/Domain/Entities/AgentTask.cs:146 |
| The one-off minted-board shape: cwd matches no project → new project (`LocalRepositoryPath = workingDirectory`) + board named `DeriveProjectName(workingDirectory, agentName)` | server/Application/Services/AgentService.cs:400-436, :408, :967-969 |
| Reusable `internal static` helpers: `PathsMatch` (normalized, OrdinalIgnoreCase) and `DeriveProjectName` | AgentService.cs:979-987, :1008-1013 |
| Real idle clock is transcript rows, NOT `AgentSession.LastSeenAt` (process heartbeat — verdict calls it "stale-but-meaningless") | server/Domain/Entities/TranscriptEntry.cs:26 (`Timestamp`, nullable), :86 (`CreatedAt`) |
| 8 h matches the installation's existing definition of "idle a long time" | ContextCompactionSettings.cs:15-16 (`IdleMinutes` default 480) |
| Fixed-constant precedent: "one number nobody has asked to tune does not need a settings class"; "a read-time Warning, not a tunable watchdog" | AttentionService.cs:37-42 (`RecencyWindow`), :52-56 (`CardlessDetailsPromptGrace`) |
| Settings-class counter-precedent exists only where a sweep shares the number | CardWorkTransitionSettings.cs:25-30 (`StaleAfterDays`, shared with the card-transition sweep) |
| The away digest is an allow-list, so a new Warning kind stays out with no change | AwayDigestProjection.cs:36, :43 |
| The nav badge counts every non-`RecentFailure` kind — the new rows will count into `Open` | AttentionDtos.cs:244-247 |
| Client kind→visual map is a total `Record` pinned by a test; a missing entry is a compile error | attentionVisuals.ts:22-38; attentionVisuals.test.ts |

## The projection: `BuildAgentOutlivedTaskItemsAsync(now, ct)`

Called from `GetAsync` after `BuildCardlessDetailsNoPromptItemsAsync` (AttentionService.cs:172);
final position in the list is irrelevant — ordering is severity-then-age at :196-199. Agent-scoped,
so the task-scoped first-match-wins loop is untouched.

### Shared candidate query (one query, both arms)

Agents where ALL of:

- `!AlwaysOn` and `!IsPoolDelegate` (the pool janitor `RetireIdleWarmAgentsAsync` owns pool rows
  and must not be second-guessed);
- no open `AgentTask`: `!_db.AgentTasks.Any(t => t.AgentId == a.Id && (Queued|Dispatched|Working|Blocked))`
  — Queued included: a task aimed at this agent means somebody still wants it;
- not channel-bound: `!_db.ChatChannels.Any(ch => ch.AgentId == a.Id)`;
- `Status` is `Running` (arm 1) or `Idle|Stopped|Failed|Disconnected` (arm 2) — `Ready` and
  `WaitingForHumanReview` never qualify.

Projected to `{ Id, Name, Status, WorkingDirectory, BoardId, PersistentSessionId, CreatedAt, UpdatedAt }`,
`AsNoTracking`. The fleet is tens of rows; the two `NOT EXISTS` subqueries are the same shape
`BuildCardStalledAsync` already runs per poll (:320-329).

### Project-root exclusion (applied in memory to the candidates)

Skip any candidate whose `WorkingDirectory` PathsMatch-es a `Project.LocalRepositoryPath` whose
project has ≥ 1 non-archived card. Two small queries: all projects `{ Id, LocalRepositoryPath }`
(the table is small), and the distinct card-bearing project ids
(`_db.Cards.Where(c => c.ArchivedAt == null).Select(c => c.Board.ProjectId).Distinct()`).
Reuse `AgentService.PathsMatch` — a re-implementation that disagreed on separators or case would
be a silent hole. This clause is what saves `Antiphon-Orchestrator` (`AlwaysOn == false`, cwd =
the Antiphon repo root, live board): a project-root worker on a board with real cards is standing
infrastructure whatever its flags say.

### Arm 1 — live idle

For candidates with `Status == Running`:

1. Parse `PersistentSessionId`; one batched query for those session ids with
   `Status == SessionStatus.Running`. Unparseable / missing / not-Running → skip silently
   (`DeadSession` and `SessionDisagreement` own latch-vs-reality drift; this row must not
   duplicate them).
2. `await SessionMessageQueueService.IsWorkingAsync(_db, sessionId, ct)` → true → skip. Mid-turn
   is never stuck — the projection's founding non-membership rule (AttentionService.cs:17-22).
3. One grouped query over the surviving session ids:
   `Max(e.Timestamp ?? e.CreatedAt)` per `AgentSessionId`. Newer than `now − 8h` → skip.
   **Zero transcript rows → skip**: an agent that never wrote anything has not "outlived" work
   it never did — `NeverStarted` / `CardlessDetailsNoPrompt` own that shape, and this rule kills
   the only overlap between the two kinds.
4. Emit: Warning, `AgentId` + `SessionId` set, `SinceUtc` = the newest-entry timestamp,
   `Actions = [OpenAgent]`, Title = agent name.
   Headline: `Standing agent idle {Duration} with no task.`
   Evidence: status, cwd, last-transcript stamp, and the remedy in words — "No open task, not
   AlwaysOn, not channel-bound. Nothing will stop it automatically; stop the agent once its work
   is confirmed done, or give it a task."

### Arm 2 — leftover identity

For candidates with `Status ∈ {Idle, Stopped, Failed, Disconnected}` (the status latch IS the
"no live session" fact — AgentStatus.cs:7-13; drift belongs to `SessionDisagreement`):

1. Clock: `SinceUtc = UpdatedAt` (stamped at creation, so it covers never-started rows too).
   Newer than `now − 2d` → skip. Any PATCH resets the clock — acceptable: an operator touching
   the row is somebody watching it.
2. One-off shape, either arm qualifies:
   - **Worktree cwd**: normalized `WorkingDirectory` contains a `.worktrees` path segment —
     the `delegate.ps1 -Worktree` / minted-worktree layout. Test on
     `DelegationWorkspaceResolver.NormalizeSeparators`, OrdinalIgnoreCase, so `\` vs `/` cannot
     dodge it.
   - **Sole agent on its same-named zero-card board**: `BoardId != null`, AND (three queries
     scoped to the candidates' distinct board ids — no wide join): the board has zero
     non-archived cards; exactly one agent row points at that board (this one); the board name
     matches `AgentService.DeriveProjectName(a.WorkingDirectory, a.Name)` or `a.Name`
     (OrdinalIgnoreCase, StartsWith to tolerate `UniqueBoardNameAsync`'s uniqueness suffix —
     implementer: verify the suffix format at AgentService before writing the comparison).
     The name clause is kept deliberately: it is what separates the minted Cohort-A shape
     (AgentService.cs:406-412) from an operator's hand-made, deliberately empty board.
3. Emit: Warning, `AgentId` set, `SessionId` null, `SinceUtc = UpdatedAt`,
   `Actions = [OpenAgent]`, Title = agent name.
   Headline: `Left-over one-off agent: {Status} for {days} days with no task and no cards.`
   Evidence names WHICH shape matched (the worktree path, or "sole agent on empty board
   '{name}'") — the operator's decision is delete-or-keep, and the shape is the argument.

The arms are mutually exclusive by `Agent.Status`, so one agent can never produce two rows.

### Thresholds — hardcoded, and why

Two `private static readonly TimeSpan` constants on `AttentionService`:
`AgentLiveIdleThreshold = 8h`, `AgentLeftoverThreshold = 2d`. NOT a settings class, and NOT read
live from `ContextCompactionSettings.IdleMinutes`:

- The settings-class precedent (`CardWorkTransitionSettings.StaleAfterDays`) exists because a
  sweep and the projection share the number and must not disagree. Nothing else consumes these
  two — there is nothing to disagree with. `RecencyWindow` (AttentionService.cs:37-42) and
  `CardlessDetailsPromptGrace` (:52-56) are the governing precedents: a read-time Warning is not
  a tunable watchdog, and one number nobody has asked to tune does not need a settings class.
- Reading 8 h live from `ContextCompactionSettings` would couple retirement detection to
  compaction tuning: an operator dropping `IdleMinutes` to 30 for compaction reasons would
  silently flood this projection. The doc-comment on the constant records that 8 h *matches* the
  compaction default (ContextCompactionSettings.cs:16) as its justification, without the coupling.

## Slices

- **S1 — server.** `AttentionKind.AgentOutlivedTask = 20` appended in AttentionDtos.cs (doc-comment
  states both arms, the exclusions, and "detection only — nothing here stops an agent"); the two
  constants; `BuildAgentOutlivedTaskItemsAsync` in AttentionService.cs; one `items.AddRange` line
  in `GetAsync` after :172. No migration, no new DI.
- **S2 — client.** `'AgentOutlivedTask'` appended to the `AttentionKind` union
  (client/src/api/attention.ts:17) and one `ATTENTION_VISUALS` entry (attentionVisuals.ts:38):
  label "Outlived its task", color `warning`, icon `TbUserOff` (or nearest available `Tb*`),
  hint "A standing agent finished (or lost) its one job and nothing retired it. Stop it, or give
  it work — nothing automatic will." The `Record` type makes the entry compile-mandatory;
  `groupOf` puts Warning in "Suspect" and `targetOf` already routes `agentId` rows to
  `/agents?agent=` — no further client code.
- **S3 — tests.** New region in tests/Antiphon.Tests/Application/AttentionServiceTests.cs under
  the shared-database discipline (:36-40): every assertion filtered to ids the test seeded, every
  test deletes what it created, never a bare count. Vitest: the pinned visuals-totality test picks
  the new entry up from the `Record`; add/adjust an enumeration only if it lists kinds explicitly.

## Test matrix (S3)

| # | Scenario | Expect |
|---|---|---|
| 1 | Arm 1: Running agent+session, idle verdict, newest transcript 9 h old, no task | one `AgentOutlivedTask`, Warning, `AgentId`+`SessionId`, `[OpenAgent]`, `SinceUtc` ≈ newest entry |
| 2 | Arm 1 but mid-turn (`IsWorkingAsync` true — open turn, no `TurnEnd`) | no row |
| 3 | Arm 1 but newest transcript 1 h old | no row |
| 4 | Arm 1 but an open task exists (representative status, e.g. Queued) | no row (also proves self-clearing) |
| 5 | Arm 1 but zero transcript rows | no row (`CardlessDetailsNoPrompt` territory) |
| 6 | Arm 2: Stopped, cwd `…\.worktrees\task-x`, `UpdatedAt` 3 d | one row, `SessionId` null, evidence names the worktree path |
| 7 | Arm 2: Idle since creation 3 d, sole agent on same-named 0-card board | one row, evidence names the board |
| 8 | Arm 2 but the board holds one non-archived card | no row |
| 9 | Arm 2 but a second agent shares the board | no row |
| 10 | Arm 2 but the sole-agent 0-card board's name matches neither agent nor cwd leaf | no row |
| 11 | Arm 2 but `UpdatedAt` 1 d | no row |
| 12 | Exclusions, one test each: `AlwaysOn`; channel-bound; `IsPoolDelegate`; project-root cwd with a live-card board (the Antiphon-Orchestrator shape, seeded as arm-1-qualifying) | no row, all four |

Run with `dotnet run --project tests/Antiphon.Tests` chunked by namespace
(`--treenode-filter "/*/Antiphon.Tests.Application/*/*"`), per docs/testing-and-build.md.

## What this card does not do

- **Never stops, kills, retires, archives or auto-escalates anything** — CARD-0153's rule, quoted
  by every sibling kind. The verbs on the row are a human's.
- No new hosted sweep, no incident row, no `AgentIncidentKind` member (43 remains reserved on
  paper by the CARD-0292 plan), no schema change.
- No create-time hardening of `POST /api/agents` `remoteControlName` (card question 2) — file
  separately if still wanted after this ships.
- No "scoped to one task" column (card question 3) — the verdict's answer is that shape already
  encodes intent.
- Does not touch `reap-zombie-agents.ps1` (unowned OS processes are a different layer) or the
  pool janitor (`RetireIdleWarmAgentsAsync` must not learn about user-created agents).
- Not added to the away digest (allow-list, AwayDigestProjection.cs:36) — a Warning is not
  "needs you now".

## Sequencing and risks

S1 → S2 → S3 in one PR is fine; S2 compiles independently but renders nothing until S1 serves
the kind.

| Risk | Standing |
|---|---|
| Idle auto-compaction writes transcript rows and refreshes arm 1's clock | Delay, not suppression: compaction fires only at ≥ 50 % fullness with a 24 h cooldown (ContextCompactionSettings.cs:19-22). Accept; note it in the member's doc-comment. |
| A PATCH to a leftover agent resets arm 2's 2-day clock | Intended reading: a touched row is a watched row. |
| Legitimate long-lived personal agent in a worktree gets a row | Warning lands in "Suspect", whose banner already says the right answer is often "leave it". No dismiss/ack mechanism exists for any kind; this one does not introduce it. |
| Per-poll cost (15 s × two endpoints) | All queries are id-scoped or over small tables; same order as `BuildCardStalledAsync`, measured acceptable since CARD-0040. |
| Board-name comparison drifts from the minting logic | Reuse `AgentService.DeriveProjectName` and `PathsMatch` (`internal static`), never copies. |
| False negative: multiple one-off agents minted onto ONE shared board (same cwd, project already existed) | Known gap in the sole-agent clause; such cohorts still surface via arm 1 while live, and via the worktree clause when cwds are worktrees (the observed cohort was). Accept for v1. |
