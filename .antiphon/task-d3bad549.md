# CARD-1506 re-cut qualification record

Landing owner: d3bad549-cfcf-444f-aa3a-4f6202c752ac. The earlier owner
84f61b86-39be-4636-8edf-537143fae95b remains Blocked; its branch is untouched.
Branch: feat/card-task-d3bad549. Worktree: /work/worktrees/task-d3bad549.
Task base: 5b44186fc19c6a8612e7b17d1647bdd2fa89679f.
Reviewed source: ab1ea6da5bb1ddbd0cf67c0f95d54bfb25fdce01, Review a773cf3a Clean.
Plan: docs/superpowers/plans/2026-10-22-card-1506-no-resident-compiler-plan.md.

The five original commits were cherry-picked with -x in order:

| Original | Re-cut |
|---|---|
| 6ff649420124da89f5d0323cc0b6fb724e421c2f | c54fe8359134c93e64b8788633352c06cf3d27b7 |
| cdfb784a8df80cd77937dd56f5b47612595479be | f352f2928a3cfbdabd0517e3d00c5bad437ecfce |
| 5f3b39b942632479e0729bf4f758f876f379fe6e | 6c7b23086dd57f61bea7baec8db3e5a01160c6fc |
| d0fc70acffa6bf2635357f5dcac5a1e8d71c7c1a | 62774641cf4d233679ec941a93f49f59c1ff0c16 |
| ab1ea6da5bb1ddbd0cf67c0f95d54bfb25fdce01 | 70deef7d177dcbfb8d139abadf84c2dfcbdd2b1c |

Only the two fingerprint commits conflicted; both retained master's manifest.
The first became an empty provenance commit. There is no merge commit or rewrite.
The generated manifest update is d1cc7c555a2610a6446f6118fe8bb8e037accccd.
The real-repository audit's -Update computed
637CFA8577440D255076CBF5CBA40BECA0C3BC8F738705A5536A35182DAD9C3E, count 58.
Only the digest changed from the assigned master manifest; no digest was hand-edited.

Equivalence: every reviewed CARD-1506 path except docs/testing-and-build.md is
byte-identical to the reviewed tip. That shared document retains one independently
landed CARD-1493 paragraph (diff stat: 1 file, 1 insertion, 1 deletion).
Reverse-applying the entire reviewed delta in a temporary index at 70deef7d1
reproduced the exact task-base tree cb10977ef2b7319874a789599c45f49b45be2e25.
Thus the CARD-1506 delta is unchanged and all master additions are preserved.
The literal full-repository diff excluding the manifest is not empty: before this
report it was 60 files, 5419 insertions, 19 deletions, all inherited master changes.
The full-tree equality wording cannot hold simultaneously with preserving master.

The five replayed commits each applied cleanly, in order, in a temporary index on
rc/2026-10-20 at 0714a9a89e4bff65aae7e0b4b94098694aeb931c, giving tree
913a3bb51743da8b7438d7a02d7dff451595d2b7. A temporary detached object inventory
of that exact tree ran its own audit script with -Update then -Check, both exit 0:
A1EBB3D0B4C0DF9BB31E417D0AFD7607CDC4BD5A404DACE986CC7CD8EC274DD6, count 57.
The master-specific generated-manifest commit has one conflict on rc, confined
to that manifest. RC must regenerate its manifest rather than copy master's value.
No rc ref or live Windows qualification was changed.

Final verification is deliberately pending at this report commit. Freeze this
tracked record before running CP-1 on its final pushed HEAD. The exact final
receipt, source validation, output counts, evidence guard and fresh merge-tree
results will be saved in ignored .antiphon/reports/task-d3bad549-final.md and in
the caller report; this file will not be edited after qualification.

Per the explicit re-cut brief, run only CP-1 via the checkpoint tool plus the
land-graph -Check and repository checks, with no Unit lane or server rebuild.
One isolated checkpoint-tool bootstrap is necessary to execute the row and will
take the build-slot gate; delete its bin-card1506-recut-tool directories afterward.
CP-1 covers V-1, V-2, R-1 and R-2 (six child scenarios plus four direct policy
cases / 24 setting assertions). Command rows have no TRX; inspect their console
roster and validate the structured report's source evidence. V-3's consumer builds
and adapter inspection were completed by the original Clean Review; this re-cut
does not rerun the server build. V-4 is the final evidence guard, master merge-tree,
and rc application/audit above. Do not repeat after green.

Pending SourceLanding Mutation: PC-1 has six variants, UseSharedCompilation,
DOTNET_gcServer, DOTNET_gcConcurrent, DOTNET_CLI_USE_MSBUILD_SERVER,
MSBUILDDISABLENODEREUSE, DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER; PC-2 Windows
guard; PC-3 exact opt-out; PC-4 wrapper adapter. None is discharged here.
Unbound candidate: docs/superpowers/mutations/2026-10-22-card-1506-no-resident-compiler-candidate.md.
Its historical owner remains unchanged for equivalence; bind future Mutation to
this task's confirmed landing. Native Windows memory/process acceptance remains
with the live rc qualification. Restart: none. Next: review.
