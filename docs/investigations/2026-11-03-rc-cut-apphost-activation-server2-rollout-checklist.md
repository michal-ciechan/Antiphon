# New rc cut, AppHost activation and server2 rollout: checklist (2026-11-03)

Task f686ba8a (Investigate, read-only). Base master `7b920d84aae97139998f940ee2cce3f457bb6e70`.
Evidence came from repository reads, `git` on origin refs, unauthenticated GETs on
`$ANTIPHON_API`, and two read-only SQL transactions against the desktop dev Postgres. No
build or test ran. No host, runner, ref, setting or file under `C:\Antiphon` was touched.

Operator question (2026-11-03): "As we have failures, are those high prio? If it can we cut
based on latest master and also update the running apphost and do a server2 rollout?"

## Answer first

- **The three failures of run 7f8e9fd1 are not high priority for the hosts.** They share one
  cause: the land-graph audit's pass-2 differential refuses a git-ignored
  `tests/Antiphon.E2E/TestOutput/` file matched by the SDK default `None` glob
  (`docs/investigations/2026-11-03-c1385-differential-none.md`, commit 7d0011c2c on master).
  It is a test-tooling defect, not a product one. It makes 7f8e9fd1 non-green, so
  candidate `rc-2026-11-01-10a826b9d61f` can never publish. Host safety is not affected. The
  fix is on master: 9ee7f706a, then the Review 436ebdc6 D-1 rework, all ancestors of
  `7b920d84a`.
- **Yes, a cut from master is possible now, and master loses nothing from the rc lines.**
  `git cherry origin/master origin/rc/2026-11-01` gives 24 `-` and 0 `+`: every rc/2026-11-01
  commit is already patch-equivalent on master. For `rc/2026-10-27` (the line the hosts run),
  it gives 22 `+`. All 22 are either docs/report-only, or code-equivalent
  (125c720b9/3cb215cf3/9c271b7e1/80b3a59c4/b226c226b, the five pairs proven in commit
  20c03ab43's investigation), or already covered on master (db8a211f2/1f3794102:
  `Card1619*` rows are present in master's `tests/test-execution-policy.json`).
- **No document requires a Windows qualification before the hosts run an rc tip.**
  `docs/release-gates.md:34-35,586-592`: the hosts run the current rc tip. The Full rc
  qualification earns permission to *publish* (tag) only (`release-gates.md:59-75`).
  "Interim" in that document is `InterimVerification`, a dormant verification-round profile
  (`Enabled=false`, `release-gates.md:654-731`). It is not an activation state, and nothing
  about it applies to this cut. The hosts already run an unqualified mid-line build
  (`8edda999`, `rc/2026-10-27`). The activation gates that do apply are the startup-path
  probe and rehearsal (B1-B2 below), restart exit 0 with SHA equality, and the rollout's
  own gates.
- **Recommended plan, option (1) refined.** Cut now, after the in-flight batch-25 Merge
  (0f4dc1cc, CARD-1645) publishes. Activate the desktop AppHost and runner now. Run the
  server2 rollout now. Let 7f8e9fd1 finish (about 15:00Z). Then start the new cut's Windows
  Full run failed-first, from a seed exported from 7f8e9fd1. Section D gives the reasoning.

## Live state observed (06:08-06:17Z)

| Item | Value | Source |
|---|---|---|
| origin/master | `7b920d84aae97139998f940ee2cce3f457bb6e70` | `git ls-remote` 06:17Z |
| origin/rc/2026-11-01 | `10a826b9d61f2c2cdfa29a43cac59fe360c99ea9` | same |
| Desktop server | `8edda999623a473be2d1970727c172bfb5088d59`, capabilities `land-v2`, `operator-shutdown-v1` | `GET /api/version` |
| 8edda999 lineage | Only on `rc/2026-10-27` (tip 5010e23f0). Not an ancestor of master or of rc/2026-11-01. | `git branch -r --contains` |
| server2 | build 8edda999, accepting, 8 sessions (7 runner sessions), epoch 9 | `GET /api/session-runners/server2/status` |
| server2-temp | Build 8edda999, `retiredAt 2026-10-30T00:45:02Z`, draining, offline. Its volumes were removed and verified at 01:16Z (docs/investigations/2026-11-02-retiring-the-stale-server2-temp-registration.md, commit bedb350de). This is the placeholder the rollout expects; keep it registered. | status route |
| desktop runner | capacity 2, occupied 0 | `GET /api/session-runners` |
| Desktop DB migrations | newest applied `20261028170715_AddAgentSessionCodexTokenCount`. The 5 migrations below are pending. | `__EFMigrationsHistory` (read-only) |
| server2 disk | `/dev/mapper/ubuntu--vg-ubuntu--lv` 705G, 395G used, 282G free (59%); `/work/worktrees` 218G across 185 dirs | `df`, `du` in this runner |
| Installed custody helper (server2 runner image) | `/usr/local/bin/antiphon-custody-kill` 2026-09-23 22:48, sha256 `e173cce8...` = the 8edda999 blob (b15240936). `/usr/local/lib/antiphon-custody-common.sh` is **absent**. | `sha256sum`, `git show` |
| Open board tasks | Merge 0f4dc1cc (batch 25, CARD-1645); Code 1296ccbd/0f317c85 (CARD-1677 S1/S3), 4555c064 (CARD-1676), a382d6ef (CARD-1674); Review fda2f329 (CARD-1673); Mutation 2deb2419; this task; desktop Custom 250a07fb Blocked | `GET /api/agent-tasks?boardId=...` |

## Change census: hosts at 8edda999 vs master 7b920d84a

The range `8edda999..master` has 2,027 commits and changes 969 files. 221 of those commits are
master-only relative to rc/2026-11-01. The rest is rc/2026-11-01 content that the hosts never
ran.

### Database migrations (all additive)

| Migration | On rc/2026-11-01 | Up() operations | Verdict |
|---|---|---|---|
| `20261030114738_AddTaskCeilingSupervision` (CARD-1610) | yes | 13 AddColumn on AgentTasks (one bool with `defaultValue: false`), CreateTable TaskSupervisionNotices, 2 CreateIndex | additive |
| `20261030141651_AddSessionExitSignalEvidence` (CARD-1619) | yes | 4 nullable AddColumn (AgentSessions, RunAttempts, AgentIncidents) | additive |
| `20261030143219_AddTaskSupervisionQueuePurpose` | yes | 1 AddColumn + 1 CreateIndex on SessionQueuedMessages | additive |
| `20261030214946_CloseTaskSupervisionReportWatches` | yes | 5 AddColumn + 3 CreateIndex on TaskSupervisionNotices | additive |
| `20261102070901_Card0824WorkspaceSlots` | **no** (master-only) | 4 AddColumn on AgentTasks (bool `defaultValue: false`) | additive |

None of the five contains DropColumn, DropTable, `Sql(`, UPDATE or DELETE
(`awk '/Up/,/Down/'` grep over each file). An old server can run against the new schema;
that rollback path is reconstructed, not tested.

### Deletes on by default after activation

- **Data retention sweep (CARD-1654 30cb5279b, CARD-1658 b46f1f3f4, CARD-1662/1670 on
  master; not on rc/2026-11-01).** The defaults are the same as at 8edda999:
  `server/Application/Settings/RetentionSettings.cs:12-37` keeps transcripts 7 d, queued
  messages 30 d, sessions 90 d, tasks 180 d, usage 30 d, residue rows 30 d, sweep every 6 h.
  What changes is that the sweep now *completes*, in bounded batches (100 roots x 50 batches
  per sweep, `:43-83`). It also runs **one pass at server start**
  (`server/Infrastructure/Supervision/DataRetentionHostedService.cs:39`). Live counts: 0
  tasks older than 180 days, 2 sessions older than 90 days, and about 14.5k old residue rows
  (CARD-1654/1658 text). The `AuditArchive` pass nulls `FullContent` with no copy, by design
  (CARD-1658 A-3). To defer the first pass, an operator would set `Retention:SweepHours=0`;
  that is a settings change.
- **CARD-1609 volume retirement (on rc/2026-11-01 and master; not in 8edda999).** Production
  deletes are disabled by design. `retire-temp` without
  `ANTIPHON_VOLUME_RETIREMENT_APPLY=enabled-<operation-id>` refuses `apply-gate-off`, exit 2,
  before any stop, remove or journal write (`docs/docker-stack.md` gate 9; CARD-1609 card).
  CARD-1642 says: "Do not enable apply until this card is done."
- **CARD-1677 runner worktree retention is not on master** (Code 1296ccbd/0f317c85 in
  flight). The operator decision on the card makes it Enabled=true, DryRun=false, server2
  only, 12 h. If it lands before a cut, that cut deletes finished server2 worktrees by
  default.

### Settings and default changes (all opt-in or alarm-only)

- `Delegation:WorkspaceSlotsEnabled` (`DelegationSettings.cs:699-702`) and
  `PhoneHome:WorkspaceSlots:Enabled` both default false. Byte-identical mirror path.
- `SessionRunnerSettings`: `PtyHostSocketDirMode=Legacy`, `PtyHostPeerCheck=Off`,
  `LinuxCustodyQualified=false` (CARD-1653 S1/S2, CARD-1614). Flags default off.
- `Alarms:RunnerDrainMaxMinutes=30` (CARD-1666/1671). After that bound, an *offline draining*
  runner raises RunnerUnavailable again. A retired runner stays excluded, so server2-temp
  (retired) does not alarm. During `redeploy-old`, the standing runner is drained and briefly
  offline, so an outage alarm can open if that offline time passes the 180 s grace beyond
  30 min of drain. This is alarm noise, not a stop.
- No `appsettings*.json` diff and no compose diff. The only Dockerfile diff adds
  `antiphon-custody-common.sh` (`docker/session-runner-grok/Dockerfile:247-258`).

### Runner and server contract compatibility

- The one new runner feature string is `workspaceSlotV1`
  (`SessionRunnerContracts.cs:152`). It is advertised only while the runner's slot flag is
  on (`PhoneHomeCommandDispatcher.cs:123-127`), and the server sends slot fields only to a
  runner that advertises it (`RemoteWorkspaceService.cs:217-220`). New contract fields are
  optional with `JsonIgnore(WhenWritingNull)` (Slot/Cold/SlotId/Generation, ExitSignal,
  ExitStopIntent). **A new server with the 8edda999 server2 runner is supported**, which is
  the state during the rollout window.
- Re-adoption across a runner upgrade works. CARD-1636 now requires the helloAck to name the
  manifest's session, and to name its host instance only when the manifest recorded one
  (`SessionRunnerRuntime.cs:3746-3748`). An 8edda999 pty-host's `HelloAckMessage` already
  carries `SessionId`, and old manifests have a null instance id. So detached desktop
  sessions survive `restart-session-runner.ps1`.

### Roster, manifest and seal files for a new cut

Nothing has to be created or edited. The candidate id is derived from the branch and SHA
(`rc-2026-11-03-<sha12>`). `candidate.json` is written by `scripts/release-candidate.ps1`.
The policy (`tests/test-execution-policy.json`, schemaVersion 2 with `policyHash`) and the
land-graph records travel with master. The only date-specific strings in scripts and tests
are test fixtures (`scripts/test-release-selection-hop-seed.ps1:14`), which are not a roster.
Documentation drift (not blocking): `docs/release-gates.md:23` still names `rc/2026-10-18`
as current.

## (A) The cut

| # | Step | Command / gate | Who |
|---|---|---|---|
| A0 | Wait for Merge 0f4dc1cc (batch 25) to publish, or give up waiting after about 30 min. The Merge role cap is 1, so a cut task queues behind it anyway. | `git ls-remote origin refs/heads/master`. Record the full SHA as `$cut`. | orchestrator |
| A1 | Prove the name is free and the rc lines have nothing master lacks. | `git ls-remote origin 'refs/heads/rc/2026-11-03'` must be empty. `git cherry $cut origin/rc/2026-11-01` must show 0 `+`. | cut task |
| A2 | Startup-path probe at the exact candidate. Required: build-slot.ps1 and lib/build-slot.ps1 changed since 8edda999 (CARD-1627 ec45ed58d, CARD-1629), and dev-aspire calls them (`dev-aspire.ps1:114,140`). | On native Windows, in a clean desktop worktree at `$cut` (not the canonical checkout), run the fresh-process block in `release-gates.md:414-430` with label `rc-native-probe`. Keep the receipt. | desktop task |
| A3 | Create the line. A branch push only; no tag. | `git push origin ${cut}:refs/heads/rc/2026-11-03`, then `git ls-remote origin refs/heads/rc/2026-11-03` must equal `$cut`. Never force. The precedent is commit 20c03ab43 (investigation §1): "In a separately commissioned integration task, pin the fetched master SHA, verify the new name is absent, then `git push`". | Merge/Custom task (the operator's message authorizes the cut) |
| A4 | Optional sanity check. | `pwsh -NoProfile -File scripts/build-slot.ps1 -Label rc-cut-graph -- scripts/audit-land-verification-graph.ps1 -Check` on the cut tree. Never `-Update`. | cut task |

What "unqualified" means here: until its Full run is complete-green, the cut earns no
`rc-release` credit and cannot be tagged. That has no effect on activation.

## (B) Desktop AppHost and runner activation

Preconditions (`docs/orchestration-loop.md:15-40`, `docs/apphost-runbook.md`):
`logs/apphost.restart.lock` and `logs/apphost.launch.lock` are absent or older than 15 min;
no land is Running (`landRequest` state on open tasks); no half-reset worktree; the canonical
checkout `C:\src\Antiphon` has no tracked edits (leave untracked user files alone). Record
`GET /api/version` = `8edda999...` before starting.

| # | Step | Command | Gate / stop |
|---|---|---|---|
| B1 | Advance the canonical checkout. It is on a **detached HEAD**, so `git pull --rebase` does not apply. | `git -C C:\src\Antiphon fetch origin`, then `git -C C:\src\Antiphon status --porcelain --untracked-files=no` (must be empty), then `git -C C:\src\Antiphon checkout --detach $cut`, then `git -C C:\src\Antiphon rev-parse HEAD` | HEAD == `$cut` |
| B2 | Repeat the probe, then the restore rehearsal (`release-gates.md:437-447`). | The same fresh-process block, with label `rc-restart-rehearsal` and args `restore Antiphon.AppHost`. `-WhatIf`/`-StopOnly` do not count. | Exit 0. Unleased or timeout counts as unqualified. |
| B3 | Restart AppHost from an independent shell outside any kill-on-close job. Task 8a7dbe6d's `Win32_Process.Create` launch is the proven detach pattern. | `pwsh -NoProfile -File scripts/restart-apphost.ps1 -ExpectedServerSha $cut`. **Record UTC start and end.** | Exit 0 only. Exit 5 = SHA mismatch: lock retained, do not treat as live. Exit 3 = refusal: inspect the locks. Then `GET /api/version` == `$cut` with `land-v2`. |
| B4 | Restart the desktop runner. AppHost does not rebuild runner source (`apphost-runbook.md` "AppHost restart does not pick up session-runner source"). | `pwsh -NoProfile -File scripts/restart-session-runner.ps1` (soft; no `-Hard`, never `-KillSessions`). **Record UTC.** | `RUNNER RESTART RESULT` exit 0, or exit 2 then `-WaitOnly -TimeoutSec 180`. `(Invoke-RestMethod http://localhost:17204/capabilities).build.commitSha` == `$cut`. `scripts/check-daemon-build.ps1` reports clean. |
| B5 | Verify. | `pwsh -File verify-dev-stack.ps1 -SkipBrowser`; `client-mode.ps1 -Status` shows a rebuild; the attention feed shows no new RunnerUnavailable for the desktop. | |

What survives: Postgres (17280), the runner on 17204 and live detached pty-host/herdr sessions
survive B3 (`apphost-runbook.md:43`). B4 stops only the runners at this checkout's Debug
executable path (`scripts/restart-session-runner.ps1:7-9`). Its supervisor rebuilds and
re-adopts detached sessions from `<SessionLogPath>/pty-hosts/manifests` and the herdr
sidecars. The server is down for the graceful stop (up to 20 s), the build and the health wait
(up to 150 s). Phone-home runners reconnect. At startup the 5 migrations apply and the first
retention pass runs.

**Interaction with the live Windows run 7f8e9fd1** (checkout `C:\Antiphon\releases\checkout-8a7dbe6d`):

- No functional coupling was found:
  - No rc/2026-11-01 test starts Aspire or DCP. A `git grep` for
    `DistributedApplicationTestingBuilder|Aspire.Hosting.Testing|dcpctrl|Aspire.Dashboard`
    under `tests/` is empty.
  - The `localhost:17202/17204` hits in tests are literal strings in fake hosts.
    `ProductionRunnerGuard` points every Program boot at a dead runner URL.
  - The Antiphon.Tests DB is its own testcontainer, not the dev Postgres
    (`docs/testing-and-build.md:165`).
  - `restart-apphost.ps1` kills only:
    - the AppHost wrapper tree,
    - owners of ports 17200/17202/17203/17205/17206/17207/17209
      (`restart-apphost.ps1:108,236-249`),
    - processes named `dcpctrl` or `Aspire.Dashboard` (`scripts/apphost-common.ps1:522-523`).
      This is a host-wide name kill. It is harmless today only because no test spawns either
      name.
  - The run's own build-slot lease was granted at launch and is never renewed
    (`scripts/build-slot.ps1:88-140`). `nightly-run`/`nightly-tests` take no further slots
    (no `build-slot` reference in `scripts/lib/nightly-*-impl.ps1`). So the restarted broker
    on 17204 cannot affect it.
- The remaining risk is CPU and disk contention: the AppHost `dotnet build` under a slot, the
  vite client rebuild, and the runner rebuild. Known load-sensitive tests:
  - TimeoutTests 200 ms TotalTimeout;
  - C0824 20 s;
  - qualification-timing child 5000 ms;
  - RunnerAlarmHostedServiceTests 5 s FirstTimer.

  There are no desktop restart timings readable from here. The desktop's own record is
  `Select-String LIVE_TIMING C:\src\Antiphon\logs\apphost.log`
  (`scripts/lib/live-startup-timing.ps1:25`). One server-only isolated build data point is
  75.56 s (B-1 in 2026-10-22-rc-bundle-c-dry-run.md).
- Mitigation: start B3 just after `watch-qual.ps1` reports a completed TRX. Avoid E2E chunks.
  Note both UTC windows (B3, B4). Afterwards, label any 7f8e9fd1 failure whose TRX time
  overlaps a window as window-suspect. It reruns first anyway in the next failed-first run.
- **Cost of a flake: low.** 7f8e9fd1 already cannot publish. A window flake only adds one
  failed-first chunk to the next run's seed. That is why activating now is preferred over
  waiting about 9 h. Do not touch anything under `C:\Antiphon\releases`.

Rollback: `git -C C:\src\Antiphon checkout --detach 8edda999623a473be2d1970727c172bfb5088d59`,
then `restart-apphost.ps1 -ExpectedServerSha 8edda999...`, then `restart-session-runner.ps1`.
The migrations stay applied; they are additive. A failed startup goes through the runbook's
"Failed startup-path pick": preserve logs, commission Debug.

## (C) Server2 rolling rollout at `$cut`

Run every phase from the canonical desktop checkout at `$cut`, after B3. Provisioning requires
a clean canonical `HEAD=<sha>` (`scripts/deploy-server2.ps1:66-78`), and the rollout requires
the server's `/api/version` to equal the rc tip (`docs/docker-stack.md:21-23`). Use one
`-Phase` per call, never `-Phase all`. Keep every receipt under `.antiphon/rolling-server2/`.

Two runner-only constraints:

- **A Deploy role task has a 240-minute ceiling.** The 8edda999 rollout's Deploy task was
  Failed by it in the middle of `drain-temp`
  (`docs/investigations/2026-10-30-rollout-8edda999-state-after-ceiling.md`, commit
  55c93e78f). Run long waits (`drain-old`, `drain-temp`) as separate tasks, or from the
  orchestrator.
- **Canaries must be dispatched by the orchestrator.** A worker's `delegate.ps1` returns 403
  `Workers cannot delegate` (2026-10-16 rollout, `docs/investigations/2026-10-16-rc-rollout.md:123`).

| Gate | Command | Expected / stop | Who |
|---|---|---|---|
| 0a census | `pwsh -NoProfile -File scripts/deploy-server2.ps1 -Rolling -Sha $cut -Phase check-census` | `RECYCLE_PROJECT` plus two `RECYCLE_CENSUS` lines. `cause=Http status=400` means the server predates the filter: B3 not done. | orch |
| 0b host jq | `... -Phase check-host-jq` (provision only on `HostJqMissing`) | jq 1.7.1 receipt | orch |
| 0c caches | `pwsh -NoProfile -File scripts/verify-card0849-caches.ps1 -Case Fixture -Sha $cut`, then `-Case Inventory -Sha $cut` | Stop on refusal. `Reset` is not a recovery. | orch |
| 0d routing | `Invoke-RestMethod http://localhost:17202/api/runner-defaults` | `globalRunnerId=server2` (true on 2026-11-02 per bedb350de) | orch |
| 1 deploy-temp | Record `docker inspect -f "{{.Id}} {{.State.StartedAt}}" antiphon-runner-session-runner-1` before and after (must be identical). Then `... -Phase deploy-temp`. | This builds `session-testing:<sha12>` and `server:<sha12>` from the cut (`scripts/c590-remote.sh:1343-1350`). `TempVolumesRetained` is not expected: the volumes were removed 2026-10-30. A refusal on the retirement-journal read (`Assert-TempRetirementClearable`) is the first use of CARD-1609 tooling against an older retirement: stop and report it. Temp must reach `buildVersion=$cut` and `acceptingNewWork=true`. About 8 min on 2026-10-16. | orch / desktop Deploy |
| 2 smoke temp | `scripts/runner-drain.ps1 status -RunnerId server2-temp`, the ssh `codex/pwsh/dotnet --version` block (`docker-stack.md:190-195`, exec as uid 1654), and an orchestrator Plan canary `-Runner server2-temp` | Canary starts, runs and reports | orch |
| 3 | No promotion phase exists. Record runner-defaults. | | orch |
| 4-5 drain-old | `... -Phase drain-old -WaitIdleMinutes 240` | `server2.draining=true`, `redirectTo=server2-temp`. **From here, dispatch new work to `server2-temp`** (continuous dispatch, `docker-stack.md:16-20`). The 8 current server2 sessions finish in place. At the 240-min cap, the operator's CARD-0934 authorization covers `runner-slots.ps1 release -RunnerId server2 -SessionId <guid> -Reason 'CARD-0934 four-hour drain cap'` per remaining seat. Mutation 2deb2419 is the likeliest long one. | orch (pre-authorized) |
| 6 redeploy-old | `... -Phase redeploy-old` (no `-RecycleVolumes`) | `C1008_RECYCLE skipped=redeploy-old reason=not-requested`. Keeps `work`, `runner-tmp`, `dind-data`. About 78 min on 2026-10-29. | orch / desktop Deploy |
| 7 smoke old | Status, the ssh block, a Plan canary `-Runner server2`, and `verify-card0849-caches.ps1 -Case Both -Sha $cut` | Afterwards C1614_Installed_helpers_advertise_custody should pass in the new container. On failure: `runner-drain.ps1 drain -RunnerId server2 -RedirectTo server2-temp -Reason 'post-upgrade smoke failed'`. | orch |
| 8 drain-temp | Send new work to `server2` immediately, then `... -Phase drain-temp -WaitIdleMinutes 240` | Temp retired, offline, container absent | orch |
| 9 retire-temp | `... -Phase retire-temp -DryRun` within the 900 s preview window | **Preview only.** Apply is human-gated, see O3. | orch (preview) / operator (apply) |

Answers to the specific rollout questions:

- **`BUILD_SLOTS_SHA12` does not need to change.** `broker_sha12` returns the stack.env pin
  (`c590-remote.sh:968-972`), and `ensure_build_slots_broker` writes a pin only when there is
  none (`:998-1000`). So the shared broker keeps its old image through the rollout. The
  wrapper changes since 8edda999 are client-side only (CARD-1627 ec45ed58d, CARD-1629), with
  no new broker route. `GET /build-slots/metrics` keeps answering 405 until an operator moves
  the pin and recreates `build-slots` while no lease is held (`docs/testing-and-build.md:1129`).
  That is optional and not part of this rollout.
- **The custody helper is replaced.** The session-runner image is
  `antiphon-server2/session-testing:${SOURCE_SHA12}` (`docker-compose.server2-runner.yml:42`).
  It is rebuilt from the cut's Dockerfile, whose `session-testing` stage (line 190) COPYs
  `antiphon-custody-kill.sh` and the new `antiphon-custody-common.sh` (lines 247-258). The
  master blob of the kill helper is `68d965da...` (c9142959b, CARD-1614).
- **The server2-temp registration stays.** It is the retired placeholder that `deploy-temp`
  reactivates (`docker-stack.md:134-163`; bedb350de).
- **Disk.** 282G is free. The 10-26 temp used about 33.6 GB over four volumes (CARD-1609
  text), plus two image tags per SHA. The main `work` volume (218G of worktrees) is kept.
  CARD-1677 is not deployed, so finished worktrees keep accumulating; CARD-1677 says the disk
  fills in 2-3 days.
- **Duration.** About 2 h of active phases (deploy-temp, redeploy-old, smokes), plus up to
  240 min each for `drain-old` and `drain-temp`. That is 2 to 10 h total. Throughput is kept
  because temp accepts work throughout.
- **Rollback.** Follow the gate table's stop column: `runner-drain.ps1 clear -RunnerId server2`
  while old is healthy, and a reviewed recovery after `redeploy-old`. Never touch the standing
  container outside the phases.

## (D) Qualifying the new cut

- **Only one Windows run at a time.** `Enter-ReleaseGateNativeLock` uses `CreateNew` on
  `C:\Antiphon\verification\native-run.lock`. On contention it returns `deferred-busy`
  (live owner) or `stale-shared-lock`, with no queue and no steal
  (`scripts/lib/release-gate.ps1:421-445`). A second start exits non-green. Stopping
  7f8e9fd1 is the operator's call (`.claude/skills/antiphon-rc-qualification/SKILL.md`,
  2026-10-31 override). This checklist does not recommend stopping it.
- **Plan (1), recommended.** Cut and activate now. 7f8e9fd1 finishes on its own (it was about
  17 h in at 06:17Z, started 2026-11-02T13:13:38Z, about 15:00Z expected). Then:
  1. `scripts/rc-qualification-status.ps1` with 7f8e9fd1's identities (candidate
     `rc-2026-11-01-10a826b9d61f`, run `7f8e9fd119c3428a8bd46ac88e884645`, state root
     `...\candidates\rc-2026-11-01-10a826b9d61f\state-8a7dbe6d`, coordination root
     `C:\Antiphon\verification`). Require a terminal state and the native lock released.
     If a lock remains after the owner exited, stop and ask (see O8).
  2. Export the seed. `scripts/export-nightly-stopped-run-seed.ps1` accepts a completed run
     directory; no refusal on completion was found (`scripts/lib/nightly-stopped-run-seed.ps1:165-230`).
     ```
     pwsh -NoProfile -File scripts/export-nightly-stopped-run-seed.ps1 `
       -RunDirectory C:\Antiphon\releases\candidates\rc-2026-11-01-10a826b9d61f\logs\2026-11-02-1313-7f8e9fd1 `
       -OutputPath C:\Users\lndco\antiphon-seeds\7f8e9fd1.json `
       -SourceRunId 7f8e9fd119c3428a8bd46ac88e884645 `
       -SourceCandidateId rc-2026-11-01-10a826b9d61f `
       -SourceSha 10a826b9d61f2c2cdfa29a43cac59fe360c99ea9 `
       -TimingRoot C:\Antiphon\releases\candidates\rc-2026-11-01-10a826b9d61f\logs\timing-7f8e9fd119c3428a8bd46ac88e884645
     ```
     Record its sha256. A seed older than 14 days refuses (`nightly-failure-history.ps1:744`).
  3. Cold-reset workspace slots:
     `scripts/workspace-slots.ps1 cold-reset -AllRunners -Reason "rc qualification rc/2026-11-03 <sha12>"`.
     It is a no-op while slots are off.
  4. Make a fresh owned clone and journal it:
     `scripts/release-candidate.ps1 -Ref rc/2026-11-03 -ReleaseRoot C:\Antiphon\releases -CheckoutRoot C:\Antiphon\releases\checkout-<task>`.
     It clones and writes `.antiphon-release-owned`. Follow task 8a7dbe6d's launch set
     (`C:\Antiphon\releases\task-8a7dbe6d`, recorded in 515f6cd1f) for both markers.
  5. Prechecks as in 8a7dbe6d:
     - no native lock and no test process;
     - disk free, pwsh version;
     - seed hash matches;
     - `ANTIPHON_HEADED_TESTS` unset;
     - `scripts/test-release-selection-hop-seed.ps1` green.
  6. Launch detached (`Win32_Process.Create` of a `start-supervisor.ps1`). nightly-run gets:
     ```
     -NoSync -Profile rc -Trigger rc -SelectionMode Full -RunId <new 32-hex>
     -Ref rc/2026-11-03 -ExpectedSha $cut -CandidateId rc-2026-11-03-<sha12>
     -CheckoutRoot <clone> -StateRoot C:\Antiphon\releases\candidates\rc-2026-11-03-<sha12>\state-<task>
     -LogRoot C:\Antiphon\releases\candidates\rc-2026-11-03-<sha12>\logs
     -ReleaseRoot C:\Antiphon\releases -CoordinationRoot C:\Antiphon\verification
     -PriorFailuresPath <seed> -PriorFailuresDigest <sha256>
     -AcceptCrossCandidateSeed -SeedSourceCandidateId rc-2026-11-01-10a826b9d61f
     ```
  7. Check `dispatch-schedule.json`: no `historyFallbackReason`, `historyDigest` equal to the
     seed sha, and P0 holding the failed chunks.

  Never run `release-cut.ps1` without `-SkipPublish`: it publishes on green. During the run,
  push nothing to `rc/2026-11-03`; repairs go to master and queue for the next candidate.
- **Plan (2), wait for CARD-1645/1673/1676/1674/1677 before cutting.** It gains nothing on the
  Windows lane, which is busy until about 15:00Z either way, and it leaves the hosts on
  2026-10-29 code for several more hours. Its one advantage: a later cut that includes those
  batches needs only one rollout, where (1) means a second rollout whenever the line's tip
  next changes. Mitigation for (1): batch rc picks and activate once per batch. The hosts may
  lag the tip by test-only picks, because a restart is needed only "when the rc tip changed",
  not on every land (standing rule 7).
- The 2026-11-01 restart records (515f6cd1f, 41629e7b2, 0c7279252) show 8a7dbe6d's order was
  stop, seed, relaunch. With (1) there is no stop, only natural completion, so no operator
  stop authority is needed.

## (E) Risk recommendation

Activating a master-derived cut that has had no Windows qualification is **acceptable under
`docs/release-gates.md`**, which gates publication, not activation. It matches the project
decision that a failure-forced restart takes a new cut from the latest master carrying all
master-only work. It is acceptable once these hold:

1. A1 is true: no `+` rows from rc/2026-11-01.
2. The A2 and B2 native probe and restore rehearsal pass. This is a startup-path pick.
3. B3 exits 0 with `/api/version` == `$cut`, and B4's runner build SHA == `$cut`.
4. No land is in flight at B1. The rollout's gates 1-9 are followed as written.
5. The restart windows are recorded, so that flakes in 7f8e9fd1 can be attributed.
6. The operator knows the first retention pass runs at startup: about 14.5k old residue
   rows deleted and audit `FullContent` nulled; today 0 tasks are past 180 d.

The main residual risk is the 221 master-only commits since rc/2026-11-01 that no Windows Full
run has covered:

- CARD-0824 S1 (flags off);
- CARD-1653 S1/S2 (flags off; card still in Review);
- CARD-1654/1658 retention;
- CARD-1663/1666/1671 alarms;
- CARD-1636 adoption identity;
- CARD-1627/1629 build-slot;
- CARD-1633 seed lane order.

The new cut's own run covers them after about 15:00Z.

## Who acts where

Operator only:

- **O1.** Approve the cut and activation. The operator's 2026-11-03 message already grants
  it: "cut based on latest master and also update the running apphost and do a server2
  rollout".
- **O2.** Any stop of 7f8e9fd1. Not recommended.
- **O3.** `retire-temp` apply: `ANTIPHON_VOLUME_RETIREMENT_APPLY=enabled-<operation-id>`.
  CARD-1642 says not before its delete-enabled qualification. Leaving the temp volumes after
  this rollout makes the **next** rollout's `deploy-temp` refuse `TempVolumesRetained`
  (CARD-1642: "due before the SECOND rollout"; this is the first rollout with CARD-1609
  tooling). The manual path currently refuses `ManualPreviewApplyIncomplete`
  (`docker-stack.md:157-160`).
- **O4.** Optional: move `BUILD_SLOTS_SHA12` and recreate `build-slots` with no lease held.
- **O5.** Tags, `publish-release.ps1`, `promote-release.ps1`, and `release-cut.ps1` without
  `-SkipPublish`.
- **O6.** Whether CARD-1677 (a feature, deletes by default) goes onto `rc/2026-11-03` before
  its Windows run starts. By the rules it is master-only.
- **O7.** Settings changes, such as `Retention:SweepHours=0` to defer the first prune, or
  budgets.
- **O8.** A `native-run.lock` left after 7f8e9fd1 exits. Conservative reading of the skill's
  "never touch the live qualification lock"; the release-gates step 4 procedure needs an
  exclusive hold.
- **O9.** `-KillSessions`, `-RecycleVolumes` and main volume recycling, Docker prune.

Orchestrator under standing autonomy:

- Commission the cut task (A0-A4).
- Run the desktop probes (A2, B2), the checkout move (B1), `restart-apphost.ps1` (B3) and the
  soft `restart-session-runner.ps1` (B4).
- Run every rolling phase and dispatch the canaries.
- Keep continuous dispatch, including the pre-authorized four-hour-cap seat release.
- Run `retire-temp -DryRun`.
- After natural completion of 7f8e9fd1: export the seed and start the new Full run (D1-D7).

## Remaining uncertainties

- Whether `Assert-TempRetirementClearable` (CARD-1609, first use) accepts the retirement that
  8edda999 tooling performed on 2026-10-30. `check-census`/`deploy-temp` will show it.
- Restart build time and CPU on the loaded desktop were not measured here. Read the
  `LIVE_TIMING` lines on the desktop after B3.
- Whether CARD-1653 S2 has a clean Final Review. The card is in Review and its S2 D-1 fix
  commits are on master.
- The run's end time (about 15:00Z) is the brief's estimate, not observed here.
- The rollback of the server to 8edda999 over the new schema is reasoned (additive
  migrations), not exercised.

## Not done, noted

- Fix idea: `Get-AppHostStrayProcesses` should kill only `dcpctrl`/`Aspire.Dashboard`
  processes in the AppHost tree or this checkout, not every process of that name on the host
  (`scripts/apphost-common.ps1:522-523`).
- Fix idea: a doc-only correction to `docs/release-gates.md:23`, which still names
  `rc/2026-10-18` as the current line.
