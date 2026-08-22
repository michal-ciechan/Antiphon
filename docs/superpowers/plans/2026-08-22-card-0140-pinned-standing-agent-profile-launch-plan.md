# CARD-0140 — a fresh spawn onto a pinned standing agent launches from the appsettings registry, ignoring its own TUI profile: plan

**Date:** 2026-08-22 · **Card:** CARD-0140 (`4fd99805-d417-4f41-be33-a974a44636c0`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** master `d323857` — every line/behaviour claim below was read out of the code on
that commit, and both reproductions were run against the live dev stack (server `:17202`,
`antiphon-postgres` `:17280`) on 2026-08-22 and cleaned up afterwards.

**Sibling:** CARD-0138 (`docs/superpowers/plans/2026-08-22-card-0138-agent-kind-profile-sync-plan.md`),
whose §R2 is this card's evidence trail and whose W1–W4 are **already shipped** (`e5197fe`,
`0ef319f`, `25213b4`). This plan is re-derived against the shipped code, not the card's snapshot:
the `Agent.Kind` restamp guard (W4) is live at `AgentTaskDispatcher.cs:1654`, and the
profile→kind sync (W1) is live and observably working — the throwaway agent created for the
reproduction below was born with `Kind = Codex` from its profile, with no `Kind` on the wire.

---

## Verdict up front

The card is right, it still reproduces on `d323857`, and it is **half** the defect. Pinning a task
to a Codex-profile standing agent is broken in *both* of that agent's states, by two different
mechanisms that share one root cause — **nothing reconciles the pinned agent's kind with the task's
`AgentKind`, which defaults to `ClaudeCode`**:

| Pinned agent state | What happens today | Loud? |
|---|---|---|
| **stopped** (non-AlwaysOn) | `PlaceOnStandingAgentAsync:1940` → `SpawnFresh` → `BuildLaunchSpec:1600` → `_agentRegistry.Resolve(DefinitionNameForKind(ClaudeCode))`. **`claude.exe` launches in the Codex agent's directory, under its name, with Claude's flags.** | **silent** |
| **running** | `PlaceOnStandingAgentAsync:1951-1957` sees `sessionKind = Codex ≠ claimed.AgentKind = ClaudeCode` and throws. The task is **Failed** and the delegation never happens. | loud, but wrong advice |

So a Codex (or Grok) standing agent cannot be delegated to at all: it either fails, or it silently
runs the wrong program. The card describes the silent arm. The loud arm was found while reproducing
it and is included here because **the fix for one is the fix for both**, and a plan that closed only
the silent arm would leave "delegate to my Codex agent" still failing.

---

## The reproduction, run for real

Both probes were driven through the **live** server and dispatcher. Task rows were inserted directly
into `AgentTasks` as `Queued` (`Delegation:AllowedRoots` is empty in `server/appsettings.json`, so a
token-less `POST /api/agent-tasks` cannot name a working directory, and this session's own delegation
token is a *worker* token that correctly refuses to delegate). Creation-time validation is upstream
of everything this card is about, so bypassing it changes nothing about what is being measured. All
rows created for these probes were deleted afterwards; the operator's real `Codex` agent
(`06a847ea`) was verified untouched and still `Running` on its own session `f04cd114`.

### Probe 1 — the silent arm (the card's defect)

Setup, all through the real API:

```
POST /api/agents  { name: "CARD-0140 Repro Codex", workingDirectory: "C:\src\Antiphon",
                    tuiProfileId: cec57c0b (the `codex` profile), modelId: "gpt-5.6-terra" }
→ Agents row 2b89fd5a: Kind = 2 (Codex)   ← CARD-0138 W1 working
                       TuiProfileId = cec57c0b, IsPoolDelegate = f, AlwaysOn = f,
                       Status = Idle, PersistentSessionId = NULL   ← stopped, unsupervised
```

Then one `Queued` task pinned to it with `AgentKind = 1 (ClaudeCode)` — exactly what a caller who
omits `agentKind` gets (`AgentTaskDtos.cs:23`, `AgentTaskService.ResolveAgentKind:823`).

**The session row the dispatcher wrote:**

| Id | DefinitionName | AgentKind | Cwd | TuiProfileRevisionId | EffectiveModelId |
|---|---|---|---|---|---|
| `ac931d5d` | **`claude`** | **1 (ClaudeCode)** | `C:\src\Antiphon` | **NULL** | **NULL** |

**The process that actually started** (`Win32_Process`, pid 46060, created 23:07:43):

```
c:\users\lndco\.local\bin\claude.exe --dangerously-skip-permissions
  --name "CARD-0140 Repro Codex"
  --model fable
  --append-system-prompt "[bundle:delegate-basics vfb3d080a] ..."
  --session-id ac931d5d-e345-4ec0-ad45-8f38c0c9257c
```

**What the same agent's own profile produces** — the operator's live `Codex` agent through the
normal interactive Start (`AgentControlService`), pid 35036, session `f04cd114`
(`DefinitionName = codex`, `TuiProfileRevisionId = afb938fc`, `EffectiveModelId = gpt-5.6-terra`):

```
…\@openai\codex-win32-x64\vendor\x86_64-pc-windows-msvc\bin\codex.exe
  --no-alt-screen --dangerously-bypass-approvals-and-sandbox
  -c model_reasoning_effort=high
  -c "developer_instructions=[bundle:style-caveman v5f2e481f]…"
```

Five things are dropped, and **nothing anywhere reports any of them**:

1. **the executable** — `claude.exe`, not `codex.exe`;
2. **the profile revision's own arguments** — `--no-alt-screen`,
   `--dangerously-bypass-approvals-and-sandbox` never appear;
3. **the managed environment** — the revision's non-secret env and any `ManagedEnvironment` secrets
   (`AgentTuiLaunchResolver.ResolveCoreAsync:264-297`) are never unprotected or merged;
4. **the model catalogue** — the agent's `ModelId = gpt-5.6-terra` (catalogue-validated by
   `EnsureModelAllowed:324`) is replaced by `--model fable`, the *Claude* Frontier alias
   (`ModelLevelAliases.ForClaude:23`), which is not a legal model for the program the operator
   attached;
5. **the provenance columns** — `TuiProfileRevisionId` / `EffectiveModelId` stay NULL, so nothing
   downstream can even tell which revision this delegate ran under.

And the launch **succeeded**. There is no error, no incident, no failed task: a healthy-looking
Claude session, running under a Codex agent's name, in its directory, with
`Agent.PersistentSessionId` repointed at it (`AgentTaskDispatcher.cs:1331`).

### Probe 2 — the loud arm (found while reproducing probe 1)

Same shape, pinned instead to the operator's **running** `Codex` agent (`06a847ea`, live Codex
session `f04cd114`). Nothing launched; the task went straight to `Failed`:

```
Task 0140beef runs on ClaudeCode, but it is pinned to agent 'Codex' whose live session
f04cd114-… is Codex. Pin it to a ClaudeCode agent, or create the task without a kind so it
runs on a fresh delegate.
```

The refusal itself is correct — `PlaceOnStandingAgentAsync:1957` will not type a brief into the
wrong TUI. Its **advice is not**: the task *was* created without a kind, and that is precisely how it
came to be `ClaudeCode`. There is no way for an operator to phrase "run this on my Codex agent"
that this path accepts.

---

## What the code does today, line by line

```
AgentTaskDispatcher.DispatchOneAsync
 :1241  TryReuseWarmAgentAsync
 :1794    → not a pool delegate → PlaceOnStandingAgentAsync
 :1940        no live session and !AlwaysOn → ReuseOutcome.SpawnFresh
 :1265  var definitionName = _agentRegistry.DefinitionNameForKind(claimed.AgentKind)   ← before any side effect
 :1292  var agent = await ResolveAgentAsync(...)          ← returns the PINNED standing agent
 :1654     if (existing.IsPoolDelegate) existing.Kind = task.AgentKind   ← CARD-0138 W4, live
 :1301  new AgentSession { DefinitionName = definitionName, AgentKind = claimed.AgentKind, ... }
 :1354  var spec = BuildLaunchSpec(claimed, agent, session, attachedBundleKeys)
 :1531     var kind = session.AgentKind                   ← every arg branch keys on this
 :1600     return _agentRegistry.Resolve(DefinitionNameForKind(kind), options)   ← appsettings, never the profile
 :1359  _apiKeyEnvResolver.ResolveSpecAsync(spec, claimed.ProjectId ?? agent.BoardId, ...)
```

The agent's `TuiProfileId` is **never read** anywhere on this path. `git grep TuiProfileId
server/Application/Services/AgentTaskDispatcher.cs` returns nothing.

The correct pattern already exists, one file over, and the card is right that it should be reused
rather than reinvented:

```
AgentControlService.StartInteractiveSessionAsync:133
 :161   var profileKind = await PeekProfileKindAsync(agent, ct)   ← the agent's own profile Kind,
 :377      agent.TuiProfileId → that profile's Kind                  then the installation default,
           → else the default profile's Kind                          then the legacy registry
           → else the legacy registry definition's Kind
 :162-170 isClaudeCode / isGrok / isCodex derived from THAT, not from any row's Kind column
  :184-197 --model only when agent.ModelId is blank (else the resolver supplies the exact model)
 :257   AgentLaunchResolution.ResolveForAgentAsync(agent, _agentRegistry, _launchResolver, options, ct, _apiKeyEnvResolver)
 :328   session.AgentKind = spec.Kind, TuiProfileRevisionId = resolved.ProfileRevisionId,
        EffectiveModelId = resolved.EffectiveModelId
```

`AgentLaunchResolution.ResolveForAgentAsync` (`AgentTuiLaunchResolver.cs:29`) is the shared funnel:
it prefers the managed profile and falls back to the legacy registry when no resolver is registered
or the agent has no profile and none is default. Five call sites use it today
(`AgentControlService:257`, `CardService:598` and `:620`, `OrchestratorService:607` and `:616`).
The dispatcher is the sixth that should and does not.

---

## Design decisions

### D1 — the kind is settled at CREATION, by extending a rule that already exists

`AgentTaskService.CreateAsync` **already** does exactly the right thing for a follow-up
(`AgentTaskService.cs:126-144`):

```csharp
// The agent is already running, as whatever program it was launched as. A follow-up
// keeps that context, so the kind is not a choice any more: unset inherits the prior
// task's, and an explicit mismatch is refused rather than silently reinterpreted.
if (request.AgentKind is { } wantedKind && wantedKind != prior.AgentKind) throw new ConflictException(…);
request = request with { …, AgentKind = prior.AgentKind, AgentId = followAgent.Id };
```

A **bare `AgentId` pin** gets none of it: `request.AgentId` is stored verbatim at `:241` and
`ResolveAgentKind` at `:202` never learns an agent was named. That asymmetry is the root cause of
both arms in §Verdict.

So: **extend the follow-up rule to a bare pin on a STANDING agent.** Unset `AgentKind` inherits the
pinned agent's `Agent.Kind`; an explicit mismatch is refused with the same shape of message; an
`Orchestrator` pinned to a non-Claude agent is refused rather than silently clamped (`:854-860`
today clamps a *policy-derived* kind back to `ClaudeCode`, which would quietly undo the inheritance
and put us back where we started).

`Agent.Kind` is the right field to read, and only became so this session: CARD-0138's D1 established
"if `TuiProfileId` is not null, `Kind` equals that profile's `Kind`", W1–W3 hold it at every write
path, and S3's migration corrected the two historical rows. Reading `Agent.Kind` therefore reads the
profile, without a join, and stays correct for a profile-less agent where the column is its own
truth.

**Pool delegates are carved out.** `FollowUpOnTask` already covers the legitimate "same delegate
again" case; a bare pin to a pool row is that expressed the long way, and `TryReuseWarmAgentAsync:1804`
plus `ResolveAgentAsync:1654` already own the kind-mismatch relaunch as the pool's contract.
Inheriting a pool row's `Kind` at creation would move that decision without moving its two readers,
and the warm pool is where CARD-0138's D3 says a wrong `Kind` is an outage rather than a lie. The
carve-out costs nothing and is separately testable (T3).

Why creation and not dispatch: the board and `AgentTaskSummaryDto` then show the true kind; probe
2's refusal stops firing on the ordinary case; and the dispatcher keeps `task.AgentKind` as the one
answer to "what program is this", which is CARD-0084 S3's whole design.

### D2 — the launch spec for a pinned standing agent comes from its own profile

The profile path is taken **iff all four** hold:

1. `task.AgentId` names an agent row that still exists (retired-between-create-and-dispatch keeps
   today's fallback, matching `TryReuseWarmAgentAsync:1789`);
2. `!agent.IsPoolDelegate`;
3. `agent.TuiProfileId is not null`;
4. the dispatcher was constructed with an `AgentTuiLaunchResolver`.

Otherwise: today's `_agentRegistry.Resolve(DefinitionNameForKind(kind), options)`, unchanged.

**(3) is load-bearing and is the non-obvious one.** `AgentLaunchResolution.ResolveForAgentAsync` on a
null `TuiProfileId` loads the **installation-default** profile (`LoadProfileAsync`, `IsDefault`),
which here is `claude`. For a profile-less Grok agent that is strictly worse than
`DefinitionNameForKind(Grok)` — it would launch Claude for a Grok task, re-creating this card's
defect by the opposite route. Absence of a profile is not evidence of the default profile.

**(2) is the CARD-0138 §R1 hazard.** Eleven historical `task-*` pool rows carry a backfilled `claude`
profile that nothing in the dispatcher put there. W2 stopped the importer creating more, but the
existing rows would route through the profile and hand a Grok task a Claude launch. A pool
delegate's `Kind` column is the evidence; the registry keeps serving it.

**(4)** keeps every harness that predates this card on the registry path, so no existing test moves.

### D3 — the session row is where the lie enters, so a pre-flight decides it

`:1265` resolves the definition name *before* the worktree is cut and before the transaction
commits, deliberately, so a kind this installation cannot launch fails with nothing to clean up.
That property must survive. It becomes:

```csharp
private async Task<DelegateProgram> ResolveDelegateProgramAsync(AgentTask task, CancellationToken ct);
internal readonly record struct DelegateProgram(AgentKind Kind, string DefinitionName, Guid? ProfileId);
```

For a pinned standing agent with a profile it is one projection over `AgentTuiProfiles`
(`Kind`, `IsEnabled`, `ActiveRevisionId`, `SourceDefinitionName`, `DisplayName`) that throws the same
kind of loud, actionable message when the profile is disabled or has no active revision — the two
`ConflictException`s `ResolveCoreAsync:246-255` would otherwise raise **after** the commit, leaving a
`Starting` session row with no process behind it (the CARD-0056 leak shape). Otherwise it returns
`(task.AgentKind, DefinitionNameForKind(task.AgentKind), null)`, byte-identical to today.

The session row (`:1301`) then takes `AgentKind = program.Kind` and
`DefinitionName = program.DefinitionName`, and **`BuildLaunchSpec` needs no change to its branching
at all**: `var kind = session.AgentKind` (`:1531`) already drives `--name`, the `--model` family,
Codex's `-c` overrides and the bundle channel. That single assignment being the one answer is
CARD-0084 S3's design, and this plan pays into it rather than around it.

With D1 shipped, `program.Kind` and `task.AgentKind` agree in every legal case. The pre-flight
asserts it and throws when they do not — which can only mean a row queued before D1 shipped, or an
operator who repointed the agent's profile between create and dispatch. A loud failure naming both
kinds is the right answer there; silently preferring either one would re-open exactly the question
CARD-0084 §4 settled ("an explicit kind is never substituted").

### D4 — the model argument: the agent's exact model wins, the tier fills in

`ResolveCoreAsync:321-330` appends `--model <agent.ModelId>` after `options.ExtraArgs`, having
validated it against the profile's catalogue. `BuildLaunchSpec:1546-1554` already appends its own
`--model <tier alias>` into those `ExtraArgs`. Routed naively, a pinned Codex agent would receive
**two** `--model` flags, the first of them `fable`.

Resolution: **mirror `AgentControlService:184-197` exactly.** When the pinned agent has a non-blank
`ModelId`, `BuildLaunchSpec` omits its tier alias and the resolver supplies the model; when it does
not, the tier alias for the *resolved* kind is added as today. A pinned dispatch and an operator's
own Start of the same agent then put the same model on the command line, which is the property that
makes this reuse rather than a second policy.

Consequence, stated out loud because CARD-0099 S3 is the cautionary tale: for a pinned standing agent
with an exact `ModelId`, `task.ModelLevel` no longer selects the model. That must not be invisible.
The Dispatched event's detail (`:1341-1342`) and the dispatch log line (`:1396-1398`) both print
`ModelLevelAliases.For(claimed.AgentKind, claimed.ModelLevel)` today; they print **the model that
actually went on the command line**, and say which of the two decided it.

### D5 — API-key resolution runs once, under the dispatcher's scope rule

`ResolveCoreAsync:355-372` resolves `{{key:NAME}}` placeholders itself, scoped
`options.ApiKeyProjectId ?? ResolveProjectIdAsync(agent.BoardId)`. The dispatcher then resolves again
at `:1359-1368` under a *different* rule: `claimed.ProjectId ?? agent.BoardId` — the task's recorded
scope first (CARD-0115 S2).

So the profile path passes
`ApiKeyProjectId: claimed.ProjectId ?? await _apiKeyEnvResolver.ResolveProjectIdAsync(agent.BoardId, ct)`
into `AgentLaunchOptions` and **skips** `:1359-1368`. The registry path keeps it untouched. The
resolver's own subject string (`agent 'X'`) is accepted in place of the dispatcher's
(`task ab12cd34 on agent 'X'`) — it still names the agent, and duplicating the wording would mean
duplicating the resolution.

### D6 — `BuildLaunchSpec` keeps its signature; an async sibling takes the profile path

Four existing suites call `dispatcher.BuildLaunchSpec(task, agent, session)` synchronously
(`CodexDelegateDispatchTests:361`, `GrokDelegateDispatchTests`, `DelegateBundleLaunchTests`,
`DelegateLaunchArgvIntegrityTests`). Making it async churns all four for no gain. Instead:

- `ComposeDelegateArgs(task, agent, session, attachedBundleKeys, includeModelAlias)` — the existing
  body from `:1534` to `:1598`, extracted;
- `BuildLaunchSpec(...)` — unchanged signature, registry path, so those suites keep asserting the
  unpinned pool-delegate contract as-is;
- `BuildLaunchSpecAsync(task, agent, session, program, attachedBundleKeys, ct)` — the single call
  site at `:1354`; takes the profile path when `program.ProfileId` is set, otherwise delegates to
  `BuildLaunchSpec`.

`AgentTuiLaunchResolver? launchResolver = null` joins the constructor's existing optional-dependency
convention (`ptyProfile`, `replies`, `checkQueue`, `runnerClient`, `bindRefusalRecovery`, `runtime`,
`apiKeyEnvResolver`, `scopeFactory`). It is `AddScoped` in `Program.cs:405`, as is the dispatcher
(`:254`), so production wiring is a one-line change.

### D7 — the session's provenance columns get filled

`AgentSession.TuiProfileRevisionId` and `EffectiveModelId` are stamped by both interactive paths
(`AgentControlService:307-308`, `:337-338`) and by nothing in the dispatcher — probe 1's row has both
NULL. The profile path stamps them from `ResolvedAgentTuiLaunch` next to the launch enqueue at
`:1369` (the session entity is still tracked after the commit; two property sets and a
`SaveChangesAsync`). Without this, the drift badge and CARD-0136's usage reader cannot tell which
revision a delegate ran under, and a profile edit mid-flight is undetectable.

---

## Where the fix goes

| # | Site | Change |
|---|---|---|
| W1 | `AgentTaskService.CreateAsync` (`server/Application/Services/AgentTaskService.cs`, after the `FollowUpOnTask` block at `:145`, before `ResolveAgentKind` at `:202`) | A bare `request.AgentId` naming a **non-pool** agent: unset `AgentKind` inherits `agent.Kind`; an explicit mismatch throws `ConflictException`; an `Orchestrator` pinned to a non-`ClaudeCode` agent throws `ValidationException`. Pool rows untouched. |
| W2 | `AgentTaskDispatcher` new `ResolveDelegateProgramAsync` + `DelegateProgram` record; replaces `:1265` | Pinned standing agent with a profile → the profile's kind + `SourceDefinitionName ?? DisplayName` + its id, validating `IsEnabled` and `ActiveRevisionId` **pre-commit**. Everything else → `(task.AgentKind, DefinitionNameForKind(task.AgentKind), null)`. Throws when `program.Kind != task.AgentKind`. |
| W3 | `AgentTaskDispatcher:1301` (session row) | `AgentKind = program.Kind`, `DefinitionName = program.DefinitionName`. |
| W4 | `AgentTaskDispatcher:1522-1607` | Split per D6; the profile arm calls `AgentLaunchResolution.ResolveForAgentAsync` with `ApiKeyProjectId` set (D5) and the model alias suppressed when `agent.ModelId` is set (D4). |
| W5 | `AgentTaskDispatcher:1341` + `:1396` + `:1369` | The Dispatched event and log line name the model that actually shipped (D4); the session's `TuiProfileRevisionId`/`EffectiveModelId` are stamped (D7). |
| W6 | Comments at `:1265`, `:1600-1603`, and `PlaceOnStandingAgentAsync`'s `<summary>` | All three currently describe a world where the registry is the only answer. `:1600-1603`'s "By KIND, not the default definition" stays true for the pool but must say the pinned-standing case goes elsewhere, or the next reader re-derives this card. |

---

## Test coverage

Ten, and none of them is decorative — the reproduction shows every one of these is a way to launch
the wrong program and look healthy.

| # | Test | Where | Pins |
|---|---|---|---|
| T1 | A task pinned to a stopped standing Codex agent, created with **no** `agentKind`, stores `AgentKind = Codex` | `tests/Antiphon.Tests/Application/` (new `PinnedAgentKindTests.cs`) | W1, the inheritance |
| T2 | The same pin with an explicit `agentKind: ClaudeCode` is refused and the message names the agent and its kind; an explicit kind that **agrees** is accepted unchanged | same | W1, the refusal — and that it is not over-broad |
| T3 | A bare pin to a **pool delegate** is untouched: kind still comes from the role policy, and `TryReuseWarmAgentAsync`'s relaunch-on-mismatch still fires | same | D1's carve-out — the edit that could touch the warm pool |
| T4 | An `Orchestrator` pinned to a Codex agent is refused, not clamped to `ClaudeCode` | same | W1 vs `ResolveAgentKind:854-860` |
| T5 | **The reproduction, as a regression pin**: a task pinned to a stopped Codex-profile standing agent writes a session with `AgentKind = Codex` and `DefinitionName = "codex"` — not `claude` | `AgentTaskStandingAgentDispatchTests.cs`, beside `a_standing_agent_that_nothing_supervises_still_gets_a_fresh_session` (`:189`) | W2, W3 |
| T6 | A disabled profile, and a profile with no active revision, fail the task **before** a worktree is cut and **before** any session row exists (assert the row counts) | same | W2's pre-commit placement — the CARD-0056 leak shape |
| T7 | The launch spec: `spec.Exe` is the revision's executable, `spec.Args` begins with the revision's own arguments, carries `-c developer_instructions=` and **not** `--append-system-prompt`, and carries no `--name` | new `PinnedProfileLaunchSpecTests.cs` (harness modelled on `AgentTuiLaunchResolverTests.BuildProvider` + `TestDbFixture.CreateIsolatedSchemaAsync`) | W4 — the five dropped things |
| T8 | The model rule: with `agent.ModelId` set, exactly **one** `--model` and it is the agent's; with `ModelId` null, the tier alias **for the profile's kind** (`gpt-5.6-terra`, not `opus`); and the Dispatched event names what shipped | same | D4 — including the CARD-0099 "silently inert tier" trap |
| T9 | The three carve-outs, one assertion each: an unpinned pool delegate's spec is byte-identical to today's; a pinned **pool** delegate carrying a backfilled `claude` profile still resolves through the registry by its own `Kind`; a pinned standing agent with `TuiProfileId == null` still resolves through the registry | same | D2 (2), (3) — the CARD-0138 §R1 hazard |
| T10 | End-to-end: the argv the adapter factory actually receives for a pinned, stopped Codex agent is **codex's**, and the session carries `TuiProfileRevisionId` + `EffectiveModelId` | new, modelled on `NamedCodexAgentLaunchTests` (which already drives `AgentControlService → AgentSessionLaunchQueue → AgentSessionService` and asserts `StartedArgs`) | W4, W5, D7 — the whole chain, at the level probe 1 measured |

Run:

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0140/ --treenode-filter "/*/Antiphon.Tests.Application/*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0140/ --treenode-filter "/*/Antiphon.Tests.AgentTui/*/*"
Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-card0140 | Remove-Item -Recurse -Force
```

Forward slash on `OutputPath`, always — a trailing backslash creates a directory whose name ends in a
space and breaks the whole repo's build with an MSB3552 that names projects you never touched.

---

## Slices

Four, each independently buildable, testable and revertible.

### S1 — the pinned kind, at creation (W1)

`AgentTaskService.CreateAsync` only. Tests T1–T4.

Landable and useful **on its own**: after it, a task pinned to a Codex agent carries
`AgentKind = Codex`, so `PlaceOnStandingAgentAsync:1951-1957` stops refusing the running case (probe 2
fixed outright), and the stopped case resolves `DefinitionNameForKind(Codex)` — the `codex`
appsettings definition, so `codex.exe` launches instead of `claude.exe`. That is most of the
user-visible harm gone in one small, self-contained edit. It is **not** the whole card: the profile's
revision arguments, managed environment, model catalogue and provenance are still dropped, and on an
installation with no `codex` definition configured the dispatch now fails loudly (correctly) where it
used to launch the wrong program silently.

### S2 — the pre-flight and the session row (W2, W3, W6)

`ResolveDelegateProgramAsync` + `DelegateProgram`, the `:1301` session row, and the comment
corrections. Tests T5, T6. Behaviour change is confined to the pinned-standing-with-profile case;
every other dispatch resolves the same definition name it does today.

### S3 — the profile launch path (W4, W5)

`ComposeDelegateArgs` / `BuildLaunchSpecAsync`, the optional `AgentTuiLaunchResolver` dependency and
its `Program.cs` wiring, the model rule, single API-key resolution, and the provenance stamps.
Tests T7, T8, T9. **T9 must land in the same commit as W4** — it is the assertion standing between
this change and a Grok pool delegate launching as Claude.

### S4 — the end-to-end pin (T10)

No production code. Ships last, once every layer beneath it is asserted, and is the test that would
have caught probe 1 at the level probe 1 was measured.

---

## Risks, and what is deliberately not done

- **The one dangerous edit is D2's `IsPoolDelegate` carve-out.** Getting it backwards routes the
  eleven historical pool rows that carry a backfilled `claude` profile through the profile path, and
  a Grok task then gets a Claude process — a successful-looking dispatch whose report never comes.
  This is the same hazard CARD-0138's W2/T5 exist for, reached by a different door. T9 exists solely
  for it.
- **Rows queued across the S1/S2 deploy fail loudly rather than launching quietly.** A task created
  before S1 with `AgentKind = ClaudeCode` and pinned to a Codex agent hits D3's disagreement throw
  instead of silently launching `claude`. That is the correct trade — the alternative is choosing a
  winner at dispatch time, which CARD-0084 §4 forbids — but the failure message must say *recreate
  the task*, not repeat probe 2's misleading "create the task without a kind".
- **A pinned dispatch onto a stopped standing agent still starts a FRESH conversation.**
  `FindResumableSessionAsync` (`AgentControlService:409`) is not on the dispatcher's path, so the
  agent's previous conversation is not resumed and `Agent.PersistentSessionId` is repointed at the
  new session (`:1331`). Unchanged by this card; named because probe 1 made it visible and an
  operator pinning work to "my Codex agent" may reasonably expect otherwise. Worth its own card.
- **Not done: unifying the two launch composers.** After this card, `AgentControlService:161-257`
  and `AgentTaskDispatcher:1522-1607` make the same three decisions (model, standing-instruction
  channel, name flag) from the same profile, in two places. CARD-0099 S3 already observed that "both
  had to learn Codex at once"; the third such change should fold them into one composer rather than
  making the same edit twice again. Recorded, not scheduled.
- **Not done: anything about `Agent.Kind` itself.** CARD-0138 shipped the invariant and its backfill
  this session; this card consumes it and adds no new writer.
- **Not done: the composite `(TuiProfileId, Kind)` foreign key** (CARD-0138 D4) — still the right
  hardening slice once the application invariant has held for a while, still not this card.
