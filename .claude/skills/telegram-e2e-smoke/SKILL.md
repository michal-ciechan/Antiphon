---
name: telegram-e2e-smoke
description: Manual, operator-initiated Telegram channel smoke check (text, outbound attachments, inbound attachments) in Family after bridge, dispatcher, messaging contracts, Kafka config, or gateway changes. Family is a real family group; agents must not send test traffic there on their own.
---

# telegram-e2e-smoke — verify the Telegram pipeline end to end

The **Family** Telegram conversation is the authorized destination for this manual,
operator-initiated check (CARD-1243, closed 2026-10-14). Its binding is configured in the
channel settings; verify the bound agent and workspace there. A test message to Family
reaches a real family group. An agent must not send test traffic there on its own.
Deployment or code-change authorization does not authorize this traffic. If the operator
has not initiated the check, report live verification as pending. Use a broker with no real
gateway attached for synthetic fake-gateway traffic, per [channel ops](../../../docs/telegram-bot-ops.md#per-bot-deployment-model).
Config-level checks (agent Running, channel bound) are NOT enough;
two live incidents (2026-07-29: lost dispatcher reply; dropped UTR photo) both passed config
checks. Live end-to-end acceptance requires an observed message round trip whose result
**renders in Family**.

## The pipeline under test

inbound:  Telegram → the configured Telegram gateway (downloads attachment bytes via getFile)
          → its configured broker `channels.inbound` (20 MB cap) → desktop bridge (saves attachments to
          `<workspace>\.antiphon\inbox\`, envelopes the message) → agent session
outbound: agent reply (`[[attach: <path>]]` markers) → dispatcher (inlines file bytes)
          → `channels.outbound` → gateway sendMessage/sendDocument → Telegram

## Operator-initiated Telegram check

Use browser-harness against the persistent CDP Edge profile (holds Mike's Telegram Web login):

```bash
# CDP Edge must be up on :9222 (see ~/.claude memory reference_browser_test_cdp)
BU_CDP_URL=http://localhost:9222 browser-harness <<'PY'
new_tab("https://web.telegram.org/k/")
PY
# Select the Family chat row by name; verify the active title before sending:
# js("document.querySelector('.chat.active .peer-title')?.textContent") must be "Family"
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

1. **Channel ingested** — `GET :17202/api/channels`: the Family row's
   `lastMessageAt`/`messageCount` moved (attachment-only messages show `lastMessagePreview: null`).
   If not: identify this machine's configured Telegram gateway and effective broker using
   [channel ops](../../../docs/telegram-bot-ops.md#per-bot-deployment-model). Check that gateway's
   known health/log location without dumping secrets. The
   [desktop Slack sidecar](../../../docs/slack-bot-ops.md#desktop-slack-sidecar) is not evidence
   of a Telegram gateway. Do not assume a remote host or fall through to SSH when a local
   task is absent. Bound broker sampling with `-n` on `rpk topic consume`.
2. **Delivered into the session** — the bound agent's `persistentSessionId` from `/api/agents`, then
   `GET :17202/api/sessions/{sid}/transcript`: a `UserPrompt` with the `[Telegram ...]` envelope;
   photos show `[photo attached: C:\src\ClaudeBot\agents\family\.antiphon\inbox\...]` and that
   file must exist with the right bytes. If the session is mid-turn the envelope waits in
   `SessionQueuedMessages` (WhenIdle) — that's queued, not lost.
3. **Agent replied** — poll the transcript for a `TurnEnd` + `AssistantText` after the prompt's
   sequence, using bounded foreground polling.
   Outbound attachments: the reply text carries `[[attach: <path>]]` markers.
4. **Rendered in Telegram** — screenshot the chat; the reply text AND any document bubble must be
   visible. Only this step closes the loop — a transcript reply alone does not prove delivery
   (dispatcher and gateway can each still drop it). For attachment sends also confirm the message
   on `channels.outbound` carries `attachments` as broker publication evidence.

## Standard test asks (phone-sized, so Mikey's replies stay cheap)

- Text: "Mikey, formatting test: reply with bold, italic, a bullet list, inline code and a link."
- Outbound file: "Mikey, attachment test: create a small one-page PDF and send it to this chat by
  putting [[attach: <absolute path to the pdf>]] on its own line in your reply."
- Inbound photo: send a generated image containing a known number with NO caption, then:
  "Mikey, what number is in the photo I just sent?" — compare the quoted number with the
  image and inspect the saved inbox file separately.
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
- Server restarts: follow the [canonical restart runbook](../../../docs/apphost-runbook.md)
  for dispatcher/bridge changes. Real gateways have an independent lifecycle outside AppHost.
  For an explicitly chosen remote gateway in the fixed `/home/mc/antiphon-messaging` layout,
  follow [remote deployment operations](../../../docs/telegram-bot-ops.md#deploying-an-optional-remote-messaging-service)
  and the `deploy-am-service.ps1 -SshTarget '<user>@<confirmed-host>'` helper. Replace the
  placeholders before use; default/WhatIf is read-only preflight, while writes require
  separate deployment authorization and `-Deploy` with ShouldProcess.
