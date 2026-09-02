# Outstanding work

A hand-maintained leftover of known-but-unfixed things, so they survive outside anyone's chat
history. The generated board list is
[docs/cards/antiphon/INDEX.md](docs/cards/antiphon/INDEX.md) (CARD-0004). Edit the card
(`scripts/card.ps1`), not those files. Items below stay here until they are themselves cards and
this file is retired.

Each entry says what is wrong, how it shows up, and what a fix has to decide. Delete an entry when
it is done — git history is the record.

## Features

### Card → repo task file sync
Planned, not started. Full design in
[docs/superpowers/specs/2026-08-09-card-task-file-sync.md](docs/superpowers/specs/2026-08-09-card-task-file-sync.md).
First slice: `CardTaskFileService` + `CardTaskFileSyncHostedService` + manual sync endpoint +
integration tests. One-way, project repo only, no UI.

## Bugs

### A session can adopt ANOTHER session's transcript — including a human's own conversation
Observed live 2026-08-09. `session-runner.log`:

```
WRN Session 18c04655-...: <session-id>.jsonl never appeared (Claude forked the id);
    adopting discovered transcript C:\Users\lndco\.claude\projects\C--src-Antiphon\37512455-...jsonl
INF Tailing transcript ...\37512455-....jsonl for session 18c04655-...
```

`37512455-…` was the **operator's own Claude Code conversation**, not the agent's. Claude's transcript
directory is per working directory (`~/.claude/projects/<slugged-cwd>/`), so when an agent's own
`<session-id>.jsonl` does not appear in the discovery window, the fallback picks another file from
that folder — and the most-recently-written one belongs to whichever session is busiest, which is
typically a human actively working in the same checkout.

**Trigger:** launch an agent into a directory that already hosts another live Claude session, where
the agent's own jsonl never lands. Here the launch 500'd (`ClearLiveBufferAsync` →
`PtyHostClient.SendAsync`, `SessionRunnerRuntime.cs:626`) and the session died before writing it.
Three Antiphon agents plus a human session all share `C:/src/Antiphon`, so the collision window is
wide open in normal use.

**What breaks if this is not fixed:** the agent ingests someone else's conversation as its own.
Confirmed effects — the brand-new orchestrator immediately reported **65 agent-touched files** that
were actually the operator's edits (including files in a worktree it had never opened); after a clean
relaunch onto its own transcript the same query returns **0**. Working/idle is then computed from a
stranger's turns, so WhenIdle deliveries fire at the wrong moments. Worst case is a channel-bound
agent: reply dispatch relays the *other* session's turn text to Telegram, i.e. an unrelated private
conversation gets sent to a chat. Nothing warns beyond one WRN line.

**Fix direction:** never adopt a transcript another live session is already tailing, and reject any
candidate whose first record predates this session's launch — a genuine transcript for a new session
cannot start before the session did. Failing both checks, run without a transcript and raise an
incident rather than silently binding to the wrong one.

### Always-on cannot be set when an agent is created
`CreateAgentRequest` has no `alwaysOn` or `remoteControlEnabled` — only `UpdateAgentRequest` does. So
every supervised agent is a two-step: create, then PATCH. In the UI that is New Agent → fill → Create
→ kebab menu → Edit settings → toggle → Save, when the create dialog already collects model level and
assignment policy and could collect these too.

**What breaks if this is not fixed:** nothing corrupts, but there is a real window where the agent
exists **unsupervised** — if the process dies between create and the always-on PATCH, nothing
restarts it, which is precisely the failure always-on exists to prevent. Scripted provisioning needs
two round-trips and has to handle the second one failing, leaving a half-configured agent.

### E2E failures needing a product decision (13)
Not flakes — each needs a call, not a retry:
- Session-dependent tests: should the E2E fixture start a session runner, or should those tests be
  marked as requiring one?
- `WorkflowDeleteTests`: needs credentials unless it sets `UseMockExecutor`. Should it?
- `Agents_page_creates_agent_and_assigns_card_to_queue`: drives UI removed in `7dd825e`. Rewrite
  against the current UI or delete.

## Reliability

### Nothing supervises the AppHost
The "Antiphon AppHost" scheduled task only fires at logon. When the AppHost dies mid-session nothing
restarts it, so `https://antiphon.desktop.codeperf.net/` (Caddy → `host.docker.internal:17203`, the
Vite dev server) stays dead until someone notices. Observed 2026-08-08 21:00 → 2026-08-09 00:20, with
thousands of `connection refused` entries in the Caddy log while a browser tab retried. Caddy itself
was healthy throughout. Needs either a supervisor loop or a health-triggered restart.

### Fake gateway logs every health poll at Information
`logs/fake-gateway.log` reached 50 MB and grows roughly 90 MB/day: each `GET /health` writes three
`Information` lines (`EndpointMiddleware`, `OkObjectResult`, `Hosting.Diagnostics`). Turn the level
down for that endpoint in the fake gateway's Serilog config.

## Test hygiene

### `OrchestratorServiceIntegrationTests` asserts global counts
Asserts on `result.SkippedGlobalConcurrency` / `Dispatched` totals. Same shape as the three flakes
already fixed: every test in the assembly shares one Postgres testcontainer, so an unscoped total
also asserts "no other test has data right now". Has not flaked yet, but it is the same trap. Scope
the assertions to rows the test created. See the shared-Postgres rule in CLAUDE.md.

### `AgentsPage.test.tsx` is load-flaky
Fails intermittently in a full-suite run, passes reliably in isolation (checked repeatedly:
19/20 then 20/20, 20/20). Timing-sensitive waits under parallel load rather than a real defect, but
it costs a re-run every time.

### `ClaudeAdapterLocalShellTests.Send_prompt_clears_live_buffer_before_send` is PTY-timing flaky
Fails under full parallel load, passes 4/4 in isolation. Documented in CLAUDE.md as a known class of
flake for PTY tests; worth making deterministic rather than living with it.
