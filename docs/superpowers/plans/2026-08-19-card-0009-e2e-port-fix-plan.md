# CARD-0009 — E2E session-runner port: plan

**Date:** 2026-08-19
**Status:** planned (not implemented)
**Card:** CARD-0009 (`549e971b-1cc6-4625-840e-5e9b3ce3de90`) — E2E targets the session-runner
port nothing runs on
**Incident:** 2026-08-08 (stray `node.exe` on 17283 → 404 instead of connection-refused);
2026-08-17 task `766dadbd` / CARD-0051 verify (`BoardE2ETests.Board_user_can_drag_backlog_card_to_in_progress_and_open_terminal_session` 404ed against 17283 because **our** Storybook was bound there)
**Already shipped:** `db7696d` moved Storybook to **17209**. That was CARD-0009's first fix
and is closed. Do not move Storybook again.

This is a planning document only. Do not write the fix in the Plan pass.

## Verdict

**17204 is canonical.** The fixture-startup probe is not enough signal. It only writes
`notes.log`; the 2026-08-17 miss happened *with the probe already in place*. Nobody reads
that file until the test has already spent 30–60 s failing as "did not reach Running status".

The actual remaining fix is to make every server that does not get the AppHost env override
talk to the always-on daemon on **17204** — the port `Antiphon.AppHost/Program.cs`,
`scripts/restart-session-runner.ps1`, `scripts/autostart-session-runner.ps1`, and the running
`Antiphon.SessionRunner.exe` already use. `server/appsettings.json` `SessionRunner:BaseUrl`
(`http://localhost:17283`) is the value the E2E fixture inherits, because
`AntiphonAppFixture` starts no runner and does not override that key.

17283 is not an E2E port. It is the leftover simple-mode runner port. `dev-start.ps1` does
not start a runner at all; the always-on Scheduled Task already serves 17204 whether Aspire
is up or not. Two runners (17204 + 17283) sharing `~/.claude` is already documented as
unsupported (`TranscriptClaimRegistry.cs:16`).

One Code+Docs slice.

## 1. Current shape (verified 2026-08-19)

### 1.1 Who picks the port

| Source | Port | Role |
|---|---|---|
| `Antiphon.AppHost/Program.cs:27-29, :50` | **17204** | Daemon `--urls` + `SessionRunner__BaseUrl` env override |
| `scripts/restart-session-runner.ps1:45` | **17204** | Canonical restart (no `-Url` parameter) |
| `scripts/autostart-session-runner.ps1` | **17204** | Logon Scheduled Task |
| `server/appsettings.json:46` | **17283** | Default the E2E fixture and a bare `dotnet run` inherit |
| `server/Application/Settings/SessionRunnerSettings.cs:5` | **17283** | C# fallback if config is missing |
| `appsettings.json.example:47` | **17283** | Shape reference; same lie |
| `restart-session-runner.ps1` (repo root) | **17283** | Old `dotnet dll` launcher; `restart.ps1 -RestartRunner` calls this |
| `verify-dev-stack.ps1:30` `-SimpleMode` | **17283** | Health-check URL only |
| `scripts/verify-agent-tui-profile.ps1:35` | **17283** | Script default |
| `client/package.json` `storybook` | **17209** | Already moved (`db7696d`) |

The card and `TODO.md` say "`scripts/restart-session-runner.ps1 -Url`" defaults to 17283.
That script has no `-Url`. The **root** `restart-session-runner.ps1` is the 17283 leftover.

### 1.2 What the E2E fixture actually does

`tests/Antiphon.E2E/Fixtures/AntiphonAppFixture.cs`:

- Starts Kestrel on a **random** port, Postgres testcontainer, no session-runner
  (`:121-128`).
- `KestrelWebApplicationFactory.ConfigureWebHost` (`:338-370`) sets connection string,
  git paths, Serilog, a raw `cmd.exe` agent definition — **not** `SessionRunner:BaseUrl`.
  That key comes from `server/appsettings.json` → 17283.
- `RecordSessionRunnerReachabilityAsync` (`:130-159`) GETs `{BaseUrl}/sessions` with a 3 s
  timeout and writes one `notes.log` line. It never fails the test, never changes the URL,
  never reaches `WaitForSessionRunningAsync`.

Session waits that still burn 30–60 s on a miss:

- `BoardE2ETests.WaitForSessionRunningAsync` (`:892`, 30 s) and the UI twin in
  `Board_user_can_drag_backlog_card_to_in_progress_and_open_terminal_session` (`:203`,
  30 s on "Session 1") — the 2026-08-17 failure.
- `ChannelE2ETests.WaitForSessionRunningAsync` (`:145`, 30 s).
- UI spawn in `BoardE2ETests` around `:335` waits 60 s for "Running".

`DelegationSequencingE2ETests` uses an in-process `TestSessionRunner` and is out of this
card.

### 1.3 Why the probe is not the fix

After `db7696d`, 17283 is usually empty, so the probe writes "unreachable" and the test
still waits the full deadline. A new squatter on 17283 (the 2026-08-08 shape) still turns
that into a 404. Pointing E2E at 17204 makes the happy path hit the process that is
actually running; consuming the probe then turns a miss into an immediate named failure
instead of a Running-status timeout.

## 2. Decisions

| Option | Decision | Why |
|---|---|---|
| Keep 17283 as appsettings default; only pin E2E | **Reject** | Bare `dotnet run` / simple-mode server still talk to a port nothing serves. The card: make appsettings, AppHost, and the restart script **agree**. |
| Start a session-runner inside `AntiphonAppFixture` | **Reject (this card)** | Separate TODO ("should the E2E fixture start a runner, or mark those tests as requiring one?"). Session-dependent E2E already intends to use the live daemon. |
| Fail every E2E at fixture init if 17204 is down | **Reject** | Most E2E tests do not need a runner. |
| Leave root `restart-session-runner.ps1` on 17283 | **Reject** | After the default flips, that script would start a second runner nobody's server uses (or, if retargeted naively to 17204, a `dotnet dll` process fighting the supervised daemon). |
| Keep AppHost `SessionRunner__BaseUrl` even though it will match appsettings | **Keep** | Explicit pin. A future appsettings drift must not silently retarget the Aspire server. |
| Storybook | **Already done** | `db7696d`. Do not touch `client/package.json`. |

Simple-mode **API 17281 / Vite 17282 stay**. Only the session-runner row of simple-mode
becomes 17204 — the daemon that is already always-on.

## 3. The slice (one Code+Docs)

### 3.1 Canonical default is 17204

Set `SessionRunner:BaseUrl` / `BaseUrl` to `http://localhost:17204` in:

- `server/appsettings.json`
- `server/Application/Settings/SessionRunnerSettings.cs`
- `appsettings.json.example` (shape reference; AGENTS.md forbids copying it over the
  tracked file, but it must not keep teaching 17283)

AppHost override stays `http://localhost:17204`.

### 3.2 E2E fixture: pin + consume the probe

`AntiphonAppFixture.KestrelWebApplicationFactory` in-memory collection: add
`["SessionRunner:BaseUrl"] = "http://localhost:17204"`. That is the E2E-side pin so a
future appsettings edit cannot silently send BoardE2E back to 17283.

Keep `RecordSessionRunnerReachabilityAsync`. Additionally:

- Store `SessionRunnerReachable` + the verdict string on the fixture.
- Add `EnsureSessionRunnerReachable()` that throws immediately with that verdict
  (URL, status or exception) when `/sessions` was not 2xx.
- Call it at the top of both `WaitForSessionRunningAsync` copies and at the start of
  every BoardE2E test that launches a session **without** that helper — at least
  `Board_user_can_drag_backlog_card_to_in_progress_and_open_terminal_session` (the
  2026-08-17 case; it waits on the Playwright "Session 1" locator, not the helper)
  and the UI-Spawn test that waits 60 s for "Running".

Do not start a runner. Do not skip. A miss is a fail with the probe text, in seconds.

### 3.3 Restart / verify scripts that still say 17283

- **Root `restart-session-runner.ps1`:** stop launching `dotnet dll` on 17283. Make it a
  thin forwarder to `scripts/restart-session-runner.ps1` (the supervised 17204 daemon).
  Do not retarget the old `dotnet dll` launcher onto 17204 — that fights the Scheduled
  Task.
- **`restart.ps1 -RestartRunner`:** call `scripts/restart-session-runner.ps1` (or the
  forwarder). Drop the assumption that simple-mode owns a private 17283 runner.
- **`verify-dev-stack.ps1 -SimpleMode`:** `$RunnerUrl = "http://localhost:17204"`. Simple
  core ports stay 17280/17281/17282; the runner is the always-on daemon.
- **`scripts/verify-agent-tui-profile.ps1`:** default `$SessionRunnerUrl` to
  `http://localhost:17204`.

### 3.4 Docs that currently leave the question open

- `CLAUDE.md` gotcha "Two session-runner ports exist…": rewrite as **17204 is
  canonical**; E2E pins it; Storybook is 17209 (`db7696d`); the probe now fails
  session-dependent tests immediately. Keep the 2026-08-08 404 story as the why.
- `TODO.md` bullet "E2E: session-runner port is ambiguous…": delete. The card is the
  record. Leave the sibling "should the fixture start a runner" product-decision
  bullet — that is not this slice.
- Simple-mode tables that list session-runner as 17283: `AGENTS.md` (simple-mode
  fallback line), `docs/bootstrap.md` port table, `.claude/skills/antiphon-run/SKILL.md`
  simple-mode table + the `17280,17281,17282,17283` listen check. API/client stay
  17281/17282; runner row becomes 17204.
- `TranscriptClaimRegistry` comment about a 17283 runner beside 17204: leave it. It is
  a warning about a shape we are retiring, not a second default.

### 3.5 Test

New cheap pin, **not** a headed BoardE2E run as the only proof:

`SessionRunnerSettings.BaseUrl` defaults to `http://localhost:17204`.

A one-liner next to whatever already constructs a default `SessionRunnerSettings` is
enough; do not spin `AntiphonAppFixture` (Postgres testcontainer) just to assert a
string. Session-dependent E2E still requires the live 17204 daemon — that is the
intended contract, not a new one.

Existing Board/Channel E2E session tests stay. Do not widen their 30/60 s timeouts.

## 4. Out of scope

- Starting a session-runner inside the E2E fixture, or `[Explicit]`/`Skip` on
  session-dependent tests (the TODO product decision).
- Decommissioning simple-mode API/client ports 17281/17282.
- `DelegationSequencingE2ETests.TestSessionRunner`.
- Touching Storybook / 17209.
- Closing the card. This plan lands; a Code slice implements.

## 5. What the Code agent runs

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0009/ -- --treenode-filter "/*/*/*SessionRunnerSettings*"
```

If no existing class is a natural home, put the default pin on a tiny new test class
and filter that instead. Forward slash on `OutputPath`. Delete the `bin-card0009/`
directories after.

Grep the runtime/config surface after the edit (`*.json`, `*.cs`, `*.ps1`) and confirm
the remaining `17283` hits are docs history, the claim-registry warning, or simple-mode
API/client (17281/17282) — not `SessionRunner:BaseUrl`.

A headed `BoardE2ETests` run is optional confirmation when 17204 is listening; it is
not the slice's unit of proof. Do not treat a red BoardE2E as this slice if the probe
now says the daemon is down.

## 6. Commit

`fix(e2e): CARD-0009 - default SessionRunner:BaseUrl to 17204 and fail fast when it is not a runner`
