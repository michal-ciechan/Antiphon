# CARD-0193 — A card spawn must resolve "no exact model" the same way a cardless start does — plan

**Date:** 2026-08-25 · **Card:** CARD-0193 (`e45da64a-9cda-4776-b60a-dd647e8bb8f5`), filed out of
CARD-0182's §6 "out of scope, stated" · **Status:** plan (no implementation in this pass) ·
**Verified against:** `master` @ `28e91fe`, with CARD-0182's four slices (`1c14cfd`, `8cd20be`,
`e7e60d3`, `81cccf8`) already in. Every file:line below was re-read out of the code on that commit,
and the agent census in §1.6 was run against the live dev database (`antiphon-postgres`, 2026-08-25).

---

## Verdict up front

The card's diagnosis is right about the symptom and wrong about the contrast, and the fix it guesses
at ("one line at each of the two named sites") is the one shape that should **not** be taken —
because it would be the third and fourth copy of a decision CARD-0182 just finished consolidating
into one.

| # | Decision | One line |
|---|---|---|
| D1 | The tier travels as a **level**, not a pre-computed alias: add `AgentLaunchOptions.ModelTier` (`AgentModelLevel?`) | The two named sites cannot compute an alias — they learn the agent KIND as an *output* of resolution, not an input |
| D2 | The derivation lives in the **funnel**, `AgentLaunchResolution.ResolveForAgentAsync`, beside the `AgentEnv` attach that is already there for exactly this reason | `CardService.cs` and `OrchestratorService.cs` change by **zero lines**, and so does the next caller nobody has written yet |
| D3 | The level→alias map lives in the **two appenders** (`AgentTuiLaunchResolver.ApplyModelArgument`, `AgentRegistry.Resolve`), keyed on the kind each of them resolves, with a **null arm** for kinds that have no ladder | Reproduces today's `isClaudeCode / isGrok / isCodex` gate without copying it, and keeps CARD-0182's "only two things may emit `--model`" structural rule |
| D4 | An explicitly-supplied `TierModelAlias` still wins over a derived `ModelTier`; both lose to an exact `ModelId` | `AgentControlService` and `AgentTaskDispatcher` keep byte-identical arguments in this slice — none of their tests move |
| D5 | The **unassigned** card spawn (no `AssignedAgent`) keeps passing nothing | There is no agent, so there is no level anyone chose; the synthetic default agent's `ModelLevel` is a property initializer, not a decision |

Net diff shape: one nullable field on a record, one `with`-expression line in the funnel, one small
mapping method with a null arm, two ~4-line appender changes, one provenance line. **Risk: low**, and
the blast radius is measured in §1.6 — three standing agents on this machine, all Claude, each
gaining the flag its own Start button already passes.

---

## 1. Established facts (Investigate, this pass)

### 1.1 The contrast in the card is not card-spawn vs the Start button. It is card vs cardless.

`AgentControlService.StartAsync` forks on whether the agent has a spawnable card, and the card arm
**is** the card-spawn path:

```csharp
var card = await ResolveStartCardAsync(agent, ct);          // AgentControlService.cs:120
if (card is not null)
{
    var spawn = await _cardService.SpawnAsync(...);          // :127  -> CardService.SpawnAsync
}
else
{
    sessionId = await StartInteractiveSessionAsync(...);     // :137  -> the tier-alias block
}
```

The whole `extraArgs` / `tierModelAlias` composition (`:198-281`) lives inside
`StartInteractiveSessionAsync` and is reached **only** by the cardless arm. So pressing Start on an
agent that has a current or queued card produces no `--model` either. The card says the same
blank-`ModelId` agent "gets a `--model <tier-alias>` argument when started manually via the Start
button" — that is true only while the agent is cardless, which for a card-worked agent is the
uncommon case. The defect is **wider** than reported; the contrast it is described by is **wrong**.

There are therefore not two card-spawn entry points but four, all funnelling into
`CardService.SpawnAsync` (`CardService.cs:545`) or its orchestrator twin:

| Entry | Reaches | Tier today |
|---|---|---|
| `POST /api/cards/{id}/spawn` (board Start / modal) | `CardService.SpawnAsync:545` | none |
| A move into an active column with `-Spawn` | `CardService.cs:306` → `SpawnAsync` | none |
| The orchestrator auto-dispatch tick | `OrchestratorService.cs:124` → `ResolveDispatchLaunchAsync:594` | none |
| `AgentControlService.StartAsync` **with a card** | `AgentControlService.cs:127` → `SpawnAsync` | none |
| `AgentControlService.StartAsync` **cardless** | `StartInteractiveSessionAsync:154` | **alias** |

### 1.2 Card-spawn already goes through `ApplyModelArgument`. It just never offers it a tier.

The card wonders whether card-spawn "might build an `AgentLaunchSpec` directly". It does not — for an
assigned agent it calls the same funnel `AgentControlService` calls:

- `CardService.cs:616` — `AgentLaunchResolution.ResolveForAgentAsync(assignedAgent, …)`
- `OrchestratorService.cs:611` — the same call, inside `ResolveDispatchLaunchAsync`
- `AgentControlService.cs:285` — the same call, with `TierModelAlias: tierModelAlias`

All three land in `AgentTuiLaunchResolver.ResolveCoreAsync` → `ApplyModelArgument`
(`AgentTuiLaunchResolver.cs:375`, body at `:453-498`). The consequence, which narrows the card:

**An exact `ModelId` already works on a card spawn today.** `ApplyModelArgument` reads
`agent.ModelId` off the agent it was handed (`:460`), so rule 2 fires and the process gets
`[--model, <exact id>]`, catalogue-checked. The only missing arm is rule 3, the tier fallback, which
reads `options.TierModelAlias` (`:461-463`) — and the two card-spawn sites construct their
`AgentLaunchOptions` without it (`CardService.cs:619-631`, `OrchestratorService.cs:599-604`).

So the defect is precisely: **rule 3 has no input on the card paths.** Not "card-spawn bypasses the
model logic".

### 1.3 Why it cannot be a one-line fix at the two named sites

To fill `TierModelAlias` a caller must pick between `ModelLevelAliases.ForClaude/ForGrok/ForCodex`,
which needs the agent **kind**. Neither card-spawn site has it: `spec.Kind` is an *output* of the
resolve whose options they are constructing. `AgentControlService` solves this with a separate
pre-flight query, `PeekProfileKindAsync` (`AgentControlService.cs:413-439`) — a three-step fallback:
the agent's own profile, else the installation default profile, else the registry default
definition's kind.

Copying that into `CardService` and `OrchestratorService` would mean:

- a third and fourth copy of the kind→alias branch that CARD-0182 D2 existed to reduce to one, and
- two more callers of a private DB pre-flight, each free to drift from the profile the resolver
  actually loads a moment later.

**`agent.Kind` is not a shortcut past this.** The column's invariant (`Agent.cs:96-119`) is *"if
`TuiProfileId` is set, this equals that profile's `Kind`"* — it says nothing about the null case, and
the null case is exactly where `PeekProfileKindAsync` falls through to the **installation default
profile**, whose kind may be anything. An agent row with no profile defaults to
`AgentKind.ClaudeCode` (`Agent.cs:119`) while the resolver launches whatever the default profile is.
Deriving from `agent.Kind` would be a silent regression on that path.

### 1.4 The funnel already attaches per-agent facts for this exact reason

`AgentLaunchResolution.ResolveForAgentAsync` opens with:

```csharp
// The agent's own launch env, attached HERE (CARD-0106 S2) rather than at each of the five
// call sites this funnel serves — a caller that forgot would launch the agent without the
// environment somebody configured for it, silently.
options = options with { AgentEnv = options.AgentEnv ?? AgentLaunchEnv.ParseForAgent(agent) };
```

(`AgentTuiLaunchResolver.cs:40-46`)

That comment is this card's design, already written down for a different field. The agent's tier is
the same kind of fact: a per-agent decision every launch of that agent should carry, which two of the
funnel's five call sites currently forget.

### 1.5 The level survives the funnel; the alias cannot

The funnel is static and holds no `DbContext`, so it cannot resolve the kind either — but it does not
need to. It can pass the **level** through and let each appender, which already knows its kind, do
the mapping:

- profile path: `AgentTuiLaunchResolver.ResolveCoreAsync` has `profile.Kind` (`:430`)
- profile-less path: `AgentRegistry.Resolve` already parses `def.Kind` into an `AgentKind` at
  `AgentRegistry.cs:105-110` and then ignores it when appending (`:118-122`)

`AgentTaskDispatcher.TierAliasFor` (`AgentTaskDispatcher.cs:1986-1993`) is already exactly this
mapping, **including the null arm** for kinds with no ladder:

```csharp
kind == Codex ? ForCodex(level)
    : kind == Grok ? ForGrok(level)
    : kind == ClaudeCode ? ForClaude(level)
    : null;
```

Note it is *not* `ModelLevelAliases.For(kind, level)` (`ModelLevelAliases.cs:76-81`), whose `_ =>` arm
falls back to the Claude ladder and whose own doc forbids launch arguments from coming through it
(`:72-74`: *"a wrong alias there is a wrong process, not a wrong word"*). The null arm is what
reproduces today's `isClaudeCode || isGrok || isCodex` gate, so Raw and OpenCode keep getting nothing.

### 1.6 Blast radius — census against the live dev database, 2026-08-25

Standing (non-pool) agents with a blank `ModelId`, i.e. the rows whose card spawns change:

| Agent | Profile | `ModelLevel` | Gains on a card spawn |
|---|---|---|---|
| `ClaudeBot-Antiphon` | `claude` (`--model`) | High | `--model opus` |
| `Codeperf` | `claude` (`--model`) | Medium | `--model sonnet` |
| `Slack Test` | `claude` (`--model`) | Medium | `--model sonnet` |

Every other standing agent already pins an exact `ModelId` (`opus`, `fable`, `haiku`, `sonnet`,
`grok-4.6`, `gpt-5.6-terra`) and is therefore **already** getting `--model <exact>` on card spawns
today (§1.2) — this change does not touch them. Pool-delegate rows are dispatcher-launched, never
card-spawned. The four profiles on this machine are `claude` / `codex` / `grok`
(`ModelArgumentName = "--model"`) and `raw-pwsh` (`null`), so CARD-0182 D1 rule 1 has a live subject
here: a `raw-pwsh` agent must keep getting nothing, derived tier or not.

In one sentence: **three agents, all Claude, each gaining on a card spawn the same flag its Start
button already passes.** That is the "own census" CARD-0182 §6 asked for before this behaviour change
lands on every card spawn at once.

---

## 2. The proposed fix

### 2.1 D1 — `AgentLaunchOptions.ModelTier`

```csharp
// CARD-0193: the agent's TIER, for a caller that cannot compute an alias because it learns the
// kind as an OUTPUT of resolution. The appenders map it against the kind they resolve. Null =
// no tier on offer. A pre-computed TierModelAlias still wins where a caller supplies one,
// because it may be keyed on a kind this launch does not resolve to (the dispatcher keys on
// session.AgentKind, not profile.Kind).
AgentModelLevel? ModelTier = null
```

`TierModelAlias` is **kept**, not replaced. Collapsing both into one field is tempting and is
deliberately not done here: `AgentTaskDispatcher` maps `task.ModelLevel` against `session.AgentKind`
(`:1954`), which is not guaranteed equal to `profile.Kind` — there is no composite
`(TuiProfileId, Kind)` foreign key (CARD-0138 D4, still open). Folding the dispatcher onto
`ModelTier` would silently re-key its alias onto the profile's kind. §5 keeps that as a follow-up
with its own evidence, not a free rider on this card.

### 2.2 D2 — one derivation line, in the funnel

`AgentLaunchResolution.ResolveForAgentAsync`, immediately after the existing `AgentEnv` attach
(`AgentTuiLaunchResolver.cs:43-46`):

```csharp
// CARD-0193: the agent's tier, attached HERE for the same reason AgentEnv is — two of this
// funnel's five call sites (CardService.SpawnAsync, OrchestratorService.ResolveDispatchLaunchAsync)
// cannot compute an alias, and so forgot the tier entirely. An exact ModelId outranks the tier,
// so a pinned agent offers none; the appenders enforce that ordering again anyway.
options = options with
{
    ModelTier = options.ModelTier
        ?? (string.IsNullOrWhiteSpace(agent.ModelId) ? agent.ModelLevel : null)
};
```

`ResolveDefaultAsync` (`:75`) is deliberately **not** touched — D5. It has no agent; the synthetic one
`AgentTuiLaunchResolver.ResolveDefaultAsync` builds (`:265-271`) carries `ModelLevel = High` from a
property initializer (`Agent.cs:23`), not from anybody's decision, and treating that as a tier would
put `--model opus` on every unassigned card spawn on this machine (the installation default profile
is `claude`, `--model`). That is a behaviour change with no operator intent behind it.

### 2.3 D3 — the level→alias map, in the two appenders

Promote the dispatcher's private mapping to `ModelLevelAliases.ForLaunch(AgentKind, AgentModelLevel)`
returning `string?`, body identical to `AgentTaskDispatcher.TierAliasFor` (`:1986-1993`) — **including
the null arm** — with an XML doc saying why it is separate from `For(kind, level)` (§1.5). Add a
sentence to `For`'s existing doc paragraph pointing at it, since that paragraph currently says launch
arguments "branch explicitly at the sites that build them" and after this card they do not.

`AgentTuiLaunchResolver.ApplyModelArgument` (`:453-498`) — the tier source becomes:

```csharp
var tierAlias = string.IsNullOrWhiteSpace(options.TierModelAlias)
    ? (options.ModelTier is { } level ? ModelLevelAliases.ForLaunch(profile.Kind, level) : null)
    : options.TierModelAlias.Trim();
```

Everything downstream of that line is untouched: rule 1 (blank `ModelArgumentName` ⇒ nothing,
provenance `ProfileOwned` when a tier was on offer), rule 2 (exact `ModelId` wins), rule 3 (append the
alias, provenance `Tier`), rule 4 (nothing). A derived tier is a tier — it must produce `ProfileOwned`
under rule 1 exactly as a supplied alias does, or a `raw-pwsh` card spawn would log `None` where the
cardless path logs `ProfileOwned`.

`AgentRegistry.Resolve` (`AgentRegistry.cs:118-122`) — the same precedence, using the `kind` it
already parsed at `:105`:

```csharp
var alias = string.IsNullOrWhiteSpace(options.TierModelAlias)
    ? (options.ModelTier is { } level ? ModelLevelAliases.ForLaunch(kind, level) : null)
    : options.TierModelAlias.Trim();
if (alias is not null) { args.Add("--model"); args.Add(alias); }
```

`AgentLaunchResolution.ResolveLegacyAsync`'s provenance (`AgentTuiLaunchResolver.cs:157-159`) reads
`options.TierModelAlias` alone and must learn `ModelTier` too, or a legacy-path card spawn reports
`None` while passing a flag. It cannot call `ForLaunch` itself (no kind in hand). Cheapest sufficient
fix: report `Tier` when either input is set, `None` otherwise — accepting that a Raw *definition*
would over-report `Tier` while correctly appending nothing. **This is the one honest wart in the
plan.** If it matters, the alternative is a small out-parameter on `AgentRegistry.Resolve` returning
the decision it actually made; the implementer may take it, at the cost of touching that signature's
four call sites.

### 2.4 What does NOT change

- `CardService.cs` — zero lines. `OrchestratorService.cs` — zero lines.
- `AgentControlService.cs:198-281` — zero lines. It keeps computing its own alias; D4 gives that
  precedence, so its arguments stay byte-identical and `LaunchModelArgumentAppenderTests` (the
  structural "these two files never contain the literal `--model`" pin) stays green untouched.
- `AgentTaskDispatcher.cs` — zero lines, same reason.
- No migration. No DTO or API-surface change. No client change.

---

## 3. Tests

| # | Test | Where | Pins |
|---|---|---|---|
| T1 | Card spawn of an assigned agent, blank `ModelId`, `ModelLevel = High`, `claude` profile (`--model`) ⇒ `adapter.StartedArgs` contains `--model` immediately followed by `opus` | new `tests/Antiphon.Tests/Application/CardSpawnModelArgumentTests.cs` | The card's own shape. Mirrors the harness/adapter pattern of `AgentControlServiceIntegrationTests.T6_blank_field_grok_profile_starts_without_a_model_argument` (`8cd20be`) |
| T2 | The orchestrator auto-dispatch tick on the same agent ⇒ the same pair | `tests/Antiphon.Tests/Application/OrchestratorServiceIntegrationTests.cs` | The second named site — a different funnel caller, not the same code |
| T3 | **Parity**: the same agent started cardless and spawned on a card produce the same `--model` pair | `CardSpawnModelArgumentTests` | The consistency the card is actually about; the one test that fails if a future change re-splits the paths |
| T4 | Card spawn on a profile with blank `ModelArgumentName` ⇒ no `--model` and no alias value; provenance `ProfileOwned` | `CardSpawnModelArgumentTests` (reuse `SeedBlankModelArgumentProfileAsync`) | CARD-0182 D1 rule 1 must outrank the new derived tier — the `raw-pwsh` shape that exists on this machine |
| T5 | Card spawn of an agent with an exact `ModelId` ⇒ **exactly one** `--model`, value is the exact id | `CardSpawnModelArgumentTests` | Regression: §1.2 says this already works; the new derivation must not double-append |
| T6 | Card spawn with **no** assigned agent ⇒ still no `--model` | `CardSpawnModelArgumentTests` | D5, the deliberate scope boundary. Fails loudly if somebody later "finishes the job" by wiring `ResolveDefaultAsync` |
| T7 | Resolver unit: `ModelTier: High` ⇒ `--model grok-4.6` on a Grok profile, `--model gpt-5.6-terra` on Codex, **nothing** on a Raw profile | `tests/Antiphon.Tests/AgentTui/AgentTuiLaunchResolverTests.cs` | D3's null arm — today's kind gate, preserved without being copied |
| T8 | Resolver unit: an explicit `TierModelAlias` plus a conflicting `ModelTier` ⇒ the explicit alias is what lands | same | D4 precedence, which is what keeps the dispatcher and control-service arguments unchanged |
| T9 | `ModelLevelAliases.ForLaunch` returns null for `Raw`/`OpenCode`, and the Claude/Codex rungs and `grok-4.6` otherwise | `tests/Antiphon.Tests/Application/`, beside `LaunchModelArgumentAppenderTests` | Cheap arithmetic pin, so a T7 failure points at the map rather than the resolver |

Existing suites that must stay green **unchanged** — they are the proof D4 held:
`AgentControlServiceIntegrationTests`, `PinnedProfileLaunchSpecTests`,
`PinnedCodexProfileDispatchLaunchTests`, `DelegateLaunchArgvIntegrityTests`,
`LaunchModelArgumentAppenderTests`, `AgentTuiLaunchResolverTests`.

---

## 4. Scope changes from what CARD-0193 assumes — stated plainly

1. **Not a one-line fix, and not at the sites the card names.** Both named sites change by zero
   lines; the change is one field, one funnel line, two appender edits and a map (§1.3, §2). The
   card's instinct — "route card-spawn through the same tier-alias-appending path" — is right; the
   path is the funnel, not the caller.

2. **The described contrast is wrong, and the defect is wider than reported.** The Start button hits
   the same gap whenever the agent has a card (§1.1). Card presence, not manual-vs-auto, is the
   discriminator, and there are four affected entry points rather than two.

3. **The defect is also narrower than reported.** "Card-spawn passes no model tier at all" is true;
   "so the agent runs the runner's default" is only true for a blank `ModelId`. An exact `ModelId`
   has always worked on card spawns, because the resolver reads it off the agent itself (§1.2). On
   this machine that covers 11 of the 14 standing agents.

4. **The census is small and one-directional.** Three agents, all Claude, each gaining the flag its
   cardless start already passes (§1.6). No agent loses an argument; no `raw-pwsh` agent gains one.

5. **A much larger adjacent gap was found and is NOT this card.** The card arm of
   `AgentControlService.StartAsync` skips *all* of `StartInteractiveSessionAsync`, so a card-spawned
   session also gets **none** of:
   - `--name <agent>` for Claude (`:212`);
   - the composed instruction bundles + reply-style block + `SystemPromptAppend`, i.e.
     `--append-system-prompt` / `--rules` / Codex developer instructions (`:236-281`) — which for a
     channel-bound agent is its channel preamble;
   - Codex's explicit `model_reasoning_effort` override (`:227-233`);
   - the `ANTIPHON_API` / `ANTIPHON_AGENT_ID` / `ANTIPHON_TASK_TOKEN` block and the session's
     `DelegationTokenHash` (`:180-186`, `:372`). The only two writers of that hash are
     `AgentControlService` and `AgentTaskDispatcher` (`:2095`), so **a card-spawned session cannot
     authenticate its own delegations**: `scripts/delegate.ps1` run from inside it falls back to the
     manual-UI identity whose 2026-08-09 live miss is recorded in `AgentSession.cs:45-53`.

   This is established from the code paths only — no runtime repro was attempted, and it is not
   asserted here as a live incident. It is a separate card, and probably a bigger one than
   CARD-0193; the model argument can be fixed in the funnel, but the bundles and the delegation
   token are per-launch compositions that would have to move out of `StartInteractiveSessionAsync`
   into something both arms call. **Recommend filing it before implementing this card**, so the
   implementer is not tempted to "just also add" the env block while they are in the file.

---

## 5. Out of scope, stated

- **Collapsing `TierModelAlias` into `ModelTier`.** The tidier end state; needs the
  `session.AgentKind` vs `profile.Kind` question settled first (§2.1, CARD-0138 D4).
- **The unassigned and `DefinitionName` card-spawn branches** (`CardService.cs:589-612`, `:638-657`).
  No agent, no chosen level — D5, pinned by T6.
- **Everything in §4.5** — bundles, `--name`, Codex reasoning effort and the delegation token on
  card-spawned sessions. Its own card.
- **`PeekProfileKindAsync`'s three-step fallback vs the profile the resolver actually loads.** They
  can disagree in one edge (the agent's profile row is deleted between the peek and the resolve).
  After this card the derived path is keyed on the profile that actually launches, which is the
  better answer; the peek stays only for the quota gate and the `extraArgs` composition.
- **Herdr / `SessionBackend`** — orthogonal; arguments are composed before the backend is chosen.
