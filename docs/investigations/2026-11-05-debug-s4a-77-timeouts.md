# S4a CP-6: the 77 inherited timeouts

Debug task `cdf5415a`, server2, 2026-11-05. This investigation changes this report only.
The reviewed product repair remains unmodified at
`88e9fc746c78503ef84c3f183bb93ea5ab713e75`. Its ordinary verification remains RED;
this investigation does not authorize landing or replace missing acceptance/native work.

**Confirmed on master: the identical 77 cases time out with normal cached startup;
all 77 pass when the shared cold bootstrap runs before test bodies, with unchanged
assertions, parallelism and 35/45-second limits. Repair shared setup first.**

## Failure census and attribution

Review `c993f042` run `20261105-184221-6c42` failed **CP-6**, whose filter is
`/*/*/(C1677RetentionServerTests*)|(C1677RetentionSettingsTests*)|(PhoneHomeConnectionTests*)|(C0824WorkspaceSlotServerTests*)/*`.
The row executed 128 cases: 51 passed, 77 failed, none skipped. The full eight-row
run had 863 passed, 77 failed and 14 skipped. Every failure was a whole-test timeout;
none was an assertion demonstrating an S4a off-switch regression.

| Cluster | Failed cases / methods | Declared test limit | Observed failed-case wall, reviewed run |
|---|---:|---:|---:|
| Workspace-slot scripted peer | 15 / 7 | 35 s | Workspace class overall: 36.183–36.672 s |
| Workspace-slot real Git/runner | 6 / 4 | 35 s | Same class; deadlines are censored failures |
| Retention scripted peer | 52 / 20 | 45 s | Retention class overall: 45.215–46.411 s |
| Retention real Git/runner | 4 / 3 | 45 s | Same class; deadlines are censored failures |

The other 29 `PhoneHomeConnectionTests`, 14 retention-settings cases, three
workspace-slot cases and five retention-server cases passed. Some passing server
controls also create a rig, so the distinction is not simply “DB versus no DB.”
They do less application/database work after acquiring it.

The Review independently selected the exact 34 affected methods at pre-fix source
`80e5686e7d616ecfdbbcded1328f5d42365d6c31`: **0 passed, 77 failed, 0 skipped**,
with every original parameterized identity matched. Run GUID
`f7a3379d-2c4d-413c-894e-a76408a1e6d4`, native exit 2, host execution 72.423 s,
admission 120 s, slot held 77 s. The shared fixtures and both affected test files
are byte-identical between that baseline and the S4a repair tip. This establishes
inheritance independently of the diagnosis below.

## Shared bottlenecks, and what the evidence actually proves

All 77 use `RemoteWorkspacePreparerTests.Rig` directly or through
`C1677RetentionServerTests.World`, `C0824SlotTransportWorld` or
`C1677RetentionTransportWorld`. `Rig.StartAsync` obtains a cloned database,
starts a fresh `PhoneHomeTestHost`, connects a peer, then builds a service provider.
Neither affected class limits its ordinary fixture concurrency. The two classes
can therefore compete for the same four-wide clone semaphore, cold EF state and
host resources in one test process. A process build-slot lease does not limit
that process's test concurrency.

The retained TUnit HTML contains complete external spans and can be decoded
without rerunning a test. These are **completed span distributions**, not p95
of successful whole tests:

| Phase | Reviewed tip: n / p95 / max | Pre-fix baseline: n / p95 / max |
|---|---|---|
| DB clone admission | 67 / 31.047 s / 31.201 s | 73 / 39.441 s / 41.565 s |
| DB clone execution | 67 / 5.210 s / 10.934 s | 73 / 8.343 s / 9.436 s |
| DB container start | 1 / 2.342 s / 2.342 s | 1 / 3.462 s / 3.462 s |

The reviewed trace totals 711.546 seconds of nested/concurrent clone waiting;
the baseline totals 841.012 seconds. These sums are **not elapsed row wall time**.
The original CP-6 host took 74.302 s, including 11.259 s startup, 49.589 s tests
and 13.453 s teardown; row execution was 75.254 s, admission 15 s, wall 90.303 s.
The run-start host snapshot was Debian 12, 24 cores, SDK 10.0.403, load
3.45/1.35/0.84. That snapshot is not a CP-6-time load measurement.

Clone queueing is established, but does not explain every failure by itself.
Two early cases show the additional common cold-start delay:

| Case | Clone queue / clone execution | Loopback connection ready | First application SQL begins |
|---|---:|---:|---:|
| `C0824_ReleasedTaskCoordinatesNotAmbiguous` | 0.742 / 0.213 s | about 3.453 s | 33.030 s |
| `Execute_off_sends_dry_run_only` | 0.480 / 0.179 s | about 1.489 s | 32.936 s |

Offsets are relative to the case's TUnit trace. Those large gaps contain no
recorded EF-model phase; they must not be relabeled measured model duration.
Across all 77 failures, the first application SQL follows the first loopback GET
completion by median **23.995 s**, p95 **33.885 s**, maximum **34.635 s**;
**48/77** gaps exceed 20 s. This establishes that the additional delay affects
most failures, including cases whose clone completed promptly. Those gaps may
include model/provider initialization, scheduling and other uninstrumented work.
Source inspection gives a specific hypothesis: `TestDbFixture.InitializeAsync`
awaits shared-store readiness once, but a successful baked-image or SQL-cache
path can return without creating an `AppDbContext` or initializing its model.
`PrepareFromBakedImageAsync` reads migration history through Npgsql and reflection;
the cache-miss path instead calls `MigrateAsync` with a real `AppDbContext`.
Consequently a cache hit can leave process-wide EF/model/provider initialization
inside the first concurrent test bodies. `Rig.NewDb` and its registered context
use the same Npgsql options as the shared fixture. This is a stronger, actionable
hypothesis than “the host was busy”; direct model timing is still needed.

PostgreSQL creation, migrations/restoration and template preparation are already
shared once per process. Per-test **database clones are intentional isolation**,
as R3 requires. Replaying migrations per test would make the problem worse.
There is a separate existing R3 concern: `Rig.StartAsync` creates a Kestrel host
and provider per scenario. Sharing that host requires preserving independent
runner identity, peer state and fake-clock behavior; sharing a mutable clock
between parallel scenarios is not an acceptable shortcut.

The broad family overlaps CARD-1722 only in real-time budgets covering expensive
setup under contention. **67 of the 77 cases use scripted peers and never build
a scratch Git repository.** A blanket Git timeout floor cannot fix them. The ten
real-runner cases additionally use `C0824SlotTransportWorld.Git`, whose synchronous
stderr/exit waits and stdout drain have no operation deadline. Their limiter and
external-call timing gaps need a distinct, scoped repair.

## Historical boundary

The identical 21 workspace-slot cases already failed at base
`38e67ae1579dc71da848fb2c89cc9c55aaa83543` in the November 3 base/tip/base
comparison retained in commit `c052585d9b43de14ecc28015a19652fc31b92e55`
(`.antiphon/task-7c36e01b.md`). Base C was 58/79 passed, 21 timed out, 61 s
wall, load about 35→46. Base A was green at lower contention. Thus this is
intermittent and demonstrably predates the S4a repair.

Retention alone had green retained rows on November 3: 69/69 at
`82d5f8f39ba6be62924fe008aa2ae1efc7606533` and 74/74 at
`58a2a23e976ad8ec0127d1e6addd5876edeec892`. Those are different historical
selections, not proof that today's combined row was green. The exact 56-case
retention failure set is proven at the November 5 pre-fix source above. The first
introducing commit for intermittent failures is not established by these records.

SQL-cache bootstrap dates to `0829b3a5a60e64cb92f2f92fc3333ad3fa7223d1`
(October 21); baked images to `74178698ea316af2902c5ef653e8ecedecf3c698`
(November 3). These are mechanism provenance, **not a proven regression bisect**.

## Fresh executions and master comparison

The pinned master was `e6da2453013e7312c52c6ac6c117fc86b6c790d3`, also confirmed
by `git ls-remote origin refs/heads/master` at 21:25 UTC. All execution was on
server2; no desktop run, product/test edit, deployment or landing occurred.

| Driver | Source | Admission | Execution | Outcome |
|---|---|---:|---:|---|
| Required native compile preflight | reviewed tip `88e9fc746…` | 0 s | MSBuild 271.120 s; lease 272 s | 0 errors, exit 0 |
| Native archived baseline build | `80e5686e7…` | 195 s | driver 448.578 s; MSBuild 447.880 s | 0 errors, exit 0 |
| Native archived master build | `e6da24530…` | 435 s | driver 680.130 s; MSBuild 678.990 s | 0 errors, exit 0 |
| `base-minimal` | `80e5686e7…` | 706 s | driver 43.365 s; TUnit 35.876 s | **2 passed, 0 failed, 0 skipped** |
| `master-default` | `e6da24530…` | 135 s | driver 92.676 s; TUnit 89.152 s | **0 passed, 77 failed, 0 skipped**, exit 2 |
| `master-migrated` | same master, same build | 360 s | driver 92.081 s; TUnit 86.599 s | **77 passed, 0 failed, 0 skipped**, exit 0 |

These are per-driver spans, not a sum of elapsed investigation time: admissions
and the two separate archive builds overlapped. Each build/run had its own host
lease; the test drivers ran sequentially. Builds used native output,
`UseAppHost=false`, and the grant's `-maxcpucount:6`; every run used `--no-build`
with the same property and `TEMP=/tmp`. The initial native compile's complete
compiler log was not retained; its terminal summary is retained separately.

The baseline probe selected only
`C0824_ReleasedTaskCoordinatesNotAmbiguous` and
`Execute_off_sends_dry_run_only`: 15.491 s and 21.729 s respectively, starting at
load 45.91/56.69/49.69. Each had about 13.189 s between loopback readiness and
first application SQL. These are one sample per method, **not a measured p95**.
This is a fresh small-selection comparison to the already retained 77-case red
baseline; it does not reclassify that prior red run as green.

The normal master run selected exactly the original 34 methods/77 parameterized
identities, starting at load 25.83/49.04/48.66. Every identity matches the Review
matrix; every failure is the original class's 35- or 45-second timeout. Workspace
case p95/max were 38.168/38.170 s and retention 46.631/46.631 s, both censored.
Clone queue p95/max were 30.592/32.998 s; clone execution p95/max were
7.461/9.332 s. **These 77 are also red on master, proven by execution, not inferred
from ancestry.** The exact first-bad master commit remains unknown: workspace's
21-case family is documented by November 3, and the full 77-case family is proven
on the November 5 pre-fix source and this November 5 master snapshot.

For the final diagnostic control only, the child received
`ANTIPHON_TEST_PG_IMAGE=off` and `ANTIPHON_TEST_SCHEMA_CACHE=off`. The selection,
parallelism, binary, product source, test source, assertions and per-test limits
were unchanged. It started at the **higher** load 75.99/54.55/50.74 and passed all
77 identities. The full trace records **one 33.639 s `db-migrate migrate` fixture
phase before test bodies**. Clone admission p95/max fell to 13.802/13.999 s and
clone execution p95/max to 2.875/5.263 s. Post-connect-to-first-SQL gaps had median
6.761 s, p95 10.279 s, and none over 20 s.

| Green control cluster | Cases | Observed case p95 / max | Provisional 2× maximum, rounded up to 5 s |
|---|---:|---:|---:|
| Workspace scripted | 15 | 29.608 / 29.608 s | 60 s |
| Workspace real runner/Git | 6 | 29.980 / 29.980 s | 60 s |
| Retention scripted | 52 | 26.044 / 27.415 s | 55 s |
| Retention real runner/Git | 4 | 27.902 / 27.902 s | 60 s |

These are heterogeneous case distributions from **one green loaded pack**, not
repeated per-method p95 estimates. The numbers are calibration references, not
authorization to widen all four clusters before repairing setup. For the ten
real-runner cases, **60 s is a measured provisional whole-test floor candidate**
if the repaired cached path retains these lifecycle costs. It is not a 60-second
allowance for each Git child. Apply R5's provisional exception/collection plan
under CARD-1514/1404 before using it; keep fast scripted methods out of a Git floor.

**Root-cause conclusion:** a shared cold-start/fixture-concurrency problem explains
the family; choosing a bootstrap that performs the cold work once before the
timed bodies removes all 77 failures even on a more loaded host. Missing eager
runtime-model/provider initialization on cache hits is the smallest source-level
repair candidate. The control changes the whole bootstrap path, so it does not
uniquely isolate model construction from provider/JIT/DB-cache effects; no direct
runtime-model duration is fabricated. Code must instrument that phase and prove
the normal cached path green after the small repair. Clone contention and repeated
host setup are measured/inspected secondary costs, not 77 separate defects.

The configuration control is diagnostic evidence, **not a normal cached-path
checkpoint receipt or a product repair**. It does not turn the original Review
green, confer `reviewedSourceClean`, or discharge native/acceptance/PC obligations.
No further build or rerun of an unchanged green selection is required by this Debug.

## Code repair boundaries

1. **Shared cold setup first.** Instrument and eagerly initialize the real
   `AppDbContext` model once in the existing selected-store assembly warm-up,
   after readiness and before any tests begin. Use the same options as consumers;
   merely checking connectivity does not initialize the model. Retain the early
   returns for worker children, DB-free selections and deferred probes. Record
   initialization as fixture setup, separately from migrations and clone waits.
   Keep the image/cache optimization: permanently disabling caches or replaying
   migrations per case is not the repair. Confirm this hypothesis with a traced
   ordinary cached-path execution after the change.
2. **Clone/host burst next, if it remains.** Bound admission of these two rig
   consumers to the measured clone capacity; retain a separate isolated database
   for each case. Start with a measured small shared lane, rather than increasing
   clone width or pool size blindly. Split the retention and workspace-slot
   selections into serial rows if packing is amended, retaining every original
   method/parameter and PhoneHome/settings coverage. That separates fixture bursts
   without deleting coverage. An outer row marked Serial does not serialize its
   tests. The existing per-scenario host/provider setup should be addressed under
   CARD-1514/R3 with a shared host and explicit scenario-state/clock isolation;
   do not mix that larger refactor into a timeout-only attribute change.
3. **Ten real-runner cases only.** Audit the four workspace-slot and three
   retention transport methods and their full helper closure for the appropriate
   assembly-local spawn limiter. Bound and time the Git helper's start, process
   exit and both output drains under one measured operation budget. Preserve
   nonzero-exit and cancellation outcomes and restore/cleanup ownership. A helper
   with any non-Git child belongs on the strict lane. Do not put the 67 scripted
   cases on an invented Git floor.
4. **Budgets follow completed measurements.** Keep the present 35/45-second test
   limits during the first fixture repair. The timeout-censored 36/46-second
   failures are not successful p95 samples and do not justify doubling the limits.
   After shared warm-up and admission are fixed, collect at least three comparable
   loaded runs of each affected method, including cold-process samples and the
   two-rig cold/warm case. Record per-method p95 and propose approximately 2× it,
   with CARD-1404/1514 provenance for provisional samples. The green control above
   supplies a provisional 60-second whole-test floor candidate for the seven
   real-runner methods (ten cases), conditional on measured costs after setup is
   repaired. It establishes no per-Git-command floor and no 90/180-second floor.

No retry, assertion weakening, skip, fake product bypass or new production timeout
is called for. Repair test infrastructure, then execute the exact required CP-6
selection once at the repaired committed source. Preserve the old RED receipt.
The historical row has 128 cases; master has subsequently added retention secret
preservation witnesses, so derive the final expected roster from the chosen Code
base instead of copying the old count onto a different source tree. Do not rerun
CP-1..CP-5/CP-7/CP-8 solely to repay unchanged green methods for this diagnosis.

## Exact affected methods

Counts include parameterized executions. Prefix each entry with the class in its
heading; the retained CSV also preserves every parameter display and full identity.

`C0824WorkspaceSlotServerTests`, scripted (15 cases):

| Method | Cases |
|---|---:|
| `C0824_ColdWorkspacePersistsAndLeaseCarriesCold` | 1 |
| `C0824_FlagOffMirrorRequestUnchanged` | 2 |
| `C0824_LegacyRemovalTreatsSlotManagedAsRemoved` | 2 |
| `C0824_ReconcilerBoundsSlotUnknownRetries` | 1 |
| `C0824_ReleasedTaskCoordinatesNotAmbiguous` | 1 |
| `C0824_ReleaseReconcilerRequiresAllConditions` | 6 |
| `C0824_SlotUnavailableHoldsWithoutFallback` | 2 |

`C0824WorkspaceSlotServerTests`, real runner/Git (6 cases):

| Method | Cases |
|---|---:|
| `C0824_BusyRunnerLeaseSerialisedNotDuplicated` | 1 |
| `C0824_LeaseRecoveryAtEachHandoff` | 2 |
| `C0824_LeaseRoundTripRealRunner` | 1 |
| `C0824_ReleaseRoundTripRealRunner` | 2 |

`C1677RetentionServerTests`, scripted (52 cases):

| Method | Cases |
|---|---:|
| `Changed_task_facts_resend_a_backed_off_tree_at_once` | 1 |
| `Control_character_inventory_names_are_reported_not_sent` | 1 |
| `Dry_run_census_covers_the_whole_population` | 1 |
| `Each_ineligible_task_alone_is_recorded_and_never_sent` | 27 |
| `Execute_off_sends_dry_run_only` | 1 |
| `Execute_requests_are_bounded_per_tick` | 1 |
| `Kept_tree_backoff_doubles_per_repeat_of_the_same_reason` | 1 |
| `Kept_tree_backoff_is_capped_at_kept_recheck_max_hours` | 1 |
| `Kept_tree_backs_off_until_its_reason_changes` | 1 |
| `Kept_trees_do_not_starve_newer_removable_ones` | 1 |
| `Known_leaf_with_a_trailing_space_is_reported_not_skipped` | 1 |
| `Outcome_recording` | 4 |
| `Preview_is_dry_run_even_when_execute_is_on` | 1 |
| `Reopened_task_is_rechecked_before_its_turn` | 1 |
| `Requests_are_paced` | 1 |
| `Second_request_waits_for_the_first_reply` | 1 |
| `Switching_off_stops_a_running_tick` | 2 |
| `Terminal_statuses_at_the_age_floor_are_sent` | 3 |
| `Tick_stops_starting_requests_past_its_budget` | 1 |
| `Unmatched_inventory_names_are_reported_not_sent` | 1 |

`C1677RetentionServerTests`, real runner/Git (4 cases):

| Method | Cases |
|---|---:|
| `Busy_runner_retain_is_serialised` | 1 |
| `Retain_recovery_at_each_handoff` | 2 |
| `Retain_round_trip_real_runner` | 1 |

## Evidence, reproduction and custody

External evidence: `/tmp/cdf5415a-evidence`. Preserve it before retiring the mirror.
It contains the original CP-6/base TRX and identity matrices, decoded original
HTML spans, the three fresh log/phase/trace sets and byte-verified
`fresh-test-results.tar` containing their original TRX/HTML outputs, exact filters,
`identity-comparison.json`, `fresh-run-summary.csv`, source-archive digests and
before/after source-byte verification. The baseline's 9,831 and master's 9,874
committed paths were verified against Git blob identities; the final checks found
zero mismatches. The immutable archives intentionally had no `.git` directory;
build version-stamp diagnostics from that condition are retained, and no
checkpoint source-clean receipt is claimed from those archive builds.

Existing consumer guards for the proposed warm-up live in
`TestDbFixtureLazyInitializationTests` and `TestDbFixtureLifecycleTests`. Preserve
the worker/probe/DB-free cold paths and test the once-only warm-up contract when
implementing the infrastructure repair.

To repeat the exact failure selection in a compiled, committed checkout, use a
fresh results directory and the retained filter (all class operands have `*`).
Run one driver through one lease, preserving its exit code. For example, in PowerShell:

```powershell
$selection = (Get-Content -LiteralPath '/tmp/cdf5415a-evidence/master-default-filter.txt' -Raw).Trim()
$env:TEMP = '/tmp'
pwsh -NoProfile -File scripts/build-slot.ps1 -Label s4a-77-repro -SlotWaitMinutes 12 -- timeout --kill-after=15s 4m dotnet run --project tests/Antiphon.Tests --no-build -p:UseAppHost=false -- --treenode-filter $selection --report-trx --results-directory /tmp/s4a-77-fresh-results
```

The actual diagnostic launchers retain a 4m execution bound (plus 15s kill grace),
12m admission bounds and 20m archive-build execution bounds. The initial native
compile omitted a separate execution timeout; it completed in 272 s and that
deviation is retained in `native-build-summary.txt`. No test timeout was changed.
Phase data does not separately identify runtime-model initialization, per-Git-call
work, all host/provider startup or all teardown work; those remain unknown or
unattributed remainder, rather than zero. The complete trace, rather than HTML's
bounded external-span collection, supplies the 33.639-second migration timing.

All owned build/test drivers were joined. Cleanup removes the 72 exact literal
native `bin`/`obj` roots, the two task-created source/build archive trees, and
the three original result directories after verifying the retained result archive;
diagnostic receipts remain only in the external evidence root. No source mutant
was made or restored. The sole task-branch commit is this requested investigation.

--- next stage ---
next: code
handoff: Repair once-per-run cached-path runtime-model/provider warm-up for all 77 cases; prove the normal cached selection green. Address remaining clone/host admission cost if measured, and the ten real-runner cases' Git limiter/deadline gaps. Use the provisional 60 s transport whole-test reference only after the fixture repair, with CARD-1514/1404 measurement provenance. Preserve every assertion and the original RED verification history.
artifact: docs/investigations/2026-11-05-debug-s4a-77-timeouts.md
