# task-bd1e86ea — round 2 review of the landing fence

Clean at `f3794e0a98a1100063efd396d07ceb14a1efdb1d` (whole tip of `feat/card-task-fd701dd9`, including `63d4c62eb`, `22db7dbe0`, and this report's parent `f3794e0a9`). Owner `fd701dd9-6a1e-4aa5-bc8a-325a1a06ee4e`. Range `ac884e807233633a07e2491b065656a041c1ebae..f3794e0a98a1100063efd396d07ceb14a1efdb1d`. No numbered defect. D-1 and D-2 do not remove the journal while a writer may live.

## D-1 (Linux)

`AnyEnvironCarries` returns null (keep; handoff `reason=pipe_open`) for every cannot-tell:

- `hidepid` other than `0`/`off`, or an unreadable `/proc/self/mountinfo`, before the scan.
- `Directory.EnumerateDirectories("/proc")` throwing `IOException` or `UnauthorizedAccessException`.
- The enumeration loop throwing the same.
- A failed environ read (`IOException` or `UnauthorizedAccessException`) when `/proc/<pid>` still exists and the process is not strictly older than the root. `unknown` stays set, so the scan returns null unless a readable carrier returns true (also keep).

A process that started later than the root and whose environ cannot be read is that last case. It returns null.

Disappeared versus unreadable is directory presence after the failed read, not a separate errno branch. `File.ReadAllBytes` surfaces a vanished proc node as `FileNotFoundException` (`ENOENT`, an `IOException`). `Directory.Exists` is then false and the pid is skipped. `ESRCH` is not a file-read error; the same vanished node is `ENOENT`. `EACCES` is `UnauthorizedAccessException`. `/proc/<pid>` stays stat-able when only `environ` is mode `0400`, so `Exists` is true and a permission error is not classed as disappeared. `hidepid` never reaches this branch: the mount scan returns null first. This host's `/proc` mountinfo line has no `hidepid`.

Start time is field 22 of `/proc/<pid>/stat` (index 19 after the last `)`), clock ticks since boot. `Attach` records the root with the same `StartTime` helper immediately after `Process.Start`. Same clock, same unit. Checked against live `/proc/self/stat` and `/proc/1/stat`: index 19 is the tick counter (self `1933271891`, pid 1 `1903918242`). Strict `<` is the skip. A descendant cannot start before its root. An equal tick is cannot-tell and keeps the record.

Pid recycle between the failed read and the start-time read is judged on the current occupant. A gone directory is skipped. A new occupant whose start time is not strictly earlier than the root sets unknown and keeps the record. A reuse after a pre-root start time was already read is the non-atomic `/proc` residual the code report already recorded. Linux allocates pids upward, so that pid is not reused inside one scan. Not a removal path.

## D-2 (Windows)

`JobVerdict` returns false only when `active == 0` and `AssignedBeforeSpawn` is true. Production `AssignedBeforeSpawn` stays false: `Attach` runs after `Process.Start`, and nothing in production sets the flag. `AnyAlive` on Windows therefore returns null for an empty job and for a failed query. `ReleaseChildAsync` removes with `pipe_open_unowned` only when `ownedAlive == false`. An empty job with `assigned=after_start` does not remove while the pipe is open; the handoff reason is `pipe_open`. The terminate line ends with `assigned=after_start` (or `before_spawn` only if the flag were set). When `TerminateJobObject` kills an in-job holder and the pipe drains inside the grace, the earlier branch removes with `reason=removed` without consulting the empty job.

## Witnesses

Green, clean tree, `HEAD` `f3794e0a98a1100063efd396d07ceb14a1efdb1d`, before the mutant. Build slot `db6678c6-1498-4edf-b65a-6981efcfdc8b` (Debug, `UseAppHost=false`, native output, `TEMP=/tmp`). Run slot `f81ccef3-d042-47ab-a61d-97b27e59af39`. Filter `/*/*/LandingGitTests/*`, `--timeout 45m`. Total 72, failed 0, succeeded 72, skipped 0, 23.833s. TRX `/tmp/bd1e86ea-landinggit/landinggit.trx`.

Passed inside that run, each once: `C1076_UnreadableEnvironOfALaterProcessKeepsTheJournal`, `C1076_UnreadableEnvironOfAnEarlierProcessDoesNotKeepTheJournal`, `C1076_LateJobAssignmentWithNoMembersKeepsTheJournal`, `C1076_JobAssignedBeforeSpawnWithNoMembersReleasesTheJournal`, `C1076_StrangerPipeHolderDoesNotKeepTheJournal`, `C1076_OwnedPipeHolderOutsideTheTreeKeepsTheJournal`, `C1076_BudgetKillDoesNotWaitOutADetachedPipeHolder`, `C1076_NormalExitWaitsForADetachedPipeHolderPastTheBudget`, `C448_DescendantSetKeepsGrandchildrenWhoseParentDiesWithTheRoot`.

Mutant, uncommitted, then restored: the unreadable-environ catch only `continue`s (not carrying). Rebuild slot `e15a6beb-89ab-44bc-b026-b4cf1cd1174f`. Run slot `fe9ba1fe-72cb-4217-8297-7aa597d8035c`. Filter `/*/*/LandingGitTests/C1076_UnreadableEnvironOfALaterProcessKeepsTheJournal`. Total 1, failed 1, succeeded 0. The journal was empty and the release line was `reason=pipe_open_unowned`. Source restored with `git restore --worktree --source=HEAD`; `git diff` empty at `f3794e0a98a1100063efd396d07ceb14a1efdb1d`.

`C1076_StrangerPipeHolderDoesNotKeepTheJournal` injecting `ReadableOrEmpty` is not a weaker stranger witness. The holder is this user's process, so its environ is read, and the assertions are still holder-alive, journal empty, `pipe_open_unowned`, no handoff. `ReadableOrEmpty` stops an unrelated post-root unreadable process from keeping the fence. The later and earlier tests own that direction. The earlier test still uses pid 1's real start time.

## Gates

- `audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll`: exit 0, `digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE count=69`, `approved`. `-Update` was not run. The DLL is the native Debug output of the slotted test build.
- `check-evidence-diff.ps1 -BaseRef ac884e807233633a07e2491b065656a041c1ebae -HeadRef f3794e0a98a1100063efd396d07ceb14a1efdb1d`: commits=3 entries=1 violations=0, exit 0.
- `git merge-tree --write-tree` exit 0 both ways. `origin/master` `10c8c2471633c7c0212294afc57348878021e183` (later than `4518ce64b`) tree `545842790767ef4f3839df5d562a542efdf734d2`. `origin/rc/2026-11-01` `3e39e0534f1942137dc856ab932f9e9d6d6a0aaf` tree `8beb93638cebe0d9203b523be32da5d475dbea1d`. Nothing was merged.

## Advisories

- Windows native group 111 is still unrun. Production `AssignedBeforeSpawn` stays false, so an empty job cannot take `pipe_open_unowned`.
- The non-atomic `/proc` scan residual is unchanged: a marked child can appear after its pid was already passed, and a pid reused after a pre-root start time was read is the same residual.

PCs stay pending. This round's required selection is the diff checks above, not the unit lane.
