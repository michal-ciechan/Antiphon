# Cache Fixture npm ownership fix publication

Task: `a45ceeca-20ce-464e-a7d9-f6bac4bc7afa` (Merge), 2026-10-16.

RC publication succeeded. Master publication is blocked by the required native
ownership rerun: this Linux session runs as UID/GID 1654, so all three native
cases explicitly skip. Passwordless general sudo is unavailable. No product
failure or merge conflict was observed. The prepared master merge remains local.

## Source and RC publication

The caller supplied Grok Review `ac5a8536`, Clean/Full, for exact source
`6ca859d006b0a3cac9a88dc6bd551c37e98b2217`. The known CARD-1310 aggregate
cleanup-receipt refusal was the caller's explicit reason to authorize this manual
publication. This report does not repair or relabel that historical receipt.

`git fetch origin` succeeded and all required objects were local. Both the fetched
RC ref and an immediate remote observation were exactly
`b7265a05aa8773f08dbf457b93c7561e7874822d` before publication.
`git merge-base --is-ancestor` proved that base is an ancestor of the reviewed SHA.

The seven commits in `git log --stat b7265a05..6ca859d0` touch exactly:

- `scripts/c590-remote.sh`
- `scripts/fixtures/c913-seed-contract.sh`
- `tests/Antiphon.Tests/Scripts/CacheSeedNpmOwnershipTests.cs`
- `tests/Antiphon.Tests/Scripts/Fixtures/cache-seed-npm-ownership.sh`
- `docs/docker-stack.md` (six lines explaining this rollout ownership failure)
- `docs/superpowers/plans/2026-10-16-cache-fixture-npm-ownership.md`
- `docs/superpowers/mutations/2026-10-16-cache-fixture-npm-ownership.md`

No server, client, migration or unrelated source was present. The literal host
verifier command and its checked failure handling remain in the host branch;
volume verification uses the existing writer helper. `git diff --check` passed.

The following non-force push succeeded as a fast-forward:

```sh
git push origin 6ca859d006b0a3cac9a88dc6bd551c37e98b2217:refs/heads/rc/2026-10-15
```

## Exact verification

One isolated build and two narrow test invocations ran sequentially against a
clean detached checkout of `6ca859d006b0a3cac9a88dc6bd551c37e98b2217` at
`/work/evidence/task-a45ceeca/rc`. Each invocation obtained and released a host
build slot; none ran unleased. No wider test selection ran.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label a45ceeca-fixture-build -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-a45ceeca/ --property:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label a45ceeca-native -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-a45ceeca/ --property:UseAppHost=false -- --treenode-filter '/*/*/CacheSeedNpmOwnershipTests/*' --report-trx --report-trx-filename native.trx --results-directory /work/evidence/task-a45ceeca/native-results
pwsh -NoProfile -File scripts/build-slot.ps1 -Label a45ceeca-adjacent -- dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-a45ceeca/ --property:UseAppHost=false -- --treenode-filter '/*/*/RemoteScriptContractTests/(C1192_Real_cache_cases_keep_the_host_payload_backend*)|(C1192_Fixture_seed_and_rerun_keep_payloads_in_volume*)|(C849_Seed_refuses_invalid_donors_and_partial_payloads*)|(C1195_Seed_import_chmod_restores_volume_root*)' --report-trx --report-trx-filename adjacent.trx --results-directory /work/evidence/task-a45ceeca/adjacent-results
```

| Selection | Selected | Passed | Failed | Skipped | Exit |
|---|---:|---:|---:|---:|---:|
| Build (142 seconds) | — | — | 0 errors | — | 0 |
| Native ownership | 3 | 0 | 0 | 3 | 8 |
| Adjacent contracts | 4 | 4 | 0 | 0 | 0 |

Build reported 1,060 warnings, including compiler/analyzer diagnostics. These
were not investigated in this narrow publication task. Native skip message:
"Native UID ownership proof requires Linux root or Windows with WSL root;
non-root execution cannot qualify it."

Fresh TRX identities were inspected: the native success method and its verify
and cleanup argument cases are all `NotExecuted`; the four named adjacent
methods are all `Passed`. This is incomplete native verification, not green
qualification and not an assertion regression. No baseline failure rerun applies.

Built test DLL SHA-256:
`d3ae6fb3b0f12ff25be6e1849094417c484800d1bd9b3c06e947eb68eac3d271`.
All 28 inventoried `bin-a45ceeca` output roots were removed after both test hosts
exited. Build/test logs, fresh TRX files, summary and output inventory remain
outside Git under `/work/evidence/task-a45ceeca/`.

## Prepared master merge and preservation proof

Current fetched and remotely observed master:
`2d2b30364d1793aedd9dae725a68246e127e7dd0`.
A clean detached worktree at `/work/evidence/task-a45ceeca/master` ran:

```sh
git merge --no-ff origin/rc/2026-10-15 -m 'Merge rc/2026-10-15 into master (cache Fixture npm ownership fix)'
```

The merge completed without conflicts or resolved hunks. Prepared merge:
`347b3fc16970d89dc7a8f3c6b713e69d692d85e7`.
Its first parent is the old master above and its second parent is
`6ca859d006b0a3cac9a88dc6bd551c37e98b2217`.

The binary diff from old master to this merge is byte-for-byte identical to the
binary RC-base-to-reviewed-source diff, as verified with `cmp`. Both patches have
SHA-256 `f96868a31aa933c6364f2b0045559613066fb84e80a9bc4fed17ed062dc3af98`.
There is no additional change or revert of master-only work, including
CARD-0987 S1/S2 and CARD-1313. The merge worktree is clean.

The prepared commit was **not pushed to master** because the mandatory native
selection could not execute. It is also retained in the verified incremental
bundle `/work/evidence/task-a45ceeca/prepared-master.bundle`; its prerequisites
are the old master and RC base above. This generated evidence is not committed.
No tags, canonical checkout, AppHost or runners were changed.

## Completion required

Run only the three native ownership cases at the exact reviewed source on Linux
root or Windows with WSL root. The four adjacent passes above already cover the
same source. Require three passes with no skips. Recheck both remote tips before
publishing master; if master changed, prepare a new merge from current master and
repeat the preservation/parent checks. Use a non-force fast-forward push, then
verify the published commit's first parent is its old master and its second
parent is the published RC tip. No source repair is indicated by this run.

--- next stage ---
next: decide
handoff: Provide root-capable native verification for the three skipped cases, then finish master publication under the original merge authorization.
artifact: docs/investigations/2026-10-16-fixture-fix-publication.md
