# CARD-0107: Slack as a channel (Kafka-backed, same shape as Telegram) — plan

**Date:** 2026-08-20
**Status:** planned (task c1d11bdb)
**Card:** CARD-0107 · **Related:** CARD-0106 (per-agent API keys — evaluated §4, does NOT gate this card)
**Template files:** `src/Antiphon.Messaging.Telegram/TelegramChannelAdapter.cs` (the sibling),
`docs/telegram-bot-ops.md` / `docs/messaging-standalone.md` (the ops shape)

## Verdict up front

Slack lands as **one new gateway project (`Antiphon.Messaging.Slack`) plus a preamble-preset
refactor** — the server needs essentially nothing else. The card's architecture investigation held
up under re-reading every cited file: `ChannelBridgeService` keys everything on
`{channel.Provider}:{conversationId}` (`ChannelBridgeService.cs:186`), `ChannelReplyDispatcher`
builds outbound replies as `Channel = target.Provider` (`ChannelReplyDispatcher.cs:336`), the
prompt envelope already capitalizes `channel.Provider` generically (`ChannelPromptFormat.cs:40`),
and `ChatChannel.Provider` is an open string (`ChatChannel.cs:15-16`). The **only** server-side
Telegram special case is the preamble preset at `AgentEndpoints.cs:48`, which S3 makes
provider-keyed instead of adding a second branch.

The seven decisions asked of this plan:

1. **Socket Mode**, not the Events API (§2). The gateway has no public HTTPS ingress and Telegram's
   adapter is long-polling; Socket Mode is the same outbound-only shape and reuses the existing
   ingress pump unmodified.
2. **Internal single-workspace bot app** with 7 bot scopes + 1 app-level scope, created from a
   manifest shipped in the ops doc (§3).
3. **Bespoke `Slack__BotToken`/`Slack__AppToken` env config now**, matching `Telegram__BotToken`.
   CARD-0106 does not apply: it resolves keys into `AgentLaunchSpec.Env` on the desktop server;
   the gateway is a self-contained deployment on server2 with its own Postgres/Kafka and no path
   to the server's DB (§4). Not blocked on CARD-0106.
4. **A thread stays inside its parent channel's conversation**; `thread_ts` rides in `ReplyHandle`
   (and `Raw`), so replies land in the thread of the message they answer — with one documented
   latest-wins limitation inherited from `ChatChannel.ReplyHandle` (§5).
5. Adapter sketch in §6 — Socket Mode receive loop, `chat.postMessage` send, a
   `SlackMrkdwnRenderer` sibling to `TelegramMarkdownRenderer`, external-upload flow for files.
6. **Same `am-service` process and image**, with adapter registration made conditional on config
   presence, plus a new `docs/slack-bot-ops.md` (§7).
7. `AgentEndpoints.cs:48` becomes a provider-keyed lookup over `ChannelPreamble` presets — the
   cheap genuinely-agnostic version, not a second special case (§8).

Estimated total: **~3–4 days** across four slices plus an operator setup task; S1 (the adapter) is
the critical path.

## 1. What was verified against the actual files (no surprises found)

Read in full or at the cited members: `IChannelAdapter.cs`, `ChannelMessage.cs`, `ChannelReply.cs`,
`ChannelCapabilities.cs`, `TelegramChannelAdapter.cs`, `TelegramSettings.cs`,
`Antiphon.Messaging.Service/Program.cs`, `TelegramIngressService.cs`, `OutboundConsumerService.cs`,
`ChannelPromptFormat.cs`, `ChannelPreamble.cs`, `ChatChannel.cs`, `ChannelBridgeSettings.cs`,
`AgentEndpoints.cs:41-51`, dispatcher/bridge provider-handling lines, the service README, and
`docs/telegram-bot-ops.md` / `docs/messaging-standalone.md`. Everything the card claims checks out.
Facts the plan leans on beyond the card's list:

- **The self-message guard already exists server-side**: `ChannelBridgeService.cs:97` drops
  `message.Author.IsSelf`. This matters more for Slack than it ever did for Telegram — Telegram's
  `getUpdates` never returns the bot's own sends, but **Slack delivers the bot's own
  `chat.postMessage` back as a `message` event**. A mis-set `IsSelf` is an infinite reply loop
  (agent reply → event → inbound → routed to agent → reply …). §6 makes the adapter drop own
  messages itself as well (belt-and-braces, same pattern as Telegram's allowlist drop at
  `TelegramChannelAdapter.cs:268`).
- **Reply targeting is resolved at dispatch time from the channel row**: `ChannelReplyDispatcher`
  looks up `ChatChannel.ReplyHandle` by `(Provider, ExternalId)` parsed out of the queued message's
  `ConversationKey` (`ChannelReplyDispatcher.cs:277-287`, the CARD-0067 design). `ReplyHandle` on
  the row is "**latest** opaque reply-routing token" (`ChatChannel.cs:38-39`) — this is what makes
  the threading decision in §5 have a known limitation rather than being free.
- **Outbound truncation is one global knob**, `ChannelBridge:MaxReplyChars = 4000`
  (`ChannelBridgeSettings.cs:22`). Slack's hard cap on `chat.postMessage` text is far higher
  (40k), so the existing 4000 stands unchanged and matches the preamble's "4000 characters,
  phone-sized" contract — that contract is Antiphon's, not Telegram's.
- **The ingress pump is already multi-adapter**: `TelegramIngressService.ExecuteAsync` is
  `Task.WhenAll(adapters.Select(PumpAsync))` (`TelegramIngressService.cs:23-24`) with per-adapter
  restart/backoff and never-die-silently logging. Only its NAME is Telegram-specific.
- **Per-instance deployment model**: one service instance per bot, self-contained token + Kafka +
  Postgres (`docs/messaging-standalone.md:3-6`, service README). The family instance on server2 is
  rebuilt by tar-syncing `src/Antiphon.Messaging*` + `Messaging.Pack.props` and
  `docker compose build` (`docs/telegram-bot-ops.md:22-31`).
- **Test template**: `tests/Antiphon.Messaging.Tests` has `FakeTelegramServer` (in-process fake of
  exactly the endpoints the adapter calls, wire-shape-faithful), `TelegramChannelAdapterTests`,
  `TelegramResilienceTests` (timeout-OCE / hang / fault-injection — the 2026-07-31 lesson class),
  and separate conformance tests against real Telegram.

## 2. Decision: Socket Mode, not the Events API

**Recommendation: Socket Mode.** Reasoning, grounded in how this deployment is actually reachable:

- `am-service` is **not a public HTTPS receiver and nothing in this repo makes it one**. Its own
  HTTP surface (`/health`, `/api/channels/...` in `Program.cs`) is compose-internal; the README and
  `messaging-standalone.md` configure instances purely via env vars, with no ingress/route/TLS
  anywhere. The Telegram adapter is long-polling and actively **deletes** any webhook at startup
  (`TelegramChannelAdapter.cs:63-64`) — the whole gateway is built outbound-only.
- server2 is reached via a Cloudflare SSH tunnel; a Traefik instance fronts other services there,
  but no public route exists for `am-service` and provisioning one is real new work: public
  DNS + TLS route, Slack **signing-secret verification** on every request, the 3-second ack
  contract, `x-slack-retry-num` dedup, and a permanent public attack surface on the box. (The
  Caddy/Cloudflare note in `docs/features/003-whatsapp-integration.md:82-84` is about the **dev
  machines**, not server2's gateway — it is precedent that a webhook *could* be done someday, not
  that it is set up.)
- Socket Mode is architecturally the **same shape as the existing long-poll**: the app opens an
  outbound WebSocket (`apps.connections.open` → wss URL), Slack pushes event envelopes down it, no
  inbound port exists. It drops into `IChannelAdapter.ReceiveAsync` and the existing ingress pump
  (`PumpAsync` restart loop) without touching the host.
- Socket Mode's one real constraint — the app cannot be distributed via the public Slack App
  Directory — is irrelevant for an internal single-workspace bot.

Cost accepted: a long-lived WebSocket needs reconnect handling (Slack sends `disconnect` envelopes
asking you to refresh, and connections are periodically recycled). That is ~the same failure
surface the Telegram poll loop already handles, and `TelegramIngressService`'s outer restart loop
backstops it for free.

## 3. Decision: the Slack app, concretely

An **internal, single-workspace bot app** ("from an app manifest" flow at api.slack.com/apps). The
ops doc (S4) ships the manifest YAML so creating it is paste-and-click. Settings:

- **Socket Mode: enabled.** App-level token (`xapp-…`) with scope **`connections:write`** — this is
  the WebSocket credential, distinct from the bot token.
- **Bot user** with **bot token scopes** (`xoxb-…` after install):
  `chat:write` (post replies), `channels:history` + `groups:history` + `im:history` +
  `mpim:history` (read messages in public/private channels, DMs, group DMs the bot is in),
  `users:read` (resolve author display names for the envelope), `files:read` (download inbound
  attachments via `url_private`), `files:write` (send outbound attachments).
- **Event subscriptions (bot events):** `message.channels`, `message.groups`, `message.im`,
  `message.mpim`. **Deliberately NOT `app_mention`/`app_mentions:read`**: a mention in a channel
  arrives as BOTH a `message.channels` event and an `app_mention` event — subscribing to both
  double-delivers the same message. We take the `message.*` stream as canonical (the conversation
  model wants every message in a bound channel, exactly like Telegram groups today) and detect
  mentions by parsing `<@U…>` tokens against the bot's own user id (§6).
- The bot must be **/invite'd into each channel** it should hear — `*:history` scopes only cover
  conversations the bot is a member of. DMs work as soon as a user opens one. This is Slack's
  native containment; the adapter's `AllowedConversationIds` allowlist (§6) is the fail-closed
  layer on top, matching `Telegram__AllowedChatIds` policy.

Both tokens go in a Bitwarden item (e.g. "Antiphon Slack Bot"), the same custody as the Telegram
token ("Antiphon Telegram Bot", `docs/telegram-bot-ops.md:11`).

## 4. Decision: where the token lives — bespoke env config now; CARD-0106 does not apply

**Recommendation: `Slack__BotToken` / `Slack__AppToken` environment variables on the gateway
instance, exactly parallel to `Telegram__BotToken`. Do not wait for, and do not build against,
CARD-0106.**

Read CARD-0106 in full. It is per-agent/per-project API key management **resolved by the main
server into `AgentLaunchSpec.Env` at agent-launch time** — placeholders in agent setup, a key store
in the server's Postgres, resolution inside `AgentRegistry.Resolve`/`AgentTuiLaunchResolver`. The
Slack bot token is a different kind of thing in a different trust boundary:

- The gateway is a **self-contained deployment on server2** with its own Postgres and its own
  Kafka (`messaging-standalone.md:3-6`). It has no connection string to the server's DB and no
  HTTP path to the server at all — by design (instances of it run for other repos, e.g.
  school-revision). CARD-0106's store is physically unreachable from where this credential is
  consumed, and building a secrets-distribution channel across that boundary would be new
  infrastructure this card has no need for.
- The credential is **per-deployment-instance, not per-agent**: one bot token per gateway instance
  is the established model ("one instance per bot", bot = persona). Nothing about it varies by
  agent or project.

So there is no migrate-later note to write either — CARD-0106 landing changes nothing here unless
it someday grows an explicit "deployment secrets" scope, which its own text does not contemplate.

## 5. Decision: threading — thread stays in the parent conversation; `thread_ts` rides `ReplyHandle`

**Recommendation: a Slack thread is NOT its own conversation.** `Conversation.Id` is the Slack
conversation id (`C…` public channel, `G…` private, `D…` DM), one `ChatChannel` row per channel/DM.

Why not thread-as-conversation: `ChatChannel` rows are discovered from inbound traffic and then
**manually bound to an agent by the operator** (`docs/telegram-bot-ops.md:46-47`,
`ChatChannelService.UpdateAsync`). Per-thread rows would each sit unbound until an operator bound
them (so thread replies would silently not route), would fragment one channel's ongoing
conversation across sessions, and would bloat the channels page with a row per thread forever.
The debounce/batching model (`ChannelBridgeSettings.cs:39-48`) also assumes conversation
granularity — batching can already merge messages from different threads into one prompt, and one
turn produces one reply, so per-thread sessions buy nothing the pipeline can use.

Mechanics:

- **Inbound:** `ReplyHandle = "{channelId}|{thread_ts}"` when the message is in a thread
  (`thread_ts` present), bare `"{channelId}"` otherwise. `ReplyHandle` is exactly the "opaque token
  carrying everything an adapter needs to address a reply back" (`ChannelMessage.cs:37-38`) — no
  contract change. The full event stays in `Raw`; the thread parent maps to
  `ReplyTo`/`ReplyReference` when Slack includes it.
- **Outbound:** the adapter parses its own handle format back into `channel` + `thread_ts` on
  `chat.postMessage`. Additionally, a set `ChannelReply.ReplyToMessageId` maps to `thread_ts` —
  in Slack, "replying to a message" *is* threading onto it (message `ts` values are the thread
  keys). `RawOverrides.thread_ts` wins over both, per the existing merge-last rule
  (`TelegramChannelAdapter.cs:662-666` equivalent).
- `Capabilities.Threads = true` (first adapter to set it).

**Known limitation, documented not fixed:** `ChatChannel.ReplyHandle` stores the **latest**
inbound's handle (`ChatChannel.cs:38-39`) and `ChannelReplyDispatcher` resolves reply targets from
the channel row (`ChannelReplyDispatcher.cs:277-287`). With two threads active in one channel
simultaneously, a reply computed for thread A after a newer message arrived in thread B lands in
thread B. This is the same genre as the accepted `(Provider, ExternalId)` two-bots-one-group
collision (`docs/telegram-bot-ops.md:17-20`); the future fix is persisting the inbound's
`ReplyHandle` per `SessionQueuedMessage` row (a server schema change on the CARD-0067 surface),
deliberately out of scope here. Goes in `docs/slack-bot-ops.md` as an accepted limitation with
that named fix.

## 6. The adapter, concretely (`Antiphon.Messaging.Slack`)

New project, sibling to `Antiphon.Messaging.Telegram`, referencing only `Antiphon.Messaging` (+
`Microsoft.Extensions.*` abstractions), added to `Messaging.Pack.props`/Dockerfile context.
`ChannelKey = "slack"`.

**`SlackSettings`** (`SectionName = "Slack"`), mirroring `TelegramSettings` member-for-member where
the concept transfers: `BotToken`, `AppToken`, `ApiBaseUrl = "https://slack.com/api"`,
`AllowedConversationIds` (string[], fail-closed allowlist like `AllowedChatIds`), `BotUserId`
(optional; resolved via `auth.test` at startup when empty — also yields the workspace/bot identity
for `IsSelf`/`IsMe`), `ErrorBackoffSeconds = 3`, `MaxRetryAfterSeconds = 60`,
`SendRetryAttempts = 2`, `MaxInlineAttachmentBytes = 14 MB`, `Formatting = "Markdown"`.

**`ReceiveAsync`** — the Socket Mode loop, structured like `TelegramChannelAdapter.ReceiveAsync`
(outer while + `PollOutcome`-style pacing):

1. `POST apps.connections.open` (Bearer = app token) → `wss://` URL. Failure → log + backoff +
   retry (never die).
2. `ClientWebSocket` connect; read envelopes. **Ack every envelope (`{"envelope_id": …}`)
   immediately on receipt, before normalization** — Slack redelivers unacked envelopes, and
   at-least-once + `ChatChannel.LastChannelMessageId` dedup (`ChatChannel.cs:41-42`) already
   handles the resulting duplicates server-side, whereas slow acks cause redelivery storms.
3. Envelope types: `hello` (ignore), `disconnect` (close + reopen a fresh connection — Slack asks
   for this routinely; NOT an error), `events_api` → `TryNormalize`.
4. Every catch follows the frozen OCE rule: `catch (OperationCanceledException) when
   (ct.IsCancellationRequested)` is shutdown; **everything else — including WebSocket receive
   timeouts and `HttpClient` timeout `TaskCanceledException`s — is transient: log, backoff,
   reconnect** (the 2026-07-31 AZ Care rule, already written into the Telegram adapter at
   `TelegramChannelAdapter.cs:78-89`). `TelegramIngressService.PumpAsync` backstops it.

**`TryNormalize`** (event callback JSON → `ChannelMessage`):

- Accept `event.type == "message"` with subtype `null` or `"file_share"`. Skip other subtypes
  (`message_changed`, `message_deleted`, `bot_message`, `channel_join`, …) in v1 — Telegram's
  adapter normalizes edits as fresh messages, but Slack's `message_changed` wraps the message
  differently and is deferrable.
- **`IsSelf`: true when `bot_id` is present or `user == BotUserId`.** The bridge drops these
  (`ChannelBridgeService.cs:97`); the adapter ALSO drops its own messages before yielding
  (belt-and-braces — this is the reply-loop guard, see §1).
- Allowlist: drop events whose conversation id is not in `AllowedConversationIds` (when non-empty),
  as the Telegram adapter does at `TelegramChannelAdapter.cs:268-269`.
- `Conversation`: id = `event.channel`; `Kind` from `channel_type` (`im` → Direct, `mpim`/`group`
  → Group, `channel` → Channel/Group); `Title` via a cached `conversations.info` lookup (DMs have
  none — the envelope renders "direct message" for Direct via `ChannelPromptFormat.cs:35-37`).
- `Author`: `users.info` (cached, `users:read`) for display name/username;
  `Mentions`: parse `<@U…>` tokens from the raw text; `IsMe` when the id equals `BotUserId`.
  Normalized `Text` gets mention tokens rewritten to `@name` best-effort (unresolvable ids pass
  through), because raw `<@U0123ABCD>` in the agent's prompt is noise.
- `Attachments`: `event.files[]` → `Attachment` (Kind by mimetype, `ChannelRef` = file id, `Url` =
  `url_private`), then **hydrate inline** by fetching `url_private_download` with
  `Authorization: Bearer <bot token>` — same reason and same semantics as
  `HydrateAttachmentsAsync` (`TelegramChannelAdapter.cs:156-196`): consumers behind the bus have
  no channel credentials; size cap and any failure keep metadata-only, never lose the message.
- `ChannelMessageId` = the message `ts` (unique per channel — feeds the existing dedup);
  `ReplyHandle` per §5; `Raw` = the full event callback, cloned.

**`SendAsync`** (`ChannelReply` → Slack):

- Text: `chat.postMessage` JSON `{channel, text, thread_ts?}`; `Kind` prefixes (⏳/❓) same as
  Telegram (`TelegramChannelAdapter.cs:641-647`); `RawOverrides` merged last (reaches `blocks`,
  `unfurl_links`, `thread_ts` override, …). Retry loop mirrors `TrySendOnceAsync`: Slack returns
  `ok:false` + `error` and 429s carry `Retry-After` — honor it capped by `MaxRetryAfterSeconds`;
  bounded retries per `SendRetryAttempts` because the outbound consumer auto-commits
  (`OutboundConsumerService.cs:28` — a dropped send is a lost reply).
- Formatting: **`SlackMrkdwnRenderer`**, sibling to `TelegramMarkdownRenderer` — producers send
  standard Markdown per the bus contract (`ChannelCapabilities.MarkdownFlavor = "Markdown"`), the
  adapter renders to mrkdwn: `**b**`→`*b*`, links `[t](u)`→`<u|t>`, headings→bold lines,
  `&<>` escaping. One welcome simplification vs Telegram: Slack does not REJECT malformed mrkdwn
  (it just renders literally), so the plain-text-resend fallback arm
  (`TelegramChannelAdapter.cs:445-453`) has no Slack equivalent to build.
- Attachments: the **external upload flow** — `files.getUploadURLExternal` → PUT bytes →
  `files.completeUploadExternal` with `channel_id` (+ `thread_ts`) — because `files.upload` is
  deprecated and closed to new apps. Same text-first, fail-whole-send-on-text-failure ordering as
  `TelegramChannelAdapter.SendAsync` (`:404-428`).

**`Capabilities`:** `Threads = true`, `Mentions = true`, `Attachments = true`, `Edit = true`,
`Delete = true`, `Reactions = true`, `TypingIndicator = false` (no Web API typing for bots),
`MarkdownFlavor = "Markdown"`, `MaxTextLength = 4000` (aligned to `MaxReplyChars` and the preamble
contract; Slack's own cap is far higher), the same `AttachmentKinds` minus Location/Contact.

## 7. Decision: deployment — same process, same image, conditional registration

**Recommendation: the same `Antiphon.Messaging.Service` process.** This is what the design already
anticipates (`Program.cs:28` — "Telegram for now; WhatsApp/Teams register the same way"), and both
hosted services iterate `IEnumerable<IChannelAdapter>` today. A separate deployment would duplicate
Kafka wiring, the inbox DB, and ops for zero isolation benefit — the per-instance model already
provides isolation where it matters (per bot).

Changes:

- **Conditional registration** in `Program.cs`: register `TelegramChannelAdapter` only when
  `Telegram:BotToken` is non-empty, `SlackChannelAdapter` only when `Slack:BotToken` is non-empty.
  One image then serves telegram-only, slack-only, or both-in-one-instance; existing deployments
  (family, school_revision) are unaffected because their Slack section is absent. A startup log
  line names the registered channels (an instance with ZERO adapters should log a loud warning,
  not sit silent).
- **Rename `TelegramIngressService` → `ChannelIngressService`** (file + class + log category). It
  already pumps all adapters; the name is a pre-second-adapter artifact. Internal class, no config
  key touched, mechanical.
- **Deployment to server2**: same process as `docs/telegram-bot-ops.md:22-31` — tar-sync
  `src/Antiphon.Messaging*` + `Messaging.Pack.props`, `docker compose build messaging-service`,
  `up -d` — plus `Slack__BotToken`/`Slack__AppToken` env in the compose. Decision for the operator
  at deploy time (not blocking the plan): add Slack to the existing **family** instance (bot =
  same persona, both channels route to the same bound agents — simplest) or stand up a separate
  slack-only instance with its own Kafka/DB per the standalone model. The image supports either.
- **New `docs/slack-bot-ops.md`** rather than growing telegram-bot-ops.md: it carries the app
  manifest, token custody, Socket-Mode-specific troubleshooting (disconnect churn, envelope acks),
  and the §5 threading limitation — mostly disjoint content. Cross-link both;
  update `messaging-standalone.md` and the service README env table with the `Slack__*` rows.
- Image NAME (`ghcr.io/michal-ciechan/antiphon-messaging-telegram`) becomes a slight misnomer.
  Renaming to `antiphon-messaging` touches two live composes and build docs — noted as optional
  housekeeping, **not** part of this card.

## 8. Decision: the preamble preset — provider-keyed, not a second special case

`AgentEndpoints.cs:46-51` currently switches `null or "telegram"` → `TelegramPresetTemplate`, else
404. The cheap genuinely-agnostic version:

- `ChannelPreamble` grows `PresetTemplateFor(string provider)` returning the template for
  `"telegram"` / `"slack"` (null for unknown). The Slack preset is the Telegram preset's skeleton
  (`ChannelPreamble.cs:45-61`) with the provider name, envelope example line
  (`[Slack "eng-antiphon" — Mike (@mike) 14:32] …` — the envelope itself is already generic,
  `ChannelPromptFormat.cs:40`), and one added sentence on threads ("replies land in the thread of
  the message they answer"). The reply contract lines (4000 chars, plain Markdown/no tables,
  `NO_REPLY`, `[[attach: …]]`) are **shared verbatim** — they are Antiphon's contract, provider
  agnostic already — so the builder composes shared fragments rather than duplicating the string.
- `AgentEndpoints` becomes: `provider ?? "telegram"` (back-compat for the existing UI call) →
  `PresetTemplateFor` → 200 or 404. No second special case to accrete.
- Client: `AgentSettingsModal.tsx:300-304`'s single "Use Telegram preset" button becomes a small
  per-provider affordance (two buttons or a menu: "Telegram preset" / "Slack preset");
  `client/src/api/agents.ts:347` already takes a provider parameter.

Deliberately NOT refactored further (scope guard): the bootstrap/restart/recovery note bodies and
`ChannelPromptFormat` are already provider-neutral; nothing else in the preamble subsystem is
Telegram-conditional.

## 9. Slices

### S0 — operator: create the Slack app (no code; anytime before S4)

From the manifest in §3 (shipped with S4's ops doc; can be drafted first): create app → enable
Socket Mode → app token (`connections:write`) → add bot scopes → install to workspace → bot token.
Both tokens into Bitwarden ("Antiphon Slack Bot"). Invite the bot to one test channel.

### S1 — `Antiphon.Messaging.Slack`: settings, adapter, renderer, fakes, tests (M, ~1.5–2 days) — CRITICAL PATH

The whole of §6, plus tests mirroring the Telegram suite in `tests/Antiphon.Messaging.Tests`:

- **`FakeSlackServer`** (sibling to `FakeTelegramServer`): in-process HTTP for
  `apps.connections.open` / `auth.test` / `chat.postMessage` / `users.info` /
  `conversations.info` / the upload pair, **plus a local WebSocket endpoint** the fake's
  `apps.connections.open` points at, from which tests push envelopes and observe acks.
  Wire-shape-faithful JSON (snake_case, `ok`/`error` envelope) so conformance-style assertions
  hold.
- **`SlackChannelAdapterTests`**: normalization (channel/DM/thread, mentions, files, allowlist,
  subtype skips, `IsSelf` on `bot_id` AND on own-user — the reply-loop guard pinned explicitly),
  send (thread_ts from handle, from `ReplyToMessageId`, `RawOverrides` win; kind prefixes;
  upload flow).
- **`SlackResilienceTests`** (the `TelegramResilienceTests` class of defect): connections-open
  hang/timeout → OCE treated as transient; mid-stream socket death → reconnect; `disconnect`
  envelope → clean reopen, no error; 429 `Retry-After` honored and capped; send retry bounded;
  ack-before-process.
- **`SlackMrkdwnRendererTests`** mirroring `TelegramMarkdownRendererTests`.
- Optional `[Explicit]` live conformance tests against a real workspace, the
  `TelegramLiveChatConformanceTests` pattern — valuable, not gating.

### S2 — host integration: registration, rename, packaging (S, ~0.5 day)

§7's code half: conditional adapter registration + zero-adapter warning in `Program.cs`;
`TelegramIngressService` → `ChannelIngressService` rename; project into `Messaging.Pack.props` and
the Dockerfile build context; `/api/channels` (already generic) now lists both capabilities.
Verify the fake gateway and existing Telegram tests untouched-green.

### S3 — server + client: provider-keyed preamble preset (S, ~0.5 day)

§8: `ChannelPreamble.PresetTemplateFor` + Slack preset + shared-fragment composition;
`AgentEndpoints` lookup; `AgentSettingsModal` per-provider preset buttons. Tests: preset endpoint
per provider + 404, preamble content pins (the Telegram preset string must not change byte-for-byte
— existing tests/fakeclaude scenarios reference it), client test for the button change.

### S4 — deploy + ops doc + live E2E smoke (S–M, ~0.5–1 day)

`docs/slack-bot-ops.md` (manifest YAML, scopes, custody, binding steps, threading limitation,
Socket Mode troubleshooting); README/`messaging-standalone.md` env-table updates; compose env on
the chosen server2 instance; deploy per `telegram-bot-ops.md:22-31` (re-verify paths on the box, as
that doc itself warns). Then the standing 4-step channel E2E smoke, per project convention: send a
Slack message → channel row appears → bind to a test agent → message reaches the transcript
enveloped → reply renders back **in the right thread**. Bind the channel before flipping the agent
always-on. Also verify the family Telegram instance still polls (shared-process regression check).

Dependencies: S1 → S2 → S4; S3 is independent of S2/S4 and can run in parallel after S1 settles the
provider key string. S0 is operator work needed only by S4 (and S1's optional live tests).

## 10. Deliberately not in scope

- **Per-message reply handles** (fixing §5's latest-wins thread targeting) — a server schema
  change on the CARD-0067 surface; documented as the named future fix.
- **`(Provider, ExternalId)` BotId discriminator** — pre-existing accepted limitation
  (`docs/telegram-bot-ops.md:17-20`); Slack inherits it (two Slack apps in one channel would
  collide) and one-bot-per-conversation policy carries over.
- **Events API webhook path** — decided against (§2); nothing built to hedge it.
- **CARD-0106 integration** — decided inapplicable (§4).
- **Slack blocks composition, interactive components (buttons, shortcuts, slash commands, modals)**
  — `RawOverrides` keeps `blocks` reachable for callers; first-class support is its own card.
- **`message_changed`/`message_deleted` handling, reactions, typing indicator** — v1 skips
  subtypes; Telegram's edit handling is richer and can be matched later if it matters.
- **Multi-workspace / OAuth distribution** — internal single-workspace app only (also what Socket
  Mode requires).
- **Image rename** to `antiphon-messaging` — optional housekeeping, noted in §7.
- **Per-provider `MaxReplyChars`** — the global 4000 matches the cross-provider reply contract;
  revisit only if a Slack-specific longer-reply need actually appears.

## 11. Card housekeeping

- CARD-0107 stays in Backlog until implementation is picked up; edit it to link this plan and
  record the seven §Verdict decisions (Socket Mode; scopes/manifest; bespoke env token —
  CARD-0106 inapplicable; thread-in-parent-conversation + latest-wins limitation; same-process
  deployment; preamble refactor).
- No change needed to CARD-0106 — its scope is untouched by the §4 finding.
- The WhatsApp idea doc (`docs/features/003-whatsapp-integration.md`) predates the messaging
  gateway and describes a webhook-into-the-server design the gateway has since superseded; not
  updated here (it is an input doc, marked as such), but a future WhatsApp card should start from
  the CARD-0107 shape, not that doc.
