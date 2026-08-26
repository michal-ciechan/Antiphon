# CARD-0032 — from a directory path to a first dispatched task

- **Card:** CARD-0032
- **Status:** design — slices 1–3 built and merged (`a6ae440`, `733a443`, `7761392`); §12 (2026-08-27)
  verifies §3–§4 against the as-built code and corrects §4.2 / §10-5 for slices 4–5
- **Date:** 2026-08-26
- **Builds on:** CARD-0210 (`d217306`, plan `8552dcb` on `feat/card-task-17c504bb`) — `POST /api/agents`
  now takes an explicit `boardId`, inherits a project's only board, refuses to guess among several,
  and only mints a project+board for a directory nobody has claimed. This design reuses that
  resolution and never adds a second one.

The card asks for a design doc under `docs/features/`; the dispatch brief asked for
`docs/superpowers/plans/`. This file is the deliverable; if the feature-doc home is wanted, move it to
`docs/features/012-project-setup/proposal.md` unchanged — the section shape below already follows
feature 010 ("what exists, verified against the code" first).

---

## 1. What exists today (verified against the code, 2026-08-26)

The story says a usable project needs six things. Here is where each one lives, how it gets
created, what checks it, and where its absence is discovered today.

| Prerequisite | Stored on | Created by | Checked at | Where the absence surfaces today |
|---|---|---|---|---|
| Working directory | `Agent.WorkingDirectory` (required); `Project.LocalRepositoryPath` (optional, `Project.cs:12`) | `POST /api/agents` (`CreateWorkingDirectory` default **false**, `AgentDtos.cs:186`); `ProjectConfig.tsx:429-435` form field | `AgentControlService.cs:174-175` — `Directory.Exists` at **start**, 409; `DelegationWorkspaceResolver.cs:65-66` at **task create**, 422 | A 409 when Start is pressed; a red toast when a task is created (`DelegateModal.tsx:104-105`). Nothing on the agents list says the directory is missing. |
| A project | `Project` | `POST /api/projects` (`ProjectService.cs:50-81`) **or** as a side effect of agent creation (`AgentService.cs:886-902`, `GitRepositoryUrl = ""`) | `ProjectService.ValidateRequest` `:307-330` — **git URL is required** on the UI/API path, but not on the agent path | The Projects tab's "Add Project" modal marks the URL `required` (`ProjectConfig.tsx:398-402`); a local-only repo cannot be created from that tab at all, yet the agent path creates the very same row with an empty URL. |
| A board with columns | `Board` + `BoardColumn` | `POST /api/boards` (`BoardService.cs:108-143`, default columns `:309-318` Backlog / In Progress *(active)* / Review / Done); or with the first agent on an unknown directory (`AgentService.cs:343-349`, `:370-372`) | `CardService.SpawnAsync` `:570-575` — "no active column" 409 | `POST /api/projects` creates **no board**; the Projects tab renders `--` in its Boards column (`ProjectConfig.tsx:502-508`). The board create modal on `BoardPage.tsx:704-751` is a separate dialog with its own project picker. |
| At least one agent with a definition and model tier | `Agent` (`Kind`, `TuiProfileId`, `ModelLevel`, `ReplyStyle`, bundles) | `POST /api/agents`; UI `AgentCreateModal.tsx` | `ApplyTuiSelectionAsync` `AgentService.cs:1100-1145` — profile disabled / no active revision are 409s | The create modal never sends `modelLevel` (call at `AgentCreateModal.tsx:95-108`; server defaults High at `AgentService.cs:387`), so the tier is invisible at create. `ModelLevelSelect.tsx` exists but is imported **nowhere** (its own doc claims "create + settings"). Bundles and `systemPromptAppend` are update-only (`AgentDtos.cs:191-192`, `UpdateAgentRequest.BundleKeys`), reachable only in `AgentSettingsModal.tsx:339-374` after the agent exists. If no enabled TUI profile exists, the Create button is silently disabled (`AgentCreateModal.tsx:246-252`) with no message. |
| Delegation settings (allowed roots, concurrency, cost ceiling) | `DelegationSettings` (`DelegationSettings.cs:17,23,25,40,279`) — configuration only; `AllowedRoots` is **not set** in the tracked `server/appsettings.json` | appsettings / user-secrets / env — no endpoint, no UI (grep `AllowedRoots` in `client/src` → nothing) | `DelegationWorkspaceResolver.cs:69-86` at task create; token-less callers (the UI's `DelegateModal`, `delegate.ps1` from a plain shell — `client.ts` sends no `X-Antiphon-Task-Token`, `delegate.ps1:117-119` only when `ANTIPHON_TASK_TOKEN` is set) inherit **nothing** and must name a directory under an allowed root (`NothingToInherit`, `:34-38`) | The 422 body, as a toast. Nothing tells the operator which roots are allowed, or that a request *from inside a session* (session token) would not need one. This is the 2026-08-11 miss the card cites. |
| A workflow template (card→session runs) | `WorkflowTemplate`; `Agent.DefaultWorkflowTemplateId` | seeded by `DatabaseSeeder.SeedWorkflowTemplatesAsync` (`:84-`) | `CardWorkflowRunFactory.cs:26-35` — "At least one workflow template is required" | Fine on a fresh install (seeded). Listed here because a deleted-last-template install fails at the card spawn, not before. |
| Channel binding (optional) | `ChatChannel.AgentId` | `PATCH /api/channels/{id}` (`UpdateChatChannelRequest.AgentId`, `ChatChannelDtos.cs:30`) | — | Already has a UI (`ChannelsPage.tsx`). Optional; out of the critical path. |

Two more facts that shape the design:

- **The reference shape of a "standing orchestrator" is only visible in the database.** The live
  `Gym Stat Orchestrator` (read from `GET /api/agents/{id}` today) is: `alwaysOn: true`,
  `attachedBundleKeys: ["orchestrator", "board-api"]`, `modelLevel: Medium`, and a hand-written
  `systemPromptAppend` naming the board, the repo URL, the local checkout and the job ("watch the
  board, pick up cards, run them through investigate → build → verify → merge → close, delegating
  every code change"). None of those four choices is suggested anywhere in the UI; the bundle keys
  exist only as `GET /api/agents/bundles` (`AgentEndpoints.cs:62-68`) and the role→bundle rule only
  in `InstructionBundles.ForDelegate` (`:161-185`). This is the "entirely undiscoverable" part of the
  card.
- **A dispatchable project is not one thing but a chain, and every link fails at a different time.**
  Create → start → dispatch are three separate moments; the row that looks "created" is checked by
  nobody until the last one. There is no readiness concept anywhere in the server (grep
  `Readiness|Preflight|Prerequisite` → nothing).

### What the operator actually did for gym-stat (the friction, itemised)

1. `POST /api/projects` with a git URL (required), then a **separate** `POST /api/boards` with the
   project id — no link between the two calls in the UI (Projects tab creates no board; the board
   dialog lives on the board page).
2. `POST /api/agents` per agent, which — before CARD-0210 — minted a wrongly named board each time.
   Fixed; the design below relies on the fixed behaviour (explicit `boardId` → nothing created).
3. Guessed the tier strings (`Frontier|High|Medium|Low`, `AgentModelLevel.cs:11-17`), the bundle
   keys (`InstructionBundles.cs:79-88`) and the reply styles (`AGENT_REPLY_STYLE_OPTIONS`,
   `agents.ts:51`) from source. The API has no single "what can I choose" answer.
4. Hit `CreateWorkingDirectory=false` + a directory that did not exist yet → 409 at start.
5. Wrote the orchestrator's `systemPromptAppend` by hand and attached the two bundles through
   `PATCH` after the fact, because create cannot carry either.

---

## 2. Design in one paragraph

Two mechanisms, one of which is worth shipping alone. **(A) A project readiness projection** —
`GET /api/projects/{id}/readiness` — that walks the chain above against the real rows, the real
filesystem and the real `DelegationSettings`, and returns a list of named checks with a status, a
plain-language reason and a fix. Rendered on the Projects tab, on the agent, and at the end of the
setup flow, it is what turns "discovered when a task fails" into "stated on screen". **(B) A guided
setup flow** — one stepper modal opened from "Add Project", backed by one transactional
`POST /api/projects/setup` — that takes a directory path and produces project + board + first agent
in one write, with presets for the agent shapes that exist in practice (standing orchestrator /
worker), and ends on the readiness panel with a "Start agent" and a "Create the first card /
Delegate a first task" button. Neither mechanism ever widens `Delegation:AllowedRoots`; the flow
explains the boundary at the point where it bites and stops there.

Why not just a wizard that chains today's three POSTs from the client? Because the failure mode the
card names is *partial creation*: a project without a board, an agent without a tier. Three calls
from a browser leave exactly that behind when the second one fails. One server-side write, one
transaction, nothing half-made. The client-only variant is listed under rejected alternatives.

---

## 3. Server design

All new code goes in `server/Application/Services/ProjectSetupService.cs` (+ DTOs in
`server/Application/Dtos/ProjectSetupDtos.cs`) and three routes on the existing
`ProjectEndpoints.cs:10` group. Nothing new is stored; readiness is computed per request.

### 3.1 `GET /api/projects/{id}/readiness` → `ProjectReadinessDto`

```csharp
public sealed record ProjectReadinessDto(
    Guid ProjectId,
    bool CanDispatch,                       // every Required check is Ok
    IReadOnlyList<ReadinessCheckDto> Checks);

public sealed record ReadinessCheckDto(
    string Key,                             // stable, see table
    ReadinessLevel Level,                   // Required | Recommended | Optional
    ReadinessStatus Status,                 // Ok | Missing | Warning | NotApplicable
    string Summary,                         // one line, present tense, names the thing
    string? Detail,                         // why it matters / what the default means
    ReadinessFixDto? Fix);                  // what to do about it

public sealed record ReadinessFixDto(
    string Label,                           // "Create directory", "Open agent settings"
    string? Route,                          // client route to deep-link ("/agents", "/settings?tab=agent-tui")
    string? Action);                        // an action key the client knows how to POST ("create-directory", "start-agent")
```

Checks, in the order they are rendered (the order is the order things fail in):

| Key | Level | Ok when | Source of truth |
|---|---|---|---|
| `directory` | Required | `Project.LocalRepositoryPath` set and `Directory.Exists` | `Project.cs:12`; same test as `AgentControlService.cs:174` |
| `git-repository` | Recommended | `DelegationWorkspaceResolver.GetRepoToplevelAsync` returns a toplevel | needed for `-Worktree` tasks (`AgentTaskService.cs:230-236`) and card worktrees; a plain directory is **allowed** (Warning, not Missing) |
| `board` | Required | ≥1 `Board` in the project with a column `IsActive && !IsTerminal` | `CardService.cs:570-575` |
| `agent` | Required | ≥1 non-pool `Agent` whose `BoardId` is one of the project's boards **or** whose `WorkingDirectory` path-matches the project (`AgentService.PathsMatch` `:904-908`) | `AgentTaskDispatcher` needs a standing agent only for pinned work; a *card* needs one on the board |
| `agent-runner` | Required | that agent's `TuiProfileId` names an enabled profile with an active revision (or the installation default exists) | `AgentService.cs:1116-1140` |
| `agent-directory` | Required | the agent's `WorkingDirectory` exists | `AgentControlService.cs:174` |
| `delegation-root` | Required for token-less dispatch | the project directory is under some `Delegation:AllowedRoots` entry | `DelegationWorkspaceResolver.IsWithinRoot`; see §5 for the wording |
| `workflow-template` | Required for cards | `WorkflowTemplates.Any()` (agent's default if set) | `CardWorkflowRunFactory.cs:26-35` |
| `orchestrator` | Recommended | some agent on the board is `AlwaysOn` with `orchestrator` **and** `board-api` attached | the live gym-stat shape; the check's Detail explains what a standing orchestrator does and links to the settings modal |
| `channel` | Optional | a `ChatChannel` is bound to an agent of this project | `ChatChannel.AgentId` |
| `github` | Optional | `GitRepositoryUrl` set (and `GitHubIntegrationEnabled` if the URL is github) | `Project.cs:11,15` |

Rules for the projection: it never mutates; it reads `DelegationSettings` through `IOptions` like
`AgentTaskService` does; it must be cheap enough for the Projects tab to call it for every row
(one query per table, filtered by project — do not call it from `GetAllAsync` on a hot path, expose
it per project and let the client fan out with `useQueries`).

### 3.2 `GET /api/projects/setup-catalog` → `ProjectSetupCatalogDto`

One read that answers "what strings exist", for the UI and for scripts alike:

```csharp
public sealed record ProjectSetupCatalogDto(
    IReadOnlyList<ModelLevelDto> ModelLevels,          // key, label, blurb, per-kind alias via ModelLevelAliases.For(kind, level)
    IReadOnlyList<ReplyStyleDto> ReplyStyles,           // enum values + the client's existing descriptions moved server-side
    IReadOnlyList<InstructionBundleDto> Bundles,        // = InstructionBundles.Attachable (same DTO as /api/agents/bundles)
    IReadOnlyList<AgentTuiProfileSummaryDto> Profiles,  // enabled profiles, default flagged (the same list AgentTuiSelection loads)
    IReadOnlyList<AgentPresetDto> Presets,              // §3.4
    DelegationSummaryDto Delegation);                   // AllowedRoots (verbatim), MaxConcurrentTasks, MaxCostUsdPerRoot, MaxDepth, DefaultLevel

public sealed record DelegationSummaryDto(
    IReadOnlyList<string> AllowedRoots, bool AllowedRootsIsEmpty,
    int MaxConcurrentTasks, decimal MaxCostUsdPerRoot, int MaxDepth, AgentModelLevel DefaultLevel);
```

Listing `AllowedRoots` read-only is not a widening — it is the operator's own server telling the
operator its own configuration; the alternative (guessing, or reading a 422) is what the card is
about. The endpoint is a **read**; there is deliberately no `PUT` (§5).

`ModelLevelDto` carries the per-kind mapping (`Frontier → fable / gpt-5.6-sol / grok-4.6`) computed
from `ModelLevelAliases.cs:21-56`, so the picker can say what a tier actually launches for the
selected profile's kind — today that knowledge is a code comment (`AgentModelLevel.cs:4-9`).

### 3.3 `POST /api/projects/setup` → `ProjectSetupResultDto`

```csharp
public sealed record ProjectSetupRequest(
    string Directory,                       // the one thing the user must type
    bool CreateDirectory = false,           // mirrors CreateAgentRequest.CreateWorkingDirectory
    string? Name = null,                    // null = directory leaf (AgentService.DeriveProjectName :929)
    string? GitRepositoryUrl = null,        // null = server reads `git remote get-url origin` if it is a repo; may stay empty
    string? BaseBranch = null,              // null = "master", as ProjectService does
    string? BoardName = null,               // null = project name
    int BoardMaxConcurrentSessions = 1,
    ProjectSetupAgentRequest? Agent = null, // null = project + board only (a valid, non-dispatchable stop; readiness says so)
    bool StartAgent = false);               // after commit, POST /start semantics (queue the launch)

public sealed record ProjectSetupAgentRequest(
    string? Preset,                         // "orchestrator" | "worker" | null (custom) — §3.4
    string? Name,                           // null = preset name pattern ("<Project> Orchestrator")
    Guid? TuiProfileId, string? ModelId,
    AgentModelLevel? ModelLevel,
    AgentReplyStyle? ReplyStyle,
    bool? AlwaysOn, bool? RemoteControlEnabled,
    IReadOnlyList<string>? BundleKeys,      // explicit list wins over the preset
    string? SystemPromptAppend);            // explicit text wins over the preset template

public sealed record ProjectSetupResultDto(
    ProjectDto Project, BoardSummaryDto Board, AgentDetailDto? Agent,
    ProjectReadinessDto Readiness,          // computed after commit — the screen the flow ends on
    IReadOnlyList<string> Notes);           // "git remote read from the checkout", "directory created", …
```

Behaviour, in order, inside **one** `_db.Database.BeginTransactionAsync` (all four services share
the request-scoped `AppDbContext`, so their `SaveChangesAsync` calls join it — the same pattern
`ProjectService.DeleteAsync` `:163-166` and `BoardService.DeleteAsync` `:157-165` use):

1. **Resolve the directory** exactly the way the resolver does for a task: `Path.GetFullPath`,
   `Directory.Exists` (create it first when `CreateDirectory`, via the same `_directoryWriter`
   `AgentService.cs:307-308` uses), `GetRepoToplevelAsync`. A directory that is a *subdirectory* of
   a git repo is refused with the toplevel named — an agent working in a subfolder of a repo is
   almost always a mistake and CARD-0210's path matching is exact.
2. **Refuse a duplicate project** using `AgentService.FindProjectForWorkingDirectoryAsync`
   (`:878-884`, the F3-normalised lookup) — 409 with the existing project's id, so the client can
   offer "open its readiness" instead of creating `Antiphon (2)` again. This helper becomes
   `internal` and is shared, not copied.
3. **Create the project** through `ProjectService.CreateAsync` with `GitRepositoryUrl` = the given
   value, else the remote read from the checkout, else empty. This requires relaxing
   `ProjectService.ValidateRequest` (`:316-324`): a git URL is required **only when
   `LocalRepositoryPath` is empty** (the auto-clone case `WorkflowEndpoints.cs:259-280` genuinely
   needs it); a project with a local path may have none. Same relaxation applies to the existing
   Projects modal — today it demands a URL the agent path never needed.
4. **Create the board** through `BoardService.CreateAsync` (`:108-143`) — default columns, so the
   `In Progress` active column exists (`:314`).
5. **Create the agent** through `AgentService.CreateAsync` with `BoardId = board.Id` — the CARD-0210
   explicit-board branch (`:326-338`): nothing else is created, `PathsMatch` is true so no
   Information log either. `CreateAgentRequest` gains two members the flow needs at birth:
   `IReadOnlyList<string>? BundleKeys` and `string? SystemPromptAppend` (`AgentDtos.cs:180-207`;
   applied the way `UpdateAsync` applies them, validated by `InstructionBundles.Exists` `:127`).
   CARD-0060's comment at `:191-192` ("Create deliberately still cannot set systemPromptAppend …
   out of scope here") was a scoping note, not a rule; update the comment. Doing it on create rather
   than create-then-PATCH means there is no moment where a "standing orchestrator" exists without
   its contract, and scripts get the same power.
6. **Commit**, then (outside the transaction) publish `BoardChanged`/`AgentChanged` — the individual
   services already publish; that is acceptable inside the transaction because the events carry ids
   the reader will re-fetch after commit, same as today's `CreateAsync`.
7. **Start** when asked, by calling `AgentControlService.StartAsync` **after** commit — a launch is
   not transactional and must not be rolled back by a later DB failure; the result carries the
   launch outcome or its 409 message as a `Note` rather than failing the whole setup (the project is
   real by then).
8. **Return readiness** computed on the committed rows.

Any exception before commit rolls back everything; the response is the ordinary problem-details
the client already renders (`ProjectConfig.tsx:197-212` reads `errors` and `detail`).

### 3.4 Agent presets (server-side, in `AgentPresets.cs`)

A preset is a **starting point the UI shows and the user can edit**, not a hidden default. Two, and
only the two shapes that exist in the live data:

| Preset | Sets | Why these values |
|---|---|---|
| `orchestrator` — "Standing orchestrator: watches the board, delegates every change" | `AlwaysOn=true`, `BundleKeys=[orchestrator, board-api]`, `ModelLevel=High`, `ReplyStyle=Caveman`, `SystemPromptAppend` = a template with `{project}`, `{board}`, `{repoUrl}`, `{directory}` placeholders rendered **at setup time** (they are project facts, not launch facts — unlike `{agentName}`/`{channels}` in `ChannelPreamble`, which render at launch) | the gym-stat shape; the template text is the gym-stat prompt generalised, kept in `server/Bundles/preset-orchestrator-prompt.md` next to the bundles so it is code and versioned |
| `worker` — "A worker you hand cards or tasks to" | `AlwaysOn=false`, no bundles (delegate-basics is composed by role at dispatch, `InstructionBundles.cs:180-182`), `ModelLevel=High`, `ReplyStyle=Normal` | today's create-modal defaults, named |

`Custom` is "no preset" — every field as it is. Presets live server-side so `scripts/` and the UI
agree on what "orchestrator" means; the catalog (§3.2) returns them with their resolved values so
the UI can render them as the step's starting state and show diffs.

### 3.5 Existing code that changes

| File | Change |
|---|---|
| `server/Application/Services/ProjectService.cs:307-330` | `ValidateRequest`: git URL required only when `LocalRepositoryPath` is blank. |
| `server/Application/Services/AgentService.cs:878-884` | `FindProjectForWorkingDirectoryAsync` → `internal`, reused by setup + readiness. |
| `server/Application/Dtos/AgentDtos.cs:180-207` | `CreateAgentRequest` + `BundleKeys`, `SystemPromptAppend`; comment at `:191-192` updated. `AgentService.CreateAsync:379-397` applies both (same validation as `UpdateAsync`). `client/src/api/agents.ts:276-296` mirrors. |
| `server/Api/Endpoints/ProjectEndpoints.cs` | three routes: `GET /{id}/readiness`, `GET /setup-catalog`, `POST /setup`. |
| `docs/antiphon-api.md:110-113` | route map entries. |
| `tests/Antiphon.E2E/ContractSnapshotTests.cs` | new snapshots only if the flow's DTOs are added to a scenario; existing snapshots are unaffected (no existing DTO changes shape except `CreateAgentRequest`, which is a request). |

---

## 4. Client design

### 4.1 Where it lives, and what it costs the surfaces it shares

- **Entry point: the Projects tab's "Add Project" button** (`ProjectConfig.tsx:224-226`) opens the
  new `ProjectSetupModal` instead of the flat create modal. The flat modal **stays as the Edit
  modal** (`openEditModal` `:124-138`), unchanged — editing a project is not setup. Cost: none;
  create and edit were already two code paths sharing one form.
- **Readiness column on the Projects table** (`ProjectConfig.tsx:231-241`). The table has six
  columns; it gains one and loses one: `Features` (two badges, `:259-277`) folds into readiness as
  the `github`/`channel` optional rows, so the column count stays at six. Cost: the GitHub /
  Notifications badges are no longer visible at a glance — they move behind the readiness popover.
  Accepted: they were decorative, and readiness is the column that answers the card's question.
- **A "Set up a project" empty-state** on the Boards list (`App.tsx:90` route) and in the Agents
  page (`AgentsPage.tsx:92-93` next to "New Agent"): a secondary button, not a banner. Cost: one
  button. The Home page is deliberately **not** touched — feature 010 already fights for that rail.
- **Per-agent readiness chip** on the Agents page cards: `directory missing` / `runner profile
  disabled` — the two states that make Start a 409 today (`AgentControlService.cs:167,175`,
  `AgentService.cs:1136-1143`). Rendered from the same `agent-*` checks, fetched lazily per visible
  agent. Cost: one chip on agents that are broken, nothing on agents that are fine.

### 4.2 The stepper — steps in reading order, and why that order

> **Read §12.2 before building this.** The step order and intent below stand; §12.2 corrects the
> component names, data sources and request shape where the as-built slices 1–3 differ from what is
> assumed here.

Mantine `Stepper` (in `@mantine/core`; not used anywhere in `client/src` yet, so this is the first
one — keep it to the plain component). Five steps; each step's fields are the fields that can fail
on the server at that point, so a refusal lands on the step that caused it.

1. **Directory** — `DirectoryAutocomplete` (`DirectoryAutocomplete.tsx`, already reports
   `pathMissing` from `GET /api/fs/browse` → `DirectoryBrowseResponse.Exists`), the existing
   "create if missing" switch. Below it, live facts: *exists / will be created*, *git repository
   (toplevel …)* or *not a git repository — worktree tasks will not be available*, *already a
   project: <name>* (the 409 from §3.3 step 2, surfaced before submit by calling
   `GET /api/projects` and matching client-side with `normalizeDir` from `projectGrouping.ts:43`).
   Why first: everything else derives from it (name, git URL, board name).
2. **Project & board** — name (prefilled leaf), git URL (prefilled from the checkout; optional,
   labelled so), base branch, board name (prefilled = project name), a read-only preview of the four
   default columns with `In Progress` marked *active — cards moved here start a session*. Why one
   step: nobody ever wants a project without a board; showing them together kills the two-call gap.
3. **First agent** — preset chips (Orchestrator / Worker / Custom), then the fields, prefilled from
   the preset: name, `AgentTuiSelection` (reused), `ModelLevelSelect` (reused — finally imported),
   reply style segmented control (reused from the create modal), `Always on` / `Remote control`
   switches, **Attached bundles** multi-select (the same `bundleOptions` the settings modal builds at
   `AgentSettingsModal.tsx:90-96`), and the prompt textarea with the rendered preset template. A
   "skip — no agent yet" link is allowed and readiness will say `agent: Missing`. Why third: an
   agent needs the board id, which now exists in the request; and the preset is where the
   undiscoverable knowledge (bundles + prompt) becomes a click.
4. **Delegation** — read-only. Shows the `delegation-root` check for this directory with the
   wording in §5, `MaxConcurrentTasks`, `MaxCostUsdPerRoot`, `MaxDepth`, `DefaultLevel` from the
   catalog, each with its one-line meaning. No inputs. Why a step at all: it is the setting that
   failed silently on 2026-08-11 and it must be *seen* before the first task, not after.
5. **Review & create** — the request as a list, the **Start agent now** switch, Create. On success
   the modal body becomes the readiness panel (§4.3) with two calls to action: **Create the first
   card** (opens `CardEditModal` on the new board) and **Delegate a task** (opens `DelegateModal`
   with `workingDirectory` prefilled — it already accepts a prefill, `DelegateModal.tsx:32`).

What the flow deliberately does **not** show: the constitution path, `DefaultLaunchEnv`, GitHub
integration and notification switches, project API keys, board `MaxConcurrentSessions`, auto-compact
overrides, session backend, assignment policy. All of these have a home in the Edit modal or the
agent settings modal; none decides whether the first task dispatches. A setup flow that shows every
field is the flat form with more clicks.

### 4.3 The readiness panel (`ProjectReadinessPanel.tsx`)

One component, three hosts (setup step 5, the Projects-tab popover, the project header on the
board page if wanted later). Rows in server order; each row is icon + summary, with detail and a fix
button when the status is not Ok. Required-and-missing rows are red and listed first; the panel's
header is the one sentence that answers the card: *"Ready to dispatch"* or *"Cannot dispatch yet —
2 things missing"*. Fix buttons either deep-link (`Route`) or perform the action inline (`Action`:
`create-directory` → `POST /api/fs/…` does not exist; use the setup endpoint's `CreateDirectory` or
an agent `PATCH` with `createWorkingDirectory` — pick the latter, it exists) and re-fetch.

### 4.4 Files

| File | Role |
|---|---|
| `client/src/api/projectSetup.ts` | `useSetupCatalog`, `useProjectReadiness(id)`, `useSetupProject` mutation; DTOs mirroring §3 |
| `client/src/features/settings/ProjectSetupModal.tsx` (+ `.test.tsx`) | the stepper; owns step state; submits once |
| `client/src/features/settings/ProjectReadinessPanel.tsx` (+ `.test.tsx`) | §4.3 |
| `client/src/features/settings/ProjectConfig.tsx` | "Add Project" → setup modal; readiness column; Features column removed |
| `client/src/features/agents/AgentsPage.tsx` | readiness chip; "Set up a project" button |
| `client/src/features/agents/AgentCreateModal.tsx` | gains `ModelLevelSelect` and the bundles multi-select (the create request now carries them); no other change |

Reused, not rebuilt: `DirectoryAutocomplete`, `AgentTuiSelection`, `ModelLevelSelect`,
`AGENT_REPLY_STYLE_OPTIONS` segmented control, `getApiErrorMessage`, `ProjectDeleteDialog`
(unchanged), `DelegateModal` prefill, `CardEditModal`.

---

## 5. `Delegation:AllowedRoots` — the boundary, handled explicitly

The rule this design commits to: **no button, endpoint, or preset changes `AllowedRoots`.** The flow
reads it (§3.2), evaluates it (`delegation-root`), and explains it. The wording on screen, in the
two states:

- **Inside a root** — *"Tasks may be created here from the UI and from scripts: `<dir>` is under
  the allowed root `<root>`."*
- **Outside every root** (including the empty-list default) — *"A task created from this screen, or
  from `delegate.ps1` in a plain shell, must run under an allowed root, and `<dir>` is not under
  one. This is a security boundary (`Delegation:AllowedRoots` in `server/appsettings.json`), so it
  is not changed from here. You do not need it for: a card moved into In Progress with Spawn (the
  session starts in the agent's own directory); a task delegated **from inside** that agent's
  session (a session inherits its own directory as its root); or an always-on orchestrator working
  the board. Add the root only if you want to dispatch into this directory from the UI or a plain
  shell."* — followed by the exact JSON line to add, verbatim, and a note that `AllowedRoots` empty
  means "each caller's own tree only", which is the safe default.

That text is the "explaining at the point of choice" the story asks for; the choice being explained
is the operator's, made in a file, on purpose. The check is `Required` only in the sense that the
**UI's** "Delegate a task" button on the readiness panel is disabled with that reason when it
fails; the project is still `CanDispatch` for cards and in-session delegation, and the panel says
which paths are open.

---

## 6. Shell path

`scripts/project.ps1 new -Dir <path> [-Orchestrator|-Worker] [-Name] [-GitUrl] [-Start]` — a thin
wrapper over `POST /api/projects/setup` in the `card.ps1` mould (header comment is the reference;
long text from files), plus `project.ps1 readiness <name|id>` printing the checks as a table, and
`project.ps1 catalog` printing tiers / styles / bundles / presets / allowed roots. This is the path
the operator actually used today (raw API) and the one an agent-facing bundle can cite. Optional
slice; the UI does not depend on it.

---

## 7. Failure and empty states

| State | Where it shows | What it says / does |
|---|---|---|
| Directory does not exist | Step 1, inline | "Does not exist — create it?" switch (existing pattern). Note that a created directory is not a git repo: worktree tasks unavailable until `git init`. |
| Directory is inside a git repo but not its root | Step 1, inline, blocks Next | names the toplevel, offers "use the repository root instead" |
| Directory already belongs to a project | Step 1, inline, blocks Next | "Already the project *X* — open its readiness" (no duplicate `X (2)` ever) |
| No enabled TUI profile with an active revision | Step 3, replaces the profile select | "No runner profile is enabled — set one up under Settings › AI Agent TUI" with a link. Today this state is a silently disabled Create button (`AgentCreateModal.tsx:246-252`). |
| Bundle key unknown (script path) | 422 `bundleKeys` | as `UpdateAsync` does today |
| Board name collides in the project | Step 2, inline from the 409 | "Board *X* exists in this project — pick another name" |
| Server fails mid-setup | Step 5 error alert; nothing created | transaction rolled back; the alert names the failing field (problem-details `errors`) and the stepper jumps to that step |
| Start requested but launch refused (409: quota, directory, profile) | Readiness panel `Notes` + the relevant check red | project and agent exist; the launch reason is shown verbatim; "Start agent" button on the panel retries |
| Outside `AllowedRoots` | Step 4 and readiness `delegation-root` | §5 wording; never blocks Create |
| Project created with no agent (skipped) | Readiness `agent: Missing` | "Add an agent" fix → `AgentCreateModal` with `boardId` preselected (`AgentCreateModal.tsx:51,186-196`) |
| Zero projects | Projects tab empty state (`ProjectConfig.tsx:306-312`) | copy becomes "No projects yet. Set up a project from a directory path." with the same button |

---

## 8. Rejected alternatives

- **Client-side wizard over the three existing POSTs.** Leaves half-made projects on the second
  failure, needs client-side rollback (delete what it created — through cascade dialogs), and gives
  scripts nothing. The transactional endpoint is ~200 lines and reuses every service as is.
- **Auto-creating a board in `POST /api/projects`.** Changes the contract of an endpoint that
  scripts and the Edit path use, and CARD-0210 just established that boards are created explicitly
  or inherited, never invented while a candidate exists. Setup creates the board because the user
  asked for one, on the same request.
- **A settings UI for `AllowedRoots`.** The card says not to make widening casual; a text box on the
  setup step is exactly casual. Read-only display with the exact file line is the compromise.
- **Presets as client constants.** Then `delegate.ps1`/`project.ps1` and the UI drift on what an
  "orchestrator" is. Server-side, returned by the catalog, one definition.
- **Storing readiness.** It is derivable from rows + disk + config, and a stored flag goes stale the
  moment someone deletes a directory. Compute per request; the Projects tab fans out per row.

---

## 9. Tests

Server (`tests/Antiphon.Tests`, TUnit + Shouldly, `dotnet run --project`, scope every assertion to
the rows the test made — shared Postgres):

- `ProjectSetupServiceTests`: happy path creates project+board+agent with `BoardId` = the new board
  and **no** other board/project (`Boards.Count(b => b.ProjectId == id) == 1`); duplicate directory
  → 409 naming the existing project; subdirectory-of-repo → 422 naming the toplevel; a failing
  agent create (unknown profile id) leaves **no** project and **no** board (transaction); orchestrator
  preset yields `AlwaysOn`, `[orchestrator, board-api]`, a rendered prompt containing the board name
  and directory; explicit `BundleKeys`/`SystemPromptAppend` override the preset; `StartAgent` with a
  refusing runner (`RefusingSessionRunnerClient` is already wired in `AntiphonWebAppFactory`)
  returns the project with a `Note`, not a failure.
- `ProjectReadinessTests`: one test per check in both states; `delegation-root` with an empty list,
  with a matching root, with a non-matching root; `agent` counts a path-matched agent on another
  board and ignores `IsPoolDelegate` rows; `CanDispatch` is false while any Required check is
  Missing.
- `ProjectServiceTests`: git URL optional when `LocalRepositoryPath` is set; still required without.
- `AgentServiceIntegrationTests` (group `AgentQueue`): `CreateAsync_applies_bundle_keys_and_system_prompt_append`;
  unknown key → 422.
- `InstructionBundleTests`: the preset prompt file is catalogued/embedded (it must not be mistaken
  for an attachable bundle — put it under a `Presets/` resource prefix, and pin that `Attachable`
  does not list it).

Client (`pwsh -File scripts/test-client.ps1`, one global timeout, no per-file overrides):
`ProjectSetupModal.test.tsx` (step gating: missing dir blocks Next unless create; duplicate project
blocks; preset fills fields; submit sends one request; server `errors` jump to the right step),
`ProjectReadinessPanel.test.tsx` (ordering, header sentence, fix buttons), `ProjectConfig.test.tsx`
(readiness column, Features column gone, empty-state copy).

E2E (`tests/Antiphon.E2E`, rebuild `client/dist` first): `ProjectSetupE2ETests` —
from a fresh temp directory (git-initialised) through the stepper to a project whose readiness reads
*Ready to dispatch*, then **Delegate a task** from the panel against the fixture's isolated runner
(`test-raw` definition) and assert the task reaches `Dispatched`. That single test is the card's
done-when, end to end. Wire `TestDiagnostics` as the AGENTS.md bullet prescribes.

---

## 10. Slices, in order

1. **Readiness projection + Projects-tab column + agent chip** (server §3.1, client §4.3 + column).
   Ships value alone: every existing project starts *saying* what it is missing. No behaviour change.
2. **Catalog endpoint + `CreateAgentRequest.BundleKeys/SystemPromptAppend` + `ModelLevelSelect` and
   bundles in the create modal + git-URL relaxation.** Small, independent, unblocks scripts today.
3. **`POST /api/projects/setup` + presets** (server §3.3–3.4) with the service tests.
4. **The stepper modal** (§4.2) replacing "Add Project"'s create path; E2E test.
5. *(refined in §12.3)* **`scripts/project.ps1` + `docs/antiphon-api.md` + a paragraph in `docs/orchestration-loop.md`
   §"Create and start an agent" (`:101`) pointing at the setup path and the orchestrator preset.**

Each slice is its own dispatch (Code role; slice 4 is the only one that needs a UI-capable tier);
1–2 can run in parallel, 3 depends on 2, 4 on 3, 5 on 3.

---

## 11. Open questions for the operator (decide before slice 3)

- **Orchestrator preset tier.** The live gym-stat orchestrator runs `Medium`; the role policy puts
  Plan/Code/Review at `Frontier` and `DefaultLevel` is `High`. The preset proposes `High` (it
  decomposes and delegates; it does not write code). Say if `Medium` was deliberate.
- **Should `delegation-root` be `Required` or `Recommended`?** This doc makes it Required for the
  UI's "Delegate a task" button only, and the project `CanDispatch` for cards regardless. The
  stricter reading (a project outside every root is "not ready") would make every new project red
  by default under the safe empty-list setting — which reads as pressure to widen the list, the
  thing the card warns against. Recommend keeping it as written.
- **Feature-doc home.** Move this file to `docs/features/012-project-setup/proposal.md` if the
  `docs/features/` convention is the one the card meant; nothing in it depends on the path.

---

## 12. As-built verification before slices 4–5 (2026-08-27)

Slices 1–3 are merged (`a6ae440` readiness, `dd4d894`/`733a443` catalog + setup + presets,
`7761392` preset reply style) and live on `localhost:17202`. This section was written by reading the
real `ProjectSetupDtos.cs`, `ProjectSetupService.cs`, `AgentPresets.cs`, `projectSetup.ts`,
`ProjectReadinessPanel.tsx`, `AgentCreateModal.tsx`, `ProjectConfig.tsx`, `AgentsPage.tsx`, and by
calling `GET /api/projects/setup-catalog` and `GET /api/projects/{id}/readiness` on the running
server. It corrects §4.2 and §10-5 where the original assumptions drifted. Nothing that already
works is redesigned; §3 stays as the record of intent and this section is the record of fact.

This file itself was never merged: it lived only on `feat/card-task-fb15a6fe` (`dce786f`). It is
restored here, on the slice-4 planning branch, so the build dispatch has one document to follow.

### 12.1 Server: what §3.1–3.4 became

**Matches the design (no action):** every DTO in §3.1–3.3 exists with the field names written there
(`ProjectReadinessDto`, `ReadinessCheckDto`, `ReadinessFixDto`, `ProjectSetupCatalogDto`,
`DelegationSummaryDto`, `ProjectSetupRequest`, `ProjectSetupAgentRequest`, `ProjectSetupResultDto`);
the eleven check keys are constants on `ReadinessKeys`; the three routes hang off
`ProjectEndpoints.cs:30-55`; setup runs in one transaction and starts the agent after commit; the
subdirectory-of-a-repo refusal, the duplicate-project refusal (via
`AgentService.FindProjectForWorkingDirectoryAsync`, now `internal`), the `git remote get-url origin`
read, `ProjectService.ValidateRequest` relaxed (URL required only when no local path,
`ProjectService.cs:321`), and `CreateAgentRequest.BundleKeys`/`SystemPromptAppend` (mirrored in
`client/src/api/agents.ts`) all landed. Enum values travel as strings on the wire (`"High"`,
`"Normal"`, `"Required"`, `"Missing"`), the same as everywhere else.

**Deviations that slice 4/5 must build against:**

| Where | Plan said | As built | Consequence |
|---|---|---|---|
| §3.4 orchestrator preset | `ReplyStyle=Caveman` | **`Normal`** (`7761392`, deliberate) | The stepper prefills `Normal`. |
| §3.4 template file | `server/Bundles/preset-orchestrator-prompt.md` | `server/Bundles/Presets/orchestrator-prompt.md`, embedded resource `Antiphon.Server.Bundles.Presets.orchestrator-prompt.md` | Path only. |
| §3.2 preset values | catalog returns "resolved values" | `AgentPresetDto` carries the **unrendered** `systemPromptTemplate` (`{project}`, `{board}`, `{repoUrl}`, `{directory}`) and a `namePattern` (`"{project} Orchestrator"` / `"{project} Worker"`); rendering is `AgentPresets.RenderTemplate`/`RenderName` at setup time, `{repoUrl}` → `(none)` when empty; no preset ⇒ name `"{project} Agent"` | The stepper renders its own preview (four `replace` calls) but sends `systemPromptAppend: null` and `name: null` unless the user edited them — the server then renders, so one renderer stays authoritative. An edited field is sent verbatim ("explicit wins", pinned by `explicit_prompt_and_bundles_override_the_preset`). |
| §11 open question | `delegation-root` Required-for-Delegate | **`Recommended`**; `Warning` outside every root and when the list is empty, `NotApplicable` without a directory; never in `CanDispatch` (`can_dispatch_ignores_delegation_root_and_optional_rows`) | The panel already says so. The live row's `summary` is the whole §5 paragraph (~600 chars) and `detail` carries the `appsettings.json` snippet. |
| §3.3 step 2 | 409 "with the existing project's id" | `ConflictException` text: `Directory 'X' already belongs to project 'Name' (guid).` — problem-details `detail`, no structured id | Do not parse the guid out of prose. The pre-submit client-side match (§4.2 step 1) is the path that yields an id. |
| §3.3 step 1 | subdirectory refused | 422 on field `directory`: `Directory 'X' is inside the repository at 'T'. Use the repository root instead.` | Pre-submit hint comes from `GET /api/filesystem/workspaces?path=` (`useWorkspaceGitInfos`, `WorkspaceGitInfo.repoRoot`/`isWorktree`). `repoRoot` is the **main** checkout for a linked worktree, so offer "use the repository root" only when `!isWorktree`; the server verdict wins. |
| §3.3 step 7 | launch refusal as a Note | `notes` gets `Agent start was refused: <message>`; HTTP is still **200** | The final screen and `project.ps1` must print `notes` — a refused start is not an error status. |
| §3.2 profiles | "the same list `AgentTuiSelection` loads" | `catalog.profiles` = enabled profiles only, `{id, displayName, kind, isDefault, hasActiveRevision}`; `AgentTuiSelection` still fetches `useAgentTuiProfiles` itself | Reuse `AgentTuiSelection` untouched; use `catalog.profiles` only for the "no runner profile" empty state and for the selected profile's `kind` → `modelLevels[].aliasesByKind` (`ClaudeCode`/`Grok`/`Codex`). `tuiProfileId: null` is accepted — `AgentService` falls back to the installation default (`AgentService.cs:1086`), so the stepper need not require a profile the way `AgentCreateModal` does. |
| §3.2 bundles | — | `catalog.bundles` = `InstructionBundles.Attachable`: live keys `board-api`, `check-interpreter`, `delegate-basics`, `orchestrator` | Build the multi-select from `catalog.bundles` (one fetch) rather than a second `useInstructionBundles`. |
| §3.3 result | — | `result.board` is a `BoardSummaryDto` (no columns) | The "Create the first card" CTA needs only `boardId`. |

Live catalog on this machine, for reference: `modelLevels` `Frontier/High/Medium/Low`; `replyStyles`
`Normal/Terse/Caveman/Explanatory` (the descriptions are the client's `AGENT_REPLY_STYLE_OPTIONS`
text moved server-side, byte for byte); `delegation` `{allowedRoots: [], allowedRootsIsEmpty: true,
maxConcurrentTasks: 6, maxCostUsdPerRoot: 200, maxDepth: 5, defaultLevel: "High"}`.

**Tests already pinning the server:** `ProjectSetupServiceTests` (6: create chain, duplicate 409,
subdirectory 422, rollback on agent failure, preset rendering, explicit-overrides-preset) and
`ProjectReadinessTests` (37, one per check state plus the two `CanDispatch` rules). Slice 4 adds no
server tests unless it changes a request shape.

### 12.2 §4.2 corrected — what the stepper actually builds on

The five steps and their order stand. These are the facts the build follows instead of the
names in §4.2/§4.4/§7:

**Reuse targets — all verified to exist under the plan's names, in `client/src/features/agents/`:**
`DirectoryAutocomplete.tsx` (props `value`, `onChange`, `createIfMissing`, `onCreateIfMissingChange`,
`onPathMissingChange?`, `label?`; it calls `GET /api/filesystem/browse` — the plan's `/api/fs/browse`
is the wrong path), `AgentTuiSelection.tsx` (`tuiProfileId`, `modelId`, `onProfileChange`,
`onModelChange`), `ModelLevelSelect.tsx` (`value`, `onChange`). The reply-style control is **not a
component**: it is a Mantine `SegmentedControl` inside `Input.Wrapper` fed by
`AGENT_REPLY_STYLE_OPTIONS` from `client/src/api/agents.ts:51`, written out inline in both
`AgentCreateModal.tsx:228-243` and `AgentSettingsModal.tsx:353-368`. Slice 4 either copies those
fifteen lines a third time or lifts them into `ReplyStyleControl.tsx` and points all three at it —
lift it; the third copy is the one that drifts. `bundleOptions` is likewise an inline `useMemo`
(`AgentCreateModal.tsx:65`), not a shared thing. `Stepper` ships in the installed `@mantine/core`
8.3.17 and has zero usages in `client/src`, so this is still the first one.

**Wrong names in the plan, corrected:**

- *Create the first card* opens **`CardModal`** (`client/src/features/board/CardModal.tsx`, props
  `boardId`, `card: CardDto | null`, `columns?`, `opened`, `onClose`; `card: null` is create).
  `CardEditModal` is edit-only (`card: CardDto` required) and is not the target.
- *Delegate a task* opens `DelegateModal` from **`client/src/features/delegations/`** (not
  `features/agents/`), prop `prefill?: DelegatePrefill` with `workingDirectory`, `goal`, `scopeGlob`.
- `AgentCreateModal` has **no `boardId` prop** (§7 "with `boardId` preselected" assumed one). Its
  board is internal state. The readiness `agent` fix is a plain route to `/agents`; leave it so. If
  the skipped-agent path wants a preselected board, that is a five-line `initialBoardId?` prop, not a
  requirement of this slice.
- The `create-directory` fix **action has no handler and no endpoint**: neither `ProjectConfig` nor
  `AgentsPage` passes `onAction`, `CreateWorkingDirectory` exists only on `CreateAgentRequest`
  (`AgentDtos.cs:186`, applied in `CreateAsync` only), and `/api/filesystem` is GET-only. §4.3's
  "agent `PATCH` with `createWorkingDirectory` — it exists" is false. The panel already degrades: with
  no `onAction` only the `route` button renders ("Create the directory" → the Projects tab / the
  agent). Slice 4 does **not** wire the action — the stepper's own step 1 creates the directory before
  the project exists, so its final readiness never shows `directory: Missing`. Wiring it for
  pre-existing projects is a follow-up card (add `CreateWorkingDirectory` to `UpdateAgentRequest`,
  mirror of create).

**Deep-links the fixes emit are all wired** (slice 1 added `?tab=` to `SettingsPage.tsx:10-15`):
`/settings?tab=projects|agent-tui|templates`, `/agents?agent=<id>` (`AgentsPage.tsx:79`), `/agents`,
`/boards`, `/channels`.

**Data sources per step, as built:**

1. *Directory* — `DirectoryAutocomplete` + its own create-if-missing switch. Live facts under it:
   `useWorkspaceGitInfos([dir])` for *git repository at …* / *not a git repository — worktree tasks
   will not be available* / *inside the repository at … (use the root?)*, and the duplicate-project
   line from `useProjects()` matched with `normalizeDir` (`features/home/projectGrouping.ts:43`)
   against `localRepositoryPath` — that match is where the existing project's **id** comes from for
   the "open its readiness" link. Both are advisory; the server's 422/409 text is the verdict and is
   rendered verbatim via `getApiErrorMessage` (`api/client.ts`).
2. *Project & board* — name prefilled with the path leaf exactly as `AgentService.DeriveProjectName`
   does (`AgentService.cs:940`: trim trailing separators, last segment); git URL **empty** in the
   form (the server reads `origin` itself when the field is blank — do not pre-read it client-side,
   there is no endpoint for it; say "read from the checkout if blank"); base branch default
   `master`; board name prefilled = project name; the four default columns are a static preview —
   the response has no columns.
3. *First agent* — preset chips from `catalog.presets`; selecting one sets `alwaysOn`, `modelLevel`,
   `replyStyle`, `bundleKeys` and renders the preview prompt/name locally (the four placeholders;
   `{repoUrl}` → `(none)` when the URL field is blank, matching the server). Send
   `agent: { preset, tuiProfileId, modelId, modelLevel, replyStyle, alwaysOn, remoteControlEnabled,
   bundleKeys, name: <edited or null>, systemPromptAppend: <edited or null> }`; "skip — no agent
   yet" sends `agent: null` and disables *Start agent now*. Empty profile list ⇒ the §7 empty state
   with a link to `/settings?tab=agent-tui`; otherwise `tuiProfileId` may stay null.
4. *Delegation* — read-only from `catalog.delegation`. The stepper computes *under an allowed root*
   client-side (`normalizeDir` prefix match over `allowedRoots`) and shows a **two-line** version of
   §5 — the full paragraph is the readiness row the user sees on the next screen, do not duplicate
   it. `allowedRootsIsEmpty` ⇒ "each caller's own tree only — the safe default".
5. *Review & create* — `useSetupProject().mutate(request)`; on success render
   `ProjectReadinessPanel` with `result.readiness` (already seeded into the query cache by the
   mutation's `onSuccess`), list `result.notes` above it (a refused start lives here), header from
   `readinessHeader()`, then the two CTAs: `CardModal` with `boardId = result.board.id`, and
   `DelegateModal` with `prefill.workingDirectory = result.project.localRepositoryPath`. Field errors:
   problem-details `errors` keyed `directory`, `name`, `gitRepositoryUrl`, `boardName`,
   `agent.preset`, `bundleKeys` → jump to the owning step (1, 2, 2, 2, 3, 3).

**Entry points (§4.1) — what slice 4 owns:** "Add Project" (`ProjectConfig.tsx:232`) → the stepper;
the flat modal becomes Edit-only (delete the `editingProject === null` branch; drop `required` from
the Git URL input at `:393`, the server no longer requires it for a local-path project); the
existing empty-state copy (`:301`, already "Set up a project from a directory path.") gains the
button; `AgentsPage` gains the secondary "Set up a project" button next to "New Agent" (`:109`).
The **Boards-list button is dropped**: `/boards` renders `BoardPage`, which has no boards-list
empty state to host it. The readiness column, the Features-column removal and `AgentReadinessChip`
are already built (`ProjectConfig.test.tsx`, `AgentsPage.test.tsx:848`).

**Tests for slice 4:** `ProjectSetupModal.test.tsx` (msw: catalog, `GET /projects`,
`GET /filesystem/workspaces`, `POST /projects/setup` success + 409 + 422-on-`directory`) and one E2E
in the `ProjectDeleteE2ETests.cs:171-174` mould (`/settings` → Tab "Projects" → Add Project → five
steps → readiness header "Ready to dispatch" or "Cannot dispatch yet — N things missing" via
`data-testid="project-readiness-header"`). `client/node_modules` is absent in a fresh worktree —
`npm install`, and `npm run build` before E2E (the fixture serves `client/dist`). Run the client
suite through `pwsh -File scripts/test-client.ps1`.

### 12.3 §10-5 corrected — `scripts/project.ps1` and the docs

Nothing in slices 1–3 touched `docs/` or `scripts/`: `docs/antiphon-api.md:110-113` still lists
only the four old project routes, `docs/orchestration-loop.md:99-107` still says "Create and start an
agent through `POST /api/agents` + `POST /api/agents/{id}/start`". Slice 5 owes all of it.

`scripts/project.ps1`, in the `card.ps1` mould (ASCII-only; `$env:ANTIPHON_API` defaulting to
`http://localhost:17202`; `X-Antiphon-Task-Token` from `$env:ANTIPHON_TASK_TOKEN`; header comment is
the reference; long text from files; non-zero exit on any HTTP failure, unlike the 200-with-notes the
server returns for a refused start — print `notes` loudly):

```
project.ps1 new       -Dir <path> [-CreateDirectory] [-Name n] [-GitUrl u] [-BaseBranch b] [-BoardName n]
                      [-Orchestrator | -Worker | -NoAgent] [-AgentName n] [-Profile <displayName|guid>]
                      [-Level Frontier|High|Medium|Low] [-ReplyStyle Normal|Terse|Caveman|Explanatory]
                      [-Bundles a,b] [-PromptFile p] [-RemoteControl] [-Start] [-Json]
project.ps1 readiness <project name|guid> [-Json]
project.ps1 catalog   [-Json]
```

Wire shape is `projectSetup.ts` verbatim: camelCase keys, enum members as strings, `agent.preset`
`"orchestrator"`/`"worker"`, `-NoAgent` ⇒ `agent: null`; an agent flag without a preset switch sends
`agent` with `preset: null`. `-Profile` by display name resolves through `catalog.profiles`
(case-insensitive, must be unique). `readiness <name>` resolves through `GET /api/projects` — exact
match first, then case-insensitive **unique** match, else list the candidates and exit 1: the live
board has `antiphon`, `Antiphon` and `Antiphon (2)` side by side. The human `readiness` view prints
the check table in server order (`key`, `level`, `status`, `summary`) with required-missing rows
first, the same order `ProjectReadinessPanel.orderedChecks` uses, plus the `fix.label`/`fix.route`
column. `catalog` prints tiers with `aliasesByKind`, styles, bundles, presets (rendered with the
placeholders left visible) and the delegation block.

Docs, exact edits: `docs/antiphon-api.md:110-113` — three lines for `GET /api/projects/{id}/readiness`,
`GET /api/projects/setup-catalog`, `POST /api/projects/setup` (one-line each, name the DTO).
`docs/orchestration-loop.md:99-107` "Launching an agent" — one paragraph before the existing one:
a new project starts with `POST /api/projects/setup` (or `scripts/project.ps1 new -Dir … -Orchestrator
-Start`), which creates project + board + a preset agent in one transaction and returns readiness;
`POST /api/agents` is for adding an agent to a project that already exists. `AGENTS.md` "Working
cards from a shell" gets one sentence pointing at `project.ps1` as the sibling of `card.ps1` —
optional, but it is where the next agent looks first.

Slice 5 depends only on slice 3 and can run before, after, or in parallel with slice 4.
