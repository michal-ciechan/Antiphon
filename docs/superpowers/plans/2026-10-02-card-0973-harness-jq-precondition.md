# CARD-0973 harness jq precondition follow-up

Harness/fake-only follow-up to the reviewed production fix at
9affbc51d034ece2c257500ebe5c80dea2d95bd3. Keep c590-remote.sh and
deploy-server2.ps1 unchanged. No Unit lane, deploy phase, remote host operation,
or drain clear is authorized in this round.

S1 adds a Linux-runnable regression driver with absent/present cases. Reproduce
the original harness red at the assigned base without jq, then run the absent
regression red before fixing the harness. S2 probes jq once in the reader's shell
(WSL on Windows), supplies a marker only to explicit marker groups, and skips
T-20 with a named CARD-0927 notice when jq is missing. The regression driver
stubs the missing probe, checks every original group's PASS line and exact
counts, and inspects invocation state to prove marker isolation. Present uses
the real probe and must execute T-20 without any skip.

## Verification design

Commit and push each slice. Final rows share one isolated committed build;
invoke run-checkpoint.ps1 directly, with literal filter pipes and the exact
ExpectedSourceSha. Set C804_ORPHAN_SWEEP_ROOT=c973-disabled and
TUNIT_MAX_PARALLEL_TESTS=1. Present runs prepend a scratch jq 1.7.1 install
verified against SHA256
5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5.
The proof driver owns no slot; build-slot.ps1 owns the outer harness lease.
The absent proof runs on a PATH without jq as well as stubbing its probe.

Final static checks: git diff --check; production files identical to assigned
base; receipt validation. Then fetch and try the land's rebase flags in a
throwaway detached scratch worktree against origin/master (80867765 or newer).
Never rebase the assigned branch. PCs remain pending SourceLanding Mutation.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-jq-base-red | base | n/a | missing-jq-red | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c973-jq-absent-red -- pwsh -NoProfile -File scripts/test-deploy-server2.ps1` | exact original regression | exit 1; T-20 CacheVolumeForeign, jq missing, 0/1/1 | n/a | 1 |
| CP-jq-assertion-red | S1 | n/a | regression-red | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c973-jq-regression-red -- pwsh -NoProfile -File scripts/test-deploy-server2-jq.ps1 -Case absent` | regression assertion goes red | exit 1; harness exit assertion fails with original failure | n/a | 1 |
| CP-jq-absent | S2 | n/a | missing-jq | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c973-jq-absent -- pwsh -NoProfile -File scripts/test-deploy-server2-jq.ps1 -Case absent` | T-1..T-19; no marker invocation | exit 0; named skip; 19/55/197; 27 regression assertions | n/a | 6 |
| CP-jq-present | S2 | n/a | available-jq | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c973-jq-present -- pwsh -NoProfile -File scripts/test-deploy-server2-jq.ps1 -Case present` | T-1..T-20; marker isolation | exit 0; no skips; 20/59/206; 27 regression assertions | n/a | 6 |
| CP-973-caches | S2 | `tests/Antiphon.Tests -> bin-c973-jq/` | cache-contracts | `/*/*/RemoteScriptContractTests*/(C849_*)\|(C912_*)\|(C973_*)` | full C849/C912/C973 cache contracts | 39 passed, 0 failed/skipped | 39 | 8 |
| CP-18 | S2 | CP-973-caches | rolling-contracts | `/*/*/(DockerStackContractTests*)\|(DindRunnerContractTests*)\|(RemoteScriptContractTests*)\|(RunnerDrainScriptTests*)\|(DockerStackDocumentationTests*)/*` | full reviewed rolling contract classes | 215 passed, 0 failed/skipped | 215 | 5 |
| CP-21 | S2 | CP-973-caches | rolling-docs | `/*/*/DockerStackDocumentationTests*/*` | reviewed docs row | 10 passed, 0 failed/skipped | 10 | 2 |
