# Review d99c29ee — CARD-1690 client test performance (round 1 of 3, full)

Subject: Code task dd90f1b7, branch feat/card-task-dd90f1b7. Tested source 86f3984458e66e9b26ad9f1702a00c65f4aefb83 (the tip 2e7023b42 adds only the report).
Outcome: **1 defect (D-1)**. The global poll-error change alters final failure messages and loses diagnostics. Everything else is sound.

## D-1 — the poll-time getElementError drops diagnostics from the final failure message
- **Where:** `client/src/test/pollingElementError.ts:14-25`. `client/src/test/setupGuards.test.tsx:51-58` pins the changed shape (1 dump).
- **Failure:** a scratch probe (not committed) ran the same 16 cases twice: at the tip, and with only `installPollingElementError()` disabled (base behaviour). Both runs used @testing-library/dom 10.4.1.
  - (a) Multiple matches inside waitFor, then a timeout (`waitFor(() => screen.getByText('dup'))`). At base, both matching elements are listed. At the tip the message reads `Here are the matching elements:` followed by blank lines. The cause: `query-helpers.js:54` builds each element string from `getElementError(null, el).message` during the poll. The helper returns `''` for each one, and that text is baked into lastError, so it is never re-printed.
  - (b) A narrow container inside waitFor (`waitFor(() => within(dialog).getByText('absent'))`, with the dialog a portal after 300 rows). At base the message contains the dialog subtree. At the tip only the timeout dump of `document` remains. It is cut at DEBUG_PRINT_LIMIT 7000 (9,232 chars), so the dialog text never appears. Mantine modals and popovers render as portals at the end of body. Live sites with this pattern: `RoutingSettingsTab.test.tsx:868` and `ProviderInputWait.test.tsx:38`.
  - (c) Fake timers advanced inside the callback. Here handleTimeout runs while `_disableExpensiveErrorDiagnostics` is still set. Base gives `Timed out in waitFor.` plus the DOM. The tip gives the bare `Timed out in waitFor.` with no DOM. This is latent: no current site advances timers inside a waitFor callback (AgentsPage advances in act() outside the callback).
  - Identical at base and tip: false-then-true still passes (waitFor and findBy). A custom error thrown by the callback, an expect failure, an async callback, waitForElementToBeRemoved, and getBy/getByRole outside a poll all give the same message. Single-match timeouts (getByText, findByText, findAllByRole, getByRole in waitFor, within().findByText) lose only the poll-time duplicate dump and keep the timeout dump of the same or a broader container. That part is acceptable.
- **Why:** the brief requires the final message to be identical except for timing. For multi-match and narrow-container failures it is not identical, and the most useful diagnostic is lost.
- **Fix:** keep the saving, but make the poll error lazy instead of message-only.
  - Return an Error named `TestingLibraryElementError` whose `message` is a configurable lazy getter. On first read it computes `printing(message, container).message` and caches it.
  - waitFor discards unread poll errors, so polls stay cheap. onTimeout, copyStackTrace and the multi-match builder read it, so the final message keeps the base shape, with the DOM as it is at read time.
  - The getter must be `configurable: true`, because onTimeout re-defines `message`.
  - Then change the setupGuards timeout case to the base shape, and add a multi-match case and a within(container) case.

## Checked and sound
- type -> pasteInto: mechanical check. Mapping `pasteInto(` back to `userEvent.type(` and dropping budget/comment lines leaves exactly three kinds of change: the import, the CardEditModal `afterEach(notifications.clean)` and the C1270 constant. Every assertion after a changed site checks a final value. None targets per-keystroke behaviour, and the replaced strings contain no `{`/`[` key syntax. The single-character `'!'` site is still typed.
- CardEditModal `notifications.clean()`: this is test isolation. The Mantine notifications store is module state that outlives each case's render, so it does not hide a product leak.
- C1270_D1_READ_WAIT_MS = 6 s: 2 x 2,885 ms, rounded up. The comment names the measurement and says provisional. It cannot mask a hang because the case stays under the 20 s cap.
  - Advisory A-1: the Code report's line "No timeout was widened" is inaccurate. These three waitFor bounds went from 1 s to 6 s. The change itself is disclosed and justified.
- 29 per-test budgets: 16-19 s, all below the 20 s CARD-0069 hang detector in `vite.config.ts:130`. Each equals ceil(3 x p95). The displayed p95 is rounded to 0.1 s, which explains 6.0 s giving both 18 s and 19 s. Each comment names its measurement and says provisional. The budgets tighten the default, so none masks a hang.

## Evidence (server2, all client runs through scripts/test-client.ps1 under build-slot.ps1)
- Full suite at 86f398445: typecheck 0, lint 0. 129 files, 1853 passed, 1 skipped, 0 failed. Duration 219.84 s, after a 405 s slot wait. Cases over 5 s: 48; over 10 s: 3.
- Touched files alone (19 files): 489/489 passed in 196.3 s.
- setupGuards red probes:
  - polling install disabled: 2 polling cases red.
  - `releaseDetachedStyleSheets()` removed from the setup.ts afterEach: "the previous case left no sheet behind" red (expected 1 to be +0).
  - Both files were restored with git checkout.
- check-evidence-diff f32aed42e..86f398445: commits=8 entries=0 violations=0. With origin/master as the base it refuses with `base_not_ancestor`, which is expected.
- Landing: `git merge-tree --write-tree origin/master(e56e4d1b6) 86f398445` conflicts, as expected. A scratch `git rebase origin/master` skips the two duplicate picks (client patch-id 135fb23c equals 467471928^..ab7a868b5) and replays six commits cleanly. Its delta is byte-identical to 75a2fe01b..86f398445. **A rebase land is required; a merge land conflicts.**
- Not run: the .NET Unit lane and integration classes (no .NET source changed; there is no checkpoint plan, so there are no CP/V/R rows). PCs stay pending: no PCs are declared, and the report's candidates (a)-(d) remain for Mutation.
- Cleanup: the scratch test and scratch worktree were deleted, and the tree is clean.
