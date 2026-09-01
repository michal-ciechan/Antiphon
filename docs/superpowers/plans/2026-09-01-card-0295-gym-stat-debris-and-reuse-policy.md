# CARD-0295 — Cleanup one-off gym-stat boards/agents; document reuse-first dispatch

**Date:** 2026-09-01 (Plan pass, task 625efca4 — design only; nothing cleaned up, no docs edited)
**Card:** CARD-0295 "Cleanup 41 one-off gym-stat boards/agents from raw POST /api/agents; document default-to-2-3-reusable-agents policy"
**Census:** GET `/api/agents`, `/api/boards`, `/api/projects` against localhost:17202, 2026-09-01. Live counts: 65 agents, 43 boards, 31 projects.

**Sources:** `server/Api/Endpoints/{Agent,Board,Project}Endpoints.cs`, `server/Application/Services/{AgentService,BoardService,ProjectService,EntityArchive,ProjectCascade}.cs`, `docs/orchestration-loop.md` §2, `server/Bundles/orchestrator.md`, `scripts/delegate.ps1`, `.claude/skills/antiphon-delegate/SKILL.md`, CARD-0291 plan (`d1c08c82`), CARD-0293 verdict.

This plan does not re-litigate CARD-0291 (named children via raw `POST /api/agents` never report back) or CARD-0293 (those creates sent `remoteControlEnabled: true` for a 26–27 Aug cohort). It cleans the debris that pattern left, and steers future dispatch onto primitives that **already work**.

---

## Decision

Two halves, in this order: **document first, then clean**. Cleaning without steering lets Gym Stat Orchestrator refill the list; documenting without CARD-0291's unbuilt `-Agent` pin is still enough, because the working reuse path today is the pool plus `-OnAgent`, not a new named agent per feature.

1. **Docs (S1, S2).** Write a reuse-first default into `docs/orchestration-loop.md` §2 and `server/Bundles/orchestrator.md`. Do **not** document `delegate.ps1 -Agent` — that flag does not exist yet (CARD-0291 S2, plan-only). Base the rule on pool dispatch and `-OnAgent`. Do not pre-create 2–3 named standing workers as part of this card: without the pin, prompting them via session messages is the CARD-0291 trap.
2. **Cleanup (S3).** Delete the one-off **agents** (hard delete is the only agent-removal API), then **archive** the one-off **projects** (hides their boards). Leave the real `gym-stat` project, the `Gym Stat` board (31 cards), Gym Stat Orchestrator, and anything Running. Dry-run script, then `-Execute`.

Do not invent an agent-archive endpoint. Do not hard-delete boards or projects. Do not touch `C:\src\gym-stat-*` directories on disk.

---

## API facts (checked, not guessed)

A prior skim of this card guessed there was no board/agent archive or delete. The routes exist. Semantics matter more than presence.

| Target | Route | What it actually does | Usable here? |
|---|---|---|---|
| Agent | `DELETE /api/agents/{id}` (`AgentEndpoints.cs:104` → `AgentService.DeleteAsync:677`) | Hard delete. Unassigns cards, drops `CardWorkflowRun`s. **No Running / AlwaysOn / live-session guard.** `AgentTasks.AgentId` has no FK, so history rows keep a dangling guid. Sessions are keyed by `Agent.PersistentSessionId`, not an `AgentId` FK — a live session would be orphaned, not stopped. | **Yes, after a live re-check excludes Running / AlwaysOn / `liveSession != null`.** UI already exposes this (`AgentSettingsModal` "Delete agent"). |
| Agent | archive | **Does not exist.** No `POST /api/agents/{id}/archive`. | No. Do not invent one on this card. |
| Board | `POST /api/boards/{id}/archive` (`BoardEndpoints.cs:92`, CARD-0217 S9) | Reversible hide. **409 if any agent is attached** (`EntityArchive.EnsureBoardArchiveableAsync:45-54`), or if a live session / open task exists. Reason required. | Yes, **only after** the board's agents are deleted. Not the preferred call — project archive hides the boards in one step. |
| Board | `DELETE /api/boards/{id}` (`BoardEndpoints.cs:55` → `BoardService.DeleteAsync:170`) | Hard-deletes the board subtree. **Agents are detached, not deleted** (`ProjectCascade.DeleteBoardsAsync:104-110`). Empty project is then deleted too. Startup `EnsureAgentBoardsAsync` (`AgentService.cs:613`, `Program.cs:636`) **recreates a board** for every remaining standing agent with `BoardId == null`. | **No.** Deleting the board first is worse than doing nothing: the debris comes back on the next AppHost start. |
| Project | `POST /api/projects/{id}/archive` (`ProjectEndpoints.cs:118`) | Reversible hide. Hides the project **and its boards** from default lists (`BoardProjectArchiveTests`). **409 if any agent is attached.** Reason required. | **Yes, after agent delete.** Preferred hide. |
| Project | `DELETE /api/projects/{id}?force=true` | Irreversible. Agents detached, not deleted. Workflows block. | No. Archive is enough. |

There is no client hook for board/project archive (`useArchiveCard` exists; `useArchiveBoard` / `useArchiveProject` do not). `scripts/project.ps1` has `new` / `readiness` / `catalog` only. Cleanup is therefore a scripted HTTP pass, not a click path.

**How the 33 boards appeared.** `POST /api/agents` without `BoardId` looks up a project by **exact** working-directory match (`FindProjectForWorkingDirectoryAsync:953-958`). A path like `C:\src\gym-stat-auth` does not match `C:/src/gym-stat`, so create minted a new project and, on the 2026-08-31 07:35:19Z `EnsureAgentBoardsAsync` sweep, a same-named board. Agents created 26–29 Aug; every one-off gym-stat board timestamp is 07:35:19–20Z the 31st. The later CARD-0291 children (`dupmachine-*`, `setupmockups`, `addonpin-plan`) landed on the real Gym Stat board instead.

---

## Frozen census (2026-09-01) — execute-time re-check required

**Keep, never in the script allowlist:**

| Name | Why |
|---|---|
| Project `gym-stat` / board `Gym Stat` (31 cards, 3 still open) | Real card history. Deletion-impact: 10 agents attached, `canDelete` is true — that is a footgun, not permission. |
| `Gym Stat Orchestrator` (`cec87812-…`, AlwaysOn, Running, RC true) | The orchestrator seat. |
| `gym-stat-addonpin-plan` (`48de1766-…`, Running, Gym Stat board) | Live session. Revisit after it stops. |
| `gym-stat-setupmockups` (`8aedb019-…`, Running, Gym Stat board) | Live session. Revisit after it stops. |
| Antiphon, AZ Care, Family, school-revision, Slack Test, Torquay Leander, ClaudeBot-Antiphon, Codeperf, antiphon-check-interpreter, and every `task-*` pool row | Out of scope. |

Spot-check: `GET /api/boards/{id}?includeArchived=true` for `gym-stat-auth-plan` and `gym-stat-accountgymforms` returned **0 cards including archived**.

### Cohort A — 33 one-off boards, each with exactly one same-named agent, 0 cards

Delete the agent, then archive the project (21 projects; several hold a plan+code pair).

| Agent / board | Status | Kind | RC | Project |
|---|---|---|---|---|
| gym-stat-accountgymforms | Failed | Grok | | gym-stat-accountgymforms |
| gym-stat-auth-code | Stopped | Codex | | gym-stat-auth |
| gym-stat-auth-plan | Failed | ClaudeCode | true | gym-stat-auth |
| gym-stat-datamodel-code | Stopped | Codex | | gym-stat-datamodel |
| gym-stat-datamodel-plan | Stopped | ClaudeCode | true | gym-stat-datamodel |
| gym-stat-deploy-code | Failed | Codex | | gym-stat-deploy |
| gym-stat-deploy-plan | Stopped | ClaudeCode | | gym-stat-deploy |
| gym-stat-fieldeditor-code | Stopped | Codex | | gym-stat-fieldeditor |
| gym-stat-fieldeditor-plan | Failed | ClaudeCode | | gym-stat-fieldeditor |
| gym-stat-floorplan-code | Stopped | ClaudeCode | true | gym-stat-floorplan |
| gym-stat-floorplan-plan | Stopped | ClaudeCode | true | gym-stat-floorplan |
| gym-stat-floorplanux | Stopped | ClaudeCode | | gym-stat-floorplanux |
| gym-stat-floorspace-code | Stopped | Codex | | gym-stat-planspace |
| gym-stat-floorspace-plan | Stopped | ClaudeCode | | gym-stat-planspace |
| gym-stat-googlesignin-code | Stopped | Grok | | gym-stat-googlesignin |
| gym-stat-googlesignin-plan | Stopped | ClaudeCode | | gym-stat-googlesignin |
| gym-stat-install-code | Stopped | Codex | | gym-stat-install |
| gym-stat-install-plan | Failed | ClaudeCode | | gym-stat-install |
| gym-stat-logging-code | Stopped | ClaudeCode | true | gym-stat-logging |
| gym-stat-logging-plan | Failed | ClaudeCode | true | gym-stat-logging |
| gym-stat-machinetypeeditor | Stopped | Codex | | gym-stat-machinetypeeditor |
| gym-stat-memberroles-code | Stopped | Codex | | gym-stat-memberroles |
| gym-stat-memberroles-plan | Failed | ClaudeCode | | gym-stat-memberroles |
| gym-stat-mock | Stopped | ClaudeCode | true | gym-stat-mock |
| gym-stat-numericoverflow | Stopped | ClaudeCode | | gym-stat-numericoverflow |
| gym-stat-offline-code | Failed | Codex | | gym-stat-offline |
| gym-stat-offline-plan | Stopped | ClaudeCode | | gym-stat-offline |
| gym-stat-privacypolicy | Stopped | ClaudeCode | | gym-stat-privacypolicy |
| gym-stat-scaffold-code | Stopped | Codex | | gym-stat-scaffold |
| gym-stat-scaffold-plan | Stopped | ClaudeCode | true | gym-stat-scaffold |
| gym-stat-tech | Failed | ClaudeCode | true | gym-stat-tech |
| gym-stat-uireview-auth | Stopped | ClaudeCode | | gym-stat-uireview-auth |
| gym-stat-uireview-flows | Stopped | ClaudeCode | | gym-stat-uireview-flows |

Ids for the executor live in the S3 script allowlist, re-fetched at run time rather than trusted from this table.

### Cohort B — 7 Stopped/Failed named children on the real Gym Stat board

Delete the **agent only**. Do not archive `Gym Stat` / `gym-stat`.

| Agent | Status | Kind |
|---|---|---|
| gym-stat-dupmachine-impl | Stopped | Grok |
| gym-stat-dupmachine-plan | Stopped | Codex |
| gym-stat-fieldkeyautogen | Failed | ClaudeCode |
| gym-stat-googledarktheme | Failed | ClaudeCode |
| gym-stat-googleusername | Stopped | ClaudeCode |
| gym-stat-weightsteps-impl | Stopped | Grok |
| gym-stat-weightsteps-plan | Stopped | ClaudeCode |

### Out of band (do not script)

`C:\src\gym-stat-scaffold` exists and holds `client/`, `server/`, `PLAN.md`, `node_modules` (not a git checkout). `C:\src\gym-stat-tech` and `C:\src\gym-stat-weightsteps` also exist. Every other one-off project path is missing on disk. Archiving the Antiphon project row does not delete a directory. Leave the filesystem to a human.

CARD-0293 item 1 (clear `remoteControlEnabled` on nine named gym-stat workers) is **subsumed** for those nine once Cohort A is deleted. Gym Stat Orchestrator stays `true`. Item 2 (`AgentAddWorkModal` checkbox default) is untouched.

---

## Slices

### S1 — `docs/orchestration-loop.md` §2: reuse-first, on today's primitives

Owner per AGENTS.md's "Read before changing" table. Insert a short subsection immediately after the existing "Launching an agent" paragraph (`docs/orchestration-loop.md:134-141`), before `## 3. Writing a brief`.

The current text is the trap CARD-0291 named: it documents `POST /api/agents` + `/start` with no warning that this mints identity (and, with a unique working directory, a project and a board) and that **no `[task done]` will ever arrive**.

Write the rule in terms of what already works:

- **Default: `delegate.ps1`.** Unrelated new work needs nothing special — the warm pool reuses an idle agent in the same directory (compacted first) and spawns a fresh ephemeral delegate only when none fits. Sequential follow-up that must keep context: `-OnAgent <taskId>` (already on the script and in the skill). Parallelism on one model: let the pool spawn another, or pass `-Worktree`. That *is* the "2–3 reusable workers per directory+model+tier, scale only for real parallelism" policy, implemented by the pool rather than by named rows.
- **`POST /api/agents` is for a standing identity**, not a unit of work: orchestrator seat, channel-bound agent, check interpreter, or a human-facing named worker that should outlive a card. Pass an existing `BoardId` (the project's real board). A unique `workingDirectory` that is not a real checkout is how 21 extra `gym-stat-*` projects appeared.
- **Do not** tell the reader to create 2–3 named standing workers and prompt them via session messages. That is CARD-0291's incident. Do **not** mention `delegate.ps1 -Agent` — it is not built. One seam sentence is enough for the later pin: work you need to hear finish is a task; pinning a task onto an existing named standing agent is CARD-0291 and is not available from `delegate.ps1` yet. Until then, use the pool / `-OnAgent`.
- Leave the CARD-0007 `modelLevel` wire-format warning in place.

Do not restatement in `AGENTS.md`, `docs/agent-kinds.md`, or the skill. The skill already documents `-OnAgent` and pool reuse (`.claude/skills/antiphon-delegate/SKILL.md` "Follow-up work"). CARD-0291 S3 will extend this same §2 paragraph with the `-Agent` sentence when that flag ships — write S1 so that addition is an insert, not a rewrite.

### S2 — `server/Bundles/orchestrator.md`: the live steering

Gym Stat Orchestrator is AlwaysOn ClaudeCode and keeps launch-time bundles until relaunch. The bundle never mentions the named-agent vs task distinction.

Add 3–5 sentences next to the existing "Reports arrive between your turns" / "Do not treat the absence of a `[task … done]` note" block (`orchestrator.md:25-35`), bundle voice, ASCII-only:

- Child work goes through `delegate.ps1` (pool by default, `-OnAgent <taskId>` to keep context).
- Do not `POST /api/agents` per feature, and do not invent a unique working directory for a child.
- A child started via `POST /api/agents` and prompted via session messages never reports back.

Do not mention `-Agent`. CARD-0291 S3 will add that clause to this same paragraph.

Budget: CARD-0291 already measured worst-case composed bundles at 9,198 against a 30,000-char command-line cap; another ~500 characters is fine. Still run `InstructionBundleTests` after the edit (`dotnet run --project tests/Antiphon.Tests --treenode-filter "/*/Antiphon.Tests.Application/*InstructionBundle*" --property:OutputPath=bin-card0295/`).

After S2 lands, **relaunch Gym Stat Orchestrator** so it picks up the bundle (S4). A standing AlwaysOn agent will not see the file until then.

### S3 — Operator cleanup script, dry-run default

New `scripts/cleanup-gym-stat-one-off-agents.ps1`, ASCII-only, same shape as `scripts/reap-orphaned-pty-hosts.ps1`: census + verdict without `-Execute`; `-Execute` performs the writes.

Behaviour:

1. `GET /api/agents`, `/api/boards`, `/api/projects` (and `/api/projects/{id}/deletion-impact` before each project archive).
2. Allowlist is name-based plus the keep-set above, **not** a frozen guid list. A row whose live `status` is Running, `alwaysOn` is true, `liveSession` is non-null, or whose board is `Gym Stat` / project is `gym-stat` **and** which is not in Cohort B, is printed under "protected" and skipped. Cohort B matches by name on the Gym Stat board and Stopped/Failed only.
3. Order, per candidate: `DELETE /api/agents/{id}` first; then `POST /api/projects/{id}/archive` with `{ "reason": "CARD-0295 one-off POST /api/agents debris", "archivedBy": "card-0295" }` for each Cohort A project once its agents are gone. 409 on archive means an agent was missed — stop, do not force-delete.
4. Never `DELETE /api/boards`, never `DELETE /api/projects`, never `PATCH`, never `POST /stop` on AlwaysOn.
5. Print a before/after count. Exit 0 on dry-run; exit 1 if any `-Execute` call failed; exit 2 if the API did not answer.
6. Do not delete or archive filesystem paths.

Run dry-run, paste the census into the card, then `-Execute` once. Re-GET `/api/boards` and `/api/agents` afterwards: 33 gym-stat one-off boards gone from the default list (`includeArchived=false`), 7 Cohort B agents gone, Gym Stat board still 31 cards, Orchestrator still Running.

No `Antiphon.Tests` changes. This is a one-shot operator script against the live API, not a new product feature.

### S4 — Relaunch the orchestrator so S2 takes effect

Out of the git repo, in the execution brief:

- Stop/start Gym Stat Orchestrator (or AppHost restart if that is how AlwaysOn is bounced) so `orchestrator.md` is composed into the next launch.
- Optional one-liner on its `Details` (standing-job metadata, CARD-0283): "Dispatch children with delegate.ps1; do not POST /api/agents per feature." That is a `PATCH /api/agents/{id}` the operator can do by hand; the script in S3 must not PATCH.

---

## What this card does not do

- **CARD-0291 S1/S2** (`CreateAgentTaskRequest.Agent`, `delegate.ps1 -Agent`). Not built; do not document as if it were. After it ships, an orchestrator that wants persistent named workers may create **at most 2–3 on the existing Gym Stat board** (explicit `BoardId`) and pin tasks with `-Agent`. That is a follow-up, not this card.
- **CARD-0293** modal default / RC-on-Add-Work. Overlapping RC-true rows in Cohort A go away with the agent delete.
- **CARD-0239** (Backlog) — detect standing agents that outlived their task. Detection, not prevention; leave it.
- **Agent-archive API.** Missing, and hard delete is already the sanctioned UI path for a Stopped/Failed named agent. Filing a new card for agent-archive is optional and not required to finish 0295.
- **Filesystem.** `C:\src\gym-stat-scaffold` in particular looks like real scratch; a human decides.

---

## Test matrix

| Layer | Test |
|---|---|
| `Antiphon.Tests` | `InstructionBundleTests` still pass after S2 (composition budget) |
| Manual | S3 dry-run prints 40 agent candidates (33+7), 21 projects, 0 protected-in-allowlist collisions; keep-set named |
| Manual | S3 `-Execute` then default `GET /api/boards` no longer lists the 33; `GET /api/boards?includeArchived=true` still can; Gym Stat 31 cards; Orchestrator Running |
| None | No new unit tests for the operator script |

Run TUnit via `dotnet run --project tests/Antiphon.Tests`, never `dotnet test`, with `--property:OutputPath=bin-card0295/` (forward slash) and delete those `bin-card0295` directories afterwards.

---

## Sequencing against CARD-0291

CARD-0291 is Review, plan-only (`d1c08c82`). This card's docs are the steering that card also wants, written against **today's** flags so they are true the moment they land. When 0291 S3 edits the same two paragraphs, it adds the `-Agent` clause; it should not replace the reuse-first default.

Cleanup does not wait on 0291 implementation. The regeneration risk after S1/S2 is "orchestrator ignores the bundle until relaunch" (S4), not "pool cannot do the work".
