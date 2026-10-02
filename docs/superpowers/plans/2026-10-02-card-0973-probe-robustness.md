# CARD-0973 missing probe executable follow-up

Harness/driver-only continuation from 3f6f2cd772a12f70b6a099b5c1d2e3700676cc06.
Production c590-remote.sh and deploy-server2.ps1 stay byte-identical. No Unit
lane or remote rollout operations. PCs remain pending SourceLanding Mutation.

S1 introduces C973_JQ_PROBE_SHELL and missing-shell/failing-shell driver cases.
The missing-shell case goes red before S2 adds application resolution and a
catch-all false result. Default probes still run wsl -e bash on Windows and bash
on Linux. Overrides use the supplied application with -c 'command -v jq'. The
failing-shell driver uses real git, whose probe arguments exit nonzero on both
platforms. Neither injected case stubs the real probe result.

The present driver runs the real probe and validates the lower roster with a
named C973_JQ_SKIPPED present jq-missing notice when unavailable. With jq it
must validate 20/59/206, four marker invocations and no skips. Successful drivers
delete only the DirectoryInfo they created for their own GUID evidence root;
failed evidence is retained. Environment-supplied roots are never deleted.

## Verification design

Commit and push each slice. Script rows use build-slot.ps1. Run present once
on the host's genuinely jq-free PATH, then with scratch jq 1.7.1 verified against
5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5.
Contract rows use run-checkpoint.ps1 directly, one isolated bin-c973-probe/
build and exact ExpectedSourceSha, C804_ORPHAN_SWEEP_ROOT=c973-disabled and
TUNIT_MAX_PARALLEL_TESTS=1. CLI filters contain literal pipes.
Finally validate receipts, production identity, whitespace, PowerShell syntax
and ASCII; try the land's rebase flags in a detached scratch worktree against
origin/master d15c8154 or newer. Never rebase the assigned branch.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-probe-red | S1 | n/a | missing-shell-red | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c973-missing-shell-red -- pwsh -NoProfile -File scripts/test-deploy-server2-jq.ps1 -Case missing-shell` | missing executable red | exit 1; harness exit assertion fails before T-1 | n/a | 1 |
| CP-probe-missing | S2 | n/a | missing-shell | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c973-missing-shell -- pwsh -NoProfile -File scripts/test-deploy-server2-jq.ps1 -Case missing-shell` | real missing executable probe | exit 0; named skip; 19/55/197; 27 driver assertions | n/a | 5 |
| CP-probe-failing | S2 | n/a | failing-shell | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c973-failing-shell -- pwsh -NoProfile -File scripts/test-deploy-server2-jq.ps1 -Case failing-shell` | real nonzero executable probe | exit 0; named skip; 19/55/197; 27 driver assertions | n/a | 5 |
| CP-probe-absent | S2 | n/a | absent | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c973-absent -- pwsh -NoProfile -File scripts/test-deploy-server2-jq.ps1 -Case absent` | original absent case | exit 0; named skip; 19/55/197; 27 driver assertions | n/a | 5 |
| CP-probe-present-skip | S2 | n/a | present-unavailable | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c973-present-skip -- pwsh -NoProfile -File scripts/test-deploy-server2-jq.ps1 -Case present` | present on jq-free host | exit 0; named driver skip; 19/55/197; 27 driver assertions | n/a | 5 |
| CP-probe-present | S2 | n/a | present | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c973-present -- pwsh -NoProfile -File scripts/test-deploy-server2-jq.ps1 -Case present` | present with checksum-verified jq | exit 0; no skips; 20/59/206; 27 driver assertions | n/a | 5 |
| CP-973-caches | S2 | `tests/Antiphon.Tests -> bin-c973-probe/` | cache-contracts | `/*/*/RemoteScriptContractTests*/(C849_*)\|(C912_*)\|(C973_*)` | cache contracts | 39 passed, 0 failed/skipped | 39 | 6 |
| CP-18 | S2 | CP-973-caches | rolling-contracts | `/*/*/(DockerStackContractTests*)\|(DindRunnerContractTests*)\|(RemoteScriptContractTests*)\|(RunnerDrainScriptTests*)\|(DockerStackDocumentationTests*)/*` | rolling classes | 215 passed, 0 failed/skipped | 215 | 2 |
| CP-21 | S2 | CP-973-caches | rolling-docs | `/*/*/DockerStackDocumentationTests*/*` | docs | 10 passed, 0 failed/skipped | 10 | 1 |
