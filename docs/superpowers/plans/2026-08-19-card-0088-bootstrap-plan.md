# CARD-0088 — Bootstrapping a brand-new Antiphon: plan

**Date:** 2026-08-19
**Status:** planned (revised; supersedes the 2e12550 pass)
**Source evidence:** CARD-0088's investigation, re-verified against the repo and this
machine on 2026-08-19. Corrections below where the card or the first plan pass is
sharper than the files, or the files are sharper than either.

This is a planning document only. Do not write `docs/bootstrap.md` or
`scripts/bootstrap-check.ps1` in the Plan pass — those are S1/S2 execution.

## Verdict up front

**Checklist doc + a thin self-check that wraps what already exists. No new
provisioning script. No Antiphon-owned memory subsystem.**

The first plan pass (2e12550) got the three card questions right. Independent
re-verification found that pass under-specified the execution: it invented
`pg_dump` prose and a from-scratch health script on top of scripts that already
exist, pointed secrets at the wrong overlay, and left a Code worker to re-decide
the dual-stack port map, the tracked machine-specific paths, and which memories
are already promoted. This revision closes those. Three slices, ~1½–2 days, no
further design decisions.

| Question | Answer |
|---|---|
| 1. Checklist vs provisioning script vs both | **Checklist.** Every automatable step is already a tracked script. A wrapper that shells out to them would rot. |
| 2. Auto-memory loss on a fresh environment | **Acceptable at the tool level.** Do not build a durable memory store. Promote the few Antiphon-operational facts that still live only in memory. |
| 3. Bootstrap self-check | **Yes, small, read-only.** Extend around `verify-dev-stack.ps1`; do not duplicate it. |

## Ground truth the first plan pass missed or got wrong

Verified 2026-08-19 against the files, not inferred from the card.

1. **Two local stacks are first-class, and the docs disagree on which is
   "first time".** `.claude/settings.json` names both: Aspire (17202/17203/17204)
   and "Local (Docker Compose)" (17281/17282). `install-autostart.ps1`,
   CLAUDE.md, and `verify-dev-stack.ps1` (default) treat Aspire as canonical.
   `AGENTS.md` "Canonical local restart" and `antiphon-run`'s "First time" /
   "Every day" still lead with `dev-start.ps1` / `restart.ps1` on 1728x.
   **S1 picks Aspire as the bootstrap path.** Simple mode stays documented as
   the fallback, one subsection, not the default.

2. **`verify-dev-stack.ps1` already exists** (repo root). Docker daemon, HNS,
   `antiphon-postgres`, listening ports, `/health`, frontend proxy, SignalR
   negotiate, optional Playwright. Defaults to Aspire; `-SimpleMode` is 1728x.
   S2 must call this, not reimplement it.

3. **`dev-backup.ps1` / `dev-restore.ps1` / `dev-fresh.ps1` already exist.**
   The first plan's "document `docker exec … pg_dump` as checklist prose" and
   "automated backups are a separate card" ignore them. S1 points at these
   scripts. Recurring scheduling of `dev-backup.ps1` stays out of scope.

4. **Secrets do not live where the first plan said.**
   - `server/Antiphon.Server.csproj` already has
     `<UserSecretsId>antiphon-server</UserSecretsId>`.
     `DatabaseSeeder.SyncProviderConfigAsync` copies a non-empty
     `Llm:Providers:*:ApiKey` from config (including user-secrets) into the DB.
   - On this machine: `dotnet user-secrets list --id antiphon-server` is empty;
     `server/appsettings.Development.json` does not exist;
     gitignored `server/appsettings.Production.json` is 248 bytes of
     `ChannelBridge` + `AntiphonMessaging` only (no API keys).
   - So today's LLM keys live **in the database** (Settings UI), which is why
     they die with `antiphon_pgdata`. Agent-TUI wrapper auth lives in
     `~/.claude` / `~/.grok/auth.json`. Managed TUI secrets use the Data
     Protection ring at `%LOCALAPPDATA%\Antiphon\DataProtection-Keys`
     (`docs/ai-agent-tui-configuration.md`).
   - **Preferred overlay for a new machine: `dotnet user-secrets set` against
     `antiphon-server`.** Also accepted: a gitignored
     `server/appsettings.Development.json`. Never the tracked
     `server/appsettings.json`.

5. **`appsettings.json.example` is at the repo root, stale, and the
   first-time instructions name a file that does not exist.**
   - AGENTS.md: "Copy `appsettings.json.example` to `server/appsettings.json`"
     — that overwrites a *tracked* file.
   - `antiphon-start`: "Copy `server/appsettings.json.example`" — that path
     does not exist.
   - The example still has `Llm:Providers` as an **array** and
     `DefaultConnection` with no `Port=17280`. Live
     `LlmSettings.Providers` is a `Dictionary<string, LlmProviderSettings>`.
   - Tracked `server/appsettings.json` also carries **this-machine leftovers**:
     `Git:WorkspacePath = D:\\src\\Antiphon\\workspace` (this checkout is
     `C:\src\Antiphon`; code default is `"work"`),
     `Git:DefaultBranch = main` (the branch is `master`),
     `Delegation:CheckInterpreterWorkingDirectory = C:\\logs\\antiphon\\check-interpreter`.
   - Tracked `src/Antiphon.SessionRunner/appsettings.json` has
     `SessionLogPath = C:\\logs\\antiphon\\session-runner` (Windows convention,
     document `mkdir`, do not "fix" it).

6. **The tracked skills a fresh clone *would* have are themselves stale.**
   `antiphon-run` still lists Postgres on Aspire-managed 17201, first-time =
   `dev-start.ps1`, backups at `C:\Antiphon\backups\` (the script defaults to
   `<repo>/backups/`). `antiphon-start` still says dashboard URL is dynamic
   (pinned to 17205) and copies the missing `server/appsettings.json.example`.
   A checklist that does not update these will be contradicted by the first
   skill an agent loads.

7. **`~/.claude` is a git checkout of `claude-home`**
   (`origin` = `https://github.com/michal-ciechan/claude-home.git`). That
   repo is this operator's, not public. A new operator cannot clone it. Gaps
   1–3 in the card (global CLAUDE.md + `@`-imports, user-level skills, the
   memory store) already survive *this operator's* new machine via `sync`;
   they do not survive a different user. Memory is still empty at a
   different path on the same user (keyed by encoded cwd).

8. **A fresh DB is not empty of everything, and it is not an orchestrator.**
   `DatabaseSeeder` writes the admin user, BMAD template group / templates,
   default LLM provider rows (empty keys), and model routing. It does **not**
   seed boards, cards, or agents. An empty board after first start is correct.
   Creating the first agent is a checklist step, not a seed script.

9. **`.NET` version is three different sentences today.** `global.json` pins
   SDK `10.0.204` (`rollForward: latestMinor`). Every csproj is `net9.0`.
   AGENTS.md says ".NET 9 SDK". `docs/project-context.md` says ".NET / ASP.NET
   Core 10.0 LTS". Bootstrap prereq: install the SDK `global.json` names.
   Targeting `net9.0` is not a contradiction.

10. **MCP is not required for Antiphon to run.** This checkout's Claude
    project `mcpServers` is empty. The card's "claude-in-chrome, todoist"
    are operator-environment (Claude user config / this Grok session), not
    product state. Checklist: optional operator subsection. S2 does not probe
    MCP.

11. **`docker-compose.dev.yml` starts Postgres *and* Redpanda.** Channel
    bridge is `Enabled: false` in the tracked file (AppHost forces it on;
    this machine's Production overlay also turns it on). Telegram / Kafka is
    **not** part of "Antiphon answers /health". S1 links
    `docs/telegram-bot-ops.md` rather than absorbing it.

12. **There is no repo-root `README.md`.** A fresh clone has AGENTS.md /
    CLAUDE.md and nothing that says "start here".

## 1. Question 1 — checklist vs provisioning script vs both

**Recommendation: one checklist doc, plus the smallest possible fixes to
existing scripts and stale first-time text so the checklist is not immediately
contradicted. No new provisioning script.**

| Gap (card) | Automatable? | What closes it |
|---|---|---|
| Global `~/.claude` + skills | Already, for this operator (`claude-home` + `sync`). Human-only for a new operator | Checklist scenario fork |
| MCP registrations | Partially; tokens are not; not required to boot | Optional operator subsection |
| Auto-memory | No (tool-level, path-keyed) | §2 / S3 |
| Docker runtime | **Already scripted** (`docker compose -f docker-compose.dev.yml up -d`) | Checklist step + the fresh-vs-migration fork |
| Scheduled Tasks | **Already scripted** (`scripts/install-autostart.ps1`) | Checklist step, with the existing `-AppHostOnly` / MSIX-pwsh caveats |
| Secrets | No (Bitwarden / operator / TUI login) | Checklist table, user-secrets preferred |

Five of six gaps are sequencing plus prose only a doc can carry. The one thing
a provisioning script could add — compose-up → autostart-install → health-wait
— is three commands, and `autostart-apphost.ps1` already waits at logon. A
script that wraps three commands and a pile of human-only steps is a doc with
worse discoverability.

**Canonical stack for the checklist: Aspire.** Commands, in order:

```
docker compose -f docker-compose.dev.yml up -d
pwsh -File scripts/install-autostart.ps1          # or Start-ScheduledTask once registered
pwsh -File scripts/restart-apphost.ps1            # never a second bare dev-aspire.ps1
pwsh -File verify-dev-stack.ps1 -SkipBrowser
pwsh -File scripts/bootstrap-check.ps1            # S2; includes the line above
```

Simple mode (`dev-start.ps1`, `restart.ps1`, 17281/17282/17283) is the
fallback subsection, pointed at `verify-dev-stack.ps1 -SimpleMode`.

**Fresh vs migration:** a new deployment correctly starts with seeder-only
data (admin + BMAD templates, no cards). A machine migration uses
`.\dev-backup.ps1` on the old box and `.\dev-restore.ps1 -BackupFile …` on the
new one. Do not inline `pg_dump`. `dev-fresh.ps1` is the nuclear reset
(volume + `C:\Antiphon\worktrees`), not a bootstrap step.

## 2. Question 2 — is auto-memory loss acceptable?

**Yes, as-is, at the tool level. Do not build an Antiphon-owned durable
memory subsystem.**

- The store is session/machine/path-scoped by the tool. This operator's
  machines already sync `~/.claude` via `claude-home`. Residual loss (new
  operator, new path) is exactly where most memory content — user
  preferences, other-project pointers, machine-specific paths — should not
  transfer.
- The failure that matters is narrower than "memory is lost": it is
  **Antiphon-operational knowledge that lives only in memory**. The repo
  already has the working answer (live misses → CLAUDE.md / `docs/` /
  skill files). Several index entries are already promoted (see S3 table).
- A memories table, a tracked memory dir, or a sync bridge would duplicate
  the tool's store and rot when the format moves. If the promotion policy
  proves leaky later, that evidence justifies tooling — not this card.

## 3. Question 3 — bootstrap self-check

**Worth building: yes — small, read-only, two-phase, reusing
`verify-dev-stack.ps1` for the live half.** Discovering gaps live is how this
deployment's worst incidents started. The script is also the doc-rot detector.

Phase A must work on a machine that has not started the stack yet (that is
the bootstrap case). Phase B is the existing stack probe and is allowed to
FAIL (not skip) when the stack is down — the checklist runs it last, after
start.

Explicit non-goals: no fixing, no secret-value validation, no live API-key
calls, no MCP probing, no Playwright (pass `-SkipBrowser` through).

## 4. Slices

### S1 — `docs/bootstrap.md` + make the existing first-time surfaces agree (M, ~½–1 day)

**New file:** `docs/bootstrap.md`. Link it from `AGENTS.md` (replace the
current "First-time setup" / "Canonical local restart" as the pointer, keep
a one-paragraph summary), a new repo-root `README.md` (does not exist today;
five lines + link is enough), and the first-time sections of
`.claude/skills/antiphon-run/SKILL.md` and
`.claude/skills/antiphon-start/SKILL.md`.

**Required structure of `docs/bootstrap.md`:**

1. **What "done" means.** Aspire stack answers `/health` on 17202/17203/17204;
   `scripts/bootstrap-check.ps1` exits 0; you can create an agent in the UI
   and it reaches Running. Cards from some other deployment are *not* part
   of done. Telegram / Redpanda / channel bind is a link out, not a step.

2. **Scenario fork up front.**
   - (a) This operator, new machine: clone `claude-home` into `~/.claude`,
     run the `sync` skill, then the machine steps. `claude-home` is private
     to this operator — do not write a public clone URL as if a stranger
     could use it.
   - (b) New operator: what repo-tracked `.claude/skills/**` covers
     (`antiphon-delegate`, `antiphon-run`, `antiphon-start`,
     `telegram-e2e-smoke`, `claude-web`, vendored `bmad-*`) and what it
     does not (global CLAUDE.md `@`-imports of browser-harness / bitwarden /
     docker-desktop, the rebase-only git policy, memsearch plugin,
     `~/.claude/commands/rc-status.md`, Grok/Claude MCP). Name them so the
     new operator knows which behaviour they are missing.
   - (c) Fresh deployment vs migration: seeder-only DB is correct vs
     `dev-backup.ps1` / `dev-restore.ps1`.

3. **Machine steps in dependency order**, each pointing at an existing
   script, never inlining its logic:
   1. Prerequisites: Docker Desktop; SDK matching `global.json` (today
      `10.0.204`, not "whatever AGENTS.md's '.NET 9' line says"); Node 20+;
      pwsh 7 via the version-independent
      `%LOCALAPPDATA%\Microsoft\WindowsApps\pwsh.exe` alias (never a
      version-pinned MSIX path — CLAUDE.md); `claude.exe` and/or `grok.exe`
      on PATH and logged in (wrapper-managed auth is how agent sessions
      authenticate — empty `Llm:Providers:*:ApiKey` does not block a TUI
      agent).
   2. Clone this repo. Create the Windows convention directories if missing:
      `C:\Antiphon\worktrees`, `C:\logs\antiphon\session-runner`,
      `C:\logs\antiphon\check-interpreter`.
   3. Secrets: table below.
   4. `docker compose -f docker-compose.dev.yml up -d` (postgres 17280 +
      redpanda 19092; only postgres is required for "done").
   5. First build: `dotnet build`, `npm install` in `client/`, `npm run build`
      (E2E bundle; `AntiphonAppFixture.EnsureClientBundleIsCurrent` hard-fails
      on a stale `dist`).
   6. `pwsh -File scripts/install-autostart.ps1` (re-register-kills-a-running-runner
      and MSIX-pwsh caveats, already in CLAUDE.md / the script header).
   7. Start: `pwsh -File scripts/restart-apphost.ps1` if an AppHost may
      already exist; otherwise `dev-aspire.ps1` via
      `Start-Process pwsh … -WindowStyle Normal` (never `wt new-tab`, never
      `-NoNewWindow` — CLAUDE.md).
   8. `pwsh -File scripts/bootstrap-check.ps1` (S2) as the last step.
   9. Create the first agent in the UI (or `POST /api/agents` +
      `POST /api/agents/{id}/start`). Seeder did not do this.

4. **Secrets table** (config key → where the live value comes from today →
   where a new machine should put it). Verify Bitwarden item names against
   the vault at write time; do not guess. Known-from-docs starting points:

   | Key / secret | Lives today | New machine |
   |---|---|---|
   | `Llm:Providers:anthropic:ApiKey` / `openai:ApiKey` | DB (Settings UI); config empty; user-secrets empty | `dotnet user-secrets set "Llm:Providers:anthropic:ApiKey" … --id antiphon-server` (preferred) or Settings UI after first start. Seeder copies a non-empty config key into the DB. |
   | `GitHub:PersonalAccessToken` | Tracked file empty, `Enabled: false` | Optional. Same user-secrets id if wanted. |
   | Agent TUI wrapper auth | `~/.claude` (Claude), `~/.grok/auth.json` (Grok) | Log the TUI in as the Windows user. |
   | Agent TUI managed secrets + Data Protection ring | DB ciphertext + `%LOCALAPPDATA%\Antiphon\DataProtection-Keys` | Fresh ring is expected; re-enter managed secrets. Back up the ring *with* `dev-backup.ps1` output on a migration (`docs/ai-agent-tui-configuration.md`). |
   | Telegram bot token | Bitwarden item "Antiphon Telegram Bot" (`docs/telegram-bot-ops.md`) | Only if standing up channels. Env / user-secrets on the *gateway*, not the desktop `appsettings.json`. |

   Never print a secret value in the doc.

5. **MCP (optional operator subsection).** Not required for "done". At write
   time, list what `claude mcp list` / this machine's Grok MCP actually has,
   and where each token lives. Do not invent a required set.

6. **Simple-mode fallback.** One subsection: `dev-start.ps1` /
   `restart.ps1` / ports 17281–17283 / `verify-dev-stack.ps1 -SimpleMode`.
   Do not lead with it.

**Doc-blocking config/doc fixes in the same slice** (otherwise the checklist
tells the operator to do something the files punish):

- Rewrite AGENTS.md First-time setup to point at `docs/bootstrap.md`. Stop
  telling anyone to copy anything over tracked `server/appsettings.json`.
- Rewrite AGENTS.md "Canonical local restart" to `scripts/restart-apphost.ps1`
  + Aspire ports; keep 17281/17282 as the fallback.
- Rewrite `antiphon-run` / `antiphon-start` first-time and port-map sections
  to match CLAUDE.md (Postgres is the always-on `antiphon-postgres` on
  **17280**, not Aspire-managed 17201; dashboard is pinned to 17205; first
  time is Aspire; backups are `.\dev-backup.ps1` defaulting to
  `<repo>/backups/`).
- Refresh root `appsettings.json.example` to the live shape: `Providers` as
  an object (not array), `ConnectionStrings:DefaultConnection` including
  `Port=17280`, `Git:DefaultBranch` = `master`, empty secret fields, no
  `D:\` paths. It stays the shape reference, never the file you copy over
  the tracked one.
- In tracked `server/appsettings.json`: change `Git:DefaultBranch` from
  `main` to `master`; change `Git:WorkspacePath` from
  `D:\\src\\Antiphon\\workspace` to `""` (WorkflowEngine already treats
  empty as "not configured"; the code default `"work"` is only used when
  the key is absent). Leave `WorktreeBasePath = C:\\Antiphon\\worktrees`
  and `CheckInterpreterWorkingDirectory` as the documented Windows
  convention — the checklist creates those directories.
- One-line fix in `docs/project-context.md` tech-stack table: runtime
  target is `net9.0`, SDK is whatever `global.json` pins.

Acceptance: a reader can go clone → green `bootstrap-check.ps1` without
consulting any other doc except where the checklist deliberately links.
`antiphon-run` / `antiphon-start` / AGENTS.md no longer name a missing
example file or the 17201 Postgres.

### S2 — `scripts/bootstrap-check.ps1` (S, ~½ day)

ASCII-only (will run on half-bootstrapped machines; pwsh 7 may be the thing
that is missing; CLAUDE.md 5.1-fallback rule). No writes. Exit code =
number of FAILs. Warn-only items never affect the exit code. First line of
output names `docs/bootstrap.md`. Last line of `docs/bootstrap.md` runs it.

**Phase A — pre-start (this script's own probes):**

1. Toolchain: `dotnet --version` satisfies `global.json`; `node` ≥ 20;
   `npm`; `pwsh` resolvable via the app-exec alias (WARN if only 5.1);
   `docker` daemon answering; `claude.exe` and/or `grok.exe` on PATH (WARN
   if neither — TUI agents will not launch).
2. Windows convention dirs exist (WARN if missing):
   `C:\Antiphon\worktrees`, `C:\logs\antiphon\session-runner`,
   `C:\logs\antiphon\check-interpreter`.
3. Docker: `antiphon-postgres` running + healthy; `antiphon_pgdata` volume
   exists (compose project name `antiphon` + volume `pgdata`). HNS sanity =
   the existing timed `docker network create` probe, WARN-only (already in
   `verify-dev-stack.ps1`; do not duplicate if Phase B will run it — run it
   here only when Phase B is skipped).
4. Database: TCP to `localhost:17280`; optionally `pg_isready` via
   `docker compose -f docker-compose.dev.yml exec -T postgres`
   (service name is `postgres`, not the container name).
5. Scheduled Tasks: `Antiphon AppHost` and `Antiphon Session Runner`
   registered (WARN-only; remedial text names `-AppHostOnly`).
6. Secrets *presence, not validity*. PASS if any one of these yields a
   non-empty `Llm:Providers:anthropic:ApiKey` **or**
   `Llm:Providers:openai:ApiKey`:
   - `dotnet user-secrets list --id antiphon-server`
   - gitignored `server/appsettings.Development.json` /
     `appsettings.Production.json`
   - env `Llm__Providers__anthropic__ApiKey` /
     `Llm__Providers__openai__ApiKey`
   WARN (not FAIL) if none are set: a TUI-only deployment can run without
   workflow LLM keys; the Settings UI is the other way in, and the seeder
   leaves the DB keys empty until then. Never print a secret value.
   `KeyRingPath` empty is OK — default
   `%LOCALAPPDATA%\Antiphon\DataProtection-Keys` is the documented location;
   WARN if that directory is missing *and* managed TUI profiles exist (skip
   this WARN on a fresh DB; the script cannot see the DB without the stack,
   so just note the default path).
7. Claude-side: repo `.claude/skills/` present (FAIL = broken clone);
   `~/.claude/CLAUDE.md` exists (WARN-only — new operator may not sync
   `claude-home`).
8. Client bundle staleness (WARN): any `client/src` file newer than
   `client/dist/index.html` — same rule as
   `AntiphonAppFixture.EnsureClientBundleIsCurrent`.
9. Tracked-config sanity (WARN):
   `Git:DefaultBranch` in `server/appsettings.json` equals the current
   branch name (`git rev-parse --abbrev-ref HEAD`);
   `Git:WorkspacePath` is empty or an existing path (the `D:\…` leftover
   is the thing this catches).

**Phase B — live stack:** invoke
`pwsh -File (Join-Path $repoRoot 'verify-dev-stack.ps1') -SkipBrowser`
and treat a non-zero exit as FAIL "stack health" with that script's own
summary as the remedial text. Do not copy its probes. If the operator
passes `-NoStack`, skip Phase B and print that stack health was not
checked.

### S3 — memory promotion: the sweep is this table, plus the policy (S, ~½ day)

Do not re-open the index and re-decide. Promote or skip exactly as follows.
Leave the memory files themselves alone (they stay the operator's cache).

| Memory file | Action |
|---|---|
| `feedback_delegate_worktree_decision` | **Skip.** Already in `.claude/skills/antiphon-delegate/SKILL.md` (the live-miss paragraph and the shared-vs-`-Worktree` checklist). |
| `project_channel_e2e_verification` | **Skip.** Already the body of `.claude/skills/telegram-e2e-smoke/SKILL.md` (4-step chain + bind-before-always-on). |
| `feedback_orchestrator_delegates_the_reading` | **Skip.** Opening of `docs/orchestration-loop.md`. |
| `project_bash_outputpath_trailing_backslash` | **Skip.** CLAUDE.md "Building while daemons run" / trailing-space gotcha. |
| `reference_claude_jsonl_transcript` | **Skip.** Superseded by CLAUDE.md transcript gotchas (lazy file, trust prompt, interrupt marker, fork-follow). |
| `feedback_latest_versions` | **Skip.** User preference. |
| `feedback_always_push_after_commit` | **Skip.** User preference; also in the delegate-basics bundle. |
| `feedback_always_use_browser_harness` / `reference_browser_test_cdp` / `reference_rc_status_tool` | **Skip as promotions.** User-level (`browser-harness`, `~/.claude/commands/rc-status.md`). Name them in S1's "new operator" subsection so the absence is visible. |
| `feedback_plans_land_on_master_fast` | **Promote.** `docs/orchestration-loop.md` §1 already says "land the plan on master" in the diagram. Add the live-miss: a finished Plan/Docs deliverable sitting in a worktree is invisible; cherry-pick / copy onto master and push when the task reports, do not wait for settle. Two 2026-08-10 cases (CARD-0002 design, CARD-0001 fix) sat unmerged for 9 hours. |
| `feedback_launch_agents_via_antiphon` | **Promote** into `docs/orchestration-loop.md` as a short "Launching an agent" note: create/start through `POST /api/agents` + `POST /api/agents/{id}/start` (or the UI), never `claude` CLI / `launch-remote`. Send `modelLevel` as the string `"Frontier"` — a numeric `0` silently becomes `High` (also in `TODO.md`; the bootstrap card is not the place to fix the API). Frontier = fable. |
| `project_cards_are_the_record` | **Promote** one paragraph into `docs/orchestration-loop.md` picking: outstanding work is a board card (`scripts/card.ps1 new`), not `TODO.md`. The board id and Backlog column id in the memory are this deployment's — do not hard-code them; "the Antiphon board" + `card.ps1` is enough. |
| `reference_am_service_deploy` | **Promote** the tar-sync deploy into `docs/telegram-bot-ops.md` (not currently there; grep of the repo has no `tar czf` / `messaging-service` deploy recipe). Steps from the memory, to be re-verified against server2 at write time: tar `src/Antiphon.Messaging*` + `Messaging.Pack.props` to `/home/mc/antiphon-messaging/build/src`, then `docker compose build messaging-service && docker compose up -d messaging-service`. Topics `channels.inbound` / `channels.outbound` carry `max.message.bytes=20971520`. |
| `reference_windmill_cleanup_schedule` | **Promote** a short note onto the header of `scripts/cleanup-build-junk.ps1` and one sentence in `docs/bootstrap.md`'s "this operator" fork: recurring cleanup is Windmill on server2 (`u/lndcobra/antiphon_build_junk_cleanup`, Mon 09:00 Europe/London), not a Windows Scheduled Task. Do not re-add a local task. |

Then add one paragraph to `docs/project-context.md`: *operational knowledge
discovered in a session is promoted into the tracked doc that owns the topic
before the session ends; auto-memory is a cache, not the record.*

Acceptance: the promoted paragraphs are in git; memory files untouched; the
S1 "new operator" subsection names the three user-level items that were
skipped on purpose.

**Order:** S1 → S2 (the doc names the script's checks). S3 is independent
and can run in parallel with either.

## 5. Out of scope (deliberately)

- **A provisioning script** — §1.
- **A durable Antiphon-owned memory subsystem** — §2.
- **Scheduling `dev-backup.ps1`** — the script exists; a Windmill/Scheduled
  Task to run it nightly is a separate card if wanted.
- **Live secret validation / MCP probing** — §3.
- **Seeding boards, cards, or a default orchestrator agent** — empty is
  correct; creating an agent is a checklist step.
- **Cross-platform / Linux-server bootstrap** — this card is a new Windows
  machine or clone of the current shape. `docs/messaging-standalone.md` and
  the S3 `telegram-bot-ops.md` addition cover the one Linux piece
  (am-service on server2).
- **Fixing the numeric-`modelLevel` silent fallback** — named in the S3
  launch-agents paragraph; the API fix is not this card (already in
  `TODO.md`).
- **Making simple-mode (1728x) go away** — document it as fallback; do not
  delete `dev-start.ps1` / `restart.ps1`.
