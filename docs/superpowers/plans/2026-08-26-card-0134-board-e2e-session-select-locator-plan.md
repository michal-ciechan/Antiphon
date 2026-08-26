# CARD-0134 — `BoardE2ETests` asserts `GetByText("Session 1")` against a Mantine `Select` value — plan

**Date:** 2026-08-26 · **Card:** CARD-0134 (`b0bdf2e1-98a7-44fa-9ab4-1d1d1f051ac4`) · **Status:** plan
(investigate + design; no implementation in this pass) · **Verified against:** `master` @ `c8ab493`,
worktree `card-task-a1d37ec8`. Every file:line below was re-read out of the code on that commit; the
Mantine markup facts were read out of `@mantine/core` **8.3.17** (`client/package.json` pins
`^8.3.17`; that is the installed version in the main checkout's `node_modules`).

---

## Verdict up front

**The card's root cause is correct and the fix is a one-locator change in the test. No UI change is
needed, and no test-id needs adding.** The test was written on 2026-05-16 (`e205829`, "feat(e08): add
board-driven terminal UI") against a `Tabs.Tab` whose label was a real text node `Session {n}`; one
day later (`1ce3241`, 2026-05-17, "fix(board): move successful agent cards to review") `SessionTabs`
became a Mantine `<Select>` and the assertion was never updated. The selected option's label lives in
the `<input>`'s **value**, and Playwright's `GetByText` matches element text content only, so the
assertion can never pass — with any `Exact`/substring option.

The corrected locator is:

```csharp
var sessionSelect = cardDialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "All sessions" });
await Expect(sessionSelect).ToHaveValueAsync(new Regex(@"^Session 1 - "), new LocatorAssertionsToHaveValueOptions
{
    Timeout = 30_000
});
```

Role, name and value are each derived below from the real markup — not guessed — and the same
role/name shape is already proven **in this very test file** against the same Mantine component
(`BoardE2ETests.cs:75`, the "Project" `Select` in the New Board dialog).

The test's original intent — "after the move, a spawned session shows up in the card dialog and its
terminal renders" — is fully served by this plus the two assertions that already follow it
(`session-terminal` test id at `:209`, `.xterm` at `:210`). Asserting on the terminal *instead* of the
session list would drop the one thing the list assertion uniquely proves (that the session is listed
and selected as session #1); asserting on a session id is not possible from the card dialog for a
Running session (the id is rendered only inside the not-running overlay,
`SessionTerminal.tsx:193-197`). §3 offers one optional hardening that makes a suppressed spawn fail
fast with a diagnosis instead of a 30 s timeout; it is not required to close the card.

---

## 1. What the dialog actually renders (the evidence for each half of the locator)

`CardModal.tsx:205` mounts the list as `<SessionTabs boardId={boardId} sessions={card.sessions} compact fill />`.
With `compact` true, `SessionTabs.tsx:83-93` renders:

```tsx
<Select
  label={compact ? undefined : 'All sessions'}          // undefined here
  aria-label={compact ? 'All sessions' : undefined}     // 'All sessions' here
  data={sessionOptions}
  value={active}
  onChange={setSelectedSessionId}
  allowDeselect={false}
  searchable
  size="xs"
  w={220}
/>
```

with `sessionOptions` built at `SessionTabs.tsx:43-46` as
`label: \`Session ${terminalSessions.length - index} - ${session.status}\`` — so the one session
that the move spawns is labelled `Session 1 - Starting` or `Session 1 - Running` depending on when the
dialog is opened (or, briefly, `Session 1 - Created`). The old `Tabs.Tab` label was status-free; the
status suffix is new with the `Select`.

**Role = `textbox`.** Mantine's `Select` (`@mantine/core/esm/components/Select/Select.mjs`) renders
its target as an `<InputBase component="input">` inside `<Combobox.Target>`. Neither
`ComboboxTarget.mjs` nor `use-combobox-target-props.mjs` sets a `role` attribute — the target props are
only `aria-haspopup="listbox"`, `aria-expanded`, `aria-controls`, `aria-activedescendant`
(`use-combobox-target-props.mjs:70-77`). `readOnly` is `readOnly || !searchable` (`Select.mjs:232`),
and the component passes `searchable`, so this is a plain editable `<input type="text">` with no
`list` attribute. Playwright's implicit-role table gives that `textbox` (an `<input type=text>` is
`combobox` only when it carries a `list` attribute; `aria-haspopup` does not change the implicit
role). Proof inside the suite: `BoardE2ETests.cs:75` locates the New Board dialog's
`<Select label="Project" searchable>` (`BoardPage.tsx:735-741`) as
`GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "Project" })` and then clicks it to
open the options — that test passes today against the identical component.

**Accessible name = `All sessions`.** `Select.mjs:283` forwards
`"aria-label": others.label ? void 0 : others["aria-label"]` onto the input, i.e. the `aria-label`
lands on the `<input>` exactly when there is no `label` — which is the compact case the card dialog
uses. The client unit test already relies on this: `SessionTabs.test.tsx:33-38`'s
`getSessionSelectInput()` is `getAllByLabelText('All sessions')` filtered to the non-hidden
`HTMLInputElement`, and `:62` asserts `toHaveValue('Session 1 - Stopped')` on it. (The filter exists
because Mantine also renders a hidden `<input type="hidden">` carrying the raw value; Playwright's
`GetByRole` never matches a hidden input, so the E2E locator needs no such filter.)

**Value = the selected option's label.** `Select.mjs:120` seeds the search input with
`finalValue: selectedOption ? selectedOption.label : ""`, and `:157-159` re-syncs it to
`selectedOption.label` whenever the controlled `value` changes. So `input.value` is
`Session 1 - <status>`, never the option's `value` (the session GUID). `Expect(...).ToHaveValueAsync`
reads the `value` property, which is what a controlled React input actually holds; do **not** use a
CSS attribute selector like `input[value^='Session 1']` — that reads the attribute, not the property.

**Why a regex and not an exact string.** The status suffix is a race with the launch: the PATCH at
`MoveCardViaUiAsync` (`BoardE2ETests.cs:1079-1099`) returns as soon as the move commits with
`spawn: true` (`MoveMenu.tsx:72-75`), and the session row goes Created → Starting → Running over the
next seconds while SignalR pushes card updates into the open dialog. `^Session 1 - ` matches all
three and is exactly as status-agnostic as the `Tabs.Tab` text the assertion was written against.
The Select only renders once `terminalSessions` is non-empty — `SessionTabs.tsx:33-36` requires a
non-empty `cwd`, and `:72-78` renders "No sessions" / "No terminal sessions yet" text before that —
so the 30 s timeout is doing the same job it always did: waiting for the spawned session to exist
with a worktree.

---

## 2. The fix (exact edit)

**File:** `tests/Antiphon.E2E/BoardE2ETests.cs`, test
`Board_user_can_drag_backlog_card_to_in_progress_and_open_terminal_session` (`:168-222`).

Replace lines `:205-208`:

```csharp
            await Expect(cardDialog.GetByText("Session 1")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions
            {
                Timeout = 30_000
            });
```

with:

```csharp
            // The session list is a Mantine <Select> (SessionTabs.tsx:83); its selected option is the
            // input's VALUE, not a text node, so GetByText can never see it (CARD-0134). In the card
            // dialog the Select is compact, so its accessible name is the aria-label "All sessions",
            // and the label is "Session 1 - <status>" — status-agnostic on purpose: the dialog can
            // open while the spawned session is still Starting.
            var sessionSelect = cardDialog.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = "All sessions" });
            await Expect(sessionSelect).ToHaveValueAsync(new Regex(@"^Session 1 - "), new LocatorAssertionsToHaveValueOptions
            {
                Timeout = 30_000
            });
```

`System.Text.RegularExpressions` is already imported (the file uses `new Regex(cardTitle)` at `:191`).
`LocatorAssertionsToHaveValueOptions` is in `Microsoft.Playwright` alongside the
`LocatorAssertionsToBeVisibleOptions` the file already uses. Lines `:209-213` (terminal test id and
`.xterm`) stay exactly as they are — they are the "terminal renders" half of the intent.

No other assertion in the E2E suite has this shape: the only other `GetByText("Session…")` is
`:572` (`"Session is not running"`), which is a real `<Text>` node in the inactive overlay
(`SessionTerminal.tsx:189`), and `:573`'s `GetByText(sessionId.ToString())` matches the `<Text>` at
`SessionTerminal.tsx:195` — both correct.

---

## 3. Optional hardening (recommended, separable, ~10 lines)

Today a move whose spawn was **suppressed** (`spawnSuppressed: true` in the PATCH body,
`client/src/api/boards.ts:223-230`, server DTO `BoardDtos.cs:155`) fails this test only as a 30 s
timeout on the list assertion with no hint why. `MoveCardViaUiAsync` already captures that PATCH
response (`:1086-1098`) and throws it away. Have it return the parsed body's `spawnedSessionId`
(`Guid?`) and `spawnSuppressed` (`bool`), and in this one test add, immediately after the move:

```csharp
            var move = await MoveCardViaUiAsync(page, "CARD-0001", "in-progress", "starting work");
            move.SpawnedSessionId.ShouldNotBeNull($"the UI move sends spawn:true (MoveMenu.tsx:75); suppressed={move.SpawnSuppressed}");
```

The other caller (`:144`, move to `review`) discards the return value. This does not change what
the list assertion proves; it only turns "timed out waiting for Session 1" into "the server did not
spawn" when that is the actual failure. Skip it if the implementer wants the minimal diff — the card
is closed by §2 alone.

**Not recommended:** waiting for Running via `WaitForSessionRunningAsync` before opening the dialog
and asserting the exact string `Session 1 - Running`. It is a stricter assertion, but it changes the
test from "open the card right after moving it" into a different flow, and the terminal assertions
already prove the session is live enough to render.

---

## 4. Verification

1. Rebuild the client bundle first — the E2E fixture serves `client/dist` and hard-fails if any
   `client/src` file is newer than `dist/index.html`:
   `cd client; npm run build` (run `npm install` first if `node_modules` is missing in the worktree —
   it is missing in `card-task-a1d37ec8` today).
2. Run the one test, on an alternate output path because the daemons hold `bin/`:

   ```powershell
   dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-card0134/ -- `
     --treenode-filter "/*/*/BoardE2ETests/Board_user_can_drag_backlog_card_to_in_progress_and_open_terminal_session"
   ```

   Expect 1 passed. On a failure, read `tests/Antiphon.E2E/TestOutput/Logs/<TestName>/page.html`
   (the DOM at failure) before touching the locator again — that file is how to confirm the
   `<input aria-label="All sessions" value="Session 1 - …">` element is present.
3. Run `Board_user_can_create_board_create_card_and_open_card_modal` (`:55`) as well, since it is the existing consumer of
   the `Textbox`-role-on-a-`Select` pattern and a Mantine bump that changed the role would break
   both together.
4. Delete the `bin-card0134` directories afterwards:
   `Get-ChildItem . -Recurse -Depth 2 -Directory -Filter bin-card0134 | Remove-Item -Recurse -Force`.

The client unit test `SessionTabs.test.tsx` is unaffected (no client code changes).

---

## 5. Why the alternatives lose

| Option | Why not |
|---|---|
| `GetByText("Session 1")` with `Exact = false` / regex | Text matching walks text nodes; an input's value is not one. Same failure. |
| `GetByLabel("All sessions")` + `ToHaveValueAsync` | Works too (label resolution finds the `aria-label`), but `GetByRole(Textbox, Name)` is the pattern the file already uses for every Mantine input (`:75, :77, :89-91, :1085`) and is stricter about which element it matches. |
| `cardDialog.Locator("input[aria-label='All sessions']")` | Couples to markup rather than semantics; would keep passing if Mantine stopped exposing the field accessibly. |
| Open the dropdown and `GetByRole(Option, Name=/^Session 1/)` | The option is only in the DOM while the dropdown is open (`Combobox.Dropdown`); adds a click, adds flake surface, proves less (that an option exists, not that it is selected). |
| Add `data-testid` to the `Select` | Unnecessary — the field is already reachable by role and name — and the E2E suite's convention is test ids for containers (`session-terminal`, `board-column-*`) and roles for controls. |
| Drop the list assertion, keep only the terminal ones | Loses the unique thing the list proves (the session is listed and selected). Terminal assertions at `:209-213` stay as the second half. |
