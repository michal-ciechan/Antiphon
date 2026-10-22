# CARD-0105 Code report (task 3850bcf9)

Outcome: implemented and verified at unit level; CP-1..CP-5 ran as the closed list (two red
witnesses red as planned, three green rows validated). CP-6 (Playwright) was not run: this lane
(server2, Linux) has no Chromium and cannot install its system libraries. The E2E case is
committed and compiles.

- Branch `feat/card-task-3850bcf9`, worktree `/work/worktrees/task-3850bcf9`, base
  `673a8c6d759b379294469985111e49ae5c8ede2b` (origin/master at start).
- Source tip `ea80c042661c616d2ac5f465bb4c7107d1b46903`; this report is committed on top of it.
- Plan: `docs/superpowers/plans/2026-10-22-card-0105-terminal-icon-context-menu-plan.md`
  (Plan task a7df5bd2, landed 4b4138fc). Evidence: `.antiphon/checkpoints/<run-id>/`.
- Files: `client/src/features/agents/AgentsPage.tsx` (+137/-24),
  `client/src/features/agents/AgentsPage.test.tsx` (+133), `tests/Antiphon.E2E/AgentE2ETests.cs` (+89).
  No `client/package.json` change; `useLongPress` is exported by `@mantine/hooks` 8.3.17
  (`grep -c` = 1), so the D-7 fallback hook was not written.

## Commits

| SHA | Slice |
|---|---|
| 39b92ef3566b1beb52446eeae39f597e26f9a975 | V-1 red witness |
| 00a6bbf11e9712c92c0d2aee3135a412d79a99a7 | S1: AgentCardDestinations, cardMenu state, onContextMenu, controlled Menu; V-2, V-3 |
| fcd49341ec6212a1653a542c3f7587e28a14a024 | S1 fix attempt (withRoles) - typecheck red, superseded |
| b70d08d3f78f9055f5ce250e27a96cfde047c8ff | S1 fix: `aria-labelledby=""` on the dropdown |
| 0ad457a052c14ae3009320d95d4dec40c8337793 | V-4 red witness |
| 11f8a9a7c5a99c2aa3d026ed040e667d6ebd1865 | S2: Shift+F10 / Menu key, focus return, useLongPress, click swallow; V-5 |
| ea80c042661c616d2ac5f465bb4c7107d1b46903 | S3: E2E case `Agents_page_terminal_icon_context_menu_reaches_settings` |

## Checkpoints (tool runs, lines unedited)

CP-1 run `20261022-052033-0f97` (RED as required):
```
CHECKPOINT CP-1 commit=39b92ef3566b1beb52446eeae39f597e26f9a975 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentsPage.test.tsx -t "C0105 right-click" executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=39b92ef3566b1beb52446eeae39f597e26f9a975 sourceState=clean buildSource=notApplicable
```
console: `CLIENT TYPECHECK EXIT CODE: 0`, `CLIENT LINT EXIT CODE: 0`, `Tests 1 failed | 27 skipped (28)`,
`AssertionError: expected true to be false` (the `allowed === false` assertion), `CLIENT TESTS EXIT CODE: 1`.

CP-2 run `20261022-052315-4e13` at 00a6bbf1 (RED, exit=1): V-2 and V-3 passed, V-1 failed
`Unable to find role="menu" and name "Terminal menu Frontend Claude"`. Cause: Mantine's Popover sets
`aria-labelledby` to the target id (the empty 1x1 anchor), and accessible-name computation does not
fall through to `aria-label`; `aria-labelledby={undefined}` is dropped by Mantine `useProps`.
CP-2 run `20261022-052709-ed76` at fcd49341 (RED, exit=2): typecheck `TS2322` - `withRoles` is not
in `MenuProps` (Menu forwards it at runtime only); vitest not started.
CP-2 run `20261022-052836-82d0` (GREEN, validated `CHECKPOINT SOURCE VALID source=b70d08d3... rows=1`):
```
CHECKPOINT CP-2 commit=b70d08d3f78f9055f5ce250e27a96cfde047c8ff build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentsPage.test.tsx -t "C0105" executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=b70d08d3f78f9055f5ce250e27a96cfde047c8ff sourceState=clean buildSource=notApplicable
```
console: typecheck 0, lint 0, `Tests 3 passed | 27 skipped (30)`, `CLIENT TESTS EXIT CODE: 0`.

CP-3 run `20261022-052954-8687` (RED as required):
```
CHECKPOINT CP-3 commit=0ad457a052c14ae3009320d95d4dec40c8337793 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentsPage.test.tsx -t "C0105 keyboard" executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=0ad457a052c14ae3009320d95d4dec40c8337793 sourceState=clean buildSource=notApplicable
```
console: typecheck 0, lint 0, `Unable to find role="menu" and name "Terminal menu Frontend Claude"`,
`Tests 1 failed | 30 skipped (31)`, `CLIENT TESTS EXIT CODE: 1`.

CP-4 run `20261022-053218-d384` (GREEN, validated `CHECKPOINT SOURCE VALID source=11f8a9a7... rows=1`):
```
CHECKPOINT CP-4 commit=11f8a9a7c5a99c2aa3d026ed040e667d6ebd1865 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentsPage.test.tsx -t "C0105 keyboard|C0105 long-press" executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=11f8a9a7c5a99c2aa3d026ed040e667d6ebd1865 sourceState=clean buildSource=notApplicable
```
console: typecheck 0, lint 0, `Tests 2 passed | 30 skipped (32)`, `CLIENT TESTS EXIT CODE: 0`.

CP-5 run `20261022-053303-c134` (GREEN, validated `CHECKPOINT SOURCE VALID source=11f8a9a7... rows=1`):
```
CHECKPOINT CP-5 commit=11f8a9a7c5a99c2aa3d026ed040e667d6ebd1865 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/agents/AgentsPage.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=11f8a9a7c5a99c2aa3d026ed040e667d6ebd1865 sourceState=clean buildSource=notApplicable
```
console: typecheck 0, lint 0, `Tests 32 passed (32)`, `CLIENT TESTS EXIT CODE: 0`, no unhandled MSW
requests. The plan expected 30: the file's census is 30 `it(` cases plus one two-case `it.each`
(`posts the explicit retry flag only when the agent is held`), so 25 + 2 pre-existing and 5 new = 32.

CP-6: NOT RUN. No Playwright browser cache exists on this host; `ldconfig -p` resolves none of
libnss3, libatk-1.0, libatk-bridge, libgbm, libxkbcommon, libasound, libcups, libXcomposite,
libXdamage, libpango-1.0; Playwright 1.63 `install-deps --dry-run chromium` wants apt packages
(`E: Unable to locate package fonts-liberation` and others) and `sudo` requires a password. Per
the plan this row needs a lane where headless Chromium launches (the desktop Windows runner):
rebuild `client/dist` with `npm.cmd --prefix client run build`, run the checkpoint tool with
`--rows CP-6 --expected-source-sha <tip>`, delete `bin-c0105-e2e/` by literal path afterwards.

## Invariants

| ID | Outcome |
|---|---|
| V-1 | pass (CP-2 green run; red at 39b92ef3 on the `allowed === false` assertion) |
| V-2, V-3 | pass (CP-2) |
| V-4 | pass (CP-4; red at 0ad457a0 with `Unable to find role="menu"`) |
| V-5 | pass (CP-4) |
| V-6 | not run (CP-6, see above); compiles (unlisted compile-only build, 0 errors) |
| R-1, R-2, R-3 | pass (CP-5 whole file green, 32/32) |
| Inspection | `AgentCard` props unchanged; both dropdowns render `AgentCardDestinations`; `Terminal <name>` and `Agent menu <name>` labels unchanged; no new package; `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 673a8c6d... -HeadRef ea80c042...` -> `commits=7 entries=0 violations=0`, exit 0 |

Landable: `git fetch origin master` (now `be14b8b0d0424eba78eb803fc1a7f43f78b0686b`, moved from the
base; nothing under `client/src/features/agents`), `git merge-tree --write-tree origin/master HEAD`
exit 0, no conflicts.

## Deviations from the plan

- `Menu.Dropdown` carries `aria-labelledby=""` beside the planned `aria-label` (reason above:
  Popover names the dropdown after the empty anchor; `withRoles` is not typed on `MenuProps`).
- V-4 waits for Mantine's focus trap (a 0 ms timer) to move focus into the menu before `Escape`.
- V-5 closes the menu with a click outside rather than `Escape` (focus position under fake timers
  is not the point of the case) and runs pending timers before restoring real ones.
- The E2E case seeds a project and a board through the API so "Open board" exists and the four
  menuitems of D-9 can be asserted (`POST /api/agents` takes an optional `boardId`).
- CP-5 expects 32, not 30 (census above).

## Unlisted builds and runs (each through `scripts/build-slot.ps1`; no test ran outside the tool)

- `npm --prefix client ci` (node_modules absent on the mirror; the plan names this prerequisite).
- Checkpoint tool build to `tools/Antiphon.Checkpoints/bin-c0105-cp/` (memory: run the built dll);
  `import --plan` validated the table (6 rows) without running it. Output deleted.
- `npm --prefix client run build` (plan S3 prerequisite), exit 0; `client/dist/index.html` is newer
  than every client source.
- E2E compile-only build to `bin-c0105-e2e-compile/` because CP-6 could not run: first attempt hit
  the documented Linux apphost collision (MSB3027/MSB3021 on `fakegrok`), second with
  `UseAppHost=false` -> `0 Warning(s) 0 Error(s)`. All 15 `bin-c0105-e2e-compile` directories deleted.
- CP-2 ran three times at three different commits (red, typecheck red, green); no unchanged
  selection was repeated.

## Pending for Mutation (zero-PC plan)

The plan has no PC table. Candidate method-scoped mutants (unbound; Mutation owns them):
drop `event.preventDefault()` in `onContextMenu` (V-1 `allowed`); remove `onKeyDown` (V-4);
remove the `swallowClick` check in `onClick` (V-5 `dialog` null); remove `terminalRef.current?.focus()`
in `closeMenu` (V-4 `activeElement`); drop "Open board" from `AgentCardDestinations` (V-1 items,
R-1 kebab). Candidate doc: `docs/superpowers/mutations/`.

## Caveats

- `useLongPress` also fires for a 500 ms mouse hold (hook semantics, as planned in D-7).
- Touch behaviour is pinned in jsdom only; no device lane was available. If a browser synthesises
  no click after a long-press, the one-shot swallow ignores the next left-click on that icon once.
