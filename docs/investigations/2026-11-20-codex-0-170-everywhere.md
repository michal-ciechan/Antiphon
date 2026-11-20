# Codex 0.170.0 and GPT-6.2 available everywhere before the rc recut

**Date:** 2026-11-20. **Base:** `8a89aa0e5715f86062bbcc7ee778810c2cf4cb3e` on `feat/card-task-ab9057c0`. Read-only except this note. No deploy, restart, docker build, docker prune, npm install, or desktop command was run. Local `127.0.0.1:17202` did not connect from this mirror, so live slots, the live container, the live desktop binary, and the live AppHost SHA were not measured.

**Outcome.** On this master tip a caller still selects Codex by `-Kind` and `-Level` only. `gpt-6.2-astra` normalizes and is in the curated profile catalogue, and a hold PUT of that exact slug already succeeds, but it is not a delegatable alias and a fresh pool delegate cannot ask for it. The server2 runner image can be replaced alone, through the existing rolling phases, without deploying the server or Postgres. The CLI floor is data only, so the new image has to be running `codex-cli 0.170.0` before the AppHost is restarted onto the new ladder. The brief's numbered order (AppHost restart, then the image) is the unsafe order. The two launch notes named as precedent do not exist.

## Measured tips

| Ref | SHA | Ladder | Image pin |
|---|---|---|---|
| `origin/master` and this branch | `8a89aa0e5715f86062bbcc7ee778810c2cf4cb3e` | High and Medium `gpt-6.2-sol`, floor `0.169.1`; Frontier `gpt-6-astra`; Low `gpt-5.6-luna` (`server/Application/Services/ModelLevelAliases.cs:30-41`) | `CODEX_VERSION=0.170.0`, SHA-512 `e9c09925…ce5bb2` (`docker/session-runner-grok/Dockerfile:136-137`) |
| `origin/rc/2026-11-29` | `26a99f1a747d94120aeff8f0d6b9a2e7c7d4e248` | High and Medium `gpt-6.1-sol`, floor `0.159.1`. `MinimumCodexCliVersion` returns a floor only when the model equals that Sol id. `git grep gpt-6.2-astra` on this commit is empty | `CODEX_VERSION=0.160.0`, SHA-512 `288ffbdc…463df6` (same Dockerfile path, lines 136-137 of that commit) |

Product hosts track the current `rc/YYYY-MM-DD` tip, never master (`docs/release-gates.md:675-676`). A master-only land does not activate the ladder. The running process was not queried, so "the desktop AppHost is still `26a99f1a`" is the documented host rule plus this remote tip, not a live `/api/version` reading.

## 1. How a caller selects a model

`scripts/delegate.ps1` has `-Level` (`Frontier|High|Medium|Low`, lines 45-48) and `-Kind` (`ClaudeCode|Grok|Codex`, lines 59-62). There is no `-Model`. The create body sends `modelLevel` and `agentKind` only (`scripts/delegate.ps1:1389-1392`). `-Agent` and `-OnAgent` continue an existing process and cannot combine with `-Runner` (`scripts/delegate.ps1:1274`). `CreateAgentTaskRequest` has `ModelLevel` and `AgentKind` and no requested-model field (`server/Application/Dtos/AgentTaskDtos.cs:17-25`). `AgentTask` stores `ModelLevel` and the specialist snapshot columns `SpecialistModelAlias` / `SpecialistModelId` (`server/Domain/Entities/AgentTask.cs:21-22`). Those specialist columns are not a public model override.

The ladder on this tip (`ModelLevelAliases.cs:30-41`, `docs/agent-kinds.md:214-218`):

| Tier | Codex slug | Reasoning effort |
|---|---|---|
| Frontier | `gpt-6-astra` | `xhigh` |
| High | `gpt-6.2-sol` | `high` |
| Medium | `gpt-6.2-sol` | `medium` |
| Low | `gpt-5.6-luna` | `low` |

`gpt-6.2-astra` is recognized, floored at `0.169.1`, and off the ladder (`ModelLevelAliases.cs:33-53`, `docs/agent-kinds.md:266-268`). `ForCodex` never returns it. Do not pass `ultra`, bare `astra`, or bare `sol` (`ModelLevelAliases.cs:85-87`, `docs/agent-kinds.md:533-534`). The catalog default for those slugs is still `low`; the tier sets the effort explicitly (`docs/agent-kinds.md:550`).

A pool launch ignores any agent `ModelId` and passes `TierAliasFor` (`server/Application/Services/AgentTaskDispatcher.cs:7380`, `7473-7475`). A pool delegate never takes the profile branch (`AgentTaskDispatcher.cs:7412-7413`). A profiled standing agent with a non-blank `ModelId` omits the tier alias and the resolver appends that exact id when the profile has a model argument and the id is in the profile catalogue or the curated list (`AgentTaskDispatcher.cs:7431-7438`, `AgentTuiLaunchResolver.cs:549-584` and `634-646`). The curated Codex list on this tip already includes `gpt-6.2-astra` (`server/Application/Services/AgentTuiRunnerCatalog.cs:44`). `MapLegacyModel` for Codex still follows `ForCodex`, so the profile picker default stays on the ladder (`AgentTuiRunnerCatalog.cs:74-81`). The rc tip has no `gpt-6.2-astra` text at all, so this standing-agent path is master code, not the running rc server.

`DelegatableAliases` is the current-ladder hold vocabulary: Codex entries are `gpt-6-astra`, `gpt-6.2-sol`, `gpt-5.6-luna` (`server/Application/Core/ModelAlias.cs:34-44`). `CanonicalHoldAlias` accepts only that set or `*` (`ModelAlias.cs:52-68`). `Normalize` is wider and kind-agnostic; `IsAstra62` matches folded `gpt 6 2 astra` and `gpt62 astra` (`ModelAlias.cs:97`, `180-181`). `ParseHoldAlias` uses `Normalize`, not `CanonicalHoldAlias` (`server/Application/Services/ModelAvailability.cs:374-378`). A hold PUT of `Codex` / `gpt-6.2-astra` therefore already succeeds on this master. `ListAvailable` walks only `DelegatableAliases`, so the available list never shows it (`ModelAvailability.cs:443-467`). Kind-wide `*` ORs with a per-alias hold (`ModelAvailability.cs:395-396`). Create checks `ModelLevelAliases.For` unless a pinned standing `ModelId` normalizes, and returns 409 `model_disabled` unless `IgnoreModelDisabled` (`AgentTaskService.cs:1246-1254`, `1272-1298`; flag at `AgentTaskDtos.cs:65-69`). True queues the task; start never honours the flag. A pool High task is checked against `gpt-6.2-sol`, not `gpt-6.2-astra`. `scripts/model-availability.ps1` verbs are `get`, `hold`, `clear` (`scripts/model-availability.ps1:7-10`). The script's API defaults to `http://localhost:17202` when `ANTIPHON_API` is unset (`scripts/model-availability.ps1:35-36`).

`MinimumCodexCliVersion` is defined at `ModelLevelAliases.cs:44-57` and called from tests only (`CodexGpt62LadderTests.cs`, `CodexCliObservationTests.cs`). Launch, create, retry, dispatch, and warm reuse do not call it. CARD-0959 says no observation refuses those paths (`docs/ops-http.md:107-108`, `docs/agent-kinds.md:269-272`). That is AC-1.

A routing-pin forbid uses `CanonicalHoldAlias(token) ?? Normalize(ClaudeCode, token)` (`RoutingPinService.cs:529-530`). Because `Normalize` is kind-agnostic, forbidding `gpt-6.2-astra` already succeeds on this master via `IsAstra62`. The error text's known list is still only `DelegatableAliases` (`RoutingPinService.cs:533-537`).

### Ready code brief

Smallest change that makes `gpt-6.2-astra` an explicit delegatable alias and moves no `ForCodex` row. Do not make it Frontier or Review.

1. Append `(AgentKind.Codex, ModelAlias.Gpt62Astra)` to `DelegatableAliases` (`ModelAlias.cs:34-44`). Rewrite the comment at lines 30-32: the list would then be every launched alias plus this one off-ladder selectable alias. `CanonicalHoldAlias`, `ListAvailable`, and the pin known-list then include it. Leave `ForCodex` (`ModelLevelAliases.cs:36-41`) unchanged.
2. Add an optional explicit alias on create: a field on `CreateAgentTaskRequest`, a `-Model` parameter on `scripts/delegate.ps1` sent only when set, and a new nullable `AgentTask` column. Do not reuse `SpecialistModelAlias`. Validate with `Normalize`. Refuse 422 when the value is unknown, `*`, or not in `DelegatableAliases` for that kind. When set, create-time hold check, `DispatchModelAlias.ForTaskAsync` (`DispatchModelAlias.cs:20-29`), and the pool `TierModelAlias` (`AgentTaskDispatcher.cs:7380`) use it ahead of `TierAliasFor`. Level still supplies reasoning effort. If a standing `ModelId` and the explicit alias both normalize and differ, 422. Omitted `-Model` keeps Frontier on `gpt-6-astra` and High/Medium on `gpt-6.2-sol`.

Red witnesses that pin today's omission and must flip, while the ladder assertions stay:

- `tests/Antiphon.Tests/Application/CodexGpt62LadderTests.cs:98` `CanonicalHoldAlias(Astra62)` is null; `:105-106` the Codex delegatable list is exactly `[gpt-6-astra, gpt-6.2-sol, gpt-5.6-luna]` and must not contain Astra62. After the change the list contains it. The `ForCodex` arms must still never return Astra62.
- `tests/Antiphon.Tests/Application/ModelAliasTests.cs:144` lists `gpt-6.2-astra` as a rejected `CanonicalHoldAlias`. Remove that argument and add an accept. `:180-183` asserts the same three Codex aliases; add `gpt-6.2-astra`.
- `tests/Antiphon.Tests/Application/CodexDelegateDispatchTests.cs:52` and `:91-92` stay the no-override witness: High pool launch is `--model gpt-6.2-sol` and `model_reasoning_effort=high`; Frontier stays `gpt-6-astra` / `xhigh`. Add one method: create with the explicit alias `gpt-6.2-astra` and level High launches `--model gpt-6.2-astra` and effort `high`; an unknown alias is 422; a hold on `gpt-6.2-astra` 409s only that alias and does not hold `gpt-6.2-sol` or `gpt-6-astra`.

Checkpoint rows, one isolated build-slot and one exact filter each. Not run here.

| CP | Filter | Proves |
|---|---|---|
| CP-1 | `/*/*/(CodexGpt62LadderTests*)|(ModelAliasTests*)/*` | alias is delegatable and still off the ladder |
| CP-2 | `/*/*/CodexDelegateDispatchTests/*` | no-override High stays `gpt-6.2-sol`; the new explicit-alias, 422, and hold methods |

**Not done, noted:** the alias and `-Model` path above were not implemented in this task.

## 2. Runner-only refresh on server2

The runner image is `antiphon-server2/session-testing:${SOURCE_SHA12}` (`docker-compose.server2-runner.yml:42`). `scripts/c590-remote.sh:1001` sets `SOURCE_SHA12="${SHA:0:12}"`. There is no separately named rollback tag. The previous image is the previous SHA's 12-hex tag, kept until something prunes it. The live tag was not inspected. UNKNOWN.

`scripts/deploy-server2.ps1` deploys the phone-home runner from a checkout that is not the host being mutated. It does not deploy the server or Postgres (`docs/docker-stack.md:1541-1550`). It requires `-Rolling` (`scripts/deploy-server2.ps1:13`). `-Phase` defaults to `all` and accepts `all`, `deploy-temp`, `drain-old`, `redeploy-old`, `drain-temp`, `retire-temp`, `check-census`, `check-host-jq`, `provision-host-jq` (`scripts/deploy-server2.ps1:15-16`). Never pass the default `all`. `ANTIPHON_API` is required; unset exits 2; there is no localhost default (`docs/docker-stack.md:1544-1546`). The script header says run it from the desktop, never on server2 (`scripts/deploy-server2.ps1:1`).

This is already runner-only. It is not an in-place rebuild of the standing container, and it does not skip the CARD-0934 gates. `scripts/restart-linux-server.ps1` is the Linux server SHA gate. It refuses runner, Postgres, and gateway container names and does not deploy the runner (`docs/docker-stack.md:1520-1525`). Replacing the runner image belongs to the rolling phases.

### Sequence and stop gates

Run from the desktop with `ANTIPHON_API` set, one phase at a time, `-Sha` the full 40-hex of the new rc tip (the tip whose Dockerfile is `0.170.0`). Record, before `deploy-temp`, `ssh mc@server2 'docker inspect -f "{{.Id}} {{.State.StartedAt}}" antiphon-runner-session-runner-1'`. Gate 1 requires that id and start time unchanged (`docs/docker-stack.md:122`).

1. `pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId server2` and `pwsh -NoProfile -File scripts/runner-slots.ps1 list -RunnerId server2`. Healthy means `buildVersion=<current sha>`, `dispatchEligible=true`, `acceptingNewWork=true` (`docs/docker-stack.md:106-109`). Running-task count is UNKNOWN from this mirror.
2. `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <sha> -Phase check-host-jq`. Every mutating phase writes `host-jq-<phase>.json` first (`docs/docker-stack.md` volume-recycling section). Stop on a host-jq refusal.
3. `-Phase deploy-temp`. Stop if temp fails or the standing container's id or start time changes. Leave old accepting. Never touch the standing container to fix temp (`docs/docker-stack.md:122`).
4. Smoke temp as uid 1654, never root (`docs/docker-stack.md:191-201`, CARD-1174):

```powershell
pwsh -NoProfile -File scripts/runner-drain.ps1 status -RunnerId server2-temp
ssh mc@server2 'c=$(docker ps -q --filter label=com.docker.compose.project=antiphon-runner-temp --filter label=com.docker.compose.service=session-runner); test -n "$c" && docker exec -u 1654:1654 -e HOME=/home/app -e CODEX_HOME=/state/codex "$c" codex --version'
```

Require the version line `codex-cli 0.170.0`. A launch canary while the AppHost is still `rc/2026-11-29` asks for `gpt-6.1-sol`, because that is the ladder the running server has. It proves the new container launches. It does not prove a 6.2 slug. The `gpt-6.2-sol` transcript canary is step 7 of the order below, after the AppHost restart. A pool `gpt-6.2-astra` canary is impossible until the code brief is in the build the AppHost is running.
5. `-Phase drain-old -WaitIdleMinutes 240`. New unpinned work goes to `server2-temp` until `redeploy-old` exits 0 and smoke/Both passed (`docs/orchestration-loop.md:34`). Rollback while old is still healthy: `scripts/runner-drain.ps1 clear -RunnerId server2 -Reason 'rolling rollback'`, then verify old accepts (`docs/docker-stack.md:125`).
6. At 240 minutes, `OldRunnerStillBusy` is a report, not permission to redeploy a busy runner. The four-hour cap's session stop is already authorised for server2 seats only (`docs/docker-stack.md:126`). Proceed to `redeploy-old` only when sessions, runnerSessions, and queuedTasks are zero.
7. `-Phase redeploy-old`. Default keeps main `work`, `runner-tmp`, and `dind-data` and prints `C1008_RECYCLE skipped`. Do not pass `-RecycleVolumes` for this upgrade. A task or pending land bound to `server2` refuses `RecycleBoundTasks` or `RecycleLandInFlight` (`docs/docker-stack.md:127`). A rollback to an older SHA runs that SHA's script (`docs/docker-stack.md:1022`). Accepting temp remains the live rollback (`docs/docker-stack.md:1034`).
8. Smoke `server2` the same way, then `scripts/verify-card0849-caches.ps1 -Case Both -Sha <sha>`. If this fails, drain old back to temp: `scripts/runner-drain.ps1 drain -RunnerId server2 -RedirectTo server2-temp -Reason 'post-upgrade smoke failed'` (`docs/docker-stack.md:128`).
9. `-Phase drain-temp -WaitIdleMinutes 240`, then `retire-temp -DryRun`, then the enabled apply with that preview's `enabled-<operation-id>` token (`docs/docker-stack.md:129-130`). Stop and report only on `RecycleUnpublishedWork`, `RecycleWorktreeDirty`, or `RecycleGitAuditUnknown reason=registry-missing`.

Disk. The 2026-11-19 census ran `docker system df` and did not prune: Images 20 total, 2 active, 6.827GB, **6.637GB (97%) reclaimable** (`docs/investigations/2026-11-19-server2-housekeeping-census.md:182-193`). That matches the brief's "6.6 GB". This session did not remeasure. Current host numbers are UNKNOWN. Docker prune is human-only (`docs/orchestration-loop.md:80`). Do not prune to make room.

## 3. Desktop launcher and the version cache

The tracked Codex definition is `Exe` `codex.cmd` (`server/appsettings.json:134-138`). Windows rewrites a recognized stock npm `codex.cmd` to sibling `node.exe`, else PATH `node`, plus `%dp0%\node_modules\@openai\codex\bin\codex.js` (`src/Antiphon.SessionRunner/CodexWindowsLaunchPolicy.cs:13-36`, `docs/agent-kinds.md:135-137`). The repo does not hard-code `C:\Users\lndco\AppData\Roaming\npm` or `C:\Users\lndco\AppData\Local\OpenAI\Codex\bin`. `scripts/test-reap-zombie-agents.ps1:126` uses the Roaming npm `codex.exe` only as a zombie-census fixture. A customized wrapper is neither rewritten nor executed (`CodexWindowsLaunchPolicy.cs:14-16`). Whether the live PATH `codex.cmd` is the npm shim at 0.170.0, and whether the OpenAI Desktop app binary is a second install, is UNKNOWN from this Linux mirror. The 2026-11-19 note recorded the desktop npm as 0.160.0 and said it was not remeasured (`docs/investigations/2026-11-19-codex-cli-0-170-and-gpt-6-2.md:121`). The brief's statement that the desktop npm is already 0.170.0 was not remeasured here.

The observation cache is feature `codex-cli-version-v1` (`src/Antiphon.SessionRunner/CodexCliVersionProbe.cs:13`). The probe executable defaults to `codex.cmd` on Windows, refresh is 5 minutes, and the cache max age is 15 (`CodexCliVersionSettings.cs:6-10`). Display freshness is a fixed 15-minute threshold (`docs/agent-kinds.md:274`). The fingerprint includes boot id, executable selector, cwd, PATH, PATHEXT, and `FullName + length + LastWriteTimeUtc.Ticks` (`CodexCliVersionProbe.cs:264-266`, `289-293`). Later refreshes are the periodic timer (`CodexCliVersionRefreshService.cs:14-21`). The cache does not select the binary and does not gate launch. A process started after an npm replace uses the files on disk. The displayed version updates on the next probe because a replaced `codex.js` changes length or mtime. A restart also clears it, because boot id is in the fingerprint. A restart is not required for new Codex processes to be 0.170.0. Already-running sessions keep the process they started.

Do not restart the desktop runner while the live Windows qualification owns it. When a restart is later wanted, the command is `pwsh -NoProfile -File scripts/restart-session-runner.ps1` (`scripts/restart-session-runner.ps1:19`). `-Hard` is a planned supervisor refresh (`scripts/restart-session-runner.ps1:14-15`) and is not indicated for an npm CLI replace. `-KillSessions` is human-only (`docs/orchestration-loop.md:37`). Confirm `logs/apphost.restart.lock` and `logs/apphost.launch.lock` are clear first.

After the AppHost moves onto the new ladder, desktop High and Medium will ask for `gpt-6.2-sol`. The desktop CLI has to already be 0.170.0 before that restart. Confirm with `codex --version` on the PATH shim the runner resolves. That measurement is still owed.

## 4. Order of operations

The brief lists AppHost restart before the server2 image refresh. AC-1 makes that unsafe: the floor is not enforced, so an AppHost on this ladder would send `gpt-6.2-sol` at a runner still on 0.160.0, and the CLI would reject it. The image goes first. The numbered list below is the order to run. This task ran none of it.

Who may run what. The orchestrator may, without a fresh ask, pull the canonical checkout, run `scripts/restart-apphost.ps1`, run `scripts/restart-session-runner.ps1` (not `-KillSessions`; `-Hard` only for a planned supervisor refresh), and run the `deploy-server2.ps1 -Rolling` phases including `retire-temp` preview then enabled apply (`docs/orchestration-loop.md:21-37`). Still human: Reset, Docker prune, recycling runner-state or cache beyond policy, killing other sessions, changing budgets, pins, or settings, spend beyond a sanctioned canary, secrets, and touching the standing server2 container outside the rolling phases (`docs/orchestration-loop.md:80-86`). Publish, promote, and tag stay on their existing authorization (`docs/orchestration-loop.md:678`). The operator sentence of 2026-11-20 authorizes the server2 and desktop restarts and the recut from master. Pushing a new `rc/*` ref is not in the generic autonomy paragraph; it is covered by that sentence. Do not use `scripts/release-cut.ps1` as the qualification launcher: the cut script journals, it does not publish, and the qualification precedent did not use the publisher.

1. **Wait until the live Windows qualification ends.** Stopping a live credited run is not part of this upgrade. Slots stay UNKNOWN until step 2's status commands run on a host that has `ANTIPHON_API`.
2. **Land the remaining chain and repair slices onto master.** The rc cut runs the merge high-radius rows on the cut pin and does not push the rc ref while a row is red (`docs/orchestration-loop.md:749`).
3. **Cut the new line.** `scripts/release-candidate.ps1` journals an existing remote `rc/YYYY-MM-DD`. It does not create the branch, run tests, publish, or restart. An omitted `-Ref` refuses `rc-ref-required` (`scripts/release-candidate.ps1:6-16`, `27-30`). `-CheckoutRoot` must be an owned clone, never the canonical checkout and never a linked worktree (`scripts/release-candidate.ps1:23-25`). Precedent that exists: `docs/investigations/2026-11-16-rc-cut-2026-11-24.md` created the line with a plain push of a new ref (`* [new branch]`), touching no existing `rc/*`, then journaled candidate id `rc-<date>-<first 12 hex>`. The two files named in the brief do not exist: `docs/investigations/2026-11-19-rc-2026-11-29-launch-commands.md` and `docs/investigations/2026-11-19-rc-cut-2026-11-29.md`. The new line name is `rc/<UTC date of the cut>`. Create it by pushing the master SHA to that new ref, then:

```powershell
pwsh -NoProfile -File scripts/release-candidate.ps1 -Ref rc/<UTC date> -SourceRef master -CheckoutRoot <owned clone> -ReleaseRoot C:\Antiphon\releases
```

4. **Refresh the server2 runner from that rc SHA** using section 2, before any AppHost restart. Stop gates are the gate table. Rollback image tag is `antiphon-server2/session-testing:<previous sha12>`.
5. **Confirm the desktop PATH `codex.cmd` is the npm shim and `codex --version` prints `codex-cli 0.170.0`.** If it does not, the npm upgrade is a separate desktop step this task did not run and did not measure. Do not restart the desktop runner unless the displayed observation is stuck past a probe; if restarting, no `-Hard` and no `-KillSessions`, and only after the Windows run has released the desktop.
6. **Restart the AppHost only after steps 4 and 5.** `restart-apphost.ps1` builds the canonical checkout's HEAD (`docs/apphost-runbook.md:237-240`). Hosts track the rc tip, so that HEAD must be the new rc tip before the restart. The runbook's recorded step is `git pull --rebase` in `C:\src\Antiphon`, then from an independent shell:

```powershell
pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha <new rc tip>
```

Stop if `git rev-parse HEAD` in `C:\src\Antiphon` is not that tip: a pull of master would activate master. Exit 0 requires dashboard, `/health`, and `/api/version` SHA equal to that HEAD. A mismatch is exit 5 with the restart lock retained. Both `logs/apphost.restart.lock` and `logs/apphost.launch.lock` must be clear; exit 3 is a refusal. The exact one-liner that moves the canonical checkout onto the new ref was not found in the runbook lines read. The stop gate is the HEAD check above.
7. **Canary after the AppHost is on the new tip.** `pwsh -NoProfile -File scripts/delegate.ps1 -Kind Codex -Level High -Runner server2 -Goal "<sanctioned canary>"`, then read the transcript. Require `--model gpt-6.2-sol` and `model_reasoning_effort=high`. A pool `gpt-6.2-astra` launch waits for the code brief. A hold, once this master code is the running server, is `scripts/model-availability.ps1 hold -Kind Codex -Model gpt-6.2-astra`. That hold does not stop `gpt-6.2-sol`.
8. **Seeded Windows Full** after the native lock is free. Shape from `docs/investigations/2026-10-22-windows-qualification-0714a9a89-start.md:58`:

```powershell
pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-<sha12>-windows-qualification -SlotWaitMinutes 90 -- pwsh -File scripts/nightly-run.ps1 -NoSync -Profile rc -Trigger rc -Ref rc/<UTC date> -ExpectedSha <full sha> -CandidateId rc-<UTC date>-<sha12> -CheckoutRoot <owned clone>
```

Launch through the qualification supervisor set (`launch-rc.ps1` under pwsh, `start-supervisor.ps1`), not through `release-cut.ps1`. Tags, `publish-release.ps1`, and `promote-release.ps1` stay human-only.

## 5. Open risks

| Risk | What the repo says | Live state |
|---|---|---|
| AppHost restarted before server2 is 0.170.0 | Floor is not enforced. New ladder sends `gpt-6.2-sol` at CLI 0.160.0 and the CLI rejects it | Running image UNKNOWN. Remote rc Dockerfile is 0.160.0 |
| Brief order vs AC-1 | Numbered list puts AppHost before the image. Section 4 reverses that | Not run |
| Desktop CLI still below 0.169.1 when the ladder activates | Desktop High/Medium become `gpt-6.2-sol` | Desktop version UNKNOWN. Last written measurement was 0.160.0 and was not redone |
| `gpt-6.2-astra` expected from `-Level` | No level returns it. Pool delegates cannot name it until the code brief lands | Not implemented |
| Hold of `gpt-6.2-astra` on the running server | Works on this master via `Normalize`. The rc commit contains no `gpt-6.2-astra` text, so the running server cannot name it | Live server SHA UNKNOWN |
| Standing agent `ModelId` | Works for a profiled agent when the server has the curated list. Pool path ignores `ModelId` | Live server is the old rc unless `/api/version` says otherwise |
| In-place runner rebuild or `restart-linux-server.ps1` | Those do not replace the runner image. Skipping CARD-0934 gates is a refusal | Not run |
| `-Phase all` | Default. Runs every mutating phase in one invocation | Do not use |
| Drain of the only accepting runner | Forbidden when moving the server. This refresh must keep temp or old accepting (`docs/docker-stack.md:1549-1550`) | Slot count UNKNOWN |
| Four-hour cap | `OldRunnerStillBusy` does not authorize redeploying a busy runner. Stopping leftover server2 seats at the cap is already authorised | Not reached |
| Volume recycle | Default `redeploy-old` skips it. `-RecycleVolumes` and pruning are separate and human-gated | Do not request |
| Disk | Historical 6.637GB reclaimable images. Prune is human-only | Current `docker system df` UNKNOWN |
| Rollback tag | Previous image is `antiphon-server2/session-testing:<old sha12>`, not a named rollback tag. Live id and StartedAt were not recorded | UNKNOWN until gate 1's inspect |
| rc precedent files | The two 2026-11-19 paths in the brief are absent. Use `docs/investigations/2026-11-16-rc-cut-2026-11-24.md` | Confirmed missing |
| Canonical checkout left on master | Restart builds that HEAD. Product hosts are supposed to track the rc tip | Checkout state UNKNOWN from this mirror |
| New rc ref push | Not in the generic autonomy list. Covered by the 2026-11-20 operator sentence. Publish, promote, and tag are not | Not run |
| Windows qualification still owns the desktop | A runner restart, and especially `-KillSessions`, would hit that run | Whether the run is still live is UNKNOWN |
