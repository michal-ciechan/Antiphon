# Seed cold-marker reuse publication to rc/2026-10-15 and master

Date: 2026-10-16. Task: `fb1e053a`. Branch for this note only: `feat/card-task-fb1e053a`.
Manual publication. Normal land stays refused by the known CARD-1310 receipt defect.
The approving Review named in the brief is fable `9ade1c2b`, Full scope. Operator rule
for this flow: merge onto `rc/2026-10-15` with `git merge --no-ff` (no cherry-pick),
then merge that RC into master with `git merge --no-ff`. Both pushes are fast-forwards.
Tags, the canonical checkout, AppHost, and runners were not touched.

## Outcome

Published. No conflicts. Each merge's diff against its first parent is the two-file
cold-marker reuse change only. Master-only work, including CARD-0987 and CARD-1313,
is absent from that diff.

## Source range

`origin/feat/card-task-db7814eb` at `6f371e490052ca4bdc436c862e64e33c4a8ad2a2`.
Base `e02719a9fca650c3f5415e57a090d720deb36597`. Five commits:

- `f61b85134fd8ccaa6f2f1ddd13d2ecfc6145e47d` test: reuse of a cold cache marker must leave gate receipts
- `f96737552b251671b9541425764e4b77dde5b737` fix: write cold seed status receipts on marker reuse
- `b296255e9345ccb9855377e3c8e885e447ed40ae` test: require cold reuse receipts to be compact json plus one newline
- `bc70a2e8e663232aa71e2ea8891e66ee77ca7231` test: require cold reuse to accept a deploy-temp hold
- `6f371e490052ca4bdc436c862e64e33c4a8ad2a2` fix: accept a deploy-temp hold when reusing a cold cache marker

`git diff --name-only e02719a9fca650c3f5415e57a090d720deb36597..6f371e490052ca4bdc436c862e64e33c4a8ad2a2`:

- `scripts/c590-remote.sh`
- `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`

No other paths. No `docs/plan/mutation` evidence notes. `171` insertions, `4` deletions.
Cold marker reuse now writes compact `status.json` and `main-status.json` receipts.
The temp gate accepts a retired runner (`retiredAt` set and `retireWhenIdle` true) or
a deploy-temp hold (`retiredAt` null and `retireWhenIdle` false), and still requires
the temp runner unavailable, ineligible, not accepting work, draining, redirected to
`server2`, and idle. Blobs at the published tips: script `8f7a8613009b579a5c3e2523147355268ef8cebb`,
tests `84dbe69c1075f5ddcaaefb257a88d8d1e13b6ee2`.

## Part A — rc/2026-10-15

Prior tip `b0e47786c5de4d612ee3a0a7196bd89d40e2a48a` (parents
`e02719a9fca650c3f5415e57a090d720deb36597` and
`6aaee1f83006e642841a97c0667c1ec251970d15`). Clean detached worktree. `git merge --no-ff`
of `6f371e490052ca4bdc436c862e64e33c4a8ad2a2` completed with the ort strategy and no
conflict.

- Merge: `09e48c5d6f1d404cfea8ec0d992270424fd2a184`
- First parent: `b0e47786c5de4d612ee3a0a7196bd89d40e2a48a`
- Second parent: `6f371e490052ca4bdc436c862e64e33c4a8ad2a2`
- Tree: `949a2dc009b53baf27e0e42537f0a76ddd1ac324`
- Message: `Merge Seed cold-marker reuse receipts and deploy-temp hold predicate into rc/2026-10-15`

`git merge-tree --write-tree` of the old RC tip and the source tip produced that same
tree before the merge commit was created.

Build, from that worktree, one isolated build of commit `09e48c5d6f1d404cfea8ec0d992270424fd2a184`,
slot granted, waited 0s, lease `deb1e61a-f619-4b9e-9b23-237ad03ef3ce`, `maxcpucount=6`,
`UseAppHost=false`, `OutputPath=bin-coldreuse/`: `dotnet build tests/Antiphon.Tests`
exit 0, 0 errors. The run passed `--property:UseAppHost=false` as well so
`dotnet run --no-build` executed the dll. Filter
`/*/*/RemoteScriptContractTests/(C849_Cold_marker_reuse_writes_gate_receipts*)|(C0995_Cold_ready_seed_refuses_a_temp_container*)`.
Slot granted, waited 0s, lease `e98b0f9d-bd21-477e-8128-732bb04021fe`, held 5s.
TRX counters: total 2, executed 2, passed 2, failed 0, skipped 0. Duration 1s 592ms.
Both results Passed:

- `C849_Cold_marker_reuse_writes_gate_receipts`
- `C0995_Cold_ready_seed_refuses_a_temp_container`

Generated TRX stayed in the temporary worktree and was not committed. `bin-coldreuse/`
directories were removed after the run.

Remote `refs/heads/rc/2026-10-15` was still `b0e47786c5de4d612ee3a0a7196bd89d40e2a48a`
at push time. Fast-forward push `b0e47786c..09e48c5d6`. `git ls-remote` after the push
returned `09e48c5d6f1d404cfea8ec0d992270424fd2a184`.

## Part B — master

Prior tip `3b28039d5c5a9da76f811446aa1112c4647ab822` (subject `docs(selection): record
the S4-S5 code report`). That tip already contained the old RC tip. Clean detached
worktree. `git merge --no-ff origin/rc/2026-10-15` completed with the ort strategy
and no conflict. `git diff --name-only HEAD^1 HEAD` is the same two paths and the same
171/4 stat as the source range. The two blobs equal the source tip. No CARD-0987 or
CARD-1313 path is in that diff.

- Merge: `8dda7cdcfa0777ce45399ec7f42c5f70441533b9`
- First parent: `3b28039d5c5a9da76f811446aa1112c4647ab822`
- Second parent: `09e48c5d6f1d404cfea8ec0d992270424fd2a184`
- Tree: `fa71f89504bfcdfa1fc822f7a8e38f2dc47c8d4b`
- Message: `Merge rc/2026-10-15 into master (Seed cold-marker reuse repair)`

Remote master was still `3b28039d5c5a9da76f811446aa1112c4647ab822` at push time.
Fast-forward push `3b28039d5..8dda7cdcf`. After fetch, `origin/master` is
`8dda7cdcfa0777ce45399ec7f42c5f70441533b9`, first parent the old master tip, second
parent the new RC tip.

## Not done, noted

SourceLanding Mutation was not run. AppHost and runners were not restarted. This note is
only on `feat/card-task-fb1e053a`.

--- next stage ---
next: none
handoff: Seed cold-marker reuse is on rc/2026-10-15 at 09e48c5d6f1d404cfea8ec0d992270424fd2a184 and on master at 8dda7cdcfa0777ce45399ec7f42c5f70441533b9. Normal land remains refused by CARD-1310. AppHost and runners were not touched.
artifact: docs/investigations/2026-10-16-cold-reuse-publication.md
