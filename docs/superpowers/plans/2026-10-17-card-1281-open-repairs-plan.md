# CARD-1281 open repairs — nothing left to build

Plan for the D-7 and D-8..D-12 slice of CARD-1281. Task `36edd914`, branch
`feat/card-task-36edd914`, base `9b4d3c89a5912795a51956d84a5a235f5de2ffd6`.
Measured 2026-10-17 against fetched `origin/rc/2026-10-15`
`5e5d5cc728949420647d15a932766e70755a6f01` and fetched `origin/master`
`79dae0ed8dfc0b6f8b6a148b28f722584ecc2f69`. The task base already contains the
repair blobs. `origin/master` after that base does not change
`tests/Antiphon.E2E` or `client/src/test/fixtures/contract`.

Classification: `docs/investigations/2026-10-16-card-1281-rc-e2e-defects-d-7-d-8-12-classification.md`
(task `c2c02264`). That note predates the repairs below. Triage row: CARD-1281
on `feat/card-task-ff95c795`, `docs/investigations/2026-10-17-backlog-triage-2.md`,
master-only, "plan the confirmed Linux fixture failure and five stale JSON
contracts; leave the release-train policy for a later decision."

`GET /api/runner-defaults` and `GET /api/session-runners` were read. This plan
does not copy that catalogue and does not pin `-Runner` or `-Platform`.

## Outcome

D-7 and D-8..D-12 are already repaired on both `origin/rc/2026-10-15` and
`origin/master`. The E2E tree and the contract fixtures are the same blobs on
both tips. No Code slice remains. The card's release-train policy is a later
decision, outside this slice.

## Decisions

### D-1 — No source slice

The six defects' repairs are ancestors of both tips. A new Code edit would
repeat landed lines. A new test of those lines cannot go red.

### D-2 — Train

The card and the 2026-10-17 triage label this slice master-only. The repairs
are also already on `rc/2026-10-15`, as separate commits with the same blobs,
so there is no remaining rc-only delta and no new master-only delta. A future
recurrence of this slice stays master-only until the operator labels it
`rc-2026-10-15`.

### D-3 — Verification stays with the aggregate train

CARD-1281's operator rule is minimal per-card verification: the Code stage and
its own new tests, with the long suite once on the aggregate. This slice adds
no test. The six OptIn E2E methods, Storybook, and the Unit lane are not a
per-card checkpoint list.

### D-4 — Release-train policy is not this plan

The card body (measure wall-clock, design the train, list owner decisions) stays
for a later decision, as the triage row says.

### D-5 — Today's other RC fixes are different defects

These landed on `rc/2026-10-15` and were merged to `master`. They do not edit
the D-7 shell or the D-8..D-12 fixtures.

| Card | What landed | rc merge | master merge |
|---|---|---|---|
| CARD-1305 | RC lane placement for D-2 and D-6 | `b61c1c550` | `6aa0be65d` |
| CARD-1306 | Nightly diagnostic state classification | `3adbe0d5e` | `1e32168a8` |
| CARD-1310 | Diagnosis note | `4ebdd47de` | `96460bf7b` |
| CARD-1288 | Closed-card CreateAsync refusal doc | `6f36219bc` | `9978f6527` |
| CARD-1290 | Incomplete verification routes to code once | `141bf74d6` | `8724aceae` |

## Ground truth

The classification ran baseline `cc8798c1fa4f3c2faa834283aef080d8e5fd28f0` and
candidate `067b47e2dc903247f1a7c8aa539032cc4266e4ad`. All six methods failed at
both. The repairs below are later.

`git diff --stat origin/rc/2026-10-15 origin/master -- tests/Antiphon.E2E client/src/test/fixtures/contract`
is empty. Fixture blobs checked equal include `agent-files.json`,
`pipeline.json`, `cards-backlog.json`, `boards-backlog.json`, `agent-tasks.json`,
`agent-task-detail.json`, `home-tasks.json`, and `session-transcript.json`.
`AntiphonAppFixture.cs` is blob `15b15310c06eb4191be2f3c2cbc7a772a28ad476` on
the task base, `origin/master`, and `origin/rc/2026-10-15`.

| Defect | What the classification found | What both tips contain now | Still to build |
|---|---|---|---|
| D-7 `SignalR_channel_send_routes_to_card_group_for_live_session` | Linux Raw exe was `SystemDirectory\cmd.exe`, so UnixPty exited 2 before the session stayed Running | `tests/Antiphon.E2E/Fixtures/AntiphonAppFixture.cs` selects `cmd.exe` only when `OperatingSystem.IsWindows()` and `/bin/sh` otherwise. Raw launch adds arguments only for Claude, Grok, and Codex, so `/bin/sh` is started with an empty argv and stays up on the pty the way interactive `cmd.exe` does on Windows | No |
| D-8 `Pipeline_status_contract` | Snapshot missed `hosts`, `taskScope`, `hostSummaryScope`, `concurrencyScopes`, `runners`, `runnerDefaults` | `pipeline.json` has those roots. `PipelineContractScenario` pins role limits, disables phone-home, and answers `/capabilities` with platform `windows`, Codex CLI `1.2.3`. The test scrubs `platformObservedAt`, `capacityObservedAt`, and `codexCliVersionCheckedAtUtc`. Pipeline DTO last changed `d14edfc74` (2026-10-14), before the snapshot | No |
| D-9 `Backlog_quadrant_contract` | 36 additive card and envelope fields | `cards-backlog.json` carries `cardFileVisibility`, `hasPrivateNotes`, both verification policies, `requiredPlatform`, and envelope `nextPageToken`. `BoardDtos.cs` last changed `9456872a2` (2026-10-14). Omitted zeros and nulls (`hasMore`, `openBlockerCount`, `blockers`, `cardFileStatus`) match the DTO's ignore conditions | No |
| D-10 `Delegated_task_board_and_drawer_contracts` | 45 additive summary fields, then `runnerId` null versus the public display value `desktop` | Summary keys through `effectiveModelId` match `AgentTaskSummaryDto`. All five list rows and `agent-task-detail.json` `summary.runnerId` are `desktop`, which is `DefaultRunnerRoutingPolicy.DisplayRunnerId` of a null stored id on every host. Detail keys include the drawer fields the repair added. `AgentTaskDtos.cs` last changed `cdcffec3d` (2026-10-11) | No |
| D-11 `Home_tasks_rail_contract` | 24 additive `requiredPlatform` / `runnerId` / `observedPlatform` fields | `home-tasks.json` items and the nested worker carry those three. `HomeTaskDtos.cs` last changed `04c6830e3` (2026-09-26). Home projection keeps a null stored runner id, unlike the task-summary display map | No |
| D-12 `Agent_files_review_threads_and_content_contracts` | Untracked `CLAUDE.md` whose hash changed with a random agent name | The scenario posts `name = "Contract Agent"`. Recomputing the provisioner body with empty details, no deny-all hook, no parent `AGENTS.md`, and no channels yields 767 UTF-8 bytes and content hash `e496e93baf369739`, the values in `agent-files.json`. `session-transcript.json` entries have the 22 `TranscriptEntryDto` fields, including the five nulls from CARD-0072 / CARD-0082 / CARD-0719. That DTO last changed `69b93b499` (2026-10-01) | No |

### Repair commits

Same trees, different commit ids. Master ids are ancestors of the task base.
Rc ids are ancestors of `origin/rc/2026-10-15` and are not the master ids.

| Repair | On `origin/master` | On `origin/rc/2026-10-15` |
|---|---|---|
| D-7 portable shell and D-12 deterministic `CLAUDE.md` plus transcript nulls | `6ad006737f9eae24c734827729c18cbfbe515b12` | `7c580d7b52a714a0c9812bcebee53ebf2e2e7a10` |
| D-8 scenario and D-9..D-11 fixtures | `30936952eac275c25b123c75360688ab56d34b27` | `e55f4bfcbbdc78240a00f79081f632fb8c3ab4aa` |
| D-8 `pipeline.json` | `ab60504f6d80f6836e565df5b3d927d37930a163` | `9ebaab1aada62365c7d916e89c93720817c1243a` |
| D-10 `runnerId` `desktop` | `59ed791fa4c51ce72c7adb8581a10a383575d1ad` | `b227f814c02a88ea2e4c9f0731c22d8ff0ffcd4d` |

`AgentTaskPipelineStatusService` differs between the tips: master has CARD-1320
`7c3ecb47f`, which moves the Investigate backlog query into
`PipelineBacklogSelection`. The rc tip keeps the previous inline query. Both
keep Backlog cards with no owner session, drop `post-land-verification`, and
treat Queued, Dispatched, Working, Blocked, and Succeeded stage tasks as
started. The pipeline DTO and `pipeline.json` are unchanged by that move.

Recorded Windows evidence, not re-run here: D-12 failed once on the random
name at `f5cb7855a6a017a4428993bbc3fd9f53aa415346` (777 bytes, hash
`49ffb5a2a3918a93`), and the same run's Windows D-7 passed 1/1 while the exe
was still `cmd.exe` (`docs/superpowers/evidence/2026-10-16-rc-e2e-d7-d12.md`).
D-10's null-versus-`desktop` failure is
`docs/investigations/2026-10-16-card-1287-d10-runner-projection.md`. The
committed notes stop before the post-repair qualification receipts. Those
receipts are not a reason to edit the source again.

## Slices

No slice. No file. No test.

| Slice | Files | Tests | Train |
|---|---|---|---|
| none | none | none | already on rc and master |

## Verification design

Ordinary scope is empty. There is no new assertion and no red-first witness:
the lines that guard these defects are already the repaired lines.

This plan adds no session that waits for input, so no release deadline is in
scope. With parking disabled, no automatic release deadline exists (CARD-1083).

Checkpoints name no lane. `-Runner` and `-Platform` are omitted.

### Checkpoints

No rows. The closed list is empty. Do not import this plan into the checkpoint
tool.

### Cost

Ordinary checkpoint floor is 0 minutes.

## What is not built

- No further edit to the Raw exe selection, the contract scenario, or the eight
  JSON fixtures named above.
- No product change to `AgentWorkspaceProvisioner`, `DefaultRunnerRoutingPolicy`,
  or the pipeline service. `DisplayRunnerId` stays the constant `desktop` for a
  blank stored id. The provisioner still writes `CLAUDE.md` and still records
  its real hash.
- No scrubber change, no widened timeout, no skipped OptIn case.
- No Storybook or client rebuild, and no re-execution of the six E2E methods.
  Story imports that already read these fixtures stay as they are:
  `PipelineStagesPanel`, `BacklogSection`, `DelegationsBoard`,
  `DelegationsHistory`, `TasksSection`, `FilesReviewPanel`, and
  `SessionTranscriptPanel`.
- No per-card Review, Mutation, Unit lane, or land. The mutation candidates in
  `docs/superpowers/mutations/2026-10-16-rc-e2e-d7-d12.md` stay with
  SourceLanding Mutation after a landed code change. This plan has none.
- No parent-directory guard around the contract workspace. `FindConventionsFile`
  can still append an absolute `AGENTS.md` path when one exists above the temp
  directory, and that would change the `CLAUDE.md` hash. The captured hash is
  the body with no conventions file. `/tmp` on this runner has no `AGENTS.md`.
  That walk is not a slice.
- The phone-home inheritance note in the classification
  (`IsolatedSessionRunner` copying the parent environment) stays caller triage.
- CARD-1281's release-train design, bisect story, and owner-decision list.

## Rejected

- Re-applying the portable shell or recapturing the JSON. Both are already on
  the tips, and a fresh capture would move stable fields.
- An rc-fix branch for this slice. The rc tip already has the blobs.
- A Linux-only Code task whose checkpoint is the existing D-7 method. The
  missing-`cmd.exe` mechanism is gone; the operator rule leaves that execution
  to the aggregate train.
- Pinning a runner or a platform into the checkpoints. The catalogue was read
  and is not part of this plan.
- Treating CARD-1305 (D-2..D-6), CARD-1306, CARD-1310, CARD-1288, or CARD-1290
  as this slice.
