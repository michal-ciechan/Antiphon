# Staged server2 rollout of rc/2026-11-29: commands

Investigate task `2d8f8392`, read-only, 2026-11-19. No deploy, restart, compose up/down, or prune was run. Live reads are labeled with the time they were taken. Anything this runner could not see is marked UNOBSERVED.

Target: `rc/2026-11-29` = `26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248`.
The Claude Code pin on that commit is `1285ab3ee10abada5fe844fd8fb049ccaf7d5699` (`CLAUDE_CODE_VERSION=2.1.327`, SHA-256 `343c744baa14bfd38286a7cb90871051f04379956da6c5b803a9e66019a76ac9`). `git merge-base --is-ancestor` of that pin against the cut is exit 0.
Standing runner to replace: `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` (image tag convention `antiphon-server2/session-testing:e229fb4d3fb8`). Live `claude --version` inside that runner is `2.1.280 (Claude Code)`.
Compose diff `e229fb4d..26a99f1a` touches `docker-compose.server2-runner.yml` (S0 `HistoryEnabled=false` replaced by the S1 history path and binding literals), `docker-compose.server2-runner.temp.yml` (adds `HistoryEnabled=false`), and `docker/session-runner-grok/Dockerfile` (2.1.280 to 2.1.327). The `build-slots` service block is not in that diff.

Run every phase from the desktop canonical checkout, with `ANTIPHON_API` set. `scripts/deploy-server2.ps1` exits 2 when `ANTIPHON_API` is unset. There is no `localhost:17202` default. Do not run it from a worktree and do not pass `-AllowWorktree`. Do not pass `-Phase all`. Do not pass `-RecycleVolumes`. Do not pass `-RefreshClaudeToken`.

## Stop before any recreate

Two facts block a standing-runner recreate. They do not block writing this note.

1. **The server binding is not effective.** At 2026-11-19 15:28:14Z, `GET /api/hosts/server2/load` returned `hostId=server2` and `physicalHostKey=null`. `GET /api/hosts/server2/load/history` for the five minutes ending 15:28:18Z returned `gaps=["binding-unknown"]`, `items=[]`, `complete=false`. On this cut, `HostLoadHistoryService.Binding` returns a row only when exactly one binding covers now (`server/Infrastructure/HostLoad/HostLoadHistoryService.cs:176-180`), and history returns `binding-unknown` before it reads samples (`:73`). The desktop user-secrets file was not read. Absent, expired, and overlapping rows are not distinguished. The effective witness is not `physicalHostKey=server2`, revision 1, epoch `58d738a7-044a-4089-a66f-42503861b8bf`.
2. **`redeploy-old` refuses while server2 still has open work or an in-flight land.** The census runs before the recycle skip (`scripts/deploy-server2.ps1:1430-1444`). `RecycleBoundTasks` is immediate. `RecycleLandInFlight` is the only refusal it waits out, for `-LandWaitMinutes` (default 30). A `NeedsResolution` land does not clear by waiting.

`bash scripts/c1893-runner-hold.sh 26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248` printed `RELEASED` (exit 0). That is the compose-shape hold, not the server binding. Temp sets `SessionRunner__HostStats__HistoryEnabled=false`, and the collector returns before opening a spool when that flag is false (`src/Antiphon.SessionRunner/HostLoadCaptureService.cs:63`). The standing S1 file has no `HistoryEnabled` line, so the default is true and `redeploy-old` is the recreate that opens `/state/host-load`. Do not run `redeploy-old` until a fresh `GET /api/hosts/server2/load` shows `physicalHostKey=server2`. Samples ingested before a matching primary binding stay unbound.

Making the binding effective is a desktop user-secrets write plus an AppHost restart (`docs/superpowers/plans/2026-11-16-card-1893-activation-plan.md`, G-1, no hot reload). A Windows Full run owns the desktop from 2026-11-19 14:30Z. This task does not restart anything.

## What was true at 15:28Z

AppHost `GET /api/version`: `version=26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248`, capabilities `land-v2`, `operator-shutdown-v1`. That is the cut.

`GET /api/runner-defaults`: revision 2, `globalRunnerId=server2`, no kind overrides, updated 2026-09-26. Unpinned work prefers server2, so the drain redirect can promote temp. Do not PUT this route.

`server2`: `buildVersion=e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`, epoch 1, store `f519bd08-e53a-47d1-adb1-2ab33475446f`, boot `0cbe03eb-02bf-4c85-b2dc-b716b8c27a49`, available, eligible, accepting, not draining, `redirectTo=null`, `sessions=5`, `runnerSessions=5`, `queuedTasks=1`. Codex CLI `0.160.0` checked 15:25:58Z, not stale. Heartbeat 15:28:06Z. `drainReason` is the historical `CARD-0727 rolling upgrade complete`. Process env has `SessionRunner__HostStats__HistoryEnabled=false` and no `HistoryPath`, `PhysicalHostKey`, `BindingRevision`, or `SourceEpoch`. Container hostname `7c9c24dac62d`, uid 1654.

`server2-temp`: retired placeholder. `retiredAt=2026-11-18T04:46:10.910953Z`, `retireReason=idle`, `draining=true`, `redirectTo=server2`, `retireWhenIdle=true`, `drainedAt=2026-11-18T03:59:31Z`, offline (`disconnectReason=unavailable`), `sessions=0`, `queuedTasks=0`, `runnerSessions=null`, `buildVersion=null`. Slots route 503 `phone_home_unavailable`. This is the shape `deploy-temp` accepts only when the four temp volumes are absent and no temp container remains. Both are UNOBSERVED (see access).

Desktop runner: `buildVersion=null`, unavailable, not accepting. `desktop-win` status 404. desktop-win phase 1 is not part of this rollout.

Slots on server2, capacity 10, occupied 5, all `Running`, `occupiesCapacity=true`, `orphan=false`:

| Session | Age at the read | Open task |
|---|---:|---|
| `e95a7afe-1e80-4ec0-99d9-8895b599831b` | 68619 s | none. This seat holds `drain-old`. Which task it belongs to is UNOBSERVED |
| `e76699ab-d345-49d1-b399-172643131596` | 4697 s | `066a9073` Code CARD-1744, Dispatched, Grok High |
| `87dc95bd-0aee-46fa-8fa7-560b02f18c07` | 256 s | `2d8f8392` this Investigate |
| `1eea8465-6752-45bc-9708-5bddd21c3fe7` | 172 s | `f1b04f65` Code CARD-1836, Dispatched, Grok High |
| `43e4a4f8-e9c0-46a5-b2f5-fe1e37aa1b64` | 157 s | `a5e3ce5c` Investigate CARD-2005, Dispatched, Codex Frontier |

Also bound to server2 and not in those five seats:

| Task | Status | Why it matters |
|---|---|---|
| `7951bfb5` Review CARD-0824, ClaudeCode High | Queued, no worktree yet | `queuedTasks=1`. Holds `drain-old` until it leaves the server2 queue |
| `22a5524a` Code CARD-1744, Grok High | Blocked since 2026-11-18. Land `NeedsResolution` since 2026-11-18 22:08Z. Slot `ws-01-d4` | Open and bound, so `redeploy-old` refuses. `NeedsResolution` is an in-flight land state. Waiting does not clear it |
| `3f0bbab5` Code CARD-0824 | Succeeded. Land `Running` since 2026-11-19 15:19:54Z. Slot `ws-04-d2` | Not an open status. `RecycleLandInFlight` waits up to 30 minutes, then refuses if the land is still Running |

Leased slots whose tasks are already terminal and have no land: `ws-02-d3` `4baa5d75` Review CARD-2000 Succeeded; `ws-06` `de01ed8b` Code CARD-0824 Succeeded; `ws-10-d2` `a7e556cc` Code CARD-1981 Failed. They do not hold the session counters. They do not by themselves trip `RecycleBoundTasks`.

## Worktrees and unpublished work

Read-only `git status --porcelain` and `rev-list --count origin/<branch>..HEAD` against the origin ref already in each checkout. No fetch. A 12-second timeout counts as UNOBSERVED, not as clean. Compared with the local tracking ref, not a fresh `ls-remote`.

Leased slots `ws-01-d4`, `ws-02-d3`, `ws-03-d2`, `ws-04-d2`, `ws-05`, `ws-06`, `ws-07`, `ws-08`, `ws-10-d2` were clean and `ahead=0` at scan time. `ws-07` is this task and becomes dirty when this note is committed. That commit is the deliverable.

Dirty on the main work volume, same two trees as the 2026-11-13 note:

- `task-d989d9d8` at `56e96eabf698`, untracked `docs/investigations/2026-11-10-run-ae84478d-profile-16-HostLoadNativeOverhead-red-windows-failure.md`, `ahead=0`.
- `task-f23857e7` at `50c36bf27bd0`, modified `docs/investigations/2026-11-08-why-missed-server-repository-mirror-windows.md`, `ahead=0`.

`/work/repos/antiphon` is `master` at `0e7dd390520a`, `ahead=0`, and the index has **9218 staged deletions** (`D ` only). That is a dirty tree. Default `redeploy-old` does not recycle the main volume, so this does not trip the default phase. `-RecycleVolumes` would. Do not pass it.

Timed out, so cleanliness is UNOBSERVED: `c0824-compose-out`, `task-3e32f1e6-s2`, `task-4292c713`, `task-741fb04b`, `task-a2845b6c`, `task-bee64303`, `task-da90742c`, `task-ec9dfd70`, `task-ed0f7942`, `task-f4320f2f`, `task-f87fdda9`. Every other `/work/worktrees` entry that finished was clean with `ahead=0` or a detached HEAD with no upstream (`ws-09`, `ws-11`, `ws-12`).

`RecycleUnpublishedWork` on the 2026-11-13 gate 9 was not these trees. It was 16 reflog-only commits in the **temp** mirror `repos/antiphon`. Default this rollout keeps the main volume. Gate 9 audits the temp volume after temp has run. Expect that audit to see whatever temp clones and whatever tasks on temp leave behind, including fixture git repos. Nested selector fixtures are classified `disposable-fixture classifier=selector-evidence-v1` on the fixed audit. A new `ls-remote` 128 is one fresh preview, then a Debug task. `RecycleUnpublishedWork` and `RecycleWorktreeDirty` stop the rollout. Do not apply.

## Disk, load, and access

Inside the standing container, kernel `4.15.0-213-generic`, `nproc=24`. `/proc/loadavg` was `2.46 5.41 8.21 5/3853 5183`. `/`, `/work`, `/state`, and `/tmp` are the same LV: 738901176 KiB, 459958556 used, **248853820 KiB available (237 GiB, 65%)**. The 2026-11-13 note recorded 128 GiB free at the failed gate 9 and about 8 GiB for a temp deploy. 237 GiB is above the 20 GiB cache-maintenance headroom and the 50 GiB `WorkspaceDiskAlarm` floor. It is not a blocker. Per-volume sizes (`docker system df -v`) are UNOBSERVED.

`ssh -o BatchMode=yes -o ConnectTimeout=12 mc@server2` from this container added the host key and then returned `Permission denied (publickey)`. This container has no user ssh config. The nested daemon is `docker context` `default`, `Name=7c9c24dac62d`, Server 27.5.1. That is not the host daemon.

Deploy scripts run on the desktop. `scripts/c590-real.ps1` uses `ssh -o BatchMode=yes -o ConnectTimeout=30 -o ServerAliveInterval=30 -o ServerAliveCountMax=240 mc@server2`. The desktop account is what has the key. From there, before gate 1, read only:

```powershell
ssh mc@server2 'echo LOAD; cat /proc/loadavg; echo DF; df -Pk /; echo DOCKER_DF; docker system df; echo PS; docker ps --format "{{.Names}} {{.Image}} {{.Status}} {{.ID}}"; echo VOLUMES; docker volume ls -q'
ssh mc@server2 'docker ps -aq --filter label=com.docker.compose.project=antiphon-runner-temp'
ssh mc@server2 'test -f /home/mc/antiphon-server2/retirement-origins.json && echo registry-present || echo registry-absent'
```

Stop before `deploy-temp` if any `antiphon-runner-temp_` volume exists (`TempVolumesRetained`) or if any temp container id is printed. A missing `retirement-origins.json` does not block gate 1. It blocks gate 9 of a non-empty temp work volume with `RecycleGitAuditUnknown reason=registry-missing`. Only the operator writes that file. Do not invent it. Do not print anything under `secrets/`.

`PhoneHome__WorkspaceSlots__Enabled=true` is set in the running standing process. The host file `secrets/stack.env` was not read (UNOBSERVED). The named phases preserve each stack file's own value. A missing key deploys as `false`. Confirm the standing file still says `PhoneHome__WorkspaceSlots__Enabled=true` before `redeploy-old`, and the temp file before `deploy-temp` if temp should use slots.

## Ordered commands

`<sha>` is `26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248`. The script default for `-WaitIdleMinutes` is 480. The rollout contract is 240, so pass it. Keep dispatching the whole time. New work goes to `server2-temp` from the moment temp is accepting until `redeploy-old` exits 0 and the gate 7 smoke and `Both` have passed. Then send new work to `server2` and start `drain-temp`. Do not drain both. Do not drop below the in-flight target of 6 because a rollout is running. Lands on the desktop keep running. The census waits only for a land bound to the runner being recycled.

Record the standing container before gate 1, and again after, and require the same id and start time:

```powershell
ssh mc@server2 'docker inspect -f "{{.Id}} {{.Image}} {{.State.StartedAt}}" antiphon-runner-session-runner-1'
```

Before `retire_superseded_server2_images` can drop the old tag, record that image id and the state-init image id and keep them under a tag whose repository is not `antiphon-server2/server` or `antiphon-server2/session-testing`. The retire loop deletes every other tag in those two repositories (`scripts/c590-remote.sh:1076-1095`). A rollback tag inside those names is deleted with the old sha12 tag. The previous image the rollback redeploy needs is `antiphon-server2/session-testing:e229fb4d3fb8` for source `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`.

| Step | Command | Stop |
|---|---|---|
| Hold | `git fetch origin` then `bash scripts/c1893-runner-hold.sh <sha>` | Anything but `RELEASED`. Already `RELEASED` on this cut |
| Census | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase check-census` | Non-zero. Expect `RECYCLE_PROJECT` and two `RECYCLE_CENSUS` lines. GET only, no SSH. Record `boundLandPending`. `22a5524a` and `3f0bbab5` were bound lands at 15:28Z |
| Host jq | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase check-host-jq` | Not qualified, and not `HostJqMissing`. Pin remains jq 1.7.1 SHA-256 `5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5`. Do not install by hand |
| Provision, only after `HostJqMissing` | `... -Phase provision-host-jq` then `check-host-jq` again. A second provision must say `installed=false` | Invalid jq, PATH shadow, receipt failure |
| Cache fixture | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture -Sha <sha>` | Non-zero. On 2026-11-13 this was about 14 minutes, `groups=9 controls=32` |
| Cache inventory | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Inventory -Sha <sha>` | Non-zero. `Reset` is not the recovery |
| Status | `pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId server2` and `server2-temp` | Old not accepting at `e229fb4d…`. Temp not the retired offline zero-work shape. A live temp with unknown `runnerSessions` stops the phase |
| 1. deploy-temp | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase deploy-temp` | Old container id or start time changed. Temp fails. `TempVolumesRetained`. `TempContainersRemain`. `OldRunnerRedirectNotEligible`. Leave old accepting. Do not touch the standing container to fix temp |
| 2. Smoke temp | The tool block below for `server2-temp` / project `antiphon-runner-temp`, then the Claude canary | Version, tool, or canary failure. Do not run `drain-old` |
| 3. Mark temp primary | No command. `drain-old` writes `server2.draining=true` and `redirectTo=server2-temp`. It does not PUT `/api/runner-defaults` | Temp not accepting, or the canary failed |
| 4-5. drain-old | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase drain-old -WaitIdleMinutes 240` | Refusal before the drain: old stays accepting. `OldRunnerStillBusy` at 240 minutes is a report, not permission to redeploy. Release only the remaining **server2** seats, and only then, with `pwsh -NoProfile -File scripts/runner-slots.ps1 release -RunnerId server2 -SessionId <guid> -Reason 'CARD-0934 four-hour drain cap'`. Do not kill other runners. The 19-hour seat with no open task holds this drain until it exits or that cap is reached |
| 6. redeploy-old | Only after counters are 0/0/0, no Queued/Dispatched/Working/Blocked task has `runnerId=server2`, and no land bound to server2 is Queued, Held, Running, or NeedsResolution. `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase redeploy-old` | Do not add `-RecycleVolumes` or `-DryRun`. Expect `C1008_RECYCLE skipped=redeploy-old reason=not-requested`. A refuse leaves temp accepting and old drained. `22a5524a` in `NeedsResolution` will still refuse this phase |
| 7. Smoke old | Tool block for `server2` / project `antiphon-runner`, a Plan canary pinned `-Runner server2`, and `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Both -Sha <sha>` | On failure, immediately `pwsh -NoProfile -File scripts/runner-drain.ps1 drain -RunnerId server2 -RedirectTo server2-temp -Reason 'post-upgrade smoke failed'`. `redeploy-old` has already cleared the drain before this canary |
| 8. drain-temp | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase drain-temp -WaitIdleMinutes 240` | Start this when gate 7 has passed, not before. It sets temp `redirectTo=server2` and `retireWhenIdle=true` and waits. Tasks already on temp do not block the start. They hold the wait, up to 240 minutes |
| 9. retire-temp | Preview, then apply within 900 seconds. Commands below | `RecycleUnpublishedWork`, `RecycleWorktreeDirty`, or `RecycleGitAuditUnknown reason=registry-missing`: stop and report. Any other refusal: one fresh preview and apply, then a Debug task |

Tool smoke, from the desktop. Codex is uid 1654. Do not exec Codex as root.

```powershell
pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId <runner>
ssh mc@server2 'c=$(docker ps -q --filter label=com.docker.compose.project=<project> --filter label=com.docker.compose.service=session-runner); test -n "$c" && docker exec -u 1654:1654 -e HOME=/home/app -e CODEX_HOME=/state/codex "$c" codex --version && docker exec "$c" pwsh --version && docker exec "$c" dotnet --version && docker exec -u 1654:1654 -e HOME=/home/app "$c" claude --version'
```

`<runner>` / `<project>` is `server2-temp` / `antiphon-runner-temp` at gate 2, and `server2` / `antiphon-runner` at gate 7. Gate 2 `claude --version` must print `2.1.327`.

Gate 9:

```powershell
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase retire-temp -DryRun
# Ready line: C1609_RETIREMENT state=Ready operation=<operation-id>
$env:ANTIPHON_VOLUME_RETIREMENT_APPLY = 'enabled-<operation-id>'
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase retire-temp -RetirementProof <operation-id>
```

Unset the env var after. `1`, `true`, or `enabled` without that operation id does not enable deletion. The Ready preview expires after 900 seconds. Then `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Retired -Sha <sha>` only after the volumes are gone. Do not run `Retired` while temp private volumes remain.

## Gate 2 canary (2.1.327)

Source: `docs/reviews/2026-11-19-card-1514-claude-cli-2-1-327-pin-review-r1.md` at `origin/feat/card-task-8d80856b` tip `0e98bc9382192cd4784e51099230289d10947d9d`. Verdict on that review was clean. The operator re-held the Fable model on 2026-11-19, so this canary does **not** require `claude-fable-5-2`. Use Opus: `-Kind ClaudeCode -Level High`.

Run it on `server2-temp` after the tool smoke and before `drain-old`. It is a Claude Code task, not Grok, and not whichever kind an untyped Plan dispatch would pick.

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Role Plan -Kind ClaudeCode -Level High -Worktree -Runner server2-temp -Card <sanctioned-canary-card> -Goal <one bounded turn>
pwsh -NoProfile -File scripts/delegate.ps1 -Status <task-id>
```

Pass only when all of these are true:

1. `claude --version` in the temp container prints `2.1.327`.
2. The task runs in a fresh `/work/worktrees/*` path.
3. The first prompt is transcript-confirmed as a `UserPrompt`, the turn completes, and the screen shows no onboarding, trust, theme, or bypass-permissions dialog.
4. After the turn, `/state/claude/.claude.json` still has `hasCompletedOnboarding` and the trust entries (`hasTrustDialogAccepted` for that path, and the seed set `hasCompletedProjectOnboarding`, `skipDangerousModePermissionPrompt`). Confirm the keys exist. Do not print the file.
5. `modelUsage` showing `claude-sonnet-5-5` for a Medium task is optional. Do not check a Fable model id.

The production store already has `lastOnboardingVersion=2.1.280` and `hasCompletedOnboarding=true` (review, read-only on the live runner). The seed writes the version only when the key is absent, so the standing volume keeps `2.1.280`. A new temp volume gets a stamp equal to the binary. Whether 2.1.327 reads a new first-run key is what this canary is for. A dialog holds the session. It does not kill it.

If the canary fails: leave `server2` accepting, do not run `drain-old`, and retire temp only through the abandon path after the temp container is proven absent.

## Rollback at each phase

The previous source is `e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`. A `redeploy-old` rollback to that SHA runs **that SHA's** `deploy-server2.ps1`, from a checkout of that SHA, after the runner being replaced is drained and the counters are zero. Accepting temp is the live rollback until old is healthy again.

| Phase | Rollback |
|---|---|
| Before deploy-temp | Stop. Nothing has changed |
| deploy-temp failed | Old stays accepting. Do not repair temp by touching old. Abandon only after `ssh mc@server2 'docker ps -aq --filter label=com.docker.compose.project=antiphon-runner-temp'` prints nothing, then `pwsh -NoProfile -File scripts/runner-drain.ps1 drain -RunnerId server2-temp -RedirectTo server2 -RetireWhenIdle -Reason 'abandon failed rolling rollout after confirmed temp absence'`, wait until `retiredAt` is set, and run `retire-temp`. If any container id remains, stop and report it. This path does not remove a leftover container |
| Gate 2 failed | Same as a failed canary: old stays accepting, no `drain-old` |
| drain-old refused before the drain | Old stays accepting |
| drain-old refused after the drain, old still healthy | `pwsh -NoProfile -File scripts/runner-drain.ps1 clear -RunnerId server2 -Reason 'rolling rollback'`, confirm old accepts, and only then drain temp |
| Four-hour cap | Report `OldRunnerStillBusy`. Do not redeploy a busy runner. The cap release above is the only session stop |
| redeploy-old refused | Temp keeps accepting. Old stays drained. Do not clear old until a rollback is intended and old is healthy |
| Gate 7 failed | Drain old back to temp with the post-upgrade smoke command above. To put the old image back, drain the standing runner and `redeploy-old` the previous SHA from that SHA's script, using the image id saved before retirement |
| drain-temp refused | Old stays accepting. Temp stays as observed. Do not start a second drain. Clear temp's drain only after old is confirmed accepting |
| retire-temp | No apply without a Ready token. `RecycleUnpublishedWork` and `RecycleWorktreeDirty` stop here. A missing registry stops here. Volumes left behind make the next `deploy-temp` refuse `TempVolumesRetained` |

## Timeline and dispatch

Measured on the rc/2026-11-19 rollout (`docs/investigations/2026-11-13-server2-rollout-rc-2026-11-19.md`), not a promise for this image:

| Stretch | Last time |
|---|---|
| Census, host jq, fixture, inventory, deploy-temp | about 40 minutes (fixture alone about 14). deploy-temp itself about 10 minutes and about 8 GiB |
| drain-old with six live tasks | 34 minutes. Cap not reached. Nothing was released |
| redeploy-old, no volume recycle | about 8 minutes |
| drain-temp with four tasks | 170 minutes |
| retire-temp preview | about 2 minutes. The apply did not run |

The 2.1.327 binary is 372,904,464 bytes. How long the host build of this Dockerfile takes beyond that 10-minute deploy-temp is UNOBSERVED. A Claude canary turn is UNOBSERVED. With the five seats and one queued task now on server2, `drain-old` is somewhere between half an hour and the 240-minute cap. `drain-temp` depends on how much work was sent to temp and has the same cap. A clean run that does not hit either cap is on the order of half a day, matching the 06:07Z-10:45Z window last time. The `NeedsResolution` land makes gate 6 a refusal after the 30-minute land wait, not a longer success.

Dispatch is not paused. From temp's first `acceptingNewWork=true` until gate 7 passes, pin new work `-Runner server2-temp`. After that, pin it `-Runner server2` and start `drain-temp`. Standing server2 is not accepting between the drain write and the end of `redeploy-old`. Temp is the accepting runner in that window. If gate 1 never starts, server2 keeps accepting and dispatch stays there.

## What this rollout does not change

- The production server stays the desktop AppHost. Do not run `scripts/deploy-server2-server.ps1`, `scripts/restart-linux-server.ps1`, or compose project `antiphon-server2-server`. Port 17400 is not this window. `scripts/restart-apphost.ps1` stays the desktop production restart.
- Postgres project `antiphon-server2-postgres` is not deployed, restarted, or queried.
- desktop-win phase 1 is not involved. The desktop runner is down and stays down.
- `runner-defaults` are not written. The drain redirect is the scheduling switch.
- Main `work`, `runner-tmp`, `dind-data`, and `runner-state` stay. The 9218 staged deletions and the two dirty legacy worktrees stay on disk.
- The Claude OAuth file, phone-home secret, deploy key, and GitHub token stay where they are. A deploy does not refresh the Claude token unless `-RefreshClaudeToken` is passed. Do not pass it. Do not print those files.
- The `build-slots` service block did not change in the compose diff. Its stored `BUILD_SLOTS_SHA12` pin was not read (UNOBSERVED). Do not recreate the broker to chase `GET /build-slots/host-load`. An old broker answers 405 on that path and the runner records an unknown authority. That does not fail this rollout.
- No `docker image prune`, no `docker volume prune`, no host `Reset`.

## Gate 9 lessons carried forward

From the rc/2026-11-19 rollout note:

- An idle `Running` seat with no open task holds the drain. Do not release it before the four-hour cap.
- `Blocked` is not terminal. `22a5524a` is that case today, and it also has a land.
- The first gate 9 refusal was `RecycleGitAuditUnknown` on nested fixture repos (`git ls-remote` status 128). One fresh preview, then a Debug task. The later audit classifies those fixtures and does not `ls-remote` them.
- The second gate 9 refusal was `RecycleUnpublishedWork` on 16 reflog-only commits in the temp `repos/antiphon`. That is stop-and-report. There was no Ready token and no apply. The commits were later shown to be published-equivalent or scratch, which did not authorize deletion by itself.
- Retained temp volumes were about 24 GiB and blocked the next `deploy-temp`. Confirm they are gone before gate 1. That confirmation is UNOBSERVED from here.
- Worktree retention reclaimed nothing that day (`eligible` rows were backed off). Do not count on a retention sweep to make disk for this rollout. Disk is not the constraint at 237 GiB free.

## Not done, noted

Host `docker ps`, image ids, volume names, and `retirement-origins.json` still need the desktop ssh. The binding row text in user-secrets was not read. The 19-hour seat has no open task id on the slots route.
