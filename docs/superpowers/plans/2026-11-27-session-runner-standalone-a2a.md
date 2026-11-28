# SessionRunner standalone mode with an A2A endpoint

Date: 2026-11-27. Owner request: "make sessionrunner be able to run in standalone mode rather than phone home mode, so it can
be controlled directly, preferably through a common interface such as Agent2Agent".

## What is already true

- The runner binary has no mode switch. With `PhoneHome:Enabled=false` (the shipped default) it is a plain loopback HTTP
  service that needs no AppHost, server, Postgres, Kafka or registration (`Program.cs`, `appsettings.json`). The AppHost
  and `autostart-session-runner` start exactly that on 17204.
- Its HTTP API drives raw pty sessions: `POST /sessions`, raw `POST /sessions/{id}/input`, transcript and `/events` SSE.
- The Codex turn logic (readiness, verified submit, turn end) lives in the server (`RunnerCodexAdapter`,
  `CodexSubmitConfirmation`), not the runner. A direct caller of the raw API has to reimplement it.

So "standalone" already exists; what is missing is a turn-level control surface a generic client can use.

## Decision

Add an optional A2A (Agent2Agent protocol v1.0) endpoint to the runner, backed by headless Codex turns.

- Protocol: A2A JSON-RPC binding plus the well-known agent card, through the official .NET SDK
  (`A2A.AspNetCore` 1.0.0-preview4, a2aproject/a2a-dotnet). Microsoft Agent Framework's A2A client
  (`Microsoft.Agents.AI.A2A`) and its A2A hosting are built on the same SDK, so MAF agents, the A2A inspector and other
  A2A clients can call it.
- Backend: one Codex thread per A2A context, one `codex exec --json` turn per A2A task, later turns through
  `codex exec resume <thread> --json`. Codex reports the thread id, items and turn end as JSON lines, so no screen
  scraping or readiness heuristics are needed. The pty session API is unchanged.
- Off by default: `SessionRunner:A2A:Enabled=false`. Nothing changes for the desktop, server2 or phone-home runners.

Rejected: porting the server's Codex pty adapter into the runner (large, timing-sensitive, and the server copy keeps
evolving); a separate gateway process over the raw HTTP API (a second process to supervise and the same turn problem).

## Behaviour

| A2A | Runner |
|---|---|
| `SendMessage` / `SendStreamingMessage`, new context | `codex exec --json -` in the context's working directory, prompt on stdin |
| same, existing context | `codex exec resume <threadId> --json -` |
| message metadata `antiphon.codex.threadId` on a new context | binds the context to an existing Codex thread |
| message metadata `antiphon.cwd` on a new context | working directory, must be under `WorkspaceRoot` |
| `item.completed` agent_message (not last) / command / file change | `working` status update with a text summary |
| `turn.completed` | artifact `response` with the last agent message, then `completed`; metadata carries thread id and usage |
| `turn.failed`, `error`, non-zero exit, no turn end, timeout | `failed` with the reason (stderr tail bounded to 4 KB) |
| `CancelTask` | kills the turn's process tree, then `canceled` |
| second turn in a busy context, or over `MaxConcurrentTurns` | `rejected` |

Access: the endpoint answers only loopback callers unless `BearerTokenFile` is set; with a token every A2A request,
loopback included, must carry `Authorization: Bearer <token>`. Contexts live in memory; a restarted runner forgets them,
and a client can rebind with `antiphon.codex.threadId`.

Settings (`SessionRunner:A2A`): `Enabled`, `Path` (`/a2a`), `PublicUrl`, `WorkspaceRoot` (required), `Sandbox`
(`read-only` default, or `workspace-write`, `danger-full-access`), `Model`, `CodexExecutable` (`codex`), `CodexHome`,
`MaxConcurrentTurns` (2), `TurnTimeoutMinutes` (30), `BearerTokenFile`.

## Steps

1. Settings, JSON-line parser, argument builder and process seam (`src/Antiphon.SessionRunner/A2A`).
2. `CodexA2AAgent : IAgentHandler`, context map, cancel registry; registration and route mapping in `Program.cs`.
3. Tests in `tests/Antiphon.SessionRunner.Tests/A2A`, in process, with a fake process seam (no codex, no network):
   parser, arguments, first and resumed turn through the SDK `A2AClient`, streaming, failure, cancel, busy, access
   guard, agent card.
4. Review the diff, then run the selector classes plus the gate set.
5. Live check on the laptop: the built runner on a scratch port with scratch log paths, real Codex (ChatGPT plan,
   approved), a JSON-RPC call by hand and a Microsoft Agent Framework `A2AAgent` client from a scratch project:
   first turn, resumed turn that remembers the first, cancel.
6. Docs: a "Standalone runner with A2A" section in `docs/docker-stack.md`.

## Outcome (2026-11-27)

Built as planned, plus what an independent review of the first draft found:

- CancelTask on a blocking SendMessage: the SDK cancels only detached runs and then waits for the task's execution lock,
  which the turn holds. `CodexA2ARequestHandler` (an `A2AServer` subclass) stops the turn first; the turn records
  Canceled itself, and falls back to the task store when the SDK has already closed a disconnected client's queue.
- Leftover children: Codex runs in a kill-on-close Job Object; reading stops at `turn.completed`/`turn.failed`, and the
  stderr wait is bounded, so a background command holding a pipe cannot hold a turn slot.
- Access: no token means direct loopback only (no forwarding headers, loopback Host); JSON-RPC needs
  `application/json`; the card names no local path. `antiphon.codex.threadId` is off unless `AllowThreadAttach`.
  `antiphon.cwd` refuses junctions and symlinks below the root. Idle contexts are capped (`MaxContexts`).

Verified on the laptop with the built runner on 17291 and real Codex 0.172.0: the card; a first turn that ran a command
and read a file; a resumed turn that remembered it; SSE streaming with progress; CancelTask on a blocking send returning
Canceled at once with the started `ping` gone; the 415 and 403 refusals; and a Microsoft Agent Framework client
(`A2AClient(...).AsAIAgent()`, one `AgentSession`, two runs that share the thread). Codex on Windows refuses every
command outside `danger-full-access` (no Windows sandbox yet); see `docs/docker-stack.md`.

## Follow-ups (not in this change)

- A `start-standalone-runner` command with a supervised, detached launch.
- Claude and Grok backends behind the same handler (`claude -p --output-format stream-json --resume`).
- Durable context map and task store, so contexts survive a restart.
- Pty-backed A2A sessions, once the Codex turn logic moves from the server into a shared library.
