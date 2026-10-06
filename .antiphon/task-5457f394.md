# CARD-1087 rollout path after the census fix landed

Date: 2026-10-06. Investigate task `5457f394`. Read-only: no `deploy-server2.ps1` phase, no pull, no restart, no server2 change.

## Outcome

The rollout paused at gate 6 cannot finish on `d985af05` with that checkout's script. Continue at gate 6 on `origin/master` `0c79cce42ede008ba0c28dc2be4c533f6ffc255b` after the canonical desktop checkout and the desktop AppHost are on that commit. Do not `retire-temp` and do not start a fresh `deploy-temp` beside the drained old runner. `server2-temp` stays the accepting rollback at `d985af05`, with its live sessions, until upgraded `server2` passes gate 7.

`git ls-remote origin refs/heads/master` returned `0c79cce42ede008ba0c28dc2be4c533f6ffc255b`. This mirror's `origin/master` ref is stale (`391302f125b1b2d2579d7033759f8add57cb6e3a`, an ancestor of that commit). Nothing was fetched or pulled.

## Confirmed mechanism

Debug task `9501d319` on 2026-10-06 measured the refusal against the desktop API (`docs/superpowers/plans/2026-10-06-card-1087-recycle-census-plan.md` at `0c79cce`, Ground truth). The old wrapper matches that measurement.

At `d985af05`, `scripts/deploy-server2.ps1`:

- `Get-RecycleProjectId` (lines 384-388) keeps a row only when `gitRepositoryUrl -eq 'https://github.com/michal-ciechan/Antiphon.git'`. Any other count throws bare `RecycleTaskCensusUnknown`. The live Antiphon row `d4ea7ae9-e769-474b-95b9-aa25fbc1303f` has `gitRepositoryUrl=""` and `localRepositoryPath="C:/src/Antiphon"` (plan, same table).
- `Assert-RecycleTaskCensus` (lines 391-478) reads `/api/agent-tasks?projectId=<id>&unscoped=include&includeChecks=true` with no status or land filter, then `GET /api/agent-tasks/{id}` for every row. `Invoke-RecycleRead` (line 380) uses `-TimeoutSec 15` and rethrows every failure as bare `RecycleTaskCensusUnknown` (line 381). The measured list was 12,143 items, 18.5 MB, 90.8 s. The projects read alone was 10.7 s.
- Line 450 treats `Failed` on the target runner as `RecycleBoundTasks`. The same live read counted 223 `Failed` rows on `server2` and 17 on `server2-temp` inside the terminal set (3,586 `Failed`+`Canceled`). Open rows were 3 Dispatched + 1 Blocked, all on `server2-temp`.

`check-census` is not in that script's `-Phase` set (lines 7-8). Passing `-Sha 0c79cce42ede008ba0c28dc2be4c533f6ffc255b` to the old script would still execute this census, and the runbook requires `<sha>` to be the canonical checkout `HEAD` (`docs/docker-stack.md` at `0c79cce`, lines 4-6).

At `0c79cce` the wrapper and the host census both change:

- Reads are two filtered closures, 60 s each (`scripts/deploy-server2.ps1:435`; `scripts/c590-remote.sh:3954` and `:4017`). Open work is `Queued/Dispatched/Working/Blocked`. Pending lands use `landPending=true`. Terminal rows without a pending land are not bound (`docs/docker-stack.md:213-220`).
- Project resolution is canonical URL `https://github.com/michal-ciechan/antiphon` or the script checkout path (`deploy-server2.ps1:465-550`).
- `landPending` is not a supported list key at `d985af05` (`AgentTaskScope.cs` has no such key). Unknown keys are HTTP 400 `unknown_query_parameter` (`AgentTaskEndpoints.cs:540-552`, `UnknownQueryParameterException.cs:4-13`). The new census against the server that is still on `d985af05` stops with `RecycleTaskCensusUnknown cause=Http status=400`. The landed doc says that 400 means the server predates the filter: restart AppHost and verify `/api/version` against `HEAD` (`docs/docker-stack.md:37-38`).

Census refusal on `redeploy-old` happens after the zero-counter and drain checks and before `deploy-parent` (`deploy-server2.ps1:1036-1045`). The plan records that no `-ResumeRecycle` journal existed. Prior evidence directories `c727af590e43c835` and `c727be3620835edd` are receipts, not permission to resume.

## (1) Old sha cannot continue

Continue only on the new master sha, with the new script, from a canonical checkout whose `HEAD` is that sha, and only after the desktop API is that sha. The old script cannot pass the census for the three reasons above.

## (2) Sequence from the live state

Live state used here is the brief plus the plan's routing read: old `server2` drained and idle (`sessions` 0, `runnerSessions` 0, `queuedTasks` 0, `draining=true`, `redirectTo=server2-temp`, build `4358939ecd85`); `server2-temp` accepting at `d985af05` with live sessions. `drain-old` writes `retireWhenIdle=false` (`deploy-server2.ps1:1006-1009`). `redeploy-old` also requires `retiredAt` null, `acceptingNewWork=false`, and temp `acceptingNewWork=true` (`:1036-1040`). Those last flags were not re-read from this mirror.

`deploy-temp` of a new sha is refused now. The temp build is not the requested sha, so the phase requires old `dispatchEligible`, `acceptingNewWork`, not draining, and not retired (`OldRunnerRedirectNotEligible`, `:956-958`) and an absent temp Compose project (`TempContainersRemain`, `:936`). Old is draining. Temp containers exist.

`retire-temp` is refused now. It requires temp already retired, redirected to `server2`, not accepting, and at zero work (`Assert-TempCleanupStatus`, `:740-762`), and old accepting (`Assert-TempCleanupMain`, `:1095-1096`). The runbook forbids draining the only accepting runner (`docs/docker-stack.md:8-9`). The CARD-0957 abandon procedure is only for a failed `deploy-temp` hold with host absence; a remaining container stops that procedure and it never removes one (`:174-186`).

The phase the refusal left open is gate 6 `redeploy-old`. When `buildVersion` is not `<sha>`, it admits a drained idle old runner redirected to an accepting temp and does not compare temp's `buildVersion` to `<sha>` (`:1026-1042`). Temp therefore keeps serving `d985af05`, including its current sessions, while `deploy-parent` replaces old with `0c79cce`. That image contains CARD-0817, CARD-1065, CARD-1076, CARD-1079, and CARD-1067 (those commits are ancestors of `0c79cce` and not of `d985af05`). Temp never canaried them. Gate 7 is the smoke of that new old image. If it fails, drain old back to temp (`docs/docker-stack.md:108`).

After gate 7, `drain-temp -WaitIdleMinutes 240` is the wait for temp's sessions. It requires old `buildVersion` equal to `<sha>` (`:1065-1066`). The deadline throws `TempContainerExitTimeout` and removes nothing live (`docs/docker-stack.md:109`; `Wait-TempContainerRetirement`, `:824`). The script default without the flag is 480 minutes (`deploy-server2.ps1:12`). Pass `240`.

The four-hour seat release is gate 5, and only for remaining **server2** sessions after `OldRunnerStillBusy` (`docs/docker-stack.md:106`): `scripts/runner-slots.ps1 release -RunnerId server2 -SessionId <guid> -Reason 'CARD-0934 four-hour drain cap'`. Old's three counters are already zero, so that wait and that release are already satisfied. Do not rerun them. Do not release seats on `server2-temp`. The same sentence says not to kill sessions on other runners.

Clearing old's drain to roll back to build `4358939ecd85`, then hand-draining temp, is a different and worse recovery. `drain-temp -Sha d985af05` cannot do it, because old is not at `d985af05`.

### Canonical checkout

Run every command from `C:\src\Antiphon`, never from a linked worktree, and never with `-AllowWorktree` (`docs/orchestration-loop.md:31`; `scripts/restart-apphost.ps1:77-79`).

`<sha>` must be that checkout's full lowercase `HEAD` (`docs/docker-stack.md:4-6`). `scripts/verify-card0849-caches.ps1:16-17` throws if `HEAD` differs. `provision-host-jq` is the only deploy phase that enforces a clean tree: `HEAD` equals `-Sha` and `git status --porcelain --untracked-files=all` is empty, or it throws `HostJqCanonicalSourceRequired` (`deploy-server2.ps1:52-66` at `0c79cce`). `check-census`, `check-host-jq`, and `redeploy-old` do not.

`canonical_checkout_dirty` is the land refusal when `git status --porcelain=v1 -z --untracked-files=all --ignore-submodules=none` produces any output (`server/Application/Services/AgentTaskLandingProtocol.cs:482-484`). That is why lands published `origin/master` and left `C:\src\Antiphon` at `d985af05`. It is not a deploy-script status. `restart-apphost.ps1:169-170` does not refuse tracked edits; it warns that SHA equality does not prove uncommitted behavior is what got built. Untracked files are outside that check. This mirror cannot see `C:\src\Antiphon`, so which dirt is present is unverified.

The paused refusal is the state that allows the pull: no phase is in flight, census stopped before `deploy-parent`, and no land may be running. Record `GET /api/version` equal to `d985af05` first. `git pull --rebase` is the autonomous advance (`docs/orchestration-loop.md:18-33`). If Git stops on local changes, stop. Do not stash, reset, or touch the user's untracked files. After the pull, `HEAD` is ahead of `/api/version` until the restart; the same paragraph requires the new match after restart.

## (3) Project row

The census still needs exactly one non-archived project (`deploy-server2.ps1:537-544`). An empty `gitRepositoryUrl` normalizes to no URL match (`:467`, `:525`). A path match is enough. On Windows the comparer is full path, slash-normalized, trailing separator trimmed, case-insensitive (`:478-486`). `C:/src/Antiphon` equals repo root `C:\src\Antiphon`, so `resolvedBy=path`. `C:/Antiphon` does not equal that root. The plan's D-1 says the live row resolves by path with no PUT. This mirror did not repeat `GET /api/projects`; that row is the 2026-10-06 ~03:30Z read. `check-census` is the confirmation. `RecycleProjectUnresolved cause=NoMatch` means neither identity matches this checkout. Do not partial-PUT the project (`docs/docker-stack.md:31-36`).

## (4) Who may do what

Autonomous (`docs/orchestration-loop.md:17-50`), from the main checkout, after no land is in progress and both AppHost locks are clear: `git pull --rebase`, `scripts/restart-apphost.ps1`, the runbook preflights `check-census` and `check-host-jq` (they do not deploy or restart; `docs/docker-stack.md:16-27` and `:77`), then `redeploy-old`, gate 7's smoke and one sanctioned Plan canary, `drain-temp`, and `retire-temp` once scheduling is back on main. In-container cleanup of a running main and removal of retired temp volumes are autonomous only when every recycling precondition passes. `provision-host-jq` only after a real `HostJqMissing`.

Human (`:52-61`): `Reset`, Docker prune, runner-state or cache-volume recycling, deleting markers or donor tars, removal while a precondition fails, killing sessions or always-on agents, budget or pin or settings changes, spend beyond the sanctioned canary, secrets, touching user untracked files, `-AllowWorktree`, `-KillSessions`, and touching the standing `server2` container outside the rolling phases. The GitHub token file is operator-provisioned (`docs/docker-stack.md` startup-identity table at `0c79cce`). A missing token warns `GithubTokenAbsent` and is not a census blocker. The only standing exception to the kill rule is the CARD-0934 four-hour release of remaining **server2** sessions, which this idle old runner does not need.

## (5) Next three actions

From `C:\src\Antiphon`, PowerShell 7, after no land is in progress. Do not pass `-AllowWorktree`.

1. Record the match, then pull. Expect `/api/version` `d985af05b5ace2f13ec3f0d886c15992e9c13c3f` before the pull and `HEAD` `0c79cce42ede008ba0c28dc2be4c533f6ffc255b` after. If the pull refuses on local changes, stop.

```powershell
Invoke-RestMethod http://localhost:17202/health
Invoke-RestMethod http://localhost:17202/api/version
git pull --rebase
git rev-parse HEAD
```

2. Restart, then require `/health` and `/api/version` sha `0c79cce42ede008ba0c28dc2be4c533f6ffc255b`. Inspect `logs/apphost.restart.lock` and `logs/apphost.launch.lock` first. Exit 3 means stop and inspect, not a second launch.

```powershell
pwsh -NoProfile -File scripts/restart-apphost.ps1
Invoke-RestMethod http://localhost:17202/health
Invoke-RestMethod http://localhost:17202/api/version
```

3. Read-only census. Exit 0 must print `RECYCLE_PROJECT id=<guid> name=Antiphon resolvedBy=path` (or `both` if a URL was added), then two `RECYCLE_CENSUS` lines, then `Census check complete:`. Proceed toward `redeploy-old` only when the `server2` line has `boundOpen=0` and both lines have `landPending=0`. `server2-temp` `boundOpen` may be nonzero. `cause=Http status=400` means the restart is not serving `0c79cce`. Retain the printed `.antiphon/rolling-server2/<run-id>/` receipts.

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 0c79cce42ede008ba0c28dc2be4c533f6ffc255b -Phase check-census
```

`check-census` authorizes no recycling. The decision before `redeploy-old` is whether to upgrade old to `0c79cce` while temp remains the `d985af05` rollback.

## Not done, noted

No code change. The fix is already `origin/master`.

## Remaining uncertainties

- `C:\src\Antiphon` dirt was not visible here, so whether `git pull --rebase` will run cleanly is unknown.
- `retireWhenIdle`, `acceptingNewWork`, and `retiredAt` on the two runners were not re-read.
- The project row was not re-read after the plan's 03:30Z observation.
- Temp's exact session count is "a few" until `check-census` prints `boundOpen`.
