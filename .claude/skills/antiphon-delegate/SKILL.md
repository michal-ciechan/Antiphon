---
name: antiphon-delegate
description: Hand a piece of work to another agent, at the model tier the work deserves. Use when work is separable and you don't need it in your own context — writing code, running tests, deploying, investigating, editing docs, or a whole chunk that needs its own decomposition.
---

# antiphon-delegate — hand work to another agent

**Run it exactly like this** — `pwsh -NoProfile -File`, then the script path, then the arguments.
The script is not directly executable; a bare `./scripts/delegate.ps1` fails, and putting the
arguments before `-File` makes PowerShell reject them as its own and print its usage banner.

```powershell
# a worker: one piece of work, reports back to you
pwsh -NoProfile -File scripts/delegate.ps1 -Role Code -Goal "add Fizz(int) in Calc.cs, multiples of 3 -> 'Fizz'"

# a sub-orchestrator: owns a chunk, decomposes it, runs its own agents
pwsh -NoProfile -File scripts/delegate.ps1 -Orchestrator -Goal "get the Postgres 18 upgrade shipped"
```

Quote every value that contains a space. If `pwsh` is not on PATH, use `powershell` — the script is
ASCII-only and runs under either.

Two decisions, in this order.

## 1. Worker or sub-orchestrator?

**Worker** when you can state the deliverable in one sentence and one agent can finish it — a doc
change, a test run, a commit, one function.

**Sub-orchestrator** when the chunk needs its own decomposition: several steps, several tiers, or
you don't yet know the shape of it. It gets this same skill and runs its own delegates; its report
is a rollup of its whole subtree, so you read one summary instead of every leaf.

Unsure? Send a worker. A worker that comes back saying "this is bigger than it looked" is cheap, and
you can re-send it as a sub-orchestrator knowing more than you did.

## 2. Which role?

Pick by what the work IS. The role sets the model tier, and that is the cost decision.

| Role | Use for | Tier |
|---|---|---|
| `Plan` | decompose, design, choose an approach | fable |
| `Code` | write or change code | fable |
| `Review` | judge whether logic is correct | fable |
| `Debug` | find out why something is broken | opus |
| `Coverage` | check what a change missed | opus |
| `Docs` | prose, markdown, comments | sonnet |
| `Commit` | git add/commit/push/branch, PRs | sonnet |
| `Test` | run a suite or build and report what failed | haiku |
| `Deploy` | run a script, restart a service, check health | haiku |

`Test` and `Deploy` are cheap because they RUN things and report what happened. Interpreting a
failure is a separate `Debug` task — don't ask haiku to work out why the build broke.

A sub-orchestrator defaults to `Plan` and never runs below opus.

## Options

| | |
|---|---|
| `-Orchestrator` | make it a sub-orchestrator instead of a worker |
| `-Level <tier>` | override the role's tier — `Frontier`/`High`/`Medium`/`Low`. Say why in `-Goal` |
| `-Dir <path>` | run somewhere else — another repo, another checkout. Defaults to yours |
| `-Worktree` | isolate in a fresh git worktree, merged back when it finishes |
| `-ReadOnly` | shared directory, but the brief says don't write |
| `-Scope "<glob>"` | declare the files this task owns; intersecting scopes are serialised |
| `-Title "<text>"` | a short label for the board; defaults to the goal's first line |

**Workspace defaults to shared** — the delegate runs right in the directory, like you would
yourself. Pass `-Worktree` when several delegates will write the same files at once, or when you
want the change reviewable before it lands.

## Working across repos

The directory is a property of the task, so one orchestrator can drive several repos — an agent per
repo, each reporting back to you:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Dir C:\src\am-service -Role Deploy -Goal "roll out the gateway build and confirm health"
pwsh -NoProfile -File scripts/delegate.ps1 -Dir C:\src\antiphon -Orchestrator -Goal "make the client speak the new contract"
```

A directory outside the configured allowed roots is refused — that is a guard, not a bug. Ask for
the root to be added rather than working around it.

## Rules

- **One task, one deliverable.** Don't delegate what you could finish in two tool calls.
- **Write `-Goal` as an outcome, not a procedure.** The delegate decides how.
- **Don't poll.** The report is delivered into your session as `[task <id> done] ...` when it lands.
  End your turn; it will reach you.
- **A delegate that asks a question comes back blocked.** Answer it — don't take the work back:
  ```powershell
  pwsh -NoProfile -File scripts/delegate.ps1 -Reply <taskId> "yes, accept negatives"
  ```
- **Need a task's full text later?** `pwsh -NoProfile -File scripts/delegate.ps1 -Status <taskId>`

## What the delegate is told

You don't need to write reporting instructions into your goal — every delegate is already told to
lead with the outcome, give only what the caller needs to act, skip preamble and narration, and
write anything past 20,000 characters to a file and summarise it instead.
