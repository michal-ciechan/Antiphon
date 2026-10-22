# CARD-1514 slow test profile

Investigation `85ca6a8c`, 2026-10-22. Source: `ffa98a8f76588c12df6b0437d24b541762b95f09`.
Evidence checkpoint; bounded Linux profiling is pending. No product or test source changes.

The Windows group-10 watchdog interrupted continuing work, not a demonstrated repeated hang.
The prior investigation records 43 CheckpointMatrix tests in 933.338 seconds, 52
PersistenceFailure tests in 1473.781 seconds, and 47/63 Publication tests in 1144.271
seconds (one assertion failure). The slower earlier run records 1549.837 and 1938.054
seconds for the first two complete classes. These are summed test durations, not
build/slot waits. Source: [group-10 Debug](2026-10-22-rc-group-10-timeout-debug.md),
D10 line coordinates and run identities therein; [measured budget plan](../superpowers/plans/2026-10-22-card-1404-group-10-measured-budget-plan.md)
records diagnostic SHA-256 hashes. The Windows raw paths are not mounted in this runner;
Windows medians/p95 cannot be recovered from these aggregates.

The next two largest rows outside these three in `scripts/lib/nightly-class-costs.json`
are RemoteScriptContractTests (499 nodes, 4591 seconds) and
DelegationDispatchRecoveryBoundaryTests (381 nodes, 3505 seconds). Both are **pace
estimates**, not measured complete-class totals. AgentTaskLandActiveSourceClaimTests
has the next largest measured row (16 nodes, 1920 seconds). This distinction prevents
calling the five requested classes the empirically established five slowest.

`LandingSafetyHarness.InitializeAsync` (`tests/Antiphon.Tests/TestHelpers/LandingSafetyHarness.cs:41`)
creates fresh Git state, then an isolated database and seed row per test.
`LandingGitFixture.InitializeAsync` (`tests/Antiphon.Tests/TestHelpers/LandingGitFixture.cs:36`)
executes ten top-level Git commands including an independent observer clone.
`TestDbFixtureLifecycle.cs:94` serializes template-database cloning, with retry delays
only for `ObjectInUse`; `:131` drops the clone. One lazy container belongs to the
assembly, not each test (`TestDbFixture.cs:13,28`). The landing verifier is controlled
(`LandingSafetyHarness.cs:432`); it does not run builds or acquire build slots.

Current-source differences matter: PersistenceFailure moved from ProcessSpawnLimit to
GitProcessSpawnLimit after candidate `0714a9a89e4bff65aae7e0b4b94098694aeb931c`.
The schema cache also postdates that candidate. Fixture-owned Git census was already
present in that candidate; it is **not** a new explanation for a Linux/Windows difference.

## Not done, noted

No speedup implemented or designed; any fixture, clock, Git-operation, concurrency or class-split change belongs to the subsequent Plan and needs measured attribution first.

--- next stage ---
next: investigate
handoff: Complete the authorized one-build, single-class Linux profiles and separate measured body/fixture costs from historical Windows aggregates; do not touch the live Windows qualification.
artifact: docs/investigations/2026-10-22-card-1514-slow-test-profile.md
