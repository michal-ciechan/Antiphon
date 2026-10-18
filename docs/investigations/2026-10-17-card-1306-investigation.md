# CARD-1306 diagnostic adapter: Timeout, Cancelled, state order, truncation

Confirmed. The native execution adapter still mis-classifies terminal
diagnostic records. No product code was changed. The same function text is
on `origin/rc/2026-10-15` `69978ec1a246cd7a860f88a364eafc53a416aff7` and
`origin/master` `5b7f62d3ddeb4473a10f9c7573a81fff6459cf49` (the 187-242
region compares identical). Master only appends `Test-NightlyExactCoverage`
after line 1033. Nothing in `git log --all --grep=1306` touches the
classifier. Label on the card is `rc-2026-10-15`, and the defect is already
on that rc, so the fix lands on rc and is then merged to master.

Reproduced by calling `ConvertFrom-NightlyDiagnosticLog -Kind execution` and
`ConvertTo-NightlyNativeSuiteVerdict` on synthetic logs shaped like
`scripts/fixtures/nightly/c487-probe/execution.diag`. No lane was run.

## What the pinned stack actually emits

`tests/Antiphon.Tests/Antiphon.Tests.csproj:17` and
`scripts/fixtures/nightly/c487-probe/Probe.csproj:3` pin TUnit 1.44.0.
`scripts/lib/nightly-coverage.ps1:11` pins MTP 2.2.2. Reflection on those
packages:

- `TimeoutTestNodeStateProperty.ToString()` is
  `TimeoutTestNodeStateProperty { Explanation = ..., Exception = ..., Timeout = ... }`.
- `CancelledTestNodeStateProperty.ToString()` is
  `CancelledTestNodeStateProperty { Explanation = ..., Exception = ... }`.
  The type is obsolete in MTP 2.2.2. TUnit 1.44 still constructs it.
- TUnit `TestState` order is NotStarted, WaitingForDependencies, Queued,
  Running, Passed, Failed, Skipped, Timeout, Cancelled.
  `TestCoordinator` publishes `CancelledTestNodeStateProperty` for
  NotStarted, WaitingForDependencies, Queued, Running, and Cancelled.
  Failed and Timeout go through `Failed`, and `GetFailureStateProperty`
  publishes `TimeoutTestNodeStateProperty` only when the failure category
  is Timeout and the duration met the configured timeout. Otherwise an
  `OperationCanceledException` becomes `ErrorTestNodeStateProperty`, which
  this adapter already maps to Failed.
- MTP `TrxReportEngine` writes the TRX `outcome` attribute as `Failed` for
  every type in `WellKnownTestNodeTestRunOutcomeFailedProperties`:
  Failed, Error, Timeout, and Cancelled. It does not write `Timeout` or
  `Cancelled`.

## Mechanism

`Get-NightlyDiagnosticStateName` (`scripts/lib/nightly-coverage.ps1:187-195`)
returns on the first unanchored substring and has no Timeout or Cancelled
arm. `PassedTestNodeStateProperty` is first (line 189, blame `86c2dbb457`).
An unrecognized record returns `''`.

`ConvertFrom-NightlyDiagnosticLog` (`:331-336`) treats that empty name as
"skip" for an execution log. If no record was recognized it throws
`unknown diagnostic format` (`:350-351`). A recognized neighbour does not.

The explanation is attached only for `State -eq 'Failed'`
(`:238-240`). The capture stops at the first `, Exception = `.

Measured on this host:

| Input | Result |
|---|---|
| Timeout-only log | throw `unknown diagnostic format` |
| Cancelled-only log | throw `unknown diagnostic format` |
| Passed record plus a Timeout record | one node, the Passed row; the Timeout uid is absent |
| `ErrorTestNodeStateProperty` whose explanation contains the text `PassedTestNodeStateProperty` | `Outcome=Passed`, no `Message` |
| Explanation `boom, Exception = INNER still the explanation` before the real `, Exception = ` field | `Outcome=Failed`, `Message=boom` |
| Checked-in D-13 Error fixture | `Outcome=Failed` and the full ShouldAssertException message, unchanged |

Suite verdict uses the TRX rows, not the diagnostic nodes
(`ConvertTo-NightlyNativeSuiteVerdict` at `:958` passes `$rows` from
`Read-NightlyTrxIdentities` into `ConvertTo-NightlyCoverageVerdict`).
Diagnostic nodes are used for the expanded-uid check (`:983`) and the
identity-count cross-check (`:988`). `Test-NightlyExpandedRowsPresent`
(`:780`) counts only Passed, Failed, and Skipped.
`ConvertTo-NightlyNormalizedOutcome` (`:359-374`) maps `Failed` and `Error`
to Failed. The strings `Timeout` and `Cancelled` fall through to Unknown.

Measured verdicts:

- Timeout-only log, TRX `outcome="Failed"`, process exit 2:
  `testsPassed=False`, reasons
  `unknown diagnostic format`, `failed-row`, `process-exit-2`.
  Terminal nodes: 0.
- Same log with a TRX attribute `outcome="Timeout"` (not what the 2.2.2
  writer emits): `unknown-outcome:C487.Probe.Ordinary.Plain` instead of
  `failed-row`.
- Mixed log, TRX Passed plus Failed, process exit 0:
  `testsPassed=False`, reasons `failed-row`,
  `missing-expanded-row C487.Probe.SlowCases.1.1.SlowOne.1.1.0`,
  `trx-diagnostic-mismatch slowcases.slowone trx=1 diag=0`.

The card's "TRX outcome counts as unknown" matches the normalizer's default
arm, and that arm does fire for a literal `Timeout` attribute. The pinned
writer does not emit that attribute. With the writer the suite stays red
because the TRX row is `Failed` (`failed-row`) and, when the diagnostic
record was dropped, because the required uid is missing. A Passed
mis-label does not remove the uid, so the order bug leaves the TRX verdict
red while the diagnostic report row says Passed and has no message.

## Weak guard

`Test-D13_ExecutionGuards` parses the failure log into `$nodes`
(`scripts/test-nightly-tests.ps1:826-827`) and then calls
`Test-NightlyExpandedRowsPresent` with `-DiscoveryNodes @()` and
`-TerminalRows @()` (`:828-830`). The assertion
`D13 missing failed UID refuses` is true for any required uid against an
empty list. `$nodes` is used only by the following extra-identity check.
`NightlyExecutionDiagnosticTests.D13_ExecutionGuards`
(`tests/Antiphon.Tests/Scripts/NightlyExecutionDiagnosticTests.cs:19-22`)
requires that row. A parser that dropped the failure would still pass it.
Calling the same function with those empty arrays returned
`Ok=False`, `Missing.Count=1`, with no parse.

## Smallest fix

Not done, noted: no parser or test change in this investigation.

Map the last structural
`(?<![A-Za-z])(Passed|Failed|Error|Skipped|Timeout|Cancelled|Discovered|InProgress)TestNodeStateProperty \{`
rather than the first bare substring. Map Timeout and Cancelled to Failed
so the existing executed set and `failed-row` see them. A framed TestNode
whose state is still unknown becomes a Failed node instead of `continue`;
a log with no TestNode still throws `unknown diagnostic format`. Capture
the explanation through the last `, Exception = ` field, including Timeout
and Cancelled. Point `D13 missing failed UID refuses` at a parsed log that
omits the uid.

Red-first witness, one new method, one filter, no whole lane:

`/*/*/NightlyExecutionDiagnosticTests/D13_StateClassification`

The new `Test-D13_StateClassification` case asserts the five measured
failures above (timeout-only throw, cancelled-only throw, mixed drop,
Passed mis-label with no message, message cut to `boom`). It is red on
this tip before the parser change. Keep `D13_ErrorTerminal`,
`D13_ExecutionCompatibility`, and `D13_ExecutionGuards` as the existing
regression set; the checked-in Error fixture still parses.

Do not add a normalizer arm for a TRX attribute `Timeout`. The 2.2.2
writer already records that state as `Failed`.
