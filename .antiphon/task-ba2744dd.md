S2 is implemented and pushed; Linux CP-3 is green (11/11) and CP-4 is green (6/6), with validated clean-source/build receipts. This incremental slice must not land or activate CARD-1619 independently.

Source base: bfc97689355219c23eab78e8b386ba146768af3c (S1). Product/test tip verified by both green rows: 08547447ac4af74ed113b55fcb34de73780d7b09. Chain S3a from this report's final pushed commit, retaining the reviewed plan. The report-only commit changes no tested product source.

Scope clarification: the brief's title mentions attribution/diagnostic recording, but its explicit ONLY-S2 instruction and reviewed slice table assign those to S4. S2 is “carry it across process/restart boundaries.” No issuer-memory tracker or diagnostic writer was added. S4 still owes signal-first ordering, the production journal's 100 ms deadline, and the never-completing-writer witnesses. Stop, custody, generation, report settlement and integer compatibility policies were not changed. Standing caller authority was sufficient; no approval was needed.

S2 contents:
- src/Antiphon.PtyHost.Protocol/{PtyHostMessages,PtyHostManifest}.cs append nullable signal evidence. Protocol/schema remain version 1. Old missing fields remain null; unknown positive numbers survive. No signal is inferred from an integer code.
- src/Antiphon.PtyHost/HostSession.cs reads S1's complete observation and copies the same signal to live EXIT, status, exited-attach replay, terminal manifest and host log. Observer failure remains unknown. Native Windows stopping/job behavior is untouched; its S1 producer still supplies zero signal with its existing integer code.
- src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs and src/Antiphon.SessionRunner/{ISessionChild,SessionRunnerRuntime}.cs carry optional signals through live events, status adoption, dead-host manifests, child callbacks, DTO/list and exited envelopes. Existing generation/sequence behavior remains. Other child backends default to unknown. PtyHostClient already forwards typed messages unchanged, so no client mapping edit was needed.
- tests/Antiphon.PtyHost.Tests/Card1619ExitWireTests.cs has the 7 V-7 controls; FrozenV1 contains the full v1 framing/message/manifest sources from the pinned S1 base, with namespace-only changes and provenance. The old reader and old read-modify-write tests use those frozen types rather than today's reader twice.
- Card1619LinuxHostExitTests.cs has 4 V-8 results through the production detached launcher and real Linux PTY: SIGKILL, SIGTERM, exited replay/status, and detached normal exit 23/signal 0. Card1619RunnerExitTests.cs has all 6 V-9 results: fresh-runner adoption, dead-host recovery, old-host unknown, real loopback GET/list plus the actual exit envelope, production phone-home event framing/List dispatcher recovery, and stale/current generation separation. Owned signal actors verify /proc PID/start ticks. Native classes use NativeLinuxOnly and the assembly-local process limiter; all owned children/hosts were joined.

Every meaningful commit was pushed separately:
75d05c0c2e89ee4ee92ffd1373ecfe51a9883ff3 — witness roster, frozen v1 sources and compile-compatible nullable contracts; mappings still missing.
a9fa490ca9618611d1fe5257ed4d27ddb910aa0b — correct new witness API signatures.
1df4ea0fa1410083648b7c8817674c8faa8df2f9 — exact native PID/start identity and same-origin host timestamp checks; runner timing spans.
81e0236add909a0d9178efe372ab1623666ac935 — deterministic filesystem release gate for the detached normal-exit witness.
08547447ac4af74ed113b55fcb34de73780d7b09 — production propagation repair.

Credited red/green:
- CP-3 red at 81e0236add909a0d9178efe372ab1623666ac935: 11 executed, 7 passed, 4 failed, 0 skipped. All four real host results failed specifically because ExitSignal was null instead of 9/15/9/0. The owned children actually reached readiness and the intended signal/normal completion.
- CP-4 red at 1df4ea0fa1410083648b7c8817674c8faa8df2f9: 6 executed, 1 passed, 5 failed, 0 skipped. Old-host unknown passed; all five native propagation assertions failed on missing signal evidence. This row was unchanged by the following host-only fixture correction.
- Both green at 08547447ac4af74ed113b55fcb34de73780d7b09: CP-3 11/11; CP-4 6/6; no failures/skips. Terminal wait returned 0. Separate validate --evidence report.json --rows CP-3,CP-4 --expected-source-sha returned CHECKPOINT SOURCE VALID. Clean source and verified build provenance apply to both rows. All executors reached terminal waits; none was left at exit 75.
- Bootstrap/import and all build/test drivers used granted host slots, alternate outputs and UseAppHost=false, with TEMP=/tmp. Import accepted all 15 plan rows. Generated imported and pinned manifests, raw logs, TRX and receipts remain ignored under /work/worktrees/task-ba2744dd/.antiphon/card1619-s2-*. The plan's exact filters/minima were retained, row budgets CP-3/4=8 minutes, slot wait=25 minutes (brief permits 10–25), total=24m/hard-wall=34m. Duration suffixes are explicit.
- Required real land-graph -Check passed, including real evaluation passes and canary approval; digest 7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99, count 68. Tool bootstrap: wait 0 s/hold 10 s, zero warnings/errors. Check: wait 0 s/hold 37 s. Checkpoint bootstrap: wait 15 s/hold 11 s, five existing warnings. These were the explicitly required preparatory builds outside the two product rows.
- No definition file, solution, props/targets, package/lock file or migration changed. No -Update or database operation was run. Evidence-diff over base..product tip: 5 commits, 0 violations. Whitespace check passed. Declaration census found precisely the three intended new test classes; final TRX contains every named CP-3/4 method with zero skips.
- Fresh origin/rc/2026-10-27 was 80b3a59c40cd18dea905829e428d35844cb89b8d; merge-tree was clean (tree 68121cd7820e5e9eb3a9894e5f0570a164fae770). Fresh origin/master stayed 698bf1b73e8abf449d5569ea74ac8c82e80f92a1. Its 12 conflicts are the identical paths at S1 base and S2 product tip; none was introduced by S2. No merge/rebase/target push/tag/publish/activation occurred. RC-first full-fix Review and a separate master port remain required.

Preserved non-credit attempts and limitations:
1. Initial run 20261030-131724-8b8f at 75d05c0c2: both builds failed, no tests. The new host test used the wrong OnOutput delegate arity, and the new phone-home test named Reply instead of Result. Corrected and pushed before running again. This is not red credit.
2. Run 20261030-132052-f836 at a9fa490ca: CP-3 7 passed/4 failed, CP-4 0 passed/6 failed. All native cases stopped at invalid cross-process .NET absolute-start-time equality; independently calculated boot-time offsets differed by milliseconds. These are fixture failures, not feature red. Replaced that invalid comparison with exact host-authored timestamp consistency and exact /proc start-tick identity checks before signaling; no time tolerance was widened.
3. Run 20261030-132547-9431 at 1df4ea0fa: CP-3 7 passed/4 failed, with three intended missing-signal failures and one normal-case reconnect EOF before the signal assertion. CP-4's 1 passed/5 failed is credited above. The normal fixture now releases an owned filesystem gate after detaching, avoiding an immediate reconnect during listener teardown. No production retry or timeout changed. CP-3 alone was rerun at the next committed source because only its fixture changed.
4. A mistyped abbreviated expected SHA was refused before any run/executor, exit 2. Credited runs use full normalized SHAs. One manual shell deletion form was refused by the environment; exact-literal PowerShell cleanup succeeded instead. No outside-task path was deleted.
5. Owner-read HTTP timeouts delayed finalization on the final CP-3 red and combined green runs. Both ultimately finalized normally (1 and 0), and green receipt validation succeeded. Raw owner-read diagnostics are retained.
6. OS-witness declaration is .antiphon/card1619-s2-os-witnesses.json, updated to recorded Linux receipts. The lint encountered its inherited unsupported interpolated-C# lexer case and returned invalid input (2); no lint pass is claimed. Runtime identity still needs ordinary Review. The S1 static-plan-coverage limitation (unresolved tests/Shared/TestClassificationMetadata.cs) also remains advisory/incomplete; it was not rerun solely to relabel it.

No existing test was modified. The planned signal/result and generation assertions were retained. New fixture API/identity/lifetime defects were corrected before feature red credit. No stop signal was gated on recording, no new success inference was added, and no timeout/retry/assertion tolerance was loosened. Server classification is intentionally still S3b work; this is why S2 cannot land alone. In particular, existing legacy integer compatibility/fallback behavior, including the runner's old manifest-null integer fallback, remains for the structured-writer/compatibility work in S3b; do not mistake it for a new native observation.

Timing: Debian 12/Linux, 24 cores, shared host, maxcpucount 6. Green wall 226.396 s, execution budget 226.395 s, admission-only 0.001 s; row execution CP-3 8.487 s, CP-4 11.907 s. Per-test nearest-rank p95/max: CP-3 1.7086134 s (11 results), CP-4 1.7428735 s (6 results). One passing sample per group: keep CARD-1619's provisional ceilings until independent Review supplies the remaining comparable samples. Wrapper startup/body/teardown spans and exact test roster/TRX durations are retained; runner-specific TestTiming spans cover native launch/readiness, detached wait/manifest and teardown. Host fine-grained pipe/filesystem/native subspans are unavailable/unknown; remainder is unattributed, not zero. No DB/container fixture was used, and only the HTTP route witness starts an in-process web app. Fresh hosts per case are necessary because spawn/exit/restart is the subject. No run exceeded 30 minutes. Task-created alternate outputs were removed by their exact literal paths after all owners joined; raw evidence remains ignored.

Essential unedited checkpoint/provenance lines from every historical attempt follow.

Source report: .antiphon/card1619-s2-red/20261030-131724-8b8f/report.md

CHECKPOINT CP-3 commit=75d05c0c2e89ee4ee92ffd1373ecfe51a9883ff3 build=failed filter=/*/*/(Card1619ExitWireTests*)|(Card1619LinuxHostExitTests*)/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=75d05c0c2e89ee4ee92ffd1373ecfe51a9883ff3 sourceState=clean buildSource=unknown
CHECKPOINT-TIMING CP-3 waited=0s executed=0.000s wall=0.000s
CHECKPOINT CP-4 commit=75d05c0c2e89ee4ee92ffd1373ecfe51a9883ff3 build=failed filter=/*/*/Card1619RunnerExitTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=0 source=75d05c0c2e89ee4ee92ffd1373ecfe51a9883ff3 sourceState=clean buildSource=unknown
CHECKPOINT-TIMING CP-4 waited=0s executed=0.000s wall=0.000s
BUDGET wall=152.776s execution=122.880s admissionOnly=29.896s

Source report: .antiphon/card1619-s2-red-assertions/20261030-132052-f836/report.md

CHECKPOINT CP-3 commit=a9fa490ca9618611d1fe5257ed4d27ddb910aa0b build=ok filter=/*/*/(Card1619ExitWireTests*)|(Card1619LinuxHostExitTests*)/* executed=11 passed=7 failed=4 skipped=0 trx=/work/worktrees/task-ba2744dd/.antiphon/card1619-s2-red-assertions/20261030-132052-f836/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=a9fa490ca9618611d1fe5257ed4d27ddb910aa0b sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=10.365s wall=10.445s
PHASES CP-3 slotWait=75s build=29.2964682s startup=2.0374649s testsWall=7.0460012s teardown=0.4081873s hostWall=9.4916546s
CHECKPOINT CP-4 commit=a9fa490ca9618611d1fe5257ed4d27ddb910aa0b build=ok filter=/*/*/Card1619RunnerExitTests/* executed=6 passed=0 failed=6 skipped=0 trx=/work/worktrees/task-ba2744dd/.antiphon/card1619-s2-red-assertions/20261030-132052-f836/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=a9fa490ca9618611d1fe5257ed4d27ddb910aa0b sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=10.939s wall=11.025s
PHASES CP-4 slotWait=0s build=72.6079191s startup=2.6862288s testsWall=6.8481365s teardown=0.5924115s hostWall=10.1267758s
BUDGET wall=246.591s execution=171.613s admissionOnly=74.979s

Source report: .antiphon/card1619-s2-red-native/20261030-132547-9431/report.md

CHECKPOINT CP-3 commit=1df4ea0fa1410083648b7c8817674c8faa8df2f9 build=ok filter=/*/*/(Card1619ExitWireTests*)|(Card1619LinuxHostExitTests*)/* executed=11 passed=7 failed=4 skipped=0 trx=/work/worktrees/task-ba2744dd/.antiphon/card1619-s2-red-native/20261030-132547-9431/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=1df4ea0fa1410083648b7c8817674c8faa8df2f9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=8.112s wall=8.189s
PHASES CP-3 slotWait=0s build=30.406369s startup=2.1115698s testsWall=4.4964174s teardown=0.5671172s hostWall=7.1751193s
CHECKPOINT CP-4 commit=1df4ea0fa1410083648b7c8817674c8faa8df2f9 build=ok filter=/*/*/Card1619RunnerExitTests/* executed=6 passed=1 failed=5 skipped=0 trx=/work/worktrees/task-ba2744dd/.antiphon/card1619-s2-red-native/20261030-132547-9431/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=1df4ea0fa1410083648b7c8817674c8faa8df2f9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=10.818s wall=10.862s
PHASES CP-4 slotWait=0s build=41.4776855s startup=2.2306694s testsWall=7.4372412s teardown=0.5684202s hostWall=10.2363306s
BUDGET wall=114.727s execution=114.726s admissionOnly=0.001s

Source report: .antiphon/card1619-s2-red-host-final/20261030-132910-60d1/report.md

CHECKPOINT CP-3 commit=81e0236add909a0d9178efe372ab1623666ac935 build=ok filter=/*/*/(Card1619ExitWireTests*)|(Card1619LinuxHostExitTests*)/* executed=11 passed=7 failed=4 skipped=0 trx=/work/worktrees/task-ba2744dd/.antiphon/card1619-s2-red-host-final/20261030-132910-60d1/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=81e0236add909a0d9178efe372ab1623666ac935 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=21.399s wall=21.473s
PHASES CP-3 slotWait=0s build=26.1023545s startup=2.1063259s testsWall=4.078268s teardown=0.5949366s hostWall=6.7795345s
BUDGET wall=93.588s execution=93.587s admissionOnly=0.001s

Source report: .antiphon/card1619-s2-green/20261030-133136-28e1/report.md

CHECKPOINT CP-3 commit=08547447ac4af74ed113b55fcb34de73780d7b09 build=ok filter=/*/*/(Card1619ExitWireTests*)|(Card1619LinuxHostExitTests*)/* executed=11 passed=11 failed=0 skipped=0 trx=/work/worktrees/task-ba2744dd/.antiphon/card1619-s2-green/20261030-133136-28e1/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=08547447ac4af74ed113b55fcb34de73780d7b09 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=8.487s wall=8.527s
PHASES CP-3 slotWait=0s build=65.4614441s startup=2.8038555s testsWall=4.1970767s teardown=0.6481376s hostWall=7.6490931s
CHECKPOINT CP-4 commit=08547447ac4af74ed113b55fcb34de73780d7b09 build=ok filter=/*/*/Card1619RunnerExitTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/task-ba2744dd/.antiphon/card1619-s2-green/20261030-133136-28e1/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=08547447ac4af74ed113b55fcb34de73780d7b09 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-4 waited=0s executed=11.907s wall=11.948s
PHASES CP-4 slotWait=0s build=72.5542354s startup=2.4113336s testsWall=7.6885754s teardown=0.970147s hostWall=11.0700562s
BUDGET wall=226.396s execution=226.395s admissionOnly=0.001s

Owed work: S3a CP-5 (8) and CP-8 (typecheck); S3b CP-13/6/7/14/15 (23/6/3/3/7); S4 CP-9/10/11 (11/8/8); final native Windows CP-2 (7); separate owned cgroup-v1 CP-12 (2). Thus 86 required results plus the typecheck remain after S1/S2. macOS and cgroup-v2 remain outside this card's qualification claim. No desktop/Windows files, C:\Antiphon, native qualification run d47848cc, or live stack was accessed. No Windows, cgroup-v1, mutation PC or deployment execution occurred.

Exact native commands at the FINAL combined S1/S2/S3a/S3b/S4 source, on the appropriate native lanes (rebuild/import there; apply the reviewed plan's timeout/phase pins before execution):

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1619-checkpoint-tool -SlotWaitMinutes 25 -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-card1619-tools/ --property:UseAppHost=false
$tool = 'tools/Antiphon.Checkpoints/bin-card1619-tools/Antiphon.Checkpoints.dll'
$plan = 'docs/superpowers/plans/2026-10-30-card-1619-linux-pty-exit-signal-plan.md'
$source = (git rev-parse HEAD).Trim()
$manifest = '.antiphon/card1619-final-native.yaml'
dotnet $tool import --plan $plan --out $manifest
# Apply the plan's pins and valid timing metadata. Preserve native Windows TEMP.
# Native Windows only, final combined source:
dotnet $tool run $manifest --rows CP-2 --expected-source-sha $source --serial --total-timeout 16m --hard-wall-timeout 26m --max-wait 45s --heartbeat 15s
# Owned cgroup-v1 fixture, its OWN run, never combined with CP-9..11:
$env:TEMP = '/tmp'
dotnet $tool run $manifest --rows CP-12 --expected-source-sha $source --serial --total-timeout 14m --hard-wall-timeout 24m --max-wait 45s --heartbeat 15s
# After each exit 75 use that invocation's exact ABSOLUTE printed run directory:
dotnet $tool wait --run $runDirectory --expected-source-sha $source --max-wait 45s --heartbeat 15s
```

For rerunning S2 only, re-bootstrap/import/pin at the selected committed source, then run CP-3,CP-4 with --serial --total-timeout 24m --hard-wall-timeout 34m --max-wait 45s --heartbeat 15s and wait to terminal. Green receipts above remain bound to 08547447ac4af74ed113b55fcb34de73780d7b09, not a later source. Do not repeat the product rows just for this report-only amendment.

--- next stage ---
next: code
handoff: Implement only S3a from this report commit's pushed tip via -StartRef, retaining the reviewed plan and S1/S2 source. Run CP-5/CP-8. Storage/transport and inert intent shape belong to S3a; all compatibility values and structured persisting/ownership consumer changes remain S3b. Issuer memory, diagnostic store, 100 ms deadline and never-completing-writer witnesses remain S4. S1..S3b must not land individually; full-fix ordinary/native Review and final native gates remain open.
artifact: /work/worktrees/task-ba2744dd/docs/superpowers/plans/2026-10-30-card-1619-linux-pty-exit-signal-plan.md
