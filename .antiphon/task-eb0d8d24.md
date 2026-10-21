# CARD-1407 code repair (eb0d8d24)

Verified source SHA `161a7b6fecf3db0941cf252082e97ae188442cf2` on `feat/card-task-eb0d8d24`. Landing owner is eb0d8d24. Original Code task is cfb93a14. This file is a report-only commit after that SHA and does not change the scripts the checkpoint verified.

Outcome: child helpers return only an integer exit code, maintenance enter is exclusive, and checkpoint run `20261021-172841-39a6` is green (3 rows, 0 red, exit 0).

## Fix

`Invoke-CutoverCommand` sends child stdout and stderr to the host and returns `[int]$LASTEXITCODE`. Restart, maintenance, keep-six, recover, docker, scp, and ssh use it. The user-secrets `set` pipe and the age identity stay on stdin and return a coerced int. `Invoke-CutoverPsql` still returns query text.

A marker that appears after the early check is refused before `set-apphost-maintenance.ps1` runs. `CutoverEnteredMaintenance` is set only after enter returns 0, so that refusal does not clear the marker. `-EnterIfAbsent` creates the marker with `FileMode.CreateNew` and exits 2 when the file exists. `-Clear` combined with `-EnterIfAbsent` exits 2 before any side effect. Default enter and clear stay the same for existing callers.

After desktop postgres has been stopped, old-string recovery runs `docker compose start postgres` before `restart-apphost.ps1`. A failed start skips that restart and prints: `RECOVERY: desktop postgres did not start. Run docker compose -f docker-compose.dev.yml start postgres, then restart-apphost.ps1 -ExpectedServerSha. The secret was not changed.`

W-20 uses a temporary repository. `logs/apphost.down-on-purpose` in this checkout is absent. `scripts/restart-apphost.ps1` blob `6ccaac534c47939634a2f8eddfed5229b5d69e0e` matches commit `4b44cdf90`. keep-six `-ClearHold` is unchanged.

## Commits

- `b47e0257a77f632926a2658aaf6519ea4569325c` failing witnesses
- `161a7b6fecf3db0941cf252082e97ae188442cf2` production fix

Files: `scripts/cutover-postgres-server2.ps1`, `scripts/set-apphost-maintenance.ps1`, `scripts/test-cutover-postgres-server2.ps1`, `scripts/test-set-apphost-maintenance.ps1`, and CP-3 in `docs/superpowers/plans/2026-10-21-card-1407-postgres-cutover-script-plan.md`.

No real cutover ran. Production data, volumes, the real AppHost, and the real maintenance marker were left alone.

## Unlisted red run

Once, at `b47e0257a`, before the fix. Command: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1407-red -- pwsh -NoProfile -File` the witness script. Lease `83b2b466-c4f8-42cb-9978-bbc424a14231`, slot=granted, waited=0s, maxcpucount=6, held=38s, exit 1.

W-20 passed. W-22 through W-26 and M-1 through M-4 failed. StopOnly and the restarts treated printed stdout as part of the exit code. The old-string path did not start desktop postgres. A foreign marker written between the check and enter was treated as this run's marker and cleared. `-EnterIfAbsent` was not a parameter yet (`A parameter cannot be found that matches parameter name 'EnterIfAbsent'`).

## Ordinary verification

V-1 through V-13 passed on CP-1 (W-1 through W-26, exit 0). V-5 and V-6 also passed on CP-2. That suite is T-1 through T-10 and T-12: 11 passed, 0 failed. T-7's Windows PowerShell 5.1 parse was skipped because `powershell.exe` is absent, and T-7 itself passed. There is no T-11 in `scripts/test-apphost-graceful-stop.ps1`. CP-3 passed M-1 through M-4.

The plan names no R-n and no PC-n. Mutation has no named control to discharge. The Unit lane is outside this closed list. The related Windows observer test calls `Get-ScheduledTask`, which this host does not provide, so that class was not added to a Linux row.

Evidence over `8683447c6d80b393a2b6da77aacf6ada0a1e68ee..161a7b6fecf3db0941cf252082e97ae188442cf2`: commits=9 entries=1 violations=0 exit 0. `git fetch origin master` moved `origin/master` to `5763320b383dc234a2073957bdc01cc6f790ca26`. `git merge-tree --write-tree origin/master HEAD` wrote `20f187b879829d013753842c520461a461218c8f` and exited 0. The branch was left unmerged.

The checkpoint tool was built first through `scripts/build-slot.ps1` (lease `02523aa0-9379-49ae-adb3-53b2422ed6fb`, waited=0s, held=14s, `OutputPath=bin-c1407/`, `UseAppHost=false`, `-nodeReuse:false`). The `run` was not wrapped in a second slot. `bin-c1407` directories are removed. Restart: none.

## Checkpoint lines

```
CHECKPOINT CP-2 commit=161a7b6fecf3db0941cf252082e97ae188442cf2 build=n/a filter=pwsh -NoProfile -File scripts/test-apphost-graceful-stop.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=161a7b6fecf3db0941cf252082e97ae188442cf2 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-3 commit=161a7b6fecf3db0941cf252082e97ae188442cf2 build=n/a filter=pwsh -NoProfile -File scripts/test-set-apphost-maintenance.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=161a7b6fecf3db0941cf252082e97ae188442cf2 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-1 commit=161a7b6fecf3db0941cf252082e97ae188442cf2 build=n/a filter=pwsh -NoProfile -File scripts/test-cutover-postgres-server2.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=161a7b6fecf3db0941cf252082e97ae188442cf2 sourceState=clean buildSource=notApplicable
```

Run `20261021-172841-39a6`. Manifest `.antiphon/checkpoints/20261021-172841-39a6/manifest.resolved.yaml`. Verdict GREEN. unlisted: none. wall 1m18s. rows: 3 green 0 red 0 skipped. sourceState=clean. buildSource=notApplicable.
