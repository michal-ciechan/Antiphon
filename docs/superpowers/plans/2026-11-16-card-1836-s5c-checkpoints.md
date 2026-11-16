# CARD-1836 S5c exact partition and status

Code/landing owner d2fff5d4; base 48a4615dcb4d67033886a3e31629dc7351ed0852.
Owner: [activation plan](2026-11-12-card-1836-incremental-activation-plan.md), S5c, D-2/D-6, V-9 and CP-9.

S5c is an inert diagnostic library and two opt-in entrypoints. Full and incomplete remain defaults; the S5b release-graph fence cannot be overridden. Proposed partitions are distinct from effective Full partitions. Neither grants execution, closure, publication or readiness credit. No nightly/selector caller changes. Status reads sealed immutable revisions and starts no children. The planner reads Git/admission evidence but builds or launches nothing.

## Verification design

V-9: all four RcLedgerStatusTests methods: PartialDecisionsCannotCloseUniverse; ParameterizedMethodsAndNonTunitStayRequired; StatusCountsOriginsWithoutDoubleCharging; CrashRevisionCannotPublishClosure. Production partition/planner/revision/status functions and actual command entrypoints are exercised. Matrices cover missing/duplicate/extra cases, all/zero carried, method/chunk expansion, fresh suite obligations, ambiguous discovery, cross-suite identity, unique origin wall cost, actual results, write-once publication, torn revisions, cumulative immutable outcomes, contention, read-only status and tamper. Test-first CP-9 proves missing-feature red; final rows prove green with fresh TRX. No deliberate mutants in Code under the operator waiver.

R-1 and unchanged adjacent admission/baseline contracts: CP-95. R-2..7 and V-1..8/V-10..18 belong to other slices, never credited here. No migrations, operation labels, selector rows, public HTTP or runtime integration changes. Native S7d remains pending. Restart none; scripts/lib is in the startup census but has no startup caller.

PC-V9-partition, PC-V9-method-suite, PC-V9-origin-wall, PC-V9-revision and all variants remain pending for caller-commissioned SourceLanding Mutation; the waiver does not mark any PC passed. Candidate inventory: [S5c candidates](../mutations/2026-11-16-card-1836-s5c-candidates.md).

## Performance budget

Provisional method 60s, child 45s plus 10s joined cleanup, as in the activation plan. Harness-owned temp fixtures, no expensive services/corpus; only the revision case needs a tiny committed Git repository. Retain first green timing/phase journals; collect five comparable Code/Review/pilot observations before measured p95, with no redundant green repetitions. Serial rows under ten minutes, row deadline 570s, one build at a time; admission separate and finite 45m. Table floor 19m excludes queue/bootstrap/index regeneration. Derived checkpoint total allowance remains an importer exception under CARD-1836, not p95. CP-91/92 use separate serial invocations so Pty never overlaps Antiphon.Tests.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-9 | S5c | `tests/Antiphon.Tests -> bin-c1836-s5c-green/` | linux-plan-status | `/*/*/RcLedgerStatusTests/*` | V-9 | all four named methods, no failures/skips | 4 | 4 |
| CP-90 | S5c | CP-9 | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | policy, categories, layering, spawn census, platform admission | all named classes, no failures/skips | 79 | 4 |
| CP-93 | S5c | CP-9 | linux-index | `/*/*/DerivedImpactIndexTests/*` | index guard | all methods, no failures/skips | 27 | 5 |
| CP-94 | S5c | CP-9 | linux-envelope | `/*/*/ReleaseImpactEnvelopeTests/*` | envelope guard | all methods, no failures/skips | 4 | 1 |
| CP-95 | S5c | CP-9 | linux-adjacent | `/*/*/(RcHistoricalAdmissionTests*)\|(RcReleaseBaselineTests*)\|(RcIncrementalLedgerTests*)\|(RcLedgerDurationTests*)/*` | R-1, adjacent readers | all methods, no failures/skips | 7 | 2 |
| CP-91 | S5c | `tests/Antiphon.Agents.Pty.Tests -> bin-c1836-s5c-pty/` | linux-pty-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | both methods, no failures/skips | 2 | 1 |
| CP-92 | S5c | `tests/Antiphon.SessionRunner.Tests -> bin-c1836-s5c-runner/` | linux-runner-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | both methods, no failures/skips | 2 | 2 |

### S5c command contract and remaining native work

`rc-ledger-plan.ps1 -RequestPath <sealed-json> -RequestDigest <sha256> -RepositoryRoot <object-store> -ExpectedSha <committed-head> -PlanPath <new-external-file>` writes one diagnostic plan, never overwriting it. Request v1 contains candidate SHA/tree/platform/rcLine, a complete suites roster (tunit cases or whole-suite fresh obligations), exact proposed case decisions, sealed input/output diagnostic receipts, exclusion/contract/boundary/history digests, admission pointers and recorded sample seed/budget. Admission pointers go through the existing S5b reader; their graph remains incomplete. Missing/invalid proposals or receipts force Full; invalid discovery or moved candidate refuses. Proposed carries and wall time are explicitly unverified, separate from the effective all-executed plan. These diagnostic receipts are not a replacement for S6b actual source/build/output provenance.

`Write-RcPlanRevision <external-root> <plan-path> <plan-digest> <cumulative-results>` appends open immutable revisions under an exclusive writer lock, reads every seal back, then atomically publishes the index. Recorded outcomes cannot be removed or replaced. An orphan next revision refuses extension rather than being adopted. `rc-ledger-status.ps1 -EvidenceRoot <root> -ExpectedPlanDigest <digest> [-Json]` reads the published revision without Git/process/HTTP calls. Even all Passed results do not close qualification; S6d owns that authority.

Restart target for this task: none, because no runtime caller imports the new library. The broad OS-witness startup-path census nevertheless includes scripts/lib/rc-incremental-plan.ps1. Its exact-candidate native Windows build-driver probe and restart rehearsal remain pending under docs/release-gates.md#startup-path-rc-picks before any later rc publication/activation. This task cannot run them on the occupied desktop. S7d native launch/closure/historical admission remains separately pending, never passed here. Current production Full/Shadow/ChangeAware execution stays unchanged; this slice supplies exact diagnostic accounting, not cross-candidate pass reuse.
