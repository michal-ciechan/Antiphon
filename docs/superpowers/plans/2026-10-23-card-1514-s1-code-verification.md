# CARD-1514 S1 execution and verification

Original Code / landing owner: `f35fcce8` (task branch `feat/card-task-f35fcce8`).
Task base: `77eb7806d35b3b522d46e3942742e4864c688de3`, current origin/master at start.
Only S1 of [the landed plan](2026-10-23-card-1514-slow-test-speedups-plan.md)
is commissioned. S3 is landed; S2 remains separate Code work. S4/S5 remain deferred.
The explicit brief prohibits Unit-lane, full-class and assembly runs beyond these
rows. No product or runtime source, timeout, assertion or limiter changes.

## Verification design

V-1: mechanically map the original seed block, standalone rm-second-failed block,
four generic partial cases, eight receipt drifts and two replacement cases into
four methods, with 1/5/8/2 arguments. Keep every setup/action/assertion and all 32
real script invocations. Preserve the standalone journal/removal observations;
retain private fixtures and JSON reloads. Every method requires native Linux and
the strict ProcessSpawnLimit. The source ledger in external evidence checks exact
blocks after whitespace-only normalization, including assertion expressions.
No new product assertion is required for this mechanical extraction. The plan
specifies no R-1 or PC-1; a baseline cancellation is not a correctness red witness.
No timing threshold or self-comparison test is introduced. Zero-PC S1 inventory
still goes to ordinary Review then SourceLanding Mutation for missing controls.

Baseline CP-0 executes the exact old method once with a nine-minute row cap, below
the historical 585-second adapter cap and unchanged 600-second test timeout. A
censored result remains incomplete, never green. CP-10 captures the exact C1374
host method once as S2's 32-vector baseline. Candidate CP-20..24 partitions the
landed CP-2 selection into its four required methods; one build is reused within
each run, no binaries across changed source. The candidate keeps the same fixture
construction/capture mode and strict limiter. No speed-up claim is expected:
S1 exposes boundaries without reducing body work. Compare semantic counts rather
than equating one old reporting node with sixteen new nodes. Host interference,
unknown materialization/process counters and absent phase timings stay explicit.

The task-owned offline adapter consumes checkpoint TRX/phase receipts, accepts
actual expected source SHA, verifies identity/counts and computes per-method
wall span/interval union/sum/median/p95. It starts no test hosts. Preserve its hash,
argv, build provenance and raw CHECKPOINT lines externally. Validate historical
multiline parsing against Persistence 52 passes and Remote 29 passes/censoring.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---:|---|---|
| CP-0 | S0 | `tests/Antiphon.Tests -> bin-c1514-s1-before/` | old-resume-baseline | `/*/*/RemoteScriptContractTests/C1008_Recycle_resume_requires_matching_receipt` | V-1 original 16-scenario baseline | 1 passed, or explicitly censored at bound | 1 | 3 | true | n/a |
| CP-10 | S0 | CP-0 | host-census-baseline | `/*/*/RemoteScriptContractTests/C1374_Host_census_recovers_transient_reads` | S2 exact 32-vector baseline only | 1 passed, 0 failed/skipped | 1 | 3 | true | n/a |
| CP-20 | S1 | `tests/Antiphon.Tests -> bin-c1514-s1/` | seed | `/*/*/RemoteScriptContractTests/C1008_Recycle_resume_after_seed_failure` | V-1 seed | 1 passed, 0 failed/skipped | 1 | 3 | true | n/a |
| CP-21 | S1 | CP-20 | partial | `/*/*/RemoteScriptContractTests/C1008_Recycle_resume_after_partial_failure` | V-1 five partial cases | 5 passed, 0 failed/skipped | 5 | 3 | true | n/a |
| CP-22 | S1 | CP-20 | drift | `/*/*/RemoteScriptContractTests/C1008_Recycle_resume_rejects_receipt_drift` | V-1 eight drifts | 8 passed, 0 failed/skipped | 8 | 3 | true | n/a |
| CP-23 | S1 | CP-20 | replacement | `/*/*/RemoteScriptContractTests/C1008_Recycle_resume_accepts_only_recorded_replacement` | V-1 two replacement cases | 2 passed, 0 failed/skipped | 2 | 3 | true | n/a |
| CP-11 | S1R | `tests/Antiphon.Tests -> bin-c1514-s1-red/` | stale-roster-red | `/*/*/(C1008PlatformContractTests*)\|(WindowsUnitPlatformAdmissionTests*)/(C1050_Remote_entries_guard_before_work*)\|(C1210_Roster_closure_matches_source_gates*)` | introduced stale identity witness | 2 assertion failures; inspect attribution separately | 2 | 3 | true | n/a |
| CP-24 | S1 | CP-20 | platform-roster-closure | `/*/*/(C1008PlatformContractTests*)\|(WindowsUnitPlatformAdmissionTests*)/(C1050_Remote_entries_guard_before_work*)\|(C1050_Audit_rejects_empty_or_incomplete_outcomes*)` | V-1 adjacent selector/skip multiset and source guards | 2 passed, 0 failed/skipped | 2 | 3 | true | n/a |
| CP-12 | S0R | `tests/Antiphon.Tests -> bin-c1514-s1-base/` | source-closure-base-attribution | `/*/*/WindowsUnitPlatformAdmissionTests/C1210_Roster_closure_matches_source_gates` | exact base attribution for C1374 helper gate | 1 assertion failure if inherited; never passed | 1 | 3 | true | n/a |

Run CP-0,CP-10 at the committed manifest SHA, then CP-20..d at the final
committed candidate SHA, via checkpoint `run --row-timeout 9m --total-timeout 60m`.
Wait through exit 75. One normal baseline/candidate pair; no repeat after green.
One unlisted build bootstraps the tool through build-slot.ps1 into external owned
output. Two leased discovery-only invocations are permitted to establish the
actual class census before/after using the corresponding source-bound build;
these execute no tests. Discovery pass count must not drop: replace one by sixteen.

Required manual work: scenario/action/assertion ledger, unchanged remainder and
C1374 helper, limiter/native guard audit, historical pace denominator unchanged,
RC scratch cherry-pick at 5a126f6fc (RC lacks CARD-1421; S1 must not need it),
current-master merge-tree, full base..HEAD evidence-diff and task output cleanup.
Every PC-3a/b/c/d remains pending from S3; none is discharged here. V-2..5 and
CP-3..5 are outside S1, not passed. Restart: none. Ordinary Review is required
before the caller lands this original Code task; S2 follows as separate Code.

Manifest subrows CP-20..23 implement landed logical CP-2. Numeric IDs are
required for build reuse; initial alphanumeric import was refused before build/tests.

## Dependent platform roster repair

The first candidate run 20261023-092445-474e was explicitly stopped during
build, before any test host launch: dependency audit found the old identity in
C1008PlatformContractTests and WindowsUnitSkipRoster. This is an introduced
selection defect, owned here. CP-11 captures the existing exact guards red at
the committed extraction, then CP-24 runs those guards and the unchanged audit
negative controls after updating literal identities/multiplicities (1/5/8/2).
No assertion is loosened. Both additions are bounded exact selections, justified
by changed selector consumers; they replace no original semantic checkpoint.
The final candidate run includes CP-20..24, with one build. Native Windows child
audits are outside this Linux-only commission; no OS-selected production branch
changes, and no Windows pass is claimed.

CP-11 at 17a17e280: C1050 failed on the removed identity as expected.
C1210 failed first on C1374CensusProbes.Check, outside this extraction. CP-12
runs that exact method at task base 77eb7806 in a detached checkout, with the
committed manifest supplied externally. CP-24 keeps only the two direct C1050
consumers; the broader inherited closure failure, if confirmed, is a documented
non-receipt finding rather than a passing gate. Preserve its source and assertion.
