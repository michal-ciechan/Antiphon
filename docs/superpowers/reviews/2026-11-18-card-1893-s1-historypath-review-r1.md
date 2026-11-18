# CARD-1893 S1 HistoryPath compose review, round 1

CLEAN. CARD-1893 S1 and CARD-1985 retire the S0 `HistoryEnabled: "false"` stop-gap, put durable host-load history on the server2 runner-state volume, and replace the section 7.0 snippet with `scripts/c1893-runner-hold.sh`. No blocker. Advisories below do not hold the land. Positive controls stay pending. Mutation stays waived (operator 2026-11-12).

Reviewed source `ca4cefd6d753cdc31c70e751e8c9d4ca1d58c76f` on `feat/card-task-d46b3ec4`. Code owner `d46b3ec4-0001-437f-99c5-52cccd011873` (Claude Code), status Succeeded, card CARD-1893, base `997f9b8d9ffc4ba870d2fd56caf7c03781591624`. Range is seven commits. Reviewer is Grok. `origin/master` at review time is `6414a71967575b2a7dfbf283ee273f930b7a2100`. `git merge-tree --write-tree origin/master HEAD` exited 0 with tree `f1e2f58da649b040a18b66a7b4fbb8ff127e0918`.

## Compose and hold

`docker-compose.server2-runner.yml` session-runner sets uninterpolated literals:

- `SessionRunner__HostStats__HistoryPath: /state/host-load`
- `SessionRunner__HostStats__PhysicalHostKey: server2`
- `SessionRunner__HostStats__BindingRevision: "1"`
- `SessionRunner__HostStats__SourceEpoch: "58d738a7-044a-4089-a66f-42503861b8bf"`
- `SessionRunner__HostStats__AuthorityId: server2-build-slots`

The standing file has no `SessionRunner__HostStats__HistoryEnabled` line. `HistoryEnabled` defaults to true (`HostStatsSettings`). The `build-slots` service is 909 bytes and byte-identical to the parent. Neither runner compose file sets `cpuset` or `cpu_shares`. The temp overlay sets `SessionRunner__HostStats__HistoryEnabled: "false"` and contains no `HistoryPath` key. `docker/stack/init-state.sh` creates `/runner-state/host-load`, then `chmod 0700`, then `own_tree /runner-state` which chowns to uid/gid 1654. `session-runner` waits for `state-init` `service_completed_successfully`. The container starts as root and `docker/session-runner-grok/dind-entrypoint.sh` drops the runner to uid 1654, so mode 0700 owned by 1654 is the runner's directory. A failed chown fails init, and the runner does not start. Volume `runner-state` is mounted at `/state`.

`scripts/c1893-runner-hold.sh` is the section 7.0 check. It uses `set -u` and no `set -e`: `git merge-base --is-ancestor` exits 1 for `NOT-AFFECTED`, and `set -e` would abort before that branch. The argument must be one lowercase 40-hex SHA. `git cat-file -e` on that SHA and on collector `9cf3e491705247bc03814e6afed77a6534805dfd` must succeed. Any other git failure exits 2 with empty stdout. The script never fetches. It reads `git show` of the two compose files. S1 `RELEASED` requires the exact base path line, no base `HistoryEnabled` line, and the exact temp `"false"` line. S0 `RELEASED` requires the quoted base disable, no base path, and no temp `SessionRunner__HostStats__History` key. Every other resolved shape, including S1 lines beside a base that still disables history, prints `HOLD`. The hold fixtures write the opposite shape into the working tree of a throwaway repo, so a working-tree read would answer wrong. Identity keys are outside the `RELEASED` predicate; the contract tests pin them separately. That matches the updated section 7.0 paragraph.

`C1893S0RunnerHoldTests.cs` is deleted. `scripts/fixtures/c1906-compiler-policy-compose.mjs` and `scripts/fixtures/c346-workspace-slot-compose.mjs` assert the S0 `HistoryEnabled` key is absent, match the five new keys, delete only those keys, and `assert.deepEqual` the rest of the rendered model against the historical `git show`. Any other environment difference still fails that equality.

## Recreate without the server binding

The runner starts and writes durable history. `HostLoadCaptureService.ExecuteAsync` returns before opening a spool only when `Enabled` or `HistoryEnabled` is false. This compose leaves both true and sets an absolute `HistoryPath`, so the spool opens at `/state/host-load` (`configured-absolute`) and keeps `RunnerStoreId` on that volume. `HostStatsSettings.Validate` accepts a missing `SourceEpoch`. Append and open failures that are expected IO or access errors are logged and retried; the host stays up. Live sampling continues.

If the server binding is absent or the epoch does not match, `HostLoadIngestService.Binding` returns null unless runner id, sample time, `PhysicalHostKey`, revision, and `SourceEpoch` all match exactly one binding. Ingest still inserts the samples and still creates the `server2` cursor for that `RunnerStoreId`. Stored `PhysicalHostKey` and `SourceEpoch` are null and `IsPrimary` is false, because those columns come from the binding. `HostLoadHistoryService.HistoryAsync` returns `binding-unknown` until a current binding exists. A later page from the same store does not rewrite rows already ingested. A page with a different `RunnerStoreId` throws `host_load_identity_conflict`. A wrong epoch leaves samples non-primary. It does not pin a second cursor.

Activation plan step 5 writes the desktop user-secrets binding (key `server2`, revision 1, epoch `58d738a7-044a-4089-a66f-42503861b8bf`, `From` now) before step 6 rolls the runner. The hold script prints `RELEASED` from compose shape alone, so it does not prove step 5 has been done. Rolling after the binding makes the first samples primary. Rolling first still uses the durable volume store, which is the outcome the hold protects, and step 7 already treats earlier samples as unbound. The order in the plan is safe. The held shape remains a collector image whose compose leaves `HistoryPath` unset.

## CARD-1744

No collision in this slice. `HostLoadHistory:DirectRunnerKey` does not appear in the tree. `docker-compose.server2-server.yml` sidecar `server-session-runner` sets `PhoneHome__Enabled: "false"`, sets no `HistoryPath` and no physical key, and mounts named volume `antiphon-server2-server_runner-state`. With phone-home off, `HostStatsRoutes` leaves `RunnerId` at the default `local`. The phone-home runner is a different compose project, runner id `server2`, and its own `runner-state` volume. Activation plan step 11 already records the follow-up that puts `HostLoadHistory__Bindings__0__*` in the server compose. A future cutover still has one primary collector for physical host `server2` and this epoch.

## Verification

Receipt `20261118-200244-438b` on Debian, source `ca4cefd6d753cdc31c70e751e8c9d4ca1d58c76f`, `state=clean`, `buildSource=verified`, `dirty=0` on every row. Tool verdict GREEN, exit 0, wall 11m53s, 9 green rows, unlisted builds none, output `bin-c1893s1/` deleted by the tool. `scripts/validate-checkpoint-receipt.ps1` printed `CHECKPOINT SOURCE VALID source=ca4cefd6d753cdc31c70e751e8c9d4ca1d58c76f rows=9`. CP-9's one skip is `C1210_Windows_linux_only_outcomes_are_exact` (`NotExecuted`), listed once on `WindowsUnitSkipRoster` at this SHA. The checkpoint tool's own `validate` command prints `CHECKPOINT SOURCE INVALID reason=row_failed` because `ReportValidator` treats any skipped count as a failed row and has no roster. CARD-0835 names the PowerShell script, and that script accepted the complete selection.

| Row | Result |
|---|---|
| CP-1 | build ok, verified, 15/15/0/0 |
| CP-2 | reused, verified, 1/1/0/0 |
| CP-3 | reused, verified, 152/152/0/0 |
| CP-4 | reused, verified, 65/65/0/0 (min 60) |
| CP-5 | reused, verified, 1/1/0/0 |
| CP-6 | command exit 0, `PASS matrices=16 refusals=120 missing-file=1 base-equivalence=1` |
| CP-7 | reused, verified, 57/57/0/0 (min 30) |
| CP-8 | command build exit 0, `buildSource=notApplicable`, dirty 0 |
| CP-9 | reused, verified, 114 passed, 0 failed, 1 skipped (min 90) |

CP-9 slow classes: `DerivedImpactIndexTests` 173s (27 tests), `WindowsUnitPlatformAdmissionTests` 60s (6 tests). Wall stayed under the plan budget.

`scripts/check-evidence-diff.ps1` over the range: commits=7, entries=0, violations=0. `scripts/impacted-classes.ps1 -MergeSubset`: `migrations=False`. The downgrade selector was not selected (count 0). The same run lists high-radius classes, including `RemoteScriptContractTests`, which the checkpoint plan defers to Merge/nightly, and `ProcessSpawnLimitTests`, which CP-7 ran (57). Ordinary scope is the plan's closed table.

The diff is compose, shell, docs, fixtures, tests, and `tests/derived-impact-index.json`. It changes no production C# and no OS-selected production branch, so no OS-witness declaration is owed. `scripts/check-os-witnesses.ps1` was not run. Runner defaults and the session-runner catalogue were read; this slice stays unpinned.

Red witnesses are commits `58af1549588c958df23af6642fb20110593f5468` and `f85780c0d92755016e0a2e531568e76aa7e1526f`, both parents of production commit `67b26d75e099d90edcbaf18aa6f8e11a2b9cc732`. This review did not re-execute those red tips. HEAD has 15 `C1893_S1_*` methods on `DockerStackContractTests` and one on `DockerStackDocumentationTests`.

Pending, not discharged: TestDesign PC-1, PC-2, PC-13 through PC-16, PC-19 through PC-21, and plan PC-22, PC-23, PC-24.

## Advisory

`C1893_S1_Standing_runner_identity_literals` and the c1906/c346 fixtures accept any GUID in `SourceEpoch`. The compose literal `58d738a7-044a-4089-a66f-42503861b8bf` is the value the server binding must repeat (activation D-3). A later edit of that literal would still pass those checks. File that as a card. It does not hold this land.
