# Feature 007 — Multi-agent orchestration: delegated tasks, model tiers, task board

**Status:** shipped through P3, including the `PreToolUse` deny hook (see [§3 Phasing](#3-phasing))
**Date:** 2026-08-06
**Supersedes the unbuilt parts of:** [002-agent-orchestrator.md](../002-agent-orchestrator.md) (2026-05-15 draft; its
long-poll/`wt.exe`/SQLite branch is dead — Antiphon went the PTY + xterm.js + Postgres route)

---

## 0. The ask

An **orchestrator agent** that keeps a small context and does its work by handing tasks to other
agents, each running at the model tier its job deserves:

| Work | Tier | Alias |
|---|---|---|
| Deploy, run tests | Low | `haiku` |
| Docs / markdown chunks | Medium | `sonnet` |
| First-pass debugging, coverage double-check | High | `opus` |
| Coding, logic review | Frontier | `fable` |
| Debugging that stalls | escalate High → Frontier | `opus` → `fable` |

When a delegate finishes, **its final message comes back to the orchestrator** with enough
identifying detail to act on. Work happens either in an isolated worktree (merged at the end) or in
the shared directory (several agents editing different parts of one markdown file). A board shows
the whole fan-out, and the files view can launch a delegate on the file in front of you.

Nothing decomposes work automatically. You launch one of exactly two things: a **worker** for a piece
of work you can name in a sentence, or a **sub-orchestrator** that owns a chunk and runs its own
agents.

---

## 1. What already exists

This is the important part: **roughly 80% of the machinery is built**. The proposal is mostly
wiring, not construction.

| Capability the ask needs | Already in the codebase |
|---|---|
| Persistent agent identity — name, cwd, board, always-on | `Agent` (`server/Domain/Entities/Agent.cs`), `AgentService`, `AgentControlService` |
| **The model-tier ladder** — Frontier/High/Medium/Low → `fable`/`opus`/`sonnet`/`haiku`, applied as `--model <alias>` at launch | `AgentModelLevel`, `ModelLevelAliases.cs`, applied at `AgentControlService.cs:148`. Aliases (never pinned ids) so each launch gets the family's current model. |
| Deliver a message into a live session, now **or when it goes idle**, surviving restarts | `SessionMessageQueueService.EnqueueAsync`, `SessionQueuedMessage` |
| Correct multi-line delivery into a TUI (LF-normalize → bracketed paste → separate `\r`) | `SessionMessageQueueService.DeliverAsync` — see the CLAUDE.md requirement; this is why delegation must go through the queue |
| Know whether an agent is mid-turn or idle, with all the hard-won exclusions (interrupt markers, local slash-commands, compaction, forks) | `SessionMessageQueueService.IsWorkingAsync` |
| **Detect turn-end, extract that turn's final assistant text, correlate it to the request that caused it, classify final-answer vs question, handle trailing text** | `ChannelReplyDispatcher` — this is the reply-back mechanism, already built and battle-scarred |
| Agent → agent messaging | `AgentMentionRouter` + `AgentChannelService.RouteMentionAsync` (`@name message` scraped from PTY output) |
| Dispatch loop with per-board/per-column concurrency caps, atomic claim, retry/backoff, reconciliation of dead sessions | `OrchestratorService.PollTickAsync`, `RetryScheduler` |
| Worktree create / list / remove / TTL-prune | `IWorktreeManager`, `WorktreeManager`, `WorktreeJanitorHostedService`, `Worktree` entity |
| Per-agent files view with git status, diffs, checkpoints at historic commits | `AgentFilesService`, `FilesReviewPanel.tsx`, `AgentFilesPage.tsx` |
| Per-agent system-prompt injection at every launch (fresh **and** resume) | `Agent.SystemPromptAppend` → `--append-system-prompt` |
| Token/cost accounting | `TokenUsage`, `CostLedgerEntry`, `CostTrackingService` |
| Skills the agents can be taught | `.claude/skills/*/SKILL.md` — picked up from the launch cwd |

### The gaps

1. **Delegation is fire-and-forget.** `RouteMentionAsync` sends into the target and returns. Nothing
   records that a request is outstanding, and nothing routes the answer home.
2. **It also bypasses the queue.** `RouteMentionAsync` calls `_runtime.SendInputAsync` directly
   (`AgentChannelService.cs:94`), so a multi-line mention is **not** LF-normalized or paste-wrapped —
   a latent instance of the exact fragmentation bug CLAUDE.md documents. It's also card-scoped
   (`AgentChannelService.cs:68` drops cardless sessions), so agent-to-agent chat doesn't work off a board.
3. **Model tier is a property of the agent, not the task.** You can't say "run *this* step on haiku".
   And because `--model` is a launch argument, a long-lived agent can't change tier without a restart.
4. **No record of a delegated unit of work** — so no parent/child tree, no board, no cost rollup per
   fan-out, no retry, no escalation.
5. **Worktrees are created and destroyed, never merged back.** `IWorktreeManager` has no merge.
6. **Nothing coordinates concurrent edits to one file** in a shared directory.
7. **No delegate action in the files view.**

---

## 2. Design

### 2.1 `AgentTask` — one delegated unit of work

A new entity. Deliberately *not* a `Card`: cards carry board columns, tracker sync, workflow
definitions and a 1:1 worktree, which is far too much for "rewrite this heading". Tasks are cheap,
nest, and can be created hundreds at a time. A task **may** reference a card, but doesn't need one.

```csharp
public class AgentTask
{
    public Guid Id { get; set; }
    public Guid RootTaskId { get; set; }            // == Id for roots; denormalized so the board is one query
    public Guid? ParentTaskId { get; set; }
    public Guid? ParentSessionId { get; set; }       // where the completion note is delivered
    public int Depth { get; set; }                   // fan-out guard

    public string Title { get; set; }                // one line — the board chip
    public string Prompt { get; set; }               // the full brief handed to the delegate

    public AgentTaskKind Kind { get; set; }          // Worker | Orchestrator — see below
    public AgentTaskRole Role { get; set; }          // Code Debug Review Test Deploy Coverage Docs Merge Custom
    public AgentModelLevel ModelLevel { get; set; }  // from the role policy; overridable per task
    public AgentModelLevel? EscalatedFrom { get; set; }
    public int Attempt { get; set; }
    public int MaxAttempts { get; set; }

    public WorkspaceMode Workspace { get; set; }     // Shared (default) | Worktree | ReadOnly
    public string WorkingDirectory { get; set; }     // absolute; may be a DIFFERENT repo (§2.5b)
    public string? RepoPath { get; set; }            // derived toplevel; null for a non-git dir
    public Guid? WorktreeId { get; set; }
    public string? MergeTargetRef { get; set; }      // defaults to the PARENT task's branch (§2.5)
    public string? ScopeGlob { get; set; }           // advisory file lease, e.g. "docs/setup.md"

    public Guid? AgentId { get; set; }               // pinned agent; null = ephemeral or pool-resolved
    public Guid? AgentSessionId { get; set; }        // filled at dispatch
    public bool Ephemeral { get; set; }              // throwaway agent, deleted when the task settles

    public AgentTaskStatus Status { get; set; }      // Queued Dispatched Working Blocked Succeeded Failed Canceled
    public string? Result { get; set; }              // the delegate's final assistant message, in full
    public string? FailureReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long TokensIn { get; set; }
    public long TokensOut { get; set; }
    public decimal CostUsd { get; set; }
}
```

Plus an append-only `AgentTaskEvent { TaskId, Type, ModelLevel?, Detail, At }` — dispatched,
escalated, merged, conflicted, retried. It gives the drawer its timeline and mirrors the existing
`AuditRecord` habit. Escalation bumps `ModelLevel` **in place** (one chip per task, `EscalatedFrom`
set, ladder visible in the events) rather than forking a second row.

#### The only structural choice: worker or sub-orchestrator

There is no automatic decomposition anywhere in this design — nothing splits a file by heading,
infers subtasks, or chops work up on your behalf. A human or an agent decides what the pieces are.
The one thing you choose when you delegate is **which of two shapes** the delegate takes:

| | `Worker` | `Orchestrator` |
|---|---|---|
| For | one piece of work you can state in a sentence — a doc change, a test run, a commit | a chunk that needs its own decomposition: several steps, several tiers, or a shape you don't know yet |
| Delegates further | **no** — the server rejects task creation from a worker | yes — it is an orchestrator in its own right |
| Preamble | reporting contract (§2.8) | reporting contract **+** the orchestrator contract (§2.9) |
| Skill on its path | no | yes |
| Tier | from its role | High or Frontier — decomposition is the expensive kind of thinking |
| Its report is | the work product | a rollup of what its own children did |

The "delegates further" line is **enforced, not advised**: a worker's `ANTIPHON_TASK_TOKEN` is issued
without the create scope, so `POST /api/agent-tasks` returns 403. That is also the runaway boundary —
a worker cannot start a fan-out even if it decides it wants to.

Recursion is therefore intentional and normal (`orchestrator → sub-orchestrator → worker` is depth 3
and the ordinary case), not an accident the depth cap exists to prevent. The controls that actually
bound it are the per-root task count and the per-root **cost ceiling** (§2.2) — depth alone is a poor
proxy for "this is running away".

**The context win is the whole point of the nesting.** A sub-orchestrator's children report to *it*,
not to its parent. The top-level orchestrator sees one rolled-up report per subtree instead of every
leaf, which is what keeps its context small as the tree grows.

### 2.2 Role → tier policy

Config, not code, so tiers can be tuned without a deploy (`appsettings.json`):

```json
"Delegation": {
  "RolePolicy": {
    "Deploy":   { "level": "Low",      "timeoutMinutes": 20 },
    "Test":     { "level": "Low",      "escalateTo": "Medium", "timeoutMinutes": 30 },
    "Docs":     { "level": "Medium",   "timeoutMinutes": 20 },
    "Debug":    { "level": "High",     "escalateTo": "Frontier", "escalateAfterMinutes": 25 },
    "Coverage": { "level": "High" },
    "Code":     { "level": "Frontier" },
    "Review":   { "level": "Frontier" },
    "Merge":    { "level": "High" }
  },
  "MaxDepth": 5,
  "MaxConcurrentTasks": 6,
  "MaxTasksPerRoot": 40,
  "MaxCostUsdPerRoot": 5.00,
  "AllowedRoots": [ "C:\\src", "C:\\Antiphon\\worktrees" ],
  "ReplyInlineMaxChars": 20000,
  "ReplyExcerptHeadChars": 6000,
  "ReplyExcerptTailChars": 6000
}
```

`MaxDepth` is deliberately generous — nesting is intended (§2.1), so depth is a backstop, not the
control. **`MaxCostUsdPerRoot` is the real one**: a recursive tree can only run away by spending, so
spend is what to bound. Crossing it stops dispatch for that root and marks it `Blocked` with the
tree so far intact, rather than killing work in flight.

**Escalation** fires on either trigger: the delegate reports failure, or it is still `Working` past
`escalateAfterMinutes` with no transcript progress (the stall condition `RunAttemptStallDetector`
already models). On escalation the current session is stopped and the task requeued one tier up with
a **handoff block** prepended to the prompt — the last few assistant messages from the failed
attempt, so the frontier model doesn't restart cold. Without that handoff, escalation just pays more
for the same dead end.

### 2.3 Dispatch

`AgentTaskDispatcher`, a hosted service ticking like `OrchestratorService.PollTickAsync` and reusing
its two proven patterns: the concurrency-cap count-then-skip loop, and the transactional claim
(`OrchestratorService.TryClaimCardAsync`, `:307`) so two ticks can't dispatch the same task.

Per task: resolve the target agent (pinned → that agent; otherwise spawn an **ephemeral** one whose
`ModelLevel` is the task's) in the task's `WorkingDirectory` — which may be a different repo
entirely (§2.5b) — create a worktree only when `Workspace == Worktree`, then deliver the brief
through `SessionMessageQueueService.EnqueueAsync(..., MessageSendMode.WhenIdle, origin: Delegation)`
and register a pending reply correlation.

**Ephemeral by default** is the right call for delegated work, for two reasons: clean context is the
entire economy of the design, and `--model` is a launch arg, so a fresh process is the only way to
pick a tier per task. Pooled long-lived agents stay the right answer for standing roles — a deploy
runner, an always-on assistant — which `Agent.AlwaysOn` already models.

> **As built (2026-08-08): the warm-agent pool.** A fresh Claude per task proved wasteful for
> queued work, so a settled `Shared` delegate now goes WARM instead of dying
> (`Agent.IsPoolDelegate` / `PoolIdleSince`), and dispatch reuses before it spawns:
>
> - **Selection**: same directory, same tier, live session. For
>   `Delegation:PoolReservedForCallerMinutes` (default 2) after settling, the agent answers only
>   to the run that just used it — so `-OnAgent <shortTaskId>` follow-ups keep their context
>   without racing the queue. After the window it serves anyone; release is a pure time
>   comparison, no state change.
> - **Unrelated reuse compacts first**: the server sends `/compact` focused on the incoming
>   goal, then the brief — old context shrinks to whatever could still help. Same-run follow-ups
>   skip the compact; their old context is the value.
> - **Token rebind**: a live process's env can't change, so the session keeps presenting the
>   previous task's bearer — dispatch moves that token's hash onto the new task (and nulls the
>   old row's) so the bearer always resolves to the CURRENT work. Without this, a reused
>   orchestrator's children would parent to a settled task.
> - **A busy pinned agent is waited for**, not interrupted: delivering a follow-up mid-task
>   would land between the running task's turns and corrupt both correlations.
> - **The janitor bounds the trade**: idle past `PoolIdleRetireMinutes` (default 5) → retired;
>   more than `PoolMaxIdlePerDirectory` (default 3) warm in one directory → oldest retired. That
>   cap is the worker-scaling knob per directory; `MaxConcurrentTasks` stays the global one.
> - Worktree delegates are never pooled (their directory dies with the merge); a user's standing
>   agents are never adopted by the pool lifecycle. `Delegation:PoolEnabled=false` restores
>   spawn-and-kill.

### 2.4 The reply path — the heart of it

`ChannelReplyDispatcher` already does the hard half: it registers a correlation when a message is
enqueued, watches for `TurnEnd`, matches the turn back to the prompt that caused it (so a human
typing in that terminal never triggers a stray reply), extracts that turn's assistant text,
classifies final-answer vs question, and handles text that arrives *after* the TurnEnd.

Generalize its sink: extract `IReplySink`, keep today's Kafka/Telegram path as `ChannelReplySink`,
add `AgentTaskReplySink` which on a matched turn-end:

1. writes `Result`, sets `Succeeded` (or `Blocked` when classified as a question — the delegate is
   asking, not finishing);
2. rolls up `TokensIn/Out/CostUsd` from the session's `TokenUsage`;
3. if `Workspace == Worktree && MergeTargetRef != null` and parent and child share a repo, runs the
   merge-back (§2.5);
4. composes a **bounded completion note** and enqueues it into `ParentSessionId` — `WhenIdle`, so it
   lands between the orchestrator's turns instead of interrupting one.

**One correction to make first:** match on an explicit task marker, not on prompt text. The
dispatcher prefixes every brief with `[antiphon-task:{shortId}]`; the sink matches that. Prompt-text
matching is fine for chat, but a delegate that reformulates or a human who pastes the brief
elsewhere would misroute a task result.

The note is what the user sees as "the final message, with some information about the agent":

```
[task 7f3a done] doc-hand · sonnet · 4m12s · $0.031 · merged → master

Rewrote "## Windows install" in docs/setup.md — 34 lines changed, every command now
pwsh 7. Left the port table alone. One decision for you: the old cmd examples are
deleted rather than kept alongside; say if you want both.
```

#### How much comes back

**The delegate's final message is forwarded essentially whole.** Its report *is* the deliverable —
clipping it to a headline would just force the orchestrator to make a second call to read what it
already paid for. Size is handled in two places, in this order:

1. **The delegate self-limits.** Its brief tells it that above `ReplyInlineMaxChars` (20 000) it must
   write the full detail to `.antiphon/task-<id>.md` in its workspace and make the final message a
   summary that references that path (§2.7). This is the mechanism that should almost always fire,
   because the delegate is the only party that knows which 20 000 characters matter.
2. **The server backstops it.** If a report still lands over the ceiling, the server writes it to
   that same path itself and forwards a **head + tail excerpt** — never a plain truncation. A hard cut
   at 20 000 characters severs the conclusion, and the conclusion is the part the caller needed.

`AgentTask.Result` always holds the untouched original regardless.

**Coalescing has to become size-aware.** Five 6 KB reports batched into one delivery is 30 KB into a
TUI. `SessionQueuedMessage.ConversationKey` already coalesces a contiguous run of same-conversation
messages — set `ConversationKey = "task:{rootTaskId}"`, add `Delegation` to the batching origins, and
add the one new rule: stop batching when the combined body would cross `ReplyInlineMaxChars`. The
remainder rides the next turn-end, which the queue already does naturally.

### 2.5 Workspaces — Shared by default for workers; an orchestrator owns something

**`Shared` is the default for workers.** A delegate runs in the working directory it was pointed
at, with no isolation, exactly as if you had opened a terminal there yourself. `-Worktree` opts in
to isolation when you want it.

> **As built (2026-08-08):** a **sub-orchestrator defaults to its own worktree** — it fans out
> writers, so it must own either a worktree or a location. A distinct `-Dir` counts as isolation
> (no worktree on top). Forcing `-Shared` is honoured but **warned**, at creation, in the
> `AgentTaskCreatedDto.Warning` the caller sees (and a `Warning` event on the timeline) — same for
> an orchestrator in a non-git directory that cannot be isolated. Inside its worktree the
> orchestrator gets the `PreToolUse` deny hook (§2.8) unless opted out.

That's the right default because most delegated work either *must* see live state (deploys, test
runs, log reads, anything touching the running stack) or is small enough that isolation is pure
overhead — a worktree costs a `git worktree add`, a branch, a merge-back, and a conflict path, and
paying that for a one-file doc change is silly. Isolation is the exception you reach for when two
delegates would genuinely collide, or when you want a change reviewable before it lands.

| Mode | What it is | Reach for it when |
|---|---|---|
| `Shared` **(default)** | Runs directly in the target directory, no isolation. Dispatcher serialises two `Shared` tasks whose `ScopeGlob`s intersect | Almost always: deploys, test runs, log reads, single-file edits, anything needing live state |
| `Worktree` | `git worktree add` on a task branch; commit-all → **rebase** onto the target → `merge --ff-only`; pruned by the existing janitor | Parallel writers that would collide; work you want to review before it lands; long-running changes |
| `ReadOnly` | Shared cwd, brief says don't write | Review, coverage audit |

The cost of `Shared` being the default is real and worth stating: two delegates editing one file
race on read-modify-write and the later write wins silently. Two things mitigate it — intersecting
`ScopeGlob`s serialise, and the orchestrator contract tells an orchestrator to reach for `-Worktree`
when it fans out multiple writers over the same area. Neither is a guarantee. `Shared` is the right
*default*, not the right answer for every fan-out.

**Merging (only when `Worktree`).** Rebase-then-fast-forward, never a merge commit — the repo
convention. A task merges into **its parent's branch**, not into master: if every leaf merged into
`master`, a dozen workers would race to integrate against a moving target and you'd resolve the same
conflict repeatedly. A sub-orchestrator owns a branch, its workers merge into that, and it merges one
level up when its subtree is done — integration once per level. It's also the right place to resolve
subtree conflicts, being the only party that knows what each child was supposed to do. On conflict
the task goes `Blocked` and a `Merge`-role task (High tier) is spawned with the conflict list.
`MergeTargetRef` defaults to the parent task's branch and is set explicitly only at the root.

### 2.5b Cross-repo: a task can point anywhere

The working directory is a property of the **task**, not inherited from the parent. That single
decision is what makes agent-per-repo orchestration work:

```
delegate.ps1 -Dir C:\src\am-service    -Role Deploy -Goal "roll out the gateway build"
delegate.ps1 -Dir C:\src\antiphon      -Orchestrator -Goal "make the client speak the new contract"
```

- `WorkingDirectory` on the task is absolute, resolved and validated at creation (must exist; must be
  inside one of the configured `AllowedRoots`).
- `RepoPath` is derived from it via `git rev-parse --show-toplevel`, so a task in a subdirectory of a
  repo, in a different repo, or in a plain non-git directory all behave sensibly. Non-git directories
  are legal in `Shared` mode and rejected for `Worktree` (nothing to branch) — with a clear message,
  not a crash.
- Omit the directory and the task inherits the parent's — the common case stays a one-liner.

**`AllowedRoots` is a real security boundary, not a nicety.** Without it, an agent that can create
tasks can point one at any path the server user can read and have a fresh Claude run there. It's
config (`Delegation.AllowedRoots`, defaulting to the parent's repo root), enforced server-side at
creation, with a rejection recorded as an incident.

A cross-repo task's report comes home the same way as any other — the reply is routed by
`ParentSessionId`, which has nothing to do with where the work happened. Merging, by contrast, is
strictly within one repo: a `Worktree` task merges into its parent's branch **only when they share a
repo**, and otherwise leaves its branch for a human with that stated in the report. Cross-repo
"merge" is a release-coordination problem and deliberately out of scope.

### 2.6 Two entry points, one core

Delegation is created two ways. They differ only in **who fills the request** and **where the reply
goes** — one endpoint, one dispatcher, one reply path underneath.

| | Manual | Agent-invoked |
|---|---|---|
| Trigger | Files view "Delegate…", board "New task" | The `antiphon-delegate` skill inside a running agent |
| Parent | none (or a chosen agent) | inferred from `ANTIPHON_SESSION_ID` |
| `ReplyTo` | `None` — the result lands on the board | `Session(parentSessionId)` — the note is delivered back |
| Who picks kind / role | the human, from a worker-or-orchestrator toggle and role chips | the agent, from the skill's tables |

Both paths create exactly the same two things — a worker or a sub-orchestrator (§2.1). There is no
third, cleverer entry point that decomposes something for you.

`ReplyTo` is a field on the task (`None | Session | Channel`), so a manual task can also be pointed
at an agent when you want one, and the reply machinery doesn't care which entry point created it.

#### The env contract

Every agent session Antiphon launches gets these injected via the existing
`AgentLaunchOptions.ExtraEnv` (`AgentRegistry.cs:65` already merges them):

```
ANTIPHON_API=http://localhost:17202
ANTIPHON_SESSION_ID=<guid>     # who is calling — the server infers the parent from this
ANTIPHON_AGENT_ID=<guid>
ANTIPHON_TASK_ID=<guid>        # set only when this session IS a delegate — carries lineage + depth
ANTIPHON_TASK_TOKEN=<opaque>   # scoped bearer; without it the endpoint refuses
```

Because the caller's identity is in the environment, **the agent never has to know or pass it**. It
says "delegate this"; parent linkage, depth accounting, fan-out caps and reply routing all follow
from the env. The token matters: without it any shell on the box could queue work onto your fleet.

#### The skill

`.claude/skills/antiphon-delegate/SKILL.md` plus `scripts/delegate.ps1`, so one invocation is one
line. Every token in that skill is charged to the orchestrator on each call, so the role table is the
only thing worth spending words on — it *is* the complexity-classification mechanism:

```markdown
# antiphon-delegate — hand work to another agent

    # a worker: one piece of work, reports back to you
    ./scripts/delegate.ps1 -Role Code -Goal "add Fizz(int) in Calc.cs, multiples of 3 -> 'Fizz'"

    # a sub-orchestrator: owns a chunk, decomposes it, runs its own agents
    ./scripts/delegate.ps1 -Orchestrator -Goal "get the Postgres upgrade to 18 shipped"

Two decisions, in this order.

**1. Worker or sub-orchestrator?**
Worker when you can state the deliverable in one sentence and one agent can finish it —
a doc change, a test run, a commit, one function.
Sub-orchestrator when the chunk needs its own decomposition: several steps, several tiers,
or you don't yet know the shape of it.
Unsure? Send a worker. A worker that comes back saying "this is bigger than it looked" is
cheap, and you can re-send it as a sub-orchestrator knowing more than you did.

**2. Which role?** Pick by what the work IS. The role sets the model tier and the cost;
that is the whole decision you are making. (A sub-orchestrator defaults to Plan.)

| Role     | Use for                                            | Tier   |
|----------|----------------------------------------------------|--------|
| Plan     | decompose, design, choose an approach              | fable  |
| Code     | write or change code                               | fable  |
| Review   | judge whether logic is correct                     | fable  |
| Debug    | find out why something is broken                   | opus   |
| Coverage | check what a change missed                         | opus   |
| Docs     | prose, markdown, comments                          | sonnet |
| Commit   | git add/commit/push/branch, PRs                    | sonnet |
| Test     | run a suite or build and report what failed        | haiku  |
| Deploy   | run a script, restart a service, check health      | haiku  |

Options
  -Orchestrator   make it a sub-orchestrator instead of a worker
  -Level <tier>   override the role's tier (say why in -Goal)
  -Dir <path>     run somewhere else — another repo, another checkout. Defaults to yours
  -Worktree       isolate in a fresh git worktree, merged back when it finishes.
                  Default is to run right in the directory, like you would yourself.
                  Use it when several delegates would write the same files at once,
                  or when you want the change reviewable before it lands.
  -ReadOnly       shared directory, but the brief says don't write
  -Scope "<glob>" declare the files this task owns; intersecting scopes serialise
  -Wait           block and print the report instead of returning (rare — prefer async)

Rules
- One task, one deliverable. Don't delegate what you could finish in two tool calls.
- Write -Goal as an outcome, not a procedure. The delegate decides how.
- Don't poll. The report is delivered into your session as `[task <id> done] …`.
- A delegate that asks a question comes back blocked: answer it with
  ./scripts/delegate.ps1 -Reply <id> "your answer" — don't take the work back.
```

> A future upgrade is an MCP server exposing `delegate` / `task_status` as real tools — structured
> args, no shell round-trip, no PATH assumptions. The skill + REST path is a tenth of the work and
> proves the model first.

### 2.7 The reporting contract given to every delegate

Composed **server-side** and attached to every dispatch, so it can't be forgotten by a calling agent
and stays identical across every delegate. It rides two vehicles: `--append-system-prompt` at launch
for ephemeral delegates (survives compaction, applies to every turn) and the delivered brief in all
cases (salient at the moment of work — and the only option for a pooled agent that is already
running).

```
[antiphon-task:7f3a2b91] role=Code tier=fable workspace=worktree scope=server/**

<the caller's goal>

--- how to report back ---
Your final message is the entire report the caller receives. Nothing else from this
session is forwarded, and the caller cannot see your screen.

Lead with the outcome in one line: what you did or found, and whether it worked.
Then only what the caller needs in order to act — files changed, commands to rerun,
decisions that are theirs to make, what is blocking you.

No preamble, no restating the task, no narrating the steps you took, no sign-off.
If you ran tests or builds, give counts and the failures, not the passing output.
If you could not finish, say that in the first line and say exactly what stopped you.

If your report would run past 20,000 characters, write the full detail to
.antiphon/task-7f3a2b91.md and make your final message a summary that points at it.
```

The last line is the primary size mechanism (§2.4) — the delegate is the only party that knows which
20 000 characters were the important ones.

### 2.8 The orchestrator's contract

The other half: an agent that reflexively does the work itself gets you nothing. This contract goes
to the root orchestrator through `SystemPromptAppend` (existing per-agent field, applied on fresh
launches *and* resumes) and to **every sub-orchestrator** through `--append-system-prompt` at launch —
same text, because a sub-orchestrator is an orchestrator:

```
You are an orchestrator. You do not do the work — you decompose it, delegate every piece,
and integrate what comes back.

Do yourself only: read enough to decompose (list files, read a spec, check git status);
decide the plan and the roles; integrate delegate reports; talk to the human.

Delegate everything else — every code edit, every test run, every git operation, every
investigation deeper than a single file read. If you are about to Edit, Write, or run a
build, stop: that is a delegation.

Reports arrive between your turns as `[task <id> done] …`. Do not poll and do not wait —
end your turn; the report will reach you. When a delegate asks a question, answer it with
-Reply. Taking the work back is the failure mode this exists to prevent.

If a piece is big enough to need its own decomposition, send a sub-orchestrator (-Orchestrator)
rather than trying to run its steps yourself.

Delegates run directly in the working directory by default. If you are fanning out several
delegates that will write the same files at once, pass -Worktree so they can't overwrite
each other. Work in another repo goes to a delegate with -Dir pointing there.
```

A sub-orchestrator gets one line more, because its report is a rollup rather than a work product:

```
Your final report covers your whole subtree. Say what was accomplished, what each delegate
concluded that the caller still needs, and what is unresolved. Do not paste your delegates'
reports — you read them so the caller doesn't have to.
```

That sentence is the load-bearing one for context economy: without it a sub-orchestrator forwards
everything it received and the nesting saves nothing.

Instruction alone is soft. The hard version is a **`PreToolUse` hook** that denies
`Edit`/`Write`/`MultiEdit`/`NotebookEdit` with the message "delegate this instead" — Claude Code
hooks can refuse a tool call outright, which turns the rule into an invariant.

> **As built (2026-08-08):** the hook is written to `.claude/settings.local.json` **in the
> orchestrator's own worktree only** — the worktree default (§2.5) is what makes this safe, since a
> settings file in a shared directory would change every session running there. The file is added
> to the repo's shared `info/exclude` so merge-back's commit-all can never sweep it onto the target
> branch. Toggles: `Delegation:OrchestratorDenyHookEnabled` (config, default on), overridden
> per-task by `DenyDirectEdits` — the modal's "Block direct edits" switch, the script's
> `-AllowDirectEdits`. Workers never get it; build-shaped `Bash` denial was deliberately dropped
> (parsing command intent is guesswork, and the edit tools are where the rule bites).

### 2.9 UI

> **As built:** `client/src/features/delegations/` — `DelegationsBoard` (tree + lanes), `TaskTree`,
> `TaskChip`/`TierBadge`, `TaskDrawer`, `DelegateModal`; mounted as the second tab of
> `features/orchestrator/OrchestratorPage` (`/orchestrator?tab=delegations`) and reachable from the
> files view's "Delegate…". Screenshots: `docs/ui-screenshots/delegations-board--*.png`.

**Delegations board** — a second tab on the existing `/orchestrator` page rather than a new
top-level section; that page already owns "what is the fleet doing right now". Left: the task tree
(root → children, indent guides, the shape of the fan-out). Right: lanes for Queued / Working /
Blocked / Done. Each chip carries agent, tier, elapsed, cost, workspace badge, and an escalation
marker when the task got bumped. A chip opens a drawer with brief, result, event timeline, links to
the delegate's transcript and files view, and Retry / Escalate / Cancel.

Tier needs its own visual axis — it is a ladder, not a status — so it must not reuse
green/orange/red. A single violet at four intensities (solid → tinted → outline → grey) reads as
rank and can never be mistaken for health.

The tree is the important half of that page once nesting is normal: a sub-orchestrator's children
belong under it, collapsed by default, with the subtree's own task count and spend on the parent row.
Expanding is how you audit a subtree; collapsed is how you read the run.

**Delegate from the files view** — in `FilesReviewPanel`, a "Delegate…" action on the selected file
(and on a selection within it, which prefills the range into the goal). The modal is deliberately
plain: a worker/sub-orchestrator toggle, role chips that derive the tier, workspace, merge target,
and the goal — prefilled with the path, nothing inferred beyond that. No split, no preview list, no
suggested decomposition; you say what the piece of work is.

### 2.10 API

```
POST   /api/agent-tasks                    create; parent + ReplyTo inferred from ANTIPHON_SESSION_ID
GET    /api/agent-tasks?rootId=&status=    board query
GET    /api/agent-tasks/{id}               detail incl. the untouched full result
POST   /api/agent-tasks/{id}/reply         answer a Blocked delegate's question; unblocks it
POST   /api/agent-tasks/{id}/cancel
POST   /api/agent-tasks/{id}/escalate      manual tier bump
POST   /api/agent-tasks/{id}/retry
```

Agent-invoked calls authenticate with `ANTIPHON_TASK_TOKEN`; the manual UI path uses the session's
existing auth. Both land in the same handler. A token issued to a **worker** carries no create
scope, so `POST /api/agent-tasks` from inside a worker is a 403 — the enforcement behind §2.1.

---

## 3. Phasing

**P1 — the spine. ✅ shipped.** `AgentTask` + migration, create/get/reply endpoints, the env contract
and task token, dispatcher against *pinned* agents, `Shared` mode + `AllowedRoots` + cross-repo
targeting, the reporting contract,
`AgentTaskReplySink` with the 20 k rule, the skill and script, the orchestrator preamble. At the end
of P1 the orchestrator delegates, stays small, and gets real reports back. Everything after is
leverage.

**P2 — the ladder. ✅ shipped**, including the `PreToolUse` deny hook (armed by default in each
orchestrator's worktree; `Delegation:OrchestratorDenyHookEnabled` / per-task `DenyDirectEdits`
toggle it). Role policy ✅,
escalation with handoff ✅ (manual via the drawer/API **and** automatic — the dispatcher tick bumps
a task with no transcript progress for its policy's `escalateAfterMinutes`; progress resets the
clock), ephemeral agents ✅ (spawned at dispatch, session stopped and row deleted when the task
settles — `AgentTask.AgentName` snapshots the name for the board), rebase merge-back ✅
(`DelegationWorktreeService`: worktree created at dispatch from the merge target, commit-all →
rebase → fast-forward on success, target advanced even while checked out, conflict → task `Blocked`
+ a `Merge`-role delegate spawned into the conflicted worktree, whose completion un-blocks the
task), the Delegations board ✅.

**P3 — the rest. ✅ shipped.** File-view delegate modal ✅, advisory scope leases ✅
(dispatcher-side), cost rollups and subtree collapse on the board ✅.

Sub-orchestrators land in **P1**, not later: the token scoping and the reply-to-parent routing are the
same code either way, and building the worker path first and retrofitting recursion would mean
redoing both.

**Fix alongside P1** (small, independent, all currently latent bugs):
`RouteMentionAsync` → route through the message queue so multi-line mentions can't fragment, and drop
the card-scope restriction; add `QueuedMessageOrigin.Delegation`.

---

## 4. Risks

| Risk | Mitigation |
|---|---|
| **Reply misrouted** — a human types in the delegate's terminal and their turn reads as the task result | Match the `[antiphon-task:id]` marker, not prompt text (§2.4) |
| **Context floods back** — 20 k reports × a wide fan-out | Delegate self-spills above the ceiling (§2.7); server head+tail backstop; size-aware coalescing; the orchestrator never reads a delegate transcript |
| **The orchestrator does the work itself**, and the fleet is decoration | Preamble contract (§2.8); optional `PreToolUse` deny hook; the pipeline E2E asserts zero Edit/Write records in the orchestrator's transcript |
| **A rogue shell queues work** onto the fleet | `ANTIPHON_TASK_TOKEN`, scoped per session, required by the endpoint |
| **Recursive fan-out runs away** — nesting is intended, so depth alone can't be the guard | Workers cannot create tasks at all (403, token scope); `MaxCostUsdPerRoot` is the real ceiling and stops dispatch without killing work in flight; `MaxTasksPerRoot` and `MaxConcurrentTasks` behind it |
| **Nesting saves no context** — a sub-orchestrator forwards everything it received | The rollup clause in its contract (§2.8); the E2E asserts a subtree's report is shorter than the sum of its children's |
| **Cost** — Frontier on every Code task adds up fast | Live $ per root on the board; role policy is config; the tier ladder means cheap work stays cheap |
| **Worktree sprawl** from hundreds of tasks | Delegated worktrees adopt the existing `WorktreeJanitorHostedService` TTL |
| **Ephemeral agents pollute the agents page** | `Ephemeral` flag, filtered from `/agents` by default, deleted when the task settles |
| **Lost edits in `Shared` mode** — two agents read-modify-write the same file and the later write wins. This is the accepted cost of `Shared` being the default | Intersecting `ScopeGlob`s serialise; the orchestrator contract says to reach for `-Worktree` when fanning out multiple writers over the same area. Mitigation, not a guarantee — stated plainly in §2.5 |
| **A task points at an arbitrary path** — an agent that can delegate could run Claude anywhere the server user can read | `Delegation.AllowedRoots` enforced server-side at creation; rejection recorded as an incident |
| **Escalation pays twice for the same dead end** | Handoff block carries the failed attempt's findings into the retry |

---

## 5. Open questions

1. **Task vs Card.** This proposes a separate lightweight entity. The alternative — every delegation
   is a Card on the delegating agent's board — reuses the board UI, retry and worktree wiring, but
   drags tracker sync and column semantics into a "rewrite one heading" unit. Recommendation:
   separate entity, with an optional `CardId` link so a task *can* belong to card work.
2. **Where the board lives.** Proposed as a tab on `/orchestrator`. A top-level "Delegations" nav
   item is the alternative if this becomes the primary way work gets done.
3. **Ephemeral vs pooled default.** Proposed ephemeral. Pooling saves a cold start per task but
   accumulates context, which is what we're spending money to avoid.
