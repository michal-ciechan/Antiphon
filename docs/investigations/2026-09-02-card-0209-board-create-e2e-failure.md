# CARD-0209 — Board create E2E fails because create lands on All-cards

**Date:** 2026-09-02
**Scope:** measurement only. No product, test, or timeout change.

## Result

`Board_user_can_create_board_create_card_and_open_card_modal` is not a flake, not
isolation-only, and not a migration race. The board and project are created
successfully. `BoardCreateModal` then navigates to `/boards` (the All-cards view
introduced in `1ce32413`), while the test still asserts the single-board subtitle
that only renders on `/boards/{id}`.

The `__EFMigrationsHistory` `[ERR]` in the same window is EF Core's first probe
of an empty testcontainer. Migrations then apply and the server starts; the
test's `POST /api/projects` and `POST /api/boards` both succeed after that.

Fix is small/contained. No Plan pass.

## Failure (reproduced 2026-09-02 on master `6c1def2f`)

Isolation (`--treenode-filter "/*/*/BoardE2ETests/Board_user_can_create_board_create_card_and_open_card_modal"`):

| Run | Result | Notes |
|---|---|---|
| Card filing (2026-08-26) | 3/3 fail | same assertion |
| This investigation, first attempt | BeforeTest hook: Playwright chromium-1234 missing | environment, not the card |
| This investigation, after `playwright.ps1 install chromium` | 1 failed / 0 passed / 26.9 s | original assertion |

Assertion:

```
Locator("p").Filter(HasText = "E08 Board Project e671df1d / E08 Delivery e671df1d")
expected visible, timeout 5000ms
Aria: heading "Boards"; paragraph "All cards"; textbox "Select board" = All;
      button "New Board"; paragraph "No cards across any board."
URL at failure: http://127.0.0.1:<port>/boards
```

"No cards across any board" is `AllCardsBoard`'s empty state, not "the boards
list is empty".

## What the DOM actually contains

Failure `page.html` (`tests/Antiphon.E2E/TestOutput/Logs/BoardE2ETests/Board_user_can_create_board_create_card_and_open_card_modal/`):

- Visible subtitle: `All cards` (rendered when `id` is unset and `boards.length > 0`).
- Hidden Mantine Select option `value="779340d7-477f-43b1-a25f-0ecbf2e252c0"`:
  `E08 Board Project e671df1d / E08 Delivery e671df1d`.
- Server log: `POST /api/projects` created the project; `POST /api/boards` at
  18:11:50.236, then `GET /api/boards/779340d7-…` — the board exists.

The `{projectName} / {boardName}` string the test looks for is the **Select
option label**, not a visible `<p>`. The `<p>` with that text only exists when
a specific board is selected (`BoardPage.tsx` `{board.projectName} / {board.name}`).

## Isolation vs suite

Each `BoardE2ETests` method constructs its own `AntiphonAppFixture` in
`[Before(Test)]`. There is no shared Postgres or shared UI state across tests,
so suite membership cannot make `navigate('/boards')` land on `/boards/{id}`.

Same-class siblings that create via API and `GotoAsync(/boards/{boardId})` pass
in isolation on the same build:

| Test | Result | Why it differs |
|---|---|---|
| `Board_stopped_claude_session_shows_terminal_overlay_and_resume_button` | 1/1 pass, 20.8 s | API board, opens `/boards/{id}` |
| `Board_user_can_drag_card_between_columns_and_reload_persists_move` | 1/1 pass, 21.2 s | API board, opens `/boards/{id}`, columns visible |

The failing test is the only one that creates a board **through the New Board
dialog**. Full `Antiphon.E2E` is 52 tests and was not re-run; it cannot hide
this, and several members spawn live sessions.

## Migration `[ERR]` is a red herring

Fresh testcontainer, then:

```
[ERR] Failed executing DbCommand … SELECT … FROM "__EFMigrationsHistory"
[INF] Acquiring an exclusive lock for migration application
[INF] Applying migration '20260316224333_InitialCreate'
… all remaining migrations …
[INF] Antiphon server starting
[INF] Created project E08 Board Project e671df1d
[INF] POST /api/boards
```

EF Core always logs that first SELECT as an error when the history table does
not exist yet, then `Migrate()` creates it. A wait/retry before the first
request would not change the create-modal navigation.

## Product regression

`e2058297` (E08) created the board UI and this E2E. Create success was:

```ts
onSuccess: (board) => {
  onClose()
  setName('')
  setDescription('')
  navigate(`/boards/${board.id}`)
}
```

`1ce32413` ("fix(board): move successful agent cards to review", 2026-05-17)
added the All-cards default for `/boards` and changed that handler to:

```ts
onSuccess: () => {
  onClose()
  setName('')
  setDescription('')
  navigate('/boards')
}
```

The E2E was not updated. After create:

- URL is `/boards` (All), not `/boards/{id}`.
- `New Card` is not rendered (`{board && (… New Card …)}`).
- Column `data-testid`s are not rendered (`AllCardsBoard` with `cardCount === 0`
  shows the empty Paper instead of columns).

So even a locator tweak that looked at the Select option would still fail the
next assertions. The rest of the test requires a selected board.

`useCreateBoard` already returns `BoardDetailDto` and caches
`boardKeys.detail(board.id)`. The id is available; create just does not
navigate to it.

## Recommended fix (small, no Plan)

Restore create-success navigation to the new board:

```ts
onSuccess: (board) => {
  onClose()
  setName('')
  setDescription('')
  navigate(`/boards/${board.id}`)
}
```

Pin it in `BoardPage.test.tsx` (no existing create-navigation case). The E2E
then becomes the integration pin without locator changes.

Do not "fix" this by selecting the board from the All dropdown in the E2E
unless product deliberately wants create to land on All. That would hide the
missing `New Card` / columns on the post-create screen. `/boards` as the
default All view can stay; only the create-success path should return to the
board that was just made.

## Not done here

No product or test edit. Playwright chromium-1234 was installed on this
machine so the E2E could actually launch; that is local tooling, not a repo
change.
