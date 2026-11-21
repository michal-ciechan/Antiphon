# Review r2: Codex 0.170 pointer-composer evidence fix (CARD-1514)

Review task `9bef45fb`. Subject `origin/feat/card-task-0a49db09` tip
`b02b4ea7ae80ad8eff3445a82d5477b80107bf44` (Code task `0a49db09-c6d5-4f93-b2b9-9733bd4938e3`, Grok).
Round 2, diff only: `2f5e1721900dcecb365d4a90c120a0cf6ad83cc2..b02b4ea7ae80ad8eff3445a82d5477b80107bf44`
(41415c404 red witness, 9adbf3ba1 fix, b02b4ea7a plan). Round 1: task `851cb98f`, CLEAN, A-1.

**Verdict: CLEAN.** No blockers. No false-Enter path found.

## Change

`CodexPointerComposerEvidence.TryReadComposer` now steps up one non-empty row when the last
non-empty row is `IsAgentShortcutsRow` (it must hold both `for agents` and `for shortcuts`). After
that it keeps the old checks: the row must be `IsModelEffortCwdFooter` and `TryReadFooterOnlyModel`
(`GPT-* <effort> · <path>`). The composer walk, `WholeBodyEquals` and `MatchingPrefixLength` are
unchanged. The step is the same one `TryReadBottomComposer` already takes.

## Attack 1: false Enter

The server presses Enter only on `observation.CompleteComposer`, and it checks that twice: in the
poll loop, and again after `SettlePostEvidenceAsync` (`SessionMessageQueueService.CodexPointers.cs`
~98 and ~106). `Prefix` is used only for the `prefix-advanced` milestone. `CompleteComposer` is
`IsComplete`, which is `WholeBodyEquals` over the bottom composer. That logic is unchanged and still
needs the full pointer.

I probed the built `Antiphon.Agents.Pty.dll` (this review's build at b02b4ea7a) with pwsh. Each
case is the measured Sol 0.170 banner plus the rows named:

| Case | complete | prefix |
|---|---|---:|
| full pointer, blank, footer, shortcuts | True | 65 |
| prefix only | False | 39 |
| pointer + trailing extra text | False | 65 |
| full pointer wrapped over two rows (indented continuation) | True | 65 |
| wrapped, second row truncated | False | 49 |
| pointer in history, empty composer below | False | 0 |
| pointer in history, non-indented status row, footer | False | 0 |
| no model footer, shortcuts row directly under composer | False | 0 |
| two shortcuts rows (single step lands on a shortcuts row; `TryReadFooterOnlyModel` false) | False | 0 |
| trust dialog on screen (Classify Trust) | False | 0 |
| `Working (… esc to interrupt)` on screen | False | 0 |
| `tab to queue message` (QueueMode) | False | 0 |
| `? for shortcuts` without `for agents` as last row | False | 0 |
| indented pointer text with no composer glyph | False | 0 |
| footer that is not a GPT model | False | 0 |
| 0.160 footer with warning chip as last row | True | 65 |
| shortcuts row above footer (order swapped) | False | 0 |
| popup rows under the composer | False | 65 |

Prefix 65 with complete=False (trailing text, popup rows) cannot send Enter, because the caller
gates on `CompleteComposer`, not on `Prefix`. This behaviour came before this change; the step only
lets 0.170 screens reach it. The wrapped-full case is complete by design: the old indented-continuation
concatenation does this, and the whole pointer is on screen.

## Attack 2: regression and red witness

- CP-1 filter on this review's build: 81 total, 81 passed, 0 failed, 0 skipped. CodexAccountBootstrapTests
  22 (both new `Codex_0170_*` included), CodexStartupReadinessTests 32, CodexV0160StartupTests 4,
  CodexReadyTrackerTests 4, CodexReadyWaitTests 19. This matches the Code report.
- Red witness: `git diff 2f5e1721..41415c404 -- src` is empty. So the red commit is exactly the
  no-step mutant. I checked it by reflection on the built dll: for the shortcuts row,
  `IsModelEffortCwdFooter` is True and `TryReadFooterOnlyModel` is False. So without the step,
  `TryReadComposer` returns false and `MatchingPrefixLength` is 0. The new assertion expects 65, so
  it goes red for the stated reason. I did not rebuild at 41415c404 (the operator allowed one build).
  The Code task's leased red run at 41415c404 (total 1, failed 1, expected 65 got 0) is consistent
  with this.
- The second new test (empty composer; shortcuts row alone) asserts False and 0. It can go red if
  the step lets a shortcuts row stand as the footer.

## Attack 3: swept readers

I confirmed each one myself:
- `CodexSubmitConfirmation.ComposerStillShows` (server, ~240) squashes and normalises the whole
  screen and looks for the first 40 chars of the body. It has no row position, so a shortcuts row
  cannot change its answer. It is used only after Enter, for the throw/diagnostic text and
  `composerMayHoldBody`. Not affected.
- `CodexWorkingIndicator.IsVisible` (`CodexDetectors.cs` ~82) checks each line for `Working (` …
  `esc to interrupt)`. It has no row position, so working detection is unaffected.
  `CodexTurnScreenTracker` uses it.
- `IsLiveSession` reads the last row only after `TryReadBottomComposer` fails. The shortcuts row
  passes `IsModelEffortCwdFooter`, so the answer is "live", which is the intended reading for that
  reader. Unchanged.
- The ready tracker, trust and update checks, and the capture store use `Classify` or whole-screen
  phrases. Unchanged by this diff.

## Attack 4: guards

- CP-2 guard batch on this review's build: 84 total, 84 passed, 0 failed (TestClassificationPolicy 45,
  TestClassificationGuard 1, SlowTestRegistryMerge 5, SlowTestTripwire 2, NativeLinuxPolicyWitness 3,
  MechanicalPcContract 11, TaskPlatformGuidance 5, C1729LayeringGuard 11, TestLaneCategoryGuard 1).
- CP-3 pins: inputDigest = pinnedInputDigest =
  `15137e48291f1b0c70668a7e967965ed1391f64812cddea271387d48ce1a9365`.
- CP-4 `audit-land-verification-graph.ps1 -Check`: exit 0, `approved`. It ran leased and rebuilt
  the missing `bin-land-audit` evaluator. This is the only extra small build, and it was needed for
  the row.
- CP-5 `check-evidence-diff.ps1 -BaseRef 2f5e1721… -HeadRef HEAD`: commits=3 entries=0 violations=0.
- The range does not touch `scripts/`, `tests/Shared` or `tests/derived-impact-index.json`, and adds
  no new class or file. The index is unchanged.
- `493bee0ee3af2052575bc7a26e9a313669b2fc2d` is an ancestor of HEAD. `git merge-tree --write-tree
  origin/master HEAD` is clean, tree `be5e6674e22f548b3677811cbb9e987ff92caf53` (equals the Code result).

## Run notes

- One leased build (`lease=139177f2-6f9a-43da-99b2-6f585c5a6aca`, waited 0s, load 2.14): a scratch
  traversal project built `Antiphon.Agents.Pty.Tests` and `Antiphon.Tests` in native default output
  with `UseAppHost=false` and `ANTIPHON_LINUX_COMPILER_POLICY=0`. The checkpoint tool was not used,
  because it needs a cold build per row and the operator allowed one build. As a result there are no
  SHA-validated checkpoint receipts.
- CP-1 run `lease=91785e8d-…` held 5s. It started at 1-minute load 18.57, which was the spike from
  this review's own build finishing. The pre-check should have waited, and this is recorded. Guards
  run `lease=4585f13e-…` started at load 10.81.
- No product or test file changed. PCs: none are defined; any stay pending for SourceLanding
  Mutation (waived by the operator 2026-11-12).

## Landing

Landing owner: Code task `0a49db09-c6d5-4f93-b2b9-9733bd4938e3`. It carries round-1 owner
`0a8d69b6` below it (2f5e1721 is an ancestor). Land with `-ExpectedSourceSha
b02b4ea7ae80ad8eff3445a82d5477b80107bf44`. This Review's evidence names 0a49db09 as the subject, so
`-FromTask` is not needed. Pass `-ReviewEvidenceId` only if the land tool cannot resolve this Review
task (9bef45fb) from the owner. The rc step is a fast-forward of `origin/rc/2026-11-30` with
493bee0e..b02b4ea7 (5 commits), and the master land goes through the owner.

## Advisories

None new.
