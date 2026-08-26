# CARD-0210 — stop minting a project/board per delegate task and per agent

**Status:** investigation + fix design. No implementation in this doc. Cleanup of the existing
163 empty boards is explicitly **out of scope** here (separate step, its own safety-gated script in
the CARD-0144 / CARD-0118 mould).

**Measured 2026-08-26 against the live API** (`GET /api/boards`, `/api/agents`, `/api/projects`):
164 boards, 72 projects, 27 agents. Exactly one board has cards (`Antiphon`, 210 cards, project
`Antiphon`, path `C:/src/Antiphon`). Of the 163 empty boards, **122 are named `task-<8hex>`**
(the delegate-task shape, Source 1), and the other 41 are one-per-standing-agent boards plus
hand-made probe boards (Source 2 / Source 3).

---

## Source 1 — a board per delegated task

### What actually creates it (it is NOT the dispatcher)

`scripts/delegate.ps1` → `POST /api/agent-tasks` → `AgentTaskService.CreateAsync` → (tick)
`AgentTaskDispatcher` → `ResolveAgentAsync`:

- `server/Application/Services/AgentTaskDispatcher.cs:2101-2144` — a fresh **pool delegate**
  `Agent` row is created with `Name = Slug = $"task-{shortId}"` (`:2122-2123`),
  `WorkingDirectory = task.WorktreePath ?? task.WorkingDirectory` (`:2129`),
  `IsPoolDelegate = true` (`:2140`) and **no `BoardId`** — the initializer never sets one, and the
  row goes in with `_db.Agents.Add(agent)` (`:2143`). Its project scope is stamped separately as
  `agent.PoolProjectId = claimed.ProjectId` (`:1586`, CARD-0115). This path creates **no Project and
  no Board**. Grepping the whole server, only two places construct a `Board`:
  `AgentService.BuildAgentBoard` (`AgentService.cs:837`) and `BoardService.CreateAsync`
  (`BoardService.cs:123`); only two construct a `Project`: `AgentService.ResolveProjectForWorkingDirectoryAsync`
  (`AgentService.cs:814`) and `ProjectService.CreateAsync` (`ProjectService.cs:55`). Neither
  `AgentTaskService`, `AgentTaskDispatcher` nor `AgentTaskReplyService` references `AgentService`
  at all.

- The board is minted **later, by the startup backfill**:
  `server/Program.cs:569-574`
  ```csharp
  // Every agent must have a default board (Add-Work and card routing rely on it) — create
  // boards for any agent that predates the rule or lost its link to the old update path.
  var backfilled = await scope.ServiceProvider.GetRequiredService<AgentService>()
      .EnsureAgentBoardsAsync(CancellationToken.None);
  ```
  → `server/Application/Services/AgentService.cs:484-546` `EnsureAgentBoardsAsync`:
  selects **every** agent with `BoardId == null` (`:491-494`, no `IsPoolDelegate` filter), then per
  agent calls `ResolveProjectForWorkingDirectoryAsync(agent.WorkingDirectory, agent.Name, …)`
  (`:507`) and, when no same-named unclaimed board exists in that project, `BuildAgentBoard(project,
  UniqueBoardNameAsync(project.Id, agent.Name), now)` (`:519-524`), saves, and publishes
  `BoardChanged` / `AgentChanged`.

- `ResolveProjectForWorkingDirectoryAsync` (`AgentService.cs:814-835`) looks up a project by
  **exact string** `p.LocalRepositoryPath == workingDirectory` (`:818-819`) and otherwise creates
  one named `DeriveProjectName(workingDirectory, fallbackName)` = the **leaf of the working
  directory** (`:854-860`). That is where the two observed shapes come from:
  - **Worktree tasks** (`-Worktree`, and every sub-orchestrator by default): the worktree is
    `C:\Antiphon\worktrees\card-task-<hex>` (`DelegationWorktreeService.cs:78` names it
    `task-<hex>`, `WorktreeManager.cs:18` prefixes `card-`). No project has that path, so a
    **new project `card-task-<hex>`** is created and the board `task-<hex>` hangs off it.
    Live: 44 task boards sit in 42 `card-task-*` projects (a sub-orchestrator worktree shared by
    several tasks accounts for `card-task-a6e163fe x3`).
  - **Shared tasks** (the default for workers): `WorkingDirectory = C:\src\Antiphon` (backslashes,
    as the caller's `ANTIPHON`/cwd reports it). The real project is stored as `C:/src/Antiphon`
    (forward slashes), so the exact-string match **misses** and a second project `Antiphon (2)`
    (path `C:\src\Antiphon`) was created once and then reused. Live: **77 task boards under
    `Antiphon (2)`**, plus a third project `antiphon` at `C:\src\antiphon`. The real `Antiphon`
    project (the one with the 210-card board) holds none of them.

### Proof it is the backfill, not the dispatch

- `server/logs/antiphon-*.log` carries one `Backfilled default boards for N agent(s)` line per
  server restart (19 restarts in the last 5 days, N between 1 and 5), and the boards' `createdAt`
  minutes cluster exactly on those restarts (e.g. log `2026-08-25 04:16:32 +01:00` ↔ 5 boards
  created `2026-08-25 03:16Z`; `2026-08-23 11:35 +01:00` ↔ 5 boards at `10:35Z`; the biggest
  cluster is 10 boards at `2026-08-20 14:21Z`).
- Of the 122 `task-*` boards only **11** are linked to an agent that still exists; the other 111
  belong to pool delegates that the janitor has since retired
  (`AgentTaskDispatcher.RetireIdleWarmAgentsAsync`, `:2607-2657`, `_db.Agents.Remove(agent)` at
  `:2647`; also `AgentTaskService.RemoveEphemeralAgentAsync` `:687-695`). Removing an `Agent`
  never touches its `Board` — `Board` has no FK to `Agent`; the only cascade is the reverse
  (`ProjectCascade.cs:106-109` nulls `Agent.BoardId` when a board is deleted). So every board
  outlives the throwaway agent it was minted for.
- The agent running this very investigation, `task-17c504bb`, is visible in `GET /api/agents`
  with `boardName: ""` — boardless, exactly as the dispatcher created it. It will get a project
  `card-task-17c504bb` + board `task-17c504bb` on the next server restart unless this is fixed.

### Why the code does this

Three rules written at three dates, none aware of the next:

1. `86c8806` (2026-06-07) — "per-agent boards: auto-create a project+board for each agent on
   creation". `Agent.BoardId` was born as "the board automatically created for this agent when it
   was added" (`Domain/Entities/Agent.cs:91-92`).
2. `31ce1dd` (2026-07-31) — "every agent has a default board — backfilled, un-clearable". The
   startup backfill was written to repair standing agents (AZ Care, Family) whose link the old
   update path had cleared. At that date every `Agent` row was operator-created, so "every agent"
   was a safe universal.
3. `2daa5a0` (2026-08-08) — the warm-agent pool started creating `Agent` rows **directly** from the
   dispatcher, boardless, one per task. Nothing was added to (2) to exclude them, so the backfill
   reads each of them as "an agent that predates the rule" and repairs it into a project+board it
   will never use.

Nothing reads a pool delegate's board. Every consumer of `Agent.BoardId` already tolerates null:
`ApiKeyEnvResolver.ResolveProjectIdAsync` returns null → global-only keys (`ApiKeyEnvResolver.cs:52-63`),
and the dispatcher consults `task.ProjectId` **first** at every call site (`AgentTaskDispatcher.cs:1653-1654`,
`:1939-1941`, `:1957-1959`) with an explicit comment "A pool delegate has neither a board nor a
path-derived fallback"; `HerdrLaunchContextResolver.cs:44-62` falls through to `PoolProjectId`;
`AgentTaskService.DeriveCallerProjectAsync` (`:349-375`) returns null for any caller that is itself a
task before it ever joins on `agent.BoardId`; the client's `AgentAddWorkModal.tsx:25,133` and
`AgentDtos` (`agent.Board?.Name`, `AgentService.cs:908,970`) handle a null board. A delegated task
has its `AgentTask` row, its workspace (`WorktreePath`/`WorkingDirectory`), and its `PoolProjectId`
scope — a board adds nothing.

### Fix design (root, one place)

**F1 — the backfill must never adopt a pool delegate.**
`AgentService.EnsureAgentBoardsAsync` (`AgentService.cs:491-494`):
`.Where(a => a.BoardId == null)` → `.Where(a => a.BoardId == null && !a.IsPoolDelegate)`.
Update the method's `<summary>` (`:477-483`) and the `Program.cs:569-570` comment to say the rule
is "every **standing** agent has a default board; pool delegates are boardless by design
(CARD-0210)". Retroactive by construction: no migration, nothing to backfill, and the janitor keeps
retiring pool rows as it does today.

**F2 — pin the invariant at the source too**, so a future "repair" cannot re-open it:
`Domain/Entities/Agent.cs:91-92` doc for `BoardId` becomes "the standing agent's default board;
always null for a pool delegate (`IsPoolDelegate`)", and `AgentTaskDispatcher.ResolveAgentAsync`
(`:2119-2144`) gets a one-line comment above the initializer stating that `BoardId` is deliberately
absent and why. No behaviour change there.

**F3 (recommended, same slice, small) — path normalisation in project lookup.**
`ResolveProjectForWorkingDirectoryAsync` (`AgentService.cs:818-819`) should match
separator-insensitively and, on Windows, case-insensitively — normalise the input with
`DelegationWorkspaceResolver.NormalizeSeparators` (`DelegationWorkspaceResolver.cs:125`) and compare
both sides lower-cased (`EF.Functions.ILike` or `.ToLower()` on the Postgres side; the column is
`varchar(1000)`, no index, 72 rows). This is what let `C:\src\Antiphon` spawn `Antiphon (2)` beside
`C:/src/Antiphon` and `antiphon`. After F1 it only affects standing-agent creation (Source 2), but
it is the same bug and the same method. Do **not** merge the three existing Antiphon projects here —
that is the cleanup step.

**Rejected alternatives**
- *Stamp the task's project board onto the pool agent* (`BoardId = <board of task.ProjectId>`): a
  pool row is reused across tasks within its `PoolProjectId` fence, and "board" carries Add-Work
  semantics an unattended delegate never has; it would also make `ProjectCascade` count delegates
  as "agents pinned to this board" in delete dialogs.
- *Delete the board when the agent is retired*: leaves the leak in place between dispatch and
  retirement and adds a second thing that has to be right.
- *Remove `EnsureAgentBoardsAsync` entirely*: it still does the job it was written for (re-linking a
  standing agent whose board link was cleared); keep it, narrow it.

### Tests

- `tests/Antiphon.Tests/Application/AgentServiceIntegrationTests.cs` (group `AgentQueue`): add
  `EnsureAgentBoardsAsync_leaves_pool_delegates_boardless` — seed an `Agent { IsPoolDelegate = true,
  BoardId = null, WorkingDirectory = "D:/src/<guid>/worktrees/card-task-deadbeef" }`, run the
  backfill, assert `BoardId` is still null **and** no `Project` with that path / no `Board` named
  after the agent exists (scope every assertion to the seeded row per the shared-Postgres rule).
  The two existing backfill tests (`:165`, `:195`) stay — they seed non-pool agents.
- `tests/Antiphon.Tests/Application/AgentTaskPoolTests.cs`: one end-to-end guard — dispatch a task
  through the dispatcher so `ResolveAgentAsync` creates the pool row, then run
  `EnsureAgentBoardsAsync`, then assert `Boards.Any(b => b.Name == $"task-{short}")` is false and
  `Projects.Any(p => p.Name.StartsWith("card-task-"))` is false for that task's paths.
- F3: `CreateAsync_matches_an_existing_project_path_regardless_of_separator_and_case`.

### Verification after landing

`pwsh -File scripts/restart-apphost.ps1`, then confirm the server log has **no**
`Backfilled default boards` line for the pool delegates alive at that moment (they will show in
`GET /api/agents` with an empty `boardName`), and `GET /api/boards | measure` has not grown.
Dispatch one throwaway `delegate.ps1 -Role Test -Worktree` task, restart once more, count again.

---

## Source 2 — a board per standing agent (`POST /api/agents` with no board)

### Where

- `server/Application/Dtos/AgentDtos.cs:180-207` `CreateAgentRequest` has **no `BoardId` member at
  all** — a caller cannot say which board the agent belongs to. `UpdateAgentRequest` (`:216-223`)
  has one, with the comment "Every agent keeps a default board — an update can move it to another
  board, never clear the link."
- `server/Api/Endpoints/AgentEndpoints.cs:78-85` → `AgentService.CreateAsync`
  (`AgentService.cs:289-360`): unconditionally, inside the retry loop,
  ```csharp
  // Every agent gets its own board to organise its work. Boards belong to a project, so
  // find-or-create a project keyed on the agent's working directory and hang the board off it.
  var project = await ResolveProjectForWorkingDirectoryAsync(workingDirectory, agentName, now, ct);   // :321
  var board = BuildAgentBoard(project, await UniqueBoardNameAsync(project.Id, agentName, ct), now);   // :322
  _db.Boards.Add(board);                                                                                // :323
  … BoardId = board.Id                                                                                  // :345
  ```
  The board is named after the **agent**, never the project, so N agents on one project = N boards
  (live: `Antiphon`, `Antiphon-Fable`, `Antiphon-Opus`, `Antiphon-Orchestrator`, `Codex`,
  `Grok 4.6`, `ClaudeBot-Antiphon` are seven empty boards for seven agents on one codebase;
  `Gym Stat Orchestrator` was today's repro).
- The client create form `client/src/features/agents/AgentCreateModal.tsx` has no board or project
  field (grep for `board`/`project` returns nothing); `client/src/api/agents.ts:276-296`
  `CreateAgentRequest` mirrors the server — no `boardId`.
- `CheckInterpreterProvisioner.cs:86-108` also adds an `Agent` directly with no board
  (`IsPoolDelegate = false`, `AlwaysOn = true`); the backfill gave it the `antiphon-check-interpreter`
  board once. One row, not a leak; F1 leaves it alone and nothing here needs to change for it.

### What relies on a standing agent having a board

Only the "Add Work" flow and the settings modal: `AgentAddWorkModal.tsx:25-38,131-140` preselects
`agent.boardId` (and already copes with null — the picker is shown either way);
`AgentSettingsModal.tsx:238-247` "Default board … can be moved, not cleared". The commit message of
`31ce1dd` is explicit that the *link* must not be lost; nothing there requires the board to be a
*fresh* one.

### Design

**Recommended: "explicit or inherited, never invented while a candidate exists".**

1. Add `Guid? BoardId = null` to `CreateAgentRequest` (server `AgentDtos.cs:180`, client
   `agents.ts:276`), validated through the existing `EnsureBoardExistsAsync` (`AgentService.cs:803`).
   When given: link to it, create **nothing**. The project/working-directory relationship is not
   checked (an agent may deliberately work in a worktree of its project); log at Information when
   the board's project path and the working directory disagree.
2. When omitted, resolve in this order (all inside the existing retry loop at `:315`):
   a. project by working directory, **F3-normalised** (this is what makes the gym-stat case work —
      the operator created the project and board first, then the agent);
   b. if that project has **exactly one** board → link to it, create nothing;
   c. if it has **several** → `400 ValidationException("boardId", …)` naming the candidates. This is
      the one place the operator's "refuse" instinct is right: guessing among boards is how a
      card lands on the wrong board;
   d. if **no project** matches the directory → today's behaviour (create project + board), but
      name the board after the **project** (the directory leaf, `DeriveProjectName`), not the agent.
      This is the genuine "first agent on a new codebase" case; refusing it would break the create
      modal (no picker) and every script that creates an agent by path. Second and later agents on
      that directory then take branch (b).
3. Client: `AgentCreateModal.tsx` gains an optional `Select label="Board"` defaulting to "Use the
   project's board" (the same `boardOptions` source `AgentSettingsModal` already loads). Show the
   (c) refusal message inline.
4. Doc/comment updates: `UpdateAgentRequest.BoardId` comment (`AgentDtos.cs:221-222`) and the
   `AgentSettingsModal` description keep "moved, not cleared"; the `CreateAsync` comment at
   `AgentService.cs:319-320` changes from "Every agent gets its own board" to the rule above.

**Strict variant (operator's call, one flag):** if a project matches the directory and
`boardId` is omitted, refuse even when it has one board. Cleaner contract, worse ergonomics for
`POST /api/agents` from scripts. Recommend against — (b) is unambiguous and matches "one board per
project" — but it is a single `if` in step 2b and can be offered as `Agents:RequireExplicitBoard`
if wanted.

**Tests** (`AgentServiceIntegrationTests`): `CreateAsync_with_boardId_links_and_creates_nothing`;
`CreateAsync_without_boardId_links_to_the_projects_only_board`;
`CreateAsync_without_boardId_refuses_when_the_project_has_several_boards`;
`CreateAsync_on_an_unknown_directory_creates_a_project_and_a_board_named_after_it`. The existing
`CreateAsync_reuses_project_for_shared_working_directory_with_distinct_boards` (`:80`) asserts the
old N-boards behaviour and must be **rewritten** to expect the second agent to link to the first's
board; `CreateAsync_creates_board_and_project_for_working_directory` (`:50`) keeps its shape but its
`BoardName.ShouldBe(agentName)` becomes the directory leaf.

### Relationship between the two fixes

Separate mechanisms, one shared helper (`ResolveProjectForWorkingDirectoryAsync`, F3). F1 is a
one-line filter and is what stops the bleeding (122 of 163); land it first, on its own. Source 2 is
a contract change to `POST /api/agents` with a client change and a rewritten test, and can follow
in its own slice.

---

## Out of scope, restated

- Deleting the 163 existing empty boards / ~55 throwaway projects, and merging `Antiphon`,
  `Antiphon (2)` and `antiphon`. Needs the CARD-0144/CARD-0118-style report-first script keyed on
  positive evidence (zero cards **and** no agent linked **and** no tracker config **and** name
  matches `^(card-)?task-[0-9a-f]{8}$` or project has no non-task board), never age alone. After
  F1 the set is closed, so that script can be written against a fixed list.
- Source 3 (hand-made probe boards) is a test-hygiene question for the suites that create them
  (`Card0007 *`, `CARD-0142 *`, `card0164-*`, `Catalog Test *`), not a server change.
