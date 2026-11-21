# Why missed in rc run 3528e6b1 group 92

Windows Full on `rc/2026-11-30` at
`bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0` reported three failures in
`antiphon-all-group-92.trx`. The two direct cases,
`Removed_role_slot_is_released_normally` and
`None_stops_new_leases_while_releases_continue(reconcile-pass)`,
expected a release count of 1 and received 0. The hosted-loop case
expected a `Free` slot and found it `Leased`. The dispatch brief
supplied these assertions. The native TRX and runner ledger were
unavailable from this worktree.

## Release path

The role gate makes the lease decision in
`RemoteWorkspaceService.ResolveSlotDecisionAsync`. The direct release
pass loads existing slot bindings and checks terminal status, session
state, sync debt and follow-ups. It calls the runner with the stored
slot, generation and task identity. The hosted loop gates on
`WorkspaceSlotsEnabled`, which the test sets to true. Both release
paths are independent of `WorkspaceSlotRoles`. The fixture awaits
preparation becoming idle and awaits the database update to terminal
status in an isolated database clone before reconciling. Both direct
calls returned a count within their wait bounds.

The likely Windows-specific step is the holder scan in
`RunnerWorkspaceSlotPool.SettleAsync`. It runs before Git reset or
directory cleanup. A holder or incomplete scan returns `deferred`,
keeps the runner slot `Leased` and leaves the release count at 0.
The pool's first retry is due in one minute. The hosted fixture polls
for ten seconds with its fake server clock fixed. A newly created slot
can lease before this scan, so a successful initial lease is
compatible with a deferred first release. The reconciler counts a
successful release or quarantine response. The native failure messages
establish the count and state. The missing response and ledger detail
leave the exact trigger unconfirmed.

The role-gate class, transport fixture, reconciler, release request
and runner probe/pool sources are identical at the rc tip and the
assigned master `0372c6f663cb2b3fe631f047198e626a94c9382e`. The
`git log -S` ranges for `ReconcileAsync` and the role gate show
no master-only release change.

## Linux replay

The class passed 20/20 at each pinned SHA, including the three cases
that failed on Windows. Both completed rows used one granted
checkpoint build-and-test lease, a fixture PostgreSQL database,
`ANTIPHON_LINUX_COMPILER_POLICY=0` and `--timeout 570s`.
At build launch the one-minute loads were 12.51 on master and 10.94
on rc. The builds reported zero errors and took 6m 16.21s and
6m 23.90s respectively.

The first master checkpoint attempt (`20261121-222106-3cb7`) was
canceled during its build because the launcher bypassed the PATH
wrapper that adds the TUnit timeout. It executed zero tests.
The completed rows passed the load and timeout wrapper explicitly
to the checkpoint tool.

```text
CHECKPOINT CP-1-master commit=0372c6f663cb2b3fe631f047198e626a94c9382e build=ok filter=/*/Antiphon.Tests.Application/C0824WorkspaceSlotRoleGateTests/* executed=20 passed=20 failed=0 skipped=0 trx=/work/worktrees/ws-08/.antiphon/checkpoints/CP-1-master-20261121-222536-695a/run.trx slot=granted waited=0s dirty=0 source=0372c6f663cb2b3fe631f047198e626a94c9382e sourceState=clean buildSource=verified
CHECKPOINT CP-2-rc commit=bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0 build=ok filter=/*/Antiphon.Tests.Application/C0824WorkspaceSlotRoleGateTests/* executed=20 passed=20 failed=0 skipped=0 trx=/tmp/antiphon-a5d2c107.QJYecYSU/rc/.antiphon/checkpoints/CP-2-rc-20261121-223327-5551/run.trx slot=granted waited=0s dirty=0 source=bd34a7ff6da9db1a3fa3690f824890ef9b7cc2c0 sourceState=clean buildSource=verified
```

| Test case | Master | rc tip |
|---|---|---|
| `Case_reuses_one_scratch_repository_and_spawns_no_git` | Passed, 4.637 s | Passed, 0.459 s |
| `Decision_is_recorded_before_the_mirror_is_sent` | Passed, 0.969 s | Passed, 10.193 s |
| `Flag_off_hosted_loop_sends_no_release` | Passed, 2.488 s | Passed, 5.507 s |
| `Flag_off_or_no_feature_ignores_the_list(flag-off:*)` | Passed, 0.741 s | Passed, 0.579 s |
| `Flag_off_or_no_feature_ignores_the_list(flag-off:none)` | Passed, 9.898 s | Passed, 0.762 s |
| `Flag_off_or_no_feature_ignores_the_list(flag-off:Plan)` | Passed, 0.451 s | Passed, 0.565 s |
| `Flag_off_or_no_feature_ignores_the_list(runner-lacks-feature)` | Passed, 0.967 s | Passed, 0.497 s |
| `Hot_reload_applies_to_the_next_lease_without_restart` | Passed, 2.893 s | Passed, 6.387 s |
| `Listed_role_leases_a_slot_on_the_real_runner` | Passed, 1.015 s | Passed, 0.790 s |
| `None_stops_new_leases_while_releases_continue(hosted-loop)` | Passed, 1.956 s | Passed, 1.667 s |
| `None_stops_new_leases_while_releases_continue(reconcile-pass)` | Passed, 16.232 s | Passed, 1.570 s |
| `Removed_role_slot_is_released_normally` | Passed, 1.240 s | Passed, 1.302 s |
| `Retry_keeps_the_first_decision_of_its_preparation(busy-then-none)` | Passed, 1.210 s | Passed, 0.937 s |
| `Retry_keeps_the_first_decision_of_its_preparation(legacy-then-listed)` | Passed, 1.302 s | Passed, 1.103 s |
| `Retry_keeps_the_first_decision_of_its_preparation(slot-refused-then-none)` | Passed, 15.155 s | Passed, 1.094 s |
| `Retry_keeps_the_first_decision_of_its_preparation(slot-then-none)` | Passed, 1.241 s | Passed, 1.175 s |
| `Unlisted_role_gets_the_legacy_mirror_and_never_holds(invalid)` | Passed, 0.938 s | Passed, 10.304 s |
| `Unlisted_role_gets_the_legacy_mirror_and_never_holds(mutation)` | Passed, 0.862 s | Passed, 0.875 s |
| `Unlisted_role_gets_the_legacy_mirror_and_never_holds(none)` | Passed, 0.889 s | Passed, 10.301 s |
| `Unlisted_role_gets_the_legacy_mirror_and_never_holds(role-not-listed)` | Passed, 14.814 s | Passed, 0.633 s |

## Introducing commit/card

CARD-0824 S1c introduced the role cases in
`ec01445ab2f75dacd7f76b88aac45ff3ead007ed` and the admission gate
in `2561829864d86fd8db54c44c86f772ad09489f74`. CARD-0824 S3
`95b4806f37e0b2d60d9997b53dab8d96cd948de1` is the first commit
on this history that checks scan completeness during release and
selects the native Windows probe. This is the introduction point
for the suspected Windows path.

## Approving reviews

- Review `7951bfb5`, round 1 Final, ran on Linux. CP-2 selected
  `C0824WindowsSlotHolderTests/*` and ran the incomplete-scan case.
  The real holder case skipped. A supplemental
  `C0824WorkspaceSlot*` application selection included the role
  class. Advisory A-5 identified the Windows process-exit race between
  opening the process and reading its cwd, which yields an incomplete
  scan. Advisory A-3 observed that the native lock test can defer on
  the child cwd before reaching Restart Manager.
  [Round 1 report](https://github.com/michal-ciechan/Antiphon/blob/351140f132c9620092b4fb8e38f85a97eb4911ec/docs/superpowers/reviews/2026-11-19-card-0824-s3-windows-slot-review.md)
- Review `1281fbe2`, round 2 Final, covered the separate checkout
  sentinel fix on Linux. Its listed selections were CP-4
  `C0824WindowsSlotAdmissionTests/*`, `C0824WorkspaceSlotServerTests`,
  `RemoteWorkspaceReadinessTests`, `DerivedImpactIndexTests` and
  the policy, classification, slow-registry, slow-tripwire and
  native-Linux guards.
  [Round 2 report](https://github.com/michal-ciechan/Antiphon/blob/7b22a12e5541272d78e9dc376f4af078cace8a14/docs/superpowers/reviews/2026-11-19-card-0824-s3-a1-grok-round2-review.md)

## Outside the selection

The role class is Integration in `Antiphon.Tests`. Linux exercises
the transport, Git worktree and fixture database using the `/proc`
holder probe. The S3 closed selections did not run the immediate
release cases against the Windows probe before qualification. The
Windows-specific holder witness expects a deferred release and cannot
prove that an otherwise idle native slot releases on its first pass.

## Cheapest pre-land check

The existing Windows-required impact kind maps
`tests/Antiphon.Tests/Application/C0824SlotTransportWorld.cs` to
`/*/*/C0824WorkspaceSlotRoleGateTests/*`. The same Windows-required
class edge is missing for changes to
`src/Antiphon.SessionRunner/WindowsWorkspaceSlotProcessProbe.cs` and
`src/Antiphon.SessionRunner/RunnerWorkspaceSlotPool.cs`.

The shortest native replay is the following three cases in one
gated `Antiphon.Tests` invocation with the fixture database and
`--timeout 570s`. Use a fresh native TRX at the integrated SHA
after the live qualification releases its lock.

```text
/*/Antiphon.Tests.Application/C0824WorkspaceSlotRoleGateTests/(Removed_role_slot_is_released_normally*)|(None_stops_new_leases_while_releases_continue*)
```

For another red, capture whether a release request reached the runner,
its outcome, and the slot's `ReleaseRequested`, `LingerAttempts`
and `Detail` before disposal. A `deferred` outcome with
`scan incomplete` distinguishes the probe hypothesis from missing
server eligibility and runner command errors.

## Exemption

None. The earlier failure in the same class died in scratch Git setup
before preparation. [Group 88 investigation](2026-11-08-why-missed-workspace-slot-role-gate-windows.md)
