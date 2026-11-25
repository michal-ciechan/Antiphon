# Why missed: C1616 admission deadline ran on real time under a fake clock

Windows Full run `b8cbfb24` on `rc/2026-12-05` at
`2fd61b643ea4231fa732cd4969b5284d43b33f39` reported one failure in
`antiphon-all-group-112`:
`CheckpointAdmissionContractTests.C1616_admission_timeout_publishes_readable_nonexecuted_receipt`
(10.1 s). The assertion at line 453 expected `row.SlotReason` to be `build_slot_busy` and got
`slot-admission-deadline-exceeded`. The receipt's `terminalReason` was
`slot-admission-deadline-exceeded`, as expected.

This is a different method from the group-112 failure in run `acca11ba`
([live-lane callers](2026-11-24-why-missed-live-lane-callers-deleted-drivers.md)).
That repair, `afbe900a10adbf944fdc7c01142601a4ac4bc7e2`, is an ancestor of the
`b8cbfb24` pin. In `acca11ba`, the C1616 method was not among the group's failures.

## Cause

`BuildSlotClient.AcquireAsync` arms its admission deadline as
`new CancellationTokenSource(_wait, _timeProvider)`. The test set `wait: 1 s` with an injected
`clock` and `delay`, but no `timeProvider`. The deadline therefore ran on `TimeProvider.System`
while the busy loop's own clock was fake. The test needs the in-process broker's first
`409 build_slot_busy` answer within one real second. If that answer arrives later, the deadline
cancels the acquire first. `last` is then null, and the client correctly reports
`slot-admission-deadline-exceeded` as the slot reason. The Windows host was loaded by its own
live qualification run, which held the build slot.

Linux red witness: `23cdd65037ce43ac81aaf1cc0227b6e17087de2b`. It delays only the first queued
acquire by a real 1.5 s, and the exact method fails with the Windows assertion. Fix:
`3d3876410e256e59692733272da0f7dad18757c8`. The deadline now shares the
`ControlledTimeProvider` that the retry delay advances. The same slow first answer passes,
because only controlled time can expire the deadline.

## Which reason wins (contract, unchanged product)

This is a test timing assumption, not a product defect. When a slot admission times out:

- `terminalReason` (row and build) is always `slot-admission-deadline-exceeded`.
- `slotReason` is the last broker answer observed before the deadline (`build_slot_busy` or
  `build_slot_memory_floor`). If no broker answer arrived before the deadline, `slotReason` is
  `slot-admission-deadline-exceeded`. A busy reason that was never observed must not be
  reported.

The production client already follows this contract (`BuildSlotClient.cs`, deadline catch:
`SlotReason = last?.Reason ?? "slot-admission-deadline-exceeded"`). The outer
`RunBudgetClock.AcquireAsync` deadline (`slotWaitMinutes`, default 45) reports the deadline
reason. In this test it never fires.

## Introducing commit/card

introducing commit/card: `0562f6e299612501c6baa522e00f9e1317c76e3b` (CARD-1616, cherry-picked
from `794974fea3131662578320336902022e18d28461`)

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- CARD-1616 Review: the task id and selection are not recorded in the repository. The class is
  `[Category("Unit")]` under `tests/Antiphon.Tests/Checkpoints`. Every Checkpoints selection
  that ran it on an idle host would pass.

## Outside the selection

why the failing test was outside that selection (lane/category/path): it was inside the selection,
in the Unit lane, category `Unit`, at `tests/Antiphon.Tests/Checkpoints/CheckpointAdmissionContractTests.cs`.
Earlier Linux and Windows runs executed it and passed. The miss is load dependence, not
coverage. An idle in-process broker answers in milliseconds. Only a host loaded enough to delay
the first answer past one real second shows the race. That was the Windows Full run sharing the
desktop with its own live qualification run.

## Cheapest pre-land check

`tests/Antiphon.Tests/Checkpoints/CheckpointAdmissionContractTests.cs` ->
`/*/*/CheckpointAdmissionContractTests/C1616_admission_timeout_publishes_readable_nonexecuted_receipt`.
The method now carries its own slow-broker handler, so the witness no longer depends on host load.

A broader guard is a possible follow-up, not filed here: reject any test-constructed
`BuildSlotClient` that injects `clock` or `delay` with a finite `wait` but no `timeProvider`.
That shape arms a real-time deadline under a fake clock. A grep of `tests/Antiphon.Tests` at
`9f704bdb1` found no other such construction. The other short-`wait` callers already pass
`timeProvider`, and the fake-clock callers use the 45-minute default.

## Exemption

none
