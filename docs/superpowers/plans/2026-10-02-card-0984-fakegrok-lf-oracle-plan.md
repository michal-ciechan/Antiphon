# CARD-0984 FakeGrok LF console-wrap oracle

Landing owner: original Code task 037ab247. Start: bb082f64745bc014a9be6e9ebdcbff3a69af5979.
Scope: tests/Antiphon.Agents.Pty.Tests/FakeGrokContractTests.cs only.

## Slices

S0: baseline at the untouched start commit.
S1: extract the inline CR/LF flattening to internal static FlattenLfSubmitEcho, preserving behavior.
S2: add pure Lf_submit_echo_flattens_console_wrap_backspaces_and_line_endings regression.
S3: remove explicit U+0008 wrap bytes in this helper only; retain existing assertions and add a native-log single-user-chunk exact-body assertion using existing fake log support.

## Verification design

Final scoped exception from the brief: only the full FakeGrokContractTests class and the new pure case; no whole Unit lane or whole Pty assembly. No shared helper changes, no unbounded classes. Windows-only cases retain their named Linux skips. No real-provider CLI calls.

V-1: baseline class roster has 18 Windows-only skipped results on Linux.
V-2: unchanged extraction retains the same 18-result roster and assertions.
V-3: pure regression uses synthetic SUBMITTED echo with wrap CRLF, ordinary CRLF and TAIL last\u0008 line; fails at label LF submit echo must remain intact across console wrap artifacts before the fix.
V-4: final full class retains 18 Windows-only skips and adds exactly one passing pure case; unchanged LF submit assertions plus native single chunk byte-equal to LF-dropped body.
R-1: same named pure case green after the helper fix, rerunning the red CP-3 row at S3.
V-5: separate later Windows inbox-host confirmation; deferred, never passed here.

PC-1 pending SourceLanding Mutation: delete the helper's U+0008 removal, run only /*/*/FakeGrokContractTests/Lf_submit_echo_flattens_console_wrap_backspaces_and_line_endings, confirm named assertion red, restore exactly and rebuild/run the same case green. Code does not execute deliberate mutants under the standing stage contract.

### Cost

Ordinary checkpoint floor: 8 minutes; authoring budget: 10 minutes. Each row includes an isolated build; no repetitions requested. Bootstrap checkpoint tool is an unlisted gated build required to execute the manifest.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S0 | `tests/Antiphon.Agents.Pty.Tests -> bin-c984-baseline/` | baseline | `/*/*/FakeGrokContractTests*/*` | V-1 | all 18 named Windows-only skips on Linux, 0 failed | 0 | 2 |
| CP-2 | S1 | `tests/Antiphon.Agents.Pty.Tests -> bin-c984-refactor/` | unchanged-extraction | `/*/*/FakeGrokContractTests*/*` | V-2 | same baseline roster, 0 failed | 0 | 2 |
| CP-3 | S2 | `tests/Antiphon.Agents.Pty.Tests -> bin-c984-regression/` | pure-regression | `/*/*/FakeGrokContractTests/Lf_submit_echo_flattens_console_wrap_backspaces_and_line_endings` | V-3, R-1 | exactly 1 executed; initial expected assertion failure, then 1 passed after S3 | 1 | 2 |
| CP-4 | S3 | `tests/Antiphon.Agents.Pty.Tests -> bin-c984-final/` | final-class | `/*/*/FakeGrokContractTests*/*` | V-4 | baseline plus 1 pure passing test, 0 failed | 1 | 2 |
