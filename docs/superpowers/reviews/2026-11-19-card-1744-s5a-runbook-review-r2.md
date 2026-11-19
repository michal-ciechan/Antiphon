# CARD-1744 S5a runbook review (round 2, Final, blockers only)

Subject: `feat/card-task-87851070` tip `a4ed511cd03007f0c2bba3dba4e11ea6f3a12ec3` (Grok author).
Range: `cbc8ad5a289c764d700f11f3d7eaafb2a5b1090e..a4ed511cd03007f0c2bba3dba4e11ea6f3a12ec3` (1 commit, 4 files, docs only).
Round 1: task 9681fa98, `docs/superpowers/reviews/2026-11-19-card-1744-s5a-runbook-review.md` at `ee4bfa912`.
Reviewer: Claude Opus, task 23dc51ef. Master checked: `87a70111e027c7f7cdd1e51ad34be088b4806b6c`.

Verdict: **CLEAN**. D-1 and D-2 are fixed. No new blocker. Advisories A-5 to A-7 go to cards and do not hold the land.

## D-1 (exit 3 recovery gated on the Linux stop): fixed

- `docs/apphost-runbook.md:170`, row 3: the recovery "delete that file, run `scripts/restart-session-runner.ps1 -Hard`, and retry" now "applies only after the Linux project is stopped (`docker compose -p antiphon-server2-server stop`) and `pg_stat_activity` shows no production-compose client". It adds: "While the Linux server is the writer, including mid-window, this refusal means stop: leave the file in place and do not retry." The action column says the same.
- Amendment line 153: the same condition, and the same stop sentence.
- `docs/docker-stack.md:2241`, phase 2 rollback: "The AppHost restart is parent R-1, after `docker compose -p antiphon-server2-server stop` and after `pg_stat_activity` shows no production-compose client." It no longer says "only then restart the AppHost" right after the `-Hard` step.
- Project name and command: `docker-compose.server2-server.yml:14` is `name: antiphon-server2-server`. `scripts/deploy-server2-server.ps1:29` is `$script:ServerProject = 'antiphon-server2-server'`, and `:226` runs `compose ... -p $script:ServerProject up -d --build`. `scripts/restart-linux-server.ps1` names no project; it takes a container from `-Config` and never stops it. The stop command matches the deployed project. It is the same command that parent R-2 and rehearsal RB already use.
- No other unconditional delete-and-retry. In `docs/`, `phone-home-config-present` appears at `apphost-runbook.md:170`, `docker-stack.md:2241` and `:2288`, and amendment lines 62, 134, 153, 186 and 188. Of those, only RB (`:188`) has a delete-and-retry STOP cell, and it comes after RB's own `docker compose stop` and `pg_stat_activity` steps. Round 1 already accepted that cell. The G1-G3 plan's D-10 lines (`2026-11-18-card-1744-blockers-g1-g3-plan.md:198`, `:389`) put the delete before the R-2 AppHost restart, and R-2 stops the Linux project first. The amendment supersedes those lines. `apphost-runbook.md:114-128` (`-Hard`) has no retry text.
- The S4 code refusal text ("Delete ... `-Hard` ... retry", on S4's branch) is being fixed by task ec244b9b. It does not block this land.

## D-2 (phase 2 rollback witness can go red): fixed

Amendment step 3 now requires two things after the `-Hard` relaunch timestamp. First, `session-runner.log` has no `Phone-home connection` line and no `Phone-home registration` line. Second, `GET /api/session-runners/desktop-win/status` on the Linux origin returns `"available": false`. Step 4 waits on both.

Checked on master `87a70111e`. `src/server` is unchanged in this range, so the citations hold at the tip too.

- `PhoneHomeConnectionService.cs:99` and `:105` are `"Phone-home registration failed: reason={Reason} attempt={Attempt} backoffMs={Backoff}"`.
- `:184` is `"Phone-home connection epoch={Epoch} connected (capacity {Capacity})"`.
- `:262` is `"Phone-home connection ended: epoch={Epoch} loop={Loop} fault={Fault} overflow={Overflow} pending={PendingEvents} pendingBytes={PendingBytes} inFlight={InFlight} lifetimeMs={LifetimeMs} close={Close}"`. `:270` is the same text plus ` wsError={WsError} socketError={SocketError}`.

All quotes are exact. No other `Phone-home registration` text exists in `src/Antiphon.SessionRunner`.

- `PhoneHomeRunnerDirectory.cs:739` is `var available = live is not null && !leaseExpired && live.SocketOpen;`, which matches "true only while a live socket is open and the lease is unexpired". `:761` is `var eligible = live is { DispatchEligible: true } && available;`. `:862-868` is the descriptor with `eligible, eligible, !eligible`. `SessionRunnerCatalogue.cs:137-147` copies it.
- The route `GET /api/session-runners/{runnerId}/status` exists (`server/Api/Endpoints/SessionRunnerEndpoints.cs:64`). An unknown runner throws `NotFoundException`, and that fails the "available false" check, which is safe.
- The check can fail. A relaunched process still in phone-home mode either connects or keeps retrying. If it connects, it writes `:184` and the status reads `available` true. If it keeps retrying, it writes `:99`/`:105`. Phase 2 rollback runs before the Linux stop, so the server is up to accept the connection. If `-Hard` did not relaunch at all, the old socket stays open and the status reads true. Each of those cases turns step 3 red.

## Folded advisories A-2, A-3, A-4: accurate

- A-2: `docker-stack.md:2286` adds "and only when S4 is on the activated tip". It is one clause, and it is accurate.
- A-3: the amendment's Orchestrator bullet is split. The AppHost restart is now the pre-window phase 1 activation, not allowed mid-window or while Linux is the writer. The phase 2 file and `-Hard` belong to the rehearsal and the window. The content is accurate, but the fold is four sentences across two bullets, not one sentence. That is not a defect.
- A-4: `pg_stat_activity` is now between the Linux stop and R-1 in three places: `docker-stack.md` R-3, parent plan R-3 (`2026-11-08-card-1744-cutover-plan.md:208`) and the amendment's R-3 paraphrase. It matches R-2 and RB.

## Advisories (cards, not land holds)

- **A-5 (citation off by one):** the amendment, step 3, cites `PhoneHomeRunnerStatusDto.Available` at `PhoneHomeContracts.cs:704` and `DispatchEligible` at `:705`. On master `87a70111e` and on `a789c877`, `Available` is at `:703` and `DispatchEligible` at `:704`. `:705` is `LastHeartbeatUtc`. The field names are right.
- **A-6 (witness timing):** step 3 can be read in the gap after `-Hard`. At that point the old socket is closed and the status is false, but a relaunched process still in phone-home mode may not have connected or logged yet. Add a settle step: re-read the log and the status at least one registration backoff (or 60 s) after `/health` turns 200.
- **A-7 (inherited, not in this diff):** parent R-3 (`cutover-plan.md:208`) still lists "stop `desktop-win` if started" after `docker compose stop`. The amendment requires the phase 2 rollback before R-3. The amendment's mapping sentence resolves this, but the parent row order still reads the other way.

## Evidence

- CP-14 (checkpoint tool, plan `docs/superpowers/plans/2026-11-19-card-1744-s5a-checkpoints.md`), run `20261119-063320-edfc`:
  `CHECKPOINT CP-14 commit=a4ed511cd03007f0c2bba3dba4e11ea6f3a12ec3 build=ok filter=/*/*/DockerStackDocumentationTests/* executed=20 passed=20 failed=0 skipped=0 slot=granted waited=0s dirty=0 source=a4ed511cd03007f0c2bba3dba4e11ea6f3a12ec3 sourceState=clean buildSource=verified`.
  The run reported `unlisted: none` and `verdict: GREEN exit=0`. `validate --expected-source-sha a4ed511cd...`: `CHECKPOINT SOURCE VALID source=a4ed511cd03007f0c2bba3dba4e11ea6f3a12ec3 rows=1`.
- `check-evidence-diff.ps1`: `cbc8ad5a2..a4ed511cd` gave commits=1 violations=0. The whole branch `751f3a0bb..a4ed511cd` gave commits=2 violations=0.
- `git merge-tree --write-tree 87a70111e a4ed511cd`: clean, tree `24732962a54b31c54bba55a0fe61dd99f125b568`.
- No code or test file changed (`751f3a0bb..a4ed511cd` touches only `docs/`), so the touched-class rerun and the guard batch do not apply.
