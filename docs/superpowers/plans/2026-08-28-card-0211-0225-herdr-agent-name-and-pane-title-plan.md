# CARD-0211 + CARD-0225 — Herdr naming: the pane title is the agent's name, the herdr agent is renamed to `Agent.Slug`

**Date:** 2026-08-28
**Status:** planned (design only — nothing here is implemented)
**Cards:** CARD-0211 (`f2c6187f-434d-4f9b-83e0-2e6c0b82302a`, "Herdr launch should apply
`Agent.Slug` as the herdr agent name") and CARD-0225
(`31f3a2fd-2a39-4be3-a822-b48d02c51bf8`, "Herdr tab/pane title is TUI profile DefinitionName, not
the agent name"), board Antiphon. Both are GitHub-tracker-sourced (#15 / the display half of it)
and each already carries a measured live repro; **this plan re-verifies the code path they cite
against `master` @ `d700bbb` and does not re-derive the repros.**
**Scope:** one buildable slice covering both cards. They are two *concerns* — a display title and
a herdr-addressable identity, which may legitimately differ — but they land in the same three
methods (`AgentSessionService.BuildRuntimeLaunchSpecAsync` → `HerdrLaunchContextResolver` →
`HerdrPaneChild.LaunchAsync`), so two parallel builds would collide on the same functions.
**Evidence base:** the two cards; `docs/herdr-sessions.md` §3–§4; herdr 0.8.2 probed **live,
read-only** on 2026-08-28 (`herdr api schema --json` → protocol 20, `herdr --skill`, `herdr agent
list`); the CARD-0187 probe corpus `.antiphon/herdr-probes-card0187/` (K5, `get-codex-*.json`,
`get-grok.json`); the bundled schema `.antiphon/herdr-schema-card0160.json`.
**Builds on:** CARD-0160 (the lane, sidecar-only ids), CARD-0187 (typed launch script, passive
detection, **never `agent.start`**), CARD-0186 (always-on relaunch into a new pane — the path
that makes both bugs recur on every crash).
**Model followed:** `docs/superpowers/plans/2026-08-28-card-0217-sub-second-pages-plan.md`.

## Verdict, in one screen

| Finding (verified 2026-08-28) | Consequence for the design |
|---|---|
| **The pane title is the TUI profile id because the server prefers `session.DefinitionName` over `agent.Name`** — `AgentSessionService.cs:1084-1086` — and the runner forwards that one string to `tab.create` label, `pane.rename` and `pane.report_metadata` title (`HerdrPaneChild.cs:99-104, 430-431`). `DefinitionName` is the profile's `DisplayName` snapshotted at launch (`AgentControlService.cs:224, 255`), shared by every agent on that profile. | §2.1: invert the precedence — `agent.Name` → `agent.Slug` → `DefinitionName` → `"agent"` — as one pure static the server calls and a unit test pins. No runner change needed for the title. |
| **The herdr agent is never named.** A CARD-0187 launch is passive detection, and a passively detected agent has **no `name` at all** (K5; `get-codex-10s.json` carries no `name` key, versus `probe-grok` in `get-grok.json` which was `agent.start`ed). `SanitizeAgentName` (`HerdrPaneChild.cs:453`) is defined and unused; `HerdrClient` wraps neither `agent.rename` nor `agent.list`. | §2.2: a new optional `HerdrLaunchOptions.AgentSlug`, two client wrappers, and a `TryApplyAgentNameAsync` step after detection. "Keep herdr's auto name" in the card means **leave it unnamed**. |
| **herdr enforces the rules we must respect** (`herdr --skill`, 0.8.2): names match `[a-z][a-z0-9_-]{0,31}`, **must be unique among live agents**, "follow the current pane occupant and are cleared when that agent exits", and agent commands accept **a unique live name or the pane id**. `agent.rename` is `{target: string, name: string \| null}` in protocol 20. | §2.2: target the rename by **pane id, never by name**; pre-check `agent.list` so a held name is never stolen; on collision suffix `-2`, `-3` … within the 32-char cap (D2); nothing to clear on exit. |
| **Antiphon's `Agent.Slug` is already unique, lowercase, `-`-joined, ≤ 120 chars** (`AgentService.cs:1259-1290`), so sanitising is a length cut plus the leading-letter rule; `pm-orchestrator-grok` passes untouched. | `SanitizeAgentName` is sufficient as written; only the suffix budget is new. |
| **Wire compatibility is free**: `HerdrLaunchOptions` is a positional record with trailing optionals; both ends deserialise with `JsonSerializerDefaults.Web` (unknown members ignored). | Append `AgentSlug` **after** `AgentKind`; null on the wire means "do not rename" — an old server in front of a new runner behaves exactly as today. |
| **The display title and the herdr name are different values on purpose.** The cards' own example: tab "Orch" (operator-chosen), herdr agent `pm-orchestrator` — and after this plan, title `PM-Orchestrator-Grok`, herdr name `pm-orchestrator-grok`. | §2.3: two fields on the options record, two code paths, never derived from each other. |

## 1. What exists today (only what the cards did not already record; verified 2026-08-28)

### 1.1 The call path

- `AgentSessionService.BuildRuntimeLaunchSpecAsync` (`server/Application/Services/AgentSessionService.cs:1071-1112`):
  `ResolveOwningAgentAsync` (`:1160` — card's assigned agent, else the agent whose
  `PersistentSessionId` is this session; **null for an unassigned delegate/card session**) →
  `paneTitle` (`:1084-1086`) → `new HerdrLaunchContextResolver(_db).ResolveAsync(session, agent,
  paneTitle, ct)` → `with { AgentKind }` (`:1091-1092`). Nothing reads `agent.Slug`.
- `HerdrLaunchContextResolver` (`server/Application/Services/HerdrLaunchContextResolver.cs`):
  workspace key/label/cwd from card → board → project, else pool project, else catch-all; copies
  `paneTitle` through `FromProject`/`CatchAll` unchanged. **No test exercises it directly** —
  its only coverage is end to end through `HerdrAlwaysOnChannelParityTests` and the two
  `*HerdrRealCliStubProxyCanaryTests`.
- `HerdrLaunchOptions` (`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:36-48`):
  `WorkspaceKey, WorkspaceLabel, WorkspaceCwd, PaneTitle, AgentKind = null`. Twelve construction
  sites (`grep "new HerdrLaunchOptions("`), all positional-through-`PaneTitle` with `AgentKind`
  named or omitted — a trailing optional is source-compatible with every one.
- `SessionRunnerHttpClient.StartAsync` (`server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:80`)
  and the test double `DirectSessionRunnerClient` (`tests/Antiphon.Tests/TestHelpers/DirectSessionRunnerClient.cs:145`)
  both pass `Herdr: spec.Herdr` through verbatim.
- `HerdrPaneChild.LaunchAsync` (`src/Antiphon.SessionRunner/HerdrPaneChild.cs:76-150`):
  connect → `EnsureWorkspaceAsync` → `AllocatePaneAsync` (`tab.create` label = `PaneTitle` on
  the create arm; a **split** arm inherits the tab's existing label) → `pane.rename(PaneTitle)`
  → `pane.report_metadata(title: PaneTitle)` → PowerShell shell check → write + type the launch
  script → `WaitForExpectedAgentAsync` (`:318`, polls `pane.get.agent` until it equals the
  expected kind) → `pane.process_info` (best-effort, Warning on failure) → sidecar → delete
  script → `ChildStarted`. The `StartHerdrAsync` catch in `SessionRunnerRuntime` kills the pane
  on any throw from this method.
- `HerdrClient` (`src/Antiphon.SessionRunner/HerdrClient.cs:112-330`): typed wrappers for
  workspace/tab/pane methods and `AgentStartAsync`; `SendRequestAsync(method, params)` returns the
  raw `result` `JsonElement`; errors surface as `HerdrApiException(Code, Message)` or
  `HerdrBackendUnavailableException`. `HerdrClientSurfaceTests` pins by reflection that no
  `*Prompt*` wrapper exists and that `PaneSendTextAsync`/`PaneSendKeysAsync`/`PaneGetAsync`/
  `AgentStartAsync` do.
- `HerdrAgentInfo` (`HerdrApiModels.cs:55-69`) already has `Name`, `Agent`, `PaneId`,
  `AgentStatus` — the shape `agent.list` returns as `{type:"agent_list", agents:[AgentInfo]}`
  (schema `success_response`). `agent.list` takes `EmptyParams`; `agent.get` takes
  `{target}`; `agent.rename` takes `{target, name?}` and its success envelope is not named in
  the bundled schema (treat the result as opaque).

### 1.2 The herdr facts the design leans on (live, 0.8.2, protocol 20)

- `herdr --skill`: *"Agent commands accept either a unique live agent name or the pane ID
  currently hosting that agent. … Names must match `[a-z][a-z0-9_-]{0,31}` and be unique among
  live agents. A name follows the current pane occupant and is cleared when that agent exits, is
  released, or is replaced."*
- `herdr agent list` on 2026-08-28 → `{"agents":[]}` — the list is **live agents only**; a name
  is not a durable herdr-side record, which is exactly why Antiphon must re-apply it on every
  launch (the card's point).
- **Unmeasured, and deliberately not depended on:** what error code `agent.rename` returns for a
  name another live agent holds, and whether herdr *rejects* or *moves* the name. The pre-check
  in §2.2 makes the design correct under either answer; the build records the measured code in
  the card (one scratch pane, two `agent.rename`s, `pane.close`) so the fake server's error code
  matches reality.

### 1.3 Test seams

- Runner: `FakeHerdrServer` (`tests/Antiphon.SessionRunner.Tests/FakeHerdrServer.cs`) dispatches
  by method name at `:310-333`; has **no** `agent.list`/`agent.rename` handler (an unknown method
  throws `InvalidOperationException`); `PaneState` (`:826-861`) has `Label`/`Title`/`Agent` but
  no agent *name*; `AgentStartJson` (`:689`) writes the requested name into `pane.Label` only.
  `HerdrLaunchShapeTests` (`[NotInParallel("HerdrLaunchShape")]`) builds a real
  `SessionRunnerRuntime` over the fake and asserts on `fake.Requests` — that is the assertion
  seam for "what was sent, in what order". A list-capturing `ILogger` exists only as a
  **private** `ListLogger<T>` inside `TranscriptAdoptionSafetyTests.cs:1975`.
- Server: `HerdrAlwaysOnChannelParityTests.Herdr_launch_definition_starts_adopts_and_exits`
  (`tests/Antiphon.Tests/Application/HerdrAlwaysOnChannelParityTests.cs:241`) launches a real
  `AgentSessionService` through `DirectSessionRunnerClient` + `FakeHerdrServer` for an agent
  named `CARD0187-{kind}` — the one existing end-to-end path where both the title and the
  slug can be asserted from the fake's request log. `SessionRunnerHttpClientHerdrWireTests`
  captures the launch POST body.

## 2. Design

### 2.1 CARD-0225 — the pane title is the agent's display name (server-only)

`HerdrLaunchContextResolver` gains one pure static, and `BuildRuntimeLaunchSpecAsync:1084-1086`
calls it instead of computing the title inline:

```csharp
/// CARD-0225: the title the operator sees on the tab/pane. The agent's name, never the
/// shared TUI profile id — one profile serves many agents, so its DisplayName cannot label
/// any one of them. DefinitionName survives only for a session that has no agent at all.
public static string PaneTitleFor(Agent? agent, AgentSession session)
{
    if (!string.IsNullOrWhiteSpace(agent?.Name)) return agent.Name.Trim();
    if (!string.IsNullOrWhiteSpace(agent?.Slug)) return agent.Slug;
    if (!string.IsNullOrWhiteSpace(session.DefinitionName)) return session.DefinitionName;
    return "agent";
}
```

- The runner's three uses (`tab.create` label, `pane.rename`, `pane.report_metadata` title) are
  **unchanged** — they already take `PaneTitle`; the value is what was wrong.
- No short-label override column (`herdrTabLabel`) — D1. The operator gets "Orch" by naming the
  agent "Orch" in Antiphon, which also renames its slug; the two cards both list "no schema
  change" as the shape of the fix.
- The split arm of the allocator leaves the **tab** label as the first occupant's name and
  labels the **pane** with this agent's — existing behaviour, now with a sensible first name.

### 2.2 CARD-0211 — the herdr agent is renamed to the sanitised `Agent.Slug` (contract + runner)

**Contract.** `HerdrLaunchOptions` gains a trailing optional:

```csharp
    string? AgentKind = null,
    // CARD-0211: Antiphon Agent.Slug; the runner applies it as the herdr agent name after
    // detection (sanitised to herdr's [a-z][a-z0-9_-]{0,31}, never stolen from a live agent).
    // Null = do not rename — an old server in front of a new runner launches exactly as today.
    string? AgentSlug = null);
```

`BuildRuntimeLaunchSpecAsync` sets it: `herdr = herdr with { AgentKind = …, AgentSlug =
string.IsNullOrWhiteSpace(agent?.Slug) ? null : agent.Slug }`. An agentless session sends null
and is not renamed (it has no slug to carry — D3).

**Client.** Two wrappers on `HerdrClient`, beside `AgentStartAsync`:

```csharp
public Task<IReadOnlyList<HerdrAgentInfo>> AgentListAsync(CancellationToken ct)
    // "agent.list", EmptyParams → DeserializeRequired<HerdrAgentListEnvelope>(...).Agents
public Task AgentRenameAsync(string target, string? name, CancellationToken ct)
    // "agent.rename", new HerdrAgentRenameParams(target, name); result ignored (opaque)
```

`HerdrAgentListEnvelope` (`agents`) and `HerdrAgentRenameParams` (`target`, `name`) join
`HerdrApiModels.cs`. `agent.prompt` stays unwrapped; `HerdrClientSurfaceTests` gets the two new
positive pins.

**Runner.** In `HerdrPaneChild.LaunchAsync`, immediately after `WaitForExpectedAgentAsync`
returns and before `pane.process_info`:

```csharp
await TryApplyAgentNameAsync(paneId, opts.AgentSlug, ct);
```

`TryApplyAgentNameAsync` — **never throws except on cancellation**; a name is identity for the
operator's convenience, and the launch has already succeeded by the time it runs:

1. `AgentSlug` null/blank → return (Debug log). This is the old-server path.
2. `desired = SanitizeAgentName(slug)`.
3. `live = AgentListAsync()`; on `HerdrApiException`/`HerdrBackendUnavailableException` → **skip
   the rename** with a Warning (`"herdr agent.list failed; not renaming pane {PaneId} — cannot
   prove '{Desired}' is free"`). Without the list, no-steal cannot be proven, and herdr's own
   enforcement mode on a duplicate is unmeasured (§1.2) — D4.
4. `held = live.Where(a => a.PaneId != paneId && a.Name is not null).Select(a => a.Name)`.
   Choose `name = desired`; while `held` contains it, `name = Suffix(desired, n)` for n = 2, 3, …
   where `Suffix` trims the base to `32 - "-n".Length` before appending (the `UniqueSlugAsync`
   rule at `AgentService.cs:1266`, transposed to herdr's cap). Cap at 9 attempts; beyond that,
   leave unnamed with a Warning. **A collision is always logged as a Warning** naming the held
   name and the pane holding it, whether or not a suffix was applied — the card asks for the
   Warning; the suffix is D2's addition on top.
5. `AgentRenameAsync(target: paneId, name)`. **Target is the pane id, never the name**: a name
   target could resolve to another live agent, and the herdr rule "never rename an agent that is
   not the one we just launched" is exactly the card's constraint. On `HerdrApiException` →
   Warning with the code (`"herdr agent.rename to '{Name}' refused ({Code}) for pane {PaneId}; agent
   stays unnamed"`), continue the launch. On success → Information (`"herdr agent on pane {PaneId}
   named '{Name}'"`, with `(from '{Desired}')` when suffixed).

Nothing is written to the sidecar (the card rules out a sidecar field; herdr clears the name when
the agent exits, and adoption never needs it). Nothing changes in `KillAsync`, adoption, the
event pump, or the status push.

### 2.3 The two values stay independent — stated, not implied

| | Source | Sanitised? | Sent as | Herdr surface |
|---|---|---|---|---|
| **Display title** (CARD-0225) | `agent.Name` (→ `Slug` → `DefinitionName` → `"agent"`) | no — herdr labels are free text | `PaneTitle` | `tab.create` label, `pane.rename`, `pane.report_metadata` title |
| **Agent identity** (CARD-0211) | `agent.Slug` (null when no agent) | yes — `[a-z][a-z0-9_-]{0,31}`, suffixed on collision | `AgentSlug` | `agent.rename` after detection; addressable as `herdr agent get <name>` |

`PM-Orchestrator-Grok` therefore shows as tab/pane **`PM-Orchestrator-Grok`** and answers to
**`pm-orchestrator-grok`**. Neither field is derived from the other, and a future short-label
override would touch only the first column.

### 2.4 Docs

- `docs/herdr-sessions.md` §3: `PaneTitle` is "the agent's name — never the TUI profile id";
  §3 adds `AgentSlug`; §4's launch sequence gains "→ `agent.list` → `agent.rename <paneId>
  <slug>` (suffixed `-2`… if a live agent holds it; skipped, Warning, if the list or rename
  fails)"; §8 gains a row: *"herdr agent is `<slug>-2`" → another live agent (often the previous
  incarnation's pane) holds `<slug>`; nothing is stolen.*
- `AGENTS.md`: one gotcha bullet — the herdr agent name is `Agent.Slug` applied at every launch
  (herdr forgets it when the agent exits), the tab/pane title is `Agent.Name`, the two are
  independent, and a live holder is never renamed out from under.

## 3. What this costs its neighbours

- **Every existing herdr test** keeps passing unchanged: `AgentSlug` defaults null, so the fake
  never sees `agent.list`/`agent.rename` from a test that does not set it — except the server-side
  parity/canary launches, whose agents *have* slugs; the fake must therefore grow both handlers in
  the same slice (a missing handler throws in the fake, which the runner would log as a Warning
  and continue — the tests would still pass, but silently against an unmodelled method; add the
  handlers first).
- **`RequireAgentPaneId()`** counts panes with `Agent is not null`; a test that seeds a colliding
  live agent must not call it. New tests assert on `fake.Requests` and pane state directly.
- **CARD-0213 (attach an operator pane)** will want the same `TryApplyAgentNameAsync`; it is a
  private method on `HerdrPaneChild` today, which is where an attach path would live too.
- **Renaming an agent in Antiphon mid-life** does not rename its live herdr agent; the next launch
  does. Same for a title. Stated in the doc, not built (§4).
- **Runner ↔ server version skew** in both directions is covered by the null default (§2.2) and by
  `Web` deserialisation ignoring unknown members; pinned by the wire test.

## 4. Non-goals (deferred, all named by the cards)

- Reusing an existing pane (e.g. the operator's "Orch") on relaunch instead of creating one —
  the linked follow-up issue.
- Attaching an operator pane Antiphon never launched — CARD-0213.
- Any Postgres column or sidecar field for herdr names, tab/pane/workspace ids — CARD-0160.
- A `herdrTabLabel`/short-title override on the agent — D1 says no.
- Re-applying a changed slug/name to a live pane; `tab.rename`; `display_agent`; changing
  `Agent.Slug` uniqueness or its 120-char ceiling.

## 5. Decisions that are the operator's — each with a recommendation

- **D1 — Title = full `agent.Name` (`PM-Orchestrator-Grok`), or a short label?** Recommend the
  full name, no new column. Rejected: "first token of the name" heuristics (`PM` is not `Orch`),
  and a new agent column (both cards say no schema change).
- **D2 — On a name collision: suffix `-2`, `-3` … (recommended) or leave the agent unnamed?**
  Suffix mirrors Antiphon's own `UniqueSlugAsync`, always yields an addressable name, and the
  Warning fires either way. Leave-unnamed is strictly simpler and the card allows it; the
  difference is one loop and one test arm.
- **D3 — A session with no owning agent (unassigned delegate/card session): title falls back to
  `DefinitionName` and no rename?** Recommend yes — there is no name or slug to carry, and the
  profile id is at least true. Alternative: the card identifier / task title as the pane title
  (would need the task row in `BuildRuntimeLaunchSpecAsync`; not worth it for a lane that is
  opt-in per standing agent today).
- **D4 — If `agent.list` fails, skip the rename (recommended) or attempt it anyway?** Skipping is
  the conservative reading of "never steal" while herdr's duplicate-name behaviour is unmeasured;
  once the build measures a rejection code, attempting-anyway becomes safe and can be relaxed in
  the same commit if the operator prefers.

## 6. The slice, tier, tests

One slice. Dispatch per this session's routing (plan → Fable/Opus; simple builds → Codex terra;
verify → Codex luna; else Grok): **Grok, `-Worktree`, `-Scope herdr,runner,session-launch`**
(server `AgentSessionService.cs` + `HerdrLaunchContextResolver.cs`, contract, `HerdrClient.cs`,
`HerdrApiModels.cs`, `HerdrPaneChild.cs`, the fake server, five test files, two docs). Build to
`--property:OutputPath=bin-c0211/` (forward slash) while the daemons hold `bin/`, and delete the
`bin-c0211` directories before finishing. Run `Antiphon.SessionRunner.Tests` and `Antiphon.Tests`
**one after the other**, never co-scheduled.

Before writing code, one **live probe** on a scratch pane (recorded in the card, then
`pane.close`): `agent.rename <scratch> probe-a`, a second scratch renamed to `probe-a` → the
error code (or the observed steal). The fake's collision error code is set from that measurement.

| What | Tests (all red-before-green) |
|---|---|
| **Server:** `HerdrLaunchContextResolver.PaneTitleFor`; `BuildRuntimeLaunchSpecAsync` uses it and sets `AgentSlug`. | `HerdrLaunchContextResolverTests` (new, `Antiphon.Tests`, no DB): name `PM-Orchestrator-Grok` + definition `grok-gkp-project` → `PM-Orchestrator-Grok`; blank name + slug → slug; null agent → `grok-gkp-project`; nothing → `agent`. `HerdrAlwaysOnChannelParityTests.Herdr_launch_definition_starts_adopts_and_exits` gains three asserts from `fake.Requests`: `tab.create` label and `pane.rename` label == the agent's `Name` (and ≠ the definition's DisplayName), `agent.rename` name == `SanitizeAgentName(agent.Slug)` targeting the launched pane id. |
| **Contract + wire:** `HerdrLaunchOptions.AgentSlug`. | `SessionRunnerHttpClientHerdrWireTests`: `AgentSlug` appears on the POST body; a body **without** `agentSlug` deserialises to a `RunnerLaunchRequest` whose `Herdr.AgentSlug` is null (old server → new runner). |
| **Client:** `AgentListAsync`, `AgentRenameAsync`, the two models. | `HerdrClientSurfaceTests`: positive pins for both; the no-`Prompt` rule still holds. `HerdrClientTests`: `agent.rename` sends `{target, name}` with `name: null` allowed; `agent.list` deserialises `name`-less agents (K5 shape). |
| **Fake:** `PaneState.AgentName`; `agent.list` (live agents = panes with `Agent != null`, `name` = `AgentName`); `agent.rename` (target = pane id **or** unique live name; regex check; uniqueness → `FakeHerdrApiException(<measured code>)`; `null` clears); `AgentStartJson` also sets `AgentName`; knobs `SeedDetectedAgent(paneId, kind, name)` and `RejectAgentRename` (code). Hoist `ListLogger<T>` from `TranscriptAdoptionSafetyTests` into a shared runner-test helper. | — (exercised by the rows below) |
| **Runner:** `TryApplyAgentNameAsync` in `LaunchAsync`. | `HerdrLaunchShapeTests` (same `NotInParallel` group): **(a)** `AgentSlug: "PM-Orchestrator-Grok-<40 chars>"` → exactly one `agent.rename`, `target` == the launched pane id, `name` == the 32-char sanitised form, sent **after** the detecting `pane.get` and before the sidecar write; the fake pane's `AgentName` equals it. **(b)** collision: seed a second pane holding `pm-orchestrator-grok`; launch with that slug → the seeded pane still holds it; the launched pane is `pm-orchestrator-grok-2` (D2 = suffix) — or receives no `agent.rename` and stays unnamed (D2 = none); `ListLogger` holds one Warning containing the held name and the holder's pane id. **(c)** `AgentSlug: null` → no `agent.list`, no `agent.rename`. **(d)** `RejectAgentRename` → session still `Running`, sidecar written, script deleted, one Warning with the code. **(e)** `SanitizeAgentName`: `PM-Orchestrator-Grok` → `pm-orchestrator-grok`; 40 chars → 32; `2pm` → `a2pm`; `Suffix("<32 chars>", 2)` → 30 chars + `-2`. |
| **Docs:** `docs/herdr-sessions.md` §3/§4/§8, `AGENTS.md` bullet. | Read-through in the PR. |

**Close-out evidence (in the card):** relaunch `PM-Orchestrator-Grok` (`2ee02f40`) once on the
live herdr and paste `herdr agent list` + the tab label — the tab reads `PM-Orchestrator-Grok`,
the agent answers to `herdr agent get pm-orchestrator-grok`, and if the operator's "Orch" pane is
still live the new agent is `pm-orchestrator-grok-2` with the Warning in
`%TEMP%\antiphon-logs\session-runner-*.log`.
