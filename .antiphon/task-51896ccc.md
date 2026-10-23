# CARD-1554 BootStall fixture continuation

Continuation task: 51896ccc-eabb-492b-8526-97fa1a76f3b4. Original Code task / landing owner: a5f9c44c-29ec-431d-9e90-a912d9273f52.
Branch: feat/card-task-51896ccc. Worktree: /work/worktrees/task-51896ccc.
Continuation base: 8c7765eb7674f9b28cbff20c60d3b0a724f354a1. Full task/evidence base: 877b9ce108e6e2bb4ba81840a99c4dd788302ca4.
Code repair commit: 7556d3331674d1944ed5e12f3846076067b0c4b1.
Previous report: /work/worktrees/task-a5f9c44c/.antiphon/task-a5f9c44c.md; previous final evidence: /work/worktrees/task-a5f9c44c/.antiphon/a5f9-evidence/final-report.log.

This manifest is frozen before final qualification. Actual tested/pushed SHA, final unedited CHECKPOINT lines, receipt qualification, all V/R outcomes, timings and RC compile result are stored in the ignored `.antiphon/c1554-evidence/final-report.log`. This committed pre-run report makes no final-green claim.

Only BootStallDetectionTests changed. The completed RepairSourceDispatchTests, DelegateCheckProbeTests, OrchestratorInstructionsRefreshTests and shared BootStallWorld are untouched by this continuation. No production code, timeout, retry or assertion body changed. Existing red tests were already committed before the fix.

## Attribution and justification

Fresh pristine-base run 20261023-130110-b0bd tested clean committed 877b9ce108e6e2bb4ba81840a99c4dd788302ca4: 8 executed, 2 passed, 6 failed, zero skipped. Fresh TRX class/method and all argument identities were inspected; all eight belong to C1151_Boot_branch_statement_counts. Source is clean, build provenance verified. Baseline receipt remains historically RED (including its still-present output cleanup refusal); it is diagnostic attribution, never qualification. Build-slot waited 0s, row-slot waited 0s; build phase 239.3884175s, startup 62.9686113s, test window 12.0322818s, host wall 77.843669s. Raw receipt/TRX/rosters copied to `.antiphon/c1554-evidence/base-receipt/`, with build source stamp beside it.

One-line justification: CARD-1518 legitimately reloads AgentTasks after transcript catch-up to revalidate status/session/attempt/dispatch identity before deciding; this inherited roster #14 query raises five exact budgets 28→29 and the identity-refusal budget 22→23.

Introduced by 3b44e076dbb1ad8f1b2e039749ae033538d369d6, strengthened by abce6eff68. AgentTaskDispatcher.TryFailOverdueAsync captures evaluatedAttempt/evaluatedDispatch, pulls, reloads, then checks all binding fields before its second deadline evaluation. Removing the read would weaken stale-decision protection. It is not an extra runner pull or inventory read. The eight exact totals, transcript-pull checks, inventory-zero checks, warning-stage checks, failed-write interleaving and no-destructive-action assertions remain strict.

| Variant | Base expected | Base actual | Attribution / fixed expected |
|---|---:|---:|---|
| young-preview | 9 | 9 | unchanged |
| working-repeated-episode | 13 | 13 | unchanged |
| working-first-detection | 28 | 29 | inherited legitimate reload / 29 |
| operator-escalation | 28 | 29 | inherited legitimate reload / 29 |
| identity-changed-before-event | 22 | 23 | inherited legitimate reload / 23 |
| event-save-fault | 28 | 29 | inherited legitimate reload / 29 |
| non-working-listed | 28 | 29 | inherited legitimate reload / 29 |
| absent-proof-refused | 28 | 29 | inherited legitimate reload / 29 |

SQL roster summary: open-task list (1); first evaluation (2–9: rules gate, two last-entry reads, Working, launch clock, three boot-turn reads); recovery gates (10–11); ownership (12); episode key (13); post-pull task reload (14); second evaluation (15–22); writer task lock (23); dedup read (24); session share lock (25); boot-turn reads (26–28); event insert (29). Identity-change stops at 23. Faulted insert still counts 29. Complete emitted SQL rosters for each red variant are retained below, unedited.

The idle cleanup witness previously reached Failed but advertised a matching Running runner session. CleanupPermittedAsync correctly requires non-Working plus positive absence. Only real-idle-failure-cleans-up now seeds BootStallListing.None; the two withholding variants keep their existing listings. Kill-exactly-once, session failure reason, no direct kill/start, no release/input/compaction-stop, no retry and refused-write assertions stay unchanged. Its red witness is the prior task's clean-source run 20261023-123736-f458 at 8c7765eb7674f9b28cbff20c60d3b0a724f354a1 (52/59 BootStall tests, seven failed).

## Verification scope and cost

Explicit continuation brief overrides the default Final scope: no Unit lane, no whole assembly, and no repeat of the three completed fixture groups. One isolated build and one exact OR filter cover the three full named classes. Invariants: detection is non-destructive, safe delivery failure cleanup requires absence, stale binding/episode changes suppress writes, recovery delivery boundaries remain intact. No unbounded classes. Expected roster: BootStallDetectionTests 59, BootStallWorkingTickCharacterizationTests 3, DelegationDispatchRecoveryBoundaryTests 381 (443 total). One ordinary run, no repetitions after green. Estimated final build/startup/tests 8m; row/build hard cap 12m, total hard cap 16m, within the brief's 40m task budget.

V-0 = fresh pristine-base attribution, all eight SQL-count variants (completed: 2 pass / 6 expected inherited failures).
V-2 = full BootStallDetectionTests, including eight SQL-count variants and all three original V-15 safe-failure variants.
R-1 = full BootStallWorkingTickCharacterizationTests.
R-2 = full DelegationDispatchRecoveryBoundaryTests, all partial files.
R-3 = ordered RC patches apply and Antiphon.Tests compiles on fresh origin/rc/2026-10-20 scratch.
R-4 = receipt validation, full task-base..HEAD evidence diff, fresh complete TRX class/method counts, merge-tree with freshly fetched origin/master, remote branch tip and task-owned output cleanup.
Earlier V-1/V-3/V-4 remain historical PASS in the previous report (41/41, 50/50, 43/43 respectively); their unchanged tests are deliberately not rerun by this continuation. Windows WB-1553/WB-1554/WB-1555/WB-1551 remain OWED, not discharged by Linux.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Environment |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | all | tests/Antiphon.Tests -> bin-c1554-final/ | boot-continuation | `/*/Antiphon.Tests.Application/(BootStallDetectionTests*)\|(BootStallWorkingTickCharacterizationTests*)\|(DelegationDispatchRecoveryBoundaryTests*)/*` | V-2, R-1, R-2 | all three full classes, 0 failed/skipped | 443 | 8 | `TUNIT_MAX_PARALLEL_TESTS=4` |

Run prebuilt checkpoint tool `run --plan .antiphon/task-51896ccc.md --expected-source-sha <committed HEAD> --row-timeout 12m --total-timeout 16m --keep-outputs --max-wait 50s`, then `wait --run <id> --max-wait 50s` until terminal. Supported keep-outputs avoids automatically retained red outputs poisoning receipt qualification (previous task's cleanup_refused). After terminal wait, inspect recorded outputPaths and task ownership, copy build-source provenance, remove only exact task-owned isolated outputs, then validate the unedited report.json using scripts/validate-checkpoint-receipt.ps1 at the actual tested SHA. Do not edit receipts.

Additional drivers are explained: the pristine-base exact-method checkpoint above was explicitly commissioned for attribution; the gated checkpoint-tool bootstrap (8s held, granted/waited=0s) supplies the driver; one gated RC compilation is explicitly commissioned, with a 12m hard timeout including slot acquisition. No unlisted test selection.

## RC and handoff obligations

Fresh RC base c16cf5ef0f0429ad9c1c6bdd41ba5a89d8e973e9. Scratch /tmp/c1554-51896ccc-rc stays detached; no RC commit/push, tag or release script. Ordered code patches apply with git apply --index:
1. 692d559ca2c5fae6211b41ad99d8e887badce451
2. 762aebee213330b75b7c9623cea71bab78aaf3a5
3. fd7622fd6cb2a639de035034fa162ec7802c95cf
4. 7556d3331674d1944ed5e12f3846076067b0c4b1

All four applied without conflict; compile is still required and its actual outcome belongs in final evidence. Report/manifest/mutation-candidate commits are not RC code picks.
GET /api/runner-defaults and GET /api/session-runners were read: Linux is eligible; no host/platform is pinned. Restart: none. No desktop use, activation or deployment.

Every prior PC-1..PC-6/variant remains PENDING under docs/superpowers/mutations/2026-10-23-rc-fixture-repairs-a5f9c44c.md. Continuation PC-7/PC-8 are PENDING under docs/superpowers/mutations/2026-10-23-card-1554-boot-continuation.md. Mutation owns method-scoped deliberate mutants, intended red/restore/green and missing-control discovery. Code ran no deliberate mutant. The brief asks for no Review; the governing Code stage contract still requires next: review when ordinary verification completes. Original landing owner remains a5f9c44c.

## Historical unedited baseline CHECKPOINT

CHECKPOINT CP-1 commit=877b9ce108e6e2bb4ba81840a99c4dd788302ca4 build=ok filter=/*/*/BootStallDetectionTests/C1151_Boot_branch_statement_counts executed=8 passed=2 failed=6 skipped=0 trx=/tmp/c1554-51896ccc-base/.antiphon/checkpoints/20261023-130110-b0bd/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=877b9ce108e6e2bb4ba81840a99c4dd788302ca4 sourceState=clean buildSource=verified

### Base SQL roster: C1151_Boot_branch_statement_counts(operator-escalation, 28, 1)

```text
C1151-BUDGET operator-escalation total=29 pulls=1 inventory=0
1. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou
2. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
3. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY t."Sequence" DESC LIMIT 1
4. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY COALESCE(t."Timestamp", t."Cr
5. -- session-state.fallback.taskdeadlinepolicy.evaluateasync SELECT s."SessionId", CASE WHEN end_seq."Sequence" IS NULL THEN EXISTS ( SELECT 1 FROM "TranscriptEntries" AS a WHERE a."
6. SELECT a."LaunchResumedAt", a."StartedAt" FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 LIMIT 1
7. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
8. -- rt.boot-reply.exists SELECT p."SessionId" AS "Value" FROM unnest(@sessionIds, @clocks) AS p("SessionId", "Clock") WHERE EXISTS ( SELECT 1 FROM "TranscriptEntries" AS t WHERE t."
9. SELECT t."Sequence", t."Kind", t."Text", COALESCE(t."Timestamp", t."CreatedAt") AS "At" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 AND COALESCE(t."Time
10. SELECT EXISTS ( SELECT 1 FROM "ApiErrorRecoveries" AS a WHERE a."AgentSessionId" = @__sessionId_0 AND a."ResolvedAt" IS NULL)
11. SELECT a."Id", a."AgentSessionId", a."AgentTaskId", a."At", a."Detail", a."InputBody", a."IsLandTerminal", a."LandRequestId", a."LandingCleanup", a."LandingMode", a."LandingOperati
12. SELECT a."Status", a."EndedAt", ( SELECT s."Status" FROM "SessionQueuedMessages" AS s WHERE s."AgentSessionId" = @__sessionId_0 AND s."Origin" = 3 AND s."CreatedAt" >= @__dispatche
13. SELECT a."Detail" FROM "AgentTaskEvents" AS a WHERE a."AgentTaskId" = @__taskId_0 AND a."Type" = 12 AND a."Detail" LIKE 'BootStall%'
14. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou
15. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
16. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY t."Sequence" DESC LIMIT 1
17. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY COALESCE(t."Timestamp", t."Cr
18. -- session-state.fallback.taskdeadlinepolicy.evaluateasync SELECT s."SessionId", CASE WHEN end_seq."Sequence" IS NULL THEN EXISTS ( SELECT 1 FROM "TranscriptEntries" AS a WHERE a."
19. SELECT a."LaunchResumedAt", a."StartedAt" FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 LIMIT 1
20. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
21. -- rt.boot-reply.exists SELECT p."SessionId" AS "Value" FROM unnest(@sessionIds, @clocks) AS p("SessionId", "Clock") WHERE EXISTS ( SELECT 1 FROM "TranscriptEntries" AS t WHERE t."
22. SELECT t."Sequence", t."Kind", t."Text", COALESCE(t."Timestamp", t."CreatedAt") AS "At" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 AND COALESCE(t."Time
23. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou
24. SELECT a."Detail" FROM "AgentTaskEvents" AS a WHERE a."AgentTaskId" = @__taskId_0 AND a."Type" = 12 AND a."Detail" LIKE 'BootStall%'
25. SELECT a."StartedAt", a."LaunchResumedAt" FROM ( SELECT * FROM "AgentSessions" WHERE "Id" = @p0 FOR SHARE SKIP LOCKED ) AS a LIMIT 2
26. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
27. -- rt.boot-reply.exists SELECT p."SessionId" AS "Value" FROM unnest(@sessionIds, @clocks) AS p("SessionId", "Clock") WHERE EXISTS ( SELECT 1 FROM "TranscriptEntries" AS t WHERE t."
28. SELECT t."Sequence", t."Kind", t."Text", COALESCE(t."Timestamp", t."CreatedAt") AS "At" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 AND COALESCE(t."Time
29. INSERT INTO "AgentTaskEvents" ("Id", "AgentSessionId", "AgentTaskId", "At", "Detail", "InputBody", "IsLandTerminal", "LandRequestId", "LandingCleanup", "LandingMode", "LandingOpera

```

### Base SQL roster: C1151_Boot_branch_statement_counts(working-first-detection, 28, 1)

```text
CHECKPOINT TEMP examined=0 deleted=0 bytes=0 remainingEligible=0 retained=0 budget=False elapsedMs=307
[CARD-0204] SessionRunner__BaseUrl=http://127.0.0.1:1, Delegation__CheckInterpreterEnabled=false, Delegation__DiagnoseEnabled=false and Delegation__OutputDistillerEnabled=false for every Program boot in this assembly (inherited SessionRunner__BaseUrl='<unset>') - test hosts never launch on the production session-runner.
[CARD-0298] Hangfire__ServerEnabled=false for every Program boot in this assembly - test hosts never start a Hangfire worker (no WMI census, no production-runner list).
[CARD-0045] cleared inherited ANTIPHON_PTY_BACKEND='inbox' — pty tests declare their backend; the suite means the same thing whoever launched it.
[testcontainers.org 00:00:00.52] Connected to Docker:
  Host: unix:///var/run/docker.sock
  Server Version: 27.5.1
  Kernel Version: 4.15.0-213-generic
  API Version: 1.47
  Operating System: Debian GNU/Linux 12 (bookworm) (containerized)
  Total Memory: 125.90 GB
[testcontainers.org 00:00:01.20] Docker container 87dbf4a11942 created
[testcontainers.org 00:00:01.36] Start Docker container 87dbf4a11942
[testcontainers.org 00:00:01.83] Wait for Docker container 87dbf4a11942 to complete readiness checks
[testcontainers.org 00:00:02.88] Docker container 87dbf4a11942 ready
[testcontainers.org 00:00:03.14] Docker container e3ae46c85a14 created
[testcontainers.org 00:00:03.17] Start Docker container e3ae46c85a14
[testcontainers.org 00:00:04.37] Wait for Docker container e3ae46c85a14 to complete readiness checks
[testcontainers.org 00:00:04.40] Execute "pg_isready --host localhost --dbname antiphon_test --username test" at Docker container e3ae46c85a14
[testcontainers.org 00:00:06.07] Execute "pg_isready --host localhost --dbname antiphon_test --username test" at Docker container e3ae46c85a14
[testcontainers.org 00:00:07.29] Execute "pg_isready --host localhost --dbname antiphon_test --username test" at Docker container e3ae46c85a14
[testcontainers.org 00:00:08.38] Execute "pg_isready --host localhost --dbname antiphon_test --username test" at Docker container e3ae46c85a14
[testcontainers.org 00:00:08.69] Docker container e3ae46c85a14 ready
[testcontainers.org 00:00:52.27] Execute "pg_dump -U test -d antiphon_test --no-owner --no-privileges -f /tmp/c1420-capture-fef84d9de3bf4222b2d1d59ae302c7e1.sql" at Docker container e3ae46c85a14
[testcontainers.org 00:00:52.57] Read "/tmp/c1420-capture-fef84d9de3bf4222b2d1d59ae302c7e1.sql" from Docker container e3ae46c85a14
[testcontainers.org 00:00:52.63] Execute "rm -f /tmp/c1420-capture-fef84d9de3bf4222b2d1d59ae302c7e1.sql" at Docker container e3ae46c85a14
C1151-BUDGET working-first-detection total=29 pulls=1 inventory=0
1. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou
2. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
3. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY t."Sequence" DESC LIMIT 1
4. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY COALESCE(t."Timestamp", t."Cr
5. -- session-state.fallback.taskdeadlinepolicy.evaluateasync SELECT s."SessionId", CASE WHEN end_seq."Sequence" IS NULL THEN EXISTS ( SELECT 1 FROM "TranscriptEntries" AS a WHERE a."
6. SELECT a."LaunchResumedAt", a."StartedAt" FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 LIMIT 1
7. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
8. -- rt.boot-reply.exists SELECT p."SessionId" AS "Value" FROM unnest(@sessionIds, @clocks) AS p("SessionId", "Clock") WHERE EXISTS ( SELECT 1 FROM "TranscriptEntries" AS t WHERE t."
9. SELECT t."Sequence", t."Kind", t."Text", COALESCE(t."Timestamp", t."CreatedAt") AS "At" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 AND COALESCE(t."Time
10. SELECT EXISTS ( SELECT 1 FROM "ApiErrorRecoveries" AS a WHERE a."AgentSessionId" = @__sessionId_0 AND a."ResolvedAt" IS NULL)
11. SELECT a."Id", a."AgentSessionId", a."AgentTaskId", a."At", a."Detail", a."InputBody", a."IsLandTerminal", a."LandRequestId", a."LandingCleanup", a."LandingMode", a."LandingOperati
12. SELECT a."Status", a."EndedAt", ( SELECT s."Status" FROM "SessionQueuedMessages" AS s WHERE s."AgentSessionId" = @__sessionId_0 AND s."Origin" = 3 AND s."CreatedAt" >= @__dispatche
13. SELECT a."Detail" FROM "AgentTaskEvents" AS a WHERE a."AgentTaskId" = @__taskId_0 AND a."Type" = 12 AND a."Detail" LIKE 'BootStall%'
14. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou
15. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
16. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY t."Sequence" DESC LIMIT 1
17. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY COALESCE(t."Timestamp", t."Cr
18. -- session-state.fallback.taskdeadlinepolicy.evaluateasync SELECT s."SessionId", CASE WHEN end_seq."Sequence" IS NULL THEN EXISTS ( SELECT 1 FROM "TranscriptEntries" AS a WHERE a."
19. SELECT a."LaunchResumedAt", a."StartedAt" FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 LIMIT 1
20. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
21. -- rt.boot-reply.exists SELECT p."SessionId" AS "Value" FROM unnest(@sessionIds, @clocks) AS p("SessionId", "Clock") WHERE EXISTS ( SELECT 1 FROM "TranscriptEntries" AS t WHERE t."
22. SELECT t."Sequence", t."Kind", t."Text", COALESCE(t."Timestamp", t."CreatedAt") AS "At" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 AND COALESCE(t."Time
23. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou
24. SELECT a."Detail" FROM "AgentTaskEvents" AS a WHERE a."AgentTaskId" = @__taskId_0 AND a."Type" = 12 AND a."Detail" LIKE 'BootStall%'
25. SELECT a."StartedAt", a."LaunchResumedAt" FROM ( SELECT * FROM "AgentSessions" WHERE "Id" = @p0 FOR SHARE SKIP LOCKED ) AS a LIMIT 2
26. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
27. -- rt.boot-reply.exists SELECT p."SessionId" AS "Value" FROM unnest(@sessionIds, @clocks) AS p("SessionId", "Clock") WHERE EXISTS ( SELECT 1 FROM "TranscriptEntries" AS t WHERE t."
28. SELECT t."Sequence", t."Kind", t."Text", COALESCE(t."Timestamp", t."CreatedAt") AS "At" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 AND COALESCE(t."Time
29. INSERT INTO "AgentTaskEvents" ("Id", "AgentSessionId", "AgentTaskId", "At", "Detail", "InputBody", "IsLandTerminal", "LandRequestId", "LandingCleanup", "LandingMode", "LandingOpera

```

### Base SQL roster: C1151_Boot_branch_statement_counts(identity-changed-before-event, 22, 1)

```text
C1151-BUDGET identity-changed-before-event total=23 pulls=1 inventory=0
1. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou
2. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
3. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY t."Sequence" DESC LIMIT 1
4. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY COALESCE(t."Timestamp", t."Cr
5. -- session-state.fallback.taskdeadlinepolicy.evaluateasync SELECT s."SessionId", CASE WHEN end_seq."Sequence" IS NULL THEN EXISTS ( SELECT 1 FROM "TranscriptEntries" AS a WHERE a."
6. SELECT a."LaunchResumedAt", a."StartedAt" FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 LIMIT 1
7. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
8. -- rt.boot-reply.exists SELECT p."SessionId" AS "Value" FROM unnest(@sessionIds, @clocks) AS p("SessionId", "Clock") WHERE EXISTS ( SELECT 1 FROM "TranscriptEntries" AS t WHERE t."
9. SELECT t."Sequence", t."Kind", t."Text", COALESCE(t."Timestamp", t."CreatedAt") AS "At" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 AND COALESCE(t."Time
10. SELECT EXISTS ( SELECT 1 FROM "ApiErrorRecoveries" AS a WHERE a."AgentSessionId" = @__sessionId_0 AND a."ResolvedAt" IS NULL)
11. SELECT a."Id", a."AgentSessionId", a."AgentTaskId", a."At", a."Detail", a."InputBody", a."IsLandTerminal", a."LandRequestId", a."LandingCleanup", a."LandingMode", a."LandingOperati
12. SELECT a."Status", a."EndedAt", ( SELECT s."Status" FROM "SessionQueuedMessages" AS s WHERE s."AgentSessionId" = @__sessionId_0 AND s."Origin" = 3 AND s."CreatedAt" >= @__dispatche
13. SELECT a."Detail" FROM "AgentTaskEvents" AS a WHERE a."AgentTaskId" = @__taskId_0 AND a."Type" = 12 AND a."Detail" LIKE 'BootStall%'
14. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou
15. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
16. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY t."Sequence" DESC LIMIT 1
17. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY COALESCE(t."Timestamp", t."Cr
18. -- session-state.fallback.taskdeadlinepolicy.evaluateasync SELECT s."SessionId", CASE WHEN end_seq."Sequence" IS NULL THEN EXISTS ( SELECT 1 FROM "TranscriptEntries" AS a WHERE a."
19. SELECT a."LaunchResumedAt", a."StartedAt" FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 LIMIT 1
20. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
21. -- rt.boot-reply.exists SELECT p."SessionId" AS "Value" FROM unnest(@sessionIds, @clocks) AS p("SessionId", "Clock") WHERE EXISTS ( SELECT 1 FROM "TranscriptEntries" AS t WHERE t."
22. SELECT t."Sequence", t."Kind", t."Text", COALESCE(t."Timestamp", t."CreatedAt") AS "At" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 AND COALESCE(t."Time
23. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou

```

### Base SQL roster: C1151_Boot_branch_statement_counts(event-save-fault, 28, 1)

```text
C1151-BUDGET event-save-fault total=29 pulls=1 inventory=0
1. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou
2. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
3. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY t."Sequence" DESC LIMIT 1
4. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY COALESCE(t."Timestamp", t."Cr
5. -- session-state.fallback.taskdeadlinepolicy.evaluateasync SELECT s."SessionId", CASE WHEN end_seq."Sequence" IS NULL THEN EXISTS ( SELECT 1 FROM "TranscriptEntries" AS a WHERE a."
6. SELECT a."LaunchResumedAt", a."StartedAt" FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 LIMIT 1
7. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
8. -- rt.boot-reply.exists SELECT p."SessionId" AS "Value" FROM unnest(@sessionIds, @clocks) AS p("SessionId", "Clock") WHERE EXISTS ( SELECT 1 FROM "TranscriptEntries" AS t WHERE t."
9. SELECT t."Sequence", t."Kind", t."Text", COALESCE(t."Timestamp", t."CreatedAt") AS "At" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 AND COALESCE(t."Time
10. SELECT EXISTS ( SELECT 1 FROM "ApiErrorRecoveries" AS a WHERE a."AgentSessionId" = @__sessionId_0 AND a."ResolvedAt" IS NULL)
11. SELECT a."Id", a."AgentSessionId", a."AgentTaskId", a."At", a."Detail", a."InputBody", a."IsLandTerminal", a."LandRequestId", a."LandingCleanup", a."LandingMode", a."LandingOperati
12. SELECT a."Status", a."EndedAt", ( SELECT s."Status" FROM "SessionQueuedMessages" AS s WHERE s."AgentSessionId" = @__sessionId_0 AND s."Origin" = 3 AND s."CreatedAt" >= @__dispatche
13. SELECT a."Detail" FROM "AgentTaskEvents" AS a WHERE a."AgentTaskId" = @__taskId_0 AND a."Type" = 12 AND a."Detail" LIKE 'BootStall%'
14. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou
15. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
16. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY t."Sequence" DESC LIMIT 1
17. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY COALESCE(t."Timestamp", t."Cr
18. -- session-state.fallback.taskdeadlinepolicy.evaluateasync SELECT s."SessionId", CASE WHEN end_seq."Sequence" IS NULL THEN EXISTS ( SELECT 1 FROM "TranscriptEntries" AS a WHERE a."
19. SELECT a."LaunchResumedAt", a."StartedAt" FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 LIMIT 1
20. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
21. -- rt.boot-reply.exists SELECT p."SessionId" AS "Value" FROM unnest(@sessionIds, @clocks) AS p("SessionId", "Clock") WHERE EXISTS ( SELECT 1 FROM "TranscriptEntries" AS t WHERE t."
22. SELECT t."Sequence", t."Kind", t."Text", COALESCE(t."Timestamp", t."CreatedAt") AS "At" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 AND COALESCE(t."Time
23. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou
24. SELECT a."Detail" FROM "AgentTaskEvents" AS a WHERE a."AgentTaskId" = @__taskId_0 AND a."Type" = 12 AND a."Detail" LIKE 'BootStall%'
25. SELECT a."StartedAt", a."LaunchResumedAt" FROM ( SELECT * FROM "AgentSessions" WHERE "Id" = @p0 FOR SHARE SKIP LOCKED ) AS a LIMIT 2
26. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
27. -- rt.boot-reply.exists SELECT p."SessionId" AS "Value" FROM unnest(@sessionIds, @clocks) AS p("SessionId", "Clock") WHERE EXISTS ( SELECT 1 FROM "TranscriptEntries" AS t WHERE t."
28. SELECT t."Sequence", t."Kind", t."Text", COALESCE(t."Timestamp", t."CreatedAt") AS "At" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 AND COALESCE(t."Time
29. INSERT INTO "AgentTaskEvents" ("Id", "AgentSessionId", "AgentTaskId", "At", "Detail", "InputBody", "IsLandTerminal", "LandRequestId", "LandingCleanup", "LandingMode", "LandingOpera

```

### Base SQL roster: C1151_Boot_branch_statement_counts(non-working-listed, 28, 1)

```text
C1151-BUDGET non-working-listed total=29 pulls=1 inventory=0
1. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou
2. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
3. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY t."Sequence" DESC LIMIT 1
4. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY COALESCE(t."Timestamp", t."Cr
5. -- session-state.fallback.taskdeadlinepolicy.evaluateasync SELECT s."SessionId", CASE WHEN end_seq."Sequence" IS NULL THEN EXISTS ( SELECT 1 FROM "TranscriptEntries" AS a WHERE a."
6. SELECT a."LaunchResumedAt", a."StartedAt" FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 LIMIT 1
7. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
8. -- rt.boot-reply.exists SELECT p."SessionId" AS "Value" FROM unnest(@sessionIds, @clocks) AS p("SessionId", "Clock") WHERE EXISTS ( SELECT 1 FROM "TranscriptEntries" AS t WHERE t."
9. SELECT t."Sequence", t."Kind", t."Text", COALESCE(t."Timestamp", t."CreatedAt") AS "At" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 AND COALESCE(t."Time
10. SELECT EXISTS ( SELECT 1 FROM "ApiErrorRecoveries" AS a WHERE a."AgentSessionId" = @__sessionId_0 AND a."ResolvedAt" IS NULL)
11. SELECT a."Id", a."AgentSessionId", a."AgentTaskId", a."At", a."Detail", a."InputBody", a."IsLandTerminal", a."LandRequestId", a."LandingCleanup", a."LandingMode", a."LandingOperati
12. SELECT a."Status", a."EndedAt", ( SELECT s."Status" FROM "SessionQueuedMessages" AS s WHERE s."AgentSessionId" = @__sessionId_0 AND s."Origin" = 3 AND s."CreatedAt" >= @__dispatche
13. SELECT a."Detail" FROM "AgentTaskEvents" AS a WHERE a."AgentTaskId" = @__taskId_0 AND a."Type" = 12 AND a."Detail" LIKE 'BootStall%'
14. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou
15. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
16. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY t."Sequence" DESC LIMIT 1
17. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY COALESCE(t."Timestamp", t."Cr
18. -- session-state.fallback.taskdeadlinepolicy.evaluateasync SELECT s."SessionId", CASE WHEN end_seq."Sequence" IS NULL THEN EXISTS ( SELECT 1 FROM "TranscriptEntries" AS a WHERE a."
19. SELECT a."LaunchResumedAt", a."StartedAt" FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 LIMIT 1
20. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
21. -- rt.boot-reply.exists SELECT p."SessionId" AS "Value" FROM unnest(@sessionIds, @clocks) AS p("SessionId", "Clock") WHERE EXISTS ( SELECT 1 FROM "TranscriptEntries" AS t WHERE t."
22. SELECT t."Sequence", t."Kind", t."Text", COALESCE(t."Timestamp", t."CreatedAt") AS "At" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 AND COALESCE(t."Time
23. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou
24. SELECT a."Detail" FROM "AgentTaskEvents" AS a WHERE a."AgentTaskId" = @__taskId_0 AND a."Type" = 12 AND a."Detail" LIKE 'BootStall%'
25. SELECT a."StartedAt", a."LaunchResumedAt" FROM ( SELECT * FROM "AgentSessions" WHERE "Id" = @p0 FOR SHARE SKIP LOCKED ) AS a LIMIT 2
26. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
27. -- rt.boot-reply.exists SELECT p."SessionId" AS "Value" FROM unnest(@sessionIds, @clocks) AS p("SessionId", "Clock") WHERE EXISTS ( SELECT 1 FROM "TranscriptEntries" AS t WHERE t."
28. SELECT t."Sequence", t."Kind", t."Text", COALESCE(t."Timestamp", t."CreatedAt") AS "At" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 AND COALESCE(t."Time
29. INSERT INTO "AgentTaskEvents" ("Id", "AgentSessionId", "AgentTaskId", "At", "Detail", "InputBody", "IsLandTerminal", "LandRequestId", "LandingCleanup", "LandingMode", "LandingOpera

```

### Base SQL roster: C1151_Boot_branch_statement_counts(absent-proof-refused, 28, 1)

```text
C1151-BUDGET absent-proof-refused total=29 pulls=1 inventory=0
1. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou
2. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
3. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY t."Sequence" DESC LIMIT 1
4. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY COALESCE(t."Timestamp", t."Cr
5. -- session-state.fallback.taskdeadlinepolicy.evaluateasync SELECT s."SessionId", CASE WHEN end_seq."Sequence" IS NULL THEN EXISTS ( SELECT 1 FROM "TranscriptEntries" AS a WHERE a."
6. SELECT a."LaunchResumedAt", a."StartedAt" FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 LIMIT 1
7. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
8. -- rt.boot-reply.exists SELECT p."SessionId" AS "Value" FROM unnest(@sessionIds, @clocks) AS p("SessionId", "Clock") WHERE EXISTS ( SELECT 1 FROM "TranscriptEntries" AS t WHERE t."
9. SELECT t."Sequence", t."Kind", t."Text", COALESCE(t."Timestamp", t."CreatedAt") AS "At" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 AND COALESCE(t."Time
10. SELECT EXISTS ( SELECT 1 FROM "ApiErrorRecoveries" AS a WHERE a."AgentSessionId" = @__sessionId_0 AND a."ResolvedAt" IS NULL)
11. SELECT a."Id", a."AgentSessionId", a."AgentTaskId", a."At", a."Detail", a."InputBody", a."IsLandTerminal", a."LandRequestId", a."LandingCleanup", a."LandingMode", a."LandingOperati
12. SELECT a."Status", a."EndedAt", ( SELECT s."Status" FROM "SessionQueuedMessages" AS s WHERE s."AgentSessionId" = @__sessionId_0 AND s."Origin" = 3 AND s."CreatedAt" >= @__dispatche
13. SELECT a."Detail" FROM "AgentTaskEvents" AS a WHERE a."AgentTaskId" = @__taskId_0 AND a."Type" = 12 AND a."Detail" LIKE 'BootStall%'
14. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou
15. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
16. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY t."Sequence" DESC LIMIT 1
17. SELECT t."Sequence", t."Kind", COALESCE(t."Timestamp", t."CreatedAt") FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 ORDER BY COALESCE(t."Timestamp", t."Cr
18. -- session-state.fallback.taskdeadlinepolicy.evaluateasync SELECT s."SessionId", CASE WHEN end_seq."Sequence" IS NULL THEN EXISTS ( SELECT 1 FROM "TranscriptEntries" AS a WHERE a."
19. SELECT a."LaunchResumedAt", a."StartedAt" FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 LIMIT 1
20. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
21. -- rt.boot-reply.exists SELECT p."SessionId" AS "Value" FROM unnest(@sessionIds, @clocks) AS p("SessionId", "Clock") WHERE EXISTS ( SELECT 1 FROM "TranscriptEntries" AS t WHERE t."
22. SELECT t."Sequence", t."Kind", t."Text", COALESCE(t."Timestamp", t."CreatedAt") AS "At" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 AND COALESCE(t."Time
23. SELECT a."Id", a."ActiveLandingId", a."AgentId", a."AgentKind", a."AgentName", a."AgentSessionId", a."Attempt", a."AutoContinueOnWait", a."AutoContinuedAt", a."BootWedgeRelaunchCou
24. SELECT a."Detail" FROM "AgentTaskEvents" AS a WHERE a."AgentTaskId" = @__taskId_0 AND a."Type" = 12 AND a."Detail" LIKE 'BootStall%'
25. SELECT a."StartedAt", a."LaunchResumedAt" FROM ( SELECT * FROM "AgentSessions" WHERE "Id" = @p0 FOR SHARE SKIP LOCKED ) AS a LIMIT 2
26. SELECT EXISTS ( SELECT 1 FROM "AgentSessions" AS a WHERE a."Id" = @__sessionId_0 AND a."GrokRulesState" IN (1, 3))
27. -- rt.boot-reply.exists SELECT p."SessionId" AS "Value" FROM unnest(@sessionIds, @clocks) AS p("SessionId", "Clock") WHERE EXISTS ( SELECT 1 FROM "TranscriptEntries" AS t WHERE t."
28. SELECT t."Sequence", t."Kind", t."Text", COALESCE(t."Timestamp", t."CreatedAt") AS "At" FROM "TranscriptEntries" AS t WHERE t."AgentSessionId" = @__sessionId_0 AND COALESCE(t."Time
29. INSERT INTO "AgentTaskEvents" ("Id", "AgentSessionId", "AgentTaskId", "At", "Detail", "InputBody", "IsLandTerminal", "LandRequestId", "LandingCleanup", "LandingMode", "LandingOpera
[testcontainers.org 00:01:05.45] Delete Docker container e3ae46c85a14

```
