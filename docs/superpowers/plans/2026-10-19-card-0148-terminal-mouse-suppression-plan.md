# CARD-0148: keep terminal-panel mouse interactions local

Date: 2026-10-19. Stage: Plan. Task: `72057511`.
Source inspected: `e7aae0e605de511fc108a404a794c54e10f1fb84`.
Target: **master only**. Code authoring and verification budget: **45 minutes**.

Suppress browser-generated mouse reports before they reach `SessionTerminal.onData`,
while leaving ordinary selection and scrollback usable. Preserve keyboard input,
paste, keypad input, rendering, and CARD-0142's query/focus suppression. The deliberate
compatibility change is that a panel no longer operates a TUI with mouse clicks,
drags, or wheel events. Keyboard navigation remains available.

The supplied brief requests a test design but does not explicitly fold the separate
TestDesign stage into Plan. This artifact includes the proposed verification design
and checkpoint selections; **next is TestDesign**, to finalize the harness and roster
before the bounded Code dispatch. No product/test implementation or test execution
is claimed by this plan.

## Inputs and ground truth

Land this plan together with the already committed
[CARD-0148 investigation](../../investigations/2026-10-19-card-0148-grok-terminal-panels-arm-sgr-mouse-tracking.md).
The investigation is in the assigned branch's start commit; preserve it without
rewriting its historical findings. Also read:
[CARD-0142 plan](2026-08-23-card-0142-terminal-mirror-replies-plan.md), especially D7/D8.
CARD-0142 deliberately retained mouse reporting; this card supersedes that decision.

| Card assumption / proposed invariant | What the inspected code actually does | Consequence |
|---|---|---|
| Grok can arm hover reporting. | `__fixtures__/grok-startup.ts` contains `ESC[?1003;1006h`; `terminalMirror.ts` lets it through. | Confirmed mechanism, independent of provider once those modes are present. |
| Mouse reports can reach the pty. | `SessionTerminal.tsx:52` sends every `onData` string for a Running session. The component does not subscribe to `onBinary`. | SGR and SGR pixel reports use the admitted input route; legacy binary mouse reports currently have no subscriber. Suppress all supported mouse protocols anyway. |
| Output-only startup tests prove safety. | `terminalMirror.test.ts` writes startup fixtures but generates no mouse events; its final test explicitly expects mode label `any`. `SessionTerminal.test.tsx` mocks xterm. | Keep the old assertions' history, add real DOM mouse events and a real component boundary test. |
| A payload filter would restore normal mouse use. | xterm disables ordinary selection and hands wheel ownership to tracking when its protocol advertises events. | A wire filter alone leaves local interaction broken. |
| Disabling tracking makes wheel local. | xterm's no-scrollback fallback can send arrow keys, including in the alternate buffer. | Suppress that fallback explicitly, while retaining the normal viewport's wheel handler. |
| Other modes can simply fall through a mouse parser hook. | The public CSI hook receives a copied parameter array and returns one consume/fall-through verdict for the whole sequence. | Consuming `?1003;2004h` also loses bracketed paste; re-enqueueing retained parameters can reorder them behind following output. |
| The server ignores these sequences. | `AgentSessionRuntime.cs:1861` records input before forwarding; `PendingTerminalInput.Append` keeps the printable remainder. `SessionRunnerRuntime.cs:3881` marks successful non-CR input pending. | Prevention at the panel also prevents this source of pending-input pollution. No claim that a live release or Grok composer corruption was reproduced. |
| A host-specific fix is required. | The changed behavior is browser xterm and React wiring; native pty code need not change. | Portable client lane; no OS or runner pin. |

Dependency ground truth is the **lockfile's npm 6.0.0 archive**, not a floating
upstream checkout. This Plan downloaded that archive, verified its full SHA-512
against the lockfile, and inspected the following files under its `package/` root:

- `src/common/services/CoreMouseService.ts:197-238`: `addProtocol` replaces an
  instance registry entry; event flags control `areMouseEventsActive`; reset does
  not rebuild that registry. At `:324`, `restrict` rejects an event before encoding.
- `src/browser/CoreBrowserTerminal.ts:718-780`: event flags enable/disable selection
  and reporting listeners. At `:806-839`, the custom wheel hook runs before the
  arrow fallback. `src/browser/Viewport.ts:65` independently enables local wheel
  handling when the protocol's WHEEL flag is absent.
- `src/common/InputHandler.ts:1923-1980`: the four tracking protocols are X10,
  VT200, DRAG, ANY; encodings include DEFAULT, SGR, SGR_PIXELS. Unrelated DEC modes
  are handled in the same parameter loop.
- `src/common/public/ParserApi.ts:12`: public CSI callbacks receive `toArray()`.
  `typings/xterm.d.ts:1076-1094` documents the public custom wheel hook.

Archive: [locked npm source](https://registry.npmjs.org/@xterm/xterm/-/xterm-6.0.0.tgz).
The downloaded source is ignored evidence, not a new vendored dependency. Use the
archive for exact line references: the upstream Git tag's typings are not identical
to the published archive inspected here.

## Decisions

### D-1 — Own suppression in the production terminal factory

`createMirrorTerminal` installs the policy before `open` or any `write`. It applies
to initial backlog, queued/live deltas, reconnect replay, and every new terminal
instance. No component-specific replay window or provider check is needed.

The invariant concerns **browser-generated pointer input**. Deliberately pasted
text is not inspected or rewritten merely because it resembles an escape report.
Rejected: SGR regexes in `onData`, an `onKey`-only transport that loses paste/IME,
and temporarily setting `disableStdin` during replay.

### D-2 — Disable the supported mouse protocols at their instance registry

Add a small `terminalMouse.ts` module exporting `configureLocalTerminalMouse`.
Access only `_core.coreMouseService.addProtocol` through a narrowly typed adapter
for the locked xterm version. Replace the four **instance** entries `X10`, `VT200`,
`DRAG`, `ANY` with `{ events: 0, restrict: () => false }`. Do not mutate shared
default objects, replace the service, patch prototypes, or import xterm's source
modules. `NONE` remains xterm's default. No encoding override is necessary.

This is an intentional private-API dependency. The factory must reject an absent
or incompatible `addProtocol` seam with a descriptive error and dispose its new
terminal, rather than returning a terminal that can forward reports. Keep this
adapter isolated and explain the dependency in its comment. A dependency update
must recheck xterm's protocol inventory and run the behavioral tests below.

Why both fields: `events: 0` restores selection/local wheel ownership and prevents
reporting listeners from being installed; `restrict: false` also prevents emission
if a service caller attempts to send an event directly. Registrations survive
terminal reset. Other terminals, including the test's bare positive control, keep
their original registry.

DECSET/DECRST still run normally, including mixed parameter lists and split writes.
Consequently `terminal.modes.mouseTrackingMode` may still say `any`: that property
reports the requested protocol name, not whether its replacement emits events.
Do not use that label as the regression oracle or require it to become `none`.

Rejected alternatives: swallowing a whole mixed DECSET loses rendering/paste modes;
queued replay of retained parameters changes ordering; synchronous private parser
calls require a larger, more fragile dependency; a new streaming ANSI filter must
handle fragmented CSI and control strings; post-parse resets have timing windows;
DOM capture blockers alone leave xterm selection disabled. The instance registry
is the smallest inspected seam that disables reporting and restores local behavior
without changing the output stream.

### D-3 — Define wheel and selection behavior explicitly

The same helper installs `terminal.attachCustomWheelEventHandler(() => false)`.
In the pinned implementation this vetoes report/arrow generation in the core mouse
handler; it does **not** veto the separate viewport wheel listener. Do not call
`preventDefault` or `stopPropagation` from this hook, and do not add custom wheel
delta conversion or a second scroll implementation.

| Interaction | Required behavior |
|---|---|
| Hover, button press/release, drag | Never emit mouse reports on either xterm data channel. |
| Ordinary left drag, without Shift/Option | Select visible text locally using xterm's normal selection service. |
| Selection copy | Existing copy shortcut and clipboard behavior remain. Its current Ctrl+C pass-through is a separate keyboard policy and stays unchanged. |
| Wheel with normal-buffer history | Move the local viewport up/down; scrollbar and `scrollToLine` remain usable; no session input. |
| Wheel at a scrollback boundary | Use existing local viewport boundary behavior; no mouse report or synthesized key. |
| Wheel in alternate buffer / with no retained history | No session input and no invented history. The viewport may have nowhere to move. |
| Alt-click | Disable `altClickMovesCursor` for mirror instances so a pointer gesture cannot synthesize cursor-movement keys. Ordinary selection remains available. |
| Explicit keyboard arrows, text, paste, keypad | Preserve normal input, subject to the existing Running-session gate. |

Application-directed clicking/dragging/wheel navigation is intentionally removed
for every provider. An opt-in interactive-mouse mode is outside this card.

### D-4 — Keep the server input route unchanged

No server guard is required for this panel defect: preventing emission means there
is no input request to pollute either pending-input tracker. The endpoint serves
other input producers and does not carry trusted provenance distinguishing pointer
reports from deliberate text. A global string blocklist would change that contract
and would not repair local selection or scroll behavior.

This is not a server security boundary and does not protect old bundles, other
clients, direct API input, or already contaminated pending buffers. A separate
server input-accounting change would need its own protocol/provenance design and
tests. Do not broaden this 45-minute client change to it.

### D-5 — Preserve the earlier report suppressors and source provenance

Keep CARD-0142's query and lone-focus handling. Mixed focus-report suppression is
not added here. Update its source comment and old test description to distinguish
parsing a mouse-mode request from emitting reports. Preserve the captured Grok
fixture exactly. Do not edit the historical CARD-0142 plan or the investigation
to pretend those earlier stages implemented suppression.

### D-6 — Placement and delivery

Read-only `GET /api/runner-defaults` and `GET /api/session-runners` succeeded during
Plan on 2026-10-19. Defaults were revision 2, with no per-kind overrides; the
catalogue contained eligible Windows and Linux capacity and a stale/draining row.
Those observations are time-sensitive, not placement configuration.

The checkpoint lane is **portable client (Node/Vitest/jsdom + PowerShell)**. Re-read
both routes at dispatch. Omit `-Runner` and `-Platform`; use `-Platform Any` only
if a pre-existing card pin needs clearing. Do not embed a fleet host/address in this
plan or change runtime defaults. The Code environment needs Node/npm; this Plan
shell has PowerShell but no Node and performs no test execution.

Publish to master only after the normal stages. No RC propagation or product-host
restart is part of this card. Publishing is not activation: existing loaded panels
retain their old code until the normal bundle activation and page reload/remount.
No session-input waiting/release lifecycle is added or changed.

## Slices

### S1 — Commit the real regression before the fix (about 20 minutes)

Add `client/src/features/board/SessionTerminal.mouse.test.tsx`. Use real xterm and
the real production factory/component, with isolated transport and FitAddon mocks.
Keep its geometry helper local unless TestDesign demonstrates reuse is worthwhile.
Use the existing `__fixtures__/grok-startup.ts` without editing captured bytes.

Add the V/R cases below. Keep the existing mocked `SessionTerminal.test.tsx` for
transport ordering, inactive input, factory wiring, resize/disposal, and copy.
Retain `terminalMirror.test.ts` as the CARD-0142 compatibility suite. Any wording
change there must retain the original assertion until the behavior is implemented.

Commit and push S1 with a truthful red-first subject, run CP-1, and retain its exact
assertion failures. Fix a harness defect before proceeding; missing geometry,
zero selected tests, runtime errors, and a failing positive control are not a
reproduction. The transport-silence assertion must fail on actual mouse input.

### S2 — Apply the policy and run one narrow green (about 10 minutes)

Add `client/src/features/board/terminalMouse.ts` for D-2/D-3. Call it from
`client/src/features/board/terminalMirror.ts` before returning the terminal; ensure
failed setup disposes the instance. Force `altClickMovesCursor = false` as part of
the installed policy, even if factory options requested it. Update source comments
and the final CARD-0142 test description as in D-5.

No production change to `SessionTerminal.tsx`, `terminalCopy.ts`, API functions,
server, runner, or pty is expected. If Code discovers that the inspected seam is
unusable, stop with the concrete failure and return to Plan; do not substitute a
payload-only filter or quietly swallow mixed modes.

Commit and push S2 before CP-2. Report the tested full SHA and per-file counts,
not just the script exit. Re-run only an affected row after a real correction.
Keep generated logs/receipts ignored. Run `scripts/check-evidence-diff.ps1` over
the Code task base through its pushed tip. Do not manufacture a passing result
when the intentional S1 red or an unrelated inherited failure is present.

## Verification design

This is the proposed closed ordinary scope for TestDesign to finalize. It requires
no live Grok session, real pty, server build, external traffic, or browser E2E run.
The durable oracle is whether a mouse gesture can become an input request, not a
claim about how a third-party composer renders those bytes.

### Deterministic harness

Mount the real `SessionTerminal` with a Running session. Mock `getSessionBuffer`,
`sendSessionInput`, `resizeSession`, SignalR connection callbacks and FitAddon only.
A spy wrapper may capture the terminal returned by the **real** factory; it must
call the original factory. Never replace xterm, the parser, mouse service,
selection service, viewport, or component's `onData` callback with a fake.

jsdom has no measured terminal geometry. Set a fixed screen rectangle with zero
padding, cell dimensions 10x20 CSS pixels, device pixel ratio 1, and a nonzero
character measurement. Keep any `_core` access for these layout facts test-local.
Use the real coordinate/event path; do not stub `triggerMouseEvent` or emit a canned
`onData` value. Drive the viewport's real render/geometry synchronization with
bounded animation-frame flushing where necessary. The positive control below
must use the **same geometry helper** as the production component.

Await actual `terminal.write(data, callback)` completion. For component writes,
observe that the expected transport callback wrote the data, then queue an empty
write with a completion callback as a parser barrier. Do not use fixed sleeps or
assume receipt of a buffer means it was parsed. Use fresh sequences on reconnect
so the component's duplicate suppression does not skip the intended replay.

Dispatch bubbling, cancelable mouse events at two distinct in-range cells, with
`buttons: 0`: pixel `(45,50)` then `(55,50)` with a screen origin of `(0,0)`.
Use explicit button/detail fields for selection events and release every drag on
the document. Dispose terminals, unmount components, remove hosts, and restore
geometry/clock/clipboard overrides after every case.

### Behavioral roster

All new cases belong in `SessionTerminal.mouse.test.tsx`. Suggested stable names
below make red/green outcomes identifiable. TestDesign may refine mechanics without
weakening the assertions or increasing the Code budget.

| ID | Case / observation | Expected at S1 / S2 |
|---|---|---|
| V-1 | `bare xterm reports two distinct Grok hover cells`: raw Terminal, same options/geometry, parsed Grok fixture; clear startup emissions before gestures. Require exact `ESC[<35;5;3M`, `ESC[<35;6;3M`. | Pass / pass. This proves mouse geometry and encoding are live. |
| V-2 | `panel keeps hover local after <source>` for backlog, live delta, reconnect replay. After the fixture parses, two hover events produce zero `sendSessionInput` calls; then `terminal.input('x')` produces exactly one call with the session ID and `x`. | Three transport-silence failures / three passes. Do not count unrelated startup/focus traffic as the red. |
| V-3 | `pointer reports are suppressed for <mode>/<encoding>`: 4 modes (`9`, `1000`, `1002`, `1003`) x 3 encodings (default, `1006`, `1016`), 12 parameterized cases. Use a real factory terminal; subscribe to both `onData` and `onBinary`; exercise down/drag/up/hover/wheel, including modified gestures. | Report assertions red / all silent. Geometry must make each bare counterpart capable of a valid press report. |
| V-4 | `normal drag selects after Grok tracking`: write known visible text, ordinary left-drag across it, require exact nonempty `getSelection()` content and zero input before any keyboard shortcut. Exercise the existing copy key handler with a clipboard spy; preserve current keyboard pass-through semantics. | Selection fails / passes. Calling `terminal.select()` is not a drag-selection proof. |
| V-5 | `wheel scrolls local history without input`: write numbered lines beyond viewport height, start at a middle scroll position, dispatch wheel on the screen/viewport DOM, flush bounded frames, require public `buffer.active.viewportY` to move in both directions and zero input. Cover a clamped boundary too. | Input/scroll invariant fails / passes. Direct `scrollLines` alone is not wheel proof. |
| V-6 | `wheel without history never synthesizes keys`: alternate buffer, valid device cell geometry, both wheel directions, require no mouse or arrow input; return to normal buffer and repeat with no retained history. | Input assertion fails / passes. Bare xterm must produce an arrow under the same no-tracking geometry. |
| V-7 | `mixed modes and split writes retain ordering`: parse `ESC[?1003;1006;2004;1049h` split inside the CSI; immediately write visible text. Assert alternate-buffer text and bracketed paste behavior, then matching reset returns to normal buffer. Mouse remains silent throughout. | Mouse assertion red / pass; unrelated rendering/paste assertions pass in both. |
| V-8 | `reset and rearm stay local`: reset the terminal, replay mouse setup, and repeat hover/press/wheel; a newly constructed bare terminal still emits reports. | Red / pass. Guards persistence and per-instance isolation. |
| R-1 | `keyboard and paste remain input`: type text, send an explicit arrow, paste under `?2004h`; assert exact expected payloads and bracketed-paste framing. Do not classify intentional escape-like pasted text as mouse output. | Pass / pass. |
| R-2 | `alt click cannot move the remote cursor`: short Alt-click with cursor and click at different cells, assert no synthesized cursor keys; ordinary text still forwards. | Red / pass. |
| R-3 | Existing 7 `terminalMirror.test.ts` cases and 5 `SessionTerminal.test.tsx` cases. Preserve query suppression, color set, focus, typing, factory wiring, inactive gate, ordering/reconnect, cleanup, copy. | 12 pass / 12 pass. |

With this expansion the proposed roster is **35 Vitest results**: 23 new (including
three ingress cases and twelve mode/encoding cases) plus 12 existing. Count actual
executions, not assertions inside a case. TestDesign must record any justified
roster adjustment before Code; do not reduce the floor to hide unselected tests.

### Execution and cost

Allow 20 minutes for harness/regression authoring, 10 for implementation, 5 for
CP-1 (including first dependency installation), 3 for CP-2, and 7 for publication,
evidence and correction margin: **45 minutes total**. These are estimates, not
measured timings. No full client suite, .NET suite, production build, lint sweep,
or live-session experiment is in this Code scope. TestDesign should flag any
required harness expansion that cannot fit, rather than weakening the positive
control or silently spending a second Code round.

Use the repository checkpoint tool's `run --plan <this-path> --rows CP-1` at the
committed S1 SHA and a separate run for `--rows CP-2` at S2, each with
`--expected-source-sha <full-sha>`. Bootstrap/build the checkpoint tool through
`scripts/build-slot.ps1` with an isolated `bin-.../` output if needed; use the
already built driver for execution. Command rows acquire their own build-slot
lease: do not wrap the row's `test-client.ps1` in a second slot. Wait/join every
owned run until terminal exit (continue `wait` after exit 75); keep source frozen
through the run. A slot timeout is not permission to run unleased.

CP-1 is intentionally a failed test run, never a green checkpoint. Preserve its
nonzero exit and actual failing assertions. CP-2 is the **one narrow green** through
`scripts/test-client.ps1`. Preserve the tool's CHECKPOINT line and the Vitest
roster/counts plus `CLIENT TESTS EXIT CODE`; command-row counts may not substitute
for the actual Vitest roster. No retries without a named failure and correction.

### Checkpoints

Both rows use the **portable client** lane. `Build: n/a` means no product build;
the script installs locked client dependencies when absent. TestDesign confirms
the proposed count and row mechanics before Code.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | n/a | portable-client-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/board/SessionTerminal.mouse.test.tsx src/features/board/terminalMirror.test.ts src/features/board/SessionTerminal.test.tsx` | V-1..V-8, R-1..R-3 | 35 executed, 0 skipped; V-1/control and existing cases pass; V-2 transport silence fails with actual SGR calls; preserve all other expected reds; nonzero test exit | n/a | 5 | true |
| CP-2 | S2 | n/a | portable-client-green | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/board/SessionTerminal.mouse.test.tsx src/features/board/terminalMirror.test.ts src/features/board/SessionTerminal.test.tsx` | V-1..V-8, R-1..R-3 | 35 executed, 0 failed/skipped; all three files; CLIENT TESTS EXIT CODE: 0 | n/a | 3 | true |

## Handoff

TestDesign should finalize the layout/frame harness, verify both positive controls
can fail for the right reason, and confirm the 35-result roster and checkpoint
execution within the 45-minute Code budget. The key review risk is the deliberately
isolated private xterm registry adapter. Do not replace the two DOM behavior
oracles (selection and wheel) with private-state assertions. No product decision
or live-session measurement is required to produce that design.

--- next stage ---
next: test-design
handoff: Finalize the real-xterm DOM/geometry harness and 35-result red/green roster for the per-instance protocol registry policy, local selection/wheel, and mixed modes; retain the two narrow client checkpoints and 45-minute Code budget. Master only; preserve the investigation alongside the plan.
artifact: docs/superpowers/plans/2026-10-19-card-0148-terminal-mouse-suppression-plan.md
