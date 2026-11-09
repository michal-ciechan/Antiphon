# Why missed: temp family registry lacked the c1778-trust pty test root

One committed note for rc qualification run a6410818 (rc/2026-11-12 at
`5fef2c23b`), chunk `session-runner-all-group-4.trx`, one failure in
`Antiphon.SessionRunner.Tests.C1677TempFamilyRegistryTests`. CARD-1781.
The Windows TRX is not readable from server2. Linux at master
`4eb2e20b98f71d5c7d78145f0e21b79c1f617987` reproduced the failure.

## Failure

`/*/*/C1677TempFamilyRegistryTests/*` at that SHA: 3 executed, 2 passed, 1 failed, 0 skipped.
The only failure is `Pty_test_root_alternation_equals_known_prefixes` (V-48b).

```
failed Pty_test_root_alternation_equals_known_prefixes
      should be
      missing: c1778-trust extra:
```

`TestSessionLogRoot.KnownPrefixes` in
`tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs` gained `c1778-trust`.
`TestSessionLogRoot.Create` throws for a prefix outside that set, so the new
`RunnerChildClaudeTokenInheritanceTests` method had to add it. The runner temp sweep's
`pty-test-root` family in `src/Antiphon.SessionRunner/RunnerTempSweep.cs` is pinned to equal
that set, and the alternation was not extended. This is classification (a): a new temp
family (`antiphon-c1778-trust-<32 hex>`) that the registry does not list. It is not a stale
c487 literal, and it is not Windows-only. The files involved are byte-identical at
master `4eb2e20b9` and the rc pin `5fef2c23b`.

Fix: `a350f1c0385ac8d13ab3ff8b805f0f62989672ca` adds the exact prefix `c1778-trust` to the
alternation. The rest of the pattern stays the same: anchored, a 32-hex suffix, ending in `\z`.

## Introducing commit/card

introducing commit/card: `de3c59991297c9d49d18bba0b7ad6d25ea270156` (CARD-1778 S2), a cherry-pick of
`df1516b315860aec4cad872114ec4b6d4a0ef272` (Code task `d68d1868`). The cherry-pick is on master and on
rc/2026-11-12. It is not on rc/2026-11-10 or rc/2026-11-11, which is why earlier runs were green.

## Approving reviews

approving review(s) with scope (Full/Interim) and the selection they actually ran:

- a2845b6c (`.antiphon/task-a2845b6c.md` at `de40bdb3a`), scope Full, round 1, reviewed range
  `76a85e11a..df1516b31`. The checkpoint rows were:
  - CP-2 `/*/Antiphon.SessionRunner.Tests/ClaudeLaunchTrustSeedTests/*` 9/9
  - CP-2b `/*/Antiphon.SessionRunner.Tests/RunnerChildClaudeTokenInheritanceTests/*` 2/2
  - CP-3 `/*/Antiphon.Tests.Infrastructure/(Server2SidecarStandingAgentsTests*)|(Server2ProductionServerContractTests*)/*` 17/17
  - CP-G1 `(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)` 50/50
  - CP-G2 `(TestClassificationGuard*)|(NativeLinuxPolicyWitnessTests*)` 4/4
- The Code task d68d1868 ran the same plan rows (CP-2, CP-3 and the guard batch).

## Outside the selection

why the failing test was outside that selection (lane/category/path): session-runner lane, `Integration`, `tests/Antiphon.SessionRunner.Tests/C1677TempFamilyRegistryTests.cs`.

- The plan's S2 row did not name `PtyHostLeakSweep.cs`. Code added the one-line
  `KnownPrefixes` entry so that `TestSessionLogRoot.Create("c1778-trust")` would not throw.
- Review checked "no policy, registry, slow-test or classification file in the diff". It did
  not treat `KnownPrefixes` as a registry input, because the only link from that set to
  `RunnerTempSweep.RegisteredFamilies` is the V-48b test. Neither file names the other in a
  way a diff reader sees.
- The guard batch covers Antiphon.Tests classification only. `tests/impacted-classes.json` has
  no row for temp families, so nothing matched the diff and selected the class.

## Cheapest pre-land check

the cheapest pre-land check as a path-glob -> class-filter entry for the impacted-classes table of CARD-1782:

`tests/Antiphon.SessionRunner.Tests/PtyHostLeakSweep.cs` (the pty-test-root producer), `src/Antiphon.SessionRunner/RunnerTempSweep.cs` and the other eight producer files the family table pins -> `/*/*/C1677TempFamilyRegistryTests/*`. The eight producer files are:

- `tests/Antiphon.Tests/TestHelpers/TestClassificationPolicyTests.cs`
- `scripts/lib/c487-harness.ps1`
- `tests/Antiphon.Tests/TestHelpers/RemoteControlPtyLane.cs`
- `tests/Antiphon.Tests/Application/SessionGenerationExitTests.cs`
- `tests/Antiphon.Tests/TestHelpers/C1514Support.cs`
- `server/Application/Services/AgentTaskLandService.cs`
- `tests/Antiphon.Tests/TestHelpers/BridgeQueueHarness.cs`
- `tests/Antiphon.Tests/Scripts/RcFullTimingContractTests.cs`

The row should also use the `added` form, `"glob": "tests/**"` with the literal
`TestSessionLogRoot.Create(`, so that any test that adds a new pty test root selects the class.
The class runs in under a second (3 tests). This row is a proposal and is not in
`tests/impacted-classes.json`: a table change alters every Code and Review Final scope, so the caller decides it.

## Exemption

none
