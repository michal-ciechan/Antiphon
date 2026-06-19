# Feature 003 — WhatsApp Integration (remote agent notifications + control)

> **Status:** Backlog — not started. Captured 2026-06-19 for future development so
> current work can focus elsewhere. This is an input/idea doc, not a committed plan;
> a provider decision (§4) and a reconciliation pass are needed before any code.

---

## 1. Goal

Use WhatsApp as a **remote notification + control channel** for Antiphon agents, so the
user can — from their phone, away from the dashboard:

1. **Be alerted** when an agent **finishes** what it was asked (and optionally when it
   stalls / appears to need input).
2. **Reply** to that WhatsApp message to **send or queue a message** back to the
   originating agent session (e.g. "looks good, now write the tests", or "stop").

It is the mobile surface for the notification + message-queue features that already exist
in the app (see §3).

## 2. Why now / why it's cheap to build

This builds directly on two already-shipped pieces, so most of the server plumbing exists:

- **Finished signal** — `SessionFinished` is broadcast when an agent reaches an `end_turn`
  with an empty queue (badge + toast + desktop notification). Server emit point:
  `SessionMessageQueueService.PublishFinishedAsync` (commit `fa3b9e9`).
- **Message queue** — `SessionMessageQueueService.EnqueueAsync(sessionId, body, mode)` with
  `mode = Now | WhenIdle`, exposed at `POST /api/sessions/{id}/messages` (commit `fa3b9e9`).
  This is exactly the API an inbound WhatsApp reply would call.

So WhatsApp = **outbound:** fan the finished/stall signal out to a new channel; **inbound:**
route a reply into the existing enqueue API. No new agent/turn logic required.

## 3. Scope (MVP)

- **Outbound notify on finish.** On `SessionFinished`, send a WhatsApp message to the
  configured number: agent/card label + a short summary (the turn's final assistant text,
  already available from the transcript). Optional later: notify on watchdog stall / "needs
  input".
- **Inbound reply → agent.** A reply in that WhatsApp thread is delivered to the originating
  session via `EnqueueAsync`. Default `WhenIdle`; a prefix (e.g. `!` or `now:`) forces `Now`.
- **Reply → session correlation.** Needed when more than one agent is active. Options:
  - Reply-to-thread / message-id correlation (cleanest if the provider supports it).
  - Short code prefix per agent in the outbound message (e.g. `[a1] Torquay Leander finished…`;
    reply `a1 run the tests`).
  - Fallback: "most recently notified agent for this number".

## 4. Provider options (DECISION NEEDED)

| Option | Official? | Cost | Setup | Notes |
|---|---|---|---|---|
| **Meta WhatsApp Cloud API** | Yes | Free tier + per-conversation pricing | Business account, registered number, public webhook | Durable/shareable; most setup |
| **Twilio WhatsApp API** | Yes (via Twilio) | Paid per message; free sandbox | Easiest onboarding, sandbox for dev | Good middle ground |
| **whatsapp-web.js / Baileys** | No (unofficial) | Free | Link a personal WhatsApp via headless session | Fastest for a personal tool; **ToS risk**, session can drop |

**Lean:** for a personal/solo tool, `whatsapp-web.js`/Baileys prototypes fastest; for anything
shared or long-lived, Meta Cloud API. Decide before building.

## 5. Architecture sketch

**Outbound**
- Introduce a server-side notification fan-out (e.g. `INotificationChannel`) so the finished/
  stall signal isn't hard-wired to SignalR. The in-app toast stays client-side; a
  `WhatsAppNotificationChannel` sends the message. Hook it at the existing emit point
  (`SessionMessageQueueService.PublishFinishedAsync`) or have it subscribe to the event bus.
- Payload already computed there: `sessionId`, `cardId`, `agentId`, `label`. Add the final
  assistant text (last `AssistantText` before the `end_turn`) for the summary.

**Inbound**
- New endpoint `POST /api/integrations/whatsapp/webhook`:
  - Verify provider signature; **allow-list** the sender number; rate-limit.
  - Resolve target `sessionId` (§3 correlation).
  - Parse mode prefix → `EnqueueAsync(sessionId, body, mode)`.

**Config & secrets**
- Provider creds + the user's WhatsApp number in config/secret store (Bitwarden skill / secret
  manager — not committed).
- Per-project or per-agent routing later; single number for MVP.

**Public webhook URL**
- The dev machines already front local services over HTTPS via Caddy + Cloudflare
  (`*.<machine>.codeperf.net`, the `proxy` skill). A webhook route can reuse that — no new infra.

## 6. Open questions

- Provider choice (§4) — cost vs ToS vs setup.
- Reply→session correlation strategy when multiple agents are live.
- Notify only on **finish**, or also on **stall / needs-input** (watchdog) and **failed**?
- Multi-recipient / per-project routing, and who is allowed to control which agents.
- Throttling: don't spam on every `end_turn` if the user is rapidly queueing.

## 7. Integration points (existing code)

- Outbound trigger: `server/Application/Services/SessionMessageQueueService.cs`
  → `PublishFinishedAsync` (emits `SessionFinished`).
- Inbound delivery: `SessionMessageQueueService.EnqueueAsync(sessionId, body, mode)`.
- Client equivalent already done for in-app: `client/src/hooks/useSessionFinishedToasts.ts`.
- Session → label/card/agent resolution: already in `PublishFinishedAsync`.

## 8. Effort & non-goals

- **Effort:** outbound-only MVP ≈ **S**; full two-way (webhook + correlation + config) ≈ **M**.
- **Non-goals (for now):** rich media/voice, group chats, full conversation mirroring,
  multi-tenant routing.
