# CARD-1303 R4 publication to rc/2026-10-15 and master

Date: 2026-10-16. Task: `f2731007`. Branch for this note only: `feat/card-task-f2731007`.
Manual publication. Normal land stays refused by the known CARD-1310 receipt defect, and the
approving Review (`ff722349`) was Interim. Operator rule for this flow: merge onto
`rc/2026-10-15` with `git merge --no-ff` (no cherry-pick), then merge that RC into master
with `git merge --no-ff`. Both pushes are fast-forwards. Tags, the canonical checkout,
AppHost, and runners were not touched. The staged server2 rollout that is running against
`e02719a9fca650c3f5415e57a090d720deb36597` is unaffected by moving the RC ref.

## Outcome

Published. No conflicts. The diff each merge introduces is the four-file R4 change only.

## Source range

`origin/feat/card-task-7f270588` at `6aaee1f83006e642841a97c0667c1ec251970d15`.
Base `5e9926ae96741e0a7c144d5dff6ec30123dfa772`.
Parents of the tip: `c536d5bb8bea147bf7b49d0b47b214d351f95af0` (test), then
`6aaee1f83006e642841a97c0667c1ec251970d15` (fix).

`git diff --name-only 5e9926ae96741e0a7c144d5dff6ec30123dfa772..6aaee1f83006e642841a97c0667c1ec251970d15`:

- `server/Application/Services/AgentTaskService.cs`
- `tests/Antiphon.Tests/Application/PipelineSpeedIntegrationTests.Admission.cs`
- `docs/antiphon-api.md`
- `docs/ops-http.md`

No other paths. `177` insertions, `7` deletions. Admission now decides from the locked card
when a sweep moves an open column (Backlog, In Progress, Review) or the active workflow run
changes. Board, identifier, platform, either verification policy, archive, and a leave from
those open columns to NeedsDecision still return `card_admission_changed`.

## Part A — rc/2026-10-15

Prior tip `e02719a9fca650c3f5415e57a090d720deb36597` (parents
`5e9926ae96741e0a7c144d5dff6ec30123dfa772` and
`8c0001ce68a63146d04242b0dce0d2cee74105b5`). Clean detached worktree. `git merge --no-ff`
of `6aaee1f83006e642841a97c0667c1ec251970d15` completed with the ort strategy and no
conflict.

- Merge: `b0e47786c5de4d612ee3a0a7196bd89d40e2a48a`
- First parent: `e02719a9fca650c3f5415e57a090d720deb36597`
- Second parent: `6aaee1f83006e642841a97c0667c1ec251970d15`
- Tree: `d7ecad11901d6d73ad9e125faca11e017a7f230b`
- Message: `Merge CARD-1303 R4 (admission decides from the locked card) into rc/2026-10-15`

`git merge-tree --write-tree` of the old RC tip and the R4 tip produced that same tree
before the merge commit was created.

Build, from that worktree, one isolated build, slot granted, waited 0s, lease
`15ef1f22-05ec-416e-ade8-f95a2c3ff2d9`, `UseAppHost=false`, `OutputPath=bin-r4pub/`:
`dotnet build tests/Antiphon.Tests` exit 0, 0 errors. The run passed
`--property:UseAppHost=false` as well so `dotnet run --no-build` executed the dll.
Filter `/*/*/PipelineSpeedIntegrationTests/(C1289*)|(C1303*)|(C1281_B3*)`. Slot granted,
waited 0s, lease `c4e7c077-da25-43a5-b049-3ff77f86d28f`. TRX counters: total 15, executed
15, passed 15, failed 0, skipped 0. Duration 1m 17s. Every result outcome was Passed:

- `C1303_CardAdmissionAllowsClaimSessionInsert`
- `C1289_ConcurrentCardEdit` (title, description, owner, token, state, archived, platform, policy)
- `C1289_GitLockDuration` (False, True)
- `C1289_CardLockOrder`
- `C1303_R4_ConcurrentCodeCreatesSurviveTransitionSweep`
- `C1281_B3AdmissionFence`
- `C1281_B3Schema`

Generated TRX stayed in the temporary worktree and was not committed. `bin-r4pub/`
directories were removed after the run.

Remote `refs/heads/rc/2026-10-15` was still `e02719a9fca650c3f5415e57a090d720deb36597`
at push time. Fast-forward push `e02719a9f..b0e47786c`. `git ls-remote` after the push
returned `b0e47786c5de4d612ee3a0a7196bd89d40e2a48a`.

## Part B — master

Prior tip `4020c4914863f72305692977614fcde5a513139f` (subject `docs: record CARD-1314
Windows RC2 gate2 duration buckets`). That commit already contained the old RC tip, so
master was 151 commits ahead of `e02719a9` and the four R4 paths were byte-identical to
the old RC tip. Clean detached worktree. `git merge --no-ff origin/rc/2026-10-15`
completed with the ort strategy and no conflict.

- Merge: `473ba2133d0911aab481bc1354d81a6a04c55f60`
- First parent: `4020c4914863f72305692977614fcde5a513139f`
- Second parent: `b0e47786c5de4d612ee3a0a7196bd89d40e2a48a`
- Tree: `6fcab8a5e432c612179921c1fb7058170213f07c`
- Message: `Merge rc/2026-10-15 into master (CARD-1303 R4)`

`git diff --name-only HEAD^1 HEAD` is the same four paths and the same 177/7 stat as the
R4 range. The four blobs equal the R4 tip (`1646c9b4`, `a5810b8a`, `0d8bea09`,
`0e7581a4`). No CARD-0987 or CARD-1313 path is in that diff. Remote master was still
`4020c4914863f72305692977614fcde5a513139f` at push time. Fast-forward push
`4020c4914..473ba2133`. After fetch, `origin/master` is
`473ba2133d0911aab481bc1354d81a6a04c55f60`, first parent the old master tip, second
parent the new RC tip.

## Not done, noted

SourceLanding Mutation was not run. AppHost and runners were not restarted. This note is
only on `feat/card-task-f2731007`.

--- next stage ---
next: none
handoff: CARD-1303 R4 is on rc/2026-10-15 at b0e47786c5de4d612ee3a0a7196bd89d40e2a48a and on master at 473ba2133d0911aab481bc1354d81a6a04c55f60. Leave the server2 rollout on e02719a9. Normal land remains refused by CARD-1310.
artifact: docs/investigations/2026-10-16-r4-publication.md
