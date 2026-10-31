# Debug fc629ae4: C849 signal intermittence

Base: fc064a68247b0186918dfb1f04465e6be0475389. Branch: feat/card-task-fc629ae4.
Required: 40 baseline sequential / 20 loaded exact-method runs, deterministic ordering witness and repair, 60 sequential / 30 loaded green runs, C84x class, roster/census and real graph Check. Native Windows C849 remains owed; no native or live qualification operations authorized here.
Evidence root: /tmp/fc629ae4. TEMP=/tmp. All build/test drivers pass build-slot.ps1; private bin-fc629ae4 output. Task-owned runs are sequential unless explicitly collecting loaded samples; other host tasks are observed, not stopped.
Initial scope hypothesis (historical): signal delivery can overlap cleanup; inspect readiness and nested-handler scheduling before attributing either observation. No assertions were weakened. The final diagnosis and continuation outcome follow below.

## Baseline and deterministic diagnosis

Base production/test source remained fc064a68247b0186918dfb1f04465e6be0475389; checkout HEAD 368c0a890624c6cfd729ae3ab708f313d9c9d8a4 differs only by this scope report. Exact selected method: RemoteScriptContractTests.C849_Refused_launch_keeps_a_foreign_container_and_signals_release_proven_cids. Fresh TRX per invocation; no skipped or zero-test rows.

| Selection | Passed | Failed | Test p50 / p95 / maximum (s) |
|---|---:|---:|---|
| Sequential alone | 40 | 0 | 41.812 / 61.489 / 72.870 |
| Four owned CPU burners | 20 | 0 | 54.176 / 71.579 / 72.409 |

"Alone" means one selected method at a time. Host-wide inactivity was not reserved: other Antiphon.Tests/checkpoint work, compiler/build processes, Node and apt activity were observed. Per-run host census, full output and TRX are under /tmp/fc629ae4/baseline-*. The driver recorded burner PIDs and joined all four by owned handles; load.jsonl retains their lifetimes. Two brief pauses between completed methods allowed controlled-order probes. The tail of the first invalid full probe overlapped raw ordinary run 18; that invocation is excluded and replaced after load teardown, leaving 40 qualifying sequential alone invocations (41 raw ordinary runs). TRX test durations exclude controller pause time. The invalid probe returned installed=0 and is not credited as a red.

The full controlled group witness deterministically forced ordinary EXIT(2) after both CIDs publish, then delivered the existing group INT after finalization entered. This reproduced the reported group-volumes symptom with every other release assertion satisfied:

```text
signal-INT-group-installed=1
signal-INT-group-exit=130
signal-INT-group-accepted=1
signal-INT-group-containers=1
signal-INT-group-procs=1
signal-INT-group-lease=1
signal-INT-group-volumes=0
signal-INT-group-foreign=1
signal-INT-group-cid-kills=1
signal-INT-group-bounded=1
signal-INT-group-trap=1
signal-INT-group-handler-returned=0
signal-INT-group-elapsed=0
```

Root cause: real custody defect in scripts/c590-remote.sh. The signal handler exits while EXIT finalization is already active. EXIT cannot run again, so remaining finalization (including volumes) is skipped. The harness already waits for process exit before reading lease/volume facts; waiting longer cannot complete the abandoned cleanup. A minimal TERM ordering probe independently showed the same missing-finalization defect, exiting 143 before its volume marker.

The independent historical TERM exit=2 mismatch was not reproduced or attributed. It must not be claimed fixed by the group-volume repair. No speculative Docker-model or assertion changes are included.

Candidate preview only (not final-source gate credit): six externally copied full witnesses under load, 3 TERM + 3 group INT, passed all existing release facts with finalization inside the handler. Provisional pooled p95=5.140s; the deterministic regression uses approximately 2x this value (10s) for synchronization guards and explicit diagnostics. No production deadline or existing assertion is widened. Required 60/30 final stress remains a separate obligation.

Build: 0 errors / 1402 warnings, 236.36s execution plus separate real slot admission. TEMP=/tmp and UseAppHost=false; private bin-fc629ae4 outputs. The continuing local driver holds lease 0ac00030-b4d3-42f3-89cf-034f8407bd10; later sequential builds explicitly use its granted maxcpucount=6. Source changes occur only at its idle phase gates, never during a build/test. Initial candidate preview separately waited 376s for lease 3e6d6bfa-e520-4dfd-845d-5d5c8660efd7, then ran 22s.

R4: real Docker/AppHost/database/migration setup is not used by these filesystem models. Full shell/CLI body and per-test durations are retained; fine-grained filesystem/CLI/setup/teardown attribution is unknown (remainder, not zero). The >30-minute repetition budget is being handled by this commissioned Debug; measured cost prevents all 150 full repetitions plus ordinary verification inside the 75-minute task budget.

## Committed deterministic red

At 2e3012060f8ba9a5f450f08fd784f3b380ef2559, the exact new method executed two cases: 0 passed / 2 failed / 0 skipped. Both installed their signal handlers, exited at the existing 143/130 codes, killed only proven CIDs, and released the lease. Both failed only the volume-removal assertion. The INT case also preserved containers/procs/foreign survival/bounded/trap/no-return facts. Full output: /tmp/fc629ae4/red/output.log and red/run.trx. This is an assertion red, not fixture/build failure. Red build: 0 errors, 1403 warnings, 258.77s. The additional warning is TUnit0015 on the new synchronous method under the existing class Timeout (the same pattern as neighboring methods); the fixture retains its bounded shell waits.

The repair calls the existing idempotent finalizer inside the signal handler before exiting. Ownership/identity probes, foreign-object preservation, positive volume-removal proof, lease deletion, 130/143 exits and m1/m5/m6/m7 controls are retained. The native Windows C849 rerun is still owed and was not run here.

## Initial verification and historical incomplete gate

Repair source: 4272c9ad42a9449c4d081f402d9af859f431a979. The two new regression cases passed 2/2. Whole C84x selection passed 32/32 (140.117s), and RcRosterRepairTests passed 3/3 (33.095s); zero failures/skips in these green selections. Total: 37 green executions, including the two deterministic cases also present in C84x. The original refused-launch/signal/foreign-container method and all m1/m5/m6/m7 controls passed in C84x.

C1404/C1577/C1584/C1410/G2865 (C1514LandingSplitBudgetTests) were NOT RUN. The continuing driver reached its explicit budget gate before census (exit 91). A separate real build-slot attempt then timed out after one minute, exit 4, position 2; no test launched and no unleased retry occurred. Evidence: /tmp/fc629ae4/census-final-driver.log. The required final 60 sequential + 30 loaded exact-method repetitions were NOT RUN inside the 75-minute budget. These are incomplete acceptance obligations, not green or skipped tests.

The real audit-land-verification-graph.ps1 -Check approved digest E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806, count=69, at the repaired source. No -Update ran. It held separate lease 273c1241-4ed4-44bc-b0ee-643c003d2fb5 after a 15-second wait and completed in 36 seconds; it overlapped the C84x process as a read-only graph evaluator, with no second test assembly. Canary was /work/worktrees/antiphon-landgraph-canary-5595f2fbba2e4473963b9683af2be782; Check reported phase=pass2/approved.

Build provenance: red build ran at 2e3012060f8ba9a5f450f08fd784f3b380ef2559. The subsequent 16.04-second build completed with 0 errors / 1 MSB3277 warning while the Bash/docs-only repair was prepared and HEAD still named that red commit. No C# or project input changed. The driver was paused after the observed build exit, then the repair was committed/pushed before any green test began. Each green selection recorded source-sha.txt=4272c9ad42a9449c4d081f402d9af859f431a979; the shell tests read the committed runtime script directly. This is manual Debug evidence, not a checkpoint reviewedSourceClean claim.

Fetched origin master and rc/2026-10-27 without tags. merge-tree against rc passed (tree 5fcc7246e48017d5baae23386b4d78f84bc17494). Master comparison returned 50 conflicts from rc/master divergence; no merge/rebase/reset/force push was performed. check-evidence-diff.ps1 over base..repair passed with zero violations.

All 36 task-created bin-fc629ae4 output directories were removed after every build/test/evaluator exited, using their exact producer-owned paths; cleanup-inventory.json retains the inventory. Generated logs/TRX/JSON remain under /tmp/fc629ae4. No desktop/live qualification/native tests or files under C:\Antiphon were touched.

Pushed implementation/history slices:
- 368c0a890624c6cfd729ae3ab708f313d9c9d8a4 — scope.
- 3e637bf28d7f077022921e4459b2339376562fc1 — baseline and deterministic diagnosis.
- 2e3012060f8ba9a5f450f08fd784f3b380ef2559 — committed failing TERM/INT cases.
- 4272c9ad42a9449c4d081f402d9af859f431a979 — custody repair and owner documentation.

Work proceeded under the caller’s standing fix/commit authority without an approval pause. At the first settlement, additional execution budget was needed for the five census cases and 60/30 final stress at this repair; the independent TERM exit=2 observation remained unattributed. Native Windows RemoteScriptContractTests C849 rerun was still owed and explicitly outside this task. That historical settlement withheld acceptance pending the incomplete checks.

The initial next stage was Debug; the caller reopened it with the continuation recorded below. The final next-stage block closes this report.

Final master attribution check: the untouched base has 48 master merge-tree conflicts; the repair has 50. The two additional conflict records are not classified here, so not all 50 are attributed as inherited. The intended rc comparison passed; master landing is not authorized by these results.

## Caller-authorized continuation: census gate

The caller reopened this task with about 60 additional execution minutes, ordered census first, then 60 sequential/30 loaded repetitions (stop at the first failure and diagnose), then at most 20 minutes on the independent TERM exit=2 observation. The preceding incomplete gate is the historical first settlement, not the final continuation outcome.

At clean pushed source f453676a71481d7c0addb00b87cf9eec16dde599, a fresh bin-fc629ae4-cont/ build passed with 0 errors / 1403 warnings in 462.91 seconds. TEMP=/tmp, UseAppHost=false; one MSBuild node conservatively stays within the broker's grant of six. Real lease 762bea17-345a-4c7a-a0ff-36300884d44c was admitted after 90 seconds. The continuing foreground driver pauses between completed selections for this evidence commit, then retains the same lease for sequential repetitions. No source is edited during an active build/test.

C1514LandingSplitBudgetTests passed 5/5, failed 0, skipped 0 (TUnit duration 117.879 seconds): C1404_Group10MeasuredBudget, C1577_Group71MeasuredBudget, C1584_E2EExpandedBudget, C1410_MeasuredCostGroups and G2865_Group28Group65MeasuredBudget. This completes the previously unrun census gate; the old exit-4 admission remains recorded above. Full build log, TRX, output, source SHA and host census: /tmp/fc629ae4-cont/build.log and /tmp/fc629ae4-cont/census-001/. Fresh per-invocation tallies will be /tmp/fc629ae4-cont/runs.jsonl; the driver explicitly stops on any failure, missing TRX or unexpected selection count. Sequential and loaded repetitions have not started at this evidence slice.

## Continuation: final exact-method repetition gate

The previously owed 60 sequential / 30 loaded repetitions are now COMPLETE: 60/60 passed alone and 30/30 passed with four owned CPU burners; 0 failures, 0 skips, exactly one test executed per fresh invocation. No stop-on-first-failure condition occurred. All 90 invocations recorded source 4bc23d89ec452d73bc83147e00f9b59f402da92d, which adds only the census report to the clean fresh build source f453676a71481d7c0addb00b87cf9eec16dde599; production/test bytes remain repair 4272c9ad42a9449c4d081f402d9af859f431a979. No source was edited during the campaign. This is manual Debug evidence, not a checkpoint reviewedSourceClean claim.

Sequential test durations: p50=40.994s, p95=56.339s, min=27.659s, max=57.629s. Sequential wall time including 60 separate dotnet/TUnit startups was 2936.887s (48m57s). The four loaded-phase burners were direct children owned by the continuing Node driver: PIDs 17789, 17792, 17793 and 17795, started 2026-10-31T12:17:45.318Z. All four were stopped through owned child handles and joined before driver exit 0 and lease release. Complete lifetimes, loaded metrics and per-run counts are retained in /tmp/fc629ae4-cont/load.jsonl, summary.json and runs.jsonl. Every invocation retains full output, fresh TRX, source SHA and host census under post-fix-alone-NNN / post-fix-loaded-NNN.

The measured 90-invocation campaign exceeded the caller's approximate additional hour; the exact required sequential selections were completed without shortening waits, weakening assertions or parallelizing the sequential phase. The independent TERM exit=2 observation is the remaining time-boxed investigation; it is not claimed fixed by these 90 passes.

## Final continuation outcome

DONE for the commissioned Linux Debug scope. The real signal-during-EXIT custody defect is repaired red-first; all required Linux selections are complete. The separate historical TERM exit=2 observation remains unreproduced and unclassified, explicitly recorded rather than claimed fixed. Native Windows C849 remains owed and was not run.

| Selection | Passed | Failed | Skipped | Test p50 / p95 / maximum (s) |
|---|---:|---:|---:|---|
| Base exact method, sequential | 40 | 0 | 0 | 41.812 / 61.489 / 72.870 |
| Base exact method, loaded | 20 | 0 | 0 | 54.176 / 71.579 / 72.409 |
| New deterministic cases, committed red | 0 | 2 | 0 | Assertion red: volume removal only |
| New deterministic cases, repaired | 2 | 0 | 0 | /tmp/fc629ae4/deterministic-green |
| Whole C84x selection, repaired | 32 | 0 | 0 | Includes original C849 and m1/m5/m6/m7 |
| RcRosterRepairTests | 3 | 0 | 0 | 33.095s selection duration |
| C1404/C1577/C1584/C1410/G2865 census | 5 | 0 | 0 | 117.879s selection duration |
| Repaired exact method, sequential | 60 | 0 | 0 | 40.994 / 56.339 / 57.629 |
| Repaired exact method, loaded | 30 | 0 | 0 | 42.359 / 64.987 / 74.611 |

The 90 post-fix invocation wall times total 4464.154s (74m24s), including separate dotnet/TUnit startup for every invocation: 48m57s sequential and 25m27s loaded. The four stress burners were joined at 2026-10-31T12:43:13.584Z, and lease 762bea17-345a-4c7a-a0ff-36300884d44c released after 5089s held (build, census, evidence idle gate and stress). Host-wide inactivity was not reserved; retained per-run censuses show competing host work. The continuation therefore exceeded the approximate additional hour to finish the explicit 90-run acceptance obligation; no evidence rows were silently dropped.

Independent TERM time box: at source 849c38ab74c8855f7c32ffbf35f592a5f24bf23b, 40/40 focused shell probes passed alone and 40/40 passed with four owned CPU burners. These are additional diagnostic shell probes, NOT additional TUnit method repetitions. The scratch copy preserves the original foreign-container witness followed by the original TERM witness, exact 143 exit and all foreign/CID/lease/positive volume-removal facts. It adds only sender/handler/EXIT/result phase logging and fixed-file snapshots; it does not override release decisions or accept exit 2. Every trace observed successful TERM send, handler entry and EXIT 143. No assertion or receipt was relaxed. This does not classify the historical absent failure as either a harness race or a product defect.

The TERM phase used real lease d57991c3-857c-4cc3-840f-19843998522d, waited 30s and held 337s; approximately 6m7s including admission, within the 20-minute cap. Diagnostic full-probe wall p50/p95/max: alone 4.568/7.284/13.183s; loaded 3.371/5.883/7.526s. These include the foreign-container setup and are not measurements of the new deterministic finalizer guard. Its original approximately 2x loaded-preview-p95 guard remains unchanged. The focused load's direct children 9845/9846/9847/9849 were stopped by their handles and joined at 2026-10-31T12:50:04.760Z. Evidence, full per-probe output, diagnostic script and phase snapshots: /tmp/fc629ae4-term/; source-sha.txt, runs.jsonl, summary.json and gate.log record provenance and counts. Instrumentation can alter scheduling, so these samples cannot prove the historical TERM symptom fixed; the 90 unmodified exact-method invocations are the final stress evidence.

An ownership census also found four orphaned fake-model Bash processes carrying this exact task ID and C1192_MUTATION=m1, with scripts/cwds inside deleted private model roots. They are deliberately broken positive-control residue, not evidence of an ordinary product cleanup failure or of the TERM exit mismatch. PID/start-time/task identity was revalidated before each signal; all four and their eight owned descendants exited after TERM, with zero surviving selected processes and no SIGKILL needed. Receipt: /tmp/fc629ae4-term/owned-cleanup-receipt.json. All 36 fresh bin-fc629ae4-cont output directories were then removed by their exact inventoried paths after every build/test process had exited; /tmp/fc629ae4-cont/cleanup-inventory.json records zero remaining paths. No production/foreign process was stopped. All foreground runs were awaited.

Files changed: scripts/c590-remote.sh (finalize before signal exit), scripts/fixtures/c1192-workspace-contract.sh (bounded deterministic EXIT-order hook), tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs (two exact red-first cases), docs/docker-stack.md and docs/testing-and-build.md (owner behavior/evidence), plus this individual report. No production changes beyond the three-line finalizer repair. Existing custody assertions and m1/m5/m6/m7 detection remain exact.

The real graph -Check approval above remains valid for the unchanged implementation; no -Update ran. Refreshed origin rc/2026-10-27 is fc064a68247b0186918dfb1f04465e6be0475389; merge-tree against the final stress-evidence tip 849c38ab74c8855f7c32ffbf35f592a5f24bf23b passes, tree e47958f66792b1295a529812e819601806d8c915. Refreshed master is 2051d327123a5e98524b4bd1ecf31c6e516bdb4e; its comparison remains conflicted and is not the landing target. No target merge/rebase/reset/force push, tags, publish, native run, desktop file access, C:\Antiphon access or live qualification d47848cc operation occurred.

Continuation evidence commits (pushed after each completed step): 4bc23d89ec452d73bc83147e00f9b59f402da92d (census), 849c38ab74c8855f7c32ffbf35f592a5f24bf23b (90-run stress), followed by this final TERM/cleanup report. Previous evidence commits retained: 9d3398b8dd3184fa4437694c1e53547a1cac455b and f453676a71481d7c0addb00b87cf9eec16dde599. Red and repair SHAs are recorded above. Final head and evidence-diff result are supplied with the completion receipt.

To rerun on Linux: acquire a host build slot, build tests/Antiphon.Tests with UseAppHost=false, TEMP=/tmp and a fresh alternate OutputPath; then run dotnet run --project tests/Antiphon.Tests --no-build with the same properties and exact treenode filter /*/*/RemoteScriptContractTests/C849_Refused_launch_keeps_a_foreign_container_and_signals_release_proven_cids under the slot. Retain fresh TRX per invocation and stop on the first failure. The deterministic filter is /*/*/RemoteScriptContractTests/C849_Signal_during_exit_finishes_custody_cleanup. Native Windows RemoteScriptContractTests C849 remains a separate owed rerun, including the previously repaired driver-ready path; no Linux result substitutes for it.

--- next stage ---
next: merge to rc
handoff: Land the repaired branch onto rc/2026-10-27. Carry the unreproduced/unclassified historical TERM exit=2 observation as an open limitation; it is not claimed fixed. Native Windows RemoteScriptContractTests C849 remains owed and was not authorized here.
artifact: /work/worktrees/task-fc629ae4/.antiphon/task-fc629ae4.md
