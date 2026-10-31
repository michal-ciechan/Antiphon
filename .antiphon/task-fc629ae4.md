# Debug fc629ae4: C849 signal intermittence

Base: fc064a68247b0186918dfb1f04465e6be0475389. Branch: feat/card-task-fc629ae4.
Required: 40 baseline sequential / 20 loaded exact-method runs, deterministic ordering witness and repair, 60 sequential / 30 loaded green runs, C84x class, roster/census and real graph Check. Native Windows C849 remains owed; no native or live qualification operations authorized here.
Evidence root: /tmp/fc629ae4. TEMP=/tmp. All build/test drivers pass build-slot.ps1; private bin-fc629ae4 output. Task-owned runs are sequential unless explicitly collecting loaded samples; other host tasks are observed, not stopped.
Initial hypothesis: signal delivery can overlap cleanup; inspect readiness and nested-handler scheduling before attributing either observation. No assertions weakened and no product attribution yet.

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

## Final verification and remaining gate

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

Work proceeded under the caller’s standing fix/commit authority without an approval pause. Additional execution budget is needed for the five census cases and 60/30 final stress at this repair; the independent TERM exit=2 observation remains unattributed. Native Windows RemoteScriptContractTests C849 rerun is still owed and explicitly outside this task. Merge to rc remains pending these incomplete checks; do not treat this report as full acceptance.

--- next stage ---
next: debug
handoff: Complete the five census cases and 60 sequential/30 loaded exact-method runs at the repaired source, investigate any remaining TERM exit mismatch, then merge to rc. Native Windows C849 remains a separate owed rerun.
artifact: /work/worktrees/task-fc629ae4/.antiphon/task-fc629ae4.md
