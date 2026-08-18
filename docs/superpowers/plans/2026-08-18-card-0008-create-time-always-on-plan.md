# CARD-0008 — Always-on (and remote control) settable at agent creation

**Status:** planned, not yet implemented (verified 2026-08-18 — `CreateAgentRequest` in
`server/Application/Dtos/AgentDtos.cs:154` still has neither field).

**Card:** every supervised agent is a create-then-PATCH two-step, with a real unsupervised window
between the two calls, and a UI flow of New Agent → Create → kebab → Edit settings → toggle → Save.

## 1. Current shape — exactly what Update has that Create lacks

`UpdateAgentRequest` fields absent from `CreateAgentRequest`, and what to do with each:

| Field | Include at create? | Why |
|---|---|---|
| `AlwaysOn` | **Yes** — the card | Supervision from birth; closes the unsupervised window. |
| `RemoteControlEnabled` | **Yes** — the card | Same category: part of "how this agent is supervised/driven", collected in the same dialog. |
| `BoardId` | No | `CreateAsync` always builds the agent its own board (find-or-create project → board → `agent.BoardId = board.Id`). A create-time board override is a different feature with its own questions (who owns the board?), not this card. |
| `SystemPromptAppend` | No | Deliberately update-only — the CARD-0060 comment on `CreateAgentRequest` says so explicitly ("SystemPromptAppend deliberately still is NOT one of them"). Leave that decision standing. |
| `BundleKeys` | No | CARD-0058 attachment editing has its own service path (`AgentBundleAttachments.SetAsync`) and its own UI (settings modal multi-select). Nothing about the two-step-window complaint applies to bundles; keep the slice small. |

Everything else composition-relevant (ModelLevel, TuiProfileId/ModelId, ReplyStyle,
AssignmentPolicy) already exists on Create.

Nullability note: on Update both fields are `bool?` because null must mean "leave unchanged" for
older callers. On Create there is nothing to leave unchanged, so plain
`bool AlwaysOn = false, bool RemoteControlEnabled = false` (matching the entity defaults) is
correct and mirrors how `ReplyStyle` was added by CARD-0060.

## 2. Server change — pure DTO + one initializer, no downstream hazards

1. **`server/Application/Dtos/AgentDtos.cs`** — append to `CreateAgentRequest`:
   ```csharp
   // CARD-0008: supervision is part of the agent's identity, not an afterthought — an agent
   // meant to be always-on must never exist unsupervised between a create and a PATCH.
   bool AlwaysOn = false,
   bool RemoteControlEnabled = false
   ```
2. **`server/Application/Services/AgentService.cs` `CreateAsync` (~line 236)** — add to the
   `new Agent { ... }` initializer:
   ```csharp
   AlwaysOn = request.AlwaysOn,
   RemoteControlEnabled = request.RemoteControlEnabled,
   ```

That is the whole server change. Evidence there is no downstream assumption that these are
PATCH-only:

- **Existence proof:** `CheckInterpreterProvisioner` (CARD-0047) already constructs an `Agent`
  entity directly with `AlwaysOn = true` at birth, bypassing the DTO entirely, and its docstring
  leans on the supervisor picking it up ("the existing sweep already ensures every AlwaysOn agent
  … has a live session"). A creation-born always-on agent is already running in production.
- **Supervisor:** `AgentSupervisorService.TickAsync` queries `_db.Agents.Where(a => a.AlwaysOn)` —
  no join to sessions, no dependence on how the flag got set. A session-less agent falls through
  `FindPersistentSessionAsync` → "not running" → restart scheduled at `Backoff(0)` (5s) →
  `_control.StartAsync(agent.Id, new StartAgentRequest(Fresh: …))`. Startup-at-boot for a
  never-started agent is an explicitly documented supervisor behavior, not an accident.
- **Remote control:** `StartAgentRequest.RemoteControl = null` means "use the agent's persisted
  `RemoteControlEnabled`" on every start path, so a create-time value is honored by the very first
  launch, supervised or manual.
- **No audit/event trail expects an UPDATE record:** the only events are `AgentChanged`/
  `BoardChanged` publishes, which `CreateAsync` already fires. Supervision state
  (`AgentSupervisionState`) is created lazily by `GetOrCreateStateAsync` on the first tick.
- The endpoint (`AgentEndpoints.cs` MapPost) is a pure DTO pass-through; nothing to change.
- `DraftAgentResponse` (the AI-draft prefill) does not carry these fields and should not — a
  draft guessing "always on" would be a surprise auto-start. Out of scope.

## 3. Client — include the dialog fields in this slice (recommended)

**Recommendation: do server + client together as one slice.** The card's complaint is explicitly
the UI two-step (New Agent → Create → kebab → Edit settings → toggle → Save); a server-only fix
closes the scripted-provisioning half but leaves the UI flow — the half the card actually
describes — untouched. The client change is two `Switch`es copied from an existing modal; splitting
it out buys nothing.

1. **`client/src/api/agents.ts`** — add to the `CreateAgentRequest` interface:
   ```ts
   /** Supervised from birth: auto-started at boot, auto-restarted on crash. */
   alwaysOn?: boolean
   remoteControlEnabled?: boolean
   ```
2. **`client/src/features/agents/AgentCreateModal.tsx`** — two `useState(false)` hooks, reset in
   `reset()`, passed in `createAgent.mutate({...})`, rendered as the same two Mantine `Switch`es
   `AgentSettingsModal.tsx` (~line 229) already has — copy the labels and descriptions verbatim
   ("Always on" / "Auto-start at boot and auto-restart on crash…", "Remote control" / "Every start
   arms /remote-control…") so the two dialogs read as one feature. Place them after the Reply
   style control, before the buttons.

## 4. Validation / ordering — nothing to gate, one behavior to be deliberate about

- **No precondition exists.** Always-on does NOT require a `PersistentSessionId` or prior session:
  the supervisor's whole boot behavior is "AlwaysOn with no live session ⇒ schedule a start". A
  create-time `AlwaysOn = true` therefore means **the agent will auto-start itself within one
  supervisor tick + ~5s of creation**, with no session having been requested by the operator.
  That is the correct, intended semantic (it is exactly what the card asks for), but the Switch
  description in the create dialog already says "auto-start at boot" — keep it, so the operator
  is told the agent will come up on its own.
- The first supervised start of a never-started agent goes through the same
  `StartAsync(Fresh: …)` path as any recovery start; `AgentStartRecoveryTests` /
  `AgentSupervisionTests` already cover it.
- No new validation: both fields are plain bools, no invalid combination exists
  (`RemoteControlEnabled` without `AlwaysOn` is a normal manual-start agent that arms
  /remote-control, already expressible today via PATCH).

## 5. Tests

Follow the CARD-0060 (`AgentReplyStyleTests`) precedent — it is the last field added to Create:

- **Server (`tests/Antiphon.Tests`):** in `AgentServiceIntegrationTests` (or a small new fixture),
  create with `AlwaysOn = true, RemoteControlEnabled = true` ⇒ returned `AgentDetailDto` and the
  stored row carry both; create with defaults ⇒ both false (the older-caller shape: an omitted
  JSON property binds to the default). A supervisor-pickup test (create AlwaysOn ⇒ next
  `TickAsync` schedules a start) is optional — `AgentSupervisionTests` already pins
  "AlwaysOn agent with no session gets a start scheduled" independent of how the flag was set;
  don't duplicate it, and remember the shared-Postgres rule (scope any new assertion to the
  agent the test made, `[NotInParallel]` with no group key if it drives a global sweep).
- **Client:** extend `AgentsPage.test.tsx` / add `AgentCreateModal` coverage: toggles render,
  default off, toggling them puts `alwaysOn: true` / `remoteControlEnabled: true` in the POST
  body (same pattern as the reply-style create test in `AgentReplyStyle.test.tsx`).

## 6. Slices

Small enough for one commit each, or one combined:

1. `feat(agents): CARD-0008 — CreateAgentRequest accepts AlwaysOn/RemoteControlEnabled`
   (DTO + `CreateAsync` + server tests).
2. `feat(agents): CARD-0008 — New Agent dialog collects always-on and remote control`
   (TS interface + modal + client tests).

No migration (columns exist), no event/audit changes, no supervisor changes.
