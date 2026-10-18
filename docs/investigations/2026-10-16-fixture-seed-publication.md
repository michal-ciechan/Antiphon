# Server2 rollout Fixture and Seed publication

Manual publication because the normal land path is refused by the known CARD-1310 receipt defect. Publication used merge commits only, then fast-forward pushes. Tags, the canonical checkout, AppHost, and runners stayed untouched. The canonical checkout stayed at `c6cee79f88d82710515106fb0228f817219871cc`.

## Tips before publication

| Ref | SHA |
|---|---|
| `origin/rc/2026-10-15` | `5e9926ae96741e0a7c144d5dff6ec30123dfa772` |
| Fixture completion `8e1d2c22` (`origin/feat/card-task-13dee490`) | `8e1d2c22633cb7916a0a81ebc8a8e4e91a8dd518` |
| Seed tip `origin/feat/card-task-efc5b7be` | `8c0001ce68a63146d04242b0dce0d2cee74105b5` |
| Shared base | `6ca859d006b0a3cac9a88dc6bd551c37e98b2217` |
| `origin/master` | `3eaf10c78ff3189e38b0595128e62abad89959fe` |

`origin/rc/2026-10-15` was exactly `5e9926ae96741e0a7c144d5dff6ec30123dfa772`, so it descends from that commit. `6ca859d006b0a3cac9a88dc6bd551c37e98b2217` is an ancestor of that RC tip and is the merge-base of the RC tip with `8c0001ce68a63146d04242b0dce0d2cee74105b5`. The Seed tip is five commits on top of the Fixture completion tip. Operator-supplied reviews: `7d035f5c` clean for the eleven commits through `8e1d2c22`, and `2d91458c` clean for the five Seed commits at reviewed SHA `8c0001ce68a63146d04242b0dce0d2cee74105b5` (the `review_evidence_subject_tip_mismatch` warning is the owner-subject mismatch and was ignored).

`origin/master` already contained the old RC tip (148 commits ahead, nothing on RC that master lacked).

## File range

`6ca859d0..8c0001ce` touches only rollout and fixture scripts, their model tests, and the plan, evidence, and mutation notes:

- `scripts/c590-real.ps1`
- `scripts/c590-remote.sh`
- `scripts/fixtures/c1192-workspace-contract.sh`
- `scripts/fixtures/c913-seed-contract.sh`
- `scripts/fixtures/cache-bridge-diagnostics.ps1`
- `scripts/fixtures/cache-fixture-completion.sh`
- `tests/Antiphon.Tests/Scripts/CacheFixtureCompletionTests.cs`
- `docs/superpowers/plans/2026-10-16-fixture-completion-repair.md`
- `docs/superpowers/plans/2026-10-16-fixture-completion-repair-evidence.md`
- `docs/superpowers/mutations/2026-10-16-fixture-completion-repair.md`

`tests/Antiphon.Tests/Scripts/CacheSeedNpmOwnershipTests.cs` already existed at the merge-base and is unchanged in this range. Server code, client code, migrations, and the R12/R14 files are absent from the range. Master had no delta on any of these paths relative to the old RC tip.

## Merge commits

No conflicts on either merge. `git merge-tree --write-tree` was clean before each merge.

| Commit | Parents | Subject |
|---|---|---|
| `e02719a9fca650c3f5415e57a090d720deb36597` | `5e9926ae96741e0a7c144d5dff6ec30123dfa772` `8c0001ce68a63146d04242b0dce0d2cee74105b5` | Merge server2 rollout Fixture completion repair and Seed fail-closed hardening into rc/2026-10-15 |
| `60e946ee65845277d4a71c6991486d04336a44dc` | `3eaf10c78ff3189e38b0595128e62abad89959fe` `e02719a9fca650c3f5415e57a090d720deb36597` | Merge rc/2026-10-15 into master (rollout Fixture/Seed repair) |

`origin/rc/2026-10-15` fast-forwarded `5e9926ae9..e02719a9f`. `origin/master` fast-forwarded `3eaf10c78..60e946ee6`. The master merge's first parent is the old master tip and its second parent is the new RC tip. The diff of `60e946ee6` against `3eaf10c78` is only the ten files listed above, so CARD-0987 S1/S2 and CARD-1313 stay on the first-parent history.

## Verification at `e02719a9fca650c3f5415e57a090d720deb36597`

Build slot `94b9090b-6cad-48b5-b87c-5e8826027691` (waited 0s, maxcpucount 6): `dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-c49b35eb1/ --property:UseAppHost=false -nodeReuse:false`. Exit 0. 0 errors, 1060 warnings. Elapsed 00:02:24.76.

Test slot `0644c279-b86e-4813-81c3-980292cb10db` (waited 0s, maxcpucount 6), `dotnet run --project tests/Antiphon.Tests --no-build` with the same OutputPath and UseAppHost, filter `/*/*/(CacheFixtureCompletionTests*)|(CacheSeedNpmOwnershipTests*)/*`. Exit 0. 14 passed, 0 failed, 3 skipped (45s). The 14 passes are every `CacheFixtureCompletionTests` case. The 3 skips are every `CacheSeedNpmOwnershipTests` case (`Volume_seed_verifies_as_writer_and_removes_npm_logs`, `Volume_seed_failure_cleans_stage_and_never_accepts(verify)`, `Volume_seed_failure_cleans_stage_and_never_accepts(cleanup)`): this Linux runner is not root, and each skip says native UID ownership proof requires Linux root or Windows with WSL root.

--- next stage ---
next: decide
handoff: Fixture completion repair and Seed fail-closed hardening are on origin/rc/2026-10-15 at e02719a9fca650c3f5415e57a090d720deb36597 and on origin/master at 60e946ee65845277d4a71c6991486d04336a44dc (first parent 3eaf10c78ff3189e38b0595128e62abad89959fe, second parent the new RC tip). No conflict. Do not land these branches again; CARD-1310 still refuses the normal land path. The three CacheSeedNpmOwnershipTests cases skipped on this non-root Linux runner. Do not restart AppHost or runners from this pass.
artifact: docs/investigations/2026-10-16-fixture-seed-publication.md
