# CARD-0137 — overlay focus vs the normal delivery path

**Date:** 2026-08-23
**Card:** CARD-0137 (`92aa7757-507d-4988-a98a-dfc7ba90a9a2`)
**Status:** investigation complete. No code changes. No fix designed.
**Live target:** standing agent **Grok 4.6** (`cbbb38fc-2c39-42db-913c-7093b58c2a1f`), session
`1e4976d4-335a-472b-92c9-345563eca5d9`, `AlwaysOn=false`, `working=false` on every GET before a
send. Server `:17202`, runner `:17204`.

---

## Verdict

The normal message-delivery path (`SessionMessageQueueService.DeliverAsync`, used by
`POST /api/sessions/{id}/messages`) has the **same overlay-focus gap CARD-0143 already closed
narrowly for the subscription-usage poll transport** — and the gap is **broader than usage-panel
polling**.

Live against Grok 4.6's `/usage` overlay, through `POST /messages {"Mode":"Now"}` (not
`TryPollLocalCommandAsync`):

| # | Question | Result |
|---|---|---|
| 1 | Overlay **closed**, `/usage` via the normal path? | **Yes.** HTTP 200 in 3.2 s. Overlay opened. Panel readable. Transcript still empty (local command, no `UserPrompt`). |
| 2 | Overlay **open**, does Esc restore composer focus? | **Yes**, when Esc is written as a bare `0x1b` through `POST /api/sessions/{id}/input` (`AgentSessionRuntime.SendInputAsync` — the same write primitive `DeliverAsync` uses for the body and the submitting CR). Overlay gone, empty `>` composer back, `working` still false. Twice. `DeliverAsync` itself never sends Esc. |
| 3 | Broader than repeated `/usage` polls? | **Yes.** A *successful* normal-path `/usage` **leaves the overlay focused**. The next `POST /messages` of `/usage` then 409s with `NoComposerEvidence` after 15.6 s, buffer SHA unchanged, sequence unchanged. The evidence check is the typed body on the rendered screen — any later body would fail the same way. CARD-0143's poll path Esc-closes after a poll; the normal path does not. |

CARD-0103's `ComposerInputProbe` does **not** already solve this. It is a launch-time
input-responsiveness round trip on the Claude adapters only, explicitly the last step of
`WaitForReadyAsync`, not a delivery-time overlay gate.

A confounding trap (live-confirmed this session, previously reasoned in CARD-0143): `/usage` writes
**no** transcript row. Q1 only succeeded in 3.2 s because this session had zero `TranscriptEntries`,
so `DeliverAsync` degraded to the screen-only verdict. On a Grok/Codex session that has already
taken a turn, the same overlay-closed `/usage` through `DeliverAsync` would type, get composer
evidence, Enter, then wait 30 s for a `UserPrompt` that never comes (`NoTranscriptRecord`). That is
why CARD-0143 built a separate transport. It is not the overlay bug, but it is what a naive
"drive `/usage` through `POST /messages`" loop hits next.

---

## 1. What CARD-0143 shipped, and what it did not touch

`TryPollLocalCommandAsync`
(`server/Application/Services/SessionMessageQueueService.cs:2084`) is the CARD-0143 local-command
transport. When `LocalCommandPoll.OpensOverlay` is true it:

1. Writes a bare Esc first, waits `OverlaySettleMs` (default 400)
   (`:2122-2127`).
2. Types the command, waits for composer evidence, Enter, waits for sequence advance
   (`:2132-2165`).
3. Writes Esc again on the way out, whether the panel rendered or not
   (`:2162-2163`, `:2178-2179`).

Grok is the only kind with `OpensOverlay: true`
(`server/Application/Services/ProviderContractCatalog.cs:108-114`, command `/usage`).
Codex `/status` is `OpensOverlay: false` (`:152-157`).

`DeliverAsync` (`:1182-1294`) is the path `EnqueueAsync` Mode.Now calls (`:163`) and the queued
send-now / turn-end flush also call (`:438`, `:900`). It:

1. Normalizes the body, size-gates, snapshots the screen.
2. `SendInputAsync` of the payload (`:1262`).
3. `WaitForComposerEvidenceAsync` (`:1264-1270`) — 15 s, then `NoComposerEvidence`, Enter withheld.
4. `SendInputAsync("\r")` (`:1278`).
5. If the session has any `TranscriptEntries`, `WaitForTranscriptConfirmAsync` (30 s, Enter re-press
   every 7 s, up to 3 Enters). Else, screen-only sequence-advance.

A search of `SessionMessageQueueService.cs` for `\u001b` / `OpensOverlay` / Esc hits **only** the
six lines inside `TryPollLocalCommandAsync`. The normal path has no overlay contract, no Esc-before,
no Esc-after.

`POST /api/sessions/{id}/messages` is `EnqueueAsync`
(`server/Api/Endpoints/SessionEndpoints.cs:90-96`).
`POST /api/sessions/{id}/input` is `AgentSessionService.SendInputAsync` →
`AgentSessionRuntime.SendInputAsync` (`SessionEndpoints.cs:64-72`,
`AgentSessionService.cs:940-941`, `AgentSessionRuntime.cs:735-756`). Esc is a control character;
`PendingTerminalInput.Append` does not treat it as a submitted command
(`AgentSessionRuntime.cs:1083-1112`), so it does not start a manual turn.

---

## 2. CARD-0103 prior art (read, does not cover this)

`ComposerInputProbe` (`src/Antiphon.Agents.Pty/ComposerInputProbe.cs:65-87`) proves the TUI is
*reading stdin* at ready time: write a non-slash token, require it to render, Ctrl+U-clear it.
Call sites:

- `RunnerClaudeAdapter.WaitForReadyAsync` → `ProbeComposerInputAsync`
  (`server/Infrastructure/Agents/SessionRunner/RunnerClaudeAdapter.cs:149-198`)
- in-process `ClaudeAdapter.WaitForReadyAsync` → the same probe
  (`server/Infrastructure/Agents/Pty/ClaudeAdapter.cs:152-158`)

The CARD-0103 plan states the probe belongs there "Not in the queue: at ready time the composer is
guaranteed empty and nobody owns the session yet." The Grok adapter's `WaitForReadyAsync`
(`RunnerGrokAdapter.cs:142-160`) is quiet-period + min-total-wait only — no probe, no overlay
handling. None of this runs at delivery time, and none of it Esc-closes a mid-life slash-command
overlay.

`ClaudeBlockingPromptDetector` / trust-dialog handling is first-launch, per-cwd, and answers one
specific modal. Unrelated to `/usage`.

---

## 3. Live measurements (2026-08-23)

Discipline: `GET /api/sessions/{id}/messages` (`working: false`) before every send. No throwaway
prompt. Only `/usage` via the normal path, and bare Esc via `POST /input`. Agent is not AlwaysOn,
so a 409 cannot take the CARD-0143 kill arm (`HandleDeliveryFailureAsync` `:1794`:
`kill = agent is { AlwaysOn: true } && !working && !allSupervision && !preFirstTurn`).

### 3.0 Starting state

Overlay **already open** from a prior operator `/usage` (the CARD-0137 found-shape). Sequence 62.
`GET /api/sessions/{id}/transcript` → `entries: []`, `lastSequence: 0`. Queue empty.

```
  ≡ master C:\src\Antiphon      12K / 500K
┌──────────────────────────────────── [x] ─┐
│  Context usage  Usage limit  Session info│
│  Weekly limit (SuperGrok)                │
│  … 1% …  Resets: August 28, 05:31        │
│  Session usage: no model calls yet in t  │
│        Tab switch  |  ↑/↓ scroll         │
│     c copy session ID  |  Esc close      │
└──────────────────────────────────────────┘
```

Footer names the close key. Tabs are focus-stealing (Tab / arrows), which is the card's hypothesized
mechanism.

### 3.1 Q2 first — Esc via `POST /input` (overlay starts open)

`POST /api/sessions/1e4976d4-…/input {"Input":"\u001b"}` → **204**.

800 ms later, sequence **63**, overlay gone, empty composer:

```
  ≡ master C:\src\Antiphon      12K / 500K

     Switched to Grok 4.6 (high effort)

  ╭──────────────────────────────────────╮
  │ >                                    │
  ╰─── Grok 4.6 (high) · always-approve ─╯

  Shift+Tab:mode  │  Ctrl+x:shortcuts
```

`working=false` still. Buffer SHA changed. Repeated at the end of the run (seq 72 → 73) with the
same restore. Esc through this write primitive **does** close Grok's `/usage` overlay and return
composer focus. Measured, not assumed.

Not measured: sending Esc as a `Mode:"Now"` *message body*. `DeliverAsync` would still demand
composer evidence of that body (`ComposerDeliveryEvidence.IsVisible`); a control character will not
render as typed text, so the API result would be a 409 even if the overlay closed as a side effect.
That is not a useful Esc path.

### 3.2 Q1 — overlay closed, `/usage` through `POST /messages`

Reconfirmed `working=false`, overlay closed (seq 63, composer `>`).

`POST /api/sessions/{id}/messages {"Body":"/usage","Mode":"Now"}` → **HTTP 200 in 3159 ms.**

```json
{"sessionId":"1e4976d4-335a-472b-92c9-345563eca5d9","messages":[],"working":false}
```

Sequence **72**. Overlay **open** on Usage limit. Panel contents (truncated by the viewport):

```
│  Context usage  Usage limit  Session info│
│  Weekly limit (SuperGrok)                │
│  ███████████░░░░░░░░░░░░░░░░░░░  38%     │
│  Resets: August 28, 05:31                │
│  Session usage: no model calls yet in t  │
│     c copy session ID  |  Esc close      │
```

`GET /transcript` still `entries: []`. Local `/usage` is not a model turn. That is why this 200
returned in 3 s rather than hanging on CARD-0055's 30 s transcript confirm: `confirmTranscript =
baseline.Observable` (`DeliverAsync` `:1248`) was false, so the path took
`WaitForSequenceAdvanceAsync` (`:1283-1291`) instead of `WaitForTranscriptConfirmAsync`.

End-to-end for the **empty-transcript + overlay-closed** cold-start: send, verify, read buffer —
works. The overlay is then **left standing**, because `DeliverAsync` has no closing Esc.

### 3.3 Q2 residual / Q3 — leftover overlay vs the next normal-path send

Same session, overlay still open from §3.2, `working=false`, seq 72. Buffer SHA
`6F-5E-DF-BB-…-E2-55`, length 29869.

Second `POST /messages {"Body":"/usage","Mode":"Now"}` → **HTTP 409 in 15561 ms**
(`EvidenceTimeoutSeconds` default 15):

```
Message delivery could not be verified — the terminal did not accept it
(the typed message never appeared in the composer). See the agent's incidents.
```

After: seq still **72**, buffer SHA **identical**, overlay still open, screen unchanged, `working`
still false. Incident `DeliveryVerificationFailed` / Error at `2026-08-23T07:33:35.623807Z`
("The message has been returned to the queue." — Mode.Now persisted nothing to revert). Session
was **not** killed (`AlwaysOn=false`).

This is the card's original 409, reproduced after a *successful* normal-path `/usage` that did not
clean up after itself.

Then Esc via `POST /input` again → composer restored (seq 73). Session left idle, overlay closed.

### 3.4 Why composer evidence is the right refusal (and is body-agnostic)

`WaitForComposerEvidenceAsync` (`:1491-1508`) calls
`ComposerDeliveryEvidence.IsVisible(before, after, body)`. For `/usage` (6 chars after
normalisation, ≤ `WindowLength` 10) that is a direct `Contains("/usage")` on the whitespace-and-box
stripped screen (`ComposerDeliveryEvidence.cs:62-76, 191-194`). The overlay's "Usage limit" /
"Session usage" text does **not** contain the slash-prefixed needle, so there is no false positive
from the panel already being on screen. The 409 means the typed bytes never landed in the composer
— they were swallowed by the focused overlay (and, in this run, produced no visible panel change
either).

The matcher keys on whatever body was sent, not on the command being `/usage`. A subsequent real
prompt through `DeliverAsync` while this overlay is up would take the same 15 s and the same 409.
That send was not performed (no throwaway prompt). The mechanism does not need a second body to
hold.

---

## 4. What is live vs what is code-only

**Live (this session):**

- Grok `/usage` overlay steals focus; footer `Esc close`.
- Overlay-closed `/usage` through `POST /messages Mode:Now` succeeds when the session has **no**
  transcript rows, and **leaves the overlay open**.
- Overlay-open `/usage` through the same path 409s `NoComposerEvidence` in ~15 s; buffer and
  sequence unchanged.
- Bare Esc through `POST /input` closes the overlay and restores the composer. Twice.
- `/usage` writes no `TranscriptEntry` (0 before, 0 after a successful send).

**Code-only (not driven live here):**

- On a Grok/Codex session **with** `TranscriptEntries`, overlay-closed `/usage` through
  `DeliverAsync` would still type and Enter (composer evidence would pass), then
  `WaitForTranscriptConfirmAsync` would re-press Enter every 7 s up to 3 times into a focused
  overlay, time out at 30 s, return `NoTranscriptRecord`. Mode.Now then calls
  `HandleDeliveryFailureAsync` (`EnqueueAsync` `:171-176`). For an **AlwaysOn** agent that is
  idle, that is a kill (`:1794`). CARD-0143's plan already named this; this session could not
  exhibit it because it had zero rows. Extra Enters into an open overlay were therefore not
  measured.
- Codex `/status` (`OpensOverlay: false`) should not take this overlay shape. Not re-measured.
- Other Grok slash commands that might also open overlays (`/help`, `/model`, …) were not sent.
  Catalog `OpensOverlay` is only set for Grok `/usage`.
- Claude `ComposerInputProbe` is Claude-launch-only; Grok ready has no equivalent. Not re-probed.
- Esc as a `Mode:"Now"` message body: not sent (see §3.1).

**Not a finding about the poll path.** CARD-0143's Esc-before / Esc-after is in tree
(`47196b5`) and is out of this card's scope except as the contrast.

---

## 5. What this does not decide

No fix is designed here. The measurements say only:

1. The gap CARD-0143 closed for `TryPollLocalCommandAsync` is still present on `DeliverAsync`.
2. It is not usage-poll-specific: any overlay-opening slash command driven (or left standing)
   through the normal path leaves the composer deaf to the next `POST /messages` / queued delivery.
3. Esc through the existing `SendInputAsync` primitive does close Grok's `/usage` overlay.
4. CARD-0103's probe is the wrong layer for a mid-life overlay.

Whether the normal path should grow overlay-aware Esc, whether slash-commands should be refused
or rerouted onto the poll transport, and whether leftover overlays should be detected before a
real user message is typed, are a later design step.
