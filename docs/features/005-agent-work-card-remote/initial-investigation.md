# Feature 005 — Remote control on the Add Card screen: why is it there, and where else does it live?

**Status:** investigation complete — direction decided, implementation for review
**Date:** 2026-08-01
**Trigger:** "Why is the remote control toggle on the add card screen? And is it on the edit/add agent screen?"
**Related:** [004 — Agents screen "Working" meaning](../004-agent-screen-working-meaning/initial-investigation.md)

> **Decision: we want to remove the per-launch override.** Remote control becomes a per-agent
> setting only. See [Decision](#decision) below.

---

## TL;DR

The Add Card modal is a **compound action** — create card → queue on agent → **start the agent** —
and the remote-control checkbox is attached to that third step. It was added on 2026-06-08, roughly
six weeks *before* remote control got a persisted home on the agent (2026-07-20). When the persisted
setting arrived, every other start path was reconciled to defer to it. Add Card was not.

The result is a live bug: **Add Card hardcodes `remoteControl: true` and silently overrides an
agent's persisted "remote control off" setting.**

Separately: the toggle **is** on the Edit Agent screen, and **is not** on the Add Agent screen.

**We are removing the per-launch override entirely** — not just fixing its default. Remote control
is a property of the agent, set where the agent is defined, and every start reads it.

The removal also pins down three cross-cutting API rules (see [API hygiene](#api-hygiene-cross-cutting)):
removals are real removals, unmatched routes get logged, and unknown request properties are rejected
with an actionable message rather than silently dropped. Probing the live server showed the current
behaviour is worse than assumed on both of the latter two.

---

## Why it's on the Add Card screen

`AgentAddWorkModal.handleSubmit` (`client/src/features/agents/AgentAddWorkModal.tsx:50-99`) chains
three mutations:

1. `createCard` — create the card on the chosen board
2. `assignCard` — queue it on the agent
3. `startAgent` — **boot the agent process** (no-op if already running)

The checkbox belongs to step 3, not to the card. Nothing about a card has a remote-control notion —
`BoardPage.tsx:399` creates cards with the same `useCreateCard` hook and has no such toggle, because
it never starts anything.

### The history

| Commit | Date | What |
|---|---|---|
| `7dd825e` | 2026-06-08 | "Add Card creates a new piece of work" — the modal is born |
| `6fc3ecf` | 2026-06-08 | "start agent process with optional remote-control monitoring" — **checkbox added here** |
| `f375b58` | 2026-07-20 | supervision UI/API — **`Agent.RemoteControlEnabled` persisted setting introduced** |
| `31ce1dd` | 2026-07-31 | default board preselected in Add Work |

The checkbox predates the persisted setting by ~6 weeks. At the time it was written, remote control
was a per-start decision with nowhere to live, and Add Card was the screen that started agents — so
a per-launch checkbox there was the only way to express the choice. It is a fossil of that era.

---

## Where the toggle lives today

| Screen | Component | Remote control? | Notes |
|---|---|---|---|
| **Add Card / Add Work** | `AgentAddWorkModal.tsx:138-143` | ✅ `Checkbox` | Per-launch, **hardcoded to `true`** on open |
| **Edit agent (Settings)** | `AgentSettingsModal.tsx:163-168` | ✅ `Switch` | Persisted `remoteControlEnabled`, the real home |
| **Add agent (Create)** | `AgentCreateModal.tsx` | ❌ **absent** | No field; `CreateAgentRequest` (`AgentDtos.cs:97-105`) has no such parameter |

So the setting can be *overridden* on a screen about cards, *configured* on the settings screen, and
**not chosen at all** on the screen where the agent is created. New agents land on
`bool RemoteControlEnabled` default `false` (`server/Domain/Entities/Agent.cs:30`) and the user must
reopen Settings to turn it on.

Note `alwaysOn` has the identical gap — present in Settings (`AgentSettingsModal.tsx:157-162`),
absent from Create. Worth fixing together.

---

## The bug

`server/Application/Services/AgentControlService.cs:76`:

```csharp
var remoteControl = request.RemoteControl ?? agent.RemoteControlEnabled;
```

Null means "use the persisted setting" — documented on the DTO (`AgentDtos.cs:135-137`):

> `RemoteControl: null = use the agent's persisted RemoteControlEnabled setting (the normal case);
> true/false override for this start only.`

Add Card never sends null. It initialises `useState(true)` (`AgentAddWorkModal.tsx:26`) and re-sets
`true` on every open (`:34`), then passes `{ remoteControl }` explicitly (`:65`).

**Consequence:** adding work to an agent whose remote control is deliberately off silently arms
`/remote-control` for that launch — renaming the session and exposing it on claude.ai — unless the
user notices and unticks a box on a card-creation dialog.

### Every other start path was reconciled

| Caller | Sends | Correct? |
|---|---|---|
| Start button — `AgentsPage.tsx:244-246` | `{}` (omits the flag) | ✅ carries an explicit comment: "Remote control comes from the agent's persisted setting" |
| CLI modal — `AgentsPage.tsx:326` → `AgentCliModal.tsx:35` | `agent.remoteControlEnabled` | ✅ equivalent to omitting |
| **Add Card** — `AgentAddWorkModal.tsx:65` | **hardcoded `true`** | ❌ overrides the persisted setting |

This is a straightforward missed migration, not a deliberate difference — the Start button was
updated in the same area and left a comment saying why.

### The test pins the stale behaviour

`client/src/features/agents/AgentsPage.test.tsx:462-463`:

```ts
// Remote control is on by default, so the booted agent should be put into remote control.
await waitFor(() => expect(startSpy).toHaveBeenCalledWith({ remoteControl: true }))
```

Compare the sibling tests at `:498-499` (`{}`) and `:520-522` (persisted value) which both assert the
*correct* contract. So the suite currently encodes both rules at once and will not catch a change to
the persisted default.

---

## Decision

**Remove the per-launch override.** Remote control is a per-agent setting; nothing overrides it at
start time.

### What changes

1. **Delete the checkbox from Add Card.** `AgentAddWorkModal` drops the `remoteControl` state, the
   reset, the `Checkbox`, and the conditional notification text; step 3 becomes
   `startAgent.mutate({})`, matching the Start button.
2. **Delete the CLI modal's redundant pass-through.** `AgentCliModal` currently forwards
   `agent.remoteControlEnabled` (`AgentsPage.tsx:326` → `AgentCliModal.tsx:35`). Behaviourally
   equivalent to omitting it, but it is the same override plumbing — drop the prop, and with it the
   "(remote control on)" suffix at `AgentCliModal.tsx:91` (or source that text from the agent
   directly).
3. **Remove `RemoteControl` from `StartAgentRequest`** (`AgentDtos.cs:137`) and collapse
   `AgentControlService.cs:76` to `var remoteControl = agent.RemoteControlEnabled;`. This is the
   change that makes the removal real rather than cosmetic — while the field exists, a future
   caller can reintroduce the bug. The removed field is then **actively rejected**, not ignored —
   see [API hygiene](#api-hygiene-cross-cutting).
4. **Add `remoteControlEnabled` and `alwaysOn` to the create-agent screen**
   (`AgentCreateModal.tsx`, `client/src/api/agents.ts`, `CreateAgentRequest` at
   `AgentDtos.cs:97-105`, `AgentService.CreateAsync` at `:167`), so the setting is chosen once,
   where the agent is defined, instead of only being reachable by reopening Settings afterwards.

### Removing the DTO field is safe — no production caller uses it

Full audit of `StartAgentRequest` construction:

| Caller | Passes | Affected? |
|---|---|---|
| `AgentSupervisorService.cs:200` | `new StartAgentRequest(Fresh: fresh)` | No |
| `ChannelBridgeService.cs:359` | `new StartAgentRequest()` | No |
| `AgentEndpoints.cs:107` | model-bound from the HTTP body | Only via the two client callers above |
| `AgentControlServiceIntegrationTests.cs:53` | `RemoteControl: true` | Test — set the fixture's `RemoteControlEnabled` instead |
| `AgentSystemPromptLaunchTests.cs:305` | `RemoteControl: false` | Test — as above |
| `SessionMessageQueueDeliveryVerificationTests.cs:222` | `RemoteControl: false` | Test — as above |

Only tests use the override for determinism, and each has a direct replacement (set the flag on the
agent fixture). No supervisor, bridge, or channel path depends on it.

### Considered and rejected

- **Fix the default only** — initialise the Add Card checkbox from `agent.remoteControlEnabled`
  rather than `true`. Smaller diff and it fixes the immediate bug, but it keeps a per-agent setting
  editable from a card dialog where a change *looks* like it might persist and doesn't. Rejected:
  we want one home for this setting, not two.
- **Do nothing.** The current behaviour contradicts an explicitly documented DTO contract
  (`AgentDtos.cs:135-137`) and silently overrides a setting the user set deliberately.

### Known trade-off, accepted

There is no longer any way to monitor a single piece of work on a normally-unmonitored agent without
first toggling the agent's setting in Settings (and toggling it back afterwards). We are accepting
that: the setting is two clicks away, and the ambiguity of a half-persisted toggle costs more than
the convenience is worth.

---

## Cost / risk

Small and contained. Removing the override:

- `client/src/features/agents/AgentAddWorkModal.tsx` — delete the `Checkbox`, the `remoteControl`
  state and reset; simplify the notification text at `:70`
- `client/src/features/agents/AgentCliModal.tsx` — drop the `remoteControl` prop and the `:91` suffix
- `client/src/features/agents/AgentsPage.tsx:326` — stop passing it
- `server/Application/Dtos/AgentDtos.cs:137` — drop the field;
  `AgentControlService.cs:76` — read the agent's flag directly
- Tests: `AgentsPage.test.tsx:462-463` → expect `{}`; `:520-522` → expect `{ fresh: false }`; the
  three server tests switch to setting `RemoteControlEnabled` on the agent fixture

Closing the create-screen gap:

- `AgentCreateModal.tsx`, `client/src/api/agents.ts`, `AgentDtos.cs:97-105`,
  `AgentService.CreateAsync` (`:167`) — two new optional bools defaulting to today's values, so
  existing API callers are unaffected

No migration needed; the `RemoteControlEnabled`/`AlwaysOn` columns already exist
(`20260720194858_AddAgentSupervision`).

**Main risk:** removing a public API field. `POST /api/agents/{id}/start` currently accepts
`remoteControl` in the body. After this change it is **rejected with a 400**, not ignored — see
below. A caller still sending it gets told exactly what to do instead, which is the point.

---

## API hygiene (cross-cutting)

The `remoteControl` removal surfaced three habits we want as project-wide rules, not one-offs.
They are recorded here because this change is the first to apply them; they likely deserve
promoting to an ADR once settled.

### 1. No deprecated endpoints or fields kept around

When something is removed, it is **removed** — no shim route, no accepted-but-ignored property, no
"soft deprecation" window. This is a single-deployment app with a first-party client; there is no
external consumer to protect, and a retained-but-dead endpoint is a trap that outlives everyone's
memory of why it exists.

### 2. Unmatched routes must be logged

**Current behaviour is worse than assumed.** Probing the running dev server:

```
GET http://localhost:17202/api/does-not-exist
→ 404, Content-Length: 0, no Content-Type, no body, NO LOG LINE
```

`ExceptionMiddleware` never sees it — nothing was thrown, routing simply matched nothing. So a
client calling a removed or misspelled endpoint gets a silent, bodyless 404 and the server records
nothing. There is no way to discover, from the server side, that anyone is hitting a dead path.

Worse, this is **environment-dependent**: `Program.cs:355` registers
`app.MapFallbackToFile("index.html")`. In dev, `wwwroot` is empty (the client runs on Vite :17203),
so the fallback can't serve and the request 404s. In a production build with `wwwroot` populated,
an unknown `/api/...` path would instead return **`index.html` with status 200** — a client would
receive HTML where it expected JSON, and the failure would surface as a JSON parse error far from
the cause.

**Proposed:**

- A terminal middleware registered after routing that fires when `context.GetEndpoint() is null`,
  logs at **Warning** with method, path, correlation id, and `User-Agent`, and returns an RFC 9457
  problem-details 404 with a body — consistent with every other error the API emits.
- Scope the SPA fallback so it **cannot** swallow `/api/*` or `/hubs/*`. Either constrain
  `MapFallbackToFile` or short-circuit those prefixes before it. This removes the
  200-HTML-for-missing-API failure mode entirely.

### 3. Unknown request properties must be rejected, with a useful message

**Current behaviour, probed live.** `POST /api/agents/{id}/queue` binds
`AssignAgentCardRequest(Guid CardId)`. Sending a body with *no valid properties at all*:

```
POST /api/agents/1b1ce2b6-.../queue   {"remoteControl":true,"bogusField":123}
→ 404 "Card with id '00000000-0000-0000-0000-000000000000' was not found."
```

Both properties were silently discarded, `CardId` defaulted to `Guid.Empty`, and the request ran all
the way into the service layer before failing with a **misleading 404 about a card the caller never
mentioned**. That is precisely the class of bug that costs an afternoon to trace back to a typo.

`Program.cs:114` configures `ConfigureHttpJsonOptions` with only the enum converter;
`UnmappedMemberHandling` is unset, so System.Text.Json ignores unknown members by default.

**Proposed:**

```csharp
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
});
```

Two follow-on pieces are needed to make this actually helpful rather than merely strict:

- **Map the resulting failure to a 400.** Minimal APIs wrap a body-binding `JsonException` in
  `BadHttpRequestException`, which is **not** an `HttpException` — so `ExceptionMiddleware.cs:42`
  would classify it as a 500. Add a case that honours `BadHttpRequestException.StatusCode`.
- **Attach guidance for retired properties.** The raw System.Text.Json message names the property
  (`The JSON property 'remoteControl' could not be mapped to any .NET member...`) but offers no
  course of action. A small registry of retired property names → guidance lets the 400 say
  something a human can act on:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "'remoteControl' is not a valid property of StartAgentRequest.",
  "errors": {
    "remoteControl": [
      "Removed 2026-08-01. Remote control is an agent-level setting, not a per-launch or per-card one. Set it once via PATCH /api/agents/{id} with 'remoteControlEnabled', or in the UI under Agent Settings → Remote control."
    ]
  },
  "traceId": "0HNNFBFJ8PH3P:00000001"
}
```

`ExceptionMiddleware` already emits a structured `errors` dictionary for `ValidationException`
(`:67-70`), so the shape exists — this reuses it.

**Also worth fixing while here:** required properties are not required. `CardId` silently defaulting
to `Guid.Empty` is the same failure wearing a different hat. Marking non-optional DTO members
`required` (or validating them at the endpoint) turns that 404 into a 400 that names the missing
field.

---

## Open questions for review

1. **Should Add Card start the agent at all?** It was the only reason the toggle existed. A modal
   named "Add work" that boots a process is doing two jobs — but the auto-start is presumably the
   point of the button, so this is likely "leave it".
2. **Bundle the `alwaysOn` create-screen gap into the same change?** Same omission, same fix —
   assumed yes above; split it out if the diff wants to stay narrow.
3. **Is `UnmappedMemberHandling.Disallow` safe to switch on globally?** It applies to every endpoint
   at once. If any current client sends extra properties anywhere, those calls start 400-ing on
   deploy. Worth a grep of the client's request builders, or a staged rollout via the per-type
   `[JsonUnmappedMemberHandling]` attribute before going global.
4. **Should the API-hygiene rules become their own ADR?** They are cross-cutting and outlive this
   feature.

---

## References

- `client/src/features/agents/AgentAddWorkModal.tsx:26,34,50-99,65,138-143`
- `client/src/features/agents/AgentSettingsModal.tsx:37,52,87,157-168`
- `client/src/features/agents/AgentCreateModal.tsx` (no remote-control field)
- `client/src/features/agents/AgentCliModal.tsx:15,25,35`
- `client/src/features/agents/AgentsPage.tsx:244-246,268-270,326`
- `client/src/features/agents/AgentsPage.test.tsx:462-463,498-499,520-522`
- `client/src/features/board/BoardPage.tsx:399` (card create with no toggle — the contrast case)
- `server/Application/Services/AgentControlService.cs:76`
- `server/Application/Dtos/AgentDtos.cs:97-105,127,135-137`
- `server/Domain/Entities/Agent.cs:30`
- `server/Application/Services/SessionHealthService.cs:131` (RC watch reads the persisted flag)
- `server/Program.cs:114-117` (JSON options), `:283-287` (middleware order), `:355` (SPA fallback)
- `server/Api/Middleware/ExceptionMiddleware.cs:42` (status mapping), `:55-70` (problem details + `errors`)
- `server/Api/Endpoints/AgentEndpoints.cs:107` (start endpoint body binding)
- `server/Application/Services/AgentSupervisorService.cs:200`, `ChannelBridgeService.cs:359` (start callers)
- Commits: `7dd825e`, `6fc3ecf` (2026-06-08), `f375b58` (2026-07-20), `31ce1dd` (2026-07-31)
