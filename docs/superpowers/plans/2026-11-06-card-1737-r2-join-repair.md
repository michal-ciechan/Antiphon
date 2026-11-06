# CARD-1737 R2 B-2: join release tick before teardown

Repair Code task ef4e959b; original Code/landing owner
`edd7cc0a-0c0d-4899-9dde-884c9f289474`. Subject base
`90433a94f40bf13e0af25b5d8c76320111da669f`. Production remains unchanged.
Review round 2 examines only the delta from that subject. Restart: none.

V-JOIN: a real scoped DesktopLegacyReleasePolicyService tick reaches its
before:legacy-candidate boundary on an eligible real Git/Postgres world. Hold
that boundary until its real token is canceled or an outer rescue signal fires.
Exercise caller cancellation, assertion failure, exception and early successful
return. Before actual world disposal the tick must have completed; its boundary
finally must see the repository still present. After disposal the repository
must be absent. A rescue finally releases and joins even the intentionally red
baseline; no orphan is credited as evidence. The first test commit routes the
three original WaitAsync callers through an equivalent local method with the
same CancellationToken.None behavior, so it observes the reviewed defect.

The fix owns a linked cancellation source and actual task, forwards the source
to the real policy in its own async service scope, and cancels/joins in both
TickAsync finally and DisposeAsync before hooks, modes or world cleanup. A
10-second join deadline fails closed: an unjoined task prevents world disposal.
Canceled diagnostic capture cannot replace the original cancellation. Keep all
existing assertions and 40/60-second witness deadlines unchanged.

Ordinary V-1..V-7 and R-1..R-3 retain their exact frozen definitions in
[the R2 design](../testdesign/2026-11-06-card-1737-r2-testdesign.md).
Whole Unit `UNIT-FINAL` / Review B-1 remains owed under CARD-1720; the brief
expressly prohibits running it here. Targeted completion is not Final/Full.
Native Windows N1 and R3/R4/U8/O-1/U10/O-2 remain separately owned.

## Performance budget

One isolated build per committed proof group; one optional gated native tool
bootstrap because this worktree has no checkpoint binary. No full assembly run.
CP-4's bounded eleven full classes cover tick ownership, classification/policy,
live-lane wrapper, mechanical and platform/layer boundaries. No unbounded class
closure is inferred. Prior Review measured cold build 646s, CP-1 31s, CP-2 29s,
CP-3 84s, full classes 128s; allow 28 minutes build and 6 minutes per row,
10 minutes admission per acquisition. Total selected expected 15-25 minutes
per group; total charged cap 52 minutes / hard wall 102 minutes as in Review.
These provisional ceilings are inherited, not measured p95; CARD-1737 owns the
small sample. New V-JOIN uses the existing 40-second new-witness limit, 10-second
boundary/rescue/join waits, no sleep/retry. Shared DB template/model and Git seed
remain run-shared; worlds use isolated stores. Retain timing JSONL and fresh TRX
with exact method/argument roster; missing phase attribution stays unknown.
Mechanical script is a separate gated ordinary harness (38 rows), not a mutant.

### Checkpoints

totalTimeoutMinutes: 52
totalTimeoutOverrideReason: Same bounded build/row ceilings as Review; estimates include shared cold build once.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-0 | RED | tests/Antiphon.Tests -> bin-c1737-join-red/ | join-regression-red | /*/*/C1708ReflogSalvageTests/C1737_NoncooperativeTickKeepsPrimaryFailureOnJoinTimeout | V-TIMEOUT baseline | one failure: primary cancellation replaced by timeout | 1 | 15 | true |
| CP-1 | FIX | tests/Antiphon.Tests -> bin-c1737-join-green/ | r2-source-roots | /*/Antiphon.Tests.Application/C1708ReflogSalvageTests/(C1737_HeadReflogOnlyCommitSurvivesRemoval*)\|(C1737_BranchReflogOnlyCommitSurvivesRemoval*)\|(C1737_MissingVerifiedSalvageRefKeepsTree*)\|(Released_tree_keeps_its_reflog_only_commit_under_a_salvage_ref*)\|(Commit_kept_only_as_a_reflog_old_id_gets_a_salvage_ref*) | V-1,V-2,V-3,R-1,R-2 | exactly 5 passed | 5 | 15 | true |
| CP-2 | FIX | CP-1 | r2-raw-evidence | /*/Antiphon.Tests.Application/C1708ReflogSalvageTests/(C1737_UnreadableRawReflogKeepsTree*)\|(C1737_MalformedRawReflogKeepsTree*)\|(C1737_MissingRawReflogKeepsTree*)\|(C1737_EmptyRawReflogKeepsTree*) | V-4,V-5,V-6,V-7 | exactly 7 passed including every variant | 7 | 5 | true |
| CP-3 | FIX | CP-1 | r2-classification-policy | /*/*/(TestClassificationGuardTests*)\|(TestLaneCategoryGuardTests*)\|(NativeLinuxPolicyWitnessTests*)\|(LiveLaneRunnerTests*)/(Registry_matches_compiled_metadata*)\|(every_test_class_is_tagged_unit_xor_integration*)\|(Policy_rows_require_pinned_attribute*)\|(WrapperOnlyPaidManifest*) | R-3 | exactly 4 passed | 4 | 4 | true |
| CP-4 | FIX | CP-1 | full-affected-and-guards | /*/*/(C1708ReflogSalvageTests*)\|(LiveLaneRunnerTests*)\|(SlowTestRegistryMergeTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)/* | V-JOIN,full affected class,named guards | all eleven class rosters, 110 passed | 110 | 6 | true |

Round-3 CP-0 is intentionally red, executed only on the new test-first commit. CP-1..4 run
once after the fix. CP-4 includes V-TIMEOUT, the regression's four arguments and the 105
previously selected results. CP-4 is explicitly added by this repair brief to
the design's closed three-row selection. No green row is repeated for timing.

## Round 3 B-2R: retain the primary failure on a bounded join failure

Code task `68422abc`, reviewed base `0c3484e064a3c7a1b6e29ac2b2fe13ab78555e67`.
Invariant: cleanup reports the original caller failure together with a failed
join, including subsequent caller cleanup/DisposeAsync, and never disposes a
world while its actual release tick remains live. Production is unchanged.

V-TIMEOUT: `C1737_NoncooperativeTickKeepsPrimaryFailureOnJoinTimeout` holds the
real scoped policy at before:legacy-candidate on a real Git/Postgres world.
It deliberately ignores cancellation until its own rescue finally. Cancel the
caller, await the existing ten-second join deadline, and assert an aggregate
containing the caller cancellation (token and original TickAsync stack) first
and the TimeoutException second. Assert the real tick is incomplete, the fixture
exists, and subsequent disposal propagates that same aggregate without deleting
the fixture. Finally release the fake boundary, boundedly await both tasks and
dispose the world directly; this rescue also works at the unchanged owner base.
The existing forty-second test limit and ten-second join bound stay unchanged.
One provisional expected ~11-second body; the 40-second limit includes inherited
shared-world setup/rescue, not a p95 claim. Retain one red and one ordinary green
sample under CARD-1737; no timing repetitions. No OS-selected behavior or rc
startup code changes; existing native Windows obligations remain owed.

The round-3 closed selection is CP-0 at test-first source, then CP-1..4 at fix
source. CP-4 is the single union-filter batch of all full affected/guard classes
(now 110 results); no separate green guard repetition. The separate mechanical
script still owes exactly 38 rows. Whole Unit B-1 remains CARD-1720's obligation.

PC-JOIN-TIMEOUT (unbound, pending): restore the reviewed replacement behavior by
removing the primary-failure aggregation in the SAL join owner only. Run exactly
`/*/*/C1708ReflogSalvageTests/C1737_NoncooperativeTickKeepsPrimaryFailureOnJoinTimeout`.
Require the `primary:W-JOIN-TIMEOUT:caller-cancellation-and-join-timeout` assertion
to fail because only TimeoutException survives; rescue must still join/dispose.
Restore, rebuild, same method green. Mutation owns that deliberate cycle and
additional missing-control discovery, including cancellation-callback failures.
No Code mutation credit is claimed.

## Pending SourceLanding Mutation

PC-JOIN: in SAL SalvageSource remove cancellation forwarding and both cancel/join
calls, restoring reviewed behavior; run only
`/*/*/C1708ReflogSalvageTests/C1737_ReleaseTickJoinsBeforeFixtureTeardown*`.
All four arguments must fail `joined:W-JOIN:release-tick-completed-before-fixture-dispose`,
with rescue observing the still-running real tick touching the disposed fixture.
Restore, fresh build, same method green. No producer mutation in Code.
Also discover narrow cancel-only/join-only controls at the actual landed source;
report masking honestly rather than combining credits. PC-JOIN and inherited
PC-1..7 (PC-5 three malformed arguments, PC-7 both empty arguments), plus the
frozen inherited S ledger, remain pending for Mutation. No author red evidence
here discharges a sourced PC.
