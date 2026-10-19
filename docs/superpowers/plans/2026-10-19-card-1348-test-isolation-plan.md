# CARD-1348: test and tooling isolation

Status: Plan complete; **Investigate the corrected cleanup/Git premises next, then TestDesign**. This dispatch does not fold TestDesign into Plan. The checkpoint proposal below is input to that stage, not permission to skip its guard inventory or start Code.

Target: **master only**. The rc is closed. No product code, tests, fleet settings, or card state changed in this Plan dispatch.

## Outcome and acceptance

Release the build lease before a checkpoint starts its known `--no-build` test host, and make every Grok SHA-pin harness invocation own its download directory. Preserve source/build binding, build admission, output custody, exact test selection, and the build broker's capacity and memory rules.

Two other card items need correction before implementation: the root sweeper already observes owner death three times, and shared Git storage is not evidence that a particular reader waits on a particular writer's lock. Do not implement an extra observation or a generic Git retry merely to match the investigation's wording.

The acceptance boundary is the wrapper, PowerShell checkpoint driver, and checkpoint tool named below. Arbitrary command rows remain conservatively leased because they can build. This is not a claim that all fleet test hosts become unleased; the landing verifier and opaque nightly/release script callers need the caller audit in A-2.

## Evidence and ground truth

Read CARD-1348, board Antiphon, including its full description (zero revisions), and CARD-1346 (Done, one revision). Fetched the named investigation branches without switching or merging branches.

- Assigned branch/base: `feat/card-task-c01e8c96`, `f99f8bffb5887d43fafae61ae979ae9e11a36f80`.
- Fetched master inspected: `d5dec9555bd726ad11bb30073c1733d6b3e7bb0e`. CARD-1346 is present there but absent at the assigned base. Future Code starts from a caller-provisioned current-master branch; this plan branch must not merge master.
- Primary evidence: `docs/investigations/2026-10-19-test-concurrency-isolation.md`, commit `3b9488e2fb31d5b4df4e2ff7905ae10eaeda686d`, branch `feat/card-task-047e36c5`. Its source citations describe an older layout. Its roster results are historical evidence, not tests executed for this plan.
- Companion evidence: `docs/investigations/2026-10-19-rc-proc-stat-flake.md`, commit `10b634482e7f676a2258d85821494d433b2cb7ce`, branch `feat/card-task-75ae5020`.
- Owners read: `AGENTS.md`, `docs/project-context.md`, the checkpoint/slot/temp-custody sections of `docs/testing-and-build.md`, and the stage/landing contracts in `docs/orchestration-loop.md`.

| Card/investigation assumption | Code inspected | Consequence |
|---|---|---|
| Six Linux roster reds could be cross-task output collisions | The investigation separates five private-repository ledger failures from the `/proc` registration race. `CheckpointBuildBinding.PathName` in `tools/Antiphon.Checkpoints/Evidence/SourceRunGuard.cs` is under the individual OutputPath, and `Check()` binds repository, project, properties, source and output. | Do not move the stamp, weaken confinement, or rerun the roster for this card. |
| A no-build checkpoint spends a build slot for its whole test run | `RunScheduler.BuildOneAsync` leases the build; `RunRowAsync` independently leases every row, including the generated `NoBuild=true` row. `Program.Row` also wraps all of `RowRunner.RunAsync` in a lease. | Fix scheduled and direct-row entry points; removing only the scheduler acquire leaves the direct CLI bug. |
| The PowerShell checkpoint lease covers both phases | `scripts/run-checkpoint.ps1` acquires around line 636 and releases around line 1170, after build and run. `-NoBuild` still acquires. Harness `Test-C589_NoBuildStillLeases` explicitly requires this old behavior. | Narrow lease scope and replace the old contract assertion in the same slice. |
| The generic wrapper can distinguish build from test | `scripts/build-slot.ps1` currently leases every command. `scripts/lib/build-slot.ps1` already classifies dotnet arguments for CPU switches; the wrapper cannot inspect arbitrary script contents. | Admit only a narrow, literal no-build command form; opaque commands retain their lease. Never infer from labels or a substring. |
| The broker needs changing to stop long roster leases | Lease lifetime is controlled by callers. The broker's PID/start identity, FIFO, renew/release, memory floor, and timeout are independent. | No broker protocol, capacity, memory-floor, TTL or host configuration change is needed for the diagnosed path. |
| Grok pin tests have isolated scratch | `tests/Antiphon.Tests/Infrastructure/GrokSha256PinTests.cs:149` creates a GUID scratch directory, but the extracted shell still uses `/tmp/grok-download`; lines 159-163 and 207-210 delete that host path. `Gate` covers only the deletes, not the whole invocation, and is process-local. | Put the downloaded artifact inside invocation scratch; preserve the real Dockerfile pin expression and fake curl evidence. |
| Temp sweep deletes after one dead owner observation | In `src/Antiphon.HostCleanup/Checkpoints/CheckpointTempRootSweep.cs`, `SweepOnce` observes the preflight owner, `Eligible` observes again under the root gate, and `TestRootGuard.MarkDeleting` observes once more before marker replacement (`TestRootGuard.cs:79`). These paths are unchanged between the assigned base and fetched master. | The requested root-owner second observation already exists. Do not add a fourth or claim a red-first root-owner defect. |
| A dead-then-live root is untested | `CheckpointTempRootSweepTests.eligibility_is_rechecked_after_the_root_lock` has six cases: live, unknown, reused, grace, marker, inventory; it asserts payload/marker/index retention. Separate tests cover live-owner lock avoidance and the dead-root lock requirement. | Reuse this narrow regression for A-1. A new baseline test of the same behavior should pass, not be reported as a new red. |
| CARD-1346 is still needed here | Fetched master includes the `DirectoryNotFoundException` catch in `ProcessIdentityProbe.Observe` and the three disappeared-child exceptions in `CheckpointTestScope.Register`. | Do not duplicate the companion fix or use the older assigned base as proof that master still lacks it. |
| A common Git directory is a single lock blocking source reads | `SourceSnapshot.CaptureOnce` runs `rev-parse`, porcelain `status`, `ls-files`, and two `diff`s; `BuildConfinement.CaptureLedger` runs its own Git reads. `SourceSnapshot` reports `git_exit`/`git_timeout` as unknown, without a classified lock error. The historical rosters did not report this failure. | Measure exact commands, lock paths, exit/error and delay. Shared objects alone do not justify retries or relaxing `source_unknown`. |

### Placement

Read `GET /api/runner-defaults` and `GET /api/session-runners` on 2026-10-19 at approximately 14:25 UTC. Defaults revision was 2 with no per-kind overrides; the default resolved to an available eligible Linux runner. An eligible Windows runner was also advertised; another configured runner was unavailable and draining. These are observations, not a placement pin.

At each later dispatch reread both routes. Omit `-Runner`; use a platform constraint only for the lane that needs that OS. Grok execution and Linux native overlap proof use **Linux**. Shared PowerShell/tool behavior also has a narrow **Windows** qualification lane. Portable fake-clock/driver checks run on the selected lane without embedding a fleet hostname, workspace root, or endpoint. No dispatch is performed by this task.

## Decisions

- **D-1 — Preserve the existing isolation boundary.** Keep per-output build stamps and owned artifacts. A build slot is a host resource budget, not an exclusive lock on a worktree or common Git directory. Reject stamp relocation and raised host concurrency: neither addresses the actual lease lifetime.
- **D-2 — Exempt only known no-build execution.** The wrapper recognizes the actual `dotnet`/`dotnet.exe` executable and `run` verb with an exact `--no-build` option before the application's `--`. An application argument named `--no-build`, a script filename, a label, or an unknown command cannot select the exemption. Retain existing leases for implicit-build `run`, build/test/publish/pack/msbuild and opaque script commands. No new general bypass flag; `-NoSlot` remains operator-only. Checkpoint typed TUnit rows are known no-build after their owned build and do not acquire another lease.
- **D-3 — Keep proven cleanup guards.** Root-owner double observation is already implemented and has a regression. A-1 must establish whether the card meant nested executor custody, a changed marker generation, or a time-separated observation across sweep passes. Those are distinct contracts; none is silently substituted for the card's root-owner claim. Keep the ten-minute grace, five-minute sweep interval and all bounds unchanged.
- **D-4 — Audit Git before designing recovery.** A-2 produces the lock/reader matrix. Reject a catch-all retry, an invented common-dir mutex, or a larger timeout. A demonstrated transient lock refusal may justify a separately specified bounded retry; unknown/error and actual source changes must still refuse. If no conflict reproduces, record that result and retain current behavior.
- **D-5 — Keep the broker and test scheduling rules.** Release scope changes at clients, with `finally`/async disposal on every acquired-build exit. Build CPU limits, FIFO, memory-floor refusal, timeout exit 4, and documented transport fallback remain. Retain scheduler width, one in-run build lane, `Serial`, Pty exclusions and assembly-local `ParallelLimiter<ProcessSpawnLimit>`. Removing test leases removes one cross-process test-host cap; it does not guarantee free memory for the next build. Do not compensate by raising budgets or adding an unrequested test broker.
- **D-6 — Report lease facts by phase.** A typed test row reports `slot=skipped`, `slot-reason=no-build`, `waited=0s`; it must not copy its completed build's `granted` state. Build progress/BUILD SLOT lines retain the real grant and wait. PowerShell's combined build-plus-run CHECKPOINT can retain the build grant as a documented build-phase fact; a pure `-NoBuild` invocation reports skipped. No schema version change is planned unless TestDesign finds a consumer that cannot represent this distinction.
- **D-7 — Isolate the Grok harness, not the image contract.** Preserve Dockerfile extraction boundaries, checksum expression and production image path. Rewrite only the test execution copy's download-path occurrences to a quoted environment-variable path in the owned scratch tree. Check that the substitution occurred and no legacy executable download-path reference remains. Remove fixed-host deletion and the now-unnecessary static delete gate. A global lock would still share destructive state and is rejected.
- **D-8 — Reconcile premises before Code.** This is a completed plan with two confirmed changes and a bounded investigation gate, not a request to invent missing red evidence. TestDesign is separate and follows A-1/A-2. The final manifest must include the ordinary closed list, guard/PC inventory, Linux/Windows selections, and separate Code/Mutation costs. No full suite or frozen-roster run is part of this card.

## Investigation gate

### A-1 — Identify the intended cleanup observation

Read the fetched-master sweep and marker transition together. Run only `/*/*/CheckpointTempRootSweepTests/eligibility_is_rechecked_after_the_root_lock` (six results) on a committed current-master investigation checkout, through the checkpoint gate. Record the tested SHA and all six retention outcomes. Do not fabricate a red result for this already-present guard.

Use a scripted `ProcessIdentityProbe` and owned temp root to inventory the observed **identity**, verdict and stage: preflight, locked eligibility, marker transition; separately inventory nested preparing/executor observations. A root-marker identity change must not be confused with a second read of the same identity. If a real single-observation deletion path remains, identify the exact identity/transition, give a deterministic safe payload-retention red witness, and return that finding to Plan before changing cleanup. Otherwise close the card item as already satisfied with source and narrow execution evidence. No sleeps, live PID race, broader sweep or deletion in shared temp are required.

### A-2 — Name the Git writer/reader conflict, if any

Inspect current callers (`SourceSnapshot`, `BuildConfinement`, `scripts/lib/checkpoint-source.ps1`) and actual build-slot consumers, including `server/Application/Services/AgentTaskLandService.VerifyWithObserverAsync`, `server/Infrastructure/Git/LandingVerifier.cs`, `scripts/lib/nightly-tests-impl.ps1`, and release scripts. The landing verifier currently holds a lease through both phases; it is an explicit remaining caller, not fixed by changing the checkpoint tool. Inventory call chains rather than assuming a wrapper reaches every gate driver.

Use one owned throwaway repository, a local bare remote, and two linked worktrees. Resolve the common/admin paths with Git; never create locks in this task's or the fleet's real common directory. For each relevant operation (status/index refresh, local fetch/ref update, local commit, worktree administration, and maintenance/repack) record:

1. Exact Git command and version, which checkout invokes it, actual common/admin lock path(s), and whether they are optional or required. Use bounded local process/file tracing when available; trace output stays ignored. No credentials or live remote traffic.
2. The actual checkpoint reader's exit code, sanitized error classification, elapsed time and resulting `SourceObservation`, with and without the writer. Trace2 duration alone does not prove a lock wait; correlate the concrete lock operation. Hold/release a discovered lock only in the fixture, with a deadline and guaranteed cleanup.
3. Whether the reader fails, waits, or completes, and whether worktree content/HEAD changed. Separate common object storage, per-worktree index locks, resource contention and actual source changes.

The red-first requirement for a Git **fix** is a demonstrated reader refusal caused by a particular fixture lock, followed by success when that same lock is released within a proposed bound. If no such failure exists, publish a no-change audit, not a passing test advertised as red-first. `git_timeout`, corrupt output, permission denial and source drift must not become retry-success by default. No new source retry or `GIT_OPTIONAL_LOCKS` change is authorized by this plan without that measurement and a plan amendment.

## Independently landable implementation slices

Each slice has a test-first commit with the real failing assertion, then the fix and its narrow green checkpoint group. Push every commit on its assigned branch. A caller may commission slices separately after the investigation gate and TestDesign; they must not share a live writing worktree. No future slice depends on another slice's uncommitted files.

### S1 — Grok download ownership

Files: `tests/Antiphon.Tests/Infrastructure/GrokSha256PinTests.cs`; new `tests/Antiphon.Tests/Infrastructure/GrokSha256PinIsolationTests.cs`; an internal harness helper in that directory only if extraction improves reuse. Leave `docker/session-runner-grok/Dockerfile`, image-contract assertions and the frozen roster unchanged.

Provide an owned parent-root seam for the real harness operations. In the red setup, map **all** former shared download-path actions, including C# pre/post deletion and shell execution, to `<owned-parent>/grok-download`. Two invocations supplied the same parent still collide under the old algorithm. This plumbing must preserve that algorithm until the red assertion is observed. Never execute the old helper against the real `/tmp/grok-download` just to demonstrate destruction.

V-1/R-1: `Shared_download_sentinel_survives_pin_rejection` seeds a sibling sentinel in that owned legacy directory and executes the malformed-pin path. Red: old pre-delete removes it. Green: rejection still precedes curl, sentinel bytes remain and only invocation scratch is removed.

V-2/R-2: `Overlapping_checks_keep_their_own_downloads` uses two independent shell children and a deterministic barrier after download creation. Finish/clean the first while the second is paused, then release the second. Both matching-artifact validations must succeed and the surviving invocation's artifact must remain until its own cleanup. This exercises the real extracted script; do not use a scheduler delay as the oracle. Bound barriers, kill/join owned children on failure, and retain the assembly process limiter. A root containing spaces/quotes verifies safe path transport.

Run the existing multiline-reject, tampered-file-reject and matching-file-accept methods as three explicit regressions of the shared helper. No full class or roster.

### S2 — Generic wrapper no-build classification

Files: `scripts/build-slot.ps1`, `scripts/lib/build-slot.ps1`, `scripts/test-build-slot.ps1`, `tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs`; the wrapper subsection of `docs/testing-and-build.md` and gate wording in `AGENTS.md`/`server/Bundles/delegate-basics.md` as needed for this exact exception. `InstructionBundleTests.C589_V6_BuildSlotGateIsAStandingRuleReviewEnforces` pins the gate wording: retain the requirement to use the gate and explain that the gate itself classifies known no-build commands. Do not remove its timeout/no-unleased-retry assertions.

V-3/R-3: `C1348_WrapperNoBuildDoesNotAcquire` drives the real wrapper with existing isolated slot/command shims. Red: POST/CMD/DELETE. Green: CMD only, literal argv and working directory preserved, command exit propagated, explicit no-build skip diagnostic, zero broker calls even when the configured fixture broker would refuse or be unreachable.

V-4/R-4: `C1348_WrapperNoBuildClassificationIsConservative` covers `--no-build` after the application's `--`, an opaque PowerShell target, implicit-build run, and an actual build. Those still acquire; build keeps `-maxcpucount`, and timeout starts no child. Reuse the literal launch/interrupt cleanup path; do not create a second execution implementation for unleased children. Update any old expected log text affected by the new branch without dropping argv assertions. Scripts remain ASCII-only.

This slice is independently useful for raw split build/run commands. It does not silently exempt `dotnet exec`, arbitrary DLL commands or a script that happens to run tests.

### S3 — PowerShell checkpoint phase boundary

Files: `scripts/run-checkpoint.ps1`, `scripts/test-run-checkpoint.ps1`, `tests/Antiphon.Tests/Scripts/RunCheckpointScriptTests.cs`, and the corresponding checkpoint/slot owner documentation.

V-5/R-5: `C1348_CheckpointReleasesBeforeRun` records POST, build, DELETE, run, using the existing shims. Red: DELETE is after run. Green: release follows the build/its required settlement and precedes launching the test host. No test launch after failed/canceled build; exactly one release for each grant, including preflight refusal and exceptions. Keep post-build/source and pre-run boundaries.

V-6/R-6: replace `C589_NoBuildStillLeases` with `C1348_NoBuildUsesBindingWithoutLease`. Red: acquisition occurs even with valid reuse. Green: a valid clean stamp permits one no-build run with no slot traffic; missing, foreign-output and wrong-source stamps refuse strict reuse before launch and still make no slot calls. Keep repeat/property/layout binding and fresh results paths. Do not turn `-NoBuild` into `-NoSlot` or skip source checks.

Keep the build lease around the build driver and required build settlement, not around test execution or TRX parsing. Explain build-phase grant versus pure reuse in the CHECKPOINT documentation. Replace the old `POST,build,run,DELETE` assertion with the new ordered assertion; retain count/failure/source assertions.

### S4 — Scheduled and direct checkpoint-tool lease scopes

Files: `tools/Antiphon.Checkpoints/Execution/RunScheduler.cs`, `tools/Antiphon.Checkpoints/Program.cs`, `tools/Antiphon.Checkpoints/Execution/RowRunner.cs`; `tests/Antiphon.Tests/Checkpoints/RunSchedulerTests.cs`, `CheckpointSlotExecutorTests.cs`, and `CheckpointSourceExecutionTests.cs`. Check report formatting/validation consumers (`tools/Antiphon.Checkpoints/Report/CheckpointLine.cs`, `ReportWriter.cs`, `ReportValidator.cs`, `scripts/validate-checkpoint-receipt.ps1`) before changing tokens. Update owner docs and `server/Bundles/stage-review.md` to accept the documented gate-managed no-build exemption. Keep broker and holder identity implementation unchanged.

Scheduled path: retain `BuildOneAsync`'s leased build and before-slot/admission/after-release confinement ordering; `RunRowAsync` acquires only for opaque command rows. Typed test rows use no-build slot metadata. Preserve scheduling and source binding independently of the removed lease.

Direct `row` path: pass lease acquisition into the actual build phase (a small callback/dependency at that I/O boundary is sufficient), or factor that phase into a shared helper. Remove the outer whole-row lease. No-build entry does not probe or acquire from the broker. The lease must dispose before test launch on success and on every early-return/error path. Do not copy a stale grant into a later row. Avoid moving build confinement into a new timing window while extracting the boundary.

V-7/R-7: `scheduled_no_build_rows_leave_capacity_for_another_build` replaces the old `concurrent_rows_hold_distinct_leases_and_their_lines_say_granted` expectation. Use a one-slot loopback broker with ample fixture memory. Hold a typed row at a barrier after its build releases; a separate holder's build request must be granted before releasing that barrier. Observe real broker occupancy and holder release, not elapsed-time improvement. Red: the row occupies the only slot. Builds remain serialized within one scheduler.

V-8/R-8: `direct_row_releases_build_before_test` records direct CLI build/lease/test order; `direct_no_build_row_never_contacts_broker` covers valid strict reuse with a refusing fixture broker. Both currently hold/acquire at the outer CLI boundary. Test valid receipt output and no holder leak on failure/cancellation as well as success.

V-9/R-9: `opaque_command_rows_keep_their_lease` and the existing `executor_row_rejection_does_not_run_command` protect the conservative command boundary. Preserve existing distinct-holder, renewal and cancellation assertions for **leased builds/commands** when updating `executor_reachable_broker_grants_build_and_rows`; do not simply delete those tests because typed rows no longer lease.

V-10/R-10: existing `strict_cli_and_reuse_require_clean_binding` and `changed_admission_and_driver_boundaries_refuse` keep source/build refusals independent of leasing. The new integrated tests must assert complete report/source identity and fresh nonzero TRX counts. A successful GET/POST or fake driver entry alone is insufficient proof.

## Checkpoint proposal for TestDesign

Names beginning `C1348_` and the new S1/S4 methods are proposed tests, not claims that they exist. TestDesign must read the final helper seams, bind every ordinary V/R and post-land guard, and move this table to the end of its required `## Verification design` section. The initial gate means this is not yet a Code-ready manifest.

The following closed selection is deliberately narrow. No assembly, namespace, Unit category or frozen-roster run is proposed. Existing method regressions may use a combined **method** filter, not a class wildcard. OR operands use the pinned TUnit hint-compatible suffix wildcard; verify the exact named methods in TRX and refuse any extra selection. A-1/A-2 evidence is commissioned separately on the audited SHA and is not falsely included in implementation counts.

Lane mapping: CP-1..CP-4 = Linux; CP-5..CP-10 = Linux with platform branches driven through the existing offline seams; CP-11..CP-16 = Linux portable/loopback; CP-17..CP-19 = Windows native launcher/phase parity; CP-20..CP-21 = Linux instruction contracts. `Group` carries the lane because the importer does not accept an arbitrary Lane column. At dispatch use `--rows` for that lane; a missing Windows lane is incomplete, not skipped. Build reuse is allowed only inside the same `After` group and the same committed SHA. Each slice has its own Windows build when needed, so it can qualify and land independently.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1348-grok/` | linux-grok-sentinel | `/*/*/GrokSha256PinIsolationTests/Shared_download_sentinel_survives_pin_rejection` | V-1, R-1 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-2 | S1 | CP-1 | linux-grok-overlap | `/*/*/GrokSha256PinIsolationTests/Overlapping_checks_keep_their_own_downloads` | V-2, R-2 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-3 | S1 | CP-1 | linux-grok-rejections | `/*/*/GrokSha256PinTests/(Runner_grok_sha_rejects_multiline_pin*)\|(Runner_grok_sha_rejects_tampered_file*)` | R-1, R-2 | 2 executed, 0 failed/skipped | 2 | 1 |
| CP-4 | S1 | CP-1 | linux-grok-success | `/*/*/GrokSha256PinTests/Runner_grok_sha_accepts_matching_file` | R-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c1348-wrapper/` | linux-wrapper-no-build | `/*/*/BuildSlotScriptTests/C1348_WrapperNoBuildDoesNotAcquire` | V-3, R-3 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-6 | S2 | CP-5 | linux-wrapper-classification | `/*/*/BuildSlotScriptTests/C1348_WrapperNoBuildClassificationIsConservative` | V-4, R-4 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-7 | S2 | CP-5 | linux-wrapper-controls | `/*/*/BuildSlotScriptTests/(C589_WrapperRunsUnderLease*)\|(C589_WrapperTimeout*)\|(C800_WrapperPassesWildcardArgvLiterally*)` | R-3, R-4 | 3 executed, 0 failed/skipped | 3 | 2 |
| CP-8 | S3 | `tests/Antiphon.Tests -> bin-c1348-script/` | linux-checkpoint-release | `/*/*/RunCheckpointScriptTests/C1348_CheckpointReleasesBeforeRun` | V-5, R-5 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-9 | S3 | CP-8 | linux-checkpoint-reuse | `/*/*/RunCheckpointScriptTests/C1348_NoBuildUsesBindingWithoutLease` | V-6, R-6 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-10 | S3 | CP-8 | linux-checkpoint-build-gate | `/*/*/RunCheckpointScriptTests/(C589_SlotGranted*)\|(C589_SlotTimeout*)` | R-5, R-6 | 2 executed, 0 failed/skipped | 2 | 2 |
| CP-11 | S4 | `tests/Antiphon.Tests -> bin-c1348-tool/` | linux-scheduler-capacity | `/*/*/CheckpointSlotExecutorTests/scheduled_no_build_rows_leave_capacity_for_another_build` | V-7, R-7 | 1 executed, 0 failed/skipped | 1 | 6 |
| CP-12 | S4 | CP-11 | linux-direct-row | `/*/*/CheckpointSlotExecutorTests/(direct_row_releases_build_before_test*)\|(direct_no_build_row_never_contacts_broker*)` | V-8, R-8 | 2 executed, 0 failed/skipped | 2 | 2 |
| CP-13 | S4 | CP-11 | linux-command-gate | `/*/*/CheckpointSlotExecutorTests/(opaque_command_rows_keep_their_lease*)\|(executor_row_rejection_does_not_run_command*)\|(executor_cancellation_releases_lease_and_holder*)` | V-9, R-9 | 3 executed, 0 failed/skipped | 3 | 2 |
| CP-14 | S4 | CP-11 | linux-source-binding | `/*/*/CheckpointSourceExecutionTests/(strict_cli_and_reuse_require_clean_binding*)\|(changed_admission_and_driver_boundaries_refuse*)` | V-10, R-10 | 2 executed, 0 failed/skipped | 2 | 2 |
| CP-15 | S4 | CP-11 | linux-scheduler-order | `/*/*/RunSchedulerTests/(builds_never_overlap_even_with_free_slots_and_width_two*)\|(serial_runs_one_activity_at_a_time_builds_included*)\|(pty_project_rows_run_alone*)` | R-7 | 3 executed, 0 failed/skipped | 3 | 2 |
| CP-16 | S4 | CP-11 | linux-holder-renewal | `/*/*/CheckpointSlotExecutorTests/executor_renews_until_driver_finishes_then_releases` | R-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-17 | S2 | `tests/Antiphon.Tests -> bin-c1348-wrapper-win/` | windows-wrapper-parity | `/*/*/BuildSlotScriptTests/(C1348_WrapperNoBuildDoesNotAcquire*)\|(C1348_WrapperNoBuildClassificationIsConservative*)` | R-3, R-4 | 2 executed, 0 failed/skipped | 2 | 8 |
| CP-18 | S3 | `tests/Antiphon.Tests -> bin-c1348-script-win/` | windows-script-parity | `/*/*/RunCheckpointScriptTests/(C1348_CheckpointReleasesBeforeRun*)\|(C1348_NoBuildUsesBindingWithoutLease*)` | R-5, R-6 | 2 executed, 0 failed/skipped | 2 | 8 |
| CP-19 | S4 | `tests/Antiphon.Tests -> bin-c1348-tool-win/` | windows-tool-parity | `/*/*/CheckpointSlotExecutorTests/(direct_row_releases_build_before_test*)\|(direct_no_build_row_never_contacts_broker*)` | R-8 | 2 executed, 0 failed/skipped | 2 | 8 |
| CP-20 | S2 | CP-5 | linux-wrapper-instructions | `/*/*/InstructionBundleTests/C589_V6_BuildSlotGateIsAStandingRuleReviewEnforces` | R-3, R-4 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-21 | S4 | CP-11 | linux-stage-instructions | `/*/*/InstructionBundleTests/(C589_V6_BuildSlotGateIsAStandingRuleReviewEnforces*)\|(each_stage_bundle_is_ascii_and_under_the_size_cap*)` | R-7, R-9 | 7 executed, 0 failed/skipped | 7 | 1 |

### Execution and evidence obligations

Before red-first execution commit/push the test-only slice and use fresh isolated output. A compilation error, unavailable shell, fixture failure, zero results or timeout is not the intended red. The red checks use the same exact methods as their green rows; TestDesign must bind these test-first invocations in the final closed manifest rather than leave them as ad hoc commands. New safety-critical guards need separately mapped post-land PCs; baseline red-first authoring does not claim those PCs have run.

Use the checkpoint tool `run --plan <this-file> --rows <lane-and-slice-rows> --expected-source-sha <full-sha>` once per committed slice group, through the documented build-slot bootstrap. Wait until exit is not 75. Do not wrap the slot-owning PowerShell checkpoint driver in another slot. Until the changed policy is landed, the commissioning task's existing gate contract still applies to its outer verification driver; fixtures test the changed behavior against isolated brokers.

Report every CP with the unedited CHECKPOINT line, actual source/build identities, counts, skips, waits and reruns; qualify source receipts with `validate-checkpoint-receipt.ps1`. Keep source unchanged during a run. Code and Review run `scripts/check-evidence-diff.ps1` over their recorded base..HEAD. Retain generated TRX/JSON/logs ignored; remove owned alternate outputs with the supported custody checks and report any retained residue. No raw recursive cleanup of unknown directories.

### Cost

All values are estimates; no build or test was run during Plan. The proposed green ordinary floor is **71 minutes** (sum of EstimatedMinutes), with **40 TUnit results** and seven isolated build outputs. Counts measure test results, not internal scenario assertions.

Authoring: S1 3-5 hours, S2 2-4 hours, S3 3-5 hours, S4 6-10 hours; total **14-24 hours**, plus A-1/A-2 **2-4 hours** and separate TestDesign **2-3 hours**. Test-first red setup/runs are provisionally **25-40 minutes** beyond the green floor; TestDesign must include their precise build/filter rows before Code commissioning. Ordinary execution budget is therefore provisionally **96-111 minutes**, excluding queue wait and genuine failure-driven reruns. Code `ExpectAbout` includes authoring plus that final ordinary table, not post-land PCs.

Post-land Mutation estimate: **90-150 minutes**, provisional until TestDesign binds the independently bypassable guards and compiling mutations. It includes precise-method red/restore/fresh-build/green cycles for directory ownership, no-build classification, release ordering, command gating, binding and cancellation, plus discovery/reporting. Never replace this with a claimed PC count of zero. Estimated total remaining engineering and execution is **21.1-35.4 hours** (authoring + investigations + TestDesign + ordinary execution + Mutation); review/landing and slot queue time are additional.

The confirmed savings are fewer occupied build-slot seconds during long no-build runs, not a promised reduction in test duration. Measure grant-to-release and occupancy at barriers; the historical roughly 35-minute roster holds motivate the change but are not a benchmark to rerun. Narrow filters avoid paying another full roster cost.

## Handoff and stopping conditions

Next is **Investigate**, limited to A-1/A-2 because the card's single-observation premise is false in the inspected code and its Git retry premise is unproven. Return the measured source SHA, cleanup identity/call sequence, concrete lock/reader matrix, and the remaining caller inventory. If no new defect exists, record those two items as no-change and proceed to TestDesign for S1-S4. If a real gap exists, amend this plan with its exact red witness and affected slice before TestDesign.

TestDesign supplies the required `## Verification design`, complete guard-to-PC map, final red/green closed checkpoint selection, native Windows script parity where needed, and numeric Code/Mutation floors. Only then hand off to Code. Code proceeds to ordinary Review, caller-controlled master land, and the normal post-land verification companion. This plan authorizes no dispatch, fleet changes, rc work or implementation in the Plan checkout.
