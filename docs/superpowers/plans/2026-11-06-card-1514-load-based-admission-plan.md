# CARD-1514: host load history and load-based admission

Status: Plan complete; separate TestDesign required, starting with **S1 telemetry + script**.
This is the load-admission workstream of CARD-1514, not a replacement for its slow-fixture plan.
Assigned task: `a653d3f6-23cc-4234-b07c-0342fb9fa768`.
Source base: `5abd946dd2c235b6e873b0cbb95c738f35216dcf`.

The operator's explicit decision, 2026-11-06, is:

> Let's remove these guards, and only while monitoring the task, before kicking a task off we check the cpu load, and also monitor load so we can start storing averages and min max etc so we can better plan how many slots to fill.

Implement durable observation first. Then replace FIFO build-slot admission with a fresh CPU
check and one generous, atomic host-wide heavy-command ceiling. Check load before task dispatch
as well. Keep the lease implementation as an explicit rollback mode. Do not enable the new mode
by merely deleting instructions or setting `BuildSlots:Enabled=false`.

This dispatch changes documents only: no builds, tests, deployment, settings writes, desktop
commands, or agent dispatch. Engineering defaults below permit S1 to proceed; later canaries
must supply the measurements specified here before changing the shipping default.

## Evidence and ground truth

Read owners: [project context](../../project-context.md), [HTTP operations](../../ops-http.md),
[testing/build](../../testing-and-build.md#build-slots-card-0589),
[performance standards](../../test-performance-standards.md),
[orchestration](../../orchestration-loop.md), and
[instruction-file contract](../../agent-instruction-file-contract.md).

The brief's `docs/investigations/2026-11-05-debug-s5b-1-performance.md` is absent from the base
and fetched `origin/master`. It was read in full with `git show` at
`c318d8edfc97e046bb9a332ec34c76973f9a4f4a`. Its source task was CARD-0824 Code `f8ac5aa5`;
it is historical measurement, not verification of this change. The run
`20261105-130833-11b7`, source `7973604a2908febc17d80220937f9232da4b2054`, took 1,849.2 s:
840.2 s admission, 817.1 s build, 124.9 s row hosts, and about 67 s remainder. Across 39
acquisitions p95 wait was 255 s and maximum 405 s. Across 11 successful cold builds p95
was 817.1 s (small sample, provisional), median 431.6 s. This supports replacing queueing;
it does not establish an optimal CPU threshold.

The [2026-10-22 investigation](../../investigations/2026-10-22-server2-build-slot-throughput.md)
measured CPU average 70.1%, p95 96.9%, maximum 99.8% over 360 five-second samples; queued
intervals ranged from 31.6% to 93.7% CPU in a small direct sample. It also identified the
local-versus-shared-broker occupancy error. The
[2026-10-23 investigation](../../investigations/2026-10-23-build-slot-starvation-debug.md)
distinguishes admission, execution and hard-wall budgets. Preserve that distinction.

Read-only API observations on 2026-11-06, approximately 18:58–19:10 UTC:

| Observation | Result and limitation |
|---|---|
| `GET /api/runner-defaults` | Revision 2; configured global default resolved to the Linux runner. This is observation, not a placement constant for scripts or bundles. |
| `GET /api/session-runners` | An eligible Windows runner and an eligible Linux runner; a third configured runner retired. Linux catalogue omitted `hostStatsV1` although stats reads worked. Capability discovery and real route support must be reconciled before enforcement. |
| `GET /api/hosts/stats`, sample 18:58:12 UTC | Linux: 24 cores, CPU 43.545%, load1 26.58, one-minute CPU avg 62.398%, 30-minute avg 48.900%. Windows: 8 cores, CPU 84.102%, one-minute avg 86.198%, 30-minute avg 84.406%, peaks 100%. These are single snapshots and existing avg/max rollups, not new p95 measurements. |
| Shared broker, through inherited `ANTIPHON_BUILD_SLOTS_URL` | Enabled, budget 4, maxcpucount 6, occupied 4, no waiters, 96,401 MiB available. `/metrics` returned 405. Its deployed version must be upgraded independently of the session runner. |
| `/api/hosts`, scoped pipeline | Task-seat budgets and in-flight counts, not CPU admission. Project pipeline later showed seven open tasks and five project slots remaining; host remaining was three. Counts changed during observation. |
| `/api/session-runners/{runnerId}/status` | Linux status eligible, build `919d8d42f14d1b11ae5dcba8dafd1e1b2a8d90cd`. Desktop status projection did not agree with catalogue availability; do not infer desktop eligibility from this phone-home view alone. |

No persistent URLs, machine paths or runner names from those readings become defaults in new
code. Before every dispatch read the live runner defaults, catalogue and project-scoped pipeline.
Normally omit `-Runner` and `-Platform`; use a platform pin only for the native witness lane.
`-Platform Any` removes an inherited OS pin. No work in this Plan runs on the desktop.

| Card assumption | Code actually does at the base | Design consequence |
|---|---|---|
| Host load needs a new sampler | `HostStatsSamplerService`, `LinuxHostStatsProbe`, `WindowsHostStatsProbe` already sample every 5 s. `HostStatsStore` keeps 360 samples / 30 min in memory. | Extend these seams; no second general-purpose monitor. |
| `/api/hosts` contains load history | It contains host task budgets. `/api/hosts/stats` contains live samples; `/stats/series` supports only 1/5/15/30 min. | Add a history/load API; preserve budget and stats route meanings. |
| Existing rollups include min/p95/hour | `RunnerHostRollupDto` contains only avg/max. Server cache holds latest snapshots; no sample table exists. | Persist samples and explicit coverage, min and percentile metadata. |
| CPU and load are interchangeable | Linux CPU is busy jiffy delta, with idle+iowait excluded; load includes runnable/blocked work. Windows load is null. `Environment.ProcessorCount` may describe a constrained process rather than the whole host. | Gate on CPU utilization; report load/core and metric scope separately. Validate the denominator. |
| Slot occupancy on Hosts is authoritative | `HostStatsStore.ReadBuildSlots()` reads its injected local broker. Compose clients use a separate shared broker. | Resolve the actual admission authority and persist that authority's count. Never use the local zero as a substitute. |
| One runner equals one physical host | Rolling deployment can run two runners on one host; `BuildSlotsOnly` is a separate service and currently does not start HostStats. | Share one heavy-command count across those runners; add stable physical-host identity and collector provenance. |
| Every lease refusal blocks execution | PowerShell permits unleased execution after 60 s unreachable; the checkpoint client has stricter malformed/HTTP-error handling; land has unreachable and optional memory-floor fallbacks. | Keep legacy behavior only in Lease mode. Load mode cannot fail open around its hard ceiling. |
| A lease covers a task | Checkpoint tool takes separate grants for builds and rows. Script holds a grant for one row; land holds one through build/test. | Replace each consumer's admission path deliberately; do not hold a task-long reservation. |
| A live holder always retains a lease | Base broker's TTL is 90 min; pid mode retains a matching live process past TTL, but `LeaseMaxHoldHours=48` reaps even live holders. Shared broker uses renew mode. | Those lease aging rules remain legacy-only. A hard count must not decrement merely because a still-running command is old or silent. |
| Concurrency settings are being abolished | `HostBudgetService` and dispatcher enforce task seats, scopes, provider and platform eligibility. | Load is an additional dispatch condition. No host-budget writes, cap increases or provider reroutes are implied. |

## Decisions

- **D-1 — Extend the existing monitor.** Keep 5-second runner sampling and existing live routes.
  Add durable history and provenance separately. Reject a shell polling loop per delegate:
  it duplicates work and loses history when the delegate exits.
- **D-2 — Store durable history in PostgreSQL; buffer on the runner.** Use the existing server
  database and a bounded runner JSONL spool. Reject only enlarging the memory ring (restart
  loss) and introducing a separate time-series service (deployment and custody cost).
- **D-3 — Use CPU utilization for the first admission policy.** Command pressure is
  `max(latestCpuPercent, avgCpuPercentOver15s)`; dispatch pressure is
  `max(latestCpuPercent, avgCpuPercentOver60s)`. Reject gating on load1/core: the observed
  26.58/24 load beside 43.545% CPU demonstrates that it would block some usable CPU intervals.
  Preserve load1/5/15 and normalized load for calibration, not as a second hard gate.
- **D-4 — Initial thresholds are 85% for commands and 75% for task dispatch.** These leave
  15 and 25 percentage points below saturation. The 70.1% historical average and 96.9% p95
  bracket the command threshold; the lower task threshold reserves startup/build headroom.
  The Windows 84–86% readings would defer new tasks, as intended. These are conservative
  measurement-informed defaults, not empirically optimal thresholds. S1 shadow observations
  and S2 canaries must validate them independently on each OS.
- **D-5 — One hard heavy-command ceiling, no FIFO queue in Load mode.** Default
  `max(2, min(16, ceil(hostLogicalCores / 2)))`, overridable as a positive integer. On the
  observed hosts this is 12 and 4, versus legacy 4 and 2. The 24-core host's four six-node
  grants can already saturate CPU, so the new count is a runaway backstop, not a throughput
  target. Reject simply raising the legacy four-slot queue and reject unlimited starts.
  Retain compiler node hints and process-custody rules; the ceiling bounds admitted heavy
  invocations, not arbitrary programs or every compiler thread on the machine.
- **D-6 — Keep `scripts/build-slot.ps1` as a compatibility entry point.** Change the policy
  behind it; do not require every caller to rename its command. In Load mode a small active
  execution registration remains necessary to enforce the hard count atomically. It has no
  FIFO position or lease scheduling TTL. Reject a read-then-spawn shell-only counter race.
- **D-7 — Fail closed on unknown admission evidence in Load mode.** Missing, malformed,
  stale, wrong-host, unavailable or recovering authority cannot grant. Preserve an explicit
  `Mode=Lease` rollback; never silently switch modes on network failure. A history database
  outage alone does not prevent a fresh local command-load check.
- **D-8 — Enforce dispatch centrally, and expose it to the orchestrator.** Add a server
  dispatch decision after placement, before claim/preparation/launch, with `delegate.ps1`
  preflight for an early explanation. Reject script-only enforcement because API/scheduled
  dispatch would bypass it. Do not wait while holding a repository mutation lease.
- **D-9 — Recommendations are advisory and conservative.** Start at 10 CPU percentage
  points per additional task, clamp by live effective task/stage/host capacity, and return
  zero for unknown load. Never modify budgets or automatically select another runner/provider.
  Per-task host context is correlation, not exclusive CPU ownership.
- **D-10 — Stage the change.** S1 is independently deployable, telemetry only. S2 ships
  disabled behind `Mode=Lease`; S3 updates instructions to describe both modes. Switch the
  default to Load only in a separate, reviewed promotion after canary evidence. No approval
  question is needed to implement the operator's stated direction.
- **D-11 — Keep verification separate.** This document supplies numbered obligations,
  candidate controls and a closed checkpoint selection for TestDesign to bind. It does not
  claim an executable final verification design, measured new-test counts, or passing tests.

## S1 telemetry model and APIs

### Sampling, authority and metric semantics

Use one `HostStatsSamplerService` per full runner, every 5 s, with `TimeProvider` for cadence
and monotonic elapsed time for deltas. The shared admission service uses the same host probe
for command admission when S2 is enabled; its admission snapshot is explicitly separate from
the designated durable history stream. Do not query PostgreSQL or enumerate all processes
inside an admission lock.

Each sample contains schema version, runner ID/store ID/boot ID, monotonic sequence, sampled UTC,
physical-host key, metric scope, physical logical-core count, optional effective/container core
limit, CPU utilization 0–100, busy-core equivalent, load1/5/15 and load/core, memory available/
used/total, swap/page-file figures with the existing OS meaning, configured volume free/total,
process count when available, and existing runner/session process CPU and working-set samples.
Disk means capacity, not a claim to measure disk throughput. Retain Linux iowait separately
if exposed while extracting CPU totals. Do not add a full `/proc` census.

Host aggregate `/proc/stat` must use the host CPU denominator, not a cgroup-limited
`Environment.ProcessorCount`. Windows uses the real host probe/factory. Unsupported OS,
first CPU delta, invalid/nonfinite value, counter reset or missing metric yields null with a
reason. Unsupported load on Windows is null, never zero. Process CPU is core-relative and can
exceed 100%; never feed it into the host-percent gate.

Add an external-I/O seam for admission-authority snapshots. When an endpoint override is
configured, read that authority's occupancy, ceiling/budget, waiters, mode, identity, observedAt
and revision using a pooled client. Otherwise read the in-process broker. A failed authority
read is unknown/stale, never local occupancy. Preserve legacy build-slot occupancy alongside
new active-command occupancy after S2. Cache for one sample interval; do not issue one HTTP
request per task.

The physical-host key is an explicit opaque deployment setting shared by colocated runners and
their admission service. The server canonicalizes the existing `local`/`desktop` aliases and
associates runner IDs with this key. Missing or conflicting binding is visible and prevents
Load-mode activation on that host. Store provenance for every collector, but select one
configured primary stream per physical host for rollups; on failover start a new source epoch.
Never sum duplicate host samples or heavy-command counts during a rolling overlap.

Server task counts come from one batched open-task query per poll, extending
`HostStatsAntiphonCounters`; no runner-side database access. Record counted-at separately from
sampled-at. Include in-flight tasks by stage/kind, pending launches/mirrors, queued/held,
live sessions and effective task capacity. Unknown counter read must stay unknown in history.
For replayed samples, join a retained counter observation only within 15 s of the sample;
otherwise counter fields are unknown. Do not attach today's count to yesterday's replay.

### Persistence, retention and aggregation

Proposed owned types/files are named in the slice table below. Add:

| Store | Identity, contents and retention |
|---|---|
| Runner spool | JSONL chunks under the configured runner state root, outside worktrees. Key `(runnerStoreId, bootId, sequence)`. Durable append and atomic cursor; 24 h or 256 MiB, whichever is first. Replay after restart; acknowledge only after server commit. Drop oldest on the explicit cap, record gap ranges and loss counters. Partial final line is quarantined, not accepted as a sample. |
| `HostLoadSamples` | Unique source identity above; index `(physicalHostKey, sampledAt)`; nullable numeric metrics, authority identity/counts, counter observation and coverage. Raw retention 7 days. Batches at most 120 samples / 1 MiB per runner request, idempotent insert and cursor commit in one transaction. |
| `HostLoadHourlyRollups` | `(physicalHostKey, sourceEpoch, hourUtc, metric)`, avg/min/max/p95, valid/expected count, coverage, sum, percentile representation/version, complete/partial. Retain 90 days; bounded hourly retention batches. Late replay corrects affected buckets idempotently. |
| `AgentTaskLoadWindows` | `(taskId, attempt, physicalHostKey, runnerBootId, windowOrdinal)`, queued/dispatch/start/end times, start/end sample identities and age, during statistics and overlap count; retain 90 days after terminal time. An open attempt is updated, never finalized merely because a poll was missed. |

Hourly and last-hour results use exact nearest-rank p95 from valid raw samples when raw data
exists (`ceil(.95*n)`, one-based). Average is the arithmetic mean of the fixed-cadence valid
observations, with explicit coverage; it is not a time-weighted estimate over gaps. An empty
metric window is null. Do not average hourly p95s to construct a longer-window percentile.

For long-running task windows keep mergeable bounded histograms, updated as samples arrive,
so raw retention does not erase the start of a task. Histogram bins: 0.1 percentage point for
CPU, 0.01 for raw/normalized load, 1 MiB for memory/disk, and integers for counts. Bound each
histogram to 4,096 bins; deterministically double its bin width and rebin on overflow. Store
bin width, count and version. Quantiles from these bins return the containing upper bound
with `p95Approximate=true` and absolute error bound equal to bin width; min/max/sum remain
exact observations. Raw-backed short windows report exact p95. Preserve this distinction in
JSON and script output. Tests must reject quantile merging that loses counts or claims exactness.

Update the per-task window on authoritative launch/terminal/retry/move events and reconcile
missed events from task state after restart. Freeze start/end observations within 15 s of the
event where possible, otherwise mark them missing/stale. A short task can have the same nearest
start/end sample but no during sample; do not invent a zero-duration average. Split retries and
host moves rather than blending them. Record ambient host load during overlap; optional known
session-process CPU is partial process attribution, not the CPU of all descendants.

Keep history writes off startup and admission critical paths. A bounded ingest worker exposes
lag, dropped samples, spool bytes, duplicate counts and write/rollup duration. Disk/DB failure
leaves live monitoring available and history visibly incomplete. No unbounded channel, per-task
timer, payload/command-line capture, or credential-bearing label goes into this store.

### Read surfaces and script

Keep `hostStatsV1`, `/api/hosts/stats` and its four existing series windows compatible. Add a
separately advertised `hostLoadHistoryV1`, with runner `/host-stats/history?after=&limit=` and
matching phone-home operation/client. Do not change existing operation ordinals. Capability
advertisement must agree on both direct HTTP and phone-home paths. Old peers remain live-only
or unsupported, never invented complete history.

Add server routes, using normal task/operator read authorization and typed errors:

- `GET /api/hosts/{hostId}/load`: current sample and age, last-hour avg/min/max/p95 plus
  count/coverage/approximation, effective command and dispatch policy/mode/revision,
  authority occupancy, decision/reasons and `recommendedAdditionalTasks`.
- `GET /api/hosts/{hostId}/load/history?from=&to=&resolution=raw|hour&limit=&cursor=`:
  bounded pages (default 720, max 2,000), maximum 7 raw days / 90 hourly days. Invalid windows
  are 400, unknown hosts 404, missing history is an explicit incomplete result.
- `GET /api/agent-tasks/{taskId}/load?attempt=`: all host windows of that attempt, with
  sample provenance and start/during/end summaries. Enforce the task's existing read scope.
- Add nullable load decision fields to pipeline `hosts` and runner status/catalogue projections;
  CPU eligibility does not overwrite connectivity, drain or existing `dispatchEligible` meanings.

`scripts/host-load.ps1 [-HostId <id>] [-Project <id>] [-Role <role>] -Json` uses inherited API
and task-token conventions, prints one JSON value to stdout, and resolves omitted placement
from the live defaults/catalogue. Diagnostics go to stderr. Human output names age, mode,
current load, last-hour avg/min/max/p95, unknown/partial coverage, ceilings and why it recommends
zero or N more tasks. A failed read exits nonzero and never suggests capacity.

Recommendation v1: let `P` be dispatch pressure and `C=10` percentage points per prospective
task. `cpuHeadroom=max(0,floor((75-P)/C))`; clamp by effective host remaining, runner seats and
the requested project/stage remaining. If project/role is omitted, return the host-only number
with `scope=hostOnly` and name the missing constraints. At or above threshold return zero.
Subtract CPU reservations for just-started tasks whose startup is not yet represented by
samples. Show all arithmetic inputs. S1 emits shadow decisions only. Later observed per-task
windows inform an operator-reviewed model; do not automatically learn causal per-task costs
from overlapping host load or automatically raise limits.

## S2 admission behind a flag

### Settings and state machine

| Owner / key | Initial default | Meaning |
|---|---|---|
| runner `SessionRunner:HeavyCommandAdmission:Mode` | `Lease` | Values `Lease`, `Observe`, `Load`; Observe uses legacy admission and records shadow load decisions. |
| same, `CpuThresholdPercent` | `85` | Strictly below admits; equality defers. |
| same, `CpuWindowSeconds`, `MaxSampleAgeSeconds` | `15`, `15` | At least three valid five-second observations; stale/future/skewed data refuses. |
| same, `MaxConcurrentHeavyCommands` | derived by D-5 | One aggregate ceiling per physical host, including transitional legacy grants and uncertain active registrations. |
| same, `WaitTimeoutSeconds` | `600` | Total command-admission wall deadline, including HTTP, backoff and authority recovery; caller may shorten it. |
| same, `RetryInitialMs`, `RetryMaxMs`, `RetryJitterPercent` | `1000`, `15000`, `20` | Bounded exponential backoff, reset on changed decision/sample; never exceed remaining deadline. |
| server `HostLoadAdmission:Enabled` | `false` | Central task-dispatch enforcement; S1 shadow decisions are independent. |
| same, `CpuThresholdPercent`, `CpuWindowSeconds`, `MaxSampleAgeSeconds` | `75`, `60`, `15` | Require at least 10/12 valid expected observations and a fresh current sample. |
| same, `TaskCpuReservationPercent`, `StartupObservationSeconds` | `10`, `15` | Prevent a burst of launches against a single low-load observation. |
| server `HostLoadHistory` | enabled; raw 7 d, hourly/task 90 d | Typed settings for ingest/retention bounds; spool settings belong to runner HostStats. |

Validate finite percentages in `(0,100]`, positive finite deadlines, internally consistent
windows and count bounds at startup. Settings are typed options; expose the effective values
and revision in decisions. Live overrides, if implemented, use the existing operator-authenticated
settings pattern, not a delegate-writable environment bypass. The shipping default stays Lease
until promotion. The 600-second admission budget is a policy cap, informed by 2 × historical
255-second p95 rounded up for HTTP/backoff; it is not a claimed p95 of the new policy.

One authority evaluates the current validated sample and active count under an atomic admission
operation. Below threshold with spare hard capacity, start immediately: no FIFO position, no
waiter queue, no polling delay. Otherwise return structured `cpu_high`, `heavy_ceiling`,
`sample_stale`, `sample_unknown`, `authority_unavailable` or `authority_recovering`, plus observed
CPU/window/age, occupancy/ceiling, policy revision and retry delay. Requests use an execution
UUID, runner identity and process birth identity; response loss/retry returns the same registration.
Two different commands in one wrapper process must not collapse to the same registration.

The wrapper waits with bounded backoff and logs `HOST LOAD waiting ...` on the first refusal,
reason/sample change and at least every 30 s. Print the load and threshold seen, not just “busy”.
At 600 s or a shorter caller deadline, exit 4 and launch nothing; cancellation also launches
nothing and cleans up an acquired-but-not-started registration. Execution timeouts start only
after admission, and existing independent hard-wall/caller cancellation still applies.

The legacy memory-floor admission condition is inactive in Load mode; memory and disk remain
visible telemetry. This is the operator's requested CPU policy plus one heavy-command ceiling,
not the legacy queue with an additional CPU gate. Independent process custody, workspace/disk
integrity and task-seat limits retain their existing purposes.

After a command starts, load spikes do not kill or pause it. Keep one count until its owned
process tree has exited or custody confirms containment empty. Normal/failing/canceled command
paths release after child exit/output drain; crash recovery uses process birth/generation identity.
Remote renewals are liveness hints, not permission to decrement the hard count after silence.
Unknown liveness becomes counted `uncertain` capacity and a diagnostic. Never reap an active
registration because 48 hours elapsed. No arbitrary PID kill is introduced.

Persist the minimal active-execution registry on the authority's state volume. On restart,
recover registrations and reconcile with each owning runner's bounded execution inventory before
admitting new work. A reservation never handed to a process can expire only with a fenced,
provable never-started identity. An authority unable to prove recovery is complete remains
recovering; deadline yields exit 4. Tests must exercise overlapping runners, authority restart,
PID reuse, owner loss, lost replies and response replay. This accounting is the price of a real
hard ceiling; an in-memory counter or an expiring live permit cannot satisfy it.

### Compatibility and consumer changes

Keep `/build-slots` and its existing request/response members as the legacy adapter. In Load
mode it delegates to the same active registry/load decision, mapping pressure refusal to the
existing busy shape with additive reason/load fields; it must not allocate a second budget.
New clients negotiate `heavyCommandAdmissionV1` and receive explicit mode. Existing clients
can display the legacy shape, but old unreachable fail-open clients are not eligible for a Load
canary: upgrade/drain them first. `BuildSlots:Enabled=false`, `-NoSlot`, checkpoint `slots=off`
and memory-floor fallback do not bypass Load mode. Keep their documented legacy/operator
behavior in Lease mode only. Mode negotiation failure under configured Load is a refusal.
Runner launch configuration supplies clients with the expected mode and policy revision, and
the server land adapter has the corresponding typed setting. A missing/mismatched policy stamp
is unknown, not evidence of Lease mode. Only an explicit Lease configuration permits the legacy
unreachable fallback. Refresh/drain existing sessions before a host switches mode so an outage
cannot leave a client guessing which policy applies.

The shared broker must use a durable state volume and actual host CPU sampling even in
`BuildSlotsOnly` mode. Both old/new runner containers resolve the same authority; changing only
the session-runner Compose stanza is insufficient. An invalid/missing shared authority must
not fall back to an independent local counter.

Retain `MaxCpuCount` as the compiler-parallelism hint (existing per-host values until measured
separately), `-nodeReuse:false`, Windows build-driver environment, literal argument transport,
working-directory semantics and native incremental outputs. `dotnet run` still must not receive
MSBuild `-maxcpucount` as a program argument. A non-dotnet wrapper still cannot enforce the
compiler hint for its descendants; say so. A loop requests admission for each heavy invocation.

The checkpoint tool and script need explicit `admissionMode`, authority/sample identity,
decision reason, threshold, count and admission duration fields. Keep historical `slot` and
`waited` values readable; never rewrite old receipts as load-admitted. Add a versioned admission
envelope, including for timeout/nonexecuted rows. Both strict validators verify new required
fields when that envelope declares Load; old versions retain their historical qualification
rules. Do not make `slot=unleased` mean a verified load decision. Preserve source SHA/build
provenance, counts, cancellation and cleanup contracts.

Land's `SessionRunnerBuildSlotGate` adopts the same mode with no Load-mode unleased or
memory-floor escape. Keep legacy settings/metrics for rollback, naming the mode in land status.
Preserve repository-lock release before waiting; do not redesign the land/dispatch protocol.

## S3 dispatch, instructions and rollout

After runner selection, the server evaluates dispatch pressure from the physical host's fresh
cache before workspace preparation, repository claim or session launch. Repeat the check at
the actual launch boundary if preparation outlived the sample. A refusal leaves the task queued
with a typed `hostLoad` hold, reason/observations and next-check time; the dispatcher revisits on
its existing ticks. Never sleep while holding a repository lease or reserve a session seat for
a load wait. Deferred state is not Failed/Blocked and does not send agent input.

Serialize host admission decisions with the existing claim/concurrency transaction mechanism.
Each accepted task adds a 10-point startup reservation. Keep it until at least 15 s of fresh
post-start observations exist; if startup fails, remove it transactionally. If the host stops
reporting, stale admission blocks further starts and the reservation stays visible. Reconcile
reservations from persisted pending/started task identities on server restart. Parallel callers
must not all receive the same “one more task” headroom. A script preflight is advisory; the
server decision is authoritative. Existing drain/platform/provider/scoped/host budget checks
still apply, and the recommendation cannot override them.

`delegate.ps1` reads the resolved candidate's load and reports a defer before requesting new
work when pressure is already high. Do not bake in the observed runner names. API callers that
race this preflight can create a queued task, but cannot launch it through the central check.
Surface decisions in pipeline hosts and the generated orchestrator instructions; monitor task
windows during execution and report load at start/end. Do not auto-kill working sessions or
automatically increase fleet caps.

This introduces no session waiting for user input. Command waits end at their admission
deadline or caller cancellation. A task deferred before launch has no new process to release.
Existing input-waiting sessions retain their seat with parking disabled; there is no automatic
release deadline (CARD-1083), and load pressure adds none.

### Migration and staged activation

1. **S1 alone:** generate EF migration via the repo CLI in the later Code task; add tables/indexes
   without altering task/session history. Deploy compatible collectors/server readers/spool state
   mounts. Read the real deployed SHA and feature/identity contract, not only health. All admission
   remains legacy. Record at least 24 h per OS and 30 completed task windows when workload permits;
   report smaller populations as provisional rather than manufacturing extra task spend.
2. **S2 dormant:** deploy authority and all clients with Mode=Lease; exercise Observe mode using
   real normal work. Compare shadow CPU decisions, command waits, occupancy, failure rates and
   cold build durations. Verify the shared broker itself was updated; its current `/metrics` 405
   demonstrates that runner rollout alone is insufficient. No desktop experiment in this dispatch.
3. **S3 contract:** land mode-aware instructions and central dispatch setting, still disabled.
   Update generated instruction inputs and let normal refresh deliver them; never edit the generated
   live instruction file. Coordinate overlaps below before touching shared files.
4. **Canary:** choose one eligible physical host from live placement, within existing authorized
   deployment work. Enable Load for that host and dispatch enforcement for its bound host identity.
   The shared authority changes mode only after old clients are drained/upgraded and its current
   registry is reconciled; do not reset counts on a mode switch. Start with existing host task caps.
   Observe 2 h and at least 20 ordinary command completions, plus an idle/busy interval and a tested
   restart/rollback in an isolated fixture. Extend observation, not artificial workload, if insufficient.
5. **Promotion:** require complete ordinary OS evidence, no count overshoot/custody loss, no unknown
   grant, bounded waits, correct task windows, sampler overhead within budget, and comparable
   load-stratified command/build p95 no worse than 20% above baseline. Admission wait should fall;
   a worse throughput/latency result holds promotion for measurement. Then promote default Load in
   a separate reviewed configuration/default change. Windows activation waits for Windows evidence;
   Linux green alone cannot enable it.
6. **Rollback:** stop new Load admissions, retain running registrations and telemetry, and wait for
   proven drain (or use existing counted transitional occupancy), then set Mode=Lease and disable
   central CPU enforcement for that host. Restore its saved legacy settings; never use unlimited
   as rollback. Do not delete history, reset counts, kill sessions, or downgrade to a binary that
   cannot read the active registry. Verify effective mode/revision and one subsequent ordinary
   decision. Default promotion and rollback are operational follow-up, not this Plan's mutation.

### Implementation slices

1. **S1 telemetry + script:** commit capture/authority identity/spooling and its tests, then
   server persistence/rollups/task windows/read surfaces/script and tests. Deployable alone;
   no checkpoint-validator or dispatch-policy mutation. Close CP-1 through CP-4 as their lanes
   become available. Land/review this slice before commissioning S2.
2. **S2 admission behind a flag:** commit authority/registry, then client/receipt adapters and
   legacy compatibility; retain Lease default. Coordinate CARD-1217 first for receipt changes.
   Close CP-5 through CP-10, with separate runs as below. Do not enable live Load mode merely
   because a code slice passes.
3. **S3 docs/AGENTS.md + orchestrator rule:** commit central dispatch/preflight and reservation
   accounting after CARD-1581 coordination, then the matching mechanical instruction changes.
   Close CP-11/CP-12. Follow with operational canary and separate default-promotion change.

Tests precede fixes in truthful commits; every meaningful slice is pushed. No slice relies on
an uncommitted instruction change to authorize its own execution.

### File and test impact inventory

Paths below are exact existing paths unless marked **new**. Code must recheck the call graph at
its frozen base; a new consumer requires a TestDesign amendment, not an undocumented extra run.

| Slice / area | Production, docs and configuration files | Tests / reason |
|---|---|---|
| S1 runner capture | `src/Antiphon.SessionRunner/{HostStatsSettings,HostStatsSamplerService,HostStatsStore,HostStatsProbe,LinuxHostStatsProbe,WindowsHostStatsProbe,HostStatsRoutes,Program,PhoneHomeCommandDispatcher}.cs`; **new** `HostLoadSpool.cs`, `HostAdmissionSnapshotClient.cs` | Existing `HostStatsSamplerTests`, `HostStatsStoreTests`, `HostStatsProbeParseTests`, runner `HostStatsEndpointTests`; new `C1514HostLoadSampleTests`, `C1514HostLoadSpoolTests`, `C1514HostLoadNativeTests`. |
| S1 wire/transport | `src/Antiphon.SessionRunner.Contracts/{SessionRunnerContracts,PhoneHomeContracts}.cs`; `server/Application/Interfaces/ISessionRunnerClient.cs`; `server/Infrastructure/Agents/SessionRunner/{SessionRunnerHttpClient,PhoneHomeRunnerClient}.cs` | Existing `SessionRunnerHttpClientHostStatsTests`; new direct/phone-home history compatibility cases in `C1514HostLoadHistoryTests`. |
| S1 storage/API | `server/Application/{Settings/HostStatsSettings.cs,Dtos/HostStatsDtos.cs,Services/HostStatsPollService.cs,Services/HostStatsCache.cs,Services/HostStatsAntiphonCounters.cs}`; `server/Api/Endpoints/HostStatsEndpoints.cs`; `server/Infrastructure/Data/AppDbContext.cs`; `server/Program.cs`; **new** Domain entities, Application `HostLoadHistoryService`, `HostLoadRecommendation`, typed settings and DTOs; generated `server/Migrations/*` | Server `HostStatsCacheTests`, `HostStatsPollServiceTests`, `HostStatsAntiphonCountersTests`, `HostStatsEndpointTests`; new `C1514HostLoadHistoryTests`, `C1514TaskLoadWindowTests`, `C1514HostLoadRecommendationTests`. Keep I/O implementations in Infrastructure. |
| S1 operator script | **new** `scripts/host-load.ps1`, offline fixture/harness and `tests/Antiphon.Tests/Scripts/C1514HostLoadScriptTests.cs`; `docs/ops-http.md`, `docs/antiphon-api.md` | JSON output, authentication forwarding without logging, missing/stale host, default resolution, exact recommendation and pagination. |
| S2 authority | Existing `BuildSlot{Settings,Broker,Routes,MetricsRecorder}.cs`, `Program.cs`, Contracts `BuildSlotContracts.cs`; **new** `HeavyCommandAdmissionSettings.cs`, `HeavyCommandAdmission.cs`, `HeavyCommandRegistry.cs` and execution-inventory seam | Existing `BuildSlotBrokerTests`, `BuildSlotSettingsTests`, `BuildSlotEndpointTests`, `BuildSlotMaxHoldTests`, `BuildSlotMetricsTests`; new `C1514HeavyCommandAdmissionTests`, `C1514HeavyCommandRecoveryTests`. Old max-hold behavior stays tested in Lease mode. |
| S2 PowerShell | `scripts/build-slot.ps1`, `scripts/lib/build-slot.ps1`, `scripts/run-checkpoint.ps1`, `scripts/test-build-slot.ps1`, `scripts/test-run-checkpoint.ps1`, `scripts/fixtures/c589-{slot,command}-shim.ps1` | `BuildSlotScriptTests`, `RunCheckpointScriptTests`, `RunCheckpointSourceScriptTests`; new `C1514HeavyCommandScriptTests`, `C1514HeavyCommandNativeTests`. Preserve argv, exit, cancellation and ASCII contracts. |
| S2 checkpoint tool | `tools/Antiphon.Checkpoints/Slots/{BuildSlotClient,LeaseHolder,FixedSlotClient}.cs`, `Execution/{RunScheduler,RunBudgetClock,RowRunner}.cs`, `CheckpointApp.cs`, `Report/{ReportModel,ReportWriter,ReportValidator,CheckpointLine,PhaseTimings}.cs`; `scripts/validate-checkpoint-receipt.ps1` | `BuildSlotClientTests`, `CheckpointSlotContractTests`, `CheckpointSlotExecutorTests`, `CheckpointAdmissionBudgetTests`, `CheckpointAdmissionContractTests`; new `C1514CheckpointLoadAdmissionTests`. Preserve baseline-runner mode and source/confinement accounting. |
| S2 land | `server/Infrastructure/Agents/SessionRunner/SessionRunnerBuildSlotGate.cs`, `server/Application/Interfaces/IBuildSlotGate.cs`, `Dtos/RunnerBuildSlotAnswer.cs`, `Settings/LandingSettings.cs`, the HTTP runner client, `Services/AgentTaskLandService.cs` and `AgentTaskLandingProtocol.cs` where new evidence is projected | `LandVerificationBuildSlotTests`, existing stage/source-freshness tests if their status projection changes; new load-mode cases in `C1514LandLoadAdmissionTests`. No repository-lock protocol change. |
| S2 indirect callers/config | `scripts/{apphost-common,run-daemon,start-windows-phone-home-runner,audit-land-verification-graph,lint-client,typecheck-client}.ps1`; `scripts/c590-remote.sh`, `docker-compose.server2-runner.yml`, runner/server appsettings and bootstrap environment propagation | Keep callers on the entry point, review comments/expected modes. `BuildDriverEnvironment` and `Directory.Build.rsp` keep their behavior. `test-cache-build-slot-inputs.ps1` must hash new admission inputs. Inspect `test-build-driver-environment.ps1`, `test-isolated-helper-build.ps1`, `test-fake-provider-layout.ps1`, client-gate harnesses and rolling stack contract fixtures for mode-specific assumptions. Do not edit their behavior without a named obligation. |
| S2 qualification readers | `scripts/qualification/{launch-rc,observe}.ps1`, nightly/watched-driver consumers and `tools/Antiphon.Checkpoints/Baseline/BaselineComparer.cs` | Preserve each driver's actual admission status. Nightly wrapper is documented not to take a slot; do not claim universal coverage or fabricate a Load receipt for it. Verify any changed reader with a named fixture; retain unsupported consumer as a canary blocker until upgraded. |
| S3 dispatch | `scripts/delegate.ps1`; `server/Application/Services/{AgentTaskDispatcher,AgentTaskPipelineStatusService}.cs`; new `HostLoadDispatchAdmission.cs`, settings and hold DTOs; existing generated-orchestrator-instruction producer | `HostBudgetAdmissionTests.cs` is a partial of **`DispatchHoldVisibilityTests`**; `HostBudgetPipelineTests.cs` is a partial of **`AgentTaskPipelineStatusTests`**. Select their `C654*` methods, plus new `C1514HostLoadDispatchTests`, `C1514DelegateLoadPreflightTests` methods named `C1514_*`. Cover both placements, retries, concurrent claims and no lease held while deferred. |
| S3 standing contracts | `AGENTS.md`; `docs/{testing-and-build,orchestration-loop,ops-http,antiphon-api,docker-stack,mechanical-pc-contract}.md`; `server/Bundles/{delegate-basics,stage-code,stage-review,stage-mutation,orchestrator}.md`; relevant existing delegation skill instructions if they repeat the old rule | `InstructionBundleTests.C589_V6_BuildSlotGateIsAStandingRuleReviewEnforces`, `TaskPlatformGuidanceTests` (including verbatim base Review instruction), `MechanicalPcContractTests`, `CheckpointManifestDocumentationTests`, `StableBuildOutputDocumentationTests`, `RunnerDefaultGuidanceTests`. Update mechanical assertions deliberately; no string removal without replacement behavior. |

In S3 replace “every driver takes a build-slot lease”, FIFO/wait wording and “outside the slot
gate” with “every heavy invocation uses the configured host admission mode”, named load evidence,
the hard ceiling and exit-4 behavior. Cite the operator's dated decision. Keep the method-scoped
PC rule, one invocation per admission, native output, strict source evidence and platform/default
rules. The `git-common` fragment has no build-slot policy to change; do not rewrite unrelated Git
invariants. `CLAUDE.md` remains its import pointer. Historical investigations/plans/receipts retain
their original lease outcomes. A narrative-only D-2 exemption does not apply to instructions,
test selections, settings, schema or policy changes.

Remaining assumption inventory (preserve, inspect; no behavior change is authorized merely by
a text match): `RoutingSessionRunnerClient`, `LandingVerifier`, `ILandingChildObserver`,
`FakeSessionRunnerClient`, checkpoint `SlotDiagnostic`, `BuildConfinement` and the
`BaselineComparer` must continue forwarding/reporting the selected mode. Existing
`BaselineComparerTests`, `C1307CliCancellationTests`, `CheckpointConfinementLedgerTests`,
`CheckpointExecutorLogTests`, `CheckpointLargeStampTests`, `CheckpointSourceExecutionTests`,
`CheckpointTaskOwnershipTests`, `RunSchedulerTests`,
`EvidenceFolderTests`, `AgentTaskLandSourceFreshnessTests`, `AgentTaskLandStageOutcomeTests`,
`LandNonCodeSkipTests`, `DirectoryBuildRspTests`, `DockerStackContractTests`,
`DockerStackDocumentationTests`, `HostCleanupPolicyTests`, `ProcessSpawnLimitTests`,
`BuildSlotEndToEndTests` and `DaemonStartupDiagnosticsTests` keep legacy fixtures explicit.
`Antiphon.E2E/Fixtures/LandDeliveryFixture.ReceiptDiagnostics.cs` and
`LandDeliveryFixtureProbeTests` must still treat an admission wait as diagnostic silence, not
delivery evidence. The implementation avoids changing these contracts; a required behavioral
edit brings its exact tests into TestDesign before Code.

Script/harness callers additionally found by source census are
`test-apphost-{dead-launch,health-window}.ps1`, `test-c1361-windows-input.ps1`,
`test-client-gate-exit.ps1`, `test-deploy-server2-jq.ps1`, `test-live-lane-evidence.ps1`,
`test-nightly-tests.ps1`, `test-qualification-supervisor.ps1`,
`verify-card0778-control-matrix.cjs`, and the cache/rolling fixtures. These keep their existing
wrapper and explicit offline shims; generated fixture payloads are not broadly rewritten.
The `Antiphon.Checkpoints` bootstrap/cache hash must include any new linked admission source.
The history/Load feature introduces no blanket build of these unrelated consumers.

Open-work collision check: `card.ps1 get CARD-1217 -Board Antiphon` and CARD-1581 both returned
InProgress on 2026-11-06. CARD-1217 owns conditional skips in the checkpoint report/manifest and
PowerShell/tool validators. S1 must not touch these; S2's additive admission envelope waits for
its validator slice or is explicitly coordinated against its landed SHA. Do not relax zero-skip
rules to get these checkpoints through. CARD-1581 owns same-card dispatch/land repository-lease
cycles in dispatcher/landing services. S3 waits for its relevant slice and inserts the load hold
before lease acquisition; preserve its pending-land ordering and run its named regression if
that insertion shares the corrected branch. Also coordinate CARD-1531 deadline accounting,
CARD-1713 legacy max hold and CARD-1629 output reuse rather than undoing them.

## Verification handoff — separate TestDesign

TestDesign must bind S1 first, then S2/S3 against their actual landed dependencies. It owns the
final `## Verification design`, exact method/parameter rosters, PC bindings and execution manifest.
The following requirements are the closed behavioral scope, not an instruction to run tests in
this Plan dispatch. New class/method names are proposed deliverables, not existing coverage.

| ID | Acceptance / regression obligation | Principal proposed witness |
|---|---|---|
| V-1 | Correct CPU units/denominator, null first delta and Windows load; real production factory on both OS lanes. | `C1514HostLoadSampleTests.Cpu_units_and_unknowns`; native Linux/Windows factories. |
| V-2 | Shared authority occupancy, stable host alias/epoch, duplicate collector exclusion and unknown authority evidence. | `C1514HostLoadSampleTests.Shared_authority_is_the_count_source`. |
| V-3 | Durable restart replay, duplicate batches, interrupted append, bounded spool/retention and explicit gaps. | `C1514HostLoadSpoolTests.Replay_preserves_identity_and_gaps`; history ingestion witness. |
| V-4 | Hour/last-hour avg/min/max/p95, missing coverage, corrected late samples and bounded quantile merging. | `C1514HostLoadHistoryTests.Percentiles_preserve_counts_and_precision`. |
| V-5 | Start/during/end task attribution for overlap, retries, host moves, short tasks and restart recovery. | `C1514TaskLoadWindowTests.Attempts_keep_their_host_windows`. |
| V-6 | Script/API current/hour/recommendation, capacity clamps, null/error semantics and direct/phone-home compatibility. | `C1514HostLoadScriptTests.Json_reports_load_and_capacity`; recommendation/history tests. |
| V-7 | Low CPU starts without FIFO; threshold equality/high pressure waits; latest and average both matter. | `C1514HeavyCommandAdmissionTests.Load_decides_without_fifo`. |
| V-8 | Concurrent attempts cannot exceed aggregate hard ceiling, even across two runners; retries are idempotent per execution. | `C1514HeavyCommandAdmissionTests.Concurrent_starts_share_one_ceiling`. |
| V-9 | Stale/unknown/wrong-host/failed mode negotiation grants nothing; finite wait includes HTTP/backoff and cancels correctly. | `C1514HeavyCommandScriptTests.Unknown_or_busy_never_starts_a_child`. |
| V-10 | Release after contained child exit; crash/restart/renew loss/PID reuse never free still-running capacity. | `C1514HeavyCommandRecoveryTests.Uncertain_owner_stays_counted`; native custody witnesses. |
| V-11 | Checkpoint script/tool and land use Load mode, keep source proof and truthful nonexecution/admission clocks. | `C1514CheckpointLoadAdmissionTests.Timeout_has_no_execution_receipt`; `C1514LandLoadAdmissionTests.Load_mode_has_no_unleased_fallback`. |
| V-12 | Dispatch/preflight defer under pressure before prep/lease/session; races and stale-after-prep recheck cannot bypass. | `C1514HostLoadDispatchTests.C1514_High_load_holds_before_preparation`; delegate preflight witness. |
| V-13 | Startup reservations limit bursts, survive restart, reconcile failed startup and never alter existing task limits. | `C1514HostLoadDispatchTests.C1514_Concurrent_dispatch_accounts_for_startup`. |
| V-14 | Observe/Lease rollback and mode transition preserve active count, legacy clients and telemetry; default remains off until qualified. | `C1514HeavyCommandAdmissionTests.Mode_switch_preserves_active_work`. |
| R-1 | Old hostStatsV1 bodies/routes, rolling old/new peers, unknown values and bounded authenticated reads remain compatible. | Existing host stats and transport tests plus history API witness. |
| R-2 | Explicit Lease mode retains FIFO, max hold, disabled/unreachable and land memory-floor behavior; Load mode never inherits these escapes. | Existing broker/client/land tests explicitly configured Lease. |
| R-3 | Literal argv, exit codes, maxcpucount placement, Windows environment and cancellation cleanup remain correct. | Existing selected wrapper tests plus new native Load-mode cases. |
| R-4 | Instruction composition/mechanical contracts describe both modes, preserve platform/default rules and size limits. | Instruction/TaskPlatform/MechanicalPc contract tests. |
| R-5 | Classification, process-spawn limiter, slow registry, timing and receipt qualification remain complete. | Classification guards in each changed test assembly and checkpoint contract tests. |
| R-6 | Telemetry overhead/query bounds meet budget; database/history failure cannot block live runner startup or command admission. | Native sampler budget and `C1514HostLoadHistoryTests.History_failure_does_not_stop_live_samples`. |

Candidate positive controls below bind to exact methods in TestDesign. Each variant requires a
specific expected assertion failure and restored green, not a build/fixture failure or zero tests.
Use only `/*/*/ClassName/ExactTestMethod` for an individual PC cycle, never the ordinary class OR.
SourceLanding mutations and evidence remain external; production/test repairs use separate Code.

| PC | Method | Mutant / required red observation |
|---|---|---|
| PC-1 | `C1514HostLoadSampleTests.Cpu_units_and_unknowns` | Divide host busy percent by cores again; expected 50% becomes wrong. Variant: coerce first delta or Windows load null to zero. |
| PC-2 | `C1514HostLoadSampleTests.Shared_authority_is_the_count_source` | Read injected local broker despite configured shared authority; fixture's local 0/shared 4 count must expose the mismatch. |
| PC-3 | `C1514HostLoadSpoolTests.Replay_preserves_identity_and_gaps` | Advance acknowledgement before committed ingest, or omit duplicate key; restart loses/doubles a named sample. |
| PC-4 | `C1514HostLoadHistoryTests.Percentiles_preserve_counts_and_precision` | Average hourly p95s; asymmetric sample distribution must produce a different p95. Variant: claim exact after histogram coarsening. |
| PC-5 | `C1514TaskLoadWindowTests.Attempts_keep_their_host_windows` | Join by task ID alone; two attempts on different hosts must incorrectly mix. |
| PC-6 | `C1514HostLoadRecommendationTests.Unknown_and_capacity_bound_the_recommendation` | Treat unknown CPU as zero or omit the effective capacity clamp; recommendation must become improperly positive/too large. |
| PC-7 | `C1514HeavyCommandAdmissionTests.Load_decides_without_fifo` | Reintroduce FIFO-head eligibility with unused hard capacity and low CPU; immediate-start witness fails. Variant: admit at 85% equality. |
| PC-8 | `C1514HeavyCommandAdmissionTests.Concurrent_starts_share_one_ceiling` | Split count by runner, remove atomic check/increment, or reuse PID-only identity; barrier-controlled starts exceed or undercount the configured ceiling. |
| PC-9 | `C1514HeavyCommandScriptTests.Unknown_or_busy_never_starts_a_child` | Enable legacy unreachable fallback in Load mode; child sentinel appears. Variant: reset admission deadline on every HTTP failure. |
| PC-10 | `C1514HeavyCommandRecoveryTests.Uncertain_owner_stays_counted` | Release on renewal silence or 48-hour age despite live/unknown child; next admission incorrectly succeeds. Variant: clear registry on restart. |
| PC-11 | `C1514HeavyCommandNativeTests.Linux_release_follows_child_exit` | Decrement before owned child exits/drains; concurrent sentinel starts while prior child is demonstrably alive. Windows counterpart has its own native control. |
| PC-12 | `C1514CheckpointLoadAdmissionTests.Timeout_has_no_execution_receipt` | Mark a timeout as executed/verified or omit admission sample identity; structured receipt/strict validator assertion fails. |
| PC-13 | `C1514LandLoadAdmissionTests.Load_mode_has_no_unleased_fallback` | Use the memory-floor/unreachable escape in Load mode; recorded child build must wrongly run. |
| PC-14 | `C1514HostLoadDispatchTests.C1514_High_load_holds_before_preparation` | Move check after repository claim or skip actual-launch recheck; owned fixture observes prep/lease/start under high or stale load. |
| PC-15 | `C1514HostLoadDispatchTests.C1514_Concurrent_dispatch_accounts_for_startup` | Omit startup reservation from next decision; two requests consume one-task headroom. |
| PC-16 | `C1514HeavyCommandAdmissionTests.Mode_switch_preserves_active_work` | Reset occupancy on rollback; a new legacy grant overshoots the physical-host count. |
| PC-17 | `C1514HostLoadHistoryTests.History_failure_does_not_stop_live_samples` | Await a blocked DB writer inline in sampling/startup; independent live-sample progress witness fails. |
| PC-18 | `C1514HostLoadScriptTests.Json_reports_load_and_capacity` | Emit a diagnostic to JSON stdout or positive capacity after failed HTTP; machine-readable result/error assertion fails. |

Classification policy: pure probe parsing, policy arithmetic, histogram and fake-clock tests are
Unit. HTTP/DB/spool/native/process tests are Integration; mark genuinely slow native overhead
measurements Slow and add their exact class/methods with adjacent CARD-1514 reason to the
assembly's `slow-tests-allowlist.txt`. No “Unit” relabel to evade selection. Every pwsh/host/process
spawner carries that assembly's `ParallelLimiter<ProcessSpawnLimit>`. Add native-only reasons
and exact method mapping to `tests/test-execution-policy.json`, regenerate its policy hash through
the existing workflow, and update `tests/test-selection-policy.json`/timing cost inputs only where
their real dependency/cost membership changes. Preserve suite/dependency ownership: runner +
shared contracts feed server, checkpoint tooling and script consumers. Do not run Antiphon.Tests
beside Antiphon.Agents.Pty.Tests. No new Pty suite selection is required unless custody code itself
changes; that would require an amendment.

### Checkpoints

This is the closed planned selection. TestDesign must replace provisional floors with frozen
method/parameter rosters before Code; no new test named here is claimed to exist yet. Every row
owns one isolated build and one exact filter. Run at most three rows per invocation; use one row
per invocation for CP-6 onward to keep cold builds and admission below the 30-minute Debug line.
S1 Any run is CP-1/CP-2; CP-3 and CP-4 are separate native runs. No whole-assembly run is added.

Lane mapping: CP-1/2/5/6/8/11/12 = Any (runtime default); CP-3/7/9 = Linux native; CP-4/10 =
Windows native on an eligible isolated worker. Never pin a fleet location. Windows work remains
pending if no permitted worker exists; it is not executed on the desktop by this dispatch.
All rows require the listed classes/method patterns, positive executed counts and zero failed/skipped.
All outputs end with `/`. Table `Min` is a conservative result-count floor, not assertion count.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c1514-load-s1-runner/` | any-sample-spool | `/*/*/(C1514HostLoadSampleTests*)\|(C1514HostLoadSpoolTests*)\|(HostStatsSamplerTests*)\|(HostStatsStoreTests*)\|(HostStatsProbeParseTests*)\|(HostStatsEndpointTests*)\|(TestClassificationGuardTests*)/*` | V-1, V-2, V-3, R-1, R-5 | all 7 listed classes, 0 failed/skipped | 7 | 4 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1514-load-s1-server/` | any-history-script | `/*/*/(C1514HostLoadHistoryTests*)\|(C1514TaskLoadWindowTests*)\|(C1514HostLoadRecommendationTests*)\|(C1514HostLoadScriptTests*)\|(HostStatsCacheTests*)\|(HostStatsPollServiceTests*)\|(HostStatsAntiphonCountersTests*)\|(HostStatsEndpointTests*)\|(SessionRunnerHttpClientHostStatsTests*)\|(TestClassificationGuardTests*)/*` | V-3, V-4, V-5, V-6, R-1, R-5, R-6 | all 10 listed classes, 0 failed/skipped | 10 | 12 |
| CP-3 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c1514-load-s1-linux/` | linux-native-sample | `/*/*/C1514HostLoadNativeTests/Linux_*` | V-1, R-6 | Linux production factory and sampler overhead methods, 0 failed/skipped | 2 | 6 |
| CP-4 | S1 | `tests/Antiphon.SessionRunner.Tests -> bin-c1514-load-s1-windows/` | windows-native-sample | `/*/*/C1514HostLoadNativeTests/Windows_*` | V-1, R-6 | Windows production factory and sampler overhead methods, 0 failed/skipped | 2 | 8 |
| CP-5 | S2 | `tests/Antiphon.SessionRunner.Tests -> bin-c1514-load-s2-authority/` | any-authority-recovery | `/*/*/(C1514HeavyCommandAdmissionTests*)\|(C1514HeavyCommandRecoveryTests*)\|(BuildSlotBrokerTests*)\|(BuildSlotSettingsTests*)\|(BuildSlotEndpointTests*)\|(BuildSlotMaxHoldTests*)\|(BuildSlotMetricsTests*)\|(TestClassificationGuardTests*)/*` | V-7, V-8, V-10, V-14, R-2, R-5 | all 8 listed classes, 0 failed/skipped | 8 | 5 |
| CP-6 | S2 | `tests/Antiphon.Tests -> bin-c1514-load-s2-checkpoints/` | any-checkpoint-admission | `/*/*/(C1514CheckpointLoadAdmissionTests*)\|(BuildSlotClientTests*)\|(CheckpointSlotContractTests*)\|(CheckpointSlotExecutorTests*)\|(CheckpointAdmissionBudgetTests*)\|(CheckpointAdmissionContractTests*)/*` | V-9, V-11, R-2, R-5 | all 6 listed classes, 0 failed/skipped | 6 | 12 |
| CP-7 | S2 | `tests/Antiphon.Tests -> bin-c1514-load-s2-wrapper/` | linux-wrapper-regressions | `/*/*/BuildSlotScriptTests/(C589*)\|(C800*)\|(C845*)\|(C1627*)\|(Wrapper_*)` | R-2, R-3 | every method in the 5 listed prefix groups; excludes C1048 Windows-only methods | 5 | 12 |
| CP-8 | S2 | `tests/Antiphon.Tests -> bin-c1514-load-s2-consumers/` | any-consumer-load | `/*/*/(C1514HeavyCommandScriptTests*)\|(C1514LandLoadAdmissionTests*)\|(LandVerificationBuildSlotTests*)\|(RunCheckpointScriptTests*)\|(RunCheckpointSourceScriptTests*)\|(TestClassificationGuardTests*)/*` | V-9, V-10, V-11, R-2, R-3, R-5 | all 6 listed classes, portable cases only, 0 failed/skipped | 6 | 14 |
| CP-9 | S2 | `tests/Antiphon.Tests -> bin-c1514-load-s2-linux/` | linux-wrapper-custody | `/*/*/C1514HeavyCommandNativeTests/Linux_*` | V-10, R-3 | Linux child-exit and restart/reconciliation methods, 0 failed/skipped | 2 | 11 |
| CP-10 | S2 | `tests/Antiphon.Tests -> bin-c1514-load-s2-windows/` | windows-wrapper-custody | `/*/*/(C1514HeavyCommandNativeTests*)\|(BuildSlotScriptTests*)/(Windows_*)\|(C1048*)` | V-10, R-3 | Windows custody method(s) and existing C1048 cases, 0 failed/skipped | 2 | 14 |
| CP-11 | S3 | `tests/Antiphon.Tests -> bin-c1514-load-s3-dispatch/` | any-dispatch-reservations | `/*/*/(C1514HostLoadDispatchTests*)\|(C1514DelegateLoadPreflightTests*)\|(DispatchHoldVisibilityTests*)\|(AgentTaskPipelineStatusTests*)/(C1514*)\|(C654*)` | V-12, V-13, R-1 | new C1514 methods and existing C654 host-budget methods in all 4 classes, 0 failed/skipped | 4 | 12 |
| CP-12 | S3 | `tests/Antiphon.Tests -> bin-c1514-load-s3-contracts/` | any-instructions-policy | `/*/*/(InstructionBundleTests*)\|(TaskPlatformGuidanceTests*)\|(MechanicalPcContractTests*)\|(CheckpointManifestDocumentationTests*)\|(StableBuildOutputDocumentationTests*)\|(RunnerDefaultGuidanceTests*)\|(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)/*` | R-4, R-5 | all 10 listed classes, 0 failed/skipped | 10 | 12 |

Before freezing the roster, TestDesign must separate any newly discovered native-only cases
from portable rows and bind affected
CARD-1217/CARD-1581 regression methods after their landed SHA is known. These are named pending
bindings, not permission for Code to invent additional runs. A split replaces this table in the
verification artifact before execution. At this base the class prefixes above have no suffix
collisions within their selected project; the static census also caught the two host-budget
filenames whose actual classes are the partials selected in CP-11. `HostStatsEndpointTests`
exists in both projects, but
each row builds only one. Existing C589/C800/C845/C1627/Wrapper_ prefixes intentionally select
their whole named groups; C1048 is excluded on Linux. Recheck generated/partial test identities
and TRX after implementation. Class/method OR operands must remain parenthesized and each end
in `*`; table pipes are escaped. A single exact-method PC filter requires no trailing wildcard.

Code uses the checkpoint tool for each committed slice group, awaits every run to completion,
and records unedited CP lines/counts/source provenance. Bootstrap builds, if needed, use the
currently active admission wrapper and are separately accounted. Do not test the new production
policy against a production runner: use isolated loopback authorities and owned fixture children.

## Performance budget

Apply [testing-and-build.md#performance-standards](../../testing-and-build.md#performance-standards)
and R1–R8 in its owner. This Plan itself has no builds/tests: its evidence is source inspection,
historical measurements and read-only API observations.

New policy and storage tests have **no measured p95 yet**. The checkpoint estimates above are
provisional CARD-1514 planning costs, not fabricated measurements. TestDesign freezes exact
timeouts after reviewing comparable retained receipts; collect per-method and phase timings
from the first ordinary baseline/green and subsequent necessary Review/PC runs, at most three
normal plus two loaded samples per unchanged proof selection. Do not run extra full suites just
to fill a percentile. Missing/small samples remain provisional with this card as owner.

| Component | Expected duration / provisional timeout | Provenance and collection obligation |
|---|---|---|
| Pure unit test | <100 ms body; provisional 2 s cap | New fake-clock/policy tests; measure first required run. No real waits for 600 s, 48 h or retention days. |
| DB/API test | 0.1–2 s body; provisional 10 s cap | New tests; expensive shared setup excluded from body and measured separately. Replace with about 2 × measured p95 when known. |
| Process/script test | 1–15 s; provisional 30 s body, 10 s cleanup | Isolated owned child/loopback authority. Native cancellation has an explicit shorter barrier/watchdog. No production calls. |
| Runner-project checkpoint build | 1–3 min expected; provisional 6 min build cap | No comparable new-row p95 available; record source/host/load and narrow after first required build. |
| Antiphon.Tests isolated cold build | Median comparator 431.6 s, provisional p95 817.1 s; 28 min cap | Historical n=11, cited S5b-1 report; `2 × 817.1 / 60 = 27.24`, rounded up. This is a contended cross-selection comparator, not a new-row p95. |
| Portable execution row, including host | 1–3 min expected; provisional 6 min cap, or 8 min for consumer script batch | S5b-1 ordinary row maxima 42–85 s and classification row max 181 s at differing load; new roster cost still provisional. Split if actual cost exceeds this, never silently widen. |
| Native sampler overhead row | Two off/on 60-second pairs plus startup/teardown, about 5 min; 10 min cap | Existing 2026-09-26 sampler measurement: Linux mean incremental CPU 0.633 percentage points of one core. New full telemetry must remain <1 percentage point of one core, with both OS results retained. |
| Admission | 0 s low pressure; 600 s maximum wall, separate from execution | Historical 39 waits p95 255 s; cap is a new policy limit. Test virtual time, not ten-minute sleeps. |
| One large-row run | About 9–14 min typical including build; loaded build plus execution can approach 20 min before admission | Use one Antiphon.Tests build row per run. Derive total/hard-wall from its build, row, tool overhead and bounded acquisitions; do not use an arbitrary 30-minute total. |

Expected total ordinary planning cost is 122 minutes across all rows/OS lanes (sum of
`EstimatedMinutes`), before authoring, PCs, canary observation or necessary reruns. It is not
one run. No batch is commissioned over 30 minutes; any expected or observed selection/run over
30 minutes requires a Debug task with SHA, roster, run ID and host/load, even if admission caused
the excess. Existing S5b-1 Debug is context, not a blanket exemption. Native Windows estimates
are explicitly provisional and cannot be inferred from Linux timings.

Shared setup: one Postgres container/migration/template per assembly run, one loopback server
fixture per run where contracts permit, transaction/schema clone per test. Spool tests use
private owned directories and fake time. Restart/partial-write/custody tests alone may construct
a fresh instance because restart is the subject; measure that phase. Avoid building fixtures or
starting the full AppHost per class. Preserve ProductionRunnerGuard/refusing runner clients for
any real server Program boot. Assemblies with process spawners run sequentially.

Record `.antiphon/checkpoints/<run>/` receipts and a gitignored timing root joined by run/test/
group/source SHA/platform/physical host. Each run records admission, build, tool discovery/ledger
remainder, container start, readiness, data preparation, web host, migrations, clone/restore,
body, teardown and DB/HTTP/filesystem/process external calls. Unknown spans stay unknown,
never zero. Report wall separately from summed or overlapping spans. Preserve historical reds
and count reruns. New HTTP operations are cancellation-bound (3 s live/ingest attempt, 5 s
history read initial budgets), one bounded batch/transaction, no retry inside the admission
critical section; bound these again from measured p95 before promotion.

Operational telemetry budget: 5-second cadence, <1 percentage point of one core incremental
sampler CPU, at most 256 MiB runner spool, one bounded ingest batch per poll, at most one shared
authority read per sample and one batched task query per server tick. Index history queries by
host/time and page them; no N+1 task queries. Record sample-to-ingest lag, DB write/rollup p95,
spool write duration and new startup spans. Proposed local sampler-tick p95 target is <25 ms;
historical primitive probe 0.143 ms is only a lower-level comparator. A missed 5-second cadence
or startup/admission I/O coupling fails R-6; inspect cost before changing cadence/timeouts.

## Open questions for the operator

These are separately recorded calibration/rollout choices; none blocks S1 TestDesign or the
authorized dormant implementation.

1. After S1 measurements, retain 85% command / 75% dispatch defaults or choose host-specific
   overrides? Default is retain them pending comparable canary evidence, never auto-tune.
2. Is 7-day raw / 90-day aggregate/task retention sufficient? Default is yes, with measured
   storage footprint and explicit gap/coverage reporting.
3. Which eligible host/maintenance window should receive the first canary and later Windows
   qualification? No fleet location is embedded; no desktop run is authorized by this Plan task.
4. If a command owner disappears and cannot prove child exit, who operates recovery of the
   counted uncertain registration? Default is hold new capacity at the ceiling and report; do not
   release on a timer or kill unknown processes.

Next: TestDesign for **S1 telemetry + script**, preserving the S2/S3 scope and dependencies above.
