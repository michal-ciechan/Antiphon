--- checkpoint report ---
run: 20261003-192113-534c   manifest: /work/worktrees/task-52bffc69/.antiphon/checkpoints/20261003-192113-534c/manifest.resolved.yaml
commit: f47844d216354b24e56cbecd72d1f1a9c1beeac9  branch: feat/card-task-52bffc69  worktree: /work/worktrees/task-52bffc69  host: Debian GNU/Linux 12 (bookworm) cores=24
source: f47844d216354b24e56cbecd72d1f1a9c1beeac9 state=clean buildSource=unknown
CHECKPOINT CP-2 commit=f47844d216354b24e56cbecd72d1f1a9c1beeac9 build=n/a filter=/*/*/(RemoteScriptContractTests*)|(RollingVolumeRecycleScriptTests*)/C1008_* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=total slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
unlisted: none (the tool ran no other build or test command)
wall: 40m00s  sequential-equivalent: 0m00s  builds: 5  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
outputs: kept bin-c1008-red/, bin-c1008-scripts/, bin-c1008-cache/, bin-c1008-docs/, bin-c1008-real/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261003-192113-534c
evidence: /work/worktrees/task-52bffc69/.antiphon/checkpoints/20261003-192113-534c/report.md
verdict: RED exit=5
