# CARD-1306 diagnostic adapter

Date: 2026-10-19. Plan task: `28c3e2b6`.
Assigned tip: `b3cfa45e2fd2eb556dfcf8cae1adf2cdd90fb037` on `feat/card-task-28c3e2b6`.
That tip is the investigation commit. Its parent is `69978ec1a246cd7a860f88a364eafc53a416aff7`.

The defect is real on this tip. The fix is already published. This plan records the landed contract and authorizes no second implementation.

## Ground truth

Read from the card (`pwsh -NoProfile -File scripts/card.ps1 get CARD-1306 -Board Antiphon`), from this tip, and from the fetched lines below. No parser was executed in this dispatch.

| What the card and the investigation measured | This tip `b3cfa45e` | Already on `origin/rc/2026-10-15` `61117dd1663d2f47ce4708c18b609b8936491986`, `origin/rc/2026-10-18` `42cbbd0417a2611f3a690e6671b13c9ba51fbf34`, and `origin/master` `c8bc95276ad1dfa22fcfcd0d1a2b3b2c6a1d80b0` |
|---|---|---|
| Timeout-only and Cancelled-only execution logs throw `unknown diagnostic format`. A Passed neighbour drops the Timeout uid. | `Get-NightlyDiagnosticStateName` (`scripts/lib/nightly-coverage.ps1:187-195`) returns on the first unanchored substring and has no Timeout or Cancelled arm. An empty name is skipped (`:331-336`). A log with nothing recognized throws (`:350-351`). | The last structural state property wins. The names are Passed, Failed, Error, Skipped, Timeout, Cancelled, Discovered, and InProgress, each followed by `TestNodeStateProperty {`. Timeout and Cancelled return Failed. |
| An Error explanation that contains the text `PassedTestNodeStateProperty` is reported Passed, with no message. | Passed is the first arm, so the quoted token wins and the Failed-only message capture does not run. | The quoted token has no structural `{`, so the Error property remains the state. The witness message is the explanation text. |
| An explanation is cut at the first `, Exception = `. | The capture is non-greedy and names only Failed and Error (`:238-240`). | `Get-NightlyDiagnosticFailureMessage` keeps the text through the last `, Exception = ` after the last failure-state `Explanation = `. |
| An unrecognized framed TestNode is dropped. A log with no TestNode throws. | An empty state continues. | A record that contains `TestNode { Uid = TestNodeUid` and no known state becomes Failed. No TestNode still throws `unknown diagnostic format`. |
| `D13 missing failed UID refuses` passes empty lists, so a parser that drops the failure still passes. | `Test-D13_ExecutionGuards` calls `Test-NightlyExpandedRowsPresent` with `-DiscoveryNodes @()` and `-TerminalRows @()` (`scripts/test-nightly-tests.ps1:828-830`). | The row parses the D13 failure after the required uid is replaced, passes those nodes as `-TerminalRows`, and requires one parsed node plus that omitted uid. `-DiscoveryNodes` stays `@()`. The presence check reads terminal rows. |
| The card text says the suite stays red because a TRX outcome counts as unknown. | `ConvertTo-NightlyNormalizedOutcome` (`:359-374`) has no Timeout arm, so a literal `Timeout` attribute is Unknown. The investigation measured that pinned MTP 2.2.2 writes Timeout and Cancelled as Failed. | The normalizer is unchanged. No Timeout or Cancelled arm was added. |

`scripts/lib/nightly-coverage.ps1` and `tests/Antiphon.Tests/Scripts/NightlyExecutionDiagnosticTests.cs` match between current master and `rc/2026-10-18`. `scripts/test-nightly-tests.ps1` matches through `Test-D13_StateClassification`; the rc file then carries later CARD-1341 content.

Publication commits, parent first:

- `69f32ab3c7f8397e9c914a425ab6d7d36d45c9b4` adds the failing witness on parent `69978ec1`.
- `42950a101ee838adab516d57fe44f16b59c80163` is the parser and guard fix, parent `69f32ab3c`.
- `1e32168a88c9dc8d3fc088ce468d98f3dec505ea` merges that rc history into master.

`42950a101` is an ancestor of all three tips above. The master SHA named by the investigation, `5b7f62d3ddeb4473a10f9c7573a81fff6459cf49`, is the earlier master from before `1e32168a8`.

## Decisions

### D-1. The landed commits are the fix

Acceptance is `69f32ab3c` plus `42950a101`, already contained in the three tips in the table. This branch stays documentation. Rejected: re-applying the parser edit onto `b3cfa45e`. That tip and the fix diverged from the same parent, and a second copy would be a new implementation of a change both lines already contain.

### D-2. Leave the rc line under test alone

`rc/2026-10-18` remains `42cbbd0417a2611f3a690e6671b13c9ba51fbf34`. This dispatch does not merge, push, or rebase onto either rc ref or master. The assigned branch stays fast-forward-only from `b3cfa45e`.

### D-3. The landed classifier is the contract

`Get-NightlyDiagnosticStateName` uses the last structural state property. Passed, Skipped, Discovered, and InProgress keep their names. Failed, Error, Timeout, and Cancelled return Failed. In `ConvertFrom-NightlyDiagnosticLog`, a framed execution TestNode whose state name is empty becomes Failed and counts as recognized. Discovery still throws `malformed discovery record` on an empty state. A log with no TestNode still throws `unknown diagnostic format`.

Failure text is captured only when the normalized state is Failed. The capture starts at the last `Failed`, `Error`, `Timeout`, or `Cancelled` property that has `Explanation = `, and otherwise at the last property of any name that has `Explanation = `. The message runs to the last `, Exception = ` after that start. With no such marker, it runs to the record's closing `} ] } }`.

`ConvertTo-NightlyNormalizedOutcome` stays free of a Timeout or Cancelled arm. Rejected: new outcome strings, which the expanded-uid set would treat as unknown, and a TRX arm for an attribute the pinned 2.2.2 writer does not emit.

### D-4. The landed witnesses stay the test

One new TUnit method, `NightlyExecutionDiagnosticTests.D13_StateClassification`, runs `Test-D13_StateClassification` with six harness rows:

- `D13 timeout-only is failed`
- `D13 cancelled-only is failed`
- `D13 mixed pass and timeout keeps both`
- `D13 error quoting PassedTestNodeStateProperty stays failed`
- `D13 explanation keeps text through the last exception field`
- `D13 unknown state is failed`

The unknown witness uses `NotStartedTestNodeStateProperty`, which is outside the structural list, and expects Outcome Failed and the explanation as Message. Synthetics are built in the script with the pinned `Version: '1.44.0.0'` and `Version: 2.2.2` headers and one `TestNodeUpdateMessage` per node. The filter is `/*/*/NightlyExecutionDiagnosticTests/D13_StateClassification`. That is one TUnit result. The six names are harness rows, not six TUnit results.

`D13 missing failed UID refuses` remains a row of `D13_ExecutionGuards`. Its terminal rows are the parsed omit log. `D13_ErrorTerminal` and `D13_ExecutionCompatibility` stay the existing regression methods.

### D-5. Placement and scope

The card platform is Any. GET `/api/runner-defaults` returned revision 2 with a global runner preference. GET `/api/session-runners` returned an eligible Linux lane and an eligible Windows lane. This plan pins neither a runner nor a platform. The script change is ASCII, `#requires -Version 5.1`, and Linux-capable. No session that waits for input is added. No C# production mutant exists: the classifier is PowerShell, and the C# method only launches the harness.

## Slices

No new slice. The historical pair is S1 = `69f32ab3c` (witness) and S2 = `42950a101` (parser, message capture, framed-unknown promotion, and the omit-uid guard). Files: `scripts/lib/nightly-coverage.ps1`, `scripts/test-nightly-tests.ps1`, `tests/Antiphon.Tests/Scripts/NightlyExecutionDiagnosticTests.cs`.

## Verification design

The red-first witness and the narrow green run already have commits. S1's parent is the confirmed buggy rc SHA. S2's parent is S1. The behaviour filter is `/*/*/NightlyExecutionDiagnosticTests/D13_StateClassification`. The green selection also retains `D13_ErrorTerminal`, `D13_ExecutionCompatibility`, and `D13_ExecutionGuards`. This dispatch did not build or run them. A run on `feat/card-task-28c3e2b6` would compile the pre-fix adapter and reproduce the investigation, then invite a duplicate fix.

### Cost

Ordinary floor for this dispatch: 0 minutes. No build, no checkpoint tool run, no repeat.

### Checkpoints

No table. Import of this plan fails closed at `### Checkpoints has no table`. That is intentional. There is no row to execute on this branch.
