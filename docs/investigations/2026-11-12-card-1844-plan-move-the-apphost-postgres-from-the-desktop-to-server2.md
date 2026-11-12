# CARD-1844: Postgres to server2 — state and next tasks

2026-11-12. Investigate task `f8a51fd1-b442-4a32-9822-23471c3b76fb`.
Evidence checkout and freshly fetched master: `cc48c70050a2932eddf4abe18cea8659d478c33e`.
Board `8988ca03-7414-47ad-b0b6-51556c701703`, project `d4ea7ae9-e769-474b-95b9-aa25fbc1303f`.
API observations: 23:46–23:50 UTC; status snapshot: 23:48:23 UTC.

## Outcome and scope

Confirmed from source, stored task rows and a local reconstruction: CARD-1844's plan and S1/S2 are pushed but unlanded; S1/S2 await their first Review, and S3 has not started in the enumerated evidence. The host Postgres service, cutover script and monitor already have landed implementations. No retrieved record establishes that the production database has moved. There is also a confirmed verification defect in the landed cutover script: failed sequence reads can be reported as matching. The operator window needs that finding disposition before execution.

This investigation changed only this report. It made authenticated read-only HTTP requests, inspected Git and public PostgreSQL documentation, and ran a local stub reconstruction. It did not access any database, Docker daemon, volume, live process, user secret or provider home. No deployment, cutover, backup execution, restart, settings change or dispatch occurred. The source investigation is complete; current host state, memory and restore performance remain explicitly unverified below.

## Slice and branch ledger

The requested `origin/feat/card-task-011a6349a` confuses a commit with a task branch. The plan is commit `011a6349a7b0f0d730387c2a523e23a924b18277` on `feat/card-task-17db3443`. There is no matching remote branch named for `011a6349a`.

| Slice | Exact source / task | Landed | Review | In flight / remaining |
|---|---|---|---|---|
| Plan | `feat/card-task-17db3443` at `011a6349a7b0f0d730387c2a523e23a924b18277`; task `17db3443-7d58-4fdb-8e24-d85cf78ffda1` Succeeded | No | No separate Review row found | Plan exists; its backup statement is stale, as detailed below |
| S1: AppHost owner wording and endpoint log | `feat/card-task-aec861c3` at `db30828432aa72db6b91c7ee01db1fc062603954`; task `aec861c3-51ed-4c90-816f-a95792b86b94` Succeeded | No | First ordinary Review not started; report says `next: review` | Code complete. Red `a997505dc8ab8de604e294aeddffe02dc0cd6d09`; first fix `50088a606811913ec3927eebd785dd5c6b27fee4` failed compilation; repaired and tested at the tip |
| S2: effective-endpoint bootstrap check | `feat/card-task-8bbde9af` at `0d983100e943006bccf43ecadf0ba77280f479ac`; task `8bbde9af-716d-4bfb-84d0-59b358bca67b` Succeeded | No | First ordinary Review not started; report says `next: review` | Code complete. Red `a4ce8a70f9f28ab8db0decbe0f787aae04b41bcc`, expanded red `3b44058252fafbbe7279afb5c329242f66a65f3d`, tested fix `7a0696dbd8320b25054107377d909daf2812e83d`; tip adds report only |
| S3: documentation and contract witness | No task, commit or branch identified | No | Not started | Plan lines 227–231 require docker-stack/bootstrap wording and `scripts/test-card1844-postgres-contract.ps1`, CP-3 / V-4 / R-3 |

Evidence: `GET /api/cards/CARD-1844/thread?boardId=8988ca03-7414-47ad-b0b6-51556c701703`; exact task detail GETs for S1/S2; complete board-scoped task-list response (8,162 rows), filtered by card GUID, role and subject identifiers. Exactly four rows bind to this card: Plan, S1 Code, S2 Code and this Investigate task. There are zero bound Review rows; neither Code detail has a land request or landing. CARD-1844 itself is InProgress, not Review. Keyword-matched thread entries about rc merge batches are not S1/S2 approvals.

Git corroboration: `git log --all --grep=1844`, path history for both plan copies and the S3 witness, branch containment, fresh `git ls-remote`, and `git cherry origin/master origin/feat/card-task-8bbde9af`. All eight plan/S1/S2/report commits are `+`, not patch-equivalent to master. S2 contains S1 and the plan. The branches containing the plan are precisely the three task branches above; no other substantive CARD-1844 implementation branch was identified. Master still has the old AppHost wording at `Antiphon.AppHost/Program.cs:19` and no new endpoint helper.

Stored verification, not rerun here:

| Slice | Reported results | Remaining limits |
|---|---|---|
| S1 | Witness 18/18; source-reading classes 31/31; guards 77/77; 11 mutant checks red; AppHost compilation green after repair | Source witness, no AppHost launch; Windows PowerShell 5.1 parse owed; `PtyHostDeploymentGuardTests` not run; tuple-deconstruction leak-check blind spot recorded |
| S2 | Witness 62/62; `CardFileBootstrapCheckTests` 3/3; guards 77/77; four mutant checks red | Windows PowerShell 5.1 parse and post-cutover full-mode check owed; four early authoring runs were unleased, disclosed by author |

Essential unedited checkpoint provenance from the stored Code reports:

```text
CHECKPOINT CP-1 commit=db30828432aa72db6b91c7ee01db1fc062603954 build=n/a filter=pwsh -NoProfile -File scripts/test-apphost-postgres-owner.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=db30828432aa72db6b91c7ee01db1fc062603954 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-2 commit=7a0696dbd8320b25054107377d909daf2812e83d build=n/a filter=pwsh -NoProfile -File scripts/test-bootstrap-database-endpoint.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=15s dirty=0 source=7a0696dbd8320b25054107377d909daf2812e83d sourceState=clean buildSource=notApplicable
CHECKPOINT-TIMING CP-2 waited=15s executed=11.695s wall=26.925s
```

CP-1 run `20261111-210023-1bad`; CP-2 run `20261111-212444-55a9`. S2's full committed report is `.antiphon/task-8bbde9af.md:1` at `0d983100e943006bccf43ecadf0ba77280f479ac`. S1's authoritative report is its task `result`. These historical Mutation obligations are recorded, but the brief's 2026-11-12 operator waiver means this handoff does not commission Mutation.

## Existing dependencies and Review notes

| Component | Evidence and actual state |
|---|---|
| Host service / volume, CARD-1406 | Board Review, but code already landed. Final service/drill repair is on master at `2e43ac0f940b4d61ba8e91da130b536275d20b52`; initial service `0853f0266ba1ce65522ecdc4ff22f91eab5584cd`, scratch isolation `2f188806cea4a45f2f7cc09dc782ac8d1f58820c`. Merge task `ac04d0ad-73c5-4cd1-9be9-f40516b2a0d4` records their publication in batch 112. That is code publication, not a running cluster or completed drill. |
| CARD-1406 Review | R1 `37d76938` found production-volume reuse by the supposed drill and a vacuous network check. R2 `18fbae37` found missing encrypted-copy workflow and broken identifier quoting. R3 `8195f9ce-f0cf-4f77-9962-18c31c8088d1` found those blockers resolved at source `800c968adbc23c72e1755495e22f78962b6f8d1f`: script 279/279, guards 220/220. Its evidence `f061855a-fe00-44bb-9983-33cbf4074818` is Clean, `reviewedSourceClean=false`, scope Interim, round 3; it is not a Full clean-receipt approval. Historical publication nevertheless already occurred. |
| CARD-1406 obligations | Actual host drill, timings and counts remain unread. R3 advisories A-19–A-24 include undefined snapshot directory / mixed desktop shell, missing restore timing, estimated `n_live_tup` rather than exact counts, omitted sequence/key queries, reused directory permissions and probe timeout. R1/R2 advisories remain historical debt. See `docs/investigations/2026-11-07-review-card-1406-r3.md:84` at `457b9db2d1c758095296e5e520e35db7f5b23d13`. Do not interpret a Review-column card as unlanded, or an Interim verdict as permission for a fresh land. |
| Snapshot, CARD-1405 / CARD-1831 | CARD-1405 Done; CARD-1831 Review. Snapshot hardening is already on master, including `a0e9f91a52ec52e410d7abdc044348ddbf833a88`. Windows witness repair landed as `f0155042e3e65f7e016ec685c922b3111c43364a`. The stored desktop observation below supersedes the CARD-1844 plan's assertion that no daily job runs. |
| Cutover, CARD-1407 / CARD-1417 | CARD-1407 Done; script and StopOnly already landed. Initial script `af4a941450f0dba60cc2555aeed9719bd8fd3de5`; latest change to that script `5717dc36ab69c9ff442cccf130aeeb48c08d27aa` is on master. A Done card is not an executed migration window. New findings below concern this script. |
| Monitor, CARD-1408 / CARD-1423 | CARD-1408 Done; landed final original repair `2659d1fc0cc07f45c05a41c92f893650a5aa391c`; later hardening through `462a12f982c100bd1892903bd46f9a49751665ee`. Baseline / post-cutover measurements remain operational evidence to obtain. |

Backup correction: desktop task `b28fd575-7187-4cbb-8f35-dac2022a51a2`, stored `result`, reports both Windmill schedules enabled (`07:50` backup and `08:00` stale check, Europe/London, desktop tag), three verified cloud dumps totalling 1,594,125,176 bytes, and the refreshed script's matching SHA-256 pin. Its Windows snapshot suite was **376 passed / 10 failed** due to the fake-copy drive-letter seam; job status was 70/70. Repair task `002811c5-4b97-4fc8-9445-8a3f0ac7b3eb` subsequently reported Linux snapshot 764/764 and job-status 77/77 at `b206e65bfbff8a40b2a04ad37a57cd028ea2016c`; the rebased landed fix is `f0155042...`. No fresh Windows rerun or later daily job receipt was retrieved here. Enabled schedules and historic dumps establish more than “no backups,” but do not establish today's recovery point or a server2 backup.

The current scheduled wrapper launches Windows PowerShell with `-Plain -Execute -OneDriveCopy`, without a different source (`scripts/windmill/invoke-postgres-snapshot.ps1:57`, `:67`). The snapshot defaults remain `Container=antiphon-postgres` and `SourceVolume=antiphon_pgdata` (`scripts/postgres-snapshot.ps1:54`, `:69`); the daemon guard defaults to local (`:732`). Changing AppHost's connection string does not retarget this job. CARD-1831 owns that outstanding transition; no server2 backup dispatch is evidenced.

## Mechanism and newly reconstructed cutover findings

The production dependency is straightforward: standalone desktop Compose owns Postgres; AppHost only creates a connection-string resource and passes it to the server (`Antiphon.AppHost/Program.cs:19`, `:66`); the server uses that string in Npgsql (`server/Program.cs:124`). An engine outage therefore removes the database independently of AppHost's lifecycle. The stored 2026-11-10 outage evidence names qualification groups 115–118, but these use their own Testcontainers, not `antiphon_pgdata` (`docs/investigations/2026-11-10-why-missed-e2e-split-7-land-delivery.md:19`). Moving production Postgres does not move those tests.

**F-1: phase 3 accepts two failed sequence reads. Confirmed by source reconstruction.**

1. `scripts/cutover-postgres-server2.ps1:586` selects `is_called` from `pg_sequences`. PostgreSQL 16's documented view columns contain no such column. Primary reference: [PostgreSQL 16 pg_sequences](https://www.postgresql.org/docs/16/view-pg-sequences.html), table 54.23. Thus that statement has an undefined-column error on the specified database version. No live SQL was run here.
2. `Invoke-CutoverPsql` maps a nonzero SQL exit to `$null` (`:544–555`). `Assert-CutoverEqual` binds both inputs as `[string]` (`:563`); that converts these nulls to empty strings before its null test. Two empty strings compare equal and it prints a match (`:564–570`).
3. A local PowerShell process extracted the unchanged `Assert-CutoverEqual`, `Invoke-CutoverVerify` and rollback predicate from master's AST. A stub returned `$null` for both sequence queries and successful fixture responses otherwise. There was no docker, SSH, network or database call. The real verification function returned successfully:

```text
fixture target=source PostgreSQL16-undefined-column=is_called returning-null
fixture target=target PostgreSQL16-undefined-column=is_called returning-null
PHASE 3 match: sequences
RESULT verification returned despite both sequence-query errors
```

Command: `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1844-evidence-reconstruction -- pwsh -NoProfile -File .antiphon/evidence/card1844/reconstruct-cutover.ps1`.
Lease `41132c48-5876-4eab-afed-0685d522a17d`, granted, waited 0 s, held 1 s, exit 0. The gate's generic non-dotnet maxcpucount warning is expected; this child only interpreted PowerShell. The exit demonstrates the defect, not a passing cutover. Script and generated API captures remain ignored evidence. Essential output is preserved above.

The SQL was introduced in `af4a941450f0dba60cc2555aeed9719bd8fd3de5`. The existing test shim returns a canned successful sequence row whenever SQL contains `pg_sequences` (`scripts/test-cutover-postgres-server2.ps1:211`), so that witness does not validate PostgreSQL's columns. The null/string boundary can also affect other paired query errors; no broader occurrence is claimed.

**F-2: rollback's “extra rows” check does not prove no data loss. Confirmed scope limitation.**

`Get-CutoverCountMap` reads only counts (`scripts/cutover-postgres-server2.ps1:603`); `Test-CutoverNewHasExtraRows` checks only whether a target count exceeds the old count (`:621–630`). A same-count UPDATE, or delete-plus-insert, is invisible. In the same isolated reconstruction, identical count maps returned `extra-rows=False`; row values are not inputs to this function. `Invoke-CutoverRollback:716` requests `-AcceptDataLossRollback` only when that predicate is true. Consequently its automatic gate is weaker than “the new database has rows the old database lacks.” There is no evidence that a real rollback has lost data. Any accepted writes after the switch require an explicit reconciliation decision; the retained old volume alone is not a zero-loss rollback.

The caller should track F-1 and F-2 before commissioning an operator window. This read-only assignment did not file cards, change source or design repairs.

## What remains before server2 can be the production database

This is the ordering already specified by CARD-1401/CARD-1844, with missing evidence identified, not a new cutover design. CARD-1844 source references in this section mean `docs/superpowers/plans/2026-11-09-card-1844-postgres-to-server2.md` at `011a6349a...`.

| Order | Existing owner / requirement | Missing evidence or action |
|---|---|---|
| 1 | Plan refresh; S1/S2 Review and land; S3 Code and Review | Neither S1/S2 approval nor S3 implementation exists. Reconcile the stale backup claim, CARD-1406's landed-but-Review status, and F-1/F-2 before claiming the window ready. Windows 5.1 parsing remains owed; no live process is needed for that parse. |
| 2 | CARD-1406 host preflight | Host-owned Docker data-root, free space, production/scratch inventory, image digest and tailnet reachability have not been measured here. Plan gate is at least 20 GiB free on the actual data-root (plan `:142`). Runner `df` is not that measurement. Resolve the existing Review/Done operator gate in plan `:250`; do not re-land already published code. |
| 3 | Operator credentials and host service | Provision the existing mode-0600 env-file contract and pinned PostgreSQL 16 image; run the existing host-owned preview/execution procedure. This is not authorized by this investigation. Defined production volume is `antiphon-server2-postgres_pgdata`, separate from desktop, scratch, runner and Windmill (`docker-compose.server2-postgres.yml:7`, `:21`, `:28`, `:44`). Actual existence/health unknown. AppHost must not acquire container lifecycle ownership. |
| 4 | CARD-1405/CARD-1831 backup and CARD-1406 scratch drill | Obtain a fresh verified recovery artifact, establish the existing encrypted-drill prerequisites, perform the isolated host restore and collect exact table counts, sequences, extensions, migration history and key-table checks. A current plain daily dump is not itself the encrypted drill input. Record dump/copy/restore durations and source size. The existing operator drill has never been evidenced in retrieved records (`docs/docker-stack.md:1691`, `:1699`, `:1718`). |
| 5 | CARD-1407 confirmed window | Book measured downtime only after the prior gates, including cutover-verifier disposition. Existing script requires `-Execute -ConfirmWindow` and its machine/repository/date confirmation token (`scripts/cutover-postgres-server2.ps1:137`, `:930`), snapshot reference, passed drill record and latency at most 20 ms (`:188`). Quiesce, empty pending lands, stop AppHost, final dump/restore and phase-3 verification precede repointing (`:826`, `:858`). |
| 6 | Production connection string / AppHost | Stop desktop Postgres while preserving `antiphon_pgdata`; set only AppHost user-secret `ConnectionStrings:DefaultConnection` for `aspire-antiphon-apphost`, then restart the approved rc SHA. Existing `WithReference` already carries the override to the child server; no tracked production password/address edit or schema migration is required (plan `:130–150`). Restore includes `__EFMigrationsHistory`; compare it to the actually activated rc, not an obsolete “latest migration” in the old plan. |
| 7 | CARD-1408 post-checks and CARD-1831 backup transition | Health/version, actual target client, old container stopped, one approved pipeline sample and 30-minute monitor; confirm the active backup reads the new database and off-host copy/stale alert still work. No automatic backup retargeting exists in the current wrapper. The read-only monitor does not itself perform rollback. |
| 8 | Operator rollback custody | Preserve both production volumes. Existing rollback starts the old container, removes the override and restarts AppHost (`scripts/cutover-postgres-server2.ps1:704`). Resolve writes since cutover before choosing old state; F-2 means count equality is insufficient evidence. |

CARD-1744 is the later control-plane move; it is not required to make the desktop AppHost use the server2 database. Staging remains isolated until its own gate. CARD-1008's runner recycle ownership does not expand to either production database (`docs/docker-stack.md:1079`, `:1095`, `:1169`).

## Risks, memory question and cheapest safe first step

| Risk | Evidence / limit |
|---|---|
| Data loss or split brain | Source and target are independent copies. The plan requires one writer and retains the desktop volume (plan `:237`). A missing override or later standalone desktop launch can point back at old data. F-1 weakens verification; F-2 weakens rollback detection. No observed data loss is asserted. |
| Downtime | The plan's 30-minute envelope is conditional on source at most 2 GiB and dump/copy/restore drill at most 10 minutes (`:144`). The 2026-11-10 size, 2,099,575,831 bytes, leaves only 47,907,817 bytes (45.7 MiB) below 2 GiB. No current size or measured rehearsal was obtained. The script's timing gates (`scripts/cutover-postgres-server2.ps1:204`) are not a guarantee of whole-window duration. |
| Backup gap | Historic dumps and enabled desktop schedules exist; a fresh successful scheduled run, server2 source and verified off-host restore are not established. A stopped desktop source makes the current default job unsuitable. |
| Capacity / failure domain | Host compose caps Postgres at 4 GiB and four CPUs (`docker-compose.server2-postgres.yml:21`). Actual host-daemon disk and pressure remain unmeasured. The historic runner-visible LV was 91% full with 66G available; that does not establish host Docker headroom (plan `:88`). Database and runner now share the server2 host's failure domain. |
| Network | Historic Tailscale median 6.72 ms is not a current measurement; each command now crosses that path. Plan abort is a fresh desktop-origin median above 20 ms (`:146`). Tailnet bind and fresh credentials are required. The plan adds no database TLS; its stated boundary is Tailscale (`:128`). |
| “5.9 GB page cache” | The brief supplies this question but no timestamped measurement, host, units or accounting fields. It is not established as PostgreSQL-owned, reclaimable or recoverable Windows RAM. Database size, Postgres RSS, cgroup cache, Linux `Cached` and Windows VM working set are different readings. No 5.9 GB saving can be claimed. |
| WSL cap | No effective desktop `.wslconfig` limit, swap setting or reclaim policy was retrieved. A configured ceiling is not current usage. Stored CARD-1461 evidence explains that guest cache is inside `vmmem`'s working set and host recovery depends on pages returning to Windows; its reported 0.5 GB recovery was not a controlled measurement (`docs/investigations/2026-10-22-card-1461-land-verify-memory-floor-starvation.md:50`, `:204`). No cache drop, WSL restart, cap change or Docker restart is justified by this investigation. |
| Qualification remains on desktop Docker | Testcontainers are independent of the production volume; moving this database does not move them or make the next rc incremental. CARD-1836/CARD-1820 remain the separate priority work. Live Windows run `0b292fa4` was not inspected or touched. |

Cheapest safe first step: a bounded read-only census from the owning hosts, alongside the already-ready source Reviews. Ask the desktop observer to record Windows available RAM and `vmmem` working set, effective WSL memory/swap/reclaim settings, guest memory fields, and already-available timestamped Postgres/cache accounting. Ask the server2 host observer for the actual Docker data-root and backing free space. Attribute the 5.9 figure before discussing savings. This neither requires a dump nor changes a daemon, cap or workload. The next operational step after those readings is the existing restore rehearsal, subject to its operator authorization; it is not part of this task.

## First three ready dispatch briefs

These follow the existing plan. They authorize no host operation. Current pipeline snapshot had Review 0/5, Code 3/5, project parallel 7/12, server2 7/10; these are observations, not reservations. The caller must refresh effective limits and overlap before dispatch. No work was spawned here. The overall next stage remains Plan to reconcile the confirmed cutover findings and operational prerequisites.

1. **Role Review; kind Codex; tier Frontier; server2; S1 ordinary round 1.** Review owner `aec861c3-51ed-4c90-816f-a95792b86b94` at exact source `db30828432aa72db6b91c7ee01db1fc062603954` on `feat/card-task-aec861c3`, using S1/CP-1 of the plan at `011a6349a...`. Assess the final compiled endpoint log and ownership contract, secret non-disclosure and unchanged local defaults; retain the first fix's compile failure, source-only witness limits and owed Windows parse. Produce the exact-source ordinary Review and land handoff if clean. Do not change the subject or launch AppHost. Mutation is waived by the operator.

2. **Role Review; kind Grok; tier Frontier; server2; S2 ordinary round 1.** Review owner `8bbde9af-716d-4bfb-84d0-59b358bca67b` at exact pushed tip `0d983100e943006bccf43ecadf0ba77280f479ac`, tested source `7a0696dbd8320b25054107377d909daf2812e83d`, against S1 baseline `db308284...`. Assess configuration precedence, malformed-secret refusal, host/port-only output, remote/local branching and the stubbed behavioural evidence for CP-2. Preserve the disclosed unleased authoring runs and native parse obligation. Coordinate S1 review so inherited source is not silently treated as approved. No live secrets, sockets or Docker. Record exact-source review evidence; Mutation is waived.

3. **Role Code; kind Codex; tier High; server2; S3 only.** Implement the already specified S3 at plan `011a6349a...:227`, using S2's published branch tip as the source basis and retaining pending S1/S2 approvals. Add the red-first documentation contract witness, the CARD-1844 owner/bootstrap wording, and CP-3 V-4/R-3 evidence. Preserve the current “A2 unverified” status and production-volume recycle exclusions. No compose/deploy/cutover, migration, backup execution or AppHost change. This is the existing plan's docs slice, not implementation of F-1/F-2.

## Remaining uncertainties and handoff

- Production cluster existence, actual active endpoint, deployment SHA, backup freshness and window history require owner-host observations; the checkout and card status cannot prove them.
- No Windows 5.1 parse, current memory/cap census, current database size, latency sample or restore drill was performed. There is no measured downtime promise.
- F-1's null/string failure path was reproduced locally; the invalid SQL is established from the versioned PostgreSQL catalog reference. It was not executed on any database. F-2 establishes a detection limitation, not an observed loss incident.
- No S1/S2 Review or S3 task was present in the complete board-scoped list and Git inventory at observation time. New work after the observation may change that result.
- No build or production test suite was run. One leased stub reconstruction completed; the stored Code/Review counts above belong to their named source SHAs, not this report.

## Not done, noted

Fix idea only: commission separate Plan/Code ownership for F-1's SQL/error boundary and F-2's rollback evidence before the operator cutover; no repair was designed or implemented here.

--- next stage ---
next: plan
handoff: Refresh CARD-1844's existing plan with the unlanded S1/S2 and absent S3 ledger, newer backup evidence, operator-only host/drill gates and confirmed cutover verifier/rollback findings F-1/F-2; preserve the three ready source dispatches and require measured memory, disk, recovery and downtime evidence before any window.
artifact: docs/investigations/2026-11-12-card-1844-plan-move-the-apphost-postgres-from-the-desktop-to-server2.md
