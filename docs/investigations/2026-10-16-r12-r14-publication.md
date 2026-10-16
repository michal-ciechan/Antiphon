# CARD-1303 R12 and R14 publication

Manual publication because the normal land path is refused by the known CARD-1310 receipt defect. No cherry-pick. No force-push. Tags, the canonical checkout, AppHost, and runners were not touched. The canonical checkout stayed at `c6cee79f88d82710515106fb0228f817219871cc`.

## Tips before publication

| Ref | SHA |
|---|---|
| `origin/rc/2026-10-15` | `6ca859d006b0a3cac9a88dc6bd551c37e98b2217` |
| R12 `origin/feat/card-task-19ca257f` | `69e4e1e5e6f9c6a9d39fa26ef025561567fcdea9` |
| R14 `origin/feat/card-task-f723b215` | `681a7ca7cfd90c6e26a308df03b95a77cd23314d` |
| `origin/master` | `347b3fc16970d89dc7a8f3c6b713e69d692d85e7` |

Both feature tips contain the RC tip. `origin/master` already contained that RC tip (128 commits ahead, nothing on RC that master lacked).

## File ranges

`6ca859d0..69e4e1e5` (R12) is the retained-evidence refusal detail, its three-argument regression, and the plan/mutation notes, plus the two HTTP docs that already own that refusal:

- `server/Infrastructure/Verification/CheckpointReceiptValidator.cs`
- `tests/Antiphon.Tests/Application/AdvisoryChangePolicyTests.Correction.cs`
- `docs/antiphon-api.md`
- `docs/ops-http.md`
- `docs/superpowers/plans/2026-10-16-card-1303-r12-retained-evidence.md`
- `docs/superpowers/mutations/2026-10-16-card-1303-r12-unbound.md`

`6ca859d0..681a7ca7` (R14) is the project-cascade purge invoked through `SessionStateDeletion.RunAsync`, its six `Pipeline_*` cases, and the plan/mutation notes:

- `server/Application/Services/ProjectCascade.cs`
- `server/Application/Services/BoardService.cs`
- `server/Application/Services/ProjectService.cs`
- `tests/Antiphon.Tests/Application/ProjectCascadeForeignKeyTests.PipelineEvidence.cs`
- `docs/superpowers/plans/2026-10-16-card-1303-r14-cascade-purge.md`
- `docs/superpowers/mutations/2026-10-16-card-1303-r14-candidate.md`

`SessionStateDeletion.cs` itself is unchanged. Master had no delta on any of these paths relative to the old RC tip.

## Merge commits

No conflicts on either merge.

| Commit | Parents | Subject |
|---|---|---|
| `bf09aa681305625fcbc1e8a13361f77f104f41f7` | `6ca859d006b0a3cac9a88dc6bd551c37e98b2217` `69e4e1e5e6f9c6a9d39fa26ef025561567fcdea9` | Merge CARD-1303 R12 (retained evidence refusal detail) into rc/2026-10-15 |
| `5e9926ae96741e0a7c144d5dff6ec30123dfa772` | `bf09aa681305625fcbc1e8a13361f77f104f41f7` `681a7ca7cfd90c6e26a308df03b95a77cd23314d` | Merge CARD-1303 R14 (cascade purge of pipeline evidence tables) into rc/2026-10-15 |
| `d577b0d21c9047a735c28f6a54be36c88e23eb0e` | `347b3fc16970d89dc7a8f3c6b713e69d692d85e7` `5e9926ae96741e0a7c144d5dff6ec30123dfa772` | Merge rc/2026-10-15 into master (CARD-1303 R12, R14) |

`origin/rc/2026-10-15` fast-forwarded `6ca859d00..5e9926ae9`. `origin/master` fast-forwarded `347b3fc16..d577b0d21`. The master merge's first parent is the old master tip and its second parent is the new RC tip. The diff against old master is only the twelve files listed above.

## Verification at `5e9926ae96741e0a7c144d5dff6ec30123dfa772`

Build slot `cc9f9727-eb59-445b-bcd8-b18a24604829` (waited 0s, maxcpucount 6): `dotnet build tests/Antiphon.Tests/Antiphon.Tests.csproj --property:OutputPath=bin-r12r14/ --property:UseAppHost=false`. Exit 0. 0 errors, 1060 warnings. The server project built in that graph.

R12 filter `/*/*/AdvisoryChangePolicyTests/C1303_R12_LandRefusesUnavailableCorrectedEvidence*`: 3 passed, 0 failed, 0 skipped (1m 05s).

R14 filter `/*/*/ProjectCascadeForeignKeyTests/(Pipeline_evidence_is_purged_leaf_first_with_its_board_or_project*)|(Pipeline_evidence_is_restored_when_the_parent_delete_fails*)|(Pipeline_project_only_evidence_is_purged_with_an_empty_project_or_its_last_board*)`: 6 passed, 0 failed, 0 skipped (48s). Postgres came up through Testcontainers (`postgres:16-alpine`).

--- next stage ---
next: decide
handoff: CARD-1303 R12 and R14 are on origin/rc/2026-10-15 at 5e9926ae96741e0a7c144d5dff6ec30123dfa772 and on origin/master at d577b0d21c9047a735c28f6a54be36c88e23eb0e (first parent 347b3fc16970d89dc7a8f3c6b713e69d692d85e7, second parent the new RC tip). No conflict. Do not land these branches again; CARD-1310 still refuses the normal land path. Do not restart AppHost or runners from this pass.
artifact: docs/investigations/2026-10-16-r12-r14-publication.md
