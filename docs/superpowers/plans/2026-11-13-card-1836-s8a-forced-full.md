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

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-4 | S8a | `tests/Antiphon.Tests -> bin-c1836-s8a/` | linux-forced-full | `/*/*/RcForcedFullTests/*` | V-4 | all 3 named methods, 0 failed/skipped | 3 | 6 |
| CP-90 | all | `tests/Antiphon.Tests -> bin-c1836-guards/` | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)\|(DerivedImpactIndexTests*)/*` | policy, categories, spawn census, index | all listed, 0 failed/skipped | 79 | 4 |
| CP-91 | S8a | `tests/Antiphon.Agents.Pty.Tests -> bin-c1836-s8a-pty/` | linux-pty-spawn | `/*/*/ProcessSpawnLimitTests/*` | spawn-limit-rosters | both methods, 0 failed/skipped | 2 | 3 |
| CP-92 | S8a | `tests/Antiphon.SessionRunner.Tests -> bin-c1836-s8a-runner/` | linux-runner-spawn | `/*/*/ProcessSpawnLimitTests/*` | spawn-limit-rosters | both methods, 0 failed/skipped | 2 | 3 |

## S8a library API and integration boundary

`Get-RcForcedFullDecision` takes CurrentContract, BaselineContract, Evidence,
FullAnchor, an injectable DateTimeOffset UtcNow (default UTC now), and ForceFull.
It returns booleans forceFull/launchAll, ordinal-sorted exact reasons, and the
original Full anchor. It launches nothing. Pass reasons to S1's existing
ForcedFull argument; do not change that API. A false forceFull is only this
policy's decision and is not a carry certificate.

The closed input contract is antiphon-rc-input-contract-v1. Required strings are
inputMapVersion (positive integer version), sdkVersion/runtimeVersion (actual
selected semantic versions), platform (linux/windows), executionImplementation
(compiled/fallback), and lowercase SHA-256 fields policyHash, catalogHash,
selectorHash, skipResolverHash, discoveryHash, parserHash, coverageHash,
launcherHash, custodyHash, buildHash, harnessHash, inputMapHash, sdkHash and
runtimeHash. Hashes must bind complete executed closures and actual receipts.
Null, wrong types, unsupported formats and extra fields are unknown. A change
in any supported field forces Full; a future format requires reviewed support.

Evidence contains independently derived baselineState=admitted-ancestor,
admissionChainState=verified, historyState=complete,
globalOwnershipState=resolved, shadowState=clean. A discrepancy is explicit;
missing/unrecognized facts force Full. These are validator findings, never
operator overrides. Complete history may still contain open failures: S6a/S1
must keep them dirty using actual fix containment. This policy clears none.

FullAnchor contains kind=executed-full, admissionState=verified, sourceSha,
closureDigest and completedAtUtc (explicit UTC ISO timestamp with up to seven
fractional digits). Admission must have verified this original Full origin.
At seven days it expires; candidate, release and sample times are irrelevant.

`Get-RcExecutedFullAnchor` consumes an independently verified closure and
returns eligible/reasons/fullAnchor plus preserved forcedFull reasons. It
requires mode=Full, coverageComplete=true, verified validationState, nonnegative
integer counts (positive requiredCount), executedCount + expectedSkipCount =
requiredCount, zero carried/failed/unexpectedSkip counts, sourceSha,
closureDigest and a nonfuture completedAtUtc. S5b/S6d own proof of the exact
roster, current skips and provenance; counts and labels do not authenticate it.
This function persists nothing, grants no publication credit, and does not
replace Windows qualification. Both functions accept dictionaries and decoded
JSON PSCustomObjects without coercing malformed scalar values into good facts.

## Observed red cost and green execution selection

The initial full red checkpoint attempt at f0ac36192 was supported-stopped
before any tests ran (exit 130), after a verified build and substantial custody
and admission overhead. It is not assertion proof. Direct gated harness runs
then supply the three semantic red assertions. Initial direct invocations had
missing result-directory cleanup errors; retain those costs and use only the
corrected three-case driver as red proof. No source changed during any run.

For green, run the closed rows in separate checkpoint invocations as needed
(one row per invocation) to avoid projecting this expensive host's four-build
batch above 30 minutes. Every row still gets its exact isolated build/filter.
This changes batching only, not filters, assertions, repeats or deadlines.
