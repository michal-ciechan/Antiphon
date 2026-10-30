S3b is implemented and pushed; CP-13/6/7/14/15 passed 23/6/3/3/7 (42 total), zero failures/skips. This incremental slice must not land or activate CARD-1619 independently.

Base: 1c15dfebe445b3d588ddbd05c07ba1187a38eb76 (S3a). Final product/test source: 9a43df07db611c8dc24791744af5c8752410a65d. Chain S4 from this report's pushed tip, retaining the reviewed plan. A report-only commit changes no tested product source. Standing caller authority was sufficient; no approval was needed.

S3b follows the reviewed table: D-7 adapter compatibility, structured persisting/ownership reads, signal-first classification, all exit consumers and truthful stop text. S4 issuer memory, attribution, diagnostic store/IO, 100 ms deadline and blocked-writer witnesses remain unimplemented and owed. No new stop, retry, custody, janitor, parking or terminal-seat release authority was added.

Delivered behavior and files:

- server/Application/Services/SessionExitPolicy.cs is the shared classifier/description helper. Unsolicited known signals fail even with actual code zero; known normal zero/nonzero retain lifecycle behavior. Legacy null-signal rows retain the old lifecycle fallback with explicit unknown wording. CPU-spin reclamation remains a resumable policy stop; memory kills fail. Unknown positive numbers retain the number/UNKNOWN name. No 128+signal conversion or self-exit claim is made.
- AgentExitObservation, IAgentProtocolAdapter, RunnerTerminalSession and all eight local/runner adapters return compatibility -1 for positive signal or missing code, otherwise the actual code. PtyAgentRunner.Exited stays the actual integer, including Linux signal zero-code. HostSession already reads structured completion from S1; that read is retained. AgentSessionService launch cleanup/ownership and memory-kill persistence, and AgentSessionRuntime's adapter snapshot writer read actual structured code/signal/intent together with this value change. CARD-1190 retains generation, stamp-time, cause, code and legacy-unknown guards; conflicting known signals refuse ownership, including normal Unix exit versus SIGKILL. Matching Windows no-signal kills retain ownership.
- AgentSessionRuntime and SessionReconciliationService classify retained accepted-generation evidence through the shared helper and retain their diagnostic prefixes. SessionTermination's first-writer behavior is unchanged. Runner dead-manifest adoption and vanished-process handling now leave missing native codes null instead of inventing -1; adapter compatibility alone supplies that sentinel. Positive signals also count in the existing explicit exit hints.
- AgentTaskLiveness snapshots, AgentTaskDispatcher's dead sweep and AttentionService carry optional signal/intent and compose existing failure details with the same evidence for Failed and Stopped rows. ProcessExit remains a diagnostic source token, not sender/voluntary-exit proof. Authentication priority, StoppedBeforeFirstPrompt, report-first settlement, grace and positive runner-liveness gates are retained. Attention's missing-reason fallback now invokes the same classifier; the real normal-zero witness found and closed this last omission. SessionStopStuck retains its pending/liveness predicate and says exit unobserved when no observation exists.
- AgentSupervisorService's crash message, CardReviewService's attempt text and DiagnosticsBundleService's session/incident JSON include the evidence. CheckCompactionContinuationService's existing policy-stop zero is explicitly not a native observation and retains unknown signal until enrichment. AgentControlService keeps its existing presence-hint semantics with positive signals. Session-runtime owner documentation and relevant source comments describe the contract. No client API definition changed in S3b.
- tests/Antiphon.Tests/Application/Card1619ExitPolicyTests.cs has the 12 V-10 results, including normal code 137, CPU/memory policy controls and both extra text rows. Card1619ExitConsumerTests.cs has all 11 V-12 results: four real Linux host->runner->mapper->runtime->DB->dispatcher/attention cases (9/15/exit0/exit23), actual adapter compatibility/runtime snapshot reads, report-first/no-report, Check/supervisor, pending stop, Working/WarmPool/terminal-seat/janitor holds, structured launch cleanup, native unknown recovery, API/attempt/diagnostic ZIP. The new Card1619ConsumerFixture reuses the established sweep wiring with isolated cloned databases and owned scopes/providers; native cases reuse S3a's Card1619NativeExitFixture. No model/provider call or real Program/AppHost is used. Only the actual native lifetime cases create fresh hosts; all owned processes are joined.

No test was removed or relaxed. R-3's ProcessExit/source/code assertions are unchanged. R-6 still requires the exact natural-crash diagnostic, source and code; its expected sentence now additionally requires the unknown-signal clause, and all seven methods remain. R-5 keeps its generation, first-source, status and event-count assertions and adds signal/intent fences. Its intentionally synthetic code-1 event is explicitly a legacy null-signal arm, while the real kill capture now separately requires Linux signal 9 (Windows 0); this avoids presenting a rewritten integer as a complete native pair. The structured launch witness goes through the real launch catch with actual code 0/signal 9 and adapter -1, retaining the not-ready error; its normal-exit and portable Windows ownership controls remain. The two hold-fixture corrections change setup/sequence, not assertions or production policy.

All six meaningful commits were pushed individually:

```text
fb8a8fa576ac2f12b70e0eae1e0687596c13da66 test(card-1619): witness signal classification and real exit consumers
0fb411b8a00cc9ddbb94f6858f6fa180f26ff3dd test(card-1619): pin structured launch ownership and adapter compatibility
5ade97380d0f1812974d67d97e33649f05006b52 test(card-1619): keep launch witness locals in distinct scopes
749530d56bf4d2b153d46dcdc06aa25ad0104703 test(card-1619): exercise recovery and diagnostics with supported assertions
e00bd6426cadeadda1294f89d62e11302ce3947d fix(card-1619): classify signals and consume actual structured exit evidence
9a43df07db611c8dc24791744af5c8752410a65d fix(card-1619): include normal exit evidence in attention fallback
```

Credited red and historical attempts (not relabeled green):

- fb8a8fa576ac2f12b70e0eae1e0687596c13da66, run 20261030-151611-a1fd: CP-13 23 executed, 1 passed/22 failed/0 skipped. Four native cases reached real exit observations: known signals wrongly became Stopped, normal exits lost final text, and the remaining evidence assertions failed. Twenty-one are intended feature failures; the Working control accidentally reached WarmPool and is fixture red, not feature credit. CP-6 6/6, CP-7 3/3, CP-14 3/3 and CP-15 7/7 establish the existing regression baseline. Wall 942.324 s, execution 537.219 s, admission-only 405.105 s.
- 5ade97380d0f1812974d67d97e33649f05006b52, run 20261030-153354-607f: no tests; one introduced CS0117 from the wrong Shouldly aggregate API. This is not feature red. Corrected to the repository's ShouldSatisfyAllConditions extension. Build/overall wall 299.271 s, no slot wait; original warnings/error retained.
- 749530d56bf4d2b153d46dcdc06aa25ad0104703, run 20261030-154031-2a42: CP-13 1 passed/22 failed, CP-14 2/1, CP-15 6/1; no skips. Actual adapter zero versus expected -1, lost snapshot signal, missing native code becoming -1, missing diagnostic signal/attempt text and the real ready-gate catch losing its not-ready reason are discriminating red. R-5 and R-6 fail specifically on their required new unknown-signal sentence. One CP-13 failure is fixture-only: warm settlement intentionally creates no release row until the janitor runs; adding that actual sweep retains the WarmPool assertion. Wall 512.770 s, execution 467.728 s, admission-only 45.042 s.
- e00bd6426cadeadda1294f89d62e11302ce3947d, run 20261030-155109-2394: CP-13 22 passed/1 failed; CP-6/7/14/15 all green at 6/3/3/7, zero skips. The one remaining real normal-zero case found attention falling back to status alone when FailureReason was null. 9a43df07db611c8dc24791744af5c8752410a65d repairs that production fallback without changing the test. Wall 676.388 s, execution 601.400 s, admission-only 74.988 s.
- An abbreviated expected-source run argument was refused before any executor/test; full normalized SHAs were then used. No timeout was widened, assertion loosened or production retry added. Native/control tests that already pass by design remain mutation-stage PC obligations; no ad hoc PC execution occurred.

Final source 9a43df07db611c8dc24791744af5c8752410a65d, run 20261030-160324-fbbc: CP-13 23/23, CP-6 6/6, CP-7 3/3, CP-14 3/3, CP-15 7/7; no failures/skips. Terminal wait returned 0. Separate receipt validation returned:

`CHECKPOINT SOURCE VALID source=9a43df07db611c8dc24791744af5c8752410a65d rows=5`

Every row is clean, stable and buildSource=verified (one CP-13 producing build, four reused rows). Final wall 669.330 s, execution 669.324 s, admission-only 0.005 s; producing build 252.3260704 s. The one bounded owner-read HTTP timeout delayed finalization but did not invalidate the final certificate; its diagnostic and final normal exit are retained.

All selected rows ran through the checkpoint tool after 15-row plan import, with granted host slots, UseAppHost=false, alternate output and TEMP=/tmp. Original filters/minima and row timeouts CP-13/6/7/14/15=8/2/4/4/4 minutes are retained; slot admission is 25 minutes as the brief permits. Valid phase GROUP_ID metadata is present in the structured-red and final runs. The initial red accidentally used GROUP rather than GROUP_ID, so its finer phase spans are unavailable, not zero. Every executor reached a terminal wait. The last complete group was rerun to bind the required closed selection to one final source after the shared-attention repair; no broader product selection was run.

Required real scripts/audit-land-verification-graph.ps1 -Check passed both evaluation passes and its canary at 9a43df07db611c8dc24791744af5c8752410a65d: digest 7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99, count 68. No -Update was run. No definition file, solution, props/targets, SDK/tool/package/lock definition or migration changed. The graph tool was already produced by the leased CP-13 build; 8 staged files were copied byte-for-byte to task-owned scratch before the checkpoint tool removed its producing output. Graph DLL SHA-256=9BD646FF31D132C51BACA5627FA3C342683DCCB85225BF144A95B42736275441. The graph check itself took a granted slot (wait 0 s, hold 40 s); no extra graph build or product test was needed. Its exact canary directory was confirmed removed. Checkpoint-tool bootstrap (explicit preparatory build): wait 31 s, hold 7 s, five existing warnings, zero errors. Product build warnings were retained, never suppressed.

check-evidence-diff.ps1 over assigned base..9a43df07db611c8dc24791744af5c8752410a65d: 6 commits, 0 violations. git diff --check passed. Declaration/actual-TRX census confirms the two intended new classes, 12 policy + 11 consumer results, all three selected generation methods and all seven ready-gate methods. Generated manifests, TRX, timing and receipts remain ignored under /work/worktrees/task-949a4df5/.antiphon/card1619-s3b-*.

Fresh origin/rc/2026-10-27=335b4fbb3b1eac700cb8d3c64ea032d9579c6b5f: merge-tree clean, product tree 1c16b28e15d6bba7ce85522cd7c92824b06417a9. Fresh origin/master=bce716158d9a3fcca5bf548a07fdb257de637f1f: 11 identical conflict paths at assigned base and product tip; none introduced by S3b. No merge/rebase/target push was performed. RC-first complete-fix Review and a separate master port remain required; the master migration must be re-scaffolded after CARD-1610 instead of copying the RC Designer/snapshot.

After all executors joined, the final checkpoint tool deleted tests/Antiphon.Tests/bin-card1619-consumers. Explicit literal-path cleanup removed src/Antiphon.HostCleanup/bin-card1619-tools, tools/Antiphon.Checkpoints/bin-card1619-tools and the task-owned /tmp/card1619-s3b-land-graph-949a4df5 copy. A separate stop-on-error -File verifier confirmed all four paths absent. One cleanup wrapper emitted a PowerShell preamble quoting error, but the literal removals completed; the independent verification is the cleanup verdict. No unowned path was deleted. The ignored builds/bin-card1619-consumers evidence-label directories contain build logs/receipts and were retained, not confused with compiler output roots.

Performance: Debian 12/Linux x64, 24 cores, shared contended host, SDK 10.0.403, TUnit runtime .NET 9.0.22, gate maxcpucount 6. One assembly TestDbFixture container/template per row process supplies cloned case databases. No per-test container, migration replay, AppHost, npm or build occurs. Native host readiness/exit/owned join and DB clone/drop phases are recorded. Fine-grained external IO and some individual DB spans remain unavailable/capped and remainder unattributed, never zero. Final nearest-rank per-test p95/max and exact roster/durations are recorded below. There is one passing sample for the new CP-13 group; retain CARD-1619's provisional ceilings until independent Review supplies the remaining comparable samples. No run exceeded 30 minutes.

| CP | Results | Per-test p95 seconds | Max seconds |
|---|---:|---:|---:|
| CP-13 | 23 | 11.8594796 | 11.9942437 |
| CP-6 | 6 | 0.0119031 | 0.0119031 |
| CP-7 | 3 | 18.1307206 | 18.1307206 |
| CP-14 | 3 | 29.9954097 | 29.9954097 |
| CP-15 | 7 | 6.1888763 | 6.1888763 |

Final exact execution roster/durations:

| CP | Result | Seconds | Test |
|---|---|---:|---|
| CP-13 | Passed | 11.8594796 | Api_attempt_and_diagnostics_preserve_optional_signal |
| CP-13 | Passed | 11.9942437 | Pool_and_terminal_seat_do_not_release_from_code_zero |
| CP-13 | Passed | 4.3279935 | Stop_stuck_remains_pending_without_observed_exit |
| CP-13 | Passed | 2.4684612 | Check_and_supervisor_keep_signal_evidence |
| CP-13 | Passed | 3.0891305 | Signal_death_without_report_never_succeeds |
| CP-13 | Passed | 1.3458743 | Report_before_signal_death_still_settles |
| CP-13 | Passed | 4.4865392 | Native_exit_reaches_failure_and_attention_text(0, 23, process exited with exit code 23) |
| CP-13 | Passed | 2.7517801 | Native_exit_reaches_failure_and_attention_text(0, 0, process exited with exit code 0) |
| CP-13 | Passed | 1.7067067 | Native_exit_reaches_failure_and_attention_text(15, 0, killed by signal 15 (SIGTERM); sender unknown) |
| CP-13 | Passed | 2.1039629 | Native_exit_reaches_failure_and_attention_text(9, 0, killed by signal 9 (SIGKILL); sender unknown) |
| CP-13 | Passed | 0.6106138 | Launch_cleanup_does_not_steal_a_normal_exit |
| CP-13 | Passed | 0.0043328 | Describe_exit_evidence(null, null, Unknown, Antiphon host stop issued; child exit unobserved (exit code unknown; signal unknown), False) |
| CP-13 | Passed | 0.0001708 | Describe_exit_evidence(0, 9, SystemRequest, Antiphon stop requested; killed by signal 9 (SIGKILL); sender unconfirmed, True) |
| CP-13 | Passed | 0.0000315 | Describe_exit_evidence(137, 0, OperatorRequest, process exited with exit code 137, False) |
| CP-13 | Passed | 0.0000337 | Describe_exit_evidence(null, null, OperatorRequest, Antiphon stop requested; signal unknown, False) |
| CP-13 | Passed | 0.0000309 | Describe_exit_evidence(null, null, ProcessExit, process ended (exit code unknown; signal unknown), False) |
| CP-13 | Passed | 0.0000382 | Describe_exit_evidence(0, null, ProcessExit, process ended (exit code 0; signal unknown), True) |
| CP-13 | Passed | 0.0000337 | Describe_exit_evidence(23, 0, ProcessExit, process exited with exit code 23, False) |
| CP-13 | Passed | 0.0000310 | Describe_exit_evidence(0, 0, ProcessExit, process exited with exit code 0, True) |
| CP-13 | Passed | 0.0000314 | Describe_exit_evidence(0, 99, ProcessExit, killed by signal 99 (UNKNOWN); sender unknown, False) |
| CP-13 | Passed | 0.0000497 | Describe_exit_evidence(0, 15, ProcessExit, killed by signal 15 (SIGTERM); sender unknown, False) |
| CP-13 | Passed | 0.0000333 | Describe_exit_evidence(0, 15, OperatorRequest, Antiphon stop requested; killed by signal 15 (SIGTERM); sender unconfirmed, True) |
| CP-13 | Passed | 0.0000365 | Describe_exit_evidence(0, 9, ProcessExit, killed by signal 9 (SIGKILL); sender unknown, False) |
| CP-6 | Passed | 0.0119031 | ClassifyFailure_empty_stopped_without_operator_source_is_StoppedBeforeFirstPrompt |
| CP-6 | Passed | 0.0011934 | ClassifyFailure_empty_stopped_with_OperatorRequest_names_the_source |
| CP-6 | Passed | 0.0009692 | ClassifyFailure_process_exit_and_legacy_unknown_are_not_operator_stops |
| CP-6 | Passed | 0.0005468 | ClassifyFailure_names_SystemRequest_and_ProcessExit_and_reserves_not_recorded_for_Unknown |
| CP-6 | Passed | 0.0004189 | ClassifyFailure_preserves_an_existing_session_failure_reason |
| CP-6 | Passed | 0.0004061 | ClassifyFailure_ProviderSignInRequired_is_AuthenticationRequired_with_reason_verbatim |
| CP-7 | Passed | 18.1307206 | a_stopped_session_with_a_marked_done_report_succeeds_instead_of_failing |
| CP-7 | Passed | 1.3229750 | a_stopped_session_with_transcript_but_no_report_still_fails |
| CP-7 | Passed | 3.8817418 | the_predicate_and_the_attention_projection_agree_on_every_case |
| CP-14 | Passed | 29.9954097 | C502_V1_queued_A_exit_released_after_B_resumed_changes_nothing |
| CP-14 | Passed | 11.9184335 | C502_V3_matching_B_exit_closes_B_after_the_stale_A_exit_was_ignored |
| CP-14 | Passed | 2.2529188 | C502_V9_severed_stream_then_a_snapshot_Exited_A_does_not_close_B |
| CP-15 | Passed | 6.1888763 | A_ready_gate_kill_records_the_not_ready_error_instead_of_ProcessExit |
| CP-15 | Passed | 0.2538253 | A_ready_gate_kill_leaves_an_earlier_crash_stamp |
| CP-15 | Passed | 0.1071397 | A_ready_gate_kill_leaves_a_stamp_from_just_before_the_kill |
| CP-15 | Passed | 0.1109096 | A_ready_gate_kill_leaves_an_in_window_natural_crash |
| CP-15 | Passed | 0.0957475 | A_ready_gate_kill_leaves_a_stamp_the_kill_did_not_produce |
| CP-15 | Passed | 0.2325482 | A_ready_gate_kill_leaves_a_stamp_from_another_generation |
| CP-15 | Passed | 0.1074224 | A_ready_gate_kill_leaves_a_natural_zero_exit_when_the_watcher_is_late |


Owed after S3b: S4 CP-9=11, CP-10=8 and CP-11=8; final Windows CP-2=7; separate owned cgroup-v1 CP-12=2 (36 required results). Native Windows behavior has a portable signed-code and ownership control here, but the actual modern/inbox witnesses remain owed at the final combined S4 source. No Windows/desktop files, C:\Antiphon, live qualification d47848cc, native cgroup fixture, tags, publishing, activation or deployment were accessed/executed. macOS and cgroup-v2 are outside this card's qualification claim. S1..S3b must not land separately; full-fix ordinary/native Review and the separately commissioned mutation gates remain open. S1/S2's documented static-lint limitations were not silently relabeled a pass.

Exact owed commands at the FINAL combined S4 source, on the corresponding native lane (retain Windows TEMP on Windows; set TEMP=/tmp only on the Linux worker). Keep CP-12 in its own run. Rebuild/import there; these commands do not reuse this slice's binary or receipt:

```powershell
# On the appropriate native checkout, at the final combined source:
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1619-checkpoint-tool -SlotWaitMinutes 25 -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-card1619-tools/ --property:UseAppHost=false
$tool = 'tools/Antiphon.Checkpoints/bin-card1619-tools/Antiphon.Checkpoints.dll'
$plan = 'docs/superpowers/plans/2026-10-30-card-1619-linux-pty-exit-signal-plan.md'
$source = (git rev-parse HEAD).Trim()
$runKey = 'card1619-native-' + [guid]::NewGuid().ToString('N')
$manifest = '.antiphon/' + $runKey + '.yaml'
dotnet $tool import --plan $plan --out $manifest
if ($LASTEXITCODE -ne 0) { throw 'Plan import failed' }
$yaml = Get-Content -LiteralPath $manifest -Raw
$yaml = $yaml.Replace('  properties: {}', "  properties:`n    UseAppHost: false")
$yaml = $yaml -replace '(?m)^  slotWaitMinutes: 45\r?$', '  slotWaitMinutes: 25'
$phaseRoot = [IO.Path]::GetFullPath('.antiphon/' + $runKey + '-timing')
$yaml = [regex]::Replace($yaml, '(?ms)^- id: (CP-(?:2|12))\r?\n.*?(?=^- id: CP-|\z)', {
    param($match)
    $id = $match.Groups[1].Value
    $timeout = if ($id -eq 'CP-2') { 8 } else { 4 }
    $suite = if ($id -eq 'CP-2') { 'Antiphon.Agents.Pty.Tests' } else { 'Antiphon.SessionRunner.Tests' }
    $group = [regex]::Match($match.Value, '(?m)^  group: (.+)\r?$').Groups[1].Value.Trim()
    $block = [regex]::Replace($match.Value, '(?m)^  timeoutMinutes: \d+\r?$', ('  timeoutMinutes: ' + $timeout))
    $block.Replace('    TUNIT_MAX_PARALLEL_TESTS: 1', "    TUNIT_MAX_PARALLEL_TESTS: 1`n    ANTIPHON_TEST_PHASE_ROOT: $phaseRoot`n    ANTIPHON_TEST_PHASE_RUN_ID: $runKey`n    ANTIPHON_TEST_PHASE_GROUP_ID: $group`n    ANTIPHON_TEST_PHASE_ATTEMPT: 1`n    ANTIPHON_TEST_PHASE_SUITE: $suite")
})
Set-Content -LiteralPath $manifest -Value $yaml -NoNewline

# Native Windows only, AFTER S4, preserving native Windows TEMP:
dotnet $tool run $manifest --rows CP-2 --expected-source-sha $source --serial --total-timeout 16m --hard-wall-timeout 26m --max-wait 45s --heartbeat 15s

# Separate owned cgroup-v1 fixture, AFTER S4; do not combine with CP-9/10/11:
$env:TEMP = '/tmp'
dotnet $tool run $manifest --rows CP-12 --expected-source-sha $source --serial --total-timeout 14m --hard-wall-timeout 24m --max-wait 45s --heartbeat 15s

# After EACH exit 75, use that invocation's exact absolute printed run directory:
dotnet $tool wait --run $runDirectory --expected-source-sha $source --max-wait 45s --heartbeat 15s
# Repeat wait until terminal, then validate the appropriate single row:
dotnet $tool validate --evidence (Join-Path $runDirectory 'report.json') --rows CP-2 --expected-source-sha $source
# For the separate cgroup invocation use --rows CP-12 instead.
```

S3b replay, only when needed at a selected committed source: bootstrap/import/pin the original closed rows with CP-13/6/7/14/15 timeouts 8/2/4/4/4 minutes and valid phase metadata, then run `--rows CP-13,CP-6,CP-7,CP-14,CP-15 --serial --total-timeout 29m --hard-wall-timeout 39m --max-wait 45s --heartbeat 15s`; wait to terminal and validate all five with that full source SHA. Current receipts remain bound to 9a43df07db611c8dc24791744af5c8752410a65d, not future code.


Essential unedited checkpoint/provenance lines from every historical attempt and final run:

Source report: /work/worktrees/task-949a4df5/.antiphon/card1619-s3b-red/20261030-151611-a1fd/report.md

```text
source: fb8a8fa576ac2f12b70e0eae1e0687596c13da66 state=clean buildSource=verified
CHECKPOINT CP-13 commit=fb8a8fa576ac2f12b70e0eae1e0687596c13da66 build=ok filter=/*/*/(Card1619ExitPolicyTests*)|(Card1619ExitConsumerTests*)/* executed=23 passed=1 failed=22 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-red/20261030-151611-a1fd/rows/CP-13/run.trx slot=granted waited=30s dirty=0 source=fb8a8fa576ac2f12b70e0eae1e0687596c13da66 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-13 waited=30s executed=88.702s wall=118.779s
PHASES CP-13 slotWait=90s build=237.9318238s startup=55.8204443s testsWall=28.6733642s teardown=3.2572825s hostWall=87.7513068s
CHECKPOINT CP-6 commit=fb8a8fa576ac2f12b70e0eae1e0687596c13da66 build=reused filter=/*/*/AgentTaskLivenessTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-red/20261030-151611-a1fd/rows/CP-6/run.trx slot=granted waited=30s dirty=0 source=fb8a8fa576ac2f12b70e0eae1e0687596c13da66 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=30s executed=10.834s wall=40.919s
PHASES CP-6 slotWait=30s build=0s startup=8.2072487s testsWall=0.0844975s teardown=1.1537107s hostWall=9.4454567s
CHECKPOINT CP-7 commit=fb8a8fa576ac2f12b70e0eae1e0687596c13da66 build=reused filter=/*/*/AgentTaskDeadSessionReconciliationTests/(a_stopped_session_with_a_marked_done_report_succeeds_instead_of_failing*)|(a_stopped_session_with_transcript_but_no_report_still_fails*)|(the_predicate_and_the_attention_projection_agree_on_every_case*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-red/20261030-151611-a1fd/rows/CP-7/run.trx slot=granted waited=60s dirty=0 source=fb8a8fa576ac2f12b70e0eae1e0687596c13da66 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=60s executed=53.169s wall=113.533s
PHASES CP-7 slotWait=60s build=0s startup=35.8699674s testsWall=13.5301777s teardown=2.9389489s hostWall=52.3390938s
CHECKPOINT CP-14 commit=fb8a8fa576ac2f12b70e0eae1e0687596c13da66 build=reused filter=/*/*/SessionGenerationExitTests/(C502_V1_queued_A_exit_released_after_B_resumed_changes_nothing*)|(C502_V3_matching_B_exit_closes_B_after_the_stale_A_exit_was_ignored*)|(C502_V9_severed_stream_then_a_snapshot_Exited_A_does_not_close_B*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-red/20261030-151611-a1fd/rows/CP-14/run.trx slot=granted waited=60s dirty=0 source=fb8a8fa576ac2f12b70e0eae1e0687596c13da66 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-14 waited=60s executed=60.709s wall=120.810s
PHASES CP-14 slotWait=60s build=0s startup=20.4445475s testsWall=34.4608834s teardown=4.5731498s hostWall=59.4785802s
CHECKPOINT CP-15 commit=fb8a8fa576ac2f12b70e0eae1e0687596c13da66 build=reused filter=/*/*/AgentSessionLaunchFailureTests/(A_ready_gate_kill_*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-red/20261030-151611-a1fd/rows/CP-15/run.trx slot=granted waited=165s dirty=0 source=fb8a8fa576ac2f12b70e0eae1e0687596c13da66 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-15 waited=165s executed=55.443s wall=220.568s
PHASES CP-15 slotWait=165s build=0s startup=35.9414809s testsWall=14.2126572s teardown=3.9274923s hostWall=54.0816304s
BUDGET wall=942.324s execution=537.219s admissionOnly=405.105s
```

Source report: /work/worktrees/task-949a4df5/.antiphon/card1619-s3b-red-structured/20261030-153354-607f/report.md

```text
source: 5ade97380d0f1812974d67d97e33649f05006b52 state=clean buildSource=unknown
CHECKPOINT CP-13 commit=5ade97380d0f1812974d67d97e33649f05006b52 build=failed filter=/*/*/(Card1619ExitPolicyTests*)|(Card1619ExitConsumerTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5ade97380d0f1812974d67d97e33649f05006b52 sourceState=clean buildSource=unknown
CHECKPOINT-TIMING CP-13 waited=0s executed=0.000s wall=0.000s
CHECKPOINT CP-14 commit=5ade97380d0f1812974d67d97e33649f05006b52 build=failed filter=/*/*/SessionGenerationExitTests/(C502_V1_queued_A_exit_released_after_B_resumed_changes_nothing*)|(C502_V3_matching_B_exit_closes_B_after_the_stale_A_exit_was_ignored*)|(C502_V9_severed_stream_then_a_snapshot_Exited_A_does_not_close_B*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5ade97380d0f1812974d67d97e33649f05006b52 sourceState=clean buildSource=unknown
CHECKPOINT-TIMING CP-14 waited=0s executed=0.000s wall=0.000s
CHECKPOINT CP-15 commit=5ade97380d0f1812974d67d97e33649f05006b52 build=failed filter=/*/*/AgentSessionLaunchFailureTests/(A_ready_gate_kill_*) executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=5ade97380d0f1812974d67d97e33649f05006b52 sourceState=clean buildSource=unknown
CHECKPOINT-TIMING CP-15 waited=0s executed=0.000s wall=0.000s
BUDGET wall=299.271s execution=299.271s admissionOnly=0.000s
```

Source report: /work/worktrees/task-949a4df5/.antiphon/card1619-s3b-red-structured/20261030-154031-2a42/report.md

```text
source: 749530d56bf4d2b153d46dcdc06aa25ad0104703 state=clean buildSource=verified
CHECKPOINT CP-13 commit=749530d56bf4d2b153d46dcdc06aa25ad0104703 build=ok filter=/*/*/(Card1619ExitPolicyTests*)|(Card1619ExitConsumerTests*)/* executed=23 passed=1 failed=22 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-red-structured/20261030-154031-2a42/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=749530d56bf4d2b153d46dcdc06aa25ad0104703 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-13 waited=0s executed=95.704s wall=95.784s
PHASES CP-13 slotWait=0s build=228.5794201s startup=49.4638939s testsWall=42.4383721s teardown=2.7203869s hostWall=94.6226803s
CHECKPOINT CP-14 commit=749530d56bf4d2b153d46dcdc06aa25ad0104703 build=reused filter=/*/*/SessionGenerationExitTests/(C502_V1_queued_A_exit_released_after_B_resumed_changes_nothing*)|(C502_V3_matching_B_exit_closes_B_after_the_stale_A_exit_was_ignored*)|(C502_V9_severed_stream_then_a_snapshot_Exited_A_does_not_close_B*) executed=3 passed=2 failed=1 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-red-structured/20261030-154031-2a42/rows/CP-14/run.trx slot=granted waited=45s dirty=0 source=749530d56bf4d2b153d46dcdc06aa25ad0104703 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-14 waited=45s executed=68.646s wall=113.780s
PHASES CP-14 slotWait=45s build=0s startup=22.8374294s testsWall=40.9625986s teardown=3.217547s hostWall=67.0175753s
CHECKPOINT CP-15 commit=749530d56bf4d2b153d46dcdc06aa25ad0104703 build=reused filter=/*/*/AgentSessionLaunchFailureTests/(A_ready_gate_kill_*) executed=7 passed=6 failed=1 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-red-structured/20261030-154031-2a42/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=749530d56bf4d2b153d46dcdc06aa25ad0104703 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-15 waited=0s executed=56.581s wall=57.086s
PHASES CP-15 slotWait=0s build=0s startup=35.9436348s testsWall=13.4622615s teardown=6.0121457s hostWall=55.4180421s
BUDGET wall=512.770s execution=467.728s admissionOnly=45.042s
```

Source report: /work/worktrees/task-949a4df5/.antiphon/card1619-s3b-green/20261030-155109-2394/report.md

```text
source: e00bd6426cadeadda1294f89d62e11302ce3947d state=clean buildSource=verified
CHECKPOINT CP-13 commit=e00bd6426cadeadda1294f89d62e11302ce3947d build=ok filter=/*/*/(Card1619ExitPolicyTests*)|(Card1619ExitConsumerTests*)/* executed=23 passed=22 failed=1 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-green/20261030-155109-2394/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=e00bd6426cadeadda1294f89d62e11302ce3947d sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-13 waited=0s executed=91.321s wall=91.381s
PHASES CP-13 slotWait=30s build=310.6242824s startup=48.7754806s testsWall=37.1548717s teardown=3.8589213s hostWall=89.7892222s
CHECKPOINT CP-6 commit=e00bd6426cadeadda1294f89d62e11302ce3947d build=reused filter=/*/*/AgentTaskLivenessTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-green/20261030-155109-2394/rows/CP-6/run.trx slot=granted waited=45s dirty=0 source=e00bd6426cadeadda1294f89d62e11302ce3947d sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=45s executed=5.283s wall=50.388s
PHASES CP-6 slotWait=45s build=0s startup=4.1871452s testsWall=0.0573832s teardown=0.42089s hostWall=4.6654178s
CHECKPOINT CP-7 commit=e00bd6426cadeadda1294f89d62e11302ce3947d build=reused filter=/*/*/AgentTaskDeadSessionReconciliationTests/(a_stopped_session_with_a_marked_done_report_succeeds_instead_of_failing*)|(a_stopped_session_with_transcript_but_no_report_still_fails*)|(the_predicate_and_the_attention_projection_agree_on_every_case*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-green/20261030-155109-2394/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=e00bd6426cadeadda1294f89d62e11302ce3947d sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=0s executed=49.375s wall=49.429s
PHASES CP-7 slotWait=0s build=0s startup=27.2382568s testsWall=19.1318065s teardown=1.8613194s hostWall=48.2313828s
CHECKPOINT CP-14 commit=e00bd6426cadeadda1294f89d62e11302ce3947d build=reused filter=/*/*/SessionGenerationExitTests/(C502_V1_queued_A_exit_released_after_B_resumed_changes_nothing*)|(C502_V3_matching_B_exit_closes_B_after_the_stale_A_exit_was_ignored*)|(C502_V9_severed_stream_then_a_snapshot_Exited_A_does_not_close_B*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-green/20261030-155109-2394/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=e00bd6426cadeadda1294f89d62e11302ce3947d sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-14 waited=0s executed=69.237s wall=69.710s
PHASES CP-14 slotWait=0s build=0s startup=21.3094931s testsWall=41.4098472s teardown=4.3660037s hostWall=67.0853435s
CHECKPOINT CP-15 commit=e00bd6426cadeadda1294f89d62e11302ce3947d build=reused filter=/*/*/AgentSessionLaunchFailureTests/(A_ready_gate_kill_*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-green/20261030-155109-2394/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=e00bd6426cadeadda1294f89d62e11302ce3947d sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-15 waited=0s executed=30.650s wall=30.917s
PHASES CP-15 slotWait=0s build=0s startup=19.6554876s testsWall=7.2009929s teardown=2.6546624s hostWall=29.5111428s
BUDGET wall=676.388s execution=601.400s admissionOnly=74.988s
```

Source report: /work/worktrees/task-949a4df5/.antiphon/card1619-s3b-green-final/20261030-160324-fbbc/report.md

```text
source: 9a43df07db611c8dc24791744af5c8752410a65d state=clean buildSource=verified
CHECKPOINT CP-13 commit=9a43df07db611c8dc24791744af5c8752410a65d build=ok filter=/*/*/(Card1619ExitPolicyTests*)|(Card1619ExitConsumerTests*)/* executed=23 passed=23 failed=0 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-green-final/20261030-160324-fbbc/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=9a43df07db611c8dc24791744af5c8752410a65d sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-13 waited=0s executed=130.623s wall=130.744s
PHASES CP-13 slotWait=0s build=252.3260704s startup=76.8569806s testsWall=46.9131744s teardown=4.8478268s hostWall=128.6182323s
CHECKPOINT CP-6 commit=9a43df07db611c8dc24791744af5c8752410a65d build=reused filter=/*/*/AgentTaskLivenessTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-green-final/20261030-160324-fbbc/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=9a43df07db611c8dc24791744af5c8752410a65d sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-6 waited=0s executed=5.327s wall=5.422s
PHASES CP-6 slotWait=0s build=0s startup=3.3931691s testsWall=0.0511493s teardown=0.5318366s hostWall=3.976155s
CHECKPOINT CP-7 commit=9a43df07db611c8dc24791744af5c8752410a65d build=reused filter=/*/*/AgentTaskDeadSessionReconciliationTests/(a_stopped_session_with_a_marked_done_report_succeeds_instead_of_failing*)|(a_stopped_session_with_transcript_but_no_report_still_fails*)|(the_predicate_and_the_attention_projection_agree_on_every_case*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-green-final/20261030-160324-fbbc/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=9a43df07db611c8dc24791744af5c8752410a65d sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=0s executed=59.967s wall=60.024s
PHASES CP-7 slotWait=0s build=0s startup=28.7254778s testsWall=23.4361336s teardown=6.4373352s hostWall=58.5989466s
CHECKPOINT CP-14 commit=9a43df07db611c8dc24791744af5c8752410a65d build=reused filter=/*/*/SessionGenerationExitTests/(C502_V1_queued_A_exit_released_after_B_resumed_changes_nothing*)|(C502_V3_matching_B_exit_closes_B_after_the_stale_A_exit_was_ignored*)|(C502_V9_severed_stream_then_a_snapshot_Exited_A_does_not_close_B*) executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-green-final/20261030-160324-fbbc/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=9a43df07db611c8dc24791744af5c8752410a65d sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-14 waited=0s executed=74.415s wall=74.495s
PHASES CP-14 slotWait=0s build=0s startup=26.0516603s testsWall=44.2174191s teardown=2.3633714s hostWall=72.6324507s
CHECKPOINT CP-15 commit=9a43df07db611c8dc24791744af5c8752410a65d build=reused filter=/*/*/AgentSessionLaunchFailureTests/(A_ready_gate_kill_*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-949a4df5/.antiphon/card1619-s3b-green-final/20261030-160324-fbbc/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=9a43df07db611c8dc24791744af5c8752410a65d sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-15 waited=0s executed=50.796s wall=50.848s
PHASES CP-15 slotWait=0s build=0s startup=21.1118678s testsWall=7.1341689s teardown=5.8006569s hostWall=34.0466934s
BUDGET wall=669.330s execution=669.324s admissionOnly=0.005s
```

Real graph check output:

```text
BUILD SLOT granted lease=9146e1de-5aed-4faa-a04f-548349133a00 waited=0s maxcpucount=6
digest=7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99 count=68
not-built: docs/investigations/evidence/card-0462/Reproduce.csproj
not-built: scripts/fixtures/nightly/c1375-probe/Probe.csproj
not-built: scripts/fixtures/nightly/c487-probe/Probe.csproj
not-built: tests/Antiphon.TimeoutAudit.Tests/Antiphon.TimeoutAudit.Tests.csproj
not-built: tools/Antiphon.NativeLinuxMigrationAudit/Antiphon.NativeLinuxMigrationAudit.csproj
canary=/work/worktrees/antiphon-landgraph-canary-41752ce28b694e64b15f1b0658bfe555
phase=pass2
approved
BUILD SLOT released lease=9146e1de-5aed-4faa-a04f-548349133a00 held=40s
```

--- next stage ---
next: code
handoff: Implement only S4 from this report commit's pushed tip via -StartRef, retaining the reviewed plan and S1/S2/S3a/S3b source. S4 owns issuer memory, conservative attribution in SessionExitPolicy, the diagnostic store after signaling/outside the gate, its fixed 100 ms deadline, and blocked-writer/ordering/native witnesses. Run CP-9/10/11 together; CP-12 stays its own native cgroup-v1 run. Final Windows CP-2 and full-fix Review remain required. Do not land S1..S3b independently.
artifact: /work/worktrees/task-949a4df5/docs/superpowers/plans/2026-10-30-card-1619-linux-pty-exit-signal-plan.md
