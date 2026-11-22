# Native Windows workspace slot release in rc groups 92 and 94

The g92 role-switch failures reproduced on native Windows at master
`6f6a789e07f031496ae99e58ee6e563d30837a64`. The Windows cwd census
read the memory of an already-exited Git process and deferred release.
The repair verifies exit on the same process handle. The original g94
failure remains inconclusive after six passes of the affected case on
unchanged master.

## Evidence

Windows Full run `3528e6b1` at rc SHA
`bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0` recorded three g92 failures
in `antiphon-all-group-92.trx` and one g94 failure in
`antiphon-all-group-94.trx`. In g92, both direct reconciliation assertions
expected 1 and received 0, and the hosted case observed `Leased` instead
of `Free`. In g94, the dirty-slot fixture expected one quarantine entry
and found zero before it called the route.

The master replay reproduced both arms of
`None_stops_new_leases_while_releases_continue`. The direct arm returned
0 in 5.48 seconds. The hosted arm finished in 18.53 seconds with the slot
still `Leased`. `Removed_role_slot_is_released_normally` passed in that
class run and in five subsequent isolated runs. The g94 route case
passed in its class run and in five subsequent isolated runs.

A temporary diagnostic build at master reproduced both g92 assertions
again. Both censuses stopped while reading the process-parameters
pointer. In each case the process was named `git` and
`ReadProcessMemory` returned `ERROR_PARTIAL_COPY` (299). The same
open handle reported exit code 0 and a nonzero process exit time.

| Git PID | Process exit UTC | Cwd read failure UTC | Holders found | Runner reply |
| --- | --- | --- | ---: | --- |
| 115144 | 03:41:21.8331229 | 03:41:22.1792498 | 0 | `deferred`, attempt 1 |
| 110328 | 03:41:24.4157097 | 03:41:24.4262200 | 0 | `deferred`, attempt 1 |

Both deferrals persisted `Leased`, `ReleaseRequested=True`,
`LingerAttempts=1`, and `Detail=scan incomplete`. This path returns
before the file-lock scan and any filesystem cleanup. The trace gives
the process names and exit state. Their parent process was unobserved.
The g94 TRX has no corresponding scan or runner response.

## Repair

`WindowsWorkspaceSlotProcessProbe` now requests a synchronizable process
handle and retains the original read access as a fallback. After a
failed cwd read, a zero-time wait can skip that process only when the
same handle is signaled. A live process whose cwd is unreadable still
makes the scan incomplete. A genuine holder still defers, and the
existing one-, two-, and four-minute retry sequence quarantines a
directory whose holder persists.

The native witness starts and joins its own child while keeping the
handle open. It asserts that the census can continue after the child's
cwd memory has gone, and failed on the pre-fix commit. The portable
control injects a persistent cwd holder, advances the fixture clock
through the retry budget, and requires an intact quarantined directory
and a free replacement with its release request cleared. The native
Windows handle witness and the portable holder control ran on Windows
in this task. The earlier [Linux replay](2026-11-21-why-missed-rc-run-3528e6b1-group-92-slot-role-gate.md#linux-replay)
had passed 20/20 on master at `0372c6f663cb2b3fe631f047198e626a94c9382e` and on the rc SHA.

## Verification

All builds and runs used `scripts/build-slot.ps1`. Each test selection
ran in its own foreground process using native build outputs and
`--timeout 570s`. The integration fixture used Docker Desktop's
local Postgres container and a schema cache inside this worktree.
Baked image creation was disabled for these runs.

The first g92 launch after building supplied an explicit Docker
endpoint that Testcontainers rejected during assembly initialization.
It ran zero test bodies. The retry used normal Docker discovery, and
the counts below are from the admitted test runs.

| Source | Selection | Passed | Failed | Skipped |
| --- | --- | ---: | ---: | ---: |
| Master | `C0824WorkspaceSlotRoleGateTests/*` | 18 | 2 | 0 |
| Master | `C1960QuarantineReleaseRouteTests/*` | 14 | 0 | 0 |
| Master | Removed-role case, five isolated runs | 5 | 0 | 0 |
| Master | g94 end-to-end route case, five isolated runs | 5 | 0 | 0 |
| Master with temporary diagnostics | `C0824WorkspaceSlotRoleGateTests/*` | 18 | 2 | 0 |
| `f06e42737c556cbedebb3480b77c150164980a8f` | Exited-process regression witness | 0 | 1 | 0 |
| `7f34808c72ddb5f59a923313b89ab58e21c0f033` | `C0824WindowsSlotHolderTests/*` | 4 | 0 | 0 |
| `7f34808c72ddb5f59a923313b89ab58e21c0f033` | `C0824WorkspaceSlotRoleGateTests/*` | 20 | 0 | 0 |
| `7f34808c72ddb5f59a923313b89ab58e21c0f033` | `C1960QuarantineReleaseRouteTests/*` | 14 | 0 | 0 |

All five gated builds completed with zero errors. The initial application build
took 12m 12s. The diagnostic application build took 4m 7s, the pre-fix and
post-fix runner builds took 1m 40s and 31s, and the final application build
took 5m 52s. The post-fix runs held their leases for 21s, 51s, and 36s
respectively.

Run the fixed selections from a native Windows worktree with local Docker.
Each build or run takes its own slot.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label slot-holder-build -- dotnet build tests/Antiphon.SessionRunner.Tests --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label slot-holder-run -- dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build -- --treenode-filter '/*/*/C0824WindowsSlotHolderTests/*' --timeout 570s
pwsh -NoProfile -File scripts/build-slot.ps1 -Label slot-app-build -- dotnet build tests/Antiphon.Tests --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label slot-g92 -- dotnet run --project tests/Antiphon.Tests --no-build -- --treenode-filter '/*/*/C0824WorkspaceSlotRoleGateTests/*' --timeout 570s
pwsh -NoProfile -File scripts/build-slot.ps1 -Label slot-g94 -- dotnet run --project tests/Antiphon.Tests --no-build -- --treenode-filter '/*/*/C1960QuarantineReleaseRouteTests/*' --timeout 570s
```

Native logs and TRX are under `.antiphon/slot-pool-debug/` in the task
worktree. The unedited diagnostic trace is `diagnostic-g92-scan.log`.
The instrumented source was restored before the regression test and
repair commits.
