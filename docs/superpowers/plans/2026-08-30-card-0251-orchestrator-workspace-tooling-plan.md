# CARD-0251 — A dedicated orchestrator workspace, per CLI: what each CLI actually loads, how to detect the good state, and where the migration warning lives

**Date:** 2026-08-30 · **Status:** plan (Plan pass; nothing built). Ground truth verified against
master `ae858bb` (CARD-0247 S0–S2 merged), **Claude Code 2.1.251**, **Codex CLI 0.151.0**
(`hooks` feature flag: `stable true`), **Grok Build 1.0.13**. Every claim about a CLI below was
either read from the docs that ship *inside* that CLI's install on this machine (Grok:
`~/.grok/docs/user-guide/*.md` and `~/.grok/README.md`; Codex: strings in `codex.exe`) or
**proven by running the real CLI against a scratch directory layout** (§1). CARD-0247's plan
(`2026-08-30-card-0247-orchestrator-delegates-investigation-plan.md`, §5) is the prior art for the
Claude Code half; it is cited, not repeated, and one of its assumptions is corrected in §1.1.

## Verdict up front

1. **Build the tooling as SIBLING-folder tooling, not parent-folder tooling — the card's sketch
   ("orchestrator config above the checkout, source nested underneath") is unsafe for Claude Code
   and unnecessary for all three.** Measured today (§1.1): Claude Code loads `CLAUDE.md` from
   every ancestor of the cwd, so with `<orch>\source\repo` nested under `<orch>`, a session started
   *in the checkout* also received the parent's codeword. A nested layout therefore injects the
   orchestrator's own instructions ("you do not do the work — delegate everything") into **every
   Shared-workspace delegate** launched in that checkout. Codex and Grok do *not* walk above the
   checkout's git root (§1.2, §1.3), so nesting is safe for them — but a sibling folder
   (`C:\src\<project>-orchestrator\` beside `C:\src\<project>\`) works for all three and has one
   property the nested shape cannot have: **the checkout never moves.** That dissolves the card's
   "real risk" for this repo outright — Scheduled Tasks, `docker-compose.dev.yml` names and the
   89 `pwsh -File scripts/...` invocations all keep `C:\src\Antiphon` as the checkout root, because
   it still is.
2. **Per-CLI feasibility (real evidence, one row each — full table in §2):**

   | CLI | Own context file in the orchestrator folder | Hooks | Skills | Detection oracle | Verdict |
   |---|---|---|---|---|---|
   | **Claude Code** 2.1.251 | `CLAUDE.md` with `@../<checkout>/AGENTS.md` — **works, but only after the per-project "Allow external CLAUDE.md file imports?" approval** (`hasClaudeMdExternalIncludesApproved` under the forward-slash project key in `~/.claude.json`); without it the import is silently dropped in `-p` and a blocking modal in the TUI (§1.1, measured) | `<orch>\.claude\settings.json` — does not walk up (the isolation benefit CARD-0247 §5 named) | cwd's `.claude\skills` → NTFS junction to the checkout's (CARD-0247 §5) | none offline; a `-p` codeword probe (the S0 canary) | **Feasible; ~1 day, the CARD-0247 estimate stands** |
   | **Codex** 0.151.0 | `AGENTS.md` only — **no import/include mechanism exists**; precedence `AGENTS.override.md` > `AGENTS.md` > `project_doc_fallback_filenames`, walk bounded by `project_root_markers` (default `.git`) from root to cwd, budget `project_doc_max_bytes` (default 32 KiB — Antiphon's 110 KB AGENTS.md is already truncated for Codex today) | `<repo>\.codex\hooks.json` is **discovered and parsed** (proven by Codex's own parse warning naming that path) and gated on `[projects.'<lowercased path>'] trust_level = "trusted"` in `~/.codex/config.toml`; **`SessionStart` firing under `codex exec` was NOT observed in 4 runs** (§1.2) — unresolved | `~/.codex/skills` (user); project-level not verified | `codex debug prompt-input` (offline, renders exactly what the model sees) | **Feasible for context + detection; hooks unverified; moot until Codex can be an orchestrator** (`docs/agent-kinds.md:44`) |
   | **Grok Build** 1.0.13 | `AGENTS.md` (also `Agents.md`, `CLAUDE.md`, `.claude/CLAUDE.md`, `CLAUDE.local.md`), walk bounded at the git root, **cwd-only outside a repo**; no import; no size cap in 1.0.13 | `<project>\.grok\hooks\*.json` (needs folder trust in `~/.grok/trusted_folders.toml`; "a nested git checkout under that folder is a separate workspace and is not covered") **and `<project>\.claude\settings.json`** via Claude compat | `.\.grok\skills`, `<repo_root>\.grok\skills`, `~/.grok/skills`, `~/.claude/skills`, `.claude\skills` (compat), plus `[skills] paths = [...]` in config — **no junction needed** | `grok inspect --json` (offline, first-class: instruction files, hooks, skills, trust) | **Feasible, best-documented of the three; moot until Grok can be an orchestrator** |

3. **The warning is server-side, and it is a *state* first and an *event* second.** The good/bad
   layout is a static property of an agent's configured `WorkingDirectory`, not something that
   happens during a session — so its primary surface is the **project readiness system that
   already exists** (`ProjectSetupService`, `ReadinessKeys`, the panel on the Agents page and
   `project.ps1 readiness`): a new `orchestrator-workspace` check, `Recommended` / `Warning`, with
   a fix action. That is never noisy because a readiness row does not repeat — it *is*. The
   launch-time nudge the user asked for is a second, thin layer on top: one `AgentIncident`
   (`Warning`) raised at `StartAsync` / `AttachHerdrAsync` / card-spawn / dispatch, **only for an
   orchestrator by declaration** (CARD-0247 §1.5's test, reused verbatim: `orchestrator` bundle
   attached, or `AgentTask.Kind = Orchestrator`), **idempotent on a fingerprint of (agent, cwd,
   detected state)** so a never-migrated project is told exactly once per agent until either the
   state changes or the operator acknowledges it on the project row (§4). A CLI-side
   `SessionStart` hook is rejected as the primary mechanism: it can only fire in the CLI the tooling
   was set up for, it cannot see the declaration, and it cannot be acknowledged anywhere durable.
4. **It is a sibling of CARD-0247 S3 in the attention feed, not a tenant of its sweep.** S3
   (not yet built) is transcript-driven and per-run; this is configuration-driven and per-agent.
   They share the Process attention group, the "detection only, never a gate" rule
   (CARD-0153/0040/0247), and the by-declaration test — nothing else. Reserve
   `AgentIncidentKind = 40` and `AttentionKind = 17` (S3 holds 39 / 16).
5. **Antiphon must not be the first migration. Gym Stat is, and it is a strong first target,
   not a compromise.** `Gym Stat Orchestrator` is the only standing agent in the system with the
   `orchestrator` bundle attached (CARD-0247 §1.5) — the exact population the warning is scoped
   to — it is `Running` today with cwd `C:/src/gym-stat`, `ClaudeCode`, and that checkout has no
   daemons, no Scheduled Tasks and no absolute-path scripts. Its migration is: create
   `C:\src\gym-stat-orchestrator\`, write the marker + `CLAUDE.md` + settings + skills junction,
   set the approval flag, PATCH the agent's `WorkingDirectory`, restart. Nothing on disk moves.
   After a week of the readiness row reading Ok and the S3 sweep (when it lands) showing the
   orchestrator behaving, Antiphon's own move is the *same* five steps on
   `Antiphon-Orchestrator` / the operator's session — and still nothing on disk moves. What
   *does* still cost for Antiphon is the cwd-habit doc pass CARD-0247 §5 priced, which §5 here
   reduces with a marker file `card.ps1`/`delegate.ps1` can follow.
6. **Tooling lives in this repo** (`scripts/orchestrator-workspace.ps1` as a front door over
   server-side rules), not in ClaudeBot: the detector needs the project row, the declaration and
   the attention feed, all of which are Antiphon's. ClaudeBot's convention
   (`docs/agent-workspaces.md`) is the *shape* being copied — a workspace directory whose
   `CLAUDE.md` is the agent's own — and the plan says so where it matters.
7. **Ship Claude Code first, with the Codex and Grok arms of the tooling writing files but marked
   unverified.** Neither can be an orchestrator today (`docs/agent-kinds.md:44`, `:353`), so a
   verified hook arm protects nobody; the *detection* arm is cheap for both because each CLI ships
   an offline oracle (`codex debug prompt-input`, `grok inspect --json`) that does the work.

## 1. Ground truth

All probes ran in the session scratchpad (`…\scratchpad\orch\…`, `…\scratchpad\sib\…`), with a
codeword in each candidate instruction file and the question *"List every codeword of the form
WORD-NNNN that appears anywhere in your instructions or context."* The layouts:

```
orch\                         (NOT a git repo — the card's "parent folder")
  CLAUDE.md                   ORCH-CODEWORD-4412  +  @source/repo/AGENTS.md
  AGENTS.md                   ORCH-AGENTS-CODEWORD-5150
  source\repo\                (git init; the "checkout")
    AGENTS.md                 REPO-CODEWORD-9931
    CLAUDE.md                 @AGENTS.md
sib\
  orch\CLAUDE.md              SIBORCH-CODEWORD-2201  +  @../repo/AGENTS.md
  repo\                       (git init)  AGENTS.md: SIB-CODEWORD-7788, CLAUDE.md: @AGENTS.md
```

### 1.1 Claude Code 2.1.251 — the upward walk, and the external-import gate

| Run (`claude -p --model haiku`) | Answer | Meaning |
|---|---|---|
| cwd `orch` (parent) | `ORCH-CODEWORD-4412, REPO-CODEWORD-9931` | The downward `@source/repo/AGENTS.md` import resolves (confirms CARD-0247 §5). |
| cwd `orch\source\repo` (nested checkout) | `ORCH-CODEWORD-4412, REPO-CODEWORD-9931` | **The parent's `CLAUDE.md` is loaded inside the checkout.** CARD-0247 §5 described the walk as "cwd upward" but only priced the orchestrator's side of it; this is the delegate's side, and it is the disqualifier for the nested shape. |
| cwd `sib\orch`, `@../repo/AGENTS.md`, two attempts | `SIBORCH-CODEWORD-2201` | The sibling import is **silently dropped**. |
| cwd `sib\orch`, absolute-path import (`@C:/…/sib/repo/AGENTS.md`) | `SIBORCH-CODEWORD-2201` | Dropped. |
| cwd `sib\orch`, `source\repo` = NTFS **junction** → `sib\repo`, `@source/repo/AGENTS.md` | `SIBORCH-CODEWORD-2201` | Dropped — the junction is resolved to its real path before the check. |
| cwd `sib\orch`, `@../repo/AGENTS.md`, with `~/.claude.json` → `projects["C:/…/sib/orch"].hasClaudeMdExternalIncludesApproved = true` | `SIBORCH-CODEWORD-2201, SIB-CODEWORD-7788` | **Resolves.** Also with `--add-dir ../repo` (not needed). |
| same, but the flag written under the backslash key `C:\…\sib\orch` | `SIBORCH-CODEWORD-2201` | Dropped — the project key is the **forward-slash** form (46 existing entries on this machine all are). |
| cwd `sib\repo` | `SIB-CODEWORD-7788` | The sibling orchestrator folder leaks nothing into the checkout. |

The gate is real and named in the binary (`~/.local/bin/claude.exe`): the dialog text is *"Allow
external CLAUDE.md file imports? — This project's CLAUDE.md imports files outside the current
working directory. Never allow this for third-party repositories."*, with the per-project keys
`hasClaudeMdExternalIncludesApproved` / `hasClaudeMdExternalIncludesWarningShown` and the component
`ClaudeMdExternalIncludesDialog`. In the interactive TUI this is a **blocking modal of the same
class as the trust dialog** CARD-0047 answers in `ClaudeBlockingPrompt.ClearStartupTrustPromptAsync`
(`src/Antiphon.Agents.Pty/ClaudeBlockingPrompt.cs`); in `-p` it is skipped and the import is
dropped without a message. Both `C:/src/Antiphon` and `C:/src/gym-stat` already carry the approved
flag on this machine (the user's global `~/.claude/CLAUDE.md` imports `C:\src\browser-harness\SKILL.md`
and two skills, which is "outside the project" for every project). **Setup must write this flag
for the orchestrator folder's path before the first launch**, which is what makes the sibling
layout viable for Antiphon-launched sessions (a launch that parks on the dialog is invisible to
every readiness signal we have — CARD-0047's exact failure).

### 1.2 Codex CLI 0.151.0 — no import, walk bounded at `.git`, hooks discovered but not seen firing

`codex debug prompt-input` renders the model-visible input list as JSON **offline** (no model
call) and is the detection oracle for Codex.

| Run | Codewords in the rendered prompt | Meaning |
|---|---|---|
| cwd `orch` (not a git repo) | `5150` only | Loads cwd's `AGENTS.md`; ignores `CLAUDE.md` (not a fallback filename by default). |
| cwd `orch\source\repo` | `9931` only | **Bounded at the checkout's git root** — the parent's files are not loaded. Nested is safe for Codex. |
| cwd `sib\orch` (only a `CLAUDE.md` there) | none | No `AGENTS.md`, nothing loaded; no import syntax exists to try. |

From the binary (`%LOCALAPPDATA%\OpenAI\Codex\bin\e305f1c75d8da435\codex.exe`, the same build the
npm shim `codex.js` resolves through the `@openai/codex-win32-x64` platform package): the
discovery source is `core\src\agents_md.rs` with the precedence `AGENTS.override.md`, `AGENTS.md`,
then `project_doc_fallback_filenames`; the root is found via `project_root_markers` (a config
array, default `.git`); the size budget is `project_doc_max_bytes` with the log line *"project
doc exceeds remaining budget; truncating"* — the default is 32 KiB, and Antiphon's `AGENTS.md` is
110 650 bytes, so **a Codex delegate in this repo today sees roughly the first 30 % of AGENTS.md**
(a side finding worth its own card; not this one's).

Hooks: `codex features list` reports `hooks  stable  true`. Layers named in the binary:
`thread / turn / system / project / mdm / session_flags / plugin / cloud_requirements`;
`SessionStart` sources `startup / resume / clear / compact`; the output schema carries
`hookSpecificOutput`, `additionalContext`, `permissionDecision` (Claude-shaped, as CARD-0247 §1.4
read from the docs). The project file is `<repo>\.codex\hooks.json` — proven the honest way: a
hooks file with an unescaped backslash produced
`warning: failed to parse hooks config C:\…\orch\source\repo\.codex\hooks.json: invalid escape at
line 1 column 100` from `codex exec`, i.e. Codex looked at exactly that path and read it. Project
trust is `[projects.'c:\src\antiphon'] trust_level = "trusted"` in `~/.codex/config.toml`
(lower-cased backslash keys; five such entries exist today) and can be supplied per-run with
`-c 'projects."<path>".trust_level="trusted"'`. **What was not observed:** with a valid
`SessionStart` command hook (`cmd /c echo … >> hook-fired.log`) in the project file, trusted or
not, and again as a user-layer `-c hooks.SessionStart=[…]` override, `codex exec` completed four
runs with no marker file written — inside the workspace, outside it, absolute and relative paths.
Either `exec` does not run `SessionStart`, or the hook's cwd/shell differs from the session's; the
TUI's `/hooks` panel and `codex app-server`'s `hooks/list` are where the Codex slice (§6, S6) must
pin this, and nothing in this card depends on it today.

### 1.3 Grok Build 1.0.13 — the best-documented of the three, with an offline inspector

CARD-0247 §1.4 could not find Grok's official hooks page. It ships with the CLI:
`~/.grok/docs/user-guide/10-hooks.md`, `12-project-rules.md`, `08-skills.md`,
`05-configuration.md`, `26-config-reference.md`, and `~/.grok/README.md` (109 KB). Everything
below is from those files, confirmed by `grok inspect --json` where the probe could reach it.

| Fact | Source | Probe |
|---|---|---|
| Instruction files: `Agents.md`, `Claude.md`, `CLAUDE.md`, `CLAUDE.local.md`, `AGENT.md`, `AGENTS.md`, plus `.claude/CLAUDE.md` at each level; home-level `~/.grok/` and `~/.claude/`; every directory from **repo root → cwd** inside a git repo; **cwd only** outside one; deeper wins; no cap ("loads each project instruction file in full") | `12-project-rules.md` | `grok inspect --json` in `orch`: `projectRoot: null`, instructions = `orch\Agents.md`, `orch\Claude.md` (+ global). In `orch\source\repo`: `projectRoot` = the repo, instructions = the repo's two files only. **Bounded at the git root; no upward leak.** |
| No import/include syntax for instruction files | grep of both docs for `import`/`include` — only Python/TS sample code | — |
| Hooks: `<project>\.grok\hooks\*.json` (requires folder trust: `~/.grok/trusted_folders.toml`; `--trust` / `/hooks-trust`), `~/.grok/hooks\*.json`, `[[hooks.<Event>]]` in `config.toml` layers, **and `<project>\.claude\settings.json` / `settings.local.json` (Claude compat, default on, `compat.claude.hooks`)**; events `SessionStart` (non-blocking, matcher on `startup`/`resume`/…), `UserPromptSubmit`, `PreToolUse` (can deny), `Stop`, … ; tool-name aliases `Read→read_file`, `Bash→run_terminal_command`, … ; "A nested git checkout under that folder is a separate workspace and is not covered" by trust | `10-hooks.md` | `grok inspect --json` in `C:\src\Antiphon` lists **this repo's CARD-0247 hook** twice (`pre_tool_use`, `session_start`; `vendor: claude`, `compatibilityStatus: enabled`) — i.e. **every Grok session in this repo already runs `orchestrator-investigation-hook.mjs`**, which today exits on `ANTIPHON_TASK_ID` for delegates but arms on rule 5 for a plain Grok session here. Side finding; not this card's. |
| Skills: `.\.grok\skills` (cwd, highest) → `<repo_root>\.grok\skills` → `~/.grok/skills` → `~/.claude/skills`; `.claude\skills` via compat; `[skills] paths = [...]` adds directories; discovery ignores `.gitignore` | `08-skills.md`, `README.md §Skills` | inspect lists this repo's `.claude\skills\*` as `vendor: claude` skills |
| Project config `.grok\config.toml` contributes only `[mcp_servers]`, `[plugins]`, `[permission]`; hooks/skills config is user-level only | `05-configuration.md:305` | — |
| `grok inspect --json` reports `cwd`, `projectRoot`, `projectTrusted`, `projectInstructions[]`, `hooks[]`, `skills[]`, `permissions`, per-vendor `compatibilityStatus` | `README.md §Introspection` | used above |

Untrusted project hooks are "silently skipped", and `inspect` does not list them until trusted
(measured: the scratch `.grok\hooks\*.json` files did not appear; `inspect` has no `--trust`).
Trusting requires an entry in the user's `trusted_folders.toml`, which this pass did not write.

### 1.4 What Antiphon already has that this plan builds on

- **Readiness checks are a first-class, cached, UI-rendered concept**: `ProjectSetupService`
  (`server/Application/Services/ProjectSetupService.cs:387–796`) produces `ReadinessCheckDto(Key,
  Level, Status, Summary, Detail, Fix)` rows keyed by `ReadinessKeys` (`ProjectSetupDtos.cs:38`),
  including an **`OrchestratorCheck`** (`:705`) that already answers "is a standing orchestrator
  with the `orchestrator` + `board-api` bundles watching this board" and an `AgentDirectoryCheck`
  (`:573`) with a fix action (`create-directory`, CARD-0214) that the Agents page renders per
  agent (`client/src/features/agents/AgentsPage.tsx:82–152`). `GET /api/projects/{id}/readiness`,
  `project.ps1 readiness`. This is where a static workspace check belongs.
- **Orchestrator by declaration** is already computed in two places and exported to the
  environment: `AgentSessionLaunchComposer.cs:48–50` (`ANTIPHON_ORCHESTRATOR=1` when the bundle
  is attached) and `AgentTaskDispatcher.BuildEnv` `:2399` (`ANTIPHON_TASK_KIND`). CARD-0247 S2.
- **Incident → attention plumbing**: `AgentIncident { AgentId, SessionId?, Kind, Severity,
  Message, CreatedAt }` (`server/Domain/Entities/AgentIncident.cs`); `AttentionService.cs:800–825`
  groups fresh incidents per `(AgentId, Kind)`; the `RecordQuotaOverrideIncident` shape at
  `AgentControlService.cs:601` is the one-line precedent for a Warning raised inside `StartAsync`.
  There is **no** dismiss/acknowledge column anywhere today (`grep Dismiss|AcknowledgedAt` over
  `server/`: nothing), which is why §4 adds one on the project row rather than on the incident.
- **Entry points the card names**: `AgentControlService.StartAsync` (`:95`),
  `AttachHerdrAsync` (`:376`), card-spawn (the `system` actor path), and dispatch of an
  `AgentTask` with `Kind = Orchestrator` (`AgentTaskDispatcher`).
- **Card scope and delegate defaults are cwd-derived** — the CARD-0247 §5 breakages. `card.ps1`
  sends `git rev-parse --show-toplevel` as `?cwd=` and `CardIdentifierScope.cs:175–187` matches
  projects whose `LocalRepositoryPath` contains it (`DelegationWorkspaceResolver.IsWithinRoot`);
  the caller's board and the standing agent's board are tried *first* (`:145–155`) when a task
  token is present, so a **declared standing orchestrator with a token is unaffected** — only the
  operator's token-less manual session in a sibling folder would fall through to "everywhere".
  `AgentTaskService.cs:81/229` default a delegate's working directory to the caller's;
  `delegate.ps1:127` reads `antiphon.areas.json` from `-Dir` or cwd.
- **Live environment facts for §5**: three Scheduled Tasks (`Antiphon Session Runner`,
  `Antiphon AppHost`, `Antiphon AppHost Watchdog`) each run
  `-File "C:\src\Antiphon\scripts\<x>.ps1"`; the project table holds `Antiphon` at
  `C:/src/Antiphon` (plus two duplicates, `antiphon` / `Antiphon (2)`, at the same path — a
  collision the readiness check will see); `gym-stat` at `C:/src/gym-stat` with a real
  checkout (`AGENTS.md`, `CLAUDE.md`, `.git`, no `.claude/`), and ~20 `gym-stat-*` sibling
  checkouts registered as their own projects.

## 2. What "the orchestrator's own agent-context file" means, per CLI

| | Claude Code | Codex | Grok |
|---|---|---|---|
| File the tool writes in `<orch>` | `CLAUDE.md`: 5–10 lines of orchestrator-only text (or `@` of `server/Bundles/orchestrator.md`-equivalent), then `@../<checkout>/AGENTS.md` | `AGENTS.md`: the same orchestrator text **plus an explicit first instruction to read `../<checkout>/AGENTS.md` at session start** — there is no include, and a copy would drift (the CLAUDE.md-stub rationale in AGENTS.md's last section applies unchanged) | `AGENTS.md`, same content as Codex's; Grok also reads a `CLAUDE.md` there, so the Claude file can double for Grok if both CLIs share one folder |
| Precondition the tool must satisfy | `~/.claude.json` → `projects["<orch, forward slashes>"].hasClaudeMdExternalIncludesApproved = true` (§1.1). Also `hasTrustDialogAccepted` — or rely on CARD-0047's answerer | `[projects.'<orch, lower-case, backslashes>'] trust_level = "trusted"` in `~/.codex/config.toml` if hooks/MCP in `<orch>\.codex\` are wanted | `<orch>` in `~/.grok/trusted_folders.toml` for project hooks; none for instructions |
| Hooks | `<orch>\.claude\settings.json` — the CARD-0247 hook entry, with `${CLAUDE_PROJECT_DIR}` replaced by the checkout path (the script lives in the checkout) | `<orch>\.codex\hooks.json` (same JSON shape; **firing unverified**, §1.2) | `<orch>\.grok\hooks\antiphon.json` or nothing — Grok reads the Claude settings file anyway |
| Skills | junction `<orch>\.claude\skills` → `<checkout>\.claude\skills` (directory junction; measured working for Claude in CARD-0247 §5 and listed by Grok's inspect too) | not applicable to the orchestrator contract today | `[skills] paths` is user-level config, so the junction is the per-project answer here too |
| Offline verification after `setup` | none — the S0 canary (`-p` codeword) is the only proof, costs one haiku call | `codex debug prompt-input` must contain the checkout's `AGENTS.md` first line | `grok inspect --json` must list `<orch>\AGENTS.md` (or `CLAUDE.md`) with `projectRoot: null` |
| Delegates launched in the checkout | see nothing of `<orch>` (sibling; measured) | see nothing of `<orch>` (bounded at `.git`; measured) | see nothing of `<orch>` (bounded; measured) |

## 3. Detection — positively identifying the good state

A pure function `OrchestratorWorkspaceLayout.Classify(dir, cli, homeState)` (C#, no I/O in the
core; the I/O adapter gathers a small record of facts) returning one of:

| State | Positive evidence required (all of it) | Meaning |
|---|---|---|
| `Dedicated` | (1) `dir\antiphon.workspace.json` parses (schema v1: `{ "version": 1, "checkout": "../gym-stat", "project": "<guid>", "cli": "claude" }`); (2) `checkout` resolves to an existing directory whose `git rev-parse --show-toplevel` is itself; (3) `dir` is **not** inside any git worktree (`git -C dir rev-parse --show-toplevel` fails) and the checkout is not inside `dir` (the nested shape is classified `DedicatedNested`, a Warning, for Claude — §1.1); (4) the CLI's context file exists in `dir` and names the checkout's `AGENTS.md` (Claude: an `@` line whose relative target resolves to it; Codex/Grok: a literal path mention); (5) the CLI's precondition holds (§2 row 2) — else `DedicatedUnapproved`, which is the state that parks a TUI on a modal and must read as **Warning**, not Ok | the good state |
| `CheckoutAsCwd` | `git rev-parse --show-toplevel` == `dir` (or `dir` is inside a worktree) **and** any of `AGENTS.md`, `CLAUDE.md`, `.claude\settings.json`, `.codex\`, `.grok\` exist at that root | today's pattern, every project — the migration warning's target |
| `Unconfigured` | a git root with none of those files | a fresh project: the message is "set up", not "migrate" (the card's own distinction) |
| `Foreign` | none of the above (e.g. `C:\logs\antiphon\check-interpreter`, a ClaudeBot agent workspace inside the ClaudeBot repo) | never warned: the check is scoped to declared orchestrators, and none of these are |

The marker file is the load-bearing piece: it is what makes "dedicated" a **positive** claim rather
than an inference from absence, it is what `card.ps1` / `delegate.ps1` follow to find the checkout
root from a sibling cwd (§5), and it is cheap for a human to read. `grok inspect --json` and
`codex debug prompt-input` are used by `setup --verify` and by the S0 canaries, not by `Classify`
(the server must not shell out to a CLI on every readiness read).

## 4. The live migration warning — placement, trigger, suppression

**Placement: server-side, two layers.**

1. **Readiness check `orchestrator-workspace`** (`ReadinessKeys.OrchestratorWorkspace`,
   `ReadinessLevel.Recommended`) in `ProjectSetupService`, evaluated for the project's
   *declared* orchestrator agent (the same `match` the existing `OrchestratorCheck` finds, `:710`):
   `Dedicated` → `Ok`; `CheckoutAsCwd` → `Warning`, summary *"Orchestrator 'Gym Stat Orchestrator'
   runs in the checkout itself (C:/src/gym-stat); its CLAUDE.md, hooks and transcript root are
   shared with every delegate launched there."*, detail = the proposed sibling path and the
   `scripts/orchestrator-workspace.ps1 plan` command, fix = `{ Label: "Show migration plan",
   Route: "/agents?agent=<id>", Action: "orchestrator-workspace-plan" }` plus a second fix
   `{ Label: "Keep as is", Action: "acknowledge-orchestrator-workspace" }`; `DedicatedUnapproved`
   / `DedicatedNested` → `Warning` with the specific precondition named; no declared orchestrator →
   `NotApplicable`. Acknowledgement writes **`Project.OrchestratorWorkspaceAcknowledgedAt`** (one
   nullable column, one migration) and the row reads `Ok` with *"acknowledged on <date>"*. This
   layer is visible exactly where starts and attaches happen in the UI (the Agents page renders
   readiness per agent) and from `project.ps1 readiness`.
2. **Launch-time incident** — `AgentIncidentKind.OrchestratorWorkspaceUnconfigured = 40`,
   `AlertSeverity.Warning`, raised inside `AgentControlService.StartAsync`, `AttachHerdrAsync`,
   the card-spawn path and `AgentTaskDispatcher` (for `Kind = Orchestrator`), **iff** (a) the
   session is an orchestrator by declaration, (b) `Classify` ≠ `Dedicated`, (c)
   `Project.OrchestratorWorkspaceAcknowledgedAt` is null, and (d) no incident of kind 40 exists
   for this `AgentId` whose message carries the same fingerprint `sha8(cwd | state | cli)`. The
   message is one sentence plus the `plan` command. Surfaced as `AttentionKind.OrchestratorWorkspace
   = 17` in the Process group, ordered after CARD-0247's 16; never counted as Broken; never
   blocks the launch (the launch proceeds before the incident is written). The API-driven paths
   (`delegate.ps1 -Kind Orchestrator`, a card spawn) are why this layer exists at all — nobody is
   looking at the readiness panel when a script starts an orchestrator.

**Suppression rule, stated plainly:** *once per agent per (cwd, state) until acknowledged or
changed.* Not "once per N days" (a reminder cadence is CARD-0231's shape and is for things that
need an answer; this needs a decision that a project row can hold), and not "once per project"
(two orchestrator agents on one project with different cwds are two different facts). A project
that is never migrated and never acknowledged is told once per orchestrator agent, forever — that
is one row, not noise. Changing the cwd (the migration itself) changes the fingerprint, so a
half-done migration (`DedicatedUnapproved`) gets exactly one fresh, more specific incident.

**Why not a CLI-side `SessionStart` hook:** it fires only in the CLI the tooling was set up for
(the card's own objection), only after the session has already started in the wrong place, cannot
see the declaration (a plain `claude` in a checkout is *not* an orchestrator by declaration —
CARD-0247 §3.1 rule 5 makes it one for the *nudge*, which is the right default there and the wrong
one here: warning every human hacking session in `C:\src\Antiphon` about a workspace it does not
have is exactly the noise the card forbids), and has nowhere durable to record an acknowledgement.
Grok would additionally run the Claude one through compat. Rejected.

**Relationship to CARD-0247 S3:** different trigger class (configuration vs behaviour), different
cadence (event at launch vs minute sweep over transcripts), different population (declared only vs
declared + by-behaviour), same feed, same group, same never-a-gate rule. Do not fold this into the
S3 sweep; do reserve adjacent enum values so the two read as siblings in the UI.

## 5. What still breaks with a SIBLING layout, and the fix for each

CARD-0247 §5's table, re-priced for a sibling folder where the checkout does not move:

| Assumption | Sibling layout | Fix |
|---|---|---|
| `card.ps1` scope from `git rev-parse --show-toplevel` (`card.ps1:217`) | the orchestrator's cwd is not a git repo → `?cwd=` is the sibling folder → matches no `LocalRepositoryPath` → "everywhere" → **409 on `CARD-0001…0021`** for a token-less operator session; a standing orchestrator with a task token is unaffected (`CardIdentifierScope.cs:145–155`) | `Get-CheckoutRoot` follows `antiphon.workspace.json` when `git rev-parse` fails; one function |
| `delegate.ps1` / `AgentTaskService` default working directory = caller's (`:81`, `:229`) | delegates would launch in the sibling folder: Shared tasks outside the repo, Worktree tasks refused ("not a git repository") | `delegate.ps1` reads the marker for its default `-Dir`; server side, `AgentTaskService` resolves a caller whose cwd carries a marker to the marker's checkout (one branch in `Caller` construction) |
| `antiphon.areas.json` from `-Dir`/cwd (`delegate.ps1:127`) | same | same marker follow |
| `CLAUDE.md` loaded from cwd upward | the orchestrator sees only `<orch>\CLAUDE.md` + its import; **delegates see nothing of it** (measured) | none — this is the point |
| Project skills from cwd's `.claude\skills` | invisible | junction (CARD-0247 §5) — written by `setup` |
| `.claude\settings.json` hooks per primary cwd | the CARD-0247 nudge would stop firing for the orchestrator | `setup` writes a settings file whose hook command points at the checkout's `scripts/hooks/*.mjs` by absolute path |
| Transcript root per cwd (CARD-0006) | the orchestrator's `~/.claude/projects/<enc-orch>/` is no longer shared with any delegate | none — the second benefit |
| The 89 `pwsh -File scripts/...` and `git` examples assume cwd = checkout | still true for the **orchestrator's own shell**; every command it types grows `-C`/`cd` | the doc pass CARD-0247 §5 priced; unavoidable, and the reason Antiphon goes last |
| Scheduled Tasks, compose names, `logs/*.pid`, `AGENTS.md`'s `C:\src\Antiphon` paths | **unchanged** — the checkout did not move | none |
| Remote-control titles, `cleanup-claude-sessions.ps1` path matching (CARD-0145) | the orchestrator session's project path changes once | small |

## 6. Slices

| # | Slice | Where | Test that pins it |
|---|---|---|---|
| S0 | **Canaries for the four load-bearing facts**: Claude's upward walk (nested parent codeword visible in the checkout), Claude's external-import gate (dropped without the flag, resolved with it, forward-slash key), Codex bounded at `.git` (`debug prompt-input`, offline), Grok bounded at `.git` (`inspect --json`, offline). Headed/`[Explicit]` for the two that spend a Claude call; the Codex/Grok ones are cheap enough to run always when the CLI is present | `tests/Antiphon.Agents.Pty.Tests` beside `ClaudeTrustPromptCanaryTests`; `tests/Antiphon.Tests/Agents` for Codex/Grok | `OrchestratorWorkspaceLayoutCanaryTests` (4) |
| S1 | **`OrchestratorWorkspaceLayout`**: the marker schema, the fact-gathering adapter, the pure `Classify`, and the per-CLI precondition readers (`~/.claude.json` forward-slash key; `~/.codex/config.toml` lower-case key; `~/.grok/trusted_folders.toml`) | `server/Application/Services/OrchestratorWorkspaceLayout.cs` (+ `Antiphon.SessionRunner.Contracts` if the runner needs it later) | `OrchestratorWorkspaceLayoutTests` over fixture directories: each state, each CLI, the nested-for-Claude Warning, the key-form traps |
| S2 | **Readiness check** `orchestrator-workspace` + `Project.OrchestratorWorkspaceAcknowledgedAt` (migration) + `POST /api/projects/{id}/acknowledge-orchestrator-workspace` + the two fix actions in the Agents page panel + `project.ps1 readiness` showing it | `ProjectSetupService`, `ProjectSetupDtos`, `ProjectEndpoints`, `AgentsPage.tsx`, migration | `ProjectSetupServiceTests` rows (Ok / Warning / acknowledged / NotApplicable); a client test for the fix buttons |
| S3 | **Launch incident** kind 40 + attention kind 17 + fingerprint once-ness, at the four entry points, declared-only | `AgentControlService`, `AgentTaskDispatcher`, the card-spawn path, `AgentIncidentKind`, `AttentionDtos`, `AttentionService`, client attention labels | `AgentControlServiceIntegrationTests` (raised once, not twice, not for a worker, not when acknowledged, again after cwd change); `AttentionServiceTests` row case |
| S4 | **`scripts/orchestrator-workspace.ps1`** — verbs `inspect <dir or project>` (prints the classification and every fact behind it), `plan <project> [-Cli claude\|codex\|grok] [-Path <sibling>]` (prints the exact files, flags and PATCH it would perform; **never writes**), `setup` (writes them, then runs the CLI's offline oracle where one exists and the S0-style codeword probe for Claude when `-Verify`), `acknowledge`. Claude arm complete; Codex/Grok arms write files and print **"hooks unverified — see CARD-0251 plan §1.2/§1.3"**. Follows `card.ps1`'s conventions (header reference, `-Json`, long text from files) | `scripts/orchestrator-workspace.ps1`; `card.ps1` `Get-CheckoutRoot` and `delegate.ps1` default `-Dir` learn the marker (§5) | Pester-style script tests are not this repo's habit; pin the marker-follow in `card.ps1`/`delegate.ps1` through the existing `DelegationUnitTests` shape for the server half, and a `[Explicit]` end-to-end that runs `plan` against the scratch fixtures |
| S5 | **Gym Stat migration** — operator-approved dispatch: `setup` on `gym-stat` → `C:\src\gym-stat-orchestrator\`, PATCH the standing agent's `WorkingDirectory`, restart it, confirm readiness `Ok`, confirm the first delegate it dispatches lands in `C:/src/gym-stat`, watch for a week | operator + one Deploy delegate | the readiness row; CARD-0247 S3's sweep once it lands |
| S6 | **Codex/Grok hook verification** — only when either kind is admitted as an orchestrator (`AgentTaskService.DelegatableKinds` / the orchestrator refusal): TUI-driven canaries for `SessionStart` firing (Codex `/hooks` or `app-server hooks/list`; Grok `/hooks-list` after `--trust`) | tests | — |
| S7 | **Antiphon** — a separate card after S5's week: the same `setup` on `Antiphon` for `Antiphon-Orchestrator` and the operator's own session, plus the doc pass (`AGENTS.md` "Working cards from a shell", `orchestration-loop.md`, the delegate skill) that names the sibling folder and the marker | docs, operator | — |

Order: S0 → S1 → S2 → S3 (the warning is live and honest) → S4 (the fix it points at exists) →
S5. S0–S4 is roughly two days of delegate work — S1/S2/S3 a day, S4 most of another, S0 a few
hours. S6 and S7 are gated, not scheduled.

## 7. Deliberately out of scope

- Any move of `C:\src\Antiphon` or any other checkout — the sibling layout makes it unnecessary,
  and the nested layout is now known to be wrong for Claude Code.
- Automated migration of a live project without an operator action: `setup` is explicit and per
  project, and PATCHing an agent's working directory is a deliberate step the operator (or a
  Deploy delegate with that instruction) takes. There is nothing to auto-migrate.
- Codex's `project_doc_max_bytes` truncation of this repo's AGENTS.md for Codex delegates
  (§1.2) — a real finding, its own card.
- The CARD-0247 nudge hook running inside Grok sessions in this repo through Claude compat
  (§1.3) — a real finding, its own card (or a one-line `[compat.claude] hooks = false` decision).
- Duplicate `Antiphon` project rows at the same path — the readiness check will name it; fixing
  the rows is a data task.
- Anything CARD-0247 S3 builds: this plan reserves enum values beside it and nothing more.

## 8. Decisions for the operator

| Decision | Recommendation |
|---|---|
| Sibling vs nested | **Sibling** (`C:\src\<project>-orchestrator\`). Nested is unsafe for Claude Code (§1.1) and buys nothing for the others. |
| Folder naming | `<checkout-name>-orchestrator` beside the checkout; the marker records the link either way, so the name is convention, not contract. |
| First migration target | **Gym Stat** (S5): the only declared standing orchestrator, no daemons, no path-bound scripts. Antiphon after a week, as S7, and only its orchestrator's cwd — the checkout never moves. |
| Warning placement | Readiness check (state) + once-per-fingerprint launch incident (event), server-side, declared orchestrators only, acknowledgeable on the project row. |
| Suppression | Once per agent per (cwd, state); `Project.OrchestratorWorkspaceAcknowledgedAt` silences both layers. |
| Tooling home | This repo: `scripts/orchestrator-workspace.ps1` over server-side rules. Not ClaudeBot. |
| Provider order | Claude Code verified and shipped; Codex/Grok detection shipped (their oracles do the work), hooks marked unverified until S6's gate opens. |
| Enum reservations | `AgentIncidentKind.OrchestratorWorkspaceUnconfigured = 40`, `AttentionKind.OrchestratorWorkspace = 17` (CARD-0247 S3 keeps 39 / 16). |

## 9. Not determined / to pin in S0–S1

- Whether the interactive TUI shows the external-imports dialog when the flag is *absent* but
  `hasClaudeMdExternalIncludesWarningShown` is true (the `-p` probe only proves the approved
  flag suffices). S0's headed arm should run the TUI once against a fresh scratch folder.
- Whether Claude deduplicates an `AGENTS.md` reached twice (the nested layout's checkout `CLAUDE.md`
  imports it and the parent's does too). Moot for the sibling layout; note it if nested is ever
  reconsidered.
- Codex `SessionStart` under `codex exec` (§1.2) — four negative runs, cause unknown.
- Whether `ProjectReadinessCache` needs invalidation on agent `WorkingDirectory` PATCH and on
  `acknowledge` (`ProjectReadinessCache.Remove(projectId)` exists; S2 should call it from both).

## 10. Environment / cleanup

No repository files changed beyond this document. Probes lived in the session scratchpad
(`orch\`, `sib\`), one scratch `projects[...]` entry was written to `~/.claude.json` for the
import-gate probe and **removed**; the scratch junction was removed; `~/.codex/config.toml` was
checked after the `codex exec` runs and holds no scratch entry (the `-c projects.… trust_level`
override was per-run only). Six `codex exec` calls (~5 K tokens each) and nine `claude -p --model
haiku` calls were spent. No `bin-*` directories were created (no build was run).
