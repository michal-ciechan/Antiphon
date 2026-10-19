# CARD-1348: cleanup contract and test-isolation slices

Status: **Plan complete; TestDesign next.** Verification design is a separate stage in this dispatch. This document supersedes the earlier plan's investigation gate and carries its S1-S4 forward. It selects an additional, separately landable C1 nested-custody hardening slice. The checkpoint selection below is closed planning input; TestDesign must bind the guard/positive-control inventory before Code.

Target: **master only**. No rc changes. This Plan dispatch changes only this document; no product/test implementation, builds or suite runs.

## Outcome and priority

1. **S1, first:** remove the Grok harness's shared destructive download path. This is the clearest isolation defect.
2. **S2-S4, next:** release build capacity before known no-build test execution, through the generic wrapper, the PowerShell checkpoint driver, and both checkpoint-tool entry points. Each caller needs its own change.
3. **C1, lower priority:** require two dead observations of the same nested process identity within a sweep, with a final custody revalidation before changing the root marker. This is a deliberately stronger cleanup contract, not a reproduced production incident.
4. **No change:** root-owner observation, CARD-1346's disappeared-process repair, and Git readers. Keep landing/nightly/release callers in the explicit remaining inventory; S1-S4 do not complete those paths.

The acceptance boundary is these named callers and the checkpoint temp-root sweep. It is not a fleet-wide promise that only builds hold leases. Opaque commands may build and remain leased. No host-budget, broker, scheduler-width, provider, release or deployment change is selected.

## Evidence and ground truth

Read the full Antiphon CARD-1348 at revision 4 (status Review), its description and both requested artifacts. The card is board-scoped. No card write was made.

- This task: feat/card-task-d4cf796f, assigned base a0f3c4f3e728655933da1782981a88fb2b7b3a56.
- Prior plan: feat/card-task-c01e8c96, **55179cd34bae82070ff647186a98202537c0726f**, docs/superpowers/plans/2026-10-19-card-1348-test-isolation-plan.md.
- Measurements: feat/card-task-acad7c25, **a195eaeb2f537de07c3f8a3e57161f8aa4f83630**, docs/investigations/2026-10-19-card-1348-a1-a2-measurements.md. The brief's abbreviated a195eaea does not resolve; the named branch supplies the actual a195eaeb prefix.
- Measurement source: **d5dec9555bd726ad11bb30073c1733d6b3e7bb0e**. Its six root-lock retention results passed, with 0 failed/skipped and a qualified clean receipt. The ten diagnostic scenarios and two failed synthetic stronger-contract assertions are separate from those six TUnit results.
- Local source comparison against that measurement SHA showed no differences in the audited HostCleanup checkpoint directory, checkpoint tool, build-slot/run-checkpoint scripts, Grok pin test, landing service or nightly test implementation. Relevant implementations and test bodies were also inspected here.
- Owner policies: AGENTS.md; docs/project-context.md; docs/testing-and-build.md (checkpoint manifest/tool, slots, temp custody); docs/orchestration-loop.md (stage handoff and D-2); docs/ops-http.md. TestDesign's owner is server/Bundles/stage-test-design.md.

The artifacts remain identifiable by immutable SHA even if their branches later retire. Use git show with the SHA and repository-relative path above if they have not landed in the next checkout. This plan is self-contained for the selected implementation boundary; historical evidence is not reclassified as work run here.

| Card assumption or request | What the code/evidence actually does | Plan consequence |
|---|---|---|
| Root sweep deletes after one dead owner observation | CheckpointTempRootSweep.SweepOnce preflight, Eligible under the root gate, and TestRootGuard.MarkDeleting each observe the owner. A changed preflight owner can mean one observation of R plus two of R2, not three of one identity. Six existing locked-eligibility cases retain payload, marker and index. | No fourth owner read, cross-pass latch, or invented root-owner red. Preserve the existing marker-generation binding. |
| Every nested identity already gets equivalent protection | Eligible checks the preparing Starter once; otherwise ToolCopyCleanup.ObserveExecutor selects executor receipt identity, then launched fallback. MarkDeleting rechecks the root owner only. | C1 adds the stronger nested contract explicitly; preserve receipt precedence and unknown-outcome retention. |
| The two failed assertions prove live-payload loss | In the diagnostic, N returned Dead once; the hypothetical next AliveSame result was never requested. The two retention expectations failed. No real false-dead observation or live-payload incident was established. | Use these as synthetic witnesses for the newly selected contract, not historical production regressions. |
| CARD-1346 still needs implementation | ProcessIdentityProbe handles disappearing /proc data and CheckpointTestScope.Register handles disappeared children on the audited source. | No duplicate repair. |
| Grok scratch is already per test | GrokSha256PinTests.Run allocates GUID scratch but deletes /tmp/grok-download before and after the shell invocation. Extracted Dockerfile text still uses that fixed path. The static Gate guards only deletion within one process. | S1 owns all artifact paths per invocation; test the old algorithm only inside a private redirected fixture. |
| One wrapper change fixes all leased no-build runs | build-slot.ps1 acquires before the whole command; run-checkpoint.ps1 acquires before build and releases after tests. RunScheduler separately leases its build and every row; Program.Row holds an outer lease around RowRunner. | S2, S3 and both S4 entry points are distinct acceptance paths. |
| Shared Git storage establishes reader lock contention | Held index/HEAD/packed-ref/remote-ref/maintenance locks and a prepared HEAD transaction did not refuse the three actual readers. All 13 qualified triplets were known/clean, with nonempty two-file ledgers. Of 416 traced Git exits, 65 were expected config-no-match exit 1; the rest were 0. | No retry, timeout increase, common-directory mutex or optional-lock policy change. A future fix needs a specific lock/reader failure and release control. |
| Source/build binding is globally shared | CheckpointBuildBinding lives under the individual output; SourceSnapshot and BuildConfinement retain source and filesystem checks independently of leases. | Keep per-output stamps and strict source checks. No output relocation or roster rerun. |
| Landing and nightly inherit S2 automatically | Landing leases VerifyUnderSlotAsync across build and tests. Nightly launches its own build and native test hosts without an acquisition in the inspected direct chain. Release-cut uses that nightly path. | Carry these as separate remaining work; do not claim they are corrected by this batch. |

### Placement and evidence lanes

Read GET /api/runner-defaults and GET /api/session-runners at approximately **2026-10-19 16:54 UTC**. Defaults revision 2 had no per-kind overrides. The configured default was a Linux runner that was available/dispatchEligible but **draining and not accepting new work**. Another advertised Linux runner and a Windows runner were accepting work. Availability alone is insufficient for dispatch.

These are dated observations, not a placement prescription. Before each dispatch reread both routes and use their current routing/drain rules. Omit -Runner unless explicitly pinning a single host. Omit -Platform for portable work; use Linux for Grok/native Linux checks and Windows for the native parity rows. -Platform Any removes an inherited OS pin. No fleet hostname, workspace location or fixed endpoint belongs in the commands.

Every checkpoint's Group names its lane. Use only that lane's selected rows. Windows parity is required for S2-S4/C1 completion, not silently skipped on Linux. No session-input or waiting-session behavior is added or changed.

## Decisions

These are choices made under the brief's instruction to decide the worthwhile slices, not unresolved defaults or requests for new operator settings.

- **D-1 — Do S1-S4, separately.** All four have a confirmed source-level defect or excessive lease lifetime and a narrow behavioral witness. S1 has the highest isolation priority. Keep their original numbering for caller continuity; do not hide nested cleanup inside S4.
- **D-2 — Preserve root-owner and Git-reader behavior.** The measured premise is wrong for the root owner and unsupported for Git readers. Reject an extra owner check, generic retries, longer timeouts and a repository-wide lock. The bounded Git audit is not proof that contention can never occur.
- **D-3 — Select C1 as bounded hardening, after the confirmed fixes.** Two observations must refer to the same complete nested identity and unchanged custody generation within one root-lock acquisition. The first is eligibility; the second is final revalidation before marker transition/deletion. Either non-Dead result or changed/unprovable evidence retains the root. No sleeps, cross-sweep history, new five-minute delay or PID-only comparison. This catches a contradictory second observation; it does not prove the first was a real false-dead or guarantee immunity to persistent probe errors. Deferring all nested checks would preserve today's single observation, while a cross-pass scheme adds persistent state and retention delay without measured need; both are rejected for this selected contract.
- **D-4 — Keep the stronger rule local to the temp-root sweep.** Do not silently change ToolCopyCleanup.Remove, session cleanup, output deletion or general process-stop policy. Preparing, executor receipt and launched fallback are distinct evidence sources. A proven not-started run has no process identity: revalidate its no-launch evidence twice, rather than invent a PID or require a nonexistent process observation.
- **D-5 — Exempt only recognizable no-build execution.** Resolve an actual dotnet/dotnet.exe application, require the run verb and a literal --no-build option before the application's -- separator. Conservative parsing must not mistake an option value for that flag. Unknown/opaque commands, implicit-build run, build/test/publish/pack/msbuild retain leasing. No generic bypass flag and no new dotnet exec/DLL exemption. Keep -NoSlot operator-only. A checkpoint typed TUnit row is known no-build after its owned build.
- **D-6 — Move the lease boundary, retain admission.** The build phase includes the native build and its necessary immediate custody/binding settlement. Release before the test child or TRX/report processing. Preserve before-slot ledger capture, admission after grant, in-build source checks and after-release verification; do not move a check into a weaker timing window. Always release an acquired lease on failure/cancellation. Broker capacity, FIFO, CPU cap, memory floor, timeout exit 4 and documented unreachable fallback remain unchanged.
- **D-7 — Keep scheduling and report meaning.** One in-run build lane, existing row width, Serial/Pty exclusion and assembly process limiters remain. Removing test leases removes one cross-process test cap, so neither throughput nor memory improvement is guaranteed. Pure no-build rows report slot=skipped, slot-reason=no-build, waited=0s; build facts retain actual grant/wait. A combined build/run row may report its completed build's grant only as an explicitly documented build-phase fact. No stale grant may masquerade as a held test lease. Existing receipt structures can carry these tokens; change schema only if TestDesign finds a concrete incompatible consumer.
- **D-8 — Own Grok execution scratch, preserve image validation.** Rewrite the execution copy's fixed path to a quoted environment variable under its GUID scratch, require replacement, and reject executable legacy references. Preserve Dockerfile extraction/checksum logic and production image path. A larger process-local/global delete lock retains shared destructive state and is rejected.
- **D-9 — Bound this batch and retain the caller backlog.** Landing/nightly/release are explicit follow-up scopes below. No rc run, release, restart, budget change or broad suite belongs to this plan. Preserve historical outcomes and incomplete verification under D-2; no documentation-only edit can turn missing checks green.
- **D-10 — TestDesign remains separate.** Use the closed selection and red witnesses below, inspect final seams, and provide the required Verification design plus guard-to-PC map before Code. No further investigation is needed to repeat the already answered A-1/A-2 questions.

## Independently landable implementation slices

Each slice has a test-first commit with the real failing assertion, then the fix and its narrow green checkpoint group. Push every commit on its assigned branch. A caller may commission slices separately after TestDesign; they must not share a live writing worktree. No future slice depends on another slice's uncommitted files.

### S1 — Grok download ownership

Files: `tests/Antiphon.Tests/Infrastructure/GrokSha256PinTests.cs`; new `tests/Antiphon.Tests/Infrastructure/GrokSha256PinIsolationTests.cs`; an internal harness helper in that directory only if extraction improves reuse. Leave `docker/session-runner-grok/Dockerfile`, image-contract assertions and the frozen roster unchanged.

Provide an owned parent-root seam for the real harness operations. In the red setup, map **all** former shared download-path actions, including C# pre/post deletion and shell execution, to `<owned-parent>/grok-download`. Two invocations supplied the same parent still collide under the old algorithm. This plumbing must preserve that algorithm until the red assertion is observed. Never execute the old helper against the real `/tmp/grok-download` just to demonstrate destruction.

V-1/R-1: `Shared_download_sentinel_survives_pin_rejection` seeds a sibling sentinel in that owned legacy directory and executes the malformed-pin path. Red: old pre-delete removes it. Green: rejection still precedes curl, sentinel bytes remain and only invocation scratch is removed.

V-2/R-2: `Overlapping_checks_keep_their_own_downloads` uses two independent shell children and a deterministic barrier after download creation. Finish/clean the first while the second is paused, then release the second. Both matching-artifact validations must succeed and the surviving invocation's artifact must remain until its own cleanup. This exercises the real extracted script; do not use a scheduler delay as the oracle. Bound barriers, kill/join owned children on failure, and retain the assembly process limiter. A root containing spaces/quotes verifies safe path transport.

Run the existing multiline-reject, tampered-file-reject and matching-file-accept methods as three explicit regressions of the shared helper. No full class or roster.

### S2 — Generic wrapper no-build classification

Files: `scripts/build-slot.ps1`, `scripts/lib/build-slot.ps1`, `scripts/test-build-slot.ps1`, `tests/Antiphon.Tests/Scripts/BuildSlotScriptTests.cs`; the wrapper subsection of `docs/testing-and-build.md` and gate wording in `AGENTS.md`/`server/Bundles/delegate-basics.md` as needed for this exact exception. `InstructionBundleTests.C589_V6_BuildSlotGateIsAStandingRuleReviewEnforces` pins the gate wording: retain the requirement to use the gate and explain that the gate itself classifies known no-build commands. Do not remove its timeout/no-unleased-retry assertions.

V-3/R-3: `C1348_WrapperNoBuildDoesNotAcquire` drives the real wrapper with existing isolated slot/command shims. Red: POST/CMD/DELETE. Green: CMD only, literal argv and working directory preserved, command exit propagated, explicit no-build skip diagnostic, zero broker calls even when the configured fixture broker would refuse or be unreachable.

V-4/R-4: `C1348_WrapperNoBuildClassificationIsConservative` covers `--no-build` after the application's `--`, a token consumed as another option's value, a similarly named non-dotnet command, an opaque PowerShell target, implicit-build run, and an actual build. Those still acquire; build keeps `-maxcpucount`, and timeout starts no child. Reuse the literal launch/interrupt cleanup path; do not create a second execution implementation for unleased children. Update any old expected log text affected by the new branch without dropping argv assertions. Scripts remain ASCII-only.

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

V-7/R-7: `scheduled_no_build_rows_leave_capacity_for_another_build` replaces the old `RunSchedulerTests.concurrent_rows_hold_distinct_leases_and_their_lines_say_granted` expectation. Use a one-slot loopback broker with ample fixture memory. Hold a typed row at a barrier after its build releases; a separate holder's build request must be granted before releasing that barrier. Observe real broker occupancy and holder release, not elapsed-time improvement. Red: the row occupies the only slot. Builds remain serialized within one scheduler.

V-8/R-8: `direct_row_releases_build_before_test` records direct CLI build/lease/test order; `direct_no_build_row_never_contacts_broker` covers valid strict reuse with a refusing fixture broker. Both currently hold/acquire at the outer CLI boundary. Test valid receipt output and no holder leak on failure/cancellation as well as success.

V-9/R-9: `opaque_command_rows_keep_their_lease` and the existing `executor_row_rejection_does_not_run_command` protect the conservative command boundary. Preserve existing distinct-holder, renewal and cancellation assertions for **leased builds/commands** when updating `executor_reachable_broker_grants_build_and_rows`; do not simply delete those tests because typed rows no longer lease.

V-10/R-10: existing `strict_cli_and_reuse_require_clean_binding` and `changed_admission_and_driver_boundaries_refuse` keep source/build refusals independent of leasing. The new integrated tests must assert complete report/source identity and fresh nonzero TRX counts. A successful GET/POST or fake driver entry alone is insufficient proof.

### C1 — Nested identity confirmation before root deletion

This is an additional independent hardening slice, not a renumbering of S1-S4 and not a claim that A-1 found a live process deleted. Prefer landing S1-S4 first. C1 can be commissioned separately once its TestDesign is complete.

Files: src/Antiphon.HostCleanup/Checkpoints/CheckpointTempRootSweep.cs; a small internal observation/evidence helper in that directory if needed; ToolCopyCleanup.cs only to expose/reuse the existing executor-selection rule without changing Remove; tests/Antiphon.Tests/Checkpoints/CheckpointTempRootSweepTests.cs; docs/testing-and-build.md's temp-custody section. TestRootGuard.MarkDeleting keeps its marker/owner contract. Do not move nested process probing into generic file deletion.

Under the root gate, capture the bounded nested inventory and, for each run, its path, custody phase/generation, selected evidence source and exact ProcessIdentity (host, boot, namespace, PID and start time fields). Bind the relevant journal and executor-receipt content/presence; comparing only the PID, only the first verdict or only a mutable object reference is insufficient.

Immediately before MarkDeleting, revalidate containment, bounded inventory, marker binding and the nested evidence. Refuse if a nested run appears/disappears, journal/receipt is missing or malformed, phase/evidence source/identity changes, the pass deadline expires, or the second observation is anything other than Dead. A different identity with a Dead verdict does not complete the old identity's pair. Do not retry the new identity inside the same delete attempt. Both checks occur while the root lock is held; keep the existing ten-minute grace, five-minute admission interval, entry/byte/root/time bounds and partial-deletion behavior. Every resumed pass checks custody again.

For a proven not-started record, recheck the same phase, absent launched identity and absent executor receipt; keep the existing structural no-launch rule. Ambiguous launch-attempted state stays retained. This closes no general hostile same-account writer race: the existing custody and filesystem trust boundary remains.

V-11/R-11: proposed parameterized method **nested_second_observation_must_be_dead**, nine results: preparing starter, launched fallback and executor receipt, each with Dead then AliveSame/Unknown/ReusedPid. The root owner stays Dead. Assert the same identity is probed exactly twice, zero completed roots, unchanged payload and retained marker/index. Current code deletes after the first nested Dead: that is the intended synthetic red. Include both active and resumed-deleting root states as internal arrangements without falsely multiplying TUnit counts.

V-12/R-12: proposed **nested_identity_change_retains_root**, five results: identity replacement, custody-phase change, evidence-source replacement, added/removed nested membership, malformed/missing custody. Drive changes through deterministic callbacks between the eligibility and final revalidation; each method invocation may exercise both named subcases. Require payload/marker/index retention and a specific refusal. A replacement identity that is also Dead must not inherit the earlier observation.

V-13/R-13: proposed **nested_stable_evidence_allows_cleanup**, four results: preparing, launched fallback, executor receipt, proven not-started. Assert complete removal and index unregistration; identity cases get two reads of the same identity, not-started gets no invented process probe. Retain executor-receipt precedence even if a different launched fallback is Dead. Exercise the final pass with an exhausted fake deadline as an additional retention arrangement; no wall-clock sleep.

V-14/R-14: retain the six-case eligibility_is_rechecked_after_the_root_lock, resumed_deletion_rechecks_every_veto and marker_survives_partial_deletion regressions. These are passing baseline controls, not new defect witnesses.

All sweeps use marked roots inside an exclusively owned sandbox and an injected cleanup context/probe/clock. Set TMPDIR/TEMP/TMP to owned scratch before test-host startup so TempDir's registration hooks cannot sweep shared OS temp. Keep cleanup through CheckpointTestScope disposal, including a second disposal after clearing an intentional veto. Never delete shared temp to make the witness fail.

## Remaining callers and completion boundary

| Caller | Current chain | Selected disposition and future acceptance |
|---|---|---|
| Generic wrapper | scripts/build-slot.ps1 -> Enter-AntiphonBuildSlot -> wrapped process -> release | S2 fixes recognized literal no-build commands only. Opaque scripts remain leased. |
| PowerShell checkpoint | run-checkpoint.ps1 -> acquire -> build -> run -> finally release | S3 splits the phases. Strict reuse still validates its binding before any test launch. |
| Checkpoint tool | RunScheduler.BuildOneAsync and RunRowAsync; Program.Row -> RowRunner.RunAsync | S4 fixes both entry points. Build and command rows retain leases. |
| Landing | server/Infrastructure/Git/LandingVerifier.cs -> AgentTaskLandService.VerifyWithObserverAsync -> VerifyUnderSlotAsync | **Remaining work.** Whole verification still leased; null injected gate is already unbudgeted. A later slice must preserve isolated artifacts, observer/cancellation, fresh nonzero passing TRX and timeout semantics while witnessing grant/build/release/test order. Inspect tests/Antiphon.Tests/Application landing-verification fixtures at that dispatch. Estimate 2-4 hours authoring plus 20-35 minutes narrow Linux/Windows checks, provisional. |
| Nightly | scripts/lib/nightly-run-impl.ps1 -> nightly-tests.ps1 -> nightly-tests-impl.ps1 -> Invoke-NightlyOwnedProcess -> nightly-owned-process.ps1 | **Remaining work.** Direct solution build and native test executables have no slot acquisition in the audited chain. An external wrapper, if one exists in deployment, is not established by this source audit; S2 would still lease that entire opaque script. Inventory actual Windmill invocation and detect-only watchdog before designing a build-only gate. No live nightly run is needed for the inventory. Estimate 1-2 hours inventory; implementation/testing requires its own plan. |
| Release | scripts/release-cut.ps1 -> nightly-run.ps1 rc profile; candidate/publish/promote use release/Git paths | **Remaining work via nightly.** No separate checkpoint-tool fix reaches it. Preserve release qualification requirements. This master-only batch authorizes no rc execution. |

Keep this inventory on CARD-1348's follow-up scope when the selected slices land; do not close a fleet-wide build-only-leasing claim on their receipts. The caller owns board revisions and any separately commissioned work. No duplicate root-owner or Git-retry card is warranted by these measurements.

## Verification scope for TestDesign

This section defines the proposed closed ordinary selection, not a completed **Verification design**. TestDesign must inspect the touched tests/fixtures, bind every safety-critical guard to a distinct compiling method-scoped PC, and confirm each exact filter and count before handing off to Code. No async session delivery or input-waiting behavior changes; the delivery-inventory item should say why it is inapplicable.

Keep S1-S4's V-1..V-10/R-1..R-10 and C1's V-11..V-14/R-11..R-14. Add R-15 for the existing build-confinement boundaries: scheduler_stamps_non_output_files_before_the_slot_and_verifies_after_release and non_output_file_changed_before_slot_release_is_refused. CP-26 protects these boundaries after S4. Positive controls must cover scratch ownership, actual option classification, release ordering, command leasing, source/build binding, cancellation/holder cleanup and each independently bypassable nested-custody veto. A baseline red witness is not a substitute for a post-land PC.

### Red-first runs and interpretation

For each selected slice, first commit and push compiling tests plus only the seams necessary to run the old behavior safely. Then run the following subset of the **same CP identities** before its fix. Commit/push the implementation and run that slice's full green group. No tests run in this Plan task.

| Slice | Test-first CP selection | Required red oracle | EstimatedMinutes |
|---|---|---|---:|
| S1 | CP-1, CP-2 | Redirected legacy sentinel removed; one paused sibling loses its download when the other cleans up. All old host-path actions must be redirected inside owned fixture scratch. | 14 |
| S2 | CP-5 | Real wrapper makes slot calls for literal no-build execution; expected zero. | 12 |
| S3 | CP-8, CP-9 | Release comes after test launch; valid strict reuse still contacts the slot broker. | 14 |
| S4 | CP-11, CP-12 | Typed row occupies the sole slot at the barrier; direct build/run release order is wrong and direct strict reuse contacts broker. | 14 |
| C1 | CP-22 | Nine stronger-contract cases remove payload after the first nested Dead instead of retaining on the scripted second non-Dead verdict. | 12 |

This schedules **17 TUnit results** in the red phase and five isolated red builds, estimated **66 minutes**. The red filters are the CP rows below, not a second implicit roster. Use separate fresh outputs for each test-only SHA (or guarded cleanup before recreating the exact row output); never reuse output across a test/fix commit. Report red and green SHA, run ID, counts and intended assertion separately. Expected red is exit 1 with that assertion and fresh TRX, never a clean receipt; build/fixture failure, zero results or deadline expiry is not evidence for the defect.

Classification-negative cases, opaque-command checks, existing source/cleanup guards and stable-dead controls may already pass before the change. Their role is regression protection and later method-scoped mutation, not fabricated red-first defects. TestDesign must amend the table for any additional ordinary selection it finds necessary; Code may not add broad runs silently.

### Execution contract

Run the checkpoint tool once per committed slice/phase/lane group, selecting only that group's CP IDs; pass the exact expected-source-sha. Bootstrap its build through scripts/build-slot.ps1 and use the tool's run --plan / wait flow from docs/testing-and-build.md. Wait until exit is not 75; own all child processes through completion. Keep one slot-owning layer. Until each new gate rule lands, outer verification obeys the existing gate policy; isolated shims/loopback brokers exercise the proposed behavior.

Group carries the lane because the importer has no Lane column. Build reuse is only within the same After group and committed SHA. Combined method filters use the pinned hint-compatible suffix wildcard; TRX must contain exactly the named methods, with the stated parameter expansion and no extra selection. No full class, namespace, Unit, frozen roster or assembly run is part of this list.

Keep source unchanged during runs. Every CP retains its unedited CHECKPOINT line, source/build identities, fresh TRX, source receipt, actual count, skipped count, wait and reruns. Validate green receipts with scripts/validate-checkpoint-receipt.ps1. Code and Review run scripts/check-evidence-diff.ps1 over base..HEAD. Generated JSON/TRX/logs remain ignored. Remove only owned alternate outputs with supported custody checks; report retained residue. No retry outside the build-slot gate after timeout.

### Cost

Estimates, not measurements from this Plan. The green table totals **145 minutes, 86 TUnit results and nine isolated builds**. Linux builds are budgeted at 12 minutes and Windows at 14, more conservatively than the first plan's 6/8-minute build rows: A-1's qualified combined checkpoint held its lease 576 seconds. This is planning allowance, not an inferred pure-build duration or performance promise.

Red-first adds **66 minutes**, so the closed ordinary budget is **211 minutes (3 h 31 m)** before slot waits or justified reruns. S1-S4 alone are 115 green + 54 red = **169 minutes**; C1 adds 30 green + 12 red = **42 minutes**. Grouped counts are planned test results, not assertion counts.

| Slice | Authoring estimate | Green + red execution | Value / tradeoff |
|---|---:|---:|---|
| S1 | 3-5 h | 16 + 14 = 30 min | Removes confirmed shared deletion; cheapest direct isolation benefit. |
| S2 | 2-4 h | 31 + 12 = 43 min | Makes split raw build/run callers useful without new bypass flags. |
| S3 | 3-5 h | 30 + 14 = 44 min | Fixes the separate script phase boundary and strict-reuse behavior. |
| S4 | 6-10 h | 38 + 14 = 52 min | Covers scheduled/direct entry points, reports and custody; highest implementation complexity. |
| C1 | 3-5 h | 30 + 12 = 42 min | Adds same-identity confirmation at modest runtime cost; no measured live-loss incident or speed benefit. |

Total authoring **17-29 h**; separate TestDesign **3-4 h**. Provisional post-land Mutation **135-225 minutes** (S1-S4 90-150 plus C1 45-75) must be replaced by the exact guard/PC cost in TestDesign, with separate method-scoped red/restore/green cycles. Total selected work is provisionally **25.8-40.3 h**, excluding Review/landing and slot queues. Remaining landing/nightly inventory above is not included.

Expected benefit is reduced occupied build-slot time during no-build tests and elimination of shared scratch destruction. Do not promise a wall-time reduction or rerun the historical 35-minute roster as a benchmark. Prove the capacity benefit with a one-slot fixture barrier and a second build grant while the no-build test is still held.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1348-grok/` | linux-grok-sentinel | `/*/*/GrokSha256PinIsolationTests/Shared_download_sentinel_survives_pin_rejection` | V-1, R-1 | 1 executed, 0 failed/skipped | 1 | 12 |
| CP-2 | S1 | CP-1 | linux-grok-overlap | `/*/*/GrokSha256PinIsolationTests/Overlapping_checks_keep_their_own_downloads` | V-2, R-2 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-3 | S1 | CP-1 | linux-grok-rejections | `/*/*/GrokSha256PinTests/(Runner_grok_sha_rejects_multiline_pin*)\|(Runner_grok_sha_rejects_tampered_file*)` | R-1, R-2 | 2 executed, 0 failed/skipped | 2 | 1 |
| CP-4 | S1 | CP-1 | linux-grok-success | `/*/*/GrokSha256PinTests/Runner_grok_sha_accepts_matching_file` | R-2 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c1348-wrapper/` | linux-wrapper-no-build | `/*/*/BuildSlotScriptTests/C1348_WrapperNoBuildDoesNotAcquire` | V-3, R-3 | 1 executed, 0 failed/skipped | 1 | 12 |
| CP-6 | S2 | CP-5 | linux-wrapper-classification | `/*/*/BuildSlotScriptTests/C1348_WrapperNoBuildClassificationIsConservative` | V-4, R-4 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-7 | S2 | CP-5 | linux-wrapper-controls | `/*/*/BuildSlotScriptTests/(C589_WrapperRunsUnderLease*)\|(C589_WrapperTimeout*)\|(C800_WrapperPassesWildcardArgvLiterally*)` | R-3, R-4 | 3 executed, 0 failed/skipped | 3 | 2 |
| CP-8 | S3 | `tests/Antiphon.Tests -> bin-c1348-script/` | linux-checkpoint-release | `/*/*/RunCheckpointScriptTests/C1348_CheckpointReleasesBeforeRun` | V-5, R-5 | 1 executed, 0 failed/skipped | 1 | 12 |
| CP-9 | S3 | CP-8 | linux-checkpoint-reuse | `/*/*/RunCheckpointScriptTests/C1348_NoBuildUsesBindingWithoutLease` | V-6, R-6 | 1 executed, 0 failed/skipped | 1 | 2 |
| CP-10 | S3 | CP-8 | linux-checkpoint-build-gate | `/*/*/RunCheckpointScriptTests/(C589_SlotGranted*)\|(C589_SlotTimeout*)` | R-5, R-6 | 2 executed, 0 failed/skipped | 2 | 2 |
| CP-11 | S4 | `tests/Antiphon.Tests -> bin-c1348-tool/` | linux-scheduler-capacity | `/*/*/CheckpointSlotExecutorTests/scheduled_no_build_rows_leave_capacity_for_another_build` | V-7, R-7 | 1 executed, 0 failed/skipped | 1 | 12 |
| CP-12 | S4 | CP-11 | linux-direct-row | `/*/*/CheckpointSlotExecutorTests/(direct_row_releases_build_before_test*)\|(direct_no_build_row_never_contacts_broker*)` | V-8, R-8 | 2 executed, 0 failed/skipped | 2 | 2 |
| CP-13 | S4 | CP-11 | linux-command-gate | `/*/*/CheckpointSlotExecutorTests/(opaque_command_rows_keep_their_lease*)\|(executor_row_rejection_does_not_run_command*)\|(executor_cancellation_releases_lease_and_holder*)` | V-9, R-9 | 3 executed, 0 failed/skipped | 3 | 2 |
| CP-14 | S4 | CP-11 | linux-source-binding | `/*/*/CheckpointSourceExecutionTests/(strict_cli_and_reuse_require_clean_binding*)\|(changed_admission_and_driver_boundaries_refuse*)` | V-10, R-10 | 2 executed, 0 failed/skipped | 2 | 2 |
| CP-15 | S4 | CP-11 | linux-scheduler-order | `/*/*/RunSchedulerTests/(builds_never_overlap_even_with_free_slots_and_width_two*)\|(serial_runs_one_activity_at_a_time_builds_included*)\|(pty_project_rows_run_alone*)` | R-7 | 3 executed, 0 failed/skipped | 3 | 2 |
| CP-16 | S4 | CP-11 | linux-holder-renewal | `/*/*/CheckpointSlotExecutorTests/executor_renews_until_driver_finishes_then_releases` | R-9 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-17 | S2 | `tests/Antiphon.Tests -> bin-c1348-wrapper-win/` | windows-wrapper-parity | `/*/*/BuildSlotScriptTests/(C1348_WrapperNoBuildDoesNotAcquire*)\|(C1348_WrapperNoBuildClassificationIsConservative*)` | R-3, R-4 | 2 executed, 0 failed/skipped | 2 | 14 |
| CP-18 | S3 | `tests/Antiphon.Tests -> bin-c1348-script-win/` | windows-script-parity | `/*/*/RunCheckpointScriptTests/(C1348_CheckpointReleasesBeforeRun*)\|(C1348_NoBuildUsesBindingWithoutLease*)` | R-5, R-6 | 2 executed, 0 failed/skipped | 2 | 14 |
| CP-19 | S4 | `tests/Antiphon.Tests -> bin-c1348-tool-win/` | windows-tool-parity | `/*/*/CheckpointSlotExecutorTests/(direct_row_releases_build_before_test*)\|(direct_no_build_row_never_contacts_broker*)` | R-8 | 2 executed, 0 failed/skipped | 2 | 14 |
| CP-20 | S2 | CP-5 | linux-wrapper-instructions | `/*/*/InstructionBundleTests/C589_V6_BuildSlotGateIsAStandingRuleReviewEnforces` | R-3, R-4 | 1 executed, 0 failed/skipped | 1 | 1 |
| CP-21 | S4 | CP-11 | linux-stage-instructions | `/*/*/InstructionBundleTests/(C589_V6_BuildSlotGateIsAStandingRuleReviewEnforces*)\|(each_stage_bundle_is_ascii_and_under_the_size_cap*)` | R-7, R-9 | 7 executed, 0 failed/skipped | 7 | 1 |
| CP-22 | C1 | `tests/Antiphon.Tests -> bin-c1348-nested/` | linux-nested-retention | `/*/*/CheckpointTempRootSweepTests/nested_second_observation_must_be_dead` | V-11, R-11 | 9 executed, 0 failed/skipped | 9 | 12 |
| CP-23 | C1 | CP-22 | linux-nested-identity | `/*/*/CheckpointTempRootSweepTests/(nested_identity_change_retains_root*)\|(nested_stable_evidence_allows_cleanup*)` | V-12, V-13, R-12, R-13 | 9 executed, 0 failed/skipped | 9 | 2 |
| CP-24 | C1 | CP-22 | linux-root-controls | `/*/*/CheckpointTempRootSweepTests/(eligibility_is_rechecked_after_the_root_lock*)\|(resumed_deletion_rechecks_every_veto*)\|(marker_survives_partial_deletion*)` | V-14, R-14 | 8 executed, 0 failed/skipped | 8 | 2 |
| CP-25 | C1 | `tests/Antiphon.Tests -> bin-c1348-nested-win/` | windows-nested-parity | `/*/*/CheckpointTempRootSweepTests/(nested_second_observation_must_be_dead*)\|(nested_identity_change_retains_root*)\|(nested_stable_evidence_allows_cleanup*)` | R-11, R-12, R-13 | 18 executed, 0 failed/skipped | 18 | 14 |
| CP-26 | S4 | CP-11 | linux-build-confinement | `/*/*/CheckpointConfinementLedgerTests/(scheduler_stamps_non_output_files_before_the_slot_and_verifies_after_release*)\|(non_output_file_changed_before_slot_release_is_refused*)` | R-15 | 2 executed, 0 failed/skipped | 2 | 2 |


## Handoff

**Next: test-design.** Append the required Verification design to this artifact, retaining the selected implementation decisions. Bind guard-to-PC recipes, exact variant/oracle/reachability, red-first SHA/output handling, the closed manifest, native lanes and numeric Code/Mutation costs. The names introduced here are proposed tests until authored; existing names/counts were checked against source, but no table was executed in Plan.

No answer is needed to produce this plan, and no suite run is authorized here. Before Code, refresh the provisioned master base and check for source drift; never merge/rebase this task branch to do so. Do not repeat A-1/A-2 merely to rediscover the root-owner/Git no-change findings. A new real false-dead incident or reproducible lock-reader failure would justify a separately measured investigation.

After TestDesign: commission the independent slices, ordinary Review, master landing, then the required post-land method-scoped Mutation. Keep the remaining caller inventory explicit when reporting card completion.
