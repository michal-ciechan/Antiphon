Fixed CARD-0973's rolling-harness jq precondition; absent and present jq proofs and all requested contracts passed.

Verified code source: 59f5edcc129312b995cdd375e60715a5aeab3669. This report is a documentation-only follow-up. Assigned branch stays fast-forward-only from 9affbc51d034ece2c257500ebe5c80dea2d95bd3; every slice was committed and pushed.

Changes:
- scripts/test-deploy-server2.ps1 probes jq once in the execution shell (wsl -e bash -c 'command -v jq' on Windows; bash directly on Linux). Only T-20's four calls opt into UseMarker. Other groups receive no marker fields and use the original fake behavior. Without jq, T-20 emits C973_SKIPPED jq-missing: T-20 marker-reader groups need jq (CARD-0927), then reports the lower frozen roster explicitly and exits 0.
- scripts/test-deploy-server2-jq.ps1 is the Linux-runnable regression driver. Its absent case stubs the missing probe; its present case always uses the real probe. Each case checks one probe notice, every T-1..T-19 PASS line, exit status, exact counts, skip behavior and saved invocation state. The absent proof also ran on a PATH genuinely without jq. State assertions confirm zero marker paths outside T-20, zero marker invocations when absent, and exactly four when present.
- docs/superpowers/plans/2026-10-02-card-0973-harness-jq-precondition.md records the closed verification rows and rerun commands.

Production scripts c590-remote.sh and deploy-server2.ps1 are byte-identical to the assigned base. The reviewed live-Docker production fix is preserved. No deploy phase, remote host operation, runner restart or drain clear ran. No Unit-lane selection. Windows execution was unavailable here; Windows uses the explicit WSL probe, and remains for companion qualification. PCs stay pending method-scoped SourceLanding Mutation.

Red first:
- At assigned base 9affbc51, no jq on PATH: exit 1, FAIL T-20 pruned cold seed image verifies before admission, DIAGNOSIS=CacheVolumeForeign, jq: command not found, Rolling deploy stopped: HostCaseFailed deploy-parent exit=2. Frozen counters: 0 groups / 1 invocation / 1 assertion / 1 failure. Build slot granted, 0s wait, released after 4s.
- S1 8b5f8094e's regression assertion was red before fixing the harness: same underlying T-20 failure, FAIL C973_JQ absent harness exit=1. One regression assertion, one failure. Slot granted, 0s wait, released after 5s. Full S1 SHA is recorded by Git history.

Green at verified code source:
- CP-jq-absent: exit 0; named skip; T-1..T-19 all passed; 19 groups / 55 invocations / 197 harness assertions / 0 failures; 27 regression assertions / 0 failures. No marker path in any saved invocation. Slot granted, 0s wait; held 264s.
- CP-jq-present: exit 0; no skips; 20 groups / 59 invocations / 206 harness assertions / 0 failures; 27 regression assertions / 0 failures. Only T-20's four invocations have marker paths. Slot granted, 0s wait; held 279s.
- CP-973-caches: 39 passed, 0 failed/skipped; one isolated build.
- CP-18: 215 passed, 0 failed/skipped; reused the verified build, full affected rolling classes.
- CP-21: 10 passed, 0 failed/skipped; reused the verified build.
- All three source.json receipts validated with validate-checkpoint-receipt.ps1 at source 59f5edcc129312b995cdd375e60715a5aeab3669: dirty=0, sourceState=clean, buildSource=verified. No extra build was run.
- The first cache-row command had an invalid SHA argument and was refused before slot/build/tests (exit 2, zero execution); corrected and rerun once. It is not failure evidence for the implementation. An attempted formatting helper found no python3; indentation was corrected with apply_patch before green qualification.
- git diff --check, PowerShell parser and ASCII checks passed. Production-file diff against assigned base is empty.

jq present: /tmp/card0973-jq-hUIy2C/bin/jq, version jq-1.7.1, checksum verified against 5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5. Downloaded to an echoed mktemp directory. Scratch artifacts were retained; no deletion or process kill was used.

Rerun the plan's final rows. Absent: pwsh -NoProfile -File scripts/build-slot.ps1 -Label c973-jq-absent -- pwsh -NoProfile -File scripts/test-deploy-server2-jq.ps1 -Case absent. Present: prepend the verified jq directory to PATH and substitute -Case present. TUnit rows run run-checkpoint.ps1 directly, never nested under build-slot; use literal pipes and the current clean SHA. Set C804_ORPHAN_SWEEP_ROOT=c973-disabled and TUNIT_MAX_PARALLEL_TESTS=1.

Final landing-feasibility check runs after this report commit, with git -c rebase.autoStash=false -c rebase.updateRefs=false rebase origin/master in a detached scratch worktree only. Its result and final remote branch SHA are reported in the stage completion. It does not authorize landing or production activation during this Code stage; next stage is Review.

Exact checkpoint receipts (preserved unedited):
CHECKPOINT CP-21 commit=59f5edcc129312b995cdd375e60715a5aeab3669 build=reused filter=/*/*/DockerStackDocumentationTests*/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-5799d88c/.antiphon/c973-jq-checkpoints/CP-21-20261002-142725-5e0c/run.trx slot=granted waited=0s dirty=0 source=59f5edcc129312b995cdd375e60715a5aeab3669 sourceState=clean buildSource=verified
CHECKPOINT CP-973-caches commit=unknown build=n/a filter=/*/*/RemoteScriptContractTests*/(C849_*)|(C912_*)|(C973_*) executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-18 commit=59f5edcc129312b995cdd375e60715a5aeab3669 build=reused filter=/*/*/(DockerStackContractTests*)|(DindRunnerContractTests*)|(RemoteScriptContractTests*)|(RunnerDrainScriptTests*)|(DockerStackDocumentationTests*)/* executed=215 passed=215 failed=0 skipped=0 trx=/work/worktrees/task-5799d88c/.antiphon/c973-jq-checkpoints/CP-18-20261002-142451-9c74/run.trx slot=granted waited=0s dirty=0 source=59f5edcc129312b995cdd375e60715a5aeab3669 sourceState=clean buildSource=verified
CHECKPOINT CP-973-caches commit=59f5edcc129312b995cdd375e60715a5aeab3669 build=ok filter=/*/*/RemoteScriptContractTests*/(C849_*)|(C912_*)|(C973_*) executed=39 passed=39 failed=0 skipped=0 trx=/work/worktrees/task-5799d88c/.antiphon/c973-jq-checkpoints/CP-973-caches-20261002-142041-d739/run.trx slot=granted waited=0s dirty=0 source=59f5edcc129312b995cdd375e60715a5aeab3669 sourceState=clean buildSource=verified
