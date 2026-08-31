# CARD-0289 — Tier-based reasoning effort for Grok and Claude dispatches

**Date:** 2026-08-31
**Status:** Plan only — no implementation; investigation probes run and recorded below.

## Outcome and design decision

Set reasoning effort EXPLICITLY on every Grok and Claude launch, exactly the way
`CodexLaunchArgs.ReasoningEffortOverride` already does for Codex: the dispatch's `-Level` tier
decides the depth, so neither the provider's per-model default nor the operator's own config file
does. Both halves are plain **launch arguments** — no boot-time slash-command typing anywhere.

The card's feared Claude hazard is moot: the `/effort` boot-typing design is **rejected** because
claude 2.1.251 exposes `--effort <level>` as a first-class CLI launch flag. There is no picker to
answer, no verified-submission handling, and no CARD-0055/0056-class typed-into-a-live-session risk.
The Grok half and the Claude half are the same shape and the same size; neither blocks the other.

## Measured evidence (2026-08-31, this machine)

### Grok — grok CLI 1.0.13 (`~/.grok/bin/grok.exe`, the binary the `grok` registry definition runs)

- `grok --help` lists a top-level option `--reasoning-effort <EFFORT>` ("Reasoning effort for
  reasoning models", alias `--effort`). It sits on the main TUI command — the same invocation
  Antiphon launches with `--always-approve --no-alt-screen` (`appsettings.json` AgentRegistry).
- The model catalog (`~/.grok/models_cache.json`, fetched 2026-08-31 from
  `https://cli-chat-proxy.grok.com/v1/models`): **grok-4.6** has `supports_reasoning_effort: true`
  with efforts **xhigh / high / medium / low**, default **high**. **grok-4.5** offers only
  high / medium / low — **no xhigh** — default high.
- The operator-config hazard is live, same shape as Codex's: `~/.grok/config.toml` sets
  `[models] default_reasoning_effort = "high"`. Left alone, a deliberately cheap Low-tier delegate
  inherits high, and a Frontier delegate is capped at high while xhigh exists. Explicit beats both.
- Values are NOT validated at parse time: `grok --reasoning-effort bogus doctor` runs cleanly, and
  the help shows no `[possible values:]` list (unlike `--permission-mode`). A wrong value surfaces
  only at runtime, so the mapping must emit catalog values only.

### Claude — claude 2.1.251

- `claude --help`: `--effort <level>` — "Effort level for the current session (low, medium, high,
  xhigh, max)".
- Live probe: `claude -p "…" --model haiku --effort low` answered normally. The flag is accepted on
  the lowest ladder rung, so it can be passed unconditionally for all four tier models
  (fable/opus/sonnet/haiku).
- Degradation is graceful: `--effort bogus` prints `Warning: Unknown --effort value 'bogus' —
  ignoring it and using the default effort. Valid values: low, medium, high, xhigh, max.` and the
  session runs at the default. A bad value cannot wedge a Claude launch.

## The mapping

One tier→effort policy, identical across all three providers and identical to the Codex table that
already ships:

| Tier | Effort |
|---|---|
| Frontier | `xhigh` |
| High | `high` |
| Medium | `medium` |
| Low | `low` |
| default arm | `high` |

- **Grok:** all four values are in grok-4.6's catalog, and the level ladder pins grok-4.6 at every
  rung (CARD-0169), so the ladder can never emit an out-of-catalog value.
- **Claude:** all five CLI values include these four; `max` is deliberately unused, the same way
  Codex's `max`/`ultra` are — headroom for a manual session, not a tier. Do not map Frontier→`max`:
  the Claude ladder already scales the MODEL per tier, and the Codex precedent for "Frontier depth"
  is xhigh. Changing frontier Claude runs to `max` is a cost/latency decision the card did not ask
  for.

**Code shape:** add two small static classes mirroring `CodexLaunchArgs`
(`server/Application/Services/`):

- `GrokLaunchArgs` — `ReasoningEffortFlag = "--reasoning-effort"` (use the canonical name, not the
  `--effort` alias) and `ReasoningEffort(AgentModelLevel)` with the switch above.
- `ClaudeLaunchArgs` — `EffortFlag = "--effort"` and `Effort(AgentModelLevel)` with the same switch.

Keep the three switches independent (each carries its own provider evidence in the doc comment, the
house style), and pin them together with a cross-provider agreement TEST rather than a shared
abstraction — see Tests. Passing shape is TWO argv elements (`"--reasoning-effort", "xhigh"`), not
one `flag=value` string; both CLIs take space-separated values.

## Wiring sites (exactly two — the same two Codex uses)

All production launch argv composition flows through these methods; `RunnerGrokAdapter` and the
other protocol adapters consume the composed spec and build no argv of their own.

1. **`AgentTaskDispatcher.ComposeDelegateArgs`** (`AgentTaskDispatcher.cs` ≈2347–2367) — delegate
   tasks and pool spawns. Next to the existing `isCodex` effort block add:
   - `isGrok` → `extraArgs.AddRange([GrokLaunchArgs.ReasoningEffortFlag, GrokLaunchArgs.ReasoningEffort(task.ModelLevel)])`
   - `kind == AgentKind.ClaudeCode` → `extraArgs.AddRange([ClaudeLaunchArgs.EffortFlag, ClaudeLaunchArgs.Effort(task.ModelLevel)])`

   Gate the Claude arm on the kind EXPLICITLY, not on the `!isGrok && !isCodex` else-shape the
   `--name` branch uses — a future OpenCode/Raw delegate must not inherit `--effort`. Add both arms
   BEFORE the `EnsureWithinCommandLineBudget` call so the budget guard counts them (that is where
   the Codex override already sits).
2. **`AgentSessionLaunchComposer.ComposeForAgentAsync`** (`AgentSessionLaunchComposer.cs` 58–72) —
   named agents (`AgentControlService`), card spawns (`CardService`), orchestrator
   (`OrchestratorService`). Same two arms beside the `isCodex` block, keyed on `agent.ModelLevel`,
   before its `EnsureWithinCommandLineBudget` call.

CARD-0182 D2 is untouched: effort is not a model argument, so it does not ride `TierModelAlias` or
the resolver's `--model` ownership. It is a plain extraArg like `DisablePasteBurst`.

## What existing machinery already guarantees (verify, don't build)

- **Warm-pool reuse:** the claim predicate requires `a.ModelLevel == claimed.ModelLevel`
  (`AgentTaskDispatcher.cs:2671`), so a warm delegate's launch-time effort always matches any task
  that can claim it — even on Grok, where every level is the same model id. No relaunch or
  effort-retyping logic is needed; a warm delegate keeps its effort the way it keeps its bundles.
- **Escalation now buys something on Grok:** `AgentTaskService.SameModelEscalationNote`
  (`AgentTaskService.cs:979`) currently tells every Grok escalation it is "a FRESH CONTEXT at the
  same model, not a larger one." Once effort is tier-wired that undersells the event: a Grok
  Low→Medium escalation buys medium effort over low. Extend the note: when both rungs map to the
  same alias AND the kind's tier-wired efforts differ, say the escalation is the same model **at
  deeper reasoning effort (low → medium)** with a fresh context. Keep the existing wording
  byte-identical when efforts are equal. Claude never takes this arm (four distinct aliases). The
  event text is a promise an interpreter reasons over (CARD-0084 S4) — this edit is part of the
  card, not polish.

## Edge cases

- **grok-4.5 + Frontier → `xhigh` is out of catalog for that model.** The ladder cannot produce the
  pairing (ForGrok pins 4.6 everywhere); it needs an explicit profile/agent ModelId of grok-4.5
  under a Frontier-level dispatch. Runtime behaviour of an out-of-catalog effort was NOT probed live
  (one Grok call would settle it; not spent during a plan pass with recurring quota outages — Codex
  was down tonight). Build pass: run `grok -m grok-4.5 --reasoning-effort xhigh -p "say ok"` once.
  If it degrades gracefully (Claude-style), document and ship; if it refuses to launch, clamp to
  `high` at the two compose sites when the effective model id is grok-4.5, or accept the refusal as
  operator error — operator's call, record it in the commit.
- **Command-line budget:** two extra argv elements, ~25 characters — counted by the existing
  `EnsureWithinCommandLineBudget` guard because the args are added before it runs.
- **OpenCode/Raw:** receive no effort flag (explicit kind gates).

## Tests

- **New `GrokLaunchArgsTests` and `ClaudeLaunchArgsTests`** mirroring `CodexLaunchArgsTests`: the
  per-tier value table and the monotonicity test ("a higher tier must never reason shallower"),
  with the Grok one asserting values ⊆ grok-4.6's catalog list and the Claude one asserting values
  ⊆ the CLI's five. Plus ONE cross-provider agreement test — for every `AgentModelLevel`,
  `CodexLaunchArgs.ReasoningEffort(l) == GrokLaunchArgs.ReasoningEffort(l) ==
  ClaudeLaunchArgs.Effort(l)` — so the three independent switches cannot drift silently.
- **`CardSpawnModelArgumentTests`**: add Grok and Claude siblings of
  `Codex_assigned_card_spawn_carries_the_reasoning_effort_override` (line 74) asserting
  `StartedArgs` contains the flag and the High-tier value.
- **`GrokDelegateDispatchTests`**: extend the dispatch-args assertions with
  `--reasoning-effort <tier value>`, and extend the escalation-note tests (lines ≈390–422) for the
  new deeper-effort wording. Note the Codex ladder has its own alias-equal rung: a Codex Low→Medium
  escalation is alias-equal (both gpt-5.6-luna) with DIFFERING tier efforts (low→medium), so
  `CodexDelegateDispatchTests`' note assertions (lines ≈189–221) take the new wording too — update
  them alongside, don't treat them as untouchable.
- **`DelegateLaunchArgvIntegrityTests`** / **`HerdrLaunchShapeTests`**: check whether their argv
  snapshots enumerate exact args; update expectations if so.
- Run scoped: `dotnet run --project tests/Antiphon.Tests` with
  `--treenode-filter "/*/Antiphon.Tests.Application/*/*"` (never `dotnet test`; alternate
  `--property:OutputPath=bin-<name>/` with a forward slash while daemons hold bin).

## Build-pass checklist

1. `GrokLaunchArgs.cs` + `ClaudeLaunchArgs.cs` with evidence doc comments (cite CLI versions and
   probes from this plan).
2. Both arms in `AgentTaskDispatcher.ComposeDelegateArgs` and
   `AgentSessionLaunchComposer.ComposeForAgentAsync`, before the budget guards.
3. `SameModelEscalationNote` deeper-effort wording.
4. The one-shot grok-4.5 xhigh live probe; clamp or document per its result.
5. Tests above; verify one real Grok delegate launch shows the effort in its TUI status line and one
   Claude delegate session reports the set effort (`/effort` shows the current value) — transcript
   evidence, not screen guesses.

## Rejected alternatives

- **Typing `/effort <level>` into a booting Claude session** — strictly dominated by the launch
  flag: a typed command needs verified-submission handling, races the boot sequence, and lands in
  the transcript; the flag does none of that. Keep `SlashCommandCatalogService`'s `/effort` entry
  as-is (recognition only).
- **Frontier→`max` on Claude** — a cost/latency escalation beyond the card's ask; xhigh matches the
  Codex precedent for what "Frontier depth" means here.
- **A shared tier→effort map abstraction** — three 4-arm switches with provider-specific evidence
  comments plus an agreement test keeps each provider's file self-evident and lets a future
  provider diverge deliberately (Codex could one day want `ultra`) without unwinding a shared type.
