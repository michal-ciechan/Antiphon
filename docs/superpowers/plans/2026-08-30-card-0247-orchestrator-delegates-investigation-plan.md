# CARD-0247 — The orchestrator delegates investigation: nudge at the keyboard, detect after the fact, and stop pretending prose will do it

**Date:** 2026-08-30 · **Status:** plan (Plan pass; nothing built). Ground truth verified against
master `f77d0d6`, Claude Code **2.1.251**, and the live database's transcript of the orchestrator
session `cefed08a` over its last 30 hours. Every number below was measured today on this machine;
every claim about Claude Code's hook system was either read from the current reference at
`code.claude.com/docs/en/hooks` today or **proven by running a hook against the real CLI** (§1.1).

## Verdict up front

**Build both — a non-blocking `PreToolUse` nudge hook AND a server-side detection sweep — scope
the hook to Claude Code only, make the sweep provider-agnostic by construction, and leave the
parent-orchestrator-folder restructuring out of this card.** Specifically:

1. **The nudge is implementable, and proven.** Claude Code's `PreToolUse` hook accepts
   `hookSpecificOutput.additionalContext`, which reaches the model *without* blocking the call —
   measured today: a hook returning `permissionDecision: allow` plus a codeword had the model repeat
   the codeword in its answer, and the tool ran (§1.1). The hook also receives `transcript_path`,
   `session_id`, `cwd` and the launching process's environment (`ANTIPHON_TASK_ID` was visible
   inside the hook), so the "has this session dispatched recently" condition needs **no external
   state** — the hook reads the tail of the session's own JSONL.
2. **The card's trigger sketch is wrong in two load-bearing ways, and the measurement says so.**
   (a) This orchestrator dispatches through `pwsh scripts/delegate.ps1` in the **PowerShell/Bash
   tools, never the `Agent` tool** — 27 dispatches and **zero** `Agent` calls in 30 hours (§1.2) —
   so "has an Agent-tool call happened" would never be true. (b) Most of its "reading" is **Bash**
   (`sed -n`, `grep -n`, `cat`) on source paths, not the `Read` tool: 76 of 108 source reads went
   through Bash. A `matcher: "Read|Grep|Glob"` hook would have missed the CARD-0246 trace's first
   four steps. The condition has to classify *commands*, not just tool names (§3.2).
3. **"This turn" is the wrong scope; "since the last delegate report, and not named in it" is the
   right one.** Reports arrive as their own user prompts (`[task <id> done] …`, typed in by the
   queue), so the verification reads always happen in a turn that contains no dispatch. Measured
   over 108 source reads: **11 named a file the most recent delegate report mentioned; 97 did not**
   (§1.3). The CARD-0246 failure is an 8-read run over 90 seconds, four files, started by the human
   prompt *"Maybe look into and fix 246"*, followed by an `Edit` — no dispatch anywhere near it.
4. **A hook alone is not enough, for the same reason the memory rule was not enough: it can only
   speak once, at the keyboard, and the session can rationalise past it.** The sweep is the part
   that makes the outcome *visible* after the fact — a `CardNeedsDecision`-style Warning row on the
   attention feed naming the session, the run length, the files, and whether a nudge fired — which
   is the shape this repo already chose for scope drift (CARD-0063), CI (CARD-0124) and card
   transitions (CARD-0040): detect and surface, never a gate that can be bypassed silently. The sweep
   also covers the one population the hook cannot: it reads `TranscriptEntries`, so it sees any
   Antiphon-launched session of any kind; the hook covers the one population the sweep cannot: a
   manually-launched `claude` with no DB row. Together they cover everything.
5. **Scope is Claude-only at the hook layer and that is not a compromise.** An orchestrator is
   `ClaudeCode` only in this system today (`docs/agent-kinds.md:44`, `:353` — Grok and Codex as
   orchestrator are *refused*), so a Codex/Grok hook would protect nobody. Both have hook systems
   that could carry the same nudge later (§1.4), and the sweep already covers their transcripts.
   Do not build three hooks; file nothing for the others until one of them can be an orchestrator.
6. **The parent-orchestrator-folder idea should wait**, agreeing with the card. It is a real
   structural benefit (it is exactly the ClaudeBot agent-workspace convention applied to the
   orchestrator, and it removes the CARD-0006 shared-transcript-root hazard for the operator's own
   session), but §5 counts what breaks today — card identifier resolution, the delegate's default
   working directory, worktree dispatch, skill discovery, AGENTS.md loading, and every one of the 89
   `pwsh -File scripts/...` invocations the docs assume run from the repo root — and it does not
   buy an *enforcement* the hook's environment discriminator (§3.1) does not already give. Build it
   only if the nudge + sweep measurably fail to move the number in §1.3, and as its own card.
7. **Sharpen `docs/orchestration-loop.md` regardless** — replacement text is drafted in §6. The
   current doc says "spend it on judgement, not on archaeology" once, in the preamble, and never
   again; the rule that actually bit (diagnosis is a Debug delegate; verification is reading a
   *named* diff) lives only in one operator's private memory file.

## 1. Ground truth

### 1.1 What Claude Code's hook system exposes (verified against 2.1.251, today)

From the reference, confirmed by the probe below:

| Question in the brief | Answer |
|---|---|
| Can `PreToolUse` inject a non-blocking message? | **Yes.** JSON output `{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"allow","additionalContext":"…"}}` allows the call and adds the text to the model's context. Exit 2 / `deny` is the blocking form (the one `DelegationWorktreeService.DenyHookSettingsJson` already uses). |
| What context does the hook get? | stdin JSON with `session_id`, `transcript_path`, `cwd`, `permission_mode`, `prompt_id`, `hook_event_name`, `tool_name`, `tool_input`, `tool_use_id`. Inside a subagent it also carries `agent_id`/`agent_type`. The hook process inherits the CLI's **environment** — the probe saw `ANTIPHON_TASK_ID` — and `CLAUDE_PROJECT_DIR`. |
| Session-scoped state? | Not a hook API, but `transcript_path` is the session's own JSONL, which already holds every tool call and every delivered report, in order. Parsing its tail is both the state and the audit trail; a per-session scratch file (`%TEMP%\antiphon-hooks\<session_id>.json`) is only needed to remember "already nudged for this run". |
| Do project hooks leak to other sessions? | Hooks in `.claude/settings.json` apply to **every session whose primary working directory is that directory** — the operator's, a Shared-workspace delegate's, a headless `-p` run's. They merge across user/project/local. They do **not** walk up: "Claude Code reads the shared `.claude/settings.json` from the session's primary working directory" (settings reference). Hooks also fire inside subagents (with `agent_id` set). |
| Other useful events | `SessionStart` with matcher `compact` (stdout/`additionalContext` is added as context after a compaction — the exact moment a long session has lost its instructions); `UserPromptSubmit` (sees the report note as it lands; `additionalContext` supported); `PostToolUse` (exit 2 shows stderr to the model but cannot block — an alternative nudge channel; `additionalContext` is *not* honoured there); `SubagentStart`/`SubagentStop`; `PreCompact`/`PostCompact`. |
| Cost | Every matched tool call pays the hook's process start. Measured today: `pwsh` 7 **331–409 ms**, `powershell.exe` 5.1 **238–305 ms**, `node` **56–80 ms**. The hook must be Node (v24.6.0 installed). |

**The probe** (scratchpad, discarded): a `--settings` file with `PreToolUse` matcher `Read|Bash`
running a Node script that logged its stdin and returned `allow` + `additionalContext: "CODEWORD
ZEBRA-7731 …"`; `claude -p --model haiku "read probe.txt …, repeat any codeword"`. Result: the file
was read (tool ran) **and** the answer contained `ZEBRA-7731`. The logged input had exactly the
fields listed above and no `agent_id`. This is the single fact the whole nudge design rests on; the
first slice re-pins it as a headed canary so a CLI upgrade that drops it goes red (§4).

**Precedent on this machine.** There are **no** hooks in `~/.claude/settings.json`,
`C:\src\Antiphon\.claude\settings.json` (it holds only a `links` block), or anywhere in
`C:\src\ClaudeBot` — the only hooks that exist are the two Antiphon *writes*: the deny-all hook in
the check-interpreter's scratch cwd (`CheckInterpretation.cs:57`) and the deny-Edit/Write hook in a
sub-orchestrator's worktree (`DelegationWorktreeService.cs:118`). The "PreToolUse path hook was
considered and rejected" line in AGENTS.md is **CARD-0063's** (scope drift), not CARD-0227's; the
reasoning is in `ScopeDriftPolicy.cs:22`: *a settings file in a shared directory changes every
session that runs there*, so a path-blocking hook could only ever be armed in a worktree, where it
protects nothing. That objection applies here with full force to a **blocking** hook in the shared
checkout, and is exactly why this plan's hook (a) never blocks and (b) decides *at run time, from
the environment*, whether the session it is running in is an orchestrator at all (§3.1).

### 1.2 What the orchestrator actually did (session `cefed08a`, last 30 h, 1 188 stored rows)

`cefed08a` is the operator's own Claude session in `C:\src\Antiphon` (definition `claude`, created
2026-08-13, no bundles composed, no standing-agent row — the same session CARD-0056 once marked
Failed). Its transcript is fully ingested, so the server can see everything below.

| Classified tool calls | Count |
|---|---|
| Source reads — `Read`/`Grep`/`Glob` tools | 32 |
| Source reads — `cat`/`sed -n`/`grep`/`rg`/`head` in Bash on a `server/`, `tests/`, `src/`, `scripts/`, `client/src/` path | **76** |
| `git diff/show/log/status/...` (verification-shaped by construction) | 209 |
| Dispatches (`delegate.ps1 -Goal/-Reply/-Refine`, all via the **PowerShell** tool) | 27 |
| `Agent` tool calls | **0** |
| Delegate report / check notes landing as prompts (`[task …]`, `[check …]`) | 30 |
| Edits (`Edit`/`Write`) | 45 (most into the scratchpad; the CARD-0246 fix was one of them) |
| Human prompts | 71 |

Twelve **cold runs** — three or more consecutive source reads with no dispatch, report or human
prompt between them and none of the files named in the most recent report — in 30 hours. What
preceded them: a human question (4), a delegate report about something *else* (2), a git read of a
plan (2), a bare `curl`/`psql` (4). The CARD-0246 run itself (seq 25342–25362, 12:18:49–12:20:06Z):

```
HUMAN   "How's it going? Maybe look into and fix 246"
READ    grep -n "_launchResolver|StartAsync" server/…/AgentControlService.cs      (Bash)
READ    Read AgentControlService.cs offset 160
OTHER   grep -rn "class AgentLaunchResolution|ResolveForAgentAsync" server/         (Bash, no path in cmd)
READ    Read AgentTuiLaunchResolver.cs (x2)
GITREAD git status --short && git log -1
READ    Read AgentTaskDispatcher.cs offset 2255
READ    grep -n "record AgentLaunchOptions" -A 20 server/…/AgentLaunchSpec.cs     (Bash)
READ    grep -n "ModelTier" server/…/AgentLaunchSpec.cs                           (Bash)
READ    Read AgentControlService.cs offset 192
EDIT    Edit AgentControlService.cs            ← the orchestrator fixed it itself
OTHER   dotnet build … ; dotnet run tests … ; git commit ; git push ; restart-apphost
```

Note the third line: a repo-wide `grep -rn … server/` carries no file path, so a path-based
classifier calls it OTHER. The detector must treat a `grep -r` whose *target* is a source directory
as a read too (§3.2 does).

### 1.3 Verification vs investigation, measured

For each of the 108 source reads: did any file it named appear in the text of the **most recent**
delegate report note?

| | named in last report | not named |
|---|---|---|
| within 20 events of that report | 4 | 24 |
| later than that | 7 | 73 |

So "file named in the latest report" is a usable **allow** signal (it is rare and, on inspection,
every one of the 11 was a real verification read), and "not named" is where the 12 cold runs live.
The false-negative risk runs the other way: the 24 unnamed reads close to a report include reads of
a *test file the report named by class, not path*, and reads of the delegate's diff through
`git show` (which the classifier already treats as verification). The trigger in §3.2 therefore
widens "named" to: path, basename, or a `CARD-nnnn`/class identifier the report contains, and
never nudges on a `git` read at all.

### 1.4 Codex and Grok

- **Codex CLI** has a Claude-shaped hook system (`~/.codex/hooks.json`, `<repo>/.codex/hooks.json`,
  or `[hooks]` in `config.toml`; events `SessionStart`, `SessionEnd`, `SubagentStart/Stop`,
  `PreToolUse`, `PermissionRequest`, `PostToolUse`, `Pre/PostCompact`, `UserPromptSubmit`, `Stop`).
  Its reference states `PreToolUse` "can intercept Bash, file edits performed through
  `apply_patch`, MCP tool calls, and other local function tools", the input carries `session_id`,
  `transcript_path`, `cwd`, and the output supports `additionalContext` ("adds model-visible
  developer context without blocking"). A third-party reference claims read tools do not fire it;
  since Codex reads through shell commands anyway, a Bash classifier covers the shape. **Not
  verified by a probe** — Codex cannot be an orchestrator today, so nothing depends on it.
- **Grok Build** documents hooks in `.grok/settings.json` with `PreToolUse`/`PostToolUse`/
  `UserPromptSubmit` (third-party write-ups; the official page under `docs.x.ai/build` was not
  located — `/build/hooks` is a 404). Unverified, and moot for the same reason.
- **Antiphon's own layer is provider-agnostic already**: `TranscriptEntry.Kind == ToolCall` with
  `ToolName`/`ToolInput` is written for every kind (Codex rows in the same table today carry
  `read_file`, `run_terminal_command`, `search_replace`). A server-side sweep needs a per-kind tool
  vocabulary and nothing else.

### 1.5 What Antiphon knows that a hook does not

- Which sessions dispatched what: `AgentTask.ParentSessionId` (38 tasks from `cefed08a` in 48 h).
- Which session is an orchestrator by declaration: a standing agent with the `orchestrator` bundle
  attached (`AgentBundleAttachments`; today only *Gym Stat Orchestrator*), or a task with
  `Kind = Orchestrator`. The operator's own session is neither — it is an orchestrator by
  *behaviour* (it dispatches), which is how the sweep should recognise it (§3.3).
- Which reads followed which report: `SessionQueuedMessages` rows with `Origin = Task` and their
  `SentAt`, plus the `UserPrompt` transcript rows they became.
- The environment each launch gets: delegates carry `ANTIPHON_TASK_ID`/`ANTIPHON_SESSION_ID`
  (`AgentTaskDispatcher.BuildEnv`, `:2389`); standing agents carry `ANTIPHON_AGENT_ID`
  (`AgentSessionLaunchComposer.cs:44`); the operator's plain `claude` definition carries
  `ANTIPHON_API`/`ANTIPHON_TASK_TOKEN` only. Nothing today says "this session is an orchestrator" in
  the environment — §3.1 adds that one variable.

## 2. Why the naive options fail (the card's constraint, made concrete)

- **Blocking hook**: the 209 `git` reads and the 11 report-named reads are the wanted behaviour and
  are indistinguishable from investigation at the `tool_name` level. Blocking Read on `server/**`
  would have stopped every merge verification in §5 of the orchestration loop. And per
  `ScopeDriftPolicy`, a blocking hook in the shared checkout also blocks every Shared delegate.
- **Advisory text only**: the rule was already in memory, already re-affirmed twice on 2026-08-09,
  and was skipped on 2026-08-30 under "this looks quick". Text is necessary (§6) and provably
  insufficient.
- **`Agent`-tool-based condition**: never true here (§1.2).
- **"This turn" scope**: the verification reads are in a different turn from the dispatch by
  construction (§1.3).

## 3. Design

### 3.1 Who the hook fires for — an environment discriminator, not a directory

The hook is installed in the repo's committed `.claude/settings.json` (`.gitignore` ignores `.claude/*` but
whitelists `.claude/settings.json`, so it is tracked). It therefore runs in every session started in
`C:\src\Antiphon`. It **exits 0 immediately, producing nothing**, unless the session is an
orchestrator, decided in this order:

1. `agent_id` present in the hook input → this is a Claude subagent → exit (a subagent *is* the
   delegated reader).
2. `ANTIPHON_TASK_KIND=Orchestrator` → a sub-orchestrator delegate → **armed**. (New variable, one
   line in `AgentTaskDispatcher.BuildEnv`, alongside `ANTIPHON_TASK_ID`.)
3. `ANTIPHON_TASK_ID` set → a worker delegate → exit. Delegates are supposed to read.
4. `ANTIPHON_ORCHESTRATOR=0` → explicit opt-out for a human hacking session → exit.
5. Otherwise (a standing agent, or the operator's interactive session in this repo) → **armed**.
   `AgentSessionLaunchComposer` additionally exports `ANTIPHON_ORCHESTRATOR=1` when the
   `orchestrator` bundle is attached, so the declaration and the default agree.

Rule 5 is the deliberate choice: in this repo, a non-delegate session *is* the orchestrator — that
is the whole premise of the card — and a human who wants to read code without being nudged sets one
variable. No directory restructuring is needed to get orchestrator-only behaviour.

### 3.2 The trigger — "cold investigation", operationalised

The hook matches `Read|Grep|Glob|Bash|PowerShell`. On each call it:

1. **Classifies the call.** `Read`/`Grep`/`Glob` with a path under a *source root* (`server/`,
   `src/`, `tests/`, `client/src/`, `scripts/`, `Antiphon.AppHost/`; **not** `docs/`, `.antiphon/`,
   the scratchpad, `~/.claude/`, cards, plans, memory) → a *source read*. `Bash`/`PowerShell`
   whose command is a read verb (`cat`, `sed -n`, `grep`, `rg`, `head`, `tail`, `Get-Content`,
   `Select-String`) **and** whose arguments name a source root or a source-rooted path → a source
   read. Any `git` subcommand, `delegate.ps1`, `card.ps1`, `curl` to the API, `dotnet`, `npm`,
   `docker`, `psql` → not a read (the git ones are *verification by definition*).
2. **Reads the transcript tail** (`transcript_path`, last ~256 KB) and walks backwards through
   `assistant`→`tool_use` and `user` records to find, most recent first:
   - the last **dispatch** (a `tool_use` of Bash/PowerShell containing `delegate.ps1` with
     `-Goal`/`-GoalFile`/`-Reply`/`-Refine`, or an `Agent` tool call — kept for completeness);
   - the last **report** (a `user` record whose text starts with or contains `[task <id> done|
     blocked|failed]`, `[check <id> #n]`, or `<task-notification>`), and the set of identifiers it
     names: file paths, basenames, `CARD-nnnn`, PascalCase class names, test names;
   - the run length: how many consecutive source reads precede this one with no dispatch, report,
     or human prompt in between.
3. **Decides.** The read is *verification* — no nudge — when **any** of: it names something the
   last report named; the last report is within `N_report = 25` tool calls; the last dispatch is
   within `N_dispatch = 10` tool calls (the orchestrator is still deciding what to send). It is
   *investigation* when none hold **and** the run length reaches `R = 3`. Thresholds are constants
   at the top of the script, chosen from §1.2: every one of the 12 cold runs is caught at `R = 3`
   (the shortest was exactly 3) and none of the 11 report-named reads is.
4. **Nudges once per run.** On the first investigation read of a run it returns:
   ```json
   {"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"allow",
     "additionalContext":"[antiphon-orchestrator] This is the 3rd consecutive source read with no delegate dispatched and no report naming these files. Diagnosis is a Debug delegate, not an inline read: pwsh -NoProfile -File scripts/delegate.ps1 -Role Debug -Goal \"…\" — and take its answer. If you are verifying a delegate's named claim, carry on; this note will not repeat for this run."}}
   ```
   and records `{session_id, run_started_seq, nudged_at}` in `%TEMP%\antiphon-hooks\<session_id>.json`
   so it does not fire again until a dispatch, report, or human prompt resets the run. A nudge that
   repeats on every read is a nag the model learns to skip — the same failure as the memory rule.
5. **Never blocks, never errors.** Any parse failure, missing transcript, or timeout → exit 0 with
   no output. A hook that can wedge the orchestrator on a malformed JSONL line would be worse than
   the problem.

Two optional companions, cheap because they share the classifier:

- `SessionStart` matcher `compact`: re-inject the one-paragraph rule as `additionalContext` after
  every compaction (the long-session failure mode named in the card).
- `UserPromptSubmit`: when the prompt is a `[task … done]` note, cache its named identifiers into
  the state file, so step 2's parse is a lookup on the next call. Optimisation only.

### 3.3 The detection sweep — `OrchestratorInvestigation` (server-side, provider-agnostic)

A read-only sweep beside the CARD-0153 stall detector, registered in
`AgentSupervisorHostedService` on the once-a-minute cadence the other sweeps use
(`SweepStaleCorrelationsAsync`, `SweepAsync` ×3):

- **Population**: sessions that are orchestrators by *behaviour* — any `AgentSession` that is the
  `ParentSessionId` of an `AgentTask` created in the last 7 days — plus by *declaration* (agent with
  the `orchestrator` bundle; task `Kind = Orchestrator`). No new column.
- **Signal**: over `TranscriptEntries` since the sweep's per-session watermark, the same classifier
  as §3.2 applied to `ToolName`/`ToolInput`, with a per-kind vocabulary (`Read`/`Grep`/`Glob`/
  `Bash`/`PowerShell` for Claude; `read_file`/`run_terminal_command`/`grep_search` for Codex; the
  Grok names from `HerdrAgentKinds`/its normaliser). Dispatches are `AgentTask` rows with this
  `ParentSessionId` (durable — better than parsing the command), reports are `SessionQueuedMessage`
  rows with `Origin = Task` that became `UserPrompt` entries. A **run** is ≥ `R` consecutive source
  reads with no dispatch/report/human prompt between and no identifier overlap with the last report.
- **Output**: one `AgentIncident` per run, new kind `AgentIncidentKind.OrchestratorInvestigation`
  (39 — next free after `HerdrPaneLeftOpen = 38`), severity **Warning**, message naming the run
  (`8 reads over 77 s across 4 files, no dispatch; nudged=yes|no`), attached to the session. It
  surfaces through the existing `RecentCriticalIncident` grouping only if escalated; instead add
  `AttentionKind.OrchestratorInvestigation = 16` so it lists in the **Process** group with the card
  and stall rows, first-match ordered after `ProgressStalled`. Idempotent by `(session, run start
  sequence)`. Detection only: nothing kills, retypes, blocks or moves a card (CARD-0153's rule).
- **Why the sweep exists even with the hook**: it is the score. The card's own success test is
  "watch whether the orchestrator still catches itself"; the sweep is what makes that watchable
  without anyone re-running today's ad-hoc analysis. It also sees the hook's *effect*: the incident
  message says whether a nudge fired before the run continued, which is the number that decides
  whether to escalate to §5.

### 3.4 What is deliberately NOT here

- No `PostToolUse` exit-2 nudge: it delivers after the read already spent the context.
- No `Stop` hook that blocks the turn until a delegate is dispatched: that is a gate, and the card
  rules gates out.
- No bundle text change for the standing orchestrator beyond one sentence pointing at the sharpened
  doc (§6): `server/Bundles/orchestrator.md` already says "every investigation deeper than a single
  file read" is a delegation; the failing session does not receive bundles at all.
- No change to `DelegationWorktreeService`'s deny hook or the check interpreter's deny-all hook.

## 4. Slices

| # | Slice | Where | Test that pins it |
|---|---|---|---|
| S0 | **Canary**: the `additionalContext` contract against real Claude — a headed `[Explicit]` test that runs `claude -p --settings <hook>` and asserts the codeword in the answer and the input fields in the hook's log. Also asserts the hook is *not* fired with `agent_id` for the main context. | `tests/Antiphon.Agents.Pty.Tests` (beside the other `*CanaryTests`) | `ClaudeHookAdditionalContextCanaryTests` |
| S1 | **Classifier + transcript walker** as a Node script with a pure `classify(input, transcriptTail, state)` function, no I/O in the core. Fixture: the real `cefed08a` tail for seq 25290–25420 (redacted) — must nudge exactly once at seq 25348 (the third read), never on the `git status` at 25353, never on the 11 report-named reads. | `scripts/hooks/orchestrator-investigation.mjs` + `scripts/hooks/__tests__/` (vitest, run by `scripts/test-client.ps1` or a sibling) | fixture-driven unit tests; the thresholds are asserted, not just exercised |
| S2 | **Install** in `.claude/settings.json` (matchers `Read|Grep|Glob|Bash|PowerShell`, `timeout: 5`), the env discriminator (§3.1), `ANTIPHON_TASK_KIND` in `BuildEnv`, `ANTIPHON_ORCHESTRATOR=1` in the launch composer when the bundle is attached. `SessionStart(compact)` re-injection. | `.claude/settings.json`, `AgentTaskDispatcher.cs`, `AgentSessionLaunchComposer.cs` | a new `BuildEnv` test beside `DelegationUnitTests` (the two new variables); `DelegateBundleLaunchTests` extension |
| S3 | **Sweep**: `OrchestratorInvestigationDetector` (pure, over a list of classified rows) + the hosted sweep + incident kind 39 + attention kind 16 + the client's attention group label. | `server/Application/Services/`, `server/Infrastructure/Supervision/AgentSupervisorHostedService.cs`, `client/src/features/attention` | `OrchestratorInvestigationDetectorTests` (replays the §1.2 rows: 12 runs found, the CARD-0246 one with 8 reads), `AttentionServiceTests` row case, idempotence |
| S4 | **Docs**: §6 text into `docs/orchestration-loop.md`; one-line pointer in `server/Bundles/orchestrator.md`; an AGENTS.md gotcha (the *measured* "reads go through Bash, dispatches through PowerShell, the Agent tool is never used" fact, so the next person does not design against the card's sketch). | docs | — |

S1 and S3 share the classification rules; write them once as a table in the plan's companion
`docs/orchestration-loop.md` section and keep both implementations' fixtures identical, the way the
three working/idle implementations are kept in lockstep.

Order: S0 → S1 → S2 (the nudge is live), then S3 (the score), then S4. S0–S2 is roughly a day of
delegate work; S3 another; S4 an hour. The card's "watch whether it still happens" period starts
when S2 lands — S3 is what makes the watching cheap.

## 5. The parent-orchestrator-folder idea — evaluated, not built

What would break today if the orchestrator's cwd were `<orch>\source\Antiphon`'s *parent*
(`<orch>`) with the checkout nested beneath:

| Assumption | Where | Breaks how | Fix cost |
|---|---|---|---|
| `card.ps1` finds the board from `git rev-parse --show-toplevel` (`Get-CheckoutRoot`, `card.ps1:217`) and sends it as `?cwd=`; `CardIdentifierScope` matches projects whose `LocalRepositoryPath` *contains* it | CARD-0218 | `<orch>` is not inside any project path → scope falls to *everywhere* → `CARD-0001…0021` collide with Gym Stat → **409 on every card verb** | `-Board` on every call, or teach `card.ps1` to look one level down, or register `<orch>` as the project path (which then breaks worktree/`git` assumptions elsewhere) |
| `delegate.ps1` default working directory is the caller's (`AgentTaskService.cs:81`, `:229`) | CARD-0009/-0063 | delegates launch in `<orch>` — Shared tasks run outside the repo; Worktree tasks fail with "not a git repository, so there is nothing to branch" (`:242`) | `-Dir source\Antiphon` on every dispatch, or a server default from the project row |
| `antiphon.areas.json` is read from the repo root of `-Dir` or cwd (`delegate.ps1:127`) | CARD-0063 | no areas → every scope name is an unknown label | same as above |
| Claude loads `CLAUDE.md` from cwd **upward**; subdirectory files load *only when Claude reads files there* (memory reference, verified today) | Claude Code | the orchestrator would not see AGENTS.md at launch — it would see it exactly when it does the thing this card forbids | `<orch>\CLAUDE.md` with `@source/Antiphon/AGENTS.md` (relative imports resolve from the importing file; 4-hop cap) — works, but AGENTS.md's 89 `pwsh -File scripts/…` and every `git` example then assume the wrong cwd |
| Project skills come from cwd's `.claude/skills` | Claude Code | `antiphon-delegate` skill invisible to the orchestrator | NTFS junction `<orch>\.claude\skills → source\Antiphon\.claude\skills` (junctions are directory-level, so this one works) |
| `.claude/settings.json` hooks apply per primary working directory, not upward | settings reference | this is the *benefit*: `<orch>` hooks never reach a delegate in `source\Antiphon` | none — but §3.1 gets the same isolation from one env var |
| Transcript root is per-cwd (`~/.claude/projects/<enc-cwd>/`) | CARD-0006 | this is the other *benefit*: the operator's session stops sharing a transcript root with every Shared delegate, retiring the stranger-transcript hazard for that session | none |
| The 89 documented `pwsh -File scripts/x.ps1` invocations and every `git merge --ff-only` / `git worktree remove` in `orchestration-loop.md` §5, §8 assume cwd = repo root; this harness resets the shell cwd on every call | docs, habit | every command grows a `cd source\Antiphon &&` prefix or `git -C` | doc pass + retraining every orchestrator session's habits |
| Remote-control / claude.ai session titles, `cleanup-claude-sessions.ps1` matching on the project path | CARD-0145 | cosmetic / needs a path tweak | small |

Estimate: **~1 day to wire** (parent dir, CLAUDE.md import, skills junction, `delegate.ps1` and
`card.ps1` defaults from the project row) plus a **doc pass across AGENTS.md, orchestration-loop
and the delegate skill**, plus an open-ended cost: two roots to keep straight in every brief and
every operator command, forever. Against that, the two real benefits (hook isolation, transcript
root) are respectively obtained by §3.1 for free and already mitigated by CARD-0006/0181's binding
rules. **Agree with the card: defer, and only revisit if S3's numbers show the nudge is being
ignored** — then it becomes a card of its own, framed as "the orchestrator gets a workspace
directory like a channel agent does" (`docs/agent-workspaces.md`), which is the shape it actually is.

## 6. `docs/orchestration-loop.md` — replacement text (drafted, not applied)

Replace the preamble's last sentence and add a §0 before "The cycle":

> The orchestrator's job is to **decide, verify and record**. The reading, the writing and the
> running are delegated. The orchestrator's context is the scarce resource: spend it on
> judgement, not on archaeology.
>
> ## 0. What the orchestrator may read, and what it must send out
>
> Two kinds of reading look identical at the keyboard and are not:
>
> | **Verification** — do it yourself | **Investigation** — a delegate does it |
> |---|---|
> | Read the diff a delegate's report names (`git show`, `git diff master..feat/…`) | Trace *why* something happens through more than one file |
> | Re-run the tests the report names, on master | Find where a value is set, who calls what, what the data shape is |
> | Open the one file the report points at, to check the claim | Read a file to *decide what to delegate* — decide from the card and the report instead |
> | `git log`, `git status`, the API, the board | Anything that starts with "let me just check" and no report named it |
>
> The test is mechanical: **is there a delegate report in front of you that names this file or this
> test?** If yes, read it. If no, it is a `Debug` (or `Plan`) delegate — *even when it looks one grep
> away*, and even when the delegate would be the same tier as you. "This one's quick" is the
> rationalisation that produced CARD-0246's inline fix (eight reads in ninety seconds, then an Edit,
> a build, a commit and a deploy, all in the orchestrator's own context). The only sanctioned
> exception is when the delegation pipeline itself is what is broken.
>
> Since CARD-0247 a `PreToolUse` hook in this repo says so at the third consecutive cold source read
> (it never blocks; `ANTIPHON_ORCHESTRATOR=0` silences it for a hacking session), and a server sweep
> records each run as an `OrchestratorInvestigation` Warning on the attention feed. A row there is
> not a fault to fix in the code — it is a habit to fix in the next brief.

And in §5 (*Verify before merging*), after "Read the commit messages rather than the report", add:

> This is the reading the orchestrator is *for*. Keep it to what the report names; the moment you
> are reading to understand rather than to confirm, that is a `Review` delegate.

## 7. Decisions for the operator

| Decision | Recommendation |
|---|---|
| Nudge thresholds `R = 3` reads, `N_report = 25`, `N_dispatch = 10` | Start there (catches all 12 measured runs, none of the 11 verification reads); revisit from S3's incidents after a week. |
| Hook language | **Node** (56–80 ms per call vs 240–410 ms for either PowerShell). At ~130 source reads/day the pwsh cost would be ~50 s/day of pure latency; Node's is ~9 s. |
| Default arming for a non-delegate session in this repo (§3.1 rule 5) | **Armed.** A human who wants silence sets `ANTIPHON_ORCHESTRATOR=0`. The alternative (opt-in only) leaves the exact session that failed unprotected. |
| Attention placement | New `AttentionKind` in the Process group (with `CardStalled`/`ProgressStalled`), Warning, never counted as Broken. |
| Provider scope | Claude-only hook; provider-agnostic sweep. No follow-up card for Codex/Grok hooks until either can be an orchestrator. |
| Parent-folder restructuring | Defer; separate card if S3 shows the nudge is ignored. |

## 8. Not determined / assumptions to pin in S0–S1

- That Claude Code writes the `tool_use` record to the JSONL **before** firing `PreToolUse` (the
  walker assumes the current call may or may not be present and de-duplicates by `tool_use_id`).
- That `additionalContext` survives into the *next* API call rather than being shown only once —
  the probe answered within the same turn; S0 should also check a multi-turn shape.
- The transcript tail size needed to see the last report on this orchestrator (its reports are up
  to 14 KB; 256 KB covers ~40 tool calls of typical size — measure on S1's fixture).
- Whether `claude` in `--resume` mode fires `SessionStart` with `source: resume` on the compaction
  path too (if not, the `compact` matcher alone is enough).

## 9. Environment / cleanup

No repo changes were made by this pass beyond this document. The probe lived in the session
scratchpad. No `bin-*` directories were created (no build was run).
