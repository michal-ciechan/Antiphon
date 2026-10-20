S2 is implemented and pushed; all 42 non-code cases pass, but the ordinary verification gate remains incomplete on C688_RealLandProfile. Stop at this slice boundary within the 60-minute brief; a Code continuation is needed before ordinary Review.

Original Code / landing owner: `efc5dea7-0623-43b6-b660-5851fe4cf6ef`.
Branch: `feat/card-task-efc5dea7`.
Worktree: `/work/worktrees/task-efc5dea7`.
Base: `480407504eae586e18281665f2808c3f77281dd6` (reviewed S1 source tip).
Plan: `docs/superpowers/plans/2026-10-20-card-1380-faster-lands-plan.md`.
Mutation candidate: `docs/superpowers/mutations/2026-10-20-card-1380-s2-candidate.md`.
Restart: none. No land, RC adoption, deployment or AppHost restart.

Commits, each pushed separately:

| SHA | Outcome |
|---|---|
| `a935ec8973a074a8b075ed313fffc87ba63b3189` | Test-only CP-3 witness and corrected CP-4 TUnit filter; expected assertion red. |
| `bbef6c6bf1cbc48241dabe73a9a2f6fb992a8584` | S2 production implementation, tests, owner documentation and unbound Mutation candidate. CP-4: 153 executed, 143 passed, 10 failed. |
| `a9e5e29584427dca1491dc1ae2cd7d07f729cfa8` | Repair scratch build-graph staging and RC target setup; strengthen nullable diagnostic assertions. Production unchanged. |
| `abd4b4159697adfb446abca7eac0b0375aedaa7b` | Bind CP-4R minimum to observed 57-case roster. Actual repaired tested source. |

This report is a subsequent evidence-only commit; the final message supplies its full pushed SHA. No production changes followed bbef6c6bf, and no source changed while any build/test run was live.

Implementation:

- `LandVerificationPolicy.cs` reads the actual pinned target-to-rebased raw NUL-delimited Git diff. Only regular mode-100644 changes to docs Markdown and root README qualify. Both rename paths/modes are checked. Other paths, links/type changes, malformed/failed reads and unknown graphs build.
- `LandVerificationBuildGraph.cs` binds the candidate to an audited SHA-256 inventory of 292 tracked project/import/solution/configuration/package/script inputs. The fingerprint is compiled into the policy, never read as candidate authority. Graph drift defaults to building.
- `AgentTaskLandingProtocol.cs` replaces new `base_unchanged` decisions with this master-only policy. ForceVerificationBuild, selected tests and other targets build. AlreadyPresent retains its independent containment semantics. Decision diagnostics retain target/candidate SHAs, graph fingerprint, task/operation/request identity and reason. Existing compiler-slot behavior is unchanged.
- `LandingSettings.cs` adds explicit ForceVerificationBuild, default false. `AgentTaskLandingState.cs` admits pinned schema-3 non-code master receipts without claiming VerificationPassed=true; historical receipts stay readable. Existing StageOutcomes recording persists Verify/Skipped with Detail=non-code.
- `LandNonCodeSkipTests.cs` uses real scratch Git repositories, isolated database persistence and a recording verifier. It checks remote SHA, stage outcome and build invocation/refusal. Stage-outcome scenarios now explicitly inject that recording verifier; other callers of their shared helper retain the original real-verifier default.
- Updated the affected retry expectation and state validator coverage, living owner documentation, checkpoint manifest and unbound Mutation candidate.

Ordinary outcomes:

| ID | Actual outcome |
|---|---|
| V-2 | PASS on repaired source: 42/42 cases across 13 methods. Docs add/edit/delete/rename/README/empty diff, actual rebased diff, code/resources/config/tooling fallback, graph drift/read failures, modes/renames, force/filter/RC and failing-verifier publication refusal. |
| R-2 | INCOMPLETE gate. 82 state cases and 14 stage-outcome cases passed in CP-4; CP-4R passed 14/15 refused-retry cases. C688_RealLandProfile still reports seven worktree listings against its unchanged upper bound of four. |
| V-1 / R-1 | Not rerun; S1 was the supplied reviewed base. |
| V-3 through V-9 / R-3 through R-9 | Not implemented or run; belong to uncommissioned S3-S9. |

Whole Unit was explicitly excluded by the task-specific brief, not deferred or claimed passed. No full assembly ran. CP-4 added the two full affected state/retry classes because the implementation changes persisted skip recognition and removes the retry's old unconditional skip. CP-4R reran the two affected integration classes after fixture repair; the unchanged 96 passing state/stage cases remain historical results, not re-labelled as a new-SHA run.

CP-3's fresh TRX executed exactly one test and failed at verifier Calls expected 0, actual 1. CP-4's eight documentation failures exposed a fixture defect: its generic git add ignored copied historical tracked scripts under .antiphon/, so the scratch graph did not match the audited graph. The RC fixture also omitted its required local target ref. Both were fixed without changing production policy or loosening assertions. CP-4R's fresh TRX has 42/42 LandNonCodeSkipTests passes and 14 passes/one failure in AgentTaskLandRefusedRetryTests. Every intended method/class was inspected with nonzero counts.

Attribution remains UNRESOLVED for C688. The checkpoint tool's --baseline comparison fetched and resolved the exact base, created a detached owned checkout, built successfully (0 errors), then invoked only `/*/*/AgentTaskLandRefusedRetryTests/C688_RealLandProfile`. It produced NO baseline TRX. The automatic `NEW` classification is therefore not evidence of an introduced failure, nor proof of inheritance. The tool's single-method baseline filter omits the brief's required trailing wildcard. Do not blame S2 or the base until a fresh, nonzero method-scoped baseline result is obtained.

Next exact work: qualify `/*/*/AgentTaskLandRefusedRetryTests/C688_RealLandProfile*` at base `480407504eae586e18281665f2808c3f77281dd6`, with a fresh bound checkpoint build and inspected TRX. Resolve the remaining profile gate without increasing its four-listing assertion or weakening fresh identity checks. Then complete the required repaired selection and commission ordinary Review. This report does not claim Clean/Full, a green ordinary gate, or landability.

Unedited checkpoint/provenance lines follow. All three runs passed committed HEAD as --expected-source-sha and used --row-timeout 40m. All were waited to terminal exit 1. The red receipts have clean source and verified build provenance; they are not eligible green receipts. Historical cleanup refusals are preserved, not edited after manual output removal.

```text
source: a935ec8973a074a8b075ed313fffc87ba63b3189 state=clean buildSource=verified
CHECKPOINT CP-3 commit=a935ec8973a074a8b075ed313fffc87ba63b3189 build=ok filter=/*/*/LandNonCodeSkipTests/C1380_DocsOnlyRebasedLandRecordsNonCodeSkip* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-efc5dea7/.antiphon/checkpoints/20261020-122100-2f00/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=a935ec8973a074a8b075ed313fffc87ba63b3189 sourceState=clean buildSource=verified
PHASES CP-3 slotWait=30s build=327.2965015s startup=41.9913434s testsWall=10.5039604s teardown=1.2412173s hostWall=53.7365192s
source: bbef6c6bf1cbc48241dabe73a9a2f6fb992a8584 state=clean buildSource=verified
CHECKPOINT CP-4 commit=bbef6c6bf1cbc48241dabe73a9a2f6fb992a8584 build=ok filter=/*/*/(LandNonCodeSkipTests*)|(AgentTaskLandStageOutcomeTests*)|(AgentTaskLandRefusedRetryTests*)|(AgentTaskLandingStateTests*)/* executed=153 passed=143 failed=10 skipped=0 trx=/work/worktrees/task-efc5dea7/.antiphon/checkpoints/20261020-123518-729b/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=bbef6c6bf1cbc48241dabe73a9a2f6fb992a8584 sourceState=clean buildSource=verified
PHASES CP-4 slotWait=30s build=436.0881068s startup=58.4962648s testsWall=350.6803733s teardown=2.9501269s hostWall=412.1267712s
source: abd4b4159697adfb446abca7eac0b0375aedaa7b state=clean buildSource=verified
CHECKPOINT CP-4R commit=abd4b4159697adfb446abca7eac0b0375aedaa7b build=ok filter=/*/*/(LandNonCodeSkipTests*)|(AgentTaskLandRefusedRetryTests*)/* executed=57 passed=56 failed=1 skipped=0 trx=/work/worktrees/task-efc5dea7/.antiphon/checkpoints/20261020-125450-0ed9/rows/CP-4R/run.trx slot=granted waited=0s dirty=0 source=abd4b4159697adfb446abca7eac0b0375aedaa7b sourceState=clean buildSource=verified
PHASES CP-4R slotWait=15s build=254.3314532s startup=52.6354777s testsWall=275.1574338s teardown=2.0361063s hostWall=329.829247s
```

Costs and additional commands:

- CP-3: 7m12s reported wall; build slot=granted waited=30s, test row slot=granted waited=0s. One isolated build and one witness.
- CP-4: 15m13s reported wall; build slot=granted waited=30s, row slot=granted waited=0s. One isolated build and the four full named classes.
- CP-4R: 10m15s reported ordinary wall; build slot=granted waited=15s, row slot=granted waited=0s. One isolated build and 57 selected cases. Baseline comparison then added about 5m38s; all its leases were granted with waited=0s, its build held a slot about 324s and its no-TRX invocation about 6s. The report's 19/20 builds are imported manifest definition counts, not executed build counts.
- The first gated `dotnet run --project tools/Antiphon.Checkpoints` implicitly bootstrapped the CLI. Launcher slot=granted waited=15s, held=94s including its wait; the wrapper warned that dotnet run's implicit build uses its default node count. Later launchers used --no-build, slot=granted waited=0s. All actual checkpoint build/row leases were granted; current builds used the grant's six-CPU cap.
- One gated, read-only reflection diagnostic used the already-built bbef6c6bf assembly to distinguish classifier failure from fixture failure. Both the actual repository graph and a real docs-only raw diff were accepted. Slot=granted waited=15s, held=2s. It was not a checkpoint/test pass, did no build, and claimed no execution counts.
- The baseline extra build/invocation was explicitly selected to establish attribution for the existing failing profile method, not a broad suite. The automatic missing-wildcard filter and missing TRX are preserved as a verification defect, not repaired in evidence.
- No timeout or assertion was widened, no flake retry was introduced, no deliberate mutant was run, and no green unchanged proof was repeated. Intermittent owner API timeouts/502s recovered under the checkpoint guard.

Generated evidence remains ignored under these absolute roots:

- `/work/worktrees/task-efc5dea7/.antiphon/checkpoints/20261020-122100-2f00/`
- `/work/worktrees/task-efc5dea7/.antiphon/checkpoints/20261020-123518-729b/`
- `/work/worktrees/task-efc5dea7/.antiphon/checkpoints/20261020-125450-0ed9/`

Each contains report.json/report.md and the current row's fresh TRX. The final root also retains baseline/build.log and classification.txt, but no baseline TRX. No green receipt validation is claimed for a red row. Cleanup preserved original CHECKPOINT/report bytes: owned bin-c1380-2r, bin-c1380-2 and bin-c1380-2fix were removed as individual regular files and empty directories after their owners stopped, after refusing tracked/link paths. The baseline tool removed its detached checkout; that absence was independently checked. No unrelated outputs or daemons were removed.

Publication preflight: fetched origin/master at `44b3147c2c7c2164447fcd24d63cf277d2a3d7d7`; merge-tree returned exit 1, tree `ed84c93603fbdacfd2e7c380b480f1fac56237b7`, with conflicts in docs/orchestration-loop.md, the faster-lands plan and LandingSettings.cs. S1 is now on master with rebased commit IDs while this authorized branch remains stacked on its original tip. No merge, rebase, reset, branch switch or force-push was performed. This is not a clean landing preflight. The full base..final-report-tip evidence-history guard is run before settlement and its result is included in the final response.

PC-1 and PC-2, all variants, remain pending for method-scoped SourceLanding Mutation; Mutation also owns missing-control discovery. The unbound candidate specifies exact methods and deliberate defects. No PC is discharged by ordinary or nightly results.

Manual acceptance remains pending the later operator RC go and real production workload: no under-two-minute docs-land or under-three-minute warm-code-land claim is made from fake verifier tests. Both required runner-default/catalogue routes were refreshed; the eligible Linux lane was used without embedding a host or OS pin.

Deferred/remaining work is explicit: qualify and resolve the R-2 profile gate, obtain a clean landing preflight through the caller's authorized process, then ordinary Review. Only after that may the caller land the original Code task and commission SourceLanding Mutation. S3-S9 remain separate future slices. Restart: none.
