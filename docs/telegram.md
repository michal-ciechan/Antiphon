# Telegram integration

How Antiphon talks to Telegram: the gateway service, message formatting (Markdown → Telegram
HTML), inbound normalization, and every knob. Companion docs:
[telegram-bot-ops.md](telegram-bot-ops.md) (standing up a bot + agent),
[messaging-standalone.md](messaging-standalone.md) (deploying gateway instances).

## Architecture in one paragraph

One `Antiphon.Messaging.Service` instance runs per bot token (bot = persona). It long-polls the
Bot API (`getUpdates`, `Telegram__LongPollTimeoutSeconds`, default 30 s — Telegram's documented
long-poll pattern), normalizes updates onto the `channels.inbound` Kafka topic, and consumes
`channels.outbound`, denormalizing each `ChannelReply` into a `sendMessage` call. The adapter is
`src/Antiphon.Messaging.Telegram/TelegramChannelAdapter.cs` — raw Bot API over `HttpClient`, no
third-party client.

## What the chat sees

The chat sees the turn that answers an inbound message, and the agent's reply to an Antiphon
note — a `[task … done|failed|blocked|canceled]` report, a `[check …]` note, or a scheduled
prompt — delivered as a follow-up to the most recent conversation, text and any `[[attach:]]`
files, unless the whole reply is exactly `NO_REPLY`. Follow-up matching is by task id (and the
note's first line), so a Grok transcript that joined the header onto the body still delivers. A bootstrap, restart or compaction note is
never delivered unless it carries `[[attach:]]`. Server-composed pings (blocked-task, decision,
and `ChannelReplyLost` incident pages) also arrive through the same outbound topic when
`Digest:Enabled` and the catalog row is `DigestEnabled`. Catalog `lastMessageAt` is the last
*inbound* message; `lastReplyAt` is the last *outbound* reply — idle between notes is waiting,
not dead.

## Outbound formatting (Markdown → Telegram HTML)

Agents write standard Markdown. Since 2026-07-28 the gateway renders it to **Telegram HTML**
(`parse_mode=HTML`) via `TelegramMarkdownRenderer` before sending, so `**bold**` arrives as
**bold**, not as literal asterisks.

### Why HTML and not MarkdownV2

Telegram offers three parse modes: `HTML`, `MarkdownV2`, and legacy `Markdown` (deprecated).
MarkdownV2 requires escaping **18 punctuation characters** (`_ * [ ] ( ) ~ ` > # + - = | { } . !`)
in *all* prose — one unescaped `.` or `-` anywhere rejects the entire message with
`400: can't parse entities`. HTML needs only `& < >` escaped and is the mode Telegram's own docs
steer programmatic senders toward. The agent's Markdown is therefore *converted* to HTML rather
than escaped into MarkdownV2.

### Supported syntax — the full mapping

| Agent Markdown | Sent as | Telegram renders |
|---|---|---|
| `**bold**` | `<b>` | **bold** |
| `*italic*` or `_italic_` | `<i>` | *italic* |
| `__underline__` | `<u>` | underline (Telegram's own `__` convention — note this differs from CommonMark, where `__` is bold) |
| `~~strike~~` | `<s>` | ~~strikethrough~~ |
| `\|\|spoiler\|\|` | `<tg-spoiler>` | tap-to-reveal spoiler (Telegram's spoiler syntax) |
| `` `inline code` `` | `<code>` | monospace, content never formatted |
| ```` ```lang … ``` ```` | `<pre><code class="language-lang">` | code block with syntax highlighting |
| ```` ``` … ``` ```` (no lang) | `<pre>` | plain code block |
| `[text](url)` | `<a href="url">` | inline link |
| `# Heading` (any level) | `<b>` | bold line (Telegram has no heading entity) |
| `- item` / `* item` / `+ item` | `• item` | bullet (indent preserved for nesting) |
| `1. item` / `1) item` | `1. item` | numbered list (numbers kept as written) |
| `> quoted` (consecutive lines merge) | `<blockquote>` | quoted block |
| Markdown table | `<pre>` | monospace block (Telegram has no tables — keep them rare; phones are narrow) |
| `---` / `***` (horizontal rule) | `———` | visual divider |

Everything else is plain text with `& < > "` HTML-escaped. Guarantees:

- **Code is sacred**: inline/fenced code content gets escaping only — never formatting.
- **Prose survives**: `2*3*4`, `snake_case_names`, lone `*` or `_` are not mangled (the span
  regexes require non-space content and word boundaries).
- **Nothing is dropped**: input the renderer doesn't recognize passes through as readable text.

### Telegram entities we do NOT emit (available via RawOverrides)

Telegram HTML also supports `<tg-emoji emoji-id="…">` (custom emoji — requires a paid bot or
specific setup) and `<blockquote expandable>` (collapsed-by-default quote). The renderer never
emits these; a producer that wants them can bypass conversion entirely with `RawOverrides` (below).

### Fallback — formatting must never cost a delivery

If Telegram rejects the rendered HTML (`400 … can't parse entities …` — a renderer bug or an
edge case), the adapter logs a warning and **resends the original text plain** (no `parse_mode`),
with a fresh retry budget. Worst case is the pre-2026-07-28 behaviour: literal markdown, but
delivered. Transient failures (429/5xx/network) retry as before (`Telegram__SendRetryAttempts`,
honouring `retry_after`).

### Opting out / overriding

- **Per instance**: `Telegram__Formatting=Plain` restores raw-text sends (kill-switch; default is
  `Markdown`).
- **Per message**: a `ChannelReply.RawOverrides` object that sets `parse_mode` (or `text`) wins —
  the adapter skips conversion and passes the overrides straight into the `sendMessage` payload.
  This is the escape hatch for hand-authored MarkdownV2/HTML, custom emoji, expandable quotes,
  `disable_notification`, etc.
- The reply-kind markers (`⏳` progress, `❓` question) are prepended before conversion; they are
  plain emoji and render identically in both modes.

### Length limits

Telegram caps `sendMessage` text at **4096 characters _after_ entity parsing** — HTML tags do not
count. The server already truncates replies at `ChannelBridge:MaxReplyChars` (default 4000) before
they reach the gateway, so rendered messages stay under the cap.

## Inbound

`getUpdates` messages/edits/channel posts are normalized to `ChannelMessage`: text (or caption),
author (+ `IsSelf` when it's the bot), conversation kind (private/group/channel), `@mentions` and
`text_mention` entities, attachments (photo/document/video/audio/voice/sticker), reply-to excerpt,
and the complete native `Update` JSON preserved in `Raw`. Inbound *formatting entities* (bold etc.
in what users type) are not interpreted — agents see the plain text, which is what they want.

Chats not in `Telegram__AllowedChatIds` are dropped at ingress (empty list = allow all — the
current Antiphon-Family setup; set the list to fail closed).

## Settings reference (`Telegram` section / `Telegram__*` env)

| Setting | Default | Purpose |
|---|---|---|
| `BotToken` | — (required) | BotFather token; never commit — env/user-secrets only |
| `BotUsername` | — | Bot's @username (without `@`) so self-mentions are flagged |
| `ApiBaseUrl` | `https://api.telegram.org` | Override for tests (FakeTelegramServer) |
| `AllowedChatIds` | `[]` (allow all) | Inbound chat-id allowlist; non-empty = fail closed |
| `Formatting` | `Markdown` | `Markdown` = render to Telegram HTML; `Plain` = raw text |
| `LongPollTimeoutSeconds` | 30 | `getUpdates` long-poll window |
| `SendRetryAttempts` | 2 | Extra attempts for transient send failures |
| `ErrorBackoffSeconds` | 3 | Backoff when no `retry_after` is provided |
| `MaxRetryAfterSeconds` | 60 | Cap on honoured `retry_after` |

## Bot-side switches that matter (BotFather / group admin)

- **Group Privacy**: must be **off** (or the bot promoted to group admin) for the bot to see
  plain group messages rather than only commands/@-mentions. `/mybots` → Bot Settings → Group
  Privacy. Note: changing it requires re-adding the bot to existing groups, and **no bot can ever
  see messages sent before it joined** — that is a platform rule, not a setting.
- **One bot per group**: Antiphon keys channels on `(provider, chatId)`, so two Antiphon bots in
  the same group would collide on one channel row (see telegram-bot-ops.md).

## Digest channel

Away digests and their loud, one-per-state wake pings are provider-neutral sends, so a Telegram
channel receives them only when both switches are on: `Digest:Enabled=true` in server configuration
and that channel's `DigestEnabled=true`. The feature is intentionally inert until an operator sets
both; `WakeOnBlocked` and `WakeOnDecision` only control their respective pings after a digest
channel exists.

Blocked work pings start `❓ task <8hex> needs an answer — …`; decision pings start
`❓ CARD-nnnn needs a decision — …`. A decision stays in the digest's `❓ Decisions` section until
the card moves out of Needs decision. Set `Digest:PublicBaseUrl` to add the decisions-page link to
decision pings and the app-root footer to digests. Do not use a
family/group channel as the digest channel unless it is deliberately the recipient for every
blocked-task and decision prompt.

## Tests

- `tests/Antiphon.Messaging.Tests/TelegramMarkdownRendererTests.cs` — pins the full mapping
  table above, escaping, and the don't-mangle guarantees.
- `tests/Antiphon.Messaging.Tests/TelegramChannelAdapterTests.cs` — HTML payload shape,
  `Formatting=Plain`, the parse-error → plain fallback, and `RawOverrides` suppression, all
  against the conformance-verified `FakeTelegramServer`.
