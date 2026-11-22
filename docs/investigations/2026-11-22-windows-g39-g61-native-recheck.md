# Native Windows recheck of rc run 3528e6b1 groups 39 and 61

Both g61 cases passed 3/3 on master with the alias-schema fix present. The g39
redirect passed 2/3 on master and 3/3 after two test-fixture repairs. All replays
used native Windows test processes and fixture PostgreSQL. The rc run remains
the historical comparison because g39 reproduced on master.

## Exact selections and results

The clean master pin was `70c13dfe001c5df6c2108000584da6151ce42e43`. It contains
`1cdc778c13c445543e18ca4048d4806d7b1f1994`, which skips recurring alias-column
DDL once the column is visible. The Windows rc pin
`bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0` predates that change.
[Original failure analysis](2026-11-22-rc-run-3528e6b1-final-analysis.md)

| Exact case | Master TRX results | Final redirect TRX results |
| --- | --- | --- |
| `C0824RefOnlyContinuationTests.Runner_redirect_requires_old_custody_and_rejects_desktop` | 2 passed, 1 failed at target slot release | 3 passed, 0 failed (59.979 s, 64.985 s, 61.571 s) |
| `BootStallDetectionTests.C1151_Boot_branch_statement_counts(identity-changed-before-event, 23, 1)` | 3 passed, 0 failed | Master coverage |
| `BootStallDetectionTests.C1151_Warning_writer_revalidates_the_episode_identity(concurrent-writers)` | 3 passed, 0 failed | Master coverage |

The g61 identity case recorded 23 SQL statements and one transcript pull on
all three attempts, and its attempt-increment assertion passed. The
concurrent-writers case reached the two-writer barrier and recorded one
warning each time. These cases are covered by the master fix in this native
recheck. The rc evidence shows a database timeout and a barrier failure.
Attribution to the alias lock remains an inference.

## Redirect failure and repair

The pristine master red reached the redirect target and then failed because
target-slot cleanup returned `deferred`. A temporary diagnostics-only patch
reproduced the failure in the `unknown` variant (2 passed, 1 failed across
three attempts). The injected `slot_unknown` kept the task on the old runner
with a `workspace_slot_release_pending` Held event. After the injected fault
was cleared, the old runner returned `released`, its slot became `Free`, and
the task rebound to the target. The target pool then recorded an incomplete
scan on its own slot.

```text
G39-DIAG variant=unknown phase=recovery old-result=released old-error= slot=ws-01/3 state=Free requested=False attempts=0 detail=
G39-DIAG variant=unknown target-result=deferred slot=ws-01/3 state=Leased requested=True attempts=1 detail=scan incomplete
```

The fixture instantiates both slot pools in the native test host. The pool
selects the Windows holder probe from the actual OS, independent of the
fixture runner ID. `RunnerWorkspaceSlotPool.SettleAsync` treats an incomplete
holder scan as lingering, leaves the slot leased, and schedules a backoff.
The diagnostic reached that branch during target cleanup. The original rc
TRX failed earlier during old-runner recovery. Its warning describes the
injected unknown reply, and the outcome of the subsequent native scan remains
unresolved. The exact Windows call that made the diagnostic scan incomplete
also remains unresolved.

- `b7c6e527d0f9c8dd5af23bde4457c9a01f76ef1b` supplies a holder-free result only
  for the redirect witness, through the pool's existing process-probe seam.
  The witness still drives the real old and target runner pools, WebSocket
  transport, ledger, Git refs and all custody assertions.
- The first repair replay had 2 passes and one 120-second TUnit timeout.
  The timeout stack reached `RunnerSeatReleaseFixture.DisposeAsync` while
  dropping a desktop-case PostgreSQL clone.
  `59a063e2efd05efe61cf1e42f2ec8f18bfbdf501` shares one seeded database and
  scratch Git source across the three desktop cases. Each dispatch has to
  emit a fresh `ref_only_requires_remote_runner` Held event. The existing
  test timeout still applies. The final code passed 3/3.

### Why missed

CARD-0824 S3 `95b4806f37e0b2d60d9997b53dab8d96cd948de1` brought the
platform-selected holder census into this fixture's release path. Earlier
Linux replays of g39 passed at both pins. The g92 investigation identifies
this mechanism as the leading hypothesis for that group and records the
Linux review selections and the Windows first-pass release coverage gap.
[Shared why-missed analysis](2026-11-21-why-missed-rc-run-3528e6b1-group-92-slot-role-gate.md)

## Reproduction and retained evidence

The host reported Windows 10.0.19045 x64, .NET runtime 9.0.23 and SDK 10.0.300.
The initial master build took 11m 03s. The diagnostic, first fix and final
builds took 3m 42s, 4m 44s and 4m 24s respectively. Each build used a granted
host slot and the project's native output directory. An initial command
exited with MSB1041 before build because PowerShell split the unquoted
`-nodeReuse:false` argument. Quoting it corrected the invocation.

Each checkpoint command row ran `dotnet run --no-build` with `--timeout 570s`,
`--maximum-parallel-tests 1` and a fresh TRX. The wrapper checked the HEAD and
the single expected test name. For g61 the UIDs came from the original rc
diagnostic, so neighbouring argument cases stayed outside the selection.

| Case | Selector |
| --- | --- |
| Redirect | `--treenode-filter "/*/Antiphon.Tests.Application/C0824RefOnlyContinuationTests/Runner_redirect_requires_old_custody_and_rejects_desktop"` |
| Boot identity | `--filter-uid "Antiphon.Tests.Application.BootStallDetectionTests.1.1.C1151_Boot_branch_statement_counts(System.String, System.Int32, System.Int32).5.1.0"` |
| Boot barrier | `--filter-uid "Antiphon.Tests.Application.BootStallDetectionTests.1.1.C1151_Warning_writer_revalidates_the_episode_identity(System.String).5.1.0"` |

The task worktree retains the plans, wrapper and per-attempt TRX under
`.antiphon/windows-g39-g61-master/`. Checkpoint reports under
`.antiphon/checkpoints/` are `20261122-035442-404f` (clean master),
`20261122-040803-976c` (temporary diagnostic),
`20261122-041913-e8b7` (holder-seam repair) and
`20261122-043353-2456` (final code). The witness was restored to clean master
before the first fix. The intermediate timeout is `redirect-22/result.trx`,
and the final TRX files are in `redirect-31/`, `redirect-32/` and
`redirect-33/`.

At the selected source SHA, build and pass a copy of the corresponding plan
with unused attempt numbers to the checkpoint tool.

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label windows-g39-g61-build -- dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj '-nodeReuse:false' --nologo
dotnet tools/Antiphon.Checkpoints/bin/Debug/net9.0/Antiphon.Checkpoints.dll run --plan $plan --serial --expected-source-sha $sha --max-wait 570s
```
