# adopt_source_invalid on server2 repair landings

Diagnosed 2026-10-10 from checkout `962075470e4d0dcfcb890c58f331b046abf4b48d` (branch `feat/card-task-93da76b5`). The running API at that time was `4f10f18985693a13887c6dfa8485713fdd9e4255`, which is an ancestor of this checkout and still throws the same detail at `AgentTaskLandService.cs:210`. `GET /api/version` advertised `land-v2`. No land POST was repeated.

## What refused

`POST /api/agent-tasks/{owner}/land/v2` with `adoptFromTaskId` returns 409 `adopt_source_invalid`, detail "Adoption requires a same-card Code worktree source.", from this single predicate:

```205:211:server/Application/Services/AgentTaskLandService.cs
if (source.Workspace != WorkspaceMode.Worktree || source.Role != AgentTaskRole.Code
    || source.RepairSourceTaskId is not null || source.SourceLandingOperationId is not null
    || source.WorktreeBranch is null || source.RepoPath is null || task.RepoPath is null
    || source.CardId is null || source.CardId != task.CardId
    || source.ProjectId != task.ProjectId)
    throw new ConflictException("Adoption requires a same-card Code worktree source.",
        "adopt_source_invalid");
```

That throw is before settled-status (`:212`), a pending land on the source (`:215`), repository identity (`:217`), and review evidence (`:232`). `RunnerId`, platform, SHA, and branch name are not inputs. The same `RepairSourceTaskId != null` exclusion is repeated later as `recovery_source_invalid` in `AgentTaskLandSourceResolver.RecoverSourceAsync` (`:228-236`) and `AgentTaskLandingProtocol.RecheckApprovalAsync` (`:601-605`). Those later guards did not run: every row below has `landRequestedAt` null and no land request.

`docs/orchestration-loop.md` states the same rule. A `-RepairSource` task is not eligible for `-FromTask` (`:179-180` and `:204-205`). A separate Code worktree started with `-StartRef` is the adoption source (`:180-181`, `:236-247`). `-RepairSource` and `-StartRef` cannot be combined (`scripts/delegate.ps1:1078-1080`). `-FromTask` and `-RecoverReviewedSource` cannot be combined (`AgentTaskLandService.cs:93-94`, code `recovery_source_ambiguous`).

`-RepairSource` is what sets the failing field. Create stores `RepairSourceTaskId` (`AgentTaskService.cs:1349`). `WorktreeBaseResolver.Resolve` then records `WorktreeBaseSource.Repair` from the owner SHA and ignores a start ref (`WorktreeBaseResolver.cs:39-40`). Dispatch cuts an isolated branch at the owner's desktop SHA (`AgentTaskDispatcher.PrepareRepairSourceAsync`, warning at `:7401`). Landing that repair task itself is a different 409, `repair_source_landing_owner_required`, at `AgentTaskLandService.cs:85-88`, before adoption is considered.

## Which field failed

GET `/api/agent-tasks/{id}` on 2026-10-10. Every source and owner is Succeeded, role Code, workspace Worktree, `repoPath` `C:\src\Antiphon`, `sourceLandingOperationId` null, `runnerId` server2, `observedPlatform` linux. Card and project match inside each pair. The only failing clause is `repairSourceTaskId`.

| Task | Card | Branch | Tip (primary progress) | repairSourceTaskId | worktreeBaseSource | worktreeBaseSha |
|---|---|---|---|---|---|---|
| `cf25e09b-e7fd-4d06-a927-25b364a46e7d` owner | CARD-1168 | `feat/card-task-cf25e09b` | `a97f0f9e553f2320b0afa33cd08f81d7e08e1ef9` | null | DefaultBranch | `1ecad09f35cce7e2ff6d0300aa40665b5da43a9b` |
| `b77f6aad-7b89-49cb-999e-c2ebd5112e12` source | CARD-1168 | `feat/card-task-b77f6aad` | `2ad461b9b79872a96ceeda9d5c259d5a23ae12bb` | `cf25e09b-e7fd-4d06-a927-25b364a46e7d` | Repair | `a97f0f9e553f2320b0afa33cd08f81d7e08e1ef9` |
| `1522ff9c-c7b7-4add-8aac-4c1123a0515b` owner | CARD-0505 | `feat/card-task-1522ff9c` | `fd6b44b667f0ebe7d68893425b6b0ddc40577a89` | null | DefaultBranch | `8239e7d1077d7c376e8dfa30d6e0f1fa1496fdb5` |
| `4b4a88ba-4128-43ec-b259-31ab6ee4f9bc` source | CARD-0505 | `feat/card-task-4b4a88ba` | `9f0f63d4f3b48e0071adc242259b0c66d5870446` | `1522ff9c-c7b7-4add-8aac-4c1123a0515b` | Repair | `fd6b44b667f0ebe7d68893425b6b0ddc40577a89` |
| `f1e22bb1-7db1-4de2-a03b-d191ae6bc0e5` source | CARD-0505 | `feat/card-task-f1e22bb1` | `4554fed6a692f8ccd582da52759e44883d5e776e` | `4b4a88ba-4128-43ec-b259-31ab6ee4f9bc` | Repair | `9f0f63d4f3b48e0071adc242259b0c66d5870446` |

`worktreeBaseRequestedRef` is null on all five, so none was dispatched with `-StartRef`. Progress origin on each repair is Primary on that repair's own commit, not RepairSource on the owner ref. `f1e22bb1` repairs `4b4a88ba`, not the landing owner; it still fails the same clause.

In this clone, `a97f0f9e…` is an ancestor of `2ad461b9…`, and `fd6b44b6…` is an ancestor of both `9f0f63d4…` and `4554fed6…`. That is the `adopt_source_lineage` shape (`AgentTaskLandSourceResolver.cs:386-399` requires the owner tip to be equal to or an ancestor of the source's `WorktreeBaseSha`). Lineage was not the 409.

## Review evidence was not reached

`GET /api/stage-outcomes?cardId=…&latestOnly=false`.

| Evidence | Subject | Outcome | Round / scope | Clean | SHA | Ref |
|---|---|---|---|---|---|---|
| `b01f02f8-35b3-4ac7-b75c-e72e5f8d001f` | `b77f6aad` | Clean | Final / Full | true | `2ad461b9…` | `refs/heads/feat/card-task-b77f6aad` |
| `38cf8ee9-2666-4aa4-ad80-ef46a60c96e7` | `cf25e09b` | Found | Final / Full | true | `a97f0f9e…` | `refs/heads/feat/card-task-cf25e09b` |
| `85fb5a6d-3c6a-4972-9ae0-c3fc8f074e98` | `f1e22bb1` | Clean | Final / Full | true | `4554fed6…` | `refs/heads/feat/card-task-f1e22bb1` |
| `2f2a5c4f-7035-46f5-a7d9-7f1c17814c17` | `4b4a88ba` | Found | Final / Full | true | `9f0f63d4…` | `refs/heads/feat/card-task-4b4a88ba` |
| `b548d838-5aac-470a-8d04-57ea662cf7df` | `1522ff9c` | Found | Final / Interim | true | `fd6b44b6…` | `refs/heads/feat/card-task-1522ff9c` |

`b01f02f8` is the right subject, ref, and SHA for adopting `b77f6aad`, and it was not consulted. Adoption evidence must name the `-FromTask` source (`LandApproval.cs:98-108`). Reusing `b01f02f8` for a new task returns `review_evidence_subject_mismatch`. `-RecoverReviewedSource` would require a Clean Final/Full review of the owner ref at the owner's pushed tip (`docs/orchestration-loop.md:190-205`). These owner reviews are Found, and the CARD-0505 owner review is also Interim scope. The repair tips are not the owner-ref tips.

## Accepted land form

Dispatch the landable repair without `-RepairSource`:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Role Code -Worktree -Runner server2 -Card CARD-1168 -StartRef 2ad461b9b79872a96ceeda9d5c259d5a23ae12bb -Goal "<carry the repair>"
pwsh -NoProfile -File scripts/delegate.ps1 -Role Code -Worktree -Runner server2 -Card CARD-0505 -StartRef 4554fed6a692f8ccd582da52759e44883d5e776e -Goal "<carry both repairs>"
```

`-StartRef` at the later CARD-0505 tip carries `4b4a88ba` as well, because `9f0f63d4…` is an ancestor of `4554fed6…`. A start at the owner tip also satisfies lineage; the reviewed SHA is then whatever that new task pushes.

Commission a new Clean Final/Full review whose `subjectTaskId` is the new task, at that task's pushed tip and `refs/heads/feat/card-task-<new>`. Then:

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Land cf25e09b-e7fd-4d06-a927-25b364a46e7d -FromTask <new-1168-task> -ExpectedSourceSha <new-tip> -ReviewEvidenceId <new-evidence>
pwsh -NoProfile -File scripts/delegate.ps1 -Land 1522ff9c-c7b7-4add-8aac-4c1123a0515b -FromTask <new-0505-task> -ExpectedSourceSha <new-tip> -ReviewEvidenceId <new-evidence>
```

The owner's desktop checkout must be clean and on its branch (`docs/orchestration-loop.md:251-253`). A desktop runner is not required. Do not `-FromTask` `b77f6aad`, `4b4a88ba`, or `f1e22bb1`. Do not `-Land` those repair tasks. Do not `-RecoverReviewedSource` these repair SHAs onto the owners.

## Card text (not filed)

Worth a small card. The refusal itself is the specified CARD-0499 / CARD-0753 D-2 / CARD-0675 exclusion, not a server2 or review-evidence bug. The defect is the detail: it names a card/role/workspace mismatch the rows do not have, so the same `-FromTask` shape was retried.

Title: `adopt_source_invalid` hides a RepairSource exclusion

Description: `POST /api/agent-tasks/{owner}/land/v2` with `adoptFromTaskId` returns 409 `adopt_source_invalid` and detail "Adoption requires a same-card Code worktree source." `AgentTaskLandService.RequestAsync` uses that one detail for every failing clause, including `RepairSourceTaskId != null`. On 2026-10-10, CARD-1168 owner `cf25e09b` adopting `b77f6aad`, and CARD-0505 owner `1522ff9c` adopting `4b4a88ba` and `f1e22bb1`, matched workspace Worktree, role Code, card, project, repo `C:\src\Antiphon`, branch, and Succeeded. The only failing clause was `repairSourceTaskId`. Admission threw before status, lineage, and review evidence; no land request row was stored. The exclusion is specified and should stay. The detail should name the RepairSource exclusion and point at a fresh `-Worktree -StartRef <sha>` task plus `-Land <owner> -FromTask <that task>`, which `PrepareRepairSourceAsync` already prints for `repair_source_owner_remote_ahead`. Do not admit `RepairSourceTaskId != null` as an adoption source.
