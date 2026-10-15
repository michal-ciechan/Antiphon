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
| CP-1 | S2 | `tests/Antiphon.Tests -> bin-d13-final/` | error-red | `/*/*/NightlyExecutionDiagnosticTests/D13_ErrorTerminal` | V-1 | 1 method, 0 failed/skipped; rerun of expected red | 1 | 7 |
| CP-2 | S2 | CP-1 | error-final | `/*/*/NightlyExecutionDiagnosticTests/*` | V-1, V-2, R-1 | all 3 methods, 0 failed/skipped | 3 | 1 |
| CP-3 | S2 | CP-1 | adapter-adjacent | `/*/*/(NightlyDiscoveryParserTests)\|(NightlyScriptsTests)/*` | R-2 | all 8 methods, 0 failed/skipped | 8 | 1 |

Historical S1 CP-1 at `226550ffa3e2b373d48ffb4401d10a6dbc1975c8` built
`bin-d13-red/` and failed 0/1/0 (pass/fail/skip) with unknown diagnostic format.
The final table reruns that exact filter as CP-1 and shares one new isolated build
with CP-2/CP-3. The expected red cost was 6m40s (build 376.847s, slot wait 0s).
Each run uses committed expected source SHA.
Bootstrap of the checkpoint CLI is an explained unlisted host-slot build.
V-3 is an explained non-build read-only manual replay, after checkpoint completion.

### Positive controls pending for SourceLanding Mutation

| PC | Mutation | Detecting filter |
|---|---|---|
| PC-1 | Remove Error recognition | `/*/*/NightlyExecutionDiagnosticTests/D13_ErrorTerminal` |
| PC-2 | Drop the failure explanation | `/*/*/NightlyExecutionDiagnosticTests/D13_ErrorTerminal` |
| PC-3 | Permit duplicate terminal UIDs | `/*/*/NightlyExecutionDiagnosticTests/D13_ExecutionGuards` |

### Cost

Final checkpoint floor: 9 minutes, plus historical expected-red cost above; authoring
and manual replay budget: 20 minutes. Total brief budget: 40 minutes. No repetitions
requested. Review follows ordinary completion; Mutation owns all deliberate mutants.
Restart: none (scripts load on invocation).
