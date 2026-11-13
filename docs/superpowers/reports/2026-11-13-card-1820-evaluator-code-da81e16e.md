# CARD-1820 Code: S1R3E source-bound evaluator

Implemented S1R3E, the first unfinished prerequisite after S1R3: its plan explicitly deferred a source-bound MSBuild evaluator before S1R6. The final closed list is **151/151 passed, 0 failed, 0 skipped**, with a valid complete checkpoint receipt. Release evidence remains **incomplete/Full**, ordinary selection code is unchanged, and incremental qualification is inactive.

## Identity and scope

- Code task / landing owner: da81e16e-5d69-45be-ad31-c927d78d2794.
- Branch: feat/card-task-da81e16e. Worktree: /work/worktrees/task-da81e16e. Assigned desktop mirror is inaccessible from this task.
- Full task base: f3f1acd02814acd5d5dde4370e816c497eef09f0.
- Qualifying tested HEAD: 07270c545bf8401539ee7162d6ea6d0e0023ddf7. The publication tip adds this report only; its SHA is in the completion progress line.
- Plan: /work/worktrees/task-da81e16e/docs/superpowers/plans/2026-11-13-card-1820-evaluator-checkpoints.md.
- Evidence root: /work/worktrees/task-da81e16e/.antiphon/checkpoints/20261113-091323-aa21/. Supporting ignored evidence: /work/worktrees/task-da81e16e/.antiphon/selector-evidence/da81e16e/.
- Round: Final, selected slice only. Whole Unit remains nightly/rc-owned. Restart: none.

The master selector plan blob matches the copy read at the task base (258626b3b67ca61ceb0cbfda96f2b65934df382a). S1R2/S1R3 Reviews and CARD-1866/CARD-1868 advisories were read before choosing this prerequisite. No subdelegation, target merge, rebase, reset, branch switch, force push or live Windows-run action occurred.

## Implementation

The existing staged Antiphon.LandGraphAudit driver gains an additive release-inputs command. ReleaseInputCommand.cs requires an exact clean checkout HEAD, validates every tracked raw byte against its Git blob, hashes the whole inventory, repeats source validation after evaluation, and records target properties, SDK/import/input hashes and algorithm binary identity. Symlinks, unsupported Git modes, unbound repository inputs, arbitrary external evaluated items, source mismatch/drift and resource limits refuse rather than grant completeness.

ReleaseInputEvaluation.cs uses the actual SDK MSBuild ProjectCollection and surviving evaluated items. It exports project-level references, Compile, Content/None, EmbeddedResource, AdditionalFiles, Analyzer items, import conditions and target context. SDK-owned evaluated inputs are hashed separately. No restore/build/target executes, local restore/generated outputs are excluded through an absent evaluation context, and no project output directory is created. The diagnostic is not a land-audit approval. Environment/probes, generated code, assembly metadata, reference-property composition and platform closure remain unproven. No consumer is wired and no negative selection is authorized.

Project-level rows provide the serialization boundary requested by CARD-1877 A-2 without writing 8.2M path/class pairs. Existing conservative source edges are not subtracted. Project/full-type identity and uncapped class membership remain protected by V-2. No all-assembly test run was used; the named eight-row selection had a 14-minute provisional estimate before cold build cost.

ReleaseImpactEvaluationTests.cs uses real staged-driver calls and isolated committed fixture repositories, cleaned by the existing owning fixture. It discriminates conditional net9/net8 references, Update/Remove, imported AdditionalFiles, SDK analyzers, changed committed bytes, dirty/mismatched sources and ignored compiled inputs. Default audit compatibility is checked separately. tests/derived-impact-index.json was regenerated through its owner script after the native driver build; regeneration after the SDK repair was unchanged.

## Ordinary outcomes

| ID | Actual outcome |
|---|---|
| V-1 | Inherited S1R1; not rerun or credited here |
| V-2 | CP-107: 4/4 passed, uncapped/qualified identities |
| V-3 | CP-4: 4/4 passed, original project closure |
| V-3E | CP-106: 4/4 passed, real source-bound evaluation |
| V-4 | Not run; S1R4 runtime scope |
| V-5 | Not run; S1R5 script scope |
| V-6 | Not run; S1R6 composition |
| V-7 | Not run; S2a |
| V-8 | Not run; S2b |
| V-9 | Not run; S3a |
| V-10 | Not run; S3b |
| V-11 | Not run; S4 |
| V-12 | Not run; S5 |
| V-13 | Not run; P1 |
| V-14 | Not run; P2 |
| V-15 | Not run; independent oracle V1 |
| V-16 | Not run; real/frozen replays V2/V3 |
| V-17 | Pending native Windows W1; orchestrator-owned |
| R-1 | Inherited ordinary cap scope; not rerun here |
| R-2 | Not run; S1R6 hand contracts |
| R-3 | CP-102: 1/1 passed, committed index equals builder |
| R-4 | Not run; P1 policy/catalog |
| R-5 | Pending native Windows W1; orchestrator-owned |

Other final rows: CP-101 78/78 (all nine policy/mechanical/platform/layering/category guard classes), CP-103 project shape 1/1, CP-104 process limiter 57/57, CP-105 default audit 2/2. Fresh TRX inspection confirmed all intended classes/methods and nonzero counts: 151 results in 16 classes. The complete roster is retained in ignored final-trx-roster.json. No skip is credited as a pass.

## Final receipt, unedited lines

Validation: CHECKPOINT SOURCE VALID source=07270c545bf8401539ee7162d6ea6d0e0023ddf7 rows=8. Every row is sourceState=clean and buildSource=verified. All slots were granted. Build admission waited 90s; row waits were CP-106 105s, CP-4 45s, CP-101 75s, others 0s. Total admission-only union was 315.064s. Final wall was 1170.153s (19m30s), charged execution 855.089s, build phase 425.808s, output cleanup 119.047s. Unattributed nested IO remains unknown/remainder; no DB/server fixture ran. Measurements remain provisional under CARD-1820/CARD-1404; no row/run exceeded 30 minutes.

```text
CHECKPOINT CP-106 commit=07270c545bf8401539ee7162d6ea6d0e0023ddf7 build=ok filter=/*/*/ReleaseImpactEvaluationTests/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-da81e16e/.antiphon/checkpoints/20261113-091323-aa21/rows/CP-106/run.trx slot=granted waited=105s dirty=0 source=07270c545bf8401539ee7162d6ea6d0e0023ddf7 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=07270c545bf8401539ee7162d6ea6d0e0023ddf7 build=reused filter=/*/*/ReleaseImpactProjectTests/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-da81e16e/.antiphon/checkpoints/20261113-091323-aa21/rows/CP-4/run.trx slot=granted waited=45s dirty=0 source=07270c545bf8401539ee7162d6ea6d0e0023ddf7 sourceState=clean buildSource=verified
CHECKPOINT CP-101 commit=07270c545bf8401539ee7162d6ea6d0e0023ddf7 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/* executed=78 passed=78 failed=0 skipped=0 trx=/work/worktrees/task-da81e16e/.antiphon/checkpoints/20261113-091323-aa21/rows/CP-101/run.trx slot=granted waited=75s dirty=0 source=07270c545bf8401539ee7162d6ea6d0e0023ddf7 sourceState=clean buildSource=verified
CHECKPOINT CP-102 commit=07270c545bf8401539ee7162d6ea6d0e0023ddf7 build=reused filter=/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-da81e16e/.antiphon/checkpoints/20261113-091323-aa21/rows/CP-102/run.trx slot=granted waited=0s dirty=0 source=07270c545bf8401539ee7162d6ea6d0e0023ddf7 sourceState=clean buildSource=verified
CHECKPOINT CP-103 commit=07270c545bf8401539ee7162d6ea6d0e0023ddf7 build=reused filter=/*/*/LandGraphAuditProjectInstanceTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-da81e16e/.antiphon/checkpoints/20261113-091323-aa21/rows/CP-103/run.trx slot=granted waited=0s dirty=0 source=07270c545bf8401539ee7162d6ea6d0e0023ddf7 sourceState=clean buildSource=verified
CHECKPOINT CP-104 commit=07270c545bf8401539ee7162d6ea6d0e0023ddf7 build=reused filter=/*/*/ProcessSpawnLimitTests/* executed=57 passed=57 failed=0 skipped=0 trx=/work/worktrees/task-da81e16e/.antiphon/checkpoints/20261113-091323-aa21/rows/CP-104/run.trx slot=granted waited=0s dirty=0 source=07270c545bf8401539ee7162d6ea6d0e0023ddf7 sourceState=clean buildSource=verified
CHECKPOINT CP-105 commit=07270c545bf8401539ee7162d6ea6d0e0023ddf7 build=reused filter=/*/*/LandGraphEvaluationTests/(C1483_S1_HarmlessFixtureApproves*)|(C1483_S1_EvaluatedSkippableItemRefuses*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-da81e16e/.antiphon/checkpoints/20261113-091323-aa21/rows/CP-105/run.trx slot=granted waited=0s dirty=0 source=07270c545bf8401539ee7162d6ea6d0e0023ddf7 sourceState=clean buildSource=verified
CHECKPOINT CP-107 commit=07270c545bf8401539ee7162d6ea6d0e0023ddf7 build=reused filter=/*/*/ReleaseImpactRelationTests/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-da81e16e/.antiphon/checkpoints/20261113-091323-aa21/rows/CP-107/run.trx slot=granted waited=0s dirty=0 source=07270c545bf8401539ee7162d6ea6d0e0023ddf7 sourceState=clean buildSource=verified
BUDGET wall=1170.153s execution=855.089s admissionOnly=315.064s
```

## Preserved failures and repairs

An initial expected-SHA transcription error was refused before any build/test. Every subsequent run passed its actual committed HEAD as expected-source-sha. Each row build used a fresh isolated output root; all processes were joined. Compiler errors/timeouts below are not regression witnesses.

Run 20261113-082651-5669: CLI duration was parsed as seconds, so the build timed out before tests; not a regression red. The existing 20-minute plan bound was restored with explicit 20m units.

```text
CHECKPOINT CP-106 commit=0bf05e7528297ca3ab1d80922b10f6eeb19ceb7b build=failed filter=/*/*/ReleaseImpactEvaluationTests/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
```

Run 20261113-082902-e7df: Fixture compile failed (missing LibGit2Sharp CommitOptions); zero test executions, not a regression red.

```text
CHECKPOINT CP-106 commit=e1473d94ab79cf9132ca59e6bbd2cc388a165a33 build=failed filter=/*/*/ReleaseImpactEvaluationTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=e1473d94ab79cf9132ca59e6bbd2cc388a165a33 sourceState=clean buildSource=unknown
```

Run 20261113-083639-7df9: Committed old-evaluator adapter: four actual assertion failures. The default audit refuses TargetFramework conditions and supplies no release inventory/source-specific refusals.

```text
CHECKPOINT CP-106 commit=1075f41b106749a1011050c2cb69bdfb93edd517 build=ok filter=/*/*/ReleaseImpactEvaluationTests/* executed=4 passed=0 failed=4 skipped=0 trx=/work/worktrees/task-da81e16e/.antiphon/checkpoints/20261113-083639-7df9/rows/CP-106/run.trx slot=granted waited=0s dirty=0 source=1075f41b106749a1011050c2cb69bdfb93edd517 sourceState=clean buildSource=verified
```

Run 20261113-084609-a072: First full run: 149 passed / 2 failed / 0 skipped. SDK-owned analyzer items were incorrectly treated as outside-repository inputs. All seven other rows passed, but validate refuses the mixed aggregate as report_failed, including subset validation.

```text
CHECKPOINT CP-106 commit=feaa04d6abbd75856c12a8a6be389cc709fa0c96 build=ok filter=/*/*/ReleaseImpactEvaluationTests/* executed=4 passed=2 failed=2 skipped=0 trx=/work/worktrees/task-da81e16e/.antiphon/checkpoints/20261113-084609-a072/rows/CP-106/run.trx slot=granted waited=15s dirty=0 source=feaa04d6abbd75856c12a8a6be389cc709fa0c96 sourceState=clean buildSource=verified
```

Run 20261113-090325-d9f2: SDK input repair: 4/4 green; exact-source validator passed. Native SDK inputs are hashed under SDK identity; repository-owned and arbitrary external inputs retain their guards.

```text
CHECKPOINT CP-106 commit=6a1181b9e0850db36ae20b763ccc7addf376e8af build=ok filter=/*/*/ReleaseImpactEvaluationTests/* executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-da81e16e/.antiphon/checkpoints/20261113-090325-d9f2/rows/CP-106/run.trx slot=granted waited=0s dirty=0 source=6a1181b9e0850db36ae20b763ccc7addf376e8af sourceState=clean buildSource=verified
```

The final full run resolves the demonstrated aggregate validation gate. It is not a flake repetition or a relabeling of the failed report. No timeout was widened and no assertion was weakened. Unchanged proof selections stayed within 3 normal + 2 loaded repetitions; no loaded cycle or Review flake exemption was used. None is required after the final green.

## Declared preparation and additional runs

All were gated through scripts/build-slot.ps1, native default outputs, UseAppHost=false where applicable, slot=granted waited=0s. Each build acquired its own lease. Checkpoint builds/rows acquired their own slots through the tool.

| Run | Reason and actual result |
|---|---|
| Checkpoint tool bootstrap | Required runner preparation; 6.65s, 0 errors, 11 existing warnings |
| Native evaluator build | Cheap API compile check before another cold test build; 5.71s, 0 warnings/errors |
| Native DerivedImpactIndex driver build | Required before owner regeneration; 3.82s, 0 warnings/errors |
| Owner regeneration -NoBuild | 41s lease held; index changed and was committed before tests |
| Native evaluator repair build | Compile repaired SDK classification before cold run; 6.87s, 0 warnings/errors |
| Repair fixture diagnostic smoke | One clean project, two real SDK analyzers, SDK 10.0.404, incomplete/Full; isolated fixture removed. Not qualifying test credit or a PC |
| Owner regeneration after repair -NoBuild | 39s lease held; index unchanged |

Checkpoint report unlisted:none refers to commands owned by that checkpoint run. The preparation/smoke above is the complete additional build/test/diagnostic list, retained in ignored logs under the supporting evidence root. The source-validation CLI also had one missing --evidence invocation, corrected without executing a build/test.

## Manual acceptance and integration obligation

- Full changed-glob census matches land-graph-audit-project and test-lane-category; their classes passed CP-103 and CP-101. The process-limiter census also passed CP-104.
- Ordinary impacted-classes.ps1, lib/release-impact.ps1 and DerivedImpactIndexBuilder.cs are byte-identical to base. Only the owner-generated ordinary index gains the new test dependencies/counts. No release selection or incremental activation changed.
- OS witness lint passed with changed files and the reused unchanged EvaluationBudget helper explicitly inventoried. No changed OS-selected policy was claimed. Native Windows W1 remains pending and orchestrator-owned; Linux does not credit it.
- Evidence-diff over base..07270c545bf8401539ee7162d6ea6d0e0023ddf7: 7 commits, entries=0, violations=0. The completion audit repeats over base..publication HEAD.
- No migration/model/route changes match: migration/downgrade rows not applicable (0 executed, no red/green credit).
- Earlier merge-tree against master fac5cc2de89cc82eb39e85cd2bf6c4df18d55ac6 was clean. Fresh master ec5de7348fe327ed1fb7c67ffb2939098f548dbd conflicts ONLY in generated tests/derived-impact-index.json (merge-tree exit 1). The branch is not currently landable against that master. Landing must rebase its integration snapshot, run owner regeneration, and rerun committed_index_matches_the_builder on the combined source. Do not merge master into this task branch. This is the existing generated-index landing obligation, not an implementation or ordinary-test failure.

CARD-1866 A-1/M7 algorithm-drift witness and A-3 old envelope fixture cleanup remain S1R6 work. Evaluator content binding does not claim to resolve P2 envelope authenticity. CARD-1868 precision/delegate work and CARD-1877 reflection/old-new composition remain pending before S1R6. No pending upstream row above is marked passed.

No PC IDs or bound variants were declared for this slice. All controls, deliberate red/restore/green cycles and missing-control/zero-PC discovery remain pending for SourceLanding Mutation under the operator's 2026-11-12 execution waiver. Ordinary regression reds do not discharge PCs. No unbound candidate pack was created; Mutation owns docs/superpowers/mutations/.

## Pushed source slices

```text
0bf05e7528297ca3ab1d80922b10f6eeb19ceb7b test(selector): commit evaluator binding and item witnesses before implementation
e1473d94ab79cf9132ca59e6bbd2cc388a165a33 docs(selector): retain twenty-minute budget with explicit duration units
1075f41b106749a1011050c2cb69bdfb93edd517 test(selector): use repository commit options in evaluator fixture
a94be27ce17a4ed857ac6b6fe7bb8a0ec839c0ec feat(selector): export source-bound MSBuild project inputs without granting completeness
feaa04d6abbd75856c12a8a6be389cc709fa0c96 chore(selector): regenerate ordinary index and finalize evaluator checkpoint scope
6a1181b9e0850db36ae20b763ccc7addf376e8af fix(selector): bind SDK-supplied evaluated items to SDK content identity
07270c545bf8401539ee7162d6ea6d0e0023ddf7 docs(selector): require one valid complete receipt after targeted evaluator repair
```

Next: Review the implemented evaluator and the valid complete receipt. Caller commissions any later SourceLanding Mutation and the generated-index integration repair. Restart: none.
