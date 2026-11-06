# Slot and retention settings: desktop server and server2 runner

Date: 2026-11-06 UTC. Task: `374f6129` (Investigate). Source inspected: **d164e93ae47a79ed175beeb9367212f7a7d5d1d5**, `rc/2026-11-08`; assigned branch `feat/card-task-374f6129`.

## Operator summary

- Runner slots need **two flags**, one in the desktop API server and one in each phone-home runner. Both default off. Restarting server2 alone does not enable the server flag.
- Runner mirror cleanup already ships **on**, executing for `server2`, every 30 minutes, with a **12-hour** completion-age floor and at most 25 requests per tick. Under the brief's stated default desktop restart, it is scheduled; actual execution/deletions still need a log or database receipt.
- Set `RunnerWorktreeRetention:MinTerminalHours=24` on the **desktop server**. This retains finished legacy runner mirrors for at least 24 hours after completion; it is not a 24-hour expiry for reusable slots, evidence, or desktop worktrees.
- The desktop server accepts an ignored `server/appsettings.Development.json` overlay or user secrets with id `antiphon-server`. Its slot flag requires a restart; its role list and retention service use options monitors. The AppHost's own secrets id is different and does not forward arbitrary settings.
- Named server2 deployment phases currently cannot convey the runner slot switch. Neither Compose file declares it; deployment also rewrites stack env files, and the standing Compose wrapper does not load `stack.env` with `--env-file`.
- Desktop automatic release is separately **on**, at `:15` every hour, with a **48-hour minimum**. A value of 24 there is invalid. Desktop release salvages reflog-only commits; runner mirror retention does not implement that salvage path.
- Enable runner support before allowing server leases, then use a Codex Plan canary. For rollback, set server roles to the string `none` and leave both flags enabled until existing leases drain. Turning off the server flag also stops its automatic slot release loop.
- This task changed only this report. No desktop command, live API/DB request, deployment, build, test, or product edit ran. A separate Code task is needed for deployment plumbing; running state and the exact first trees to disappear remain unverified.

## Finding and evidence boundary

**Confirmed configuration/deployment mechanism, reconstructed from the committed source:** the desktop server selects slot requests, the runner advertises and executes slot capability, and the named deployment path has no setting transport for enabling it. Separately, shipped retention defaults are already enabled. This is not evidence that a specific production tick ran or deleted a named tree.

Read-only command observations in this investigation:

```text
$ git status --short
(empty)
$ git branch --show-current
feat/card-task-374f6129
$ git rev-parse HEAD
d164e93ae47a79ed175beeb9367212f7a7d5d1d5
$ git check-ignore server/appsettings.Development.json Antiphon.AppHost/appsettings.Development.json
server/appsettings.Development.json
Antiphon.AppHost/appsettings.Development.json
```

The brief reports a desktop restart at 22:00Z on this date with defaults and 120+ remaining worktrees; neither observation was independently verified. Its summary of deployment task `90ec246b` was supplied evidence, not a file retrieved here. The desktop checkout and `C:\Antiphon\releases\task-90ec246b\rollout-note.md` are unreachable from this mirror, and the brief explicitly forbids desktop execution. No transcript or production database rows were retrieved. Test sources below describe stored assertions, **not tests executed by this task or proof of their current passing status**.

All file:line citations below refer to the inspected SHA, before this report-only commit.

## 1. Desktop server configuration

### Retention keys and exact shipped values

All keys in this table have prefix `RunnerWorktreeRetention:`. The type defaults and shipped JSON agree except `Runners`, deliberately empty in C# and populated only by JSON. Evidence: `server/Application/Settings/RunnerWorktreeRetentionSettings.cs:14`, `server/appsettings.json:243`; stored assertions: `tests/Antiphon.Tests/Application/C1677RetentionSettingsTests.cs:21`.

| Suffix | Shipped effective default | Meaning |
|---|---|---|
| `Enabled` | `true` | Service kill switch, read before each request |
| `Execute` | `true` | Execute versus DryRun; disabling during an execute tick stops further requests |
| `Runners` | `["server2"]` | Allow-list; does not include `server2-temp` |
| `MinTerminalHours` | `12` | Integer **hours since CompletedAt**; minimum valid value 1 |
| `MaxRemovalsPerTick` | `25` | Maximum candidate requests per runner, not guaranteed deletions; valid 1..100 |
| `PauseBetweenSeconds` | `5` | Delay between sends |
| `RequestTimeoutSeconds` | `300` | Budget for one runner request |
| `MaxTickMinutes` | `20` | Stop starting requests after this tick budget |
| `KeptRecheckMinutes` | `60` | Initial kept-tree retry backoff |
| `KeptRecheckMaxHours` | `24` | Backoff ceiling, **not retention age** |
| `RecurringJobId` | `antiphon:runner-worktree-retention` | Hangfire job id (C# default) |
| `Cron` | `*/30 * * * *` | Five-field cron, UTC |
| `WorktreeRoot` | `/work/worktrees` | Server-side candidate-path confinement |
| `RetiringRoot` | `/work/worktrees-retiring` | Sibling retiring root |
| `TempSweep:Enabled`, `TempSweep:Execute` | `false`, `false` | Independent temp cleanup remains off/dry |

Temp sweep's separate defaults are `MinAgeHours=48`, `MaxEntriesPerTick=200`, `MaxBytesPerTick=21474836480`, and `Families:claude-scratch=false` (`RunnerWorktreeRetentionSettings.cs:73`). They are unrelated to the requested mirror floor.

Registration binds configuration and validates on start (`server/Program.cs:232`). With `Hangfire:ServerEnabled=true` (shipped at `server/appsettings.json:185`), startup registers the retention job when Enabled, or removes it when disabled (`server/Program.cs:1166`). Cron uses UTC (`server/Infrastructure/Agents/HangfireConfiguration.cs:72`). The job disables automatic retries and self-overlap and logs the run id (`server/Infrastructure/Agents/RunnerWorktreeRetentionJob.cs:15`). There is no explicit immediate startup tick in this registration. With a successful 22:00Z start, the next regular boundary is 22:30Z; missed/executed tick history cannot be inferred from source.

### Server slot keys and reload behavior

| Key | Default | Consumer / reload |
|---|---|---|
| `Delegation:WorkspaceSlotsEnabled` | `false` | Captured from `IOptions<DelegationSettings>`; **restart required** |
| `Delegation:WorkspaceSlotRoles` | `"*"` | Read from `IOptionsMonitor` for new preparation decisions; JSON reload supported |

Evidence: `server/Application/Settings/DelegationSettings.cs:698`, `server/Application/Services/RemoteWorkspaceService.cs:66`, `server/Application/Services/WorkspaceSlotRoleGate.cs:8`. Role grammar is one string: `*`, `none`, or canonical case names separated by commas, no spaces (`Plan,Investigate,TestDesign,Docs`, for example). Arrays, including `[]`, are invalid; Mutation cannot be listed, and SourceLanding never leases (`server/Application/Settings/WorkspaceSlotRoles.cs:22`, `:96`; `DelegationSettingsRegistration.cs:23`). Invalid initial values fail validation; invalid reloads cause the gate to choose legacy mirrors, with a warning (`WorkspaceSlotRoleGate.cs:21`).

There are no server-side overflow or re-home knobs in this slot selection: those belong to `PhoneHome:WorkspaceSlots` on the runner. A separate feature, `Delegation:RefOnlyDesktop`, defaults to `Enabled=false`, `RunnerIds=[]`, `Roles=none` (`server/Application/Settings/RefOnlyDesktopSettings.cs:7`). Runner slots alone do **not** eliminate desktop per-task checkout creation. Existing Checkout and RefOnly paths are explicitly separate (`RemoteWorkspaceService.cs:18`). Enabling RefOnlyDesktop is a separate scope/acceptance decision, not implicit in this request.

### Where desktop configuration comes from

The server uses `WebApplication.CreateBuilder(args)` (`server/Program.cs:68`): standard configuration sources include server `appsettings.json`, environment-specific JSON, Development user secrets, environment variables (`__` separates sections), and command-line configuration. Later/higher-priority providers can shadow the JSON overlay. The server's secrets id is **`antiphon-server`** (`server/Antiphon.Server.csproj:8`). Repository guidance explicitly supports user secrets and the ignored server Development overlay, never editing tracked server JSON (`docs/bootstrap.md:202`; ignore rule `.gitignore:5`). Use the same Windows account that runs the server for user secrets.

Aspire pins the **server** to Development, sets its runner URL, channel bridge, PTY request and port, and supplies the database connection reference (`Antiphon.AppHost/Program.cs:65`). AppHost has a separate secrets id **`aspire-antiphon-apphost`** (`Antiphon.AppHost/Antiphon.AppHost.csproj:10`). Its explicit configuration forwarding is the optional messaging broker (`Antiphon.AppHost/Program.cs:54`); it does not read and forward arbitrary retention/slot settings. Putting these keys only in `Antiphon.AppHost/appsettings.Development.json` or the AppHost secrets store does not configure the child server.

The wrapper launches from the main source tree and uses `Start-Process pwsh` (`dev-aspire.ps1:138`); it sets none of the target retention/slot environment variables. Inherited launch environment may affect children, but neither its current contents nor scheduled-launch inheritance was inspected. Prefer the server overlay or server secrets over relying on a transient shell variable. Do not list all secrets or dump environment to diagnose precedence; check only the relevant non-secret keys on the owning host.

There is no generic settings-table write for these options: their consumers bind IConfiguration, not a DB settings service. The settings routes catalog contains templates, template groups, providers and model routing (`docs/antiphon-api.md:1223`; endpoint groups `server/Api/Endpoints/SettingsEndpoints.cs:15`, `:83`, `:254`). Runner defaults, capacity, host budgets and dispatch concurrency are separate APIs and do not set these flags (`docs/ops-http.md:151`, `:181`).

Retention uses a monitor (`RunnerWorktreeRetentionService.cs:54`); JSON edits apply to the next tick. Enabled/Execute and MaxTickMinutes are re-read between requests (`:307`), while age/cap/list/pacing are the settings snapshot captured at tick entry (`:83`, `:169`, `:182`). Raising age mid-tick does not revise that tick's snapshot, and disabling cannot recall a request already sent. Job registration/removal and cron updates occur at server start. S4a also reads a monitor and treats invalid reload as disabled (`DesktopLegacyReleasePolicyService.cs:99`).

## 2. Runner settings and the deployment gap

The runner uses CreateBuilder, binds `PhoneHome`, then supplies `IOptions<PhoneHomeSettings>.Value` to its singleton dispatcher (`src/Antiphon.SessionRunner/Program.cs:12`, `:60`, `:99`). Its tracked `appsettings.json` only sets `PhoneHome:Enabled=false`, without a workspace-slot override (`src/Antiphon.SessionRunner/appsettings.json:22`). The image also defaults phone-home off (`docker/session-runner-grok/Dockerfile:175`). Compose enables phone-home and injects the connection/root settings (`docker-compose.server2-runner.yml:50`). Container recreation/process restart is needed to change this runner's captured settings.

All following keys have prefix **`PhoneHome:WorkspaceSlots:`**, with environment spelling `PhoneHome__WorkspaceSlots__<suffix>`. Defaults are defined at `src/Antiphon.SessionRunner/PhoneHomeWorkspaceSlotSettings.cs:7`:

| Suffix | Default |
|---|---|
| `Enabled` | `false` |
| `Headroom` | `4` |
| `LingerRetries` | `3` |
| `OverflowWaitSeconds` | `10` |
| `MaxOverflowSlots` | `4` |
| `SlotBytesEstimateGiB` | `3` |
| `PoolDiskBudgetGiB` | `64` |
| `IdleRetireHours` | `72` |
| `MaxQuarantinedDirectories` | `4` |
| `MaxRehomesPerSlotPerDay` | `3` |
| `ColdResetSchedule` | `Weekly` (`Off`, `Daily`, `Weekly`) |
| `ColdResetDayOfWeek`, `ColdResetHourUtc` | `Sunday`, `2` |
| `EvidenceCapBytes` | `268435456` (256 MiB per task) |
| `EvidenceRetentionDays` | `7` |

Capacity is shipped as 10 (`docker-compose.server2-runner.yml:64`); pool target is current capacity + headroom (14 at that value), with up to four overflow slots (`RunnerWorkspaceSlotPool.cs:60`, `:150`). The disk estimate/budget check is in the **overflow creation** arm, not a general hard cap on every base slot. Idle retirement acts only on free slots while active count exceeds target (`:1153`), so `IdleRetireHours=24` would not mean all task worktrees expire at 24h. More than four open quarantines holds the pool (`:92`); more than three re-homes in a rolling 24h retires that slot (`:812`); failed replacements can fail an individual slot. Those limits must not be confused with the requested legacy-mirror retention.

### Reconstructed configuration transport

1. Server2's Compose topology is explicitly only a session runner and supporting services, not the Antiphon API server or Postgres (`docker-compose.server2-runner.yml:1`). Its runner environment block contains **no `PhoneHome__WorkspaceSlots__Enabled` or passthrough**. The temp file overrides runner id and build-slot URL, not workspace settings (`docker-compose.server2-runner.temp.yml:1`). Temp merges with the base, so a setting in the base would be shared unless overridden.
2. `scripts/c590-remote.sh:900` defines the two Compose files and `/home/mc/antiphon-server2/secrets/stack.env` / `stack.temp.env` under SERVER2_ROOT. `compose_temp` renders base + temp with `--env-file "$SERVER2_TEMP_ENV"` (`:974`). An env-file value is interpolation input; without a Compose environment reference or env_file service declaration, it is not automatically inserted into the container.
3. **Standing differs:** `compose_host` passes selected variables inline but has **no `--env-file "$SERVER2_ENV"`** (`:949`). It reads selected saved keys through helpers such as `broker_sha12`, not the entire file. Merely adding a workspace key to stack.env would not make the standing render see it.
4. `deploy-parent` **rewrites** stack.env with a fixed here-document (`:9552`); temp deployment similarly rewrites stack.temp.env (`:9819`). Neither document includes a workspace key. A manual extra line would also disappear on the next named deployment. The desktop driver exposes named phases, SHA and lifecycle options, without a workspace-slots parameter (`scripts/deploy-server2.ps1:4`).

These source facts reconstruct why the earlier deployment could not enable slots by setting a stack env key. No Docker Compose render or deployment was executed here.

### One-sided enablement and rollback

The runner advertises `workspaceSlotV1` only with its flag enabled and lazily creates its pool under that flag (`PhoneHomeCommandDispatcher.cs:144`, `:166`). The server chooses slots only when its own flag, a committed preparation binding, runner capability and role gate all permit them (`RemoteWorkspaceService.cs:247`, `:345`). Therefore runner-only on or server-only on continues ordinary legacy mirrors for new preparations; neither requires an atomic simultaneous flip. An explicit slot RPC to a disabled runner refuses because the pool is absent (`RunnerWorkspaceService.Slots.cs:18`).

Stored assertions cover both halves off/no capability (`C0824WorkspaceSlotRoleGateTests.cs:343`), actual task/ledger/branch agreement (`:39`), and roles-none rollback with releases continuing (`:103`). **Server flag off also disables the minute release loop** (`WorkspaceSlotReleaseService.cs:266`; stored assertion `C0824WorkspaceSlotRoleGateTests.cs:365`). Roll back new selection using roles `none` first; do not disable either runtime while outstanding leases still need its release implementation. A preparation's decision is persisted before send and reused on retries: roles-none does not rewrite an already recorded slot decision (`RemoteWorkspaceService.cs:238`).

The directory is `ws-NN` or `ws-NN-dK`, with ledger `/work/workspace-slots/ledger.json`; evidence and quarantine policy are documented at `docs/docker-stack.md:761`. Slots reuse runner directories and retain selected native output, but keep task-specific Git branches. They do not migrate old `task-*` trees or reuse those trees as slots (`docs/session-runtime-invariants.md:328`).

## 3. What an enabled retention tick will do first

Given the brief's default-start premise, the server registers this job independently of the slot flags. Actual activity can also be prevented by runner unavailability/capability, an override, job failure, or every candidate being held. Source alone cannot distinguish those live hypotheses.

The server selects tasks bound to that runner with non-null RemoteWorktreePath, ordered by CompletedAt then task id (`RunnerWorktreeRetentionService.cs:132`). It classifies the whole population first. Among eligible trees, **never-sent candidates go first in that order**, then due retries ordered by last send (`:147`). At most 25 candidates enter the per-send loop, five seconds between sends, and it starts no new request after the tick budget. A failed recheck can reduce actual sends; a runner refusal consumes a sent request. Thus the first removals would be the oldest never-sent eligible mirrors that also pass the runner checks, **not the oldest 25 filesystem directories**. Existing cursors can change the order. A 120+ count cannot predict names, deletion count or completion time without current DB/cursor/runner evidence.

Server protections (`RunnerWorktreeRetentionService.cs:338`):

- Only Worktree tasks on the named runner, Succeeded/Failed/Canceled, non-null CompletedAt at least the configured age, exact direct-child `task-<task-id8>`, and a branch recorded.
- No slot id, SourceLanding, repair source, or Mutation task. No land request/start timestamp or pending land request.
- No Pending/Ready/Held settlement sync debt; no other Queued/Dispatched/Working/Blocked task sharing the path; no Created/Starting/Running/Stopping session bound directly or via the task's events.
- Report preservation has a precise condition: it holds if Result is null **and** ResultFilePath points inside this mirror (`:379`). It does not unconditionally require every terminal task to have a non-null Result. This is narrower than the prose in `docs/docker-stack.md:788`.
- Every candidate is reread before sending; task disappearance, changed classification or changed path defers it (`:180`). Unknown/ambiguous inventory names are only reported, never selected for removal (`:454`).

Runner protections (`src/Antiphon.SessionRunner/RunnerWorkspaceRetention.cs:226`):

- Own root, exact task id/branch, canonical direct child with no link, own registered repository and unlocked worktree. Slot leaves and ledger-owned paths are protected; unreadable ledger also holds.
- HEAD on the task branch; clean tracked and untracked status including submodule changes; no HEAD commits outside origin remote-tracking refs; live origin must still have the exact branch and contain HEAD, with its tip known locally (`:296`). A deleted origin branch holds, even if work may have landed elsewhere.
- No cwd/fd holders; incomplete or timed-out process scan holds (`:367`). Dispatcher additionally protects recently mirrored/launched paths for `PhoneHome:RetentionRecentUseMinutes=10` and live sessions (`PhoneHomeCommandDispatcher.cs:1004`; `PhoneHomeSettings.cs:97`). Production now wires the workspace-use gate (`Program.cs:90`); the older comment at retention `:261` must not be read as its current composition.
- Ignored secret-floor files and uncertain ignored listings hold; collapsed ignored directories are inspected for secrets (`RunnerWorkspaceRetention.cs:502`). Ordinary ignored build output can disappear when Git removes the tree; nested repositories are counted, not categorically preserved (`:389`).
- Execute repeats checks, moves via `git worktree move` to the sibling retiring root on the same device, then runs unforced `git worktree remove` outside the command lock (`:93`, `:115`, `:141`). A failed remove leaves registered retiring residue for later handling. It does not prune registrations globally, fetch, remove branches, or delete Docker volumes (`:23`).

**Salvage distinction:** the runner retention implementation checks current HEAD publication but contains no desktop-style reflog salvage/ref-pruning operation. Do not attribute CARD-1708's salvage guarantee to this tick. Desktop S4a's separate retirement calls the salvage path (`server/Application/Services/TaskWorktreeRetirementService.cs:479`), documented with 64-tip bound, collision refusals and 30-day ref retention at `docs/orchestration-loop.md:2619`. Slot release separately checks branch tip/HEAD reflog for unpublished commits and quarantines unsafe content (`RunnerWorkspaceSlotPool.cs:685`).

Kept trees back off 60 minutes, doubling to 24 hours for repeated reasons; changed task facts/reason can make them due sooner. Never-sent trees take precedence, so repeated dirty trees do not monopolize the cap (`RunnerWorktreeRetentionService.cs:25`). Success clears RemoteWorktreePath and writes a `runner_mirror_retained` task event only after a removed/fully absent reply (`:386`).

### Watching decisions and obtaining missing receipts

Use the **operator context**, not this child task token. Both previews and residue-run reads reject a child token (`server/Api/Endpoints/AgentTaskEndpoints.cs:143`, `:158`; `TaskWorktreeRetirementService.cs:292`). These commands are for the operator; none ran here:

```powershell
# From the canonical desktop checkout, existing credential helpers keep tokens private:
pwsh -NoProfile -File scripts/logs.ps1 -Source desktop-server -Tail 500
pwsh -NoProfile -File scripts/logs.ps1 -Source hangfire -Tail 100
pwsh -NoProfile -File scripts/logs.ps1 -Source server2-runner -Tail 100
```

Search the server output for the exact job message shape:

```text
Runner worktree retention tick runner=server2 examined=<n> eligible=<n> backedOff=<n> sent=<n> stopped=<reason|-> run=<guid>
Runner worktree retention <runner> task <guid> path <path> mode <mode>: <outcome> <reason> ...
```

Sources: `RunnerWorktreeRetentionJob.cs:24`, `RunnerWorktreeRetentionService.cs:449`. Desktop Serilog lives under the primary server content root's `logs/antiphon-*.log`; the logs helper resolves that root instead of the linked worktree (`docs/logs.md:11`). Hangfire history is in-memory and disappears at server restart (`docs/logs.md:55`); residue runs are durable DB evidence.

Operator HTTP operations, using the established operator authentication context without a child task header:

```text
GET  /api/version
GET  /api/session-runners/server2/status
GET  /api/session-runners
POST /api/agent-tasks/worktree-residue/runner-retention/server2/preview
GET  /api/agent-tasks/worktree-residue/runs/<returned-runId>?page=0
POST /api/agent-tasks/worktree-residue/desktop-legacy-release/preview
```

Runner preview is **always DryRun**, but requires retention Enabled and server2 in Runners; it still records a run and send cursors, so it is not side-effect-free observation (`RunnerWorktreeRetentionService.cs:80`, `:205`, `:442`). It can defer subsequent ordinary requests through backoff. Desktop preview runs even while its Enabled flag is false and never files a release (`DesktopLegacyReleasePolicyService.cs:129`). Follow run pages from **0** until `(page + 1) * pageSize >= totalRows`; the DTO has `rows`, `page`, `pageSize`, `totalRows`, not a hasMore field (`TaskWorktreeRetirementService.cs:292`). Inspect RunnerMirror, RunnerInventory and DesktopLegacyPolicy lanes and all reason codes. A preview's would_remove rows are not a production execution receipt.

## 4. Desktop S4a bounds

Prefix `WorktreeLegacyRelease:`; type and shipped values: `server/Application/Settings/WorktreeLegacyReleaseSettings.cs:13`, `server/appsettings.json:230`.

| Key suffix | Default / constraint |
|---|---|
| `Enabled`, `Execute` | `true`, `true` |
| `MinTerminalHours` | `48`; valid **48..8760**, so 24 fails startup validation |
| `MaxReleasesPerTick` | `25`; 1..100; bounds prechecks, including held outcomes |
| `PauseBetweenSeconds` | `5`; 0..300; follows actual filing/removal |
| `MaxTickMinutes` | `20`; 1..55 |
| `RefusedRecheckMinutes` | `60`; positive |
| `RefusedRecheckMaxHours` | `24`; positive; retry ceiling |
| `MaxRefusedAttempts` | `5`; 1..50; then retry_exhausted |
| `SalvageRefRetentionDays` | `30`; 1..3650; C# default |
| `RecurringJobId` | `antiphon:desktop-legacy-release` |
| `Cron` | `15 * * * *`, UTC |

Unknown keys, invalid scalars and array/object/null shapes fail startup; invalid reload disables the policy (`WorktreeLegacyReleaseSettings.cs:58`, `:139`). Only `card-task-<id8>` desktop worktrees under `Git:WorktreeBasePath` qualify. Server facts protect open owners, land/sync debt, shared paths, live sessions, missing report, open dependents and leased slots; guarded removal independently proves linked-worktree identity, clean status, publication and ignored-content safety. See `DesktopLegacyReleasePolicyService.cs:19`, `:149` and detailed guards at `docs/orchestration-loop.md:2600`.

A runner in `RunnerWorktreeRetention:Runners` is skipped by desktop mirror cleanup with `runner_mirror_owned_by_retention`; the desktop half can still retire (`docs/orchestration-loop.md:2643`). Leaving server2 in that allow-list while disabling runner retention does not transfer deletion responsibility to S4a. Source branches on origin remain, while desktop automatic release can remove its local branch and keep salvage refs under the separate retention conditions (`docs/orchestration-loop.md:2607`).

## 5. Evidence-based operator sequence and reversals

These are **existing configuration and operational controls**, not an implemented fix or a deployment performed by this task. Desktop commands must be run by the caller on the canonical main checkout from the independent operator shell described in `docs/apphost-runbook.md:87`. The operator's enable/restart authority is already recorded in the brief; no new permission is requested. This read-only task's explicit scope prevents executing it here.

1. **Establish current state before another rollout.** Read desktop `/api/version`, both runner status/catalogue entries, server retention log/run ids and the current effective source checkout. Check `logs/apphost.restart.lock` and `logs/apphost.launch.lock`. The brief's rc SHA is the investigation baseline, not permission to overwrite a later active rc tip. Save the existing values of the specific non-secret keys before editing. Reversal: no change to reverse.

2. **Raise runner retention to 24 hours on the desktop first.** Existing supported command, under the server's owning Windows account:

   ```powershell
   dotnet user-secrets set 'RunnerWorktreeRetention:MinTerminalHours' '24' --id antiphon-server
   ```

   Alternatively merge `"RunnerWorktreeRetention": { "MinTerminalHours": 24 }` into the canonical checkout's ignored `server/appsettings.Development.json`, preserving every existing property. The two providers must not conflict: user secrets/environment can shadow this JSON. Keep Enabled/Execute/list/cap/cron as the shipped values unless an explicit pause is wanted. For a deterministic pause before the age change, the existing ignored JSON controls are `Enabled=false` or `Execute=false`; wait for any already-sent operation before treating the pause as effective. Reversal: restore the prior key/value; if absent before, `dotnet user-secrets remove 'RunnerWorktreeRetention:MinTerminalHours' --id antiphon-server` restores lower-provider behavior (12 with defaults). Removing a key is not guaranteed to restore 12 when other overrides exist. Raising the floor cannot restore a tree already removed.

3. **Prepare the server slot switch behind a closed role gate, then restart once.** Merge these exact server overlay settings, without overwriting unrelated overlay contents:

   ```json
   {
     "Delegation": {
       "WorkspaceSlotsEnabled": true,
       "WorkspaceSlotRoles": "none"
     }
   }
   ```

   An equivalent restart-bound switch command is `dotnet user-secrets set 'Delegation:WorkspaceSlotsEnabled' 'true' --id antiphon-server`; keep the role gate in reloadable server JSON and ensure no same-key secret/environment overrides it. Retention settings above can share this restart. From the main checkout whose HEAD matches the intended rc source:

   ```powershell
   pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha <full-current-intended-rc-sha>
   Invoke-RestMethod http://localhost:17202/api/version
   ```

   The restart checks source-root HEAD and loaded server SHA, not merely health (`scripts/restart-apphost.ps1:184`; `docs/apphost-runbook.md:29`). It neither pulls rc nor upgrades the separate desktop runner. Reversal before any lease: restore old settings and restart. After leases exist: keep both flags on and set roles `none` until release is complete; only then restore false/restart.

4. **Runner enablement is blocked by the confirmed deployment transport gap at this SHA.** Do not append a key to stack.temp.env and claim success, or run named phases expecting them to forward it. Commission the separate tracked change noted below, then use its reviewed rc SHA and its documented setting interface. Once runner support is actually delivered, the existing phase commands are:

   ```powershell
   pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <reviewed-rc-sha> -Phase deploy-temp
   # Smoke/version/capabilities and the slot canary below BEFORE drain-old.
   pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <reviewed-rc-sha> -Phase drain-old -WaitIdleMinutes 240
   # Only after the documented zero-work gate:
   pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <reviewed-rc-sha> -Phase redeploy-old
   # Repeat smoke and slot canary on server2 BEFORE drain-temp.
   pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha <reviewed-rc-sha> -Phase drain-temp -WaitIdleMinutes 240
   ```

   These commands alone **do not enable slots at the investigated SHA**. Use one phase per gate, follow `docs/docker-stack.md:120`, and finish the separately gated retire-temp preview/apply at `:130`; do not execute Phase all. Runner-first support with roles-none keeps new work legacy while capability is checked. Rollback before drain-old: leave old accepting. After drain-old, if old is verified healthy, existing rollback is `pwsh -NoProfile -File scripts/runner-drain.ps1 clear -RunnerId server2 -Reason 'rolling rollback'`. If upgraded old fails smoke, existing rollback is `pwsh -NoProfile -File scripts/runner-drain.ps1 drain -RunnerId server2 -RedirectTo server2-temp -Reason 'post-upgrade smoke failed'`, with accepting temp verified first (`docs/docker-stack.md:125`, `:128`). No force, manual slot deletion or unreviewed image rollback is authorized by these commands.

5. **Open a narrow canary role, then verify an actual lease.** Change server `WorkspaceSlotRoles` from `none` to **`Plan`** in the reloadable overlay. Check the matching runner row's `features` includes `workspaceSlotV1` (`server/Application/Services/SessionRunnerCatalogue.cs:155`). On a capable runner, dispatch one sanctioned ordinary primary-repository Worktree Plan task (then repeat on server2 after upgrade). Use the caller's existing card/project/full goal rather than inventing a separate task here:

   ```powershell
   $canaryGoal = Get-Content -LiteralPath '<existing-sanctioned-canary-brief>' -Raw
   pwsh -NoProfile -File scripts/delegate.ps1 -Role Plan -Kind Codex -Worktree -Runner server2-temp -Dir '<canonical-primary-checkout>' -Card <existing-card-guid> -Title 'workspace slot canary' -Goal $canaryGoal
   ```

   A decision event alone is insufficient: it is written **before** the RPC (`RemoteWorkspaceService.cs:238`). Confirm:

   - `GET /api/agent-tasks/<canary-guid>` contains `WorkspaceSlotDecision` detail `workspace_slot_decision: slot preparation=<guid> reason=listed` (events are returned by `AgentTaskService.cs:2885`, `:2977`).
   - The persisted task has matching runner, slot id, generation and `RemoteWorktreePath=/work/worktrees/ws-NN` (or re-homed suffix). These are set from the runner reply (`RemoteWorkspaceReadiness.cs:22`). Operator read-only SQL:

     ```sql
     SELECT "Id", "Attempt", "RunnerId", "RemoteWorktreePath",
            "RemoteWorkspaceSlotId", "RemoteWorkspaceSlotGeneration",
            "RemoteWorkspaceSlotReleasedAt"
     FROM "AgentTasks" WHERE "Id" = '<canary-guid>';
     ```

   - On the corresponding runner, `/work/workspace-slots/ledger.json` has the same task/preparation, slot id, leaseGeneration, path and Leased state. The ledger uses web/camelCase JSON (`RunnerWorkspaceSlotLedger.cs:109`). Observe task execution and its normal stored report; do not infer a lease from directory existence or `workspaceSlotV1` alone. Stored assertion for precisely this correlation: `C0824WorkspaceSlotRoleGateTests.cs:39`.
   - After terminal settlement, no live seat/debt/follow-up remains and release is confirmed; server clears the path only on released/already_free/quarantined (`WorkspaceSlotReleaseService.cs:46`). There is no general workspace-slot inventory HTTP route at this SHA: `docs/ops-http.md:418` explicitly leaves `GET /sessions/workspace-slots` to S4. `/api/session-runners/<id>/slots` reports **session seats**, not this worktree ledger (`SessionRunnerEndpoints.cs:228`).

   Canary rollback: set roles back to `none`, verify the next fresh preparation records legacy `reason=roles_none`, and let accepted leases drain. Preserve the preparation/retry caveat above. After canary acceptance, `WorkspaceSlotRoles="*"` admits every eligible non-Mutation role; the exact default behavior is source-established, not a rollout acceptance result.

6. **Watch the first production ticks.** Confirm an executing run with server2 at 24h, inspect every page and kept reason, and retain run ids alongside version evidence. Do not use a preview as proof the recurring job ran. Dry-run rollback is `RunnerWorktreeRetention:Execute=false`; kill switch is Enabled=false. Apply through the same supported server provider, checking for higher-priority shadowing; restart when job registration or captured switches change. S4a rollback uses its separate Enabled/Execute keys and leaves its 48h floor intact.

## Not done, noted

Potential smallest Code slice for caller triage: a default-false runner-slot Compose passthrough in `docker-compose.server2-runner.yml` (inherited by temp), plus durable validated setting transport/preservation in `scripts/c590-remote.sh` for both fixed env writers and standing/temp wrappers (and `scripts/deploy-server2.ps1` only if its public interface needs it); relevant existing coverage is `scripts/test-deploy-server2.ps1`, Compose fixture `scripts/fixtures/c1105-compose-v218-config.json`, `C0824WorkspaceSlotServerTests`, `C0824WorkspaceSlotRoleGateTests` and `C1677RetentionSettingsTests`; no fix, test design or implementation was performed here.

## Remaining uncertainties and handoff

- Live desktop version, startup/job success, effective overrides and Hangfire state: resolve with operator `/api/version`, source HEAD and server logs/dashboard before restart erases in-memory history.
- Which of the reported 120+ trees are eligible and their actual send order: resolve with current AgentTasks, sync/land/session ownership, retention cursors and runner filesystem/process/git state; retain the first executing residue run id and all pages. No exact tree list is asserted here.
- Actual runner flag/capability at each rollout stage: resolve with the runner catalogue plus inspected non-secret container setting, followed by correlated task/ledger canary evidence. No production slot lease was observed.
- Whether the operator intends to eliminate desktop per-task checkouts as well as runner mirrors: slots alone do not activate the separate RefOnlyDesktop feature. No additional feature was enabled or scoped by this report.
- Runner reflog-only preservation does not have the desktop S4a salvage mechanism in the inspected source. No live loss was observed; this report makes no claim of equivalent custody guarantees.

Validation: source and documentation inspection only; **0 builds, 0 tests**, as explicitly required. Only this Markdown report is committed. No live approval was requested or operational setting changed.

--- next stage ---
next: decide
handoff: Decide activation scope using the confirmed desktop/runner split and current-state evidence; the operator sequence uses existing controls, but named runner slot enablement needs a separately commissioned Code task for the documented deployment transport gap. Obtain first executing retention run and correlated slot canary receipts before claiming activation.
artifact: docs/investigations/2026-11-06-investigate-slot-and-retention-settings.md
