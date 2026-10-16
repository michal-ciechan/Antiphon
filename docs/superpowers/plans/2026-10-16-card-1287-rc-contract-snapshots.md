# RC contract snapshot repairs D-8 through D-11

Code owner / landing owner: `98df1e5f-93e3-4ea1-a0d7-8aedf2b0a146`.
Base: `b412a32ef89c0f12563eb6c0e8b4a0b586d85793`.
Classification: `fd513c184de20ba3c348d1888900eb36747511c0`,
`docs/investigations/2026-10-16-card-1281-rc-e2e-defects-d-7-d-8-12-classification.md`.

Only E2E fixtures and their client JSON contracts change. Preserve every full-object
comparison, downstream assertion and timeout. D-7 and D-12 belong to other tasks.
The specific CARD-1287 brief excludes the whole Unit lane. No product change or restart.

## Verification design

- S0: commit this closed selection; establish red for each existing affected method.
- S1: reconcile intentional additive fields, make pipeline environment evidence deterministic,
  and record every snapshot delta's source. Commit before green verification.
- V-1: pipeline snapshot including host, concurrency, runner and defaults shape.
- V-2: backlog cards plus the downstream boards snapshot.
- V-3: delegated list plus drawer and recorded-base assertions, including absence from summaries.
- V-4: home rail item and nested worker contracts.
- R-1: consumer inventory: these four methods are the only E2E readers of the six affected
  snapshot files. There is no additional E2E reader to select; CP-5 through CP-8 cover them all.
- R-2: named component tests for the five story consumers and drawer.
- R-3: compile the fresh frontend and all Storybook consumers.
- Manual: inspect full semantic snapshot diff and story imports; inspect fresh TRX names/counts,
  validate clean receipts, check owned-process teardown, and run the full base..HEAD evidence guard.

Prerequisites (explicitly listed non-checkpoint setup): gated checkpoint-tool bootstrap build
to `bin-c1287-tool/`, `npm --prefix client ci`, and gated `npm --prefix client run build`
before the first E2E run. The tool is then invoked with `--no-build`; its rows take their own slots.
Sanitize child environment: remove `PhoneHome__*`, `ANTIPHON_PHONE_HOME_*`, live/headed flags,
production service overrides; set `PhoneHome__Enabled=false`. Keep checkpoint ownership bound;
only a missing-token refusal permits the brief's unbound fallback. Never use production runner
sessions or restart the shared AppHost. E2E fixture owns its random-port runner and disposable DB.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S0 | `tests/Antiphon.E2E -> bin-c1287-red/` | red-pipeline | `/*/*/ContractSnapshotTests/Pipeline_status_contract` | V-1 red | Pipeline_status_contract | 1 | 5 |
| CP-2 | S0 | CP-1 | red-backlog | `/*/*/ContractSnapshotTests/Backlog_quadrant_contract` | V-2 red | Backlog_quadrant_contract | 1 | 2 |
| CP-3 | S0 | CP-1 | red-delegations | `/*/*/ContractSnapshotTests/Delegated_task_board_and_drawer_contracts` | V-3 red | Delegated_task_board_and_drawer_contracts | 1 | 2 |
| CP-4 | S0 | CP-1 | red-home | `/*/*/ContractSnapshotTests/Home_tasks_rail_contract` | V-4 red | Home_tasks_rail_contract | 1 | 2 |
| CP-5 | S1 | `tests/Antiphon.E2E -> bin-c1287-green/` | pipeline | `/*/*/ContractSnapshotTests/Pipeline_status_contract` | V-1, R-1 | Pipeline_status_contract | 1 | 4 |
| CP-6 | S1 | CP-5 | backlog | `/*/*/ContractSnapshotTests/Backlog_quadrant_contract` | V-2, R-1 | Backlog_quadrant_contract | 1 | 2 |
| CP-7 | S1 | CP-5 | delegations | `/*/*/ContractSnapshotTests/Delegated_task_board_and_drawer_contracts` | V-3, R-1 | Delegated_task_board_and_drawer_contracts | 1 | 2 |
| CP-8 | S1 | CP-5 | home | `/*/*/ContractSnapshotTests/Home_tasks_rail_contract` | V-4, R-1 | Home_tasks_rail_contract | 1 | 2 |
| CP-9 | S2 | n/a | consumers | `pwsh -NoProfile -File scripts/test-client.ps1 PipelineStagesPanel.test.tsx BacklogSection.test.tsx DelegationsBoard.test.tsx DelegationsHistory.test.tsx TaskDrawer.test.tsx TasksSection.test.tsx` | R-2 | six files, zero failures | n/a | 2 |
| CP-10 | S2 | n/a | frontend | `npm --prefix client run build` | R-3 | exit 0 | n/a | 2 |
| CP-11 | S2 | n/a | stories | `npm --prefix client run build-storybook -- --output-dir storybook-static` | R-3 | exit 0 | n/a | 2 |

### Cost

27 minutes checkpoint estimate, plus prerequisites/authoring, within the 60-minute brief.
No full assembly: impact is bounded to the four contracts and their named consumers.
Red rows are diagnostic failures, never green receipts. No repeat proof after green.

### Mutation handoff

Zero authored PC rows. SourceLanding Mutation still owns missing-control discovery and
deliberate red/restore/green cycles after ordinary Review and caller landing. No PC discharged here.

Execution refinement: import this table to an ignored YAML manifest and explicitly set
`build.properties.UseAppHost: "true"` on Windows. The tool's implicit false build
property otherwise disagrees with its Windows launch command. S1 and S2 share the final
source commit; restore the generated frontend dependency cache between their runs.
CP-5 may first remain red while its deterministic full response is inspected; update only
reviewed additions, commit, and rerun the red row with all final S1 rows.
