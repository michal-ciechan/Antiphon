# CARD-0984 FakeGrok LF console-wrap oracle

Landing owner: Code task 12869ac9 (supersedes 037ab247). Repair start: 6e62ca291ce050ebd4f6a7d1734a4b0e2c135e54; carries the four preceding Code commits. Original start: bb082f64745bc014a9be6e9ebdcbff3a69af5979.
Scope: tests/Antiphon.Agents.Pty.Tests/FakeGrokContractTests.cs only.

## Slices

S0: baseline at the untouched start commit.
S1: extract the inline CR/LF flattening to internal static FlattenLfSubmitEcho, preserving behavior.
S2: add pure Lf_submit_echo_flattens_console_wrap_backspaces_and_line_endings regression.
S3: remove explicit U+0008 wrap bytes in this helper only; retain existing assertions and add a native-log single-user-chunk exact-body assertion using existing fake log support.

S4 (Review 10955a8f repair): add explicit U+0009 to the pure test's echoed fixture and expected string. Preserve the helper, assertion label, Windows test and native-log assertions. The surviving tab follows the backspace location, so failing to remove U+0008 still produces the first mismatch at that same location.

## Verification design

Final scoped exception from the brief: only the full FakeGrokContractTests class and the new pure case; no whole Unit lane or whole Pty assembly. No shared helper changes, no unbounded classes. Windows-only cases retain their named Linux skips. No real-provider CLI calls.

V-1: baseline class roster has 18 Windows-only skipped results on Linux.
V-2: unchanged extraction retains the same 18-result roster and assertions.
V-3: `FakeGrokContractTests.Lf_submit_echo_flattens_console_wrap_backspaces_and_line_endings` uses synthetic SUBMITTED echo with wrap CRLF, ordinary CRLF and TAIL last\u0008 line; fails at label `LF submit echo must remain intact across console wrap artifacts` before the fix.
V-4: final full class retains 18 Windows-only skips and adds exactly one passing pure case. `FakeGrokContractTests.Unbracketed_body_with_LF_line_endings_submits_as_one_turn_with_newlines_dropped` retains label `LF endings must stay in the composer and submit as one turn`, adds label `LF body must produce exactly one native user message chunk`, label `native LF submit must contain the entire body with only LFs dropped`, and label `LF body must complete exactly one native turn`. This method compiles on Linux but its assertions require the separate Windows confirmation.
R-1: `FakeGrokContractTests.Lf_submit_echo_flattens_console_wrap_backspaces_and_line_endings` green after the helper fix, rerunning the red CP-3 row at S3.
V-5: separate later Windows inbox-host confirmation; deferred, never passed here.

V-6: repair final full `FakeGrokContractTests` class at the committed S4 SHA has exactly 19 Linux results: the pure method passes and the same 18 Windows-only methods skip, with zero failures. A tab is a surviving control, distinct from CR/LF/U+0008; exact equality forbids over-stripping it. No shared helper changed in this repair, so the scoped Final exception still excludes the whole Unit lane and whole project.
R-2: the pure method preserves the assertion label `LF submit echo must remain intact across console wrap artifacts` and its U+0008-deletion guard; ordinary green is covered by CP-5. Deliberate red/restore/green proof stays Mutation-owned.

PC-1: pending SourceLanding Mutation: delete the helper's U+0008 removal, run only `FakeGrokContractTests.Lf_submit_echo_flattens_console_wrap_backspaces_and_line_endings`, confirm label `LF submit echo must remain intact across console wrap artifacts` red, restore exactly and rebuild/run the same case green. Code does not execute deliberate mutants under the standing stage contract. Mutation also owns missing-control discovery for the added Windows-only native assertions.

PC-1 variant MUT-A: pending; stop stripping U+0008. First mismatch must remain actual U+0008 versus expected space after `TAIL last`, at the existing label.
PC-1 variant MUT-B: pending; replace the helper with removal of every `char.IsControl` character. The actual string must lose the surviving U+0009 before `FAKE response`, while expected retains it, at the existing label. Mutation must record actual/expected assertion output, verify restored bytes and empty source diff, rebuild, then prove the committed helper green. These are expected outcomes, not executed results. Review 10955a8f demonstrated the original MUT-B gap.

### Cost

Ordinary checkpoint floor: 8 minutes; authoring budget: 10 minutes. Each row includes an isolated build; no repetitions requested. Bootstrap checkpoint tool is an unlisted gated build required to execute the manifest.

Repair round floor: CP-5 only, 2 minutes plus authoring. CP-1 through CP-4 are inherited historical rows for completed S0-S3, not rerun in S4. There are no unbounded affected classes and no full-assembly run. V-1 through V-4 and R-1 retain prior evidence; V-5 was confirmed on Windows at 6e62ca29 (19/19), per the repair brief. Reconfirmation at S4 is optional separate Debug work, not claimed here.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S0 | `tests/Antiphon.Agents.Pty.Tests -> bin-c984-baseline/` | baseline | `/*/*/FakeGrokContractTests*/*` | V-1 | all 18 named Windows-only skips on Linux, 0 failed | 0 | 2 |
| CP-2 | S1 | `tests/Antiphon.Agents.Pty.Tests -> bin-c984-refactor/` | unchanged-extraction | `/*/*/FakeGrokContractTests*/*` | V-2 | same baseline roster, 0 failed | 0 | 2 |
| CP-3 | S2 | `tests/Antiphon.Agents.Pty.Tests -> bin-c984-regression/` | pure-regression | `/*/*/FakeGrokContractTests/Lf_submit_echo_flattens_console_wrap_backspaces_and_line_endings` | V-3, R-1 | exactly 1 executed; initial expected assertion failure, then 1 passed after S3 | 1 | 2 |
| CP-4 | S3 | `tests/Antiphon.Agents.Pty.Tests -> bin-c984-final/` | final-class | `/*/*/FakeGrokContractTests*/*` | V-4 | baseline plus 1 pure passing test, 0 failed | 1 | 2 |
| CP-5 | S4 | `tests/Antiphon.Agents.Pty.Tests -> bin-c984-surviving-control/` | surviving-control-class | `/*/*/FakeGrokContractTests*/*` | V-6, R-2 | exactly 1 pure passed, same 18 named Windows-only skips on Linux, 0 failed | 1 | 2 |
