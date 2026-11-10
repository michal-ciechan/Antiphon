# CARD-1833 Final Review, round 2

Verdict: CLEAN. Both round-1 blockers stay closed on `585b6c1956e60a8b1f27de6eabbc96d8622fed96`. A slash palette is Clear only when the highlighted command equals the composer draft and the screen has no other dialog. The remote-control arm sends Enter only when that current observation names `/remote-control`.

Reviewed subject: Debug task `c0753d6e-744e-4907-ab7d-f717459ffbdd` (Succeeded), branch `feat/card-task-c0753d6e`, `deliverableRef` `585b6c1956e60a8b1f27de6eabbc96d8622fed96`. The brief names that branch and does not name `-FromTask`. Round-1 subject `ad16ed45-c64b-472b-b04a-788643bd7e21` is a different branch. Range `51142c464b0dc6c4cd7dcf669506e0b76b663fc4..585b6c1956e60a8b1f27de6eabbc96d8622fed96` (red `2ed7cd10654b70df37818f4bd7345fc8715cbb01`, fix `585b6c1956e60a8b1f27de6eabbc96d8622fed96`). Diff is 5 files, +408/−10.

`reviewedSourceClean` is false. The runs are leased worktree-native builds at the reviewed source bytes. There is no checkpoint receipt and no SHA-validated checkpoint provenance. There is no plan `### Checkpoints` table, so the brief's named classes ran through `scripts/build-slot.ps1` in the project's own output with `UseAppHost=false`.

`ordinaryScopeCompleted` is Full for this commissioned round: dialog and screen classes, `RemoteControl*`, `RemoteControlConditionalInputTests*`, the guard union, evidence-diff, merge-tree, and the three mutants. PCs stay pending for method-scoped SourceLanding Mutation.

## Round-1 blockers

B-1, probe `choice-8space-under-palette`. An 8-space `1. Yes` under the highlighted slash row is not read as the composer. State is Blocked. `AllowsRemoteControlSubmit` is false. `C1833_ChoiceAtIndent8UnderPaletteIsNotClear` pins `TryReadComposer` false.

B-2, probe `highlight-is-resume-draft-is-remote-control`. Highlight `/resume` with draft `/remote-control` is Unknown, `EnterCommand` is `/resume`, and submit is not allowed. `C1833_HighlightedCommandMustEqualDraft` pins State is not Clear. `C1833_Enter_is_withheld_when_the_highlighted_command_is_not_remote_control` (inside the 98) expects `ArmUnconfirmed`, conditional inputs exactly `["/remote-control"]`, and no submitted body.

## What the fence does

`ClaudeDialogObservation.Observe` reads the composer. When a highlighted slash command is present, `EnterCommand` becomes that highlight. A highlight that is not ordinal-equal to the draft returns Unknown. A highlight that matches, while `SlashPaletteCarriesAnotherDialog` is true, returns Blocked when the detector names a dialog and Unknown when it does not. `AllowsRemoteControlSubmit` is true only for State Clear and `EnterCommand` ordinal-equal to `/remote-control`.

`WaitComposerAndSubmitArmAsync` sends `\r` only when that observation allows submit and `TryReadComposer` on the same snapshot equals `/remote-control`. It then waits for transcript catch-up, returns `ObservationUnknown` when the session is Working, and otherwise calls `SendGuardedAsync` with that snapshot's `LastSequence` and `DialogIntent.None`. `StaleObservation` retries the arm loop, which observes the new frame.

The runner rechecks `LastSequence` under the launch gate and again inside the screen lock immediately before I/O. `HandleOutput` advances the sequence and feeds the screen under that same lock. `C514_Stale_sequence_refuses_with_zero_backend_bytes` passed. `CheckDialogInput` for `DialogIntent.None` allows any Clear state and does not read `EnterCommand` again. A content change that did not advance the sequence would be a residual hole. Pty output advances the sequence with the screen. That residual is an advisory.

## Attack screens

A scratch probe under `/tmp/c1833-probe` (not committed) drove the restored reader. After correcting one expectation, 59 named screens matched the safe result. The captured Claude 2.1.322 palette stays Clear with `EnterCommand` `/remote-control` and submit allowed. CARD-1588 wrapped `/rc` hint stays Clear with an empty enter command and submit withheld.

Safe results that matter:

- Highlight `/remote-control` plus a second row `/remote-control-something`: Clear, submit allowed. Enter selects the highlight, which equals the draft.
- Highlight `/remote-control-something` or `/remote-control-extra` against draft `/remote-control`: Unknown, submit withheld.
- Highlight `/remote` against draft `/remote-control`, and draft `/remote` against highlight `/remote-control` (prefix / tab-completion): Unknown, submit withheld.
- Draft with a trailing space is trimmed by the existing composer reader, so a matching highlight stays Clear and submit stays allowed.
- Different case (`/Remote-Control` draft, `/remote-control` highlight): Unknown, submit withheld. Both sides `/Remote-Control`: Clear with that enter command, submit withheld because the arm requires the canonical spelling.
- Marker `*` , a row with no marker, and `❯` are not highlights. Composer read fails, state Unknown, submit withheld. ASCII `>` with a tab separator stays Clear and submit is allowed. The captured fixture uses ASCII `>`.
- Trust, permission, bypass, usage, update, login, the Remote Control management modal, and the effort prompt, placed above or below the palette: submit withheld. Login is Unknown because the detector does not name it; the login keywords still keep it off Clear. The others above are Blocked. The others below are Blocked.
- Numbered `1.` / `2.` choices and an Enter/Esc footer at every indent 0 through 12: composer read fails, other-dialog is true, state Blocked, submit withheld. A sweep of `1. Yes` at indent 0 through 80 never became Clear.
- Footer alone at indent 0, indent 8, and column 41: Blocked. A long footer at column 41: Blocked.
- A lone `1. Yes` at column 41: Unknown, submit withheld. After the composer read fails, Observe requires the last line to look like a footer or a `2.` row before it returns Blocked.
- Half-drawn one-row palette whose highlight matches: Clear, submit allowed. Half-drawn `/resume`: Unknown, submit withheld.
- Resize with unequal rule widths, description wrapped to column 0: composer read fails, Unknown, submit withheld.

The probe's indent sweep also showed `1) Allow`, `* Yes`, `- Yes`, `Proceed`, and `Keep xhigh` become Clear with submit allowed once they sit at the description column (indent 43 or more on this 40-column fixture). `1.` choices do not. That is the wrapped-description rule, recorded below as an advisory. Real Claude choices in this reader are `1.` / `2.`, Yes/No, and Enter/Esc footers, and those stayed off Clear at every indent that was tried.

## Mutants

Each mutant had its own build lease and its own test lease, then the file was restored. Post-restore hashes: `ClaudeDialogObservation.cs` `79ac23911318453de2d0177218940cfa144d0b1b9711ea34485dfdd313503d28`, `ClaudeScreen.cs` `253d9eb582c6ae1e6e9c485a76a9134f841e1b2c681141ad9d9d22fff1f082d6`. `git diff` on both files was empty. Those hashes were checked again after the SessionRunner build.

1. Continuation treated as indent >= 8. `C1833_ChoiceAtIndent8UnderPaletteIsNotClear` failed because `TryReadComposer` became true. Build `fec0ce69-7293-4f5f-a914-94b35ca2dc60`, test `021772c5-30fa-4fc7-9f7b-7743e4d848bd`, exit 2.
2. Highlight-equals-draft check removed. `C1833_HighlightedCommandMustEqualDraft` failed because State became Clear. `EnterCommand` stayed the highlight, so the arm would still withhold. Build `97d33389-791d-423b-a79e-8e2fa0bedd20`, test `ef9f831f-f2b7-4341-adab-081895f0a21b`, exit 2.
3. Observe returned Clear immediately from the composer text. The same test failed because State became Clear. This is the unsafe shape: `EnterCommand` would be the draft while the highlight was `/resume`. Build `9793a6bc-1b15-406a-9b8b-f485cb9e4a47`, test `83d10dc8-7f1d-4044-8142-a51988f8bc15`, exit 2.

## Runs at the reviewed source

All through `scripts/build-slot.ps1`. No exit 4. Native output. `UseAppHost=false`. `TEMP=/tmp`.

| Selection | Result | Lease |
|---|---|---|
| Pty build, then `ClaudeDialogObservationTests*` \| `ClaudeScreenTests*` | build 0 errors; 15 passed, 0 failed, 1s 157ms | build `43389500-43e6-4e51-9a2b-205708ed1cad`; test `2294f948-458c-41b8-93de-ce99fa47cbec` |
| `Antiphon.Tests` build, then `/*/*/RemoteControl*/*` | build 0 errors; 98 passed, 0 failed, 3m 34s 976ms | build `9adf530b-1450-419f-9d2f-c2d153f88d3a`; test `d078d496-c487-4127-b65e-1d54dfa388ac` |
| Guard union (9 classes, 78 tests) | 78 passed, 0 failed, 2m 50s 618ms | `a25ec4fb-4837-4578-b118-3c16229712de` |
| SessionRunner build, then `RemoteControlConditionalInputTests*` | build 0 errors, 161 pre-existing warnings; 4 passed, 0 failed, 10s 913ms | build `1e86d09f-65da-42e2-9b6e-38c3929ef56e`; test `8bbab702-f006-40db-a70b-6c10f89eaa8b` |

The 15-count ran before the mutants, on the reviewed bytes. Later builds recompiled `Antiphon.Agents.Pty` from the restored source. The C514 pty-pipe connect timeout named in the brief did not occur in the 98. No solo rerun was required. The four conditional-input methods are `C514_Input_uses_microsecond_accepted_generation`, `C514_Stale_sequence_refuses_with_zero_backend_bytes`, `C514_Replacement_cannot_enter_between_validation_and_write`, and `C514_Nonwritable_destinations_have_distinct_refusals`.

Guard classes: `TestClassificationPolicyTests`, `TestClassificationGuardTests`, `SlowTestRegistryMergeTests`, `SlowTestTripwireTests`, `NativeLinuxPolicyWitnessTests`, `MechanicalPcContractTests`, `TaskPlatformGuidanceTests`, `C1729LayeringGuardTests`, `TestLaneCategoryGuardTests`.

`scripts/check-evidence-diff.ps1` over the reviewed range: `commits=2 entries=0 violations=0`.

`scripts/impacted-classes.ps1 -MergeSubset` on that range: `migrations=False` (downgrade selector count 0). Kinds `pty-host-fixture`, `test-lane-category`. This round's named selection includes `TestLaneCategoryGuardTests`. The fixed land-notification, receipt, and model classes are the script's standing land rows. They are outside this round's capped selection, and this diff does not touch them.

`git fetch origin master` then `git merge-tree --write-tree origin/master 585b6c1956e60a8b1f27de6eabbc96d8622fed96` wrote tree `86b3bc97bee62279b185088e5025bfca8a6edfa0` and exited 0. Current `origin/master` is `f125755c1648533c473ce46311c307788b4a9b65`. The brief's pin `057bf1f0361bb507dd11a818763e9e094fe1d192` is behind that tip. An earlier merge-tree against the pin also exited 0 (tree `8f157689bcd0cb614c5f38ddcbe0523a0d1fcb6f`). None of the five diff files changed on master between the pin and `f125755c`.

## Activation

It is safe to land this reader and then activate the host that runs Antiphon-Decisions. Land publishes; the human check is the activation proof.

Start Antiphon-Decisions `9a6616c9-0777-4b16-962d-e697f8662942` fresh. Confirm the transcript contains `Remote-control setup completed`, the bridge is armed, and `/rename` names the agent. The current generation stays vetoed (`RemoteControlArmSuppressed`). Leave the pending 12:51 prompt on session `7b80d3e0-f9c3-4093-86c3-39e2ee95556c` undelivered.

The card title on the board is still "Human review questions: the operator decision queue". This review does not retitle it.

## Advisories

These do not hold the land and do not call for another Review.

- A paren or bullet choice, or prose such as `Proceed` / `Keep xhigh`, aligned with the palette description column is treated as wrapped description and can be Clear with submit allowed. Left-aligned `1.` choices and Enter/Esc footers are not.
- A lone `1. Yes` at column 41 is Unknown rather than Blocked. Submit stays withheld.
- Login text beside a palette is Unknown rather than Blocked. Submit stays withheld.
- `CheckDialogInput` does not re-check `EnterCommand` after the sequence fence. The demonstrated stale-sequence path refuses with zero bytes.
- `docs/session-runtime-invariants.md` still describes one to three hint rows. The reader allows four hint rows or a palette. Carried from round 1; the doc was not re-edited here.
- The CARD-1833 board text does not describe this diff.

## Evidence

```
--- review evidence ---
subjectTaskId: c0753d6e-744e-4907-ab7d-f717459ffbdd
reviewedSourceSha: 585b6c1956e60a8b1f27de6eabbc96d8622fed96
reviewedSourceClean: false
ordinaryScopeCompleted: Full
reviewRound: 2
reviewedRange: 51142c464b0dc6c4cd7dcf669506e0b76b663fc4..585b6c1956e60a8b1f27de6eabbc96d8622fed96
```
