# CARD-0291 — Named-agent children report to nobody: route child work through the task path

**Date:** 2026-08-31 (Plan pass, task 4b4cc8bd — design only; nothing built)
**Card:** CARD-0291 "Gym orchestrator didn't notice its 2 sub-agents finished until prompted"
**Diagnosis:** complete and conclusive (task 5b0c61da, card revision 3 + discussion `65478a48`).
This plan does not re-litigate it.

**Sources:** `server/Api/Endpoints/AgentEndpoints.cs`, `server/Application/Services/AgentTaskService.cs`
(CARD-0140 S1 standing-agent pin, `AgentTaskService.cs:203-227`), `AgentTaskDispatcher.cs`
(`PlaceOnStandingAgentAsync`, `AgentTaskDispatcher.cs:2816`; shared-writer lease,
`AgentTaskDispatcher.cs:243-286`), `AgentTaskReplyService.OnTurnEndAsync`, `AgentSessionRuntime`
turn-end fan-out (`AgentSessionRuntime.cs:452-482`), `Domain/Entities/{Agent,AgentTask,AgentSession,SessionQueuedMessage}.cs`,
`scripts/delegate.ps1`, `.claude/skills/antiphon-delegate/SKILL.md`, `server/Bundles/orchestrator.md`,
`docs/orchestration-loop.md`, and the CARD-0285 plan (same failure class, disjoint gap).

---

## The incident in one paragraph

The Gym Stat Orchestrator (ClaudeCode AlwaysOn) spawned two children as **named agents** via
`POST /api/agents` + `/start` + session messages. Zero `AgentTask` rows existed, so nothing in the
completion machinery — settlement on TurnEnd, the `[task done]` WhenIdle note, the check ramp,
card-transitions — had anything to fire on. Both children finished real committed work 89 and 37
minutes before the parent knew, and it only knew because the user asked. CARD-0264/0285/0286/0288
fixes are all downstream of an AgentTask row and could never have fired.

## Decision

**Hybrid, weighted to direction (1): make the AgentTask path subsume the named-agent use case, and
steer orchestrators onto it. Do not build direction (2)'s parallel completion-notification pipeline
for raw named-agent children.**

Concretely:

1. The server can already run a task **on a pinned standing agent** (CARD-0140 S1 +
   `PlaceOnStandingAgentAsync` — the check interpreter exercises this path daily), but the
   capability is unreachable from `delegate.ps1`: the request field is a bare `AgentId` guid and the
   script exposes nothing for it. Expose it end-to-end (`-Agent <name|slug|guid>`), so "my named
   standing child" and "work I get notified about" stop being mutually exclusive.
2. Fix the steering, which today points INTO the trap: `docs/orchestration-loop.md` §2 documents
   `POST /api/agents` + `/start` as "Launching an agent" with no warning that no completion signal
   will ever come, and `server/Bundles/orchestrator.md` never mentions the distinction at all.
3. State honestly that hard enforcement is neither possible nor desirable (below), and record the
   deferred alternatives so the next incident doesn't re-derive them.

### Why not direction (2) — a completion-notification path for raw named-agent children

Evaluated seriously; rejected for v1 on four grounds:

- **"Completion" is undefined without a work-unit record.** A standing agent's session TurnEnds on
  *every* turn — clarifying questions, channel messages, human prompts, each refinement. A raw
  TurnEnd relay either spams the parent with a note per turn or needs "the work is done" semantics,
  and those semantics are exactly what `AgentTask` provides: the correlation marker in the brief,
  marked-turn extraction (`ExtractMarkedTurnAsync`), the `[antiphon-report:… done|blocked|failed]`
  verdict line. A relay can't distinguish "finished" from "stopped to ask" from "answered something
  unrelated" without rebuilding that contract.
- **No report without re-deriving the hardest-won machinery in the repo.** Carrying the child's
  last AssistantText to the parent re-opens the text-after-TurnEnd race (CARD-0046), pty inline
  ceilings (CARD-0027/0037), delivery confirmation (CARD-0055), and unmarked-turn ambiguity
  (CARD-0159/0248) — a second, weaker settlement pipeline maintained forever alongside the first.
- **It is equally opt-in.** There is no caller attribution on `POST /api/agents`, `/start`, or
  `POST /api/sessions/{id}/messages` (verified: `Agent`, `AgentSession`, `SessionQueuedMessage`
  carry no creator/sender session; "send now" messages aren't even stored). Any notification path
  therefore needs the orchestrator to explicitly declare "notify me" — the same steering problem
  direction (1) has, with a strictly worse payoff (a "went idle" ping instead of settlement,
  report, cost, check ramp, and card movement).
- **The incident's real need is fully covered by the task path.** Checked shape by shape: children
  keeping persistent identity/context across cards → standing agent + repeated pinned tasks (the
  check interpreter's exact pattern); AlwaysOn child not yet running → `PlaceOnStandingAgentAsync`
  returns `WaitForAgent` and the dispatch waits; another repo → `-Dir` (the caller's own tree is
  always inside `AllowedRoots` semantics — the gym orchestrator's cwd IS `C:/src/gym-stat`);
  Codex/Grok children → allowed for Workers (CARD-0084/0099), which is precisely what the two gym
  children were. Nothing about the incident required stepping outside `AgentTask`.

### Is (1) enforceable, and should it be?

Hard enforcement — refusing agent-create/start/message calls from orchestrator callers — fails on
both axes:

- **Not possible:** those endpoints have no caller identity (single-user API; only
  `POST /api/agent-tasks` authenticates, via the `ANTIPHON_TASK_TOKEN` bearer that
  `AgentSession.DelegationTokenHash` verifies). The gym orchestrator called them with plain curl.
- **Not desirable:** creating standing agents is legitimate and documented (the UI uses the same
  endpoints; per-repo standing agents, channel-bound agents, and the check interpreter all live
  there). The line to draw is not "don't create named agents" but **"a named agent is an identity;
  work you need to hear finish is a task"** — create the agent once, then dispatch each unit of
  work as a task pinned to it.

So enforcement is: make the good path strictly more capable (S1/S2), fix the documents that steer
(S3), and accept that an orchestrator that ignores both will reproduce the gap — at which point the
recurrence lands on this card's evidence trail, not on a new mystery.

---

## Slices

### S1 — Server: resolve a standing agent by name on task create

`CreateAgentTaskRequest` gains `string? Agent = null` (same doc style as `Card`): accepts a guid, a
slug, or a name.

- Resolution order: parses as guid → lookup by id; else exact slug match; else case-insensitive
  exact name match. Neither `Name` nor `Slug` is unique in the schema (checked
  `AppDbContext.cs:782-802` — no index), so **two or more matches → 422 naming the candidates and
  their guids**; zero matches → 422. An explicit value that silently binds nothing or the wrong
  thing is worse than a refusal (same argument as `Card`).
- A resolved agent with `IsPoolDelegate == true` → 422 ("that is a pool delegate; use
  `-OnAgent <taskId>` for a follow-up"). Dispatcher-spawned delegates are all `IsPoolDelegate`, so
  this one check fences the whole ephemeral population.
- Both `Agent` and `AgentId` set and disagreeing → 422; agreeing is fine (idempotent callers).
- The resolved id feeds the existing pinned path at `AgentTaskService.cs:208` unchanged: CARD-0140
  S1 already inherits the agent's `Kind`, refuses an explicit mismatch with a 409, sets
  `Ephemeral = false`, routes subscription-quota checks at the pinned agent, and the dispatcher's
  `PlaceOnStandingAgentAsync` already delivers into the live session, serialises tasks per agent
  (`AgentTaskDispatcher.cs:2850`), warns on env divergence, and waits for an AlwaysOn agent with no
  live session.
- `-Agent` + `FollowUpOnTask` in one request → 422 (two different "run it on that agent" idioms;
  the follow-up already pins).

Tests (`Antiphon.Tests`, alongside the CARD-0140 pin tests): guid/slug/name resolution, ambiguous
name 422 listing candidates, unknown 422, pool-delegate 422, Agent+AgentId disagree 422,
Agent+FollowUpOnTask 422, and one end-to-end create asserting the task lands pinned
(`AgentId` set, `Ephemeral == false`).

### S2 — `delegate.ps1 -Agent` + skill doc

- New `[string]$Agent` on the Create parameter set, passed through as `agent`. Script-side
  validation: combined with `-OnAgent` → refuse locally with the same wording as the server 422
  (don't burn a round trip). `-Kind` may be omitted (inherits the pinned agent's kind) or must
  match — the server already refuses mismatch; the script does not duplicate that check.
- Help text states the contract in one line: *"Run this task on an existing standing agent by
  name/slug/guid. The task queues while that agent is busy; you get the normal `[task … done]`
  note when it settles."*
- `.claude/skills/antiphon-delegate/SKILL.md`: add the `-Agent` row to the parameter table next to
  `-OnAgent`, plus one worked example under a "Named standing children" heading: create the agent
  once (`POST /api/agents`), then dispatch every piece of work to it with
  `delegate.ps1 -Agent gym-stat-dupmachine-plan -Role Plan -Goal … -Card CARD-0029` — and say why:
  work sent by raw session message reports to nobody.
- ASCII-only (delegate.ps1 parses under Windows PowerShell 5.1).

### S3 — Steering: the two documents that caused this, plus the bundle

- **`docs/orchestration-loop.md` §2 "Launching an agent"** — this section is the trap the gym
  orchestrator fell into: it documents `POST /api/agents` + `/start` with no mention that nothing
  will ever report back. Add a short paragraph immediately after it: creating an agent gives you an
  identity, not a report; work you need to hear finish goes through `delegate.ps1` (use
  `-Agent <name>` to run it on the named child); a raw session message to a child is for steering
  an existing task, never for handing over work — no `[task … done]` will ever arrive for it
  (CARD-0291).
- **`server/Bundles/orchestrator.md`** — add the same rule in bundle voice (2–3 sentences, next to
  the existing "Reports arrive between your turns" paragraph): *"A child you start via
  POST /api/agents and prompt via session messages never reports back — no `[task done]`, no check,
  no card movement. Dispatch child work with delegate.ps1 (add `-Agent <name>` to run it on a named
  standing child); message a child's session directly only to steer work you already dispatched."*
  Bundle growth is ~400 chars against the 30,000-char command-line budget (worst case measured
  9,198; `InstructionBundleTests` pins it) — no risk, but the test run will confirm.
- **`docs/agent-kinds.md` / AGENTS.md**: no changes — the rule lives in the two places above;
  restating it in a third place is the drift AGENTS.md warns about.

Out of repo scope but worth the operator's minute: the Gym Stat Orchestrator's own agent `Details`
(and any similar standing orchestrator) should gain the same one-liner, since a standing agent
keeps its launch-time bundles until relaunch.

### Behavioural consequence to state, not hide

Under the task path, the gym's two same-repo children would have been **Shared writers in one
checkout and therefore serialised** (`SerialiseSharedWriters`, on by default; only Check-role and
ReadOnly tasks sit outside the lease — `AgentTaskDispatcher.cs:243-259`). That is the documented
collision default, not a regression: two agents writing one checkout concurrently is the exact
pattern the lease exists to stop, and the orchestrator buys parallelism explicitly with
`-Worktree` (or `-ReadOnly` for reads). The skill example in S2 should show the parallel variant
with `-Worktree` so the first user of `-Agent` doesn't read the hold as a bug. Note the tension for
pinned tasks: a `-Worktree` task pinned to a standing agent runs in the worktree, not the agent's
own directory — for a standing child whose value is its cwd/context, sequential Shared dispatch is
usually what's wanted anyway.

---

## Considered and deferred (do not build now; recorded so they aren't re-derived)

1. **One-shot idle watch** — `POST /api/sessions/{id}/watch-idle {notifySessionId}`: on the watched
   session's next TurnEnd with an empty queue, queue one System note into the watcher and disarm.
   Small (reuses the queue + the `FlushQueueOnIdleAsync` hook), clean one-shot semantics, and the
   only shape that covers a child that genuinely isn't running a task. Deferred because it is
   opt-in like everything else, carries no report/verdict, and S1/S2 leave it with no expected
   caller. Revisit only if a real child shape appears that the task pin cannot serve.
2. **Caller attribution on the agents API** — accept the `ANTIPHON_TASK_TOKEN` bearer on
   `POST /api/agents`/`/start` and stamp `CreatedBySessionId`, enabling recurrence detection
   ("named agent created by an orchestrator session; no task ever pinned to it; session went idle
   with commits"). Honest assessment: the orchestrator that ignores the S3 steering also omits the
   header, so this detects the compliant and misses the offender. Not worth the column until
   something else needs creator attribution.
3. **Card-evidence detector** — attention Warning when a card revision is authored by an agent
   session with no open AgentTask against that card (both gym cards stayed Backlog while children
   edited them). Plausible, but depends on revision `editedBy` reliably naming the agent, and the
   sweep would need careful carve-outs for humans and standing board-working agents. Verify
   `editedBy` contents first if this is ever picked up.

## Test matrix

| Layer | Test |
|---|---|
| `Antiphon.Tests` | `Agent` resolution: guid, slug, name, ambiguous→422 with candidates, unknown→422 |
| `Antiphon.Tests` | pool-delegate pin refused 422; `Agent`+`AgentId` disagree 422; `Agent`+`FollowUpOnTask` 422 |
| `Antiphon.Tests` | create with `Agent` → task pinned (`AgentId` set, `Ephemeral=false`, kind inherited) |
| `Antiphon.Tests` | existing CARD-0140 pin tests stay green (kind mismatch 409, quota owner) |
| `Antiphon.Tests` | `InstructionBundleTests` composition budget still passes with the grown orchestrator bundle |
| Manual/E2E | `delegate.ps1 -Agent <standing> -Goal …` from a standing orchestrator session: task queues while agent busy, delivers into the live session, `[task … done]` note lands in the caller |

Run TUnit via `dotnet run --project tests/Antiphon.Tests` (never `dotnet test`), sequentially, per
`docs/testing-and-build.md`.

## What this closes and what stays open

- Closes CARD-0291's gap for every orchestrator that follows the steering: named standing children
  + full completion machinery, no new pipeline.
- CARD-0285 (WhenIdle note into a busy caller) is orthogonal and already planned; CARD-0288 (stuck
  Dispatched) is orthogonal; neither is touched here.
- A non-compliant orchestrator can still reproduce the silence — accepted, documented above, with
  the detection options recorded for the day it matters.
