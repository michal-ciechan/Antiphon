# CARD-0543: Detect and surface a stale `.git/index.lock` before the land pipeline writes

Status: Plan complete with the verification design folded in (the card asks for acceptance guards and a regression test plan); Code is the next stage. Nothing here has been implemented.

Authoring baseline: `0a4e9fbd` (branch `feat/card-task-bd5687a0`). Plan task: `bd5687a0`. Card: `CARD-0543` (Antiphon board, created 2026-09-16 00:34Z while landing CARD-0481). The server API refused connections on 17202 during this stage (an AppHost restart was in flight at 11:41 local), so the card body, revision history and land rows were read straight from Postgres (`antiphon-postgres`, port 17280); the 2026-09-15 server log and the watchdog log were read from `C:\src\Antiphon`.

Owners to read before changing these areas: [orchestration](../../orchestration-loop.md) (land request, holds, refusals), [ops HTTP](../../ops-http.md) (`-Status` fields), [AppHost runbook](../../apphost-runbook.md) and [bootstrap](../../bootstrap.md) (restart/verify/watchdog scripts), [testing and build](../../testing-and-build.md) (script tests, real-git tests, alternate output path).

## Outcome and scope

1. Name the mechanism that left the orphaned lock, with evidence, and remove that mechanism where the server owns it.
2. Make every land attempt detect an `index.lock` in a checkout it is about to mutate and surface `git_index_lock_stale` or `git_index_lock_held` with the path, age and the exact removal command, instead of `target_advance_failed`, `rebase_failed`, `land_request_identity_conflict` or `source_remote_diverged`.
3. Flag a stale lock proactively from the existing restart/verify/watchdog scripts.

Out of scope, per the card: redesigning the land protocol, its phases, resumption or cleanup; deleting locks the pipeline did not create (see D-2); other lock files (`HEAD.lock`, `packed-refs.lock`, ref locks); changing `Git:TimeoutSeconds`.

## Investigation: what left the lock (card ask 1)

### Timeline of the 2026-09-15 incident (UTC; the card's "22:50" is local, +01:00)

| Time (UTC) | Evidence | Meaning |
|---|---|---|
| 21:42:48 | `AgentTaskEvents`: task `d7ff26d6` settled (Worktree, `C:\src\Antiphon`) | Ordinary settlement. |
| 21:43:14 | event `Land requested: expected=3b0ca030...` | Land of `d7ff26d6` queued. |
| 21:49:28 | event `landed operation=f197e7b0 ... mode=Fresh`; `AgentTaskLandings.f197e7b0` Phase=10 (Complete), `TargetCheckoutPath=C:\src\Antiphon` | This land's `merge --ff-only` in the canonical checkout succeeded. No lock yet. |
| **21:50:37** | `server/logs/antiphon-20260915.log:20236` `[WRN] git status --porcelain -z --untracked-files=all timed out after "00:00:15" in C:\src\Antiphon; child killed` (`GitWorkspaceService`) | **The lock's birth.** Same minute as the card's lock timestamp, same directory. |
| 21:58 | `logs/watchdog-apphost.log`: `health slow ... HttpClient.Timeout of 5 seconds` x4 | The machine was under load; a 15 s `git status` budget is credible. |
| 00:05 to 00:24 (09-16) | `AgentTaskLandings` `6c993e11` (task `7f3e581c`) and `d8226ce2` (task `73a2660b`) both still at Phase=5 (`TargetAdvanceStarted`) today, `LastReason` now `source_changed` and `land_request_identity_conflict` | Both reached target advance, failed there, and were then overwritten by later retries' reasons. |
| 00:26 to 00:33 | `AgentTaskLandings.725ca437` Phase=10 | First land after the lock was deleted by hand succeeded. |

No AppHost restart happened in that window (no `Application started`/`shutting down` lines on 09-15, no watchdog restart), so the restart script is not the cause of this instance.

### Mechanism, verified empirically today (git 2.50.1.windows.1, scratch repos only)

- A default `git status` refreshes the index as an optional side effect: it creates `index.lock`, writes the refreshed index into it, then renames it over `index`. Measured: after touching a tracked file, `git status --porcelain` changed `.git/index`'s mtime; the same command under `GIT_OPTIONAL_LOCKS=0` did not.
- `GitWorkspaceService.RunCoreAsync` (`server/Application/Services/GitWorkspaceService.cs:1026-1099`) enforces `Git:TimeoutSeconds` (default 15) with `process.Kill(entireProcessTree: true)`. `TerminateProcess` gives git no chance to roll the lock back. Measured: a `git commit --only` killed while its pre-commit hook slept left `index.lock` behind (209 bytes).
- Three callers issue exactly that command shape in a task's checkout: `TryGetChangesAsync` (`:85`, Files panel via `AgentFilesService`), `GetWorkingTreeCountsAsync` (`:428`, `DelegateCheckProbe`) and `GetDirtyPathsAsync` (`:465`, settlement). All three run through the same kill path; which one fired is immaterial to the fix.
- Reads keep working against a stale lock (measured: `git status` exit 0 with and without `GIT_OPTIONAL_LOCKS=0`); mutations that need the index fail: `merge --ff-only <branch>` exit 1 `Unable to create '.../.git/index.lock': File exists`, `add` exit 128, `reset --hard` fatal. `update-ref` succeeds (it does not touch the index).
- `LandingGit.ExecuteAsync` deliberately discards stderr (`server/Infrastructure/Git/LandingGit.cs:71`), so the protocol only ever sees `git_exit_1` and raises `target_advance_failed` (`AgentTaskLandingProtocol.cs:276`).

### Why the later codes were misleading

The catch block in `AgentTaskLandingProtocol.RunAsync` (`:315-327`) transitions an operation to `Refused` only from `Inspected`, `RecoveryPinned`, `RebaseStarted` or `Prepared`. A failure at `TargetAdvanceStarted` leaves the operation at phase 5 so the same request can resume it. A *new* request with a different `expectedSourceSha` then trips `RecheckRemoteSourceAsync` (`:430-437`) into `land_request_identity_conflict`; manual branch resets/force-pushes during the retries produced `source_remote_diverged` (resolver `ClassifyAsync`, `AgentTaskLandSourceResolver.cs:369-379`) and `source_changed`. None of those checks were wrong; they were all downstream of one silent `merge --ff-only` failure.

### Other lock-orphaning mechanisms in this repository, ranked

| # | Mechanism | Lock taken? | Kill mechanism | Disposition in this plan |
|---|---|---|---|---|
| 1 | `GitWorkspaceService` read commands (`status`) in the canonical checkout | Yes, optional index refresh | 15 s timeout kill | **Root cause of the incident.** Removed by S1 (`GIT_OPTIONAL_LOCKS=0`). |
| 2 | `GitWorkspaceService` mutations under `GatedCommitService` (`add`, `commit`, `reset`, `read-tree`, `write-tree`) during a Shared task's commit-on-settle in `C:\src\Antiphon` | Yes, mandatory | Same 15 s kill | Self-heal of the runner's own orphan after its own kill (S1, D-3). |
| 3 | `restart-apphost.ps1` step 1 `taskkill /T /F` on the AppHost tree (`scripts/restart-apphost.ps1:172-190`) while a server git child holds the lock | Depends on the child | Tree kill | Report only, post-kill NOTE (S4). No delete. |
| 4 | `LandingGit` 5 min budget kill of `merge --ff-only` (target checkout) or `rebase` (source worktree) | Yes | `Kill(entireProcessTree)` at `LandingGit.cs:64` | Left to the protocol's existing interrupted-child custody; the next attempt now surfaces the lock (S3). |
| 5 | A human, IDE, or Shared delegate interrupted mid-write in `C:\src\Antiphon` | Yes | External | Detected and surfaced (S3, S4); never deleted. |
| - | `update-ref`, `fetch`, `push`, `ls-remote`, `rev-parse`, `worktree add/remove/list`, `merge-base` | No index lock | - | Unaffected. |
| - | `LandingGit` reads | No (`GIT_OPTIONAL_LOCKS=0` already set) | - | Unaffected. |
| - | Card-file sync commits (`CardFileRepository`) | Yes | own kill | Not a source here: `Boards.SyncCardFiles=false` for every Antiphon board. Not changed. |

## Ground truth

| Card premise or plausible shortcut | Confirmed behaviour at `0a4e9fbd` | Design consequence |
|---|---|---|
| "Unknown what left the lock." | Server log names the killed `git status` at the lock's minute; mechanism reproduced today. | Fix the source (S1) as well as detecting (S3). |
| The land's git-write step is one place. | Three index-lock mutations: `rebase`/`rebase --abort` in the source worktree (`AgentTaskLandingProtocol.cs:203-210`), `merge --ff-only` in the target checkout (`:275`), and the resolver's `merge --ff-only` source fast-forward (`AgentTaskLandSourceResolver.cs:114`). `update-ref` (`:274`) needs no index lock. | Probe the exact checkout before each of the three; never before `update-ref`. |
| `.git/index.lock` is the only path. | A linked worktree's index lock is `<common>/worktrees/<name>/index.lock`; `git rev-parse --path-format=absolute --git-path index.lock` resolves both. | Resolve the path through git, not string concatenation. |
| A held-open probe can prove liveness. | Measured: during a `git commit` pre-commit hook the lock exists but an exclusive `FileShare.None` open succeeds. Git closes the lock's handle between write and rename. | Liveness comes from a `git` process census by start time plus age (D-1), never from file sharing. |
| Creation time gives the lock's age. | NTFS tunnelling reuses the creation time of a same-named file deleted within 15 s, which `index.lock` is constantly. | Age from `LastWriteTimeUtc` only. |
| A refusal is the only surfacing channel. | `HoldAsync` (`AgentTaskLandService.cs:836-868`) records a reason code and a 2000-char detail without consuming an attempt; `SweepAsync` (`:522-533`) re-enqueues every pending request, Held included, every `Delegation:LandSweepSeconds` (5 s); Held requests appear in the Attention feed (`AttentionService.cs:1222-1231`) and produce a `Held` notification; `delegate.ps1 -Status` prints `Reason: <code>; ... <detail>` (`scripts/delegate.ps1:563`). | Pre-admission detection is a hold that clears itself once the file is gone. |
| A post-admission hold is equivalent. | Admission increments `Attempt`/`LandAttempt` (`:262-268`); `LandMaxAttempts` (3) refuses after that; the docs promise a hold consumes no attempt. | Inside the protocol, surface as a refusal with the specific code; the operation stays resumable exactly as today. |
| `LastReason` can carry the path. | `LastReason` is the bare code printed as `Landing reason:`; the terminal event line comes from `RefuseAsync(task, reason)` (`:661`). No column for a detail on `AgentTaskLanding`. | Add an in-memory `Detail` to `LandingProtocolResult`; append it to the event line and warning. No migration. |
| The restart script is the periodic checker. | `restart-apphost.ps1` runs only on demand; `watchdog-apphost.ps1` fires every 2 minutes; `verify-dev-stack.ps1` is the manual health report. None reads any git lock. | Helper in `apphost-common.ps1`; NOTE in restart, row in verify, WARN in watchdog (S4). |
| Reading the card needs the API. | 17202 refused during this stage; Postgres held the card and rows. | Recorded above; no design impact. |

## Decisions

### D-1. Classify a present lock as `stale` or `held` by age plus a git process census

`stale` = the file exists, its `LastWriteTimeUtc` is at least `Git:IndexLockStaleAfterSeconds` old (default 300, validated >= 30), and no `git` process is alive whose `StartTime` is at or before that mtime plus 2 s of clock skew. Anything else present is `held`. A process whose `StartTime` cannot be read counts as a candidate holder. The census is `Process.GetProcessesByName("git")` (works on Windows and Linux); it is a conservative filter, so an unrelated long-lived git elsewhere degrades to `held`, never to a wrong `stale`.

Rejected: the file-share probe (disproved above); `handle.exe`/`NtQuerySystemInformation` handle walks (privileged, slow, still blind between write and rename); parsing git stderr (discarded by design in `LandingGit`, and would only say "File exists"); age alone (a 6-minute `git add` on a loaded box is real).

### D-2. The pipeline and scripts never delete a lock they did not create

Surface, do not heal, for foreign locks. The land is a background service; the card's own diagnosis needed a human process census before deleting; the orchestrator session that receives the hold notification has a shell and the detail line gives it the exact `Remove-Item`. A future flip to automatic removal of a `stale` lock is one policy line behind a new `Landing:IndexLock:RemoveStale` setting; it is not implemented and not defaulted on.

Rejected: auto-remove `stale` in the land (deletes a file owned by an unknown process from a service that cannot see that process's intent); auto-remove in `restart-apphost.ps1` after its own kill (an IDE or delegate can create a lock in the same window; the script cannot tell).

### D-3. Remove the identified source in `GitWorkspaceService`; do not reclaim

1. `RunCoreAsync` sets `GIT_OPTIONAL_LOCKS=0` on every child, matching `LandingGit`. Mandatory locks (`add`, `commit`, `reset`, `read-tree`, `write-tree`) are unaffected; `status` and other reads stop creating `index.lock` at all, so a timeout kill of a read can no longer orphan one. Results are unchanged (git still hashes stale entries; it only skips writing the refreshed index back).
2. **Review repair (task 112287d8):** do not delete `index.lock` on the timeout path. A lock-taking child can be alive for seconds before `hold_locked_index` (git commit --only runs `core.fsmonitor` first). A foreign lock written in that window has mtime after child start, so the old reclaim deleted it. `GIT_OPTIONAL_LOCKS=0` already removes the incident's mechanism; S3 surfaces any leftover lock on the next land. After `TryKill`, wait for the child to exit (bounded 2 s) and return `timeout`.

Scope stays on `GitWorkspaceService`. `CardFileRepository` (sync disabled for these boards), `WorktreeManager` (`worktree add/remove/checkout-index`, worktree-local) and `LandingGit` (its kills are already fenced by `RepositoryChildJournal` and `interrupted_process_requires_inspection`) are not changed; S3 makes any lock they leave visible on the next attempt.

### D-4. Pre-admission hold; in-protocol refusal with the specific code

- Before admission (`AgentTaskLandService.RunRequestAsync`, after the writer check at `:221-229`): probe `task.RepoPath` and `task.WorktreePath`. `stale` -> `HoldAsync(task, request, "git_index_lock_stale", null, detail)`; `held` -> `HoldAsync(... "git_index_lock_held" ...)`; return `LandRunResult.Held`. The sweep re-picks the request every 5 s, so removing the file resumes the land without a re-POST, and a `held` lock that ages past the threshold flips to `stale` on a later pass (new hold episode, new notification).
- Inside `AgentTaskLandingProtocol`: `RequireNoIndexLockAsync(checkout)` immediately before `rebase`, `rebase --abort` (source worktree) and `merge --ff-only` (target checkout) -> `LandingRefusal("git_index_lock_stale")` / `("git_index_lock_held")` with the detail carried on the result. After a failed `merge --ff-only` or `rebase`, re-probe once; a lock now present makes the reason `git_index_lock_held` instead of `target_advance_failed` / `rebase_failed` (the abort path still runs for a rebase). The resolver's source fast-forward gets the same pre-check and reports through its existing `RefuseAsync` with the code and detail.
- Phase behaviour at `TargetAdvanceStarted` is unchanged: a lock refusal leaves the operation at phase 5 and the same-SHA re-POST resumes it (V-9). **Review repair:** a lock refusal at `RebaseStarted` (after `rebase --abort`, including a rebase that never started because the lock appeared when rebase was first seen) transitions to `Refused`, so a same-SHA re-POST replaces the operation instead of hitting `interrupted_rebase_requires_inspection`. `RecoveryPinned` / `Inspected` / `Prepared` still exclude lock codes from the Refused transition (V-8).

Rejected: holding from inside the protocol (would put a hold after `Attempt++`, contradicting the documented hold contract); converting these to `LandFailureDiagnostic` terminal failure codes (they are refusals of a known condition, not unexpected exceptions).

### D-5. Detail text

Hold detail and refusal detail use one formatter in the pure helper:

`Git index lock present at <path> (age <Nh Nm Ns>, <bytes> bytes). <liveness>. Remove it and the land resumes on the next sweep: Remove-Item '<path>'. Never remove a lock while a git process older than it is running.`

`<liveness>` is `No git process older than the lock is running; it is an orphan from an interrupted git write` for `stale`, and `A git process started before the lock is still running (PID <n>, started <utc>)` or `The lock is younger than <threshold> s` for `held`. Bounded to 2000 chars by `HoldAsync`; the event line is `land refused: git_index_lock_stale; <detail>`.

### D-6. One pure helper, one I/O seam

`server/Application/Services/GitIndexLock.cs` (static): `Observe(path, now, census)`, `Classify(observation, staleAfter)`, `FormatDetail(...)`. There is no reclaim helper. `ILandingGit.InspectIndexLockAsync(string checkout, CancellationToken)` returns `LandingIndexLockObservation(string Path, bool Present, DateTime? LastWriteUtc, long? Length, IReadOnlyList<(int Pid, DateTime? StartUtc)> CandidateHolders, string? Reason)`; `LandingGit` implements it with `rev-parse --path-format=absolute --git-path index.lock` plus `FileInfo` plus the census. `FixtureGit` inherits it, so land tests exercise the real implementation on scratch repositories. A probe failure returns `Reason` (`index_lock_path_error`) and the caller treats it as `held` (safe direction) rather than proceeding blind.

### D-7. Scripts: helper, NOTE, row, WARN; never refuse, never delete

`apphost-common.ps1` gains `Get-AppHostGitIndexLock -SourceRoot` (path via `git -C <root> rev-parse --path-format=absolute --git-path index.lock`, `LastWriteTimeUtc`, age minutes, `Get-Process git` census by `StartTime`, `Stale` boolean using the same 5-minute threshold) and `Format-AppHostGitIndexLockNote`. `restart-apphost.ps1` prints the NOTE next to the tracked-edits NOTE (`:163-165`) and again after step 3 if a lock now exists whose mtime is after the script's own start (`probably orphaned by this restart's kill`). `verify-dev-stack.ps1` adds an `Add-Result "Git index lock"` row (`OK absent`, `OK fresh (<age>)`, `FAIL stale ...`). `watchdog-apphost.ps1` writes one `WARN` line per fire while a stale lock is present. Exit codes of all three scripts are unchanged.

Rejected: a new server-side periodic sweep or alert (the admission check is the on-demand check; holds already reach the Attention feed; the card forbids new alert sinks); refusing the restart on a stale lock (a lock does not stop a build).

### D-8. Thresholds and settings

`GitSettings.IndexLockStaleAfterSeconds` (default 300) is the single threshold for the server and the documented value the scripts mirror as a constant. Census skew 2 s. No other new settings.

## Slices

### S1. Remove the source (`GitWorkspaceService`)

Files: `server/Application/Services/GitWorkspaceService.cs` (`RunCoreAsync`: env var; timeout catch: bounded wait, no delete), `server/Application/Services/GitIndexLock.cs` (new), `server/Application/Settings/GitSettings.cs` (+ validation where `GitSettings` is validated, or in `Program.cs` `Configure<GitSettings>`), tests `tests/Antiphon.Tests/Application/GitIndexLockTests.cs` (new), `tests/Antiphon.Tests/Application/GitWorkspaceServiceIndexLockTests.cs` (new, real git on `ScratchGitRepo`).

### S2. Landing git seam

Files: `server/Application/Interfaces/ILandingGit.cs`, `server/Application/Dtos/LandingDtos.cs` (`LandingIndexLockObservation`), `server/Infrastructure/Git/LandingGit.cs` (`InspectIndexLockAsync`), `server/Application/Services/LandFailureDiagnostic.cs` (add the template `git rev-parse --git-path index.lock` to `AllowedCommands`/`CommandTemplate` so a probe failure reports safely). Tests: `GitIndexLockTests` path cases on a `LandingGitFixture` (main checkout and linked worktree).

### S3. Land admission hold and protocol refusals

Files: `server/Application/Services/AgentTaskLandService.cs` (pre-admission probe and hold; append `result.Detail` in the unpublished branch at `:340-351`), `server/Application/Services/AgentTaskLandingProtocol.cs` (`RequireNoIndexLockAsync`, post-failure re-probe, `LandingProtocolResult.Detail`), `server/Application/Services/AgentTaskLandSourceResolver.cs` (pre-check before the source `merge --ff-only`). Tests: new `tests/Antiphon.Tests/Application/AgentTaskLandIndexLockTests.cs` on `LandingSafetyHarness` (V-5 to V-10).

### S4. Scripts

Files: `scripts/apphost-common.ps1`, `scripts/restart-apphost.ps1`, `verify-dev-stack.ps1`, `scripts/watchdog-apphost.ps1`, new `scripts/test-apphost-git-index-lock.ps1` (pattern of `scripts/test-apphost-lock-age.ps1`: scratch repo under `$env:TEMP`, PASS/FAIL lines, `... EXIT CODE: n` trailer, ASCII-only, PowerShell 5.1 compatible, never touches `C:\src\Antiphon` or `logs/apphost.*.lock`).

### S5. Documentation

`docs/orchestration-loop.md` (land request section: the two codes, that a hold self-clears once the file is removed, that the pipeline never deletes), `docs/ops-http.md` (`-Status` `Reason:` line now includes lock holds), `docs/apphost-runbook.md` (the NOTE, what to do, the `Remove-Item` rule), `docs/bootstrap.md` gotcha list (one entry: a hard-killed git child orphans `index.lock`; reads keep working, writes fail), `docs/testing-and-build.md` (how to simulate a stale lock: scratch repo, `rev-parse --git-path index.lock`, `File.SetLastWriteTimeUtc`; the new script test), `.claude/skills/antiphon-orchestrator/SKILL.md` section 6 item 5 (point at the new codes instead of "check for and remove a lock").

Suggested order: S1, S2, S3, S4, S5; commit and push after each slice with the real test counts in the message.

## Verification design

Profile: build with `--property:OutputPath=bin-c543/` and delete the `bin-c543` directories afterwards. Targeted runs with `dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/*/Antiphon.Tests.Application/<Class>/*"`; one full `Antiphon.Tests.Application` chunk before the Code report. Script test: `pwsh -File scripts/test-apphost-git-index-lock.ps1` and, unchanged, `scripts/test-apphost-lock-age.ps1` and `scripts/test-apphost-main-worktree-guard.ps1`.

Simulating a stale lock safely (every test below): only inside `ScratchGitRepo`, `LandingGitFixture` or `LandingSafetyHarness` temp directories; resolve the path with `git rev-parse --path-format=absolute --git-path index.lock` in the checkout under test; create it with `File.WriteAllBytes(path, [])`; age it with `File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromHours(1))`; no test ever reads or writes `C:\src\Antiphon\.git`. Liveness cases inject a census list into the pure classifier instead of spawning long-lived git processes; the one real-process case (V-4) uses the repository's proven sleeping pre-commit hook pattern (`CardFileGitFailureAcceptanceTests`) with a 1 s service timeout.

### Acceptance guards

| Id | Guard |
|---|---|
| A-1 | A `git status` issued by `GitWorkspaceService` never creates `index.lock` (index mtime unchanged after a stat-stale status). |
| A-2 | After `GitWorkspaceService` kills a child on timeout, it does not delete `index.lock` (own leftover or foreign). S3 surfaces any leftover on the next land. |
| A-3 | A pre-existing `index.lock` in the repository or source worktree holds a land before admission with `git_index_lock_stale` (old, no candidate holder) or `git_index_lock_held` (young, or a candidate holder alive); `Attempt` stays 0, no operation row is created, the verifier is not called. |
| A-4 | Removing the file makes the next sweep run the land to its normal outcome with no new POST; a `HeldReleased` event precedes it. |
| A-5 | A lock that appears after admission in the checkout about to be mutated refuses with the specific code before the mutation runs; a lock that appears during the mutation turns the failure into `git_index_lock_held`; the operation phase and resumability are as today. |
| A-6 | `update-ref` advances (target branch not checked out) are not probed and not blocked by a lock in the repository. |
| A-7 | Every hold detail and refusal event names the lock path and contains `Remove-Item '<path>'`; `LastReason` stays the bare code. |
| A-8 | No code path in the land service, protocol, resolver or scripts deletes an `index.lock` it did not create. |
| A-9 | `Get-AppHostGitIndexLock` reports absent/fresh/stale correctly on a scratch repo and never deletes; restart/verify/watchdog exit codes are unchanged by a present lock. |

### Tests (V) and regression pins (R)

| Id | Test | Decisive assertion |
|---|---|---|
| V-1 | `GitIndexLockTests.Classify_matrix` (`[Arguments]`: absent; age 10 s; age 600 s with census `[(pid, mtime-5s)]`; age 600 s with census `[(pid, mtime+5s)]`; age 600 s with an unreadable start time; age exactly `staleAfter`; age `staleAfter - 1 s`) | `None`, `Held`, `Held`, `Stale`, `Held`, `Stale`, `Held`. |
| V-2 | `GitIndexLockTests.Path_resolves_main_and_linked_worktree` on `LandingGitFixture` | `InspectIndexLockAsync(Repository).Path` ends with `\.git\index.lock`; `InspectIndexLockAsync(Source).Path` contains `\worktrees\` and ends with `\index.lock`; both `Present == false` before creation and `true` after. |
| V-3 | `GitWorkspaceServiceIndexLockTests.Status_never_takes_the_optional_index_lock` on `ScratchGitRepo`: commit, sleep 1.1 s, touch a tracked file, record `.git/index` mtime, `TryGetChangesAsync` | Result succeeded; index mtime unchanged. Control in the same test: raw `git status` via `ScratchGitRepo.GitInAsync` after another touch changes it. |
| V-4 | `GitWorkspaceServiceIndexLockTests.Timeout_kill_does_not_delete_index_lock`: service with `TimeoutSeconds = 1`; sleeping pre-commit hook (8 s); `CommitOnlyAsync(repo, ["b.txt"], ...)` | Code `-1`, stderr `timeout`; `hook-started` exists; no `Reclaimed orphaned git index lock` log; `rev-parse HEAD` unchanged; after deleting any leftover lock and the hook, a following `CommitOnlyAsync` succeeds. |
| V-4b | `GitIndexLockTests.TryReclaimAfterKill_is_gone` | `TryReclaimAfterKill` / `IsLockTakingVerb` / `FirstVerb` are not on the type. |
| V-13 | `GitWorkspaceServiceIndexLockTests.Timeout_kill_does_not_delete_a_foreign_lock_created_during_fsmonitor_stall`: `core.fsmonitor` writes a marker then `ping -n 6`; `TimeoutSeconds = 2`; `CommitOnlyAsync(["b.txt"])`; after the marker and before our lock, write a foreign `index.lock` | timeout; foreign bytes still present; no reclaim log. |
| V-14 | `AgentTaskLandIndexLockTests.Lock_created_during_rebase_is_reported_held_and_same_sha_repost_lands`: move master so a rebase is required; `BeforeCommand` creates a fresh lock when `rebase` is first seen (not `--abort`); delete lock; re-POST same SHA | first: `git_index_lock_held`, `op.Phase == Refused`, no `interrupted_rebase_requires_inspection`; second: `Landed`, new operation id. |
| V-5 | `AgentTaskLandIndexLockTests.Stale_lock_holds_before_admission`: harness `InitializeAsync`, `AddSourceAsync`, stale lock in `Fixture.Repository`; `RunAsync()` | `LandRunResult.Held`; `request.State == Held`, `HoldReasonCode == "git_index_lock_stale"`, `HoldDetail` contains the path and `Remove-Item`; `Attempt == 0`; `OperationAsync() == null`; `Verifier.Calls == 0`; one `Held` notification. |
| V-6 | `..._Fresh_lock_holds_as_held_then_ages_to_stale`: lock with current mtime; `RunAsync()`; then age it; `RunAsync()` | first `git_index_lock_held`, second `git_index_lock_stale` with `HoldEpisode` incremented by one and a second `Held` event. |
| V-7 | `..._Removing_the_lock_resumes_the_land`: after V-5, `File.Delete(lock)`; `RunAsync()` | `HeldReleased` event; outcome `Landed`; `AssertRemoteSourceAsync` passes. |
| V-8 | `..._Source_worktree_lock_refuses_before_rebase`: move `master` in `Fixture.Repository` by one commit so a rebase is required; `FixtureGit.BeforeCommand` creates a stale lock in the source worktree's git dir when it first sees `rev-parse --git-path index.lock` for `Fixture.Source`; `RunAsync()` | Terminal `LandRefused` event whose detail starts with `land refused: git_index_lock_stale;` and names the path; `op.Phase == RecoveryPinned`; `Trace` contains no `rebase`; no `rebase --abort`. |
| V-9 | `..._Target_lock_refuses_before_ff_and_same_request_resumes`: `BeforeCommand` creates a stale lock in `Fixture.Repository` when it first sees the index-lock probe for that path at target advance; `RunAsync()`; delete lock; `RequestAsync(expectedSourceSha: same)`; `RunQueuedAsync()` | First: refusal `git_index_lock_stale`, `op.Phase == TargetAdvanceStarted`, `Trace` has no `merge --ff-only` and no `push`, target ref unchanged. Second: same operation id resumes, `Landed`, no `land_request_identity_conflict`. |
| V-10 | `..._Lock_created_during_ff_merge_is_reported_held`: `BeforeCommand` creates a fresh lock when it sees `merge --ff-only` in `Fixture.Repository` (after the pre-check) and lets the real command run | Reason `git_index_lock_held`, not `target_advance_failed`; detail names the path; `op.Phase == TargetAdvanceStarted`. |
| V-11 | `..._Update_ref_advance_ignores_repository_lock`: `git -C Repository checkout --detach` so no worktree holds `master`; stale lock in `Repository`; `RunAsync()` | `Landed`; `Trace` contains `update-ref` and no index-lock probe for the target; V-5's pre-admission probe is skipped for the repository when `TargetCheckoutPath` is null (assert the hold does not fire). **V-11s (Code):** pre-admission reads worktree registrations and probes the registered target checkout only when a worktree currently has the merge-target branch. `RepoPath` is not probed unconditionally, so a lock in a detached canonical checkout does not hold a land that will `update-ref` (A-6). A registration lookup failure falls back to probing `RepoPath` (safe direction). |
| V-12 | `scripts/test-apphost-git-index-lock.ps1`: scratch repo; T1 absent; T2 fresh lock; T3 lock aged 60 min with `Get-Process git` census empty of older processes; T4 note text; T5 wiring guard (`Select-String 'Get-AppHostGitIndexLock'` in restart, verify and watchdog scripts); T6 the helper never deletes the file | `$null`; `Present -and -not Stale`; `Stale`; contains path and `Remove-Item`; three hits; file still exists. |
| R-1 | `AgentTaskLandHoldVisibilityTests`, `AgentTaskLandRefusedRetryTests`, `AgentTaskLandPublicationTests`, `AgentTaskLandStageOutcomeTests`, `AgentTaskLandFailureDiagnosticTests`, `LandingProtocolHarnessTests` | Unchanged and green: existing hold codes, refusal codes, resume and identity behaviour. |
| R-2 | `DelegationWorktreeTests.a_commit_all_failure_on_a_live_worktree_still_fails` (`:800-822`) | Still `Failed` with `Committing the delegate's work failed` (a foreign lock in a worktree is never reclaimed by the merge-back path). |
| R-3 | `CardFileGitFailureAcceptanceTests`, `CardTaskFileServiceTests.Index_lock_is_git_error_then_next_sync_after_removal_commits` | Unchanged: `CardFileRepository` behaviour is out of scope. |
| R-4 | `GatedCommitServiceTests` and the other `GatedCommit*` suites | Unchanged outcomes under `GIT_OPTIONAL_LOCKS=0`. |
| R-5 | `scripts/test-apphost-lock-age.ps1`, `scripts/test-apphost-main-worktree-guard.ps1` | Still exit 0. |

### Positive controls for the Mutation stage (method-scoped)

| PC | Mutation | Expected red |
|---|---|---|
| PC-1 | Remove `GIT_OPTIONAL_LOCKS` from `RunCoreAsync` | V-3 |
| PC-2 | Restore `TryReclaimAfterKill` (delete a young lock after a timeout kill) | V-4b (API present) and V-13 (foreign lock deleted) |
| PC-3 | In `Classify`, treat a census entry with `StartUtc <= mtime` as not a holder | V-1 (third arm) |
| PC-4 | Skip the pre-admission probe | V-5, V-6 |
| PC-5 | Remove the post-failure re-probe after `merge --ff-only` | V-10 (`target_advance_failed` returns) |
| PC-6 | Probe before `update-ref` too | V-11 |
| PC-7 | Return `held` for a stale lock in `Get-AppHostGitIndexLock` | V-12 T3 |
| PC-8 | Exclude lock codes from the Refused transition at `RebaseStarted` | V-14 re-POST hits `interrupted_rebase_requires_inspection` |

## Risks and notes for Code

- `Process.GetProcessesByName("git")` on a busy machine returns many short-lived processes; only those with `StartTime <= mtime + 2 s` count, so a lock older than five minutes is `stale` unless a genuinely long-running git exists. If that ever mis-holds, the detail line names the PID.
- Time: compare `LastWriteTimeUtc` with `TimeProvider.GetUtcNow()` (the harness clock) for age, but the file mtime is real time; tests age the file, not the clock.
- `LandingGit.InspectIndexLockAsync` must not throw for a missing checkout; return `Reason` and let the caller treat it as `held`.
- The refusal path inside the protocol runs under the repository lease; the pre-admission probe runs after the lease is acquired too, so a concurrent land cannot be the holder.
- Keep all four scripts ASCII-only; `Get-Process git` needs `-ErrorAction SilentlyContinue`; `StartTime` can throw for another user's process (treat as a candidate holder).
- Do not widen `Git:TimeoutSeconds` to make V-4 pass; the test sets 1 s on its own instance.

--- next stage ---
next: code
handoff: Implement S1-S5 of docs/superpowers/plans/2026-09-18-card-0543-stale-index-lock-plan.md: GIT_OPTIONAL_LOCKS=0 plus own-orphan reclaim in GitWorkspaceService, ILandingGit.InspectIndexLockAsync, pre-admission holds git_index_lock_stale/held and in-protocol refusals with path detail, script NOTE/row/WARN, docs; run V-1..V-12 and R-1..R-5.
artifact: docs/superpowers/plans/2026-09-18-card-0543-stale-index-lock-plan.md
