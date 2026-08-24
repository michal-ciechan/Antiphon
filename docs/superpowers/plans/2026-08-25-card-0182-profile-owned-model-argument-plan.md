# CARD-0182 — A profile that declares no model argument must be honoured by every launch path — plan

**Date:** 2026-08-25 · **Card:** CARD-0182 (`090eec5b-cd67-431d-82d0-d959c75ddd63`), a
GitHub-imported bug report with a proven root cause and a verified live workaround ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** `master` @ `92a4bf0`. Every file:line below was re-read out of the code on
that commit. CARD-0169 (`7d08655`, the Grok ladder collapse) touched `ModelLevelAliases.cs`,
`AgentTaskService.cs`, `DelegationPricingSettings.cs` and three test files — **not** either
injection site, not the resolver, not the profile service. It changed which string is injected
(`grok-4.6` at every tier); it did not change whether one is. The injection predicate
`string.IsNullOrWhiteSpace(agent.ModelId)` has stood since `fdfec20` (profile launch), and the Grok
arm since `5754e02` (Grok as a first-class runner).

**Where the live state is — and is not.** The card's `grok-gkp-project` profile, `gkp.ps1` and
`gk-common.ps1` are on the reporter's machine (`C:\Users\mike.ciechan\.local\bin`, the same place
the operator guide's `ocg.ps1` lives). This machine has neither: its four profiles are `claude`,
`codex`, `grok` (all three `modelArgumentName: "--model"`, all `WrapperManaged`) and `raw-pwsh`
(`null`). Nothing here can reproduce the exit-1; the mechanism below is reconstructed from the
card's quoted wrapper message plus the code, and it needs nothing from the wrapper beyond the two
facts the card states — it accepts exactly `--model maven-grok`, and it pins `maven-grok` itself
when no `--model` is passed.

---

## Established facts (Investigate, this pass)

- **Both injection sites are unconditional on the profile, and the report has the sites right.**
  `AgentControlService.StartInteractiveSessionAsync`
  (`server/Application/Services/AgentControlService.cs:209-221`) appends
  `--model <ModelLevelAliases.For{Codex|Grok|Claude}(agent.ModelLevel)>` into `extraArgs` whenever
  `agent.ModelId` is blank and the profile kind is Claude, Grok or Codex. `AgentTaskDispatcher`
  does the same for a task **pinned to a standing agent with a profile**:
  `BuildLaunchSpecAsync` computes `includeModelAlias = string.IsNullOrWhiteSpace(agent.ModelId)`
  (`AgentTaskDispatcher.cs:1946`) and `ComposeDelegateArgs` appends the alias for
  `task.ModelLevel` (`:2000-2010`). Neither reads the profile revision. The pool-delegate path
  (`BuildLaunchSpec`, `:1889`, `includeModelAlias: true`) has no profile at all and is **not** part
  of this bug — it launches Antiphon's own `claude`/`grok`/`codex` registry definitions, which
  accept `--model`.

- **The resolver never treated a null `modelArgumentName` as "no model argument". It treats it as
  `--model`.** `AgentTuiLaunchResolver.ResolveCoreAsync` (`AgentTuiLaunchResolver.cs:365-374`):

  ```csharp
  var modelArgumentName = string.IsNullOrWhiteSpace(revision.ModelArgumentName)
      ? "--model"
      : revision.ModelArgumentName.Trim();
  ```

  So the card's premise — "modelArgumentName = null only stops the resolver path" — is not quite
  what happens. The resolver path is not stopped either: it appends the exact `ModelId` under
  `--model` regardless of the blank field, which is exactly why the workaround (`ModelId =
  maven-grok`, field still blank) produced `--model maven-grok` on the command line. **Nowhere in
  the system does a blank field mean "none".** The UI (`AgentTuiProfileModal.tsx:144`,
  `modelArgumentName.trim() || null`) and the service (`AgentTuiProfileService.cs:2100`,
  `NullIfWhiteSpace`) both store a blank as `null`; the resolver then reads `null` as `--model`.
  An operator who blanks the field to say "none" is silently overruled.

- **The blank field's only honest reading already exists in the catalogue.**
  `AgentTuiRunnerCatalog.Runner` (`AgentTuiRunnerCatalog.cs:79-90`) hard-codes
  `DefaultModelArgumentName = "--model"` for Claude/Codex/OpenCode/Grok, and the Raw kind
  (`:50-58`) declares `null` — meaning *this runner has no model argument*. The importer copies
  that default into the revision (`AgentTuiProfileImporter.cs:239`). The revision field and the
  catalogue field are the same fact; only the resolver's `?? "--model"` makes them disagree.

- **The documented contract is "empty model = no `--model`", and the code contradicts it for the
  three kinds that matter.** `docs/ai-agent-tui-configuration.md` (Concepts table: *"Exact model —
  passed as separate `--model` + value args, **or omitted for runner default**"*; OpenCode section:
  *"Leave model empty to omit `--model`"*), the agent picker
  (`client/src/features/agents/AgentTuiSelection.tsx:47`, `'Use runner default (no exact model)'`;
  `:74`, *"Leave as runner default to omit the model argument"*) and `docs/agent-kinds.md:63`
  (*"No tier and no `ModelId` means no `--model` and the runner's own default"*) all promise
  omission. But there is **always** a tier — `Agent.ModelLevel` is a non-nullable enum defaulting
  to `High` (`server/Domain/Entities/Agent.cs:23`) and every task carries one — so for Claude, Grok
  and Codex "empty" has meant "the tier alias" since `fdfec20`. The promise is true only for
  OpenCode and Raw, which sit outside the `isClaudeCode || isGrok || isCodex` block. The reporter
  did what the UI told them to do and got the opposite.

- **The tier alias bypasses the profile's catalogue; an exact model does not.** An exact `ModelId`
  is checked at agent save (`AgentService.EnsureModelInProfile`, `AgentService.cs:1119-1139`, 409
  `model_not_in_profile`) and again at launch (`AgentTuiLaunchResolver.EnsureModelAllowed`). The
  tier alias is composed before resolution and checked against nothing. The catalogue would not have
  caught this case anyway — `grok-4.6` is a curated Grok model, and the curated fallback admits it
  for every Grok profile — which is why "validate the alias against the profile" is not the fix
  (see Decision 1, rejected alternatives).

- **`WrapperManaged` cannot be the gate, and neither can the executable's name.** Every profile on
  this machine is `WrapperManaged`, including direct `claude.exe`/`grok.exe`: the mode says who holds
  the credentials, not who picks the model (`docs/agent-kinds.md:159`, *"Normal operation is
  wrapper-managed: `claude.exe` is already logged in"*). And the operator guide's other wrapper,
  `ocg.ps1`, **accepts** `--model` (*"the runnable Grok 4.5 selection is the discovered
  `maven/grok-4.5` identifier"*). "Wrapper ⇒ no model" is false inside this repo's own docs.

- **The card-spawn paths already omit the tier — a third behaviour.** `CardService.cs:594` and
  `OrchestratorService.cs:603` resolve with `ExtraArgs: null`, so an agent with a blank `ModelId`
  launched by a card gets **no** `--model` (runner default), while the same agent's Start button
  gets the tier alias. Three launch paths, two policies, none of them the documented one. Named
  here; scoped out below (§6).

- **Guidance is prose.** `AgentTuiProfileRevision.Guidance` is displayed and never read by any
  launch code (no `.Guidance` consumer in `server/Application` outside the DTO mapping). The
  reporter's *"do not pass --model from Antiphon"* was written into the one field that could not
  enforce it, because the field that could was silently defaulting.

- **The live workaround is the designed mechanism, not a hack — but it is the wrong shape for this
  profile.** "Exact `ModelId` wins, tier fills in" (CARD-0140 D4) is the rule; `maven-grok` on the
  profile's models list and on the agent satisfies it. What is wrong is that the operator has to pin
  a model on every agent that uses the profile to stop Antiphon contradicting the program, and a
  task pinned to that agent still needs the same pin. The fact that the wrapper owns the model
  belongs on the **profile**, once.

---

## Verdict up front — the three decisions

**Option 2 from the card, made honest: a blank `modelArgumentName` means "this program owns its
model; no caller may append a model argument", and there is exactly one appender per launch path
to enforce it.** Not option 1 (there is no reliable "wrapper" or "gkp" signal, and mapping a tier
to a profile's "fixed model" is a silent rewrite of the tier, which the guide forbids). Not option 3
(the documented contract is already the right one; the code is what lies, and documenting the
workaround would be documenting a UI that says "omit" and means "inject").

| # | Decision | One line |
|---|---|---|
| D1 | The revision's `ModelArgumentName` is the single authority; `null` = no model argument | Aligns the revision field with the catalogue's existing `DefaultModelArgumentName: null` meaning, with a semantics-preserving data migration so no existing launch changes |
| D2 | One appender per path: the tier alias moves out of `ExtraArgs` into `AgentLaunchOptions.TierModelAlias` | The profile resolver applies D1; `AgentRegistry.Resolve` appends it for the profile-less path; neither `AgentControlService` nor `AgentTaskDispatcher` ever writes `--model` again |
| D3 | An exact `ModelId` on a no-model-argument profile is a 409, never a silent drop and never a rewrite | At agent save and at launch, `model_argument_unsupported` |
| D4 | Observability: the launch says which of the four things it did with the model | `ResolvedAgentTuiLaunch.ModelArgument` provenance, a log line, the `modelArgument` capability, `EffectiveModelId` untouched |
| D5 | The docs and the two UI strings say the real rule | Tier fills in; blank profile field = none; a gkp-style recipe in the operator guide |

---

## 1. Decision 1 — `ModelArgumentName == null` means "no model argument", enforced

### The rule

For a launch through a profile revision:

1. `revision.ModelArgumentName` blank ⇒ **append nothing**, whatever the tier and whatever
   `agent.ModelId` says (D3 makes the `ModelId` case loud before it gets here).
2. Otherwise `agent.ModelId` set ⇒ `[<argName>, <ModelId>]`, catalogue-checked (today's exact arm,
   unchanged).
3. Otherwise a tier alias supplied ⇒ `[<argName>, <alias>]`.
4. Otherwise nothing (the card-spawn shape today).

This is what the catalogue already means by `DefaultModelArgumentName: null` for Raw; the change is
that the revision field stops being reinterpreted on its way to the process.

### The migration — semantics-preserving, stated plainly

Revisions are immutable snapshots, and this plan rewrites one column on some of them. That is
acceptable **only** because the rewrite changes no launch: today every null-`ModelArgumentName`
revision of a non-Raw kind resolves to `--model` at launch (`AgentTuiLaunchResolver.cs:369-371`),
so writing the literal `--model` into those rows records what they have always done. After the
migration, a blank field is a blank the operator chose *after* this card landed, on a revision
created *after* it.

```sql
UPDATE "AgentTuiProfileRevisions" r
SET "ModelArgumentName" = '--model'
FROM "AgentTuiProfiles" p
WHERE p."Id" = r."ProfileId"
  AND r."ModelArgumentName" IS NULL
  AND p."Kind" <> <Raw>;
```

Raw stays null — its catalogue default is null, and the catalogue's `modelArgument` capability for
Raw already reads `Unknown("Raw commands have no runner-owned model contract")`. A Raw profile with
an agent carrying an exact `ModelId` would, after this, hit D3's 409 at its next launch instead of
receiving `--model X` from a resolver default the catalogue never promised. The census on this
machine has zero such agents; the migration's log line names any it finds elsewhere.

On the reporter's machine this means the live workaround **keeps working byte-for-byte on deploy**
(`grok-gkp-project` gains `--model`; `PM-Orchestrator-Grok` keeps `maven-grok`; the command line is
unchanged). To reach the intended state they blank the field once (new revision) and clear the
agent's model — or leave it as it is. Nothing breaks on the day the code lands.

### Rejected alternatives, with the reason each fails

- **Gate on `AuthenticationMode == WrapperManaged` or on the executable containing `gkp`.** Both
  false in this repo's own docs (facts above): `WrapperManaged` is the normal mode for a direct
  `claude.exe`, and `ocg.ps1` is a wrapper that takes `--model`. An executable-name match is a
  per-machine hack that the next wrapper contradicts.
- **Map the tier to the profile's "declared fixed model" (its single model, or
  `IsSuggestedDefault`).** Profiles declare *lists*, not a fixed model, and `IsSuggestedDefault` is
  a picker hint (index 0 of the curated list — `fable` for Claude). A `Low` Claude agent would
  launch on `fable`, and the operator guide's own line — *"Do not rewrite a selected identifier to a
  wrapper default"* — is the same principle CARD-0136 states as "never silently reroute". The tier is
  a selection; substituting it is a rewrite.
- **Validate the tier alias against the profile's catalogue like an exact model.** Sound in
  principle, useless here: `grok-4.6` is curated for every Grok profile, so `EnsureModelAllowed`
  admits it. Dropping the curated fallback for tier aliases only would make a fresh Claude profile
  with an empty models list unable to launch a `Low` agent. The catalogue answers "which models can
  this profile run", not "does this program take a model argument at all" — different question,
  different field.
- **A new boolean column (`OmitModelArgument`) instead of overloading blank.** More explicit on
  paper, but it leaves the text field's null→`--model` default in place and adds a second field
  that must agree with the first. One fact, one field: the catalogue already uses null for it.
- **Document the workaround and change nothing (option 3).** Rejected explicitly, not because it is
  small: the contract in the operator guide, the agent picker and `agent-kinds.md` is *already*
  "empty = omit", and it is the code that fails to honour it for Claude/Grok/Codex. Documenting
  "set `ModelId = maven-grok`" would leave a blank field that reads as "none" and means `--model`,
  a picker that says "omit" and injects, and a failure whose only evidence is a wrapper's exit-1 —
  with no Antiphon-side log naming what was injected or why. The report's real ask is that the
  profile's declaration hold everywhere; that is a code property.

---

## 2. Decision 2 — one appender per launch path

Today three places compose a model argument: `AgentControlService.cs:211-221`,
`AgentTaskDispatcher.ComposeDelegateArgs` `:2000-2010`, and the resolver `:365-374`. CARD-0140 D4
kept the first two in agreement *by hand* ("mirror `AgentControlService` exactly"). This card is
what happens when a rule lives in one of them and not the others. The fix removes the choice:

- **`AgentLaunchOptions` gains `string? TierModelAlias = null`** (`server/Application/Dtos/AgentLaunchSpec.cs:9`).
  Callers that resolve a tier compute the alias exactly as they do now — the explicit
  `isCodex ? ForCodex : isGrok ? ForGrok : ForClaude` branch, kept explicit for the CARD-0084 S3 /
  CARD-0099 S3 reason ("a wrong alias here is a wrong process") — and pass it **instead of**
  putting `--model` in `ExtraArgs`. `AgentControlService` passes it whenever `agent.ModelId` is
  blank (as today); `AgentTaskDispatcher.BuildLaunchSpecAsync` passes it under the same
  `includeModelAlias` predicate. `ExtraArgs` never again contains `--model` from either service.
- **`AgentTuiLaunchResolver.ResolveCoreAsync`** applies the D1 rule after `ExtraArgs`, where the
  exact arm sits today (`:365-374`). The `?? "--model"` default is deleted.
- **`AgentRegistry.Resolve`** (`server/Application/Services/AgentRegistry.cs:112-114`) appends
  `["--model", options.TierModelAlias]` after `ExtraArgs` when the alias is non-null. This is the
  profile-less path — pool delegates (`BuildLaunchSpec`), the legacy fallback
  (`AgentLaunchResolution.ResolveLegacyAsync`) — whose definitions are Antiphon's own and accept
  `--model`. A registry definition has no revision, so there is nothing for D1 to read; the alias is
  always right there.

Position on the command line moves: the alias currently precedes `--append-system-prompt`/`--rules`
and will follow them. All three CLIs take flags in any order; the tests that pin the model
(`AgentControlServiceIntegrationTests.cs:126-127`, `PinnedProfileLaunchSpecTests` T8 `:68-101`,
`GrokDelegateDispatchTests.cs:41-56`, `CodexDelegateDispatchTests`, `NamedCodexAgentLaunchTests`,
`AgentSystemPromptLaunchTests.AssertModel :166-171`) all use `Contains`/`IndexOf`, and
`AgentTuiLaunchResolverTests` uses `TakeLast(2)` for the exact arm, which still holds. The two
argv-integrity suites (`DelegateLaunchArgvIntegrityTests`, `LaunchArgvGuardTests`) build their own
arrays. The implementer confirms none pins a *prefix* order.

The CARD-0140 D4 property — "one `--model`, not two" — stops being a coordination between two
services and becomes structural: only the resolver (profile path) or the registry (no-profile path)
can write the argument.

**Rejected: keep both `ExtraArgs` sites and gate each with a profile peek.** `PeekProfileKindAsync`
already fetches the profile row; widening it to the active revision's `ModelArgumentName` and
adding `if (argName is not null)` at both sites plus fixing the resolver default is a smaller diff.
It is also three copies of the rule, and the third copy is the one that was wrong this time.

---

## 3. Decision 3 — exact model on a no-model-argument profile is a conflict

`agent.ModelId` set + `revision.ModelArgumentName` null is a contradiction the operator wrote, and
the system has two bad ways to resolve it silently: pass it anyway (today, via the resolver's
default — the program may reject it, as gkp does for anything but `maven-grok`), or drop it (the
guide's forbidden "rewrite to a wrapper default", in the other direction). It is refused instead:

- **At agent create/PATCH** — `AgentService.ApplyTuiSelectionAsync` (`AgentService.cs:1023-1078`)
  already loads `ActiveRevision`; beside `EnsureModelInProfile`, a non-null `modelId` on a revision
  with a blank `ModelArgumentName` throws `ConflictException("The selected runner profile passes no
  model argument; clear the exact model or set the profile's model argument name.",
  "model_argument_unsupported")`. Clearing the model succeeds.
- **At launch** — the resolver applies the same check, because the active revision can change after
  the agent was saved (an operator blanks the field on a profile ten agents use). Defence in depth,
  same code, same shape as `model_not_in_profile` being checked in both places today.

Never a Warning-and-continue: a launch that reaches the process with a model the profile says it
cannot pass is this card's exit-1 with a different spelling.

---

## 4. Decision 4 — observability

- **`ResolvedAgentTuiLaunch` gains `LaunchModelArgument ModelArgument`**, an enum
  `{ None, ProfileOwned, Exact, Tier }`: `None` = no alias offered and no `ModelId` (card spawns
  today), `ProfileOwned` = D1 rule 1 suppressed something, `Exact`, `Tier`. `ResolveLegacyAsync`
  reports `Tier`/`None`. **`EffectiveModelId` keeps its current meaning — the exact model or null.**
  The drift badge (`AgentService.MapTuiSelection`, `:996-1020`) compares it with `agent.ModelId`
  ordinally; recording the tier alias there would badge every tier-launched agent "restart
  required" forever. The metrics' `Exact`/`Default` mode label is likewise unchanged.
- **One log line at Information when `ProfileOwned` fires**, from the resolver:
  `"{Subject}: profile '{Profile}' rev {Rev} declares no model argument; tier {Level} ({Alias}) not
  passed"`. Today the only evidence of the injection is the wrapper's own stderr.
- **`AgentTaskDispatcher.ShippedModelDisplay`** (`:2061-2066`) reads the provenance so the
  Dispatched event says `none (profile owns the model)` rather than naming an alias that never
  reached the process — the CARD-0140 W5 "name the model that actually shipped" rule.
- **The `modelArgument` capability** (`AgentTuiProfileService.GetCapabilitiesAsync`, `:220`) is
  overridden to `Unsupported("The active revision declares no model argument.")` when the
  revision's field is blank, so the profile page and the agent picker (D5) have an existing
  vocabulary to read instead of a new flag.

---

## 5. Decision 5 — the docs and the UI say the real rule

- `docs/agent-kinds.md:59-64` — replace the "No tier and no `ModelId`" sentence (there is always a
  tier) with the four-step rule from D1 and name the single-appender property.
- `docs/ai-agent-tui-configuration.md` — Concepts row *Exact model*: "…or omitted, in which case the
  agent's tier picks the model for Claude/Grok/Codex (`agent-kinds.md`), and a profile whose model
  argument is blank passes none at all". Add a **Local llm-key-proxy (gkp) Grok profile** section in
  the same table shape as the OpenCode and Grok ones: Executable `pwsh.exe`, launch args through
  `gkp.ps1`, Auth `WrapperManaged`, **Model arg: blank**, models list `maven-grok` optional, and the
  sentence "`gkp` accepts exactly one model and pins it itself; leave the model argument blank and
  leave every agent's exact model empty — pinning `maven-grok` on the agent also works and is what
  a profile saved before CARD-0182 does". The "Leave model empty to omit `--model`" line under
  OpenCode stays true (OpenCode receives no tier) and gains "(OpenCode; Claude/Grok/Codex agents
  receive their tier's alias instead)".
- `client/src/features/agents/AgentTuiSelection.tsx:47,74` — option label `'Use the agent's tier
  (no exact model)'`; description *"Optional. Leave empty and the agent's tier chooses the model;
  on a profile that passes no model argument, nothing is passed."* When the selected profile's
  capabilities report `modelArgument: Unsupported`, the select is disabled with that reason.
- `client/src/features/settings/AgentTuiProfileModal.tsx:331-335` — description on *Model argument
  name*: *"Blank means the program owns its model: Antiphon never passes one, for the tier or for an
  exact model."* The default stays `--model`.

---

## 6. Out of scope, stated

- **Card-spawn paths passing a tier.** `CardService.cs:594` and `OrchestratorService.cs:603` offer
  no alias, so a blank-`ModelId` agent spawned onto a card runs the runner's default while its Start
  button runs the tier. With D2 the fix is one `TierModelAlias:` argument at each site — but it is
  a behaviour change on a path nobody reported, on every card spawn at once, and it deserves its own
  card with its own census. This plan's `None` provenance makes that card's evidence cheap.
- **Guidance as enforcement.** Free text stays free text.
- **A composite `(TuiProfileId, Kind)` foreign key** (CARD-0138 D4) — still not this card.
- **Herdr / `SessionBackend`** — orthogonal; the argument list is composed before the backend is
  chosen.

---

## 7. Verification / test design

| # | Test | Where | Pins |
|---|---|---|---|
| T1 | Profile with blank `ModelArgumentName`, agent `ModelId` null, `TierModelAlias: "grok-4.6"` ⇒ `Args` contains no `--model` and no `grok-4.6`; `ModelArgument == ProfileOwned`; `EffectiveModelId` null | `tests/Antiphon.Tests/AgentTui/AgentTuiLaunchResolverTests.cs` | D1 rule 1 — the card's shape |
| T2 | Same profile with `--model`, `ModelId` null, alias offered ⇒ exactly one `--model`, value is the alias, `ModelArgument == Tier` | same | D1 rule 3, D2 |
| T3 | The three existing exact-arm tests (`:24`, `:47`, `:71`) stay green unchanged, `ModelArgument == Exact` | same | D1 rule 2 |
| T4 | Blank field + `ModelId` set ⇒ 409 `model_argument_unsupported` from the resolver | same | D3 at launch |
| T5 | `PATCH /api/agents/{id}` with `modelId` on a blank-field profile ⇒ 409; clearing succeeds; `AgentTuiSelection` picker disabled with the capability reason (vitest) | `AgentService` tests + `client/src/features/agents/` | D3 at save, D5 |
| T6 | `AgentControlServiceIntegrationTests` legacy-Claude case (`:126-127`, `haiku`) still passes with the alias arriving via `TierModelAlias`; new case: a Grok agent on a seeded blank-field profile starts with `StartedArgs` containing no `--model` | `tests/Antiphon.Tests/Application/AgentControlServiceIntegrationTests.cs` | D2 through the real launch queue |
| T7 | `PinnedProfileLaunchSpecTests` T8 unchanged; new T8b: pinned standing agent on a blank-field profile ⇒ zero `--model`, Dispatched event detail says `none (profile owns the model)` | `tests/Antiphon.Tests/Application/PinnedProfileLaunchSpecTests.cs` | D2 dispatcher arm, D4 |
| T8 | Pool Grok delegate (`GrokDelegateDispatchTests.cs:41`) and Codex/Claude pool delegates keep `--model <alias>` via `AgentRegistry.Resolve`; `--model` count is exactly one | existing suites | D2 registry arm — no regression on the path that is not this bug |
| T9 | Migration: a Grok revision seeded with null gains `--model`; a Raw revision stays null | `tests/Antiphon.Tests/AgentTui/AgentTuiPersistenceTests.cs` or a migration test beside it | D1 migration |
| T10 | Capability snapshot reports `modelArgument: Unsupported` for a blank-field revision and `Supported` otherwise | `AgentTuiProfileServiceTests` | D4 |
| T11 | `grep -rn '"--model"' server/Application/Services/AgentControlService.cs server/Application/Services/AgentTaskDispatcher.cs` returns nothing — an assertion in a test or a CI grep, the implementer's choice | — | D2's structural claim |

Run:

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0182/ --treenode-filter "/*/Antiphon.Tests.AgentTui/*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0182/ --treenode-filter "/*/Antiphon.Tests.Application/*/*"
pwsh -File scripts/test-client.ps1
Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-card0182 | Remove-Item -Recurse -Force
```

Forward slash on `OutputPath`, always. `Antiphon.Tests` is ~12 minutes whole; the two namespace
filters fit one foreground window each.

**Live verification, on the reporter's machine (the only place the wrapper exists):** re-save
`grok-gkp-project` with a blank model argument, clear `PM-Orchestrator-Grok`'s exact model, Start
it; the session buffer must show gkp's own `maven-grok` banner and the server log must carry the
D4 line naming the suppressed tier. Then PATCH the agent to `modelId: maven-grok` and confirm the
409 body names `model_argument_unsupported`.

---

## 8. Build order

Four slices, each green and committed on its own, in this order because each is what the next one
is tested through:

1. **S1 — the field's meaning.** EF migration (D1) + `AgentTuiLaunchResolver` D1 rule with a
   `TierModelAlias` option that nobody passes yet + D3 in resolver and `AgentService` + T1–T4, T9.
   After S1 the resolver honours a blank field for exact models; the two services still inject.
2. **S2 — one appender.** `AgentLaunchOptions.TierModelAlias`; `AgentRegistry.Resolve` appends it;
   `AgentControlService` and `AgentTaskDispatcher.BuildLaunchSpecAsync` pass it and drop their
   `--model` arms; `BuildLaunchSpec` (pool) passes it too and drops `includeModelAlias`. T6–T8,
   T11. This is the slice that fixes the card.
3. **S3 — say what happened.** `ModelArgument` provenance, the log line, `ShippedModelDisplay`,
   the capability override. T7's event assertion, T10.
4. **S4 — docs and UI.** D5. T5's vitest half.

Estimated at the verification floor plus authoring: S1 and S2 are the substance (~half a day
together); S3 and S4 are an hour each.
