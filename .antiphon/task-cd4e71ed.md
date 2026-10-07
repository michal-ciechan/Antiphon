# CARD-1108/1124 follow-ups Plan (task cd4e71ed)

Outcome: plan written, importer-validated (35 rows, exit 0), committed and pushed on
`feat/card-task-cd4e71ed`. Inspected source `origin/master` =
`9975d163e34501e0fe66bf1f8486ef1a6413b4d2`; the branch was fast-forwarded from `5b713f685` to it
before any citation. Platform read: runner-defaults revision 2 (global `server2`); session-runners
desktop accepting 0/2, server2 draining, server2-temp accepting 3/10; no -Runner, no -Platform.

Artifact: `docs/superpowers/plans/2026-10-07-card-1108-1124-followups-plan.md`.

## Per-card verdicts (all still true at 9975d163e; citations in the plan's ground-truth table)

| Card | Decision | Slice |
|---|---|---|
| CARD-1135 | production hardening: `HoldAsync` branches on the row's real state (Held -> `StampHeldAttemptAsync` with reason; else `PersistStateAsync(park.State -> Held)`); the stamp writes `ReasonCode`; `PrepareAsync` sets the in-memory park to Requested after a successful intent persist (load-bearing: without it CARD-1108 G-8 regresses) | S1 |
| CARD-1137 | fixture fix, cause named: the loopback `SessionRunnerHttpClient` inherits the production `ListTimeoutSeconds` default (3 s), which cancels a cold in-process Kestrel inventory read under class concurrency; the fixture's own stated budget is 10 s. One `LoopbackBudgetSeconds` constant governs `HttpClient.Timeout`, the factory and the settings. No production timeout changes; NotInParallel and ParallelLimiter rejected with reasons | S1 |
| CARD-1129 | production hardening: `Released` counts only ledger rows with `ConfirmedAt >= runStart` (zero added statements); a per-run visited set stops the run at the first repeat; test-only 119 s arm | S2 |
| CARD-1141 | dated amendments to the CARD-1108 plan (V-6/D-6/PC-11 read 3/4; CP-28 reads 15); owner-doc Known-limits sentence rewritten and pinned (`c1141-known-limits`) | S2, S6 |
| CARD-1103 | production hardening: `HasConfirmedPublishedParkAsync(task)` scoped to the current attempt, Parked/ResumePending only (Resumed can never match: its attempt is always task.Attempt - 1); the remote-pool 422 keeps its code and order and names the Blocked task and `-Reply` when a current-attempt park is confirmed; owner and orchestration-loop sentences flip | S3 |
| CARD-1097 | item 1 production hardening: one `park_resume_refused` warning per reason (newest-Warning read replaces the per-tick insert); items 2-5 stay on the card (see recommendations) | S4 |
| CARD-1138 | test-only: sampler tick pinned at 15 reader statements per runner host (14 without a card; the prune delete is a non-reader the interceptor does not capture) through a `TestDbFixture` interceptor overload; cross-task join arm (Park null, receipt true) | S5 |
| CARD-1140 | dated amendments: CARD-1065 G-184/PC-184 name CARD-1124 PC-1+PC-2 as the executed guards; CARD-1124 Mutation handoff and docs row 10 corrected; the CARD-1124 Mutation brief carries the note | S5 |
| CARD-1104 | test-only pointer-path arm at production `PtySingleChunkBytes` 1024 (pointer UserPrompt + retained spill carries review-evidence/reviewed-sha); owner sentence flips (`c1065-v27-inline`) | S6 |
| CARD-1106 | test-only: five pins bound to `nameof`/constants plus a `hostBudget` pin | S6 |

## Shape

Six slices in safety order (S1 Held re-stamp + fixture budget; S2 sweep counters; S3 guidance; S4
warning dedupe; S5 pins + CARD-1140 amendments; S6 pointer arm + pin binding + Known-limits),
each 39-59 min with its rows. 13 V rows with negative arms, 9 R rows, 14 G/PC rows, 35 checkpoint
rows (narrow `C11nn_*` rows per behaviour, Postgres classes serial, no whole-Unit), 8 docs
sentences with pins, Mutation handoff for PC-1..PC-14 plus the CARD-1124/CARD-1108 brief notes.

AppHost restart: S1-S4 change server code and activate on the next restart after landing
(all inert while parking is off); S5-S6 need none. Per-tick desktop statements unchanged
(S4 trades an insert for a read on the refused-resume path only; S1 replaces a no-op CAS with one
update on a refused due visit; S2 adds none).

## Recommendations

- Close as not needed: CARD-1097 items 3-5 (inspection lease window, confirm-path bookkeeping,
  whole-repository prune): documented, pinned, fail-closed, no path to a wrong release. Re-scope
  CARD-1097 to item 2 (resume-time runner identity read). No other card closes without work.
- After the bundle lands: CARD-1129, 1135, 1137, 1138, 1140, 1141, 1103, 1104, 1106 close.
- The CARD-1124 Mutation brief must record PC-184 as closed by PC-1+PC-2 (CARD-1140); the
  CARD-1108 Mutation brief reads PC-11's red as VerifyCalls 4 against the pin 3 and CP-28 as 15.

No production edits, no test run; one build (the checkpoint driver, through build-slot, output
deleted). Tree clean apart from the plan and this report.
