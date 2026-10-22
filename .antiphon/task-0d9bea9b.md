# CARD-1502 S1 re-cut Final Review: task 0d9bea9b

Clean. The re-cut at `55f998fab6ff5a6e3d0b204540aff638b5040847` is the same CARD-1502 product as reviewed tip `28a325193e8e74336b399583842b7a50b27e61d4`. Land Code owner `4965dfb4-669d-4f39-b429-3eec8d5dda3d` with `-ExpectedSourceSha 55f998fab6ff5a6e3d0b204540aff638b5040847`.

## Equivalence

Five product/test blobs are identical at both tips:

- `server/Infrastructure/Git/WorktreeManager.cs` `84a78fa2660d69e89fea29762fc09711ece8a7cd`
- `tests/Antiphon.Tests/Infrastructure/WorktreeManagerTests.cs` `f4e04be792dc16b93213b067d3020bb8e88a992a`
- `tests/Antiphon.Tests/Application/DelegationWorktreeTests.C1502.cs` `598195518f4aafdec12614da07cccd7906b0e042`
- `tests/Antiphon.Tests/Application/AgentSessionServiceIntegrationTests.cs` `3d6a75c06944f345b9e2daa283d204fa4b04d641`
- `tests/Antiphon.Tests/TestHelpers/InterruptedWorktreeCreation.cs` `c29c2caf2030f9f1e83ab2cf6ed87359fd5394aa`

All eight replayed commits match the originals by `git patch-id --stable`:

| Original | Re-cut | Patch-id |
|---|---|---|
| a178812e51dab77a33230ec5c51462ed848f8cd3 | 173104b91ee1c3a036d39165cfbd4ba3164cdfb8 | 3e86eb6610576e32467ec09d28c500bc0d6028cd |
| 5f561fb377c0f9cb39a74308c4a299952b7ce49e | 50ff34229105c93bf90d6a398dc197f95b337f38 | 95a0354a1785801c7ce7a45b56458abad26a4e95 |
| 65668365f3f0004fcb0d6cb52c1a675af4906e7c | 1fbeb2c7dd80ae4f7244cce7db6d9db9eefd74b0 | 97b284a0ac5467a1608eb4236229a16bcb846146 |
| 0670d28519bb855c68432324b8e14a293cd4a9f9 | aad6ca7a206ef2aced9dbdb5da39de7133c2e901 | 30dcac7a331edae983bf43ff166f7f5f704171fa |
| a07f64a7b57c78c39bc856dcd71bfe7e5f9b2b59 | d3a27554fddd9790ae1efba430e453eac438fc4f | b87f438a97ff1f98a97528e033687e64247c48f9 |
| 15136802ceb7646dabba7eca61fa328481cdc1d2 | 74ca025bdc9293fcd1f3c2cc267cf8610242e398 | 889b7e43fdb26f91370a2808b8dcfe0673e7a60e |
| 80625980cc94db97c81d0adb16982f3be2392883 | 0d7c501366c0836c740ae514b7c796bad9651d08 | d49557ff554070851912f180f09d6b88bb059ecb |
| 28a325193e8e74336b399583842b7a50b27e61d4 | d1e2459b1f78d4545a5ae8b6450f1cd104687ff6 | 2011247b7bfb1bca4548393a57ea875d7d684c21 |

`06643061982c31a5443752e8c3790f4873f9e703..28a325193` is exactly those eight commits. From merge-base `713801a8f98c0403bb9981bcbe954e68110cbb19`, the original tip adds only those eight plus the four CARD-1448 carries. Nothing else was dropped.

The four carries match upstream commits that are ancestors of re-cut base `5b44186fc19c6a8612e7b17d1647bdd2fa89679f`:

| Carry | Upstream | Patch-id |
|---|---|---|
| 513572379e46b1695c833b9ad181d4181b513a52 | 0498c748a448f367e6153316c992585d36dea74b | 6cdae7e545f6918d32ca484d864896f1e9ea6236 |
| 5d68ad69299c090677c29ce2eb003862ecaca845 | a23ca2869618811619bcb0965d386ecb6e786c18 | 213df40d43edecfc0ad75c777e26d5a23f4cfb49 |
| d134c2c77b7a97d79ea938b8a2df6afca61f3822 | e4ef4c621cb43835254db8d9d21fb79abc18f7bd | 96d3555a4dc8d71887f386d7264b1f43f7f8af84 |
| 06643061982c31a5443752e8c3790f4873f9e703 | 222ceeb0074427fb42b7a7cb8b439fa0e17ffa59 | 48f842a8662b4ebcf9e1e3210ffe5db2a851dd4a |

`git diff --binary --full-index` SHA-256 of `server tests`:

- CARD-1502 range `066430619..28a325193` and `5b44186fc..55f998fab` both `7b3ebfa7d0fa689c0b6b41d1e9dfb0a39ce471ea2b92b5aec045704de0fa6ba1`, stable patch-id `7974ce55b0ae80310276cfb505614c6d5bc1f79f`.
- Residual `066430619..5b44186fc` and `28a325193..55f998fab` both `0a51681fe69e51961505e12fabe92b2d8ae021a8d2f9909eecfe8700b49af3a1`, stable patch-id `5beb0325481aff3dd47528d6e56a107380d692cc`.
- Shortstat: 12 files, +1281/-13, on both.

Whole-tree `git diff --binary --full-index` excluding `.antiphon/task-4965dfb4.md` is the same hash on both sides: `1ebd619c33a042c9504b0ae2db0c5f85e1afafceff54f7f4cb3158cd834d16d7` (59 files, +5592/-17). The only tip-diff path absent from the base-to-base diff is that re-cut report, added by `55f998fab` itself. No base-diff path is missing from the tip diff. The 12 server/test files are upstream content, not a CARD-1502 edit.

## Verification

Runner defaults `globalRunnerId=server2`. `GET /api/session-runners`: server2 linux `dispatchEligible=true`. This run is on that host. No `-Runner` or `-Platform` override.

Checkpoint run `20261022-214039-63be` at `55f998fab6ff5a6e3d0b204540aff638b5040847`, source clean, `buildSource=verified`, exit 0. One unlisted bootstrap build of the checkpoint tool (`bin-c1502-fr-tool`, UseAppHost=false, slot granted, 0 errors) was required to launch the tool. The tool deleted `bin-c1502/`.

| Row | Result |
|---|---|
| CP-1 | executed=10 passed=10 failed=0 skipped=0 build=ok |
| CP-3 | executed=21 passed=21 failed=0 skipped=0 build=reused |

`scripts/validate-checkpoint-receipt.ps1` on `report.json` with rows CP-1,CP-3: `CHECKPOINT SOURCE VALID` exit 0. Counts match the plan, so CP-2 was not run. Evidence: `.antiphon/checkpoints-c1502-fr/20261022-214039-63be/`.

`scripts/check-evidence-diff.ps1` over `5b44186fc..55f998fab`: commits=9 entries=2 violations=0. The two entries are the tracked report markdown files.

Throwaway mutation in detached `/tmp/c1502-mut` (discarded): the branch-elsewhere guard in `RecoverInterruptedCreationAsync` was removed. `C1502_BranchCheckedOutElsewhereRemovesNothing` on `WorktreeManagerGitIntegrationTests` failed 1/1 (expected `ConflictException`, threw `ValidationException`), exit 2. The file was restored before the worktree was removed. The review checkout was not edited. Two earlier invocations used the sibling class name `WorktreeManagerTests` and ran 0 tests (exit 8); they are not the red evidence.

## Land and RC

`git fetch origin master`. `origin/master` is `ffa98a8f76588c12df6b0437d24b541762b95f09`, which contains `0737fddc57eca08b946b57dc76447dc6f946b25e`. `git merge-tree --write-tree origin/master HEAD` exited 0, tree `2ade9ac5738fd07bcc72a607907b580c144fd38f`.

RC `origin/rc/2026-10-20` is `0714a9a89e4bff65aae7e0b4b94098694aeb931c`. A discarded detached scratch worktree cherry-picked this order with no conflicts. The five product blobs at the scratch tip match `55f998fab`. `dotnet build tests/Antiphon.Tests` (`OutputPath=bin-c1502-rc/`, `UseAppHost=false`, build slot) exited 0 with 0 errors.

Pick order, oldest first:

1. `0498c748a448f367e6153316c992585d36dea74b`
2. `a23ca2869618811619bcb0965d386ecb6e786c18`
3. `e4ef4c621cb43835254db8d9d21fb79abc18f7bd`
4. `222ceeb0074427fb42b7a7cb8b439fa0e17ffa59`
5. `173104b91ee1c3a036d39165cfbd4ba3164cdfb8`
6. `50ff34229105c93bf90d6a398dc197f95b337f38`
7. `1fbeb2c7dd80ae4f7244cce7db6d9db9eefd74b0`
8. `aad6ca7a206ef2aced9dbdb5da39de7133c2e901`
9. `d3a27554fddd9790ae1efba430e453eac438fc4f`
10. `74ca025bdc9293fcd1f3c2cc267cf8610242e398`
11. `0d7c501366c0836c740ae514b7c796bad9651d08`
12. `d1e2459b1f78d4545a5ae8b6450f1cd104687ff6`
13. `55f998fab6ff5a6e3d0b204540aff638b5040847` (re-cut report only; the product is complete at item 12)

PC-1 through PC-7 stay pending for method-scoped SourceLanding Mutation. D-3 remains deferred on the plan. No product defect.

--- review evidence ---
subjectTaskId: 4965dfb4-669d-4f39-b429-3eec8d5dda3d
reviewedSourceSha: 55f998fab6ff5a6e3d0b204540aff638b5040847
reviewedSourceClean: true
ordinaryScopeCompleted: Full
