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
