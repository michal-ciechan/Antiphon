S4 is implemented and pushed; CP-9/10/11 passed 11/8/8 (27 total), zero failures/skips, with independently validated clean source and build receipts. CP-12 separately skipped both native witnesses because the entrypoint namespace marker is absent. Final ordinary/native Opus Review is next; do not land or activate yet.

Assigned base: 52997c7a59683bb2227db3b10cd596cfc30e1183. Product/test source: b6f3f91568852c416642055a41ac020601044400. The later report-only commit changes no tested product source. Standing caller authority sufficed; no approval was needed. The S1..S4 product chain is now implemented, but its native qualification and Final Review gates remain incomplete.

S4 contents and trust boundary:

- ProcessStopIntentStore owns generation-bound issuer memory and a separate diagnostic writer. Requested memory precedes the authorized operation; issued is published only after an observable successful operation. Snapshot publication takes that memory gate. Diagnostic offers occur after signaling and outside it, including nested custody/helper attempts, whose offers are deferred until the outer gate releases. No record IO or logger runs inline on the signal/observer thread. No signal, stop completion, observer or shutdown awaits the writer.
- TryRecordAsync enforces a 100 ms deadline from offer, including queueing, using an injected TimeProvider, cancellation and monotonic expiry checks. One dedicated worker and one pending snapshot bound IO per owner; an expired in-flight operation remains owned and rejects more work while stuck. No retry loop or per-request thread is created. Eventual writer errors are observed. A timeout does not cancel a kernel fsync. Fault/saturation/timeout logs once and retains durable=false; it never downgrades matching live issuer attribution.
- Diagnostic files use create-new, WriteThrough, Flush(true), atomic rename and actual Linux directory fsync. Unsupported fsync, ENOSPC or an unwritable root degrades diagnostics only. Ordinary host manifest flush alone does not fsync its directory. An interrupted write can leave no final record. Serialized durability/provenance never provides authority.
- HostSession and SessionRunnerRuntime carry issuer memory through the S3a contract. Connected-host evidence is checked against session/generation, live host instance, target PID/start and target kind. Disk manifests and recovery journal reads always overwrite provenance to RecoveredUnverified. A surviving issuing host can carry its memory across a runner restart; a dead issuer cannot rebuild authority from its files or request id. Runtime-owned host cleanup can publish its own memory as host-target evidence without inventing a child wait observation.
- SessionExitPolicy qualifies stopped by Antiphon only for a matching live issued child/custody attempt and the observed signal. Durability changes the record qualifier only. Server-owned request alone says Antiphon stop requested / sender unconfirmed. Planted/recovered records say sender unconfirmed (untrusted stop record). Host-target issued / child-unobserved text remains distinct. Failed/requested-only, mismatched signal/generation and normal-exit races cannot qualify death. Late stronger issuer/durability evidence enriches the existing actual code/signal without erasing it; a different earlier requested attempt cannot block matching issued evidence.
- A known-signal policy cause alone no longer fabricates an independent SystemRequest. Actual prior operator/policy sources retain first-writer priority. Legacy/no-signal source behavior is retained. Signal-first classification, adapter compatibility, report-first settlement, CPU-spin resumability and MemoryKilled failure policy are unchanged from S3b.
- The primitive PTY callback avoids a PTY-to-host dependency. Direct in-process runners retain local issued-signal memory through that callback surface; host/runner paths use the protocol store. Native PID/start checks prevent a reused root from being signaled. Porta's pinned libporta_pty pty_kill ABI supplies the observable SIGHUP fallback result, preserving its native behavior. Disposal's stream-close plus swallowed SIGHUP is requested-only, never issued proof. Linux closes before joining its blocked reader; Windows keeps its original teardown order.
- Refused/unobserved Linux pending/orphan PID stops retain the current owner/sidecar and existing retry behavior until process death is observed. Helper success is not custody success: CARD-1614 namespace/container checks, post-drain accounting, Unknown recovery and retained cgroups remain. No privilege, custody advertisement, stop/escalation/parking deadline, report success or release authority was added. Windows native spawn/kill/job/signed-code behavior is unchanged in source; actual CP-2 qualification remains owed.

Kill-site audit (src/server kill/SIGKILL/SIGTERM/SIGHUP census repeated; no unlisted session kill was silently omitted):

| Authorized entry/site | Recorder and evidence |
|---|---|
| Operator stop, CPU watchdog (existing 5 s KillTimeout), CARD-0079 conditional stop, pool release and KillAll | Existing server/runtime checks -> RunnerSession.KillAsync -> HostSession.RecordStop -> PtyAgentRunner/PtySession low-level callback -> ProcessStopIntentStore. Actual child wait remains the exit evidence; server KillGraceMs remains 5000. |
| Host KillMessage and owner/shutdown disposal | Same host store. Tree SIGKILL and fallback SIGHUP are separate attempts. No normal-exit race is latched as KilledByRequest. |
| PtySession.Kill and Dispose; PtyAgentRunner.DisposeAsync | Same primitive callback; native result for the fallback, requested-only upstream disposal, close before read join on Linux. Disposal after observed exit creates no new attribution. |
| SessionRunnerRuntime.KillPidBestEffort, TryKillOrphanedChild, CloseHerdrAfterBarFailed and KillPendingHerdr | Same runtime store after existing ownership checks, with PID/start binding. Refusal/unobserved exit keeps stop debt/sidecar. Wrong generation sends nothing through KillGeneration. |
| HerdrPaneChild.KillPidBestEffort (additional audit hit) | Injected shared runtime store, child target; foreign-foreground protection and attached-origin nontermination retained. Unobserved Linux PID stop does not publish verified closure. |
| SessionRunnerRuntime.KillHostIfStillOurs (launch cleanup / shutdown fallback) | Same runtime store, separately host-target, own start identity retained. A host kill cannot populate child code/signal. |
| PtyHostLauncher.TryKillSpawnedHostAsync (former :229) | Same recorder, LaunchCleanup / Host. Linux verifies the shadow image and exact session argument before signaling a returned PID. Launch exception remains primary. |
| PtyAgentRunner.SealAndObserveCustodyAsync / IPtyCustodyContainment | Host memory before installed helper invocation; record afterward, outside the gate. Helper outcome is an issued operation only; accounting still owns custody. |
| IPtyCustodyContainment helper timeout tree-kill | Same callback, Helper target. Its successful kill is not root exit/custody proof. Existing helper timeout is unchanged. |
| ILinuxCustodyRecovery / RunnerCustodyLedger.ReleaseDeadHost | Shared runner store; existing seal, namespace/container, host-gone checks precede the helper. Outcome remains Unknown even after issued helper work; producer receipt is not fabricated. |
| ModernConPtyConnection / WindowsJobObject | Windows native kill/job/close behavior unchanged. No Linux signal inference is added. |
| LinuxCgroupCustodyProbe, RunnerProcessReaper, CLI/auth/version probes, workspace/git/land/checkpoint/hook/command helpers, FakeGrok child and watchdog signal registration | Separate command/probe/control processes or signal receivers, not tracked PTY death consumers. As the plan directs, no probe/helper result becomes child wait or custody evidence. Their kill semantics were not globally rewritten. |

Changed product areas: src/Antiphon.PtyHost.Protocol/{ProcessStopIntent,ProcessStopIntentStore}.cs; src/Antiphon.Agents.Pty/{PtyStopCallback,PtySession,PtyAgentRunner,IPtyCustodyContainment}.cs; HostSession/HostCustodyJournal; PtyHostLauncher; SessionRunnerRuntime/HerdrPaneChild/ILinuxCustodyRecovery/RunnerCustodyLedger; application SessionExitPolicy/SessionExitEvidence/SessionTermination and live/reconciliation source calls. Session-runtime and PTY owner docs describe the boundary. Tests add the four exact Card1619 S4 classes and reuse existing native/DB/release fixtures. Existing assertions were not removed or relaxed; fixture visibility, setup and teardown were made reusable/owned. No definition file (csproj/sln/props/targets/SDK/tool/package/lock) or migration changed in S4. The full chain still contains S3a's additive migration; no EF operation or -Update was run here.

Red-first and historical outcomes (not relabeled green):

1. d2378d8443b489acc71c10983317eb2e7ef76037 introduced compile-compatible unused seams and real stop witnesses. Run 20261030-163544-025e was stopped/joined while waiting for admission after inspection caught witness API/import mistakes, before any tests. Terminal wait 6. Initial stop tool-copy cleanup reported IOException; that exact diagnostic residue is retained, not swept as test output.
2. b737f3d428c6c640c203263c0fb464c6a004c686, run 20261030-163652-d383: CP-9 1 passed / 10 failed / 0 skipped. Real child exits reached the intended missing-memory/writer assertions. CP-10/11 had no execution: the new native test incorrectly treated receipt bytes as decoded receipt, and the new server test lacked its DTO import. These compile errors are not red credit.
3. 25fda9ae6aeea7764c9f95fa3cfdc3b0ec401105, run 20261030-164812-a4fd: CP-9 1/10, CP-10 1/7, CP-11 4/4; zero skips. Intended feature failures include null issuer intent after actual SIGKILL, absent writer invocation, missing requested/issued memory, lost late enrichment and missing qualified final text. Natural/recovered-record/no-intent controls already pass and remain method-scoped PC obligations for the separately commissioned mutation stage. Early null-snapshot cases raised NullReferenceException rather than explicit null assertions; the final witnesses make those assertions explicit.
4. a3cc630ca9b91f16070984397a70d054a30d2cad strengthens the actual launcher cleanup, actual PTY fallback/disposal, synchronous-before-Task writer stall, and real planted-journal/dead-host recovery paths. Run 20261030-170359-9561 was stopped/joined after CP-9 failed to compile because its project does not link the shared timing helper. CP-10 was joined during its partial row; no final credit. Terminal wait 6. Replaced the timing calls with local correlated phase measurements, without changing the graph or test assertions.
5. 9eb6c326ef7c15dd337961a736bca37e517d5751, run 20261030-170637-8c55: CP-9 1/10, CP-10 1/7, CP-11 4/4, no skips. Launcher cleanup really executes but has no record; the not-yet-wired fallback seam leaves actual SIGKILL instead of SIGHUP; real self-SIGKILL plus dead-host adoption loses the journal. The live observer is deliberately not blocked on a disk read; recovery reads it separately, always unverified.
6. c069ba187803cad7ec72b41418854a77bedb1232 adds the no-inferred-server-request assertion. Run 20261030-171903-1197 was stopped/joined during the CP-9 admission interval to include the discovered pending-stop-debt witness before repair. Its partial work has no qualification credit. Terminal wait 6.
7. 8fbbb36e2c54719b304b7a9bc55fefa8a9f0b7cb, run 20261030-172243-1e6d: CP-9 1 passed/10 failed, CP-10 1/7, CP-11 3/5, no skips. The new pending arm exercises real Runtime.KillAsync on an owned ready PTY child with mismatched creation identity and requires retained pending owner/sidecar. The current code incorrectly closes it. The new known-signal cause arm specifically observes SystemRequest where independently corroborated ProcessExit is required. This is the final compiled feature-red source, then repaired at b6f3f91568852c416642055a41ac020601044400.
8. Final source b6f3f91568852c416642055a41ac020601044400, run 20261030-174308-734f: CP-9 11/11, CP-10 8/8, CP-11 8/8, no failures/skips. Terminal wait 0; separate validate returned CHECKPOINT SOURCE VALID source=b6f3f91568852c416642055a41ac020601044400 rows=3. Every row has dirty=0, sourceState=clean, buildSource=verified. Synchronous blocked writers and never-completing Tasks both terminate the owned child within existing grace, assert signal latency below the provisional 250 ms boundary, retain issuer memory and time out diagnostics; fixtures release/join them. Actual record IO/fsync, unwritable /proc root, ENOSPC seam, native self-signal, fallback/disposal, real launcher failure and final DB/task/attention clauses are covered.
9. Separate CP-12 run 20261030-175959-0c54 at the same source: 0 executed, 0 passed/failed, 2 skipped; both require the absent entrypoint-published namespace marker. Terminal wait 3 (zero-tests-executed / Min not met); independent validate correctly returned CHECKPOINT SOURCE INVALID reason=report_failed. CP-12 is native-pending, not green, and is never combined with the CP-9..11 certificate.

All eight meaningful commits were individually pushed, fast-forward on the assigned branch:

```text
d2378d8443b489acc71c10983317eb2e7ef76037 test(card-1619): witness real stop ordering and issuer attribution before S4
b737f3d428c6c640c203263c0fb464c6a004c686 test(card-1619): add separate owned cgroup witnesses and correct witness compilation
25fda9ae6aeea7764c9f95fa3cfdc3b0ec401105 test(card-1619): decode custody receipt and import server event contract
a3cc630ca9b91f16070984397a70d054a30d2cad test(card-1619): use actual launcher fallback disposal and disk-recovery boundaries
9eb6c326ef7c15dd337961a736bca37e517d5751 test(card-1619): capture host fixture phases without a graph change
c069ba187803cad7ec72b41418854a77bedb1232 test(card-1619): distinguish an observed cause from a stored server stop request
8fbbb36e2c54719b304b7a9bc55fefa8a9f0b7cb test(card-1619): retain real pending-stop debt until observed exit
b6f3f91568852c416642055a41ac020601044400 fix(card-1619): record signal-first issuer memory with bounded untrusted diagnostics
```

Verification mechanics and limits:

All selected rows ran through the checkpoint tool after 15-row plan import, with UseAppHost=false, alternate output and TEMP=/tmp. CP-9/10/11 stayed together at every complete round; CP-12 ran alone. Filters/minima/roster stayed unchanged; row timeout pins are 8/8/10/4 minutes and slot admission 25 minutes as the brief permits. Duration arguments explicitly use m/s suffixes. Every executor and foreground wrapper reached terminal observation or supported stop/join; none remains at exit 75. No unleased product run or unlisted product selection occurred. One wrong expected SHA was used on an early wait-only observer; it did not alter the executor or become qualification evidence. Exact-SHA terminal waits and validation above are the certificate. Owner-read HTTP timeouts/one 502 were bounded and retained; final receipts completed normally.

Preparatory builds outside the closed product rows: checkpoint bootstrap was explicitly required, leased (105 s wait, 14 s hold), with five existing warnings and zero errors. The real land graph tool was already built and staged by the verified CP-11 build, so no extra graph build was needed. scripts/audit-land-verification-graph.ps1 -Check passed its two real evaluations and canary, digest 7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99 / count 68. The graph invocation held a granted slot for 27 s after zero wait. DLL SHA-256: 6702A566094CF1CAB8C7453486A0D138E1910A25854E91262E3C89F2AD071CE8. Canary /work/worktrees/antiphon-landgraph-canary-4d2cab602e4e4ed5a44ba747303e010c was independently confirmed absent. No -Update. Product build warnings were retained, never suppressed. Local read-only IL/import inspection confirmed Porta disposal's swallowed TryKill and native libporta_pty / pty_kill(Int32,Int32) import; it was not an extra build or test selection.

Evidence guard over assigned base..b6f3f91568852c416642055a41ac020601044400: 8 commits, zero violations. git diff --check passed. Final report-commit range guard and literal-output cleanup are recorded below. Test declarations and final TRX contain the intended CP-9=11, CP-10=8, CP-11=8 roster; no distinct class-prefix collision. CP-12 names both planned methods and records their native skips.

Performance: Debian 12/Linux x64, 24 cores, shared contended host, SDK 10.0.403, TUnit runtime .NET 9.0.22, leased maxcpucount 6. Final combined wall 942.517 s, execution 537.948 s, admission-only 404.568 s; native-pending CP-12 wall 238.099 s, execution 103.102 s, admission-only 134.997 s. There is one passing sample per new group; retain CARD-1619's provisional ceilings until independent Review supplies remaining comparable samples. No complete run exceeded 30 minutes. CP-11 shares its assembly DB container/template and clones per case; no per-test AppHost, container/migration replay, model/provider call, npm or build. Fresh native hosts are necessary because exit/stop/recovery is the subject. CP-9 records correlated local fixture phases; CP-10/11 use existing TestTiming spans, with run/group/attempt/suite metadata. Fine-grained native/IO/DB spans not captured (including the 100-span external cap) remain unknown/unattributed, never zero. Native wall latency is asserted against the provisional boundary; no invented per-syscall p95 is claimed.

| CP | Passing results | Nearest-rank per-test p95/max seconds |
|---|---:|---:|
| CP-9 | 11 | 0.7191295 / 0.7191295 |
| CP-10 | 8 | 1.8984312 / 1.8984312 |
| CP-11 | 8 | 10.9333684 / 10.9333684 |

Exact green execution roster/durations:

| CP | Seconds | Test |
|---|---:|---|
| CP-9 | 0.2227422 | Signal_precedes_durable_record_when_storage_works |
| CP-9 | 0.4978732 | Blocked_record_write_still_signals_within_bound |
| CP-9 | 0.0762455 | Write_failure_still_stops_and_preserves_live_issuer_attribution(False) |
| CP-9 | 0.072447 | Write_failure_still_stops_and_preserves_live_issuer_attribution(True) |
| CP-9 | 0.0127358 | Issued_update_follows_syscall |
| CP-9 | 0.0726022 | Crash_before_send_is_only_requested |
| CP-9 | 0.0100506 | Crash_after_send_before_update_is_unknown |
| CP-9 | 0.0296657 | Fallback_records_SIGHUP_separately |
| CP-9 | 0.0368822 | Natural_exit_race_is_not_our_kill |
| CP-9 | 0.7191295 | Host_kill_is_not_child_wait_evidence |
| CP-9 | 0.0633782 | Planted_issued_record_and_self_signal_remain_unconfirmed |
| CP-10 | 1.8440373 | Own_stop_preserves_SIGKILL_and_intent |
| CP-10 | 1.4420074 | Restart_recovers_requested_intent |
| CP-10 | 1.8984312 | Kill_all_records_each_owned_generation |
| CP-10 | 0.8072018 | Orphan_and_pending_kills_keep_identity |
| CP-10 | 0.1027405 | Custody_invocation_publishes_memory_before_helper_and_records_after |
| CP-10 | 1.537742 | Unobserved_or_failed_kill_retains_stop_debt |
| CP-10 | 1.4212642 | Unwritable_record_directory_does_not_block_stop |
| CP-10 | 1.5286428 | Blocked_record_write_still_signals_within_bound |
| CP-11 | 10.9333684 | Matching_live_issuer_intent_is_qualified_Antiphon_stop |
| CP-11 | 1.4151931 | No_intent_means_sender_unknown |
| CP-11 | 0.4976455 | Mismatched_generation_or_signal_is_not_attributed |
| CP-11 | 0.5573124 | Requested_only_and_failed_intents_remain_uncertain |
| CP-11 | 0.4265163 | Late_intent_enrichment_preserves_known_exit |
| CP-11 | 2.3193638 | Operator_and_policy_sources_are_not_overwritten |
| CP-11 | 1.4079219 | Planted_record_self_signal_stays_unconfirmed_in_task_and_attention |
| CP-11 | 1.8936119 | Server_request_without_issuer_is_request_only |

Landing status and owed work:

Fresh origin/rc/2026-10-27 = 335b4fbb3b1eac700cb8d3c64ea032d9579c6b5f: merge-tree clean, product tree 47f80ca36c150ea8e02fc002ff7aba87493dfa94. Fresh origin/master = 31f990317038635c2f1e652b6a950905fcf07a31: merge-tree has 20 conflict paths versus 13 at the assigned S4 base. The seven additional S4/master conflicts are HostCustodyJournal.cs, ILinuxCustodyRecovery.cs, RunnerCustodyLedger.cs, SessionRunnerRuntime.cs, LinuxRunnerCustodyLifecycleTests.cs, RunnerCustodyReleaseTests.cs and SessionRunner.Tests/slow-tests-allowlist.txt. They are not an RC conflict or a claim of master landability. Do the separately reviewed master port against current CARD-1614 and recreate the migration on top of CARD-1610; do not copy the RC Designer/snapshot or merge this whole RC-derived branch into master. No merge, rebase, land, target push, tag, publish, deploy, activation or desktop/Windows access occurred. Live qualification d47848cc and everything under C:\Antiphon were untouched.

Owed at the final combined source: native Windows CP-2 (7 results, modern and explicit inbox), separate owned cgroup-v1 CP-12 (2 results), and Final Opus ordinary/native Review. macOS and cgroup-v2 are outside CARD-1619's activation claim; CARD-1614's v2 obligation stays open. Prior S1/S2 advisory lint/parser limitations remain recorded in their reports, not silently relabeled. No mutation PC execution occurred; those controls remain separately commissioned. S1..S3b must not land individually; review and merge the full S1..S4 product chain to rc only after required native gates. Exclude plan/report-only branch artifacts from the eventual product port.

Exact owed native commands (run from the checkout root; each run line belongs to its corresponding native lane, and must be waited to terminal before any other group; re-bootstrap/import at final combined source on the corresponding eligible native lane; preserve Windows TEMP, set TEMP=/tmp on Linux only):

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1619-checkpoint-tool -SlotWaitMinutes 25 -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-card1619-tools/ --property:UseAppHost=false
$tool = 'tools/Antiphon.Checkpoints/bin-card1619-tools/Antiphon.Checkpoints.dll'
$plan = 'docs/superpowers/plans/2026-10-30-card-1619-linux-pty-exit-signal-plan.md'
$source = (git rev-parse HEAD).Trim()
$runKey = 'card1619-final-native-' + [guid]::NewGuid().ToString('N')
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
# Native Windows only, final combined source:
dotnet $tool run $manifest --rows CP-2 --expected-source-sha $source --serial --total-timeout 16m --hard-wall-timeout 26m --max-wait 45s --heartbeat 15s
# Owned cgroup-v1 fixture only, its OWN run:
$env:TEMP = '/tmp'
dotnet $tool run $manifest --rows CP-12 --expected-source-sha $source --serial --total-timeout 14m --hard-wall-timeout 24m --max-wait 45s --heartbeat 15s
# After EACH exit 75 use that run's exact absolute printed directory; repeat to terminal:
dotnet $tool wait --run $runDirectory --expected-source-sha $source --max-wait 45s --heartbeat 15s
# Independently validate the applicable single row; skip/zero remains incomplete:
dotnet $tool validate --evidence (Join-Path $runDirectory 'report.json') --rows CP-2 --expected-source-sha $source
# cgroup invocation uses --rows CP-12 instead.
```

S4 replay only when needed: import/pin the plan at the selected committed source, CP-9/10/11 timeouts 8/8/10, slotWaitMinutes=25 and valid timing metadata; run --rows CP-9,CP-10,CP-11 --serial --total-timeout 29m --hard-wall-timeout 39m --max-wait 45s --heartbeat 15s; wait to terminal, then validate all three together. Current green receipts bind to b6f3f91568852c416642055a41ac020601044400, not future code.

Essential unedited checkpoint/provenance lines from every completed run:

Source report: /work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-red-fixed/20261030-163652-d383/report.md

CHECKPOINT CP-9 commit=b737f3d428c6c640c203263c0fb464c6a004c686 build=ok filter=/*/*/Card1619StopIntentTests/* executed=11 passed=1 failed=10 skipped=0 trx=/work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-red-fixed/20261030-163652-d383/rows/CP-9/run.trx slot=granted waited=75s dirty=0 source=b737f3d428c6c640c203263c0fb464c6a004c686 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-9 waited=75s executed=7.047s wall=82.136s
PHASES CP-9 slotWait=225s build=30.8761199s startup=2.5906816s testsWall=2.4655323s teardown=1.1276181s hostWall=6.1838333s
CHECKPOINT CP-10 commit=b737f3d428c6c640c203263c0fb464c6a004c686 build=failed filter=/*/*/Card1619LinuxStopIntentTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=b737f3d428c6c640c203263c0fb464c6a004c686 sourceState=clean buildSource=unknown
CHECKPOINT-TIMING CP-10 waited=0s executed=0.000s wall=0.000s
CHECKPOINT CP-11 commit=b737f3d428c6c640c203263c0fb464c6a004c686 build=failed filter=/*/*/Card1619StopAttributionTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=b737f3d428c6c640c203263c0fb464c6a004c686 sourceState=clean buildSource=unknown
CHECKPOINT-TIMING CP-11 waited=0s executed=0.000s wall=0.000s
BUDGET wall=584.583s execution=269.789s admissionOnly=314.793s

Source report: /work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-red-compile-fixed/20261030-164812-a4fd/report.md

CHECKPOINT CP-9 commit=25fda9ae6aeea7764c9f95fa3cfdc3b0ec401105 build=ok filter=/*/*/Card1619StopIntentTests/* executed=11 passed=1 failed=10 skipped=0 trx=/work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-red-compile-fixed/20261030-164812-a4fd/rows/CP-9/run.trx slot=granted waited=15s dirty=0 source=25fda9ae6aeea7764c9f95fa3cfdc3b0ec401105 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-9 waited=15s executed=6.332s wall=21.391s
PHASES CP-9 slotWait=60s build=28.1805171s startup=2.2101169s testsWall=2.7851482s teardown=0.5461345s hostWall=5.541413s
CHECKPOINT CP-10 commit=25fda9ae6aeea7764c9f95fa3cfdc3b0ec401105 build=ok filter=/*/*/Card1619LinuxStopIntentTests/* executed=8 passed=1 failed=7 skipped=0 trx=/work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-red-compile-fixed/20261030-164812-a4fd/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=25fda9ae6aeea7764c9f95fa3cfdc3b0ec401105 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-10 waited=0s executed=12.615s wall=12.692s
PHASES CP-10 slotWait=60s build=85.2912602s startup=2.2087039s testsWall=9.1489624s teardown=0.5764617s hostWall=11.9341206s
CHECKPOINT CP-11 commit=25fda9ae6aeea7764c9f95fa3cfdc3b0ec401105 build=ok filter=/*/*/Card1619StopAttributionTests/* executed=8 passed=4 failed=4 skipped=0 trx=/work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-red-compile-fixed/20261030-164812-a4fd/rows/CP-11/run.trx slot=granted waited=60s dirty=0 source=25fda9ae6aeea7764c9f95fa3cfdc3b0ec401105 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-11 waited=60s executed=80.373s wall=140.465s
PHASES CP-11 slotWait=60s build=409.4521267s startup=59.811856s testsWall=16.5934478s teardown=2.2773058s hostWall=78.68261s
BUDGET wall=845.025s execution=665.136s admissionOnly=179.889s

Source report: /work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-red-real-routes-fixed/20261030-170637-8c55/report.md

CHECKPOINT CP-9 commit=9eb6c326ef7c15dd337961a736bca37e517d5751 build=ok filter=/*/*/Card1619StopIntentTests/* executed=11 passed=1 failed=10 skipped=0 trx=/work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-red-real-routes-fixed/20261030-170637-8c55/rows/CP-9/run.trx slot=granted waited=0s dirty=0 source=9eb6c326ef7c15dd337961a736bca37e517d5751 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-9 waited=0s executed=7.114s wall=7.340s
PHASES CP-9 slotWait=0s build=29.7607693s startup=2.3248493s testsWall=3.3657521s teardown=0.4767983s hostWall=6.1673993s
CHECKPOINT CP-10 commit=9eb6c326ef7c15dd337961a736bca37e517d5751 build=ok filter=/*/*/Card1619LinuxStopIntentTests/* executed=8 passed=1 failed=7 skipped=0 trx=/work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-red-real-routes-fixed/20261030-170637-8c55/rows/CP-10/run.trx slot=granted waited=0s dirty=0 source=9eb6c326ef7c15dd337961a736bca37e517d5751 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-10 waited=0s executed=19.893s wall=19.956s
PHASES CP-10 slotWait=15s build=57.2870718s startup=4.5773458s testsWall=13.6346872s teardown=0.5355409s hostWall=18.7475736s
CHECKPOINT CP-11 commit=9eb6c326ef7c15dd337961a736bca37e517d5751 build=ok filter=/*/*/Card1619StopAttributionTests/* executed=8 passed=4 failed=4 skipped=0 trx=/work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-red-real-routes-fixed/20261030-170637-8c55/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=9eb6c326ef7c15dd337961a736bca37e517d5751 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-11 waited=0s executed=72.112s wall=72.164s
PHASES CP-11 slotWait=0s build=343.9276012s startup=45.1085075s testsWall=17.5155467s teardown=2.7065488s hostWall=65.3306048s
BUDGET wall=590.379s execution=575.397s admissionOnly=14.982s

Source report: /work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-red-final-boundaries/20261030-172243-1e6d/report.md

CHECKPOINT CP-9 commit=8fbbb36e2c54719b304b7a9bc55fefa8a9f0b7cb build=ok filter=/*/*/Card1619StopIntentTests/* executed=11 passed=1 failed=10 skipped=0 trx=/work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-red-final-boundaries/20261030-172243-1e6d/rows/CP-9/run.trx slot=granted waited=45s dirty=0 source=8fbbb36e2c54719b304b7a9bc55fefa8a9f0b7cb sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-9 waited=45s executed=7.242s wall=52.432s
PHASES CP-9 slotWait=45s build=29.1353828s startup=1.6863054s testsWall=4.0713253s teardown=0.558616s hostWall=6.3162443s
CHECKPOINT CP-10 commit=8fbbb36e2c54719b304b7a9bc55fefa8a9f0b7cb build=ok filter=/*/*/Card1619LinuxStopIntentTests/* executed=8 passed=1 failed=7 skipped=0 trx=/work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-red-final-boundaries/20261030-172243-1e6d/rows/CP-10/run.trx slot=granted waited=60s dirty=0 source=8fbbb36e2c54719b304b7a9bc55fefa8a9f0b7cb sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-10 waited=60s executed=24.107s wall=84.279s
PHASES CP-10 slotWait=75s build=80.0205913s startup=6.6614759s testsWall=14.0129783s teardown=0.7921281s hostWall=21.4665825s
CHECKPOINT CP-11 commit=8fbbb36e2c54719b304b7a9bc55fefa8a9f0b7cb build=ok filter=/*/*/Card1619StopAttributionTests/* executed=8 passed=3 failed=5 skipped=0 trx=/work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-red-final-boundaries/20261030-172243-1e6d/rows/CP-11/run.trx slot=granted waited=15s dirty=0 source=8fbbb36e2c54719b304b7a9bc55fefa8a9f0b7cb sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-11 waited=15s executed=56.658s wall=72.294s
PHASES CP-11 slotWait=90s build=460.9662375s startup=37.9847474s testsWall=13.1995601s teardown=4.6690896s hostWall=55.8533971s
BUDGET wall=918.663s execution=708.635s admissionOnly=210.028s

Source report: /work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-green-first/20261030-174308-734f/report.md

CHECKPOINT CP-9 commit=b6f3f91568852c416642055a41ac020601044400 build=ok filter=/*/*/Card1619StopIntentTests/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-green-first/20261030-174308-734f/rows/CP-9/run.trx slot=granted waited=90s dirty=0 source=b6f3f91568852c416642055a41ac020601044400 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-9 waited=90s executed=10.868s wall=100.936s
PHASES CP-9 slotWait=195s build=27.9395674s startup=2.0145134s testsWall=1.8576758s teardown=1.7642241s hostWall=5.6363899s
CHECKPOINT CP-10 commit=b6f3f91568852c416642055a41ac020601044400 build=ok filter=/*/*/Card1619LinuxStopIntentTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-green-first/20261030-174308-734f/rows/CP-10/run.trx slot=granted waited=15s dirty=0 source=b6f3f91568852c416642055a41ac020601044400 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-10 waited=15s executed=14.110s wall=29.184s
PHASES CP-10 slotWait=180s build=37.7247511s startup=2.0665838s testsWall=10.6260391s teardown=0.6711682s hostWall=13.3637934s
CHECKPOINT CP-11 commit=b6f3f91568852c416642055a41ac020601044400 build=ok filter=/*/*/Card1619StopAttributionTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-green-first/20261030-174308-734f/rows/CP-11/run.trx slot=granted waited=0s dirty=0 source=b6f3f91568852c416642055a41ac020601044400 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-11 waited=0s executed=82.689s wall=82.762s
PHASES CP-11 slotWait=30s build=294.2814521s startup=56.0610297s testsWall=19.5180107s teardown=6.2168383s hostWall=81.7958786s
BUDGET wall=942.517s execution=537.948s admissionOnly=404.568s

Source report: /work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-cgroup-separate/20261030-175959-0c54/report.md

CHECKPOINT CP-12 commit=b6f3f91568852c416642055a41ac020601044400 build=ok filter=/*/*/Card1619LinuxCustodyStopIntentTests/* executed=0 passed=0 failed=0 skipped=2 trx=/work/worktrees/task-2e5a1c42/.antiphon/card1619-s4-cgroup-separate/20261030-175959-0c54/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=b6f3f91568852c416642055a41ac020601044400 sourceState=clean buildSource=verified reason=zero-tests-executed
CHECKPOINT-TIMING CP-12 waited=0s executed=4.194s wall=4.272s
PHASES CP-12 slotWait=135s build=58.6705889s startup=2.7475161s testsWall=0.0754963s teardown=0.5945393s hostWall=3.4175307s
BUDGET wall=238.099s execution=103.102s admissionOnly=134.997s

Final qualification boundary: product/test source b6f3f91568852c416642055a41ac020601044400; CP-9..11 clean-source validation passed; CP-12 qualification refused as expected, native-pending. Cleanup and report-only publication checks follow in the stored finalization entry.

Finalization: all executors are terminal (supported stops joined, final CP-9..11 wait 0, separate CP-12 wait 3, graph wrapper 0). The six task-created compiler output roots were removed by exact literal paths after those joins: tests/Antiphon.PtyHost.Tests/bin-card1619-host-intent; tests/Antiphon.SessionRunner.Tests/bin-card1619-runner-intent; tests/Antiphon.Tests/bin-card1619-attribution; tests/Antiphon.SessionRunner.Tests/bin-card1619-cgroup-intent; src/Antiphon.HostCleanup/bin-card1619-tools; tools/Antiphon.Checkpoints/bin-card1619-tools. Each was independently checked absent. Raw ignored run reports/TRX/source/build receipts, timing and builds/bin-* evidence-label folders were retained; they are not compiler roots. No unowned path was deleted. The first stopped run's tool-copy cleanup IOException residue is retained as diagnostic evidence. The report-only publication is the next commit after the tested source; its final 40-hex SHA is supplied by the completion progress line. The final whole-range evidence guard is required after that publication; no additional product tests are warranted by report-only content.

--- next stage ---
next: review
handoff: Final Opus ordinary and native Review of the complete S1..S4 chain. Native CP-2=7 and separate owned cgroup-v1 CP-12=2 remain owed at final combined source. Only after those gates and Final Review, Merge the complete product chain to rc; no partial slice land. The separate master port must resolve the current custody overlap and recreate its migration on top of CARD-1610.
artifact: /work/worktrees/task-2e5a1c42/docs/superpowers/plans/2026-10-30-card-1619-linux-pty-exit-signal-plan.md
