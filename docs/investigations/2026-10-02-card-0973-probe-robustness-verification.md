# CARD-0973 probe robustness verification

Fixed the missing-executable regression in the offline rolling harness. Final
code source: 7b81953102bcd4757a715ff1a164554f6a9e66d7. This report commit
changes documentation only. Assigned branch remains fast-forward-only from
3f6f2cd772a12f70b6a099b5c1d2e3700676cc06; every slice was pushed.

Changed scripts/test-deploy-server2.ps1: Test-C973Jq resolves one application,
supports C973_JQ_PROBE_SHELL and returns false for missing executables, nonzero
exits and launch/native exceptions. Defaults remain wsl -e bash on Windows and
bash on Linux. The override receives -c 'command -v jq'.

Changed scripts/test-deploy-server2-jq.ps1: missing-shell injects a nonexistent
application; failing-shell injects real Git, whose probe arguments exit 1.
Neither case stubs the real probe. Present uses the real probe, validates the
lower roster when unavailable, and emits C973_JQ_SKIPPED present jq-missing.
With jq available it requires T-20, 20/59/206, four marker invocations and no
skips. Successful runs delete only the DirectoryInfo returned when creating
their own GUID root. Failed evidence is retained; deletion never consumes an
environment-supplied path. The plan records exact rerun commands and filters.

Red first at seam-only S1 2d5109ca7eeeded81ac0056a26f33ef4c901c87a:
missing-shell exited 1 before T-1 with CommandNotFoundException. The driver
failed its first assertion, harness exit=1. Slot granted, zero wait, held 2s.
Log: .antiphon/c973-missing-shell-red.log. Production remained the assigned
base throughout this red; only the probe injection and driver were added.

Verification results (all script runs leased, zero slot wait):

| Row | Source | Result | Driver assertions | Slot held |
|---|---|---|---:|---:|
| CP-probe-missing | 7b819531 | exit 0; named skip; 19/55/197; no marker invocation | 27, zero failures | 263s |
| CP-probe-failing | 7b819531 | exit 0; named skip; 19/55/197; no marker invocation | 27, zero failures | 263s |
| CP-probe-absent | fb7281ed | exit 0; named skip; 19/55/197; no marker invocation | 27, zero failures | 253s |
| CP-probe-present-skip | fb7281ed | exit 0; named driver skip; 19/55/197; no marker invocation | 27, zero failures | 279s |
| CP-probe-present | 7b819531 | exit 0; real probe True; no skips; 20/59/206; four marker invocations | 27, zero failures | 277s |
| CP-973-caches | 7b819531 | 39 passed, zero failed/skipped; isolated build | n/a | receipt below |
| CP-18 | 7b819531 | 215 passed, zero failed/skipped; reused build | n/a | receipt below |
| CP-21 | 7b819531 | 10 passed, zero failed/skipped; reused build | n/a | receipt below |

All five successful case types removed their evidence roots. Final missing,
failing and present logs: .antiphon/c973-{missing-shell,failing-shell,present}-rerun.log.
Absent and present-skip logs: .antiphon/c973-{absent,present-skip}.log.

One intermediate jq-present attempt at fb7281ed exited 0 but skipped T-20,
so it did not qualify the present proof. Get-Command returned both /usr/bin/bash
and /bin/bash; using their combined Source as a command caused false absence.
7b819531 selects the first application for both Bash and the Git fixture. Direct
calibration then returned jq path/exit 0 and Git exit 1. The initial failing-shell
green was superseded because its Git resolution could have had the same issue.
Missing, failing and present rows were rerun after this correction (one rerun
each); absent and present-unavailable behavior was unchanged. Independent
reruns overlapped the contract build under separate broker leases to respect
the 40-minute time box. No extra .NET build or Unit-lane selection ran.

jq: echoed mktemp root /tmp/card0973-probe-jq-qq4d1A, binary bin/jq,
version jq-1.7.1. SHA256 verified before chmod/use:
5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5.
Absent and present-skip ran on the genuinely jq-free host PATH. Final present
and contracts prepended the verified bin directory. Scratch files are retained.

Contracts used run-checkpoint.ps1 directly, literal pipes, bin-c973-probe/,
ExpectedSourceSha=7b81953102bcd4757a715ff1a164554f6a9e66d7,
C804_ORPHAN_SWEEP_ROOT=c973-disabled and TUNIT_MAX_PARALLEL_TESTS=1.
All three source.json receipts validated: dirty=0, sourceState=clean,
buildSource=verified. PowerShell parser, ASCII and git diff --check passed.

Production c590-remote.sh and deploy-server2.ps1 are byte-identical to the
assigned base (blob IDs 39315a83a07021ce55bb30d52aa9e3c882e28c2b and
92e03fd3894a45c767ddde023b31a104431fa65e). No live deploy phase, server2
operation, restart or drain clear ran. Windows execution was unavailable here;
the cross-platform real missing/nonzero executable cases ran on Linux.
PCs remain pending method-scoped SourceLanding Mutation. Next stage: Review;
this task is the landing owner after review, using plain land.

Final scratch rebase feasibility and final remote SHA are included in the stage
completion after this documentation-only commit. The assigned branch is never
rebased. Exact checkpoint receipts follow, preserved unedited.

CHECKPOINT CP-973-caches commit=7b81953102bcd4757a715ff1a164554f6a9e66d7 build=ok filter=/*/*/RemoteScriptContractTests*/(C849_*)|(C912_*)|(C973_*) executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/task-ba33544f/.antiphon/c973-probe-checkpoints/CP-973-caches-20261002-152657-fa3d/run.trx slot=granted waited=0s dirty=0 source=7b81953102bcd4757a715ff1a164554f6a9e66d7 sourceState=clean buildSource=verified
CHECKPOINT CP-18 commit=7b81953102bcd4757a715ff1a164554f6a9e66d7 build=reused filter=/*/*/(DockerStackContractTests*)|(DindRunnerContractTests*)|(RemoteScriptContractTests*)|(RunnerDrainScriptTests*)|(DockerStackDocumentationTests*)/* executed=215 passed=215 failed=0 skipped=0 trx=/work/worktrees/task-ba33544f/.antiphon/c973-probe-checkpoints/CP-18-20261002-153106-87e6/run.trx slot=granted waited=0s dirty=0 source=7b81953102bcd4757a715ff1a164554f6a9e66d7 sourceState=clean buildSource=verified
CHECKPOINT CP-21 commit=7b81953102bcd4757a715ff1a164554f6a9e66d7 build=reused filter=/*/*/DockerStackDocumentationTests*/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-ba33544f/.antiphon/c973-probe-checkpoints/CP-21-20261002-153325-6fc9/run.trx slot=granted waited=0s dirty=0 source=7b81953102bcd4757a715ff1a164554f6a9e66d7 sourceState=clean buildSource=verified
