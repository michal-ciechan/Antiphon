# CARD-0148 mutation candidate bindings

Code owner: `ede2412f-f65b-4909-a21e-4e797c1ab6e2`.
Plan: [final verification design](../plans/2026-10-19-card-0148-terminal-mouse-suppression-plan.md#verification-design).
This candidate is **unbound to a SourceLanding operation**. All 17 PCs and every
variant remain pending. Ordinary bare-terminal reachability controls are not
mutation cycles. Mutation owns deliberate defects, red/restore/green and any
missing-control discovery. The plan's 53-minute Mutation floor is unchanged.

Production paths below are relative to `client/src/features/board/`. All new test
titles belong to `SessionTerminal.mouse.test.tsx`, describe `CARD-0148 mouse policy`.
Use the plan's anchored full-title filters, one result per cycle; keep the three
encoding variants in each selected protocol result. No full-file PC runs.

| PC / guard | Bound production site and defect | Exact test title; decisive assertion / variants |
|---|---|---|
| PC-1 / G-1 | `terminalMirror.ts`, `createMirrorTerminal`, remove `configureLocalTerminalMouse(terminal)` | `panel rejects hover from backlog`; no transport calls after exact bare SGR hover control |
| PC-2 / G-2 | `terminalMouse.ts`, registration loop, events `name === 'X10' ? 1 : 0` | `protocol X10 stays local`; exact `HELLO` selection for DEFAULT/SGR/SGR_PIXELS |
| PC-3 / G-3 | Same loop, events `name === 'VT200' ? 1 : 0` | `protocol VT200 stays local`; same three selection variants |
| PC-4 / G-4 | Same loop, events `name === 'DRAG' ? 1 : 0` | `protocol DRAG stays local`; same three selection variants |
| PC-5 / G-5 | Same loop, events `name === 'ANY' ? 1 : 0` | `protocol ANY stays local`; same three selection variants |
| PC-6 / G-6 | Same loop, restrict `() => name === 'X10'` | `protocol X10 stays local`; direct return false and both empty channels, three encodings |
| PC-7 / G-7 | Same loop, restrict `() => name === 'VT200'` | `protocol VT200 stays local`; same three direct-event variants |
| PC-8 / G-8 | Same loop, restrict `() => name === 'DRAG'` | `protocol DRAG stays local`; same three direct-event variants |
| PC-9 / G-9 | Same loop, restrict `() => name === 'ANY'` | `protocol ANY stays local`; same three direct-event variants |
| PC-10 / G-10 | `configureLocalTerminalMouse`, replace incompatible-seam throw with return | `factory rejects incompatible mouse seam`; descriptive throw for missing core, missing service, absent method, noncallable method |
| PC-11 / G-11 | `createMirrorTerminal` catch, remove dispose only | `factory disposes on setup failure`; exact newly allocated terminal disposed once after second registration throws |
| PC-12 / G-12 | `configureLocalTerminalMouse`, wheel handler returns true | `wheel without history stays local`; empty channels in alternate/normal-zero-scrollback, before/after Grok setup |
| PC-13 / G-13 | Same helper, remove forced `altClickMovesCursor = false` | `alt click stays local`; empty channels despite explicit true override, before/after Grok setup |
| PC-14 / G-14 | Registration loop, Object.assign into existing `_protocols[name]` instead of addProtocol (narrow mutation-only cast) | `reset and instances remain independent`; exact press/hover reports from bare terminals created before/after mirrors; restore shared defaults |
| PC-15 / G-15 | Helper, capture original protocol entries; wrap reset with real reset then original-entry registration | `reset and instances remain independent`; post-reset/rearm silence and selection for all four modes |
| PC-16 / G-16 | Helper, register consuming CSI `?h` handler when parameters contain 1003 | `mixed split modes retain order and input`; alternate buffer, ALT text and bracket-framed deliberate paste |
| PC-17 / G-17 | Helper, set `terminal.options.disableStdin = true` | `panel rejects hover from backlog`; exact explicit x input call after pointer silence |

Ordinary bindings: V-1 backlog; V-2 queued/live/reconnect; V-3 four protocol titles
with three encodings each; V-4 `normal drag copies selection`; V-5 `wheel scrolls
local history`; V-6 `wheel without history stays local`; V-7 and R-1 `mixed split
modes retain order and input`; V-8 reset/instances; R-2 alt click; R-3 all seven
`terminalMirror.test.ts` and five `SessionTerminal.test.tsx` existing cases;
R-4 incompatible seam; R-5 disposal. Expected CP-2 roster: 14 + 7 + 5 = 26.

These tests assert DOM outcomes or exact transport calls, never session receipt.
No real-browser/OS clipboard or downstream delivery claim is made. SourceLanding
must bind this candidate to its actual landed SHA before execution.
