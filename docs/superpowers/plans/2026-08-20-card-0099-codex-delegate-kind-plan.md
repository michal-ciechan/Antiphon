# CARD-0099: Codex as a delegate/worker kind (Sol/Luna/Terra) — plan

**Date:** 2026-08-20
**Status:** planned (task 29faba7d)
**Template:** CARD-0084 (Grok as delegate kind, shipped `ad258cd`…`8050a10`) — read alongside
`docs/superpowers/plans/2026-08-18-grok-delegate-kind-card-0084.md`
**Coordinates with:** CARD-0083 (capability contract), CARD-0090 (complexity tiers — consumer, §7)

## Verdict up front

Codex can become a delegate kind the same worker-only, opt-in way Grok did, **but its critical
path is longer than Grok's was**: when CARD-0084 landed, Grok already had a structured transcript
pipeline (CARD-0080 S1/S2); Codex has none — `ProviderContractCatalog.Codex` declares
*no transcript tailed, quiet-time-only turn completion, screen-only delivery verification*, and
`RunnerCodexAdapter` is a pure screen-scraper. Turn-end settlement, report extraction, working/idle
and CARD-0055 delivery confirmation all ride transcript rows, so **a Codex delegate cannot settle
until a Codex transcript pipeline exists (S1)**. The raw material is good: Codex writes a rollout
JSONL per session with explicit turn boundaries and per-turn usage (§2), so S1 is CARD-0080-shaped
work, not research. The Sol/Luna/Terra names are **verified real** (§1). Everything CARD-0084 built
as a seam (DelegatableKinds, `RolePolicy.Kind`, `KindRates`, kind-keyed spill gate, kind-keyed pool
claim, `ModelLevelAliases.For`) is generic and ready — the delegate plumbing itself (S3) is small.
Estimated total: ~4–6 days across five slices, S1 → S3 the critical path.

Two defects found while verifying, worth knowing regardless of this card:

- **The codex launch definition is broken on this machine today.** `Agents:Definitions:codex`
  runs `pwsh … -Command "cx.ps1 …"`, and `cx.ps1` does not exist anywhere on this machine (not in
  `~/.local/bin`, not on user/machine PATH — `HeadedCodexGate.ResolveCx()` returns null, so the
  headed Codex tests all skip too). What exists: the npm shim `codex`/`codex.cmd`/`codex.ps1`
  (`%APPDATA%\npm`, codex-cli 0.147.0) and the Desktop app's own
  `%LOCALAPPDATA%\OpenAI\Codex\bin\<hash>\codex.exe`. An interactive Codex launch through Antiphon
  today would die on CommandNotFound. S3 fixes the definition.
- **A named Codex agent with no exact `ModelId` gets a Claude alias.** `AgentControlService`
  (`:172-180`) branches only `isGrok ? ForGrok : ForClaude` — a Codex agent falling back to the
  legacy level alias would be launched with `--model fable`. S3's `ForCodex` arm closes this for
  both launch paths.

## 1. Sol/Luna/Terra are real — verified against the live CLI (question: verify, don't accept)

Measured 2026-08-20 against `~/.codex/models_cache.json` (fetched 2026-08-20 by codex-cli 0.147.0)
and live `codex exec` probes:

| Slug | Display name | Description | Priority | Default effort | Efforts |
|---|---|---|---|---|---|
| `gpt-5.6-sol` | GPT-5.6-Sol | Latest **frontier** agentic coding model | 1 | low | low…max, **ultra** |
| `gpt-5.6-terra` | GPT-5.6-Terra | **Balanced** agentic coding model for everyday work | 2 | medium | low…max |
| `gpt-5.6-luna` | GPT-5.6-Luna | **Fast and affordable** agentic coding model | 3 | medium | low…xhigh, max |

Also listed: `gpt-5.5` (older frontier), `gpt-5.4`, `gpt-5.4-mini`. Note the capability order is
**Sol > Terra > Luna** — not the "Sol/Luna/Terra" spoken order. The codebase already records the
correct mapping twice, written in anticipation of this card:

- `ModelLevelAliases` doc comment: "a future GPT kind adds its own ladder here (Sol = Frontier,
  Terra = High, Luna = Medium)".
- `AgentTuiRunnerCatalog.MapLegacyModel` already maps `AgentKind.Codex`: Frontier → `gpt-5.6-sol`,
  High → `gpt-5.6-terra`, Medium/Low → `gpt-5.6-luna`.

**Measured constraints:**

- `codex exec -m gpt-5.6-luna` works (session ran, answered, ~14k tokens). `-m luna` **fails**:
  "Model metadata for `luna` not found" + HTTP 400 "The 'luna' model is not supported when using
  Codex with a ChatGPT account". **Full versioned slugs are mandatory** — there are no unversioned
  aliases, so the Codex ladder pins `gpt-5.6-*` and needs a bump when 5.7 ships (the Grok ladder
  already pins versioned ids, so this breaks no rule Grok didn't).
- **This deployment authenticates Codex with a ChatGPT account**, not an API key (the 400 above
  says so; `~/.codex/auth.json`). Only the models_cache list is usable, and marginal per-token cost
  is subscription-covered — which changes the pricing question (§6).
- The `Llm.Providers.openai` section in `appsettings.json.example` (gpt-5.2, gpt-4o, o3…) is the
  **API-key LLM subsystem, unrelated to the Codex CLI's ChatGPT-account models** — it needs no
  change for this card and must not be "cross-updated" to say sol/luna/terra.
- Reasoning effort rides config, not the model flag: the interactive default here is
  `model_reasoning_effort = "xhigh"` in `~/.codex/config.toml` and `codex exec` inherited it
  (probe printed `reasoning effort: xhigh`). Sol's *model* default is **low** — a Frontier Codex
  delegate left to defaults would reason at low effort. S3 sets effort explicitly (§5).

## 2. Codex has the raw material for a transcript pipeline (the real gap)

Codex writes `~/.codex/sessions/YYYY/MM/DD/rollout-<ts>-<session-uuid>.jsonl` per session
(`CODEX_HOME`-relative). Measured record surface (real session + exec probe, cli 0.147/0.148):

- `session_meta` — session_id, **cwd**, timestamp, cli_version, originator, full base_instructions.
- `turn_context` — model, effort, cwd per turn.
- `event_msg/user_message` and `event_msg/agent_message` — the prompt/response rows.
- `event_msg/task_started` / `event_msg/task_complete` — **explicit structured turn boundaries**.
- `event_msg/token_count` — cumulative `total_token_usage` + per-turn `last_token_usage` with
  `input_tokens`, `cached_input_tokens`, `cache_write_input_tokens`, `output_tokens` — the same
  four counters the existing usage rollup wants.
- `response_item` rows (message/reasoning/function_call/…) — richer detail, not needed for v1.

`codex exec` prints `session id: <uuid>` on stdout at startup; whether the TUI renders it needs a
headed check in S1 (if it does, the tailer gets a **positive** session-id bind and skips heuristics
entirely).

## 3. What CARD-0084 built that Codex inherits for free (read of the shipped diff)

- `AgentTask.AgentKind` column + create DTO + `delegate.ps1 -Kind` posting `agentKind` — S2 `135076f`.
- `AgentTaskService.DelegatableKinds` allowlist + `ResolveAgentKind` (explicit > `RolePolicy.Kind`
  > ClaudeCode; loud rejection otherwise; **orchestrator = ClaudeCode only**) — the exact seam to
  widen. The worker-only constraint holds for Codex for the same reason it held for Grok: the
  orchestrator contract (PreToolUse deny hook, delegate.ps1 usage patterns, check-interpreter
  interplay) has only ever run on Claude, and Codex can't execute Claude hooks at all.
- `BuildLaunchSpec` keys everything on `session.AgentKind` (one answer to "what is on the other end
  of this pty") — S3 `6a5a6d7`; registry definition resolved by kind; pool claim + per-directory
  caps count per (directory, kind); `Agent.Kind` on the pool row.
- `FitBriefForTyping(…, session.AgentKind)` — the kind-keyed spill gate (S1 `ad258cd`).
- `ModelLevelAliases.For(kind, level)` for every human/interpreter-facing tier name (S4 `cfec4b6`),
  with the doc-comment contract: **a third delegatable kind must add its arm HERE at the same time
  as DelegatableKinds admits it**, or its tasks silently read as Claude.
- `DelegationPricingSettings.KindRates` per-kind rate overlay with (kind, level) → (kind, High) →
  Claude-`Rates` fallback (S5 `83175d8`).
- Board chip / home / card-thread naming the task's model (S6/S7).

So CARD-0099's delegate plumbing is: add `Codex` to two allowlists, one alias ladder, one launch
branch, one rates table — *after* S1 makes a Codex session observable.

## 4. Slices

### S1 — Codex transcript pipeline: tailer + normalizer + contract update (M–L, ~1.5–2.5 days) — CRITICAL PATH

The CARD-0080-for-Codex slice. Without it a Codex delegate never settles (no TurnEnd rows → no
turn-end queue flush → no report extraction → task hangs at InProgress forever; delivery
verification permanently degrades to the screen-only verdict CARD-0055 replaced).

- `CodexTranscriptTailer` + `CodexTranscriptNormalizer` in `Antiphon.SessionRunner`, mirroring
  `GrokTranscriptTailer`/`GrokTranscriptNormalizer`: rollout JSONL → normalized rows —
  `user_message` → UserPrompt, `agent_message` → AssistantText, `task_complete` → TurnEnd,
  `token_count.last_token_usage` → per-turn usage (four counters map 1:1).
- **Discovery must obey the CARD-0006 binding rules** (C1 unclaimed, C2 cwd — `session_meta.cwd`,
  C3 first timestamp not older than the child, C4 prompt matched against `SessionInputLog`). Codex
  has no `--session-id` launch flag, so scan `CODEX_HOME/sessions/<date>/` for rollouts created
  after child spawn and bind on C1–C4; `session_meta` carries cwd and timestamp natively, which
  makes C2/C3 exact. First headed check: whether the TUI prints its session id on screen (exec mode
  does) — if yes, read it off the screen for an exact bind and keep C1–C4 as the guard. Nothing
  qualifying ⇒ run with NO transcript + `TranscriptBindFailed` incident, same as Claude.
- `ProviderContractCatalog.Codex` updated to declare the tailed transcript, structured turn
  completion, and transcript-capable delivery verification — with the measured facts in the
  reason strings (this is also what lets CARD-0055 confirmation work unchanged).
- Verify at implementation: is the rollout file created eagerly at session start or lazily on first
  turn (affects the tailer's wait behavior, same trap as Claude's lazy creation); does an exec-mode
  and TUI-mode rollout differ in record surface (probe showed same types).
- Pinned by: `CodexTranscriptNormalizerTests` over a captured real fixture (capture it from a live
  session, the way `compact-full-manual.jsonl` was), tailer binding tests alongside
  `TranscriptAdoptionSafetyTests`' shapes, and a headed `CodexCanaryTests` arm pinning the real
  record shapes so a CLI update goes red first.

### S2 — measure Codex's TUI delivery shape, then set the spill/ceiling policy (S–M, ~½–1 day)

The CARD-0084-S1 analog. **Nothing is measured about Codex's composer today** — not multi-line
typed input (does a `\n` land as a literal newline or submit? does CR fragment?), not bracketed
paste on the modern backend (accepted? collapsed to a placeholder?), not Enter-on-empty-composer,
not clip behavior. The queue's normalize-LF/wrap/CR contract and CARD-0055's confirm loop assume
answers to these.

- Headed `[Explicit]` canaries through a real ConPTY (the `HeadedCodexGate` harness exists, gated
  on the missing `cx.ps1` — S3's definition fix unblocks it, or point the gate at the npm shim):
  multi-line LF body typed; CRLF body; bracketed-paste body on the modern backend; Enter on empty
  composer; whether the rollout's `user_message` carries the full body (the CARD-0055 confirm
  baseline needs this, same assumption the paste-placeholder canary pinned for Claude).
- **Until measured, the conservative default ships**: treat Codex like Grok in the spill gate —
  brief/refinement inline ceiling 0, everything multi-line spills to
  `.antiphon/task-<id>-brief.md` with a join-safe pointer. This is one arm in `FitBriefForTyping`
  (`kind != ClaudeCode` rather than `== Grok`), and the measurement can *relax* it later, never
  the reverse. That makes S2's measurement non-blocking for S3 — the safe default is independent
  of the answer.
- Pinned by: the canaries above + the fakecodex mirrors in S5.

### S3 — delegate plumbing: allowlists, launch branch, aliases, definition fix (M, ~1 day)

- `AgentTaskService.DelegatableKinds` → `[ClaudeCode, Grok, Codex]`; the rejection text keeps
  naming CARD-0083. Orchestrator restriction unchanged (worker-only).
- `delegate.ps1 -Kind` ValidateSet → `('ClaudeCode', 'Grok', 'Codex')`; the WORKERS-ONLY comment
  gains the Codex line. `DelegateScriptKindTests` gets the Codex arm.
- `ModelLevelAliases.ForCodex`: Frontier → `gpt-5.6-sol`, High → `gpt-5.6-terra`, Medium/Low →
  `gpt-5.6-luna`; `For(kind, …)` gains the Codex arm **in the same commit** (the doc comment's own
  requirement). Keep `MapLegacyModel` delegating to it rather than a second copy.
- `BuildLaunchSpec` Codex branch: resolve the `codex` registry definition; `--model <slug>`; no
  `--name`; instruction bundles ride **`-c developer_instructions="<rendered>"`** — measured
  2026-08-20: `developer_instructions` and `instructions` are both accepted config keys and both
  reach the model (codeword probe answered from them); `experimental_instructions_file`,
  `user_instructions`, `base_instructions` are rejected by `--strict-config`. Use
  `developer_instructions` — `instructions` risks *replacing* Codex's base instructions
  (session_meta carries `base_instructions` as its own field; confirm replace-vs-append semantics
  at implementation and pin it). Command-line budget guard applies as-is. Same branch in
  `AgentControlService` for named Codex agents (fixes the `--model fable` defect, §0).
- **Reasoning effort set explicitly per level** — `-c model_reasoning_effort=…` — because Sol's
  model default is `low` (§1). Proposed: Frontier → `xhigh`, High → `high`, Medium → `medium`,
  Low → `low`; confirm with the user (§8).
- **Fix the codex registry definition**: replace the phantom `cx.ps1` with a resolvable command.
  Recommend restoring `cx.ps1` as a thin documented wrapper in `~/.local/bin` (the catalog's
  guidance already says "a wrapper that owns authentication", and `HeadedCodexGate` looks there
  first), keeping `--no-alt-screen --dangerously-bypass-approvals-and-sandbox`; alternatively
  point the definition at the npm `codex.cmd`. Either way `AgentRegistrySettingsTests` pins it.
- Consider a `codex-delegate` **profile** (`-p`, layering `$CODEX_HOME/<name>.config.toml`) to pin
  approval policy/sandbox and shed the desktop plugin/MCP baggage a delegate inherits from the
  operator's `config.toml` (notify hooks, browser/computer-use plugins, node_repl MCP server —
  all live in the shared CODEX_HOME today). Verify profile semantics at implementation; if flaky,
  `-c` overrides per flag.
- Escalation ladder note: unlike Grok (Frontier and High both grok-4.6), every Codex escalation
  step is a **real model change** (luna → terra → sol) — the escalation event text needs no
  special-casing.
- Pinned by: `AgentTaskAgentKindTests` Codex arms, launch-spec tests alongside CARD-0084 S3's,
  alias tests.

### S4 — Codex pricing entries in `KindRates` (S, ~½ day)

`KindRates` exists; Codex needs entries keyed `gpt-5.6-sol/terra/luna` levels. **Decision needed
(§8): ChatGPT-account auth means marginal cost is $0** — a subscription covers usage. Options:
(a) **shadow-price at OpenAI's published API rates** for the gpt-5.6 family (look them up at
implementation time — do not trust a model's memory of them), so per-root cost ceilings keep
braking Codex dispatch like everything else; (b) zero-rate with an explicit comment, accepting
that ceilings never brake Codex and the real constraint is the ChatGPT usage limit — whose signal
shape is `UsageLimitSignalForm.Unknown` (CARD-0083's survey owns that, and it is the strongest
promotion gate here, same as it was for Grok). Recommend (a): the ceiling is a resource brake,
not an accounting ledger, and (b) silently exempts Codex from the only brake that exists. The
usage rollup itself is free — `token_count.last_token_usage` has the same four counters.

### S5 — proving it: FakeCodex + integration + headed canary (M, ~1 day)

- **FakeCodex does not exist** (only FakeClaude/FakeGrok). Build it modeling S1/S2's *measured*
  behaviors: renders a composer, accepts input per the measured multi-line semantics, writes a
  real rollout JSONL (session_meta/user_message/task_started/agent_message/task_complete/
  token_count) so the tailer and settlement run end to end in CI. Contract-mirror tests the same
  way `FakeGrokContractTests` mirror `GrokCanaryTests`.
- Integration: `-Kind Codex` task → codex definition launched with
  `--model gpt-5.6-…`/`-c developer_instructions` → brief spilled → transcript-confirmed delivery
  → turn-end settlement → cost stamped from Codex rates.
- Headed `[Explicit]` canary: one real Codex delegate task through
  `delegate.ps1 -Kind Codex -Role Test …` — first real mileage, template for promotion evaluation.
- New process-spawning test classes take `[ParallelLimiter<ProcessSpawnLimit>]` (CARD-0050 S5).

**Order:** S1 first (critical path; nothing settles without it), S2 in parallel (its conservative
default unblocks S3 regardless of measurement), S3 after S1, S4 parallel to S3, S5 last.

## 5. Worker-only, opt-in, promotion by config — same posture as CARD-0084

`-Kind Codex` explicit opt-in; no role default changes; orchestrators stay ClaudeCode (rejected
loudly, not reinterpreted). Promotion of a role to Codex is a `RolePolicy.<Role>.Kind` config edit
after real mileage — with the same gates Grok's promotion has: settle rate/report quality over
~20 real tasks, and the ChatGPT usage-limit signal survey (CARD-0083) done, because a Codex
delegate hitting the subscription wall currently looks like a silent stall (exactly the CARD-0090
fable-outage shape).

## 6. What CARD-0083 owns vs this card

Same boundary as CARD-0084 §3: this card ships the mechanical allowlist widening, the measured
delivery shape, the transcript pipeline, aliases and rates; CARD-0083 owns the capability-query
replacement for the allowlist, the usage-limit/quota survey (ChatGPT-account limits for Codex),
and where provider-self-reported cost lives. S1's contract-catalog update *is* CARD-0083-shaped
data and should be written as such (measured reason strings, states), not as prose.

## 7. Interaction with CARD-0090 (question 4)

**Ship CARD-0099 first, fully independently.** CARD-0090's "Medium → Codex" example is *blocked
on* this card (its own text says so); nothing in this card depends on CARD-0090. The touch points:
0090 consumes `DelegatableKinds`/`ResolveAgentKind` as the candidate space for its fallback
chains, and 0090's "is this kind available?" question lands on exactly the usage-limit-signal gap
flagged in §5/§6 — worth one line in 0090's plan pointing at CARD-0083's survey rather than
designing detection twice. No shared config surface: 0090's ComplexityPolicy would sit above
`RolePolicy`, not inside it. Do not bundle the two; a joint ship only couples 0090's genuinely
open design forks (who classifies, what "unavailable" means) to mechanical work that is ready now.

## 8. Decisions for the user

1. **Pricing on a subscription** (§4 S4): shadow API rates (recommended) or zero-rate?
2. **Per-level reasoning effort** (§4 S3): proposed Frontier→xhigh, High→high, Medium→medium,
   Low→low. Your interactive config runs terra@xhigh — want High→xhigh instead?
3. **`cx.ps1` restore vs repoint** (§4 S3): restore the wrapper at `~/.local/bin/cx.ps1`
   (recommended — matches the test gate and the catalog guidance) or change the definition to the
   npm `codex.cmd` directly?
4. **Low tier**: mapped to `gpt-5.6-luna` (same as Medium). If a genuinely cheaper rung is wanted,
   `gpt-5.4-mini` exists — but three names were asked for, so Luna-for-both is the default.

## 9. Measured-fact appendix (all 2026-08-20, codex-cli 0.147.0, ChatGPT-account auth)

- `-m gpt-5.6-luna` accepted; `-m luna` → metadata warning + 400 (full slugs mandatory).
- `codex exec` prints `session id: <uuid>`; inherits `model_reasoning_effort` from config.toml.
- `-c developer_instructions="…"` and `-c instructions="…"` both accepted under `--strict-config`
  and both reach the model (codeword probe). `experimental_instructions_file`, `user_instructions`,
  `base_instructions` rejected.
- Rollout JSONL at `CODEX_HOME/sessions/YYYY/MM/DD/rollout-<ts>-<uuid>.jsonl`; record types:
  session_meta, turn_context, world_state, event_msg{user_message, agent_message, task_started,
  task_complete, token_count, …}, response_item{message, reasoning, function_call, …};
  `token_count.info.last_token_usage` = {input, cached_input, cache_write_input, output, reasoning
  output, total}.
- `cx.ps1` absent from this machine (PATH, `~/.local/bin`, registry PATHs); npm `codex.cmd` and
  Desktop `codex.exe` present.
- Codex TUI composer behavior (multi-line, paste, Enter semantics): **unmeasured** — S2.
