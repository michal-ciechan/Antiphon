# CARD-0165 — investigation: the "silent stall" of a full `Antiphon.Tests` run (2026-08-29)

Investigation only. Nothing in the test suite or the server was changed. The one file added
besides this document is `scripts/run-tests-watched.ps1`, the dump-on-stall watchdog the card's
own 2026-08-28 recipe asked for, so the next person does not have to rebuild it from a scratchpad.

Card `efb4fc2c-e3dc-421c-b2fe-f1e59c11d841`; task `43e4a6f9`. Brief:
`docs/investigations/2026-08-28-card-0222-antiphon-tests-hang.md` §4 is the recipe; §2.6 the
suspect list; reproduce under a watchdog, dump, read the dump; do not close on inference.

## Verdict up front

1. **Not reproduced.** Three watched runs, two of them on the exact tree the card was filed
   against (`13d43f6`, the last commit before the card's 08:44Z creation), one of those under a
   continuous concurrent build (the card's "a live delegate build task was active" condition):
   **0 stalls in 82 minutes of suite time.** Every run reached `Test run summary`. There is no
   dump, so there is no leaf to compare against CARD-0228's six harnesses; §5 says what structural
   reading can and cannot say about the one frozen-clock harness that existed on 08-24.

2. **The reported signature is what a healthy run looks like for its last 15–18 minutes.** TUnit
   runs the global `[NotInParallel]` tests as the final phase, sequentially, and under the default
   `--output Normal` a passing test prints nothing. Measured across the three runs (§4): the
   canary skip the card names as "the last visible output" is followed by **492–821 more results
   over 15–18 minutes**; that phase averages **4–7 CPU-seconds per minute** (7–12 % of one core),
   with stretches of **3–5 minutes at 0.4–0.9 CPU-s/min**; and in two of the three runs exactly
   one test failed after the canary, so with `Normal` output stdout would have been flat for the
   whole phase. The card's second occurrence — *alive, "Responding", ~10 CPU-seconds over 9
   minutes, output file not growing, killed* — is **1.1 CPU-s/min with no output**, inside that
   band. The baseline the observer was measuring against was CARD-0110's **14m15s** for 1 878
   tests (2026-08-20); the 08-24 tree takes **~28 minutes** on this machine today.

3. **The two 08-24 artefacts with this exact shape were not stalls either.**
   `logs/card-0162-antiphon-tests.log` (01:39–01:45, last line the canary skip, no summary) is
   described by its own author in `.antiphon/card-0162-close.md` as *"aborted mid-run — cascading
   PostgresException: 53300: sorry, too many clients already"*; and `.antiphon/card-0116-build-report.md`
   (08:58, the verification the card's session was re-checking) says the full suite *"exceeded the
   command limit twice (120 s and 604 s) without a terminal test verdict; its exact orphaned runners
   were stopped"* — the CARD-0110 foreground-window problem, not a hang.

4. **A real, reproducible defect did surface, and it is not a stall: the suite exhausts the
   Postgres testcontainer.** On master today **46 of 53 failures** are `53300: sorry, too many
   clients already` / `53200: out of shared memory` (or EF's transient-failure wrapper around them);
   the watchdog measured **104 connections against `max_connections = 100`** at t≈2m45s. The same
   signature is in the 08-24 log (51 × 53300) and on the 08-24 tree today (15 × 53200). §6 has the
   mechanism and a fix direction; it wants its own card.

5. **Recommendation:** close CARD-0165 as *not reproduced; signature explained; reopen only with a
   dump* (D1), file the Postgres exhaustion as its own card (D2), and adopt the two-line rule in
   §7 for anyone verifying with the full suite: do not kill a run under ~35 minutes, and run it
   with `--output Detailed` or under `scripts/run-tests-watched.ps1` so silence means something.
   If a dumped stall ever does appear and its leaf is a `Task+DelayPromise` under a
   `UtcNow()`-bounded loop, the fix lives in that harness's clock — CARD-0228 already audited the
   six candidates and found none exposed today; this card would then point there, not duplicate it.

## 1. What the card reports, against what the tree and the artefacts say

| Card claim (2026-08-24) | What the evidence says |
|---|---|
| Two large runs stalled "partway through, well past the normal completion window" | Normal window at the time was believed to be ~14 min (CARD-0110, 08-20). The 08-24 tree runs **27m55s** full / **26m26s** Application-only here, and 15–18 of those minutes are the silent sequential tail (§4). |
| "Process still Responding, ~10 CPU-seconds over 9 minutes, output not growing" | 1.1 CPU-s/min, no stdout. Healthy tail phase measured at 4–7 CPU-s/min average, with 3–5 minute stretches at 0.4–0.9 CPU-s/min, and stdout flat under `Normal` output (§4). |
| "Last visible output was `skipped Catalog_matches_live_claude_slash_menu`, which in every healthy run is immediately followed by the HTML report and summary" | False on every tree measured, including 08-24's. The canary is `[NotInParallel("Headed")]` — a *keyed* constraint, which TUnit 1.44 schedules **concurrently with the parallel phase**; the global (unkeyed) `[NotInParallel]` classes — 29 of them on `13d43f6` — always run after it (§2). 661 results followed it on the 08-24 tree. |
| "No orphaned Testcontainers containers, so not a DB-connection leak" | Container hygiene is unrelated to the leak that does exist: connections *inside* the one container peak above `max_connections` (§6). |
| Two of two large runs affected | Zero of three watched runs here, including the same tree and a concurrent build. |

Card created 2026-08-24T08:44:32Z. Commits on master in the hour before it: `79ee051` (07:55, the
CARD-0164 fix the card's session was verifying), `deb082b`/`979837e`/`13d43f6` (08:13–08:21,
CARD-0116). `13d43f6` is the tree used below.

## 2. Method

- **Build** to `--property:OutputPath=bin-c165/` (master, `217096e`, 1m12s) and, for the 08-24
  tree, a detached worktree of `13d43f6` in the session scratchpad (its own `obj/`, 1m01s).
  Worktree removed and all 19 `bin-c165*` directories deleted afterwards; no trailing-space
  directories left; `scripts/reap-orphaned-pty-hosts.ps1` dry-run clean.
- **Watchdog** (`scripts/run-tests-watched.ps1`, the committed form of the session script):
  starts `Antiphon.Tests.exe` with `--no-progress --no-ansi --output Detailed`, samples every 20 s
  (stdout bytes, seconds since stdout last grew, cumulative and delta CPU, working set, threads,
  and `select count(*), max_connections from pg_stat_activity` inside the Testcontainers Postgres),
  and declares a stall only when stdout has not grown for 150 s **and** the last sample burned
  under 1 CPU-second — then records the process tree, `pg_stat_activity` with wait events, a
  `dotnet-stack report`, a `dotnet-dump collect`, and only then kills. Hard ceiling 40 min. It
  never fired.
- **`--output Detailed` prints a line per passing test** (verified on the first run) — the only
  way stdout growth is a usable signal for this suite. Under the default `Normal` only
  `failed`/`skipped` lines print.
- **TUnit phase order**, checked against the v1.44.0 source
  (`TUnit.Engine/Scheduling/TestScheduler.cs`, `ExecuteAllPhasesAsync`): (1) unconstrained
  parallel tests and (2) keyed `[NotInParallel("key")]` groups run **concurrently**; then (3)
  `[ParallelGroup]`s one at a time; then (4) **global `[NotInParallel]` tests last, one at a
  time**. This is the `TestScheduler.ExecuteAllPhasesAsync` frame in CARD-0222's dump chain. Note
  the canary is *keyed*, so "the canary skipped" is a phase-2 event and says nothing about the
  end of the run.
- **Pre-guard tree**: `13d43f6` predates `ProductionRunnerGuard` (`b504cfa`, 08-25), so a
  `Program` boot there would launch the check interpreter on the production session-runner
  (CARD-0204). The guard's two settings were applied from outside
  (`SessionRunner__BaseUrl=http://127.0.0.1:1`, `Delegation__CheckInterpreterEnabled=false`). On
  run 2 the wrapper mis-split the pair and the first variable carried the literal
  `,Delegation__…` suffix, which `SessionRunnerHttpClient`'s constructor rejects as an invalid
  port — that is the 72 `UriFormatException` failures in run 2 (§3), all of them at construction,
  none of them reaching a runner. Fixed for run 3.
- **Load** for run 3: `dotnet build server --no-incremental` in a loop for the whole run — 75
  builds of 18–28 s each, back to back.

## 3. The three runs

| # | Tree | Filter | Load | Wall | Total | Failed | Stall |
|---|---|---|---|---|---|---|---|
| 1 | master `217096e` | whole assembly | none | **28m10s** | 2 920 | 53 (46 Postgres) | no |
| 2 | `13d43f6` (08-24) | whole assembly | none | **27m55s** | 2 405 | 89 (72 wrapper env, 14 Postgres, 3 other) | no |
| 3 | `13d43f6` (08-24) | `/*/Antiphon.Tests.Application/*/*` | 75 consecutive server builds | **26m26s** | 1 698 | 4 | no |

Run 1 failures, all 53: 32 tests on `53300: sorry, too many clients already` (18 as a
`BeforeTest` hook failure, 14 direct), 8 on `53200: out of shared memory` (one of them in an
`After(Test)` hook), 6 on EF's *"An exception has been raised that is likely due to a transient
failure"* wrapper (the retry-classified form of the same resource errors), and 7 unrelated:
`The_escalation_does_not_re_fire_within_its_repeat_window`,
`a_failed_worktree_add_leaves_no_registration_branch_or_directory` (test timeout),
`Two_entry_point_invocations_racing_on_startup_both_serve` (ObjectDisposed),
`a_claude_delegates_launch_arguments_are_unchanged_by_this_slice`,
`a_Kind_Grok_worker_runs_from_the_delegate_script_to_a_grok_priced_settlement`,
`a_ClaudeCode_worker_still_launches_claude_types_its_brief_inline_and_prices_on_the_claude_ladder`,
`A_codex_agent_with_an_exact_model_id_keeps_it_and_still_sets_effort`. Whether those seven are
pre-existing red is not this card's question and was not chased; they are listed so nobody reads
"53 failed" as 53 mysteries.

Run 3 failures: `Channel_mention_to_missing_runtime_target_publishes_ignored_event`,
`Start_is_idempotent_when_a_live_session_already_exists` (TaskCanceled),
`A_body_typed_while_an_overlay_is_up_recovers_via_Esc_and_submits`,
`An_uncorrelated_report_on_a_Working_task_raises_the_incident_and_nothing_else` — plausibly the
build load, not investigated.

`AgentSupervisionTests` — the only frozen-clock harness that existed on 08-24 and runs in the
final phase — passed 7/7 in all three runs (and 7/7 in CARD-0222's targeted run).

## 4. The tail phase, measured — why it reads as a stall

Per run, from the sample in which the canary's `skipped` line landed in stdout to the summary:

| Run | Canary printed at | Summary at | Tail length | Results printed after the canary | Failures after the canary | Avg CPU in tail | Samples < 1 CPU-s / 20 s | Longest low-CPU stretch |
|---|---|---|---|---|---|---|---|---|
| 1 | t ≈ 590–606 s | 1 693 s | **18 min** | 821 | **1** | 6.95 CPU-s/min (11.6 %) | 23 / 53 (43 %) | 3.0 min @ 0.59 CPU-s/min |
| 2 | t ≈ 745–764 s | 1 677 s | **15 min** | 661 | 62 (the env-var wrapper failures) | 6.50 CPU-s/min (10.8 %) | 20 / 44 (45 %) | 3.5 min @ 0.93 CPU-s/min |
| 3 | t ≈ 440–461 s | 1 589 s | **18 min** | 492 | **1** | 3.99 CPU-s/min (6.7 %) | 30 / 51 (59 %) | 5.3 min @ 0.36 CPU-s/min |

Put the card's second occurrence on the same axes: 10 CPU-s over 9 min = **1.1 CPU-s/min**,
stdout not growing, process alive and responsive. Every run above spent several consecutive minutes
below that rate, and in runs 1 and 3 a `Normal`-output stdout would not have moved at all between
the canary and the summary. The observation is real; the inference "stalled" does not follow from
it. Working set in the tail also falls steadily (2 GB → 200–300 MB) as parallel-phase fixtures are
collected, which is the opposite of a wedged process holding state.

What is in that phase: on `13d43f6`, 29 global-`[NotInParallel]` classes (`AgentSupervisionTests`,
the `AgentTaskCheck*`/`AgentTaskDeadSessionReconciliation`/`AgentTaskDeliveryWatchdog`/
`AgentTaskOverdueDeadline` family, `AttentionServiceTests`, `OrchestratorServiceIntegrationTests`,
`SessionReconciliationServiceTests`, the `AgentTui*`/`ApiKey*` API classes, …), each of which
boots its own service graph or `WebApplicationFactory`. That is the CARD-0110 growth story seen
from the other end: the sequential tail is now the majority of wall time, so parallelism does not
help it and the suite's wall clock is roughly the sum of those classes.

## 5. The frozen-clock candidate, on the tree where the card was filed

CARD-0222's mechanism needs three things at once: a `TimeProvider` whose `GetUtcNow()` does not
advance, a `SessionMessageQueueService` wait loop bounded by `UtcNow()`, and a session for which
the loop is *entered*. On `13d43f6`:

- `AgentSupervisionTests` registers the byte-identical frozen `MutableTimeProvider` (line 584) as
  the graph's only clock and registers the queue service (line 465). It is global
  `[NotInParallel]`, so it runs in the tail phase, sequentially — the right place for a hang that
  presents as "silence after everything else printed".
- The loops that existed then: `WaitForTranscriptConfirmAsync` (1565), two post-failure grace
  windows (1746, 1788), `WaitForComposerEvidenceAsync` (1874) and `WaitForSequenceAdvanceAsync`
  (1894). `SettlePostEvidenceAsync` — the leaf in both CARD-0222 dumps — **did not exist until
  `09a6a8b` (2026-08-27)**. `PollIntervalMs` defaulted to **500 ms**, not the 50 ms the herdr
  harness sets, so a wedge there would have burned ~10× less CPU than CARD-0222 measured.
- Whether a delivery in that harness can *enter* one of those loops: its adapter is the `Raw` kind
  (`Definitions["fake"] = new AgentDefinition { Kind = "Raw", … }`), and every evidence loop sits
  behind `verify`, which `IsVerifiedDeliverySessionAsync` grants only to kinds whose
  `ProviderContractCatalog` row has `DeliveryVerification.State == Supported` (the comment there
  names Claude, Grok and Codex). Its `StubRunnerClient` throws `NotSupportedException` from
  `GetSnapshotAsync`, the same short-circuit CARD-0228 found protecting
  `AgentTaskDeadSessionReconciliationTests`. So by reading, the harness does not reach a
  frozen-clock loop; by measurement, 7/7 four times.
- The other 08-24 frozen-clock harnesses (`FakeTimeProvider(DateTimeOffset.UtcNow)`, timers frozen
  too): the same set CARD-0228 audited on master and found unexposed, and on this tree they are
  the same classes with less code. Nothing measured contradicts that audit.

None of this is a proof of absence — the mechanism is a race, and CARD-0222 saw four hangs in four
runs where I saw none — but the only way to *confirm* it was a dump, the card was right to demand
one, and three watched runs did not produce one. The frozen-clock class therefore stays what
CARD-0222 called it: a candidate, now with less to recommend it than the explanation in §4, which
needs no race at all.

## 6. The defect that did reproduce: Postgres resource exhaustion inside the testcontainer

`TestDbFixture` starts one `postgres:16-alpine` with the image defaults (`max_connections = 100`,
`max_locks_per_transaction = 64`, nothing overridden). Two things in the suite then compete for
that budget during the parallel phase:

- **`CreateIsolatedSchemaAsync`** (22 call sites) builds a **distinct connection string per
  schema** (`SearchPath = test_<guid>`), and Npgsql pools **per connection string**, so every
  isolated schema is its own pool whose connections stay open for `ConnectionIdleLifetime` (300 s)
  after the test is done with them. Each also runs the full migration set in its schema.
- **23 classes boot `WebApplicationFactory`/`AntiphonWebAppFactory`**, each a whole server graph
  with its own pools and hosted services.

Measured in run 1: connections climbed from 33 at t=40 s to **101 and 104 at t=145–167 s** (server
limit 100 — the excess are connections mid-handshake that the server then refuses), and the 53300
storm lands exactly there: 32 tests fail at `BeforeTest`/first query. The **53200 "out of shared
memory"** failures are the lock table (`max_locks_per_transaction × max_connections` slots) filling
under concurrent migrations across many schemas — Postgres's wording for that condition, not the
OS. Same signature on the 08-24 tree today (15 × 53200, 1 × 53300, fewer tests) and in the 08-24
01:39 log (51 × 53300, which is why that run was aborted).

This is the actual red on master (46 of 53 failures) and it is load- and timing-dependent, so it
also moves test ordering and timing in the parallel phase from run to run. It does **not** hang
anything — every affected test fails fast — which is why it is a sibling card, not this one.
Fix direction for that card, cheapest first: `new PostgreSqlBuilder().WithCommand("-c",
"max_connections=300", "-c", "max_locks_per_transaction=256")` on the fixture (a one-line
container change; memory cost is trivial at this scale); then, or instead, cap
`CreateIsolatedSchemaAsync` concurrency with a semaphore and shorten pool idle lifetime
(`Connection Idle Lifetime=30;Max Pool Size=10` on the isolated-schema string) so a finished test's
pool drains before the next schema's migrations need the slots.

## 7. Decisions that are the operator's — each with a recommendation

- **D1 — Card disposition.** *Recommend close*, reason: "Not reproduced in three watched runs
  (two on the filing tree, one under concurrent build); the reported signature — alive, ~1 CPU-s/min,
  no stdout after the canary skip — is the measured shape of the 15–18 minute sequential tail phase
  under `--output Normal`; the two 08-24 artefacts of the same shape were an abort and a
  foreground-window timeout. Reopens only with a `dotnet-dump` of a process the watchdog caught."
  The alternative — keep it open for one more attempt in the original observer's exact
  environment — buys little: the tree, the machine and the concurrent-build condition were all
  reproduced here.
- **D2 — File the Postgres exhaustion as its own card** (§6). *Recommend yes, now*: it is the
  majority of master's red today and it made the 08-24 CARD-0162 run un-verifiable.
- **D3 — Verification convention.** *Recommend* the AGENTS.md gotcha added with this doc: a full
  run is ~28 minutes, its last 15–18 are silent under `Normal` output, and a run is not a stall
  until `scripts/run-tests-watched.ps1` (or the same three checks by hand: stdout flat, CPU delta
  ≈ 0, `dumpasync` shows the wedge) says so.
- **D4 — Feed CARD-0110.** *Recommend* noting there that the global-`[NotInParallel]` tail is now
  the majority of wall time (15–18 of 26–28 min), so the wins CARD-0110 planned in the parallel
  phase cannot bring the suite under a foreground window on their own.

## 8. Rerun

```powershell
dotnet build tests/Antiphon.Tests --property:OutputPath=bin-c165/
pwsh -File scripts/run-tests-watched.ps1 -Exe tests/Antiphon.Tests/bin-c165/Antiphon.Tests.exe -Tag full -Detailed
# Application namespace only:
pwsh -File scripts/run-tests-watched.ps1 -Exe tests/Antiphon.Tests/bin-c165/Antiphon.Tests.exe -Filter "/*/Antiphon.Tests.Application/*/*" -Tag app -Detailed
# a pre-CARD-0204 tree needs the guard from outside:
#   -EnvVars "SessionRunner__BaseUrl=http://127.0.0.1:1;Delegation__CheckInterpreterEnabled=false"
# if it fires: dotnet-dump analyze logs\watched\full.dmp -c dumpasync -c exit
Get-ChildItem C:\src\Antiphon -Recurse -Depth 2 -Directory -Filter bin-c165 | Remove-Item -Recurse -Force
```

Session artefacts (stdout, `.progress` samples, watchdog output for all three runs) are in this
session's scratchpad only; the numbers above are copied from them.

## Non-goals

No change to any test harness clock (CARD-0228's territory, and it found nothing to change); no
change to `TestDbFixture` (D2's card); no change to production code; no card edits — the card's
disposition is D1.
