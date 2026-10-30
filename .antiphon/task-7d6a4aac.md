S3a is implemented and pushed: CP-5 passed 8/8 and CP-8 passed both client typechecks. This storage/transport slice must not land or activate CARD-1619 independently.

Assigned base: f2d3171e0aa425085116f2aa5be65f56a6ff87d7. Green product/test source: 483afdcb0e77096d91418229eac63a9a387fdcf9. Chain S3b from this report commit's pushed tip, retaining the reviewed plan. The report-only commit changes no tested source. Caller authority was sufficient; no approval was needed.

S3a follows the reviewed slice table: nullable storage, migration, manifest/session/wire transport, adapter structured-completion readiness, projections, handwritten clients and the inert intent shape. Compatibility values, classification, ownership-consumer conversion and DescribeStop remain S3b. Attribution, issuer memory, best-effort diagnostic IO, the 100 ms deadline and blocked-writer witnesses remain S4. The broader handoff wording does not expand this slice. No Windows native behavior changed.

Delivered source:
- server/Domain/Entities/{AgentSession,RunAttempt,AgentIncident}.cs add nullable ExitSignal. AgentSession also has nullable ExitStopIntentJson, bounded to 4096 characters by the model. CLI-generated migration server/Migrations/20261030141651_AddSessionExitSignalEvidence.cs, its Designer and AppDbContextModelSnapshot add exactly those four nullable columns, with no defaults, backfill, indexes or history rewriting. No live database migration was run.
- ProcessStopIntentDto in SessionRunner.Contracts is the shared bounded primitive/enum contract. Protocol already depends on Contracts, so Protocol/ProcessStopIntent.cs supplies the host-facing derived record and disk-recovery downgrade without a reverse reference or graph change. Production intent fields remain inert/null until S4. Serialized intent never supplies attribution authority. Disk manifest loads always downgrade provenance to RecoveredUnverified.
- Protocol messages/manifests, child/session records, host status/replay/terminal manifest, runner exit envelopes/list/adoption, server HTTP/phone-home DTO mapping and typed already-exited launch exception carry optional signal/intent. Protocol and schema remain version 1. Missing members remain unknown/null, never zero; unknown positive signals survive. Old typed writers can still drop new members; stronger server evidence survives later missing-field replay.
- IAgentProtocolAdapter and all eight local/runner adapters expose AgentExitObservation Completion. RunnerTerminalSession reports actual nullable code/signal/intent there, including unknown on observer/generation failure. Every legacy Exited integer result is unchanged; PtyAgentRunner.Exited is unchanged. S3b must change adapter compatibility values and every persisting/ownership consumer together.
- SessionExitEvidence, runtime, service, reconciliation, control and compaction paths copy/reset nullable evidence. Accepted-generation event and snapshot paths enrich already-terminal rows while retaining their lifecycle and first termination source. The first complete native pair supersedes an incomplete legacy integer; conflicting complete observations retain the first pair and log bounded detail. Event publication uses retained DB evidence. Resume/re-adoption clear signal and intent with ExitCode. RunAttempt and supervisor incident copying, public session/event/incident projections and client/src/api/{agents,boards}.ts include optional fields. There is no committed client generator output to regenerate at this source.
- Application/Card1619ExitPersistenceTests.cs and TestHelpers/Card1619NativeExitFixture.cs provide the exact eight-result V-11 roster. Four real Linux PTY exits (SIGKILL, SIGTERM, normal 0 and 23) traverse detached host -> runner -> server mapper -> committed database -> actual API projection/JSON. Fresh contexts verify persistence, replay/conflict retention, terminal live/reconciliation parity, request-source preservation, reset/stale-generation fences and a real isolated migration downgrade/upgrade preserving a legacy row. The real launch manifest is captured while the owned child is ready; unverified intent recovery uses a separate owned diagnostic copy populated with the actual exit fields, because normal runner exit acknowledgement deletes the live manifest. Assertions were retained and strengthened, never relaxed. The native IO lifetime was tightened from 20 to the planned 10 seconds after the initial measurements; joins stay bounded at 5 seconds.

Commits, each separately pushed:
cd3518aebf7fe24ac4994459f8d7223fa5250371 test(card-1619): witness Linux exit signal loss at server storage boundary
c0e7ea8829f56f9e5c9f99ba1f42a89579a7051f feat(card-1619): carry nullable exit evidence into server storage and projections
c4bbbb068fe8c694f3c8e6dae8325d6528b1e845 fix(card-1619): preserve incident recorder interface while copying signal
f7fe966b591c98c003aba2cb732121b613ee9cc4 feat(card-1619): scaffold nullable exit evidence migration and verify terminal enrichment
7cfb06469eee824e1705eff35207b918e68c0b9e test(card-1619): pin first complete native observation over legacy integer
5ae2aa610eed5f327951e626d1eb639172d3a6cb fix(card-1619): prefer first complete native evidence over legacy integer
483afdcb0e77096d91418229eac63a9a387fdcf9 test(card-1619): keep intent recovery witness independent of live exit acknowledgement

Credited evidence:
- Initial red at cd3518aebf7fe24ac4994459f8d7223fa5250371: CP-5 executed 8, passed 0, failed 8, skipped 0. All four native frames actually carried 9/15/0/0 and failed where the server mapper dropped them; the other assertions found the absent storage member/migration. Build and fixture succeeded; this is feature red.
- First-complete red at 7cfb06469eee824e1705eff35207b918e68c0b9e: CP-5 executed 8, passed 7, failed 1, skipped 0. A stopped operator-request row seeded with legacy code 123/null signal refused actual code 0/signal 9. The production merge rule was then repaired at 5ae2aa610eed5f327951e626d1eb639172d3a6cb.
- Final source 483afdcb0e77096d91418229eac63a9a387fdcf9: CP-5 8 passed, 0 failed/skipped; CP-8 command exit 0 for tsconfig.app.json and tsconfig.node.json. Terminal wait returned 0. Separate strict receipt validation returned: CHECKPOINT SOURCE VALID source=483afdcb0e77096d91418229eac63a9a387fdcf9 rows=2. Both required rows have clean, stable source and verified build provenance (command build binding notApplicable).
- All selected rows ran through the checkpoint tool after 15-row plan import, with granted host slots, UseAppHost=false, alternate outputs and TEMP=/tmp. CP-5/8 timeout pins remain 6/4 minutes; slot admission is 25 minutes as explicitly allowed by the brief. Filters, minima and roster were not changed. Imported and pinned manifests, TRX, logs, timing records and receipts remain ignored under /work/worktrees/task-7d6a4aac/.antiphon/card1619-s3a-*. Every executor reached a terminal observation; the intentionally stopped run is recorded below.

Required tooling and graph:
The pinned dotnet-ef 9.0.20 was restored. EF scaffold used the verified assembly /work/worktrees/task-7d6a4aac/server/bin-card1619-migration/Antiphon.Server.dll, --no-build, environment OutputPath=bin-card1619-migration/ and AppendTargetFrameworkToOutputPath=false. No application-argument OutputPath workaround was used. A non-live connection address prevented accidental database access; scaffolding only obtained the design-time service provider.
The real scripts/audit-land-verification-graph.ps1 -Check passed both evaluation passes and the canary at f7fe966b591c98c003aba2cb732121b613ee9cc4. Digest stayed 7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99, count 68. Subsequent changes are C# copying/test fixes, with zero definition-input delta. No csproj, solution, props/targets, SDK/tool/package/lock definition changed. No -Update was run. Bootstrap/check commands were the explicitly required preparatory builds outside the closed product rows, all leased. Graph tool build: wait 30 s, hold 5 s, 0 warnings/errors. Graph check: wait 180 s, hold 43 s. Checkpoint bootstrap: wait 0 s, hold 35 s (33.68 s build, five existing warnings). EF tool restore: wait 45 s, hold 1 s. The ordinary CP-5 producing builds retained their 1415 existing warnings with zero errors; warnings were not suppressed.

Historical non-credit attempts and corrections (retained, not relabeled green):
1. 20261030-134901-8f8c: CP-8 exit 0; CP-5 build refused unreadable before compilation because npm installed two hardlinked esbuild files. Both task-local paths were replaced with byte-identical independent copies (same SHA-256 c638273fcf95573ca74af586677800b4dd874c55f8f945adb54316d6902ba14b). No npm audit repair was made.
2. 20261030-135238-ffcb and 20261030-135415-7d7b: zero tests, pre-existing-output refusal from the first attempt's owner marker. The exact producer-owned bin path was removed after terminal waits. One empty-directory removal failed while that marker remained; no outside-task deletion occurred.
3. Migration build at c0e7ea8829f56f9e5c9f99ba1f42a89579a7051f failed with one introduced CS0535 because the incident recorder's parameter addition no longer implemented IAgentIncidentRecorder. c4bbbb068fe8c694f3c8e6dae8325d6528b1e845 restores the public signature and uses a private signal-aware writer. The next isolated build passed (23 existing warnings, zero errors; hold 47 s). The failed build held 18 s after waiting 105 s; it is not feature red.
4. First EF --no-build attempt exited 129 because metadata appended net9.0 to the correct isolated output. It wrote no migration. Pinning AppendTargetFrameworkToOutputPath=false made metadata resolve the exact built DLL; the scaffold then passed. First/second scaffold waits 15/15 s, holds 4/12 s. The repository-pinned tool/runtime patch warning was retained; dependencies were not changed.
5. 20261030-142316-ec04 at f7fe966b591c98c003aba2cb732121b613ee9cc4 was explicitly stopped during CP-5 build after source review found the first-complete merge defect. Supported stop verified executor identity, killed/joined its tree and returned 0. Terminal wait returned 6 (stopped before phase=done); no final receipt or CP-5 green is claimed. Partial CP-8 success is not substituted for the final certificate. Source was edited only after stopping/joining.
6. 20261030-144057-45e4 at 5ae2aa610eed5f327951e626d1eb639172d3a6cb: CP-8 exit 0; CP-5 7 passed/1 failed. Core signal, DB, terminal reconciliation and intent assertions passed, then the supplementary recovery check dereferenced the acknowledged/deleted live manifest. 483afdcb0e77096d91418229eac63a9a387fdcf9 fixes the fixture lifetime with an independently owned copy and adds explicit nonnull/code/signal assertions. No production retry, timeout widening or assertion weakening was used.

Timing: Debian 12/Linux, 24 cores, shared contended host, maxcpucount 6. Final CP-5 per-test nearest-rank p95/max = 5.2275437 s, n=8. This is one comparable passing group sample; retain CARD-1619's provisional ceilings until independent Review supplies the remaining samples. Initial red p95/max was 7.5816258 s (native four cases 0.8832141-1.6703237 s). Build, admission, wrapper startup/body/teardown and test TRX durations are separate below. Native fixture phases cover host/readiness, signal/exit envelope and owned teardown; the upgrade case records DbMigrate; shared fixture records clone/drop. Fine-grained pipe/native/filesystem and some individual DB spans remain unavailable/unknown and remainder unattributed, never zero. One assembly container/template is shared; test stores are cloned, with the additional isolated upgrade DB justified by its subject. No per-test AppHost/web host, npm install or build was used. API proof exercises the production projection and JSON, not a separately booted server route. No run exceeded 30 minutes.

Final per-test durations:
- Native_exit_reaches_database_and_api(9, 0): 5.2275437 s
- Native_exit_reaches_database_and_api(15, 0): 0.9297263 s
- Native_exit_reaches_database_and_api(0, 0): 1.0696153 s
- Native_exit_reaches_database_and_api(0, 23): 1.2731147 s
- Live_event_and_reconciliation_agree: 1.9898859 s
- Terminal_row_is_enriched_without_changing_request_source: 2.8105537 s
- Resume_and_stale_events_keep_generation_boundary: 1.2640072 s
- Migration_preserves_old_rows_and_nullable_contract: 0.4513822 s

Fresh merge-tree evidence: origin/rc/2026-10-27 = 80b3a59c40cd18dea905829e428d35844cb89b8d was clean at the product source (tree 47f1da3ef2e511718c3d3bb9e84f77957b7c7102). Fresh origin/master = bce716158d9a3fcca5bf548a07fdb257de637f1f has 11 identical conflict paths at the assigned base and product tip; none was introduced by S3a. No merge or rebase was performed. RC-first complete-fix Review and a separately reviewed master port remain required; re-scaffold the master migration after CARD-1610 rather than copying this RC snapshot/Designer.

Final checks: check-evidence-diff.ps1 over the assigned base..green source reported 7 commits and 0 violations; git diff --check passed. A declaration census found only the intended Card1619ExitPersistenceTests and Card1619NativeExitFixture, and final TRX contains all eight named V-11 executions with zero skips. The definition-file delta is empty. All task-created alternate output directories were removed through 12 exact literal paths after owners joined (11 removed, the green tool had already deleted the test output). The cleanup record and all raw evidence remain ignored; no outside-task path was removed. The green run had one bounded owner-read timeout during finalization and still reached normal terminal exit 0; its raw diagnostic is retained. No additional product test run is needed for this report-only commit.

Owed work after S3a: S3b CP-13/6/7/14/15 = 23/6/3/3/7; S4 CP-9/10/11 = 11/8/8; final Windows CP-2 = 7; separate owned cgroup-v1 CP-12 = 2. Thus 78 required results remain across later slices/native Review. S1..S3b must not land separately. No Windows, cgroup-v1, desktop/native qualification d47848cc, C:\Antiphon, tags, publish, activation or deployment was accessed/executed. No mutation PC execution occurred; that remains separately commissioned.

Exact owed native commands at the FINAL combined source (bootstrap/import anew on the appropriate native lane, apply the reviewed timeout/timing pins and preserve the native Windows TEMP):

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1619-checkpoint-tool -SlotWaitMinutes 25 -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-card1619-tools/ --property:UseAppHost=false
$tool = 'tools/Antiphon.Checkpoints/bin-card1619-tools/Antiphon.Checkpoints.dll'
$plan = 'docs/superpowers/plans/2026-10-30-card-1619-linux-pty-exit-signal-plan.md'
$source = (git rev-parse HEAD).Trim()
$manifest = '.antiphon/card1619-final-native.yaml'
dotnet $tool import --plan $plan --out $manifest
# Apply the plan's row timeout pins, valid timing metadata and slotWaitMinutes=25.
# Native Windows only, final combined source:
dotnet $tool run $manifest --rows CP-2 --expected-source-sha $source --serial --total-timeout 16m --hard-wall-timeout 26m --max-wait 45s --heartbeat 15s
# Owned cgroup-v1 fixture, its OWN run, never combined with CP-9..11:
$env:TEMP = '/tmp'
dotnet $tool run $manifest --rows CP-12 --expected-source-sha $source --serial --total-timeout 14m --hard-wall-timeout 24m --max-wait 45s --heartbeat 15s
# After each exit 75 use that invocation's exact ABSOLUTE run directory:
dotnet $tool wait --run $runDirectory --expected-source-sha $source --max-wait 45s --heartbeat 15s
```

For S3a replay only, re-bootstrap/import/pin at the selected committed source and run CP-5,CP-8 with --serial --total-timeout 22m --hard-wall-timeout 32m --max-wait 45s --heartbeat 15s; wait to terminal and validate. Current green receipts remain bound to 483afdcb0e77096d91418229eac63a9a387fdcf9, not future code.

Essential unedited checkpoint/provenance lines:

Source report: /work/worktrees/task-7d6a4aac/.antiphon/card1619-s3a-red/20261030-134901-8f8c/report.md

CHECKPOINT CP-8 commit=cd3518aebf7fe24ac4994459f8d7223fa5250371 build=n/a filter=pwsh -NoProfile -File scripts/typecheck-client.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=45s dirty=0 source=cd3518aebf7fe24ac4994459f8d7223fa5250371 sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-8 waited=45s executed=86.703s wall=131.983s
CHECKPOINT CP-5 commit=cd3518aebf7fe24ac4994459f8d7223fa5250371 build=failed filter=/*/*/Card1619ExitPersistenceTests/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s slot-reason=refused: unreadable dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-5 waited=0s executed=0.000s wall=0.000s
BUDGET wall=183.866s execution=138.856s admissionOnly=45.011s

Source report: /work/worktrees/task-7d6a4aac/.antiphon/card1619-s3a-red-assertions/20261030-135238-ffcb/report.md

CHECKPOINT CP-5 commit=cd3518aebf7fe24ac4994459f8d7223fa5250371 build=failed filter=/*/*/Card1619ExitPersistenceTests/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s slot-reason=refused: pre-existing-output dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-5 waited=0s executed=0.000s wall=0.000s
BUDGET wall=47.097s execution=47.097s admissionOnly=0.000s

Source report: /work/worktrees/task-7d6a4aac/.antiphon/card1619-s3a-red-assertions/20261030-135415-7d7b/report.md

CHECKPOINT CP-5 commit=cd3518aebf7fe24ac4994459f8d7223fa5250371 build=failed filter=/*/*/Card1619ExitPersistenceTests/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s slot-reason=refused: pre-existing-output dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-5 waited=0s executed=0.000s wall=0.000s
BUDGET wall=33.095s execution=33.095s admissionOnly=0.000s

Source report: /work/worktrees/task-7d6a4aac/.antiphon/card1619-s3a-red-assertions/20261030-135501-5a9e/report.md

CHECKPOINT CP-5 commit=cd3518aebf7fe24ac4994459f8d7223fa5250371 build=ok filter=/*/*/Card1619ExitPersistenceTests/* executed=8 passed=0 failed=8 skipped=0 trx=/work/worktrees/task-7d6a4aac/.antiphon/card1619-s3a-red-assertions/20261030-135501-5a9e/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=cd3518aebf7fe24ac4994459f8d7223fa5250371 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=0s executed=87.165s wall=87.230s
PHASES CP-5 slotWait=0s build=673.7420583s startup=54.9641832s testsWall=16.0858722s teardown=3.6196831s hostWall=74.6698011s
BUDGET wall=804.004s execution=804.003s admissionOnly=0.001s

Source report: /work/worktrees/task-7d6a4aac/.antiphon/card1619-s3a-red-first-complete/20261030-142815-1f1f/report.md

CHECKPOINT CP-5 commit=7cfb06469eee824e1705eff35207b918e68c0b9e build=ok filter=/*/*/Card1619ExitPersistenceTests/* executed=8 passed=7 failed=1 skipped=0 trx=/work/worktrees/task-7d6a4aac/.antiphon/card1619-s3a-red-first-complete/20261030-142815-1f1f/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=7cfb06469eee824e1705eff35207b918e68c0b9e sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=0s executed=53.617s wall=53.678s
PHASES CP-5 slotWait=30s build=606.6480897s startup=37.3745087s testsWall=13.3321866s teardown=1.9151791s hostWall=52.621887s
BUDGET wall=731.302s execution=701.312s admissionOnly=29.990s

Source report: /work/worktrees/task-7d6a4aac/.antiphon/card1619-s3a-green-final/20261030-144057-45e4/report.md

CHECKPOINT CP-8 commit=5ae2aa610eed5f327951e626d1eb639172d3a6cb build=n/a filter=pwsh -NoProfile -File scripts/typecheck-client.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=5ae2aa610eed5f327951e626d1eb639172d3a6cb sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-8 waited=0s executed=10.348s wall=10.459s
CHECKPOINT CP-5 commit=5ae2aa610eed5f327951e626d1eb639172d3a6cb build=ok filter=/*/*/Card1619ExitPersistenceTests/* executed=8 passed=7 failed=1 skipped=0 trx=/work/worktrees/task-7d6a4aac/.antiphon/card1619-s3a-green-final/20261030-144057-45e4/rows/CP-5/run.trx slot=granted waited=135s dirty=0 source=5ae2aa610eed5f327951e626d1eb639172d3a6cb sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=135s executed=55.362s wall=190.477s
PHASES CP-5 slotWait=135s build=490.3599828s startup=38.1705351s testsWall=15.2470423s teardown=1.3931556s hostWall=54.8107432s
BUDGET wall=740.220s execution=605.156s admissionOnly=135.064s

Source report: /work/worktrees/task-7d6a4aac/.antiphon/card1619-s3a-green-verified/20261030-145543-c26a/report.md

CHECKPOINT CP-8 commit=483afdcb0e77096d91418229eac63a9a387fdcf9 build=n/a filter=pwsh -NoProfile -File scripts/typecheck-client.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=483afdcb0e77096d91418229eac63a9a387fdcf9 sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-8 waited=0s executed=11.578s wall=11.690s
CHECKPOINT CP-5 commit=483afdcb0e77096d91418229eac63a9a387fdcf9 build=ok filter=/*/*/Card1619ExitPersistenceTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-7d6a4aac/.antiphon/card1619-s3a-green-verified/20261030-145543-c26a/rows/CP-5/run.trx slot=granted waited=15s dirty=0 source=483afdcb0e77096d91418229eac63a9a387fdcf9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=15s executed=68.020s wall=83.281s
PHASES CP-5 slotWait=15s build=343.9606666s startup=48.3914517s testsWall=15.0744226s teardown=3.6315539s hostWall=67.0974211s
BUDGET wall=565.572s execution=550.561s admissionOnly=15.011s

--- next stage ---
next: code
handoff: Implement only S3b from this report commit's pushed tip via -StartRef, retaining the reviewed plan, migration and S1/S2/S3a source. Change D-7 adapter compatibility and all structured persisting/ownership consumers in the same slice; keep PtyAgentRunner.Exited actual. Run CP-13/6/7/14/15, including all seven ready-gate regressions. S4 still owns attribution, issuer memory, post-signal diagnostic store and 100 ms writer deadline. S1..S3b must not land individually. Full-fix ordinary/native Review and final Windows/cgroup-v1 gates remain open.
artifact: /work/worktrees/task-7d6a4aac/docs/superpowers/plans/2026-10-30-card-1619-linux-pty-exit-signal-plan.md
