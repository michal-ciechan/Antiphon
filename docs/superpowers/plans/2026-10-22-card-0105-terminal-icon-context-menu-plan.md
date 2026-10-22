# CARD-0105: right-click the agent card's terminal icon for a context menu

Date: 2026-10-22. Stage: Plan (TestDesign folded in; `## Verification design` is below). Task: `a7df5bd2`.
Source inspected: `aebf80885bd94554f364e61eaf3b059f8fa6deb2` (branch base = `origin/master` at inspection time).
Target: **master only**. Code authoring and verification budget: **75 minutes** (three slices, six checkpoint rows).

Right-clicking the terminal icon on an agent card of the agents page (`/agents`) opens a context
menu at the pointer with the destinations the card's kebab menu already offers, plus "Open
terminal". Keyboard (Shift+F10, Menu key) and touch (long-press) reach the same menu. The kebab
stays. No new dependencies, no API change, no label change.

## Inputs and ground truth

Card CARD-0105 (board Antiphon, read in full; no notes, two revisions). Brief: plan the smallest
coherent change with Vitest component tests (one red witness and one narrow green per slice) and
one Playwright case if an existing harness covers the page.

| Card assumption / brief wording | What the inspected code does | Consequence |
|---|---|---|
| Left-click on the terminal icon already opens the terminal. | `AgentsPage.tsx:476-506`: `ActionIcon` with `aria-label="Terminal <name>"`, `onClick={() => onOpenTerminal(agent)}`, wrapped in a liveness `Tooltip`. `AgentsPage.tsx:379-386` renders `AgentCliModal` for `terminalAgent`. | Confirmed. The "Open terminal" item reuses `onOpenTerminal`; nothing new is wired for the terminal itself. |
| A separate kebab already offers files / board / settings. | `AgentsPage.tsx:507-543`: `Menu` on `aria-label="Agent menu <name>"` with `Open files view` (`component="a"`, `href=/agents/<id>/files`, `target="_blank"`), `Open board` (router `Link` to `/boards/<boardId>`, only when `boardId`), divider, `Edit settings` (`onEditSettings(agent)`). | Confirmed. The context menu renders the **same** item list through one shared component so the two menus cannot drift (D-2). |
| Card line numbers `170-183` and `184-220`. | Drifted. The card body is now the memoised `AgentCard` component (`AgentsPage.tsx:422-545`) taking `onOpenTerminal` / `onEditSettings` props; the grid passes `setTerminalAgent` / `setSettingsAgent` (`:157-158`). | Edit `AgentCard`, not the page body. Keep its prop shape: the memo test renders 49 `AgentCard`s directly with those props. |
| `FilesReviewPanel.tsx` has the right-click pattern to copy. | `FilesReviewPanel.tsx:154-156` state `{x, y, ...} \| null`; `:228-230` `onContextMenu` calls `preventDefault` and stores `clientX/clientY`; `:283-331` renders `<Menu opened onClose=... position="bottom-start" withinPortal>` with a `Menu.Target` of a 1x1 `Box pos="fixed"` at those coordinates. | Confirmed and reused as-is (D-5). The repo has no other `onContextMenu`, no long-press hook use and no keyboard-menu (`F10`/`ContextMenu`) handling: those two paths are new (D-6, D-7). |
| `CardRow.tsx:83` and `BoardCard.tsx:53` render static terminal icons. | Now `CardRow.tsx:115` (live-session marker next to the assigned agent name) and `BoardCard.tsx:61` (icon in a "Session active" tooltip); neither is a button or has a kebab. Also `home/AgentRail.tsx:81` (liveness colour inside a row button that selects the agent), `home/tasks/TaskCard.tsx:490` and `delegations/TaskDetailBody.tsx:247`. | All are status indicators or tab labels. Out of scope (D-1). |
| "Project view" might be a different screen. | `HomePage.tsx:122` has a `projectView` memo and the home layout shows an `AgentRail` (added 2026-08-08, before the card was filed 2026-08-20). The rail has no per-agent destination buttons: selecting a row already drives the files pane and chat dock, and its terminal icon is a `span` inside the row button. | The card's "currently these live behind a separate button" matches only the agents page kebab. Target stays `AgentsPage` (D-1). The rail is recorded, not changed. |
| "Settings" could mean something terminal-specific. | `AgentCliModal.tsx:74-111` tabs are terminal / messages / transcript / schedules; it takes `agent, remoteControl, opened, onClose` and no initial-tab prop. `AgentSettingsModal` (`title="Agent Settings"`, `Name`/`Details` fields) is the only per-agent settings surface. | "Edit settings" opens `AgentSettingsModal` (D-3). No transcript/messages items (D-4). |
| Tests exist for this page. | `AgentsPage.test.tsx`: 25 cases; `Agent menu <name>` is pinned by the files-link, edit and delete tests; `Terminal <name>` by the tooltip test and the "Start agent" test; the memo test asserts one re-render per readiness change. `renderWithProviders` = Mantine `env="test"` (portals inline, transitions immediate, `referenceHidden` pinned false) + `BrowserRouter` + QueryClient; MSW via `agentHandlers()`; jsdom with `matchMedia`/`ResizeObserver` mocks. `fireEvent` is imported from `@testing-library/react` in three client test files; no client test fires `contextmenu` today. | New cases go in the same file and `describe`, named with a `C0105` prefix so `-t C0105` selects them. Right-click is `fireEvent.contextMenu(icon, { clientX, clientY })`; its boolean return (`false` when the handler called `preventDefault`) is the crisp witness. |
| Client test runner. | `scripts/test-client.ps1` runs `typecheck-client.ps1` and `lint-client.ps1` for the whole client before Vitest and exits before Vitest if either fails; file paths and `-t <regex>` pass through; it prints `CLIENT TESTS EXIT CODE: n`. A green run prints only the aggregate `Tests N passed` line. | Every CP row below is typecheck + lint + the named Vitest selection. Report aggregate counts plus the selection census. |
| An E2E harness covers the page. | `tests/Antiphon.E2E/AgentE2ETests.cs` (`[NotInParallel]`, `[Category("OptIn")]`, `UsePrebuiltFrontend = true`, headless Chromium) opens `/agents`, waits for the `Agent <name>` tile and interacts by role. `ScheduleCliE2ETests.cs:55` seeds an agent via `POST /api/agents`. `WorkflowDeleteTests.cs:274` locates `AriaRole.Menuitem`. `AntiphonAppFixture.EnsureClientBundleIsCurrent` hard-fails when `client/src` is newer than `client/dist/index.html`. | One new test method in `AgentE2ETests` (D-10). `npm run build` in `client/` is a listed S3 prerequisite. An E2E build row leaves `.playwright/node` the checkpoint tool cannot reclaim: run it in its own checkpoint run and delete its `bin-*` before any later build. |
| Versions. | React 19.2, `@mantine/core`/`@mantine/hooks` 8.3.17, react-router 7.13, Vitest 4.1, `@testing-library/user-event` 14.6, jsdom 29. `client/node_modules` is absent on this plan mirror, so the `useLongPress` export of `@mantine/hooks` could not be read here. | D-7 names the Code-time check and its fallback. |
| Platform. | `GET /api/runner-defaults`: `globalRunnerId = server2`; `GET /api/session-runners`: `server2` linux and `desktop` windows both dispatch-eligible, `server2-temp` stale. Nothing in this card touches a pty, Windows path or ConPTY. | Portable client lane; no `-Runner`, no `-Platform`. The E2E row needs a lane where headless Chromium launches; see Platform below. |

## Decisions

### D-1 — Target is the agents page card icon only
The change is confined to `AgentCard` in `client/src/features/agents/AgentsPage.tsx`. The home
rail, board card, card row, task card and task detail icons stay status indicators.
Rejected: also wiring the home `AgentRail` icon. Its row already selects the agent and drives the
files pane; it has no "separate button" to collapse, and the card's wording names one.

### D-2 — Supplement the kebab, share its items
The kebab (`Agent menu <name>`) stays. Its four items move into one function component,
`AgentCardDestinations`, rendered inside both dropdowns; the context menu prepends "Open terminal".
Reasons: right-click is undiscoverable on its own; touch users may not know long-press; three unit
tests and the role-based E2E idiom pin the kebab. Rejected: replacing the kebab (the card itself
calls that a deliberate, larger UX change); duplicating `Menu.Item` lists (drift).

### D-3 — "Settings" is the existing per-agent settings modal
"Edit settings" calls `onEditSettings(agent)` and opens `AgentSettingsModal`, exactly as the kebab
does. There is no terminal-specific settings surface to point at.

### D-4 — Menu items, in order
1. Open terminal (`onOpenTerminal(agent)`; same action as left-click; label constant, the modal
   itself offers "Start agent" when no session is live)
2. Open files view (`component="a"`, `href=/agents/<id>/files`, `target="_blank"`, as today)
3. Open board (router `Link` to `/boards/<boardId>`, only when `boardId`)
4. divider
5. Edit settings

Rejected: "Open transcript" / "Open messages". `AgentCliModal` has no initial-tab prop; adding one
is a separate small card, not part of the smallest change.

### D-5 — Cursor-anchored controlled `Menu`, the `FilesReviewPanel` idiom
State `cardMenu: { x: number; y: number } | null` lives in `AgentCard`. When non-null, render
`<Menu opened onClose={closeMenu} position="bottom-start" withinPortal shadow="md">` whose
`Menu.Target` is a 1x1 `Box pos="fixed"` at `(x, y)`. Pointer right-click anchors at
`clientX/clientY`; keyboard and long-press anchor at the icon's `getBoundingClientRect()`
bottom-left. A `contextmenu` event whose `clientX` and `clientY` are both 0 (a browser-synthesised
keyboard event) also anchors at the icon. Rendering nothing while closed keeps 49 cards cheap, and
the state being local keeps the memo contract (a menu open re-renders only its card).
Rejected: making the icon the `Menu.Target` (the target's click toggles the menu and
`aria-haspopup` would misdescribe a button whose primary click opens the terminal); a custom
popover (new positioning mechanism).

### D-6 — Keyboard path is explicit
`onKeyDown` on the icon handles `Shift+F10` and `key === 'ContextMenu'`: `preventDefault()` (so a
browser that would synthesise `contextmenu` from those keys does not double-fire) and open at the
icon anchor. `onClose` (Escape, outside click, item click) clears the state and returns focus to
the icon through a ref. Mantine `Menu` supplies `role="menu"`, `role="menuitem"` and arrow
navigation. Rejected: relying on native keyboard-to-`contextmenu` synthesis (absent on Safari).

### D-7 — Touch path is a long-press
Use `useLongPress` from `@mantine/hooks` with `threshold: 500`; its callback sets a
`swallowClick` ref and opens at the icon anchor. The icon's `onClick` first checks and clears that
ref, so the click that follows a long-press does not also open the terminal modal. The
`onContextMenu` handler's `preventDefault` suppresses Android's native long-press menu, so there is
no double menu. **Code-time check (first thing in S2):** `grep -c useLongPress
client/node_modules/@mantine/hooks/esm/index.mjs` after dependencies are installed. If it is 0,
write `client/src/hooks/useLongPress.ts` (same handler shape: `onMouseDown/onMouseUp/onMouseLeave/
onTouchStart/onTouchEnd`, one `setTimeout`, cleared on release or unmount) and report the deviation;
V-5 is unchanged either way. Rejected: a new dependency; `onTouchStart` timers inline in the card.

### D-8 — No label, tooltip or prop changes
`aria-label="Terminal <name>"`, the liveness tooltip, `AgentCard`'s props and the kebab label stay
byte-identical. The context menu dropdown gets `aria-label="Terminal menu <name>"`; the anchor
`Box` gets `data-testid="card-menu-anchor"` and `data-anchor="<x>,<y>"` (test instrumentation in
the same spirit as `onRendered`).

### D-9 — One E2E case in the existing harness
`AgentE2ETests.Agents_page_terminal_icon_context_menu_reaches_settings`: seed one agent through
`POST /api/agents` (the `ScheduleCliE2ETests` idiom, no create dialog), open `/agents`, right-click
`Terminal <name>` (`LocatorClickOptions { Button = MouseButton.Right }`), expect the four
menuitems, click "Edit settings", expect the dialog's `Details` textbox; then focus the icon, press
`Shift+F10`, expect the menu, press `Escape`, expect it gone. The class attributes stay as they are.
Not required to run red: its reachability is proven by V-1 and V-4 at unit level, and S1/S2 land
on the same branch before it exists.

### D-10 — Docs
No living document describes the agents page menus (`grep` found none). No doc change.

## Design

`client/src/features/agents/AgentsPage.tsx` only (plus tests and one E2E file):

```tsx
function AgentCardDestinations({ agent, onOpenTerminal, onEditSettings }: {
  agent: AgentSummaryDto
  onOpenTerminal?: (agent: AgentSummaryDto) => void   // present only in the context menu
  onEditSettings: (agent: AgentSummaryDto) => void
}) { /* D-4 items; files / board / divider / settings exactly as the kebab renders today */ }

// inside AgentCard
const terminalRef = useRef<HTMLButtonElement>(null)
const [cardMenu, setCardMenu] = useState<{ x: number; y: number } | null>(null)
const swallowClick = useRef(false)
const iconAnchor = () => { const r = terminalRef.current?.getBoundingClientRect(); return { x: r?.left ?? 0, y: r?.bottom ?? 0 } }
const closeMenu = () => { setCardMenu(null); terminalRef.current?.focus() }
const longPress = useLongPress(() => { swallowClick.current = true; setCardMenu(iconAnchor()) }, { threshold: 500 })

<ActionIcon ref={terminalRef} /* existing props unchanged */
  onClick={() => { if (swallowClick.current) { swallowClick.current = false; return } onOpenTerminal(agent) }}
  onContextMenu={(e) => { e.preventDefault(); setCardMenu(e.clientX || e.clientY ? { x: e.clientX, y: e.clientY } : iconAnchor()) }}
  onKeyDown={(e) => { if ((e.shiftKey && e.key === 'F10') || e.key === 'ContextMenu') { e.preventDefault(); setCardMenu(iconAnchor()) } }}
  {...longPress}
/>
{cardMenu && (
  <Menu opened onClose={closeMenu} position="bottom-start" withinPortal shadow="md">
    <Menu.Target><Box pos="fixed" left={cardMenu.x} top={cardMenu.y} w={1} h={1} data-testid="card-menu-anchor" data-anchor={`${cardMenu.x},${cardMenu.y}`} /></Menu.Target>
    <Menu.Dropdown aria-label={`Terminal menu ${agent.name}`}>
      <AgentCardDestinations agent={agent} onOpenTerminal={onOpenTerminal} onEditSettings={onEditSettings} />
    </Menu.Dropdown>
  </Menu>
)}
```

Item clicks close through Mantine's default `closeOnItemClick`, which calls `onClose` and then the
item's own handler opens the modal or navigates. The `Tooltip` around the icon merges event
handlers with `createEventHandler`, so the icon's new handlers and the long-press touch handlers
both fire; V-5 proves it.

## Slices

### S1 — Context menu on right-click (about 30 minutes)
Files: `client/src/features/agents/AgentsPage.tsx`, `client/src/features/agents/AgentsPage.test.tsx`.
1. Commit V-1 alone (red): `C0105 right-click on the terminal icon opens the card menu at the pointer`.
   Run CP-1.
2. Add `AgentCardDestinations`, the `cardMenu` state, `onContextMenu`, the controlled `Menu`;
   rewire the kebab dropdown to the shared component. Add V-2 and V-3. Run CP-2. Commit, push.

### S2 — Keyboard and long-press parity (about 20 minutes)
Files: same two files (plus `client/src/hooks/useLongPress.ts` only under the D-7 fallback).
1. Do the D-7 export check. Commit V-4 alone (red): `C0105 keyboard Shift+F10 opens the menu and
   Escape returns focus to the terminal icon`. Run CP-3.
2. Add `onKeyDown`, the ref, focus return on close, `useLongPress` and the click swallow. Add V-5.
   Run CP-4, then CP-5 (whole file, R-1..R-3). Commit, push.

### S3 — Playwright case (about 25 minutes, including both builds)
Files: `tests/Antiphon.E2E/AgentE2ETests.cs`.
1. Prerequisite, listed here so it is not an unlisted build: install client dependencies if
   `client/node_modules` is absent (`scripts/test-client.ps1` already did this in S1), then
   `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c0105-client-dist -- npm --prefix client run build`
   (on a Windows lane invoke `npm.cmd`, never the `npm.ps1` shim, CARD-0307). Report its exit code.
2. Add the D-9 test method. Run CP-6 in its own checkpoint run. Delete `bin-c0105-e2e/` by literal
   path before any other build in the worktree. Commit, push.

### S4 — Repair of the long-press click swallow (Code task bde3f3ad, after Review 74b6b98b D-1)
Files: `client/src/features/agents/AgentsPage.tsx`, `client/src/features/agents/AgentsPage.test.tsx`, `client/src/hooks/useLongPress.ts`.
Defect: the one-shot swallow set by the 500 ms long-press was only cleared by a later click on the icon, so a held right button (Mantine `useLongPress` 8.3.17 starts on any button; releasing the right one yields no click) or a touch ended by `touchcancel`/scroll (the hook stops only on mouseup, mouseleave, touchend) left it armed and ate the next genuine left-click; the menu also opened mid-scroll.
1. Commit V-7, V-8, V-9 red; run CP-7.
2. Replace the Mantine hook with the D-7 local hook (`client/src/hooks/useLongPress.ts`, no new dependency): it starts only for `button === 0` or a touch, cancels on mouseup, mouseleave, touchmove, touchend, touchcancel and unmount, and exposes `cancel` for the icon's `contextmenu`. The swallow is cleared on every press start (`onPressStart`) and in `closeMenu`, so it can only ever consume the click of the press that set it. V-5 is the control that a completed touch long-press still swallows its own trailing click. Run CP-8, CP-2, CP-4, CP-5.

## Verification design

Folded into this Plan (brief: `next: code`). Expected outcomes are requirements, not execution
claims; no build or test was run by this stage.

### Inspection
- `AgentCard` prop shape unchanged; the memo test's direct render still compiles.
- The kebab dropdown and the context menu both render `AgentCardDestinations`; no second item list.
- `aria-label="Terminal <name>"` and `aria-label="Agent menu <name>"` unchanged.
- No new package in `client/package.json`.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef aebf80885bd94554f364e61eaf3b059f8fa6deb2 -HeadRef <pushed sha>` exit 0.

### Harness (all unit cases)
`renderWithProviders(<AgentsPage />)` with `server.use(...agentHandlers())` (one agent,
`boardId: 'board-1'`, no live session). `icon = await screen.findByRole('button', { name: 'Terminal Frontend Claude' })`.
`fireEvent` is imported from `@testing-library/react`. V-5 uses `vi.useFakeTimers()` with
`fireEvent` only (no `userEvent` under fake timers) and restores real timers in `finally`.

### Proves it works now

| ID | Case (Vitest, `AgentsPage.test.tsx`) | Recipe | Decisive assertions | Red at |
|---|---|---|---|---|
| V-1 | `C0105 right-click on the terminal icon opens the card menu at the pointer` | `const allowed = fireEvent.contextMenu(icon, { clientX: 120, clientY: 80 })` | `allowed === false`; `await screen.findByRole('menu', { name: 'Terminal menu Frontend Claude' })`; `within(menu).getAllByRole('menuitem')` names are exactly `['Open terminal', 'Open files view', 'Open board', 'Edit settings']` in order; files item `href` `/agents/agent-1/files` and `target` `_blank`; board item `href` `/boards/board-1`; `screen.getByTestId('card-menu-anchor')` has `data-anchor="120,80"`; `screen.queryByRole('dialog')` is null (left-click path not taken). | Base: `allowed` is `true` (no handler), assertion 1 fails. |
| V-2 | `C0105 context menu opens the terminal` | right-click as V-1, `userEvent.click(menuitem 'Open terminal')` | `await screen.findByRole('button', { name: 'Start agent' })` (the CLI modal's no-session prompt); `screen.queryByRole('menu')` is null. | Base: no menu. |
| V-3 | `C0105 context menu opens the settings modal` | right-click as V-1, `userEvent.click(menuitem 'Edit settings')`, `settleQueries` | `screen.getByLabelText('Details')` present; `screen.getByText('Agent Settings')` present; `screen.queryByRole('menu')` is null. | Base: no menu. |
| V-4 | `C0105 keyboard Shift+F10 opens the menu and Escape returns focus to the terminal icon` | `icon.focus()`; `await userEvent.keyboard('{Shift>}{F10}{/Shift}')` | menu present with the same four items; `data-anchor` equals `"0,0"` in jsdom (icon rect is all-zero; the anchor came from the icon, not the pointer); `await userEvent.keyboard('{Escape}')`; `await waitFor(() => expect(screen.queryByRole('menu')).toBeNull())`; `document.activeElement === icon`. Second half: `fireEvent.keyDown(icon, { key: 'ContextMenu' })` opens it again. | S1 tip: no `onKeyDown`, `findByRole('menu')` times out. |
| V-5 | `C0105 long-press on the terminal icon opens the menu and swallows the follow-up click` | `vi.useFakeTimers()`; `fireEvent.touchStart(icon)`; `act(() => vi.advanceTimersByTime(499))` then `expect(screen.queryByRole('menu')).toBeNull()`; `act(() => vi.advanceTimersByTime(1))` | menu present; `fireEvent.touchEnd(icon)`; `fireEvent.click(icon)` leaves `screen.queryByRole('dialog')` null; a second `fireEvent.click(icon)` after closing the menu does open the CLI modal (`Start agent` button), proving the swallow is one-shot. | S1 tip: no long-press, menu never appears. |
| V-7 | `C0105 repair: a held right-click then Escape leaves the next left-click opening the terminal` | `vi.useFakeTimers()`; `fireEvent.mouseDown(icon, { button: 2 })`; `fireEvent.contextMenu(icon, { button: 2, clientX: 120, clientY: 80 })`; advance 600 ms; `fireEvent.mouseUp(icon, { button: 2 })`; real timers; Escape with focus in the menu | menu gone; `await userEvent.click(icon)` shows the CLI modal (`Start agent` button). | 0e62b31a (S3 tip): the secondary button started the long-press, so the swallow stayed armed with no click to spend it on; `Unable to find role="button" and name "Start agent"`. |
| V-8 | `C0105 repair: touch movement before the threshold cancels the long-press` | fake timers; `touchStart`; 200 ms; `touchMove`; 400 ms | `screen.queryByRole('menu')` is null (no menu mid-scroll); `touchEnd`; real timers; `userEvent.click(icon)` shows `Start agent`. | 0e62b31a: the menu opened at 500 ms despite the move (menu-is-null assertion). |
| V-9 | `C0105 repair: a long-press ended by touchcancel leaves the next tap opening the terminal` | fake timers; `touchStart`; 500 ms (menu open); `touchCancel`; real timers; a tap (`touchStart`, `touchEnd`, `click`) | `Start agent` button present. | 0e62b31a: the tap's click was swallowed by the earlier press; `Unable to find role="button" and name "Start agent"`. |
| V-6 | `AgentE2ETests.Agents_page_terminal_icon_context_menu_reaches_settings` (Playwright) | D-9 recipe | Four menuitems visible after right-click; "Edit settings" opens a dialog whose `Details` textbox is visible; after closing it, `Shift+F10` on the focused icon shows the menu and `Escape` hides it. Screenshot under `TestOutput/` only (no `docs/screenshots` write). | Not run red (D-9). |

### Guards the regression

| ID | Existing case(s) | Expected |
|---|---|---|
| R-1 | `links the files view from the card menu`, `edits an agent and changes its board via the settings modal`, `deletes an agent via the settings modal` | Kebab still opens and its items still work after the item extraction (D-2). |
| R-2 | `offers to start the agent from the card CLI icon when no terminal is running`, `describes terminal liveness in the card terminal icon tooltip` | Left-click and tooltip unchanged (D-8). |
| R-3 | `memoises cards so a readiness update renders only the changed row` | Local menu state does not break the memo contract (D-5). |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | n/a | c0105-contextmenu-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentsPage.test.tsx -t "C0105 right-click"` | V-1 witness | typecheck and lint pass; exactly 1 selected, 1 failed at the `allowed === false` assertion; `CLIENT TESTS EXIT CODE: 1` | n/a | 4 | true | n/a |
| CP-2 | S1 | n/a | c0105-contextmenu-green | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentsPage.test.tsx -t "C0105"` | V-1..V-5, V-7..V-9 (every `C0105` case; S4 note below) | exactly 8 selected, 8 passed, 0 failed/skipped; `CLIENT TESTS EXIT CODE: 0` | n/a | 4 | true | n/a |
| CP-3 | S2 | n/a | c0105-keyboard-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentsPage.test.tsx -t "C0105 keyboard"` | V-4 witness | exactly 1 selected, 1 failed with `Unable to find role="menu"`; `CLIENT TESTS EXIT CODE: 1` | n/a | 4 | true | n/a |
| CP-4 | S2 | n/a | c0105-parity-green | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentsPage.test.tsx -t "C0105 keyboard\|C0105 long-press"` | V-4, V-5 | exactly 2 selected, 2 passed, 0 failed/skipped; `CLIENT TESTS EXIT CODE: 0` | n/a | 4 | true | n/a |
| CP-5 | S2 | n/a | c0105-agents-page-file | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentsPage.test.tsx` | R-1, R-2, R-3, V-1..V-5, V-7..V-9 | `Tests 35 passed` (27 pre-existing incl. the two-case `it.each` + 5 from S1/S2 + 3 from S4), 0 failed/skipped; `CLIENT TESTS EXIT CODE: 0` | n/a | 5 | true | n/a |
| CP-6 | S3 | `tests/Antiphon.E2E -> bin-c0105-e2e/` | c0105-e2e-contextmenu | `/*/*/AgentE2ETests/Agents_page_terminal_icon_context_menu_reaches_settings` | V-6 | 1 executed, 1 passed, 0 failed/skipped; current `client/dist` (fixture bundle check passes) | 1 | 14 | true | n/a |
| CP-7 | S4 | n/a | c0105-repair-red | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentsPage.test.tsx -t "C0105 repair"` | V-7, V-8, V-9 witnesses | typecheck and lint pass; exactly 3 selected, 3 failed: V-7 and V-9 with `Unable to find role="button" and name "Start agent"`, V-8 at the menu-is-null assertion; `CLIENT TESTS EXIT CODE: 1` | n/a | 4 | true | n/a |
| CP-8 | S4 | n/a | c0105-repair-green | `pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentsPage.test.tsx -t "C0105 repair"` | V-7, V-8, V-9 | exactly 3 selected, 3 passed, 0 failed/skipped; `CLIENT TESTS EXIT CODE: 0` | n/a | 4 | true | n/a |

S4 (repair, Code task bde3f3ad) adds CP-7 and CP-8; the `-t "C0105"` selection of CP-2 and the whole-file count of CP-5 grew with V-4, V-5 and the three S4 cases (the earlier S1 expectation of 3 predates S2). CP-6 runs in its own checkpoint run (`--rows CP-6`) after the S3 prerequisite bundle build;
delete `bin-c0105-e2e/` by literal path afterwards. If Chromium cannot launch on the dispatched
lane, report CP-6 as not run with the launch error and `next: code` for a Windows-lane rerun of S3;
do not pin a host from this plan. The `-t` regex in CP-4 is a literal pipe inside the quoted
argument (escaped here only for the table).

### Cost
Ordinary Code floor: 4 + 4 + 4 + 4 + 5 + 14 = 35 minutes of rows, plus about 40 minutes of
authoring: `-ExpectAbout 75`. `checkpoints: docs/superpowers/plans/2026-10-22-card-0105-terminal-icon-context-menu-plan.md@<plan commit sha> section "### Checkpoints"`.

## Checklist: sessions that wait for input
Not applicable. This change adds no session, no wait and no release path; CARD-1083's park rule is
untouched.

## Platform
Portable client lane. Omit `-Runner` and `-Platform`; `runner-defaults` already places work on
server2. CP-1..CP-5 run on any lane with Node. CP-6 needs headless Chromium for the Playwright
fixture; the lane that cannot launch it reports the row as not run (see above). No Windows-only
code is touched.

## Open items and follow-ups
- "Open transcript" / "Open messages" items need an initial-tab prop on `AgentCliModal`: file a
  small follow-up card if wanted (D-4).
- If the D-7 export check fails, the local hook lands under `client/src/hooks/` and the report
  names it as a plan deviation; no re-plan needed.

--- next stage ---
next: code
handoff: Implement CARD-0105 per docs/superpowers/plans/2026-10-22-card-0105-terminal-icon-context-menu-plan.md: shared AgentCardDestinations, cursor-anchored controlled Menu on the terminal icon, Shift+F10/Menu-key and long-press parity, one Playwright case; run CP-1..CP-6 as the closed list.
artifact: docs/superpowers/plans/2026-10-22-card-0105-terminal-icon-context-menu-plan.md
