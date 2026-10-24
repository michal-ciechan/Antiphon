# Land graph record repair (Code fdd9519d)

Base: `ceb6b8da3b0c11d062594d84c7b98751a681e99c`. Landing owner:
`fdd9519d-676b-4fb3-afbe-6958cd145434`. Caller authorized option (b).

Remove HostCleanup's project-level DefineConstants self-append. Fixed forwarding
types preserve its internal redactor namespace and delegate to the unchanged linked
implementations. No pins, allowlists, timeouts or assertions change.

The real audit `-Check` failed at the base with the precheck's disallowed
DefineConstants diagnostic. Commit source before `-Update`; only the real tool may
write the two records. Commit records before the ordinary checkpoint.

## Verification design

The caller's explicit selection is four existing acceptance tests, one test-project
build and audit `-Check`. No new test is introduced. The existing audit/registry
oracles guard the observed defect; this task adds no deliberate mutation controls.
SourceLanding Mutation remains a caller-owned follow-up, including discovery of
missing controls. No PC IDs are assigned by this brief.

- V-1: real `audit-land-verification-graph.ps1 -Update`, then `-Check` succeeds.
- V-2: `C1385_RegistryMatchesNarrowGraph` binds the embedded and stored registry to HEAD.
- V-3: `C1483_S5_CheckRequiresEvaluationRecord` validates record admission and the real tree.
- R-1: `C1385_UnrelatedScriptChangeStillSkips` preserves the narrow graph exclusion.
- R-2: `C1380_DocsOnlyRebasedLandRecordsNonCodeSkip` preserves landing acceptance.
- R-3: evidence history guard over base..HEAD and conflict-free merge-tree against fetched master.

The two build prerequisites are separately accounted utility bootstraps, not extra
test-project builds: the checkpoint tool is needed to run the table; the Release
audit evaluator is needed to regenerate the records before the test build embeds
them. Both use the host build-slot gate and isolated outputs. They execute no tests.
The checkpoint tool's own build may be reused for its subsequent invocations.
No full-assembly test run is selected. Expected cost: utility builds 3 minutes,
audit 2 minutes, one test build plus four narrow acceptance tests 12 minutes,
slot admission additional. No repeats after green. Restart: none.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | Repair | `tests/Antiphon.Tests -> bin-fdd9519d-final/` | graph-records | `/*/*/(LandNonCodeSkipTests*)\|(LandGraphEvaluationTests*)/(C1385_RegistryMatchesNarrowGraph*)\|(C1483_S5_CheckRequiresEvaluationRecord*)\|(C1385_UnrelatedScriptChangeStillSkips*)\|(C1380_DocsOnlyRebasedLandRecordsNonCodeSkip*)` | V-2 V-3 R-1 R-2 | exactly 4 passed, 0 failed/skipped | 4 | 12 |
