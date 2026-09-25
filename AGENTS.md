# Antiphon agent context

AGENTS.md is the universal index and mandatory safety core for this repository. Each linked living document owns its detailed behaviour; read the owner before changing that area. CLAUDE.md is the one-line Claude Code import pointer to this file. Its supported-import and portability contract is in [docs/agent-instruction-file-contract.md](docs/agent-instruction-file-contract.md).

## Read before changing

| Change area | Required owner |
|---|---|
| Backend/domain/client conventions, layers, naming, errors, configuration | [docs/project-context.md](docs/project-context.md) |
| Telegram/channel formatting and gateway settings | [docs/telegram.md](docs/telegram.md) |
| Cards, delegates, landing, scopes, and tracker orchestration | [docs/orchestration-loop.md](docs/orchestration-loop.md) |
| Card state versus session state and decision questions | [docs/agent-card-lifecycle.md](docs/agent-card-lifecycle.md) |
| Inspecting agents, boards, and live sessions over HTTP | [docs/ops-http.md](docs/ops-http.md); full route map [docs/antiphon-api.md](docs/antiphon-api.md) |
| Retrieving desktop, runner, Hangfire, remote, and transcript logs | [docs/logs.md](docs/logs.md) |
| Workflow tracker configuration | [docs/workflow-tracker-block.md](docs/workflow-tracker-block.md) |
| Agent kinds, provider settings, remote control, and Codex test isolation | [docs/agent-kinds.md](docs/agent-kinds.md), [docs/ai-agent-tui-configuration.md](docs/ai-agent-tui-configuration.md) |
| Secrets, keys, and configuration custody | [docs/agent-credentials.md](docs/agent-credentials.md) |
| Herdr panes and delivery | [docs/herdr-sessions.md](docs/herdr-sessions.md) |
| Session, transcript, launch, reconciliation, and delivery invariants | [docs/session-runtime-invariants.md](docs/session-runtime-invariants.md) |
| Pty backend architecture and evidence | [docs/adr/0002-modern-conpty-backend.md](docs/adr/0002-modern-conpty-backend.md) |
| Bootstrap, AppHost, Docker, ports, logging, scheduled tasks, and recovery | [docs/bootstrap.md](docs/bootstrap.md) |
| Tests, builds, E2E diagnostics, and test-time process safety | [docs/testing-and-build.md](docs/testing-and-build.md) |
| Nightly backstop, watchdog and qualification | [docs/nightly-watchdog.md](docs/nightly-watchdog.md), [docs/testing-and-build.md](docs/testing-and-build.md) (Nightly) |
| Release candidates, RC coverage profile, published releases, Interim activation | [docs/release-gates.md](docs/release-gates.md) |
| Real browser, vault relay, per-site notes, and Outlook work | [docs/external-site-operations.md](docs/external-site-operations.md) |

## Essential front doors

- [Canonical local restart](docs/apphost-runbook.md): use pwsh -NoProfile -File scripts/restart-apphost.ps1 to restart the AppHost; read the Job Object / current-checkout caveat first. The restart asks the server to stop gracefully first (CARD-0716). Never run a second dev-aspire.ps1; exit 3 is a refusal, so inspect the launch/restart locks before retrying.
- Verify the standard local stack with pwsh -File verify-dev-stack.ps1 -SkipBrowser. Aspire uses server 17202, built client 17203, runner 17204, and dashboard 17205. Port 17204 is the production runner; E2E owns an isolated random runner instead.
- Under Aspire, 17203 serves the built bundle. Wait for its watcher to rebuild (client-mode.ps1 -Status) before treating a browser observation as current; use client-mode.ps1 -Mode dev only when HMR is required.
- For a new local stack, follow [docs/bootstrap.md](docs/bootstrap.md). Use only docker compose -f docker-compose.dev.yml up -d; do not delete antiphon_pgdata unless the database is deliberately being recreated.
- Use scripts/card.ps1 for board work. Give long card text through -DescriptionFile or -ReasonFile; its default fresh concurrency-token read is the normal write path. A move starts no agent unless -Spawn is supplied.
- To list or inspect agents, boards and live sessions, read [docs/ops-http.md](docs/ops-http.md) instead of grepping MapGet. The server is 17202 /api/...; the runner is 17204 /sessions/... with no /api. There is no GET /api/sessions and no /api/board, and GET /api/cards needs one of boardId, status, or updatedSince.

## Immediate safety triggers

### Local stack

- Before killing a process or changing a port, establish ownership. A stale foreign dcpctrl.exe on 17202, a racing AppHost restart, and HNS/Docker failure have different recoveries; use the bootstrap operations guide rather than guessing from a podman-looking error.
- Restart and deploy scripts refuse by default when run from a linked Git worktree instead of the main checkout (exit 3) — they control the shared local ports and would otherwise silently replace the canonical stack. Re-run from the main checkout; `-AllowWorktree` is an explicit, rarely-needed override.
- `restart-apphost.ps1` builds from whatever is on disk in the main checkout; it does not fetch or pull. Pushing straight to `origin/master` from a worktree (a `-Land` workaround) never advances the main checkout's own `master` — the next restart then silently serves stale code while reporting healthy (CARD-0358; see the bootstrap doc's last gotcha). After any out-of-band push, `git pull --rebase` in the main checkout before restarting. Exit 0 now requires dashboard + `/health` + `/api/version` SHA equal to source-root HEAD (or `-ExpectedServerSha`); a mismatch is exit 5 with the restart lock retained. After a restart that matters, confirm `GET /api/version` (SHA and `land-v2`) rather than trusting a passing health check. A land confirms publication, not activation — see the Post-land server activation check in [docs/orchestration-loop.md](docs/orchestration-loop.md). More generally: check an assumption about running state directly and immediately — checkout HEAD, whether a restart picked up a change, whether a service is authenticated — never infer it from a downstream signal like "healthy" or "no errors logged."
- Keep filesystem paths in Windows configuration in backslash form. Do not hard-code the live messaging broker in AppHost code or send fake-gateway traffic to a live broker.
- Daemon and auto-start PowerShell scripts must stay ASCII-only for Windows PowerShell 5.1 fallback.

### Tests and builds

- Run TUnit with dotnet run --project tests/<ProjectName>, not dotnet test; run Antiphon.Tests and Antiphon.Agents.Pty.Tests sequentially. Use pwsh -File scripts/test-client.ps1 for Vitest, and rebuild client/dist before E2E. Process-spawning tests, including SessionRunner pty-host and direct-process tests, carry an assembly-local ParallelLimiter<ProcessSpawnLimit> (one limiter per test project, not shared across assemblies).
- A Code stage runs the plan's `### Checkpoints` table as a closed list: one isolated build and one exact filter per row, reported per CP-n with counts; unlisted builds/test runs need a stated reason, and a new test that cannot go red is a stub. Owner: [docs/testing-and-build.md](docs/testing-and-build.md) (Checkpoint manifest, CARD-0585).
- Every build or test driver takes a host build slot from the session runner (CARD-0589): run-checkpoint.ps1 does it itself, anything else runs as pwsh -NoProfile -File scripts/build-slot.ps1 -Label <what> -- <command>. The wait prints BUILD SLOT lines; exit 4 is a slot timeout to report, never a reason to run unleased. Owner: [docs/testing-and-build.md](docs/testing-and-build.md) (Build slots).
- Overnight build+test is Windmill `u/lndcobra/antiphon_nightly_tests` (scripts/nightly-run.ps1 against C:\Antiphon\nightly\checkout). Do not add a local Scheduled Task.
- Do not hand-quote arbitrary alternate OutputPath values. Read the testing/build guide for the current isolated-output, headed-test, test-clock, shared-Postgres, and slow-build procedures.
- A test host that boots real Program must never launch against the production runner. Use the established guard or an isolated runner.

### Sessions and pty

- Treat transcript-confirmed UserPrompt evidence as the delivery verdict; screen redraws, sidecar guesses, and Herdr events are not proof. Pull a transcript before acting on its absence.
- A process release must leave it killed, pooled warm, or owned by a standing agent; the dispatcher's pool-release sweep enforces it for pool delegates, and no terminal writer may rely on being the only release. A stall is a detection/decision state, never an automatic kill.
- CARD-0079 is the only automatic stop of a Working session: an AlwaysOn Claude Check seat whose explicit `(auto)` CompactBoundary and synthetic continuation have been silent for `Delegation:CheckCompactionContinuationWaitMinutes` (default 10, zero disables). It uses the conditional runner stop and a strict same-conversation resume, then waits for a new useful Check and a whole caller receipt. It does not change the stall rule for any other turn.
- Preserve the session delivery contract: LF plus bracketed paste plus a separate Enter for multi-line input. Read the session and Pty owners before changing transcript, terminal, launch, compaction, reconciliation, or remote-control behaviour.

### Cards and tracker

- Orchestrators delegate the reading: a session that dispatches delegates sends one for how something works and takes its answer, reading directly only what it must quote exactly or judge personally — even when it looks one grep away, even to another frontier-tier agent. A delegate reads its own files and never sub-delegates. Owner: [docs/orchestration-loop.md](docs/orchestration-loop.md) §0.
- Pipeline stages (Investigate/Plan/TestDesign/Code/Mutation/Review) are `AgentTaskRole` values, not a separate concept; every stage report ends with the `--- next stage ---` block (`next:`/`handoff:`/`artifact:`), and the orchestrator dispatches the named next stage from the completion header's `next=`, never by re-reading the report body (CARD-0146).
- An orchestrator working a board runs one task per pipeline stage with the stages in parallel, each in its own worktree, and never two tasks in the same stage; keeps the Code stage at a depth of two by pulling the next Backlog card; lands as soon as a stage is confirmed; files a card for every structural defect the moment it is found; and passes `-IgnoreConcurrencyLimit` only for an `axis: absolute` 409 with no same-stage occupant — a same-stage collision, or a Code task touching the same source area as one already in flight, defers instead. Owner: [docs/orchestration-loop.md](docs/orchestration-loop.md) §1 (CARD-0533).
- Scope is a comma-separated list of area names and/or path globs. Unknown areas warn rather than reject; scope drift is recorded at settlement. Extend antiphon.areas.json only for a real collision and avoid leading filename wildcards.
- A decision belongs on the card move/reopen revision and attention feed, never a new column or an alert sink. CARD-nnnn is board-scoped; #N means only CARD-000N.
- A delegated-work listing needs an explicit `projectId`/`boardId`; an unscoped list is the whole fleet.
- Files under `docs/cards/` are generated from the board (CARD-0004); edit the card, not the file.
- External ChatGPT/Codex dispatch uses a named Delegation Capability (`delegate.ps1 -Capability`); issue/rotate/revoke is `scripts/capability.ps1`. Do not edit `Delegation:AllowedRoots` for this. Owner: [docs/ops-http.md](docs/ops-http.md).
- Subscription-quota 409 is a launch refusal. Choose another allowed agent or explicitly use the documented override; never silently reroute. A 409 `provider_sign_in_required` is the same shape for a Grok pool whose `GROK_HOME` has no usable session: pick another kind, run `grok login`, or pass `allowUnauthenticatedProvider` (`delegate.ps1 -AllowUnauthenticatedProvider`); never silently reroute. Tracker writes are explicit, YAML-activated actions, not orchestration-tick side effects. A scheduled card action with `Release`/`Spawn` is a scheduled spend; it needs `acceptSpend` and is previewed first. Settlement commits a Shared task's own footprint through a gated primitive; `-NoCommit` is the only opt-out an instruction can carry.

### External tools and secrets

- For real external sites, use the shared CDP browser lane and read the site note first. Prefer real clicks/keystrokes, clear blocking consent UI, and update the per-site note after learning a durable quirk.
- Never print, echo, log, or manually paste a secret. Use the approved vault relay/fill path. Do not expose mail credentials or a user's Codex home; Codex rollout deletion goes through its CLI.

<!-- AGENTS-CORE-END: CARD-0254 -->
