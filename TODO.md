# Outstanding work

A hand-maintained list of known-but-unfixed things, so they survive outside anyone's chat history.
Interim measure: the intent is for board cards to generate this automatically — see
[Card → repo task file sync](docs/superpowers/specs/2026-08-09-card-task-file-sync.md). Until that
ships, add items here by hand.

Each entry says what is wrong, how it shows up, and what a fix has to decide. Delete an entry when
it is done — git history is the record.

## Features

### Card → repo task file sync
Planned, not started. Full design in
[docs/superpowers/specs/2026-08-09-card-task-file-sync.md](docs/superpowers/specs/2026-08-09-card-task-file-sync.md).
First slice: `CardTaskFileService` + `CardTaskFileSyncHostedService` + manual sync endpoint +
integration tests. One-way, project repo only, no UI.

## Bugs

### Card identifiers are reused after a delete
`CardService.NextIdentifierAsync` (`server/Application/Services/CardService.cs:253-257`) is
count-based: `CARD-{count+1}`. Delete a card and the next one created takes the identifier that just
freed up, so `CARD-0007` can refer to two different cards over time. Anything keyed on the
identifier rather than the id — links, comments, task files, agent prompts — silently points at the
wrong card. Fix is max+1 over existing identifiers (parse and take the highest), not count+1.

### A numeric `modelLevel` is silently ignored on agent create
`POST /api/agents` with `"modelLevel": 0` returns **200 with an agent on `High`**, not `Frontier`.
`CreateAgentRequest.ModelLevel` is a nullable enum and the API serialises enums as *strings*, so a
numeric value fails to bind, lands as null, and takes the `High` default. Confirmed live 2026-08-09.

**What breaks if this is not fixed:** a script or integration that provisions agents believes it
created a Frontier (fable) agent and gets an Opus one. Nothing errors, nothing logs, and the only
symptom is the wrong model tier and the wrong bill — the response body reads `"modelLevel": "High"`
and a caller that does not diff it against what it sent will never notice. It also makes the API
inconsistent with itself, since `PATCH /api/agents/{id}` with `"modelLevel": "Frontier"` works fine.

**Fix:** reject an unbindable `modelLevel` with a 400 rather than defaulting. Silent coercion of a
value the caller explicitly supplied is the bug; the string-only enum is fine.

### Always-on cannot be set when an agent is created
`CreateAgentRequest` has no `alwaysOn` or `remoteControlEnabled` — only `UpdateAgentRequest` does. So
every supervised agent is a two-step: create, then PATCH. In the UI that is New Agent → fill → Create
→ kebab menu → Edit settings → toggle → Save, when the create dialog already collects model level and
assignment policy and could collect these too.

**What breaks if this is not fixed:** nothing corrupts, but there is a real window where the agent
exists **unsupervised** — if the process dies between create and the always-on PATCH, nothing
restarts it, which is precisely the failure always-on exists to prevent. Scripted provisioning needs
two round-trips and has to handle the second one failing, leaving a half-configured agent.

### E2E: session-runner port is ambiguous, and E2E targets the one nothing runs on
`Antiphon.AppHost/Program.cs` starts the runner on **17204** and overrides `SessionRunner__BaseUrl`
to match. `server/appsettings.json` defaults `SessionRunner:BaseUrl` to **17283**, and so does
`scripts/restart-session-runner.ps1 -Url`. The E2E fixture runs the server from plain appsettings and
starts no runner, so every session-dependent E2E test targets 17283 and fails ~30-60s later with
"did not reach Running status". `AntiphonAppFixture` now probes the URL and writes the verdict to
`notes.log`, but the underlying question is undecided: **which port is canonical?** Pick one, then
make appsettings, the AppHost override, and the restart script agree.

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
