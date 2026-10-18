# Cache Fixture npm ownership fix publication

Task: `a45ceeca-20ce-464e-a7d9-f6bac4bc7afa` (Merge), 2026-10-16.

RC and master publication succeeded. Master is now
`347b3fc16970d89dc7a8f3c6b713e69d692d85e7`, published by a non-force
fast-forward push. The initial attempt stopped after the three native cases
skipped under this session's UID/GID 1654. The caller subsequently authorized
completion using the existing same-source root-host passes identified below,
with no further native rerun here. No product failure or merge conflict was
observed.

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
methods are all `Passed`. This local attempt did not qualify native ownership
and did not demonstrate an assertion regression. No baseline failure rerun applies.

On resumption, the caller supplied existing native qualification: all three cases
passed on a root-capable host in Review `ac5a8536`, run
`20261016-132554-501d`, at reviewed source
`6ca859d006b0a3cac9a88dc6bd551c37e98b2217`; they also passed in the owner's
CP-1. The caller explicitly authorized publication using those passes and the
identical merge delta, and instructed this task not to rerun the native cases
here. This is caller-supplied prior-run provenance; this task did not independently
rerun or revalidate those receipts. The local skips and the historical CARD-1310
aggregate-receipt outcome remain unchanged. No additional build or test ran on
resumption.

Built test DLL SHA-256:
`d3ae6fb3b0f12ff25be6e1849094417c484800d1bd9b3c06e947eb68eac3d271`.
All 28 inventoried `bin-a45ceeca` output roots were removed after both test hosts
exited. Build/test logs, fresh TRX files, summary and output inventory remain
outside Git under `/work/evidence/task-a45ceeca/`.

## Published master merge and preservation proof

Old fetched and remotely observed master before publication:
`2d2b30364d1793aedd9dae725a68246e127e7dd0`.
A clean detached worktree at `/work/evidence/task-a45ceeca/master` ran:

```sh
git merge --no-ff origin/rc/2026-10-15 -m 'Merge rc/2026-10-15 into master (cache Fixture npm ownership fix)'
```

The merge completed without conflicts or resolved hunks. Published merge:
`347b3fc16970d89dc7a8f3c6b713e69d692d85e7`.
Its first parent is the old master above and its second parent is
`6ca859d006b0a3cac9a88dc6bd551c37e98b2217`.

The binary diff from old master to this merge is byte-for-byte identical to the
binary RC-base-to-reviewed-source diff, as verified with `cmp`. Both patches have
SHA-256 `f96868a31aa933c6364f2b0045559613066fb84e80a9bc4fed17ed062dc3af98`.
There is no additional change or revert of master-only work, including
CARD-0987 S1/S2 and CARD-1313. The merge worktree is clean.

The initial attempt left this commit unpublished because the native selection
could not execute locally. Following the caller's explicit resumption
authorization, `git fetch origin` and fresh remote observations confirmed master
was still the old master above and RC was still the reviewed source above.
Both parent identities, fast-forward ancestry, exact binary-delta equality and
`git diff --check` were checked again before publication. This command succeeded
without force:

```sh
git push origin 347b3fc16970d89dc7a8f3c6b713e69d692d85e7:refs/heads/master
```

A subsequent fetch and `git ls-remote` confirmed:

- `refs/heads/master`: `347b3fc16970d89dc7a8f3c6b713e69d692d85e7`
- First parent: `2d2b30364d1793aedd9dae725a68246e127e7dd0` (old master)
- Second parent: `6ca859d006b0a3cac9a88dc6bd551c37e98b2217` (published RC)
- `refs/heads/rc/2026-10-15`: `6ca859d006b0a3cac9a88dc6bd551c37e98b2217`

The commit is also retained in the verified incremental bundle
`/work/evidence/task-a45ceeca/prepared-master.bundle`; its prerequisites are the
old master and RC base above. This generated evidence is not committed.
No tags, canonical checkout, AppHost or runners were changed. Publication is
complete; no activation or deployment was requested.

--- next stage ---
next: none
handoff: RC and master publication complete; caller-supplied root-host qualification accepted under explicit authorization. No remaining publication work.
artifact: docs/investigations/2026-10-16-fixture-fix-publication.md
