# CARD-0110 — Antiphon.Tests suite speed plan

**Date**: 2026-08-21 · **Status**: planning + measurement only (S1 executed; no test code touched)
**Baseline**: 14m15s wall for 1878 tests (card, 2026-08-20, `520715d`). Today the assembly holds
**2053 tests** (2048 passed, 4 headed-gated skips, 1 flake — §6) — ~90 net new tests/day since the
card was filed.

**Raw data**: `2026-08-21-card-0110-test-timings.csv` beside this plan — one row per test
(chunk, class, name, outcome, seconds), aggregated from 39 TRX reports produced by chunked
`--treenode-filter` runs of the full assembly on this machine today (clean master, no code changes,
AppHost + session-runner daemons running as usual, no concurrent test runs).

## 1. Measured verdict — where the time actually goes

Summed per-test execution time across the whole assembly: **2126 s** (~35.4 test-minutes). The
14m15s wall is that sum divided by TUnit's effective parallelism (~2.5×), floored by the serial
chains in §3.

**The distribution is extremely top-heavy:**

| Bucket | Tests | Summed time | Share |
|---|---|---|---|
| ≥15 s | 25 | 601 s | 28.3 % |
| 5–15 s | 70 | 605 s | 28.5 % |
| 1–5 s | 304 | 678 s | 31.9 % |
| <1 s | **1654** | 242 s | **11.4 %** |

**95 tests (4.6 %) cost 57 % of the total.** The 1654 sub-second tests — the "our logic against
fast fakes" default the user's architectural principle asks for — are already cheap and are NOT why
the suite outgrew 10 minutes. The suite's growth rate is also not the driver per se: most new tests
land in the <1 s bucket. The problem is the slow tail, and it decomposes into five concrete causes:

| Cause | Summed cost | Classes (worst first) |
|---|---|---|
| **Real `git` child processes**, ~1.2 s per spawned git op on this machine, 15–25 ops per test | **~515 s** | GitDiffSpikeTests 124 s/5 tests · GitServiceTests 116 s/12 · WorktreeManagerGitIntegrationTests 111 s/6 · DelegationWorktreeTests 111 s/10 · WorkspaceHookRunnerTests 34 s · GitIgnorePreviewTests 20 s |
| **Full 55-migration EF replay per isolated schema** (`TestDbFixture.CreateIsolatedSchemaAsync`, ~10–12 s per call; 27 call sites + one per WebAppFactory boot) | **~200 s+** | ApiKeyLaunchPathTests 101 s/9 (11.2 s avg!) · AgentTuiLaunchResolverTests 85 s/8 · plus every factory-owning class's first test |
| **Fake-CLI through a real ConPTY with production-scale poll/retry waits** | **~250 s** | SessionMessageQueuePtyIntegrationTests 104 s/7 (max 53.7 s) · CodexAdapterLocalShellTests 45 s · GrokDelegateEndToEndTests 21 s · RawPtyAdapterTests 18 s |
| **Real `pwsh` spawn per test in a 1-wide lane** | **135 s** | RunnerProcessProbeTests — 89 tests × ~1.5 s, all `[NotInParallel("RunnerProcessProbe")]` + `ProcessSpawnLimit` |
| **Production-scale timeouts inside otherwise-fake tests** | **~200 s** | ReviewLoopTests: ONE test at 51.7 s · SessionMessageQueueDeliveryVerificationTests 105 s/43 (max 23.4 s) · ApiErrorRecoveryServiceTests `Unknown_parks_at_three_attempts` 20 s · AgentTaskDeliveryWatchdogTests max 12.2 s · ProgramStartupConcurrencyTests 29.9 s (boots two real hosts, racing — inherent) |

AttentionServiceTests (86 s / 30 tests, 2.9 s avg, no artificial waits found) is the one big class
whose cost is unexplained; S4 investigates it before touching it.

### The card's own hypothesis, answered

> "if the slow parts turn out to be real-CLI-adjacent tests that SHOULD be [Explicit]/headed-gated
> but currently aren't"

**Mostly not what the data shows.** Real-Claude/Codex/Grok canaries are correctly gated already —
only 4 tests skipped as headed-gated, zero un-gated real-CLI tests ran. The two genuine gating
finds:

- **`GitDiffSpikeTests` (124 s / 5 tests) is a finished spike.** Its header says so: Story 2.13,
  findings documented in `docs/spike-git-diff-cascade.md`, including an NFR performance assertion
  ("completes within five seconds" — a test that takes 27.7 s to prove something takes <5 s). This
  is exactly the "few, [Explicit]-gated, exists to pin real behavior" category. It runs in the
  default suite today.
- **`ProgramStartupConcurrencyTests` (29.9 s)** boots two full real hosts to pin a startup race.
  Defensible in the default suite, but it is a single 30 s serial pole; a candidate for the same
  gate if S2 doesn't shrink it (each host boot pays a migration replay — S2 helps it directly).

Everything else slow is real-*process*-adjacent (git, pwsh, ConPTY) or DB-schema cost — the fix is
cheaper provisioning and compressed waits, not gating.

## 2. Serialization map — what NotInParallel actually costs

Summed test time by group (from the CSV joined against source attributes):

| Group | Tests | Summed time |
|---|---|---|
| (fully parallel) | 968 | 1214 s |
| MessageQueue | 177 | 229 s |
| keyless `[NotInParallel]` (serial vs EVERYTHING) | 338 | 147 s |
| RunnerProcessProbe | 89 | 135 s |
| Headed (the *non-gated* members, i.e. fakeclaude pty suites) | 16 | 127 s |
| Pty | 41 | 93 s |
| AgentQueue | 264 | 76 s |
| all other keyed groups | 160 | ~105 s |

Wall-clock floor ≈ keyless (147 s, nothing else runs) + the longest lane. The **ProcessSpawnLimit
1-wide lane** carries RunnerProcessProbe + the running Headed suites + Pty ≈ **~355 s** and is the
longest serial pole; MessageQueue (229 s) is second.

**Conclusion on narrowing (card question 2): don't.** The keyless classes are 338 tests but only
147 s — the CLAUDE.md shared-Postgres rule ("a test that drives a global sweep needs NotInParallel
with NO group key") is bought back for ~2.5 min of wall, and the incident record (three separate
"flaky" tests, AgentSupervisionTests' key-only serialisation being insufficient) says the race is
real. ProcessSpawnLimit's 1-wide lane is CARD-0050 S5, measured (FakeClaude rotating under a
concurrent pair). AgentQueue/MessageQueue serialise suites that drive the same global dispatcher
and queue flush paths. **The profitable move is to shrink the time spent *inside* the lanes (S3–S5),
not to widen the lanes.** No NotInParallel attribute changes anywhere in this plan.

## 3. Assembly split (card question 3) — deferred, with a decision gate

A fast-lane split is attractive on the numbers (1654 sub-second tests = 242 s summed ≈ a 1–2 min
parallel run) but is **not the first move**:

- The tail fixes (S2–S5) remove an estimated 700–900 s of summed time — likely enough to bring the
  one-shot wall to ~7–9 min, back inside a foreground window, without moving a single file.
- A split costs real ongoing friction: another project to build (full build is ~2–3 min warm),
  another `bin-*`/OutputPath surface, another place the fakeclaude staging target must be wired,
  and a permanent "which project does this test go in" tax on every card.
- The filter engine can't express a fast lane in-place: `--treenode-filter` has **no OR**, no
  negation, `[a-z]` ranges **crash the MTP parser** (`InvalidOperationException` in
  `TreeNodeFilter.ParseFilter`, measured today), `?` matches nothing, and `--list-tests` ignores
  the filter entirely. So "run only the fast tests" genuinely requires either a split or per-chunk
  runs.

**Gate: re-measure after S2–S5 land (same chunked-TRX method, or one-shot if it fits). Split only
if the full suite still cannot finish a clean run in under ~8 minutes foreground.** If split, the
line is mechanical, not conceptual: tests needing the Postgres container / ProcessSpawnLimit /
ConPTY stay in `Antiphon.Tests`; pure-logic tests (no container, no spawn, no WAF) move to a new
`Antiphon.Tests.Unit`. CI runs both; the local default loop runs Unit.

## 4. Targeted runs (card question 4)

Card plans already use per-class `--treenode-filter` commands routinely (every 08-19/08-20 plan
names them), so targeted running is the norm *during* a build. The "just run everything" habit
appears at **verification** time — CARD-0109's report measured ~11 wasted minutes per occurrence of
re-running 1900 tests to confirm 4 named failures at a base commit, and found the root cause is
documentation: no delegate-visible verification protocol exists. That fix (a protocol paragraph in
`server/Bundles/delegate-basics.md`) is CARD-0109's recommendation (a); this plan's S6 adds the
mechanical half — the chunk recipe and the filter engine's real limitations, which today are
documented nowhere (the card believed CLAUDE.md documented the OR-less limitation; it does not —
only "Filter by `--treenode-filter`"). Also relevant: a targeted run's fixed overhead is **~20–25 s**
(process + discovery + Postgres testcontainer + factory boot — measured as the consistent delta
between chunk wall and summed test time across 39 runs), so batching related classes into one
namespace-level filter beats N single-class runs.

## 5. Slices

**S1 — measure and report the real breakdown. DONE (this document + CSV).**
Method for re-measurement (works inside 10-min foreground windows): build once to an alternate
OutputPath, then run the exe per-namespace / per-class-prefix chunks with
`--report-trx --report-trx-filename c<card>-<chunk>.trx`, aggregate TRX. Chunk with `*` prefixes
only (no OR / ranges / `?`).

**S2 — make isolated schemas cheap: migrate once, clone thereafter.**
Replace the per-call 55-migration replay in `TestDbFixture.CreateIsolatedSchemaAsync` with a
migrate-once template cloned per consumer — preferred shape: migrate a **template database** once
per assembly run (extending the existing `[Before(Assembly)]` migration), then
`CREATE DATABASE test_x TEMPLATE antiphon_tmpl` (~100–300 ms) per isolation request, returning a
connection string with `Database=test_x` instead of `SearchPath=`. Serialise clone calls (Postgres
requires the template connection-free); keep the existing `IsolatedTestSchema` dispose contract
(drop database). WAF boots then also pay ~0 s (Program.cs `MigrateAsync` becomes a no-op version
check). Consumers don't change: the contract is "a connection string to an empty migrated store".
Risk to verify in-slice: any test SQL that names the `public` schema explicitly; grep first.
Expected recovery: ~200 s summed + faster boots everywhere, and it stops the per-migration growth
tax (55 today, +1 with nearly every card).
Verify: ApiKeyLaunchPathTests 101 s → <20 s; AgentTuiLaunchResolverTests 85 s → <20 s;
ProgramStartupConcurrencyTests shrinks; full re-measure.

**S3 — gate the finished git spike; share git scratch-repo setup.**
(a) `[Explicit]` on `GitDiffSpikeTests` (findings already pinned in `docs/spike-git-diff-cascade.md`;
it keeps its role as a perf canary, run on demand). −124 s.
(b) For GitServiceTests / WorktreeManagerGitIntegrationTests / DelegationWorktreeTests: build the
scratch repo ONCE per class (or a prebuilt `.git` fixture copied per test — file copy, not 15 git
processes) where tests don't mutate shared history; keep per-test repos where they do. Each git
child process costs ~1.2 s here (Windows process spawn + Defender), so the win is "fewer spawns",
not faster git. Target: ~515 s → ~200 s. This slice must NOT weaken what the tests pin — worktree
lifecycle tests that create/remove real worktrees keep their own repos.

**S4 — compress production-scale waits in the named worst tests, one by one.**
Targets, worst first: ReviewLoopTests `Two_dispatched_threads_route_their_replies_independently`
(51.7 s — find the wait; the test body is fake-adapter fast), SessionMessageQueueDeliveryVerificationTests
`Parking_a_channel_bound_agents_message_raises_a_critical_incident` (23.4 s),
ApiErrorRecoveryServiceTests `Unknown_parks_at_three_attempts` (20 s), AgentTaskDeliveryWatchdogTests
`zero_transcript_plus_unrelated_shared_commit_still_fails_and_kills` (12.2 s), and the
AttentionServiceTests 2.9 s/test average (investigate, then decide). Rule: shorten the settings the
test itself constructs (`ReEnterIntervalSeconds` etc. — SessionMessageQueuePtyIntegrationTests
already does this in places); never widen a wait, never loosen an assertion (CLAUDE.md: a test that
needs more than the budget is a test to make cheaper). Tests whose waits ARE the pinned behavior
(e.g. "3 Enters at 0/2/4s then give up" — already compressed) are left alone. Target: −150–200 s.

**S5 — RunnerProcessProbeTests: fewer pwsh spawns.**
89 tests × ~1.5 s in the 1-wide ProcessSpawnLimit lane = 135 s of pure serial wall. Read the probe
design first; then either (a) split the redaction/formatting cases off to drive the redaction logic
directly (the card's own fake-boundary principle — most of the 89 look like redaction-shape cases
that don't need a real pwsh to pin), keeping a handful of true end-to-end spawn tests, or (b) batch
multiple cases per spawned process. Target: −100 s from the longest serial lane, which converts
~1:1 into wall time.

**S6 — write the testing docs + a slow-test tripwire.**
(a) A TESTING section (AGENTS.md or `docs/testing.md`): the chunked-run recipe, per-run ~20–25 s
fixed overhead, and the measured filter limitations (no OR; `[ranges]` crash; `?` unsupported;
`--list-tests` ignores the filter; `--filter-uid` exists for exact lists). Coordinate with
CARD-0109's delegate-basics protocol fix — don't write it twice.
(b) A duration tripwire so the tail cannot silently regrow: a small script (or CI step) that parses
the TRX of a full run and reports tests ≥5 s, diffed against a checked-in allowlist of the known
slow set — a new entrant is a warning with a name, the same shape as the client suite's single test
budget. This operationalizes the user's architectural principle going forward.

**S7 (conditional) — split the assembly.** Only if the §3 gate fails after S2–S5. Shape as
described there.

Estimated end state after S2–S5: summed time ~2126 s → ~1200–1300 s, longest serial lane
~355 s → ~200 s, one-shot wall ~14m → **~7–8 min**, comfortably inside one foreground window, with
headroom for growth in the <1 s bucket where growth actually happens.

## 6. Deliberately NOT in scope

- **Any `[NotInParallel]` removal or key-narrowing** — §2; each group's incident record
  (shared-Postgres rule, CARD-0020, CARD-0050 S5's ProcessSpawnLimit, the AgentSupervisionTests
  key-was-not-enough lesson) outweighs the measured ≤147 s the keyless set costs.
- **Raising foreground timeouts / normalizing background runs** — the card's explicit "what NOT
  to do".
- **Touching the fake/real canary conventions** — verified already followed; zero un-gated
  real-CLI tests in the default suite.
- **`Antiphon.Agents.Pty.Tests` and the other test projects** — the card is about
  `Antiphon.Tests`; the same techniques (esp. S4/S6) can be applied there later by a follow-up.
- **PtyLargeWriteTests-class ceilings and CARD-0037/0048 pinned timings** — the DA1 3 s stall floor
  and delivery ceilings are measured contracts; nothing here may compress a wait that pins a
  real-binary behavior.

## 7. Housekeeping (found while measuring)

- **16 stale `bin-*` junk directories deleted** (card84 series, bin-s2g/s2m/s3, bin-c13g/c44g/c54g,
  bin-card0112 — some nested three deep, e.g.
  `server\bin-review-card-0103-queue-rerun\bin-review-card-0103-queue\bin-verify0119\`). They broke
  today's first build outright (MSB3030: content globbed from junk dirs vanished mid-build) —
  because `Directory.Build.props` `DefaultItemExcludes` only names `bin-verify/`, `bin-ptyhost/`,
  `bin-profile*/`, `bin-c37*/`. **Recommendation (small standalone fix, arguably S0): change the
  exclude to `bin-*/**`**, which ends both the per-evaluation glob tax and the junk-nesting cascade
  (every alternate-OutputPath build currently re-copies prior junk into its own output, one level
  deeper). Today's `bin-c0110` measurement dirs were deleted after aggregation.
- **Pre-existing flake observed**: `CodexAdapterLocalShellTests.Wait_for_turn_complete_does_not_succeed_on_a_stripped_empty_slow_start`
  failed once in a chunk run ("the fake turn's Working indicator must have been seen and then
  scrolled away" — screen empty), passed an isolation re-run. Clean master, no code changes here —
  timing-sensitive quiet-period test under load; relevant to CARD-0108's done-detection area.
- The card's premise that CLAUDE.md documents the `--treenode-filter` OR limitation is slightly off
  — no repo doc states it today; S6(a) closes that.
- Test count for the record: 2053 total / 2048 passed / 4 headed-gated skips / 1 flake (above),
  measured 2026-08-21 evening on clean master `1ea8404`.
