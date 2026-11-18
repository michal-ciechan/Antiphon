# CARD-1406 review round 1: final one-core layout

Subject task `ae97c85e-4055-4384-bcbf-deb8b54ef60b` (`feat/card-task-ae97c85e`).
Reviewed source `18628faa77148d53ff6cd5ef7328d14d046f579d`.
Range `ae7bf7368231d819f555386350e1c30b185a589a..18628faa77148d53ff6cd5ef7328d14d046f579d` (5 commits, no merge commits).
Review task `ff5ff6a5-ca90-4134-9d77-67b8368a2cf2`.

Behavior verdict: clean. No product blocker. `reviewedSourceClean` is false: the runs below are build-slot leases, not checkpoint-tool receipts.

## Layout

- `docker-compose.server2-postgres.yml` and `docker-compose.server2-postgres-drill.yml`: `cpuset: "0,12"`, `cpu_shares: 2048`, `mem_limit: 8g`, `shm_size: 1gb`, blkio weight 800, and no `cpus:` key.
- `Get-Server2PostgresCpuRefusal` requires exactly one `cpuset` line equal to `$script:Server2PostgresCpuset` (`0,12`), exactly one `cpu_shares: 2048` line, and refuses any `cpus:` line. Both compose refusals call it.
- `docker-compose.server2-runner.yml` is byte-identical to `832fc75cb0` (the SHA named in `31871abdaf`) and to `b612dd5f7d436ce89b7e64db947958fba10f6914` (`1cffbb8ad6^`, the commit before the disjoint cpuset pin). No `cpuset` or `cpu_shares` on any service. The temp overlay has neither.

## Checks at 18628faa

| Check | Result |
|---|---|
| `scripts/test-server2-postgres-compose.ps1 -Case cpu-isolation` (lease `745114f4`, 9s) | passed=95 failed=0 |
| `node scripts/fixtures/c1906-compiler-policy-compose.mjs` | PASS, exit 0 |
| `node scripts/fixtures/c346-workspace-slot-compose.mjs` | PASS, exit 0 |
| `dotnet build tests/Antiphon.Tests -p:UseAppHost=false` (lease `e75f41ea`, 233s) | 0 errors |
| Guard filter below (lease `4a929a9d`, 59s, `--timeout 570s`) | 56 passed, 0 failed, 0 skipped |
| `check-evidence-diff.ps1` `ae7bf7368..18628faa` | commits=5 violations=0 |
| `git merge-tree --write-tree origin/master HEAD` | exit 0, tree `9421005d7654d30ac29c02c41d88b809465669e4` (`origin/master` = `ae7bf7368`) |
| Downgrade selector | 0 (no migration path in the diff) |

Guard counts: TestClassificationPolicyTests 42, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3, TestLaneCategoryGuardTests 1, TestTimingOperationLabelTests 2.

`impacted-classes.ps1` matched `timing-labels` (the two fixtures) and `test-lane-category` (the documentation test). Those two classes are in the 56. The derived DockerStack/runner classes were not re-run; the commissioned brief accepts the author's DockerStack 136/0.

`Temp_retirement_trusted_origins_registry_is_documented` adds `withdrawn-ref` and checks `scripts/c1609-host-proof.ps1` for `RecycleOriginMoved reason=withdrawn-ref`. The other six reasons still have to appear in `scripts/c1008-retirement.sh`. Stage pins are unchanged.

The case count is 95, not 407. `51 + 44 = 95` was the red case total in the plan. Commit `31871abdaf` attributes passed=407 failed=0 to the whole script.

## Advisory

`docs/superpowers/plans/2026-11-18-card-1406-cpu-shared-checkpoints.md` still contains the superseded reserved-core design and a `### Checkpoints` table whose CP-1 expects that red witness and whose CP-3 names a custom output. The "Final layout" section supersedes that text. It does not hold the land. V-4 host `docker inspect` stays an operator check. No PC is named; mutation stays waived (operator 2026-11-12). No OS-gated production factory changed, so `check-os-witnesses.ps1` was not run.
