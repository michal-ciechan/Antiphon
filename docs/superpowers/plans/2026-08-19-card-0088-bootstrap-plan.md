# CARD-0088 — Bootstrapping a brand-new Antiphon: plan

**Date:** 2026-08-19
**Status:** planned (task db49e6fa)
**Source evidence:** CARD-0088's own investigation (grounded, verified against the repo while
planning — corrections below where the repo says something sharper than the card).

## Verdict up front

**Checklist doc, not a provisioning script** — every automatable step is *already* a tracked
script (`docker-compose.dev.yml`, `install-autostart.ps1`, `restart-apphost.ps1`,
`dev-aspire.ps1`); what is missing is the connective document that sequences them and names the
human-only steps (secrets, user-level Claude config, MCP). A new provisioning script would mostly
shell out to those scripts and rot. **Yes to a small self-check script** (`bootstrap-check.ps1`,
~½ day) — it is the thing that keeps the checklist honest, and it doubles as a standing health
probe. **Auto-memory loss is acceptable at the tool level** — do not build a durable-memory
subsystem; instead codify the promotion practice the repo already uses (live-miss lessons →
CLAUDE.md / docs) and run one audit sweep of the current memory index. Three slices, all
executable by a Code-role worker with zero further design decisions, ~1½–2 days total.

Two facts the card under-states, established while planning:

1. **`~/.claude` is a git checkout of the `claude-home` repo** (synced across the user's
   machines via the `sync` skill: commit + pull --rebase + push, plus memsearch junction
   re-wiring). So gaps 1–3 in the card (global `CLAUDE.md` + its `@`-imports, user-level skills,
   the memory store) already survive *this operator's* new machine — the genuinely-lost case is
   a **different operator/user**, or a clone at a **different path** (memory is keyed by encoded
   cwd, so even a synced `~/.claude` yields an empty store for `D:\src\Antiphon`). The checklist
   must say which recovery path applies to which scenario instead of treating "new environment"
   as one case.
2. **The tracked secrets pattern is self-contradictory today.** `server/appsettings.json` is
   *tracked* and carries empty secret fields (`Llm:Providers[*]:ApiKey = ""`,
   `GitHub…PersonalAccessToken = ""` at line 42, `AgentTui…KeyRingPath = ""`), while `.gitignore`
   line 5 ignores `appsettings.*.json` (Development/Production). But `AGENTS.md` "First-time
   setup" step 1 says *copy `appsettings.json.example` to `server/appsettings.json` and fill in
   your LLM API key(s)* — i.e. it instructs the operator to clobber a tracked file and put real
   secrets into a tracked path, one `git add -A` away from a leak. S1 fixes this instruction as
   part of writing the checklist (fill secrets into gitignored
   `server/appsettings.Development.json` / `appsettings.Production.json`, never the tracked
   file).

## 1. Question 1 — checklist vs provisioning script vs both

**Recommendation: checklist doc + targeted fixes to existing scripts where the checklist finds
them wanting. No new provisioning script.**

Reasoning, gap by gap (the card's six):

| Gap | Automatable? | What closes it |
|---|---|---|
| Global `~/.claude` config + skills | Already automated for this operator (`claude-home` clone + `sync` skill); human-only for a new operator | Checklist step: clone `claude-home` OR the "new operator" subsection naming what the repo-tracked `.claude/skills/**` does and does not cover |
| MCP registrations | Partially (`claude mcp add` commands are scriptable but tokens are not) | Checklist step listing the registrations this deployment uses and where each credential lives |
| Auto-memory | No (tool-level, path-keyed) | §2 — promotion policy, not tooling |
| Docker runtime state | **Already scripted**: `docker compose -f docker-compose.dev.yml up -d` | Checklist step + the fresh-vs-migration fork (below) |
| Scheduled Tasks | **Already scripted**: `scripts/install-autostart.ps1` | Checklist step (with the existing `-AppHostOnly` / re-register-kills-runner caveat from CLAUDE.md) |
| Secrets | No (credentials live in Bitwarden / operator's head) | Checklist table: config key → where the real value lives → which gitignored file it goes in |

Five of six gaps are closed by *sequencing things that exist* plus prose only a doc can carry
(where secrets live, what order, which caveats). The one step a provisioning script could
genuinely add — chaining compose-up → autostart-install → health-wait — is three commands, and
`autostart-apphost.ps1` already does the waiting half at every logon. A script that wraps three
commands and a pile of human-only steps is a doc with worse discoverability.

**The fresh-vs-migration fork (must be explicit in the doc):** a *new deployment* correctly
starts with an empty database — cards/sessions/transcripts belong to the old deployment, and an
empty `antiphon_pgdata` is not a bootstrap failure. A *machine migration* wants the data:
document `docker exec antiphon-postgres pg_dump -U antiphon antiphon > backup.sql` and the
matching restore, as checklist prose. Scheduled/automated backups of `antiphon_pgdata` are a
real idea but **out of scope** — file as its own card if wanted (the Windmill scheduler already
exists for it); do not smuggle it into bootstrap.

## 2. Question 2 — is auto-memory loss acceptable?

**Yes, as-is, at the tool level. Do not build an Antiphon-owned durable memory subsystem.**

- The store is genuinely session/machine/path-scoped by the tool's design, and for the operator's
  own machines it already syncs via `claude-home`. The residual loss cases (new operator, new
  path) are exactly the cases where most memory content — user preferences, other-project
  references, machine-specific pointers — *should not* transfer.
- The failure mode that matters is narrower than "memory is lost": it is **Antiphon-operational
  knowledge that lives only in memory**. The repo already has the working answer — every live
  miss became a CLAUDE.md gotcha; investigations and plans are tracked in `docs/superpowers/`.
  What is missing is (a) the practice written down as policy and (b) one audit of the current
  index.
- Building a durable equivalent (a memories table, a tracked memory dir, a sync bridge) would
  duplicate the tool's store, split writes across two systems, and rot the moment the tool's
  format moves. Not worth building now; the promotion policy captures ~all of the value at ~none
  of the cost. If the policy proves leaky in practice, that evidence — not this card — justifies
  tooling later.

S3 executes this: a one-time sweep of the current `MEMORY.md` index, promoting anything
Antiphon-operational not already in tracked docs, plus a short policy paragraph in
`docs/project-context.md`.

## 3. Question 3 — bootstrap self-check

**Worth building: yes — small, read-only, reusing probes the repo already has.** Discovering
gaps live is exactly how this deployment's worst incidents started (a missing trust-folder
answer, a squatted port, a stale dist); a bootstrap gap has the same shape — silent until an
agent is mid-task on it. The script is also the doc-rot detector: when the checklist and reality
drift, the check reds first. And it is cheap because nothing in it is new — every probe below
already exists somewhere (`dev-aspire.ps1`'s Docker/HNS checks, `autostart-apphost.ps1`'s
health-wait, the port-squatter checks in CLAUDE.md).

S2 builds it with this exact check list (each check prints PASS/FAIL/SKIP + one remedial line
naming the checklist section; exit code = failure count; read-only throughout):

1. Toolchain: `dotnet` ≥ 9, `node` ≥ 20, `npm`, `pwsh` 7 resolvable via the app-exec alias
   (warn if only 5.1), `docker` daemon answering.
2. Docker: `antiphon-postgres` container running + healthy; `antiphon_pgdata` volume exists;
   HNS sanity (the existing timed `docker network create` probe, warn-only).
3. Database: TCP connect to `localhost:17280`; optionally `pg_isready` via `docker exec`.
4. Services: HTTP 200 from server `/health` (17202); session-runner port 17204 listening;
   client 17203 answering (warn-only — dev-only concern).
5. Scheduled Tasks: `Antiphon AppHost` and `Antiphon Session Runner` registered (warn-only,
   with the `-AppHostOnly` caveat in the remedial text).
6. Secrets *presence, not validity*: a gitignored `server/appsettings.Development.json` or
   `appsettings.Production.json` exists and its effective config yields non-empty
   `Llm:Providers[anthropic]:ApiKey` and GitHub `PersonalAccessToken`; `KeyRingPath` non-empty
   *or* default DataProtection location present. Never print a secret value, only key names.
   No live API-key validation calls (spends money/quota, needs network — out of scope).
7. Claude-side: `.claude/skills/` present in the repo (tracked — failure means a broken clone);
   `~/.claude/CLAUDE.md` exists (warn-only if the operator chose not to sync `claude-home`).
8. Client bundle staleness (warn-only): any `client/src` file newer than
   `client/dist/index.html` — same rule `AntiphonAppFixture.EnsureClientBundleIsCurrent`
   enforces, surfaced before an E2E run does it the hard way.

Explicit non-goals for S2: no fixing (read-only), no secret-value validation, no MCP-registration
probing (no stable CLI surface to probe without side effects — checklist prose covers it).

## 4. Slices

### S1 — `docs/bootstrap.md`: the checklist (M, ~½–1 day)

One doc, linked from `AGENTS.md` and `README`-adjacent surfaces. Required structure:

1. **Scenario fork up front**: (a) this operator, new machine — `claude-home` clone + `sync`,
   then the machine steps; (b) new operator — what repo-tracked config covers, what it does not
   (global CLAUDE.md `@`-imports, browser-harness, bitwarden/docker-desktop skills, git
   rebase policy — name them so the new operator knows what behavior they are missing);
   (c) fresh deployment vs migration (empty DB is correct vs pg_dump/restore).
2. **Machine steps in dependency order**, each pointing at the existing script, never inlining
   its logic: prerequisites → clone → `docker compose -f docker-compose.dev.yml up -d` →
   secrets (the table below) → first build (`dotnet build`, `npm install` in `client/`,
   `npm run build` for the E2E bundle) → `install-autostart.ps1` (with the re-register and
   MSIX-pwsh caveats already in CLAUDE.md) → start via `dev-aspire.ps1` / Scheduled Task →
   run `scripts/bootstrap-check.ps1` (S2) as the final step.
3. **Secrets table**: config key → gitignored file it belongs in → where the real value lives
   (Bitwarden item names where applicable — verify against the vault at write time, do not
   guess). Includes the Data Protection key-ring note: a fresh ring means previously-protected
   agent TUI secrets are unrecoverable and must be re-entered, which is expected, not a bug.
4. **MCP registrations**: list what this deployment registers and where each credential lives.
5. **Fix the `AGENTS.md` contradiction** as part of this slice: rewrite First-time-setup step 1
   to fill secrets into the gitignored per-environment file, never the tracked
   `server/appsettings.json`; `appsettings.json.example` stays as the shape reference.

Acceptance: a reader can go clone → green `bootstrap-check.ps1` without consulting any other
doc except where the checklist deliberately links.

### S2 — `scripts/bootstrap-check.ps1` (S, ~½ day)

The §3 check list, exactly. ASCII-only (CLAUDE.md's 5.1-fallback rule for daemon/auto-start
scripts applies — this one will be run on half-bootstrapped machines where pwsh 7 may be the
thing that is missing), no writes, exit code = number of FAILs, warn-only items never affect
exit code. Last line of `docs/bootstrap.md` runs it; first line of its output names the doc.

### S3 — memory promotion: one sweep + the policy (S, ~½ day)

- Sweep the current memory index for Antiphon-operational facts not in tracked docs. From
  today's index the candidates are: the channel-E2E verification procedure
  (`project_channel_e2e_verification` — 4-step smoke, bind-channel-before-always-on), the
  delegate worktree decision rule (`feedback_delegate_worktree_decision`), plans-land-on-master
  (`feedback_plans_land_on_master_fast`), and the am-service deploy reference
  (`reference_am_service_deploy` → belongs in `docs/telegram-bot-ops.md` or a deploy doc).
  Skip user-preference and other-project memories. For each: promote into the tracked doc that
  already owns the topic (CLAUDE.md gotchas only for live-miss-class rules; prefer `docs/`).
- Add one paragraph to `docs/project-context.md`: *operational knowledge discovered in a
  session gets promoted into the tracked doc that owns the topic before the session ends;
  auto-memory is a cache, not the record.*
- Acceptance: the promoted content is in git; the memory files themselves are left alone (they
  remain the operator's working cache).

Order: S1 → S2 (the doc names the script's checks; writing the doc first keeps the script the
doc's enforcement, not the other way round), S3 independent/parallel.

## 5. Out of scope (deliberately)

- **A provisioning script** — §1.
- **A durable Antiphon-owned memory subsystem** — §2.
- **Automated DB backups** of `antiphon_pgdata` — real, separate card if wanted (§1).
- **Live secret validation / MCP probing** in the self-check — §3 non-goals.
- **Cross-platform bootstrap** (Linux server deployment) — the card's scenario is a new
  machine/clone of the current Windows-shaped deployment; `messaging-standalone.md` /
  `reference_am_service_deploy` cover the one Linux piece (am-service on server2) and S1 links
  rather than absorbs them.
