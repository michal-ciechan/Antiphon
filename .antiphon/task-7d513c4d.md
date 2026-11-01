# CARD-1545 combined set merged onto master

Outcome: the reviewed combined set is on `feat/card-task-7d513c4d`, the land graph was updated once and re-checked approved, and the required rows are green. Current `origin/master` (`df92aa7b7886b278514dc73dc9e53dc461baa781`) merges cleanly. This delegate does not tag, publish, or push `master` or `rc`. Cutting a new rc waits until after the orchestrator lands this branch.

## Merge

- Merge commit: `8f721e971d80cb41e74e08bf99ea42aa2c47c2e7`
- Parents: `df92aa7b7886b278514dc73dc9e53dc461baa781` and `ffeaa28f0fd5dfcb4c6589fc6fb2c62645ac9370`
- Land-graph record commit: `4c0cdfe3a6a73414f277f17ac42e9ffa3505916f`
- Conflicts: none
- Census seed `49b77d40b`, master `df92aa7b7886b278514dc73dc9e53dc461baa781`, and subject `ffeaa28f0fd5dfcb4c6589fc6fb2c62645ac9370` are ancestors
- `git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` before this report: exit 0, tree `9833132b9979a021e584582765ea3985a9baad11`, equal to `4c0cdfe3a6a73414f277f17ac42e9ffa3505916f^{tree}`
- `scripts/check-evidence-diff.ps1` base `df92aa7b7886b278514dc73dc9e53dc461baa781` head `4c0cdfe3a6a73414f277f17ac42e9ffa3505916f`: commits=112 entries=40 violations=0

## Land graph

One `-Update`, then `-Check` approved. Allow-list unchanged. Pins file unchanged.

| | digest | count |
|---|---|---|
| Recorded before update | `E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806` | 69 |
| Live and recorded after update | `D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE` | 69 |

SDK `10.0.403`. Pins blob `d7513d002e3b9cd3ed95faf82ee26d037c9604a1`. The first `-Check` refused only with `manifest does not match the selected graph`.

## CP-30

`FullCensusUnchanged` is a one-time commission-scoped candidate gate. `scripts/test-live-lane-manifest.ps1 -Case FullCensusUnchanged` runs only with `ANTIPHON_LIVE_PORTABLE_CONTEXT`. The lane plan and the S1a test design keep the historical comparison on the reviewed pre-land pack and seed `bf0b874e695a642118e2dd55913228e2f635d009`, say later unrelated edits do not invoke that check, and reject a permanent seed-pinned Unit or nightly row. Ordinary inventory tests call `ExactCoreRoster`, `EveryLiveGateHasDisposition`, and `ExactPtyCoreRoster`. Nightly and rc scripts do not call `FullCensusUnchanged`. No re-pin, range comparison, or retirement edit is documented, so the pin and the census engine were left as they are.

The reviewer already recorded `undeclared-new-uid` for `Card1619ExitPersistenceTests.Capture_budget_covers_host_launch_and_connect` on the merge with `d7fdee50b`. That method passed 1/1 here as an ordinary unit test. The historical command was not re-run: the required rows do not include a green CP-30, and the command needs CP-25 and CP-1 discovery exports in an owned context. A re-run against this HEAD is expected to flag master's post-pin UIDs. That is the commission contract for a candidate-only UID. It is not a standing master failure.

## Rows

All of these ran on `4c0cdfe3a6a73414f277f17ac42e9ffa3505916f`. Failed is 0 except where a skip is named.

| Row | total | passed | skipped |
|---|---:|---:|---:|
| CP-1 `LiveLaneInventoryTests` (includes CP-27 `ExactCoreRoster`) | 2 | 2 | 0 |
| CP-25 / CP-28 `ExactPtyCoreRoster` (one run, same method) | 1 | 1 | 0 |
| CP-16 `LiveLaneOutcomeTests` | 6 | 6 | 0 |
| CP-17 `LiveClaudeExecutableWiringTests` | 3 | 3 | 0 |
| CP-23 `LiveLaneExecutableTests` | 4 | 4 | 0 |
| CP-18 wrapper `20261101-071749-5d1b` | 6 | 6 | 0 |
| CP-24 wrapper `20261101-071912-5675` | 4 | 4 | 0 |
| CP-24-PARITY `StartupArgvMatchesProductionApplyOff` | 1 | 1 | 0 |
| CP-3 `LiveClaudeStartupWiringTests` | 5 | 5 | 0 |
| CP-19 `LiveClaudeDeliveryWiringTests` | 4 | 4 | 0 |
| CP-20 `20261101-072442-629d` | 2 | 2 | 0 |
| CP-26 same run | 2 | 2 | 0 |
| CP-29 `20261101-073340-d69d` | command exit 0 | EVIDENCE-SCAN clean, publicationCount=1 | |
| `PlanCoverageBindingTests` | 7 | 7 | 0 |
| `RcRosterRepairTests` | 3 | 3 | 0 |
| Census `C1514LandingSplitBudgetTests` (C1404, C1577, C1584, C1410, G2865) | 5 | 5 | 0 |
| Policy witness antiphon, agents-pty, session-runner, pty-host | 3 each | 3 each | 0 |
| `Capture_budget_covers_host_launch_and_connect` | 1 | 1 | 0 |
| `Card1619RunnerExitTests` | 10 | 10 | 0 |
| `Card1619LinuxStopIntentTests` | 11 | 11 | 0 |
| `Card1619LinuxCustodyStopIntentTests` | 2 | 0 | 2 |

The two custody tests skipped with `Requires the entrypoint-published namespace marker.` Failed 0. The run summary was `Zero tests ran`. The class is `[NativeLinuxOnly("CARD-1619 requires an owned native cgroup-v1 fixture")]`. The custody store-clock witness is `Card1619LinuxStopIntentTests` (11/11).

The inherited `8397984d` policy red was absent. CP-2 `WrapperOnlyPaidManifest` was left unrun. Native CP-4 through CP-7 and CP-21 stay owed and were not run.

## Verdict

Ready for the orchestrator to land `feat/card-task-7d513c4d` to master with `-Land`.
