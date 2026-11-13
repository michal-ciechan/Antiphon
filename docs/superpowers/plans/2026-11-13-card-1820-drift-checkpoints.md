# CARD-1820 S1R6D: algorithm source-drift witness

S1R6D is the bounded S1R6 prerequisite selected after S1R3E. The upstream
[selector plan](2026-11-12-card-1820-selector-v2-trustworthy-for-skipping-plan.md)
orders full S1R6 after S1R3-S1R5. This base has S1R3E but no S1R4/S1R5 modules;
therefore this task implements the explicitly requested CARD-1866 A-1/M7 drift
witness and A-3 fixture cleanup, without claiming full pinned composition.
CARD-1877 A-1/A-3 belongs to task 46940554. No closure edge is subtracted.
Ordinary selection remains byte-identical and unknown release evidence stays Full.

The invariant is that each currently admitted algorithm file's **raw disk bytes**
match its committed blob before any algorithm identity is published. Git clean
filters/text normalization must not hide drift. The admitted inventory remains the
existing two scripts; this does not claim the future graph/evaluator closure is
bound. CARD-1866 A-2 candidate content/authenticity and P2 remain pending.

## Verification design

V-6D: ReleaseImpactEnvelopeTests.AlgorithmSourceDriftIsRefused invokes private,
committed copies of the real CLI and module. Four variants change each algorithm
file, with either an ordinary comment edit or CRLF-only drift concealed by text
attributes. Each variant first verifies clean incomplete/Full output, then asserts
exit 2, structured incomplete/Full refusal, the source-drift reason and no digest.
The CRLF variants are the ordinary red-first regression; no deliberate mutant is
executed. AlgorithmDigestBindsCommittedBlobs independently constructs the current
inventory digest from fixture Git blobs and checks a committed algorithm change
changes the digest while baseline/candidate remain fixed. FixtureCleansUpAfterFailure
asserts the owned temp repository disappears when its body throws. All existing
four V-1 envelope scenarios now dispose their fixtures, including partial setup.

V-1 is the existing four-method envelope contract. V-2/V-3/V-3E are named adjacent
relation/project/evaluator classes; they retain qualified, uncapped identities and
conservative closure. R-2/R-3 retain the upstream S1R6 hand/index rows verbatim.
The guard union covers policy, mechanical contract, platform, layering and the
changed-glob test-lane-category row. ProcessSpawnLimitTests covers the added
process-spawning arguments and existing assembly-local limiter.
Full V-6 (DeletedDependencyUsesOldGraph, NewCandidateClassUsesNewGraph,
CompleteUnionHasReasons, UnknownNeverYieldsCarry) remains deferred to full S1R6.
Other upstream V/R IDs remain inherited or deferred, reported individually.
Native W1/R-5 is pending and orchestrator-owned. The scripts/lib path also
retains the native Windows pre-rc probe/restore rehearsal obligation in
docs/release-gates.md#startup-path-rc-picks at the eventual exact integration
candidate. No host action is authorized by this test receipt. Restart: none.

No PCs/variants are declared as executed. All PC discovery and deliberate
red/restore/green cycles remain pending for SourceLanding Mutation, under the
operator's 2026-11-12 execution waiver; ordinary reds do not discharge PCs.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-108 | S1R6D | `tests/Antiphon.Tests -> bin-c1820-drift-final/` | drift-envelope | `/*/*/ReleaseImpactEnvelopeTests/*` | V-1, V-6D, cleanup | ten argument-expanded results, zero failed/skipped after repair | 10 | 3 | true |
| CP-8 | S1R6D | CP-108 | hand-contracts | `/*/*/ImpactedClassesSelectorTests/(contract_snapshot_ranges_select_contract_snapshot_tests*)\|(label_remote_budget_and_checkpoint_ranges_select_their_rows*)\|(malformed_table_is_refused*)\|(C1843_mirror_test_paths_select_their_own_project*)` | R-2 | all named methods and arguments, zero failed/skipped | 4 | 3 | true |
| CP-9 | S1R6D | CP-108 | legacy-index | `/*/*/DerivedImpactIndexTests/(migration_only_diff_keeps_hand_classes_and_the_derive_set*)\|(merge_subset_matches_the_hand_selector*)\|(committed_index_matches_the_builder*)\|(windows_only_classes_stay_out_of_linux_filters*)\|(linux_only_methods_stay_off_the_windows_filter*)` | R-3 | five methods, zero failed/skipped | 5 | 4 | true |
| CP-101 | S1R6D | CP-108 | final-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/*` | policy/mechanical/platform/layering | all nine classes, zero failed/skipped | 78 | 4 | true |
| CP-104 | S1R6D | CP-108 | spawn-limiter | `/*/*/ProcessSpawnLimitTests/*` | process limiter census | all cases, zero failed/skipped | 57 | 1 | true |
| CP-107 | S1R6D | CP-108 | relation | `/*/*/ReleaseImpactRelationTests/*` | V-2 | four methods, zero failed/skipped | 4 | 1 | true |
| CP-4 | S1R6D | CP-108 | project | `/*/*/ReleaseImpactProjectTests/*` | V-3 | four methods, zero failed/skipped | 4 | 1 | true |
| CP-106 | S1R6D | CP-108 | evaluator | `/*/*/ReleaseImpactEvaluationTests/*` | V-3E | four methods, zero failed/skipped | 4 | 3 | true |

## Performance budget

20 minutes provisional selection cost plus one cold isolated build (prior S1R3E
build observations 291-426 seconds). Retain the existing 20-minute row/build
bound with explicit `--row-timeout 20m`. Derived execution allowance is 50m, not
a prediction; expected whole run stays below 30m, otherwise caller owes Debug.
Admission is separately bounded at 45m. No new timeout or relaxed assertion.
Red: CP-108 at the committed pre-fix source. The first run (14e283a8b)
had 8 passed / 2 failed at an incorrect clean-status setup assertion, not the
production guard. Correct that fixture to prove filtered-hash equality and raw-hash
inequality directly. That corrected run at 5162524e7 reproduced two actual
production assertion failures: exit 0 instead of 2 on both CRLF variants.
The original production module is byte-identical to the task base in both red
commits. Green: all rows once, fresh
owned output/evidence. No repetitions after green (cap 3 normal + 2 loaded).

Declared preparation outside checkpoint rows: leased native checkpoint-tool
bootstrap; leased native DerivedImpactIndex driver build before leased owner
regeneration with -NoBuild. Each driver gets its own slot, native default output,
UseAppHost=false. Index regeneration is committed before green checkpoints.
No AppHost, DB, Docker, migration, external executor or live qualification work.
Each test owns one temp repository and cleans it on normal/exceptional exit;
setup failures also dispose. Child Git is bounded 60s, selector 120s, output drain
10s, teardown/process reap 30s. Emit per-child and cleanup phase times; retain
TRX per-test wall times and checkpoint build/admission times under ignored
.antiphon/checkpoints and .antiphon/selector-evidence/2187186f. Nested filesystem
IO is unknown/remainder, never fabricated zero. Samples/p95 remain provisional
under CARD-1820/CARD-1404; use subsequent ordinary observations, no sample-only runs.

Manual acceptance: full base..HEAD evidence-diff; fresh fetched-master merge-tree;
changed-glob impacted-table census (including added-literal rows); OS witness
inventory/lint; ordinary selector and conservative graph blob identity to base.
No migrations or model changes, so downgrade/model execution is not applicable.
Native Windows behavior/proof remains pending for the orchestrator, not credited
by Linux argument variants. Unit lane remains nightly/rc-owned.
