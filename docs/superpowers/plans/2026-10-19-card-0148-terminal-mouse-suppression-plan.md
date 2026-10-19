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

## Proposed verification (Plan-stage history)

The appended **Verification design** is the finalized TestDesign contract. This
proposal is retained as history; its roster, run selections and handoff are
superseded. Decisions D-1 through D-6 and the fix design above are unchanged.

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

### Proposed checkpoints (superseded)

Both rows use the **portable client** lane. `Build: n/a` means no product build;
the script installs locked client dependencies when absent. TestDesign confirms
the proposed count and row mechanics before Code.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | n/a | portable-client-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/board/SessionTerminal.mouse.test.tsx src/features/board/terminalMirror.test.ts src/features/board/SessionTerminal.test.tsx` | V-1..V-8, R-1..R-3 | 35 executed, 0 skipped; V-1/control and existing cases pass; V-2 transport silence fails with actual SGR calls; preserve all other expected reds; nonzero test exit | n/a | 5 | true |
| CP-2 | S2 | n/a | portable-client-green | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/board/SessionTerminal.mouse.test.tsx src/features/board/terminalMirror.test.ts src/features/board/SessionTerminal.test.tsx` | V-1..V-8, R-1..R-3 | 35 executed, 0 failed/skipped; all three files; CLIENT TESTS EXIT CODE: 0 | n/a | 3 | true |

## Plan-stage handoff (historical)

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

## Verification design

Finalized 2026-10-19 by TestDesign `cf7542ec`, against
`d4901688f0442f1b2b72dbed04ab591e6d63b085`. This section replaces the proposed
verification scope above, without changing D-1..D-6. **Code remains 45 minutes,
with exactly one red-first witness and one narrow green run.** TestDesign ran no
builds or tests; the optional quick proof was not used. Expected outcomes below
are requirements, not execution claims.

### Inspection

Bodies read, including setup/cleanup and assertions:

- `terminalMirror.ts`, all seven `terminalMirror.test.ts` bodies and the unchanged
  Grok/Claude/Codex startup fixtures | factory, query/focus and parser boundaries
  -> V-1, V-3, V-7, V-8, R-1, R-3.
- `SessionTerminal.tsx`, all five `SessionTerminal.test.tsx` bodies, including its
  hoisted Terminal/SignalR mocks | Running input, snapshot/live/reconnect ordering,
  disposal and copy -> V-1, V-2, V-4, R-3. These are the nearest fixtures for the
  new `SessionTerminal.mouse.test.tsx`; do not copy the fake Terminal into it.
- `terminalCopy.ts`, `TerminalKeypad.tsx`, `test/utils.ts`, `test/setup.ts`,
  `vite.config.ts`, `package.json` and xterm's lockfile entry | providers,
  clipboard, matchMedia, null canvas, geometry, timeout and dependency boundaries
  -> V-4, R-1, R-2 and the harness below.
- `scripts/test-client.ps1`, `PlanTableImporter.ImportMarkdown/ExtractSection`,
  and the testing owner's checkpoint schema | argument forwarding, real Vitest
  results, first exact `### Checkpoints` heading -> CP-1/CP-2. Only the appended
  table has that exact heading; the historical proposal cannot be imported.
- Re-downloaded the lockfile's xterm 6.0.0 archive and verified its full SHA-512.
  Read `CoreBrowserTerminal.bindMouse/open`, `CoreMouseService` protocol/encoding
  definitions, constructor, reset and trigger; `InputHandler.setModePrivate`;
  `MouseService`, `input/Mouse`, `CharSizeService`, `DomRenderer` measurement,
  `WidthCache`, `Viewport` and SelectionService down/move/up bodies | geometry,
  event ownership, direct restriction, selection, wheel fallback and mixed modes
  -> V-1..V-8, R-1/R-2, G-1..G-17. Extracted source is ignored evidence under
  `.antiphon/card0148-testdesign/`, not a product dependency.
- CARD-0148 investigation, CARD-0142 D7/D8, project/testing/orchestration owners
  and session runtime delivery rules | historical claims and downstream receipt
  boundaries -> delivery exclusions below. No server implementation is changed.

### Delivery inventory

**New or changed asynchronous delivery paths: zero.** This change removes a
browser event producer before the existing input route. No queue, persistence,
retry, acknowledgment, transcript or recipient-state contract is changed.

| Boundary | Producer -> destination | Identity / persistence / recovery | Observable evidence and limit |
|---|---|---|---|
| Changed suppression boundary | DOM pointer -> real xterm -> real component onData -> `sendSessionInput` spy | Session ID; no new durable message ID, persistence or recovery | V-1/V-2 assert zero calls after real gestures and a working explicit-input control. This proves suppression before request creation, not receipt. |
| Existing output ingress exercised | Buffer response or SignalR delta -> component -> real xterm parser | Session ID + sequence; existing buffer snapshot/replay and in-memory pending deltas | V-2 waits for the actual write and parser completion, then probes gestures. Mocks do not prove network recovery or durable output delivery. |
| Existing admitted explicit input | Keyboard/paste/keypad -> existing input API -> runner/pty/session | Existing session identity; unchanged downstream persistence/recovery | R-1 checks exact calls only. A request, HTTP success, Sent flag, queue row or ack is never recipient evidence. Actual session delivery would require the matching complete UserPrompt transcript. |

The real-queue producer-to-recipient battery (busy recipient, already eligible
recipient, crash/enqueue-failure cuts at each durable handoff) is **not applicable**
to this patch: it creates/changes none of those handoffs. No V/R makes a delivery
claim. If Code changes a queue, retry or downstream input handoff, return to Plan
for that inventory and complete recipient tests; do not extend the mock verdict.

Substitutes are explicit: transport mocks cannot prove receipt; jsdom geometry
cannot prove physical-browser rendering or OS selection; a clipboard spy proves
the copy request, not the OS clipboard; a direct mouse-service invocation below
proves the defensive restriction only, not DOM reachability. The DOM witness
remains mandatory. There are no new delivery/recovery guards to omit from PCs.

#### Harness recipe H (shared by all new cases)

All new cases live in `client/src/features/board/SessionTerminal.mouse.test.tsx`
under the exact describe name `CARD-0148 mouse policy`. Use real xterm, parser,
selection, viewport, production factory and component. Only mock transport API,
SignalR and FitAddon. A factory spy must call the original. Use the existing
provider utility and lifecycle cleanup; local helpers do not change global setup.
R-4/R-5 alone may wrap the helper export with a pass-through fault-injection spy:
it receives the factory's real terminal, installs the requested seam fault, then
calls the real helper. This places the fault after existing query setup and before
mouse setup. Restore the original core/service inside a forwarding dispose spy
before invoking real disposal. Do not replace the constructor or helper behavior
with a fake verdict. Normal behavior tests run with no fault hook.

Install geometry before `open`: DPR 1, zero padding on `.xterm` and
`.xterm-screen`, origin (0,0), 20 columns x 6 rows, 10x20 CSS cells, 200x120
canvas, lineHeight 1 and letterSpacing 0. The component capture wrapper may pass
these layout overrides to the real factory, preserving its `disableStdin` option.
With the existing null-canvas setup, xterm uses DOM measurement. Supply scoped
`offsetWidth`/`offsetHeight` getters for `.xterm-char-measure-element`: width =
text length x 10 (the measuring string repeats 32 times), height = 20; supply
screen/container rectangles and client dimensions. Delegate unrelated getters to
the originals. This lets real CharSizeService and DomRenderer calculate both CSS
and device dimensions. Assert their nonzero 10x20 cell/200x120 canvas facts as
harness preconditions, not as the product oracle. Any additional private access
is narrowly typed, test-local and limited to measurement/synchronization, except
for the explicitly declared restriction/failure-injection tests.

Set `smoothScrollDuration: 0`. Await `write(data, callback)`. For component writes,
first observe the expected real write via a pass-through spy, then await an empty
write callback as a FIFO parser barrier. Drive a real refresh and at most ten
animation frames until the required render/viewport condition holds. A failed
bound is a harness failure, never success or a reason to increase the global
20-second timeout. Do not replace scrolling functions, assign `viewportY`, fake
selection, stub `triggerMouseEvent`, or call onData manually for a pointer test.

Use bubbling/cancelable events. Hover uses `(45,50)` then `(55,50)`, `buttons: 0`.
A left press uses `button: 0`, `buttons: 1`, `detail: 1`; move on document with
buttons 1 and release there with buttons 0. On visible `HELLO WORLD` at row zero,
drag `(1,10)` to `(51,10)` to select exactly `HELLO`. Wheel uses `deltaMode: 1`,
`deltaY: -1` then `1`, no modifiers, dispatched on the real screen inside xterm's
scrollable element. Add Ctrl/Alt-modified press/move variants; Shift intentionally
forces selection on some platforms, so it is not the sole report witness.

Clear startup emissions only after parsing, before the gestures. Collect both
onData and onBinary on factory/bare terminals; component transport calls are a
separate oracle. Each bare control is separately constructed with the same
geometry, without the mirror policy. Cleanup always releases document drags,
disposes terminals/subscriptions, unmounts, removes hosts, restores property
getters, fault hooks, timers and clipboard; no shared default object is
left mutated. Run these tests serially within the file.

### Proves it works now

Every row binds **recipe / variant / exact case / oracle / reachability /
classification**. Names below are literal `it` titles, not prose placeholders.
Payload and source bindings for new tests remain **binding required** until Code
commits their bodies. Code records the exact title, path, decisive assertion and
actual count for every row; an unbound payload remains binding required after
Code and prevents a complete verification claim.

| ID | Case / layer | Recipe and variants | Oracle and reachability | Classification |
|---|---|---|---|---|
| V-1 | `panel rejects hover from backlog` / real component | H; bare Terminal consumes unchanged GROK_STARTUP, then Running panel consumes it as backlog sequence 1 | Bare hover emits exactly `['\x1b[<35;5;3M','\x1b[<35;6;3M']` onData. Panel hover must call transport zero times. Afterwards `terminal.input('x')` calls `(session-1,'x')` exactly once. CP-1 must fail at the zero-call assertion with those actual SGR calls, after the bare control passed. | Real DOM regression witness; one result, embedded positive reachability control. |
| V-2 | `panel rejects hover from live and reconnect` / real component | H; hold initial buffer, queue matching sequence-2 Grok delta; release empty snapshot sequence 1. Then send live sequence 3. Reset tracking before each phase so it must rearm. Reconnect returns Grok snapshot sequence 4. | Verify each real write plus parser barrier before two hover events; zero calls at each phase, then exact `x` call. Fresh sequences avoid deduplication. | Ingress coverage; one result, three labeled phases; existing mocked tests still own ordering assertions. |
| V-3 | `protocol X10 stays local`, `protocol VT200 stays local`, `protocol DRAG stays local`, `protocol ANY stays local` / real factory | H; four results for modes 9/1000/1002/1003. Within each result use fresh terminals for DEFAULT, SGR (1006), SGR_PIXELS (1016), exercising down/up, held drag, hover, wheel and Ctrl/Alt variants. | Bare unmodified press at (45,50) must emit `\x1b[M \x25\x23` onBinary for DEFAULT, `\x1b[<0;5;3M` onData for SGR, `\x1b[<0;45;50M` onData for pixels. Factory channels stay empty and ordinary drag selects `HELLO` in every variant. Then call the real service with a fresh valid `{col:4,row:2,x:45,y:50,button:0,action:1,ctrl:false,alt:false,shift:false}`; require return false and both channels empty. A bare fresh event must return true and emit the same press bytes. | Four protocol regressions; 12 labeled encoding variants, not 12 extra test results. Direct invocation separately covers restrict when event flags are zero. |
| V-4 | `normal drag copies selection` / real factory + copy handler | H; parsed Grok fixture, visible HELLO WORLD, plain drag. Invoke the production copy handler with Ctrl+C and clipboard spy. | Exact `HELLO` selection and zero pointer input before shortcut; clipboard receives `HELLO`; handler returns true, preserving existing pass-through semantics. Bare no-tracking drag with H selects the same text. | Local interaction and copy compatibility; one result. |
| V-5 | `wheel scrolls local history` / real factory | H; scrollback 100, 30 numbered lines, parsed Grok modes. Start at public scrollToLine(10), synchronize, then wheel up/down; also wheel at top/bottom boundaries. | Public `buffer.active.viewportY` decreases/increases from the prior position; at boundaries remains clamped; both input channels empty throughout. Bare no-tracking history scrolls with the same wheel recipe. scrollToLine establishes starting positions only. | Real viewport behavior; one result; no fabricated scroll oracle. |
| V-6 | `wheel without history stays local` / real factory | H; fresh alternate buffer and fresh normal terminal with **scrollback: 0**, no tracking, both directions; repeat after Grok mode setup. | Bare no-tracking counterpart emits exactly `\x1b[A` then `\x1b[B`; mirror channels stay empty, baseY/viewportY remain 0. Empty normal history with nonzero scrollback is not enough: xterm's hasScrollback tests buffer capability. | Wheel fallback regression; one result. |
| V-7 | `mixed split modes retain order and input` / real component/factory | H; write `\x1b[?1003;1006;20`, then `04;1049hALT` in the next queued write; await completion. Exercise explicit input as R-1, hover, then `\x1b[?1003;1006;2004;1049lNORMAL`. | Active buffer is alternate and row zero begins ALT before reset; paste is bracket-framed; pointer channels empty; reset restores normal and NORMAL appears there; subsequent paste is unframed. Query/focus handlers cannot consume the combined CSI. | Mixed parser ordering + compatibility; one result shared with R-1. |
| V-8 | `reset and instances remain independent` / real factory | H; construct a bare control before mirror. Mirror reset(), rearm each of four modes plus SGR in turn, then hover/press/wheel and ordinary drag; construct a fresh mirror and another bare control afterwards. | All mirror channels empty and selection still HELLO after rearm; both bare instances still emit exact SGR press, and ANY hover emits the V-1 bytes. | Reset persistence and per-instance isolation; one result with labeled modes. |

### Guards the regression

| ID | Exact case / recipe and variants | Decisive assertion / reachability / classification |
|---|---|---|
| R-1 | V-7's `mixed split modes retain order and input`; explicit textarea keydown ArrowUp, public input('x'), public paste('literal \x1b[<35;5;3M'); enable matchMedia's coarse-pointer branch and click the real keypad Arrow up button | Exact separate calls `\x1b[A`, `x`, `\x1b[200~literal \x1b[<35;5;3M\x1b[201~`, `\x1b[A` for session-1; after reset paste is unframed. Escapelike deliberate paste is preserved. No extra result. Keyboard/clipboard/keypad admission, not receipt. |
| R-2 | `alt click stays local`; H, no tracking, visible text, cursor at column 10, Alt-down/up at column 2 within 100 ms with no drag; repeat after Grok setup. Factory receives override `altClickMovesCursor: true` | Bare same-geometry no-tracking terminal with that option true emits nonempty cursor movement bytes; mirror both channels empty and subsequent input('x') emits x. The explicit true override tests the policy cannot be defeated by option precedence. One result. |
| R-3 | All seven existing terminalMirror tests and all five existing SessionTerminal tests, unchanged assertions | Preserve startup silence, bare query/focus controls, colour set, typing, lone-focus versus mixed-mode parsing, input gating, transport order/reconnect, cleanup and copy. Only rename the old mixed-mode description/comment as D-5 directs; keep `mouseTrackingMode === 'any'`. Twelve results; no new downstream claim. |
| R-4 | `factory rejects incompatible mouse seam`; H's helper pass-through hook injects missing core/service or absent/noncallable addProtocol immediately before mouse setup, one at a time | Factory throws a descriptive error naming mouse policy/xterm/addProtocol and returns no instance. Hook restores the real core before cleanup so disposal remains callable. A valid unmodified factory returns a real working terminal. One result; shape-failure injection only. |
| R-5 | `factory disposes on setup failure`; H's helper hook makes addProtocol throw a sentinel on the second registration, spying on and forwarding real dispose | Factory propagates setup failure and disposes the exact new terminal once; no usable instance escapes. Valid factory creation in the same fixture must still work. One result; partial-setup recovery. |

**Final roster: 14 new + 12 existing = 26 Vitest results.** V-1/2/4/5/6/7/8
contribute seven, V-3 four, R-2/4/5 three. R-1 shares V-7; R-3 contributes twelve.
Only V-1 is needed in S1: commit/push that real witness and its harness, then CP-1.
Complete the other thirteen new results with S2 before its commit and CP-2. This
refines the verification authoring order without changing the production slices.
No test is marked skip/todo. Internal encoding/ingress loops carry assertion
labels so failure evidence identifies the variant; they do not inflate counts.

### Guard inventory

Safety here means preventing unintended session input and preserving the local
terminal contract, not a server security boundary. Independently bypassable
protocol event ownership and direct restriction are deliberately separate guards.
Existing CARD-0142 query/focus and Running-session guards are unchanged, retained
by R-3, and excluded from this patch's new mutation obligations; no claim is made
that their historical PCs were executed here.

| Guard | Plan reference and guard/invariant | Distinct control |
|---|---|---|
| G-1 | D-1: every factory result installs the policy before any open/write | PC-1 |
| G-2 | D-2: X10 events=0 restores ordinary selection | PC-2 |
| G-3 | D-2: VT200 events=0 restores ordinary selection | PC-3 |
| G-4 | D-2: DRAG events=0 restores ordinary selection | PC-4 |
| G-5 | D-2: ANY events=0 restores ordinary selection | PC-5 |
| G-6 | D-2: X10 restrict rejects a valid direct event | PC-6 |
| G-7 | D-2: VT200 restrict rejects a valid direct event | PC-7 |
| G-8 | D-2: DRAG restrict rejects a valid direct event | PC-8 |
| G-9 | D-2: ANY restrict rejects a valid direct event | PC-9 |
| G-10 | D-2: unavailable/incompatible registration capability fails closed with diagnostic | PC-10 |
| G-11 | D-2: failed or partial policy setup disposes the allocated instance | PC-11 |
| G-12 | D-3: custom wheel veto prevents no-scrollback arrow generation | PC-12 |
| G-13 | D-3: forced altClickMovesCursor=false overrides caller options | PC-13 |
| G-14 | D-2: instance replacement never mutates shared default protocol objects | PC-14 |
| G-15 | D-1/D-2: policy survives reset and subsequent rearm | PC-15 |
| G-16 | D-2/D-5: mixed/split DECSET and reset keep unrelated modes and ordering | PC-16 |
| G-17 | D-1/D-3: disabling pointer reports does not disable ordinary input | PC-17 |

No new guard is omitted as untestable. Audit: **guards=17, mapped=17, missing=0,
duplicate PC maps=0**. Local selection/wheel behavioral assertions additionally
reject an implementation that merely discards outgoing reports.

### Positive controls

These are post-land Mutation obligations. Code runs V/R; ordinary Review judges
bindings and this inventory before land. **All PC-1..PC-17 must run**, including
all stated variants, on the landed implementation. None has run in TestDesign.
Do not interpret the bare harness controls as completed mutation cycles.

Each row specifies a compiling defect and exact test title. For client tests,
method scope means the file plus anchored full Vitest name, not a TUnit tree
filter. Use the standard build-slot gate around `scripts/test-client.ps1
src/features/board/SessionTerminal.mouse.test.tsx -t '<anchored full name>'`.
The anchored name is `^CARD-0148 mouse policy <literal title>$`; dots or other
regex characters must be escaped if a later bound title contains them. Do not
run the class/file unfiltered for a PC. Record one selected result per cycle,
any other cases deselected, zero selected results never red. The protocol tests
execute all three internal encoding variants in both red and restored green;
use soft/assertion aggregation only there so one expected red does not hide the
other labels. Failures must be the named product assertion, not compile, fixture,
geometry, timeout or control failure.

| PC | Compiling defect breaking its G | Exact case expected red / decisive assertion |
|---|---|---|
| PC-1 | Remove the policy-install call from the factory | `panel rejects hover from backlog`: transport zero-call assertion sees the two SGR hover calls after bare control passes. |
| PC-2 | Set only X10's replacement events to 1; retain restrict=false | `protocol X10 stays local`: exact HELLO drag-selection assertion fails for each encoding. Wire silence alone would survive this defect. |
| PC-3 | Set only VT200 events to 1; retain restrict=false | `protocol VT200 stays local`: same exact HELLO assertion fails. |
| PC-4 | Set only DRAG events to 1; retain restrict=false | `protocol DRAG stays local`: same exact HELLO assertion fails. |
| PC-5 | Set only ANY events to 1; retain restrict=false | `protocol ANY stays local`: same exact HELLO assertion fails. |
| PC-6 | Set only X10 replacement restrict to () => true; retain events=0 | `protocol X10 stays local`: direct valid press return-false assertion and empty channel assertion fail, with DEFAULT binary / SGR / pixel bytes identified. |
| PC-7 | Set only VT200 restrict to () => true; retain events=0 | `protocol VT200 stays local`: same direct-press assertions fail for all three encodings. |
| PC-8 | Set only DRAG restrict to () => true; retain events=0 | `protocol DRAG stays local`: same direct-press assertions fail for all three encodings. |
| PC-9 | Set only ANY restrict to () => true; retain events=0 | `protocol ANY stays local`: same direct-press assertions fail for all three encodings. |
| PC-10 | Replace the unavailable-capability error branch with early return, leaving valid setup unchanged | `factory rejects incompatible mouse seam`: expected descriptive throw fails for missing and noncallable variants; no TypeError qualifies as intended red. |
| PC-11 | Remove only factory error-path dispose, retaining rethrow | `factory disposes on setup failure`: exact allocated-terminal dispose-count=1 assertion fails. |
| PC-12 | Change wheel hook to () => true | `wheel without history stays local`: no-tracking mirror emits Up/Down, violating empty-channel assertions for alternate and normal scrollback=0 variants. |
| PC-13 | Remove forced altClickMovesCursor assignment (or set it true) | `alt click stays local`: empty-channel assertion sees cursor keys under explicit true override; bare control still succeeds. |
| PC-14 | Replace addProtocol's fresh-entry registration with Object.assign into the existing `_protocols[name]` object through a narrow test mutation cast | `reset and instances remain independent`: pre-existing/new bare terminal exact SGR press assertion fails because shared default objects were changed. Preserve and restore those objects or use a fresh Vitest process between cycles. |
| PC-15 | In the helper, capture original entries before replacement and wrap reset to call the real reset then re-register those original entries | `reset and instances remain independent`: post-reset/rearm mirror silence fails with real pointer bytes; initial installation still works. This temporary wrapper is the defect, not proposed production code. |
| PC-16 | Add a CSI `?h` handler that consumes any parameter list containing 1003 | `mixed split modes retain order and input`: expected alternate-buffer assertion fails and bracketed-paste framing is lost. |
| PC-17 | Set the created terminal's disableStdin option to true during mouse setup | `panel rejects hover from backlog`: positive explicit x-input exact-call assertion fails after pointer silence. |

Execution is baseline green -> break -> expected red -> restore -> fresh green,
with source frozen during each run and complete per-PC evidence. Every PC is a
separate cycle even when titles repeat. Code must bind mutation locations to its
actual implementation; the proposed structural recipes above remain binding
required until those sites exist. A survivor, equivalent mutation or inability
to reach an assertion is a finding to classify, never a waived PC. Post-land
SourceLanding mutations remain uncommitted and fully restored; evidence belongs
in the separately assigned persistent evidence root.

### Out of scope

- Server/runner/pty changes, live Grok composer effects, pending-input repair and
  complete UserPrompt delivery evidence: no downstream path is changed or claimed.
- Real-browser/E2E rendering, OS clipboard access and provider-version survey:
  this portable lane proves the specified client behavior with declared substitutes.
- Mixed focus-report redesign and expanding old CARD-0142 mutation coverage:
  unchanged guards stay under the existing twelve compatibility results.
- Full client suite, production bundle, lint and .NET test suites: the factory
  policy has a bounded direct consumer set; these add cost without a named new
  invariant. Vitest compiles the changed TypeScript for these tests.
- Existing loaded bundles and product activation: master-only publication is the
  authorized target; no RC propagation, host restart or live-session experiment.

### Checkpoints

One exact command per row, one isolated Vitest process, `Build: n/a` because no
product build is required. Locked dependency installation is included in CP-1.
Use checkpoint-tool `run --plan <this-plan> --rows CP-1` at committed S1, and
`--rows CP-2` at committed S2, each with the full `--expected-source-sha`. The row
owns its slot; do not nest a second lease. Bootstrap the tool through build-slot
with an isolated `bin-.../` path if necessary; remove task-owned alternate outputs
before completion. Await every run, continuing `wait` on exit 75. Preserve the
unedited CHECKPOINT line, actual Vitest title/count roster and CLIENT TESTS EXIT
CODE. CP-1 is intentionally red, never a clean receipt; CP-2 must be clean.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---:|---|
| CP-1 | S1 | n/a | portable-client-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/board/SessionTerminal.mouse.test.tsx -t '^CARD-0148 mouse policy panel rejects hover from backlog$'` | V-1 witness and bare reachability | 1 executed, 1 assertion failure at panel transport silence, 0 skipped with S1-only witness; exact bare reports passed; nonzero CLIENT TESTS EXIT CODE | n/a | 5 | true |
| CP-2 | S2 | n/a | portable-client-green | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/board/SessionTerminal.mouse.test.tsx src/features/board/terminalMirror.test.ts src/features/board/SessionTerminal.test.tsx` | V-1..V-8, R-1..R-5 | 26 executed: 14 new + 7 factory + 5 component; 0 failed/skipped; CLIENT TESTS EXIT CODE: 0 | n/a | 3 | true |

### Cost

All times are estimates, not measured results. **Ordinary V/R floor (Code):
5 + 3 = 8 minutes**, exactly the two CP EstimatedMinutes values. CP-1's exact
single-witness filter includes first dependency setup; CP-2's three-file filter
is the only green. Code allocation remains 20 minutes harness/test authoring +
10 implementation + 8 checkpoints + 7 evidence/publication/correction = **45**.
If the real DOM harness cannot fit or cannot reach its oracles, return to Plan
with the concrete seam; do not replace selection/wheel with private flags.

**PC floor (Mutation): 53 minutes** = 2 setup/binding minutes + 17 cycles x 3
minutes. Each anchored title filter listed above budgets 0.5 baseline green +
0.5 defect placement + 0.5 red + 0.5 restore + 0.5 fresh green + 0.5 evidence.
All three protocol encodings and failure-shape variants are internal labeled
assertions in that selected result, included in its estimate; they require no
additional suite run. No product build is planned; each Vitest invocation freshly
transforms source. If Code adds guards or distinct executing variants, update the
numeric Mutation floor before Review rather than absorbing it into Code's 45.

Combined verification floor = **8 + 53 = 61 minutes**; combined Code plus Mutation
allocation = **45 + 53 = 98 minutes**, excluding separate ordinary Review and
landing. Setup/build time is included once per stage as stated, not charged again.
TestDesign execution cost: 0 test/build minutes; this 20-minute design task is
separate. The proposal would run 35 results twice (70); this design runs 1 red +
26 green (27), saving **43 executions (61%)**. It removes repeated full-file red
work and combines encoding assertions, while adding explicit failure-path guards.
The ordinary wall-time allowance remains 8 minutes because dependency/tool setup
and harness variance dominate; no unmeasured wall-time saving is claimed.

Before Code handoff: guards=17, mapped=17, missing=0, duplicate PC maps=0;
all PCs scheduled for post-land execution, **0 executed at TestDesign**. New test
and mutation source bindings remain required Code deliverables. No human product
choice or known unverifiable seam blocks Code.

--- next stage ---
next: code
handoff: Implement D-1..D-5 within 45 minutes using the appended verification contract: S1 contains one real-xterm backlog witness and CP-1 red; S2 completes the 26-result roster and CP-2 green. Bind all 17 guards/PCs for post-land Mutation; preserve fixture bytes and master-only scope. Return to Plan if real selection/wheel oracles cannot be reached.
artifact: docs/superpowers/plans/2026-10-19-card-0148-terminal-mouse-suppression-plan.md
