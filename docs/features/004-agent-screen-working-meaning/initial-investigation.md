# Feature 004 — Agents screen: what does "Working" actually mean?

**Status:** initial investigation, for review
**Date:** 2026-08-01
**Trigger:** "Why are all agents showing as working on the agents page?"

---

## TL;DR

The "Working" badge on the agents page is **not** a live activity signal. It is a persisted
lifecycle latch (`Agent.Status`) that is set once at start and never returns to idle. Every agent
with a live session therefore reads "Working" forever.

Antiphon *does* have a real per-turn working/idle signal — `IsWorkingAsync`, computed over the
persisted transcript — but it is only surfaced next to the terminal (`SessionWorkingBadge`), never
on the agents list. The two disagree right now for 4 of 5 always-on agents.

---

## Observed behaviour

All six agents render a yellow-ish `Working` badge on the agents page, permanently, regardless of
whether the underlying Claude session is mid-turn or sitting idle at the prompt.

Live check against the dev server (`GET /api/agents` vs `GET /api/sessions/{id}/messages`) on
2026-08-01:

| Agent | Card badge (`agent.status`) | Transcript `working` | Session status |
|---|---|---|---|
| Antiphon | Working | **true** | Running |
| Antiphon-Opus | Working | **true** (mid-turn) | Running |
| AZ Care | Working | false | Running |
| Family | Working | false | Running |
| school-revision | Working | false | Running |
| Torquay Leander | Working | false | Running |

Four of six are demonstrably idle and still badge as "Working".

---

## Root cause

### The badge renders a persisted enum, not a computation

`client/src/features/agents/AgentsPage.tsx:126` (card) and `:199` (detail header):

```tsx
<Badge variant="light">{agent.status}</Badge>
```

`agent.status` comes straight off the entity — `AgentService.ToSummaryDto` (`AgentService.cs:617`)
and `ToDetailDto` (`:662`) both project `agent.Status` with no enrichment.

### `Agent.Status` is a one-way latch

`server/Domain/Entities/Agent.cs:14` — persisted column, default `Idle`.

Every write site in the server:

| Site | Transition |
|---|---|
| `AgentService.cs:167` | `Idle` — on agent creation |
| `AgentControlService.cs:97` | → `Working` — on **Start** |
| `AgentControlService.cs:274` | → `Stopped` — on explicit **Stop** |
| `AgentSessionRuntime.cs:168-170` | → `Stopped`/`Failed` — on process exit (cardless agents only) |
| `AgentSessionService.cs:294-297` | → `Failed` |
| `SessionReconciliationService.cs:198` | → `Failed` — reconciler found no live session |

**There is no transition back to `Idle` or `Ready` when a turn ends.** `Working` here means
"this agent has been booted and has not since stopped or crashed" — a *lifecycle* state, closer to
"Running" than to "busy". With five always-on agents plus one interactive session, that is
everything on the page.

Note `SessionReconciliationService.cs:175-204` also *depends* on this meaning: it treats
`Status == Working` as the set of agents that ought to have a live session, and flips them to
`Failed` when they don't. So the field is load-bearing as a lifecycle latch and cannot simply be
repurposed to mean per-turn busy.

### The real signal exists, elsewhere

`SessionMessageQueueService.IsWorkingAsync` (`server/Application/Services/SessionMessageQueueService.cs`,
~`:545-577`) computes working/idle from the persisted transcript: the last activity record's
sequence vs the last turn-end's, excluding `TurnEnd`, `TurnTitle`, `CompactBoundary`, local
slash-command records, and interrupt markers (each of those exclusions is a fix for a real
stranding incident — see the CLAUDE.md gotchas).

It is exposed on `GET /api/sessions/{id}/messages` → `SessionQueueDto.working`
(`BuildQueueDtoAsync`, `:579-590`), and rendered by
`client/src/features/agents/SessionWorkingBadge.tsx` — which polls every 3s and is deliberately
placed next to the terminal so a human can cross-check it against the terminal output.

There is also a client-side twin, `isWorking()` in `SessionTranscriptPanel.tsx:112`, kept in
lockstep with the server rule.

So the agents page and the terminal panel answer two different questions, and only one of them is
the question a user reading the agents page is actually asking.

---

## Why this matters

- **The page is misleading at a glance.** The status column is the primary at-a-glance signal on
  the agents screen and it is constant. It carries no information for a fleet of always-on agents.
- **It hides the failure mode the signal was built to expose.** The stuck-"Working" symptom is
  exactly what the interrupt-marker and local-command detection gaps looked like
  (2026-07-29, 2026-07-31). If the agents page badge is *always* "Working", it cannot be used to
  spot a genuine working/idle detection regression — a real stuck session is indistinguishable
  from normal.
- **"Stop" button gating is wrong for the same reason.** `AgentsPage.tsx:216` shows Stop vs Start
  based on `status === 'Working'`. That happens to be correct today (it means "has a live
  session"), but it reads as if it means "is busy", which invites future edits that break it.

---

## Options

### Option A — surface both (recommended)

Add a live `working` bool to `AgentSummaryDto`/`AgentDetailDto`, populated from `IsWorkingAsync`
over the agent's `PersistentSessionId`, and render it as a second badge on the card. Keep `status`
for the lifecycle states (`Stopped`, `Failed`, `Idle`), which are genuinely useful and have no
other home.

- Card reads e.g. `Running` + `Idle` / `Running` + `Working…`.
- One server-side query per listed agent, batched in the list projection — **not** a per-card
  client fan-out of `getSessionQueue` polls, which is what a naive fix would produce.
- Reuses the already-hardened rule; no second implementation to keep in lockstep.

**Open question for review:** the `Working` *label* on the lifecycle enum is the real trap. Renaming
`AgentStatus.Working` → `AgentStatus.Running` would make both badges self-explanatory, but it is a
persisted enum (int-backed, so no data migration) with string comparisons on the client
(`AgentsPage.tsx:216`) and in tests. Worth doing as part of this, or separately?

### Option B — client-side only

Have each card mount a `SessionWorkingBadge` for its live session. Zero server change, but N
polling queries at 3s from the agents page. Cheap to build, poor at fleet scale.

### Option C — recompute `Agent.Status` per turn

Write `Working`/`Ready` back to the entity on turn boundaries. **Not recommended** — it makes a
persisted column track a high-frequency signal, adds write amplification on every turn-end, and
breaks `SessionReconciliationService`'s use of `Status == Working` as "should have a live session".

---

## Cost / risk

Option A is roughly:

- `server/Application/Dtos/AgentDtos.cs` — one field on each of two records
- `server/Application/Services/AgentService.cs` — resolve live-session ids (already done for
  `liveSession`, `:113`), call the working check for each, thread into `ToSummaryDto`/`ToDetailDto`
- `client/src/api/agents.ts` — one field
- `client/src/features/agents/AgentsPage.tsx` — one badge
- Tests: extend `AgentsPage.test.tsx`; a server test asserting an idle live session reports
  `working: false` while `status` stays `Working`/`Running`

Risk is low and contained. The main design decision is whether `IsWorkingAsync` should be lifted
out of `SessionMessageQueueService` into something both it and `AgentService` depend on, rather
than `AgentService` reaching into a queue service.

---

## Verification commands used

```powershell
# Agent lifecycle status
Invoke-RestMethod http://localhost:17202/api/agents |
  Select-Object name, status, @{n='session';e={$_.liveSession.status}}

# Transcript-derived working/idle for a session
Invoke-RestMethod http://localhost:17202/api/sessions/<sessionId>/messages |
  Select-Object working
```

---

## References

- `client/src/features/agents/AgentsPage.tsx:126,199,216`
- `client/src/features/agents/SessionWorkingBadge.tsx`
- `client/src/features/agents/SessionTranscriptPanel.tsx:112` (`isWorking`)
- `server/Domain/Entities/Agent.cs:14`, `server/Domain/Enums/AgentStatus.cs`
- `server/Application/Services/AgentControlService.cs:97,274`
- `server/Application/Services/AgentService.cs:113,617,662`
- `server/Application/Services/AgentSessionRuntime.cs:168-170`
- `server/Application/Services/SessionReconciliationService.cs:175-204`
- `server/Application/Services/SessionMessageQueueService.cs:110,545-590`
- `CLAUDE.md` — working/idle gotchas (interrupt markers, local slash-commands, `/clear` forks)
