# CARD-1833 round 1 Final Review — slash autocomplete under the composer

Verdict: blocker. Reviewed source `51142c464b0dc6c4cd7dcf669506e0b76b663fc4` on `feat/card-task-ad16ed45` (red `04ef5a8726f9f16642cc2fb809456a27de117d2e`, fix `51142c464b0dc6c4cd7dcf669506e0b76b663fc4`, parent `ac6d00022e940ee3b90584c7d313567b4491c498`). Worktree was clean at that SHA. No product code was changed. Positive controls stay pending.

Source task `ad16ed45-c64b-472b-b04a-788643bd7e21` (role Custom, status Succeeded, branch `feat/card-task-ad16ed45`, deliverable `docs/investigations/2026-11-10-antiphon-decisions-remote-control.md`). The brief does not name `-FromTask`. CARD-1833 (`24df5302-4052-4d3f-91b1-d71ff1f56f8c`) is InProgress and its board title is "Human review questions: the operator decision queue". That title does not describe this diff. This review does not retitle the card.

There is no plan `### Checkpoints` table, so the named classes ran through `scripts/build-slot.ps1` in the worktree's native output (`UseAppHost=false`, `TEMP=/tmp`) rather than an invented checkpoint plan. `dotnet run` used `--no-build` after the matching build.

## Blocker

Where: `ClaudeScreen.IsSlashSuggestionContinuation` (`src/Antiphon.Agents.Pty/ClaudeScreen.cs`, the indent `>= 8` branch) and `ClaudeDialogObservation.Observe`, which returns Clear as soon as `TryReadComposer` succeeds, before `ClaudeBlockingPromptDetector.Detect`. The arm that consumes that Clear is `RemoteControlRecoveryService.WaitComposerAndSubmitArmAsync`: when the composer text is exactly `/remote-control` it sends `\r` (`DialogIntent.None`).

Failure: a highlighted slash row plus an 8-space numbered choice list is Clear. Probe `choice-8space-under-palette` against the built `Antiphon.Agents.Pty` DLL at this SHA printed `read=True state=Clear content=/remote-control` and the probe exited 1. The screen is the captured palette head (40-column rules, draft `>\u00A0/remote-control`, highlighted `> /remote-control …`) followed by:

```
        1. Yes
        2. No
        3. No, and tell Claude what to do differently
        Enter to confirm · Esc to cancel
```

The same reader also returns Clear for a palette whose highlighted row is `> /resume` while the draft is `/remote-control` (probe `highlight-is-resume-draft-is-remote-control`: `read=True state=Clear content=/remote-control`). `WaitComposerAndSubmitArmAsync` compares only the draft. Enter on Claude's slash palette selects the highlighted row. That screen would submit `/resume`. The captured 2.1.322 fixture itself highlights `/remote-control`, so that one frame would submit the intended command. Nothing in the arm requires the highlighted token to equal the draft.

Why: after the highlight row, a continuation is any row with indent at least 8, non-empty text, and not a rule. A numbered answer and an `Enter to confirm` footer meet that test. `C1833_SlashAutocompleteUnderComposerIsClear` pins a choice at indent 2 (`  1. Yes` / `  2. No`), which correctly stays unreadable, and does not pin indent 8. Observe never reaches the blocking detector once the composer read succeeds.

Fix: reject continuations that are numbered choices (`1.` / `2.` / `3.`), `Enter to` / `Esc to` footers, or highlighted menu rows. Withhold the arm Enter unless the highlighted slash token equals the composer draft. Add both screens next to C1833.

## What marks a palette

The new grammar is not "any line that starts with `/`" and not the hint phrase `esc to cancel`.

- The first non-hint row under the lower rule must be `IsSlashSuggestionHighlight`: after `TrimStart`, an ASCII `>` (the prompt glyph `❯` does not qualify), then a slash token (`/`, a letter, then letters, digits, `-` or `_`, then end or whitespace).
- Later rows are `IsSlashSuggestionCommand` (indent at most 6 and a slash token) or `IsSlashSuggestionContinuation` (indent at least 8, non-empty, not a rule).
- At most 12 suggestion rows. Blank rows are skipped. With no hint rows, at least one suggestion is required. Hint rows still use the CARD-1588 `IsComposerHintRow` path (`youveused`+`limit`, `pasteagaintoexpand`, wrapped `/rc` and `/effort`). This change does not remove that path.

## Adversarial reader results

Real `TryReadComposer` / `Observe` on the built DLL. Intended Clears: the captured 2.1.322 palette and a one-row palette (`content=/remote-control`); a user draft `/logout` sitting on ordinary hint rows; a draft that is only `/` while `/resume` is highlighted (`content=/`, so the arm does not send Enter); a multi-line paste between the rules (the paste is the draft).

Stayed not Clear: scrolled palette whose first under-box row is `/rc-status` (Unknown); trust 2.1.258, numbered trust, unrecognised trust; permission 1/2, permission 1/2/3, boxed permission 1/2/3, `PermissionWaitFixture.CapturedFrame`, a permission body that merely contains a `/logout` line; the indent-2 choice list; question picker, multi-select, free-text question, unknown choice, effort choice, model picker, reconstructed usage picker, reconstructed bypass-permissions warning; standalone rate-limit and `Login expired · Please run /login` (Unknown); both Remote Control menu fixtures; menu, trust, permission, and boxed permission appended under the captured palette (Blocked, because their first line is not an indent-8 continuation); multi-line paste under the box; prose that mentions `/rc`; Codex fixtures `v0156-trust-prompt.txt`, `v0156-update-modal.txt`, `v0156-signed-out.txt`, `v0158-update-modal.txt`; golden `effort-dialog-2968433b.ansi`.

The repo has no captured bypass-permissions WARNING dialog and no captured Claude `/usage` picker or login TUI. The reconstructed warning and usage picker stayed Blocked. The hint bar `bypass permissions on` is a recognised hint, not that dialog. Rate-limit text and the login sentence placed above a live idle composer with `? for shortcuts` read Clear. That is the existing "readable bottom composer" rule, and those two screens are not the new hole.

## Enter, the binary, and CARD-1588

Installed Claude on this runner is `/usr/local/bin/claude` 2.1.280 (233,709,640 bytes). Its palette is `position:"absolute", bottom:"100%"` (above the composer). The under-composer layout is the 2.1.322 change recorded in the committed fixture. That binary has no `launch-remote` and no `rc-status` strings. `immediate:!0` on `name:"remote-control"` means the command may run mid-turn, not that it runs without Enter. The fixture is the 2.1.322 highlight evidence: the first row is `/remote-control`.

CARD-1588 (`69b49a68-2157-4a30-873d-e3800741f483`, Backlog, "Claude 2.1.310 composer hint rows broke every agent relaunch") is the wrapped-hint path already in `IsComposerHintRow`. `ClaudeDialogObservationTests` plus `ClaudeScreenTests` at this SHA: 11 passed, 0 failed, 583 ms, lease `db36e4f3-2ef2-41ac-8a89-feaddf4b6ffa`. No class is named `ProviderDialog*`.

## Runs at `51142c464b0dc6c4cd7dcf669506e0b76b663fc4`

| Selection | Result |
|---|---|
| `ClaudeDialogObservationTests*` + `ClaudeScreenTests*` | 11 passed, 0 failed, 583 ms |
| Guard batch (policy, classification guard, slow-registry merge, tripwire, NativeLinuxPolicyWitness, mechanical PC contract, TaskPlatformGuidance, C1729 layering, TestLaneCategoryGuard) | 78 passed, 0 failed, 2 m 12 s, lease `ace35df8-650d-4d11-9332-37c7db091631` |
| `/*/*/RemoteControl*/*` in `Antiphon.Tests` | 97 passed, 0 failed, 1 m 32 s, lease `397eb0da-f0d6-4e92-ac58-b3df9158d1e2` |
| `RemoteControlConditionalInputTests*` in `Antiphon.SessionRunner.Tests` | 4 passed, 0 failed, 9 s, lease `4ca021cb-f846-40ae-a90e-b8ccc5827c55` |

`Antiphon.Tests` build lease `12ff3e96-b286-425f-b3f3-a9435eea9364`, 0 errors. SessionRunner.Tests build lease `a3dc41d4-0795-4760-8e10-f83b2fb4f8f1`, 0 errors. Pty build lease `a962cd11-ab75-4a27-9ffb-4c4859b75a49`, 0 errors. Warnings on those builds are pre-existing.

`scripts/check-evidence-diff.ps1 -BaseRef ac6d00022e940ee3b90584c7d313567b4491c498 -HeadRef HEAD`: commits=2, entries=0, violations=0. `origin/master` is not an ancestor of this tip (`base_not_ancestor`), so that form is the wrong range. `git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` against `b8812bcb12ba309092007e216251284466181b5d` wrote tree `1e4d3b2924b2e0cd317171941a1019557d8d965f` and exited 0. The brief's earlier master pin `da6b708a2cab8ced435feb2f359d2e90618ae333` is no longer `origin/master`.

`scripts/impacted-classes.ps1 -MergeSubset`: `migrations=False` (downgrade selector count 0), kind `pty-host-fixture`, plus the fixed land-notification and receipt classes, including `Card1277PtyHostFixtureTests`. Those rows were not executed. They do not read this composer grammar, and the blocker already returns the work to code.

## Human check and rollback

After this reader is activated, start Antiphon-Decisions (`9a6616c9-0777-4b16-962d-e697f8662942`) fresh. Confirm `Remote-control setup completed`, the bridge armed, and `/rename` to the agent name. The current generation stays vetoed (`RemoteControlArmSuppressed`). Do not deliver the pending 12:51 prompt on session `7b80d3e0-f9c3-4093-86c3-39e2ee95556c` into the stuck draft.

Rollback is reverting `51142c464`'s reader change so a covered hint bar stays Unknown and the arm continues to withhold Enter.

Advisory, recorded here and not a separate hold: `docs/session-runtime-invariants.md` still describes one to three hint rows under the box, while the reader allows four hint rows or a palette. PCs remain pending for method-scoped SourceLanding Mutation.
