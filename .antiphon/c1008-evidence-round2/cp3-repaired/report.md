--- checkpoint report ---
run: 20261003-173710-56b2   manifest: /work/worktrees/task-1b2e83e3/.antiphon/checkpoints/20261003-173710-56b2/manifest.resolved.yaml
commit: bdeb1a21333de0cdd4b6cbe04640250d1a76bf45  branch: feat/card-task-1b2e83e3  worktree: /work/worktrees/task-1b2e83e3  host: Debian GNU/Linux 12 (bookworm) cores=24
source: bdeb1a21333de0cdd4b6cbe04640250d1a76bf45 state=clean buildSource=verified
CHECKPOINT CP-3 commit=bdeb1a21333de0cdd4b6cbe04640250d1a76bf45 build=ok filter=/*/*/RemoteScriptContractTests*/(C849_*)|(C912_*)|(C973_*)|(C944_*)|(C951_*)|(C976_*)|(C946_*)|(C957_*) executed=54 passed=53 failed=1 skipped=0 trx=/work/worktrees/task-1b2e83e3/.antiphon/checkpoints/20261003-173710-56b2/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=bdeb1a21333de0cdd4b6cbe04640250d1a76bf45 sourceState=clean buildSource=verified
PHASES CP-3 slotWait=0s build=205.9863431s startup=5.0905008s testsWall=161.8035164s teardown=0.8398909s hostWall=167.744493s
FAILED Antiphon.Tests.Scripts.RemoteScriptContractTests.C849_Saved_donor_archive_is_checked_and_imported_without_a_container (CP-3) ShouldAssertException: output     should contain (case insensitive comparison) "deploy-temp=seed-result=false:PastSeedGate"     but was actually "success=0 seed-result=true: marker-written saved-identity payload-imported recovery-retained recover..." -> rows/CP-3/failures.md
SLOW CLASS Antiphon.Tests.Scripts.RemoteScriptContractTests 161s tests=54 (CP-3)
unlisted: none (the tool ran no other build or test command)
wall: 6m16s  sequential-equivalent: 2m48s  builds: 5  max-concurrent-builds: 1  rows: 0 green 1 red 0 skipped
outputs: kept bin-c1008-red/, bin-c1008-scripts/, bin-c1008-cache/, bin-c1008-docs/, bin-c1008-real/ -> dotnet run --project tools/Antiphon.Checkpoints -- clean --run 20261003-173710-56b2
evidence: /work/worktrees/task-1b2e83e3/.antiphon/checkpoints/20261003-173710-56b2/report.md
verdict: RED exit=1
