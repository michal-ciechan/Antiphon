# CARD-0385 — Slack Web Driver feasibility

**Date:** 2026-09-05 (task `dbf28fc6`; CDP-sniff follow-up `0c93073f`; bounded
per-thread-tab follow-up `b13d0092`)
**Card:** CARD-0385 (`6da0d468-327f-4ed7-95a0-509cb1a24d58`)
**Status:** investigation complete, including the CDP-sniff follow-up in §6 and
the bounded per-thread-tab follow-up in §7. No app code was changed. No live
Slack session credential was extracted or persisted. No Slack tab was CDP-attached.
**Verified against:** worktree `feat/card-task-dbf28fc6` (original) and
`feat/card-task-0c93073f` (follow-up); this pass on `master` at the `b13d0092`
dispatch; `docs/external-site-operations.md`, `docs/telegram.md`,
`docs/slack-bot-ops.md`, `src/Antiphon.Messaging.Slack`, Slack's current
User Terms / API Terms / Developer Policy / Salesforce AUP, the Chrome DevTools Protocol
Network domain (stable 1-3 and tot) plus DOM 1-3 and Page tot (retrieved 2026-09-05),
Slack's live connection-help page, the public unofficial-client corpus cited below,
`C:\src\browser-harness` (`daemon.py`, `helpers.py`, `SKILL.md`,
`interaction-skills/tabs.md`, `tests/unit/test_daemon.py`), and
`C:\src\claudebot\launch-edge-cdp.ps1`. Live CDP target *list* only:
`GET http://127.0.0.1:9222/json` (URLs/titles; no attach). Nothing here was
confirmed by talking to a live Slack session.

---

## Verdict, in one sentence

Do **not** build a first-party Antiphon channel that reuses a browser Slack session — neither
`xoxc`/`xoxd` token-pair extraction nor a CDP "Slack Web Driver". The token-pair is the only
unofficial approach that could actually implement `IChannelAdapter`; it is also a clear Slack
Developer Policy / API Terms violation. CDP is ToS-cleaner and matches this repo's existing
external-site lane, but it is the wrong shape for a standing channel gateway. Use the Slack bot
adapter that already shipped (`Antiphon.Messaging.Slack`, Socket Mode, `xoxb` + `xapp`). If
replies must appear as the human user and the workspace will install an app, the supported path
is an official **user token** (`xoxp-`), not a scraped web session.

The CDP-sniff follow-up (§6) does not change that. Passively watching the already-logged-in
tab's WebSocket is a real, documented CDP capability and Slack's web client still speaks
plaintext JSON over `wss-primary.slack.com` — but it is still reverse-engineering an
unpublished first-party protocol, still the wrong process for a gateway, and still ends at
DOM-automation replies (occasional use) or the token you already saw in the handshake
(standing use).

The bounded per-thread-tab follow-up (§7) does not change it either. One already-logged-in
tab per watched thread is a *lamp*, not a gateway: mechanically a plausible attended ops
recipe, ToS-cleaner than token-pair, and **not** the same object as a human leaving Slack
open in a few tabs. Do not build a standing watcher or a bounded prototype; the existing
CDP lane cannot host N Slack SPAs 24/7 without new infra.

---

## What already exists in this repo

Antiphon already has a ToS-clean Slack channel:

- `src/Antiphon.Messaging.Slack/SlackChannelAdapter.cs` — `IChannelAdapter` over the Web API
  (`chat.postMessage`, file upload) and Socket Mode (`apps.connections.open` → WebSocket).
  Credentials are `xoxb-` (bot) + `xapp-` (app-level, `connections:write`). Channel key `"slack"`.
- Ops: `docs/slack-bot-ops.md`. Plan: `docs/superpowers/plans/2026-08-20-card-0107-slack-channel-plan.md`.
- Same gateway shape as Telegram (`docs/telegram.md`, `docs/messaging/build-your-own-gateway.md`):
  one process, `ReceiveAsync` until cancel, `SendAsync` for replies, Kafka
  `channels.inbound` / `channels.outbound`.

That adapter cannot do what CARD-0385 asks. The bot only hears conversations it is a member of;
DMs are DMs *to the bot*; replies post as the bot user, not as the logged-in human; creating the
app needs workspace install (and usually admin approval). The card is specifically "no bot, no
OAuth app, reuse the browser login, read messages sent to the user and reply as the user."

The CDP lane (`docs/external-site-operations.md`) already drives a shared headed Edge profile
(`C:\Users\lndco\edge-cdp`, port 9222) for occasional real-site work, including Slack *app
setup* at `api.slack.com` (see `docs/slack-bot-ops.md` and `C:\src\claudebot\sites\api.slack.com.md`).
That is a one-shot ops tool, not a 24/7 message pump.

---

## 1. What the browser holds after a slack.com login (including SSO)

A successful web login — password, Google, or SAML/OIDC SSO — does **not** leave a reusable
IdP credential in the Slack origin. SSO is a redirect dance; when it finishes, Slack sets **its
own** session. That session is three things (PaperMtn, updated December 2025):

| Piece | Where it lives | Prefix / name | Role |
|---|---|---|---|
| Session cookie | Cookie jar, domain `app.slack.com` / `.slack.com` | cookie name `d`, value `xoxd-…` | Proves the browser session. **HttpOnly** (JS cannot read `document.cookie`; DevTools / CDP / the cookie DB can). |
| Session-binding cookie | Same jar | cookie name `d-s` (a timestamp) | Sometimes required, especially Enterprise Grid. Unofficial clients that only send `d` occasionally get `invalid_auth` until they add `d-s`. |
| Per-workspace API token | `localStorage.localConfig_v2.teams[<TEAM_ID>].token` | `xoxc-…` | The web client's own API token. One per workspace the user has open. Enterprise Grid pages also expose `enterprise_api_token` (org-wide search etc.). |

Official Slack token documentation lists `xoxb`, `xoxp`, `xapp`, `xwfp`, config, and service
tokens. It does **not** list `xoxc` or `xoxd`
([Tokens](https://docs.slack.dev/authentication/tokens)). Slack staff, answering a Stack Overflow
question in 2020, described `xoxc` as "special tokens that are used by the web client",
"cookie dependent", and said that "while we might not explicitly prevent it, using `xoxc` tokens
for the API is **not supported or recommended**"
([SO 62759949](https://stackoverflow.com/questions/62759949/accessing-slack-api-with-chrome-authentication-token-xoxc)).

**Usable from a .NET process on the same machine, without Bot/App OAuth?** Yes, technically.
Every unofficial client does the same HTTP shape:

```
POST https://<workspace>.slack.com/api/<method>
Authorization: Bearer xoxc-…
Cookie: d=xoxd-…          # often also d-s=…
Content-Type: application/x-www-form-urlencoded
# token=xoxc-… in the body is an equivalent form used by the web client itself
```

That is a pair of strings plus `HttpClient`. This repo's Slack adapter already speaks the Web API
and a WebSocket over `HttpClient` / `ClientWebSocket` with no third-party Slack SDK, so the
*transport* is not new work. The `d` cookie being HttpOnly does not block a .NET process: it only
blocks page JS. Obtaining the pair from the browser is CDP `Network.getCookies` +
`Runtime.evaluate` on `localStorage`, or reading Chromium's DPAPI-encrypted cookie DB — the same
methods `slackcli`, slkcli, and agent-messenger use. **This investigation did not do either.**

SSO does not change the resulting pair. It only changes how the `d` cookie is minted. Enterprise
Grid adds the second `xoxc` (`enterprise_api_token`) and more often wants `d-s`.

---

## 2. The "personal token" pattern — real, how obtained, how long it lasts

It is real, widely used, and has been the standard unofficial Slack-as-yourself path since Slack
retired legacy test tokens (~2020–2022, wee-slack PR #857). Living examples:

| Project | What it does with the pair |
|---|---|
| [korotovsky/slack-mcp-server](https://github.com/korotovsky/slack-mcp-server/blob/master/docs/01-authentication-setup.md) | MCP server; documents the localStorage one-liner and the `d` cookie. Prefers `xoxp` > `xoxb` > `xoxc`/`xoxd`. |
| [rusq/slackdump](https://github.com/rusq/slackdump/blob/master/doc/login-manual.md) | Workspace export. Same extraction; also a browser-login wizard. |
| [mautrix/slack](https://docs.mau.fi/bridges/go/slack/authentication.html) | Matrix bridge. Cookie login validated via unpublished `client.boot`; realtime via **RTM** (`rtm.connect`) when the token is `xoxc`/`xoxs`. App login uses Socket Mode. Still current as of v0.2511.0 (2025-11). |
| [insomniacslk/irc-slack](https://github.com/insomniacslk/irc-slack), [adsr/irslackd](https://github.com/adsr/irslackd) | IRC gateways. `xoxc\|d=…` as the IRC password. |
| [cgrossde/slackcli](https://pkg.go.dev/github.com/cgrossde/slackcli) | Agent CLI. Opens a browser, intercepts the pair via **CDP**, stores in Keychain, then calls the Web API and a live WebSocket. |
| [jimmystridh/slacko](https://github.com/jimmystridh/slacko) | Rust SDK "stealth mode". |
| [PaperMtn](https://www.papermtn.co.uk/retrieving-and-using-slack-cookies-for-authentication/) | Security write-up, updated December 2025. `d` cookie alone can mint a fresh `xoxc` from `https://<workspace>.slack.com` (`api_token` in the boot HTML). |
| [Shaharia Azam, Sep 2025](https://shaharia.com/blog/slack-browser-tokens-golang-sdk-bypass-app-creation/) | Official Slack Go SDK + custom `HttpClient` that injects the `d` cookie. Explicitly "not suited for production-grade integrations". |

**How it is obtained (mechanism only — not performed here):**

1. Log into Slack in a browser (SSO included).
2. `xoxc`: DevTools console
   `JSON.parse(localStorage.localConfig_v2).teams[document.location.pathname.match(/^\/client\/([A-Z0-9]+)/)[1]].token`
   or the `token` field on any `*.slack.com/api/*` request in the Network tab.
3. `xoxd`: Application → Cookies → cookie named `d`. Url-encode if the browser shows it decoded
   (Firefox).
4. Optional: cookie `d-s`.
5. Confirm with `auth.test` sending **both** the bearer/token and the `d` cookie. Token alone
   returns `invalid_auth` — Slack designed that dependency as a theft mitigator (staff SO reply).

**Lifetime — not a stable secret:**

- The pair is the **browser session**, not an OAuth grant. Logout, password change, "sign out of
  all other sessions", and admin session reset all kill it.
- Workspace / org **session duration** can be hours to years, and Enterprise can terminate the
  session when the desktop app or browser window closes
  ([Manage session duration](https://slack.com/help/articles/115005223763-Manage-session-duration);
  [`admin.users.session.setSettings`](https://docs.slack.dev/reference/methods/admin.users.session.setSettings)
  min 8 hours, max ~10 years, plus `desktop_app_browser_quit`).
- PaperMtn **December 2025**: Slack shortened the `d` cookie TTL from **10 years** to on the
  order of **a year+**. Still long, no longer "forever".
- `xoxc` can rotate independently of `d`. Several tools re-mint it by GETting the workspace URL
  with only the `d` cookie. That is why persisting `d` is more valuable to unofficial clients
  than persisting `xoxc`.
- Slack Engineering (June 2024, [cookie hijacking](https://slack.engineering/proactive-measures-against-password-breaches-and-cookie-hijacking))
  runs threat-intel invalidation of leaked `d` cookies and emails the user. A .NET process
  replaying the cookie from a different User-Agent / IP, without the rest of the browser's
  cookie jar, is in the same detection class as theft.
- Shaharia (Sep 2025): "they rotate when Slack invalidates that session … great for personal
  automation and prototyping but **not** suited for production-grade integrations."

There is no refresh token. Recovery is "log in again and re-extract."

---

## 3. Read and write without registering a Slack app?

**Yes, technically, with the user's own permissions.** That is the whole point of the pattern.

| Direction | How unofficial clients do it | App required? |
|---|---|---|
| Auth check | `auth.test` with bearer `xoxc` + cookie `d` | No |
| Read history / list conversations / search | Web API `conversations.history`, `conversations.list`, `search.messages`, `users.list`, … | No. Scopes are "whatever this user can already see", including private channels and DMs they are in. |
| Write | `chat.postMessage` (and file upload). Posts **as the user**, not as a bot. | No |
| Realtime subscribe | `rtm.connect` → WebSocket, the path mautrix still uses for `xoxc`. Also the web client's own unpublished socket (`wss-primary.slack.com` with `token=xoxc-…`, flannel). Polling `conversations.history` / unpublished `client.counts` is the fallback. | No |
| Slack Events API / Socket Mode | Needs an app and an `xapp-` token (`apps.connections.open`). | **Yes** — this is what `SlackChannelAdapter` already uses, and it is unavailable without an app. |

Official RTM is deprecated for *new granular-permission Slack apps*
([Legacy RTM API](https://docs.slack.dev/legacy/legacy-rtm-api/): "Granular permission Slack apps
cannot use the RTM API"). `xoxc` is not such an app token; it is the web client's session, and
`rtm.connect` still returns a socket for it in current unofficial bridges. That can change without
notice — API Terms say undocumented behaviour may change at any time.

Operational side effects if Antiphon held this socket 24/7:

- The user may appear **active** while the unofficial client is connected (RTM presence).
- Replies are indistinguishable from the human typing. That is the feature and the abuse case
  (PaperMtn's phishing example is `chat.postMessage` with Block Kit from a stolen pair).
- Rate limits still apply. Slack's May 2025 API ToS / rate-limit changes targeted unlisted apps
  pulling `conversations.history` / `conversations.list` at scale
  ([changelog](https://docs.slack.dev/changelog/2025/05/29/tos-updates/),
  [rate-limit FAQ](https://archive.is/2025.08.20-175706/https://api.slack.com/changelog/2025-05-terms-rate-limit-update-and-faq)).
  An `xoxc` client is not an "app" in that table, but the intent ("unsanctioned data scraping")
  is the same class of traffic.
- Unpublished methods the web client actually uses (`client.boot`, `client.userBoot`,
  `client.counts`) are what mautrix uses to validate cookie login. Calling them is an explicit
  Developer Policy violation (see §5).

So: read + write + a websocket, from .NET, no app registration, is a solved problem in other
codebases. It is not a solved *supported* problem.

---

## 4. Alternative: drive Slack only through CDP

This is the card's "Slack Web Driver": no token leaves the browser; Antiphon clicks and types in
the slack.com SPA the way `docs/external-site-operations.md` already drives other real sites.

**What would actually work**

- Login, including SSO and MFA, in a real headed Edge profile. This is the one place CDP is
  strictly better than token extraction: the IdP sees a real browser.
- Stay logged in for as long as the profile's cookies last (same session-duration rules as §2).
- Type into the composer and send, with trusted clicks/keystrokes (the existing CDP rule:
  synthetic `element.click()` / `setValue()` often fails client-side validation).
- One-shot ops: open a thread, copy a message, post something the operator is looking at. That
  is the same class of work as the existing `api.slack.com` app-creation notes.

**What would not work as an `IChannelAdapter`**

The gateway contract is a long-running `ReceiveAsync` that yields every inbound message, plus
`SendAsync` that addresses a stable `ReplyHandle`. Telegram long-polls `getUpdates`. The existing
Slack adapter ACKs Socket Mode envelopes. A DOM driver has no event stream:

- Slack web is a React SPA with virtualized message lists, hashed class names, and frequent UI
  churn. Selectors rot. Per-site notes would become a standing maintenance job, not a one-page
  quirk file.
- "Messages sent to the user" live across DMs, group DMs, channels, threads, Activity, huddles.
  The Activity feed is the closest single surface and is still incomplete (no full channel
  history, pagination, edits, deletes, files).
- A 24/7 headed browser on the shared CDP lane (`C:\Users\lndco\edge-cdp`, port 9222) would
  fight every other tool that attaches there. The owner doc says "never open a second,
  uncoordinated CDP browser." A dedicated profile would be required, which is new infra, not
  reuse.
- The gateway today is a headless .NET service (local AppHost or `am-service` on server2). A
  CDP adapter would pin a desktop Edge instance, a display session, and the browser-harness CLI
  on the machine that currently just runs the gateway. That is a different deployment.

CDP *extraction* of the token pair (what `slackcli` does) is not a "Web Driver". It is approach
§2 with a nicer obtain step. The card asked to distinguish those; they are not the same ToS
object (see §5).

---

## 5. ToS / policy — the distinction the card asked for

Three different documents apply, plus the employer (Customer) overlay.

### 5.1 Token-pair unofficial API client — not a gray area, a documented "no"

An `xoxc`/`xoxd` client is an Application that uses the Slack APIs, so the
[API Terms](https://slack.com/terms-of-service/api) (effective 10 October 2025) and the
[Slack App Developer Policy](https://docs.slack.dev/developer-policy/) (effective 10 December
2024) apply.

Developer Policy, Security:

- "Providing access to Slack in any fraudulent or unauthorized way, including **bypassing or
  circumventing Slack protocols and access controls**"
- "**Using unpublished APIs**"
- "Attempting to reverse engineer or otherwise derive source code, trade secrets, or know-how
  in the Slack API"

The OAuth app-install flow *is* Slack's access control for third-party API use. Replaying the
web client's session token is how you skip it. `xoxc`/`xoxd` are unpublished token types
(absent from the official token list). `client.boot` / `client.counts` / the flannel websocket
are unpublished methods. Slack staff said using `xoxc` for the API is unsupported.

API Terms, Access:

- Use APIs only in accordance with the Contract and the Documentation.
- Do not access APIs in a manner that "compromises, breaks or circumvents any of our technical
  processes or security measures".
- "Parts of our APIs are undocumented … you should not rely on their behaviours."

May–October 2025 ToS updates are explicitly about "unsanctioned data scraping" and tightening
unlisted-app bulk history pulls. Developer Policy also forbids using Slack Data to **train** an
LLM; routing inbound Slack into an Antiphon agent is *use*, not training, but it is the same
sensitivity class Slack has been hardening.

Enforcement Slack has already built: cookie-hijack invalidation (2024), shortened `d` TTL
(2025), rate limits on history methods (2025). Practical outcomes for this operator: session
killed (user kicked out of the real browser too, because it is the same `d` cookie), token
useless, in a bad case account or workspace disable. For an **employer** workspace the Customer
(employer) owns the data under the [User Terms](https://slack.com/terms-of-service/user) and
typically forbids unsanctioned integrations in the first place — that is a second, independent
"no".

This is the pattern slackdump / mautrix / slack-mcp-server / slackcli use. Slack has not
mass-killed those tools, and personal-script enforcement is historically rare. That is not the
same as "safe to ship as a first-party Antiphon channel." Those projects are third-party
clients the user chooses to run; Antiphon putting the pattern in-tree makes *this* repo the
client Slack would look at.

### 5.2 CDP driving the official web UI — cleaner, not clean, and the wrong product

User Terms incorporate Salesforce's
[Acceptable Use and External-Facing Services Policy](https://www.salesforce.com/en-us/wp-content/uploads/sites/4/documents/legal/Agreements/policies/ExternalFacing_Services_Policy.pdf)
(last updated 8 July 2025). That AUP is **not** a LinkedIn-style "no robots on our UI" clause.

Relevant bits:

- **6.A.XXIV** bans accessing a *third-party* web property for scraping/crawling/monitoring
  through a Salesforce service without a proper User-Agent and robots.txt. That is about using
  Slack/Salesforce *as the scraper*, not about automating slack.com.
- **6.A.XXII** bans significant load or security testing without written consent.
- **6.A.I.c** bans mining/harvesting web properties for email addresses / account information.
- No paragraph says "you may not use RPA or a headed browser on your own Slack tab."

So the card is right that CDP sits on **more solid ground** than token extraction:

| | Token-pair API client | CDP Web Driver |
|---|---|---|
| Uses Slack APIs as an Application | Yes → API Terms + Developer Policy | No, if it never calls `slack.com/api` itself |
| Unpublished APIs / unpublished tokens | Yes | No (the official web client does, on Slack's behalf) |
| Circumvents app-install access control | Yes, that is the mechanism | No |
| Slack staff "not supported" | Explicit | Not addressed |
| Cookie-theft detectors | In the blast radius | Same cookies stay in the real browser |
| AUP | API Terms are the better fit | AUP does not clearly forbid own-account UI automation |
| Employer Customer policy | Almost certainly forbidden | Often still forbidden as "unsanctioned automation" |
| Fits `IChannelAdapter` | Yes | No (see §4) |

"More solid" is not "solid enough to build a standing gateway on." A 24/7 DOM scraper of
workplace chat is still automated access to Customer Data, still visible as a non-human usage
pattern if anyone looks, and still a bad engineering bet (SPA rot, shared CDP lane, headed
browser). The existing CDP convention in this repo is **occasional, operator-attended, real-site
ops** (fill a form, create an app, check Outlook). Stretching that into a channel provider
would be a new product, not a reuse.

### 5.3 The ToS-clean paths that actually exist

1. **Current bot adapter** (`xoxb` + `xapp`, Socket Mode). Already shipped. Replies as the bot.
   Hears only conversations the bot is in. Needs an app install.
2. **Official user token** (`xoxp-`). Documented, in the token table, "work directly on behalf
   of users." Still a Slack app; still typically admin approval for `im:history`,
   `groups:history`, `chat:write` as the user. This is the supported answer to "post as me."
   slack-mcp-server documents it as Option 2 and prefers it over `xoxc`/`xoxd`.

If the reason for CARD-0385 is "the workspace will not approve any app", neither unofficial
path makes that a good idea. If the reason is "I want DMs sent to me, as me", the supported
move is an `xoxp` app, not a web-session steal.

---

## Integration shape, if this were built anyway

Only so the decision is informed, not as a recommendation to proceed.

A new `IChannelAdapter` (do **not** overload channel key `"slack"` — that is the bot adapter).
Something like `"slack-user"` / `"slack-web"`. Same Kafka topics, same `ReceiveAsync` /
`SendAsync`, same Bitwarden-not-in-git custody. Gateway still holds the credential; Antiphon
server never sees it (`docs/messaging/build-your-own-gateway.md`).

| Approach | Ingress | Egress | Credential custody | Fit |
|---|---|---|---|---|
| Token-pair | `rtm.connect` WebSocket (mautrix pattern) or poll `conversations.history` | `chat.postMessage` as the user | `xoxc` + `d` (+ `d-s`) in a new Bitwarden item; expect rotation; never log | Matches the existing Slack adapter's HTTP/WS stack. This is the only unofficial shape that can meet the channel contract. |
| CDP Web Driver | DOM mutation / Activity-page scrape on a **dedicated** Edge profile | Trusted keystrokes into the composer | Session stays in the browser profile | Does not match the gateway. Wrong process, wrong machine assumptions, no reliable inbox. |
| CDP-sniff hybrid (§6) | `Network.webSocketFrameReceived` on the Flannel socket of a dedicated headed tab | Trusted keystrokes into the composer (or, in practice, the token from the handshake + `chat.postMessage`) | "Don't persist the handshake URL" is a discipline; the events contain `xoxc` and `d` | Better detector than DOM scrape; same egress and deployment problems as the Web Driver. Becomes token-pair the moment replies or catch-up have to work. |

A hybrid "CDP only to refresh the pair, HTTP for the actual channel" is still the token-pair
client. The obtain step being automated does not change the ToS object.

---

## 6. Follow-up: passive CDP WebSocket sniffing (task `0c93073f`)

New question from the operator, after the original investigation landed: instead of
extracting the `xoxc`/`xoxd` pair to make our own authenticated API calls, can CDP be used
**purely passively** — watching the already-logged-in browser tab's own network traffic — to
notice when a new Slack message arrives, without ever extracting or reusing the session
credential ourselves?

Short answer: **detection is technically real; it is not a ToS-clean gateway, and the hybrid
does not stay token-free once you want reliable replies.** Do not build this either.

### 6.1 Does CDP inspect WebSocket traffic on a live page?

**Yes. This is a first-class, documented Network-domain feature, not an exotic hook.** It is
the same protocol Chrome DevTools itself uses for the Network panel's WebSocket **Messages**
tab.

Stable CDP 1-3 (and tot, retrieved 2026-09-05) define, after `Network.enable`:

| Event | What it fires |
|---|---|
| `Network.webSocketCreated` | Socket opened. Parameters include `url`. |
| `Network.webSocketWillSendHandshakeRequest` | About to send the HTTP upgrade. Includes request headers. |
| `Network.webSocketHandshakeResponseReceived` | Upgrade response available. |
| `Network.webSocketFrameReceived` | A WebSocket **message** arrived. |
| `Network.webSocketFrameSent` | A WebSocket **message** was sent. |
| `Network.webSocketFrameError` | A WebSocket message error. |
| `Network.webSocketClosed` | Socket closed. |

Citations:

- [CDP Network domain, stable 1-3](https://chromedevtools.github.io/devtools-protocol/1-3/Network/) — events listed above, plus the `Network.WebSocketFrame` type.
- [CDP Network domain, tot](https://chromedevtools.github.io/devtools-protocol/tot/Network/#event-webSocketFrameReceived) — same events; tot also has WebTransport and DirectSocket variants that Slack messaging does not currently use.
- Chrome DevTools docs, [Analyze the messages of a WebSocket connection](https://developer.chrome.com/docs/devtools/network/reference) — the UI that those events power.

`Network.WebSocketFrame` (stable 1-3 wording):

> WebSocket message data. This represents an entire WebSocket message, not just a fragmented
> frame as the name suggests.

Fields: `opcode` (number), `mask` (boolean), `payloadData` (string). Opcode `1` is text and
`payloadData` is UTF-8. Any other opcode is binary and `payloadData` is base64. Chrome
delivers the **decompressed application message** — if the socket negotiated
`permessage-deflate`, that is already undone before the event.

CDP also has `Network.eventSourceMessageReceived` for SSE. If Slack had moved off WebSockets
to EventSource, that would still be a documented inspect path. It has not (see §6.2).

Operational caveats that are CDP, not Slack:

- Events only flow **after** `Network.enable` on that target. Attach to a tab whose Flannel
  socket is already up and you see nothing until the client reconnects (or you reload).
- The socket may live on a worker / shared-worker target rather than the page target.
  DevTools handles this; a CDP client must `Target.setAutoAttach` (or equivalent) and
  `Network.enable` on the worker session too, or it will watch the wrong target.
- `webSocketCreated.url` and the handshake request headers are part of the event stream. For
  Slack that URL is `wss://wss-primary.slack.com/?token=<xoxc>&gateway_server=<team_id>`
  (slackcli, still current as of 2026-08). The handshake carries the `d=xoxd-…` cookie. A
  "purely passive" subscriber **sees the credential in the events it asked for**, even if it
  never calls `Network.getCookies` or `Runtime.evaluate`. Not persisting it is a coding
  discipline, not a protocol property.

### 6.2 What does Slack's web client actually use for realtime in 2026?

**Still a WebSocket, to Flannel / Gatewayserver, not SSE and not long-poll as the primary
path.** It is also **not** the public RTM API the original investigation discussed for
unofficial `xoxc` clients, though the frame *shape* is the same family.

Evidence current as of this follow-up (2026-09-05), none of it from a live session:

- Slack's own help article [Manage Slack connection issues](https://slack.com/help/articles/360001603387-Manage-Slack-connection-issues) (live): "Slack uses WebSockets over port 443." The connection test at `https://my.slack.com/help/test` still scores **WebSocket (Flannel [Primary])** and **WebSocket (Flannel [Backup])**. Proxies that decrypt TLS must exempt `wss-primary.slack.com`, `wss-backup.slack.com`, and `wss-mobile.slack.com`.
- Slack Engineering, [Traffic 101](https://slack.engineering/traffic-101-packets-mostly-flow/) (2023, still the current public architecture write-up): first-party clients send and receive messages over WebSockets ingested by `envoy-wss`, DNS `wss-primary.slack.com` / `wss-backup.slack.com`. Routing is to **Gatewayserver** (first-party) or **Applink** (Socket Mode for apps — that is what `SlackChannelAdapter` already uses). **Flannel** is the edge cache first-party clients sit on.
- slackcli (`github.com/cgrossde/slackcli`, docs dated 2026-08-31) still dials that same first-party gateway after `client.userBoot`:
  `wss://wss-primary.slack.com/?token=<xoxc>&gateway_server=<team_id>`, then reads JSON
  events in a loop and pings with `{"type":"ping","id":N}` every 30 s.

HTTP is still used for boot, history, search, and `client.counts`. That is fallback and
hydration, not the live inbox. Socket Mode (`wss-primary.slack.com/link`, `xapp-`) is the
*app* path and is unavailable without an app — same as §3.

So: a CDP sniffer on the logged-in web tab is watching Flannel JSON, not RTM-as-an-app and
not Events API envelopes.

### 6.3 What do the frames look like — enough to detect "a new message arrived"?

**Mostly yes for a coarse detector, on plaintext JSON, with unpublished-protocol fragility.**
The frames are not application-encrypted. TLS is terminated by Chrome; CDP sees the
decrypted UTF-8 payload.

Unofficial clients of this same socket (slackcli `Event`, mautrix RTM path, the documented
legacy RTM message object) all parse the same envelope family:

```json
{ "type": "hello" }

{ "type": "message",
  "channel": "C024BE91L",
  "user": "U023BECGF",
  "text": "Hello",
  "ts": "1358878749.000002",
  "thread_ts": "1358878749.000001" }

{ "type": "message",
  "subtype": "message_changed",
  "hidden": true,
  "channel": "C024BE91L",
  "message": { "type": "message", "text": "…", "ts": "…" } }

{ "type": "desktop_notification", "…": "…" }
{ "type": "user_typing", "channel": "C…", "user": "U…" }
{ "type": "pong", "reply_to": 1 }
```

slackcli's live stream allowlist (2026-08) is `message`, `reaction_added` /
`reaction_removed`, membership/channel events, `team_join`, and `desktop_notification`.
Everything else (presence, typing, pings, the long tail of Flannel's 100+ event types) is
dropped. That is a workable "something happened in a channel/DM I am in" detector:

- `type == "message"` and no hidden subtype → new visible message.
- `channel` starting `D` / `G` vs `C` distinguishes DM / MPDM / channel.
- `thread_ts` marks thread replies.
- `desktop_notification` is the closest "this would have pinged me" signal.

Why this is still fragile as a product, not as a weekend script:

- **Unpublished and versioned by Slack's web client, not by a public schema.** API Terms:
  undocumented behaviour may change at any time. A field rename, a move of message fanout
  onto a subscription the idle tab has not joined, or a switch from text opcode 1 to binary
  protobuf, and the parser goes dark. Flannel's own 2017/2018 posts already described moving
  presence (and planning more) onto pub/sub so idle clients do not receive every team event.
- **Volume and subtypes.** File shares, unfurls, edits, deletes, channel_join, bot messages,
  and your own echoes all arrive as `type: message` with different `subtype`s. A detector
  that does not filter will either miss file-only DMs or flood the gateway.
- **No history, no catch-up.** CDP does not replay frames from before `Network.enable`. A
  reconnect gap is a missed inbox. The web client heals itself via HTTP (`conversations.history`
  / `client.counts`); a sniffer that refuses to call those APIs cannot.
- **Addressing vs. body.** `channel` + `ts` is a stable handle. The body may be incomplete
  (blocks-only messages with empty `text`, files with no text). Completeness for an agent
  often wants a follow-up HTTP fetch — which is the token-pair client again.
- **Not opaque, not encrypted, not a solved protocol.** Plain JSON today is the thing that
  makes sniffing *tempting*. It is also why Slack can change it without a deprecation notice.

Reliable enough to light a "you have a new DM" lamp. Not reliable enough to be
`ReceiveAsync` for a standing `IChannelAdapter`.

### 6.4 ToS / policy for *this* approach, honestly

Passive observation of your own already-authenticated browser session is **meaningfully
different** from extracting `xoxc`/`xoxd` and calling `slack.com/api` from a .NET process.
It is **not** meaningfully *safe*, and "passive" is doing a lot of work in the question.

What actually gets better versus §5.1:

| | Token-pair API client (§2) | CDP-sniff of the live tab |
|---|---|---|
| Independent `slack.com/api` calls as an Application | Yes | No, if the sniffer never issues its own requests |
| Replays `d` cookie from another User-Agent / IP | Yes — cookie-hijack detector blast radius | No — cookie stays in the real browser |
| Circumvents app-install as the *mechanism of API access* | Yes | No API access of our own |
| Unpublished token types used by *us* | Yes (`xoxc`/`xoxd`) | We see them in the handshake; using them is a choice |
| Cookie-theft / session-kill risk from our traffic pattern | Material | Lower (we are not a second client) |

What does not get better, or gets only slightly better:

1. **The handshake *is* the credential.** `Network.webSocketCreated.url` contains `token=xoxc-…`.
   Handshake request headers contain `Cookie: d=xoxd-…`. A subscriber that logs events, dumps
   them for debugging, or even holds the last `webSocketCreated` URL in memory has extracted
   the pair. The original investigation's "this investigation did not extract" bar is easy to
   trip without calling `getCookies`. Any hybrid that later "just" posts via `chat.postMessage`
   is the §2 client with a different obtain step.

2. **Parsing Flannel is reverse-engineering an unpublished protocol.** Developer Policy
   (10 Dec 2024) forbids Applications from "Using unpublished APIs" and from "Attempting to
   reverse engineer or otherwise derive source code, trade secrets, or know-how in the Slack
   API." Whether a CDP sniffer is an "Application that uses the Slack APIs" is the grey bit:
   we are not calling the APIs, we are decoding the first-party client's private socket.
   Functionally we are still a third-party consumer of Slack's unpublished realtime protocol.
   "The user could open DevTools and look" is true and is not the same as a 24/7 process
   shipping every frame into Kafka.

3. **Automated ingest of Customer Data is the product.** User Terms: the employer Customer
   owns the data. May–October 2025 API ToS updates were aimed at "unsanctioned data
   scraping." Sniffing every `type: message` off the wire and handing it to an Antiphon
   agent is scraping by another transport. Routing into an agent is *use*, not LLM
   *training* (the explicit Developer Policy ban), but it is the same sensitivity class.
   Salesforce AUP 6.A.XXIV still does not say "no RPA on your own tab"; it also does not
   bless standing capture of workplace chat.

4. **Employer overlay is unchanged.** An unsanctioned always-on reader of private channels
   and DMs on a Customer workspace is a policy problem whether the bytes came from
   `conversations.history` or from the tab's own socket.

Honest ranking, lowest to highest legal/policy heat for *this operator*:

1. Shipped bot adapter / official `xoxp` (supported).
2. Occasional, operator-attended CDP clicks on the official UI (existing lane; §5.2).
3. CDP-sniff detection only, no persist of handshake URL/headers, no independent API calls,
   no 24/7 inbox — still unpublished-protocol reverse engineering, but no second client.
4. CDP-sniff + DOM reply hybrid as a standing gateway (this section's proposal used as a
   product) — 24/7 headed capture plus UI automation.
5. Token-pair unofficial API client (clear Developer Policy / API Terms "no").

(3) is better than (5). It is not a thing this repo should ship. The "passive = I didn't
touch the cookie" framing collapses the moment the process is unattended, stores frames, or
needs to reply.

### 6.5 Hybrid: sniff for detect, DOM-automate for reply, never extract the token?

**Coherent as occasional attended ops. Not coherent as `IChannelAdapter`. In practice it
grows a token.**

Detection via sniff can work (with the gaps in §6.3). Reply without the token is exactly
§4's CDP Web Driver: trusted keystrokes into the composer on a dedicated headed profile.
The new information from the socket (`channel` + `ts`) is a *better address* than scraping
the Activity DOM — you could navigate to
`https://<workspace>.slack.com/archives/<channel>/p<ts-without-dot>` or a `slack://`
deeplink and type. That is a real improvement over "watch the Activity feed and hope."

It still does not become a gateway:

- `SendAsync` has to steal the focused tab, navigate away from whatever the human (or the
  last reply) was looking at, wait for the SPA, find the composer, type, send, and restore.
  Selectors still rot. Threads, huddles, canvases, and "channel not in the sidebar" still
  fail. Two outbound messages in flight contend for one UI.
- The shared CDP lane (`C:\Users\lndco\edge-cdp`, port 9222) still cannot host this 24/7
  next to every other real-site tool. A dedicated profile is new infra.
- The gateway process is still a headless .NET service. Pinning a desktop Edge instance is
  still a different deployment.
- Catch-up, edits, deletes, files, and "what did I miss while CDP was detached" still want
  HTTP, which wants the pair.

The pressure toward the token is mechanical, not moral. After `Network.enable` you already
have `xoxc` in the socket URL. `chat.postMessage` as the user is one `HttpClient` call,
does not fight the SPA, and returns a stable `ts`. Every unofficial client that started as
"just watch the browser" ended there (slackcli: CDP to *extract*, then Web API + its own
WebSocket; this hybrid would be CDP to *sniff*, then DOM until DOM hurts). Shipping the
hybrid as a first-party channel is how Antiphon acquires a token-pair client in all but
name, with a worse inbox.

So: the hybrid is a reasonable description of **operator-attended** "tell me when this DM
lands, then I'll (or an agent will) type a reply in the real tab." It is not a design for
the messaging gateway, and it does not stay token-free if anyone asks it to be reliable.

---

## 7. Follow-up: bounded per-thread tabs (task `b13d0092`)

New question from the operator after the card was closed as "do not build" and then
reopened: instead of a 24/7 gateway across the whole workspace, open **one** already-logged-in
browser tab per *watched Slack thread* (a handful, not the inbox) via this repo's existing
shared CDP lane, observe only that tab — DOM mutation/read, or `Network.webSocketFrameReceived`
on that tab's CDP target — and signal Antiphon to go check. Never extract, persist, or reuse
the `xoxc`/`xoxd` pair outside the live browser context.

This section does not redo §1–§6. Token-pair remains a Developer Policy / API Terms "no."
A full-workspace CDP gateway remains the wrong shape. The question is whether *bounding
the tabs* changes either verdict.

### Verdict for this angle

**Still do not build as a standing Antiphon service or channel. Do not build a bounded
prototype either.** On its own terms the idea is a *lamp* (nudge when this thread moved),
not `IChannelAdapter`. A DOM-only lamp on two or three tabs the operator already has open
is mechanically plausible as an attended ops recipe in the existing lane. Bounding the
tabs does **not** make unattended CDP observation the same object as a human leaving Slack
open, does not change the ToS category enough to ship, and cannot be hosted 24/7 by the
browser-harness tooling this repo already has.

### 7.1 ToS: does bounding the tabs change the analysis?

Three comparisons, because they are different questions.

**Vs. the token-pair unofficial API client (§5.1).** Yes, still meaningfully better, for
the same reasons as §6.4: no independent `slack.com/api` calls, the `d` cookie stays in
the real browser, no second User-Agent/IP replaying it. The number of threads is irrelevant
to that comparison; staying inside the official client is what matters.

**Vs. a 24/7 full-workspace CDP gateway / full Flannel sniffer (§4, §6).** Yes, smaller.
Less Customer Data in scope, less resemblance to an inbox replacement, closer to the
existing "occasional real-site ops" convention in `docs/external-site-operations.md`. A
detector that only fires "this thread moved" and then a human (or an attended agent) looks
at the already-open tab is the product §6.5 already called coherent as occasional attended
ops.

**Vs. a human leaving Slack open in a few browser tabs.** No. That is the analogy the
question asked for, and it does not hold.

A human with three Slack tabs open is using Slack's official client the way Slack designed
it. "Open in new window" is a first-party desktop feature
([Open separate windows in Slack](https://slack.com/help/articles/4403608802963-Open-separate-windows-in-Slack)).
The bytes that move are Slack's own JS talking to Flannel. Presence heartbeats, connection
tests, and session-duration rules all assume this.

A standing CDP subscriber on those same tabs is a different actor:

1. **Purpose.** The output is a signal into Antiphon — automated ingest of Customer Data
   (User Terms: the employer Customer owns it). Slack's May–October 2025 API ToS updates
   targeted "unsanctioned data scraping." Scoping the scrape to three threads does not
   stop it being a scrape. Volume is a rate-limit concern; category is a policy concern.
2. **Unattended duration.** A human glancing at DevTools' Network/Elements panel is the
   same *protocol* (`Network.enable` / `DOM.enable` is what DevTools itself uses). A
   process that holds those domains open 24/7 and forwards events is not a human with
   DevTools open. Slack policy talks about Applications and automated access, not about
   whether you used the DOM or the socket.
3. **WS sniff is still unpublished-protocol reverse engineering**, whether the tab's URL
   is a thread permalink or the workspace root. Developer Policy still forbids using
   unpublished APIs and reverse-engineering the Slack API. §6.4's grey bit ("is a CDP
   sniffer an Application that uses the Slack APIs?") is unchanged by tab count.
4. **DOM-only is the cleanest variant of this angle**, and the only one that actually
   resembles "watching the UI." Salesforce AUP 6.A.XXIV still does not ban RPA on your
   own tab. The employer overlay (unsanctioned automation of workplace chat) is still a
   second, independent "no" on a Customer workspace.
5. **N full Slack SPAs is not quieter than one.** A thread permalink
   (`https://<workspace>.slack.com/archives/<id>/p<ts>?thread_ts=…&cid=…`,
   [`chat.getPermalink`](https://docs.slack.dev/reference/methods/chat.getPermalink/))
   still boots the full web client, not a lightweight thread widget. If each tab opens
   its own Flannel socket (the usual web-chat default; Slack-specific SharedWorker sharing
   was **not** confirmed — no live attach this pass), N tabs is N `client.userBoot`s and
   N sockets from one user. That is *more* visible than one tab, not less. Flannel's 2017
   pub/sub plan was explicitly to stop idle clients receiving every team event; slackcli
   (docs 2026-08-31) still reads workspace-wide `message` events off the first-party
   gateway. A "thread tab" does not, on current public evidence, give you a thread-scoped
   socket.

Honest ranking, lowest to highest legal/policy heat, inserting this angle into §6.4's
list:

1. Shipped bot / official `xoxp` (supported).
2. Occasional, operator-attended CDP clicks (existing lane).
3. **Bounded per-thread tabs, DOM-mutation lamp only, no `Network.enable`, no persist,
   operator (not a 24/7 agent) goes to look** — closest to (2); still unattended
   monitoring if the process is standing.
4. CDP-sniff detection only (§6.4 item 3) — unpublished protocol, and possibly
   workspace-wide frames even on a thread URL.
5. Bounded-tab WS sniff + "signal Antiphon to go check" as a standing service — this
   follow-up's proposal used as a product.
6. Full CDP-sniff + DOM-reply gateway (§6).
7. Token-pair unofficial API client.

(3) is the only version of this angle that is ToS-distinct from §6 in a way that matters.
It is still not "just like leaving tabs open," and it is not something this repo should
ship unattended on an employer workspace. If the workspace is one the operator admins
(personal / family), the employer overlay drops and (3) becomes an attended recipe in
the existing lane — which is already allowed, and does not need a prototype.

### 7.2 Mechanics: can CDP notice a new message in seconds without looking like scraping?

Two detectors. They are not equivalent, and only one is actually bounded to the thread.

**A. DOM mutation / read — thread-scoped, renderer-dependent**

CDP can observe DOM changes without polling Slack:

- `DOM.enable` + `DOM.getDocument` implicitly starts DOM events.
  `DOM.childNodeInserted` / `childNodeRemoved` / `characterDataModified` fire for nodes
  the client has already requested
  ([CDP DOM 1-3](https://chromedevtools.github.io/devtools-protocol/1-3/DOM/#event-childNodeInserted):
  "Backend keeps track of the nodes that were sent to the client and never sends the
  same node twice").
- More robust for a React SPA: inject a `MutationObserver` via `Runtime.evaluate` or
  `Page.addScriptToEvaluateOnNewDocument`
  ([CDP Page tot](https://chromedevtools.github.io/devtools-protocol/tot/Page/#method-addScriptToEvaluateOnNewDocument))
  in an isolated world, and have it signal the CDP client. That is event-driven. It does
  not hit Slack's HTTP/WS as a scraper. Local `drain_events()` polling of the CDP daemon
  is not Slack traffic.

Why this can work in seconds on a *visible, unfrozen* thread tab: Slack's web client
already renders new thread replies into that pane; the observer sees the `childList`
mutation when the SPA commits it.

Why it is not a reliable 24/7 detector:

- Slack web is a virtualized React list with hashed class names (§4). The observer has
  to watch a region, not a stable CSS selector. `DOM.documentUpdated` invalidates all
  node ids on document replacement.
- **Hidden tabs.** browser-harness `SKILL.md`: `document.visibilityState === 'hidden'`
  means Chrome throttled the page. Chrome 57+ throttles background timers; WebSockets
  are exempt from the *timer-budget* kill
  ([Background tabs in Chrome 57](https://developer.chrome.com/blog/background_tabs/)),
  but the SPA may not *paint* new messages into a virtualized list while hidden.
  `MutationObserver` only fires if the renderer actually mutates the DOM.
- **Freeze.** Chrome 133+ (February 2025) freezes CPU-intensive hidden tabs when Energy
  Saver is on: event handlers including network/sensor, timers, and promise resolvers
  are suspended
  ([Freezing on Energy Saver](https://developer.chrome.com/blog/freezing-on-energy-saver)).
  A frozen tab will not run the observer. CDP `Page.setWebLifecycleState` can force
  `active` / `frozen` (experimental,
  [Page tot](https://chromedevtools.github.io/devtools-protocol/tot/Page/#method-setWebLifecycleState))
  — using it to keep Slack "active" while hidden is itself a tell and is not a
  human-like tab.
- **Discard.** Memory Saver unloads the tab; DOM is gone; WS is gone; next look is a
  full reload. `document.wasDiscarded` is only visible after that reload.
- Catch-up: if the tab was frozen or discarded, the SPA heals itself via HTTP when it
  next runs. A DOM observer that was dead during the gap has no record. Same "no
  history" hole as §6.3, on the renderer side.

Latency when it works: sub-second to a few seconds after Slack's own UI commit — inside
"seconds, not minutes." Latency when the tab is hidden, frozen, or discarded: unbounded
until the tab is foregrounded again.

**B. `Network.webSocketFrameReceived` on that tab's CDP target — not thread-scoped**

Capability is unchanged from §6.1: first-class CDP, plaintext JSON on
`wss-primary.slack.com`, `type: "message"` is a coarse detector.

What bounding the tab does **not** do:

- It does not give you a thread-only socket. The first-party gateway slackcli still
  consumes is workspace-scoped. Filtering `thread_ts` / `channel` in the sniffer is
  *our* filter after seeing every frame — i.e. we still parsed the unpublished stream.
- It may not even live on the page target. §6.1: the socket may be on a worker; a CDP
  client must `Target.setAutoAttach` and `Network.enable` on that session too.
  browser-harness does not call `Target.setAutoAttach` (only `Target.attachToTarget`,
  `daemon.py` / `helpers.py`).
- Events only after `Network.enable`. Attach to an already-open tab and you see nothing
  until reconnect or reload.
- The handshake URL still contains `token=xoxc-…` and the cookie. A "purely passive"
  subscriber still *sees* the pair. Not persisting it remains a coding discipline.

Latency: seconds, when the socket is up and the sniffer is attached. Not polling Slack.
The scraping-look problem for WS is not request rate; it is that we are a third-party
decoder of Flannel.

**Which detector matches the brief?** The brief wants "observe only that tab" and
"scoped to just that page's own connection." DOM mutation is the one that is actually
scoped to the thread the tab is showing. WS sniff on that tab is a workspace sniffer
that happens to be attached to a thread-shaped URL.

Polling `js("messagePane.innerText")` every 500 ms would look like scraping *and* would
trip freeze / CPU-intensive heuristics. Event-driven DOM or WS is the only shape that
satisfies "seconds without aggressive polling."

### 7.3 Keeping N tabs open long-term with this repo's browser-harness

The existing lane is **occasional, operator-attended, one-current-tab**. It is not a
standing N-tab watcher. Evidence from the tooling, not from a design:

| Need | What the harness actually is |
|---|---|
| Long-lived attach to N targets | One daemon, one `self.session` / `self.target_id` (`C:\src\browser-harness\src\browser_harness\daemon.py` around the `Daemon` fields and `attach_first_page`). `switch_tab` replaces the current session. Named daemons get one dedicated tab, not N. |
| Event stream from background tabs | Global `deque(maxlen=500)` (`BUF = 500` in `daemon.py`). Helpers already document that a previously-attached background tab keeps emitting `Network.*` into this buffer and poisons `wait_for_network_idle` (`helpers.py`). One Slack Flannel socket will rotate that buffer in well under a minute. |
| DOM-only (no credential in events) | Every attach / `set_session` runs `_enable_default_domains` = `Page`, `DOM`, `Runtime`, **`Network.enable`** (`daemon.py`; regression-tested in `tests/unit/test_daemon.py`). There is no "don't enable Network" switch. Enabling Network is how the handshake URL with `xoxc` lands in `drain_events()`. |
| Don't deface the tab | `_mark_tab()` prepends a horse emoji to `document.title` on attach and on every `Page.loadEventFired`. |
| Shared lane | `docs/external-site-operations.md`: dedicated Edge profile `C:\Users\lndco\edge-cdp`, port 9222, "never open a second, uncoordinated CDP browser." `C:\src\claudebot\launch-edge-cdp.ps1` without `-KeepTabs` / `-EnsureOnly` **kills every `msedge` process** and relaunches. |
| Visible vs hidden | `switch_tab` does not change Chrome's visible tab (`interaction-skills/tabs.md`). Screenshots and CDP input work on background tabs; pages that pause while `visibilityState=hidden` need `activate_tab` or `Emulation.setFocusEmulationEnabled`. Slack's SPA is in the latter class. |
| Worker sockets | No `Target.setAutoAttach`. |
| Standing process | `browser-harness` is a CLI that execs a script and exits. The daemon holds the browser WS, not a user-supplied watcher loop. |

Live check this pass (2026-09-05, `GET http://127.0.0.1:9222/json` only — no target
attach, no cookies, no frames): the shared lane is up. Open page targets were a HiBob
login tab and the Links dashboard, plus extension backgrounds. **No Slack tab.** The
profile is already doing other real-site work. Pinning 3–5 Slack SPAs here would fight
that work and would be wiped by the next un-flagged `launch-edge-cdp.ps1`.

What a standing watcher would actually have to do (mechanism, not a recommendation):

1. Persist the list of **thread permalinks** (public URL shape, not credentials).
2. Own a CDP client that is *not* the harness CLI: `Target.attachToTarget` per tab, keep
   N sessions, do **not** `Network.enable` if the detector is DOM-only,
   `Target.setAutoAttach` if the detector is WS.
3. Detect death: `Target.targetDestroyed` / `Target.detachedFromTarget`; URL moving to a
   sign-in / SSO host; `Page.lifecycleEvent` after `Page.setLifecycleEventsEnabled`;
   `Runtime.evaluate` of `document.visibilityState` / login copy. Session duration can
   also kill the cookies out from under the tabs (`admin.users.session.setSettings`,
   including `desktop_app_browser_quit` — §2).
4. After browser restart: `launch-edge-cdp.ps1 -EnsureOnly` brings the profile back;
   cookies may survive; **tabs do not**, unless Edge "continue where you left off" is
   on. Re-`createTarget` and navigate each permalink. If SSO is needed, stop and ask
   (existing CDP login-wall rule).
5. After Memory Saver discard: the target is a new renderer; re-inject the observer;
   `Network.enable` again sees no frames until the SPA reconnects.
6. Stay out of the way of every other tool on :9222, or use a **dedicated profile** —
   which §4 already called new infra, not reuse.

None of that is in the current harness. Reusing "the same pattern already used for other
real external sites" covers *opening* a Slack tab the way we open `api.slack.com`. It
does not cover keeping N of them observed unattended.

### 7.4 Practical bound

Constraint is memory and freeze/discard, not a CDP connection cap. Chromium will attach
to tens of page targets; Slack will not.

- Slack web memory: Slack Engineering (2017, [Reducing Slack's memory footprint](https://slack.engineering/reducing-slacks-memory-footprint/))
  ~130 MB p10 to ~960 MB p99 **per team / webview**. A 2026 public RAM benchmark (Zovo)
  put Slack around **190 MB initial / 340 MB active per tab**. Desktop Electron is
  commonly 1–2 GB; that is a different process, but it is the same SPA.
- 3 watched-thread tabs ≈ 0.6–1+ GB in the shared Edge profile that also runs
  HiBob / Outlook / other ops, plus the rest of the operator's session.
- Each tab is a full client boot, likely its own Flannel socket, plus presence
  heartbeats (browser tabs count as a Slack client). N connections from one user is a
  usage-pattern tell if anyone looks; it is also how a human with N windows looks, so
  it is not dispositive.
- Chrome Memory Saver will discard the least-recently-viewed Slack tabs first — they
  are heavy and hidden. Energy Saver freeze hits CPU-intensive hidden tabs. Either
  event blinds a DOM observer and kills catch-up.
- Harness event buffer (500) is unusable as the WS ingest path at N≥1.

**Reasonable bound: 2, at most 3, watched-thread tabs**, and only if they stay in a
window that is not a discard candidate (pinned, or `slack.com` on Memory Saver's
"always keep these sites active" list, or actually visible). At 4–5 this is its own
reliability project (dedicated profile, freeze/discard policy, standing CDP client,
login-wall runbook). Above that it is the 24/7 headed gateway §4 already rejected,
just with more tabs.

Detection risk from Slack-server, DOM-only, no extra API calls: low and similar to a
power user with a few tabs. Detection risk from employer policy / "why is there a
24/7 observer on our chat": unchanged by N.

### 7.5 What this pass did and did not do

Did:

- Read CARD-0385 history (revision 6 reopen reason is this angle) and §1–§6 in full.
- Read `docs/external-site-operations.md`, `C:\src\browser-harness\SKILL.md`,
  `interaction-skills/tabs.md` / `connection.md`, `daemon.py`, `helpers.py`,
  `launch-edge-cdp.ps1`, `tests/unit/test_daemon.py`.
- Retrieved current CDP DOM 1-3 and Page tot (`childNodeInserted`,
  `addScriptToEvaluateOnNewDocument`, `setWebLifecycleState`).
- Listed live CDP targets on :9222 by HTTP `/json` (URLs/titles only).

Did not:

- Attach to any Slack tab.
- Enable Network on a Slack target or drain frames.
- Read cookies, localStorage, or handshake URLs.
- Open Slack, inject a MutationObserver, or measure freeze/discard on this machine.
- Confirm whether this operator's 2026 Slack web client shares one socket across tabs
  via SharedWorker (public sources do not settle it; a live `chrome://inspect/#workers`
  on a logged-in Slack tab would).

Uncertainties that would change *mechanics*, not the ToS verdict:

- SharedWorker vs per-tab Flannel (affects "scoped to that page's own connection" and
  N-socket load).
- Whether 2026 Flannel message fanout is per-view pub/sub (2017 plan) or still
  workspace-wide (slackcli 2026). Affects whether WS sniff can be thread-scoped.
- Whether this Edge profile's Memory Saver / Energy Saver would actually freeze a
  hidden Slack tab overnight. Measurable only with a live tab.
- Target workspace: employer Customer vs operator-admin. Employer overlay is the
  difference between "do not" and "attended recipe in the existing lane."

---

## What was not done

- No implementation.
- No live `d` / `xoxc` read from the operator's browser, cookie DB, or Slack desktop app.
- No `auth.test` against a real workspace.
- No browser automation of slack.com for this card.
- No CDP attach to a live Slack tab, and no capture of real WebSocket frames. Frame shape
  in §6 is from Slack's public RTM docs plus current unofficial clients of the same
  first-party gateway, not from this operator's session. §7 listed `:9222` target URLs
  via HTTP `/json` only.
- Not done, noted: a dedicated long-lived CDP client that attaches to N targets without
  `Network.enable` and injects an isolated-world MutationObserver, persisting only
  permalinks, would be the shape of a lamp if this were ever built.

---

## Decision this card is for

The investigation's recommendation is **do not build** — including the CDP-sniff hybrid
(§6) and the bounded per-thread-tab lamp as a standing service or prototype (§7).
If that is accepted, the card can close (or move to a "won't: use existing Slack bot /
consider official `xoxp` if user-identity is required"). If it is rejected, the only
unofficial implementation that can work as a standing channel is still token-pair, and
that needs an explicit accept of: Developer Policy violation, unpublished APIs, cookie
rotation, Slack's hijack-invalidation pipeline, and employer-policy risk on any workspace
the operator does not own. CDP-sniff does not offer a third implementation path that meets
`IChannelAdapter` without becoming token-pair or remaining a headed ops tool. Bounding
to a handful of thread tabs does not offer a fourth path either: it is an attended lamp
in the existing lane, or new infra that is still automated ingest of Customer Data.

Questions that change the recommendation only at the edges:

1. Is the target workspace one the operator **admins** (personal / family) or an employer
   Customer workspace?
2. Can a Slack app be installed at all? If yes, `xoxp` user-token beats both unofficial
   approaches.
3. Must outbound messages appear as the human user, or is the existing bot identity acceptable?
4. Is the actual want a 24/7 gateway, or an occasional "nudge me when this DM lands"? The
   latter can already be a headed CDP recipe in the existing lane; it is not CARD-0385 as
   written. §7 is that question asked with an explicit bound of "a handful of thread tabs."
   Same answer: attended recipe, not a product.

---

## Sources

**Slack / Salesforce (canonical)**

- [Tokens](https://docs.slack.dev/authentication/tokens) — official types; no `xoxc`/`xoxd`.
- [User Terms](https://slack.com/terms-of-service/user) (17 Feb 2023) — AUP incorporated;
  Customer owns Customer Data.
- [API Terms](https://slack.com/terms-of-service/api) (10 Oct 2025) — Documentation-only use;
  no circumvention of security measures; undocumented APIs may change.
- [App Developer Policy](https://docs.slack.dev/developer-policy/) (10 Dec 2024) — unpublished
  APIs forbidden; no bypassing access controls; no LLM training on Data.
- [Acceptable Use Policy landing](https://slack.com/acceptable-use-policy) → Salesforce
  [AUP PDF](https://www.salesforce.com/en-us/wp-content/uploads/sites/4/documents/legal/Agreements/policies/ExternalFacing_Services_Policy.pdf)
  (8 Jul 2025).
- [API ToS updates, 29 May 2025](https://docs.slack.dev/changelog/2025/05/29/tos-updates/) —
  unsanctioned scraping / unlisted-app rate limits.
- [Legacy RTM](https://docs.slack.dev/legacy/legacy-rtm-api/) / [`rtm.connect`](https://docs.slack.dev/reference/methods/rtm.connect).
- [Manage session duration](https://slack.com/help/articles/115005223763-Manage-session-duration).
- Slack Engineering, [cookie hijacking invalidation](https://slack.engineering/proactive-measures-against-password-breaches-and-cookie-hijacking) (Jun 2024).
- Slack staff on `xoxc`: [SO 62759949](https://stackoverflow.com/questions/62759949/accessing-slack-api-with-chrome-authentication-token-xoxc).
- [Manage Slack connection issues](https://slack.com/help/articles/360001603387-Manage-Slack-connection-issues) — WebSockets over 443; Flannel primary/backup test; `wss-primary` / `wss-backup` / `wss-mobile`.
- Slack Engineering, [Traffic 101](https://slack.engineering/traffic-101-packets-mostly-flow/) (2023) — `envoy-wss`, Gatewayserver vs Applink, Flannel.
- Slack Engineering, [Flannel](https://slack.engineering/flannel-an-application-level-edge-cache-to-make-slack-scale/).

**Chrome DevTools Protocol (follow-up §6, §7)**

- [Network domain, stable 1-3](https://chromedevtools.github.io/devtools-protocol/1-3/Network/) — `webSocketFrameReceived` / `webSocketFrameSent` / `WebSocketFrame`.
- [Network domain, tot](https://chromedevtools.github.io/devtools-protocol/tot/Network/#event-webSocketFrameReceived).
- Chrome for Developers, [Network features reference — WebSocket messages](https://developer.chrome.com/docs/devtools/network/reference).
- [DOM domain, stable 1-3](https://chromedevtools.github.io/devtools-protocol/1-3/DOM/#event-childNodeInserted) — `childNodeInserted` / `getDocument` / "events only for nodes known to the client."
- [Page domain, tot](https://chromedevtools.github.io/devtools-protocol/tot/Page/) — `addScriptToEvaluateOnNewDocument`, experimental `setWebLifecycleState` (`frozen` / `active`).
- Chrome for Developers, [Background tabs in Chrome 57](https://developer.chrome.com/blog/background_tabs/) — timer throttling; WebSocket exemption from the timer-budget kill.
- Chrome for Developers, [Freezing on Energy Saver](https://developer.chrome.com/blog/freezing-on-energy-saver) (20 Jan 2025, Chrome 133) — hidden CPU-intensive tabs frozen; JS event handlers including network do not run.
- [chat.getPermalink](https://docs.slack.dev/reference/methods/chat.getPermalink/) — thread permalink shape `…/archives/…/p…?thread_ts=…&cid=…`.
- Slack Help, [Open separate windows in Slack](https://slack.com/help/articles/4403608802963-Open-separate-windows-in-Slack) — first-party "open thread in new window" is the desktop app, not a lightweight web widget.
- Slack Engineering, [Reducing Slack's memory footprint](https://slack.engineering/reducing-slacks-memory-footprint/) (2017) — ~130–960 MB per team webview.
- Zovo, [RAM Benchmark: Website Memory Usage](https://zovo.one/research/ram-benchmark) (2026) — Slack ~190 MB initial / ~340 MB active per tab.

**Unofficial clients / write-ups (mechanism)**

- PaperMtn, [Retrieving and Using Slack Cookies](https://www.papermtn.co.uk/retrieving-and-using-slack-cookies-for-authentication/) (updated Dec 2025, TTL change).
- Shaharia Azam, [xoxc/xoxd with the Go SDK](https://shaharia.com/blog/slack-browser-tokens-golang-sdk-bypass-app-creation/) (Sep 2025).
- [slack-mcp-server auth setup](https://github.com/korotovsky/slack-mcp-server/blob/master/docs/01-authentication-setup.md).
- [mautrix-slack authentication](https://docs.mau.fi/bridges/go/slack/authentication.html) and
  [cookie login source](https://github.com/mautrix/slack/blob/v0.2511.0/pkg/connector/login-cookie.go).
- [slackdump manual login](https://github.com/rusq/slackdump/blob/master/doc/login-manual.md).
- [slackcli](https://pkg.go.dev/github.com/cgrossde/slackcli) (CDP extraction + Web API).
- [slackcli internal/slack](https://pkg.go.dev/github.com/cgrossde/slackcli/internal/slack) (2026-08-31) — first-party gateway URL shape, JSON event envelope, `AllowedEventTypes`.
- wee-slack [PR #857](https://github.com/wee-slack/wee-slack/pull/857) (`xoxc` + `d`, later `d-s`).

**This repo**

- `docs/external-site-operations.md`, `docs/telegram.md`, `docs/slack-bot-ops.md`,
  `docs/messaging/build-your-own-gateway.md`,
  `src/Antiphon.Messaging.Slack/SlackChannelAdapter.cs`,
  `src/Antiphon.Messaging/IChannelAdapter.cs`.

**browser-harness / shared CDP lane (follow-up §7)**

- `C:\src\browser-harness\src\browser_harness\daemon.py` — one session; `BUF = 500`;
  `_enable_default_domains` always includes `Network.enable`.
- `C:\src\browser-harness\src\browser_harness\helpers.py` — `switch_tab` /
  `list_tabs` / `_mark_tab`; `wait_for_network_idle` notes background-tab event leak.
- `C:\src\browser-harness\tests\unit\test_daemon.py` — `set_session` must enable
  Page/DOM/Runtime/Network.
- `C:\src\browser-harness\SKILL.md`, `interaction-skills/tabs.md`,
  `interaction-skills/connection.md`.
- `C:\src\claudebot\launch-edge-cdp.ps1` — kills every `msedge` unless
  `-KeepTabs` / `-EnsureOnly`.
- Live `GET http://127.0.0.1:9222/json` on 2026-09-05: shared lane up; HiBob + Links
  page targets; no Slack tab.
