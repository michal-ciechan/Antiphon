# CARD-0142 — garbage typed into the composer at launch: the web client's xterm.js answers the session's terminal queries, and its answers are typed into the live TUI: plan

**Date:** 2026-08-23 · **Card:** CARD-0142 (`49fb4e6e-3594-43de-a640-c2c7ac3e5375`) ·
**Status:** plan (no implementation in this pass) ·
**Verified against:** master `9cf7187`. Every line/behaviour claim below was read out of the code on
that commit. Every measurement below was taken against the **live dev stack** on 2026-08-23 — server
`:17202`, session runner `:17204`, Vite client `:17203`, real Chrome via `browser-harness` — using
three throwaway agents (Codex / ClaudeCode / Grok) that were stopped and **deleted** afterwards.

**Related:** CARD-0048 (`Da1StartupResponder`, the server-side answer to the *same* DA1 query — prior
art and, as it turns out, the thing that proves the client's answer is redundant), CARD-0047
(something typed too early), CARD-0136 / CARD-0141 (the `GET /api/sessions/{id}/buffer` capture
technique used here).

---

## Verdict up front

**Root-caused, reproduced end to end in the real client, and it is none of the card's three
hypotheses as written — it is the fourth one they collectively circle.**

`client/src/features/board/SessionTerminal.tsx:62`

```ts
const dataDisposable = terminal.onData((input) => {
  if (!inputEnabled) return
  void sendSessionInput(sessionId, input)
})
```

xterm.js's `onData` does **not** mean "the human typed something". It fires for every byte the
terminal emits, and a VT terminal emits bytes *of its own accord* whenever the stream it is shown
contains a query: device attributes, cursor position, mode state, colours, focus. `SessionTerminal`
forwards all of them into the live pty's **stdin**.

Every Antiphon session's replay buffer begins with `ESC[1t ESC[c` — `OpenConsole.exe`'s startup DA1
query (CARD-0048) — and Codex additionally queries `ESC]10;?` / `ESC]11;?` (foreground/background
colour) in the same init burst. So the moment the operator opens the Terminal panel, `replayBacklog`
writes that init burst into xterm.js, xterm.js *answers all three*, and the answers are POSTed to
`/api/sessions/{id}/input`.

**Measured, in the real browser, in the real client, on a session whose composer was verifiably
empty one second earlier:**

```
before:  › Write tests for @filename          (Codex's ghost hint — composer empty)
after:   › [?1;2c]10;rgb:d9d9/e2e2/efef\]11;rgb:1111/1313/1717\
```

The two colour values are the smoking gun: `d9e2ef` is `SessionTerminal.tsx:50`'s
`theme.foreground` and `111317` is `theme.background` (`:49`). Nothing else in the stack knows
those constants. The garbage is the web client's own theme, reported back to a terminal that never
asked it.

**Three corrections to the card, all measured:**

| Card says | Measured |
|---|---|
| "possibly other kinds — not confirmed" | **Not Codex-specific.** Grok reproduces identically. Claude receives the same bytes and discards them — see the differential below. |
| hypothesis 1: a startup handshake leaking into the rendered composer | Half right about the *shape*, wrong about the actor. `Da1StartupResponder` behaves exactly as CARD-0048 specified; its answer is consumed by the pty input state machine and never reaches the child. It is the **client's second, much later answer** to the same query that lands as typing. |
| hypothesis 3: an auto-typed command colliding with a not-yet-ready TUI | No. Nothing Antiphon types at boot is involved; the repro agent had `remoteControlEnabled = false` and no boot prompt. The trigger is a human opening a UI panel, at any time. |

The fix is **client-side and narrow**: the browser terminal is a *mirror* of a pty that already has
its own terminal emulator (`OpenConsole.exe`) answering the child's queries. It must never generate
protocol traffic of its own. Scope is one component and one new helper module; no server, runner,
pty or adapter change.

---

## The reproduction, run for real

### Probe 1 — what is actually in the replay buffer (all three kinds)

`GET /api/sessions/{id}/buffer`, raw, byte 0 onward (`\x1b` written as `ESC`):

```
Codex   ESC[1t  ESC[c  ESC[?1004h  ESC[?9001h  ESC]0;C:\Windows\system32\cmd.exe ESC\
        ESC[?2004h  ESC[?1004l  ESC[?1004h   ESC]10;? ESC\    ESC]11;? ESC\   ESC]0;Antiphon BEL ...

Claude  ESC[1t  ESC[c  ESC[?1004h  ESC[?9001h  ESC]0;claude ESC\  ESC7 ESC[r ESC8
        ESC[?25h ESC[?25l  ESC[?2004h  ESC[?1004h  ESC[?2031h   ESC[>0q    ESC[c   ...

Grok    ESC[1t  ESC[c  ESC[?1004h  ESC[?9001h  ESC]0;grok BEL   ESC[?1003;1006h
        ESC[?1004h  ESC[?2004h  ESC[?25l  ESC]12;rgb:c8/c8/c8 BEL ...
```

Query census over the whole live Codex buffer (53 991 chars): `ESC[c` ×1 (offset 4), `ESC]10;?` ×1,
`ESC]11;?` ×1, `ESC[?9001h` ×1. `ESC[c` sits at **offset 4 of every session of every kind**; Claude
carries a **second** one at offset 83 (its own DA1-sync terminator after `ESC[>0q`).

### Probe 2 — what xterm.js emits when fed those buffers

A throwaway vitest probe constructing `new Terminal({...})` with **`SessionTerminal.tsx`'s exact
options** (jsdom), subscribing `onData`, writing each captured buffer:

```
claude  FULL  disableStdin:false -> ["\u001b[?1;2c", "\u001b[O", "\u001b[O", "\u001b[?1;2c"]
claude  FULL  disableStdin:true  -> []
grok    FULL  disableStdin:false -> ["\u001b[?1;2c", "\u001b[O", "\u001b[O"]
grok    FULL  disableStdin:true  -> []
codex   FULL  disableStdin:false -> ["\u001b[?1;2c", "\u001b[O", "\u001b[O"]
codex   FULL  disableStdin:true  -> []
```

Truncating each buffer to its first 400 chars gives the identical output — the emissions come from
the **init burst alone**. `disableStdin: true` suppresses them entirely, which exactly matches
`SessionTerminal.tsx:46` (`disableStdin: !inputEnabled`) and `:63` (`if (!inputEnabled) return`):
this is why the bug only bites once the session flips to `Running`.

jsdom does not run the colour path (no render service), so the probe shows only DA1 + focus reports.
The real browser adds the two OSC colour reports — see Probe 4.

### Probe 3 — what those bytes do to a live TUI (the differential)

`POST /api/sessions/{id}/input` with `ESC[?1;2c`, `ESC[O`, `ESC[O`, `ESC[?1;2c`, then read the
runner's own `renderedScreen` (`GET :17204/sessions/{id}/snapshot` — Antiphon's `TerminalScreen`
scraper, a different renderer from the client's, so this is server-side ground truth):

| Kind | Composer after injection |
|---|---|
| **Codex** | `› [?1;2c` — **inserted as literal text** |
| **Grok** | `[?1;2c[?1;2c` drawn across the input box's bottom border — **inserted, both copies** |
| **Claude** | `> ` — clean. Raw output grew (1 488 → 2 521 chars, it redrew) so the bytes *arrived*; Claude Code's key parser discards the unrecognised CSI. |

`ESC[O` (focus-out) was consumed silently by all three — it never appears on screen.

**This is the whole answer to the card's open question.** The trigger is universal; the visible
symptom is not, because Codex's and Grok's input parsers insert an unrecognised CSI's printable
remainder into the composer and Claude's drops it.

### Probe 4 — end to end, real Chrome, real client

Composer cleared to empty (8 × `DEL`), confirmed clean, no client attached for ~40 min:

```
› Write tests for @filename          <- ghost hint, composer empty
```

Then, via `browser-harness` against the running Vite client on `:17203`: Agents page → the repro
Codex agent's **Terminal** button → the `AgentCliModal` mounts `SessionTerminal`. Nothing else
touched, no key pressed, no click inside the terminal. Two seconds later, read from the **runner**,
not the browser:

```
› [?1;2c]10;rgb:d9d9/e2e2/efef\]11;rgb:1111/1313/1717\
```

Stable across four polls. Decoding it:

| Bytes | What it is | Answering |
|---|---|---|
| `ESC[?1;2c` | xterm.js DA1 **response** ("VT100 with Advanced Video Option") | `OpenConsole.exe`'s startup `ESC[c` (offset 4) |
| `ESC]10;rgb:d9d9/e2e2/efef ESC\` | OSC 10 foreground report; `#d9e2ef` = `SessionTerminal.tsx:50` | Codex's own `ESC]10;?` |
| `ESC]11;rgb:1111/1313/1717 ESC\` | OSC 11 background report; `#111317` = `SessionTerminal.tsx:49` | Codex's own `ESC]11;?` |

The same run against the throwaway **Claude** agent's panel left its composer clean — second
confirmation of Probe 3's differential.

---

## Why the answers reach the child at all

CARD-0048 established that the DA1 query at offset 4 is `OpenConsole.exe` asking **the outer
terminal** (Antiphon) for device attributes, and that `Da1StartupResponder`'s reply is *consumed by
the pty's input state machine and never reaches the child* — which is why that fix could not
possibly type into a TUI. That property is **conditional on timing**: the pty input parser is only
waiting for a DA1 during the startup handshake. `Da1StartupResponder` answers within ~40 ms
(measured in CARD-0048). The client answers when a human opens a panel — minutes, hours or days
later — by which point the input pipe is a plain byte pipe to the child. Same bytes, different
fate.

The OSC 10/11 queries are Codex's *own*, forwarded outward by `OpenConsole.exe`. Nothing in Antiphon
answers them, and Codex correctly falls back to defaults. The client's late answer is therefore not
"a delayed reply" from Codex's point of view — it is typing.

`Da1StartupResponder` is behaving exactly as specified and needs no change. Its `QueriesSeen`
counter, which CARD-0048 designed as the tripwire for "a second DA1 appeared", reads **1** on the
Codex sessions measured, correctly — the second answer never was a query.

---

## Why "on launch"

The runner bounds its mirror at `SessionRunnerSettings.ReplayBufferMaxChars = 256 * 1024`
(`src/Antiphon.SessionRunner/SessionRunnerSettings.cs:6`; `TrimLiveBuffer` evicts from the front,
`SessionRunnerRuntime.cs:901`). The queries live at offsets 4–107. So:

- a **young** session's replay still contains the init burst → every panel mount re-answers →
  garbage;
- a session that has produced more than ~256 KiB of output has had its head evicted → the panel
  mounts clean → the bug appears to have "gone away".

That is the card's "immediately at startup" framing, exactly. Note the two long-lived Claude
sessions measured (`AZ Care`, `school-revision`) still carry DA1 queries at offsets 223 408 and
13 289 — a mid-buffer relaunch or `/clear` puts a fresh init burst back inside the replay window, so
the exposure is not strictly limited to the first 256 KiB.

It fires again on every remount: the `useEffect` deps are `[inputEnabled, sessionId]`
(`SessionTerminal.tsx:166`), so a `Starting → Running` flip alone builds a fresh `Terminal` and
replays; and `connection.onreconnected` calls `replayBacklog(true)` (`:127-130`), which replays the
whole buffer through a terminal whose stdin is enabled.

---

## Why no test caught it

`client/src/features/board/SessionTerminal.test.tsx` **mocks `@xterm/xterm` wholesale** — the mock's
`onDataHandler` only ever fires when the test itself calls it. A parser-generated reply is
unrepresentable in that harness by construction. The existing tests are still the right tests for
the wiring they cover; the regression test for this defect has to drive a **real** `Terminal`.

---

## What xterm.js 6.0.0 emits without user input

Enumerated by reading every `triggerDataEvent(` call site in
`client/node_modules/@xterm/xterm/lib/xterm.js` (pinned version 6.0.0). Sites that pass
`wasUserInput = true` are human-origin (keypress, `input`, paste, composition, wheel, mouse
protocol, `moveToCellSequence`); every site below passes it **false** — i.e. these are the terminal
speaking for itself:

| Emission | Trigger | In our buffers today |
|---|---|---|
| `ESC[?1;2c` (DA1) | `CSI c` / `CSI 0 c` | **yes** — offset 4, every kind; Claude twice |
| `ESC[>0;276;0c` (DA2) | `CSI > c` | not seen |
| `ESC]<10\|11\|12\|4;n>;rgb:... ESC\` | OSC 4/10/11/12 with `?` | **yes** — Codex `]10;?`, `]11;?` |
| `ESC[I` / `ESC[O` (focus) | DOM focus/blur once `CSI ? 1004 h` armed | **yes** — all three kinds arm 1004 |
| `ESC[0n`, `ESC[r;cR`, `ESC[?r;cR` (DSR/CPR) | `CSI 5 n`, `CSI 6 n`, `CSI ? 6 n` | not seen |
| `ESC[?m;v$y` (DECRQM report) | `CSI ? ... $ p` | not seen |
| `ESC P 1$r ... ESC\` (DECRQSS) | `DCS $ q ... ST` | not seen |
| `ESC[4;h;wt`, `ESC[6;h;wt`, `ESC[8;r;ct` (XTWINOPS) | `CSI 14 t`, `CSI 16 t`, `CSI 18 t` | `CSI 1 t` is present (de-iconify, no report) |

`ESC[>0q` (XTVERSION, which Claude sends) has **no** handler in xterm.js 6.0.0 — nothing to
suppress, and the regression test will keep that true.

---

## Design decisions

**D1 — The fix belongs in the client, at the terminal, not on the wire and not on the server.**
The browser terminal is a mirror. The child's real terminal is `OpenConsole.exe` inside the pty, and
it answers the child's queries itself — proven by the fact that Codex, Claude and Grok all run
correctly for hours with no browser attached. Therefore *every* byte xterm.js generates on its own
is by construction spurious, and the rule is a single invariant rather than a list of symptoms:
**the mirror never speaks.**

**D2 — Suppress at the parser, not by filtering `onData` payloads.** Rejected: pattern-matching the
outgoing string against known report shapes. It is a blocklist at the wrong end, and `TerminalKeypad`
(`SessionTerminal.tsx:213`) legitimately sends real escape sequences (`ESC[A`, `ESC`, ctrl chars)
through the same endpoint, so any shape-based filter has to distinguish a DA1 response from an arrow
key forever. `IParser.registerCsiHandler` / `registerOscHandler` / `registerDcsHandler` are public
API, "the most recently added handler is tried first", and returning `true` stops the built-in from
running (`@xterm/xterm/typings/xterm.d.ts:1805-1866`). The query is consumed before a reply exists.

**D3 — Suppress the query, keep the command.** OSC 10/11/12 are *both* "report your colour" (data
is `?`) and "set this colour" (Grok sends `ESC]12;rgb:c8/c8/c8` to set the cursor colour). The
handler returns `true` only for the `?` form and `false` for a set, which falls through to xterm's
built-in and keeps the rendering correct. Same discipline for `CSI t`: `true` only for the reporting
params 14/16/18, `false` for everything else so `CSI 1 t` and friends still behave.

**D4 — Rejected: `disableStdin` around the replay.** `terminal.write(buf, cb)` has a completion
callback, so `disableStdin = true` for the duration of the replay is implementable and would kill
the *observed* symptom. Rejected because it fixes the trigger, not the defect: a query arriving in a
**live delta** (a relaunch's init burst, a TUI re-querying colours on a theme change, Claude's
`ESC[c` on a redraw) is answered exactly the same way, and the fix would silently not cover it.

**D5 — Rejected: a server-side filter in `AgentSessionService.SendInputAsync`.** Same blocklist
problem as D2, one layer further from the cause, and it would have to stay in lockstep with whatever
report set the client's xterm.js version implements. The one thing a server-side rule buys is
protection against a *future* client; the regression test in D6 buys that more cheaply.

**D6 — The durable guard is a test that drives a real `Terminal` over real captured buffers.**
Fixture: the first ~400 bytes of a real Codex, Claude and Grok session buffer (all three captured
during this investigation; the exact bytes are in Probe 1). The test builds the terminal through the
**production factory** the fix introduces, writes each fixture, and asserts `onData` never fired.
An xterm.js bump that adds a new report, or a refactor that stops using the factory, goes red
immediately. This is the whole reason the fix should be a factory rather than a few lines inline in
the `useEffect`.

**D7 — Focus reports are in scope but separable.** `ESC[I`/`ESC[O` are not parser-generated — they
fire from DOM focus/blur once `CSI ? 1004 h` has armed `sendFocus`, and all three kinds arm it.
Measured, all three TUIs consume them silently, so they are **not** part of the reported symptom.
They are still a mirror speaking unbidden, and a spurious FocusOut is exactly the kind of thing a
TUI may act on (dim, pause a spinner). Suppression is precise and cheap — a `CSI ? h` / `CSI ? l`
handler that returns `true` only for a lone `1004` and `false` otherwise, so multi-param DECSETs
like Grok's `?1003;1006h` still reach the built-in. Kept as its own slice so it can be dropped
without touching the fix for the observed defect.

**D8 — Out of scope: mouse reporting.** Grok arms `ESC[?1003;1006h` (any-event tracking, SGR
encoding), so moving the mouse over a Grok terminal panel plausibly sends SGR mouse reports into the
pty. **Not measured in this pass.** It is also a different class: xterm.js flags those
`wasUserInput = true`, and a real terminal would send them, so "the mirror never speaks" does not
obviously condemn them. Recorded here so the next person does not think it was missed; it wants its
own card and its own measurement, not a guess bundled into this fix.

**D9 — Out of scope: garbage already sitting in a live composer.** One-shot, operator deletes it.
No migration, no cleanup pass.

---

## Slices

Each slice is independently testable and independently committable.

### W1 — `createMirrorTerminal` factory, with the report suppressors

New module `client/src/features/board/terminalMirror.ts` exporting a factory that constructs the
`Terminal` with the options `SessionTerminal.tsx:43-56` uses today and registers the suppressing
handlers before returning it. Its doc comment carries the invariant from D1 and the reason (this
mirrors a pty whose real terminal is `OpenConsole.exe`), so the next reader does not "clean up" a
pile of no-op handlers.

Handlers, all returning `true` (consume, do not reply):

- `registerCsiHandler({ final: 'c' }, ...)` — DA1
- `registerCsiHandler({ prefix: '>', final: 'c' }, ...)` — DA2
- `registerCsiHandler({ final: 'n' }, ...)` and `({ prefix: '?', final: 'n' }, ...)` — DSR/CPR
- `registerCsiHandler({ intermediates: '$', final: 'p' }, ...)` and the `?`-prefixed form — DECRQM
- `registerDcsHandler({ intermediates: '$', final: 'q' }, ...)` — DECRQSS
- `registerOscHandler(4 | 10 | 11 | 12, data => isQuery(data))` — **`true` only when the data is/ends
  with `?`** (D3); a colour *set* returns `false` and falls through
- `registerCsiHandler({ final: 't' }, params => params[0] === 14 || params[0] === 16 || params[0] === 18)`
  — XTWINOPS reports only (D3)

`SessionTerminal.tsx:43` switches to the factory. No behaviour change beyond suppression.

**Red before:** the W2 test.

### W2 — the regression test (the point of the whole exercise)

`client/src/features/board/terminalMirror.test.ts`, using the **real** `@xterm/xterm` (not the
`SessionTerminal.test.tsx` mock, D6):

1. `a mirror terminal answers nothing over a real Codex startup buffer` — write the Codex fixture,
   assert `onData` never fired.
2. the same for the Claude fixture (which carries **two** DA1 queries) and the Grok fixture.
3. `an unpatched terminal does answer` — the same fixtures through a bare `new Terminal(...)` with
   the same options, asserting the emissions from Probe 2 (`ESC[?1;2c` etc.). This is the control:
   without it, cases 1–2 pass trivially if the fixtures ever stop containing queries.
4. `a colour set still reaches the renderer` — `ESC]12;rgb:c8/c8/c8` (Grok's real cursor-colour
   command) is applied, not swallowed (D3).
5. `real typing is still forwarded` — drive input through `terminal.input('x')` and assert it is
   emitted, so the suppressors cannot be widened into muting the operator.

Fixtures live in `client/src/features/board/__fixtures__/` as the captured byte prefixes, with a
comment naming the session kind and the capture date.

### W3 — `SessionTerminal` wiring test

Extend `SessionTerminal.test.tsx` (which keeps its mock) with one assertion that the component
builds its terminal through `createMirrorTerminal`, so a future refactor that reverts to a bare
`new Terminal(...)` fails here rather than silently reopening the defect against W2's still-green
factory test.

### W4 — focus reporting (D7, droppable)

`registerCsiHandler({ prefix: '?', final: 'h' | 'l' }, params => params.length === 1 && params[0] === 1004)`
in the factory, plus a test that a lone `ESC[?1004h` followed by a blur emits nothing and that
`ESC[?1003;1006h` still reaches the built-in. Ship or drop independently of W1–W3.

### W5 — live verification and card close-out

Re-run Probe 4 against the rebuilt client: launch a throwaway Codex agent, confirm the composer is
clean, open the Terminal panel, confirm from `GET :17204/sessions/{id}/snapshot` that the composer
is **still** clean; repeat for Grok. Delete the throwaway agents. Note in the card that the client
bundle must be rebuilt for E2E tests to see the change (`client/dist`, per `AGENTS.md` /
`EnsureClientBundleIsCurrent`).

---

## Test coverage summary

| Test | Guards |
|---|---|
| `terminalMirror.test.ts` cases 1–2 | the defect itself, per agent kind, over real captured bytes |
| `terminalMirror.test.ts` case 3 (control) | the fixtures still contain queries — stops cases 1–2 rotting into a tautology |
| `terminalMirror.test.ts` case 4 | D3 — a colour *set* is not collateral damage |
| `terminalMirror.test.ts` case 5 | the suppressors never mute real typing |
| `SessionTerminal.test.tsx` (new case) | the component keeps using the factory |
| `terminalMirror.test.ts` W4 cases | focus reports, and that multi-param DECSETs are untouched |

An xterm.js version bump that adds a new self-reporting sequence turns cases 1–3 red on the next run,
which is the only automatic warning available for a defect whose symptom lives in someone else's
TUI.

---

## What was NOT determined

- **Mouse reporting on Grok panels** (D8) — unmeasured, deliberately out of scope, wants its own card.
- **Whether any of this has ever submitted a turn.** All observed garbage stayed unsubmitted in the
  composer (no `CR` is among the emissions). No evidence was sought of a case where a subsequent
  Enter — the operator's or `SessionMessageQueueService`'s — carried the garbage into a real prompt.
  The queue's own delivery verification (CARD-0055 / CARD-0024) would treat a body prefixed with
  `[?1;2c` as a completeness failure, so if it has happened it should be visible as a `Truncated`
  verdict; nobody has looked.

---

## Environment / cleanup

Three throwaway agents were created, launched, probed and **deleted** (`DELETE /api/agents/{id}`,
all 204): `CARD-0142 Repro Codex` (`a1d2ff9b`, session `0b13cb88`), `CARD-0142 Repro Claude`
(`2ed454aa`, session `47480488`), `CARD-0142 Repro Grok` (`5cce0b80`, session `ce68138d`). The
operator's real `Codex` (`06a847ea` / session `f04cd114`) and `Grok 4.6` (`cbbb38fc`) agents were
read only, never written to, and were `Running` before and after. The throwaway vitest probe file
was deleted; `git status` is clean and no `bin-*` or trailing-space directories were created (no
.NET build was run in this pass).
