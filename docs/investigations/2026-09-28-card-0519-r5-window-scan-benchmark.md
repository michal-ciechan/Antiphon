# CARD-0519 R5-1: trailing-window scale measurement

Measured at source commit `b65224602c6c59cdf83848273912c3c5662d1ae4` on 2026-09-28, PostgreSQL 16. The [query](2026-09-28-card-0519-r5-window-query.sql) is the SQL produced by EF Core `ToQueryString()` for `OpenTrailingWindows(db)` followed by the actual trailing `GroupBy`/recency/`Take(100)` expression in `ChannelReplyDispatcher`. Only EF's `@__p_0` page-size parameter was replaced by `100`. A reflection-based throwaway harness called the private method, so the filter was not copied by hand into the measurement.

The [schema](2026-09-28-card-0519-r5-window-schema.sql) contains the two relevant tables, their session/sequence and publication interval indexes, and the columns read by that query. The [seed](2026-09-28-card-0519-r5-window-seed.sql) inserts 20 transcript rows and one Published publication per turn. Each turn has a UserPrompt at row 1, AssistantText at row 10, and TurnEnd at row 20. Only the last publication in a session remains open; every earlier publication is evaluated as a closed window. The seed uses one or four long-lived sessions, not thousands of short sessions. Other entities and indexes in the full database are absent, so these times measure this query shape rather than the entire worker pass.

Reproduce in a disposable PostgreSQL 16 database: apply the schema once; for each row below, run `psql -v sessions=<Sessions> -v turns=<Turns> -f <seed.sql>`, then `SET jit=off; EXPLAIN (ANALYZE, BUFFERS) <query>`. JIT is disabled to remove compilation startup from the scaling comparison. `ANALYZE` is part of the seed. The query returned one session ID per session in every case.

| Sessions | Turns/session | Transcript rows | Publications | Execution time | Time/publication |
|---:|---:|---:|---:|---:|---:|
| 1 | 100 | 2,000 | 100 | 5.794 ms | 0.058 ms |
| 1 | 400 | 8,000 | 400 | 23.016 ms | 0.058 ms |
| 1 | 1,000 | 20,000 | 1,000 | 50.909 ms | 0.051 ms |
| 4 | 1,000 | 80,000 | 4,000 | 240.111 ms | 0.060 ms |

Three immediate repeats at 80,000 rows were 242.406, 241.539, and 241.839 ms. At that size the plan had **4,000 next-opening index probes** and **3,996 bounded text probes**. Each next-opening probe skipped 19 intervening rows; each closed text probe inspected 10 rows up to the next opener. The plan used the transcript session/sequence index for both probes, with no transcript sequential scan. The total time grows with publication count while work per old publication stays roughly constant. The review's pre-fix 4-session/80,000-row pass took 400 seconds on the full schema; this benchmark is not a controlled before/after on the same schema and host.

CARD-0068's late text before a next prompt is retained by the new `Sequence < NextOpening` bound. Ordinary CP-1 and CP-4 cases independently checked that behavior after this change; this performance seed intentionally has no late text.
