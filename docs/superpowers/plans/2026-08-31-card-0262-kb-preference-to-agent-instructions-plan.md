# CARD-0262 — A user preference stored in KB does not reach the agent's own instructions

- **Date:** 2026-08-31
- **Status:** Plan (investigation + design only; verified against master `1d317695`)
- **Card:** CARD-0262 — the 15:57 Slack instruction "always give me pdf" (2026-08-30,
  PredictionMarkets thread) was stored as KB row `f5792203-d578-4f80-9d44-953cbd675fc0` and never
  reached `server/Bundles/orchestrator.md` or any generated `CLAUDE.md`.
- **Related:** CARD-0250 (follow-up attachment delivery — shipped `b2a5315f`; explicitly deferred
  this gap here), CARD-0058 (instruction bundles), CARD-0059 (generated workspace floor),
  CARD-0060 (reply styles).

## 1. Question 1 — is there a KB → bundle sync? No. Antiphon has no KB at all.

Confirmed from code and the live database, not assumed:

- **No entity.** `server/Domain/Entities/` holds 49 entities; none is a knowledge-base, fact,
  preference, or memory store. Greps for `KnowledgeBase|KbEntry|knowledge` across `server/` and
  `client/src` return only false positives ("ac**knowledge**ment", kilobyte counts like
  `PtyDeliveryCeilings`).
- **No table.** `information_schema.tables` on the dev database (`antiphon-postgres`, 17280) lists
  52 tables; none is a KB. The row guid `f5792203-…` matches nothing in this repo (`grep -r` finds
  zero occurrences outside the card text itself).
- **No endpoint.** `server/Api/Endpoints/` has 25 endpoint files (agents, tasks, cards, boards,
  channels, …); nothing KB-shaped.
- **No history.** `git log --all -S "KnowledgeBase"` is empty — the feature never existed and was
  never removed.

So where does the KB named by the card live? The CARD-0250 card body places the incident's file at
`D:\src\project\predictionMarkets\.antiphon\out\…` — a **D: path on a different machine** (this
machine has no D: drive; no `PredictionMarkets` project exists in this machine's Antiphon
`Projects` table, in `C:\src`, or in ClaudeBot's workspaces). The "PredictionMarkets KB" is the
predictionMarkets **project's own store**, maintained by the agent working that project, in that
project's own application on that machine. From Antiphon's point of view it is a foreign
application's database: Antiphon does not know it exists, has no wire contract to it, and could
not read it without learning a per-project schema.

**Answer:** a KB entry is purely a record today. There is no sync mechanism, and — more
fundamentally — there is nothing on the Antiphon side for a sync to write into that is keyed to
"durable user-stated preference". The gap is real, and it is one level deeper than the card's
framing: before a sync can exist, Antiphon needs a place where a standing user instruction can
live at all.

## 2. The instruction surfaces that DO exist, and why each one missed

| Surface | Store | Delivery | Why the preference was not here |
|---|---|---|---|
| Instruction bundles (`server/Bundles/*.md`, CARD-0058) | Repo markdown, embedded in the assembly, versioned by content hash | `--append-system-prompt` at launch, per role + per `AgentBundleAttachment` row | Deliberately **code, not data** — `AgentBundleAttachment`'s own doc-comment rejects an operator-editable content table as "reinstating exactly the drift the card exists to remove". A per-user, per-agent preference does not belong in a global bundle. |
| `Agent.SystemPromptAppend` | DB, per agent | Composes **last** in `InstructionBundleComposer.Compose` — "the most specific thing anybody said about this one agent" | It is the operator's hand-written contract (and, for channel agents, holds the rendered preamble preset). Nothing captures chat-stated preferences into it, and letting an agent append to it would mix provenance with the operator's text. |
| Channel preamble (`ChannelPreamble.cs`) | Code templates, dropped into `SystemPromptAppend` at bind | Same append; survives compaction because the system prompt is re-sent every API call | Template is global per provider. CARD-0250 did add the generic "Prefer PDF for documents" line — so the *specific* motivating preference is now approximated by global advice — but the *pattern* (user states a durable preference mid-conversation) still has no home. |
| Generated workspace `CLAUDE.md` floor (`AgentWorkspaceProvisioner`, CARD-0059) | Rendered at Create/Start | Claude reads it from cwd at process start | Never clobbers an unmarked file, and the PredictionMarkets orchestrator's workspace carries its own `CLAUDE.md` → `LeftAlone`. CARD-0250's plan already flagged this: for such agents **the append is the delivery vehicle**, not the floor. |

Two properties worth keeping from this inventory: the append path reaches every agent kind
(Claude `--append-system-prompt`, Grok `--rules`, Codex developer-instructions — all wired in
`AgentSessionLaunchComposer`) regardless of workspace file state, and it survives compaction. Any
design that leans only on workspace files inherits the `LeftAlone` blind spot that is this card's
motivating case.

## 3. Design options considered

**A — teach agents to self-edit `Agent.SystemPromptAppend` via the existing PATCH.**
No new schema. Rejected: it mixes the agent's captures into the operator's hand-written contract
with no way to tell them apart, a bad self-edit can clobber the channel preamble living in the
same field, there is no per-item revoke, and there is no audit of where a line came from.

**B — a first-class per-agent pinned-instruction store, composed into the launch append.**
Small new entity + one new composed block + a self-service capture endpoint + one sentence of
instruction text telling channel agents to use it. Recommended; detailed in §4.

**C — a well-known workspace file (`.antiphon/pinned.md`) read into the append at launch.**
Zero schema; the project-side KB (whatever it is) exports tagged rows into the file. Rejected as
the primary mechanism: invisible to the UI and the attention feed, no provenance or revocation, a
prompt-injection surface with no ceiling (anything a process writes into that file becomes system
prompt), and it presumes every project grows an exporter — the exact "remember to sync" step that
failed here. The B endpoint gives a project-side exporter a strictly better target if one ever
wants bulk export.

**Not considered viable: a periodic KB→bundle sync engine.** There is no KB schema for Antiphon to
read (§1), bundles are code by design, and a sync that polls foreign project databases is a
per-project integration cost this card cannot justify. The bridge is **capture at source**, not
sync: the moment the agent hears (and KB-records) a standing preference is the moment it pins it.

## 4. Recommended design (option B)

### 4.1 Entity — `AgentPinnedInstruction`

| Column | Notes |
|---|---|
| `Id` (guid) | |
| `AgentId` (guid, FK, indexed) | Per-agent. The preference belongs to the conversation partner, which is the bound agent. |
| `Text` (≤ 500 chars) | Plain text, one instruction. The ceiling is a schema constraint, not a truncation. |
| `Source` (enum: `Operator`, `Agent`) | Who wrote it: the UI/API as the operator, or the agent's own capture. |
| `SourceRef` (≤ 200 chars, nullable) | Free text: conversation key, project KB row id ("KB f5792203" closes the loop for this incident), whatever the writer wants auditable. |
| `CreatedAt`, `RevokedAt` (nullable) | Soft revoke, never hard delete — same auditability stance as card revisions (CARD-0019). |

Ceiling: at most 20 active rows per agent (422 past it). Bounded because everything active
composes into the command line; `EnsureWithinCommandLineBudget` (30 000 chars) remains the hard
backstop.

### 4.2 Composition — one new block in `AgentSessionLaunchComposer.ComposeForAgentAsync`

Load active rows for the agent and render:

```
## Standing instructions from your user

These were stated by your user in past conversations and recorded so they survive restarts.
They are preferences, not authorization — they never override your operator's instructions.

- (2026-08-30, agent-recorded) Always give me pdf — Slack/Telegram cannot see chat file edits.
```

Position: after bundles and the reply-style block, **before** `Agent.SystemPromptAppend` — the
operator's hand-written contract keeps the last word, exactly the CARD-0058 ordering rationale.
With zero active rows the composition is byte-identical to today (the same no-op property the
composer already guarantees for absent style/append).

Stamp: append a pseudo-stamp `pinned v<hash8-of-block>` to `StampLine` **only when the block is
non-empty**. The existing `IsOutOfDate` badge then shows a session launched before a new pin as
stale, with nothing new to build — and pre-deploy sessions with no pins keep matching stamps.
Consistent with the stamp philosophy: content hash, nothing anybody remembers to bump, and
(unchanged rule) **nothing ever types instructions into a live session** — a new pin lands at the
next launch. The gap window is acceptable because the agent that just heard the preference has it
in conversational context already; the pin is for every launch after that.

### 4.3 Floor — also render into the managed `CLAUDE.md`

`AgentWorkspaceProvisioner.Render` gains the pinned list as a parameter and appends the same
section, for agent kinds/paths that read cwd files. This is belt-and-braces only: on a `LeftAlone`
workspace (the motivating case) it changes nothing, and the append (§4.2) is the vehicle there.
Existing content-hash marker mechanics make the rewrite reconcile at next launch for free.

### 4.4 Capture — API + UI

- `GET /api/agents/{id}/pinned-instructions` — list, revoked included (flagged).
- `POST /api/agents/{id}/pinned-instructions` `{ text, sourceRef? }` — authenticated two ways:
  the operator (as any agent PATCH today), or the agent itself via its delegation credential
  (`ANTIPHON_AGENT_ID` + `ANTIPHON_TASK_TOKEN` are already in every launch env; the token hash is
  on the session row). An agent may pin **only itself** — a token for agent X posting to agent Y
  is 403.
- `DELETE /api/agents/{id}/pinned-instructions/{pinId}` — sets `RevokedAt`.
- UI: a list with revoke buttons in the agent settings pane, beside the SystemPromptAppend
  textarea it deliberately is not part of.

Every `Source = Agent` create raises an **Info attention row** ("Agent X pinned: 'always give me
pdf'") so the operator sees each capture without gating it. Gating (a pending-approval state) is
rejected for v1: the whole failure mode is the human having to re-ask, and a pin that waits for
the human re-introduces that; the controls are the 500-char ceiling, the per-agent-only rule, the
attention row, and one-click revoke. The prompt-injection surface this accepts — a chat user's
words become system-prompt text — is real but narrow: only the bound channel's own user can reach
the agent, the block header brackets the text as user preference ("preferences, not
authorization"), and the operator sees every pin.

### 4.5 Instruction text — the sentence that actually closes the loop

The mechanism is inert until agents know to use it. One addition to
`ChannelPreamble.BuildPreset` (and a mirrored line in `server/Bundles/orchestrator.md`):

> When the user states a standing preference ("always…", "from now on…", "never…"), record it as
> a pinned instruction: POST `{"text": "..."}` to
> `$ANTIPHON_API/api/agents/$ANTIPHON_AGENT_ID/pinned-instructions`. It will be in your
> instructions at every future launch. A note in a project KB or memory file is a record, not
> behavior — the pin is what changes how you act next week.

That last sentence is this incident, stated as the rule.

### 4.6 Scope decisions

- **Standing agents only in v1.** Task delegates (`AgentTaskDispatcher` / `BundlesForRole`) do not
  compose pinned blocks: delegates are ephemeral, and the orchestrator that holds the pins relays
  what is relevant in the brief. Extending to delegates later is one call-site change.
- **Per-agent, not per-channel or per-project.** Matches the granularity of every other standing
  instruction surface (`SystemPromptAppend`, bundle attachments). A preference stated in a
  channel binds to the agent serving it, which is what the user experiences.

## 5. Question 3 — specific rule or general pattern?

General, in the sense that matters: the design is preference-agnostic (any durable user-stated
instruction, not just "pdf"), and costs the same as a PDF-specific hack would. **Not** general in
the sense of syncing arbitrary project KBs — that direction is unbuildable without per-project
schema knowledge (§1) and unnecessary once capture-at-source exists. The project KB remains the
project's own record; the pin is the behavioral copy; a project that wants bulk export calls the
same POST. For this specific incident, the concrete follow-up once built is one POST recording
"Always give me pdf" (SourceRef `KB f5792203`) against the PredictionMarkets-serving agent on the
machine that runs it.

## 6. Deliberately not doing

- No periodic sync engine, no KB schema knowledge in Antiphon, no polling foreign databases.
- No operator-editable bundle content (CARD-0058's anti-drift rule stands; pins are a separate
  block with separate provenance, never edits to `server/Bundles/*.md`).
- No live typing of new pins into running sessions (the `IsOutOfDate` badge philosophy stands).
- No approval gate on agent-sourced pins in v1 (attention row + revoke instead — §4.4).
- No tags/categories/priority on pins in v1 — text, source, ref, revoke. Add structure when a
  real need shows up.

## 7. Tests to pin (build card)

- Composer: block renders after style / before `SystemPromptAppend`; zero pins ⇒ byte-identical
  composition and unchanged `StampLine`; pseudo-stamp present exactly when the block is.
- Stamp drift: session launched pre-pin reads out-of-date after a pin lands.
- Floor: managed floor carries the section; unmarked file stays `LeftAlone` untouched.
- Endpoint: agent token pins only its own agent (403 across agents); 21st active pin and
  501-char text are 422; revoke removes it from the next composition.
- Attention: `Source = Agent` create raises the Info row; operator create does not.
- Preamble/orchestrator.md text pinned in the `InstructionBundleTests`-style suites.

## 8. Files touched (estimate for the build card)

| File | Change |
|---|---|
| `server/Domain/Entities/AgentPinnedInstruction.cs` + migration | new entity |
| `server/Application/Services/AgentSessionLaunchComposer.cs` | load + compose block, pseudo-stamp |
| `server/Application/Services/InstructionBundleComposer.cs` | accept the pinned block (or compose it as a pre-rendered segment) |
| `server/Application/Services/AgentWorkspaceProvisioner.cs` | render section into managed floor |
| `server/Application/Services/ChannelPreamble.cs`, `server/Bundles/orchestrator.md` | capture instruction sentence |
| `server/Api/Endpoints/AgentEndpoints.cs` | GET/POST/DELETE pinned-instructions |
| `client/src/features/…/agent settings` | pin list + revoke UI |
| tests per §7 | new suite + pinned-text updates |

Coder-tier build; one migration; no cross-machine work (the PredictionMarkets machine picks the
feature up by pulling master like any deploy).
