# Full real-vs-fake test timing breakdown (extends CARD-0474)

Continuation of task `bb781960` (canceled when Codex hit its 2026-09-15 usage-limit exhaustion
mid-run). Its retained worktree/evidence at `C:\Antiphon\worktrees\card-task-bb781960\.antiphon\profile-bb781960\`
is reused below rather than re-measured. CARD-0474's full report
(`C:\Antiphon\worktrees\card-task-722e21e7\.antiphon\task-722e21e7.md`) already profiled the three
principal `AgentTaskLand{Boundary,Admission,Concurrency}Tests` matrices in depth (77m 51.6s, ~85% of
its broad slice); this report does not repeat that and instead covers everything else in scope, plus
one significant extension CARD-0474 did not have: **the C448 land-matrix problem is bigger than three
classes.**

No source, test, or configuration changes were made. No tests were re-run beyond what was already
captured in the retained evidence and one Python re-aggregation of already-produced logs (no new test
execution).

## What bb781960 already captured (reused, not re-measured)

Evidence root: `C:\Antiphon\worktrees\card-task-bb781960\.antiphon\profile-bb781960\`. Profiled commit
`0eff2a32`. Same worktree, same machine (desktop-ktlkpif), same day as this task.

- `runner-summary.json` / `runner-analysis.json` / `metrics.json` — a full `Antiphon.SessionRunner.Tests`
  run (551 cases, 544 passed / 2 failed / 5 opt-in-skipped, outer 354.07s, discovery only 0.265s).
- `dbprobe.json` / `dbprobe-repeat.json` / `metrics.json` — a dedicated 12-trial Postgres/TestDbFixture
  microbenchmark (init, clone, drop, EF query, 8-concurrent-clone, 100 round trips), run twice.
- `herdr-ping.json` / `herdr-isolated-ping.json` / `herdr-owner.json` — FakeHerdrServer vs real Herdr
  CLI protocol round-trip comparison, using a disposable named Herdr session (no production session
  touched).
- `remaining-clean.trx` / `remaining-measured.log` / `remaining-clean.log` — an attempted broad run
  across ~320 remaining classes (everything in `Antiphon.Tests` outside the three CARD-0474 matrices).
  This is the run that was interrupted by the Codex cancellation: the `.trx` only closed out the
  earlier failed Docker-endpoint attempt (4,546 instant failures, 50.97s, excluded), and the real
  attempt that followed left no final TRX/summary — but its full text progress log survived
  (`remaining-measured.log`), and its companion script (`live-progress.py`) already exists specifically
  to reconstruct per-class timing from that raw log by cross-referencing `remaining-clean.trx`'s
  `TestDefinitions` (valid regardless of the Results/outcome section) for class names. I ran that
  existing script (no test execution) and it resolves 1,440 of 1,455 reported cases to a single class.
- `source-findings.md` — working notes already recording the DB fixture's clone-lock mechanism in
  detail, the Herdr real-session setup, and a first pass at "new costs beyond CARD-0474"
  (RunnerRestartHealthTests, ChannelBridgeTests).

## 1–2. Wall time and real-vs-overhead split, by category

### Real git.exe (~30 classes) — land matrices + git-integration classes

CARD-0474's number stands: the three principal matrices (`AgentTaskLandBoundaryTests` 117 cases/44m17.7s,
`AgentTaskLandAdmissionTests` 24/20m31.9s, `AgentTaskLandConcurrencyTests` 28/13m02.0s) total
**77m51.6s**, ~636–689 real top-level `git.exe` invocations per admission case, serialized behind the
shared `ProcessSpawnLimit<T>` (fixed at 1). Fixture bootstrap (bare remote + seeded repo + worktree +
clone) is cheap; the cost is almost entirely in-test repeated protocol/inspection traffic
(`rev-parse`/`show-ref`/`symbolic-ref` dominate: 247+120+58 of 636 top-level calls in one traced case).

**New finding not in CARD-0474**: the `remaining` run reached two more full-protocol land-matrix
classes before cancellation, and they are large:

| Class | Cases reached | Sum of case seconds |
|---|---:|---:|
| `AgentTaskLandCheckpointMatrixTests` | 41 | 1513.28s (25.2 min) |
| `AgentTaskLandCleanupSafetyTests` | 21 | 1136.92s (18.9 min) |

That is another **~44 minutes** of case-sum time in classes CARD-0474 never profiled, using the same
C448-style full-protocol pattern (individual rows like `C448_V27_EveryDeletionRefreshesRemoteProof`
take 33s–2m10s each; `C448_V18_CleanupRetryPreservesChangedWork` rows run 33–45s each). The filter list
for the interrupted run (`remaining-filter.txt`) also names `AgentTaskLandIdentityMatrixTests`,
`AgentTaskLandRemovalMatrixTests`, `AgentTaskLandNotificationPersistenceTests`,
`AgentTaskLandNotificationRecoveryTests`, `AgentTaskLandPublicationTests`, `AgentTaskLandReceiptTests`,
`AgentTaskLandRecoveryTests`, `AgentTaskLandRefusedRetryTests`, `AgentTaskLandSweepTests`,
`AgentTaskLandingPersistenceTests`, `AgentTaskLandMonitoringTests`, `AgentTaskLandHoldVisibilityTests`,
`AgentTaskLandPreparationIdentityTests`, `AgentTaskLandStageOutcomeTests`,
`AgentTaskLandPersistenceFailureTests` — 15 more `AgentTaskLand*` classes that were **never reached**
before cancellation (the run got through roughly 87 of ~320 classes, and TUnit's scheduling order put
these near the end). **Whether they are matrix-style (real full-protocol, expensive) or narrow
unit-style (cheap) is unconfirmed** — some names (`...HoldVisibilityTests`, `...MonitoringTests`) read
as narrow, others (`...IdentityMatrixTests`, `...RemovalMatrixTests`) read as matrix-shaped like the two
just found. This is the single biggest open question left by the cancellation.

Other real-git classes (`WorktreeManagerGitIntegrationTests`, `GitServiceTests`, `GitDiffSpikeTests`,
`WorkspaceInfoGitIntegrationTests`, `LandingGitTests`, `CardFileGitFailureAcceptanceTests`,
`GitIgnorePreviewTests`, ~20 more per `git-filter.txt`) were **not reached** in the interrupted run
either. CARD-0474 measured three of them directly: `WorktreeManagerGitIntegrationTests` (15 cases,
22.15s), `GitServiceTests` (12, 5.20s), `GitDiffSpikeTests` (5, 10.98s/13.74s sum) — all cheap, real but
narrow single-command Git usage, not the matrix pattern. No evidence contradicts treating the rest of
this ~30-class group as similarly cheap, but that is an inference from a small sample, not a
measurement of all ~30.

### Real Herdr binary/live session (~3 opt-in headed classes)

No dedicated headed-class run exists in bb781960's evidence (the runner project ran with
`ANTIPHON_HEADED_TESTS=0`/`headed: false` per `runner-summary.json`). What bb781960 did measure is
**protocol overhead only**: a same-operation ping using compiled `FakeHerdrServer` vs a disposable
real Herdr CLI server (`antiphon-profile-bb781960`, PID recorded in `herdr-owner.json`, default live
session unavailable — a `HerdrBackendUnavailableException` was hit once against the real production
default socket before the disposable session was created, confirming no production session was used).

| Peer | Sample | Median | Mean |
|---|---|---:|---:|
| fake (`herdr-ping.json`) | 20 trials | 0.65ms | 3.36ms |
| real, same run | 20 trials | 12.63ms | 14.21ms |
| fake (isolated repeat, `herdr-isolated-ping.json`) | 20 trials | ~0.6ms (noisy, 0.19–26.2ms) | — |
| real, isolated repeat | 20 trials | — | — |

Real Herdr round-trip is ~19x the fake's median, but both are single-digit-to-low-double-digit
milliseconds — **this category is not a wall-clock concern at the ~3-class scale**, whatever the actual
headed-class bodies cost beyond the protocol call itself (unmeasured here). This is a genuine gap: the
three opt-in headed classes' own wall time was not captured by either task.

### PostgreSQL (100+ classes via Testcontainers)

Direct microbenchmark (`dbprobe.json`, `dbprobe-repeat.json`, `metrics.json`), 12 trials each, run
twice for consistency:

| Phase | Run 1 mean | Run 2 mean |
|---|---:|---:|
| `initialize` (container + migrate + template) | 24,367ms | 25,714ms |
| `clone` (CREATE DATABASE TEMPLATE) | 181.3ms | 139.4ms |
| `drop` | 60.2ms | 49.5ms |
| `ef_first_query` (cold model/compile) | 141.7ms | 117.5ms |
| `ef_20_warm_queries` | 44.7ms (2.2ms/query) | 40.4ms (2.0ms/query) |
| `transaction_rollback` (round trip) | 5.9ms | 5.7ms |
| `npgsql_100_roundtrips` | 165.3ms (1.65ms/roundtrip) | 99.2ms |
| 8-concurrent-clone total (lock-serialized) | 2,061.6ms | 1,359.5ms |

This matches CARD-0474's 14–37s bootstrap figure (24.4–25.7s here) and is a **fixed, one-time-per-process
cost**, separate from per-test query time. Per-case marginal cost is small (a clone is ~140–180ms, a
warm query ~2ms).

The interrupted `remaining` run gives a real-world cross-check: of 1,440 classified cases (mostly
`Antiphon.Tests.Application.*` and `Antiphon.Tests.ApiKeys.*`, i.e. real DB-backed service/integration
tests, excluding the two land-matrix classes above and `ChannelBridgeTests`), the other ~85 classes sum
to **~1,140s across ~1,340 cases**, i.e. under 1s/case average — consistent with the DB fixture's
per-test cost being small and the fixed bootstrap (paid once per process, not per class) being the real
tax. **The full 100+-class Postgres population was not exhaustively run to completion** — the
interrupted run reached roughly 87 of ~320 classes in the combined "remaining" filter before
cancellation, so this is a representative sample, not a complete census.

### PtyHost native CreateProcess/ConPTY spawn (~15+ classes)

Best proxy is the full `Antiphon.SessionRunner.Tests` run (`runner-summary.json`): **551 cases in
354.07s outer wall, with only 0.265s of that in discovery** — a dramatically cheaper fixed-cost profile
than `Antiphon.Tests` (no Postgres/Testcontainers dependency). Per CARD-0474's own numbers, raw
ConPTY/native-process spawn is cheap in isolation: `SessionRunnerRuntimeTests` (5 cases, 5.33s, max
1.88s), `RawPtyAdapterTests` (12, 15.52s, max 6.22s), `RunnerProcessProbeTests` (27, 68.51s, max
11.22s). The bulk of the 354s in this project is **not** raw ConPTY cost — it's two classes doing real
child-process/script work:

| Class | Cases | Sum |
|---|---:|---:|
| `RunnerRestartHealthTests` | 55 | 143.23s |
| `TranscriptAdoptionSafetyTests` | 44 | 71.21s |
| `DaemonStartupDiagnosticsTests` | 8 | 36.54s |
| `RunnerRestartScriptCompatibilityTests` | 8 | 25.96s |
| `PtyHostAdoptionTests` | 6 | 18.93s |
| `RunnerStartupReadinessTests` | 2 | 16.17s |

`RunnerRestartHealthTests` alone is 40% of the project's wall time: each of its 55 cases launches a real
PowerShell process for AST/safety-boundary validation, then a second for real script execution against
platform fakes, and repeats full preflight validation every row even though the script under test is
often identical across rows (per `source-findings.md`).

### Browser PDF rendering (1 class)

From CARD-0474 (not re-measured): `MarkdownPdfRendererTests`, 6 cases, 5.27s occupied, 4.12s slowest
case. Trivial in absolute terms.

## 3. Fake vs. real counterpart comparisons

| Pair | Fake | Real | Ratio | Source |
|---|---:|---:|---:|---|
| Herdr wire protocol ping (`FakeHerdrServer` vs real CLI) | 0.65ms median | 12.63ms median | ~19x | `herdr-ping.json` |
| `SessionMessageQueueBridgeQueueHarness` (short verification budgets: 1s post-submit, 3s transcript-confirm) vs `ChannelBridgeTests`'s own `HarnessAsync` (default `TimeProvider.System` + default `SessionMessageQueueService` settings, both already using `FakeAgentProtocolAdapter`/fake messaging) | n/a (both use fakes) | main-path `ChannelBridgeTests` cases repeatedly cost ~31s each despite already being fully faked | — | `source-findings.md`, cross-checked against `ChannelBridgeTests` summing to 658.76s/40 cases (16.5s/case avg, driven by a subset of ~31s cases) in `remaining-measured.log` |
| PTY payload via `FakeClaude`/`FakeGrok` vs real ConPTY spawn | n/a — no isolated same-operation comparison exists in either task's evidence | `SessionRunnerRuntimeTests` (real spawn) 1.88s max/case | — | not directly comparable; CARD-0474 already noted queue tests use real inbox PTY + `FakeClaude`, i.e. this category is a hybrid, not a clean fake/real pair |
| LLM API via `Antiphon.FakeLlmApi` | `FakeLlmApiSelfTests`, 12 cases, 13.47s total (~1.1s/case) | no directly comparable real-LLM-API test exists in this test project (real model calls are out of scope for `Antiphon.Tests`) | — | `remaining-measured.log` classification |

The standout is the **ChannelBridgeTests row**: it is the clearest case in this profile of a test that
is *already* using every available fake (agent adapter, messaging) but is still slow, because it
inherits production `SessionMessageQueueService` default timeout windows (15s evidence / 30s post-submit
/ 30s transcript-confirm per CARD-0474's cited `SupervisionSettings.cs`) instead of the short budgets its
sibling harness (`BridgeQueueHarness`) already uses. This is a fake-vs-fake-but-still-slow finding, not
a fake-vs-real one, and it is a bigger single-class cost (658.76s) than the entire PDF, Herdr, and raw
PTY-spawn categories combined.

## 4. TestDbFixture's Postgres reset mechanism — near-optimal, or is there a faster path?

Reused from `source-findings.md`, confirmed by the two independent `dbprobe` runs above:

- **Mechanism**: one-time `Before(Assembly)` spins up Testcontainers PostgreSQL 16, migrates
  `antiphon_test`, clears connections, creates a template (`antiphon_tmpl`, `IS_TEMPLATE`/
  `ALLOW_CONNECTIONS false`) once. `CreateIsolatedSchemaAsync` then does `CREATE DATABASE ... TEMPLATE`
  per test case needing isolation (a real new database, not a schema), serialized through a
  `CloneLock` with retry (5 attempts, 50/100/150/200ms backoff) against `ObjectInUse`.
  `TransactionalTestBase` uses rollback instead for cases that don't need a fully independent commit.
- **Per-operation cost is already small**: clone ~140–180ms mean, drop ~50–60ms, a round-trip query
  ~1–2ms. The one large, unavoidable cost is the 24.4–25.7s one-time assembly bootstrap
  (container start + migrate + template creation), which CARD-0474 already identified and this profile
  reconfirms independently (two separate probe runs, ~1.3s apart in mean).
- **Is a faster in-memory/no-network alternative available that preserves fidelity?** No — and this
  profile did not find one, consistent with CARD-0474. The source-level reasons are concrete, not just
  asserted: the schema uses `jsonb`, provider-specific migration SQL, EF Core concurrency tokens,
  independent per-clone contexts with real commits, and `SELECT ... FOR UPDATE` locking
  (`AgentTaskLandService.cs:68,575`). EF Core's own guidance
  (https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy) and Postgres's own
  `CREATE DATABASE` docs (https://www.postgresql.org/docs/16/sql-createdatabase.html, noting WAL_LOG is
  already the efficient default for template cloning, not FILE_COPY) both support this: SQLite/EF
  InMemory would materially change transaction/locking semantics these tests depend on, and switching
  the template's copy strategy is not a free win.
- **Where real (unimplemented, unmeasured) headroom exists** is architectural, not in the clone
  mechanism itself: (a) the assembly hook runs unconditionally even for classes that don't touch
  Postgres — `SessionDeliveryProfileTests` uses real Postgres and `MarkdownPdfRendererTests` launches a
  browser even inside what CARD-0474 called the "Unit" lane, so simply reclassifying test categories
  doesn't remove the hook; a genuinely DB-free assembly/lazy initialization is the only way to skip the
  24s+ cost for classes that don't need it; (b) a bounded precloned-database pool, if fully proven safe
  under concurrent test writers. Neither was implemented or benchmarked in either task — this is a
  design option, not a measured saving.

**Conclusion: the clone-to-known-state mechanism itself is near-optimal for what it guarantees.** The
actual lever is architectural (avoid paying the one-time bootstrap for DB-free classes), not a faster
clone algorithm.

## Priority ranking (aggregate wall-clock savings in a typical Code/Review regression dispatch)

1. **C448-style land-matrix classes, all of them, not just the three CARD-0474 profiled.** Confirmed
   floor: 77m52s (3 classes, CARD-0474) + ~44m (2 more classes, this task) = **~122 minutes** of
   case-sum time across just 5 classes, with 15 more `AgentTaskLand*` classes of unconfirmed size still
   unmeasured. Even if most of those 15 turn out narrow, the two newly found ones alone nearly double
   CARD-0474's already-dominant number. This remains overwhelmingly the largest lever, and the highest
   remaining unknown — resolving whether the other 15 classes are matrix-shaped or narrow is the single
   highest-value next measurement.
2. **`ChannelBridgeTests` (658.76s/40 cases).** Unlike the land matrices, this is not real-external-tool
   cost — it is a test class using the same fakes as its own cheap sibling harness but inheriting
   production timeout windows instead of `BridgeQueueHarness`'s short verification budgets. This is the
   cheapest-to-fix, highest-payback item in this report: no new fakes needed, just point the harness at
   short budgets like its sibling already does, preserving the same delivery-verification guarantees.
3. **`RunnerRestartHealthTests`/`TranscriptAdoptionSafetyTests` (SessionRunner.Tests, 143s+71s across
   99 cases).** Real subprocess-per-row cost with repeated preflight; caching preflight per exact
   script hash while keeping per-row execution is a concrete, scoped optimization.
4. **TestDbFixture bootstrap (24–37s fixed, once per `Antiphon.Tests` process).** Already
   near-optimal at the per-clone level; the only real lever is architectural (lazy/DB-free assembly
   split), which is a bigger, riskier change than 2–3 above for a bounded, one-time-per-process payoff.
5. **PDF rendering, real-Herdr protocol overhead, raw ConPTY spawn.** All measured in single-digit
   seconds or low milliseconds at their current scale — not worth dedicated optimization effort.

## Gaps / what would resolve them

- The 15 unreached `AgentTaskLand*` classes' actual size is the biggest open question. Resolving it
  needs a targeted run of just those 15 classes' filter (a subset of `remaining-filter.txt`), not a
  re-run of the full ~320-class sweep.
- The ~30-class real-git group beyond the 5 profiled land-matrix classes and 3 CARD-0474 spot-measured
  classes (`WorktreeManagerGitIntegrationTests`, `GitServiceTests`, `GitDiffSpikeTests`) was never run
  in either task; `git-filter.txt` already exists as a ready-to-use filter for that.
- The 3 opt-in headed Herdr classes' own body cost (as opposed to bare protocol round-trip) is
  unmeasured by either task; would need a `ANTIPHON_HEADED_TESTS=1` run scoped to those classes.
- The Postgres-heavy remainder of the ~320-class filter (roughly 233 classes not reached) was not run;
  the 87 that were reached suggest most are cheap (<1s/case average), but that is inference from a
  partial, non-random-order sample, not a completed census.

## Evidence

All cited files are under `C:\Antiphon\worktrees\card-task-bb781960\.antiphon\profile-bb781960\`:
`source-findings.md`, `metrics.json`, `dbprobe.json`, `dbprobe-repeat.json`, `runner-summary.json`,
`runner-analysis.json`, `herdr-ping.json`, `herdr-isolated-ping.json`, `herdr-owner.json`,
`remaining-clean.trx`, `remaining-measured.log`, `remaining-filter.txt`, `git-filter.txt`,
`live-progress.py` (existing script, run once here with no test execution to reclassify the retained
log — output not separately saved, reproducible via `C:\Python310\python.exe live-progress.py` from
that directory). CARD-0474's full report:
`C:\Antiphon\worktrees\card-task-722e21e7\.antiphon\task-722e21e7.md`.
