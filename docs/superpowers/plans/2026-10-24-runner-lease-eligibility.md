# Runner eligibility after heartbeat lease expiry

Code/landing owner: 5e38fb0f-f27f-4902-9113-b07caa20309f.
Base: ceb6b8da3b0c11d062594d84c7b98751a681e99c (origin/master).
Assigned branch: feat/card-task-5e38fb0f. Final round.

S1 commits regression tests before production changes. CP-1 is the intentional red
witness at S1. S2 restores eligibility only through a successful same-connection
catch-up, with final identity/epoch, lease, disconnect, drain/retirement and event
backlog checks. Keep the existing observer group, initial registration/catch-up,
retry delay and periodic inventory behavior. No reconnect or restart is recovery.

## Performance budget

Server application/phone-home integration scope. Antiphon.Tests references the
server and SessionRunner projects; this changes only server recovery/directory,
not runner protocol or persistence. No full assembly run. Unit is required by the
Code stage contract; the task prose's no-Unit request conflicts with that contract.
Final full affected integration classes: PhoneHomeRecoveryEligibilityTests,
PhoneHomeEventPumpTests, PhoneHomePendingInventoryTests, RunnerEligibilityObserverTests.
These cover catch-up, observer lifecycle, inventory/capacity and eligibility edges.

One build per source slice, reused by final rows. Tool bootstrap is an additional
explicitly accounted build through scripts/build-slot.ps1. Provisional estimates:
red 4m including build; final 12m including build, Unit and integrations. Existing
Unit measurement in testing-and-build.md is 70.29s for 1,992 older cases; current
case count/cost is unknown. No repeated green proofs. Collect fresh TRX method
durations and checkpoint phase/build/admission times; uninstrumented nested
HTTP/host-start/body/teardown spans remain unknown, not zero (CARD-1514).
Row cap 8m, total execution 20m, wall cap 25m; these are provisional bounded
collection limits, not measured p95. New connection-lifecycle cases have 30s test
caps, 20s cancellation, 5s local waits. Each needs a fresh socket and fake clock
because the subject is expiry/reconnection; existing DB setup remains shared by
TestDbFixture with per-test schema isolation. No AppHost or production runner.

## Verification design

V-1: expired lease -> same-epoch heartbeat -> successful catch-up restores
eligibility, accepting-new-work, catalogue capacity and observer notification;
two independent episodes on the same connection/epoch without reconnect.
V-2: wrong-epoch heartbeat cannot revive an expired lease; valid heartbeat can.
V-3: failed inventory leaves eligibility false and honors catch-up retry delay.
V-4: after inventory starts, disconnect reason, draining, retirement, backlog,
expired lease, changed registration epoch, or replacement connection refuse
recovery. Assert both eligibility and dispatch status.
R-1: full recovery eligibility class preserves initial failed/silent registration
catch-up and transcript-persistence behavior.
R-2: full event-pump and pending-inventory classes preserve event ownership and
inventory refresh behavior.
R-3: full eligibility observer class preserves notifications and lease snapshots.
R-4: whole Unit lane; fresh TRX nonzero count and complete failure reporting.
Manual: verify remote branch SHA and conflict-free merge-tree against freshly
fetched origin/master. Server activation requires caller AppHost restart after
publication; this task does not restart/deploy. No release scripts/tags.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-5e-red2/` | lease-red | `/*/*/PhoneHomeRecoveryEligibilityTests/Lease_*` | V-1,V-2,V-3,V-4 | diagnostic: guarded defect must fail | 10 | 4 | true |
| CP-2 | S2 | `tests/Antiphon.Tests -> bin-5e-final/` | recovery | `/*/*/PhoneHomeRecoveryEligibilityTests/*` | V-1,V-2,V-3,V-4,R-1 | all listed, 0 failed | 13 | 4 | true |
| CP-3 | S2 | CP-2 | observers-inventory | `/*/Antiphon.Tests.Application/(PhoneHomeEventPumpTests*)\|(PhoneHomePendingInventoryTests*)\|(RunnerEligibilityObserverTests*)/*` | R-2,R-3 | all listed, 0 failed | 3 | 3 | true |
| CP-4 | S2 | CP-2 | unit | `/*/*/*/*[Category=Unit]` | R-4 | >= 1 executed, 0 failed | 1 | 5 | true |

## Mutation handoff

PC-1: bypass successful same-connection catch-up.
PC-2 variants: remove final connection identity, epoch, fresh lease, disconnect,
drain, retirement or pending-event validation independently.
PC-3: allow a failed inventory to mark recovery or bypass retry delay.
PC-4: start a duplicate observer group after same-connection recovery.
All PCs/variants are pending for method-scoped SourceLanding Mutation, including
missing-control discovery. Code owns only ordinary regression red/green.
Next stage is ordinary Review under the Code stage contract.

Initial S1 witness at 7bb28a7a339391c9a2057e24a9fda422c57d1f6b:
10 executed, 6 passed, 4 assertion failures; build 260.97s, test wall 1.36s.
Advancing the fake clock also made periodic refresh due, allowing an unrelated
List to satisfy some negative-case request counts. Disable periodic refresh in
the new fixture and repeat CP-1 with fresh bin-5e-red2/ before the product fix.
Preserve this initial diagnostic run; no acceptance credit for its negative cases.
