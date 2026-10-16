# RC activation stopped before mutation: 2026-10-16

Task: feb1f83b-6798-4f04-a6f5-b8c52ede4c1b (Deploy, CARD-1311).
Requested release: `rc/2026-10-15` at exactly
`b7265a05aa8773f08dbf457b93c7561e7874822d`.

Latest status (11:11 UTC): the caller activated canonical/AppHost/desktop on RC.
The resumed server2 rollout stopped at the cache Fixture prerequisite with
`FixtureSeedFailed`; server2 still accepts work on its previous image. See the
appended resumption record. The original first-attempt record below is retained.

Outcome: **blocked before switching or restarting anything**. The local execution
process is in a Windows Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`.
The canonical restart implementation has no Scheduled Task handoff, and the
AppHost runbook expressly requires an independent operator PowerShell outside
that job. No deployment, drain, runner restart, checkout switch, fetch, reset,
lock deletion, or process termination was performed. The canonical stack remains
on its original source tree. This report is the only task deliverable.

## Required answers before activation

1. **Canonical checkout and client watcher.** Restart builds the files in
   `C:\src\Antiphon`; the deployment worktree cannot substitute for it. In a safe
   operator environment, first establish no pending/running land and no half-reset
   worktree, record the old matching HEAD/API SHA, inspect both AppHost locks,
   fetch origin, and require the remote RC ref to equal the full SHA above.
   Preserve the two untracked patch files. A non-forced
   `git switch --detach b7265a05aa8773f08dbf457b93c7561e7874822d` in the canonical
   checkout would select the exact committed tree without moving master. Stop if
   Git reports an obstruction; do not stash, clean or reset the user's files.
   The worktree guard tests main-checkout identity rather than requiring master.
   Run canonical `restart-apphost.ps1 -ExpectedServerSha <full-rc-sha>` only from
   the independent operator environment, then require health, the exact API SHA,
   `land-v2`, and `verify-dev-stack.ps1 -SkipBrowser` success.

   Port 17203 runs `vite preview` plus `vite build --watch` against that checkout's
   `client/dist`. **Leave the canonical checkout pinned to RC while serving RC.**
   Switching back to master while the watcher runs permits a rebuild of master
   and would mix server RC with a different client. A later checkout change is
   another coordinated activation, with a fresh client build/status check.
   AppHost restart owns the client/watcher teardown and relaunch; no independent
   watcher manipulation was attempted here.

2. **Landing and canonical master advancement.** Schema-3 landing uses a dedicated
   land workspace and the observed remote target, publishes first, then attempts
   local target advancement. In `AgentTaskLandingProtocol.CanonicalDecisionAsync`
   and `ScanTarget`, if canonical HEAD is detached or on RC and master is not
   checked out elsewhere, the master target has no checkout: advancement uses
   compare-and-swap `update-ref`, leaving the RC files and HEAD in place. It does
   not silently turn the serving checkout back into master. If master is checked
   out in canonical, status includes untracked files; the user's patches can
   produce `canonical_checkout_dirty`, a post-publication residue rather than
   proof publication failed. A master checkout in another linked worktree or
   unprovable identity has its own refusal/residue gates.

   `target_changed` belongs to the legacy `DelegationWorktreeService` merge path,
   whose clean-target and checkout identity must stay stable across the operation.
   Switching during an in-flight operation can invalidate those expectations.
   It is not the schema-3 publication race code (`remote_changed_before_push`).
   Wait for pending lands before any canonical checkout transition, and do not
   disable guards or infer that every landing path behaves identically.

3. **server2 image and source identity.** Each named rolling phase must receive
   `-Sha b7265a05aa8773f08dbf457b93c7561e7874822d` from canonical at that exact HEAD.
   The generated host-case manifest contains `sourceSha=$Sha` and
   `c604Branch='master'`. The latter is the clone/fetch branch for repository
   preparation, not an instruction to build master: `c590-remote.sh`'s
   `ensure_checkout` fetches the requested SHA, detaches at it and asserts HEAD
   equality. `build_server2_images` uses that checkout as Docker context and
   `SOURCE_REVISION=$SHA`, tagging session-testing and server images with the
   first 12 SHA characters. Image revision checks and registered runner
   `buildVersion` must still prove the full requested SHA. The runner's task
   repository fetch of master is separate from the built runner image source.
   None of these image/build claims was exercised live in this pass.

## Observations and command record (UTC)

All times below are 2026-10-16 UTC. Read-only output was inspected in the task
session; no secrets were printed. Report writing/commit/push occurs only on
`feat/card-task-feb1f83b` in the assigned worktree.

| Time | Action | Result |
|---|---|---|
| 10:58:53 onward | Read full external task brief, then orchestration autonomy/landing, AppHost and staged Docker rollout owners before operational work | Identified the existing CARD-0381 checkout limitation and mandatory stop behavior. Read release/HTTP guidance and relevant script/landing implementation to resolve RC source questions. |
| 10:59:48 | Inspect canonical `git status`, HEAD, branch and AppHost lock files | Branch `master`; HEAD `6e23e0b4c8d6b2ae12218c2c0058593dbf773530`; tracked files clean; two untracked patches. Cached origin/master status behind 32. Restart lock stamp `38784 2026-10-16T01:19:04.7304365Z`; launch lock absent. Restart stamp is older than the 15-minute active threshold; not removed. |
| 11:00:07 | Read-only Windows `IsProcessInJob` / `QueryInformationJobObject` probe in a local inherited PowerShell child | PID 34948, both API calls succeeded; `inJob=true`, extended limit flags `10240` (`0x2800`), kill-on-job-close bit `8192` set. This proves the unsafe custody condition rather than assuming it from the UI. |
| 11:00:07 | GET `/api/version`, `/health`, desktop `/capabilities` | API version `6e23e0b4c8d6b2ae12218c2c0058593dbf773530`, matching canonical HEAD; `land-v2` and `operator-shutdown-v1`; health HTTP 200. Desktop runner build `893ddfceeebf4d13d9401848c535abada3983c27`. |
| 11:00:08 | `git ls-remote origin refs/heads/rc/2026-10-15 refs/heads/master` | Remote RC exactly `b7265a05aa8773f08dbf457b93c7561e7874822d`; remote master `2d2b30364d1793aedd9dae725a68246e127e7dd0`. No fetch or branch movement. |
| 11:00:27 | Canonical `client-mode.ps1 -Status` | Built mode, serving, PID 17328; since 01:21:40, last build 01:21:45 as displayed by the status script; client root HTTP 200, observed built mode. The display's date values have no explicit offset, so they are retained without conversion. |
| 11:00:27 | Canonical `check-daemon-build.ps1` | Desktop session-runner stale for 7 source changes even against the existing canonical HEAD; fake-gateway has no newer source in its project closure. This script only inspected build provenance; no build was run. |
| 11:00:27 | `runner-drain.ps1 status` for server2 and temp | Main available, dispatch eligible, accepting, not draining; build `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`; sessions=3, runnerSessions=3, queuedTasks=0. Temp unavailable/offline, not eligible/accepting; draining toward main, retireWhenIdle=true, retiredAt `2026-10-11T14:23:12.192262+00:00`; sessions=0, queuedTasks=0, runnerSessions=null, buildVersion=null. Host container absence not established. |
| 11:00:29 | GET `/api/agent-tasks/pipeline` (fleet-wide deliberately, for restart safety) | No `Working` rows at this snapshot. Dispatched: this Deploy and Plan `58a7e5b9-30a8-4b75-b716-5ad6e321893a` (CARD-1313). Desktop occupied=1, server2 occupied=3. No stop/kill was issued. Runner default remains server2. Pending-land closure was not completed because activation had already stopped. |
| 11:00:38 | Hash patches; recheck canonical status/HEAD; inspect restart launch helper | Both patches present; canonical remains master at original HEAD. `Start-AppHostDevLaunch` uses `Start-Process pwsh`; no supported independent task-only handoff was established. |

Untracked user files retained, with SHA-256 observed after the stop:

- `C:\src\Antiphon\docs\investigations\2026-09-30-card-0859-codex-startup.patch`:
  `248d70d1053dd9534dfbf886d064b11f7c23d76557635dc2fa38c127531e2a9b`
- `C:\src\Antiphon\docs\investigations\2026-09-30-card-0860-codex-c4.patch`:
  `de49c49d6551868a4ec8e3bd5f0e439de38a5c5c0c64321e4f7cf6376c2d1855`

## Gates not run and recovery options

No AppHost restart or full stack verification was run. No server2 phase was
started, including check-census, host jq qualification, cache Fixture/Inventory/Seed,
deploy-temp, smoke/canary, drain-old, redeploy-old, smoke/Both, drain-temp or
retire-temp. Tests/builds: **0 executed, 0 failures; activation incomplete**.
The roster counts supplied in the brief are upstream evidence, not this pass's
execution results. No service reports the requested RC SHA in these observations.

The desktop runner needs a source update; its restart remains **pending**.
The documented soft restart preserves/re-adopts detached PtyHosts, but this pass
does not establish an independent safe restart context, and source must first be
the intended RC tree. Re-read the pipeline before resumption; do not interrupt
Working tasks. No `-Hard` need was established; never use `-KillSessions`.

Caller options:

1. Perform the canonical RC switch and restart from an independent operator
   PowerShell outside the kill-on-close job, following the preconditions above.
   Verify exact RC API SHA/land-v2 and stack health, then resume Deploy for desktop
   runner assessment and every staged server2 gate. Keep canonical pinned to RC.
2. Commission the existing CARD-0381 Scheduled Task restart-handoff repair as a
   separate Code/Review change, with safe launch custody verified, before asking
   an agent in this execution environment to restart AppHost.

Launching a new window from this job, bypassing the worktree guard, using a bare
dev-aspire launch, or creating an ad hoc scheduled wrapper is not a documented
recovery. This pass made no such workaround. No half-switched stack was left.

--- next stage ---
next: Deploy
handoff: Blocked on a documented independent AppHost restart environment (or separately reviewed CARD-0381 repair). Preserve canonical patches; activate exactly b7265a05aa8773f08dbf457b93c7561e7874822d, keep canonical pinned, recheck live task/land gates, verify AppHost and desktop runner, then execute all server2 rolling gates with the same SHA.
artifact: docs/investigations/2026-10-16-rc-activation.md

## Resumption: caller activated RC; server2 cache prerequisite failed

At resumption the caller reported that, from its independent environment, it had
pinned canonical detached at the requested RC, restarted AppHost successfully,
verified exact API SHA plus `land-v2`, passed `verify-dev-stack.ps1 -SkipBrowser`,
and soft-restarted the healthy desktop runner. Those restart/verification results
are caller evidence. This resumed pass did not restart or change either service,
switch canonical, or change its untracked patches.

All commands below ran from `C:\src\Antiphon`, except report editing/commit/push
in the assigned task worktree. Each rolling invocation supplied
`-Rolling -Sha b7265a05aa8773f08dbf457b93c7561e7874822d` and the explicit project
`-ProjectId d4ea7ae9-e769-474b-95b9-aa25fbc1303f`. Every command was awaited.

| UTC on 2026-10-16 | Gate / observation | Result |
|---|---|---|
| 11:08:17 | Read the canonical RC rollout owner and scripts; inspect canonical and API identity | Canonical detached at `b7265a05aa8773f08dbf457b93c7561e7874822d`; only the original two untracked patches; API exact same SHA with `land-v2`; `/health` HTTP 200. |
| 11:08:25–11:08:45 | `deploy-server2.ps1 ... -Phase check-census -ProjectId d4ea7ae9-e769-474b-95b9-aa25fbc1303f` | Exit 0. Project Antiphon resolved explicitly. Main: open=11, boundOpen=3, landPending=0, scopes=2. Temp: open=11, boundOpen=0, landPending=0, scopes=2. Receipt root `C:\src\Antiphon\.antiphon\rolling-server2\c72737a9b4c10a82`. |
| 11:08:49–11:08:54 | `deploy-server2.ps1 ... -Phase check-host-jq -ProjectId d4ea7ae9-e769-474b-95b9-aa25fbc1303f` | Exit 0, qualified existing `/usr/local/bin/jq`, jq-1.7.1, uid/gid 0, mode 755; trueExit=0, falseExit=1, installed=false. Digest `5942c9b0934e510ee61eb3e30273f1b3fe2590df93933a93d7c58b81d19c8ff5`. No provision phase: the documented provision authorization is only `HostJqMissing`, which did not occur. Check/no-op receipt retained. |
| 11:09:05–11:10:30 | `verify-card0849-caches.ps1 -Case Fixture -Sha b7265a05aa8773f08dbf457b93c7561e7874822d` | **Failed**, wrapper exit 1; case receipt `accepted=false`, `diagnosis=FixtureSeedFailed`, `exit=2`. Local result lastWriteTimeUtc `2026-10-16T11:10:30.4428303Z`. Mandatory stop before Inventory, Seed or gate 1. |
| During failed Fixture | stderr symptom | Twice: `rm: cannot remove '/fixture/cache/stage-c8490fb677fc8c7642420-37NLzwfN/npm-cache/_logs/2026-10-16T11_10_17_996Z-debug-0.log': Permission denied`. This is observed failure context, not a proven root-cause attribution. No repair, permission change or retry was attempted. |
| 11:10:54–11:10:57 | Read main/temp status and canonical state after stop | Main healthy/accepting and not draining, buildVersion still `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`; sessions=3, runnerSessions=3, queuedTasks=0. Temp remains offline, not eligible/accepting, draining toward main, retireWhenIdle=true, retiredAt `2026-10-11T14:23:12.192262+00:00`; sessions=0, queuedTasks=0, runnerSessions=null. Canonical remains detached at requested RC with the two patches. |
| 11:10:54 | Read-only SSH container census | Main container `fc2983ea4991fe85b31f54ee2ef66150e0d8fef03897d6cf1c2d6ca9c0889832`, StartedAt `2026-10-11T13:42:57.09302366Z`, image ID `sha256:ea8bafe24373694b3421d63afdffb4533faf0710f709d96dea661123b55e6835`. Main container SOURCE_REVISION is `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`. Temp Compose project census (including stopped containers) returned no IDs, SSH exit 0. |
| 11:10:54 | Requested pipeline URL `/api/agent-tasks/pipeline?unscoped=false&projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f` | HTTP 422 `validation_failed`: `projectId and unscoped are mutually exclusive.` This is not evidence of no Working tasks. No drain was attempted. |
| 11:11:20 | Documented project-only pipeline query `/api/agent-tasks/pipeline?projectId=d4ea7ae9-e769-474b-95b9-aa25fbc1303f` | Successful snapshot: only Working row is this Deploy `feb1f83b-6798-4f04-a6f5-b8c52ede4c1b`; Plan `58a7e5b9-30a8-4b75-b716-5ad6e321893a` is Dispatched. Pipeline row runnerId is not exposed, so no runner assignment is inferred from it. Runner defaults revision 2, global server2, no kind overrides. Re-read and resolve task runner identity before any future drain. |
| 11:11:20 | Read-only SSH image identity | The main image's `org.opencontainers.image.revision` label is `7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce`, matching registered buildVersion and differing from requested RC. No claim of RC activation on server2. |

Retained generated evidence (ignored, not committed):

- Census: `C:\src\Antiphon\.antiphon\rolling-server2\c72737a9b4c10a82\`.
- Host jq: `C:\src\Antiphon\.antiphon\rolling-server2\c72791dac230f7e6\host-jq-check-host-jq.json`,
  observedAtUtc `2026-10-16T11:08:53.4175233Z`, sshExit=0, requested source SHA.
- Fixture wrapper: `C:\src\Antiphon\.antiphon\c849-c8490fb677fc8c764242\0\c590-result.json`.
- Fixture case: `C:\src\Antiphon\.antiphon\c849-c8490fb677fc8c764242\0\runner-cache-fixture\c590-result.json`.
  Both result files contain `{"accepted":false,"diagnosis":"FixtureSeedFailed","exit":2}`.
  Case receipts record two completed groups (`PASS F-1`, `PASS F-2`) and 15
  expected-red control observations before the failure; these do **not** make
  the fixture pass. The fixture cleanup-residue and cleanup-ledger files were
  empty when read. Remote gate evidence is under
  `/work/test-evidence/c8490fb677fc8c7642420/runner-cache-fixture/` (manifest
  runId `c8490fb677fc8c7642420`, source SHA exactly the requested RC).

Resumed verification totals: **2 prerequisite checks passed; 1 cache Fixture
command failed**. No full test suite or standalone build was run. Inventory,
Seed, deploy-temp, temp smoke/canary, drain-old, redeploy-old, main smoke/Both,
drain-temp and retire-temp were **not run**, because the earlier mandatory
prerequisite failed. No standing runner was stopped, replaced or drained; no
sessions were killed. No background command remains owned by this pass.

Next action belongs to the caller: commission a bounded investigation/repair of
the cache fixture's seed/cleanup permission failure on the requested RC. Retain
the receipts; do not bypass Fixture, reset caches, change live ownership, or
restart services as an improvised recovery. Resume the full remaining staged
sequence only after the prerequisite is qualified, with canonical still pinned
to the intended RC and a fresh task/land census. If a source repair changes the
release SHA, the caller must explicitly supply the new tested RC target.

--- next stage ---
next: Debug
handoff: Investigate FixtureSeedFailed at RC b7265a05aa8773f08dbf457b93c7561e7874822d using retained c849-c8490fb677fc8c764242 evidence; permission-denied fixture npm log observed. Server2 remains accepting on 7507b3cfe0e2f7660a0c9f9f8eb19dc89a937bce; temp retired/absent; no drain/deploy occurred. Preserve canonical RC and user patches, and do not bypass the cache prerequisite. After qualified recovery, resume Deploy from fresh preconditions.
artifact: docs/investigations/2026-10-16-rc-activation.md
