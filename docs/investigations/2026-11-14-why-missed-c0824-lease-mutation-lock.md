# CARD-1926: stale C0824 lease mutation-lock witness

The live Windows run `34d41b7e`, `session-runner/all-group-2`, failed
`C0824WorkspaceSlotCustodyTests.C0824_LeaseWaitsForTheDispatcherMutationLock`.
The product behavior is intentional: CARD-1892 removed Input and ConditionalInput
from `PhoneHomeCommandDispatcher.MutateAsync`; their existing per-session input
gate remains responsible for input serialization. WorkspaceMirror still takes
the dispatcher mutation semaphore. The fixture incorrectly treated a held Input
as proof that semaphore was held.

## Why the earlier checks missed it

CARD-1892 round 2's CP-1 selected `C1677RetentionDispatchTests`,
`PhoneHomeCommandDispatcherTests` and `C1722GitTimeoutBudgetTests`, but not the
slot-custody class. Round 3 retained that runner evidence and selected server-side
attention/recovery classes. Its Reviews therefore never executed this dependent
runner witness. See the [parent selection](../superpowers/plans/2026-11-13-card-1892-input-uncertainty.md),
[round-3 selection](../superpowers/plans/2026-11-13-card-1892-round3.md) and
[round-3 evidence](../superpowers/plans/2026-11-13-card-1892-round3-evidence.md).

This was a cross-project selection gap: a SessionRunner-side test depends on the
lock behavior changed in the server delivery work. The CARD-1892 sweep on
`origin/feat/card-task-15406ecc` measured 44 reverse consumers of
`src/Antiphon.SessionRunner/PhoneHomeCommandDispatcher.cs`, with 19 beyond the
25-class cap. The committed index at both the round-3 tip `15afa484617287e68cbbd7e2ab15044305f53f80`
and this repair's base already retains `C0824WorkspaceSlotCustodyTests`.
Consequently the cap itself does **not** explain this particular omission:
the named checkpoint execution failed to carry this dependent class through.

The cheapest pre-land check was the single lease-lock method, followed by the
whole custody class when changing the dispatcher lock owners. A hand-maintained
impacted-classes row for the dispatcher path should select the custody class
even for Merge's hand-only selection, independent of derived ranking.

## Baseline proof before repair

Assigned branch `feat/card-task-0055a160` started clean at current origin/master
`21c9f51b4a01c3bd07547c46a5e6da4e58e6c243`. The native-output SessionRunner build
passed (193 warnings, zero errors; build 45.52 s, slot wait 60 s).

The exact method ran alone on that unmodified base: **1 executed, 0 passed,
1 failed, 0 skipped**, exit 2, TUnit wall 1.798 s, test 0.778 s; slot wait 90 s.
It failed at the required assertion:

```text
entered.Task.IsCompleted should be False but was True
the lease entered the pool while the mutation lock was held
```

Command (each build/run takes a separate host slot):

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1926-base-build -- dotnet build tests/Antiphon.SessionRunner.Tests --nologo -p:UseAppHost=false
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1926-base-red -- dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build -p:UseAppHost=false -- --treenode-filter '/*/*/C0824WorkspaceSlotCustodyTests/C0824_LeaseWaitsForTheDispatcherMutationLock' --report-trx --report-trx-filename base-red.trx --results-directory .antiphon/task-0055a160/base-red
```

Ignored evidence: `.antiphon/task-0055a160/`, including `base-build.log`,
`base-red.log` and `base-red/base-red.trx`. Initial `/proc/loadavg`:
`9.08 11.05 16.81 9/3949 12560`; red admission sample:
`21.25 17.18 18.00 6/4067 18692`. TRX retains individual duration and membership;
unrecorded fixture/external-call phases remain unknown.

## Repair and validation

Pending execution at this baseline-evidence commit. Keep every serialization
assertion and existing timeout. Hold a genuine non-slot WorkspaceMirror at the
existing workspace-admission hook, then release/join both fixture operations
even after assertion failure. Production locking must remain unchanged.
