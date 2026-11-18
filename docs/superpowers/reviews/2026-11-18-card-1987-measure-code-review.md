# CARD-1987 measure slice (M1-M3 + prompt-span counters): Code review, round 1 (Final/Full)

Review task d0906b3c (Claude Opus; author Grok). Subject `origin/feat/card-task-53932621` at
**64061dc51c52044b23281998623104686f1b48b3**, range `997f9b8d9ffc4ba870d2fd56caf7c03781591624..64061dc51c`
(5 commits). Plan: `docs/superpowers/plans/2026-11-18-card-1987-query-rate-and-hot-path-caching-plan.md`
(M1-M3); note: `docs/investigations/2026-11-18-repeat-reads-and-cache-candidates.md` (Q5). Master
6414a71967575b2a7dfbf283ee273f930b7a2100. Host: server2 runner mirror.

**Verdict: CLEAN. No blockers, no regressions.** Five advisories below; none holds the land.

## Evidence (unedited CHECKPOINT lines)

Run 20261118-202126-93ac: the subject's closed extract `docs/superpowers/plans/2026-11-18-card-1987-checkpoints.md`
(validate: `CHECKPOINT SOURCE VALID source=64061dc51c... rows=3`). Run 20261118-203027-07fe: review rows in an
ignored `.antiphon/review-d0906b3c-plan.md` (its CP-1 = touched classes, CP-2 = guard batch).

```
CHECKPOINT CP-1 commit=64061dc51c52044b23281998623104686f1b48b3 build=ok filter=/*/*/(DbCommandMetricsTests*)|(TranscriptPromptSpanTests*)|(DerivedImpactIndexTests*)/* executed=52 passed=52 failed=0 skipped=0 trx=/work/worktrees/ws-08/.antiphon/checkpoints/20261118-203027-07fe/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=64061dc51c52044b23281998623104686f1b48b3 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=64061dc51c52044b23281998623104686f1b48b3 build=reused filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)/* executed=53 passed=53 failed=0 skipped=0 trx=/work/worktrees/ws-08/.antiphon/checkpoints/20261118-203027-07fe/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=64061dc51c52044b23281998623104686f1b48b3 sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=64061dc51c52044b23281998623104686f1b48b3 build=n/a filter=pwsh -NoProfile -File scripts/test-pg-qps.ps1 executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=64061dc51c52044b23281998623104686f1b48b3 sourceState=clean buildSource=notApplicable
CHECKPOINT CP-2 commit=64061dc51c52044b23281998623104686f1b48b3 build=ok filter=/*/*/(DbRateSamplerTests*)|(DbCallSiteMetricsTests*)|(PromptSpanOutcomeTests*)/* executed=13 passed=13 failed=0 skipped=0 trx=/work/worktrees/ws-08/.antiphon/checkpoints/20261118-202126-93ac/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=64061dc51c52044b23281998623104686f1b48b3 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=64061dc51c52044b23281998623104686f1b48b3 build=reused filter=/*/*/PromptSpanOutcomeReadTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/ws-08/.antiphon/checkpoints/20261118-202126-93ac/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=64061dc51c52044b23281998623104686f1b48b3 sourceState=clean buildSource=verified
```

- CP-1 harness: `WITNESS passed=5 failed=0`, `VERSION 16.14 (Debian 16.14-1.pgdg13+1) snapshotMs=340`.
  Matches Code's CP-1 5/0, CP-2 13/13, CP-3 1/1.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef 997f9b8d9 -HeadRef 64061dc51c`: commits=5 violations=0, exit 0.
- `audit-land-verification-graph.ps1 -Check`: subject refuses (recorded 98B4ECF0..., computed
  3B34C94165AAD889..., count 80 both); master 6414a7196 `approved`. Only this batch changes a definition
  file (`server/Antiphon.Server.csproj`, one EmbeddedResource); master's S1+S2 range touches none. That
  is the "batch's own record" class: `scripts/land-graph-land-refresh.ps1` at land is correct.
- `git merge-tree --write-tree origin/master HEAD`: two conflicts, see "Land".
- Mutation waived (operator 2026-11-12); no PC rows; nothing discharged.

## Review points

- **M1 `scripts/pg-qps.ps1`**: read-only (two counter reads + one text read, no reset, no write);
  `#requires -Version 5.1`, ASCII, AST/text checks are W-5. `-ConnectionEnv` is parsed in memory and reaches
  psql only as PG* child environment; stderr is discarded and every failure prints a fixed line. Redaction
  regex = plan D-3; W-3 proves the `ALTER ROLE ... PASSWORD` literal (before and inside the window) appears
  in no output byte on real 16.14. Plannable literals are normalised by pg_stat_statements.
- **M2 sampler + `GET /api/diagnostics/db-rate`**: one counter read a minute (`pg_stat_statements(false)`,
  5 s command timeout, no execution strategy so no retry), text only for uncached ids; every failure is a
  gap and the PeriodicTimer keeps the 60 s cadence, so an unreachable DB neither crashes the host nor spins;
  exception text is never logged. Ring 1,440 minutes with per-minute caps (15 statements, 32 ops,
  16 sites) and 2,000 redacted labels: bounded (order of a few MB). Route has the same (absent) auth as its
  `/api/diagnostics/*` neighbours; `minutes` outside 1..1440 is 400. No SQL text beyond redacted <=120-char labels.
- **M3 `DbCallSiteMetrics`**: Interlocked counters for every begin/commit/rollback/savepoint/release,
  one StackTrace per 64 commands / 16 tx events, 256-key cap then overflow; keys are type.method only.
- **Prompt-span counters**: six Interlocked counters placed exactly on the five `LoadPromptRowsAsync` paths
  plus the oversize drop in `Store`; no behaviour change; `prompt-span.` added to the `DbCommandMetrics`
  allowlist (Q5 row 1). `TranscriptPromptSpanTests` and `DbCommandMetricsTests` stay green.
- **Witness tagging**: psql's scanner drops a leading `--` comment, so the harness sends the tagged workload
  as one simple-query message on a localhost-published port. That reproduces how Npgsql/EF deliver
  `TagWith` text (comment kept, PG16 records it from statement location 0). Sound for the witness.

## Advisories (cards, not land holds)

- **A-1 text read past the label cap.** `DbRateSampler.SampleAsync` collects every snapshot id the window
  has no label for, not only new ids. Once 2,000 labels are cached (pg_stat_statements.max is 5,000), every
  tick re-reads text for all excess ids: a second read a minute that loads the whole query-text file. The
  `IN (/*IDS*/)` literal list also makes a new pg_stat_statements entry for each distinct list length on
  PostgreSQL 16 (IN-list merging is 18+). Fix: remember ids already attempted, or bind `= ANY(@ids)`.
- **A-2 gap reason.** SQLSTATE 55000 (library not in `shared_preload_libraries`, the test/E2E Postgres
  case `docs/logs.md` describes) is reported as `unavailable`, not `extension-missing`.
- **A-3 script's own tag.** pg-qps.ps1 prefixes `-- rt.diag.pgss` but sends through psql, which strips it,
  so the script's own reads show as normalised text, not the tag. Cosmetic.
- **A-4 untested shell.** No witness drives `DbRateSampler` itself (failed read becomes a gap, no throw) or
  the `db-rate` route (400, body shape). Correct by inspection; pure core and SQL are covered.
- **A-5 witness hba.** `test-pg-qps.ps1` sets `trust` for all hosts on the throwaway container, published on
  `127.0.0.1` only, for about a minute. Low; a local user could connect while it lives.

## Land

`merge-tree` conflicts with master 6414a7196 (S1+S2 landed):

1. `docs/superpowers/plans/2026-11-18-card-1987-checkpoints.md` add/add. Keep master's S1+S2 file byte for
   byte (its CP-1..CP-4 are the landed S1/S2 evidence). This branch's M table (CP-1..CP-3) cannot be folded
   into it without renumbering Code's CP identities; it must live at its own path (for example
   `docs/superpowers/plans/2026-11-18-card-1987-measure-checkpoints.md`).
2. `tests/derived-impact-index.json` (generated). Take neither side: regenerate on the combined tree with
   `scripts/regenerate-derived-impact-index.ps1`, then `DerivedImpactIndexTests`.

Then `scripts/land-graph-land-refresh.ps1` (only the server project record; 98B4ECF0 -> recomputed).
No product file conflicts.
