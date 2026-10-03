--- checkpoint report ---
run: 20261003-200344-4a3f   manifest: /work/worktrees/task-52bffc69/.antiphon/checkpoints/20261003-200344-4a3f/manifest.resolved.yaml
commit: 3e58423b012ee64ab20aa8242fc2c1cd7d486dea  branch: feat/card-task-52bffc69  worktree: /work/worktrees/task-52bffc69  host: Debian GNU/Linux 12 (bookworm) cores=24
source: 3e58423b012ee64ab20aa8242fc2c1cd7d486dea state=clean buildSource=verified
CHECKPOINT CP-3 commit=3e58423b012ee64ab20aa8242fc2c1cd7d486dea build=ok filter=/*/*/RemoteScriptContractTests*/(C849_*)|(C912_*)|(C973_*)|(C944_*)|(C951_*)|(C976_*)|(C946_*)|(C957_*) executed=54 passed=54 failed=0 skipped=0 trx=/work/worktrees/task-52bffc69/.antiphon/checkpoints/20261003-200344-4a3f/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=3e58423b012ee64ab20aa8242fc2c1cd7d486dea sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=157.1578807s startup=5.1183813s testsWall=136.4708851s teardown=0.7140335s hostWall=142.3033232s
SLOW CLASS Antiphon.Tests.Scripts.RemoteScriptContractTests 136s tests=54 (CP-3)
CHECKPOINT CP-4 commit=3e58423b012ee64ab20aa8242fc2c1cd7d486dea build=ok filter=/*/*/(DockerStackContractTests*)|(DindRunnerContractTests*)|(DockerStackSmokeCommandTests*)|(DockerStackDocumentationTests*)|(CheckpointImportTests*)|(CheckpointManifestTests*)/* executed=207 passed=207 failed=0 skipped=0 trx=/work/worktrees/task-52bffc69/.antiphon/checkpoints/20261003-200344-4a3f/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=3e58423b012ee64ab20aa8242fc2c1cd7d486dea sourceState=clean buildSource=verified
PHASES CP-4 slotWait=0s build=146.3132501s startup=5.3469102s testsWall=33.6080235s teardown=0.848539s hostWall=39.8034746s
unlisted: none (the tool ran no other build or test command)
wall: 8m07s  sequential-equivalent: 3m03s  builds: 5  max-concurrent-builds: 1  rows: 2 green 0 red 0 skipped
outputs: kept bin-c1008-red/, bin-c1008-scripts/, bin-c1008-cache/, bin-c1008-docs/, bin-c1008-real/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261003-200344-4a3f
evidence: /work/worktrees/task-52bffc69/.antiphon/checkpoints/20261003-200344-4a3f/report.md
verdict: GREEN exit=0
