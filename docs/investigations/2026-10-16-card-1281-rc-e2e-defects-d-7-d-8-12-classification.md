# RC E2E defects D-7 and D-8..D-12 classification

Investigation `c2c02264`, started 2026-10-15 UTC, completed 2026-10-16 UTC; CARD-1281 release-candidate pilot. No separate numbered defect card was supplied. No product, test, fixture, policy or timeout changes.

**Verdict: confirmed; all six fail at both revisions.** D-7 is a pre-existing Linux fixture incompatibility; D-8..D-12 are pre-existing contract-test drift, rather than regressions introduced by this candidate. Classification does not turn these failing gates green.

## Source and execution identity

- Requested baseline B: `cc8798c1fa4f3c2faa834283aef080d8e5fd28f0`.
- Requested candidate C: `067b47e2dc903247f1a7c8aa539032cc4266e4ad`.
- Candidate execution/build HEAD: `209fe1f99146cbd1b4609ae4823ddcdec227c6e8`, whose complete delta from C is this investigation's initial Markdown note. Product/test/client/build inputs are C; the binary truthfully carries the documentation commit identity. This is diagnostic evidence, not an exact-C formal release receipt.
- Baseline execution/build HEAD: B, in detached worktree `/work/worktrees/task-c2c02264-baseline`. Candidate runs used `/work/worktrees/task-c2c02264` on assigned branch `feat/card-task-c2c02264`; no branch switch, merge or product/test source mutation.
- Original report and evidence note read at `84fb9b546bc2966159feb8812fab426c205be668`, `docs/investigations/2026-10-15-release-candidate-defects.md` and `2026-10-15-release-candidate.md`.
- Raw evidence root R: `/work/evidence/task-c2c02264/raw/`. Paths below starting `R/` expand to this absolute path. All generated logs, TRX, JSON, observer scripts and patches remain outside Git.
- `R/source-identity.json` proves identical Git blobs at B/C for 19 relevant files: both test classes, both runner/app fixtures, the four response DTO files, five relevant services, HostSession, and all five JSON fixtures. `R/source-attribution.json` verifies every cited historical introducing input is an ancestor of B; `R/source-introductions.patch` retains the actual patches. Historical source introduction is reconstructed, not a claim to have executed each old commit.

`npm ci` and fresh gated client builds completed in both worktrees; gated E2E builds completed with zero errors. Tests used one exact method per invocation, one invocation at a time, disposable Postgres and each fixture's loopback random-port runner. `UseAppHost=false` is the documented Linux build setting; the E2E project stages its native PtyHost. No production AppHost/server/runner was restarted or stopped.

These six methods use HTTP/SignalR and database fixtures, not Playwright pages. D-8..D-12 are **JSON snapshots, not screenshot comparisons** (`tests/Antiphon.E2E/ContractSnapshotTests.cs:18`, `:1331`, `:1388`). Pretty-printing and `ReplaceLineEndings("\n")` normalize JSON formatting. Chromium/fonts cannot explain these assertions. Passwordless sudo was unavailable; no OS packages were installed and no browser visual claim is made.

| Prerequisite | Exit | Outer wall (s) |
|---|---:|---:|
| baseline ci | 0 | 19.967 |
| baseline client-build | 0 | 63.986 |
| baseline e2e-build | 0 | 234.183 |
| candidate ci | 0 | 84.072 |
| candidate client-build | 0 | 30.316 |
| candidate e2e-build | 0 | 176.843 |

### Execution matrix

| Defect / exact method | B passed / failed / skipped | B outer wall (s) | C passed / failed / skipped | C outer wall (s) | Exit B/C |
|---|---:|---:|---:|---:|---:|
| D-7 / `SignalR_channel_send_routes_to_card_group_for_live_session` | 0 / 1 / 0 | 125.921 | 0 / 1 / 0 | 85.664 | 2 / 2 |
| D-8 / `Pipeline_status_contract` | 0 / 1 / 0 | 91.003 | 0 / 1 / 0 | 88.982 | 2 / 2 |
| D-9 / `Backlog_quadrant_contract` | 0 / 1 / 0 | 72.526 | 0 / 1 / 0 | 92.879 | 2 / 2 |
| D-10 / `Delegated_task_board_and_drawer_contracts` | 0 / 1 / 0 | 76.495 | 0 / 1 / 0 | 72.066 | 2 / 2 |
| D-11 / `Home_tasks_rail_contract` | 0 / 1 / 0 | 67.012 | 0 / 1 / 0 | 57.829 | 2 / 2 |
| D-12 / `Agent_files_review_threads_and_content_contracts` | 0 / 1 / 0 | 59.952 | 0 / 1 / 0 | 64.614 | 2 / 2 |

Total: **12 executed, 0 passed, 12 failed, 0 skipped**. Sum of completed driver wall times (including slot wait): 954.943 seconds. The separate canceled attempt is not counted as an execution.


All six method names and exact argv are retained in each `R/{baseline|candidate}-D<n>/argv.json`; `run.trx`, `result.json`, `assertion.txt` and `stack.txt` are beside it. `R/trx-audit.json` binds the executed method, count, run ID, duration and TRX SHA-256. Build provenance and binary hashes are in `R/{baseline|candidate}-provenance.json`. These scoped runs do not substitute for a full RC profile.

### Read-only observations and isolation correction

The external observer copied fixture-owned runner/host logs before teardown could delete them and read only the fixture's discovered random loopback server. Stored HTTP observations are identified in `http-observations.jsonl`; historical filenames `pipeline-response-N.json` contain the selected method's endpoint response (board for D-7, pipeline for D-8, cards for D-9, tasks for D-10, home tasks for D-11). `compare-http.mjs` applies the existing test's fixed timestamp and scenario-ID filters; it does not change a fixture or assertion. `normalized-live.json` and `structural-diff.json` retain full semantic differences omitted by Shouldly's 5,000-character display. Responses obtained during fixture teardown are not test failures or acceptance evidence.

One initial candidate D-7 run inherited host `PhoneHome__*` settings. Its runner attempted registration and received HTTP 409 refusals. It was canceled through SIGINT to its proven owned test PID; TUnit reported **Aborted, 0 tests, exit 3**, 258.263 seconds including slot wait. Its evidence remains in `R/candidate-D7-interrupted/` and `R/candidate-interrupted-TestOutput/`, excluded from the twelve completed cases. Both drivers were then hardened only in the external evidence root to clear `PhoneHome__*`/`ANTIPHON_PHONE_HOME_*` and set `PhoneHome__Enabled=false`, in addition to clearing live/headed/task-token/production service overrides. Subsequent stored runner logs have no phone-home registration attempts. No secret values were inspected or printed by this investigation. This inherited environment-isolation gap is caller triage, not a code repair in this task (`tests/Antiphon.E2E/Fixtures/IsolatedSessionRunner.cs:143` inherits its parent's environment; test policy clear list at `tests/test-execution-policy.json:566` omits phone-home).

## D-7 — SignalR_channel_send_routes_to_card_group_for_live_session

| Item | Evidence / finding |
|---|---|
| Classification | **(b) Linux/host-specific fixture input, also (c) pre-existing. No (a) regression. Confirmed mechanism.** |
| Reproduction | B fails the original `did not reach Running status` timeout for session `5e2ed52f-1fac-46d0-b6fd-c6bb45a8d8db`. C fails with `HubException` at SendAsync; the server records `NotFoundException` for exited session `56929ffa-1b72-49c8-98f4-e061ea410b78`. Both are one failed test, not a green rerun or evidence of a harmless flake. |
| Mechanism | `tests/Antiphon.E2E/Fixtures/AntiphonAppFixture.cs:640` selects Raw with `Path.Combine(Environment.SystemDirectory, "cmd.exe")` at `:642`. On this Debian host the executable is absent (`R/cmd-probe.json`: `spawnSync cmd.exe ENOENT`). Both owned PtyHost logs record UnixPty launch followed by exit code 2 within milliseconds. The test samples Running every 250 ms for 30 seconds (`tests/Antiphon.E2E/ChannelE2ETests.cs:150`, `:163`, `:170`, `:173`), then sends at `:65`. `src/Antiphon.PtyHost/HostSession.cs:190` records Running before its exit observer sets Exited at `:394`; `server/Application/Services/AgentChannelService.cs:171` excludes a no-longer-live session. |
| Stored rows / transcript | `R/baseline-D7/session-row-observations.json` and underlying board responses: requested session Starting → Failed/2 → briefly Running/2 → Failed/2. Baseline PtyHost log `5e2ed52f1fac46d0b6fdc6bb45a8d8db.log`: launch 23:56:27.9381192Z, exit/2 23:56:27.9424931Z. Candidate log `56929ffa1b7249c898f4e061ea410b78.log`: launch 23:48:13.7527649Z, exit/2 23:48:13.7552817Z. Both live under their run's `observed/runner/<run>/pty-hosts/logs/`. Candidate server log at `observed/Logs/ChannelE2ETests/SignalR_channel_send_routes_to_card_group_for_live_session/antiphon-20261015.log:459` records the failed hub call and NotFoundException. Runner logs also record the broken-pipe clear-live-buffer HTTP 500 after host exit. |
| Introducing input / card | **`e20582976c233202697de8a9294e57446121bb73`**, `feat(e08): add board-driven terminal UI`, 2026-05-16. No CARD identifier verified for that commit. The original report's `d04ab36d26d9bafea6d93396120e1fcdf6638161` / CARD-0009 attribution was a blame artifact: that patch adds a comma to the existing executable line while adding a runner URL. Both predate B. |
| Owner | E2E fixture/session-runtime owner; Windows native verification owner for the Windows receipt. |
| Smallest fix | One-line idea below; no implementation or design performed. |

### Not done, noted

Use a platform-appropriate fixture-owned long-lived raw process and retain the existing channel assertion and timeout.

Remaining uncertainty: Windows execution was not available; no claim that its entire channel case passes. The original RC runner's exact HTTP-500 body was not retained; this investigation reconstructs the unavailable-executable mechanism from fresh executions and stored rows, not from an invented original runner trace. The transient Running/exit-code race is observed, not separately repaired or fully characterized.

## D-8 — Pipeline_status_contract

| Item | Evidence / finding |
|---|---|
| Classification | **(c) Pre-existing stale API contract fixture. Confirmed; no (a) regression or font/pixel issue.** |
| Reproduction / exact diff | One failure at B and C. `R/{baseline|candidate}-D8/structural-diff.json`: six added root fields — `hosts`, `taskScope`, `hostSummaryScope`, `concurrencyScopes`, `runners`, `runnerDefaults`. All pre-existing fixture values match after the test's normalization. Full observed bodies resolve the original truncated assertion. |
| Mechanism / intent | `server/Application/Dtos/AgentTaskPipelineDtos.cs:23`, `:26`, `:29`, `:32`, `:38`, `:44`; projection at `server/Application/Services/AgentTaskPipelineStatusService.cs:286` and `:301`. These are deliberate host/concurrency/runner contracts, explicitly asserted by `tests/Antiphon.Tests/Application/EffectiveSettingsPipelineTests.cs:50` and `tests/Antiphon.Tests/Application/DispatchConcurrencyPipelineTests.cs:166`. The strict full-object comparison at `tests/Antiphon.E2E/ContractSnapshotTests.cs:1279` still uses the earlier fixture. `client/src/features/orchestrator/PipelineStagesPanel.stories.tsx:7` and `client/src/features/home/tasks/TasksSection.stories.tsx:15` import it. The client pipeline type at `client/src/api/agentTasks.ts:597` still describes the original subset; this failure does not demonstrate a broken rendered panel. |
| Introducing inputs / cards | Earliest observed additive mismatch: **`01840c26c33d0a0a1d1948bb7a1748cbb1b9d39c` / CARD-0654**, unconditionally adds `Hosts`. Later **`be539dd6e705c392a210ac68c5d805597ff957a5` / CARD-0505** adds scope/concurrency, and **`7c4e40b8b95b1d208ab12373880bd97e850e926a` / CARD-0881** adds runners/defaults/remaining. Stored fixture last captured at `e40bd3571e98855e1401ccc6c91d59f2440bcd84` / CARD-0557, before all three. All ancestors of B. |
| Owner | Pipeline contract/E2E and Storybook owners, with CARD-0654/0505/0881 API owners. |
| Smallest fix | One-line idea below. |

### Not done, noted

Reconcile this fixture with the intended additive contract and deterministic host/runner evidence, then validate its story consumers.

Remaining uncertainty: newly exposed runner timestamps/version/platform and defaults import timestamps are live environment data; a blind recapture is not proven stable. A Linux runner reports `platform: linux`, but the missing root fields would also exist on Windows. No visual comparison was run.

## D-9 — Backlog_quadrant_contract

| Item | Evidence / finding |
|---|---|
| Classification | **(c) Pre-existing stale API contract fixture. Confirmed; no (a) regression.** |
| Reproduction / exact diff | One failure at B and C. Each of seven seeded cards adds `cardFileVisibility: Inherit`, `hasPrivateNotes: false`, `codeVerificationPolicy: FullOnly`, `reviewVerificationPolicy: FullOnly`, `requiredPlatform: Any`; the envelope adds `nextPageToken: null`. **36 additions; all old values match.** See `R/{baseline|candidate}-D9/structural-diff.json`. |
| Mechanism / intent | Additive DTO properties at `server/Application/Dtos/BoardDtos.cs:102`, `:108`, `:112`, and pagination at `:182`; strict comparison `tests/Antiphon.E2E/ContractSnapshotTests.cs:1189`. `client/src/features/orchestrator/BacklogSection.stories.tsx:10` imports the stale JSON. The client already declares the platform field at `client/src/api/boards.ts:120`; missing newer mock fields are not evidence that the backend should stop emitting them. |
| Introducing inputs / cards | **`e6fcff5f67295b8721e71d48eeceecccd6892b45` / CARD-0408**, **`0489e33fa942dbcf40d9246ac46bcb9584abbaa6` / CARD-0544**, **`99d153a4a666f954c57c5321eeab9372de86cd64` / CARD-0710**, **`b50e21f4289d34624a3baabcac8091f23a4f9a3a` / CARD-0846** respectively. Fixture last captured `77d0dee6dfc294a53cc490798666644f37e49079` / CARD-0094. All additions predate B. |
| Owner | Backlog/card API contract and Storybook owners. |
| Smallest fix | One-line idea below. |

### Not done, noted

Refresh the reviewed backlog contract for its intended card-policy/platform/pagination additions and validate the Backlog story.

Remaining uncertainty: the test stops at the cards snapshot; its later boards snapshot at `tests/Antiphon.E2E/ContractSnapshotTests.cs:1224` is not assessed by this failure.

## D-10 — Delegated_task_board_and_drawer_contracts

| Item | Evidence / finding |
|---|---|
| Classification | **(c) Pre-existing stale API contract fixture. Confirmed; no (a) regression.** |
| Reproduction / exact diff | One failure at B and C. Five task rows each add nine fields: `requiredPlatform`, `runnerId`, `requirementSource`, `observedPlatform`, `runnerDefaultsRevision`, `runnerSelectionSource`, `interruptWritten`, `refinementDelivered`, `effectiveModelId`. **45 additions; all old values match.** See `R/{baseline|candidate}-D10/structural-diff.json`. |
| Mechanism / intent | Summary DTO additions at `server/Application/Dtos/AgentTaskDtos.cs:343` through `:364`, strict first snapshot at `tests/Antiphon.E2E/ContractSnapshotTests.cs:380`. The six platform/default fields are from CARD-0710; interrupt status is expressly documented at `server/Application/Dtos/AgentTaskDtos.cs:350`; recorded model identity at `:360`. Client type represents platform/runner/model at `client/src/api/agentTasks.ts:193` and `:202`; `client/src/features/delegations/DelegationsBoard.stories.tsx:15` and `client/src/features/delegations/DelegationsHistory.stories.tsx:9` import the old fixture. |
| Introducing inputs / cards | **`99d153a4a666f954c57c5321eeab9372de86cd64` / CARD-0710** (six platform/default fields), **`431d7ce62339c517a835045358c547bcba161109` / CARD-0491** (two refinement/interrupt fields), **`cdcffec3d7bca31cf0e3ee07283c730caf2c2013` / CARD-1009** (effective model). Fixture last recaptured `b04bacaecc4d02f894968ba982ff06601a694939` / CARD-0515; all later additions still predate B. Patch inspection avoids misattributing RunnerSelectionSource to the later punctuation change in CARD-0491. |
| Owner | Delegation contract/E2E and board/drawer Storybook owners. |
| Smallest fix | One-line idea below. |

### Not done, noted

Reconcile the reviewed task-list fixture with the intended platform/refinement/model fields and validate the board and drawer consumers.

Remaining uncertainty: first list assertion aborts the method; later task-detail and recorded-decision assertions at `tests/Antiphon.E2E/ContractSnapshotTests.cs:382` onward are not established by these runs.

## D-11 — Home_tasks_rail_contract

| Item | Evidence / finding |
|---|---|
| Classification | **(c) Pre-existing stale API contract fixture. Confirmed; no (a) regression.** |
| Reproduction / exact diff | One failure at B and C. `requiredPlatform`, `runnerId`, `observedPlatform` added to seven items and one nested worker: **24 additions; all old values match**. See `R/{baseline|candidate}-D11/structural-diff.json`. |
| Mechanism / intent | `server/Application/Dtos/HomeTaskDtos.cs:52` and `:107` append the fields to worker/item records. `tests/Antiphon.E2E/ContractSnapshotTests.cs:1153` compares the entire normalized response to the fixture. Client types include these properties at `client/src/api/homeTasks.ts:34` and `:85`; `client/src/features/home/tasks/TasksSection.stories.tsx:14` imports the older fixture. |
| Introducing input / card | **`04c6830e3c3a745a68d832e02d8bbe96fab1521a` / CARD-0710**. Fixture last changed `eab2608849e0c6db1aa91894f084df29ebf0dc0f` / CARD-0039. Both are before B. |
| Owner | Home tasks contract/E2E and Storybook owners, with CARD-0710 platform UI owner. |
| Smallest fix | One-line idea below. |

### Not done, noted

Refresh the reviewed home-task fixture for the intended item/worker platform fields and validate the rail story.

Remaining uncertainty: no browser rendering or broader home-task behavior was assessed.

## D-12 — Agent_files_review_threads_and_content_contracts

| Item | Evidence / finding |
|---|---|
| Classification | **(c) Pre-existing fixture/scenario drift plus non-deterministic generated-file content. Confirmed; no (a) regression and no demonstrated Windows/Linux text difference.** |
| Reproduction / exact diff | One failure at B and C at the first `agent-files.json` assertion. Added untracked `CLAUDE.md`, 777 bytes, zero agent edits, null review level, not stale. The two original entries (`notes/report.md`, `README.md`), their hashes, review state and baseline object remain equal. See `R/{baseline|candidate}-D12/structural-diff.json` and `R/original-Agent_files_review_threads_and_content_contracts-diff.json`. This corrects the original report's suggestion that common-file hashes/review values also changed: positional array alignment made them appear different. |
| Mechanism / intent | Scenario creates only README/notes (`tests/Antiphon.E2E/ContractSnapshotTests.cs:45`) then POSTs an agent with randomized name (`:58`). `server/Application/Services/AgentService.cs:638` provisions its workspace; `server/Application/Services/AgentWorkspaceProvisioner.cs:144` writes a missing CLAUDE.md, deliberately preserving existing unmarked files. `server/Application/Services/AgentFilesService.cs:174`, `:184`, `:222`, `:245` includes changed/untracked files, hashes them and sorts by path, so CLAUDE.md belongs first. Its generated body includes the random agent name (`server/Application/Services/AgentWorkspaceProvisioner.cs:213`) and a body-derived marker (`:197`). The test explicitly leaves file hashes real (`tests/Antiphon.E2E/ContractSnapshotTests.cs:1339`), so replacing just the stored snapshot would retain a changing hash. |
| Introducing input / card | **`5dfcc432037d03a09e8f38f13175d2cefb9240ef` / CARD-0059**, adds workspace provisioner and AgentService create hook. Fixture last changed `9a58b661250f2239603c5de541af879be407f3b4` (files-review baselines), before provisioner existed. Both predate B. |
| Owner | Files-review contract/E2E owner, coordinated with CARD-0059 workspace provisioning owner. |
| Smallest fix | One-line idea below. |

### Not done, noted

Make the generated-file part of the contract scenario deterministic while preserving the provisioner's intended behavior, then recapture the reviewed fixture.

Remaining uncertainty: later review-thread, content and transcript snapshots at `tests/Antiphon.E2E/ContractSnapshotTests.cs:166` onward were not reached. No product code suppressing CLAUDE.md is justified by this failure. Candidate CLAUDE.md hash is `e516f5fec65e4bbf`; original RC hash was `a747b4e1342278a6`; baseline hash is `a8c26d986ae523da`. All three differ while the common file entries match.

## Replay

The exact commands are recorded in each run's `argv.json`. Rebuild first: outputs were removed. Use the retained `run-driver.mjs` wrapper for its child environment clearing, passing a **new evidence directory name** and the selected checkout; it invokes each recorded gated command. Then `battery.mjs` shows the six exact method filters. Use fresh result directories and preserve the original TRX. No additional test selection is required to reproduce these six failures.

## Boundaries and handoff

No fix designed or implemented; one-line ideas above are not a plan. No assertion removed, fixture recaptured, timeout widened, required case skipped, production restart performed, or release-green claim made. Historical failures and the canceled run remain retained. Before repairs can be accepted, their owners must validate downstream assertions/story consumers that the first mismatch currently prevents reaching. Windows execution remains a separate obligation.

All foreground drivers completed. Before cleanup, the six DLL hashes plus two rebuilt client index hashes still matched their build provenance. The final census initially found one detached PtyHost from the canceled attempt, PID 6978, session `74599dfc-4886-4ef4-9e60-2af850badfb2`, still alive despite its shutdown-ack log; its child PID 7021 was already absent. PID/start time, session and task-specific executable path established ownership. SIGTERM terminated that host; final census is empty. This cancellation residue is recorded in `R/interrupted-host-cleanup.json`, not hidden as successful fixture cleanup. Removed all 30 inventoried `bin-c2c02264` roots; `R/output-cleanup-result.json` verifies none remain. Evidence and final per-test logs stay under R. No production process was signaled. Both worktrees had clean source before this final report-only edit.

--- next stage ---
next: plan
handoff: Plan scoped E2E repairs for confirmed inherited Linux cmd.exe fixture failure and five stale JSON contracts; preserve intent, make dynamic snapshot inputs deterministic, and verify previously blocked downstream assertions and story consumers.
artifact: docs/investigations/2026-10-16-card-1281-rc-e2e-defects-d-7-d-8-12-classification.md
