---
name: telegram-e2e-smoke
description: End-to-end smoke-test the Telegram channel pipeline (text, outbound attachments, inbound attachments) using the Antiphon-Family group as the test bed. Use after touching the bridge, dispatcher, messaging contracts, Kafka config, or the server2 gateway — a channel change is NOT done until this passes.
---

# telegram-e2e-smoke — verify the Telegram pipeline end to end

The **Antiphon-Family** Telegram group (chat id `-5370465377`, agent "Family"/Mikey, workspace
`C:\src\ClaudeBot\agents\family`) is the designated test bed — never smoke-test in AZ Care or
other production groups. Config-level checks (agent Running, channel bound) are NOT enough;
two live incidents (2026-07-29: lost dispatcher reply; dropped UTR photo) both passed config
checks. A change is done only when a real message round-trips and the result **renders in the
Telegram chat**.

## The pipeline under test

inbound:  Telegram → gateway `am-service` (server2, downloads attachment bytes via getFile)
          → Redpanda `channels.inbound` (20 MB cap) → desktop bridge (saves attachments to
          `<workspace>\.antiphon\inbox\`, envelopes the message) → agent session
outbound: agent reply (`[[attach: <path>]]` markers) → dispatcher (inlines file bytes)
          → `channels.outbound` → gateway sendMessage/sendDocument → Telegram

## Driving Telegram as Mike

Use browser-harness against the persistent CDP Edge profile (holds Mike's Telegram Web login):

```bash
# CDP Edge must be up on :9222 (see ~/.claude memory reference_browser_test_cdp)
BU_CDP_URL=http://localhost:9222 browser-harness <<'PY'
new_tab("https://web.telegram.org/k/#-5370465377")   # NOTE: hash nav does NOT switch chats on a warm SPA —
PY                                                    # click the chat row and VERIFY the active title first:
# js("document.querySelector('.chat.active .peer-title')?.textContent") must be "Antiphon-Family"
```

Send text: focus composer (`click_at_xy` on it), `type_text(...)`, `press_key("Enter")`.

Send a photo/file (no native dialog): open the paperclip menu, then
```python
cdp("Page.setInterceptFileChooserDialog", enabled=True)
click_at_xy(...)  # "Photo or Video" / "Document" menu item
nodes = cdp("DOM.querySelectorAll", nodeId=cdp("DOM.getDocument", depth=-1)["root"]["nodeId"],
            selector="input[type=file]")["nodeIds"]
cdp("DOM.setFileInputFiles", files=[r"C:\path\to\test.png"], nodeId=nodes[-1])
cdp("Page.setInterceptFileChooserDialog", enabled=False)
# preview popup appears → click its send arrow. Send with NO caption to cover the
# attachment-only shape (the one that dropped Ola's UTR photo).
```

## The verification chain (all four steps, in order)

1. **Channel ingested** — `GET :17202/api/channels`: the Antiphon-Family row's
   `lastMessageAt`/`messageCount` moved (attachment-only messages show `lastMessagePreview: null`).
   If not: gateway or broker. Check `ssh mc@server2 'docker logs am-service --since 5m'` and
   `docker exec am-redpanda rpk topic consume channels.inbound -o -3 -n 3` (always pass `-n`,
   an unbounded consume hangs).
2. **Delivered into the session** — Family's `persistentSessionId` from `/api/agents`, then
   `GET :17202/api/sessions/{sid}/transcript`: a `UserPrompt` with the `[Telegram ...]` envelope;
   photos show `[photo attached: C:\src\ClaudeBot\agents\family\.antiphon\inbox\...]` and that
   file must exist with the right bytes. If the session is mid-turn the envelope waits in
   `SessionQueuedMessages` (WhenIdle) — that's queued, not lost.
3. **Agent replied** — poll the transcript for a `TurnEnd` + `AssistantText` after the prompt's
   sequence (poll with a background `until curl ... ; do sleep 5; done` loop, not fixed sleeps).
   Outbound attachments: the reply text carries `[[attach: <path>]]` markers.
4. **Rendered in Telegram** — screenshot the chat; the reply text AND any document bubble must be
   visible. Only this step closes the loop — a transcript reply alone does not prove delivery
   (dispatcher and gateway can each still drop it). For attachment sends also confirm the message
   on `channels.outbound` carries `attachments` (proves it rode the bus, not a side channel).

## Standard test asks (phone-sized, so Mikey's replies stay cheap)

- Text: "Mikey, formatting test: reply with bold, italic, a bullet list, inline code and a link."
- Outbound file: "Mikey, attachment test: create a small one-page PDF and send it to this chat by
  putting [[attach: <absolute path to the pdf>]] on its own line in your reply."
- Inbound photo: send a generated image containing a known number with NO caption, then:
  "Mikey, what number is in the photo I just sent?" — the reply must quote the number (proves
  inbox save + vision read).
- Multi-line message (the 2026-07-29 fragmentation miss — big pasted bodies MUST reach the agent
  as ONE prompt): send a message with real line breaks whose first line is "ALPHA ..." and last
  line asks "reply with the exact first and last lines of THIS message". Verify the transcript
  `UserPrompt` contains the envelope AND every line (ALPHA through the tail) in ONE entry, and the
  reply quotes both. Sending multi-line via browser-harness: `press_key("Shift+Enter")` does NOT
  work — insert the whole text into the composer instead:
  `js("...ed.focus(); document.execCommand('insertText', false, lines.join(String.fromCharCode(10)))...")`
  then `press_key("Enter")`. (Join JS strings with String.fromCharCode(10) — literal \n inside the
  heredoc-nested JS breaks the evaluator.)

## Gotchas

- **Bind the channel to the agent BEFORE enabling always-on** — a bootstrap reply with no bound
  channel is dropped silently.
- The live session's system preamble is baked at launch; preamble changes (e.g. new attach
  syntax) only apply after a session relaunch — include the syntax in the test message itself.
- Mike uses these agents for real work: check the transcript for an in-flight turn before piling
  on test messages, and keep tests to one message at a time.
- Server restarts: `pwsh -File scripts/restart-apphost.ps1` (dispatcher/bridge changes);
  gateway: tar-sync `src/Antiphon.Messaging*` to server2 `/home/mc/antiphon-messaging/build/src`,
  `docker compose build messaging-service && docker compose up -d messaging-service`.
