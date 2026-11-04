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

## Test speed

- [ ] **Land tests fail on a machine with git older than 2.43.** `LandingGit.IdentityAsync` (and
  `LandOperationFactory`, `GuardedWorktreeRemoval`, `GuardedVerificationRemoval`) run
  `git show-ref --exists`, added in git 2.43. On the laptop (2.42.0) every land inspection is
  `source_ref_error`, so ~277 of 819 land tests in 34 classes failed on master; the desktop has 2.50.
  Laptop upgraded to 2.56.0 on 2026-11-03. Checking the floors on every host and entry point (not
  only in the Docker images) is CARD-1698.

From rc run `0b05506b` (rc-2026-11-04-f32aed42e58a, desktop, 2026-11-03). Chunk times match
earlier runs, so these are standing costs, not a regression. Logs:
`C:\Antiphon\releases\candidates\rc-2026-11-04-f32aed42e58a\logs\2026-11-03-1636-0b05506b\`.

- [x] **all-group-11, 1,484 s: `PostLandMutationDeliveryTests` (67 tests).** Done 2026-11-03:
  the class is on the git lane, the two cuts that start a worker child are in
  `PostLandMutationDeliveryWorkerTests` on the 1-wide process lane, and the 11 tests that repeated
  matrix rows are gone (a comment maps the CARD-0478 plan's PC-134..148 guards to their rows).
  `LandingGit` resolves the common dir from the `.git` entry instead of starting git (each mutating
  command's child journal did that) and memoises `check-ref-format` verdicts per instance. Laptop:
  56 tests in 107 s wall instead of ~9.6 min serially; a land-path test starts 152 git processes
  instead of 174. Left over:
  - Most of the remaining git calls are deliberate re-checks: `DestinationAsync` re-reads
    `remote get-url --push --all origin` and `symbolic-ref -q <target>` at every boundary (25 and
    12 per land). Batching them would need a decision on what may be read once per land.
  - The desktop is ~2.5× slower per test than the laptop for the same git-bound work
    (21 s vs 8.6 s). Check per-process spawn cost there (Defender exclusions on the rc checkout
    and temp roots).
- [ ] **Chunk timing shows `testCount: 0` for all-group-11.** Its `.timing.json` says
  `why: timing-read-refused`, although the TRX has 67 passed tests. Find out why the phase-timing
  read was refused, so slow chunks report their tests.
- [ ] **all-group-124, 1,126 s, 381 tests.** Looked into 2026-11-03: fully serial (sum of test
  time 1,130 s = wall). 29 of its 30 classes sit on the 1-wide `ProcessSpawnLimit` lane, but their
  only children are `pwsh` and git, not the ConPTY/fake-agent/session-runtime hazards that lane is
  for (CARD-0050). Only the ownership/timeout classes (ScriptHarnessCancellation,
  ScriptHarnessProcess, ScriptHarnessWindowsOwnership, NightlyNativeOwnership,
  ProcessSpawnTimeoutPolicy, ~169 s) need it. Fixes, largest first:
  - A 3–4 wide "pwsh script" lane like `GitProcessSpawnLimit` for the pwsh+git classes: est.
    ~1,120 s → ~350–450 s. `ScriptHarnessProcessContractTests.New_spawning_classes_take_process_limiter`
    accepts any `ParallelLimiter`.
  - `ReleaseGateFollowupTests.C1336_NightlyCandidateIdentity` is one 25-row test (147 s, 13% of the
    chunk): ~20 sequential `nightly-run.ps1` calls, five of them real runs at 13–16 s. Split it so it
    does not cap the parallel lane (~60–100 s more).
  - Its refusal/derived-id and master-isolation rows repeat `test-release-gate.ps1`'s
    `Test-C599_ParameterHops` and `Test-C599_MasterStateIsolation` (~40–60 s).
  - Every child pwsh reloads the nightly libraries; `test-phase-evidence.ps1` alone is ~0.85 s.
- [ ] **all-group-20, 1,088 s, 63 tests** (history 1,050–1,192 s). Not looked into yet.
- [ ] **all-group-126, 921 s, 115 tests.** Includes `ActualLoopFollowsSchedule` at 313 s.
- [ ] **all-group-125, 920 s, 213 tests.** Not looked into yet.
- [ ] **all-group-22, 830 s, 38 tests.** Includes `C1065_ClaimAndReplyInvalidateLegacyCandidate` at
  200 s.
- [ ] **`C1065_ReportPublicationPrecedesPhysicalRelease`, 427 s** (all-group-27), the slowest single
  test in the run.
