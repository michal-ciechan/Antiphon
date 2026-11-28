# GH-61 / CARD-2108 Workflow step output

Base `b5b349d968b5c5ae28a66c73201f1ab06a540aa5`. Code owner `243d3d7d`.

The Workflow tab displays the existing execution DTO. Keep each execution's output legible at the floating window's width and report its actual result on the graph in the selected iteration. The client uses the server's refused commit-status warning format and recorded execution statuses.

| ID | Invariant | Witness |
|---|---|---|
| V-1 | File-list lines, blank lines and indentation survive rendering in a wrapping monospace block | WorkflowStepOutput.test.tsx output fixture |
| V-2 | Refused commit statuses have a warning list and a subsequent missing-edge failure remains in step output | WorkflowStepOutput.test.tsx warning and routing failure fixture |
| V-3 | The execution table keeps cells on one line and remains reachable by keyboard inside the panel | WorkflowStepOutput.test.tsx wide execution fixture |
| V-4 | Graph header shows the recorded terminal status and route or concise successful output. A continued failure remains visibly failed and a stopped failure carries no outgoing route | WorkflowStepOutput.test.tsx outcome fixtures |
| V-5 | At narrow widths the panel retains its bounds, the output wraps, and the full execution history is reachable inside the table's scroll region | Describe the delivered UI and keyboard affordance |
| R-1 | The touched components typecheck and lint, and all named regressions execute | `test-client` gates and fresh Vitest JSON |
| R-2 | The repository classification, policy, layering, checkpoint and live-lane contracts retain their baseline behavior | Named TUnit guards, fresh TRX, and exact baseline classification for any failures |

S1 publishes the regression tests before implementation. S2 publishes the client and documentation. The client checkpoints each invoke the same exact test file through the typecheck and lint gates. The TUnit rows share one build and serve bounded repository invariants. GH-56 changes no Workflow tab file used here. No OS-selected behavior changes.

### Cost

Budget 19 minutes of ordinary checkpoint execution including a cold client install and one isolated TUnit build. Any inherited guard failure is confirmed against the full task base using only the failed methods. Native placement and a full-assembly run have no role in this change.

## Performance budget

The row estimates are provisional for CARD-2108. Budget four minutes for the cold client install, typecheck, lint and one fixture file, then three minutes for the warm client run. Budget eleven minutes for the isolated TUnit build and guards, plus one minute for the reused build's two live-inventory cases. The client run installs dependencies once and each component fixture renders in a fresh DOM. The guard run shares one build. These fixtures use no real application or database host.

The first runs record wall time and build-slot admission in the checkpoint receipts, per-case time in Vitest JSON and TRX, and build and host phase timing in each checkpoint report. Default derived checkpoint deadlines apply. The initial sample can inform a later estimate.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | n/a | client-red | `pwsh -NoProfile -File scripts/test-client.ps1 WorkflowStepOutput.test.tsx -JsonResultPath ../.antiphon/gh-61-client-red.json` | V-1, V-2, V-3, V-4 | Red before implementation. All 8 original results execute and fail on the intended assertions | n/a | 4 | true |
| CP-2 | S2 | n/a | client-final | `pwsh -NoProfile -File scripts/test-client.ps1 WorkflowStepOutput.test.tsx -JsonResultPath ../.antiphon/gh-61-client-green.json` | V-1, V-2, V-3, V-4, R-1 | Typecheck and lint green. All results pass, 0 failed or skipped | n/a | 3 | true |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-gh61-guards/` | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(TestFixtureCleanupGuardTests*)\|(PlanCheckpointClassSelectionGuardTests*)/*` | R-2 | Each named class executes. Inspect any red rows against the task base | 78 | 11 | true |
| CP-4 | S2 | CP-3 | live-lane | `/*/*/LiveLaneInventoryTests/(EveryLiveGateHasDisposition*)\|(ExplicitGatesAreNeverFullRequired*)` | R-2 | Both named methods execute with 0 failures or skips | 2 | 1 | true |
| CP-5 | R1 | CP-3 | warning-provenance | `/*/*/ReviewExecutorApiTests/Manual_intake_walks_to_the_reviewers_and_fails_loudly_with_no_project_for_the_repo` | V-2 | The persisted delta response matches the summary and warnings fixture rendered by CP-2 | 1 | 1 | true |
