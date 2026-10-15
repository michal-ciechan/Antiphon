# D-13 native diagnostic Error compatibility

Owner: Code task df7c3a1a-8e1f-47da-8e28-b04ad33d3c77. Base:
`b7b2f112da759fe578b1a48ba16dfb747c55418c`.
Brief source: investigation D-13 at `84fb9b546bc2966159feb8812fab426c205be668`.

S1 commits a failing regression with a selected native failing diagnostic/TRX pair.
S2 recognizes Error as Failed and retains its explanation in the terminal report node.
Pass/skip records and existing UID, version and red-verdict gates stay unchanged.
The fixture `scripts/fixtures/nightly/d13-error-pair.ps1` intentionally retains one
matching record pair from task 1c14bf28 for `NightlyExecutionDiagnosticTests`.
It is test input, not a checkpoint receipt or an RC qualification claim.

## Verification design

The explicit CARD-1287 brief narrows this Final round to the closed adapter scope;
no whole Unit lane or shared test-infrastructure change. No full assembly run.
Searches for nightly-coverage / ConvertFrom-NightlyDiagnosticLog identify the
NightlyDiscoveryParserTests harness and NightlyScriptsTests source contract peers.

| ID | Witness |
|---|---|
| V-1 | `NightlyExecutionDiagnosticTests.D13_ErrorTerminal`: native Error under LF/CRLF retains failed outcome, UID, class, method and multiline message; matching real TRX gives complete coverage but failed verdict. |
| V-2 | `NightlyExecutionDiagnosticTests.D13_ExecutionCompatibility`: pass/skip unchanged, legacy Failed accepted, discovery/in-progress omitted. |
| R-1 | `NightlyExecutionDiagnosticTests.D13_ExecutionGuards`: duplicate Error, version drift, missing failed UID and extra failed identity remain refused. |
| R-2 | Full NightlyDiscoveryParserTests and NightlyScriptsTests classes. |
| V-3 | Read-only replay of retained RC diagnostic/TRX: exactly 167 terminal records, 54 Passed / 110 Failed / 3 Skipped, matching identities and preserved failure text. No new RC qualification. |

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-d13-red/` | error-red | `/*/*/NightlyExecutionDiagnosticTests/D13_ErrorTerminal` | V-1 | 1 expected red before fix | 1 | 4 |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-d13-final/` | error-final | `/*/*/NightlyExecutionDiagnosticTests/*` | V-1, V-2, R-1 | all 3 methods, 0 failed/skipped | 3 | 4 |
| CP-3 | S2 | CP-2 | adapter-adjacent | `/*/*/(NightlyDiscoveryParserTests)\|(NightlyScriptsTests)/*` | R-2 | all 8 methods, 0 failed/skipped | 8 | 1 |

CP-1 is red-first evidence, rerun at S2 before CP-2/CP-3 if needed by the runner;
its fixed test is also included in CP-2. Each run uses committed expected source SHA.
Bootstrap of the checkpoint CLI is an explained unlisted host-slot build.
V-3 is an explained non-build read-only manual replay, after checkpoint completion.

### Positive controls pending for SourceLanding Mutation

| PC | Mutation | Detecting filter |
|---|---|---|
| PC-1 | Remove Error recognition | `/*/*/NightlyExecutionDiagnosticTests/D13_ErrorTerminal` |
| PC-2 | Drop the failure explanation | `/*/*/NightlyExecutionDiagnosticTests/D13_ErrorTerminal` |
| PC-3 | Permit duplicate terminal UIDs | `/*/*/NightlyExecutionDiagnosticTests/D13_ExecutionGuards` |

### Cost

Ordinary checkpoint floor: 9 minutes, including expected-red build; authoring and
manual replay budget: 20 minutes. Total brief budget: 40 minutes. No repetitions
requested. Review follows ordinary completion; Mutation owns all deliberate mutants.
Restart: none (scripts load on invocation).
