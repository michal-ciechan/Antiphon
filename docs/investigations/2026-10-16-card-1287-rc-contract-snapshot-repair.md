# D-8 through D-11 snapshot repair evidence

Owner and landing owner: `98df1e5f-93e3-4ea1-a0d7-8aedf2b0a146`.
Worktree: `C:\Antiphon\worktrees\card-task-98df1e5f`.
Branch: `feat/card-task-98df1e5f`; base `b412a32ef89c0f12563eb6c0e8b4a0b586d85793`.
Plan: `docs/superpowers/plans/2026-10-16-card-1287-rc-contract-snapshots.md`.
Raw evidence: `.antiphon/task-98df1e5f/` and `.antiphon/checkpoints/` in this worktree.

## Reviewed changes

All snapshot changes are additive properties plus appending commas. No existing expected
value is replaced. Full JSON equality and downstream assertions remain.

| Snapshot / additions | Source / introducing change |
|---|---|
| Seven cards: `cardFileVisibility=Inherit`, `hasPrivateNotes=false`; two boards: `syncCardFiles=false` | BoardDtos, CARD-0408 `e6fcff5f67295b8721e71d48eeceecccd6892b45` |
| Seven cards: `codeVerificationPolicy=FullOnly`, `reviewVerificationPolicy=FullOnly` | BoardDtos, CARD-0544 `0489e33fa942dbcf40d9246ac46bcb9584abbaa6` |
| Seven cards: `requiredPlatform=Any` | BoardDtos, CARD-0710 `99d153a4a666f954c57c5321eeab9372de86cd64` |
| Cards envelope: `nextPageToken=null` | CARD-0846 `b50e21f4289d34624a3baabcac8091f23a4f9a3a` |
| Five task summaries and one drawer summary: `requiredPlatform=Any`, `runnerId=null`, `requirementSource=Default`, `observedPlatform=null`, `runnerDefaultsRevision=null`, `runnerSelectionSource=null` | AgentTaskSummaryDto, same CARD-0710 commit |
| Each summary: `interruptWritten=null`, `refinementDelivered=null` | CARD-0491 `431d7ce62339c517a835045358c547bcba161109` |
| Each summary: `effectiveModelId=null` | CARD-1009 `cdcffec3d7bca31cf0e3ee07283c730caf2c2013` |
| Drawer: `requestedWorktreeBaseMode=Auto`, `requestedWorktreeBaseTaskId=null`, `worktreeBaseBranch=null`, `worktreeBasePreviewJson=null` | CARD-0442 `ab9094127b309eacaa48340f37d7ee87566eaa96` |
| Drawer: `internalDecisionQuestions=[]` | CARD-0407 `98c439057456aab4a5a9ab81aeee6bd67874f07f` |
| Drawer: `parkSync=null` | CARD-1065 `f94f5565e0328c58371cc83bcfe998c4e121b2f5` |
| Drawer: `syncDebt=null` | CARD-1082 `80db50749fcead4278c06f1147ef8e0811416bb6` |
| Seven home items and one worker: `requiredPlatform=Any`, `runnerId=null`, `observedPlatform=null` | HomeTaskDtos, CARD-0710 `04c6830e3c3a745a68d832e02d8bbe96fab1521a` |

Pipeline pins original role limits (Code/Review 2, other named roles 1), local capacity 2,
max open 6, no remote fleet/default. The desktop red case observed Plan/Investigate 3 and
Code/Review 5, unlike the original classification's host; fixture inputs remove that
environment dependence. A fixture-only capabilities handler supplies a fixed Windows
runner sample. The real HTTP pipeline endpoint still projects database occupancy,
policies, defaults and runner evidence. Observation timestamps must parse before
normalization; the defaults import timestamp is seeded in the database. An ignored
`pipeline.actual.json` aids inspection, never automatic acceptance.
Pipeline JSON was inspected against the deterministic actual response at
`f9b55b74630ef23ee8d66b96e0b20d910e8abdbc`: all old values equal. Its only root
additions are `hosts` (CARD-0654 `01840c26c33d0a0a1d1948bb7a1748cbb1b9d39c`),
`taskScope`, `hostSummaryScope`, `concurrencyScopes` (CARD-0505
`be539dd6e705c392a210ac68c5d805597ff957a5`), and `runners`, `runnerDefaults`
(CARD-0881 `7c4e40b8b95b1d208ab12373880bd97e850e926a`). Every added JSON line
belongs to these objects: local host 1 occupied/2 capacity/1 remaining; null and
scenario-project effective policy/occupancy; fixed desktop capability evidence and
revision-1 migration defaults. The nested CLI version fields are CARD-0959
`2c80d38bc0e8f25d3fe9121b9345ed5ad92ab722`; draining/accepting fields are CARD-0727.
Observation clocks are normalized only after type validation; fixed input version
1.2.3 remains exact. No old snapshot field changed.

The first CP-5 build succeeded but confinement refused two 93,580,104-byte Playwright
node.exe files in unused prior output: the receipt reader has a 64-MiB ceiling.
Recursive cleanup was rejected by automatic approval review (`blocked by policy`).
Owned prior outputs were instead moved intact into ignored `red-build-retained` and
`probe-build-retained` evidence directories. No source bytes were restored or changed.
The second CP-5 produced the intended equality failure, 1 executed/1 failed, source
clean and build provenance verified. A batch-start Git timeout was also retained;
foreground checkpoint rows avoided that launcher failure. No deadline was widened.

## Verification and consumers

At `152ab0df360fae702686c833cadde0bc1ba3cfd9`, four exact E2E methods failed JSON
equality: 4 executed / 0 passed / 4 failed / 0 skipped, with fresh TRX identities checked.
There is no new test or removed assertion. The only E2E readers of the six JSON files are
these four methods. The later backlog boards and drawer/recorded-base checks remain.
Story imports inspected: PipelineStagesPanel, BacklogSection, DelegationsBoard,
DelegationsHistory, TasksSection. Their old fields/cache shapes remain intact. Final
CP-9/10/11 results belong in the stored report after source is frozen.

## Infrastructure attempts (not test failures)

- Declared gated prerequisites: checkpoint-tool build 0 errors/2 existing warnings,
  npm ci and fresh frontend build exit 0. Slots granted, waited 0s; held 5s/67s/28s.
- `20261016-011644-77ff`: tracked-snapshot refusal, zero tests; wait also refused its
  malformed outputPaths receipt. Source clean. Standalone read-only ledger probe later
  succeeded. Historical receipt retained unchanged.
- `20261016-012144-de04`: build passed, but DLL-only build/exe launch mismatch caused
  four zero-TRX rows. Red then used the checkpoint tool's exact `row --no-build` filters,
  its verified build and child `UseAppHost=false`. Final manifests explicitly pin true.
- Phone-home variables cleared and `PhoneHome__Enabled=false`; isolated random-port
  runner and disposable Postgres. No production restart or session operation. Read-only
  observer captured no HTTP bodies and was stopped/joined.
- Generated node_modules moved into the ignored task root between runs to avoid repeated
  Windows ledger hashing; restore before frontend checks. No tracked source moved.
- Baseline row receipts are under `.antiphon/task-98df1e5f/red/`; unedited CHECKPOINT
  lines are in `CP-1-red.log` through `CP-4-red.log` and the final stored task report.

Zero authored PC rows; SourceLanding Mutation owns missing-control discovery after
ordinary Review and caller landing. Restart: none. D-7/D-12 remain outside this task.

