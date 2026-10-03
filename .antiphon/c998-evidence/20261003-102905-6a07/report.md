--- checkpoint report ---
run: 20261003-102905-6a07   manifest: /work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-102905-6a07/manifest.resolved.yaml
commit: 03618bb0336fab72fb21ed255f9eacee157f94da  branch: feat/card-task-3eceba1b  worktree: /work/worktrees/task-3eceba1b  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 03618bb0336fab72fb21ed255f9eacee157f94da state=clean buildSource=verified
CHECKPOINT CP-1 commit=03618bb0336fab72fb21ed255f9eacee157f94da build=ok filter=/*/*/CheckpointTempUsageTests*/(namespace_census_matches_compiled_checkpoint_cases)|(full_suite_checkpoint_floor_uses_the_current_census) executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-102905-6a07/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=03618bb0336fab72fb21ed255f9eacee157f94da sourceState=clean buildSource=verified
PHASES CP-1 slotWait=15s build=411.1241153s startup=unavailables testsWall=unavailables teardown=unavailables hostWall=unavailables reason=trx_timestamps_missing
unlisted: none (the tool ran no other build or test command)
wall: 7m12s  sequential-equivalent: 0m20s  builds: 3  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
outputs: kept bin-c998-tests-red/, bin-c998-final/, bin-c998-unit/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261003-102905-6a07
evidence: /work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-102905-6a07/report.md
verdict: RED exit=3
