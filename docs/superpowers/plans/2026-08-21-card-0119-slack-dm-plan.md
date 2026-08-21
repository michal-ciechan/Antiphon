# CARD-0119 — Slack DMs: enable the Messages tab, and prove one DM is one continuous history

**Date:** 2026-08-21
**Status:** planned (investigation complete; no Slack app touched, no code written in this pass)
**Card:** CARD-0119 (`6567e6e8-38a0-45b7-aecd-263e222b212b`) — enable the Slack app's Messages tab
(`features.app_home` manifest block) so users can DM the Antiphon bot.
**New requirement added by the operator, not yet on the card:** a DM conversation must have a
**single, continuous message history** — one stable channel row and one transcript across many
back-and-forth messages, not fragmenting into duplicate rows on repeated opens/closes or re-installs.

**Evidence.** Everything below was read or measured on 2026-08-21, not inferred from the card:

| What | Where |
|---|---|
| Slack DM → conversation keying | `src/Antiphon.Messaging.Slack/SlackChannelAdapter.cs:405-429` (`Conversation.Id = channelId`), `:980-988` (`ConversationKindOf`) |
| Channel row identity | `server/Application/Services/ChatChannelService.cs:76-108` (`UpsertFromInboundAsync`), `server/Infrastructure/Data/AppDbContext.cs:199` (`HasIndex(Provider, ExternalId).IsUnique()`) |
| Bind gate / routing | `server/Application/Services/ChannelBridgeService.cs:105`, `:117`, `:186`, `:314-364` (`EnsureAgentSessionAsync`) |
| Reply addressing | `server/Application/Services/ChannelReplyDispatcher.cs:260-290`; `SlackChannelAdapter.cs:668-688` (`ResolveTarget`) |
| Live channel catalog | `GET http://localhost:17202/api/channels` — the `slack`/`C0N8YDJH0` row, **4 messages on ONE row** |
| Live gateway config | `ssh mc@server2 'cd /home/mc/antiphon-messaging && docker compose config' \| grep Slack__` — **only** `Slack__BotToken` + `Slack__AppToken` are set |
| Live gateway log shape | `docker logs am-service` — `[ingress] slack C0N8YDJH0 -> channels.inbound`, bot user `U0BRX5EHKTK` |
| Existing DM test coverage | `tests/Antiphon.Messaging.Tests/SlackChannelAdapterTests.cs:81` — `Direct_message_normalizes_as_a_direct_conversation` |
| Slack's own Messages-tab procedure | `docs.slack.dev/surfaces/app-home/` (fetched) |
| Browser-automation traps for this app | `C:\src\claudebot\sites\api.slack.com.md` (176 lines, written during CARD-0107) |

---

## Verdict

**No Antiphon C# production code change is needed. Confirmed, not assumed.** CARD-0119 is
(a) a Slack-side app-config edit driven by browser automation, (b) a live verification pass, and
(c) the `docs/slack-bot-ops.md` edit the card's Done-when #4 already asks for. The next dispatch is
a **browser-automation + verification** dispatch, not a code dispatch.

**The single-continuous-history requirement is already structurally guaranteed, and the guarantee is
already measurable in production data.** A Slack DM's conversation id is a `D…` id that Slack keeps
stable for the lifetime of that (user, bot-user) relationship; the adapter puts it verbatim into
`Conversation.Id` (`SlackChannelAdapter.cs:412`); `ChatChannelService.UpsertFromInboundAsync` looks
the row up by `(Provider, ExternalId)` and only inserts when that lookup misses
(`ChatChannelService.cs:82-95`); and `(Provider, ExternalId)` carries a **unique index**
(`AppDbContext.cs:199`), so a duplicate row is not merely unlikely — it is a database error. The
live `slack`/`C0N8YDJH0` row already demonstrates the property with real traffic: `messageCount: 4`,
`createdAt 16:24:08`, `lastMessageAt 16:34:35` — four messages over ten minutes, one row, across a
gateway redeploy that happened between them.

**Three corrections to the card, all material:**

1. **"Then reinstall the app" is probably unnecessary and is the only step that carries risk.**
   Slack's own App Home documentation describes enabling the Messages tab as a dashboard toggle
   (App Home → Show Tab → Messages tab), with no reinstall step. `features.app_home` is not a scope
   change, and this app has `token_rotation_enabled: false`. **Save the manifest and re-check the DM
   view first; treat reinstall as a fallback, not a step.**
2. **If a reinstall *does* prove necessary, the card's "nothing on server2 needs touching" claim
   breaks.** A workspace reinstall goes through `oauth.v2.access` again and can mint a **new
   `xoxb-` bot token**, revoking the old one. That would require updating the Bitwarden item, the
   mode-600 `.env` on server2, and **restarting `am-service` — which restarts the live Telegram
   gateway carrying the Family and AZ Care conversations** (one process, both adapters). This is a
   contingency the card does not mention and the plan below handles explicitly. The app-level
   `xapp-` Socket Mode token is unaffected either way (it is app-scoped, not workspace-install-scoped).
3. **The card's Done-when skips the fact that the first DM cannot get a reply.** A newly discovered
   channel row has `AgentId = null` and `ChannelBridgeService.cs:117` returns before routing
   (`First_inbound_message_discovers_the_channel_unrouted`). DM #1 *creates* the row; the binding
   happens after; DM #2 is the first that can round-trip. The verification sequence below is ordered
   accordingly.

One further confirmation the card needed: **`Slack__AllowedConversationIds` is NOT set on the
deployed gateway** (checked live via `docker compose config`). The fail-closed allowlist at
`SlackChannelAdapter.cs:388` is therefore inert, and a `D…` conversation will be accepted the moment
Slack starts delivering `message.im` events. Had that allowlist been populated with `C0N8YDJH0`,
CARD-0119 would have silently required a gateway config change — this was worth checking and is now
ruled out.

---

## 1. How a Slack DM is keyed, and why one DM is one row forever

The chain, end to end:

1. **Slack event → conversation id.** `TryNormalizeAsync` reads `event.channel` and puts it
   unmodified into `Conversation.Id` (`SlackChannelAdapter.cs:384, 412`). For a DM that is the
   `D…` IM channel id. Slack allocates one IM channel per (user, bot-user) pair and keeps it for the
   life of that relationship — closing and reopening the DM in the client does **not** mint a new id,
   it re-opens the same conversation.
2. **Kind.** `ConversationKindOf(event.channel_type, channelId)` (`:980-988`) maps `"im"` →
   `ConversationKind.Direct`, with a `channelId.StartsWith('D')` fallback for event shapes that omit
   `channel_type`. Two independent signals agree; a DM cannot be mis-kinded. Already pinned by
   `SlackChannelAdapterTests.cs:81`.
3. **Row identity.** `UpsertFromInboundAsync` (`ChatChannelService.cs:76-108`) selects on
   `c.Provider == message.Channel && c.ExternalId == message.Conversation.Id` and constructs a new
   `ChatChannel` **only** when that returns null. `Provider` is the adapter's
   `ChannelKey = "slack"` constant (`SlackChannelAdapter.cs:30`).
4. **The invariant is enforced by the database, not by the code path.**
   `AppDbContext.cs:199` — `entity.HasIndex(c => new { c.Provider, c.ExternalId }).IsUnique()`. A
   second row for the same `D…` id cannot exist; an attempt would surface as a duplicate-key
   `DbUpdateException`, not as silent fragmentation.
5. **Reinstalling the app does not change the key.** The `D…` id is a function of the workspace's
   user and the app's **bot user** (`U0BRX5EHKTK` here). A reinstall of the same app to the same
   workspace preserves the bot user, so it preserves the IM channel id, so the row survives.
   *Deleting the app and creating a new one* would mint a new bot user and therefore a new `D…` —
   that is the only realistic way to fragment a DM history, and it is not part of this card.

**What is *not* guaranteed by any of the above, and must be said plainly:** channel-row continuity
and *transcript* continuity are two different properties with two different owners. The row is
keyed by the conversation; the transcript belongs to the **agent's persistent session**.
`EnsureAgentSessionAsync` (`ChannelBridgeService.cs:314-364`) resolves `Agent.PersistentSessionId`
and reuses it whenever its status is `Running`, starting the agent only when it is not. So DM #2
lands in the same Claude conversation as DM #1 **provided the agent's session has not been
restarted in between**. If the agent restarts (crash, `always-on` supervisor, manual relaunch), the
channel row is untouched and `messageCount` keeps climbing, but a new `AgentSession` row and a new
JSONL transcript begin. That is correct, existing, deliberate behaviour — but it means the
verification must assert *both* properties separately rather than treating "the reply looked right"
as proof of either.

---

## 2. What a reinstall touches — and the contingency if it is needed

| Thing | Effect of a workspace reinstall |
|---|---|
| `ChatChannel` rows | **None.** They live in Antiphon's Postgres and are keyed by `(provider, external id)`. Slack has no visibility into them. |
| The `D…` IM channel id | **Unchanged** — same app, same bot user (`U0BRX5EHKTK`), same IM channel. |
| Agent binding (`ChatChannel.AgentId`) | **None.** Antiphon-side column. |
| App-level token (`xapp-…`, `connections:write`) | **Unchanged.** App-scoped, issued from Basic Information, independent of workspace install. |
| Bot token (`xoxb-…`) | **At risk.** A reinstall re-runs `oauth.v2.access`. Slack's guidance treats reinstall as the way to *rotate* a token. Assume it may change; verify empirically. |

**Therefore: do not reinstall unless the Messages tab fails to appear without it.** Slack's App Home
docs (fetched 2026-08-21) present the Messages tab as a dashboard toggle with no reinstall step, and
`features.app_home` changes no OAuth scope.

**If reinstall turns out to be required, the contingency is:**

1. Re-capture the new `xoxb-` from `api.slack.com/apps/A0BRR9DS9QV/install-on-team` — it sits in a
   readonly `<input>`; match it **by the `xoxb-` prefix**, never by the hashed CSS-module class
   (`C:\src\claudebot\sites\api.slack.com.md`, *Retrieving the token VALUES safely*).
2. Move it into Bitwarden item **"Antiphon Slack Bot"** using the two-script, file-brokered pattern
   in the `bitwarden` skill. **The value must never enter agent output** — no `Get-Member`, no bare
   echo, no screenshot of that page while revealed.
3. Update the mode-600 `.env` beside `/home/mc/antiphon-messaging/docker-compose.yml` and
   `docker compose up -d messaging-service`.
4. **Because both adapters share one process, this restarts the live Telegram gateway.** Re-verify
   Telegram in both directions afterwards (`docs/telegram-bot-ops.md` verify step). Do not stop at
   "the container came up".

**Cheap empirical check for whether the token changed at all**, without printing it — run *before*
touching anything else after the reinstall:

```bash
# lengths only; xoxb- is 56 chars, xapp- is 98
ssh mc@server2 'cd /home/mc/antiphon-messaging && docker compose config' \
  | grep -E 'Slack__' | awk -F': ' '{gsub(/"/,"",$2); print $1": len=" length($2)}'

# the authoritative check: is the socket still live and is auth still good?
ssh mc@server2 'docker logs --since 10m am-service 2>&1 | grep -iE "slack|invalid_auth|not_authed"'
```

A revoked token shows up as `invalid_auth` / `not_authed` in that log, or as the Socket Mode loop
failing to reopen. A message posted in `#general` that still round-trips is positive proof the old
token survived.

---

## 3. The "Slack Test" agent, and why the DM needs its own binding

**Live state, read from `/api/agents` and `/api/channels`:**

- Agent **Slack Test** — `eecab440-0f89-4691-9596-ea1e8ff049d0`, slug `slack-test`, cwd
  `C:/src/ClaudeBot/agents/slack-test`, `alwaysOn: true`, `remoteControlEnabled: true`,
  status `Running`, `persistentSessionId 047c47c1-8ba4-4464-86aa-c634c042695f`, `modelLevel: Medium`,
  context fullness 0.35.
- Channel **slack / `C0N8YDJH0` (`#general`)** — `c906f485-5f72-495d-b9b4-c2f6243cdc5d`, bound to
  Slack Test, enabled, 4 messages.

**It is a reasonable target, and binding is genuinely per-row — confirmed in code, not assumed.**
`ChatChannel.AgentId` is a column on the row (`ChatChannel.cs:29`), and the routing gate reads it
off the row that the inbound message resolved to (`ChannelBridgeService.cs:117`). Nothing keys a
binding by agent-to-provider or agent-to-workspace. The live catalog already demonstrates the
many-rows-to-one-agent shape independently: the **Family** agent
(`a7647365-4803-4541-a457-ba9e7fcfa8f0`) is bound to *both* the telegram Group row `-5052370282`
*and* the telegram Direct row `8738110514`, with independent `enabled` flags (`true` and `false`
respectively).

**So the DM row will arrive unbound and must be bound explicitly.** Reusing Slack Test is the right
call: it already exists, is always-on, is disposable ("throwaway persona verifying the Slack channel
round trip"), and its system prompt already carries the Slack envelope grammar. Binding it to the DM
*in addition to* `#general` is also the more informative test — it proves the two rows route
independently into one session rather than interfering.

Two consequences to expect and not mistake for defects:

- With both rows bound to one agent, `#general` traffic and DM traffic **share one transcript**.
  That is correct (the session belongs to the agent, not the conversation), but it means the
  transcript assertions in §4 must match on the DM's own envelope text, not merely on "a UserPrompt
  appeared".
- The DM row's `Title` will be **null**. `ResolveConversationTitleAsync` (`SlackChannelAdapter.cs:601`)
  calls `conversations.info`, and a DM has no `name` field, so null is cached and returned. The
  Channels UI falls back to the raw id (`ChannelsPage.tsx:92`), so the row will display as
  **`D0…`** with kind `Direct`. Identify it by `externalId` prefix, never by title. The prompt
  envelope is unaffected — `ChannelPromptFormat.Format` renders `[Slack direct message — …]` for
  `Kind == Direct` without consulting `Title`.

---

## 4. Verification sequence for the single-continuous-history requirement

The card's Done-when proves *one* DM round-trips. This sequence proves *continuity*. Every step
names the artifact that constitutes proof, so nothing rests on "the replies looked right in Slack".

**Preconditions.** Record these before sending anything, so every later assertion has a baseline:

```bash
# A. Baseline channel catalog — note there is NO row whose externalId starts with D
curl -s http://localhost:17202/api/channels | python -m json.tool | grep -A3 '"provider": "slack"'

# B. Baseline transcript length for the Slack Test session
curl -s "http://localhost:17202/api/sessions/047c47c1-8ba4-4464-86aa-c634c042695f/transcript" \
  | python -c "import json,sys; d=json.load(sys.stdin); print(len(d) if isinstance(d,list) else d)"
```

Confirm the agent's `liveSession.id` is still `047c47c1-…` at this point; if the agent has been
restarted since this plan was written, use the current `persistentSessionId` throughout and say so
in the report.

**Step 1 — DM #1: the row is discovered, unbound, and gets no reply.**
Send `CARD-0119 DM one` from the operator's Slack account to the Antiphon bot's DM.

Proof to capture:
- Gateway: `ssh mc@server2 'docker logs --since 5m am-service 2>&1 | grep "\[ingress\] slack"'` →
  a line `[ingress] slack D<id> -> channels.inbound` (the format is confirmed live for `C0N8YDJH0`;
  it is emitted at `ChannelIngressService.cs:47`).
- Catalog: `GET /api/channels` now contains exactly one new row with `provider: "slack"`,
  `externalId` starting `D`, **`kind: "Direct"`**, `agentId: null`, `messageCount: 1`. Record its
  `id` (call it `$DM_ROW`) and its `createdAt`.
- **Expect no reply.** Silence here is the correct behaviour, not a failure
  (`ChannelBridgeService.cs:117`). Card Done-when #2 is satisfied by this step.

**Step 2 — bind the DM row to Slack Test.**

```bash
curl -s -X PATCH http://localhost:17202/api/channels/$DM_ROW \
  -H 'Content-Type: application/json' \
  -d '{"agentId":"eecab440-0f89-4691-9596-ea1e8ff049d0","enabled":true}'
```

(or the Channels page at `http://localhost:17203/channels`). Confirm the response shows
`agentName: "Slack Test"`.

**Step 3 — DM #2: the first round trip.**
Send `CARD-0119 DM two - please reply with the word PERSIMMON`.

Proof to capture:
- A reply arrives **in the DM's main pane**, not as a threaded reply (see §5 hazard 1).
- `GET /api/channels` — the row is still the **same `id`** with the **same `createdAt`**, and
  `messageCount` is now **2**. There is still exactly one `provider: "slack"` row whose
  `externalId` starts with `D`.
- Transcript: `GET /api/sessions/<sessionId>/transcript?since=<baseline>` contains a
  `Kind == "UserPrompt"` entry whose `text` contains `[Slack direct message —` and `DM two`.
  Card Done-when #3 is satisfied here.

**Step 4 — close the DM, reopen it, and let real time pass. Then DM #3.**
This is the step that actually tests the new requirement, so do all three perturbations:
close the DM conversation in the Slack client, reopen it, and wait **at least 5 minutes** (well past
`DebounceWindowMs: 500` / `DebounceMaxMs: 2000`, so #3 is unambiguously its own turn rather than a
debounced continuation of #2). Then send `CARD-0119 DM three - what did I ask you in DM two?`.

Proof to capture — **this is the assertion set that constitutes the requirement**:
- **One row, not two.** `GET /api/channels` still returns exactly **one** `slack` row with a `D…`
  `externalId`; its `id` and `createdAt` are byte-identical to what Step 1 recorded; `messageCount`
  is now **3**; `lastMessagePreview` reads `CARD-0119 DM three…`.
- **Direct DB confirmation** (the catalog endpoint and the DB agree, and a duplicate would be
  visible here even if the API deduped it in some way):

  ```bash
  docker exec -i antiphon-postgres psql -U antiphon -d antiphon -c \
    "SELECT \"Id\", \"ExternalId\", \"Kind\", \"AgentId\", \"MessageCount\", \"CreatedAt\", \"UpdatedAt\"
       FROM \"ChatChannels\" WHERE \"Provider\"='slack' ORDER BY \"CreatedAt\";"
  ```

  Exactly one `D…` row. `MessageCount = 3`. `CreatedAt` unchanged from Step 1, `UpdatedAt` moved.
- **One transcript, not two.** All three DM envelopes are `TranscriptEntry` rows under the **same**
  `AgentSessionId`:

  ```bash
  docker exec -i antiphon-postgres psql -U antiphon -d antiphon -c \
    "SELECT \"AgentSessionId\", \"Sequence\", left(\"Text\", 90)
       FROM \"TranscriptEntries\"
      WHERE \"Kind\"='UserPrompt' AND \"Text\" LIKE '%CARD-0119 DM%'
      ORDER BY \"Sequence\";"
  ```

  Expect two rows (DM two and DM three — DM one was never routed), **sharing one
  `AgentSessionId`**, with strictly increasing `Sequence`. If the `AgentSessionId` values differ,
  the agent was restarted mid-test: the channel-row half of the requirement still holds, and that
  must be reported as such rather than as a continuity failure.
- **Semantic continuity — the check a human would actually care about.** The reply to DM #3 should
  correctly recall what DM #2 asked. That only works if both landed in one conversation, so it is a
  genuine end-to-end confirmation rather than a UI impression. Record the reply text verbatim.
- **UI confirmation:** `http://localhost:17203/channels` shows one `D…` row, kind `Direct`,
  `3 msgs` (`ChannelsPage.tsx:92-108`).

**Step 5 — the reinstall-survival check, only if a reinstall was performed.**
After the reinstall completes and the gateway is confirmed healthy, send
`CARD-0119 DM four - post-reinstall`. Assert the **same row `id`**, `messageCount: 4`, and unchanged
`createdAt`. This is what closes the operator's "not fragmenting on re-installs" clause with
evidence rather than with the reasoning in §2.

**Optional, cheap durable proof.** The behaviour above is guaranteed by a unique index, but nothing
in the test suite currently states it. `ChannelBridgeTests` has
`First_inbound_message_discovers_the_channel_unrouted` and `Redelivered_message_is_not_routed_twice`
but no "a second, *distinct* message on the same conversation reuses the row" case. A ~15-line test
next to those — two inbound messages with different `ChannelMessageId`s on one `D…` conversation,
asserting one row, `MessageCount == 2`, unchanged `Id`/`CreatedAt` — would pin the requirement
permanently. **This is a test, not production code**, and it is the only C# the card could
reasonably grow. Recommended, but it does not gate the Done-when.

---

## 5. Hazards found while investigating (not blockers; record them)

1. **A DM reply threads if — and only if — the operator threads first.**
   `ReplyHandle = threadTs is {Length: > 0} ? $"{channelId}|{threadTs}" : channelId`
   (`SlackChannelAdapter.cs:427`), and `ResolveTarget` prefers `ReplyHandle` over `ConversationId`
   specifically so threads are preserved (`:668-688`). `thread_ts` is present only when the
   *inbound* message was itself in a thread, so a top-level DM produces a bare `D…` handle and a
   top-level reply. **But** `UpsertFromInboundAsync` overwrites `channel.ReplyHandle` on every
   inbound (`ChatChannelService.cs:99`) and `ChannelReplyDispatcher` reads whatever is current at
   dispatch time (`:275-280`), so a threaded DM message can leave a stale `D…|ts` handle addressing
   a reply that is still in flight. **Verification consequence:** send every test DM at the DM's top
   level. If a reply ever appears collapsed under "1 reply" instead of in the main pane, that is
   this hazard, not a fragmentation failure — the channel row is unaffected either way.
2. **A Slack *public channel* displays as kind `Broadcast`.** `ConversationKind.Channel` has no
   counterpart in `ChatChannelKind` and falls through `MapKind`'s `_ => Broadcast`
   (`ChatChannelService.cs:110-115`) — visible live on the `#general` row. Cosmetic, pre-existing,
   and **does not affect DMs** (`Direct → Direct` maps exactly). Not this card's problem; worth its
   own card if the operator wants the UI label fixed.
3. **The DM row shows as a raw `D…` id in the Channels UI** because `conversations.info` returns no
   `name` for an IM (§3). Expected, not a bug.
4. **The bot's own DM replies come back down the same socket** as `message.im` events and are
   dropped by the `IsSelf` guard at `SlackChannelAdapter.cs:203`, with `ChannelBridgeService` as the
   second brace. If a DM ever loops, that guard is where to look — but `[slack] bot identity
   resolved: U0BRX5EHKTK` in the live log confirms the identity it needs is present.

---

## 6. Is any Antiphon code change needed? — stated plainly

**No.** Itemised against the actual DM path:

| Requirement | Already satisfied by | Evidence |
|---|---|---|
| Subscribe to DM events | `message.im` in the deployed manifest's `bot_events` | `docs/slack-bot-ops.md`; verified on the app's Event Subscriptions page during CARD-0107 |
| Scopes to read/send DMs | `im:history`, `im:read`, `chat:write` all installed | CARD-0119's own live check; card body |
| Normalize a DM to a Direct conversation | `ConversationKindOf` — `"im"` **and** the `D` prefix fallback | `SlackChannelAdapter.cs:980-988`, pinned by `SlackChannelAdapterTests.cs:81` |
| Resolve a DM title without crashing | `conversations.info` returns no `name`; null cached and returned | `SlackChannelAdapter.cs:601-630` |
| Not drop the DM at the allowlist | `Slack__AllowedConversationIds` unset on server2 → allowlist inert | live `docker compose config` |
| One row per DM, forever | `(Provider, ExternalId)` lookup + **unique index** | `ChatChannelService.cs:82-95`, `AppDbContext.cs:199` |
| Route the DM to an agent | Per-row `AgentId`; same gate as any channel | `ChannelBridgeService.cs:117` |
| Route the reply back to the DM | `ConversationKey = "slack:D…"` persisted on the queued row; target resolved at dispatch | `ChannelBridgeService.cs:186`, `ChannelReplyDispatcher.cs:260-290` |
| Reply survives a restart mid-round-trip | CARD-0067 durable correlation — no in-memory map | `ChannelReplyDurabilityTests` |

The only repo changes CARD-0119 produces are **documentation** (`docs/slack-bot-ops.md`) and,
optionally, **one regression test** (§4, last paragraph).

---

## 7. Next steps

**Dispatch shape: browser-automation + live verification. Not a code dispatch.**

1. **Slack-side edit (browser-harness, CDP Edge on :9222 — never a second CDP browser).**
   Open `api.slack.com/apps/A0BRR9DS9QV` → **App Manifest**, add the `features.app_home` block from
   the card, Save. Before anything else on that domain: set the viewport
   (`Emulation.setDeviceMetricsOverride`, 1400×1400) and clear the OneTrust banner on **every**
   navigation — both traps are documented with working snippets in
   `C:\src\claudebot\sites\api.slack.com.md`. Read `state.lint.marked` after a 1–2s debounce before
   clicking Save; check `aria-disabled`, never `.disabled`.
   *Equivalent alternative, and possibly simpler:* the **App Home → Show Tab → Messages tab** toggle,
   which is the path Slack's own docs describe. Either way, also un-tick read-only.
2. **Check the DM view immediately, before reinstalling.** If the composer renders, stop — §2's
   contingency is not needed and nothing on server2 is touched.
3. **Only if the composer is still absent:** reinstall, then run §2's token check *before* declaring
   success, and follow the token contingency if the length/auth check says the token moved.
4. **Run §4 Steps 1–5.** Report the actual row `id`, `createdAt`, and `messageCount` progression, and
   the two `psql` result sets — those are the evidence for the operator's new requirement.
5. **Docs.** Replace the "DMs need the Messages tab — the manifest above does NOT enable it" section
   of `docs/slack-bot-ops.md` with the working manifest, add `features.app_home` to the manifest
   block at the top of that doc, and record whichever of "reinstall was/was not required" turned out
   to be true — that is the fact the next person needs. If a reinstall did rotate the token, add
   that to the doc too; it is exactly the kind of thing that is expensive to rediscover at 2am with
   the Family gateway down.
6. **Card housekeeping.** Amend CARD-0119's "Done when" to add a fifth item covering the continuity
   requirement (one row, `messageCount` climbing, one `AgentSessionId`, survives close/reopen and
   reinstall), and soften "then reinstall the app" to "save, re-check the DM view, reinstall only if
   still absent — a reinstall may rotate the bot token and therefore touches server2". Cards are
   correctable in place since CARD-0019. Move to In Progress on dispatch.

---

## 8. Deliberately not in scope

- **Fixing `ConversationKind.Channel → Broadcast`** (§5 hazard 2). Cosmetic, pre-existing, unrelated
  to DMs.
- **The stale-`ReplyHandle` threading race** (§5 hazard 1). Real but narrow, pre-existing since
  CARD-0107, and it cannot fragment a channel row. Worth its own card only if a threaded reply is
  actually observed hurting someone.
- **A `BotId` discriminator on `ChatChannel`.** `docs/telegram-bot-ops.md`'s accepted "one bot per
  group" limitation applies identically to Slack — two Antiphon bots in one conversation would
  collide on one row. Out of scope; there is one bot.
- **Home tab (`home_tab_enabled`).** Stays `false`. Nothing renders one and enabling it would add an
  `app_home_opened` surface with no handler.
- **Any gateway rebuild or redeploy.** The image is unchanged. The *only* circumstance that touches
  server2 is a rotated bot token (§2), and that is an `.env` edit plus a container restart, not a
  build.
- **Production-code changes of any kind.** §6.
