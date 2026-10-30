S1 is implemented and pushed; Linux CP-1 is green (10/10). This is an incremental slice, not permission to land or activate CARD-1619.

Source base: e4c9547e10e3da59fd20684b49c029cdc6063fa8. Product/test tip verified by CP-1: f73f2b398faf1ab486800d6e23316a1a553c5ad9. The final commit adds only this report; chain S2 from that final pushed tip. No rebase, merge, target push, tag, deployment, qualification-run access or Windows/desktop execution occurred. Standing caller authority was sufficient; no approval was needed.

S1 contents:
- src/Antiphon.Agents.Pty/PortaExitSignalReader.cs validates the loaded Porta.Pty Linux type/direct base, private declared int field, assembly 1.0.7.0 and informational version 1.0.7+54684ba55148ed6bcd0c827ad7e8841a3289a466. It caches the validated accessor per reader, reads only after completion, and returns null with one bounded diagnostic on contract/access failure. Windows returns zero without reflection; other Unix platforms stay unknown. Porta remains the sole reaper; no dependency/native-asset changes.
- PtyExitObservation.cs adds immutable actual code/nullable signal evidence. PtySession.cs caches it before publication and replays WaitForExit(0); PtyAgentRunner.cs subscribes then reads the session cache and deduplicates handling. Completion exposes structured evidence; Exited retains the actual integer, including zero for a Linux signal.
- ModernConPtyConnection.cs changes only the observation/completion seam and cache. Native spawn, kill, job and disposal paths remain unchanged. HostSession.cs consumes structured actual code and uses null on observer failure instead of inventing native -1. Signal transport is S2 work.
- Card1619UnixExitTests.cs supplies the exact 10-result CP-1 roster: real external SIGKILL/SIGTERM with readiness and PID/start identity; normal 0/23; separate actual-zero/signal; exit before wrapper subscription, before runner subscription, and callback/replay overlap; production-reader loaded contract; three unsupported contracts with repeated unknown reads and one diagnostic.
- Card1619WindowsExitTests.cs adds two owed production-factory modern/inbox witnesses for normal 0/23 and existing kill results, with signal zero. The inbox expected kill integer is -1, consistent with .NET's existing TerminateProcess(handle, -1), while modern job termination uses 1 (src/Antiphon.Agents.Pty/IPtyCustodyNative.cs). Source reference: https://github.com/dotnet/runtime/blob/v9.0.10/src/libraries/System.Diagnostics.Process/src/System/Diagnostics/Process.Windows.cs#L90 . These tests were compiled, not executed on Windows.
- The exact reviewed plan was copied first as its own docs commit at docs/superpowers/plans/2026-10-30-card-1619-linux-pty-exit-signal-plan.md. Both specified reviews, investigation and public card were read. The exit consumer census is retained in .antiphon/card1619-s1-exit-consumer-census.txt; no additional S1 consumer was found.

All five implementation commits were individually pushed:
691cc4c7e40c2641cbccc65f22878a8adce01abd — reviewed plan copy.
675507d6fe72155bc42edf08dcd5a7b642b43bbe — Linux red witnesses and compile-compatible unknown seams.
5bf77d78136f79189188708ab75c43531cb0cb56 — owed Windows witnesses.
caa5209a25b86cd1eeb73afcd5e5fdcf8ddb7c4b — deterministic callback/replay overlap witness.
f73f2b398faf1ab486800d6e23316a1a553c5ad9 — implementation and stronger repeated-read diagnostic guard.

Verification:
- CP-1 red at caa5209a25b86cd1eeb73afcd5e5fdcf8ddb7c4b: 10 executed, 3 passed, 7 failed, 0 skipped. The three unsupported-contract controls passed. Seven assertions failed on missing signal/replay/accessor evidence, not compilation, fixture errors or timeouts. Native signal witnesses actually observed owned children killed by the external test actor.
- CP-1 green at f73f2b398faf1ab486800d6e23316a1a553c5ad9: 10 executed, 10 passed, 0 failed/skipped. Terminal wait exit 0; separate validate --evidence report.json --rows CP-1 --expected-source-sha f73f2b398faf1ab486800d6e23316a1a553c5ad9 returned CHECKPOINT SOURCE VALID. Both runs have clean source and verified build provenance. Every executor was joined; no run was left at exit 75.
- Both imported and pinned 15-row manifests, TRX, logs and receipts remain ignored under /work/worktrees/task-4811f7cc/.antiphon/card1619/. Only CP-1 ran. The manifest pins UseAppHost=false and row timeout 4 minutes, with TEMP=/tmp and valid phase metadata. No test selection or assertion was removed/relaxed.
- Preparatory builds outside the product roster were the explicitly required checkpoint bootstrap and real land-graph evaluator. Both took host slots and alternate outputs with UseAppHost=false. Bootstrap: wait 481 s, build/hold 30 s, five existing warnings. Graph tool: wait 61 s, hold 23 s, zero warnings/errors. Graph -Check: wait 196 s, hold 57 s; two real evaluation passes/canary approved, digest 7BDABBA075B620C10A47B50CE1277A932DDD801E9314A932C4EAC03885441A99, count 68. No definition, solution, props/targets, lock file or migration changed. No -Update was run.
- Evidence-diff guard over base..product tip: 5 commits, 0 violations. Diff whitespace check passed. Declaration census found only the two intended new classes; CP-1 produced every named Linux case, with no suffix collisions.
- RC merge-tree at e4c9547e10e3da59fd20684b49c029cdc6063fa8 was clean. Fresh origin/master remained 698bf1b73e8abf449d5569ea74ac8c82e80f92a1. Its 12 conflict paths were reproduced at the pinned base and product tip; none is introduced by S1. No merge/rebase was performed. The complete fix remains RC-first with a separate master port.

Preserved non-credit attempts and lint limitations:
1. The plan's bare --total-timeout 12 / --hard-wall-timeout 22 values parse as seconds, rounded to one minute. Run 20261030-124727-b37a ended with no tests at deadline, executor exit 5. Its terminal wait refused the inherited receipt shape with `receipt-shape: outputPaths required for outputNames` (exit 2). That receipt is retained at .antiphon/card1619/s1-red/runs/20261030-124727-b37a/. The intended minute budgets were then spelled 12m/22m; the four-minute CP-1 row budget was unchanged. Use explicit duration suffixes in every future command. A relative --run wait also observed no real run; all real terminal waits used absolute run directories.
2. One attempted green start used a mistyped expected SHA. The source guard refused it with exit 2 before creating a run or executing anything. The credited green run uses the correct full SHA above.
3. OS-witness declaration is .antiphon/card1619/os-witnesses.json (Linux receipt recorded, Windows pending). The lint returned 2 because its bounded lexer does not support interpolated C# strings. No lint pass is claimed; native runtime identity still needs Review.
4. Static plan coverage returned 2, INPUT_INVALID / unresolved selected class at tests/Shared/TestClassificationMetadata.cs. Complete unedited output follows. This did not run tests or prove coverage. These tool limitations remain explicit; no product changes were made solely to satisfy advisory lint.

PLAN-COVERAGE schema=1 mode=static plan="docs/superpowers/plans/2026-10-30-card-1619-linux-pty-exit-signal-plan.md" planSha256=388308ee5c129349d2dfaf003cfdc22ad6db227f05102716f3b8620f05cfcf93 inputsSha256= files=0
COVERAGE code=INPUT_INVALID planLine=0 planColumn=1 id= test="" name="" testPath="tests/Shared/TestClassificationMetadata.cs" testLine=0 detail="unresolved selected class"
PLAN-COVERAGE-END obligations=0 matched=0 missing=0 unmapped=0 pcIssues=0 pcAdvisories=0 result=invalid reachability=unproven

Essential unedited checkpoint lines:
CHECKPOINT CP-1 commit=caa5209a25b86cd1eeb73afcd5e5fdcf8ddb7c4b build=ok filter=/*/*/Card1619UnixExitTests/* executed=10 passed=3 failed=7 skipped=0 trx=/work/worktrees/task-4811f7cc/.antiphon/card1619/s1-red-minutes/runs/20261030-124937-657e/rows/CP-1/run.trx slot=granted waited=210s dirty=0 source=caa5209a25b86cd1eeb73afcd5e5fdcf8ddb7c4b sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=210s executed=3.652s wall=213.779s
PHASES CP-1 slotWait=285s build=122.3564183s startup=2.0407713s testsWall=0.5727459s teardown=0.3865719s hostWall=3.0000878s
BUDGET wall=443.947s execution=158.906s admissionOnly=285.041s
CHECKPOINT CP-1 commit=f73f2b398faf1ab486800d6e23316a1a553c5ad9 build=ok filter=/*/*/Card1619UnixExitTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-4811f7cc/.antiphon/card1619/s1-green/runs/20261030-125910-0dbe/rows/CP-1/run.trx slot=granted waited=45s dirty=0 source=f73f2b398faf1ab486800d6e23316a1a553c5ad9 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=45s executed=4.891s wall=49.961s
PHASES CP-1 slotWait=225s build=72.6105485s startup=3.2190954s testsWall=0.4222205s teardown=0.5336931s hostWall=4.1750071s
BUDGET wall=370.825s execution=145.799s admissionOnly=225.026s

Timing: Debian 12/Linux, 24 cores, contended shared host, maxcpucount 6. Red wall 443.947 s versus execution 158.906 s and admission-only 285.041 s; green wall 370.825 s versus execution 145.799 s and admission-only 225.026 s. TRX nearest-rank per-test p95/max: red 0.2377003 s, green 0.1028784 s, each 10 results. This is one passing run, not three comparable passing group samples; keep CARD-1619's provisional ceilings until Review supplies the remaining samples. Wrapper startup/tests/teardown phases are above; finer native-spawn/readiness/body/drain spans are unavailable/unknown and remainder is unattributed, never zero. No DB/container/web-host fixture was used. No selection exceeded 30 minutes. All task-created bin-card1619-* directories were removed by exact literal paths after their owners joined; raw evidence stays ignored.

Owed rows: CP-2 (7 native Windows results) at the final combined S4 source. S2: CP-3=11 and CP-4=6. S3a: CP-5=8 and CP-8=one typecheck. S3b: CP-13=23, CP-6=6, CP-7=3, CP-14=3, CP-15=7. S4: CP-9=11, CP-10=8, CP-11=8. CP-12=2 remains separately owed on its own cgroup-v1 native fixture. No Windows, cgroup-v1, desktop, live qualification or mutation PC execution was attempted. After S1, 103 required results plus the typecheck remain to later slices/native Review. A skipped or unrun row has no green credit.

Exact native commands below must run on the required lanes at the final combined source, after rebuilding/importing the tool/manifest there and retaining the plan's pins/timing metadata. Tool bootstrap must hold a build slot:

```powershell
$env:TEMP = '/tmp' # Linux worker shell; use the native Windows TEMP on Windows
pwsh -NoProfile -File scripts/build-slot.ps1 -Label card1619-checkpoint-tool -SlotWaitMinutes 25 -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-card1619-tools/ --property:UseAppHost=false
$tool = 'tools/Antiphon.Checkpoints/bin-card1619-tools/Antiphon.Checkpoints.dll'
$plan = 'docs/superpowers/plans/2026-10-30-card-1619-linux-pty-exit-signal-plan.md'
$source = (git rev-parse HEAD).Trim()
$manifest = '.antiphon/card1619-final-native.yaml'
dotnet $tool import --plan $plan --out $manifest
# Apply the reviewed plan's timeout and timing metadata pins before run.
# Native Windows, final source; do not run this on Linux:
dotnet $tool run $manifest --rows CP-2 --expected-source-sha $source --serial --total-timeout 16m --hard-wall-timeout 26m --max-wait 45s --heartbeat 15s
# Owned cgroup-v1 fixture, a SEPARATE run from CP-9..11:
dotnet $tool run $manifest --rows CP-12 --expected-source-sha $source --serial --total-timeout 14m --hard-wall-timeout 24m --max-wait 45s --heartbeat 15s
# After any exit 75, use that invocation's exact ABSOLUTE printed run directory:
dotnet $tool wait --run $runDirectory --expected-source-sha $source --max-wait 45s --heartbeat 15s
```

For S2 use only CP-3,CP-4 with total-timeout 24m and hard-wall-timeout 34m after committing that slice. Keep all generation/custody/stop authority unchanged. Do not land S1..S3b separately; full fix Review follows S4 and all required native results. Exclude the branch-only plan/report delta from the eventual RC product port.

--- next stage ---
next: code
handoff: Implement S2 from this report commit's pushed tip via -StartRef; retain the reviewed plan, carry nullable signal across host/runner/restart boundaries, and run CP-3/CP-4. S1 must not land independently. Static-lint limitations and final native gates remain open.
artifact: /work/worktrees/task-4811f7cc/docs/superpowers/plans/2026-10-30-card-1619-linux-pty-exit-signal-plan.md
