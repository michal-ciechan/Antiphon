# RC merge into master, 2026-10-16

Published `rc/2026-10-15` into master with merge commit
`2d2b30364d1793aedd9dae725a68246e127e7dd0`. The merge had no conflicts and
introduced no content changes: the complete resulting tree equals the old
master tree. All RC content was already present on master.

## Source and publication identity

| Identity | Full SHA |
| --- | --- |
| Task dispatch base | `6e23e0b4c8d6b2ae12218c2c0058593dbf773530` |
| Old master; merge first parent | `62adf992b0058a4df72bd05adddfbad7d58d5760` |
| RC tip; merge second parent | `b7265a05aa8773f08dbf457b93c7561e7874822d` |
| Published merge | `2d2b30364d1793aedd9dae725a68246e127e7dd0` |
| Old master and merged tree | `eaa1f86f0da4d128a5a221081dadbf480418abcd` |

The assigned checkout remained on `feat/card-task-20d69842`. Its clean tip was
fast-forwarded to current master before merging, providing the requested clean
master content without switching branches. No rebase, cherry-pick, reset,
amendment or force push was used.

Fetched with `git fetch origin`, followed by
`git fetch --refetch --no-filter origin`. The repository is not shallow.
`git rev-list --objects --all --missing=print` reported zero missing objects.
`git ls-remote` confirmed both source tips before merging and again before
publication. The RC tip still equaled the dispatch SHA, satisfying the source
ancestry condition.

The merge was prepared with `git merge --no-ff --no-commit
origin/rc/2026-10-15`, inspected, and committed with the exact requested message:
`Merge rc/2026-10-15 into master (RC fixes, release line)`.

## Conflicts and preservation of master-only work

Resolved conflict hunks: **none**. Git automatically combined
`HostCleanupCheckpointFixture.cs` and `HostCleanupCheckpointReceiptTests.cs`
without conflicts; neither produced a change relative to master. No manual
resolution or source edit was necessary.

Before publication, `git diff --exit-code origin/master HEAD` returned zero with
no output, and the tree IDs matched. Thus there were zero added, modified or
deleted paths relative to old master. This proves preservation of every
master-only change, including CARD-0987 S1/S2 and any CARD-1310 work present at
the old master tip. There was no remaining RC-only content delta to add.

## Required build verification

Both builds verified committed merge
`2d2b30364d1793aedd9dae725a68246e127e7dd0`, whose source tree was clean throughout.
The test projects changed between the branches' merge base and the RC tip were
`Antiphon.Tests` and `Antiphon.E2E`; each was built once, sequentially.

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-merge20d69842-tests -SlotWaitMinutes 10 -- dotnet build tests/Antiphon.Tests --property:OutputPath=bin-merge20d69842/ --property:UseAppHost=false --nologo
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-merge20d69842-e2e -SlotWaitMinutes 10 -- dotnet build tests/Antiphon.E2E --property:OutputPath=bin-merge20d69842/ --property:UseAppHost=false --nologo
```

| Project | Build exit | Errors | Warnings | MSBuild elapsed |
| --- | --- | --- | --- | --- |
| Antiphon.Tests | 0 | 0 | 1,064 | 2m07.26s |
| Antiphon.E2E | 0 | 0 | 5 | 14.82s |

Both host slots were granted immediately at `maxcpucount=6` and released normally:
`8253cb43-6ba4-4a02-8b20-f92bb709a913` (held 128s) and
`10ff8efa-ffa6-4050-9ec2-4aaa07e2047e` (held 16s).
Tests executed: **0**; this pass's requested verification was compilation.
These builds do not establish runtime or release qualification.

Ignored local evidence under `.antiphon/merge-20d69842/` contains
`build-tests.log`, `build-e2e.log` and `build-output-inventory.txt`.
All 30 task-owned `bin-merge20d69842` output directories were inventoried and
removed after both build processes exited; a final scan found zero remaining.

## Publication receipt and report custody

Published the verified merge to the assigned task branch, then fast-forwarded
master with `git push origin HEAD:master`. Both pushes succeeded.
Subsequent `git ls-remote origin` returned:

```text
2d2b30364d1793aedd9dae725a68246e127e7dd0 refs/heads/feat/card-task-20d69842
2d2b30364d1793aedd9dae725a68246e127e7dd0 refs/heads/master
b7265a05aa8773f08dbf457b93c7561e7874822d refs/heads/rc/2026-10-15
```

Fetched the published master and inspected its commit: its first parent was
`62adf992b0058a4df72bd05adddfbad7d58d5760` and its second parent was
`b7265a05aa8773f08dbf457b93c7561e7874822d`, exactly as required.

This report is a subsequent task-branch-only documentation commit. It is not
part of the master merge. RC, tags, the canonical checkout, AppHost and runners
were not modified. Publication is confirmed; service activation was outside
this brief.

--- next stage ---
next: none
handoff: Merge complete; caller may record the confirmed master publication.
artifact: docs/investigations/2026-10-16-rc-merge-to-master.md
