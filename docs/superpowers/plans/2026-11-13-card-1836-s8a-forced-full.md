# CARD-1836 S8a: forced-Full implementation checkpoints

Original Code/landing owner: 9e251e03-b5df-4928-9293-de2d1efa860e.
Base: fcc4b428a8fa3dd43847dd73134c5b989cef0a1c.
Authority: activation plan at 2c7f3548a, D-5 and S8a.
This slice adds only rc-forced-full.ps1, its harness and RcForcedFullTests.
No production caller, launch, Full default, S1 ledger API or publication behavior changes.

V-4 covers ContractOrUnknownForcesFull, AnchorAgeCannotRenewThroughCarry and ExecutedFullCanCloseWithReasons.
R-1 through R-7 belong to other integration slices; S8a changes none of their surfaces.
V-1 through V-3 and V-5 through V-18 remain outside this unit.
No manual activation acceptance is required for this standalone unit.
No PC is defined for S8a; mutation execution is operator-waived; caller owns post-land discovery.

The red commit uses a named default-deny adapter. Witnesses must fail observable
reason/launch/closure assertions before implementation. The adapter is not a deliverable.

## Performance budget

CP-4 and CP-90 retain the parent provisional 6m/4m costs and isolated builds.
Each new harness has a 45s child deadline, 10s cleanup and a 60s TUnit budget
as specified by the parent (CARD-1836; no measured p95 yet). Retain red and green
method durations without calling them p95. Fixtures are in-memory maps with an
injected UTC clock; no DB, host, browser, network, Git fixture or expensive setup.
Checkpoint receipts retain build/slot/run phases and per-method TRX.
No selection is expected to exceed 30 minutes.

Actual base..HEAD impact selects spawn-limit-rosters and test-lane-category.
CP-90 covers their Antiphon.Tests classes. Add CP-91 and CP-92 for the two
other assemblies explicitly selected by spawn-limit-rosters (2 methods each).
Their provisional isolated build/run costs are 3 minutes each. No nightly-live-lane
glob matches: this slice does not change nightly scripts or execution policy.
DerivedImpactIndexTests is added to CP-90 to verify index freshness after adding
a script test class. The existing six policy guards remain in the same union.

### Checkpoints

| ID | After | Build | Group | Filter | Guards | Expect | Min | EstMin |
|---|---|---|---|---|---|---|---|---|
| CP-4 | S8a | `tests/Antiphon.Tests -> bin-c1836-s8a/` | linux-forced-full | `/*/*/RcForcedFullTests/*` | V-4 | all 3 named methods, 0 failed/skipped | 3 | 6 |
| CP-90 | all | `tests/Antiphon.Tests -> bin-c1836-guards/` | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)\|(DerivedImpactIndexTests*)/*` | policy, categories, spawn census, index | all listed, 0 failed/skipped | 79 | 4 |
| CP-91 | S8a | `tests/Antiphon.Agents.Pty.Tests -> bin-c1836-s8a-pty/` | linux-pty-spawn | `/*/*/ProcessSpawnLimitTests/*` | spawn-limit-rosters | both methods, 0 failed/skipped | 2 | 3 |
| CP-92 | S8a | `tests/Antiphon.SessionRunner.Tests -> bin-c1836-s8a-runner/` | linux-runner-spawn | `/*/*/ProcessSpawnLimitTests/*` | spawn-limit-rosters | both methods, 0 failed/skipped | 2 | 3 |
