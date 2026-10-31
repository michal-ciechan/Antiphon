# CARD-1609 H7 helper graph import Debug

H7 is repaired and its exact official retest is green. Shared-helper qualification is pending. Assigned baseline f5b9e0c4bdbb3953d4e98a37024c38bbe64b370e, branch feat/card-task-7dd4e872.

## Observed cause

`origin-present` is emitted by `consider_common` when `comparisons` is empty, after both local-present and imported/fetched advertised commit lists are empty. It does not require a missing origin remote. The scratch origin remote is expected configuration, not proof of publication.

The standalone reproduction used the production extracted audit and actual `Write-C1609HostOriginProof`, with the same HTTPS-to-owned-bare host adapter, real Git A->B, A-only target, v2 bindings and `c1008-fake-docker.sh` chroot. This adds the actual helper namespace missing from the two prior isolated-function probes; those previously passing probes were not repeated.

Observed baseline trace: bundle unbundle succeeds, then the advertisement comparison calls `awk`, which fails with `awk: command not found`. `/usr/bin/awk -> /etc/alternatives/awk -> /usr/bin/mawk`; the fixture mounted /usr/bin but omitted /etc/alternatives. The importer returns 1, fetched=0 and present=0, and the audit refuses `RecycleGitAuditUnknown audit check=origin-present status=0 repo=repos/school-revision`. The bare scratch config has origin=https://fixture.invalid/card-1609/private.git and blob:none; it has no refs.

Replaying the exact same bundle/target/program through a disposable fixture copy with only a read-only /etc/alternatives bind changes the result to import=0, fetched=1, present=0; scratch refs contain `8b51a6356c5dc06351ebdca4c2b98068abd8d00b refs/c1008/origin/1`; audit exits0 with repositories=1 partial=0. A is `7d2a1a77020dcd6bdf80d0ce92f6624e66ff78df`. Both probes were host-slot leased (120s/135s admission; 3s/1s held), diagnostics only, not checkpoint credit.

Standalone sources: /tmp/h7-probe.cjs and /tmp/h7-replay.cjs. Evidence: /tmp/c1008-host-h7probe/result.log, preview-helper-stderr and fixed-result.log. Scratch is retained. Rerun in a fresh scratch path through `scripts/build-slot.ps1 -- node <probe>`; replay uses the exact first Docker-trace argv, not a hand-written publication verdict.

Plan authority (H7 recipe): "With v2, the real host writer fetches B's graph in origin-only scratch and proves A; the same entry reaches Ready with no removals, no helper credentials and the A->B witness in the receipt." D-6 requires a real advertised graph and rejects cached tracking refs as proof. The test's protective expectation is correct. Fix the fixture namespace; keep production content, graph, refusal and deletion logic unchanged. No exact-build dependency is added.

## Verification and remaining scope

Pending official red/green and CP-3/4/15 results. CP-3 is Content+Receipt (25 methods), CP-4 HostProof+Wrapper (12), CP-15 RetiredTempContainerScriptTests (16). The complete final Linux roster, long-scan/manual/v2 acceptance and Windows qualification remain separate Code work. Production deletes remain disabled; first production action stays preview-only.

## Baseline official red

Exact unchanged H7 at the assigned base: 1 executed / 0 passed / 1 failed / 0 skipped. Intended unpublished-child classifier assertion expected RecycleUnpublishedWork, observed origin-present. This confirms the failure is inherited at the assigned base. Build compiler228.60s, scheduler build270.284s; total admission135s (build15s,row120s); test35.223s, row execution40.705s. Run wall466.359s, charged331.363s. Clean source and verified producing build. Optional phase journals /tmp/h7-base-phases; unrecorded internal phases remain unknown. Bootstrap import was leased (wait30s/held9s), 16 rows, five warnings/zero errors. One preliminary manifest command failed because python3 is absent and then refused the missing manifest; zero build/test credit, fixed using Node.

```text
CHECKPOINT CP-14 commit=f5b9e0c4bdbb3953d4e98a37024c38bbe64b370e build=ok filter=/*/Antiphon.Tests.Scripts/VolumeRetirementContentTests/Private_origin_advanced_head_uses_real_host_proof executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-7dd4e872/.antiphon/checkpoints/20261031-041521-f4b0/rows/CP-14/run.trx slot=granted waited=120s dirty=0 source=f5b9e0c4bdbb3953d4e98a37024c38bbe64b370e sourceState=clean buildSource=verified
```

Terminal wait exit1. Output cleanup reported still-present and retained bin-h7-base; exact owned output cleanup is deferred until all required checks finish. Target B absence after the successful standalone replay was checked directly: cat-file exited128 (not a valid object).

## Committed repair and isolated green

Red evidence was committed/pushed as dffe931e303bd33ef9ed3a052084c5e34d65c1df before the fixture repair 98cde6eabd060bb07bd6f898b428f0cc9e71a8b8. The eight-line fixture change conditionally binds /etc/alternatives read-only beside the existing /usr/bin bind. No production/test assertion/timeout/definition changes. The awk comparison arrived in 85776c1c8e201699aa1318cb2f00d69f85b5e83f; its helper filesystem requirement exposed the pre-existing incomplete chroot.

H7 green: 1/1/0/0 at the repair SHA, including Ready admission, unpublished child, target-B absence, no helper remote lookup and seven malformed-proof refusal controls. Compiler197.99s, scheduler build247.514s; admission105s (build30s,row75s); test94.902s, row99.860s. Total wall478.619s/charged373.626s. Baseline failed early; its shorter duration is not a comparable full-method performance sample. No p95 claim or timeout increase.

```text
CHECKPOINT CP-14 commit=98cde6eabd060bb07bd6f898b428f0cc9e71a8b8 build=ok filter=/*/Antiphon.Tests.Scripts/VolumeRetirementContentTests/Private_origin_advanced_head_uses_real_host_proof executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-7dd4e872/.antiphon/checkpoints/20261031-042359-0e52/rows/CP-14/run.trx slot=granted waited=75s dirty=0 source=98cde6eabd060bb07bd6f898b428f0cc9e71a8b8 sourceState=clean buildSource=verified
```

Terminal wait exit0, source clean/build verified. Run20261031-042359-0e52. Optional phases /tmp/h7-green-phases; producing outputs retained for the real land evaluator and later literal cleanup.

Real `audit-land-verification-graph.ps1 -Check` passed against the current tip using the evaluator from that verified build, through the host gate; no -Update. Raw log .antiphon/h7-land-check.log. Source definitions are unchanged. Shared rows next use fresh isolated outputs and exact imported plan filters/floors, CP-3=28m, CP-4=5m, CP-15=6m from the commissioned prior Debug caps.
