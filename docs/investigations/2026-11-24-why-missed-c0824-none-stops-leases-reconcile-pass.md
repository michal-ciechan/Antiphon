# Why missed in rc run acca11ba group 92

Windows Full at `6ed4cb58cd75fe386e94965165d96752452431a8` reported one failure in `antiphon-all-group-92.trx`. `None_stops_new_leases_while_releases_continue(reconcile-pass)` completed a reconciliation and got 0 releases where it expected 1. The failure text came from the dispatch brief. The native TRX, runner reply and slot ledger were unavailable on server2.

## Release path and Linux witness

The role option applies when preparing a new lease. `WorkspaceSlotReleaseService.ReconcileAsync` loads owed slot bindings, checks the terminal task, session, sync debt and follow-ups, and asks the runner to release the stored slot and generation. It counts only `released`, `already_free` and `quarantined`. `PrepareAsync` joins the preparer and `MakeTerminalAsync` awaits the database update before the test invokes reconciliation. Each case gets an isolated database and a private runner pool, socket, ledger and copy of the scratch repository. `Bound` would throw on expiry. The failing call returned a count. [Release service](../../server/Application/Services/WorkspaceSlotReleaseService.cs) · [Role gate tests](../../tests/Antiphon.Tests/Application/C0824WorkspaceSlotRoleGateTests.cs)

An incomplete holder scan defers the release before Git cleanup. The ledger retains the lease and schedules its first retry one minute later. This reproduced the assertion on server2 at `8da1af8615b70af0195442fb107d97daf650a1bb`, with the real runner returning `deferred` and the ledger showing `Leased`, `ReleaseRequested=True`, `LingerAttempts=1` and `Detail=scan incomplete`. The `hosted-loop` control received a complete, empty scan and passed. The gated build took 4m and the two-case run took 25s with 1 pass, 1 fail and 0 skips. [Runner release and retry](../../src/Antiphon.SessionRunner/RunnerWorkspaceSlotPool.cs)

The earlier native investigation captured this deferral from an exited Git process. Both the current rc pin and the assigned master base already include `21115af40541210b0704a622b2315ecc3402c19b`, which skips an exited process when the same handle proves exit. An unreadable live process or an incomplete file-lock query can still defer. The Linux witness establishes how the native failure can occur. Its specific trigger in `acca11ba` remains unconfirmed. [Earlier Windows traces](2026-11-22-slot-pool-windows-g92-g94.md)

The test fixture now supplies a complete, empty scan for the immediate-release checks. Both `reconcile-pass` and `hosted-loop` use it, as does `Removed_role_slot_is_released_normally`. The hosted case was susceptible to the same deferral through its final `Free` and released-row assertions. It polls for ten seconds while its server clock stays fixed, so it cannot observe a retry due in a minute. The tests still exercise the real server, transport, runner release, Git reset, ledger and database assertions. Native holder coverage lives in `C0824WindowsSlotHolderTests`. [Holder tests](../../tests/Antiphon.SessionRunner.Tests/C0824WindowsSlotHolderTests.cs)

## Introducing commit/card

CARD-0824 S1c introduced the role tests in `ec01445ab2f75dacd7f76b88aac45ff3ead007ed`. S3 introduced the incomplete-scan deferral and native Windows probe in `95b4806f37e0b2d60d9997b53dab8d96cd948de1`. Together they made the immediate-release assertion depend on the host-wide census.

## Approving reviews

- S3 Review `7951bfb5`, round 1 Final on Linux, ran `C0824WindowsSlotHolderTests/*` with the native lock case skipped and ran the role class through a supplemental `C0824WorkspaceSlot*` application selection. Review `1281fbe2`, round 2 Final on Linux, ran the checkout sentinel, workspace-slot server, readiness, impact-index and policy guards. [S3 review selections](2026-11-21-why-missed-rc-run-3528e6b1-group-92-slot-role-gate.md#approving-reviews)
- The later census repair's Full Review `26427a41` ran `C0824WorkspaceSlotRoleGateTests/*` on Linux and passed 20/20. The native retained-handle and file-lock methods skipped in its holder row. The review found roster and impact-index blockers, which were addressed before the rc pin. [Review report at `b2d6db8c`](https://github.com/michal-ciechan/Antiphon/blob/b2d6db8c88c6be9f4b6b93fa120868bacd237d6c/docs/superpowers/reviews/2026-11-22-slot-pool-windows-review-r1.md)

## Outside the selection

The role class is `Integration`. CARD-1856 retired the frozen Linux roster. A Linux class run reaches `/proc` and passed 20/20 in the earlier replay even when Windows reproduced both cases. The short Merge gate runs subject witnesses once and leaves stress to qualification. For the census fix the derived impact edge selected the holder test class, whose native methods skip on Linux. Windows qualification exercised the cwd and Restart Manager census under load in the immediate-release scenario. [Linux roster retirement](../docker-stack.md#server2-runner-startup-identity-files-card-0631) · [Merge verification](../orchestration-loop.md#merge-verification-is-short-2026-11-18) · [Earlier replay](2026-11-21-why-missed-rc-run-3528e6b1-group-92-slot-role-gate.md#linux-replay)

## Cheapest pre-land check

`tests/Antiphon.Tests/Application/C0824WorkspaceSlotRoleGateTests.cs` → `/*/*/C0824WorkspaceSlotRoleGateTests/*`. The narrow checkpoint below selects only the three immediate-release cases. A forced incomplete scan is sufficient to expose the old fixture assumption. A repeat or load witness is useful for the native probe itself, with an idle fixture-owned slot and concurrent short-lived processes. For a native recurrence, retain the release request and reply, scan completeness, and ledger `ReleaseRequested`, `LingerAttempts` and `Detail`. The existing derived edge from `src/Antiphon.SessionRunner/WindowsWorkspaceSlotProcessProbe.cs` selects `/*/*/C0824WindowsSlotHolderTests/*`, which is the place for that native witness once the trigger is known. [Derived impact index](../../tests/derived-impact-index.json)

## Exemption

The same incomplete-scan deferral was demonstrated for the earlier group 92 failure. This note records the recurrence with the exited-process repair already present and the fixture isolated from host census results. [Earlier why-missed note](2026-11-21-why-missed-rc-run-3528e6b1-group-92-slot-role-gate.md)

## Performance budget

The row uses one cold build and one application test host, with shared scratch Git and PostgreSQL setup and per-case isolated schemas. The red run measured 25s end to end, including fixture start and teardown, and the failed body took 6.6s. The 5-minute estimate is provisional and includes the 4-minute cold build sample. Existing test bounds stay in effect. The checkpoint receipt records slot wait and execution separately.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c0824-g92-release/` | role-gate-release | `/*/Antiphon.Tests.Application/C0824WorkspaceSlotRoleGateTests/(None_stops_new_leases_while_releases_continue*)\|(Removed_role_slot_is_released_normally*)` | Both `none` paths and removed-role reconciliation | 3 passed, 0 failed | 3 | 5 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
