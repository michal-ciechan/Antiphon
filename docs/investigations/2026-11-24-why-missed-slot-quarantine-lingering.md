# Why settled tasks left server2 workspace directories quarantined

Re-homing freed the logical slot and returned `quarantined`. The server treated
that reply as final for the lease and stopped requesting release. The old locked
directory still counted against pool admission. Runner maintenance retried
deferred leases and retired idle slots, with no path back to the old directory
after a holder exited. With the configured maximum of four open quarantines,
five open directories refused new leases with `workspace_slot_pool_degraded`.

## Incident evidence

The November 23 server2 ledger records eight `custody/lingering` directories
closed by operator `Pushed` disposition in two batches. Six recorded `sleep`
working-directory holders. The other two recorded `pwsh` and `node`.

| UTC release window | Task | Holder executable | Terminal status from the board API |
| --- | --- | --- | --- |
| 04:02–04:03 | `bda68f67` | `sleep` | Failed |
| 04:02–04:03 | `b668bdc3` | `sleep` | Succeeded |
| 04:02–04:03 | `62cec2de` | `pwsh` | Succeeded |
| 04:02–04:03 | `d4e9f133` | `node` | Succeeded |
| 10:37 | `1658d145`, `45290bd6`, `a2668d12`, `f5d4eda2` | `sleep` | Unqueried |

The ledger still showed directory `b8c1e6c1e13a` open for unpublished work.
Directory `5c361996e203` was also open. It recorded two `sleep` cwd holders
after Review `8f467d8d` succeeded and predates the settlement signal. The
ledger records PIDs, executable names and holder kinds. Parentage and launch
ownership for the historical holders were unavailable. The process names
alone cannot establish whether the children came from builds, tests or
session commands. The earlier [Windows g92 investigation](2026-11-22-slot-pool-windows-g92-g94.md)
documented incomplete scans after an exited Git process. The server2 entries
above name concrete cwd holders.

Coverage in [C0824WorkspaceSlotProcessScanTests](../../tests/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotProcessScanTests.cs)
ended at quarantine and re-home for a persistent holder, or at normal release
for a holder that exited during the retry window. The disposition tests in
[C0824WorkspaceSlotRehomeTests](../../tests/Antiphon.SessionRunner.Tests/C0824WorkspaceSlotRehomeTests.cs)
invoked operator release directly. The missing transition was maintenance
closing the old quarantine after the lease had already settled.

## Repair and operation

The terminal-task reconciler supplies settlement authority for the exact
task, slot and lease generation. An omitted signal keeps the operator path
for older ledger entries and nonterminal drain releases. Runner maintenance
checks up to four authorized lingering quarantines per pass, serving the
oldest unchecked entries first. Maintenance runs on the first slot operation
after startup and at most once every 15 minutes on subsequent slot operations,
including dispatch retries.

The runner proves directory and lease identity, checks ownership and live
processes, and refreshes origin before verifying the quarantined HEAD, current
HEAD, task branch and reflog. It checks tree cleanliness and Git operation
markers, retains task evidence, and records removal intent before deleting the
registered worktree. Confirmed removals close with disposition `Pushed` and
audit key `workspace_slot_quarantine_auto_released`. Open entries record the
latest reason in `outcome`, such as `holders`, `origin_unavailable`,
`unpushed_or_history_unknown` or `dirty_or_status_unknown`. Runner logs
carry the directory, slot and task IDs, plus the reason. Degraded admission
logs the open count and configured maximum.

This change addresses automatic release and runner diagnostics. Attention-feed
and pipeline presentation, attribution and reaping of the original children,
and recovery of pre-upgrade entries are outside this change. Raising the limit
would buy capacity at the cost of retaining more directories. One entry in
the first incident was approximately 12.8 GB, so this repair uses the existing
limit.

## Verification

The Linux witness uses real local Git repositories, a controlled holder probe,
and a fixture clock. It reopens the pool from disk to verify that settlement
authority survives a restart. It exercises a holder, an unpublished commit,
an untracked file, an origin fetch failure despite cached refs, an authorized
automatic removal, and a legacy hold with no terminal authorization.

| Source | Selected method | Passed | Failed | Skipped | Test body |
| --- | --- | ---: | ---: | ---: | ---: |
| `fbf54dc085a8b5873b5673cd32513df6755ee628` | `C2061_SettledLingeringQuarantineReleasesOnlyWhenPublishedAndClean` | 0 | 1 | 0 | 0.649 s |
| `b7628ae36423490ed91a8d11a9599bc7ab086b64` | Same method | 1 | 0 | 0 | 1.267 s |

The red assertion expected `IsOpen == false` after the clean, published
directory lost its holder. It received `true`. All runs acquired a build slot.
The red run used an implicit build and the wrapper reported that its CPU grant
could only be applied to an explicit build. The initial fix compiled at
`8e446c6dd993a637ee6a8d10ade5ca0c05e0927b` before the Git-failure refinement.
The final explicit runner build took 73 seconds of leased time with zero
errors. The green test run held its lease for four seconds. The server
compile at the same source took 275 seconds of leased time with zero errors.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c2061-build -- dotnet build tests/Antiphon.SessionRunner.Tests --nologo -v:q
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c2061-test -- dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build -- --treenode-filter '/*/*/C0824WorkspaceSlotRehomeTests/C2061_SettledLingeringQuarantineReleasesOnlyWhenPublishedAndClean' --timeout 120s --report-trx
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c2061-server-compile -- dotnet build server/Antiphon.Server.csproj --nologo -v:q
```

TRX evidence is under the runner test project's native Debug
`net9.0/TestResults` directory. The selected run did not exercise native
Windows process scanning or deploy the updated runner.
