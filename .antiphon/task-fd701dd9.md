# task-fd701dd9 — review bd90f6b5 D-1 and D-2 fixed (landing fence)

Base `ac884e807233633a07e2491b065656a041c1ebae` (tip of feat/card-task-2f9c3f5c). Branch `feat/card-task-fd701dd9`.

- `63d4c62ebcb8e57f603060a0fa2a9850a24ab31e` test: the seams (tip behaviour kept) and the witnesses, which are red here
- `22db7dbe02b3ab813c51240d210ca6feb9cc5aff` fix
- the commit that adds this report

## D-1 (Linux): an unreadable environ is "cannot tell"

`OwnedChildMarker.AnyEnvironCarries(value, rootStartTime, readEnviron)` now handles a failed `/proc/<pid>/environ` read like this:
- If the `/proc/<pid>` directory is gone, the process exited and is skipped.
- If the process's `/proc/<pid>/stat` start time (field 22, clock ticks since boot, readable for every listed pid) is strictly earlier than the owned root's, it is skipped. No descendant can start before its root. `Attach` reads the root's start time right after start.
- Otherwise the result is "unknown". A later marker hit still returns true. After the loop, unknown returns `null`.

A `/proc` mounted with `hidepid` other than 0/off, or an unreadable `/proc/self/mountinfo`, returns `null` up front, because other users' processes are then missing from the listing. A `/proc` listing error was already `null`. `ReleaseChildAsync` is unchanged: `null` means the record is kept and the handoff reason is `pipe_open`.

Why the start-time rule: without it, `pid 1` and every daemon of another uid are unreadable on a normal host. `pipe_open_unowned` could then never fire on Linux. A pre-root process cannot carry a marker created for this root, so skipping it removes no proof.

What this costs: on a host where other uids keep spawning processes (server2 runs Postgres uid 70 backends and Docker), any such process started after the root is "cannot tell". The record then stays until EOF, which was the pre-2f9c3f5c behaviour and is safe. Because of this, `C1076_StrangerPipeHolderDoesNotKeepTheJournal` now injects `ReadableOrEmpty`, so a Postgres backend started during the run cannot flip it. The unreadable rule has its own witnesses.

## D-2 (Windows): an empty job proves nothing when the child joined late

`JobVerdict(assignedBeforeSpawn, active)` returns:
- `true` when `active > 0`
- `false` only when `active == 0` and `AssignedBeforeSpawn`
- `null` otherwise

Production `Attach` runs after `Process.Start`, so `AssignedBeforeSpawn` stays false. On Windows, `pipe_open_unowned` is therefore unreachable. The unowned-removal path is effectively Linux-only.

Chosen design: the SAFE fallback from the brief, not CREATE_SUSPENDED. Reasons:
- A P/Invoke CreateProcess launcher that reproduces Process.Start's redirected pipes, environment block and working directory is a large Windows-only change.
- This Linux host cannot execute it, and a Windows run is live.
- The fallback cannot weaken the fence. Every late-assigned empty job keeps the record.

The suspended launcher can follow as its own card. It would set `AssignedBeforeSpawn = true` and needs nothing else changed.

On Windows, the group-111 outcome now relies on `TerminateJobObject` killing the reparented msys sleep. Its pipe ends close and the existing EOF path removes the record with `reason=removed`. The terminate line now ends with `assigned=after_start|before_spawn`.

What the native Windows rerun of group 111 must show:
- `repository_child_job_terminate root=<pid> active=<n> terminated=True assigned=after_start`
- no `repository_child_job unavailable` line
- `repository_child_journal_release ... pipe_drained=True ... reason=removed`
- no `repository_child_journal_handoff` line, and no `pipe_open_unowned` on Windows
- the journal directory empty afterwards

There is no job-assigned-before-spawn proof line, because that design was not built. If the sleep started before the assignment (a window of microseconds), the outcome is `handoff reason=pipe_open`, kept until the sleep exits. That is safe, not a removal.

## Witnesses (LandingGitTests, Linux stand-in holder outside the killed set)

| Test | at 63d4c62e (seams, tip behaviour) | fixed 22db7dbe |
|---|---|---|
| C1076_UnreadableEnvironOfALaterProcessKeepsTheJournal (holder environ throws UnauthorizedAccess) | **red**: release `pipe_open_unowned` | green: handoff `pipe_open`, journal kept, removed after holder exit |
| C1076_LateJobAssignmentWithNoMembersKeepsTheJournal (JobProbe 0, late) | **red**: `pipe_open_unowned` | green: handoff `pipe_open` |
| C1076_UnreadableEnvironOfAnEarlierProcessDoesNotKeepTheJournal (`/proc/1/environ` throws) | green | green: `pipe_open_unowned` |
| C1076_JobAssignedBeforeSpawnWithNoMembersReleasesTheJournal (JobProbe 0, before spawn) | green | green: `pipe_open_unowned` |

Red run at 63d4c62e: total 4, failed 2, succeeded 2.

Mutants on the fixed tip. They were uncommitted, then restored.
- M-A plus M-B, built together because they touch different methods. M-A treats an unreadable environ as not carrying (an unconditional `continue`). M-B ignores the late flag (`active == 0` gives false). Witness filter plus the stranger test: total 5, failed 2 (`UnreadableEnvironOfALater…` and `LateJobAssignment…`, each caught by its own mutant), succeeded 3.
- M-C ignores the start-time rule, so every unreadable live process is unknown. Total 2, failed 1 (`UnreadableEnvironOfAnEarlier…`), succeeded 1.
- After restoring the source, a rebuild and the six C1076 stranger, owned, unreadable and job tests: total 6, passed 6.

## Commissioned run (22db7dbe, one build, native output, UseAppHost=false, TEMP=/tmp, build-slot)

Filter `/*/*/(LandingGitTests*)|(RepositoryMutationLeaseTests*)|(OwnedProcessTreeTests*)|(RepositoryChildJournalInspectorTests*)/*`, `--timeout 45m`: **total 126, succeeded 124, failed 0, skipped 2**. That is the 122 set plus the 4 new tests. The skips are C448_V13_WindowsJunctionAndOtherProcessShareTheLease and C448_V28_ExitedRootKeepsItsJournalWhileADescendantOwnsOutput (Windows only).

## Gates

- `audit-land-verification-graph.ps1 -Check -ToolPath tools/Antiphon.LandGraphAudit/bin/Debug/net10.0/Antiphon.LandGraphAudit.dll`: exit 0, `digest=D92450D9AABCBF9C783B506F73DF06C45635F415D03E65C5F1A97AF415CC36BE count=69`, `approved`. `-Update` was not run.
- `check-evidence-diff.ps1`: ac884e80..22db7dbe gave commits=2 entries=0 violations=0. ead93feb..22db7dbe gave commits=6 entries=1 violations=0.
- `git merge-tree --write-tree` exited 0 for both targets. Against origin/master `4518ce64b70c13a4c03a375d9630152342ee732c` the tree is `061d10ffd6bce49eb08601bc44a680acc3276f2a`. Against origin/rc/2026-11-01 `ead93feb00fcaf8bf31018599437f17bee8e934c` the tree is `e4f43938ca56f03296936e99b406eb321fc2e7a8`. Nothing was merged.

## Residual (not changed)

- Review advisory: the `/proc` readdir is not atomic. A marked process can fork, die, and leave a child whose pid the scan has already passed. Closing that needs a subreaper or a cgroup, so it is out of this slice.
- Paths 2 and 3 (EOF removes without asking ownership) predate this work.
- The Windows native qualification of the job path is still outstanding.

--- next stage ---
next: Review
handoff: diff-only round 2 Review of ac884e807..HEAD on feat/card-task-fd701dd9 (round 2 of 3; blockers only if rounds run out), then Merge-role fast-forward rc/2026-11-01 and master; Windows native group 111 rerun must show the lines above
artifact: .antiphon/task-fd701dd9.md
