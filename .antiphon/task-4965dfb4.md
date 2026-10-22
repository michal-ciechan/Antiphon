# CARD-1502 S1 re-cut: task 4965dfb4

Replayed only CARD-1502 onto assigned base `5b44186fc19c6a8612e7b17d1647bdd2fa89679f`.
Every replay used `cherry-pick -x` and was pushed individually. No merge, rebase, reset,
amend, branch switch, or modification of the original reviewed branch was used.
This report is committed before the final checkpoint run so its commit is the tested tip.
Fresh results and unedited CHECKPOINT lines are in the checkpoint tool's ignored
`.antiphon/checkpoints/<run-id>/report.md` and the final caller report; this document
does not predeclare those executions green.

Branch: `feat/card-task-4965dfb4`. Worktree: `/work/worktrees/task-4965dfb4`.
Landing owner: original Code task for this re-cut, `4965dfb4-669d-4f39-b429-3eec8d5dda3d`.
Prior Code `259765b1` remains Blocked. Restart: none.
Plan: `docs/superpowers/plans/2026-10-22-card-1502-worktree-heal-plan.md`.

## Replay and upstream proof

The brief says seven CARD-1502 commits; the actual range contains eight including
the final report. All eight were retained, in this exact order:

| Original | Re-cut |
|---|---|
| a178812e51dab77a33230ec5c51462ed848f8cd3 | 173104b91ee1c3a036d39165cfbd4ba3164cdfb8 |
| 5f561fb377c0f9cb39a74308c4a299952b7ce49e | 50ff34229105c93bf90d6a398dc197f95b337f38 |
| 65668365f3f0004fcb0d6cb52c1a675af4906e7c | 1fbeb2c7dd80ae4f7244cce7db6d9db9eefd74b0 |
| 0670d28519bb855c68432324b8e14a293cd4a9f9 | aad6ca7a206ef2aced9dbdb5da39de7133c2e901 |
| a07f64a7b57c78c39bc856dcd71bfe7e5f9b2b59 | d3a27554fddd9790ae1efba430e453eac438fc4f |
| 15136802ceb7646dabba7eca61fa328481cdc1d2 | 74ca025bdc9293fcd1f3c2cc267cf8610242e398 |
| 80625980cc94db97c81d0adb16982f3be2392883 | 0d7c501366c0836c740ae514b7c796bad9651d08 |
| 28a325193e8e74336b399583842b7a50b27e61d4 | d1e2459b1f78d4545a5ae8b6450f1cd104687ff6 |

The four omitted carries match upstream by subject and `git patch-id --stable`:

| Carry | Upstream | Stable patch ID |
|---|---|---|
| 513572379e46b1695c833b9ad181d4181b513a52 | 0498c748a448f367e6153316c992585d36dea74b | 6cdae7e545f6918d32ca484d864896f1e9ea6236 |
| 5d68ad69299c090677c29ce2eb003862ecaca845 | a23ca2869618811619bcb0965d386ecb6e786c18 | 213df40d43edecfc0ad75c777e26d5a23f4cfb49 |
| d134c2c77b7a97d79ea938b8a2df6afca61f3822 | e4ef4c621cb43835254db8d9d21fb79abc18f7bd | 96d3555a4dc8d71887f386d7264b1f43f7f8af84 |
| 06643061982c31a5443752e8c3790f4873f9e703 | 222ceeb0074427fb42b7a7cb8b439fa0e17ffa59 | 48f842a8662b4ebcf9e1e3210ffe5db2a851dd4a |

All five CARD-1502 product/test files are byte-identical to `28a325193`:
WorktreeManager.cs, WorktreeManagerTests.cs, DelegationWorktreeTests.C1502.cs,
AgentSessionServiceIntegrationTests.cs, InterruptedWorktreeCreation.cs.

Acceptance clarification: the entire `server/ tests/` diff against `28a325193` is
**not empty**: master already contains independent changes in 12 other files.
Reverting these would violate the requested re-cut onto current master. Their
diff is byte-for-byte identical to the base-to-base diff, not an unexplained
CARD-1502 change. Exact reproducible proof (`git diff --binary --full-index`):

- `066430619..28a325193 -- server tests` and `5b44186fc..HEAD -- server tests`
  both SHA-256 `7b3ebfa7d0fa689c0b6b41d1e9dfb0a39ce471ea2b92b5aec045704de0fa6ba1`;
  stable patch ID `7974ce55b0ae80310276cfb505614c6d5bc1f79f`.
- `066430619..5b44186fc -- server tests` and `28a325193..HEAD -- server tests`
  both SHA-256 `0a51681fe69e51961505e12fabe92b2d8ae021a8d2f9909eecfe8700b49af3a1`;
  stable patch ID `5beb0325481aff3dd47528d6e56a107380d692cc`.

Residual `git diff --stat 28a325193 HEAD -- server tests`:

```text
 server/Application/Services/AgentSessionService.cs | 20 +-
 server/Application/Services/AgentTaskReplyService.cs | 30 +-
 server/Application/Services/AgentTaskService.cs | 40 +-
 server/Application/Services/DispatchModelAlias.cs | 17 +
 server/Application/Services/LandVerificationBuildGraph.manifest.txt | 4 +-
 tests/Antiphon.Tests/Antiphon.Tests.csproj | 15 +-
 tests/Antiphon.Tests/Application/AgentTaskReplyIntegrationTests.C1499ReleaseNotFound.cs | 179 +
 tests/Antiphon.Tests/Application/BlockedReplyReportRetentionTests.cs | 106 +-
 tests/Antiphon.Tests/Application/HeldStandingAgentChainTests.cs | 225 +
 tests/Antiphon.Tests/Application/LandGraphEvaluationTests.cs | 479 +
 tests/Antiphon.Tests/Application/LandNonCodeSkipTests.cs | 75 +
 tests/Antiphon.Tests/Checkpoints/CheckpointImportTests.cs | 104 +
 12 files changed, 1281 insertions(+), 13 deletions(-)
```

## Verification scope and provenance

The specific re-cut brief requires CP-1 (10) and CP-3 (21), CP-2 (60) only on a
count difference, and no Unit lane. The plan has D-1/D-2/coverage (B), not V-n/R-n
IDs. D-3/uncommitted-file recovery remains explicitly deferred on CARD-1502.
No new test or production change was invented in this re-cut; original red
witnesses and costs remain in `.antiphon/task-259765b1.md`.

Both runner catalog routes were read. The global default and eligible assigned
Linux runner are server2. No runner or platform override was introduced.
One unlisted bootstrap build was necessary to run the checkpoint tool from this
checkout: `scripts/build-slot.ps1 -Label c1502-recut-checkpoint-bootstrap --
dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1502-recut-tool/ --nologo`.
It passed (0 errors, 4 nullable warnings), slot=granted, waited=30s, held=24s,
build elapsed 22.44s, source `d1e2459b1f78d4545a5ae8b6450f1cd104687ff6`.
Only this report changes after bootstrap; checkpoint tool source stays identical.
The product/test build will be made by the tool at this report's pushed commit,
with `--expected-source-sha`, CP-1/CP-3, and no repeated proof selection.

## Land and RC port

`git merge-tree --write-tree origin/master HEAD` was clean against master
`5b44186fc19c6a8612e7b17d1647bdd2fa89679f`; final HEAD is checked again after the run.

RC `0714a9a89e4bff65aae7e0b4b94098694aeb931c` lacks CARD-1448. Direct first
CARD-1502 replay conflicts in WorktreeManagerTests.cs and the missing
InterruptedWorktreeCreation.cs. A read-only per-commit merge-tree simulation,
using each commit's parent as merge base and unreferenced commit-tree objects,
is clean for this exact order:

1. `0498c748a448f367e6153316c992585d36dea74b`
2. `a23ca2869618811619bcb0965d386ecb6e786c18`
3. `e4ef4c621cb43835254db8d9d21fb79abc18f7bd`
4. `222ceeb0074427fb42b7a7cb8b439fa0e17ffa59`
5. All eight re-cut SHAs above, in table order.
6. This report commit, if preserving the whole task history (documentation only).

No RC ref was changed; textual applicability is not runtime RC qualification.

## Mutation handoff

All pending: PC-1 (heal call / false return), PC-2 (index and ancestry variants),
PC-3 (complete-intent guard), PC-4 (all-admin deletion / prune variants),
PC-5 (rollback warning), PC-6 (card-spawn filter true variant with complete
control), PC-7 (branch-elsewhere guard). Mutation owns deliberate mutants,
missing-control discovery, and method-scoped red/restore/green after landing.
No mutation result is claimed by ordinary green. No unbound candidate was added.

Next: Review this pushed re-cut branch; after clean Review, caller lands task
4965dfb4 and commissions SourceLanding Mutation.
