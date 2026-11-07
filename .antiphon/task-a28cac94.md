# CARD-1514 host-load telemetry S1, Review round 3 (diff since 6e21459f7, blockers only)

Outcome: clean. D-1 is fixed honestly, the CP-7 row ceiling in the frozen test design is an advisory with a required follow-up, and the diff carries no new defect. The touched row was rerun once through the checkpoint tool at the tip: CP-7 7/7, `sourceState=clean`, `buildSource=verified`, receipt validated.

Subject `origin/feat/card-task-2b953d03` tip `036bbcba09178ded1e2319664f6df008305a6370` (test source `e62f9f0ca18be1a6a917d934ada7f9280f705383`; the two later commits touch only `.antiphon/task-2b953d03.md`). Previously reviewed `6e21459f78b7697976beafee49df60934531515b`. Original Code landing owner `924f8ae2-e285-400d-8efe-fcd1a16a9ced`; repair Code task `2b953d03-b2da-477e-aa70-d978254c8d14`. Master `c2965dc7b1a377ce57aabfede628d5179bcdfaad`. Review is read-only: nothing fixed, no PC run, nothing activated, no desktop work.

Diff: `e62f9f0ca1` only, +42/-13 in `tests/Antiphon.SessionRunner.Tests/C1514HostLoadNativeOverheadTests.cs` and `tests/Antiphon.Tests/TestHelpers/C1514HostLoadFixture.cs`. No product, admission, migration or policy file changed.

## 1. D-1 tick percentile is a real p95 (honest)

- Population: ticks logged after a fixed 20 s warmup, snapshotted by count before the window (`warmupTicks`), then every tick that arrives in the 120 s window. Exclusion is by time, not by value; round 2 asked for exactly this ("warm the late paths before the measured window").
- Percentile: nearest rank `ceil(.95*n)-1` over the sorted window, the same one-based `ceil(.95*n)` the design fixes for the product (testdesign line 157). With n=24 the index is 22, the 23rd of 24; the floor `n >= 20` is the exact bound at which nearest rank stops being the maximum (`.95n <= n-1` iff `n >= 20`), and `index < n-1` is asserted structurally. Nothing is trimmed by value.
- Limits unchanged: `< 1d` CPU point, `>= 12` samples, `< 25d` ms, `<= 256 MiB` spool, requests `<= TotalSamples`. Workload unchanged: `IntervalMs` default 5000 (`HostStatsSettings.cs:14`), child arguments identical, `DOTNET_TieredCompilation=0` on both children as before, cold tick still unlogged (`HostStatsSamplerService.cs:101-102`).
- Non-blocking: the `>= 12` samples floor is now half the window's 24 expected samples; cadence stays bounded by the `>= 20` tick floor (ticks and samples are 1:1 in `SampleOnceAsync`), so no gap. Fold a 24 floor into the design amendment below.

Independent rerun at the tip (run `20261107-172934-318c`), 1-min load 13-28 across the round-2 failure band:

| pair | off points | on points | delta | samples | ticks | p95 ms | spool | authority | load before -> after |
|---|---|---|---|---|---|---|---|---|---|
| 0 | 0.0333 | 0.1750 | 0.142 | 24 | 24 | 1.427 | 43598 | 15 | 20.58 -> 21.19 |
| 1 | 0.0250 | 0.1750 | 0.150 | 24 | 24 | 2.009 | 43643 | 15 | 21.19 -> 13.01 |

Code's proving run `20261107-164542-efc7` was read on this host (`/work/worktrees/task-2b953d03/.antiphon/checkpoints/20261107-164542-efc7`): GREEN 7/7 at `e62f9f0ca1`, clean/verified, pair p95 1.405 / 2.064 ms, 24 ticks each, as reported. Its manifest `c1514-s1l.yaml` differs from the design packet only by `timeoutMinutes: 14`.

## 2. CP-7 row ceiling: advisory with a required follow-up (A-7), not a blocker

Facts. The design packet `c1514-s1l.yaml` sets `timeoutMinutes: 10` explicitly; `RunScheduler.RowDeadlineMinutes` uses an explicit value as-is, so the test host would be killed at 600 s with no TRX. Measured row execution: 578.4 s (Code) and 575.3 s (this review); the overhead method alone is 570-572 s; its arithmetic floor is 4 x 140 s = 560 s. The class `Timeout(720_000)` already exceeds the 600 s row cap, so the tool would kill before the class timeout. Under R5 a row cap should be about 2x the measured p95, about 20 minutes; the design's 10 is 1.04x.

Why advisory. Product code is unaffected. Both proving receipts were produced by the checkpoint tool with explicit 14 and 15 minute caps and a stated reason. The tool's plan-table route derives 24 minutes from the 8-minute estimate (`DeriveRowMinutes` = 3x), so the row is runnable by the tool; only the design's hand-written packet value is stale. The land does not replay CP-7, SourceLanding Mutation does not replay N3/N4 (testdesign line 1269), and nightly runs the class under the 720 s class timeout and its own watchdog. The frozen design is a document this Code task could not edit.

Required follow-up before the pending Windows CP-8 desktop run, which has the same body (560 s floor plus slower Windows process starts) and the same 10-minute packet cap and would be killed: a Docs task plus card amending the frozen test design: V-51/V-52 rows to "two off/on 120 s pairs, 20 s warmup, at least 20 steady ticks"; packet rows CP-7/CP-8 `estimatedMinutes: 10`, `timeoutMinutes: 20` (2x the 577 s two-sample execution; provisional under R5); the phase-table "Native overhead method" row (about 9.6 min, 20 min cap); packet ceilings 15+20+2; samples floor 24. The 720 s class timeout (1.25x two samples) stays provisional on that card.

## 3. No new defect in the diff

- `EventuallyAsync` 5 s -> 15 s: a fixture readiness poll; all six call sites wait for a predicate and none asserts the cancellation as an outcome, so no outcome assertion or product deadline moved. Documented with the witness run and R5 in the code. The `ReadyRecipientReadsDurableSample` TRX Code cites (`/tmp/c1514-ready-trx/ready.trx`) is no longer on this host and was not rerun here (Antiphon.Tests build plus one 34 s method did not fit the budget after CP-7).
- `HostLoad()` is Linux-guarded and read-only; `Measurement` gained `MeasuredTicks` and both return sites were updated; `loadBefore/After` are receipt fields only.
- Migrations: none touched this round; the three S1 ids still sort after master's latest.
- `git merge-tree --write-tree origin/master HEAD`: conflicts only in `scripts/test-live-lane-run.ps1` (PinnedInputDigest) and `tests/test-execution-policy.json` (policyHash), for the land merge as the brief states.
- `scripts/check-evidence-diff.ps1 -Repository . -BaseRef fbb2be214a849549cfa72f2e9a17646cc848cfeb -HeadRef HEAD`: commits=43 entries=3 violations=0 (mirror not shallow).

## Unedited checkpoint evidence (run 20261107-172934-318c, manifest `.antiphon/checkpoints/review-a28cac94-s1l.yaml`, ignored)

The manifest is the design's s1l packet with `timeoutMinutes: 15` (reason: the design's 10 is below the one 578 s sample; 15 bounds the row without widening anything else). Row deadline 15 min; build deadline 15 min.

~~~text
CHECKPOINT CP-7 commit=036bbcba09178ded1e2319664f6df008305a6370 build=ok filter=/*/*/(C1514HostLoadNativeTests*)|(C1514HostLoadNativeOverheadTests*)|(HostStatsProbeLiveTests*)|(TestClassificationGuardTests*)|(NativeLinuxPolicyWitnessTests*)/(Linux_*)|(Registry_matches_compiled_metadata*)|(Policy_rows_require_pinned_attribute*)|(Pinned_attribute_contract_matches_native_platform*)|(Markers_do_not_expand_policy_exclusions*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-a28cac94/.antiphon/checkpoints/20261107-172934-318c/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=036bbcba09178ded1e2319664f6df008305a6370 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-7 waited=0s executed=575.279s wall=575.327s
BUDGET wall=855.733s execution=855.732s admissionOnly=0.001s
unlisted: none (the tool ran no other build or test command)
verdict: GREEN exit=0
CHECKPOINT SOURCE VALID source=036bbcba09178ded1e2319664f6df008305a6370 rows=1
~~~

Leases: tool bootstrap `28248569-b78c-4510-be5c-159f34a8c6eb` (label `a28cac94-checkpoint-bootstrap`, `dotnet build tools/Antiphon.Checkpoints`, 9 s); build `35e0d23d-f9f8-4484-aa2f-f678651c0f82` (`bin-c1514-s1l`, 52.1 s, waited 0 s); row `133726f4-52be-4d9c-a168-e1f758da2626` (waited 0 s). Overhead method duration 00:09:31.69. The tool deleted `bin-c1514-s1l` itself (output-cleanup 145 s); `git status` clean; no leftover under `tests/Antiphon.SessionRunner.Tests`.

## Scope

Round 3 is diff-only by brief. Not run here: the Unit lane, CP-8, the Windows manual acceptance, the `ReadyRecipientReadsDurableSample` rerun. `ordinaryScopeCompleted: Interim`. PC-1..102 and every variant stay pending for method-scoped SourceLanding Mutation.
