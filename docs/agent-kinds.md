# Agent kinds — what Antiphon can run, and how each is launched

An **agent kind** is which terminal program a session actually runs: `claude.exe`, `grok.exe`,
`codex.cmd`, or something raw. Antiphon does not talk to any model API for these sessions — it
starts the vendor's own TUI in a pseudoconsole (or a herdr pane), types into its composer, and
reads its transcript back. Everything a kind can and cannot do follows from that.

This document is the reference for **which kinds exist, how each is launched, and what each
supports**. Credentials and the launch environment are a separate concern with their own
document: [agent-credentials.md](agent-credentials.md). Profile management through the UI is
[ai-agent-tui-configuration.md](ai-agent-tui-configuration.md).

## Source of truth

Do not treat the tables below as authoritative when they disagree with the code. Three files own
these facts, and each states its own evidence:

| Fact | Owner |
|---|---|
| What a live session of a kind can *signal* (transcript, turn end, delivery verification, resume, compaction, usage, blocking modals, local commands) | `server/Application/Services/ProviderContractCatalog.cs` |
| What a *profile* of a kind can be configured to do (model argument, discovery, permission bypass, remote control, system-prompt append) | `server/Application/Services/AgentTuiRunnerCatalog.cs` |
| Which model a tier resolves to | `server/Application/Services/ModelLevelAliases.cs` |
| The env/args that point a CLI at a non-default API endpoint | `src/Antiphon.FakeLlmApi/RealCliStubEnv.cs` |

`ProviderContractCatalog` in particular is written as measured facts with the card that measured
them. If you need to know whether Codex can resume, read that file — the answer there is
`Unknown` with the reason "not probed", which is a different and more useful answer than a table
guessing "no".

## 1. The kinds

`AgentKind` (`server/Domain/Enums/AgentKind.cs`) has five members:

| Kind | Program | Delegatable? | Orchestrator? | Structured activity | herdr? |
|---|---|---|---|---|---|
| `ClaudeCode` | `claude.exe` | yes | **yes — the only one** | transcript (JSONL) | yes |
| `Grok` | `grok.exe` (xAI Grok Build TUI) | yes, worker only | no | transcript (ACP `updates.jsonl`) | yes (CARD-0187) |
| `Codex` | `codex.cmd` (OpenAI codex-cli) | yes, worker only | no | transcript (rollout JSONL) | yes (CARD-0187) |
| `OpenCode` | `opencode` / a wrapper | no | no | quiet-time only | **no** — refused |
| `Raw` | any command (`pwsh.exe`, …) | no | no | quiet-time only | **no** — refused |

"Delegatable" is `AgentTaskService.DelegatableKinds` — `[ClaudeCode, Grok, Codex]`. A task asking
for `OpenCode` or `Raw` is **refused with a 422 naming the reason**, never silently substituted.
An orchestrator is `ClaudeCode` only: its contract (the PreToolUse deny hook, `delegate.ps1`, the
check interpreter) has only ever run on Claude, and an explicit orchestrator kind of `Grok`/`Codex`
is rejected rather than reinterpreted.

`OpenCode` and `Raw` are screen-only lanes: no transcript is tailed, delivery is a blind
`SendLineAsync` with no composer evidence and nothing to confirm against, and turn completion is
PTY quiet time. They exist; they are not a place to put work that matters.

## 2. How a launch is composed

A session's command line is built in layers, and no single file holds the whole thing:

1. **Definition or profile** — `Agents:Definitions:<name>` in `server/appsettings.json`, or a
   managed TUI profile revision (`AgentTuiLaunchResolver`). Supplies `Exe`, `ArgsTemplate`, `Env`,
   working directory.
2. **Model argument** — one appender per path, never two, and never from ExtraArgs
   (CARD-0182 D2). Callers that have a tier offer it as `AgentLaunchOptions.TierModelAlias`;
   `AgentTuiLaunchResolver` (profile path) or `AgentRegistry.Resolve` (no-profile path) is the
   only place that may write the flag. For a profile revision the four-step rule is:

   1. `ModelArgumentName` blank ⇒ append nothing (the program owns its model). An exact
      `ModelId` on that profile is 409 `model_argument_unsupported`, never dropped or rewritten.
   2. Else `agent.ModelId` set ⇒ `[<argName>, <ModelId>]`, catalogue-checked.
   3. Else a tier alias supplied ⇒ `[<argName>, <alias>]`.
   4. Else nothing (card-spawn today).

   There is always a tier (`Agent.ModelLevel` is non-nullable and defaults to High). A blank
   *agent* model on Claude/Grok/Codex therefore fills in the tier alias unless the *profile*
   field is blank. Raw's catalogue default is already null. The argument name is per-revision;
   `--model` is the default for every kind that takes one.
3. **Standing instructions** — the channel differs per kind, and it is branched explicitly at each
   launch site (`AgentControlService`, `AgentTaskDispatcher`):

   | Kind | Argument |
   |---|---|
   | `ClaudeCode` | `--append-system-prompt <text>` |
   | `Grok` | `--rules <text>` |
   | `Codex` | `-c developer_instructions=<text>` — no `--append-system-prompt`, no `--rules` |

   In all three cases it is an **argument, never typed**, so it survives compaction and no pty
   delivery ceiling applies; the bound is the command line, guarded by
   `InstructionBundleComposer.EnsureWithinCommandLineBudget` (`Delegation:CommandLineBudgetChars`),
   which throws rather than truncating. Composition order is attachments → `ReplyStyle` block →
   `SystemPromptAppend`; `Normal` composes nothing. A change takes effect at the next launch — the
   drift badge is informational, not an action.
4. **Session identity** — appended last, by `AgentSessionService.BuildSessionIdentityArgs`, and
   only for kinds whose `SessionResume` contract is `Supported`. Any pre-existing
   `--session-id` / `-s` / `--resume` / `-r` / `--continue` / `-c` in the profile args is stripped
   first, then exactly one of `--session-id <guid>` / `--resume <guid>` / `--continue` is added.
5. **Claude remote-control overlay** — `ClaudeRemoteControlLaunchArgs.ApplyOff` appends
   `--settings <file>` with `remoteControlAtStartup: false` for `AgentKind.ClaudeCode` only,
   at `AgentSessionService.BuildRuntimeLaunchSpecAsync` (the one funnel that actually starts
   the process). Antiphon then types `/remote-control` only when `remoteControlName` is set.
   Operator-launched `claude` in a terminal is untouched.
6. **Environment** — merged and resolved separately; see
   [agent-credentials.md](agent-credentials.md).

The tracked definitions in `server/appsettings.json` today:

| Definition | Kind | Exe | `ArgsTemplate` |
|---|---|---|---|
| `claude` (default) | `ClaudeCode` | `claude.exe` | `--dangerously-skip-permissions` |
| `codex` | `Codex` | `codex.cmd` | `--no-alt-screen`, `--dangerously-bypass-approvals-and-sandbox` |
| `grok` | `Grok` | `grok.exe` | `--always-approve`, `--no-alt-screen` |
| `raw-pwsh` | `Raw` | `pwsh.exe` | `-NoLogo`, `-NoProfile` |

Permission bypass is **not implied by the kind** — `AgentTuiRunnerCatalog` reports
`permissionBypass: Supported` only when the profile's own arguments contain the kind's bypass flag
(`--dangerously-skip-permissions`, `--dangerously-bypass-approvals-and-sandbox`,
`--always-approve` / `--permission-mode bypassPermissions`, `--auto`). A profile without it
launches a TUI that will sit on approval prompts nobody is there to answer.

Antiphon also exports its own orchestration identity into the agent process — this is how
`delegate.ps1` and `card.ps1` know who they are without being told:

| Launched as | Gets |
|---|---|
| A **named agent** (`AgentControlService`) | `ANTIPHON_API`, `ANTIPHON_AGENT_ID`, `ANTIPHON_TASK_TOKEN` |
| A **delegated task** (`AgentTaskDispatcher`) | the above plus `ANTIPHON_SESSION_ID`, `ANTIPHON_TASK_ID` |

(`ANTIPHON_WORKSPACE_PATH`, `ANTIPHON_WORKTREE_PATH` and `ANTIPHON_CARD_ID` are a different
surface — they are set on **workspace hook** processes, not on agent sessions.)

These outrank every configured env layer and **may not be overridden** from a task's env
overlay — `ANTIPHON_*` is refused 422 by name. See
[agent-credentials.md](agent-credentials.md).

## 3. Model levels

Antiphon dispatches at a *tier* (`Frontier` / `High` / `Medium` / `Low`), not a model id.
`ModelLevelAliases` maps a tier to a per-provider identifier at launch:

| Tier | Claude | Grok | Codex |
|---|---|---|---|
| `Frontier` | `fable` | `grok-4.6` | `gpt-5.6-sol` |
| `High` (default) | `opus` | `grok-4.6` | `gpt-5.6-terra` |
| `Medium` | `sonnet` | `grok-4.6` | `gpt-5.6-luna` |
| `Low` | `haiku` | `grok-4.6` | `gpt-5.6-luna` |

Three things worth knowing about that table:

- **Claude rides family aliases, never versioned ids**, so a launch picks up the family's current
  model without a code change.
- **Grok is collapsed to `grok-4.6` at every tier** (CARD-0169) — the operator's instruction, not
  a capability judgement. `grok-4.5` is still a selectable model id in the profile catalogue and
  in historical records; it is only gone from the ladder new dispatches resolve through.
- **Codex pins full slugs and needs a deliberate bump.** Measured against codex-cli 0.147.0:
  `-m luna` is rejected locally ("Model metadata for `luna` not found") *and* by the service
  (HTTP 400). There are no unversioned aliases in Codex's catalogue. Medium and Low deliberately
  share `gpt-5.6-luna`, which is why a Low→Medium Codex escalation is told it bought a fresh
  context rather than a bigger model.

`ModelLevelAliases.For(kind, level)` is what every *human-facing* string goes through — task
events, escalation notes, the check digest, completion-note headers. Launch arguments deliberately
do **not**: they branch explicitly at the sites that build them, because a wrong alias in an
argument is a wrong process, not a wrong word.

## 4. Claude Code

**Launch.** `claude.exe --dangerously-skip-permissions [--model <alias>]
[--append-system-prompt <text>] (--session-id <guid> | --resume <guid> | --continue)`.

**Environment applied automatically** (`AgentTuiLaunchResolver.ApplyClaudeEnvironmentDefaults`,
only when a key is not already set):

| Variable | Value | Why |
|---|---|---|
| `DISABLE_AUTOUPDATER` | `1` | an update mid-session rewrites the binary under a live pty |
| `CLAUDE_CODE_DISABLE_ALTERNATE_SCREEN` | `1` | alt-screen output cannot be scraped |
| `CLAUDECODE`, `CLAUDE_CODE_CHILD_SESSION`, `CLAUDE_CODE_SESSION_ID`, `CLAUDE_CODE_BRIDGE_SESSION_ID`, `CLAUDE_CODE_ENTRYPOINT` | empty string | nesting markers, cleared so a session launched from inside another Claude does not inherit an "already nested" refusal |

**Credentials.** Normal operation is *wrapper-managed*: `claude.exe` is already logged in as the
Windows user and Antiphon holds nothing. To point it somewhere else — a proxy, a stub, a gateway —
the two variables are `ANTHROPIC_BASE_URL` and `ANTHROPIC_API_KEY` (which travels as the `x-api-key`
header, not `Authorization`). `CLAUDE_CONFIG_DIR` isolates the config/credential directory.

**Transcript.** Per-cwd JSONL under `~/.claude/projects/<enc-cwd>/`. The file is **discovered**,
not computed, and binding it requires the full C1–C4 claim rules (CARD-0006) — several agents and
the human operator share one cwd, so "most recently written file in the right directory" once bound
an agent to a stranger's live conversation. Never add a code path that picks a transcript file
outside `TranscriptTailer`.

**Behaviour worth knowing:**
- First launch into a directory nobody has run Claude in parks on the **trust dialog**, which makes
  no output and therefore reads as "ready" to every quiet-period detector. `ClaudeBlockingPromptDetector.ClearStartupTrustPromptAsync` answers it (and *only* it) in both Claude adapters.
- Remote control (`/remote-control`) is supported and is what puts a session in the claude.ai
  session list. A failed `/remote-control` degrades to an `RcDegraded` incident; it never fails the
  launch.
- `--append-system-prompt` is supported.
- Manual `/compact` is a turn **end**; auto-compaction is mid-turn housekeeping. Both need the
  CARD-0041 handling — see AGENTS.md / CLAUDE.md gotchas.
- Subscription-usage polling is `Unknown`: there is no established TUI command that renders it.
  Do not guess one.

## 5. Grok (xAI Grok Build TUI)

**Launch.** `grok.exe --always-approve --no-alt-screen [--model grok-4.6] [--rules <text>]
(--session-id <guid> | --resume <guid>)`.

`--rules`, not `--append-system-prompt` — Grok's `systemPromptAppend` capability is
`Supported` *through `--rules`*, and the launch sites branch on the kind for exactly this reason.

**Environment applied automatically** (`ApplyGrokEnvironmentDefaults`, when unset):
`GROK_TELEMETRY_ENABLED=0`, `GROK_FEEDBACK_ENABLED=0`.

**Credentials.**

| Variable | What it does |
|---|---|
| `GROK_CODE_XAI_API_KEY` | the API key. Presented as `Authorization: Bearer <key>` on `GET /api-key`. |
| `GROK_CLI_CHAT_PROXY_BASE_URL` | **the chat redirect.** This is the one that moves real turns. |
| `GROK_XAI_API_BASE_URL` | redirects the `/api-key` credential lookup **only**. |

> **`GROK_XAI_API_BASE_URL` is a proven false safety.** Setting it alone makes the CLI *look*
> redirected — the credential probe goes to your endpoint — while every actual turn still hits real
> xAI and spends real money. It is defence-in-depth for the credential oracle and nothing more.
> `RealCliStubEnv.ForGrok` makes this executable rather than advisory: it sets both, and throws if
> `GROK_CLI_CHAT_PROXY_BASE_URL` is missing, so a caller cannot construct a safe-looking overlay
> without it. Never touch `GROK_AUTH_PATH` to force API-key-only auth — chat-path auth is expected
> to be an OAuth JWT when locally logged in.

**Endpoints Grok actually uses** (re-probed on CARD-0167/0168 — the earlier claim that the user
turn went to `/responses` was wrong and was corrected in that card's revision 2):

| Path | Used for |
|---|---|
| `GET /api-key` | credential lookup |
| `GET /models`, `GET /settings` | startup |
| `POST /responses` | **session title only** |
| `POST /chat/completions` | **the user turn** |

**Transcript.** `GROK_HOME/sessions/<url-encoded-cwd>/<session-id>/updates.jsonl` — an ACP update
stream. The path is **deterministic**, because grok honours `--session-id` (measured 1.0.5), so
none of Claude's discovery/claim/fork machinery applies and the CARD-0006 hazard cannot arise. The
file is created lazily at the first submit (~1.1 s after Enter), is held open with a `.lock` for
the whole session (reads must share write/delete), and is flushed per update.

**Behaviour worth knowing:**
- First launch into a directory nobody has run Grok in parks on **Do you trust the contents of
  this directory?** (`y` / `n`). `--always-approve` is already on the shipped Grok
  `ArgsTemplate` and does not skip this (it is tool-execution approval, a different gate).
  There is no `--trust` on `grok` or `grok agent`. Nested git worktrees are separate
  workspaces: trusting `C:\src\Antiphon` covers `C:\src\Antiphon\server` (same repo), but
  trusting `C:\Antiphon\worktrees` does **not** cover `C:\Antiphon\worktrees\card-task-*`
  (`grok inspect --json` `projectTrusted`, measured 1.0.13). Exact-path seed of the worktree
  itself does. `GrokTrustPromptDetector` answers `y` in `RunnerGrokAdapter.WaitForReadyAsync`
  after the quiet wait (CARD-0315); Enter is not safe because both options render bold. An
  **unauthenticated `GROK_HOME` parks on a device-code login that swallows input** — that one
  is **fail-fast, never auto-answered**, and is global per `GROK_HOME`.
- No remote control. Refused at create/PATCH/start/card-spawn with `409 remote_control_refused`
  and never typed (`RemoteControlPolicy`, CARD-0212). No claude.ai session entry.
- Context occupancy is Grok's own numbers: `auto_compact_completed.tokens_after` and single-call
  `turn_completed.usage.inputTokens`, against a self-reported **500 000** token window. A multi-call
  turn does not move the badge.
- `/usage` opens a focus-stealing overlay and writes no prompt row; the usage poll is `Degraded`
  (tab navigation and bar polarity unmeasured), so Grok stays behind `IncludeDegradedProviders`.
- There is **no measured manual compaction command.** `RefocusCompact` is `Unknown`, and the
  idle-compaction sweep therefore never types `/compact` into Grok.

## 6. Codex (OpenAI codex-cli)

**Launch.** `codex.cmd --no-alt-screen --dangerously-bypass-approvals-and-sandbox
[--model gpt-5.6-terra] -c model_reasoning_effort=<level> -c disable_paste_burst=true
[-c developer_instructions=<text>]`
— **and no session-identity argument**, because `SessionResume` is `Unknown` for Codex and
`BuildSessionIdentityArgs` only fires for kinds whose resume contract is `Supported`.

Codex has **no `--name`, no `--append-system-prompt` and no `--rules`.** Everything that is not
the model rides `-c` TOML config overrides, all of which live in
`server/Application/Services/CodexLaunchArgs.cs`. Three of them matter:

| `-c` override | Why |
|---|---|
| `developer_instructions=<text>` | the standing-instructions channel. **Measured, not read off the docs**: it lands as an additional `input_text` block at the head of the first developer message and *appends*, leaving Codex's own base instructions byte-identical. The neighbouring key `instructions` is **inert** in this CLI version — a bundle sent that way is silently dropped. Passed as one argv element with no quoting of our own; Codex parses it as TOML and falls back to the raw literal, which is what a multi-line markdown bundle always does (newlines, tabs, quotes, backticks and Windows backslashes all survive). |
| `model_reasoning_effort=<low\|medium\|high\|xhigh>` | set **explicitly on every launch**, from the tier. Codex's own per-model defaults are wrong at both ends — `gpt-5.6-sol` defaults to `low`, and the operator's `~/.codex/config.toml` here says `xhigh` and would otherwise be inherited by a Low-tier delegate. Neither default tracks the tier the caller asked for. |
| `disable_paste_burst=true` | CARD-0133. Codex's PasteBurst heuristic suppresses Enter for 120 ms after a typed burst and re-extends that window on every suppressed Enter; the queue's ~20 ms body→Enter gap lands inside it (9 of 78 cold Codex delegate launches). A static launch flag, not a delay. Official top-level boolean (default false); `-c` outranks `~/.codex/config.toml`, which does not set this key. Applied to both delegate and named-agent Codex launches. |

Tier → reasoning effort: `Frontier`→`xhigh`, `High`→`high`, `Medium`→`medium`, `Low`→`low`.

**Credentials.** `OPENAI_API_KEY` — plus, to point Codex at a different endpoint, **five `-c`
launch arguments**, because Codex has no base-URL environment variable at all:

```
-c model_providers.stub.name="Stub"
-c model_providers.stub.base_url="https://your-endpoint/v1"
-c model_providers.stub.env_key="OPENAI_API_KEY"
-c model_providers.stub.wire_api="responses"
-c model_provider=stub
```

The provider base URL **must end in `/v1`** — Codex requests `/v1/models` and `/v1/responses`
relative to it. `RealCliStubEnv.ForCodex` appends `/v1` if you did not.

**Transcript.** `CODEX_HOME/sessions/YYYY/MM/DD/rollout-<local-ts>-<uuid>.jsonl`
(`CODEX_HOME` defaults to `~/.codex`). There is no `--session-id` flag and the interactive TUI
never prints its id on screen, so the path is **discovered under the full C1–C4 rules**, same as
Claude. The file is created lazily at the first submit — 30 s of an idle rendered composer wrote
zero bytes — and is held open by Codex, so reads must share write+delete.

**Headed-test home.** Opt-in real-service headed tests (`ANTIPHON_CODEX_HEADED_TESTS=1`) use the
dedicated persistent home `%LOCALAPPDATA%\Antiphon\codex-test-home`, never the user's
`~/.codex`. Seed it once, by choice, with
`CODEX_HOME=%LOCALAPPDATA%\Antiphon\codex-test-home codex login`; tests skip with that message
until the home contains `auth.json`. Do not copy the user's normal `auth.json` programmatically.
The headed tests export this `CODEX_HOME` on every launch, so their rollouts and thread rows cannot
appear in the user's Codex Desktop sidebar.

For an ad-hoc `codex exec` probe from an agent scratchpad, set
`CODEX_HOME=<scratchpad>\codex-home` first. Scratchpad probes are not attributable to a committed
test helper and must never write into the user's normal Codex home.

**Behaviour worth knowing:**
- Turn completion is an explicit `event_msg/task_complete` row. Codex renders **no "Worked for Ns"
  done-line**, so the screen fallback is the *lifecycle* of the `Working (Ns — esc to interrupt)`
  indicator — appeared, then left. Bare quiet time was the CARD-0108 defect: it certified a
  non-turn as complete in ~3.2 s and returned the status bar as the answer.
- A per-directory **trust prompt fires on first launch into any unseen cwd, even under
  `--dangerously-bypass-approvals-and-sandbox`**, plus a startup update-available modal that
  swallows keystrokes the same way. `AcceptTrustPromptIfVisibleAsync` auto-accepts the trust prompt.
- `/status` is the usage-poll command — it renders the weekly-limit panel into scrollback with no
  overlay.
- **`/usage` is FORBIDDEN on Codex.** It opens a `1. Show usage` / `2. Redeem usage limit reset`
  picker, and an auto-confirming send can redeem the account's one usage-limit reset. It is listed
  in `ProviderContractCatalog`'s `Forbidden` map for exactly this reason.
- No remote control. Refused at create/PATCH/start/card-spawn with `409 remote_control_refused`
  and never typed (`RemoteControlPolicy`, CARD-0212). Compaction is marked (`compacted` + `event_msg/context_compacted`) and happens
  mid-turn as pure housekeeping, so unlike Claude's manual `/compact` it needs no turn-end
  treatment.
- Bracketed-paste and large-body behaviour are **unmeasured**, so the conservative spill policy
  applies and briefs/refinements travel by file.

## 7. Pointing a CLI at a different API endpoint

Whenever you need a real CLI to talk to something other than its vendor — a stub, a proxy, a
gateway — **use `RealCliStubEnv` (`src/Antiphon.FakeLlmApi/RealCliStubEnv.cs`) rather than
hand-rolling the environment.** It is the only sanctioned builder, and it exists because the safety
decisions (Grok's mandatory chat-proxy var, Codex's five `-c` args, Claude's isolated config dir)
are exactly the things a hand-rolled overlay gets wrong in the direction that costs money.

```csharp
var overlay = RealCliStubEnv.ForGrok(stub.BaseUrl, syntheticKey);
// overlay.Env  -> environment variables to set
// overlay.Args -> extra launch arguments (Codex only, today)
```

`src/Antiphon.FakeLlmApi` is the matching stub server (`FakeLlmApiServer`), speaking the Anthropic
Messages API, the OpenAI Responses API and the OpenAI Chat Completions API, with a scripted-turn
store and a request recorder. The `*RealCliStubProxyCanaryTests` in `tests/Antiphon.Tests/Agents/`
run the real binaries against it; they are `[Explicit]` and gated on
`ANTIPHON_REAL_CLI_STUB_TESTS=1`, and they use a **dual-hit oracle** — a nonce must reach the stub
on the chat path *and* the injected key must arrive on the credential path — because either alone
would pass while real turns leaked to the vendor.

For a *production* redirect (a proxy in front of an agent) the same variables go through the normal
env layers: a profile revision's env, the project default env, the agent's own launch env, or a
per-task `envOverride` — with the secret itself referenced as `{{key:NAME}}` rather than pasted.
See [agent-credentials.md](agent-credentials.md).

## 8. What is deliberately not supported

- **`OpenCode` and `Raw` as delegates.** Refused at task creation with the reason.
- **Remote control on Grok / Codex / OpenCode / Raw.** The catalog marks `remoteControl`
  Unsupported; `RemoteControlPolicy` refuses an explicit ask (`409 remote_control_refused`) at
  create, PATCH, start and card-spawn, and `SendRemoteControlCommandsAsync` types nothing.
- **Grok or Codex as an orchestrator.** Refused; the orchestrator contract has only run on Claude.
- **A fourth delegatable kind without a `ModelLevelAliases` arm.** `For()` falls back to the Claude
  ladder, so a kind admitted to `DelegatableKinds` without its own arm would silently tell its own
  delegates they are running on `fable`.
- **Numeric enum values on the wire.** The API accepts enum *names* only.

## See also

- [agent-credentials.md](agent-credentials.md) — where keys live and how a launch environment is
  assembled.
- [ai-agent-tui-configuration.md](ai-agent-tui-configuration.md) — creating and editing runner
  profiles through the UI.
- [herdr-sessions.md](herdr-sessions.md) — the optional herdr session lane (ClaudeCode, Grok, Codex).
- [orchestration-loop.md](orchestration-loop.md) — picking a tier and a kind when delegating.
- `docs/adr/0002-modern-conpty-backend.md` — the pseudoconsole these TUIs run in, and the delivery
  ceilings that follow from it.


<!-- CARD-0254 preserved source begins -->

## CARD-0254 preserved operational detail

### Preserved Gotcha #13

- **The AppHost no longer names a messaging broker** (CARD-0185): `Antiphon.AppHost/Program.cs` forwards `AntiphonMessaging:BootstrapServers` from its own configuration only when set. Fresh clones stay on `localhost:19092` (the `docker-compose.dev.yml` Redpanda). The live Family Telegram path on this machine is the `aspire-antiphon-apphost` user-secret (`dotnet user-secrets set "AntiphonMessaging:BootstrapServers" "server2:19092" --project Antiphon.AppHost`). Never put the hostname back in `Program.cs`, and never forward it to the fake gateway (a `POST :17208/inbound` on the live broker would be answered through the real bot).

### Preserved Gotcha #55

- **Orphaned claude.ai rows are Claude Code auto-connect, not Antiphon typing `/remote-control`** (CARD-0145, rewritten CARD-0306): a plain `claude --dangerously-skip-permissions` launch creates a claude.ai session because the CLI's unset `remoteControlAtStartup` follows the org default (`true` here). Antiphon's pool delegates used to inherit that default even with `remoteControlName: null`. Antiphon now forces `remoteControlAtStartup: false` on its own Claude launches via `--settings` (CARD-0306); **manual** operator launches are untouched, so the operator advice still holds — `/exit` rather than closing the window, and do not duplicate a Running orchestrator on claude.ai. CARD-0144 remains the cleanup owner for disconnected backlog.

### Preserved Gotcha #58

- **A 409 `subscription_quota_low` is a launch refusal, not a footnote on a launch that already happened** (CARD-0136): `AgentControlService.StartAsync` and `AgentTaskService.CreateAsync` check the latest CARD-0143 sample against remaining-%-vs-time-to-reset (defaults 10%/>1 day, 5%/>2 h). No reading, a stale sample, or a reset already in the past always passes — Claude will never have a sample, and `SubscriptionUsageMonitoring.Enabled` ships false so the gate is inert until an operator turns monitoring on. Two ways forward: pick another `agentKind`/agent, or re-send with `ignoreSubscriptionQuota` (`delegate.ps1 -IgnoreSubscriptionQuota`). The dispatcher never refuses; it only records an informational Warning. Internal AlwaysOn/channel/check-interpreter starts pass the flag in code. Never silently reroute.

- **Usage walls are per-model, not fleet-wide** (CARD-0022 / CARD-0309): a Fable 5 cap pauses `fable` on `ClaudeCode` and leaves Sonnet/Haiku/Grok running. Session-limit stubs (`You've hit your session limit · resets 6:10pm (Europe/London)`) pause that model until `ResetAt + 2 min` and schedule one same-session resume. Per-model caps (`You've reached your Fable 5 limit…`) write `DisabledUntil = null` and **never** the CARD-0072 30-minute `WallPrompt`. Create/start of a held alias is **409 `model_disabled`** with the remaining aliases listed; already-queued work is skipped until the hold clears. `/usage-credits` is a spend/redeem action, not a readout — do not type it. A Fable AlwaysOn orchestrator restart is refused; do not silently reroute. Operators write `Source = Manual` onto the same `ModelAvailabilityHold` table (`PUT /api/model-availability/{kind}/{alias}`, alias `*` for kind-wide). Manual outranks AutoDetected: a later wall must not shorten a human `DisabledUntil` or demote `Source`. `delegate.ps1 -IgnoreModelDisabled` queues only; Start never ignores.

### Preserved Gotcha #59

- **`/remote-control` is typed only into a kind whose catalog row says Supported** (CARD-0212):
  `RemoteControlPolicy` refuses an explicit ask with `409 remote_control_refused` at create,
  PATCH, start and card-spawn, ignores an inherited stale flag at start with a Warning, and
  `SendRemoteControlCommandsAsync` types nothing on any other kind. The fact lives on
  `AgentTuiRunnerCatalog.SupportsRemoteControl`; never add a `kind == ClaudeCode` check beside it.

### Preserved Gotcha #66

- **A mechanism is only a redirect once the stub has seen the request** (CARD-0168): never trust an unverified base-URL variable name. The test oracle for every real-CLI stub-proxy canary is stub receipt of a per-run nonce via `RealCliStubEnv.ForClaude`/`ForGrok`/`ForCodex` — never the CLI's own exit code or output, and never a hand-rolled stub env dictionary. **`GROK_XAI_API_BASE_URL` is a false safety** — it redirects only `GET /api-key`; chat still hits real xAI. Canonical Grok chat redirect is `GROK_CLI_CHAT_PROXY_BASE_URL`. Opt-in: `ANTIPHON_REAL_CLI_STUB_TESTS=1` (distinct from `ANTIPHON_HEADED_TESTS` / `ANTIPHON_CODEX_HEADED_TESTS`); category `RealCliStubProxy`. Isolated `CLAUDE_CONFIG_DIR` must be seeded (`RealCliStubClaudeConfig.SeedOnboarding`) or interactive Claude parks on first-run dialogs. B-agent Codex is deferred until CARD-0167 (five `-c` args through `AgentControlService`); A-tier `codex exec` and B-runner `RunnerLaunchRequest` already prove the mechanism. Interactive Grok also `GET /billing`.

### Preserved Gotcha #67

- **Never delete a Codex rollout file by hand — `codex delete --force <uuid>` is the only delete; a hand-deleted file leaves a `threads` row that `codex doctor` reports as stale and the desktop/mobile sidebar still lists.**

### Preserved Gotcha #68

- **Headed and stub-proxy Codex tests must set `CODEX_HOME`; a launch that inherits the user's `~/.codex` writes into the user's Codex Desktop thread list.**

### Gotcha #76

- **Antiphon's `remoteControlEnabled` / `/remote-control` preamble do not control whether claude.ai lists a session** (CARD-0306): the CLI auto-connects unless `remoteControlAtStartup` is false. Antiphon forces that false on its own launches via `--settings <file>` at `BuildRuntimeLaunchSpecAsync`; a pool delegate with `remoteControlName: null` must not appear on claude.ai. `/remote-control` remains the opt-in for seats that set a name. Do not write `~/.claude/settings.json` and do not set `disableRemoteControl` (that refuses the opt-in). CARD-0292's menu Esc stays the backstop if a CLI upgrade ignores `--settings`.

### CARD-0144 — Remote Control cleanup follow-up

CARD-0144 remains the separate owner for Remote Control cleanup. CARD-0254 files no duplicate:
closing or resuming a terminal must identify and clean stale remote sessions without terminating a
live one.

<!-- CARD-0254 preserved source ends -->
