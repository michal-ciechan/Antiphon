# Post-cut activation runbook for rc/2026-11-30

Measured 2026-11-20 22:59Z from this runner. The cut is already on the remote.
`origin/rc/2026-11-30` is `493bee0ee3af2052575bc7a26e9a313669b2fc2d`
(candidate `rc-2026-11-30-493bee0ee3af`, journal checkout named by the brief
`C:\Antiphon\releases\checkout-18c3d990`). This runner cannot see that Windows
path. The running control plane is still `rc/2026-11-29`.

Execute from `C:\src\Antiphon` in the order below. One phase per invocation.
The server2 image is refreshed before AppHost starts the new ladder.

## Live state (this read)

| Fact | Value |
|---|---|
| `GET https://antiphon.desktop.codeperf.net/api/version` | `26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248`, `land-v2` present. That SHA is `origin/rc/2026-11-29`. `/health` on that origin returned the client shell, not an API health body. `/api/health` was 404. |
| `origin/master` | `9abaaa88f84735cfde849a5e070bbeb7de715046`, three commits ahead of the pin. The only path in `pin..master` is `tests/Antiphon.Tests/Checkpoints/CheckpointConfinementLedgerTests.cs`. |
| server2 status | accepting, `dispatchEligible=true`, `buildVersion=e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4`, `sessions=3`, `runnerSessions=3`, `queuedTasks=0`. That commit's Dockerfile is `CODEX_VERSION=0.160.0`. |
| server2-temp status | unavailable, `draining=true`, `retireWhenIdle=true`, `redirectTo=server2`, `retiredAt=2026-11-18T04:46:10Z`, `sessions=0`, `queuedTasks=0`, `runnerSessions` null. |
| desktop directory row | `available=true`, `occupied=0`. The status route says `available=false`, `disconnectReason=desktop`. |
| `desktop-win` | 404, not in the runner map. |
| `GET /api/hosts/server2/load` | `physicalHostKey` empty. |
| `GET /api/runner-defaults` | revision 2, `globalRunnerId=server2`. |
| section 8 | two lines, both the rollout dispatch rule. No old-file deletion line. |
| `bash scripts/c1893-runner-hold.sh 493bee0ee3af2052575bc7a26e9a313669b2fc2d` | `RELEASED` (exit 0) in this checkout. Re-run it on the desktop after `git fetch`. |
| container id and `StartedAt` | not recorded. `ssh mc@server2` from this runner returned `Permission denied (publickey)`. |
| `C:\src\Antiphon` git status | not readable here. |
| `C:\Antiphon\nightly\run.lock` and `native-run.lock` | not readable here. |
| desktop `codex --version` | not measured here. |
| SHA-stamp change | not landed. `origin/master` and the pin both still have `SetSourceRevisionIdFromGit` in `Directory.Build.props`. `tests/Antiphon.Tests/Infrastructure/SourceRevisionStampContractTests.cs` is absent on both. |

## Preconditions that are not met

Do not start the matching step until the desktop confirms the unmarked rows.

1. Canonical checkout cleanliness, current HEAD, and whether a land is writing it are unknown. Step 1 stops if porcelain is non-empty or a land is in progress.
2. The control plane is `26a99f1a747d`, not the pin. That is expected until step 3. It is not a reason to restart AppHost early.
3. The standing runner is Codex 0.160.0 (`e229fb4d3`) with 3 live sessions. The image refresh has not started.
4. Temp is a retired placeholder. `deploy-temp` still refuses `TempVolumesRetained` if any of the four temp-private volumes exists. That census was not run.
5. `physicalHostKey` is empty, so the CARD-1893 server binding is not loaded. The pin's runner compose is `RELEASED` and may roll anyway. History then stays `binding-unknown` until an operator writes `HostLoadHistory:Bindings` and a later start loads it.
6. Desktop Codex 0.170.0 was not confirmed. Step 3 stops if `codex --version` is not `codex-cli 0.170.0`.
7. `C:\Antiphon\runner\phone-home.json` was not tested. If it exists, step 3 exits 3. Do not delete the file.
8. The release checkout, its `core.longpaths`, and the native lock were not tested. Step 5 stops until the desktop shows the lock free and that checkout's HEAD equal to the pin.
9. The SHA-stamp change is not on `origin/master`. Do not run `restart-session-runner.ps1 -Hard` for it.

## What this runbook must not do

- Do not restart AppHost, or run `dev-aspire.ps1`, before the standing server2 smoke prints `codex-cli 0.170.0`. `MinimumCodexCliVersion` is called from tests only (`ModelLevelAliases.cs:44` on the pin). Launch does not enforce the 0.169.1 floor, so an early restart sends `gpt-6.2-sol` at CLI 0.160.0.
- Do not `git merge`. Do not `git pull` and do not `git pull --rebase`. `origin/master` is `9abaaa88f`, and a pull lands there. Hosts track the rc tip, not master.
- Do not rebase, reset, or force-push any ref. Do not push `rc/*` again. The ref already exists.
- Do not pass `-Phase all`, `-RecycleVolumes`, `-AllowWorktree`, `-KillSessions`, or `-Hard`.
- Do not `Reset`, Docker-prune, or touch the standing `antiphon-runner-session-runner-1` container outside the named phases.
- Do not drain both runners, and do not drain the only accepting runner.
- Do not `redeploy-old` while `sessions`, `runnerSessions`, or `queuedTasks` on server2 are non-zero. `OldRunnerStillBusy` at 240 minutes is a report, not permission.
- Do not delete `C:\Antiphon\runner\phone-home.json`. Do not create it either.
- Do not query production Postgres. Do not print a secret, a token, or `secrets.json`.
- Do not run `scripts/release-cut.ps1`, `scripts/publish-release.ps1`, or `scripts/promote-release.ps1`. Do not tag.
- Do not start the Windows Full on `desktop-win`, on server2, or while `native-run.lock` / `run.lock` names a live owner.
- Do not pass `ultra`, bare `astra`, or bare `sol`. `-Level High` without `-Model` is `gpt-6.2-sol`, not `gpt-6.2-astra`.
- Do not downgrade migration `20261120091515_AddRequestedModelAlias`. Its `Down` throws.
- On `retire-temp`, stop and report only `RecycleUnpublishedWork`, `RecycleWorktreeDirty`, or `RecycleGitAuditUnknown reason=registry-missing`. Any other refusal gets one fresh preview and apply, then a Debug task.

## 1. Canonical checkout, before any deploy

The runner image is not built from the desktop working tree.

`scripts/deploy-server2.ps1` scp's `scripts/c590-remote.sh` from the checkout that contains the script (`scripts/c590-real.ps1` around the `deploy-parent` / `deploy-temp-runner` copy). On server2, `ensure_checkout` (`scripts/c590-remote.sh:143-156`) fetches `origin <sha>` into `C590_CHECKOUT` (default `/work/repos/antiphon`) and runs `checkout --detach` plus `reset --hard`. `build_server2_images` (`c590-remote.sh:1414-1421`) then runs `docker build` with that directory as the context and tags `antiphon-server2/session-testing:<sha12>`. `-Sha` is the image identity. The desktop HEAD is not the build context.

`provision-host-jq` is the exception: it throws `HostJqCanonicalSourceRequired` unless `C:\src\Antiphon` is the main worktree, porcelain-clean, and `HEAD` equals `-Sha` (`deploy-server2.ps1:85-99`). `check-census` path-matches that same root to the project row `C:/src/Antiphon`. Run every deploy command from `C:\src\Antiphon`, never from a linked worktree and never from this Linux mirror.

Move that checkout onto the pin without a merge commit. A rebase onto master is the wrong destination. Detach to the exact pin. Replay of local commits would make HEAD a new SHA, and `-ExpectedServerSha` would then refuse.

Preconditions: no land in progress, `git status --porcelain` empty, both AppHost locks clear. Expected duration: under 2 minutes. Stop if porcelain is non-empty. Do not stash and do not touch untracked files.

```powershell
git -C C:\src\Antiphon fetch origin
git -C C:\src\Antiphon status --porcelain
git -C C:\src\Antiphon rev-parse HEAD
git -C C:\src\Antiphon checkout --detach 493bee0ee3af2052575bc7a26e9a313669b2fc2d
git -C C:\src\Antiphon rev-parse HEAD
git -C C:\src\Antiphon status --porcelain
```

Verify: HEAD prints `493bee0ee3af2052575bc7a26e9a313669b2fc2d` and porcelain is empty. The running `/api/version` stays `26a99f1a747d` until step 3. While this checkout is detached, do not land into it.

`bash scripts/c1893-runner-hold.sh` reads local objects and does not fetch. On the desktop, after the fetch above:

```powershell
bash scripts/c1893-runner-hold.sh 493bee0ee3af2052575bc7a26e9a313669b2fc2d
```

Verify: stdout is exactly `RELEASED`. `HOLD` or exit 2 stops the rollout. This pin already printed `RELEASED` here.

## 2. Server2 runner image, one phase at a time

`$env:ANTIPHON_API` must be set. Unset exits 2. This runner's origin was `https://antiphon.desktop.codeperf.net`. Do not pass `-Phase all`. Do not pass `-RecycleVolumes`. Keep dispatching. New unpinned work goes to `server2-temp` from the moment `drain-old` redirects until `redeploy-old` exits 0 and smoke plus `verify-card0849-caches.ps1 -Case Both` have passed. Then send new work back to `server2` and start `drain-temp`. There must always be one of those two accepting. Do not drop the in-flight target of 6 because of the rollout. server2 currently has 3 occupied seats, so refill toward 6 on whichever runner is accepting. Do not pause lands that are not bound to the runner being recycled.

`<sha>` in every command is `493bee0ee3af2052575bc7a26e9a313669b2fc2d`. There is no separate promotion command. `drain-old` is what redirects unpinned work to temp (`docs/docker-stack.md` gate 3).

Record the standing container before `deploy-temp`, and require the same id and `StartedAt` after it (`docs/docker-stack.md` gate 1):

```powershell
ssh mc@server2 'docker inspect -f "{{.Id}} {{.State.StartedAt}}" antiphon-runner-session-runner-1'
```

| Step | Command | Stop | Duration | Verify |
|---|---|---|---|---|
| Status | `pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId server2` and the same for `server2-temp`. `pwsh -NoProfile -File scripts/runner-slots.ps1 list -RunnerId server2`. `Invoke-RestMethod "$env:ANTIPHON_API/api/runner-defaults"`. | server2 not accepting, or `globalRunnerId` is not server2. A 404 or null counter on a live runner stops. Temp may have `runnerSessions=null` only while offline, which matches the retired placeholder read above. | under 5 min | server2 `buildVersion=e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4` until redeploy, `dispatchEligible=true`, `acceptingNewWork=true`. |
| Census | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 493bee0ee3af2052575bc7a26e9a313669b2fc2d -Phase check-census` | `RecycleProjectUnresolved`, `Ambiguous`, `Archived`, malformed, or `cause=Unstable`. On `NoMatch` only, re-run every later phase with `-ProjectId d4ea7ae9-e769-474b-95b9-aa25fbc1303f` after the printed name is the Antiphon project. | under 5 min | one `RECYCLE_PROJECT` line and two `RECYCLE_CENSUS` lines (`server2`, `server2-temp`). GET only. |
| Host jq | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 493bee0ee3af2052575bc7a26e9a313669b2fc2d -Phase check-host-jq` | any refusal other than `HostJqMissing`. On `HostJqMissing` only: `-Phase provision-host-jq`, then `check-host-jq` again, then `provision-host-jq` once more. The second provision must record `installed=false`. | under 10 min | `host-jq-*.json` under the printed `.antiphon/rolling-server2/<run-id>/`. These phases deploy nothing. |
| Cache prep | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture -Sha 493bee0ee3af2052575bc7a26e9a313669b2fc2d` then `-Case Inventory -Sha 493bee0ee3af2052575bc7a26e9a313669b2fc2d`. Empty volumes use `-Case Seed -Cold` instead of ordinary Seed. | Seed or Inventory refusal. `Reset` is not the recovery. | Fixture is the longer of the two. Stop on refusal rather than stretching it. | receipts retained. npm verification stays uid 1654. |
| Gate 1 deploy-temp | The inspect above, then `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 493bee0ee3af2052575bc7a26e9a313669b2fc2d -Phase deploy-temp`, then the inspect again. | temp fails, the standing id or `StartedAt` changes, `TempVolumesRetained`, `TempContainersRemain`, `StoreMismatch`, or old stops accepting. Leave old accepting. Do not touch the standing container to fix temp. | image build of `session-testing` and the state-init image. Not remeasured here. | both statuses. Old unchanged and still accepting. Temp reaches `acceptingNewWork=true` and `buildVersion` equal to the pin. |
| Gate 2 smoke temp | Status for `server2-temp`, then the uid-1654 block below with project `antiphon-runner-temp`. Then `pwsh -NoProfile -File scripts/delegate.ps1 -Role Plan -Worktree -Runner server2-temp -NoCommit -Title 'rc30 temp smoke' -Goal 'Reply with one line ready. Do not edit files.'` | version is not `codex-cli 0.170.0`, or the Plan canary does not start and report on `server2-temp`. Do not `drain-old`. | smoke is minutes; the canary is one task. | `codex --version` prints `codex-cli 0.170.0`. While AppHost is still `26a99f1a747d`, a pool launch asks for `gpt-6.1-sol`. That proves the container launches. It does not prove a 6.2 slug. |
| Gate 4 drain-old | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 493bee0ee3af2052575bc7a26e9a313669b2fc2d -Phase drain-old -WaitIdleMinutes 240` | refusal before the drain: old stays accepting. Refusal after: stop and report the code. Rollback while old is healthy: `pwsh -NoProfile -File scripts/runner-drain.ps1 clear -RunnerId server2 -Reason 'rolling rollback'`, then confirm old accepts. | up to 240 minutes. It will wait: 3 sessions and 3 runner sessions are live now. The script default without this flag is 480. | server2 `draining=true`, `redirectTo=server2-temp`, temp still accepting. A new unpinned task lands on temp. |
| Gate 5 zero | The same `drain-old` wait. At the deadline, `pwsh -NoProfile -File scripts/runner-slots.ps1 list -RunnerId server2` and status. | `OldRunnerStillBusy` does not authorize `redeploy-old`. The four-hour cap allows a resumable stop of the remaining **server2** seats only: `pwsh -NoProfile -File scripts/runner-slots.ps1 release -RunnerId server2 -SessionId <guid> -Reason 'CARD-0934 four-hour drain cap'`. Recheck counters and rerun `drain-old`. If anything remains, stop. Do not kill seats on other runners. | included in the 240 minutes. | `sessions`, `runnerSessions`, and `queuedTasks` are all zero. |
| Gate 6 redeploy-old | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 493bee0ee3af2052575bc7a26e9a313669b2fc2d -Phase redeploy-old` | `RecycleBoundTasks` or `RecycleLandInFlight` when a task or pending land is bound to server2. Keep temp accepting and old drained. Do not clear an unverified old runner. | recreate of the standing runner. The image tag from gate 1 is reused when present. | stdout contains `C1008_RECYCLE skipped=redeploy-old reason=not-requested`. `buildVersion` equals the pin. Drain on old is clear. Temp still accepting. |
| Gate 7 smoke old | The uid-1654 block with runner `server2` and project `antiphon-runner`. A Plan canary `-Runner server2 -NoCommit`. Then `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Both -Sha 493bee0ee3af2052575bc7a26e9a313669b2fc2d`. | on failure immediately `pwsh -NoProfile -File scripts/runner-drain.ps1 drain -RunnerId server2 -RedirectTo server2-temp -Reason 'post-upgrade smoke failed'`, then stop. | smoke plus Both. | `codex-cli 0.170.0` as uid 1654, canary reported on server2, Both receipt clean. This is the gate that unlocks step 3. |
| Gate 8 drain-temp | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 493bee0ee3af2052575bc7a26e9a313669b2fc2d -Phase drain-temp -WaitIdleMinutes 240` | refusal: leave old accepting and report the code. Do not start two drains. | up to 240 minutes for tasks already on temp. New work is on server2 and does not block this wait. | old accepting at the pin. Temp drained, `retireWhenIdle=true`, `redirectTo=server2`, then the temp container is absent. |
| Gate 9 retire-temp | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 493bee0ee3af2052575bc7a26e9a313669b2fc2d -Phase retire-temp -DryRun` | `RecycleUnpublishedWork`, `RecycleWorktreeDirty`, or `RecycleGitAuditUnknown reason=registry-missing`: stop and report. The registry `/home/mc/antiphon-server2/retirement-origins.json` is operator-written. | preview, then apply within 900 seconds of `state=Ready`. | preview prints `would delete:` and `C1609_RETIREMENT state=Ready operation=<operation-id>` and removes nothing. |

Apply, in the same shell, immediately after a Ready preview. Unset the variable afterwards. `enabled` without the operation id does not enable deletion.

```powershell
$env:ANTIPHON_VOLUME_RETIREMENT_APPLY = 'enabled-<operation-id>'
pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha 493bee0ee3af2052575bc7a26e9a313669b2fc2d -Phase retire-temp -RetirementProof <operation-id>
Remove-Item Env:ANTIPHON_VOLUME_RETIREMENT_APPLY
```

Verify: `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Retired` prints `tempPrivateVolumes=0`. A preview that is not applied leaves the volumes, and the next `deploy-temp` refuses `TempVolumesRetained`.

Smoke block, from the desktop. Codex is uid 1654, never root (`docs/docker-stack.md` smoke gate, CARD-1174).

```powershell
pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId <runner>
ssh mc@server2 'c=$(docker ps -q --filter label=com.docker.compose.project=<project> --filter label=com.docker.compose.service=session-runner); test -n "$c" && docker exec -u 1654:1654 -e HOME=/home/app -e CODEX_HOME=/state/codex "$c" codex --version && docker exec "$c" pwsh --version && docker exec "$c" dotnet --version'
```

Rollback image, if a later phase must return to the previous runner: `antiphon-server2/session-testing:e229fb4d3fb8`, using that SHA's script. Accepting temp is the live rollback while it still accepts.

## 3. AppHost restart onto the pin

Preconditions, all required:

- Gate 7 has printed `codex-cli 0.170.0` for standing server2.
- `C:\src\Antiphon` HEAD is still `493bee0ee3af2052575bc7a26e9a313669b2fc2d` and porcelain is empty.
- Desktop PATH shim: `codex --version` prints `codex-cli 0.170.0`. If it does not, stop. The npm upgrade is a separate desktop step. Do not restart the desktop runner for an npm replace, and do not pass `-Hard`.
- `Test-Path C:\Antiphon\runner\phone-home.json` is false. `Get-WindowsPhoneHomeLaunchRefusal` (`scripts/lib/windows-phone-home-env.ps1:283-292`) makes `restart-apphost.ps1` exit 3 with `REFUSED: phone-home-config-present` while that file exists. If the Linux server is the writer, the refusal is correct: leave the file and stop. Do not delete it to force this restart.
- `logs\apphost.restart.lock` and `logs\apphost.launch.lock` are absent, or their stamps are older than 15 minutes and not an in-flight launch. A stamp younger than 15 minutes is active even when the holder PID is gone.
- Operator startup writes in section 6, if they are going to load on this start, are already done. `StandingInstructions` is startup `IOptions` (`OrchestratorInstructionsSettings.cs:4`). `Database.Migrate()` runs at `server/Program.cs:1149` before seed. Migration `20261120091515_AddRequestedModelAlias` adds nullable `RequestedModelAlias` `character varying(80)` with `lock_timeout` 5s. `Down` throws.

Run from an independent PowerShell window whose close does not kill the AppHost, in `C:\src\Antiphon`. Expected wait: the script's no-progress limit is 150 seconds and the hard cap is 600 seconds (`docs/apphost-runbook.md` exit 1), after the build. A cold build can consume that window.

```powershell
git -C C:\src\Antiphon rev-parse HEAD
pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha 493bee0ee3af2052575bc7a26e9a313669b2fc2d
```

Stop gates:

- Exit 3: refused. Nothing was killed. Wrong HEAD versus `-ExpectedServerSha`, unreadable HEAD, worktree guard, phone-home file, or a held lock. Inspect both locks and HEAD. Do not retry a phone-home refusal by deleting the file. Do not pass `-AllowWorktree`.
- Exit 5: health came up but `/api/version` mismatched, was unreadable, or the checkout moved. The restart lock is retained and the child is left running. Do not treat the stack as the pin. Read the printed SHAs, then restart again with the same `-ExpectedServerSha` only after HEAD is the pin.
- Exit 1: build failed or the health wait ended. Read `logs\apphost.log`. Do not immediately rerun. Exit 4 is a DCP dependency timeout. Inspect Docker before another attempt.
- Exit 0 only: dashboard, `/health` 200, and `/api/version` SHA equal to `493bee0ee3af2052575bc7a26e9a313669b2fc2d` with `land-v2`.

Verify:

```powershell
Invoke-RestMethod "$env:ANTIPHON_API/api/version"
```

The SHA is the pin. Postgres on 17280 and the desktop runner on 17204 stay up. This restart does not reload session-runner source. A failed migration fails the start. Do not connect to Postgres to look at the column.

## 4. Canaries

Preconditions: exit 0 from step 3, `/api/version` is the pin, standing server2 `buildVersion` is the pin and accepting. Two throwaway tasks. Neither edits files. Spend is these two launches only.

Ladder, no `-Model`. High must resolve through `ForCodex` to `gpt-6.2-sol` (`ModelLevelAliases.cs:36-41` on the pin).

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Role Custom -Kind Codex -Level High -Runner server2 -NoCommit -Title 'rc30 ladder canary' -Goal 'Reply with one line ready. Do not edit files.'
```

Astra, explicit alias. `-Level High` sets effort `high`. `-Model` is what selects `gpt-6.2-astra` (`scripts/delegate.ps1:64-68`).

```powershell
pwsh -NoProfile -File scripts/delegate.ps1 -Role Custom -Kind Codex -Level High -Model gpt-6.2-astra -Runner server2 -NoCommit -Title 'rc30 astra canary' -Goal 'Reply with one line ready. Do not edit files.'
```

For each, `pwsh -NoProfile -File scripts/delegate.ps1 -Status <taskId>` until the session id exists, then `GET /api/sessions/<sessionId>/transcript?since=0`. Duration: one launch each, minutes, not a suite.

Verify:

- Ladder transcript or recorded launch argv contains `--model gpt-6.2-sol` and `model_reasoning_effort=high`.
- Astra transcript or recorded launch argv contains `--model gpt-6.2-astra` and `model_reasoning_effort=high`.
- A 409 `model_disabled` or a 422 unknown alias stops the activation. Do not remap the kind. Do not hold `gpt-6.2-astra` as part of this runbook. A hold does not stop `gpt-6.2-sol`, and it is a settings write.

## 5. Seeded Windows Full

Preconditions:

- Step 4 passed.
- No live owner on `C:\Antiphon\verification\native-run.lock` (default `CoordinationRoot`). Not visible from this runner. A second native owner is a refusal. Also no live owner on the state root's `run.lock`. `launch-rc.ps1` exits 3 while that `run.lock` names a live owner.
- Checkout `C:\Antiphon\releases\checkout-18c3d990` exists, is detached at the pin, has local `core.longpaths=true`, and contains `.antiphon-nightly-owned`. `nightly-run.ps1` refuses `unmarked-clone` without that marker. If the path or the marker is missing, stop and report it. Do not point the run at `C:\src\Antiphon` and do not point it at `desktop-win`.
- Seed file `C:\Users\lndco\antiphon-seeds\e7f92f66.json` is still inside the window the recut note recorded, valid until `2026-12-04T07:33Z`, digest `a489a7e210401b003fc43f13c39fba63fca2c58655e17932d92b0a08f8c92a5c`, source `rc-2026-11-29-26a99f1a747d`. Re-read it with the pin checkout's seed reader before launch. A digest or source mismatch refuses. Today 2026-11-20 is inside that recorded window.
- A fresh `-RunId`. Do not reuse `e7f92f66`.

Duration: a serial Windows Full under one native lock. Wall was not remeasured. It is the long pole. Launch is minutes. The run is hours.

```powershell
git -C C:\Antiphon\releases\checkout-18c3d990 rev-parse HEAD
git -C C:\Antiphon\releases\checkout-18c3d990 config --local core.longpaths true
pwsh -NoProfile -File scripts/workspace-slots.ps1 cold-reset -AllRunners -Reason "rc qualification rc/2026-11-30 493bee0ee3af"
```

HEAD must print the pin. `cold-reset` does not wait for leased slots. It is a no-op until a runner has workspace slots enabled. Record the printed request ids.

Copy the selected checkout's `scripts/qualification/` set into a new `C:\Antiphon\releases\task-<id>\`. Do not use a private copy. Write `launch-record.json` there:

```json
{
  "checkoutRoot": "C:\\Antiphon\\releases\\checkout-18c3d990",
  "expectedSha": "493bee0ee3af2052575bc7a26e9a313669b2fc2d",
  "buildSlotLabel": "rc-493bee0ee3af-windows-qualification",
  "nightlyArguments": [
    "-NoSync", "-Profile", "rc", "-Trigger", "rc", "-SelectionMode", "Full",
    "-Ref", "rc/2026-11-30",
    "-ExpectedSha", "493bee0ee3af2052575bc7a26e9a313669b2fc2d",
    "-CandidateId", "rc-2026-11-30-493bee0ee3af",
    "-CheckoutRoot", "C:\\Antiphon\\releases\\checkout-18c3d990",
    "-ReleaseRoot", "C:\\Antiphon\\releases",
    "-StateRoot", "C:\\Antiphon\\releases\\state-<id>",
    "-LogRoot", "C:\\Antiphon\\releases\\candidates\\rc-2026-11-30-493bee0ee3af\\logs",
    "-RunId", "<fresh-run-id>",
    "-PriorFailuresPath", "C:\\Users\\lndco\\antiphon-seeds\\e7f92f66.json",
    "-PriorFailuresDigest", "a489a7e210401b003fc43f13c39fba63fca2c58655e17932d92b0a08f8c92a5c",
    "-AcceptCrossCandidateSeed",
    "-SeedSourceCandidateId", "rc-2026-11-29-26a99f1a747d"
  ]
}
```

Preflight, then run the copied starter and observer with the launch record's `checkoutRoot` as `-CheckoutRoot`. The starter passes that path to the copied supervisor. For a pin that predates `-CheckoutRoot` support, run the starter and observer from the checkout and omit the switch. Start the starter with `pwsh` through `Win32_Process` Create so it leaves the caller job. `powershell.exe` 5.1 launches nothing.

```powershell
pwsh -NoProfile -File C:\Antiphon\releases\task-<id>\preflight.ps1 -CheckoutRoot C:\Antiphon\releases\checkout-18c3d990
pwsh -NoProfile -File C:\Antiphon\releases\task-<id>\start-supervisor.ps1 -CheckoutRoot C:\Antiphon\releases\checkout-18c3d990 -TaskRoot C:\Antiphon\releases\task-<id> -LaunchScript C:\Antiphon\releases\task-<id>\launch-rc.ps1 -LogRoot C:\Antiphon\releases\candidates\rc-2026-11-30-493bee0ee3af\logs -StateRoot C:\Antiphon\releases\state-<id> -RunId <fresh-run-id>
```

Preflight prints `preflight: core.longpaths true` and exits 0. After start, `start-supervisor.json` and `supervisor-identity.json` exist and the observer below credits that run id. `launch-rc.ps1` refuses with exit 2 on HEAD mismatch or `longpaths-not-enabled`, and exit 3 if `run.lock` is live. `-StateRoot` and `-LogRoot` in `nightlyArguments` are the same paths passed to `start-supervisor.ps1`. `launch-rc.ps1` reads `run.lock` from the nightly `-StateRoot`, and it falls back to `C:\Antiphon\nightly` when that argument is omitted. Leave `-CoordinationRoot` unset so the run takes the real lock at `C:\Antiphon\verification\native-run.lock`. Pass `-ReleaseRoot` explicitly as shown. Missing or blank values cause the rc run to exit 3 with `rc-release-root-required` in the final JSON's `refusal` field.

Run the observer after the identity file exists. Re-arm each bounded watcher about every 30 minutes.

```powershell
pwsh -NoProfile -File C:\Antiphon\releases\task-<id>\observe.ps1 -CheckoutRoot C:\Antiphon\releases\checkout-18c3d990 -TaskRoot C:\Antiphon\releases\task-<id> -StateRoot C:\Antiphon\releases\state-<id>
pwsh -NoProfile -File scripts/orchestration/watch-qual.ps1 -LogDir C:\Antiphon\releases\candidates\rc-2026-11-30-493bee0ee3af\logs\<run> -StateFile <owned-scratch>\qual.seen.txt -Heartbeat C:\Antiphon\releases\task-<id>\supervisor-heartbeat.json
pwsh -NoProfile -File scripts/orchestration/watch-tasks.ps1 -ProjectId d4ea7ae9-e769-474b-95b9-aa25fbc1303f -StateFile <owned-scratch>\tasks.json
pwsh -NoProfile -File scripts/orchestration/watch-master.ps1 -From 9abaaa88f84735cfde849a5e070bbeb7de715046 -Repository C:\src\Antiphon
```

The SHA-stamp change is not on `origin/master` `9abaaa88f` and not on the pin. Do not run `restart-session-runner.ps1 -Hard` before or after this launch for that change. `-Hard` remains a planned supervisor refresh only, and `-KillSessions` is human-only.

## 6. Operator-only steps

These are not autonomous. The orchestrator does not run them. Both settings loads happen at process start, so a write after step 3 does not take effect until a later AppHost restart. Do not insert that restart while the Windows Full is using the desktop. Write them before step 3 when the operator is present. Otherwise leave them for the next restart and say so.

Standing instructions. Section 8 already has two lines (indexes 0 and 1). Do not replace them. There is no API that appends a line. `POST /api/orchestrator-instructions/refresh` only re-renders. The cap is 10 lines of 300 characters and the text must not contain `{{key:`.

The third line is 255 characters:

`Old finished-work deletion is OK without asking (operator 2026-11-20). Owner: docs/orchestration-loop.md standing rule 12. Exact literal path only, via a Custom delegate. Never delete live worktrees, sessions, .git, databases, volumes, /state, or secrets.`

From `C:\src\Antiphon`, operator shell:

```powershell
dotnet user-secrets set "Delegation:OrchestratorInstructions:StandingInstructions:2" "Old finished-work deletion is OK without asking (operator 2026-11-20). Owner: docs/orchestration-loop.md standing rule 12. Exact literal path only, via a Custom delegate. Never delete live worktrees, sessions, .git, databases, volumes, /state, or secrets." --id antiphon-server --project server\Antiphon.Server.csproj
```

After the restart that loads it, section 8 shows that line and the two existing rollout lines.

DW-3. Dry run, then execute. The helper writes the `desktop-win` map and the secret file and prints no value (`scripts/desktop-win-phase1-secrets.ps1:1-63`). Run it as the account that owns the secrets directory. Do not add `-SweepStranded` on the first run.

```powershell
pwsh -NoProfile -File scripts/desktop-win-phase1-secrets.ps1
pwsh -NoProfile -File scripts/desktop-win-phase1-secrets.ps1 -Execute
```

`desktop-win` phase 1 after that (clone `C:\Antiphon\runner\app`, `phone-home.phase1.json`, listen on 17214) stays operator work and is independent of the Windows Full. It is not started by this runbook. `GET /api/session-runners/desktop-win` is 404 until that registration exists.

CARD-1893 `HostLoadHistory:Bindings` stays an operator user-secrets write. The key values were not projected here. Do not invent them. Until they are written and loaded, `physicalHostKey` stays empty. That does not block the `RELEASED` runner roll.

## Not done, noted

Fix idea: none. The activation itself is the orchestrator's next execution of this file, not a code change.
