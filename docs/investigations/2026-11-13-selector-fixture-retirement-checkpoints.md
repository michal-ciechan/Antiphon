# Selector fixture retirement Debug verification

Task: 49ae044d. Base: 90ae19f65bc4aff58a8f10162614c94e14d42bde (master at dispatch).
Branch: feat/card-task-49ae044d. Mutation checks waived by the operator on 2026-11-12.

## Preserved historical attempts

The red-first native-default build and exact new-method union were required to
reproduce both defects before their fix. At 512d58221 they executed 7 cases:
4 passed, 3 assertion failures, 0 skipped. Failures: ignored fixture Ready,
unpublished parent reaching its publication refusal, fixture location outside checkout.
The fixed selection additionally covers linked parents and both origin and backup remotes.

At 754306353 the additional exact method
Selector_fixture_cannot_use_disposable_seed_parent_as_publication_proof executed
1 case and failed its exit-code assertion (expected refusal 2, actual Ready 0).
That separately committed red witness required another native-default build
(384 seconds) and 23 seconds execution. The fixed parent requires origin and the
ordinary full publication proof; a disposable no-origin seed cannot substitute.

20261113-110706-6655 was stopped during its first build before tests, to close
that edge. 20261113-112052-ef31 refused CP-1 with pre-existing-output from the
stopped build (0 tests), then was stopped before CP-2 tests. Their outputs remain.

The initial broad two-row selection at d0cb4692e ran as
20261113-112323-7c65. It was stopped with no TRX and receives no green credit.
The phase journal preserves completed and pending case records. Debug found the
scope error: RollingVolumeRecycleScriptTests included
C1008_Legacy_rolling_and_jq_rosters_remain, whose four modes execute 252 child
invocations of the complete legacy/JQ rosters. The 20-minute CP-1 estimate was
incorrect. RemoteScriptContractTests also should not be selected wholesale for
an audit-body change. Existing container-host cases consumed 765 seconds (22
cases), container script cases 224 seconds (16 cases), and the private-origin
proof matrix 248 seconds (one case). These are observed case-span sums, not p95.
The first isolated build took 406 seconds including its recorded boundary work.
No assertion or timeout was relaxed. The closed list below supersedes that broad
selection after this same Debug task investigated the over-30-minute run.

## Verification design

The final closed list selects the changed audit's full content class, retirement
proof/apply/receipt classes, exact legacy Git-audit regressions and the requested
guards. Unchanged whole-rollout/JQ aggregate rosters, container-removal phase
classes, Docker daemon integration and native-Windows placement probes are outside
this Linux audit-body fix. The task prohibits touching Docker volumes or a live
host; the selected native host tests use the existing fake Docker/HTTP fixtures
with real Git and read-only private namespace mounts. All requested positive and
negative fixture witnesses remain mandatory. No live retirement is performed.

Build the native-default Antiphon.DerivedImpactIndex driver through build-slot,
then run regenerate-derived-impact-index.ps1 -NoBuild through build-slot before
these rows. Commit changed index bytes as their own slice. The first regeneration
changed the index (c660cecf2); the next regeneration was byte-identical.

Run one isolated build per row, serially, through the checkpoint runner. Fresh
output names preserve stopped attempts without deleting or reusing their output.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-debug49-bounded-content/ | content | `/*/*/VolumeRetirementContentTests/*` | all new fixture witnesses, existing content/publication/metadata and closed-admin refusals | all listed, 0 failed | 1 | 25 |
| CP-2 | all | tests/Antiphon.Tests -> bin-debug49-bounded-gates/ | gates | `/*/*/(VolumeRetirementApplyGateTests*)\|(VolumeRetirementReceiptTests*)\|(VolumeRetirementHostProofTests*)\|(VolumeRetirementWrapperTests*)/*` | preview/apply/token/expiry, receipt and proof binding, wrapper protocol, protected names and live guards | all listed, 0 failed | 1 | 24 |
| CP-3 | all | tests/Antiphon.Tests -> bin-debug49-bounded-audit/ | audit-regressions | `/*/*/(RollingVolumeRecycleScriptTests*)\|(RemoteScriptContractTests*)/(C1594_Antiphon_evidence_gitdir*)\|(C1008_Transport_scripts_are_ascii*)\|(C1008_Documentation_and_transport_pins_match*)\|(C1008_Refusal_receipts_do_not_leak_secrets*)\|(C1008_Recycle_audits_work_as_1654*)\|(C1008_Recycle_refuses_unpublished_and_dirty_work*)\|(C1008_Recycle_refuses_uninspectable_git*)\|(C1008_Retire_temp_rechecks_absence_and_retirement*)\|(C1008_Recycle_dry_run_never_mutates*)` | unchanged legacy Git admin evidence, dirty/unpublished/unknown refusals, read-only audit and transport | all listed, 0 failed | 1 | 15 |
| CP-4 | all | tests/Antiphon.Tests -> bin-debug49-bounded-guards/ | producer-guards | `/*/*/(ReleaseImpactEnvelopeTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(ProcessSpawnLimitTests*)\|(ShellFixtureLineEndingContractTests*)\|(DockerStackDocumentationTests*)/*` | temp fixture producer, required guard union, process ownership and touched shell/document contracts | all listed, 0 failed | 1 | 10 |

## Performance budget

The superseding groups budget 25, 24, 15 and 10 minutes including isolated builds.
The overall run exceeds 30 minutes: this Debug task has already investigated that
cost and replaced aggregate roster expansion with the bounded groups above. No
row is intended to exceed 30 minutes. The four mandated cold builds account for
roughly 27 minutes at the observed 406-second sample. There is no comparable p95;
existing per-test timeouts stay unchanged. A row that overruns requires retention
and further targeted Debug analysis, not an automatic retry or a wider timeout.

Fresh host fixture state is necessary for destructive/refusal vectors; the existing
per-session Compose seed is shared. Retain per-case TRX and phase journals plus
slot/build timings under .antiphon/checkpoints and .antiphon/debug-49ae044d.
Noninstrumented phases remain unknown. Case-span sums can overlap and are reported
separately from wall time. The strict process-spawn limiter and checkpoint --serial
prevent overlapping test hosts or builds in this worktree.
