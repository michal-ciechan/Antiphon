--- checkpoint report ---
run: 20261003-211642-32ac   manifest: /work/worktrees/task-5fcba512/.antiphon/checkpoints/20261003-211642-32ac/manifest.resolved.yaml
commit: 9c4077e76237baa913dc08e60ff8a92e4ae5c045  branch: feat/card-task-5fcba512  worktree: /work/worktrees/task-5fcba512  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 9c4077e76237baa913dc08e60ff8a92e4ae5c045 state=clean buildSource=verified
CHECKPOINT CP-2 commit=9c4077e76237baa913dc08e60ff8a92e4ae5c045 build=ok filter=/*/*/(RemoteScriptContractTests*)|(RollingVolumeRecycleScriptTests*)/C1008_* executed=19 passed=18 failed=1 skipped=0 trx=/work/worktrees/task-5fcba512/.antiphon/checkpoints/20261003-211642-32ac/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=9c4077e76237baa913dc08e60ff8a92e4ae5c045 sourceState=clean buildSource=verified
PHASES CP-2 slotWait=0s build=117.2387929s startup=3.9817709s testsWall=2020.7880962s teardown=1.2446587s hostWall=2026.0145311s
FAILED Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests.C1008_Refusal_receipts_do_not_leak_secrets (CP-2) ShouldAssertException: run.Output     should not contain (case insensitive comparison) "SENTINEL_C1008_HTTP_CREDENTIAL"     but was actually "SENTINEL_C1008_HTTP_CREDENTIAL Rolling deploy stopped: RecycleTaskCensusUnknown " -> rows/CP-2/failures.md
SLOW CLASS Antiphon.Tests.Scripts.RemoteScriptContractTests 1153s tests=11 (CP-2)
SLOW CLASS Antiphon.Tests.Scripts.RollingVolumeRecycleScriptTests 867s tests=8 (CP-2)
unlisted: none (the tool ran no other build or test command)
wall: 35m46s  sequential-equivalent: 33m47s  builds: 5  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
outputs: kept bin-c1008-red/, bin-c1008-scripts/, bin-c1008-cache/, bin-c1008-docs/, bin-c1008-real/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261003-211642-32ac
evidence: /work/worktrees/task-5fcba512/.antiphon/checkpoints/20261003-211642-32ac/report.md
verdict: RED exit=1
