--- checkpoint report ---
run: 20261003-182708-9de2   manifest: /work/worktrees/task-52bffc69/.antiphon/checkpoints/20261003-182708-9de2/manifest.resolved.yaml
commit: b6e671ae7861d17530a829862d54b60c8b0e7378  branch: feat/card-task-52bffc69  worktree: /work/worktrees/task-52bffc69  host: Debian GNU/Linux 12 (bookworm) cores=24
source: b6e671ae7861d17530a829862d54b60c8b0e7378 state=clean buildSource=unknown
CHECKPOINT CP-2 commit=b6e671ae7861d17530a829862d54b60c8b0e7378 build=n/a filter=/*/*/(RemoteScriptContractTests*)|(RollingVolumeRecycleScriptTests*)/C1008_* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=total slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
unlisted: none (the tool ran no other build or test command)
wall: 40m00s  sequential-equivalent: 0m00s  builds: 5  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
outputs: kept bin-c1008-red/, bin-c1008-scripts/, bin-c1008-cache/, bin-c1008-docs/, bin-c1008-real/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261003-182708-9de2
evidence: /work/worktrees/task-52bffc69/.antiphon/checkpoints/20261003-182708-9de2/report.md
verdict: RED exit=5
