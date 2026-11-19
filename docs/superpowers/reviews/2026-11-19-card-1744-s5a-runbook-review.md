# CARD-1744 S5a runbook review (round 1, Final)

Subject: `feat/card-task-d2568cde` tip `cbc8ad5a289c764d700f11f3d7eaafb2a5b1090e` (Grok author),
range `751f3a0bba4000234a6aa88508228e8583838824..cbc8ad5a289c764d700f11f3d7eaafb2a5b1090e` (1 commit,
6 files, docs only). Reviewer: Claude Opus, task 9681fa98. Master checked: `a789c87770d31a77687f75a619c5af646d1ce91f`.

Verdict: **2 blockers** (D-1, D-2). Both are one-sentence doc fixes. Advisories A-1 to A-4 should not hold the land.

## Blockers

### D-1 The `apphost-runbook.md` exit 3 recovery has no "Linux server stopped" condition

- **Where:** `docs/apphost-runbook.md`, exit-code table, row 3 (the added sentence "Recovery then is to
  delete that file, run `scripts/restart-session-runner.ps1 -Hard`, and retry.").
- **Failure:** `phone-home.json` exists only during the rehearsal (RH-3) or the window, so every time this
  refusal fires, the Linux server may be the writer. An operator or an orchestrator (orchestrators may run
  `restart-apphost.ps1` and `restart-session-runner.ps1` under standing autonomy) who runs
  `restart-apphost.ps1` mid-window, gets exit 3, and follows this row will delete the file, run `-Hard`,
  and retry. On master, `restart-apphost.ps1` has no maintenance-marker guard (no `down-on-purpose`
  check), so the retry launches the desktop AppHost. With the user-secret removed at parent step 1.7, the
  AppHost falls back to `Antiphon.AppHost/appsettings.json` `DefaultConnection` (the desktop
  `localhost:17280` database) while the Linux server still serves the public origin. That is two live
  control planes, which the guard exists to prevent. The S4 refusal text (asserted in
  `scripts/test-windows-phone-home-runner.ps1` on `feat/card-task-a4c9312a`, `apphost-guard.present`)
  also says "Delete ... `restart-session-runner.ps1 -Hard` ... retry", so the docs are the only place the
  condition can live.
- **Why:** the amendment's own uses are safe: line 152 comes after the parent rollback, and the RB STOP
  cell comes after `docker compose stop` and the `pg_stat_activity` check. The front-door row drops
  that context. `docs/docker-stack.md` already says "Do not run `scripts/restart-apphost.ps1` while the
  Linux server is a writer", but the exit 3 row does not.
- **Fix:** in that row, make the recovery conditional. For example: "Only during rollback, after the
  Linux project is stopped and `pg_stat_activity` shows no production-compose client (R-2/R-3, RB). While
  the Linux server is the writer, this refusal is the guard working: stop and do not delete the file."
  Add the same condition to the amendment's line 152 sentence so it stands on its own.

### D-2 The phase 2 rollback check uses a log line that a connected phone-home runner never writes

- **Where:** `docs/superpowers/plans/2026-11-18-card-1744-runbook-g1-g3-amendment.md`, "Phase 2 rollback
  in front of R-2 and R-3", step 3: "`C:\src\Antiphon\logs\session-runner.log` has no `Phone-home
  registration` line from the new process."
- **Failure:** the only `Phone-home registration` lines on master are the two failure warnings
  (`src/Antiphon.SessionRunner/PhoneHomeConnectionService.cs:99`, `:105`,
  `Phone-home registration failed: ...`). A runner that is still in phone-home mode and connects
  successfully logs `Phone-home connection epoch={Epoch} connected (capacity {Capacity})` (`:184`).
  The check therefore passes when the rollback did *not* take effect, and that is the case it is meant to
  catch. Step 4 then runs the parent R-1 AppHost restart, and AppHost adopts the 17204 listener
  (`DaemonProcessService.cs:34-37`) while that listener is registered as `desktop-win` with the public
  origin. That is the D-10 runner split brain.
- **Why:** this is the rollback's only positive witness that 17204 left phone-home mode before the AppHost
  start, and it cannot go red.
- **Fix:** check for the absence of `Phone-home connection` (and `Phone-home registration`) lines after
  the `-Hard` relaunch timestamp. Better: also require `GET <origin>/api/session-runners/desktop-win/status`
  to report not available, or require that the new service's environment has no `PhoneHome__Enabled`.

## Advisories (cards, not land holds)

- **A-1 (RH-3/phase 2 live refusal probe):** "`restart-apphost.ps1` exits 3 ... and stops nothing" is a
  live-fire probe. If the S4 guard is missing or misplaced on the activated tip, the probe launches the
  desktop AppHost during RH-3 or the window. Precede it with a static check, such as
  `Select-String 'phone-home-config-present' scripts/restart-apphost.ps1, dev-aspire.ps1` on the activated
  checkout, or run it only as `-WhatIf -StopOnly`, which does not exercise the guard.
- **A-2 (`docs/docker-stack.md` phase 2 paragraph):** the paragraph does not say that phase 2 requires S4
  on the activated tip. The amendment and `apphost-runbook.md` do say it. Without S4 the step is harmless:
  the runner stays direct and the status check fails. Add the gate for parity anyway.
- **A-3 (amendment, Orchestrator bullet 3):** the sentence that lists `restart-apphost.ps1 -ExpectedServerSha`
  under "only inside an operator-run rehearsal or window" can be read as allowing an orchestrator AppHost
  restart during the window. Split it: the AppHost restart is the pre-window phase 1 activation, and the
  phase 2 file plus `-Hard` belong to the rehearsal and the window.
- **A-4 (parent R-3, inherited):** the parent R-3 and the amendment's paraphrase of it have no
  `pg_stat_activity` check between `docker compose stop` and R-1. R-2 and RB both have it. Copy it into R-3.

## Checked and correct

- Every cited line on master `a789c877`: `autostart-session-runner.ps1:20-27,46-55` (no params, always
  `--urls http://localhost:17204`); `run-daemon.ps1:166` `UseShellExecute=$false`, `:174-176` append to
  the log; `session-runner-restart-health.ps1:229-233` (without `-Hard` and with a supervisor recorded,
  only the service stops; `-Hard` stops the supervisor and runs `start-task`), `:122`
  `Start-ScheduledTask 'Antiphon Session Runner'`, `:255-263` exit 0 on a verified 200;
  `restart-session-runner.ps1:23`; `install-autostart.ps1:83,191-194,211`;
  `restart-apphost.ps1:94-97,112-115,125-128`; `dev-aspire.ps1:22-39`;
  `start-windows-phone-home-runner.ps1:29-39,51-56,75-97,109-110` (default origin is the public origin,
  so the phase 1 fallback is right); `build-slot.ps1:27-29`; `SessionRunner/Program.cs:19-40,82-100`;
  `AppHost/Program.cs:33-39`; `DaemonProcessService.cs:34-37`; compose `:70-84` (`MaxCapacity` 2);
  `deploy-server2-server.ps1:30,344-348`; `AgentTaskScope.cs:61-71`;
  `AgentTaskEndpoints.cs:643-672`; `AgentTaskDtos.cs:350-351`; `launch-rc.ps1:60-65`;
  `logs.ps1` session-runner source; `GET /build-slots` exists (`BuildSlotRoutes.cs:68`).
- What S4 adds, and how it is marked: `scripts/lib/windows-phone-home-env.ps1` is absent on master, and no
  script reads `phone-home.json`. Each step that depends on S4 is marked adopted/not-on-this-master and is
  gated "only when S4 is on the activated tip". Those steps are the phase 1 `-PhoneHomeConfigPath` start,
  the `phone-home.phase1.json` reader, all of phase 2 (the file, `-Hard` changing mode, and capacity
  1 to 2), the `phone-home-config-present` refusal and its recovery, the phase 1 autostart and phase 2
  Serilog log rows, RH-3, and the RB STOP recovery. Phase 1 has a runnable master fallback. The
  documented JSON schema matches the S4 red witnesses on `feat/card-task-a4c9312a` `172a49f43`: the default
  `phone-home.json` refuses `port` 17214 (`port-not-broker`), and an explicit `-PhoneHomeConfigPath`
  accepts it.
- Operator-only items: `-KillSessions`, every host `-Execute` and `docker compose stop`, the
  maintenance marker and Scheduled Task enable/disable, the vhost switch, `pg_stat_activity`, and O-12.
  The `RecycleUnpublishedWork`, `RecycleWorktreeDirty` and `RecycleGitAuditUnknown
  reason=registry-missing` stops appear verbatim. P-2 (24 h after the monitor exits 0) is listed and
  marked UNOBSERVED.
- Rollback order: phase 2 rollback, then the Linux stop, then `pg_stat_activity`, then R-1. Phase 1 is
  stopped before the phase 2 file is written, so one `desktop-win` exists at a time. Apart from D-1
  and D-2, no step order puts two servers on one runner or one database.
- Secrets: the diff has no password, token value, connection string, or IP address other than
  `127.0.0.1`.

## Evidence

- CP-14 (checkpoint tool, plan `docs/superpowers/plans/2026-11-19-card-1744-s5a-checkpoints.md`): run
  `20261119-052901-3efa`, `executed=20 passed=20 failed=0 skipped=0`, slot granted, source
  `cbc8ad5a2` clean, buildSource verified, `CHECKPOINT SOURCE VALID rows=1`.
- CP-9/CP-14 count: `DockerStackDocumentationTests` has 20 `[Test]` and 0 `Arguments` at `a789c877` and at
  the tip, and 19 at `997f9b8d9`. Min 20 is correct.
- Doc guard extract (review-only row RV-1, `.antiphon/review-9681fa98-guards.md`; it is not a plan
  row, run because the brief names the doc guards). It covers the Server2ProductionServerContract,
  StandingPipelinePolicyDocumentation, Server2StagingServerContract, Server2SidecarStandingAgents,
  RunnerPushCredentialDocs, LinuxServerRestartGate and DeployServer2ApiDefault classes. Run
  `20261119-054602-ff19`: `executed=63 passed=63 failed=0 skipped=0`, slot granted, buildSource
  verified, `CHECKPOINT SOURCE VALID rows=1`. An earlier attempt, `20261119-053752-919e`, was refused
  `tampered`: the reviewer wrote this file into the worktree mid-run. No build or test ran in that attempt.
- `check-evidence-diff.ps1`: commits=1, violations=0. `git merge-tree --write-tree origin/master
  (a789c877) cbc8ad5a2`: clean (tree `7c6e76c64`).
- No code or test file changed. The guard batch (TestClassificationPolicy* and the others) is
  not required.
