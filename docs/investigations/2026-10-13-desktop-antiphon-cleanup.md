# Desktop Antiphon cleanup - final after refinement

The caller refinement arrived and authorized retiring qualifying old terminal worktrees despite residue holds, extending cleanup to 16:20 local. The task removed **118 worktrees and three Temp directories, 31.073 GB logical**, plus **72 alternate build-output directories contained in those worktrees**. Cleanup stopped cleanly at the refined limit with **zero deletion failures**; **1,013 candidates / 312.691 GB logical remain pending**.

Task/Code owner: `29f3946d-24ca-453b-b6ef-991c8fbf74f3`. Branch: `feat/card-task-29f3946d`. Worktree: `C:\Antiphon\worktrees\card-task-29f3946d`. Full task source base: `63b2e70479c846cacd01af6b20190e845f8258e4`. Only this summary is changed in Git. The standing cleanup authority and refinement were used without another approval request.

| Kind | Deleted | Kept | Pending | Failed | Logical GB removed |
| --- | ---: | ---: | ---: | ---: | ---: |
| A: worktrees | 118 | 282 | 630 | 0 | 23.917 |
| B: Temp scratch | 3 | 62 | 359 | 0 | 7.156 |
| C: code/review evidence | 0 | 7 | 24 | 0 | 0 |
| D: alternate build output | 72 with A | 352 | 0 with parent | 0 | included in A |

One A path disappeared during refresh and is recorded as already-absent, never claimed deleted by this task. D overlaps its parent and cannot be added to A. GB=1,000,000,000 bytes; logical lengths do not prove allocated space returned. Exact removed bytes: A=23916750734, B=7155928259, total=31072678993. Pending bytes: A=87992387328, B=223823724340, C=874889950.

C: free at initial observation was **342.231 GB**, inventory baseline **339.225 GB**, and final cleanup stop **368.241 GB**. Exact bytes: 342230777856, 339225063424, 368241401856. Net initial-to-stop **+26.011 GB**. Concurrent desktop activity prevents attributing the delta wholly to cleanup or splitting physical reclaim reliably by kind.

## Guard results and verification

A fresh persisted audit classified 748 worktrees as qualifying, 282 retained and one absent. Each A removal repeated terminal/completion age, creation and newest descendant age, live-process/native-directory-handle, source HEAD/branch/common-directory identity, tracked-change and live origin-containment checks. All 118 removed worktrees still have their exact source SHA on both their local branch and origin after cleanup; missing/changed refs=0 and deleted paths still present=0.

The owner preview initially held all 4187 rows (run e5118040-bb34-4dc6-b580-92f50f4f5415); refinement superseded that blanket hold. Retained A reasons now come from data-loss, age or ownership/live-use guards. Overlapping counts: nonterminal=7, completion within 24h=171, recent creation/descendants=167, tracked changes=4, unproven origin/publication=124. All 13 Git-common-directory-unproven paths also fail publication proof. The four tracked-dirty paths are:

- C:\Antiphon\worktrees\card-task-0f9c42e4
- C:\Antiphon\worktrees\card-task-570fd325
- C:\Antiphon\worktrees\card-task-cd2866b8
- C:\Antiphon\worktrees\card-task-dfc6f878

Every kept path and exact reason, including detailed tracked status, is in the manifest. B/C retention covers recent changes, active-use evidence, metadata uncertainty and unproven scratch ownership. Registered C descendants in e3c11469 and task-60ccb73c were preserved. Previously missing saved tracked-state evidence was resolved by the refreshed per-path facts. An externally disappearing path interrupted the refresh once; completed facts were retained and only the remaining paths resumed.

The manifest existed before deletion. Reparse points were unlinked without traversing targets; registered A paths were retired with git worktree remove --force. Branches were retained. B removals additionally checked exclusive file opens without reading payloads. No sessions were stopped and no restart/deployment occurred. Final git worktree prune printed: `Removing worktrees/base: gitdir file points to non-existent location` (stale registration metadata only).

No builds/tests ran, as the cleanup brief explicitly required. No plan/checkpoint, ordinary V/R or PC IDs were assigned; no test/build/Review-clean result is claimed. Build/test slot=n/a; waited=0. Restart: none. Manual verification is the scope/age/process/handle/source/publication/deletion evidence above. All commands and bounded read workers were awaited. scripts/check-evidence-diff.ps1 is run over the full task base..HEAD before final reporting. Generated payloads remain outside Git at the requested evidence paths.

## Artifacts and continuation

- Full manifest and outcomes: C:\Users\lndco\Documents\antiphon-reports\desktop-cleanup-manifest-2026-10-13.md
- Structured manifest: C:\Users\lndco\Documents\antiphon-reports\desktop-cleanup-manifest-2026-10-13.json
- Fresh per-path A facts: C:\Users\lndco\Documents\antiphon-reports\desktop-cleanup-worktree-audit-29f3946d
- Local helper: C:\Users\lndco\Documents\antiphon-reports\desktop-cleanup-29f3946d.ps1
- Brief/refinement: C:\src\Antiphon\.antiphon\task-29f3946d-brief.md and task-29f3946d-refinement-20261013143409.md

Resume pending A with fresh guards, then pending B/C, using a new 35-minute window for each command:

```powershell
pwsh -NoProfile -File C:\Users\lndco\Documents\antiphon-reports\desktop-cleanup-29f3946d.ps1 -Action ExecuteWorktrees -StopAt ((Get-Date).ToUniversalTime().AddMinutes(35))
pwsh -NoProfile -File C:\Users\lndco\Documents\antiphon-reports\desktop-cleanup-29f3946d.ps1 -Action Execute -StopAt ((Get-Date).ToUniversalTime().AddMinutes(35))
```

These commands preserve previously kept rows and recheck pending paths; they do not claim the remaining candidates complete. No additional deletion occurred after the refined stop.

--- next stage ---
next: decide
handoff: Continue the 630 pending A worktrees and 359 B/24 C scratch candidates from the manifest with fresh guards; preserve dirty/unpublished, recent and live owners. The refined run removed 31.073 GB logical without deletion failures.
artifact: docs/investigations/2026-10-13-desktop-antiphon-cleanup.md
