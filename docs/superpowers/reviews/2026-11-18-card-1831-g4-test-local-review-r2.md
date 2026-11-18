# CARD-1831 G-4 Final Review, round 2: test-local rename

- Review task: `14c87e0b-3c8e-4d17-abf5-b2d7c8257e2a` (Grok). Author: Code `2ad4156e-6b1f-4946-a6a5-69213d7d674d` (ClaudeCode). Different companies.
- Subject: `origin/feat/card-task-2ad4156e`, tip `4791d7307b24e370141b9b67ce8e8f2e8d4ed881`.
- Previous reviewed SHA: `f75c1751021f5f6281499a32484701fd21487d96` (round 1 `b6506cbf-7ae7-4ec7-956f-0542adcd960c`, subject `b92e5206-b479-4f8a-9d73-8209020b316e`).
- Round: 2 of 3, Final, diff only. `reviewedSourceClean` is false: these runs were leased build-slot runs, not checkpoint-tool receipts.
- Verdict: **clean**. No blocker. The round-1 harness abort is fixed. G-1, G-2 and G-4 are clean to land together as tip `4791d7307b24e370141b9b67ce8e8f2e8d4ed881`.

## Round-1 blocker

Round 1 (`b6506cbf`, reviewed SHA `f75c1751021f5f6281499a32484701fd21487d96`, `ordinaryScopeCompleted: Interim`) found one defect: `Test-PlainOneDriveLink` assigned a filesystem path to `$target` after dot-sourcing `postgres-snapshot.ps1`. That script's `[ValidateSet('desktop','server2')]$Target` rejected the path, and the full harness exited before its summary (737 PASS, later cases unrun). The same round held G-1, G-2 and the rest of G-4 clean.

## What the diff does

`4791d7307b24e370141b9b67ce8e8f2e8d4ed881` changes only two test scripts. Product code is unchanged.

- `scripts/test-postgres-snapshot.ps1` `Test-PlainOneDriveLink`: the path local is `$escapedDump`. The symlink, the write, and the "outside bytes" read all use that name. `$Target` stays the dot-sourced parameter.
- `scripts/test-job-status.ps1` `Test-TargetRollbackRestoresDesktop`: the status-file local is `$statusPath`, passed as `-StatusPath`. It no longer assigns `$status` in the function that dot-sources `invoke-postgres-snapshot.ps1` (which dot-sources `job-status.ps1` and therefore binds `$Status`).

## Collision scan

Same-scope assignments after a dot-source of `postgres-snapshot.ps1`, `job-status.ps1`, or `invoke-postgres-snapshot.ps1` were parsed with the PowerShell AST (the scripts were not executed by the scan).

- `postgres-snapshot.ps1` script parameters include `Target`. `Target` is the only `ValidateSet` parameter.
- `job-status.ps1` script parameters include `Job` and `Status`. Neither has `ValidateSet`.
- `invoke-postgres-snapshot.ps1` has no script `param` block. Dot-sourcing it brings in `job-status.ps1`'s parameters.

On parent `f75c1751021f5f6281499a32484701fd21487d96` the scan hits exactly the two locals this commit renames. On `4791d7307b24e370141b9b67ce8e8f2e8d4ed881` it hits none, across every script parameter, not only `Target`, `Job` and `Status`. Files scanned: `scripts/test-postgres-snapshot.ps1`, `scripts/test-job-status.ps1`, and `scripts/test-server2-postgres-compose.ps1` (it names the snapshot script and does not dot-source it).

`$status = Test-Path ...` in `Assert-DestUntouched` and `Assert-PublishRejected` is a helper-local. Those helpers do not dot-source. The `$target` tokens inside `Install-SnapshotFake` are bash text in a single-quoted here-string.

## Runs

Both commands were leased, native default output, `UseAppHost=false`, `TEMP=/tmp`. Each started with 1-minute load below 16. Neither script accepts `--timeout`; the plan's review budget for the snapshot harness is 18 minutes.

| Harness | Load | Result | Lease |
|---|---|---|---|
| `scripts/test-postgres-snapshot.ps1` (no `-Case`) | 10.83 | 920 passed, 0 failed, exit 0 | `cf1b50ac-7ad5-4928-9d5b-0095ca34be2e`, held 561s |
| `scripts/test-job-status.ps1` (no `-Case`) | 4.61 | 140 passed, 0 failed, exit 0 | `6215e0df-8aa5-473b-8548-e15163f1f123`, held 11s |

The snapshot summary is `CARD-1405 postgres-snapshot: 920 passed, 0 failed` and `POSTGRES SNAPSHOT TESTS EXIT CODE: 0`. That count is above the plan's master sample of 764 because this branch adds the retarget cases. `Test-PlainOneDriveLink` reached `PASS symlinked dump leaves the outside bytes`. `Test-TargetRollbackRestoresDesktop` reached `PASS TargetRollbackRestoresDesktop child gets -Target desktop` and `PASS TargetRollbackRestoresDesktop job exits 0`.

The named C# guard batch (`TestClassificationPolicy*`, `TestClassificationGuard*`, `SlowTestRegistryMerge*`, `SlowTestTripwire*`, `NativeLinuxPolicyWitnessTests*`) was not run. This diff does not touch those classes or product code. The touched tests are the two harnesses above.

`scripts/impacted-classes.ps1 -Range f75c1751..4791d730 -MergeSubset` reported `migrations=False` and no selected kind. The downgrade selector did not engage (0 callers). `scripts/check-evidence-diff.ps1` on `f75c1751..4791d730` is `commits=1 entries=0 violations=0`. On merge-base `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4..4791d730` it is `commits=22 entries=0 violations=0`. `git merge-tree --write-tree origin/master HEAD` against `f4d7830332f652376a84c42b45bc5c8d0f4fa07f` exited 0, tree `508eb64e1c46bc1c50483e6732eaca6f187ffbdc`.

No input-wait behavior changed. The plan names no PC. Mutation stays waived (operator 2026-11-12) and is not discharged. Windows PowerShell 5.1 parse of the three runner scripts remains the plan's operator window step W-1, outside this diff.

## Land

`feat/card-task-b92e5206` is `f75c1751021f5f6281499a32484701fd21487d96`. `feat/card-task-2ad4156e` is that commit plus `4791d7307b24e370141b9b67ce8e8f2e8d4ed881`. One publication of `4791d7307b24e370141b9b67ce8e8f2e8d4ed881` carries G-1, G-2, G-4 and this rename.
