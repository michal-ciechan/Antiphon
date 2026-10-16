# D-10 runner determinism continuation

Code and landing owner: `8aa5fb3c-2f0b-42f8-adb4-9ece8d838ea4`.
Branch: `feat/card-task-8aa5fb3c`; base `95294927a528e9e6b80df155f234ebe80b8a9bb3`.
Continues [the snapshot plan](2026-10-16-card-1287-rc-contract-snapshots.md),
including its V-1 through V-4 and R-1 through R-3. Historical results stay unchanged.

## Verification design

S0 commits this selection and reproduces D-10's existing equality failure.
S1 corrects the six delegated summary runnerId expectations from null to desktop,
matching CARD-0710's public display mapping, as authorized on continuation.
Do not pin configuration for this field. Preserve full equality, every other
snapshot value, every assertion and timeout. The D-10
method must reach drawer equality, recorded-base values and summary exclusion.
V-1 pipeline, V-2 backlog, V-3 delegations, V-4 home; R-1 verifies the E2E reader
inventory and all four readers; R-2 verifies six client consumers; R-3 builds the
frontend and stories. Manual: inspect semantic diff, TRX identities/counts, clean
receipt provenance, owned-process teardown and the complete task-base..HEAD guard.

The specific continuation brief excludes whole Unit and narrows E2E to these
methods, superseding its generic Final footer. No whole assembly or unrelated
contract case: only these four methods read the six repaired snapshots.

Listed setup: gated checkpoint-tool bootstrap to `bin-d10-tool/`, gated npm ci,
and gated frontend build before the red E2E. Import the table to ignored YAML,
pin build.properties.UseAppHost to true, and run one committed group at a time
with exact expected HEAD. Rows reuse one build per source group. Keep bound
ownership; only a missing-token refusal permits clearing ANTIPHON_TASK_ID.
Clear inherited PhoneHome__*, ANTIPHON_PHONE_HOME_*, live/headed and production
service overrides in children; set PhoneHome__Enabled=false. The fixture owns
its random-port runner and disposable Postgres. No shared service restart.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S0 | `tests/Antiphon.E2E -> bin-d10-red/` | red-delegations | `/*/*/ContractSnapshotTests/Delegated_task_board_and_drawer_contracts` | V-3 red | Delegated_task_board_and_drawer_contracts | 1 | 4 |
| CP-2 | S1 | `tests/Antiphon.E2E -> bin-d10-green/` | delegations | `/*/*/ContractSnapshotTests/Delegated_task_board_and_drawer_contracts` | V-3, R-1 | Delegated_task_board_and_drawer_contracts | 1 | 4 |
| CP-3 | S1 | CP-2 | pipeline | `/*/*/ContractSnapshotTests/Pipeline_status_contract` | V-1, R-1 | Pipeline_status_contract | 1 | 2 |
| CP-4 | S1 | CP-2 | backlog | `/*/*/ContractSnapshotTests/Backlog_quadrant_contract` | V-2, R-1 | Backlog_quadrant_contract | 1 | 2 |
| CP-5 | S1 | CP-2 | home | `/*/*/ContractSnapshotTests/Home_tasks_rail_contract` | V-4, R-1 | Home_tasks_rail_contract | 1 | 2 |
| CP-6 | S1 | n/a | consumers | `pwsh -NoProfile -File scripts/test-client.ps1 PipelineStagesPanel.test.tsx BacklogSection.test.tsx DelegationsBoard.test.tsx DelegationsHistory.test.tsx TaskDrawer.test.tsx TasksSection.test.tsx` | R-2 | six files, zero failures | n/a | 2 |
| CP-7 | S1 | n/a | frontend | `npm --prefix client run build` | R-3 | exit 0 | n/a | 2 |
| CP-8 | S1 | n/a | stories | `npm --prefix client run build-storybook -- --output-dir storybook-static` | R-3 | exit 0 | n/a | 2 |

### Cost

20 minutes ordinary rows plus setup, authoring and slot waits within 45 minutes.
Run CP-6/7/8 before CP-2/3/4/5 so the frontend is fresh. Generated node_modules
may move into ignored task evidence between runs to avoid confinement hashing;
restore it for consumer builds. No repeat required after green.

### Mutation handoff

Zero authored PC rows or variants. SourceLanding Mutation owns deliberate mutants,
red/restore/green and missing-control discovery after Review and caller landing.
No PC discharged here. Candidate directory: `docs/superpowers/mutations/`.
Restart: none.

## S0 finding

CP-1 reproduced the intended red at `1f2211da08d5af737d4a0ce5e1bc4a91518cebc5`.
The [projection investigation](../../investigations/2026-10-16-card-1287-d10-runner-projection.md)
shows that configuration cannot produce the required null display value. S1 and
CP-2 through CP-8 were held for scope clarification. The caller subsequently
authorized exactly six snapshot corrections to desktop, retaining full equality
and requiring CARD-0710 in the commit message. No fixture pin was added.
