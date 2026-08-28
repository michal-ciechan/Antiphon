# CARD-0212 — Never type `/remote-control` into a kind whose catalog says it cannot take it

**Date:** 2026-08-28
**Status:** planned (design only — nothing here is implemented)
**Card:** CARD-0212 (`71b6d6e0-25a4-4b64-8e37-9dee3a36fb07`), board Antiphon — GitHub-tracker-sourced bug
**Scope:** one build slice. Server gate + persistence rule + four client controls + tests + docs.
**Out of scope (the card's own words):** implementing remote control for Grok. Nothing here
touches the Claude boot path's *behaviour* — `SendRemoteControlCommandsAsync`'s arm-wait, rename
ordering, CARD-0056 degrade-not-fail — all of that stays byte-for-byte for ClaudeCode.
**Model followed:** `docs/superpowers/plans/2026-08-28-card-0217-sub-second-pages-plan.md` (shape
only; this card is a single slice).

## Verdict, in one screen

| Fact (verified 2026-08-28, line numbers current at `69d26e6`) | Consequence for the design |
|---|---|
| The catalog already states the answer: `AgentTuiRunnerCatalog` marks `remoteControl` **Supported** for ClaudeCode only (`server/Application/Services/AgentTuiRunnerCatalog.cs:104`) and **Unsupported** for Codex (`:118`), OpenCode (`:132`), Grok (`:146`) and Raw (`:160`). `AgentTuiProfileServiceTests.Runner_catalogue_is_curated_and_truthful_without_probing` pins all five. `ProviderContractCatalog`'s own doc says launch/config capabilities "stay on `AgentTuiRunnerCatalog`" (`ProviderContractCatalog.cs:9-11`), and `SessionHealthService.LoadCandidatesAsync` already leans on exactly that (`SessionHealthService.cs:283-286`, "D3 deliberately left it off ProviderContract"). | **D1: gate on the catalog capability, never on a kind literal.** One new query on the catalog, `SupportsRemoteControl(AgentKind)`, is the only place the string `"remoteControl"` is compared. A `kind == ClaudeCode` check anywhere would be a second copy of a fact the catalog and its tests already own, and would silently diverge the day a second kind grows a bridge. |
| There are **four** places a remote-control name reaches a session, not three: (1) `AgentControlService.StartAsync` (`AgentControlService.cs:121-122`), (2) `AgentService.CreateAsync`/`UpdateAsync` persisting `RemoteControlEnabled` (`AgentService.cs:409`, `:502-503`), (3) `AgentSessionService.SendRemoteControlCommandsAsync` (`AgentSessionService.cs:1280`), and (4) **`POST /api/cards/{id}/spawn` → `CardService.SpawnAsync`**, whose `SpawnCardRequest.RemoteControlName` (`BoardDtos.cs:270-272`) is HTTP-bindable and flows straight into `StartAgentSessionRequest.RemoteControlName` (`CardService.cs:830`) **without passing through `AgentControlService` at all.** | **D2: one static policy, four one-line call sites.** `RemoteControlPolicy` (new, static, DI-free — the pattern `AgentService.cs:1247` already uses when it does `new AgentTuiRunnerCatalog()`) owns the refusal and the warning text. Each call site is a single call. The deepest one, in `SendRemoteControlCommandsAsync`, keys on `session.AgentKind` — the kind the adapter was actually created from (`AgentSessionService.cs:361`) — and is what makes the property hold regardless of which door was used. |
| Every **internal** restart passes `RemoteControl: null` and inherits the row's flag: the AlwaysOn supervisor (`AgentSupervisorService.cs:203`), the channel bridge (`ChannelBridgeService.cs:357`), the check-interpreter provisioner (`CheckInterpreterProvisioner.cs:128`), project setup (`ProjectSetupService.cs:338`). A row created before this card can hold `RemoteControlEnabled = true` on a Grok agent. | **D3: refuse the explicit ask (409), ignore the inherited flag (warn).** `StartAgentRequest.RemoteControl == true` on a non-capable kind is a caller stating an intent the kind cannot honour → `409 remote_control_refused`, the exact shape of `herdr_refused` (`AgentService.cs:1370-1375`) and `subscription_quota_low` (a refusal, never a footnote — CARD-0136). A **null** request that would inherit a stale `true` from the row is *not* refused — that would make an AlwaysOn Grok agent un-restartable over a bookkeeping flag (the CARD-0063 rule: a bookkeeping field must not refuse a launch) — it launches with `remoteControlName = null` and one Warning log line naming the fix. Never degrade-after-send: nothing is typed in either case. |
| Persistence today writes the flag verbatim on create (`AgentService.cs:409`, *before* `ApplyTuiSelectionAsync` may change `Kind` at `:423-428`) and on PATCH (`:502-503`, with no look at `Kind`). The Herdr gate already solved this exact ordering problem: create re-checks **after** the profile is applied (`:429`), PATCH resolves the request's *final* Kind first via `ResolveFinalKindAsync` (`:485-486`). | **D4: same rule, same spot — the state the request would leave behind must be valid.** Create: after `ApplyTuiSelectionAsync`, `Require(agent.Kind, agent.RemoteControlEnabled)`. PATCH: `Require(finalKind, request.RemoteControlEnabled ?? agent.RemoteControlEnabled)` next to the Herdr check, before any field is written. Refuse, never coerce: a silent flip is the failure mode CARD-0139's assert-or-set was built to avoid, and the UI (D5) makes the refusal unreachable from the screens. |
| The client already has both facts: every enabled profile carries `capabilities` (`client/src/api/agentTui.ts:93`, used by `AgentTuiSelection.tsx:55` for `modelArgument`), and `/api/agent-tui/runner-types` (`useAgentTuiRunnerTypes`, `agentTui.ts:162`) exposes the catalog per kind for an agent that has no profile. `AgentSummaryDto.kind` and `.tuiProfileId` are on the wire (`agents.ts:153`, `:192`). | **D5: one hook, four controls.** `useRemoteControlSupport({ tuiProfileId, kind })` resolves *profile → kind → unknown*, and the four controls (`AgentCreateModal`, `AgentSettingsModal`, `ProjectSetupModal`, `AgentAddWorkModal`) disable-or-hide on it and **submit `false`** whenever it is not Supported. The create/settings switch is **disabled with the catalog's reason as its description** (the `modelArgument` precedent in `AgentTuiSelection`); the Add-Work checkbox is **hidden** (a start-time option with nothing to explain). |
| `AgentSessionLaunchFailureTests` drives its card-path fixture as **`AgentKind.Raw`** (`:759`, `:762`) and one test asserts `/remote-control` was typed on it (`Card_work_prompt_delivery_failure_still_fails_the_launch`, `:324-330`). | The deep gate turns that test red for the right reason: Raw is catalog-Unsupported. The fixture grows a `kind` parameter (default Raw, unchanged for the other four callers) and that one test says `ClaudeCode` — the kind a test about `/remote-control` should always have named. |

## 1. What exists today (verified 2026-08-28)

### 1.1 The capability, and the one query that is missing

- `AgentTuiRunnerCatalog` (`server/Application/Services/AgentTuiRunnerCatalog.cs`) is a
  DI singleton (`Program.cs:436`) with a parameterless constructor; `Get(kind, profileArguments?)`
  returns an `AgentTuiRunnerTypeDto` whose `Capabilities` is a list of
  `AgentTuiCapabilityDto(Name, State, Reason)`. `remoteControl` does **not** depend on
  `profileArguments` (only `permissionBypass` does), so `Get(kind)` with no arguments is exact.
- There is no "is X supported for kind K" query on the catalog. The three consumers that ask the
  question today each write their own `Capabilities.First(c => c.Name == …).State ==
  AgentTuiCapabilityState.Supported` (`AgentTuiProfileService.cs:1690-1694` private static,
  `AgentTuiSelection.tsx:55` client-side, and `SessionHealthService` which hard-codes
  `AgentKind.ClaudeCode` in its query at `:298` with the comment explaining it is standing in
  for the catalog).
- `AgentTuiCapabilityState` has four values (`Domain/Enums/AgentTuiEnums.cs:30`); the
  `ProviderContract` doc fixes the reading for enabling machinery: **Unknown behaves as
  Unsupported** (`ProviderContract.cs:23-27`). Only `Supported` enables.

### 1.2 The four doors

| # | Door | Kind it knows | What happens with a name today |
|---|---|---|---|
| 1 | `AgentControlService.StartAsync` (`:88`) | `PeekProfileKindAsync(agent)` at `:102` (already computed for the quota gate; nullable), and `agent.Kind` (row truth, `Agent.cs:119`, synced from the profile by invariant) | `remoteControl = request.RemoteControl ?? agent.RemoteControlEnabled` → `remoteControlName = agent.Name` (`:121-122`), handed to `SpawnAsync` (card branch, `:132`) or `StartInteractiveSessionAsync` (`:141`). No gate. |
| 2 | `AgentService.CreateAsync` (`:308`) / `UpdateAsync` (`:471`) | create: `agent.Kind` **after** `ApplyTuiSelectionAsync` (`:423`); PATCH: `ResolveFinalKindAsync` (`:485`) | Flag written verbatim (`:409`, `:502-503`). No gate. |
| 3 | `AgentSessionService.SendRemoteControlCommandsAsync` (`:1280`) | `session.AgentKind` — the adapter was created from it at `:361`; the card path's session kind is `request.AgentKind` | Types `/remote-control`, waits for the armed marker, types `/rename <name>`; on delivery failure raises `RcDegraded` (CARD-0056). Called from the card path (`:196`) and the interactive path (`:388`). |
| 4 | `CardService.SpawnAsync` (`:668`, enqueue at `:819`) via `POST /api/cards/{id}/spawn` (`CardEndpoints.cs:106`) | `spec.Kind`, resolved before the enqueue (`:825`) | `request.RemoteControlName` copied onto `StartAgentSessionRequest` (`:830`). No gate. Also reached from `AgentControlService.StartAsync` (door 1, already gated by then) and from `AgentChannelService` (`:137`, never passes a name). |

Doors that already pass **no** name and need nothing: `AgentTaskDispatcher` (`:1779`,
`remoteControlName: null`; pool delegates are created with `RemoteControlEnabled = false` at
`:2257`), `CheckInterpreterProvisioner` (`:101`), `SessionHealthService.WatchRcAsync` (queues
`/remote-control` mid-life, but `LoadCandidatesAsync` already restricts to ClaudeCode sessions).

### 1.3 The client controls

| Control | File:line | Today | Kind/profile it can see |
|---|---|---|---|
| Create — "Remote control" `Switch` | `AgentCreateModal.tsx:254-259` | always rendered, default off, submits `remoteControlEnabled` | `tuiProfileId ?? default profile` (`:105`, `:223`); `useAgentTuiProfiles()` already called at `:58` |
| Settings — "Remote control" `Switch` | `AgentSettingsModal.tsx:269-274` | always rendered, seeded from `agent.remoteControlEnabled`, submits `remoteControlEnabled` | `tuiProfileId` state (`:65`, seeded `:111`); `agent.kind` (summary DTO) as fallback |
| Project setup — "Remote control" `Switch` | `ProjectSetupModal.tsx:297` | always rendered, submits `remoteControlEnabled` into `ProjectSetupService` → `CreateAgentRequest` (`ProjectSetupService.cs:323`) | `tuiProfileId` state (`:80`); the catalog summary only carries `kind` (`projectSetup.ts:47`), but `AgentTuiSelection` (`:276`) already fetches the full profiles, so `useAgentTuiProfiles()` is a cache hit |
| Add work — "Remote control" `Checkbox` | `AgentAddWorkModal.tsx:145-149` | always rendered, **default `true`** (`:26`, `:39`), sends `startAgent({ remoteControl })` (`:71`) — under D3 this would 409 on every Grok add-work until fixed | `agent.tuiProfileId` / `agent.kind` (summary DTO prop, `:10`) |
| CLI modal | `AgentCliModal.tsx:42`, fed by `AgentsPage.tsx:308` | sends `remoteControl: agent.remoteControlEnabled` explicitly | after D4 the persisted flag is `false` on every non-capable kind that has been through the UI once; a pre-existing stale `true` hits D3's explicit-refusal arm — see §5 D-A |
| Agents page Start button | `AgentsPage.tsx:225` | sends `{}` (inherit) | D3's ignore-and-warn arm |

### 1.4 What does not exist (the build list)

- `AgentTuiRunnerCatalog.SupportsRemoteControl(AgentKind)` and the `RemoteControlCapability`
  name constant.
- `RemoteControlPolicy` (static): `Require(kind, wanted, what)` → `ConflictException(…,
  "remote_control_refused")`; `Permits(kind)`; the shared Warning text.
- The four call-site lines (§2.2) and the `SpawnAsync` line.
- Client: `remoteControlCapability(profile | runnerType)` helper + `useRemoteControlSupport` hook;
  four control edits.
- Tests (§4) and docs (§2.5).

## 2. Design

### 2.1 The fact: `AgentTuiRunnerCatalog.SupportsRemoteControl`

```csharp
// AgentTuiRunnerCatalog.cs
public const string RemoteControlCapability = "remoteControl";

/// CARD-0212. True only when the kind's catalog row declares remoteControl Supported.
/// Unknown and Degraded read as not-supported for enabling machinery (ProviderContract rule 2).
public bool SupportsRemoteControl(AgentKind kind) =>
    Enum.IsDefined(kind)
    && Get(kind).Capabilities.Any(c =>
        string.Equals(c.Name, RemoteControlCapability, StringComparison.Ordinal)
        && c.State == AgentTuiCapabilityState.Supported);
```

`Enum.IsDefined` first because `Get` throws `ArgumentOutOfRangeException` on an unknown kind and a
gate must answer "no", not blow up, on a value a future migration has not taught it yet (the same
guard `ContextCompactionService.IsContextWindowEligible` uses, `:96`). The five string literals
`"remoteControl"` already in the catalog's capability builders (`:104/:118/:132/:146/:160`) become
`RemoteControlCapability` so the name cannot drift from the query.

Optional, zero-risk tidy in the same slice: `SessionHealthService.LoadCandidatesAsync` replaces
its `s.AgentKind == AgentKind.ClaudeCode` literal (`:298`) with an `IN` over
`Enum.GetValues<AgentKind>().Where(catalog.SupportsRemoteControl)` computed once, and the "D3"
comment there is updated to point at this card. Behaviour is identical today; the builder may skip
it if the harness there resists the change — say so in the report.

### 2.2 The policy: `RemoteControlPolicy` (new file, `server/Application/Services/RemoteControlPolicy.cs`)

```csharp
/// CARD-0212. The one place that turns the catalog's remoteControl capability into a decision.
/// Static and DI-free on purpose: AgentService, AgentControlService, CardService and
/// AgentSessionService are all hand-constructed in tests, and a constructor parameter here would
/// ripple through every harness for a pure lookup (precedent: AgentService.cs:1247).
public static class RemoteControlPolicy
{
    public const string RefusalCode = "remote_control_refused";
    private static readonly AgentTuiRunnerCatalog Catalog = new();

    public static bool Permits(AgentKind kind) => Catalog.SupportsRemoteControl(kind);

    /// Throws 409 remote_control_refused when <paramref name="wanted"/> is true on a kind that
    /// cannot take it. <paramref name="what"/> names the request for the message
    /// ("agent 'X'", "start of agent 'X'", "spawn of card CARD-0012").
    public static void Require(AgentKind kind, bool wanted, string what)
    {
        if (!wanted || Permits(kind)) return;
        throw new ConflictException(
            $"Remote control is not available for {kind} agents ({Reason(kind)}); {what} asked for it. "
            + "Send remoteControlEnabled: false (or omit remoteControl on the start request).",
            RefusalCode);
    }

    /// Message for the inherit-and-ignore arm (D3) and the deep gate (2.2 #3). Logged at Warning.
    public static string IgnoredMessage(AgentKind kind, string what) => …;

    private static string Reason(AgentKind kind) => /* the catalog row's Reason, or "not in the catalog" */;
}
```

Message contract, pinned by tests: contains the kind name, the code is `remote_control_refused`,
and the message tells the caller what to send instead (the `herdr_refused` message names its
supported list for the same reason).

**The four call sites, each one line:**

1. **`AgentControlService.StartAsync`**, replacing `:121-122`:
   ```csharp
   var launchKind = kind ?? agent.Kind;                     // profile kind if resolvable, else row truth
   RemoteControlPolicy.Require(launchKind, request.RemoteControl == true, $"start of agent '{agent.Name}'");
   var remoteControl = request.RemoteControl ?? agent.RemoteControlEnabled;
   if (remoteControl && !RemoteControlPolicy.Permits(launchKind))
   {
       _logger.LogWarning(RemoteControlPolicy.IgnoredMessage(launchKind, …));   // inherited stale flag: ignore, never refuse (D3)
       remoteControl = false;
   }
   var remoteControlName = remoteControl ? agent.Name : null;
   ```
   `kind` is the `PeekProfileKindAsync` result already computed at `:102` for the quota gate — the
   kind of the process that will actually launch (the default profile when the agent has none).
   Falling back to `agent.Kind` when it is null keeps the legacy-registry path (and the existing
   `Start_with_remote_control_boots_queue_head…` test, whose agent has no profile) on the row's
   own default of ClaudeCode instead of failing closed on an unresolvable kind. The `Require`
   runs **before** `ResolveStartCardAsync`/`SpawnAsync` so a refused start creates no session row
   and leaves `agent.Status` untouched — the same "refused before anything happened" contract the
   quota gate gives at `:103-113`. Note `HasLiveSessionAsync` (`:98`) still returns early first:
   a request for RC on an already-running Grok session is a no-op today and stays one.

2. **`AgentService`** — create, immediately after the Herdr re-check at `:429`:
   ```csharp
   RemoteControlPolicy.Require(agent.Kind, agent.RemoteControlEnabled, $"agent '{agent.Name}'");
   ```
   PATCH, immediately after `ValidateSessionBackendPairing(finalBackend, finalKind)` at `:486`:
   ```csharp
   RemoteControlPolicy.Require(finalKind, request.RemoteControlEnabled ?? agent.RemoteControlEnabled, $"agent '{agent.Name}'");
   ```
   Both are before any write, so a refused request leaves the row exactly as it was (pinned).
   A PATCH that switches a Claude agent with RC on to a Grok profile is refused unless the same
   request sends `remoteControlEnabled: false` — identical to how a Kind change is checked against
   Herdr in the same request (`:482-486`). The settings modal (D5) always sends the field, and sends
   `false` whenever the selected profile is not capable, so the screen never trips this.

3. **`AgentSessionService.SendRemoteControlCommandsAsync`**, after the null-name early return at
   `:1287`:
   ```csharp
   if (!RemoteControlPolicy.Permits(session.AgentKind))   // signature gains the AgentSession (or its Kind)
   {
       _logger.LogWarning(RemoteControlPolicy.IgnoredMessage(session.AgentKind, $"session {sessionId}"));
       return;
   }
   ```
   Types nothing, raises **no** incident: `RcDegraded` means "asked the TUI and it did not arm";
   here nothing was asked. The two callers (`:196` card path, `:388` interactive) pass the
   `session` they already hold. This is the last line of defence — unreachable from the public
   API once 1, 2 and 4 are in, and exactly why it must exist: the next door someone adds is
   covered by it on day one. Everything after this line in the method is untouched.

4. **`CardService.SpawnAsync`**, immediately before the `EnqueueInteractive` at `:819`:
   ```csharp
   RemoteControlPolicy.Require(spec.Kind, !string.IsNullOrWhiteSpace(request.RemoteControlName), $"spawn of card {card.Identifier}");
   ```
   Makes `POST /api/cards/{id}/spawn` with a name on a Grok card a 409 like every other explicit
   ask, instead of a silently dropped name. The `AgentControlService` caller never reaches this
   with a name on a non-capable kind (door 1 cleared it), so the AlwaysOn card path is unaffected.

### 2.3 Refuse vs. coerce, stated once

| Situation | Verdict | Why |
|---|---|---|
| `POST /agents` / `PATCH /agents/{id}` would leave `RemoteControlEnabled = true` on a non-capable kind | **409 `remote_control_refused`** | Herdr precedent (`herdr_refused`, refusal never silent remap). A coerced write is a request the server answered 200 to and then did something else with. |
| `POST /agents/{id}/start` with `remoteControl: true` on a non-capable kind | **409** | Explicit intent the kind cannot honour; CARD-0136's rule that a launch refusal is a refusal, not a footnote. Nothing launched, no row created. |
| `POST /agents/{id}/start` with `remoteControl: null` inheriting a stale `true` (pre-card row) | **Launch without RC, one Warning log** | Every internal restart path is this shape (`AgentSupervisorService.cs:203` etc.). Refusing would make an AlwaysOn Grok agent un-restartable over a bookkeeping flag. The Warning names the PATCH that clears it. |
| `POST /cards/{id}/spawn` with `remoteControlName` on a non-capable kind | **409** | Same door class as start-with-true. |
| A name reaching `SendRemoteControlCommandsAsync` on a non-capable session kind | **Type nothing, Warning, no incident** | Belt-and-braces; the incident kinds all mean "asked and failed". |
| Any of the above on ClaudeCode | **Unchanged** | Acceptance criterion 3. |

### 2.4 The client (D5)

`client/src/api/agentTui.ts` gains:

```ts
export const REMOTE_CONTROL_CAPABILITY = 'remoteControl'
export function remoteControlCapability(
  source: { capabilities: AgentTuiCapabilityDto[] } | undefined,
): AgentTuiCapabilityDto | undefined
```

and a hook, `client/src/features/agents/useRemoteControlSupport.ts`:

```ts
/** CARD-0212. Resolves the catalog's remoteControl row for the runner an agent would launch:
 *  the selected/attached profile first, the kind's runner type when there is no profile,
 *  undefined while neither has loaded. `supported` is true ONLY on a Supported row — an
 *  unloaded or Unknown row disables the control, mirroring the server's Unknown-is-Unsupported. */
export function useRemoteControlSupport(input: { tuiProfileId?: string | null; kind?: AgentKind | null }):
  { supported: boolean; reason: string | undefined; resolved: boolean }
```

Resolution order: `useAgentTuiProfiles()` → the profile with `id === tuiProfileId` (or, when the
caller passes none, the default profile — the create modal's own rule at `:105`) → its
`capabilities`; else `useAgentTuiRunnerTypes()` → the entry with `kind` → its `capabilities`.
Both queries are already cached by every screen that shows the profile picker, so the hook adds
no requests on the create/settings/setup modals; the Add-Work modal pays one `runner-types` GET
(tiny, cached 5 s).

| Control | Change |
|---|---|
| `AgentCreateModal` | `const rc = useRemoteControlSupport({ tuiProfileId: tuiProfileId ?? defaultProfileId })`. `Switch` gets `disabled={!rc.supported}`, `checked={rc.supported && remoteControlEnabled}`, `description={rc.supported ? <today's text> : rc.reason ?? 'Not available for this runner.'}`. Submit sends `remoteControlEnabled: rc.supported && remoteControlEnabled`. |
| `AgentSettingsModal` | Same, with `{ tuiProfileId, kind: agent?.kind }` so an agent with no profile still resolves by kind. Submitting `false` on a non-capable kind is what heals a pre-card row the first time its settings are saved. |
| `ProjectSetupModal` | Same as create, with `{ tuiProfileId }` (the picker there is the same `AgentTuiSelection`; when no profile is picked, the default one — `ProjectSetupService` resolves the same default server-side). |
| `AgentAddWorkModal` | `const rc = useRemoteControlSupport({ tuiProfileId: agent.tuiProfileId, kind: agent.kind })`. The `Checkbox` renders only when `rc.supported`; state default becomes `rc.supported` (not `true`); the start call sends `{ remoteControl: rc.supported && remoteControl }`. Toast text already branches on the flag. |
| `AgentCliModal` / `AgentsPage` | No change required (see §1.3). Optional one-liner: `remoteControl={terminalAgent.remoteControlEnabled && rc.supported}` at `AgentsPage.tsx:308` so the "(remote control on)" prompt text cannot lie on a stale row. |

"Disable, not hide" for the three settings-shaped switches because the reason is worth showing
(the `modelArgument` precedent in `AgentTuiSelection.tsx:73-77`); "hide" for Add-Work because a
disabled start-time option with a paragraph of reason is noise on a form whose job is the card.

### 2.5 Docs

- `docs/antiphon-api.md:52` — add `remote_control_refused` to the branchable-codes list;
  `:154-156` — extend the PATCH sentence: "…and where the remote-control capability gate fires
  (`409 remote_control_refused` on a kind whose catalog row is not Supported — ClaudeCode only
  today)". Add one line under the agents route block for `POST /agents/{id}/start` and
  `POST /cards/{id}/spawn` refusing with the same code.
- `docs/agent-kinds.md` — the per-kind table already mentions remote control (grep
  `remote-control`); make the Grok/Codex/OpenCode/Raw "No remote control" lines (`:238`, `:310`, …) say it is refused at create/PATCH/start
  and never typed.
- `AGENTS.md` Gotchas — one bullet in the house style, next to the Herdr/CARD-0136 ones:
  "**`/remote-control` is typed only into a kind whose catalog row says Supported** (CARD-0212):
  `RemoteControlPolicy` refuses an explicit ask with `409 remote_control_refused` at create,
  PATCH, start and card-spawn, ignores an inherited stale flag at start with a Warning, and
  `SendRemoteControlCommandsAsync` types nothing on any other kind. The fact lives on
  `AgentTuiRunnerCatalog.SupportsRemoteControl`; never add a `kind == ClaudeCode` check beside it."
- `docs/superpowers/plans/…` (this file) — flip **Status** to `built (<sha>)` in the build commit.

## 3. What this costs its neighbours

- **Claude boot path:** zero behavioural change. `SendRemoteControlCommandsAsync`'s new early
  return is after the null-name check and before the first `SnapshotRawOutput`; for ClaudeCode
  `Permits` is true and the method proceeds exactly as today. Every existing
  `AgentSessionLaunchFailureTests` interactive test runs on a `ClaudeCode` session row (`:679`)
  and is untouched.
- **`AgentSessionLaunchFailureTests` card path:** `StartCardSessionAsync` (`:755`) and
  `LaunchSpec` (`:762`) gain a `AgentKind kind = AgentKind.Raw` parameter; only
  `Card_work_prompt_delivery_failure_still_fails_the_launch` (`:324`) passes `ClaudeCode`. The
  other four callers (`:147`, `:520`, `:547`, `:567`) keep Raw and keep their semantics
  (Raw is what makes those sessions deliver blind, which some of them rely on).
- **Internal restarts** (supervisor, channel bridge, check interpreter, project setup): all pass
  `RemoteControl: null`; a Grok row with a stale `true` now logs one Warning per start instead of
  typing a command. No path that used to succeed now fails.
- **`AgentAddWorkModal` on a Grok agent:** the *server* change alone would turn every Add-Work
  into a 409 (the modal defaults `remoteControl: true`). The client change in §2.4 ships in the
  same slice — do not merge the server half without it.
- **Existing rows:** a pre-card Grok/Codex agent with `RemoteControlEnabled = true` (none known
  on this machine — HGP-Orchestrator-Grok is `false`) is not rewritten by this card. It is
  harmless at start (ignore + warn), self-heals on its first settings save, and any *unrelated*
  PATCH that omits `remoteControlEnabled` gets the 409 with the fix in the message. See §5 D-A.
- **`ContractSnapshotTests` (E2E):** no DTO shape changes — the gate adds no fields. Nothing to
  re-snapshot.
- **`SessionHealthService`:** unchanged behaviour whether or not the optional tidy in §2.1 lands.

## 4. Tests

Server (`tests/Antiphon.Tests`, TUnit; run chunked by namespace per AGENTS.md):

1. **`RemoteControlPolicyTests`** (new, `Application/`): `Permits` is true for exactly the kinds
   whose catalog row is Supported, enumerated over `Enum.GetValues<AgentKind>()` so a sixth kind is
   covered the day it is added (today: `[ClaudeCode]`); `Require(kind, wanted: false, …)` never
   throws; `Require(Grok, true, …)` throws `ConflictException` with `Code ==
   "remote_control_refused"` and a message containing `"Grok"` and `"remoteControlEnabled: false"`;
   `Permits((AgentKind)99)` is false, not an exception.
2. **`AgentTuiProfileServiceTests.Runner_catalogue_is_curated_and_truthful_without_probing`** —
   add `catalog.SupportsRemoteControl(kind)` assertions beside the existing five `remoteControl`
   state pins so the query and the rows are pinned together.
3. **`AgentRemoteControlGateTests`** (new, `Application/`, shaped like
   `AgentSessionBackendTests` `:285-310`):
   - create with a Grok profile and `RemoteControlEnabled: true` → 409 `remote_control_refused`,
     **no row** persisted;
   - create with a Grok profile and the flag omitted → 200, stored `false`;
   - PATCH a Claude agent (RC on) to a Grok profile with `RemoteControlEnabled` omitted → 409,
     row unchanged (`TuiProfileId` still the Claude one, flag still `true`);
   - same PATCH with `RemoteControlEnabled: false` → 200, stored Grok + `false`;
   - PATCH a Grok agent with `RemoteControlEnabled: true` → 409;
   - Claude create/PATCH with `true` → unchanged (`AgentCreateSupervisionTests` already pins the
     positive case; reference it rather than duplicate).
4. **`AgentControlServiceIntegrationTests`** — two new tests beside
   `Start_with_remote_control_boots_queue_head_and_sends_rename_then_remote_control_before_work`:
   - Grok-profiled agent, `StartAsync(new StartAgentRequest(RemoteControl: true))` → 409, no
     `AgentSessions` row, `agent.Status` still `Idle`, `adapter.Prompts` empty;
   - Grok-profiled agent whose row was seeded `RemoteControlEnabled = true` directly (bypassing the
     service, as a pre-card row would be), `StartAsync(new StartAgentRequest())` → launches,
     `adapter.Prompts` contains **no** `/remote-control` and **no** `/rename`, first prompt is the
     work/launch prompt.
5. **`AgentSessionLaunchFailureTests`** — fixture `kind` parameter (§3); new test
   `A_remote_control_name_on_a_non_capable_kind_types_nothing`: card session with
   `AgentKind.Grok` + `RemoteControlName: "Card Agent"` → `adapter.Prompts == ["do the work"]`,
   and **no** `RcDegraded` incident row for the agent.
6. **`CardService` spawn refusal** — in whichever card integration suite already spawns through a
   `QueueAdapterFactory` (`BoardServiceIntegrationTests` or `CardReviewServiceIntegrationTests`):
   spawn with `RemoteControlName` on a card whose resolved spec kind is Grok → 409
   `remote_control_refused`, no session row.

Client (`pwsh -File scripts/test-client.ps1`; never read a Bash pipeline's exit code):

7. **`AgentRemoteControl.test.tsx`** (new, msw, shaped like `AgentSessionBackend.test.tsx`):
   - create modal with a default profile whose `capabilities` has `remoteControl: Unsupported`
     (reason text supplied) → the switch is disabled, unchecked, its description is the reason;
     submitting sends `remoteControlEnabled: false`; swap the default profile to a Supported one →
     enabled;
   - settings modal for `agent.kind = 'Grok'`, `tuiProfileId: null`, `remoteControlEnabled: true`
     (a stale row) with `/api/agent-tui/runner-types` returning the catalog → switch disabled,
     save sends `remoteControlEnabled: false`;
   - add-work modal for a Grok agent → no "Remote control" checkbox in the DOM, the start request
     body is `{ remoteControl: false }`; for a Claude agent the checkbox is present and defaults on.

Build/verify commands for the build dispatch (alternate output path, forward slash, delete
`bin-rc/` dirs afterwards):

```
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-rc/ --treenode-filter "/*/Antiphon.Tests.Application/*/*"
dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-rc/ --treenode-filter "/*/Antiphon.Tests.AgentTui/*/*"
pwsh -File scripts/test-client.ps1
```

## 5. Decisions that are the operator's — each with a recommendation

- **D-A — Backfill pre-existing rows?** A data-only EF migration
  (`UPDATE "Agents" SET "RemoteControlEnabled" = FALSE WHERE "RemoteControlEnabled" AND "Kind" <> 1`
  — `ClaudeCode = 1`, `Domain/Enums/AgentKind.cs:6`) would clear stale flags in one step but
  hard-codes "ClaudeCode only" in SQL, needs the server stopped to author, and encodes a fact the
  catalog owns. **Recommendation: no migration.** The build brief runs
  `SELECT COUNT(*) FROM "Agents" WHERE "RemoteControlEnabled" AND "Kind" <> 1` against 17280
  first and reports the number; if it is non-zero, clear those rows with one `PATCH` each
  (`remoteControlEnabled: false`) as part of the deploy, and state which agents in the commit. The
  start-path ignore arm covers them either way.
- **D-B — Disable vs hide for the create/settings switch.** This plan says disable-with-reason
  (§2.4). Hiding is one fewer element on the form; disabling teaches the user why. Either satisfies
  the acceptance criterion. **Recommendation: disable**, for parity with the `modelArgument`
  control right above it.

Everything else in this plan is settled by the card's own text or by an existing precedent in the
codebase, cited inline.
