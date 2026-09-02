# 008 — Home workspace: a files-first cockpit for the current project

Status: **as built** (this doc describes the shipped design; deltas are noted inline).

## 1. Why

The home page (`/`) is the Workflows dashboard — a card grid of workflow runs. That surface answers
"what are my pipelines doing", but it is not where day-to-day work happens anymore. Since feature
007, the actual working loop is:

1. Look at files an agent produced (usually a rendered markdown doc or a diff).
2. React to what you read — comment, or hand a piece of work to the agent pool.
3. Watch a small set of agents per directory: who is working, what is queued, what finished.
4. Talk to an agent mid-flight in messages.

Today that loop is spread across four pages (`/agents`, `/agents/:id/files`, `/orchestrator?tab=
delegations`, `/channels`), and the entry point (`/`) serves none of it. Concretely:

- **Files are two clicks and a new tab away.** The files view — the surface we spend most time in —
  hangs off an agent card's kebab menu and opens full-screen with no way back into the rest of the
  app (deliberately chrome-less).
- **"Kick off work" lives on the wrong page.** Delegation is reachable from the files viewer
  (Delegate… button) and the orchestrator board, but the home page can't start anything.
- **No project axis.** Agents, tasks, and files are all directory-scoped on the server (the pool is
  per-directory, delegation is per-directory), but no client surface groups by directory. Switching
  projects means re-finding the right agent on `/agents` every time.
- **Reading a doc is a dead end.** In the rendered view you can comment on a *line* (raw view only)
  — but the natural gesture when reviewing prose is to select a passage and say "change this".

## 2. What already exists (reused, not rebuilt)

| Capability | Where it lives | State |
|---|---|---|
| File tree + diff/raw/rendered viewer, review marks, baselines | `FilesReviewPanel` (embedded + sidebar layouts) | ✅ reused as the center pane |
| Talk to an agent's session (transcript + composer + queue) | `SessionTranscriptPanel` / `SessionMessageQueue` | ✅ reused as the chat dock |
| Delegate a task (worker/sub-orchestrator, role→tier, workspace) | `DelegateModal` + `POST /agent-tasks` | ✅ reused for kick-off + selection sends |
| Task board with lanes, drawer, retry/escalate | `/orchestrator?tab=delegations` | ✅ linked; a compact per-project list is embedded |
| Agent liveness/working/queue badges | `AgentsPage` card internals | ✅ distilled into the agent rail |
| Warm agent pool per directory | server (feature 007) | ✅ surfaced: pool delegates appear in the rail like any agent |

The revamp is therefore mostly **composition**: one page that puts the existing pieces in the shape
of the actual working loop, plus one genuinely new interaction (selection → prompt).

## 3. Design

### 3.1 The project axis

A **project is a distinct agent working directory** — nothing new is modelled. The server already
treats the directory as the unit of pooling, delegation, and scope; the client now derives the same
grouping from `useAgentList()`:

- Group agents by `workingDirectory` (case-insensitive, trailing-slash-normalised).
- Label a project by its trailing path segment(s); the full path is the tooltip/subtitle.
- Directories from in-flight delegations (`useAgentTasks()`) that have no standing agent still
  appear — a project you delegated into is a project, even before an agent row exists.

The switcher is a compact dropdown in the page header — deliberately small, because the user
switches "now and again, not all the time". The selection persists in `localStorage`
(`antiphon-home-project`), as does the selected agent per project. The `Projects` entity used by
Workflows is intentionally NOT the axis here: it models a git remote for pipeline runs, not a local
working directory, and half the directories agents work in (worktrees, other repos) never appear
in it.

### 3.2 Layout — one screen, three regions

```
┌────────────────────────────────────────────────────────────────────────┐
│  [Project ▾ antiphon]  ...path...      [Delegate work]  [Board ↗]      │
├──────────┬──────────────────────────────────────────┬──────────────────┤
│ Agents   │  Files (tree + viewer)                   │ Chat             │
│  ● axc   │   – tree sidebar                         │                  │
│  ○ pool-1│   – viewer: RENDERED default for md      │  transcript      │
│──────────│   – select text → "Send to agents"       │  + composer      │
│ Tasks    │                                          │                  │
│  groups  │                                          │                  │
└──────────┴──────────────────────────────────────────┴──────────────────┘
```

- **Agent + Tasks rail (left, ~300px).** Agents occupy the top third (max-height 33%): every agent
  in the project's directory, one compact row: name, working spinner / attention badge (same
  semantics as AgentsPage — quiet states show nothing), liveness dot, queue length. Clicking
  selects the agent — which drives BOTH the files pane and the chat dock. Pool delegates are
  ordinary rows here; watching the pool is watching this rail. Footer links: manage agents
  (`/agents`), new agent. Below a divider, the **Tasks** section (`GET /api/home/tasks`, filtered
  to the selected project's directories) lists Cards and unbound delegations in five groups
  (Needs you · Running · To review · Up next · Done). Bound delegations nest as the card's
  worker line, and CARD-0031 extends that rail in place: Running items show a progress verdict
  plus elapsed/last-activity, Up next shows why a queued task has not started (and when a Plan
  is ready for Code), and Done cards show `TerminalReason`. See CARD-0002 / CARD-0031 / feature 010.
- **Files center.** `FilesReviewPanel` in `sidebar` layout for the selected agent. This is the
  page's default and dominant surface. Markdown now defaults to **Rendered** everywhere (it used to
  default to Diff when the file had changes); Diff/Raw remain one click away.
- **Right dock (~400px), chat only.** `SessionTranscriptPanel` with composer against the selected
  agent's live/persistent session; empty-state points at Start when there is no session. The old
  Tasks tab is gone (`ProjectTasksPanel` deleted); a stale `?tab=tasks` bookmark lands on chat.
  The header **To read** badge scrolls the rail's Done group (`#home-tasks-done`) into view.
- **Header.** Project switcher, full path, **Delegate work** (opens `DelegateModal` prefilled with
  the project directory — the same queue the orchestrators use), and a link to the Delegations
  board.

### 3.3 Highlight → prompt (the new interaction)

In the **Rendered** view, selecting text pops a small floating **"Send to agents"** affordance at
the end of the selection. Clicking it opens an inline composer showing the selection as a quote,
with a free-text instruction box and a role picker (Code/Docs/Plan chips, Docs preselected for
markdown). Submit queues a **delegation**:

- `goal` = `In <path>:\n> <selected text, quoted>\n\n<instruction>`
- `workingDirectory` = the agent's workspace root, `scopeGlob` = the file path
- `workspace` = `null` → the server decides (workers run Shared, so the warm pool picks it up —
  this is exactly the "queue it up for the pool of agents" path from feature 007)

"More options…" opens the full `DelegateModal` with the same goal prefilled for anything beyond the
defaults. The selection flow deliberately creates a *task*, not a chat message: tasks survive the
reader closing the page, get picked up by the pool, escalate on stall, and report back — a chat
message is only right when you're already mid-conversation, and the Chat tab covers that.

Raw-view line comments ("Comment & send to agent") are unchanged and complementary: line-anchored
threads for code, passage-anchored delegations for prose.

### 3.4 Routes and navigation

| Route | Before | After |
|---|---|---|
| `/` | Workflows dashboard | **Home workspace** |
| `/workflows` | — | Workflows dashboard (unchanged content) |
| `/workflow/:id`, `/boards`, `/agents`, `/agents/:id/files`, `/channels`, `/orchestrator`, `/settings` | unchanged | unchanged |

Nav order: **Home · Workflows · Boards · Agents · Channels · Orchestrator · Settings**. The
full-screen files page stays: it is the "one file, maximum space" view; Home is the "whole project"
view.

## 4. Decisions & rejected alternatives

- **Project = directory, derived client-side.** Rejected: new server Project↔directory modelling —
  the server already keys everything on the directory string; a second source of truth would drift.
- **Selection sends create delegations, not session messages.** Rejected: typing into the selected
  agent's session — bypasses the pool, breaks when the agent is mid-turn, and loses the
  report-back/board lifecycle.
- **Chat docked, not a separate page.** The 007 loop showed replies land while you read the files
  the agent produced; context-switching to `/channels` for that is the exact friction this page
  removes.
- **Rendered as the markdown default even when the file changed.** Reading flow beats review flow
  on this page; the Diff tab remains one click away and the review marks are untouched.
- **Monaco (raw/diff) selection-to-delegate deferred.** Different selection API, and the line-
  comment flow already covers code. Revisit if prose reviews start happening in raw view.

## 5. Verification

- Unit: project grouping (normalisation, delegation-only directories, labels), selection composer
  (quote building, role default, more-options handoff), home page composition (rail selection
  drives files+chat, tasks filter by directory, empty states).
- Existing `FilesReviewPanel` tests updated for the rendered-markdown default.
- Live: browser-harness (CDP Edge) walkthrough against the dev client — switch project, select
  agent, read a rendered doc, select a passage, queue a task, see it in the Tasks tab.
