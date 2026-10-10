# CARD-0881 Final Review (task 5b9cda94) of Code task 7b32e72e

Outcome: CLEAN. Subject `feat/card-task-7b32e72e` at `dd73540e97065c9d865b2e7f63d9010da39b604b` (6 commits on `b30a3d8e746d7b454d2a534e564520a83a298e98`). Read-only: the subject branch was not rebased, amended or modified. Bound run (ANTIPHON_TASK_TOKEN present; not printed). Post-land Mutation was NOT run; PC-1..PC-4 stay pending for method-scoped SourceLanding Mutation.

## Merge-tree

`git merge-tree --write-tree origin/master dd73540e9` against fetched `origin/master` = `fb533571d3821fc61fb3efbfb1d5417362eb4b24` (unchanged since dispatch): exit 0, no conflicts, tree `96a888c022c5373b68047f8f73f3bd9b05619c5a` (branch tree `d2432fb117f7f153c37eb8110d78448458e4a964`; the difference is master's three CARD-1177/CARD-1178 commits, which touch none of the branch's 16 files). `server/Bundles/orchestrator.md` is unchanged on master since the base.

## Ordinary scope (CP-1, CP-2) rerun at the tip

Tested-source deltas: CP-2 (`c18147511..dd73540e9`) is docs/Results only. CP-1 (`3891db5b6..dd73540e9`) includes `OrchestratorInstructionsSnapshotBuilder.cs` (one XML comment) and the S2 docs/bundle/pin tests, so both rows were rerun once through the checkpoint tool at the tip (run `20261010-193845-09b5`, `--expected-source-sha dd73540e9…`), unedited lines:

```
CHECKPOINT CP-1 commit=dd73540e97065c9d865b2e7f63d9010da39b604b build=ok filter=/*/*/(EffectiveSettingsPipelineTests*)|(DispatchConcurrencyPipelineTests*)|(AgentTaskPipelineStatusTests*)|(HostEndpointTests*)/* executed=78 passed=78 failed=0 skipped=0 trx=/work/worktrees/task-5b9cda94/.antiphon/checkpoints/20261010-193845-09b5/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=dd73540e97065c9d865b2e7f63d9010da39b604b sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=dd73540e97065c9d865b2e7f63d9010da39b604b build=ok filter=/*/*/(StandingPipelinePolicyDocumentationTests*)|(OrchestratorInstructionsGuidanceTests*)|(InstructionBundleTests*)|(TaskPlatformGuidanceTests*)|(RunnerDefaultGuidanceTests*)|(CheckpointRepeatDocumentationTests*)/* executed=99 passed=99 failed=0 skipped=0 trx=/work/worktrees/task-5b9cda94/.antiphon/checkpoints/20261010-193845-09b5/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=dd73540e97065c9d865b2e7f63d9010da39b604b sourceState=clean buildSource=verified
unlisted: none (the tool ran no other build or test command)
wall: 4m28s  sequential-equivalent: 1m30s  builds: 2  max-concurrent-builds: 1  rows: 2 green 0 red 0 skipped
outputs: deleted bin-c881-s1/, bin-c881-s2/
verdict: GREEN exit=0
```

`validate-checkpoint-receipt.ps1 -Rows CP-1,CP-2 -ExpectedSourceSha dd73540e9…` on that `report.json`: `CHECKPOINT SOURCE VALID source=dd73540e9… rows=2`, exit 0. The Code task's own green receipts also validate: run `20261010-191127-c8d2` (CP-1 at `3891db5b6`, 78/78) and run `20261010-192656-a6da` (CP-2 at `c18147511`, 99/99), exit 0 each. Both floors hold (CP-1 min 51; CP-2 min 99). No `bin-c881-*` directory remains in this worktree. `check-evidence-diff.ps1 -BaseRef b30a3d8e7 -HeadRef dd73540e9`: commits=6 entries=1 violations=0.

Red-first: the Code task's run `20261010-190712-1585` at `1a562d839` (tests present, S1 absent) failed exactly the three new tests on the missing JSON properties `runners`, `runnerDefaults`, `remaining` (78 executed, 75 passed, 3 failed). CP-2's phrase pins cannot pass on the pre-S2 text (`effective-settings route` absent, `three-route` present, old byte pin).

## Brief checks

1. Endpoint fields correct and read-only. `RunnerDefaultSettingsService.ReadAsync` is `ReadSnapshotAsync` (AsNoTracking) then the private read-only `ProjectAsync`; no `EnsureInitializedAsync`, no SaveChanges. `runners` is `SessionRunnerCatalogue.ListAsync(_directory, _db, _settings, _remotePrep, ct)`, the same static projection and the same scoped services the `GET /api/session-runners` lambda resolves. `Remaining` is `Math.Max(0, limit - InFlight)` or null. Program registers `PhoneHomeRunnerDirectory` and `RunnerDefaultSettingsService`, so production never takes the null branches. The route mapping is unchanged (`tasks.MapGet("/pipeline", ReadPipelineAsync)`); none of the three endpoint files or Program carries a per-route authorization marker, so visibility is identical. No fail-open: a limit is never widened; a per-runner catalogue read failure yields the existing `catalogue_read_failed` row with `dispatchEligible=false`.
2. Tests can go red and no assertion was weakened. Phrase pins were swapped (`effective concurrency limits`->`effective-settings route`, `GET /api/hosts`->`/api/agent-tasks/pipeline?projectId=`), three negative pins and one new test were added, the byte pin was re-pinned. V-1 compares `runners` JSON to the live `GET /api/session-runners` response; V-2 counts `RunnerRoutingSettings`/`RunnerRoutingRevisions` rows after reads; V-3 asserts the clamp after a zero budget.
3. Doc/bundle truth. Every field name the docs cite exists on the DTOs (`effectiveLimit`, `configured`, `declared`, `source`, `inFlight`, `remaining`; `capacity`, `occupied`, `dispatchEligible`, `acceptingNewWork`; `globalRunnerId`, `kindDefaults`; `open`/`parallel`/`queued`/`parallelRemaining`/`queuedRemaining` per scope and per role). Source vocabularies match code: `default`/`global`/`project` (`DispatchConcurrencyPolicy`), `budget`/`config`/`runner` (`HostBudgetService`). `three-route` appears nowhere outside the negative pins, the pin comment and the plan. Remaining `GET /api/session-runners` / `GET /api/runner-defaults` mentions are placement-contract text the plan keeps (stage bundles, the orchestrator Platform sentence, loop doc "Runner defaults are not model pins", renderer `Owner:` line), the new equivalence sentences in ops-http/antiphon-api, and DTO comments. No forward-looking "CARD-0881 will replace" sentence remains outside historical plans/investigations.
4. Bundle pin and argv. `sha256sum server/Bundles/orchestrator.md` = `cc5dfdfc6fd462fd162d21d1de5a07f982e94c068d7db578856e30fa0c4bf057` (0 CR bytes), equal to the `CheckpointRepeatDocumentationTests` pin. Trimmed LF text SHA-256 prefix `b202af70` and untrimmed length 13379 chars (13378 trimmed) match the Results; the file is 13401 bytes because of UTF-8 em-dashes, and the pin tests measure `.Length`. `the_worst_case_composition_measured_sits_far_under_the_budget` (`budget - 500` = 29500) passed inside CP-2 at the tip. CARD-0822 builder: only the summary comment changed; `ReadPipelinePolicy` still reads `DispatchConcurrencySettingsService`/`HostBudgetService`, and the renderer's "Live counts: GET /api/agent-tasks/pipeline" is consistent with D-4.
5. Bundle edit vs master. The committed policy block (from "When you are working a board through its pipeline" to "Model-tier names are") is byte-identical to the plan's exact S2 block (2699 bytes). Besides the wording swap it removes two pointers the plan's block omits: "GET /api/hosts gives host limits and in-flight counts" (hosts now ride the pipeline) and "Read the scoped pipeline and `scripts/dispatch-concurrency.ps1` for the live limits." Every other rule (operator defaults, worktree, server2 preference, depth cap, same-source-area defer, retrospective companion, 409 handling, live-settings file, autonomy pointer, Platform paragraph pins) is retained.

## Disclosures (R2/R3, not defects)

- R3: the Platform sentence "the pipeline read's runnerDefaults and runners equal GET /api/runner-defaults and GET /api/session-runners" holds once the defaults row exists; before the first import the pipeline carries null while `GET /api/runner-defaults` seeds revision 1. The null case is stated in ops-http, antiphon-api and the DTO comment, and placement (`AgentTaskService`) and the instructions-file builder both call `EnsureInitializedAsync`, so a running orchestrator always sees the row.
- R3: V-1 checks `runnerDefaults` scoped-vs-fleet equality and its literal fields, not equality against the `GET /api/runner-defaults` body (the test host does not map that route); both bodies come from the same `ProjectAsync`.
- R3: the bundle drops the `scripts/dispatch-concurrency.ps1` pointer (planned); the script stays documented in docs/ops-http.md.
- R3: the generated instructions file (CARD-0822 renderer, unchanged by D-4) still says "Live counts: GET /api/agent-tasks/pipeline" without `?projectId=` and "Owner: GET /api/session-runners" for section 2; consistent, not stale.
- Follow-ups already named in the plan (seed-origin `default` source; occupancy helper duplication) remain for the caller to file if wanted.

## Scope statement

The brief's authoritative SCOPE DEFINITION is CP-1 and CP-2 (the plan's closed table; D-6 excludes a whole-Unit run). Both rows ran at the tip in one checkpoint-tool run with fresh identities and nonzero counts. No manual acceptance row exists in this plan. PCs pending.

--- review evidence ---
subjectTaskId: 7b32e72e-d9b0-4619-8741-79e7821bf04c
reviewedSourceSha: dd73540e97065c9d865b2e7f63d9010da39b604b
reviewedSourceClean: true
ordinaryScopeCompleted: Full
