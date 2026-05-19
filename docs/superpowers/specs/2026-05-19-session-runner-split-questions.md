# Superpower Flow: Stable Session Runner Split

Date: 2026-05-19
Status: User decisions recorded; ready for implementation
Target: Antiphon local development and agent execution stability

## Goal

Split live agent session execution out of the Antiphon web backend so the
dashboard, backend, frontend, and AppHost can be restarted during development
without killing active terminal-backed agent sessions.

The likely implementation shape is:

- Antiphon backend remains the control plane and public API.
- A separate runner process owns live terminal sessions, PTY adapters, stream
  buffers, input, resize, kill, and process lifetime.
- Frontend still talks to Antiphon backend and SignalR.
- Backend talks to the runner over an internal local API.
- First protocol preference is JSON HTTP commands plus Server-Sent Events for
  runner-to-backend event streaming.

## Current Baseline

Antiphon already has:

- `src/Antiphon.Agents.Pty` for PTY primitives.
- `server/Application/Services/AgentSessionRuntime.cs` for live session state,
  buffers, SignalR delta fanout, manual xterm turn tracking, and input/resize/kill.
- `server/Application/Services/AgentSessionService.cs` for DB session lifecycle,
  worktree creation, launch/resume/kill, prompts, run attempts, and runtime calls.

The problem is that the runtime is currently inside the web server process. When
the backend restarts, live PTY ownership disappears.

## Preferred Defaults

Use these unless you answer otherwise:

- Runner project name: `Antiphon.SessionRunner`.
- Transport: JSON HTTP commands plus SSE events.
- Frontend streaming: frontend stays on backend SignalR; no browser connection
  directly to the runner.
- Runner scope: local development first, service-ready shape.
- Persistence: database for session metadata, file-backed ANSI logs for stream
  replay.
- Orchestration: backend keeps card/workflow/orchestrator decisions; runner owns
  only live process/session mechanics.
- Migration: incremental compatibility layer, not a big-bang rewrite.

## How To Answer

Reply with answers by question ID. Short answers are fine.

Example:

```text
Q1 Antiphon.SessionRunner
Q2 JSON HTTP + SSE
Q6 Backend keeps orchestration
Q14 Windows local dev first
```

If you agree with the defaults, say:

```text
Use defaults except Q3...
```

## Questions

### A. Naming And Ownership

| ID | Question | Recommended Default | Your Answer |
| --- | --- | --- | --- |
| Q1 | What should the new runner project be named? Options: `Antiphon.SessionRunner`, `Antiphon.PtyRunner`, `Antiphon.AgentRunner`. | `Antiphon.SessionRunner` | |
| Q2 | Should the runner live in the Antiphon repo as a new project, or in `mikeys-agents` as a separate repo/service? | Antiphon repo | |
| Q3 | Should the public product language say "Session Runner", "Agent Runner", or "PTY Runner"? | Session Runner in UI/docs, PTY only in internals | |

### B. Process And Startup

| ID | Question | Recommended Default | Your Answer |
| --- | --- | --- | --- |
| Q4 | Should AppHost start the runner alongside backend and frontend? | Yes | |
| Q5 | Should restart scripts restart backend/frontend without restarting the runner by default? | Yes | |
| Q6 | Should there be a separate `restart-runner.ps1` for explicitly bouncing live sessions? | Yes, with warnings | |
| Q7 | Should the runner auto-start if backend cannot reach it, or should startup fail visibly? | Fail visibly in dev | |

### C. Transport

| ID | Question | Recommended Default | Your Answer |
| --- | --- | --- | --- |
| Q8 | Transport between backend and runner: JSON HTTP + SSE, gRPC, named pipes, or other? | JSON HTTP + SSE | |
| Q9 | Should SSE be per-session (`/sessions/{id}/events`) or global runner stream (`/events`) with session IDs in payloads? | Global `/events` | |
| Q10 | Should backend reconnect to SSE automatically and replay missing events from last sequence? | Yes | |
| Q11 | Should we design the client abstraction so gRPC can replace HTTP/SSE later? | Yes | |

### D. Responsibilities

| ID | Question | Recommended Default | Your Answer |
| --- | --- | --- | --- |
| Q12 | What stays in backend? | Boards, cards, workflow state, run attempts, orchestration, SignalR | |
| Q13 | What moves to runner? | Start/resume process, live PTY adapter, input, resize, kill, screen snapshot, ANSI log writes, event stream | |
| Q14 | Should manual xterm turn detection live in backend or runner? | Backend for DB/run-attempt ownership; runner only emits input/output events | |
| Q15 | Should mention routing observe deltas in backend or runner? | Backend | |
| Q16 | Should Claude-specific turn completion detectors stay in adapters inside runner? | Backend owns turn completion and all agent-specific logic. Runner should avoid agent-specific detectors so backend can iterate without restarting live runner sessions. | User answered: No; keep agent-specific logic in backend. |

### E. Persistence And Recovery

| ID | Question | Recommended Default | Your Answer |
| --- | --- | --- | --- |
| Q17 | Where should live session metadata be stored? | PostgreSQL `AgentSessions` plus runner in-memory map | |
| Q18 | Where should terminal output replay live? | Runner-owned ANSI log files, exposed by `/sessions/{id}/buffer` | |
| Q19 | On backend restart, should backend discover runner live sessions and mark matching DB sessions as running? | Yes | |
| Q20 | On runner restart, should backend mark previously live DB sessions as failed/stopped? | Yes, after health timeout | |
| Q21 | Should a running session survive frontend restart? | Yes | |
| Q22 | Should a running session survive backend restart? | Yes | |
| Q23 | Should a running session survive runner restart? | No, not initially | |

### F. API Shape

| ID | Question | Recommended Default | Your Answer |
| --- | --- | --- | --- |
| Q24 | Runner start endpoint shape? | `POST /sessions` with full launch spec | |
| Q25 | Runner resume endpoint shape? | `POST /sessions/{id}/resume` | |
| Q26 | Runner input endpoint shape? | `POST /sessions/{id}/input` | |
| Q27 | Runner resize endpoint shape? | `POST /sessions/{id}/resize` | |
| Q28 | Runner kill endpoint shape? | `POST /sessions/{id}/kill` | |
| Q29 | Runner list endpoint shape? | `GET /sessions` | |
| Q30 | Runner buffer endpoint shape? | `GET /sessions/{id}/buffer` | |
| Q31 | Runner health endpoint shape? | `GET /health` | |

### G. Event Model

| ID | Question | Recommended Default | Your Answer |
| --- | --- | --- | --- |
| Q32 | What events should runner emit first? | `SessionStarted`, `SessionOutput`, `SessionExited`, `SessionError`, `SessionHeartbeat` | |
| Q33 | Should output events include monotonic sequence numbers? | Yes | |
| Q34 | Should output events include raw ANSI chunks, rendered screen snapshots, or both? | Raw chunks for stream, snapshots on demand | |
| Q35 | Should backend republish runner events to existing SignalR event names? | Yes, keep frontend compatible | |

### H. Security And Scope

| ID | Question | Recommended Default | Your Answer |
| --- | --- | --- | --- |
| Q36 | Should runner bind only to localhost? | Yes | |
| Q37 | Should runner require a local shared secret/API key? | No for now | User answered: no for now. |
| Q38 | Should runner reject cwd outside configured workspace/repo roots? | No for now | User answered: no. |
| Q39 | Should runner own memory limits and process job objects? | Yes | |

### I. Testing And Acceptance

| ID | Question | Recommended Default | Your Answer |
| --- | --- | --- | --- |
| Q40 | Minimum E2E acceptance test? | Start session, stream output, restart backend/frontend, confirm session still accepts input and streams | |
| Q41 | Should we add a Playwright test for "session survives backend restart"? | Yes | |
| Q42 | Should we add runner contract tests without browser? | Yes | |
| Q43 | Should runner tests use raw shell first before Claude Code? | Yes | |
| Q44 | Should existing PTY library tests remain unchanged? | Yes | |

### J. Migration Plan

| ID | Question | Recommended Default | Your Answer |
| --- | --- | --- | --- |
| Q45 | Should we keep an in-process runtime fallback while building the runner? | No fallback. Remove backend in-process runtime from the production path. | User answered: no fallback. |
| Q46 | Should the first PR add runner project plus client interface, without moving orchestration? | No staged PRs. Do the split in one implementation pass. | User answered: all in one commit. |
| Q47 | Should the second PR move start/input/resize/kill behind the runner client? | Not applicable; included in the single split. | User answered: all in one commit. |
| Q48 | Should the third PR add backend restart survival E2E coverage? | Not applicable; tests included before commit. | User answered: tested before commit. |
| Q49 | When stable, should in-process runtime be removed or kept as test-only? | Remove backend runtime from production path as part of the split. | User answered: remove backend runtime. |

## User Decision Overrides

- Q16: Turn completion and all agent-specific logic belongs in the backend, not
  the runner. The runner should remain stable while backend agent logic can be
  restarted and iterated.
- Q37: Do not add a runner shared secret/API key for now.
- Q38: Do not enforce cwd allow-listing in the runner for now.
- Q45-Q49: Do not use a staged fallback migration. Implement the split in one
  pass, remove the backend in-process runtime from the production path, test it,
  and create one local commit. Do not push.

## Proposed First Implementation Slice

1. Add `Antiphon.SessionRunner` worker/minimal API project.
2. Move live PTY process ownership, input, resize, kill, ANSI logs, and output
   events into runner-side services.
3. Keep Claude/raw turn-completion and other agent-specific interpretation in
   backend services.
4. Add `ISessionRunnerClient` in backend application layer.
5. Add HTTP/SSE implementation in backend infrastructure.
6. Keep backend SignalR unchanged.
7. Add config:
   - `SessionRunner:BaseUrl`
   - `SessionRunner:Enabled`
8. Add restart script behavior:
   - `restart.ps1` restarts backend/frontend only.
   - `restart-runner.ps1` explicitly restarts runner.
9. Add tests for raw shell session survival across backend restart.

## Non-Goals For First Slice

- Multi-host runner fleet.
- Browser directly connecting to runner.
- Replacing SignalR.
- Kubernetes/service deployment.
- Preserving sessions across runner process restart.
- Moving board/workflow/orchestrator state out of the backend.
