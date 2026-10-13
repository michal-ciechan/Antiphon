# Desktop Antiphon cleanup - 2026-10-13

Partial cleanup: three old verifier directories were removed, containing **7.156 GB of logical files**. Cleanup stopped cleanly at the task's 40-minute limit. There were **zero deletion failures**. Another **383 candidates, 224.699 GB logical**, remain pending; they have not been removed or verified as completed.

Task/Code owner: `29f3946d-24ca-453b-b6ef-991c8fbf74f3`. Branch: `feat/card-task-29f3946d`. Worktree: `C:\Antiphon\worktrees\card-task-29f3946d`. Source base: `63b2e70479c846cacd01af6b20190e845f8258e4`. This commit changes only this summary. The standing operator cleanup authority was used without another approval request.

## Outcome

GB means 1,000,000,000 bytes. Logical file lengths do not prove allocated space reclaimed. Other desktop activity changed volume usage during this task.

| Kind | Deleted | Kept | Pending | Failed | Logical GB removed |
| --- | ---: | ---: | ---: | ---: | ---: |
| A: task worktrees | 0 | 1,031 | 0 | 0 | 0 |
| B: Temp scratch | 3 | 62 | 359 | 0 | 7.156 |
| C: review/code evidence | 0 | 7 | 24 | 0 | 0 |
| D: alternate build output | 0 | 424 | 0 | 0 | 0 |

D rows are contained within retained worktrees and must not be added to parent sizes. A sizes use the earlier disk report's estimates, totaling 176.727 GB for known paths; some new paths have unknown size. D sizes are included in parent estimates or unknown, not measured zero.

C: free space was **342.231 GB** at the initial task observation, **339.225 GB** at the saved inventory baseline, and **345.909 GB** at cleanup stop. Exact bytes: 342230777856, 339225063424, and 345908834304. Net change from the initial observation: +3.678 GB; from inventory: +6.684 GB. Neither delta can be attributed exclusively to deletion.

Deleted exact paths, all under `C:\Users\lndco\AppData\Local\Temp`:

- `antiphon-land-verify-104c262c57964087b42686372fb86313`: 2385379201 bytes.
- `antiphon-land-verify-97c715ca1f2b407589dd524f95c0510e`: 2385379180 bytes.
- `antiphon-land-verify-6fb7a5196ec14588881070edd7a30fb8`: 2385169878 bytes.

## Retention and evidence limits

The installed owner script supports Preview/Release, not Execute. Owner preview run `e5118040-bb34-4dc6-b580-92f50f4f5415` held all 4,187 fleet rows. The inventoried A paths have these owner reasons: release_required=985, publication_retry=16, settling=14, live_owner=9, repair_source_excluded=5, and no owner-validated candidate=2. No policy hold was bypassed, and no A/D deletion was attempted. The nine nonterminal owners include this task.

124 worktree paths have HEAD not proven contained on their observed origin branch, or no corresponding origin branch. Their exact paths, branch/HEAD/origin observations and retention reasons are in the manifest. Tracked-change state is **UNKNOWN in the saved per-path manifest**: an initial detailed audit completed, but its subsequent scratch lock scan was stopped before those results were saved. The final inventory therefore grants no worktree-deletion authority. This is a verification limitation, not a clean-source claim.

B/C retention covers recent creation/modification, recent descendants, active process evidence, metadata uncertainty and unproven scratch ownership. Registered worktree descendants in `C:\Antiphon\review-evidence\e3c11469` and `C:\Antiphon\review-evidence\task-60ccb73c` were retained. Every kept path and its reason is listed in the manifest. Pending B size is 223823724340 bytes; pending C size is 874889950 bytes.

The manifest was written before deletion. Each removal then rechecked the exact allowed parent, age, current process command lines/executable paths, exclusive native directory opens, and exclusive file opens without reading file contents. Metadata or sharing uncertainty retained the directory. The removal helper unlinked reparse points without traversing their targets and supported long paths through .NET/native Windows APIs. All commands and bounded read workers were awaited. `git -C C:\src\Antiphon worktree prune -v` completed without output. No branches were deleted, no sessions stopped, and no restart/deployment occurred.

## Artifacts and continuation

- Full manifest/outcomes: `C:\Users\lndco\Documents\antiphon-reports\desktop-cleanup-manifest-2026-10-13.md`.
- Structured metadata: `C:\Users\lndco\Documents\antiphon-reports\desktop-cleanup-manifest-2026-10-13.json`.
- Local operating helper: `C:\Users\lndco\Documents\antiphon-reports\desktop-cleanup-29f3946d.ps1`.
- Brief: `C:\src\Antiphon\.antiphon\task-29f3946d-brief.md`.

To continue the pending B/C rows with fresh guards and a new 35-minute window:

```powershell
pwsh -NoProfile -File C:\Users\lndco\Documents\antiphon-reports\desktop-cleanup-29f3946d.ps1 -Action Execute -StopAt ((Get-Date).ToUniversalTime().AddMinutes(35))
```

The continuation preserves all A/D and previously kept rows. Resolve owner retirement holds separately while preserving unpublished work. Candidate rows in the manifest mean pending, never passed/deleted.

Verification: manual scope/age/process/handle/deletion checks only. The cleanup brief explicitly required no builds or tests; none ran. No plan, checkpoint, ordinary V/R IDs or PCs were assigned. Build/test slot=n/a; waited=0. Restart: none. Generated manifests remain outside Git at the operator-requested paths; only this small Markdown summary is committed. The full source range is checked with `scripts/check-evidence-diff.ps1` before final reporting.

--- next stage ---
next: decide
handoff: Resolve the owner retirement holds while preserving unpublished work, and resume the 383 pending B/C scratch candidates from the saved manifest with fresh guards.
artifact: docs/investigations/2026-10-13-desktop-antiphon-cleanup.md
