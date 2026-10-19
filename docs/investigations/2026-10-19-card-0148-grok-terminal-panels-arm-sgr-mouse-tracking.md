# CARD-0148: Grok Terminal panels arm SGR mouse tracking

Date: 2026-10-19 (UTC execution date). Stage: Investigate. Task: `168f48ad-f4e1-43fd-a8ae-c73cf330beed`.

**Confirmed by reconstruction:** a running Terminal panel forwards no-button mouse motion into the session input path when its xterm has processed Grok's stored `ESC[?1003;1006h`. CARD-0142 deliberately preserved this behavior, and it remains at master `6ec71d427574f3a1b74bfbe2042e0ea9a819d191`. No SGR report filter exists on the traced path. This confirms the transport mechanism, not a live observation of Grok composer corruption.

## Scope and evidence provenance

This was a static investigation. No browser was driven, no live session was opened or written to, and no product or test source was changed. Tests/builds run: **0**. Existing assertions below were inspected, not reported as passing. The artifact uses today's UTC date as required by the Investigate stage, rather than the brief's next-day filename.

The read-only command `pwsh -NoProfile -File scripts/card.ps1 get CARD-0148 -Board Antiphon` returned this board record:

```text
CARD-0148 InProgress
title: Grok Terminal panels arm SGR mouse tracking - measure whether hovering leaks mouse reports into the pty
id: 6a7c449d-b830-4e55-9c58-50207afe27d3
board: 8988ca03-7414-47ad-b0b6-51556c701703
revisions: 2
```

Stored terminal-output evidence is the August 23 Grok startup capture in `docs/superpowers/plans/2026-08-23-card-0142-terminal-mirror-replies-plan.md:83`, preserved as `client/src/features/board/__fixtures__/grok-startup.ts:1` and `:11`. It includes `ESC[?1003;1006h`. The plan's `:289` explicitly says mouse movement was not measured. Its `:404` identifies the throwaway Grok session as `ce68138d` (abbreviated ID). These are historical buffer evidence and provenance, **not** a new conversation transcript or a current-session measurement.

The lockfile pins `@xterm/xterm` **6.0.0** (`client/package-lock.json:3793`). The package was downloaded from its [locked npm archive](https://registry.npmjs.org/@xterm/xterm/-/xterm-6.0.0.tgz) for source inspection; its bytes matched the lockfile's complete integrity value:

```text
sha512-TQwDdQGtwwDt+2cgKDLn0IRaSxYu1tSUjgKarSDkUM0ZNiSRXFpjxEsvc/Zgc5kq5omJ+V0a8/kIM2WD3sMOYg==
```

Below, `X/` means the archive's `package/src/`, extracted locally under `.antiphon/evidence/card-0148/package/src/`. Those generated dependency files remain gitignored. Re-fetching the locked archive reproduces these source line references; no installed dependency or upstream floating branch is assumed. The upstream [6.0.0 mouse service](https://github.com/xtermjs/xterm.js/blob/6.0.0/src/common/services/CoreMouseService.ts) and [browser terminal](https://github.com/xtermjs/xterm.js/blob/6.0.0/src/browser/CoreBrowserTerminal.ts) corroborate the behavior, but the line numbers below refer to the integrity-verified npm files.

## 1. What is forwarded

`SessionTerminal` enables stdin only for `session.status === 'Running'` (`client/src/features/board/SessionTerminal.tsx:29`, `:43`). Both backlog and live output go through `terminal.write` (`:95`, `:77`), so either can arm tracking. The production factory installs CARD-0142's query suppressors (`client/src/features/board/terminalMirror.ts:26`, `:46`). Its DECSET/DECRST handler consumes only a **lone 1004**, the focus-report mode (`:42`, `:68`); 1000, 1002, 1003 and 1006 reach xterm unchanged, individually or in Grok's combined sequence.

The handler at `SessionTerminal.tsx:52` forwards every `onData` string when input is enabled. It does not inspect its content. There is **no `onBinary` subscription** in this component and no `wasUserInput` check. Xterm's public events expose strings (`X/browser/public/Terminal.ts:72`); `wasUserInput` is an internal argument, not metadata available to that handler.

Tracking protocol and encoding are separate:

| Mode | xterm behavior | Forwarded by this panel? |
|---|---|---|
| `?1000h` | Press/release/wheel; no motion | With SGR encoding, yes |
| `?1002h` | Press/release/wheel and motion while a button is held | With SGR encoding, yes; no hover |
| `?1003h` | Adds motion with no button held | With SGR encoding, yes, including hover movement |
| `?1006h` | Selects SGR encoding; does not itself enable tracking | SGR uses `onData` |
| Tracking with default encoding | Emits legacy `ESC[M...` binary reports | No: the panel has no `onBinary` handler |

Source: `X/common/InputHandler.ts:1923`, `:1944`; `X/common/services/CoreMouseService.ts:47`, `:62`, `:77`, `:324`. Thus “any enabled mouse mode always reaches the server” would be too broad; Grok's observed **tracking plus SGR** combination does.

## 2. Hover reconstruction and complete input route

Under ANY tracking, xterm installs an element `mousemove` listener; its no-button branch explicitly calls the mouse sender (`X/browser/CoreBrowserTerminal.ts:720`, `:743`). There is no click or focus prerequisite in that branch. The sender converts `buttons === 0` to button NONE and action MOVE (`:617`), then invokes the mouse service (`:671`). NONE is 3 and MOVE is 32 (`X/common/Types.ts:288`, `:306`). The SGR encoder combines these to 35, increments the coordinates to one-based values, and uses uppercase `M` for motion (`X/common/services/CoreMouseService.ts:92`, `:143`, `:307`).

**Calculated example, not executed:** with no modifiers, a first movement at zero-based cell `(4, 2)` produces `ESC[<35;5;3M`; movement to `(5, 2)` produces `ESC[<35;6;3M`. These become two independent `sendSessionInput` calls. A stationary pointer generates no motion event, out-of-range coordinates are rejected, and otherwise-identical motion in the same cell is deduplicated (`X/common/services/CoreMouseService.ts:289`, `:311`, `:350`). “Every movement/pixel” is therefore inaccurate for normal SGR cell reporting.

SGR calls `triggerDataEvent(report, true)` (`X/common/services/CoreMouseService.ts:331`). That method checks `disableStdin`, optionally scrolls to the bottom and fires the internal user-input event, then emits the original string on `onData` (`X/common/services/CoreService.ts:61`). `wasUserInput = true` does not mean “a key was typed” and does not bypass an Antiphon filter: there is no such filter here.

The normal admitted pty route preserves the report:

| Boundary | Evidence |
|---|---|
| Browser → API JSON `{ input }` | `client/src/features/board/SessionTerminal.tsx:52`; `client/src/api/sessions.ts:172` |
| `/api/sessions/{id}/input` → service → runtime | `server/Api/Endpoints/SessionEndpoints.cs:18`, `:69`; `server/Application/Services/AgentSessionService.cs:2212` |
| Runtime → selected runner | `server/Application/Services/AgentSessionRuntime.cs:1844`, `:1862`; `server/Infrastructure/Agents/SessionRunner/RoutingSessionRunnerClient.cs:90` |
| HTTP runner input request | `server/Infrastructure/Agents/SessionRunner/SessionRunnerHttpClient.cs:739`; `src/Antiphon.SessionRunner/Program.cs:298` |
| Phone-home alternative | `server/Infrastructure/Agents/SessionRunner/PhoneHomeRunnerClient.cs:369`; `src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs:348` |
| Runner launch/release authorization → original string write | `src/Antiphon.SessionRunner/SessionRunnerRuntime.cs:1049`, `:125`, `:3875`, `:3917` |
| Pty-host message → runner writer | `src/Antiphon.PtyHost.Client/PtyHostClient.cs:149`; `src/Antiphon.PtyHost/PtyHostServer.cs:171`; `src/Antiphon.PtyHost/HostSession.cs:264` |
| UTF-8 bytes → pty input stream | `src/Antiphon.Agents.Pty/PtyAgentRunner.cs:301`, `:584` |

There are lifecycle/refusal gates, but no `ESC[<` suppression on this path. The Herdr alternative also passes the original string (`SessionRunnerRuntime.cs:3907`). This reconstruction ends at the host input stream; the receiving terminal host/TUI's interpretation is a separate question.

One additional effect is already visible in Antiphon's own code: the server records input before forwarding it (`AgentSessionRuntime.cs:1861`). `PendingTerminalInput.Append` discards control characters but appends every printable character (`:2308`, `:2335`). The calculated report above therefore leaves **`[<35;5;3M`** in its pending-text buffer, even if Grok discards the actual mouse report. `HasPendingTerminalInput` then reports text (`:1868`, `:2299`). The runner also sets `_terminalComposerPending` for any successful input other than a lone carriage return (`SessionRunnerRuntime.cs:3881`), which contributes to `TerminalReleaseCustodyHold` (`:2486`). These are reconstructed bookkeeping effects, not evidence that a release was actually attempted or blocked; the server's identified watchdog consumer is Claude-specific (`server/Application/Services/WatchdogService.cs:77`).

## 3. Selection, scrolling, and the cost of suppression

Mouse interaction is supported in the panel; its actual operator usage is not measured here. Antiphon attaches a selection-copy keyboard handler (`SessionTerminal.tsx:49`), and that handler reads the selection and writes it to the clipboard (`client/src/features/board/terminalCopy.ts:26`). Xterm supplies drag selection and a scrollable viewport.

When tracking is armed, xterm disables ordinary selection; when it is disarmed, selection is enabled (`X/browser/CoreBrowserTerminal.ts:727`). Forced selection remains possible through Shift on non-Mac platforms, or Option on Mac when its option is enabled (`X/browser/services/SelectionService.ts:437`, `:462`). Selection and copying are local operations, not SGR reports required by the pty.

Wheel behavior is different: tracking subscribes to wheel reports and cancels the browser event (`X/browser/CoreBrowserTerminal.ts:710`, `:751`); the viewport disables local wheel handling while the protocol owns it (`X/browser/Viewport.ts:65`). Consequently, **dropping outgoing mouse strings while leaving tracking armed would leave ordinary selection disabled and consume wheel events without delivering them to the program**. Disabling all reports also removes application-directed mouse clicks, drags, and wheel interaction. Without mouse tracking, the alternate-buffer wheel fallback can emit arrow-key sequences (`X/browser/CoreBrowserTerminal.ts:806`, `:837`), so “disable mouse reports” does not imply “wheel can never send input.” These are compatibility facts for planning, not a suppression design.

## 4. Master status and existing coverage

At inspection, both checkout HEAD and `git ls-remote origin refs/heads/master` were:

```text
6ec71d427574f3a1b74bfbe2042e0ea9a819d191
```

CARD-0142's factory/component change is `4f0877124692ccafd8df2f080d3120fa00c41720` (2026-08-23). `git diff --quiet 4f0877124692ccafd8df2f080d3120fa00c41720 HEAD -- client/src/features/board/SessionTerminal.tsx client/src/features/board/terminalMirror.ts` exited 0. The real-xterm tests were added in `1b1dae4dacc2765c3fdd43f6a95077dc06478124` that day. The pertinent component and suppressor have not changed since August; this is not an already-handled mouse fix.

`client/src/features/board/terminalMirror.test.ts:123` writes Grok's mouse DECSET and explicitly expects `mouseTrackingMode === 'any'` at `:139`. It never generates a mouse event. The startup test at `:82` likewise asserts silence while writing output, which does not exercise a later hover. `SessionTerminal.test.tsx:66` replaces xterm with a mock. Neither test establishes that mouse movement is suppressed.

## Remaining uncertainties

- The stored capture proves Grok armed this mode in August, not that every current Grok version/session still does. A later reset, missing startup bytes in an evicted replay buffer, inactive session, or unmounted panel changes exposure.
- There is no stored SGR-input/composer before-and-after capture here. Whether Grok renders garbage, takes a UI action, ignores the report, or correctly uses it is unconfirmed. The earlier DA1 corruption measurement cannot be relabeled as mouse evidence. Resolving that would require a separately authorized isolated TUI observation or stored input/output evidence.
- Current Claude/Codex tracking modes, actual operator reliance on application mouse controls, and event/request rates were not measured. The forwarding mechanism itself is provider-independent once the same modes are armed.
- The bookkeeping effects above are source reconstructions. No live manual-turn, watchdog, or seat-release consequence is claimed.

## Not done, noted

Recommend **disable mouse-report forwarding**, with a test, because incidental hover reaches both live input and Antiphon's pending-text bookkeeping; no fix was designed or implemented. Proposed deterministic Vitest evidence: mount `SessionTerminal` with real `createMirrorTerminal`, mocked transports and fixed cell geometry; await writing the stored Grok fixture; dispatch no-button moves to two distinct cells and assert zero `sendSessionInput` calls, while a bare-xterm positive control produces `ESC[<35;5;3M` and `ESC[<35;6;3M` for those cells and ordinary `terminal.input('x')` still forwards once; this zero-call expectation is predicted red on the inspected source, not an executed result, and selection/wheel compatibility remains a Plan obligation.

--- next stage ---
next: plan
handoff: SGR hover forwarding is confirmed at 6ec71d42 from the stored Grok DECSET and pinned xterm source; reports also enter pending-input bookkeeping. Plan mouse-report suppression with a deterministic real-xterm regression and explicit selection/wheel behavior. Grok composer corruption remains unmeasured; do not claim it reproduced.
artifact: docs/investigations/2026-10-19-card-0148-grok-terminal-panels-arm-sgr-mouse-tracking.md
