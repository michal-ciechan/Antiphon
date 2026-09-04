# CARD-0385 — Slack Web Driver feasibility

**Date:** 2026-09-05 (task `dbf28fc6`)
**Card:** CARD-0385 (`6da0d468-327f-4ed7-95a0-509cb1a24d58`)
**Status:** investigation complete. No app code was changed. No live Slack session
credential was extracted or persisted.
**Verified against:** worktree `feat/card-task-dbf28fc6`, `docs/external-site-operations.md`,
`docs/telegram.md`, `docs/slack-bot-ops.md`, `src/Antiphon.Messaging.Slack`, Slack's current
User Terms / API Terms / Developer Policy / Salesforce AUP, and the public unofficial-client
corpus cited below. Nothing here was confirmed by talking to a live Slack session.

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

A hybrid "CDP only to refresh the pair, HTTP for the actual channel" is still the token-pair
client. The obtain step being automated does not change the ToS object.

---

## What was not done

- No implementation.
- No live `d` / `xoxc` read from the operator's browser, cookie DB, or Slack desktop app.
- No `auth.test` against a real workspace.
- No browser automation of slack.com for this card.

---

## Decision this card is for

The investigation's recommendation is **do not build**. If that is accepted, the card can close
(or move to a "won't: use existing Slack bot / consider official `xoxp` if user-identity is
required"). If it is rejected, the only unofficial implementation that can work is token-pair,
and that needs an explicit accept of: Developer Policy violation, unpublished APIs, cookie
rotation, Slack's hijack-invalidation pipeline, and employer-policy risk on any workspace the
operator does not own.

Questions that change the recommendation only at the edges:

1. Is the target workspace one the operator **admins** (personal / family) or an employer
   Customer workspace?
2. Can a Slack app be installed at all? If yes, `xoxp` user-token beats both unofficial
   approaches.
3. Must outbound messages appear as the human user, or is the existing bot identity acceptable?

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

**Unofficial clients / write-ups (mechanism)**

- PaperMtn, [Retrieving and Using Slack Cookies](https://www.papermtn.co.uk/retrieving-and-using-slack-cookies-for-authentication/) (updated Dec 2025, TTL change).
- Shaharia Azam, [xoxc/xoxd with the Go SDK](https://shaharia.com/blog/slack-browser-tokens-golang-sdk-bypass-app-creation/) (Sep 2025).
- [slack-mcp-server auth setup](https://github.com/korotovsky/slack-mcp-server/blob/master/docs/01-authentication-setup.md).
- [mautrix-slack authentication](https://docs.mau.fi/bridges/go/slack/authentication.html) and
  [cookie login source](https://github.com/mautrix/slack/blob/v0.2511.0/pkg/connector/login-cookie.go).
- [slackdump manual login](https://github.com/rusq/slackdump/blob/master/doc/login-manual.md).
- [slackcli](https://pkg.go.dev/github.com/cgrossde/slackcli) (CDP extraction + Web API).
- wee-slack [PR #857](https://github.com/wee-slack/wee-slack/pull/857) (`xoxc` + `d`, later `d-s`).

**This repo**

- `docs/external-site-operations.md`, `docs/telegram.md`, `docs/slack-bot-ops.md`,
  `docs/messaging/build-your-own-gateway.md`,
  `src/Antiphon.Messaging.Slack/SlackChannelAdapter.cs`,
  `src/Antiphon.Messaging/IChannelAdapter.cs`.
