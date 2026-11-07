# CARD-1514 load telemetry S1 verification design

Date: 2026-11-06. TestDesign `1ddc4796`, branch `feat/card-task-1ddc4796`.
Source/Plan base: `41cd16dd0dfdb533592f465d2117246960b2f26c`.
Inspected current master: `315f8566726e4e33a5288d9861080257068b69d3`;
`git merge-tree --write-tree origin/master HEAD` was conflict-free before authoring.
Final pre-publication fetch: master `d164e93ae47a79ed175beeb9367212f7a7d5d1d5`;
its only change since that inspection is ProjectReadinessBatchTests, outside this
selection. The repeated merge-tree check remained conflict-free. No merge,
rebase, target push, land-graph update or desktop action was performed.

This appends the verification contract for **S1 telemetry + script** to the unchanged
[fix design](../plans/2026-11-06-card-1514-load-based-admission-plan.md), also reached
through [the requested Plan name](../plans/2026-11-06-load-based-admission-plan.md).
Format precedents: CARD-1737 R2 at `origin/feat/card-task-0ba30c05` and CARD-1217 at
`origin/feat/card-task-097141bd`. This document replaces only the Plan's provisional
S1 verification selection, not its fix design or S2/S3 decisions.

**Admission remains unchanged.** S1 publishes shadow advice; it neither removes
build-slot guards nor starts, delays, reroutes or reserves real work. Defaults of
85% command pressure and 75% dispatch pressure are provisional observations, not
permission to activate Load mode. Ordinary Code then Review precede land; formal
Mutation follows land. This documents-only task ran **0 builds, 0 tests, 0 PCs**.
Every new payload, source patch and compiled roster remains **binding required
after Code**. All controls below are mandatory later executions, not claimed kills.

## Verification design

### Inspection

`RT/` = `tests/Antiphon.SessionRunner.Tests/`; `AT/` = `tests/Antiphon.Tests/`;
`R/` = `src/Antiphon.SessionRunner/`; `SC/` = runner Contracts;
`A/`, `I/`, `D/` = server Application, Infrastructure, Domain.

| Bodies read | Boundaries -> V/R IDs or exclusion |
|---|---|
| Plan S1 model, persistence, API/script, S2/S3 exclusions, file inventory, candidate V/R/PCs and performance; both format precedents | All S1 witnesses below; Plan V-7..14 / admission controls deferred to their own TestDesign |
| project-context; testing/build checkpoint schema, runner, native witnesses, host stats, PCs/restoration; test-performance-standards; orchestration stage order and D-2; resilience single-attempt host reads; ops-http route/auth guidance | Closed selection, bounded timing, no fake native credit, no retry-policy widening |
| R/HostStatsProbe, LinuxHostStatsProbe, WindowsHostStatsProbe, HostStatsSamplerService, HostStatsStore, HostStatsSettings, HostStatsRoutes complete; Program host-stats registration/capability wiring; PhoneHomeCommandDispatcher host-stats dispatch/results | S, A, B, W and N roster below; host denominator, nullable measurements, real factory, spool seam and authority override |
| RT/HostStatsSamplerTests, HostStatsStoreTests, HostStatsProbeParseTests, HostStatsEndpointTests, HostStatsTestHost/ScriptedHostStatsProbe, HostStatsProbeLiveTests complete; PhoneHomeCommandDispatcherTests three Host_stats methods and HostStatsDispatcher fixture | Nearest fixtures for all new RT files; R-1, R-2; old live-core assertions need deliberate correction |
| A/Services/HostStatsPollService and HostStatsAntiphonCounters, API HostStatsEndpoints complete; SC host-stats DTO and operation enum; HTTP and phone-home clients' host read/error bodies | H, E; actual namespace of PollService is Infrastructure despite Application path; ordinal preservation; one-attempt reads |
| AT/Application/HostStatsCacheTests, HostStatsPollServiceTests, HostStatsAntiphonCountersTests, HostStatsEndpointTests, SessionRunnerHttpClientHostStatsTests complete | R-1..3, nearest H/E/Q fixtures; do not equate scripted peer response with runner producer |
| AT/TestHelpers/PhoneHomeTestHost including RecordingLocalClient and PhoneHomeScriptedPeer complete; TestDbFixture including clone/setup/disposal and SharedStoreWarmup reachability traversal | Real loopback websocket/HTTP, production directory, one template per run, cloned PostgreSQL; substituted session runtime cannot prove session delivery |
| AT/Application/HostBudgetPipelineTests complete; HostOccupancySampleEndpointTests bounded-window/reason cases; SeatOccupancySamplerTests Rig/CreateAsync/SampleAsync, task/session seeding and counting-interceptor patterns; AgentTask Attempt/event fields and AgentSession identity/timestamps | T/E, task-attempt and host provenance; existing task fields alone cannot reconstruct every missed historical move |
| AT/Scripts/DispatchConcurrencyScriptTests complete (nearest command/loopback recorder); ScriptHarness and LiveLaneRunnerTests complete; scripts/keep-six read/API/token path; measure-host-stats-overhead.ps1 complete | J/N; reuse isolation/stream ownership, not old combined stdout+stderr or unsafe cleanup; no old measurement script run |
| shared TestClassificationGuardTests, NativeLinuxOnlyAttribute, NativeLinuxPolicyWitnessTests; TestLaneCategoryGuardTests complete; NativeLinuxPolicyExecutionTests native child method; nightly-policy canonical/hash functions and live-lane protected-input pin | R-5, precise native method policy rows and coupled policyHash/PinnedInputDigest |
| RT csproj, runner/server/AT direct ProjectReference lists and staged child targets | Dependency closure below; no Pty or whole-assembly execution introduced |

Source findings that the new witnesses must expose: Linux currently uses
`Environment.ProcessorCount`; process CPU currently uses UTC wall deltas; local
broker occupancy can differ from the configured authority; a counter read failure
currently projects zeros; memory ring and server cache have no durable history.
`HostStatsProbeLiveTests` currently expects process-visible cores on both OSes.
Correct that assertion to an independent native host count; preserve its other
assertions. The public status GET for tasks currently uses ResolvePollingCallerAsync
and tolerates an absent/stale token. S1 must reuse actual read policy, not invent a
new token-required policy under the words “existing scope”. Runner history still
uses its existing trusted HTTP/phone-home identity boundary.

#### Frozen sample and persistence contract

JSON is camelCase, UTF-8, UTC round-trip timestamps, nullable unknown metrics and
finite JSON numbers. Store byte counts as nonnegative int64, sequence as positive
int64 (no reuse within boot), IDs as opaque strings or canonical GUIDs as below.
No process command lines, task goal/input, connection strings, tokens or arbitrary
exception text. Reasons are closed codes (`first-delta`, `unsupported`, `missing`,
`counter-reset`, `invalid`, `unavailable`, `stale`, `binding-unknown`, `gap`).
No unknown numeric field is materialized as zero. A real observed zero is valid.

The v1 **sample** object has these fields (all fields present; nullable as stated):

```text
schemaVersion: 1
source: { runnerId:string, runnerStoreId:guid, bootId:guid, sequence:int64 }
sampledAt:utc; intervalSeconds:5
physicalHostKey:string?; bindingRevision:int64?; sourceEpoch:guid?
metricScope: host | constrained | unknown
hostLogicalCores:int32?; effectiveCoreLimit:number?
cpuPercent:number?; busyCoreEquivalent:number?; ioWaitPercent:number?
load1:number?; load5:number?; load15:number?; loadPerCore:number?
memory: { totalBytes:int64?, availableBytes:int64?, usedBytes:int64? }
swap: { totalBytes:int64?, freeBytes:int64?, usedBytes:int64?, meaning:swap|commit }
volumes:[{ path:string, freeBytes:int64?, totalBytes:int64? }]
processCount:int32?
processes:[{ name:runner|session|pty-host, sessionId:guid?, cpuPercent:number?, workingSetBytes:int64? }]
unknownReasons:{ metricPath:closed-code }
authority:{ id:string?, physicalHostKey:string?, revision:int64?, observedAt:utc?,
  state:known|unknown|stale, mode:Lease|Load|unknown, occupied:int32?,
  budget:int32?, waiters:int32?, activeCommands:int32?, hardCeiling:int32? }
```

`busyCoreEquivalent=cpuPercent*hostLogicalCores/100`; loadPerCore uses the same
host denominator. Linux host core count comes from online host CPU records, with
cgroup quota/affinity described separately; Windows uses an all-processor-groups
native host count. A denominator that cannot be established stays unknown.
Linux idle+iowait are excluded from busy; guest columns are not counted twice.
Windows load1/5/15/loadPerCore are null. Windows page-file numbers retain their
existing commit meaning. Process CPU remains core-relative, possibly >100%.
The v1 history DTO is additive; old hostStatsV1 bodies retain their shape and
1/5/15/30-minute behavior. Correcting Cores' host semantics is deliberate.

Server enrichment adds `ingestedAt`, canonical physicalHostKey/sourceEpoch and
`counters:{ countedAt, tasksInFlight, byStage, byKind, pendingLaunches,
pendingMirrors, queued, held, sessionsLive, effectiveTaskCapacity }`. All counters
are nullable on failure, including maps. Replay may use a retained counter
observation only if `abs(countedAt-sampledAt)<=15s`; ingestion time never replaces
sample time. Host aliases local/desktop resolve to one configured physical key.
Two colocated collectors retain separate provenance; exactly one designated
primary contributes per source epoch. A missing/conflicting binding yields
incomplete history and unknown shadow advice, never guessed host attribution.

Durable identity **K=(runnerStoreId,bootId,sequence)** is unchanged on retry.
Payload equality is compared over canonical sample content, excluding server
enrichment. A duplicate K with conflicting content is an ingest conflict, never
last-writer-wins. Storage has a unique K index and `(physicalHostKey,sampledAt,K)`
read index. HostLoadSamples, HostLoadHourlyRollups and AgentTaskLoadWindows follow
the Plan's names and keys; EF migrations are CLI-generated in Code.

Runner JSONL spool lives in configured runner state, outside worktrees. Flush a
whole record before advertising it; replace its durable acknowledgement cursor
atomically. Keep at most 24h or 256MiB, whichever is first; cap accounting includes
pending chunks, quarantine and loss metadata. A partial final line is quarantined,
not a sample. Dropping at the explicit cap persists lost K ranges/reason and
increments loss counters; restart must preserve that visible incompleteness.
Disk-write failure leaves live sampling available and records loss as soon as the
store can persist again. No claim of survival before the durable append boundary.

Pull protocol: `/host-stats/history?after=<opaque>&limit=<1..120>` returns
`{schemaVersion:1, source, samples, gaps, nextCursor, more, complete}` bounded to
120 records **and** 1MiB of uncompressed JSON. First after is absent, never an
invented zero cursor. The trusted server sends only its **committed** after cursor;
it is the runner acknowledgement boundary, not the page's optimistic nextCursor.
A cursor binds store/boot/range and a persisted contiguous committed-or-explicitly-
lost frontier. It cannot acknowledge another store, skip uncommitted holes, or
make a new boot discard an old boot's unacknowledged chunk. Phone-home carries the
same request/response through a new explicitly numbered HostLoadHistory operation;
existing ordinals 1..49, including gaps, remain unchanged. Reserve 50 at this base;
if already used at Code's base, allocate a new unused ordinal and bind both ends
before committing, never renumber an existing operation.

One bounded ingest queue sits behind polling (provisional capacity: two 1MiB
batches per runner, measured/configured through typed settings). Full/enqueue-
failed work remains replayable in the durable spool; no cursor advancement.
One transaction commits inserted samples, loss ranges and contiguous cursor.
Derived-work progress is durable: crash after sample commit but before rollup/window
updates must replay from a separate durable watermark, never the ingest cursor.
Raw retention 7 days; hourly and terminal-task windows 90 days. Retention runs in
bounded hourly batches and never deletes raw data before its required summaries
and long-task histogram updates are durable. Open task windows are not aged out.

Each metric summary is `{avg,min,max,p95,sum,validCount,expectedCount,coverage,
complete,p95Approximate,p95ErrorBound,histogramVersion,binWidth}`. Empty metrics
have null statistics, validCount=0 and explicit coverage; counts themselves remain
known. Fixed 5s slots use half-open `[from,to)`, UTC hour boundaries, with
expectedCount=number of scheduled slots in the interval and coverage=valid/expected.
Do not count a replayed/duplicate K twice or fill a missing slot with zero.
Raw-backed p95 is the sorted valid observation at one-based `ceil(.95*n)`.
Average is sum/validCount, not an hourly-average mean or time-weighted gap fill.
Late replay replaces affected summaries idempotently. Percentiles from multiple
hours use raw samples or mergeable histograms, never averaged p95s.

Histogram v1 uses CPU 0.1 percentage-point, load 0.01, memory/disk 1MiB, counts 1
initial bin widths. It holds <=4096 bins; deterministically double width/rebin on
overflow, preserving count, exact sum/min/max. Quantile is the containing upper
bound, approximate=true with absolute error bound=binWidth. A long task retains
this state after raw expiry. No claim of exactness after coarsening.

Task key **W=(taskId,attempt,physicalHostKey,runnerBootId,windowOrdinal)**. Window
JSON includes queuedAt, dispatchedAt, startedAt, endedAt, state=open|closed,
start/end `{eventId,eventAt,sampleIdentity,sampleAt,ageSeconds,state}`, during
summaries, overlapCount and attribution=`ambientHost` (optional session process
statistics are explicitly partial). Start is authoritative accepted session start,
not the queued/dispatch timestamp. Snapshot boundary observations within 15s;
choose nearest absolute distance, earlier timestamp then K to break ties. Preserve
missing/stale instead of attaching a newer unrelated sample. During is strictly
inside the event boundaries; a short task can share one start/end sample and have
no during statistics. Attempts, moves and boots split windows. S1 stores event
identity and its observer watermark so restart can reconcile missed notifications
from authoritative persisted task/session events. If a historical attempt/move
cannot be reconstructed, expose a gap; do not fabricate it from current task state.
This observational event adapter does not reorder dispatcher or landing leases.

#### Frozen API and script contract

- `GET /api/hosts/{hostId}/load[?projectId=<guid>&role=<role>]` returns one object:
  `{schemaVersion:1,hostId,physicalHostKey,observedAt,current,currentAgeSeconds,
  lastHour,coverage,policy,authority,decision,recommendation}`. `current` includes K.
  `policy={mode,revision,shadow:true,commandThresholdPercent:85,
  dispatchThresholdPercent:75,perTaskCpuPoints:10}`; mode is the actual authority's
  Lease/unknown, never fictitious enabled Load. decision includes separate command
  and dispatch pressure, eligible/hold/unknown and closed reason arrays.
- `GET /api/hosts/{hostId}/load/history?from=&to=&resolution=raw|hour&limit=&cursor=`
  returns `{schemaVersion:1,hostId,from,to,resolution,items,nextCursor,complete,gaps}`.
  Default range=last hour, default limit=720, clamp >2000 to 2000; <=0, malformed
  scalar, inverted/overflowing range, >7 raw days or >90 hourly days is typed 400.
  Cursor binds host/range/resolution/order and stable keyset frontier. New inserts
  behind a page do not duplicate/skip the fixed snapshot's members. Unknown host
  is 404; empty history is 200 with incomplete coverage, not invented observations.
- `GET /api/agent-tasks/{taskId}/load?attempt=` returns `{schemaVersion:1,taskId,
  attempt,windows,complete,gaps}`; absent attempt resolves current authoritative
  attempt and echoes it. Invalid attempt is 400; missing task is 404. Reuse the
  existing task resolution/read policy. Host rows and pipeline/runner nullable
  `load` projections do not overwrite connectivity, drain or dispatchEligible.

Recommendation JSON is `{recommendedAdditionalTasks,scope,missingConstraints,
pressurePercent,startupDebtPercent,cpuHeadroom,hostRemaining,runnerSeatsRemaining,
projectRemaining,stageRemaining,inputsAt,reasons}`. scope is `projectRole` only
when both inputs are supplied, otherwise `hostOnly` with omitted constraints named.
Use `max(latestCpu,mean60s)` for dispatch and `max(latestCpu,mean15s)` for command.
Fresh latest is age <=15s; any absent/nonfinite/foreign-host input, stale capacity,
unknown binding or missing required pressure window yields zero/unknown advice.
Coverage of required 15s/60s windows must be complete for positive advice; partial
statistics remain displayable. Future-dated samples beyond the declared clock
uncertainty are unknown, not negative age. Do not use receipt time to refresh age.

S1 shadow startup debt is computed from already-persisted accepted launches not
represented in the pressure window: 10 points per unique (taskId,attempt,window).
It is **not an S3 admission reservation**. Debt expires from advice only when the
window represents that launch or authoritative startup failure closes it. For
P=dispatch pressure and D=debt, `cpuHeadroom=max(0,floor((75-P-D)/10))`.
Clamp independently by known effective host, runner seats, requested project and
stage remaining; never substitute a configured maximum for remaining capacity.
At command85 or dispatch75 equality the corresponding shadow decision is hold.
At P=44,D=10, CPU headroom=2; host=4,seats=3,project=2,stage=1 yields exactly **1
more task**. With project/role omitted it yields hostOnly=2 and names both omissions.

`scripts/host-load.ps1 [-HostId <id>] [-Project <guid>] [-Role <role>] -Json` reads
ANTIPHON_API/task-token conventions, sends token only in headers, and resolves an
omitted HostId from live runner-defaults/catalogue, never a baked runner name.
Because this command has no AgentKind argument, use the global default; report
that resolution source, and allow explicit HostId to disambiguate a kind override.
It performs bounded GETs only, forwards scope to the server recommendation and
prints exactly that one complete object plus `placement:{runnerId,source,revision}`
to stdout. Human text names age/mode/unknowns/coverage/current and hour statistics,
thresholds, ceilings and MORE-task arithmetic. Diagnostics go to stderr. HTTP,
JSON, placement or required read failure exits nonzero with empty stdout and no
capacity suggestion. A successful incomplete read exits 0 with zero advice and
explicit reasons. No silent alternate host, POST, budget write or enqueue.

### Delivery inventory

These are new asynchronous **telemetry** paths. There is no session input path;
a session delivery claim would additionally require the matching complete
UserPrompt transcript. SignalR publication remains a live refresh hint and is
not durable-history evidence.

| Path / producer -> recipient | Persistence and identity | Recovery and observable receipt |
|---|---|---|
| D1 sampler -> bounded append worker -> runner spool | K; complete flushed JSONL record and gap ledger | Busy append writer, append/enqueue failure and partial tail. Restart reopens actual spool. B methods read surviving K/body or explicit missing K/gap; live memory alone is no receipt |
| D2 spool -> direct HTTP or phone-home -> bounded ingest worker -> PostgreSQL | K/page cursor; unique sample + loss + cursor transaction | H ready/busy and handoff methods use the **production queue**, not direct Insert calls. Disconnect, queue refusal, transaction fault, crash before/after commit and lost acknowledgement. Independent fresh DbContext plus public raw-history read must see the exact K and metrics once |
| D3 committed samples -> rollup worker -> hour and task-window readers | K, source epoch, aggregate watermark, W | Crash before enqueue, after enqueue, after calculation and after write/before watermark. Recovery uses persisted work frontier; API hour/task result contains expected count/sum/min/max/p95 and contributing identities/range. An event/ack/watermark alone is insufficient |
| D4 persisted launch/terminal/retry/move events -> task-window observer -> W/API | event ID, task attempt/session identity, observer watermark | Worker busy or notification enqueue fails; stop/recreate before/after window commit. Reconciliation gives the same actual W rows, missing historical evidence remains a gap. Open attempt stays open |
| D5 durable read API -> host-load.ps1 process -> stdout recipient | K/W and query/snapshot identity inside returned JSON | Successful real script run reads committed queue-produced sample; failed or truncated body has no successful object. Assert parsed stdout and exact fields, exit and separate stderr after joined process completion |

The ready-recipient case starts the production consumer before producing K=101;
the busy case holds the recipient DB command at a barrier, produces K=102..105,
uses one-record batches so one write is held, two batches are queued and the fourth
is refused; confirms K remains spool-replayable, then releases.
Both continue through fresh API reads; the ready case also invokes the real script.
Handoff case runs each D1..D4 cut separately with a new service graph over the same
owned state/DB and a literal K/event ledger. For process-death cuts use an owned
staged runner/server helper, real spool/DB and joined termination; its loopback
transport must invoke the production PhoneHomeCommandDispatcher. No graceful
Dispose that drains the queue may stand in for a crash. Reuse built images; never
build a helper inside a test. The fixture peer's fabricated Reply is permitted
only for old-peer/error tests, never D2's producer-to-recipient claim.

Substitutes and limits: fake time and scripted native-read delegates prove arithmetic
and deterministic cut timing, not OS counters or filesystem crash guarantees.
DB interceptors force a failure at the real transaction; they cannot prove power-
loss durability. Process kill/reopen proves process-crash recovery, not controller
cache/fsync behavior on sudden machine power loss. Loopback TCP/websocket and
owned PostgreSQL prove the local production queue/transport/recipient graph, not
fleet network availability. A stub launch/event fixture does not prove task dispatch;
T's adapter wiring must call the existing persisted-event producer/service through
its normal state transition, with a refusing session client for unrelated calls.
Script recorder cases prove HTTP selection/streams; D5 supplies the separate real
API/queue wiring. No design may stop at a request, queue insert, event, Sent flag,
ack or helper-produced receipt.

### Proves it works now

The following is the frozen **new method roster**, not a claim that these methods
exist. Each is one `[Test]`, no Arguments or data generators; internal vector/cut
loops count as **one** TUnit execution. Each recipe has a case ID, independently
computed literal oracle and a reached-boundary observation. New source filenames
match class names. Alias-qualified method IDs below expand to exact class/method
filters in the PC table. No test copies the production calculation as its oracle.

| Alias | New class / location | Layer, category, fixture |
|---|---|---|
| S | RT/C1514HostLoadSampleTests.cs | Unit; scripted OS read delegates and FakeTimeProvider |
| A | RT/C1514HostLoadAuthorityTests.cs | Integration; loopback real authority HTTP client, local broker with deliberately different occupancy |
| B | RT/C1514HostLoadSpoolTests.cs | Integration; real files/private directory, fake time, real bounded append queue |
| W | RT/C1514HostLoadWireTests.cs | Integration; shared loopback routes following the HostStatsTestHost pattern and real dispatcher |
| H | AT/Application/C1514HostLoadHistoryTests.cs | Integration; cloned PostgreSQL, real spool/poll/ingest graph, owned staged crash child where required |
| U | AT/Application/C1514HostLoadAggregateTests.cs | Unit; literal arrays and histograms |
| T | AT/Application/C1514TaskLoadWindowTests.cs | Integration; cloned PostgreSQL and actual event adapter/reconciliation |
| Q | AT/Application/C1514HostLoadRecommendationTests.cs | Unit; frozen policy/capacity observations and fake time |
| E | AT/Application/C1514HostLoadApiTests.cs | Integration; production endpoint mapping and service graph, cloned PostgreSQL |
| J | AT/Scripts/C1514HostLoadScriptTests.cs | Integration; real pwsh through ScriptHarnessProcess, loopback fixture and captured separate streams |
| N1/N2 | RT/C1514HostLoadNativeTests.cs | Integration; real factory/native counters/spool, isolated runner child |
| N3/N4 | RT/C1514HostLoadNativeOverheadTests.cs | Integration + class-level Slow; owned native overhead experiment |

H/J/N and any other class that spawns a child carry the **assembly-local**
ParallelLimiter<ProcessSpawnLimit>. There is no provider process, paid agent, live
broker or production runner input. New helpers are C1514HostLoadFixture in RT and
AT/TestHelpers/C1514HostLoadFixture, plus scripts/test-host-load.ps1 and private
scripts/fixtures/c1514-host-load fixture files. Reuse ScriptHarnessProcess ownership
and shared TestDbFixture lifecycle; do not copy the older direct Process.Start
helper or recursive cleanup from the nearest scripts. H/T/E share one loopback
host per assembly-run fixture and private DB/context state per case. Crash cases
alone replace owned service graphs/processes because restart is the subject.

| V / case | Exact method | Recipe / variant | Expected oracle and reachability |
|---|---|---|---|
| V-1 / S1 | `C1514HostLoadSampleTests.CpuPercentUsesHostDenominator` | Linux50% busy on24 host cores/effective2; Windows idle600/kernel800/user200 | 50%,busy12,load24/core1; Windows40%; real projection reached |
| V-2 / S2 | `C1514HostLoadSampleTests.UnknownMetricsStayNull` | First delta/reset/failed read/NaN/infinity/out-of-range and Windows load | Null with exact reason, then valid recovery; normalizer and injected OS arm reached |
| V-3 / S3 | `C1514HostLoadSampleTests.ProcessCpuDoesNotBecomeHostCpu` | Host40/session250/missing process/reused PID | Host40 retained, session250 core-relative, reused PID null; sampler projection reached |
| V-4 / S4 | `C1514HostLoadSampleTests.CadenceAndDeltasUseMonotonicTime` | Monotonic+5s, UTC backward/forward jumps; disabled sampler | Delta50%,five-second cadence, no burst/disabled reads; timer/delta clock reached |
| V-5 / S5 | `C1514HostLoadSampleTests.SourceIdentityAndPayloadAreStable` | Two boots same sequence; retry; credential sentinel in unrelated task/process metadata | Distinct boot K, retry identity/metrics stable, sentinel absent; real serialization |
| V-6 / S6 | `C1514HostLoadSampleTests.DisabledHistoryKeepsLegacySampling` | History disabled/live enabled then history re-enabled | Live time/sequence progresses, old routes work, no fictitious history cursor; real registration |
| V-7 / A1 | `C1514HostLoadAuthorityTests.OverrideReadsSharedAuthority` | Local0/shared HTTP4 then absent override/local2 | Count4/id shared then count2; pooled client selection reached |
| V-8 / A2 | `C1514HostLoadAuthorityTests.AuthorityFailureRemainsUnknown` | 503/malformed/hung body/cancel with local0 | Unknown/null; one attempt and3s virtual budget; HTTP/body cancellation reached |
| V-9 / A3 | `C1514HostLoadAuthorityTests.AuthorityIdentityAndFreshnessAreChecked` | Wrong physical key/authority identity, age15001ms then15000ms | Foreign/stale unknown; exact15s boundary valid; validation reached |
| V-10 / A4 | `C1514HostLoadAuthorityTests.AuthorityReadsAreCachedPerInterval` | 20 concurrent projections, advance5s | One request then one new request; real HTTP request counter |
| V-11 / B1 | `C1514HostLoadSpoolTests.AppendSurvivesRestart` | K101 append, dispose/recreate spool over exact files; process-crash proof belongs to H3 | Independent reader sees exact K/body once; real flush/reopen |
| V-12 / B2 | `C1514HostLoadSpoolTests.PartialTailIsQuarantined` | Complete K101 plus truncated UTF-8/JSON K102 tail/restart | K101 survives; K102 only gap/quarantine, not accepted; real parser |
| V-13 / B3 | `C1514HostLoadSpoolTests.CursorCannotSkipUncommitted` | Committed100/offered102 with pending101; foreign-store cursor | Pending101/102 retained, cursor100 preserved; real cursor update |
| V-14 / B4 | `C1514HostLoadSpoolTests.ReplayPreservesBootIdentity` | Old boot pending101, new boot seq1 | Both original K/metrics once at reader; multi-boot chunk replay |
| V-15 / B5 | `C1514HostLoadSpoolTests.CapsPersistExplicitGaps` | Tiny byte/age caps, real chunks/quarantine and fake24h boundary | Bounded bytes, oldest-only eviction, lost K ranges survive reopen; cap paths reached |
| V-16 / B6 | `C1514HostLoadSpoolTests.DiskFailurePreservesLiveSampling` | Writer barrier/full queue/IOException then recovery | Live next K, bounded queue and persistent loss; disk failure and next tick reached |
| V-17 / W1 | `C1514HostLoadWireTests.HistoryRoutePreservesCursorAndBounds` | Route+dispatcher120/121,1MiB boundaries, schema2,invalid/foreign cursor | Same K/cursor, cap/refusal before eviction; both real handlers |
| V-18 / W2 | `C1514HostLoadWireTests.CapabilitiesAndOrdinalsStayCompatible` | Enabled/disabled/old runtime; direct and phone-home capabilities | Advertisement iff implemented; literal old ordinals unchanged; unsupported explicit |
| V-19 / H1 | `C1514HostLoadHistoryTests.ReadyRecipientReadsDurableSample` | D1-D3 ready consumer, separate direct and phone-home worlds, K101 real queue then real script | Fresh DB/API/stdout agree K101/cpu40/one row; entire producer-recipient chain |
| V-20 / H2 | `C1514HostLoadHistoryTests.BusyRecipientEventuallyReadsSameSample` | D2 both transports, DB barrier, capacity2 queue,K102..105 then release/restart | All four K once via API, no early ack; queue-full and replay reached |
| V-21 / H3 | `C1514HostLoadHistoryTests.RecoveryAtEveryHandoffPreservesReceipt` | D1-D3 enqueue failure; pre/post DB commit/ack; derived enqueue/write/watermark crashes | Literal K/event set once or explicit pre-durable gap; raw/hour/task API after fresh graph |
| V-22 / H4 | `C1514HostLoadHistoryTests.IdentityConflictsNeverOverwriteStoredSamples` | K/cpu40 duplicate then same K/cpu99 | One cpu40 row; conflict visible, cursor stops; unique key/conflict path |
| V-23 / H5 | `C1514HostLoadHistoryTests.PrimaryEpochsDoNotDoubleCount` | Two runners one host40/80, failover then late old stream, missing/conflicting binding | One primary per epoch, no doubled count, incomplete binding explicit; selection reached |
| V-24 / H6 | `C1514HostLoadHistoryTests.CountersKeepObservationTimeAndUnknowns` | 20 tasks batched, failed read, replay join15s/15001ms | Known at15s,null beyond/failure, original sampledAt, one query; enrichment reached |
| V-25 / H7 | `C1514HostLoadHistoryTests.RawAndRollupsSurviveRetentionAndLateReplay` | Raw7d/aggregate90d/open task, late K replay twice | Summaries survive pruning; late sum/count corrected once; retention transaction reached |
| V-26 / H8 | `C1514HostLoadHistoryTests.HistoryFailureDoesNotStopLiveSamples` | DB write barrier while sampler/poll/startup run, full queue then recovery | New live K/startup before release; history visibly incomplete then catches up |
| V-27 / H9 | `C1514HostLoadHistoryTests.BatchesAndQueriesStayBounded` | 121 records/>1MiB/1 then100 tasks, SQL interceptor | 120/1MiB caps, no partial cursor, one task query, bounded indexed read; real HTTP/SQL |
| V-28 / U1 | `C1514HostLoadAggregateTests.ExactWindowsMatchLiteralStatistics` | A=19zeros+100,B=20twenties; null/gap/boundary; late100 | Combined avg12.5,min0,max100,p9520,count40; mean hourly p95=10 rejected |
| V-29 / U2 | `C1514HostLoadAggregateTests.HistogramPreservesCountsAndErrorBound` | 4097 load bins0..40.96, uneven/opposite-order merges and raw expiry | Count4097,sum83906.56,min0,max40.96;<=4096 bins,upper quantile/error width; coarsening reached |
| V-30 / U3 | `C1514HostLoadAggregateTests.EmptyMetricsStayUnknown` | Empty/all-null/actual zero/partial cadence | Empty stats null; real0 validCount1; explicit partial coverage; serializer reached |
| V-31 / T1 | `C1514TaskLoadWindowTests.AttemptsAndMovesKeepSeparateWindows` | Attempt1 hostA/bootA; attempt2 hostB/bootB then hostA/bootC | Three W rows; no cross-attempt/host/boot data; real producer/observer |
| V-32 / T2 | `C1514TaskLoadWindowTests.BoundarySamplesRespectFifteenSeconds` | Boundary +/-15s/15001ms,equidistant tie,late replay | Frozen eligible K, earlier tie, missing/stale beyond15s; selector/API reached |
| V-33 / T3 | `C1514TaskLoadWindowTests.ShortAndOverlappingTasksKeepAmbientMeaning` | 1s task same boundary sample/no interior; two overlapping tasks | Short during null; ambientHost/overlapCount2; no exclusive CPU claim |
| V-34 / T4 | `C1514TaskLoadWindowTests.RestartReconcilesMissedEventsWithoutClosingOpenTask` | Real notification enqueue failure, crash pre/post W commit; one Working task | One recovered closed W,one open W,no duplicate; persisted reconciliation |
| V-35 / T5 | `C1514TaskLoadWindowTests.LongTasksKeepStatisticsAfterRawExpiry` | Open8days then91days,early histogram then expiry/terminal | Start provenance/exact sum/count/min/max retained; bounded approximate p95; final API |
| V-36 / Q1 | `C1514HostLoadRecommendationTests.PressureUsesLatestAndAverages` | Latest90/means20,30 then latest20/means90; process999/load99 | Distinct max pressures; load/process ignored; recommendation function reached |
| V-37 / Q2 | `C1514HostLoadRecommendationTests.UnknownAndStalePressureRecommendZero` | Unknown/nonfinite/foreign/partial60s/stale15001ms/future;valid15000ms | Bad input zero/unknown; receipt time cannot refresh; preconditions reached |
| V-38 / Q3 | `C1514HostLoadRecommendationTests.CapacityClampsAreIndependent` | P5; host,seats,project,stage each sole limiting1,others7; unknown constraint | Exactly1 each; unknown0; each clamp reached |
| V-39 / Q4 | `C1514HostLoadRecommendationTests.StartupDebtIsSubtractedOnce` | P44,one unrepresented launch repeated, then failure/representation | Debt10/headroom2,not20; clear only on representation/failure; no real reservation |
| V-40 / Q5 | `C1514HostLoadRecommendationTests.ThresholdsAndHostOnlyScopeStayExplicit` | P44, capacities4/3/2/1; omitted scope; equality75/85; floor64.9/65.1;command80 | Scoped1,hostOnly2,omissions named,proper floor/equality/thresholds |
| V-41 / E1 | `C1514HostLoadApiTests.RoutesReturnDurableProvenanceAndPages` | 2001 records,bad window/cursor,concurrent insert | Exact K roster across pages,default720/cap2000,typed400/404; real endpoints |
| V-42 / E2 | `C1514HostLoadApiTests.CurrentAndTaskReadsRespectExistingScope` | Two tasks/attempts/hosts; current/explicit attempt; absent/stale/valid task token | Exact requested windows and existing public status policy; no cross-task join |
| V-43 / E3 | `C1514HostLoadApiTests.OldPeersRemainLiveOnly` | HTTP404/phone-home unsupported;v1 success;bad history/canceled body | Live retained,history incomplete,no fabricated cursor; real old operation works |
| V-44 / E4 | `C1514HostLoadApiTests.ShadowProjectionCannotChangeAdmission` | Low/high/unknown load in pipeline and occupied legacy broker,observe state | Only nullable load changes; connectivity/drain/eligibility/budget unchanged; no acquire/release/launch |
| V-45 / J1 | `C1514HostLoadScriptTests.JsonIsOneValueWithArithmetic` | Real pwsh -Json with scoped1/hostOnly2/partial stats and stderr diagnostic | One complete object,exact K/stats/arithmetic/nulls,separate streams,exit0 |
| V-46 / J2 | `C1514HostLoadScriptTests.DefaultsAndScopeResolveFromLiveReads` | Change global default/revision; explicit host; omissions | Recorded GET path/query and placement match fresh default/scope |
| V-47 / J3 | `C1514HostLoadScriptTests.FailuresNeverPrintPositiveCapacity` | 503/404/malformed/truncated JSON/default unavailable/missing mandatory field | Nonzero,empty stdout,no positive capacity/reroute; joined child |
| V-48 / J4 | `C1514HostLoadScriptTests.TokenStaysInHeaders` | Owned token sentinel; success/error-echo; human/JSON output | Header forwarded; sentinel absent from stdout/stderr/logs; real child HTTP |
| V-49 / N1 | `C1514HostLoadNativeTests.Linux_ProductionFactoryPreservesHostSamples` | Native AddHostStats/SystemHostStatsProbe/Linux arm,independent /proc/core read,native spool reopen | Finite delta/online host cores/memory/load/exact K; actual factory object recorded |
| V-50 / N2 | `C1514HostLoadNativeTests.Windows_ProductionFactoryPreservesHostSamples` | Native AddHostStats/SystemHostStatsProbe/Windows arm,all-group core API,spool reopen | Finite delta/host cores/memory/commit/load null/exact K; actual factory object |
| V-51 / N3 | `C1514HostLoadNativeOverheadTests.Linux_SamplerOverheadStaysWithinBudget` | Two off/on60s pairs,warmup15s,isolated full history runner/loopback authority | Incremental CPU<1 point of one core,>=12 samples/pair,tick p95<25ms,bounded spool/cadence |
| V-52 / N4 | `C1514HostLoadNativeOverheadTests.Windows_SamplerOverheadStaysWithinBudget` | Same native Windows owned-child experiment | Same measured budgets and receipts on Windows; no Linux substitution |

#### Boundary wiring and preservation assertions

Use literal T0=2026-11-06T12:00:00Z, distinct owned host/store/boot IDs and
sequences101..105. Inject faults at actual external I/O/lifecycle boundaries,
never in the final reader. Before the operation prove local/shared counts differ,
the selected primary/epoch, held barrier, original K bytes and nonselected windows
that cannot rescue the case. Capture reached-boundary evidence joined to K/W;
a trace marker is not the outcome.

H1/H2/H3 read through a fresh independent DbContext AND public API after recreation.
T1/T4 call the actual persisted task/session event producer then registered
observer, with a refusing session client. Seeding current task state alone cannot
prove new notification wiring. H1 closes D5 with real script stdout; J recorder
cases supply only HTTP selection/stream/error proof.

Each PC's decisive assertion has its literal key below. Assertions are unconditional;
they never inspect the mutation selector. Collect physical observations before
asserting; keep finally-owned evidence when red aborts. An earlier fixture failure,
unreached branch, wrong assertion, zero tests, build error or timeout is not red.
If an intact independent guard masks the defect, retain binding-required and repair
the design; never disable that other guard to manufacture a kill.

### Guards the regression

| ID | Exact regression selection | Decisive assertion |
|---|---|---|
| R-1 | HostStatsSamplerTests, HostStatsStoreTests, HostStatsProbeParseTests; runner/server HostStatsEndpointTests; dispatcher three Host_stats methods | Existing live/ring/null/disabled/query/operation29/30 behavior; only Cores assertion changes to native host denominator |
| R-2 | HostStatsCacheTests, HostStatsPollServiceTests, SessionRunnerHttpClientHostStatsTests | Repeated sample does not refresh age; retired/draining/fault semantics; one-attempt3s timeout |
| R-3 | HostStatsAntiphonCountersTests and H6/H9/E2/E4 | Existing counts preserved; new history unknown stays unknown; admission unaffected |
| R-4 | W2/E3/J1..4 | Both transports, old peers, defaults/token/JSON and recipient receipt |
| R-5 | TestClassificationGuardTests.Registry_matches_compiled_metadata and NativeLinuxPolicyWitnessTests all3 in both assemblies; TestLaneCategoryGuardTests.every_test_class_is_tagged_unit_xor_integration; LiveLaneRunnerTests.WrapperOnlyPaidManifest | Class/native/Slow mappings and policyHash/PinnedInputDigest agree; offline wrapper only |
| R-6 | H8/H9/N3/N4 | No N+1 or inline DB wait, bounded spool/overhead and truthful lag |

V methods also protect the corresponding new behavior. Unchanged verification
guard implementations retain their existing mutation obligations; this is not a
whole-repository mutation campaign.

### Guard inventory

Each row is one independently bypassable S1 safety invariant, including new and
currently untested delivery/recovery paths. References name the Plan's decisions,
sampling/persistence/read sections, V1..6 and R1/R5/R6. Native overhead limits
N3/N4 are performance qualification rather than authorization guards; bounded
storage and deterministic nonblocking guards still receive PCs.

| Guard | Plan ref + safety-critical invariant | Distinct control |
|---|---|---|
| G-1 | sampling/V1: host CPU denominator | PC-1 |
| G-2 | sampling/V1: first delta unknown | PC-2 |
| G-3 | sampling/V1: Windows load unknown | PC-3 |
| G-4 | sampling/V1: invalid/reset/nonfinite metric rejected | PC-4 |
| G-5 | sampling/V1: process CPU not host CPU | PC-5 |
| G-6 | sampling/D1: monotonic elapsed delta | PC-6 |
| G-7 | sampling/V3: K includes boot | PC-7 |
| G-8 | sampling/V2: shared authority override selected | PC-8 |
| G-9 | sampling/D7: failed shared read stays unknown | PC-9 |
| G-10 | sampling/V2: authority host/identity bound | PC-10 |
| G-11 | sampling/V2: authority freshness checked | PC-11 |
| G-12 | sampling/R6: one authority read per interval | PC-12 |
| G-13 | D1 persistence: flush before advertising spool record | PC-13 |
| G-14 | D1 persistence: quarantine partial tail | PC-14 |
| G-15 | D1 recovery: cursor cannot skip uncommitted holes | PC-15 |
| G-16 | D1 recovery: retain old-boot pending chunks | PC-16 |
| G-17 | D1 bounds: byte cap | PC-17 |
| G-18 | D1 bounds: age cap | PC-18 |
| G-19 | D1 recovery: persistent lost ranges | PC-19 |
| G-20 | D1 R6: append failure/full queue does not block live | PC-20 |
| G-21 | D10/R1: history disable preserves live monitor | PC-21 |
| G-22 | read/V6: runner page count120 | PC-22 |
| G-23 | read/R1: advertise iff registered/enabled | PC-23 |
| G-24 | read/R1: preserve existing operation ordinals | PC-24 |
| G-25 | D2 delivery: produced page enters real ingest queue | PC-25 |
| G-26 | D2 delivery: failed enqueue remains replayable | PC-26 |
| G-27 | D2 recovery: server after cursor only follows commit | PC-27 |
| G-28 | D2 persistence: sample/cursor atomic transaction | PC-28 |
| G-29 | D2 persistence: duplicate K stored once | PC-29 |
| G-30 | D2 recovery: lost ack does not stop replay | PC-30 |
| G-31 | D2 recovery: restart schedules pending replay | PC-31 |
| G-32 | D2 persistence: conflicting K cannot overwrite | PC-32 |
| G-33 | sampling/V2: primary-only contribution | PC-33 |
| G-34 | sampling/V2: source epoch partition | PC-34 |
| G-35 | sampling/V5: counter failure stays unknown | PC-35 |
| G-36 | sampling/V5: replay counter join within15s | PC-36 |
| G-37 | D3 persistence: retention waits for durable derived work | PC-37 |
| G-38 | D3 persistence: late replay corrects prior bucket | PC-38 |
| G-39 | persistence/D2:90-day aggregate retention | PC-39 |
| G-40 | D1/D2 R6: DB write off sampler/startup path | PC-40 |
| G-41 | D2 bounds: ingest record count120 | PC-41 |
| G-42 | D2 bounds: ingest bytes1MiB | PC-42 |
| G-43 | sampling/R6: one batched counter query | PC-43 |
| G-44 | persistence/V4: valid-count arithmetic mean | PC-44 |
| G-45 | persistence/V4: raw nearest-rank combined p95 | PC-45 |
| G-46 | persistence/V4: truthful half-open coverage | PC-46 |
| G-47 | persistence/V4: rebin/merge retains all counts | PC-47 |
| G-48 | persistence/V4: approximate quantile/error declared | PC-48 |
| G-49 | persistence/V4: histogram4096-bin bound | PC-49 |
| G-50 | persistence/V4: empty statistics null | PC-50 |
| G-51 | D4/V5: attempt partitions W | PC-51 |
| G-52 | D4/V5: host move splits W | PC-52 |
| G-53 | persistence/V5: boundary sample<=15s | PC-53 |
| G-54 | persistence/V5: no invented short-task during | PC-54 |
| G-55 | D9/V5: overlapping attribution is ambient | PC-55 |
| G-56 | D4 recovery: reconcile persisted missed events | PC-56 |
| G-57 | D4 recovery: no poll-based closing Working task | PC-57 |
| G-58 | persistence/V5: long task retains pre-expiry stats | PC-58 |
| G-59 | D3/V6: latest in pressure max | PC-59 |
| G-60 | D3/V6: average in pressure max | PC-60 |
| G-61 | D9/V6: unknown/foreign/partial gives zero | PC-61 |
| G-62 | D9/V6: freshness based on sampledAt | PC-62 |
| G-63 | D9/V6: host remaining clamp | PC-63 |
| G-64 | D9/V6: runner seat clamp | PC-64 |
| G-65 | D9/V6: project remaining clamp | PC-65 |
| G-66 | D9/V6: stage remaining clamp | PC-66 |
| G-67 | D9/V6: unique unrepresented startup debt | PC-67 |
| G-68 | D9/V6: floor headroom | PC-68 |
| G-69 | D9/V6: missing constraints named | PC-69 |
| G-70 | D4/V6: command85 distinct from dispatch75 | PC-70 |
| G-71 | read/V6: validate public bounds before DB | PC-71 |
| G-72 | read/V6: cursor binds query/snapshot | PC-72 |
| G-73 | read/V5: requested attempt constrains W read | PC-73 |
| G-74 | read/R1: unsupported is incomplete | PC-74 |
| G-75 | D10/R1: shadow cannot change dispatch flags | PC-75 |
| G-76 | D10: shadow cannot change budget | PC-76 |
| G-77 | read/V6: one stdout JSON value | PC-77 |
| G-78 | read/V6: use live runner default | PC-78 |
| G-79 | read/V6: errors never print capacity | PC-79 |
| G-80 | privacy: token header only | PC-80 |
| G-81 | sampling/V1: Linux real factory selected | PC-81 |
| G-82 | sampling/V1: Windows real factory selected | PC-82 |
| G-83 | privacy: sample excludes command/credential label | PC-83 |
| G-84 | D3 recovery: derived watermark follows summary commit | PC-84 |
| G-85 | D4/V5: boot change splits W | PC-85 |
| G-86 | D1 recovery: ack cursor store-bound | PC-86 |
| G-87 | sampling/R6: whole authority request deadline | PC-87 |
| G-88 | read/V6: page byte cap independent of count | PC-88 |
| G-89 | R5: classification policy and live-lane pin coupled | PC-89 |
| G-90 | D4/V5: start observation binds accepted session start,not queue/dispatch | PC-90 |
| G-91 | persistence/V4: quantile error equals actual bin width | PC-91 |
| G-92 | persistence/V4: histogram quantile uses containing upper bound | PC-92 |
| G-93 | persistence/V4: exact sum survives coarsening | PC-93 |
| G-94 | persistence/V4: exact minimum survives coarsening | PC-94 |
| G-95 | persistence/V4: exact maximum survives coarsening | PC-95 |
| G-96 | D2 R6: ingest memory queue bounded independently of spool | PC-96 |
| G-97 | D4 persistence: open task never aged out as terminal | PC-97 |
| G-98 | read/V6: unsupported sample schema cannot enter spool/ingest | PC-98 |
| G-99 | sampling/V2: absent/conflicting host binding cannot contribute | PC-99 |
| G-100 | privacy: error diagnostics redact token-bearing response bodies | PC-100 |
| G-101 | D4/V6: equality at command85 is hold | PC-101 |
| G-102 | sampling/R6: caller cancellation reaches whole authority request | PC-102 |

G-89 protects this slice's coupled pin publication. The unchanged hash validator
is exercised by R-5 and retains its inherited controls. Native method mapping
additions make the pair an S1 changed input. No guard is waived for cost or native
availability.

### Positive controls

**All 102 PCs run after land.** Code runs V/R and prepares bindings; Review judges
before land; Mutation runs baseline, break/red/restore/green on a method-scoped
bound pack. Every row currently is **higher-tier-required / binding-missing**.
Pure arithmetic cases may become mechanically eligible only after Frontier
validates landed patch, payload, oracle, reachability and restoration provenance.
Unbound payloads stay binding required after Code.

Recipe IDs: C1514-S1-PC-n; variant IDs: PC-n/case. Literal case, layer and ordinary
classification come from the V roster. F below is the whole filter, Min1 in every
phase. Statements are candidate compiling defects in the named typed production
boundary, not claims of a compiled landed patch. Code binds the actual symbol,
typed locals, exact before/after bytes and patch hash. If implementation shape
differs, bind an equivalent compiling defect with design review. Never mutate a
test expectation or replace the final reader. No permanent production selector.

Temporary C# helper inside each mutated owner:

```csharp
private static bool C1514Pc(string id) => string.Equals(
    Environment.GetEnvironmentVariable("ANTIPHON_C1514_S1_PC"), id,
    StringComparison.Ordinal);
```

Unknown/Reject/Missing/Quarantine denote the real typed result constructor at
the bound site, not fixture-generated outcomes. PC-14 requires typed recovery of
a partial record, not a parser exception. PC-32 requires an actual overwriting
continuation; if the DB still refuses, it is masked and requires rebinding.
PC-13 uses a buffered append and process death before disposal; skipped flush
must not be rescued by cleanup. PC-83 uses only the owned synthetic command-line
sentinel at the actual target-label projection, never a real credential.

For PC-4 nominate finite invalid CPU=-1 as the preservation input, so a NaN JSON
serialization exception cannot masquerade as its kill. For PC-28 bind both sides
of the misplaced commit: the selected early commit occurs after cursor write
and before sample write, and the normal final commit is gated with
`if (!C1514Pc("PC-28")) await transaction.CommitAsync(ct);`. The expected injected
sample-write failure is caught by the production recovery path; the final fresh
API's missing K is the red, not an already-completed-transaction exception.
PC-30's return is from the replay worker loop, not one polling helper that would
immediately retry. PC-97 supplies a still-open day91 task and its own retained
histogram so the wrong terminal retention branch is actually reached.

Before the nominated assertion, record reached:PC-n with true boundary inputs,
K/W, selector and source/build identity. The output/value preservation assertion
is decisive; diagnostics alone never kill a delivery control. Script selectors
are forwarded only to the owned child. PC-89 runs the existing offline live-lane
harness and its full-inputs assertion, not J1's JSON method.

| PC / variant | Break its G with this env-gated defect | Exact method filter; Min1 | Expected preservation red |
|---|---|---|---|
| PC-1 / S1 | `cpuPercent = C1514Pc("PC-1") ? cpuPercent / hostLogicalCores : cpuPercent;` | `/*/*/C1514HostLoadSampleTests/CpuPercentUsesHostDenominator` | `preserved:PC-1:cpu`: 50% becomes2.08333 on24 cores |
| PC-2 / S2 | `cpuPercent = C1514Pc("PC-2") ? cpuPercent ?? 0d : cpuPercent;` | `/*/*/C1514HostLoadSampleTests/UnknownMetricsStayNull` | `preserved:PC-2:first-delta`: First CPU becomes0 |
| PC-3 / S2 | `load1 = C1514Pc("PC-3") ? 0d : load1;` | `/*/*/C1514HostLoadSampleTests/UnknownMetricsStayNull` | `preserved:PC-3:windows-load`: Unsupported load becomes0 |
| PC-4 / S2 | `if (invalid && !C1514Pc("PC-4")) return null;` | `/*/*/C1514HostLoadSampleTests/UnknownMetricsStayNull` | `preserved:PC-4:invalid`: Invalid input escapes nullable finite schema |
| PC-5 / S3 | `cpuPercent = C1514Pc("PC-5") ? processCpuPercent : cpuPercent;` | `/*/*/C1514HostLoadSampleTests/ProcessCpuDoesNotBecomeHostCpu` | `preserved:PC-5:host-cpu`: Host250 instead of40 |
| PC-6 / S4 | `elapsedSeconds = C1514Pc("PC-6") ? (now - previousUtc).TotalSeconds : elapsedSeconds;` | `/*/*/C1514HostLoadSampleTests/CadenceAndDeltasUseMonotonicTime` | `preserved:PC-6:elapsed`: UTC rollback corrupts50% delta |
| PC-7 / S5 | `bootId = C1514Pc("PC-7") ? Guid.Empty : bootId;` | `/*/*/C1514HostLoadSampleTests/SourceIdentityAndPayloadAreStable` | `preserved:PC-7:boot-key`: Different boots lose distinct K |
| PC-8 / A1 | `useOverride = C1514Pc("PC-8") ? false : useOverride;` | `/*/*/C1514HostLoadAuthorityTests/OverrideReadsSharedAuthority` | `preserved:PC-8:shared-count`: Local0 replaces shared4 |
| PC-9 / A2 | `occupied = C1514Pc("PC-9") ? localOccupied : occupied;` | `/*/*/C1514HostLoadAuthorityTests/AuthorityFailureRemainsUnknown` | `preserved:PC-9:unknown-count`: Local0 masks unavailable authority |
| PC-10 / A3 | `if (!identityMatches && !C1514Pc("PC-10")) return Unknown();` | `/*/*/C1514HostLoadAuthorityTests/AuthorityIdentityAndFreshnessAreChecked` | `preserved:PC-10:authority-host`: Foreign authority accepted |
| PC-11 / A3 | `if (stale && !C1514Pc("PC-11")) return Unknown();` | `/*/*/C1514HostLoadAuthorityTests/AuthorityIdentityAndFreshnessAreChecked` | `preserved:PC-11:authority-age`: 15001ms accepted fresh |
| PC-12 / A4 | `if (cacheValid && !C1514Pc("PC-12")) return cached;` | `/*/*/C1514HostLoadAuthorityTests/AuthorityReadsAreCachedPerInterval` | `preserved:PC-12:read-count`: 20 reads instead of1 |
| PC-13 / H3 | `if (!C1514Pc("PC-13")) stream.Flush(true);` | `/*/*/C1514HostLoadHistoryTests/RecoveryAtEveryHandoffPreservesReceipt` | `preserved:PC-13:durable-record`: Buffered K absent after owned process death |
| PC-14 / B2 | `if (partial && !C1514Pc("PC-14")) return Quarantine(line);` | `/*/*/C1514HostLoadSpoolTests/PartialTailIsQuarantined` | `preserved:PC-14:tail`: Typed recovered partial K102 incorrectly accepted |
| PC-15 / B3 | `if (hasUncommittedHole && !C1514Pc("PC-15")) return false;` | `/*/*/C1514HostLoadSpoolTests/CursorCannotSkipUncommitted` | `preserved:PC-15:cursor-frontier`: Cursor102 discards101 |
| PC-16 / B4 | `if (C1514Pc("PC-16") && chunkBootId != currentBootId) continue;` | `/*/*/C1514HostLoadSpoolTests/ReplayPreservesBootIdentity` | `preserved:PC-16:old-boot`: Old K101 absent |
| PC-17 / B5 | `if (overByteCap && !C1514Pc("PC-17")) DropOldest();` | `/*/*/C1514HostLoadSpoolTests/CapsPersistExplicitGaps` | `preserved:PC-17:spool-bytes`: Actual bytes exceed cap |
| PC-18 / B5 | `if (overAgeCap && !C1514Pc("PC-18")) DropOldest();` | `/*/*/C1514HostLoadSpoolTests/CapsPersistExplicitGaps` | `preserved:PC-18:spool-age`: Expired chunk remains |
| PC-19 / B5 | `if (!C1514Pc("PC-19")) PersistGap(gap);` | `/*/*/C1514HostLoadSpoolTests/CapsPersistExplicitGaps` | `preserved:PC-19:gap`: Dropped K has no durable gap |
| PC-20 / B6 | `if (C1514Pc("PC-20")) await pendingAppend;` | `/*/*/C1514HostLoadSpoolTests/DiskFailurePreservesLiveSampling` | `preserved:PC-20:live-progress`: Next live K absent at barrier; bounded assertion, not timeout |
| PC-21 / S6 | `if (C1514Pc("PC-21") && !historyEnabled) return;` | `/*/*/C1514HostLoadSampleTests/DisabledHistoryKeepsLegacySampling` | `preserved:PC-21:live-enabled`: Next live sample absent |
| PC-22 / W1 | `limit = C1514Pc("PC-22") ? 121 : Math.Min(limit, 120);` | `/*/*/C1514HostLoadWireTests/HistoryRoutePreservesCursorAndBounds` | `preserved:PC-22:page-count`: 121 records escape |
| PC-23 / W2 | `historyEnabled = C1514Pc("PC-23") ? true : historyEnabled;` | `/*/*/C1514HostLoadWireTests/CapabilitiesAndOrdinalsStayCompatible` | `preserved:PC-23:capability`: Disabled peer advertises |
| PC-24 / W2 | `operation = C1514Pc("PC-24") ? (PhoneHomeOperation)28 : operation;` | `/*/*/C1514HostLoadWireTests/CapabilitiesAndOrdinalsStayCompatible` | `preserved:PC-24:ordinal`: Operation29 result/literal ordinal wrong |
| PC-25 / H1 | `if (!C1514Pc("PC-25")) await queue.Writer.WriteAsync(batch, ct);` | `/*/*/C1514HostLoadHistoryTests/ReadyRecipientReadsDurableSample` | `preserved:PC-25:recipient`: Final API missing K101 |
| PC-26 / H2 | `if (C1514Pc("PC-26") && !enqueued) committedCursor = page.NextCursor;` | `/*/*/C1514HostLoadHistoryTests/BusyRecipientEventuallyReadsSameSample` | `preserved:PC-26:busy-recipient`: Busy K lost after retry |
| PC-27 / H3 | `if (C1514Pc("PC-27")) committedCursor = page.NextCursor;` | `/*/*/C1514HostLoadHistoryTests/RecoveryAtEveryHandoffPreservesReceipt` | `preserved:PC-27:precommit`: Precommit crash loses K |
| PC-28 / H3 | `if (C1514Pc("PC-28")) await transaction.CommitAsync(ct);` | `/*/*/C1514HostLoadHistoryTests/RecoveryAtEveryHandoffPreservesReceipt` | `preserved:PC-28:transaction`: Cursor committed before sample-write fault loses K |
| PC-29 / H3 | `sequence = C1514Pc("PC-29") ? sequence + replayOrdinal : sequence;` | `/*/*/C1514HostLoadHistoryTests/RecoveryAtEveryHandoffPreservesReceipt` | `preserved:PC-29:dedup`: Replay produces extra K/count |
| PC-30 / H3 | `if (C1514Pc("PC-30") && acknowledgementLost) return;` | `/*/*/C1514HostLoadHistoryTests/RecoveryAtEveryHandoffPreservesReceipt` | `preserved:PC-30:lost-ack`: Next pending K never received |
| PC-31 / H3 | `if (!C1514Pc("PC-31")) await ReplayPendingAsync(ct);` | `/*/*/C1514HostLoadHistoryTests/RecoveryAtEveryHandoffPreservesReceipt` | `preserved:PC-31:restart`: Outstanding durable K missing |
| PC-32 / H4 | `if (payloadDiffers && !C1514Pc("PC-32")) throw new InvalidOperationException("host_load_identity_conflict");` | `/*/*/C1514HostLoadHistoryTests/IdentityConflictsNeverOverwriteStoredSamples` | `preserved:PC-32:conflict`: Real upsert replaces original40 with99 |
| PC-33 / H5 | `if (!isPrimary && !C1514Pc("PC-33")) continue;` | `/*/*/C1514HostLoadHistoryTests/PrimaryEpochsDoNotDoubleCount` | `preserved:PC-33:primary`: 40/80 both counted |
| PC-34 / H5 | `sourceEpoch = C1514Pc("PC-34") ? Guid.Empty : sourceEpoch;` | `/*/*/C1514HostLoadHistoryTests/PrimaryEpochsDoNotDoubleCount` | `preserved:PC-34:epoch`: Epoch provenance lost/late stream mixed |
| PC-35 / H6 | `tasksInFlight = C1514Pc("PC-35") ? tasksInFlight ?? 0 : tasksInFlight;` | `/*/*/C1514HostLoadHistoryTests/CountersKeepObservationTimeAndUnknowns` | `preserved:PC-35:counters`: Unknown becomes0 |
| PC-36 / H6 | `if (tooOld && !C1514Pc("PC-36")) return null;` | `/*/*/C1514HostLoadHistoryTests/CountersKeepObservationTimeAndUnknowns` | `preserved:PC-36:counter-time`: Today's count on old sample |
| PC-37 / H7 | `if (!derivedCommitted && !C1514Pc("PC-37")) return;` | `/*/*/C1514HostLoadHistoryTests/RawAndRollupsSurviveRetentionAndLateReplay` | `preserved:PC-37:retention-frontier`: Raw erased before summary survives |
| PC-38 / H7 | `if (C1514Pc("PC-38") && bucketExists) return;` | `/*/*/C1514HostLoadHistoryTests/RawAndRollupsSurviveRetentionAndLateReplay` | `preserved:PC-38:late`: Late K absent from sum/count |
| PC-39 / H7 | `retentionDays = C1514Pc("PC-39") ? 7 : 90;` | `/*/*/C1514HostLoadHistoryTests/RawAndRollupsSurviveRetentionAndLateReplay` | `preserved:PC-39:retention-age`: Day8 summary missing |
| PC-40 / H8 | `if (C1514Pc("PC-40")) await pendingHistoryWrite;` | `/*/*/C1514HostLoadHistoryTests/HistoryFailureDoesNotStopLiveSamples` | `preserved:PC-40:live-progress`: Next live K/startup incomplete at held barrier |
| PC-41 / H9 | `if (batch.Count > 120 && !C1514Pc("PC-41")) return Reject();` | `/*/*/C1514HostLoadHistoryTests/BatchesAndQueriesStayBounded` | `preserved:PC-41:batch-count`: 121 inserted |
| PC-42 / H9 | `if (payloadBytes > 1048576 && !C1514Pc("PC-42")) return Reject();` | `/*/*/C1514HostLoadHistoryTests/BatchesAndQueriesStayBounded` | `preserved:PC-42:batch-bytes`: Oversized record inserted |
| PC-43 / H9 | `if (C1514Pc("PC-43")) foreach (var task in tasks) await ReadCountersAsync(ct);` | `/*/*/C1514HostLoadHistoryTests/BatchesAndQueriesStayBounded` | `preserved:PC-43:query-count`: 100 extra SQL reads |
| PC-44 / U1 | `average = C1514Pc("PC-44") ? sum / expectedCount : sum / validCount;` | `/*/*/C1514HostLoadAggregateTests/ExactWindowsMatchLiteralStatistics` | `preserved:PC-44:average`: Gap mean differs from literal |
| PC-45 / U1 | `p95 = C1514Pc("PC-45") ? hourlyP95s.Average() : p95;` | `/*/*/C1514HostLoadAggregateTests/ExactWindowsMatchLiteralStatistics` | `preserved:PC-45:p95`: 10 instead of20 |
| PC-46 / U1 | `if (C1514Pc("PC-46")) validCount = expectedCount;` | `/*/*/C1514HostLoadAggregateTests/ExactWindowsMatchLiteralStatistics` | `preserved:PC-46:coverage`: Missing slot reported complete |
| PC-47 / U2 | `if (C1514Pc("PC-47")) bins.Remove(bins.Keys.Max());` | `/*/*/C1514HostLoadAggregateTests/HistogramPreservesCountsAndErrorBound` | `preserved:PC-47:histogram-count`: 4097 observations lose bin |
| PC-48 / U2 | `p95Approximate = C1514Pc("PC-48") ? false : p95Approximate;` | `/*/*/C1514HostLoadAggregateTests/HistogramPreservesCountsAndErrorBound` | `preserved:PC-48:approximate`: Coarsened p95 claims exact |
| PC-49 / U2 | `if (bins.Count > 4096 && !C1514Pc("PC-49")) Rebin();` | `/*/*/C1514HostLoadAggregateTests/HistogramPreservesCountsAndErrorBound` | `preserved:PC-49:bin-cap`: 4097 bins remain |
| PC-50 / U3 | `average = C1514Pc("PC-50") ? average ?? 0d : average;` | `/*/*/C1514HostLoadAggregateTests/EmptyMetricsStayUnknown` | `preserved:PC-50:empty-stat`: Empty average0 |
| PC-51 / T1 | `attempt = C1514Pc("PC-51") ? 1 : attempt;` | `/*/*/C1514TaskLoadWindowTests/AttemptsAndMovesKeepSeparateWindows` | `preserved:PC-51:attempt`: Attempt2 merged into1 |
| PC-52 / T1 | `physicalHostKey = C1514Pc("PC-52") ? firstHostKey : physicalHostKey;` | `/*/*/C1514TaskLoadWindowTests/AttemptsAndMovesKeepSeparateWindows` | `preserved:PC-52:host-window`: HostB data attributedA |
| PC-53 / T2 | `if (distanceSeconds > 15 && !C1514Pc("PC-53")) return Missing();` | `/*/*/C1514TaskLoadWindowTests/BoundarySamplesRespectFifteenSeconds` | `preserved:PC-53:boundary-age`: 15001ms accepted start |
| PC-54 / T3 | `duringAverage = C1514Pc("PC-54") ? duringAverage ?? 0d : duringAverage;` | `/*/*/C1514TaskLoadWindowTests/ShortAndOverlappingTasksKeepAmbientMeaning` | `preserved:PC-54:short-during`: Empty interior0 |
| PC-55 / T3 | `attribution = C1514Pc("PC-55") ? "exclusiveTaskCpu" : "ambientHost";` | `/*/*/C1514TaskLoadWindowTests/ShortAndOverlappingTasksKeepAmbientMeaning` | `preserved:PC-55:attribution`: False exclusive claim |
| PC-56 / T4 | `if (!C1514Pc("PC-56")) await ReconcileEventsAsync(ct);` | `/*/*/C1514TaskLoadWindowTests/RestartReconcilesMissedEventsWithoutClosingOpenTask` | `preserved:PC-56:event-replay`: Terminal/move W absent at API |
| PC-57 / T4 | `closed = C1514Pc("PC-57") ? true : closed;` | `/*/*/C1514TaskLoadWindowTests/RestartReconcilesMissedEventsWithoutClosingOpenTask` | `preserved:PC-57:open-window`: Working W closed |
| PC-58 / T5 | `if (C1514Pc("PC-58")) histogram.Clear();` | `/*/*/C1514TaskLoadWindowTests/LongTasksKeepStatisticsAfterRawExpiry` | `preserved:PC-58:long-task`: Day1 counts lost on day8 |
| PC-59 / Q1 | `pressure = C1514Pc("PC-59") ? averageCpu : Math.Max(latestCpu, averageCpu);` | `/*/*/C1514HostLoadRecommendationTests/PressureUsesLatestAndAverages` | `preserved:PC-59:latest-pressure`: Latest90 masked |
| PC-60 / Q1 | `pressure = C1514Pc("PC-60") ? latestCpu : Math.Max(latestCpu, averageCpu);` | `/*/*/C1514HostLoadRecommendationTests/PressureUsesLatestAndAverages` | `preserved:PC-60:average-pressure`: Average90 masked |
| PC-61 / Q2 | `if (!pressureKnown && !C1514Pc("PC-61")) return Zero();` | `/*/*/C1514HostLoadRecommendationTests/UnknownAndStalePressureRecommendZero` | `preserved:PC-61:unknown-capacity`: Numeric default yields positive advice |
| PC-62 / Q2 | `sampledAt = C1514Pc("PC-62") ? receivedAt : sampledAt;` | `/*/*/C1514HostLoadRecommendationTests/UnknownAndStalePressureRecommendZero` | `preserved:PC-62:freshness`: Stale K gains positive advice |
| PC-63 / Q3 | `additional = C1514Pc("PC-63") ? additional : Math.Min(additional, hostRemaining);` | `/*/*/C1514HostLoadRecommendationTests/CapacityClampsAreIndependent` | `preserved:PC-63:host-clamp`: Host sole limit1 ignored |
| PC-64 / Q3 | `additional = C1514Pc("PC-64") ? additional : Math.Min(additional, runnerRemaining);` | `/*/*/C1514HostLoadRecommendationTests/CapacityClampsAreIndependent` | `preserved:PC-64:seats-clamp`: Seat sole limit1 ignored |
| PC-65 / Q3 | `additional = C1514Pc("PC-65") ? additional : Math.Min(additional, projectRemaining);` | `/*/*/C1514HostLoadRecommendationTests/CapacityClampsAreIndependent` | `preserved:PC-65:project-clamp`: Project sole limit1 ignored |
| PC-66 / Q3 | `additional = C1514Pc("PC-66") ? additional : Math.Min(additional, stageRemaining);` | `/*/*/C1514HostLoadRecommendationTests/CapacityClampsAreIndependent` | `preserved:PC-66:stage-clamp`: Stage sole limit1 ignored |
| PC-67 / Q4 | `startupDebt = C1514Pc("PC-67") ? 0d : startupDebt;` | `/*/*/C1514HostLoadRecommendationTests/StartupDebtIsSubtractedOnce` | `preserved:PC-67:startup-debt`: Headroom3 instead of2 |
| PC-68 / Q5 | `headroom = (int)(C1514Pc("PC-68") ? Math.Ceiling((75d - pressure - startupDebt) / 10d) : Math.Floor((75d - pressure - startupDebt) / 10d));` | `/*/*/C1514HostLoadRecommendationTests/ThresholdsAndHostOnlyScopeStayExplicit` | `preserved:PC-68:headroom`: 65.1 gives1 instead of0 |
| PC-69 / Q5 | `scope = C1514Pc("PC-69") ? "projectRole" : scope;` | `/*/*/C1514HostLoadRecommendationTests/ThresholdsAndHostOnlyScopeStayExplicit` | `preserved:PC-69:scope`: HostOnly falsely scoped |
| PC-70 / Q5 | `commandThreshold = C1514Pc("PC-70") ? 75d : 85d;` | `/*/*/C1514HostLoadRecommendationTests/ThresholdsAndHostOnlyScopeStayExplicit` | `preserved:PC-70:threshold`: Command80 incorrectly holds |
| PC-71 / E1 | `if (invalidQuery && !C1514Pc("PC-71")) throw new ArgumentException("invalid_host_load_query");` | `/*/*/C1514HostLoadApiTests/RoutesReturnDurableProvenanceAndPages` | `preserved:PC-71:query-bound`: Oversized window returns rows,not400 |
| PC-72 / E1 | `if (!cursorMatches && !C1514Pc("PC-72")) throw new ArgumentException("invalid_host_load_cursor");` | `/*/*/C1514HostLoadApiTests/RoutesReturnDurableProvenanceAndPages` | `preserved:PC-72:page-cursor`: Foreign cursor returns data |
| PC-73 / E2 | `attempt = C1514Pc("PC-73") ? currentAttempt : requestedAttempt;` | `/*/*/C1514HostLoadApiTests/CurrentAndTaskReadsRespectExistingScope` | `preserved:PC-73:task-scope`: Explicit old attempt returns current |
| PC-74 / E3 | `complete = C1514Pc("PC-74") ? true : complete;` | `/*/*/C1514HostLoadApiTests/OldPeersRemainLiveOnly` | `preserved:PC-74:unsupported`: Old peer falsely complete |
| PC-75 / E4 | `dispatchEligible = C1514Pc("PC-75") ? loadEligible : dispatchEligible;` | `/*/*/C1514HostLoadApiTests/ShadowProjectionCannotChangeAdmission` | `preserved:PC-75:eligibility`: Legacy eligibility changes |
| PC-76 / E4 | `if (C1514Pc("PC-76")) effectiveLimit = recommendedAdditionalTasks;` | `/*/*/C1514HostLoadApiTests/ShadowProjectionCannotChangeAdmission` | `preserved:PC-76:admission-state`: Existing budget overwritten |
| PC-77 / J1 | `if ($env:ANTIPHON_C1514_S1_PC -ceq 'PC-77') { Write-Output 'diagnostic' }` | `/*/*/C1514HostLoadScriptTests/JsonIsOneValueWithArithmetic` | `preserved:PC-77:stdout`: Prefix breaks parse-one-value |
| PC-78 / J2 | `if ($env:ANTIPHON_C1514_S1_PC -ceq 'PC-78') { $hostId = 'stale-fixture-host' }` | `/*/*/C1514HostLoadScriptTests/DefaultsAndScopeResolveFromLiveReads` | `preserved:PC-78:placement`: Wrong endpoint/default revision |
| PC-79 / J3 | `if ($env:ANTIPHON_C1514_S1_PC -ceq 'PC-79') { Write-Output '{"recommendation":{"recommendedAdditionalTasks":1}}'; exit 0 }` | `/*/*/C1514HostLoadScriptTests/FailuresNeverPrintPositiveCapacity` | `preserved:PC-79:error-output`: HTTP error emits success/capacity |
| PC-80 / J4 | `if ($env:ANTIPHON_C1514_S1_PC -ceq 'PC-80') { Write-Output $token }` | `/*/*/C1514HostLoadScriptTests/TokenStaysInHeaders` | `preserved:PC-80:secret-output`: Owned sentinel leaked |
| PC-81 / N1 | `if (C1514Pc("PC-81") && OperatingSystem.IsLinux()) return null;` | `/*/*/C1514HostLoadNativeTests/Linux_ProductionFactoryPreservesHostSamples` | `preserved:PC-81:native-sample`: Native sample absent |
| PC-82 / N2 | `if (C1514Pc("PC-82") && OperatingSystem.IsWindows()) return null;` | `/*/*/C1514HostLoadNativeTests/Windows_ProductionFactoryPreservesHostSamples` | `preserved:PC-82:native-sample`: Native sample absent |
| PC-83 / S5 | `name = C1514Pc("PC-83") ? commandLine : name;` | `/*/*/C1514HostLoadSampleTests/SourceIdentityAndPayloadAreStable` | `preserved:PC-83:payload`: Owned synthetic sentinel serialized |
| PC-84 / H3 | `if (C1514Pc("PC-84")) derivedWatermark = ingestWatermark;` | `/*/*/C1514HostLoadHistoryTests/RecoveryAtEveryHandoffPreservesReceipt` | `preserved:PC-84:derived-receipt`: Crash loses summary in API |
| PC-85 / T1 | `runnerBootId = C1514Pc("PC-85") ? firstBootId : runnerBootId;` | `/*/*/C1514TaskLoadWindowTests/AttemptsAndMovesKeepSeparateWindows` | `preserved:PC-85:boot-window`: BootC mixed into previous W |
| PC-86 / B3 | `if (!sameStore && !C1514Pc("PC-86")) return false;` | `/*/*/C1514HostLoadSpoolTests/CursorCannotSkipUncommitted` | `preserved:PC-86:cursor-store`: Foreign cursor evicts local pending K |
| PC-87 / A2 | `using var deadline = new CancellationTokenSource(C1514Pc("PC-87") ? TimeSpan.FromSeconds(6) : TimeSpan.FromSeconds(3), time);` | `/*/*/C1514HostLoadAuthorityTests/AuthorityFailureRemainsUnknown` | `preserved:PC-87:deadline`: At virtual3s request still pending; assert before cleanup |
| PC-88 / W1 | `if (wireBytes > 1048576 && !C1514Pc("PC-88")) break;` | `/*/*/C1514HostLoadWireTests/HistoryRoutePreservesCursorAndBounds` | `preserved:PC-88:page-bytes`: Oversized record escapes |
| PC-89 / J1 | `if ($env:ANTIPHON_C1514_S1_PC -ceq 'PC-89') { $script:PinnedInputDigest = ('0' * 64) }` | `/*/*/LiveLaneRunnerTests/WrapperOnlyPaidManifest` | `C1545 WrapperOnlyPaidManifest/full-inputs`: Full-inputs preservation fails with correct policyHash |
| PC-90 / T2 | `eventAt = C1514Pc("PC-90") ? queuedAt : acceptedStartedAt;` | `/*/*/C1514TaskLoadWindowTests/BoundarySamplesRespectFifteenSeconds` | `preserved:PC-90:start-event`: Boundary K/time belongs to queued interval |
| PC-91 / U2 | `p95ErrorBound = C1514Pc("PC-91") ? 0d : binWidth;` | `/*/*/C1514HostLoadAggregateTests/HistogramPreservesCountsAndErrorBound` | `preserved:PC-91:error-bound`: Approximate quantile claims zero error |
| PC-92 / U2 | `p95 = C1514Pc("PC-92") ? binLower : binUpper;` | `/*/*/C1514HostLoadAggregateTests/HistogramPreservesCountsAndErrorBound` | `preserved:PC-92:upper-bound`: Quantile falls below containing upper bound |
| PC-93 / U2 | `sum = C1514Pc("PC-93") ? roundedBinSum : sum;` | `/*/*/C1514HostLoadAggregateTests/HistogramPreservesCountsAndErrorBound` | `preserved:PC-93:exact-sum`: 83906.56 sum replaced by bin-rounded sum |
| PC-94 / U2 | `min = C1514Pc("PC-94") ? min + binWidth : min;` | `/*/*/C1514HostLoadAggregateTests/HistogramPreservesCountsAndErrorBound` | `preserved:PC-94:exact-min`: Observed0 minimum lost |
| PC-95 / U2 | `max = C1514Pc("PC-95") ? max + binWidth : max;` | `/*/*/C1514HostLoadAggregateTests/HistogramPreservesCountsAndErrorBound` | `preserved:PC-95:exact-max`: Observed40.96 maximum changed |
| PC-96 / H2 | `capacity = C1514Pc("PC-96") ? int.MaxValue : capacity;` | `/*/*/C1514HostLoadHistoryTests/BusyRecipientEventuallyReadsSameSample` | `preserved:PC-96:queue-bound`: Queue accepts third held batch instead of leaving replayable |
| PC-97 / T5 | `terminal = C1514Pc("PC-97") ? true : terminal;` | `/*/*/C1514TaskLoadWindowTests/LongTasksKeepStatisticsAfterRawExpiry` | `preserved:PC-97:open-retention`: Open day91 window deleted |
| PC-98 / W1 | `if (schemaVersion != 1 && !C1514Pc("PC-98")) return Reject();` | `/*/*/C1514HostLoadWireTests/HistoryRoutePreservesCursorAndBounds` | `preserved:PC-98:schema`: Version2 body accepted as v1 |
| PC-99 / H5 | `if (!bindingKnown && !C1514Pc("PC-99")) return Incomplete();` | `/*/*/C1514HostLoadHistoryTests/PrimaryEpochsDoNotDoubleCount` | `preserved:PC-99:host-binding`: Unbound collector gains complete host aggregate |
| PC-100 / J4 | `if ($env:ANTIPHON_C1514_S1_PC -ceq 'PC-100') { [Console]::Error.WriteLine($rawErrorBody) }` | `/*/*/C1514HostLoadScriptTests/TokenStaysInHeaders` | `preserved:PC-100:stderr-secret`: Owned token echoed to stderr |
| PC-101 / Q5 | `commandEligible = C1514Pc("PC-101") ? commandPressure <= 85d : commandPressure < 85d;` | `/*/*/C1514HostLoadRecommendationTests/ThresholdsAndHostOnlyScopeStayExplicit` | `preserved:PC-101:threshold-equality`: 85 equality eligible |
| PC-102 / A2 | `var requestToken = C1514Pc("PC-102") ? deadline.Token : linked.Token;` | `/*/*/C1514HostLoadAuthorityTests/AuthorityFailureRemainsUnknown` | `preserved:PC-102:caller-cancel`: Canceled caller remains pending before deadline |

Binding record per PC: recipe/variant, O/L and author SHA, exact relative path,
symbol/line, before/after bytes, SHA-256 patch, selector, complete literal input,
F and nominated first assertion, reached marker, staged-image hash and execution
classification/reason. Retain baseline/red/green TRX, counts/exits/logs and source/
restoration hashes. Build/fixture failure, timeout, zero/skip/wrong assertion is
not red. Restore bytes, unset selector, rebuild, run same F green. Never edit during
a run. Each build/run takes its own active build-slot gate lease; no loop-wide
lease. SourceLanding reports/restoration stay in its external evidence root,
never committed from its snapshot.

### Out of scope

- S2 authority registry/atomic hard ceiling, lease rollback, wrappers, checkpoint
  and land envelopes: Plan V7..11/V14 and PCs7..13/16 need their own TestDesign.
- S3 dispatch/preflight, authoritative reservations, repository lease ordering
  and instruction changes: Plan V12/13 and PCs14/15 remain pending. S1 debt is
  observational; no CARD-1581 implementation path edit.
- Promotion/canary rollout, budgets/provider changes, desktop commands, paid
  agents and Full/rc qualification: separate scoped commissions.
- Sudden power loss, fleet network availability and causal task CPU attribution:
  local crash/queue witnesses cannot prove them.
- Unchanged policy/parser/process/Git guard internals: existing mutation designs
  remain authoritative; required integration guards are selected by R-5.

#### Fixture closure, classification and overlap order

H3 needs a new thin `tests/Antiphon.HostLoad.Probe/{Program.cs,Antiphon.HostLoad.Probe.csproj}`,
staged by Antiphon.Tests exactly once through its ProjectReference/copy target.
Nearest fixture read: Antiphon.ChannelOutbound.Probe csproj and Program's owned
config, DB interceptor, flushed barrier and real producer construction; also
ScriptHarnessHost csproj and Linux owner lifecycle. The probe references server
and SessionRunner, builds only as part of AT's checkpoint build, and accepts only
parent-owned DB/directory/loopback inputs and nonce-bound cut messages. It calls
the real sampler/spool/dispatcher/ingest/rollup graph. It has no alternate storage
implementation. Parent captures K before the selected cut, kills only its owned
child, awaits exit/drain, then recreates from the same durable state. Existing
ScriptHarnessProcess custody supplies process ownership. No new direct build or
full AppHost fixture. B1 is a spool-object restart witness; H3/PC-13 supplies the
actual process-death/flush witness.

Production task-state/event observations must bind to the real persisted producer
and registration in Code. AgentTaskEvent already has Id, AgentTaskId, SessionId,
Type and At but not a complete historical attempt/host snapshot. Do not infer
missing old attempts from today's row. T4 must include a recoverable new-event
case AND an explicitly incomplete historical case; unsupported reconstruction
is a gap, not fabricated success. If the chosen adapter cannot supply the Plan's
new-event identity at the real persistence boundary without changing admission/
lease ordering, return **next: plan** for that seam, not a mock-only acceptance.

The new classes are explicitly Unit or Integration as the alias table states.
Native N1/N3 have method-level NativeLinuxOnly with CARD-1514 reasons and exact
opposite-Windows exclusions; N2/N4 use the existing HostStatsProbeLiveTests Windows
convention: an OperatingSystem.IsWindows branch and named opposite-OS Skip.Test
reason, exact method policies and Windows-only checkpoint selection. They are not
Explicit and require no extra runner option. There is no invented
NativeWindowsOnly attribute. N3/N4 live in the separate class-level Slow
C1514HostLoadNativeOverheadTests: the inspected registry rejects method-level
Slow. Add its exact FQN with an adjacent CARD-1514 reason to the runner registry. No whole-class exclusion hides portable methods. Missing native
prerequisites fail qualification; a skip is never a native pass. The native
checkpoint invocation verifies both N2/N4 ran on Windows; do not lower Min.

When `tests/test-execution-policy.json` changes, refresh **policyHash AND
PinnedInputDigest together in the same commit**. Compute the former with the
actual Get-NightlyPolicyHash helper in scripts/lib/nightly-policy.ps1; compute
the latter with `pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest`.
Keep the independently reviewed value in that script; never use current HEAD as
its own expected pin. Update test-selection-policy/timing inputs only for the real
new probe/dependency/category closure. Retain exact native mapping and both
assemblies' registry witnesses. PC-89 isolates the pin guard with a correct hash.

**Landing order:** CARD-1217 **S1 schema/import/strict TRX lands first** at its
reviewed SHA. Load telemetry S1 can be implemented in disjoint files while it is
pending, but its final checkpoint/policy binding uses that landed tool baseline.
Then this S1 lands after ordinary Review and required native evidence. CARD-1217
S2/S3 own validator/runtime and policy changes; serialize the shared policy/pin
commit if they are already in flight, and regenerate both hashes from the combined
actual landed policy, never cherry-pick an obsolete digest. S1 needs no conditional
skip exception. Load S2 waits for CARD-1217's relevant validator slice. Load S3
waits for CARD-1581's dispatcher/landing lease-order repair at a known landed SHA.
This is file ownership/dependency order, not a request to pause disjoint work or
change a frozen Review branch.

| Overlap | Owner/order and S1 boundary |
|---|---|
| Checkpoint import/TRX/schema/validators; CARD-1217 S1 then S2/S3 | S1 telemetry edits none; run its manifests against the landed tool, no receipt exception |
| tests/test-execution-policy.json, scripts/test-live-lane-run.ps1, slow registry, AT csproj/staged child lists | One writer at a time across CARD-1217/load S1; retain both changes and refresh both hashes in the final common slice |
| AgentTaskDispatcher/AgentTaskLandingProtocol and repository lease flow; CARD-1581 | No S1 edit to admission/preparation/lease ordering; observational adapter stays outside that branch. S3 integrates only after CARD-1581 |
| Pipeline DTO/catalogue projection and host counters | Add nullable load fields, preserve existing availability/eligibility/capacity; E4 owns regression. Do not insert S3 holds |
| HostStats legacy probe/store/routes and new history contracts | Load S1 exclusive owner; separate history capability, old ordinal/body compatibility |
| CARD-1531 clocks, CARD-1713 lease aging, CARD-1629 output reuse | No S1 behavioral edit; checkpoint budgets retain current semantics and active lease gate |
| SourceLanding/receipt provenance and D-2 | Missing native row stays incomplete; no advisory relabel/blanket Full claim |

S1 internal commit groups: **S1A** capture/identity/spool/authority/transport and
runner tests, then **S1B** persistence/rollups/task windows/API/script/staged probe/
classification and both hashes. Commit failing tests before each fix; run the
closed packet at each completed committed group. Only S1 as a whole receives
acceptance; S1A does not authorize admission or claim server delivery coverage.

### Checkpoints

Schema: [testing-and-build.md](../../testing-and-build.md#checkpoint-manifest-card-0585).
Each row has one exact filter and one isolated build identity; CP-n in Build is
same-packet output reuse, not another build. At most **three rows per run**.
Cheap guards are batched with the corresponding ordinary packet. No full suite
or unlisted exploratory build. Min means TUnit executed results, not assertions,
helper subcases, time or process count. Every expected method must appear once;
all rows require zero failed/skipped and source-qualified receipts.
| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1A | tests/Antiphon.SessionRunner.Tests -> bin-c1514-s1a/ | s1a-new | `/*/*/(C1514HostLoadSampleTests*)\|(C1514HostLoadAuthorityTests*)\|(C1514HostLoadSpoolTests*)\|(C1514HostLoadWireTests*)/*` | V-1..18 | 18 new methods; 0 failed/skipped | 18 | 5 | true |
| CP-2 | S1A | CP-1 | s1a-legacy | `/*/*/(HostStatsSamplerTests*)\|(HostStatsStoreTests*)\|(HostStatsProbeParseTests*)\|(HostStatsEndpointTests*)\|(TestClassificationGuardTests*)\|(NativeLinuxPolicyWitnessTests*)/*` | R-1,R-5 | 29 legacy +4 guards; 0 failed/skipped | 33 | 2 | true |
| CP-3 | S1A | CP-1 | s1a-phone | `/*/*/PhoneHomeCommandDispatcherTests/Host_stats_*` | R-1,R-4 | 3 exact Host_stats methods; 0 failed/skipped | 3 | 1 | true |
| CP-4 | S1B | tests/Antiphon.Tests -> bin-c1514-s1b/ | s1b-delivery | `/*/*/(C1514HostLoadHistoryTests*)\|(C1514TaskLoadWindowTests*)\|(C1514HostLoadApiTests*)/*` | V-19..27,V-31..35,V-41..44,R-3,R-6 | 18 methods / 28 executions (H3 has 11 arguments); 0 failed/skipped | 28 | 16 | true |
| CP-5 | S1B | CP-4 | s1b-surfaces | `/*/*/(C1514HostLoadAggregateTests*)\|(C1514HostLoadRecommendationTests*)\|(C1514HostLoadScriptTests*)\|(HostStatsCacheTests*)\|(HostStatsPollServiceTests*)\|(HostStatsAntiphonCountersTests*)\|(HostStatsEndpointTests*)\|(SessionRunnerHttpClientHostStatsTests*)/*` | V-28..30,V-36..40,V-45..48,R-1..4 | 12 new +28 existing methods; 0 failed/skipped | 40 | 4 | true |
| CP-6 | S1B | CP-4 | s1b-guards | `/*/*/(TestClassificationGuardTests*)\|(NativeLinuxPolicyWitnessTests*)\|(TestLaneCategoryGuardTests*)\|(LiveLaneRunnerTests*)/(Registry_matches_compiled_metadata*)\|(Policy_rows_require_pinned_attribute*)\|(Pinned_attribute_contract_matches_native_platform*)\|(Markers_do_not_expand_policy_exclusions*)\|(every_test_class_is_tagged_unit_xor_integration*)\|(WrapperOnlyPaidManifest*)` | R-5 | 6 exact guard methods; 0 failed/skipped | 6 | 2 | true |
| CP-7 | S1B | tests/Antiphon.SessionRunner.Tests -> bin-c1514-s1l/ | s1-linux | `/*/*/(C1514HostLoadNativeTests*)\|(C1514HostLoadNativeOverheadTests*)\|(HostStatsProbeLiveTests*)\|(TestClassificationGuardTests*)\|(NativeLinuxPolicyWitnessTests*)/(Linux_*)\|(Registry_matches_compiled_metadata*)\|(Policy_rows_require_pinned_attribute*)\|(Pinned_attribute_contract_matches_native_platform*)\|(Markers_do_not_expand_policy_exclusions*)` | V-49,V-51,R-1,R-6,R-5 | 3 Linux witnesses +4 shared guards at final source; 0 failed/skipped | 7 | 8 | true |
| CP-8 | S1B | tests/Antiphon.SessionRunner.Tests -> bin-c1514-s1w/ | s1-windows | `/*/*/(C1514HostLoadNativeTests*)\|(C1514HostLoadNativeOverheadTests*)\|(HostStatsProbeLiveTests*)\|(TestClassificationGuardTests*)\|(NativeLinuxPolicyWitnessTests*)/(Windows_*)\|(Registry_matches_compiled_metadata*)\|(Policy_rows_require_pinned_attribute*)\|(Pinned_attribute_contract_matches_native_platform*)\|(Markers_do_not_expand_policy_exclusions*)` | V-50,V-52,R-1,R-6,R-5 | 3 Windows witnesses +4 shared guards at final source; 0 failed/skipped | 7 | 9 | true |

S1A=CP-1..3 (54 results); S1B=CP-4..6 (74); Linux=CP-7 (7);
Windows=CP-8 (7). **142 total expected executions: 128 portable, 6 native
behavior witnesses and 8 final-source native-lane guard executions**.
The runner guards repeat after S1B because its policy/fixture inputs can change.
These are source-derived counts, not compiled discovery. If Code adds parameters,
partial classes or changes names, amend the roster/filter/floor together before
running. Do not count internal cut loops or script PASS lines as TUnit results.

The historical Plan S1 floor of four rows/30 minutes is replaced by four bounded
packets/eight rows/47 minutes. Reuse is explicit and each packet has one build.
S1A and S1B are portable default-placement packets; N1/N3 require native Linux,
N2/N4 native Windows on an eligible isolated worker. **Nothing runs on the desktop.**
A missing native worker leaves that packet pending and S1 acceptance incomplete;
it does not stop disjoint Code and is not a green skip.


#### Code amendment: H3 process initialization and independent cut cases

At Code 924f8ae2, H3 retains one method and its exact filter, but expands to 11 named Arguments: append-before-flush, append-after-flush, ingest-before-enqueue, ingest-after-enqueue, ingest-before-save, ingest-before-commit, ingest-after-commit, derive-before-call, derive-before-save, derive-after-write and derive-after-commit. Each case retains Timeout(60_000), ScriptHarness execution 60 seconds and cleanup 10 seconds. Cold process/EF connection/model initialization is an explicit fixture phase, acknowledged by an atomic nonce-bound initialized receipt; the guarded operation receipt still has the original five-second deadline. Case/child budgets include initialization. No prior deadline value or outcome assertion is relaxed. The four derive cases also persist a real task/session/terminal ledger and assert one restored task window, K101 at start/end and count/sum/min/max/p95 1/40/40/40/40 through the public task API. CP-4 now has 28 executions, the portable packet 128 and the complete packet 142. Earlier 18/118/132 receipts remain historical facts. A repair run may select only the previously red exact H3 method (11 executions), reusing the 17 unchanged green methods from the full CP-4 receipt at 6ccd0322eec472819b1170c773d948a7dc2d1316; this is a targeted red-row repair, not a green-row repetition.

#### Exact inherited method roster

All entries below are one execution, no Arguments. Prefix/class filters above
select only these bodies at the inspected base; Code verifies fresh TRX membership.
The same simple endpoint class name is disambiguated by each row's project.

```text
tests/Antiphon.SessionRunner.Tests/HostStatsSamplerTests.cs [5]
  SampleOnce_adds_one_sample_at_the_fake_clock_time
  Faulting_probe_leaves_store_unchanged_and_sampler_alive
  Session_process_cpu_is_delta_over_wall_and_null_sample_is_null
  Disabled_sampler_never_calls_the_probe
  Reused_pid_and_returning_target_start_with_no_cpu_delta
tests/Antiphon.SessionRunner.Tests/HostStatsStoreTests.cs [9]
  Empty_store_answers_null_latest_and_null_rollups
  Ring_evicts_oldest_at_capacity_and_series_is_time_ordered
  Rollups_match_hand_computed_avg_and_max_for_a_sawtooth
  Partial_window_averages_only_the_samples_it_holds
  Window_boundary_includes_sixty_seconds_and_excludes_beyond
  Metric_absent_from_samples_yields_null_rollups
  Series_rejects_unknown_metric_and_window
  Snapshot_carries_interval_and_retention_and_copies
  Writers_and_readers_wait_for_the_store_gate
tests/Antiphon.SessionRunner.Tests/HostStatsProbeParseTests.cs [11]
  Server2_compose_indexed_volume_environment_keys_bind_through_AddHostStats
  ProcStat_cpu_percent_is_busy_delta_over_total_delta
  ProcStat_short_first_line_still_parses
  MemInfo_yields_total_available_and_swap
  MemInfo_without_MemAvailable_yields_null_available
  LoadAvg_yields_three_loads_and_host_process_count
  Windows_cpu_percent_treats_kernel_as_including_idle
  First_read_has_no_cpu_and_second_read_has_the_delta
  Linux_probe_memory_floor_and_sample_read_the_same_meminfo
  Platform_switch_selects_the_arm_for_each_os
  Windows_probe_maps_a_failed_GetSystemTimes_to_null
tests/Antiphon.SessionRunner.Tests/HostStatsEndpointTests.cs [4]
  Host_stats_answers_newest_sample_and_one_minute_rollups
  Series_route_answers_points_in_window
  Bad_metric_or_window_is_400_problem
  Disabled_host_stats_is_404
tests/Antiphon.Tests/Application/HostStatsCacheTests.cs [7]
  Recorded_sample_projects_live
  Old_sample_projects_stale_with_the_same_values
  Repeated_sample_time_does_not_refresh_observation_or_hide_staleness
  Unrecorded_disconnected_host_projects_offline_with_null_current
  Unsupported_failure_projects_unsupported
  Changed_is_true_after_a_new_sample_and_false_after_an_empty_tick
  Disabled_poll_projects_every_host_offline_disabled
tests/Antiphon.Tests/Application/HostStatsPollServiceTests.cs [10]
  Tick_fills_both_hosts_live_and_publishes_once_to_group_hosts
  Silent_peer_goes_stale_while_desktop_stays_live
  Unsupported_peer_projects_unsupported
  Local_client_fault_does_not_fault_the_tick
  Disconnected_peer_projects_offline_and_recovers_live_on_reconnect
  C1663_a_retired_offline_runner_reads_retired_not_stale
  C1663_a_draining_offline_runner_reads_draining_not_stale
  C1663_an_offline_runner_without_state_keeps_its_reason
  C1672_an_eligible_draining_runner_whose_stats_fail_keeps_its_old_reason
  Publish_failure_does_not_fault_the_tick_and_the_next_tick_republishes
tests/Antiphon.Tests/Application/HostStatsAntiphonCountersTests.cs [3]
  Group_counts_in_flight_by_stage_and_kind_per_host
  Held_and_lands_count_only_their_flags
  Null_or_empty_runner_is_desktop_and_blocked_is_open_not_in_flight
tests/Antiphon.Tests/Application/HostStatsEndpointTests.cs [5]
  Hosts_stats_answers_the_cache_projection_with_counters
  Series_proxies_the_peer_operation_30_answer
  Unknown_host_is_404
  Bad_window_is_400_before_any_runner_call
  Series_for_a_disconnected_runner_is_409_unavailable
tests/Antiphon.Tests/Application/SessionRunnerHttpClientHostStatsTests.cs [3]
  Host_stats_read_is_one_attempt
  Not_found_maps_to_unsupported
  Hung_runner_is_cancelled_at_the_request_timeout
```

Additional exact roster: PhoneHomeCommandDispatcherTests methods
Host_stats_operation_answers_the_store_snapshot,
Host_stats_series_answers_points_and_rejects_a_bad_window,
Host_stats_without_the_seam_is_unsupported (3).
The four shared guards are Registry_matches_compiled_metadata plus
Policy_rows_require_pinned_attribute, Pinned_attribute_contract_matches_native_platform
and Markers_do_not_expand_policy_exclusions. CP-6 adds
every_test_class_is_tagged_unit_xor_integration and WrapperOnlyPaidManifest (6).
Native legacy methods are Linux_probe_reads_live_proc and
Windows_probe_reads_live_system_times (1 per respective OS).

#### Budgeted checkpoint manifests

Materialize each fenced YAML verbatim under the named ignored path during Code.
They are execution artifacts, not tracked deliverables. Use the schema1 tool's
positional manifest input to retain separate build/row budgets; do not pass
--plan simultaneously, which would discard YAML overrides. The Markdown table
and manifest filters/counts are identical. This is the documented manifest form
of the checkpoint tool, not an ad hoc test driver.

```text
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1514-checkpoint-bootstrap -- dotnet build tools/Antiphon.Checkpoints -p:UseAppHost=false
dotnet run --no-build --project tools/Antiphon.Checkpoints -p:UseAppHost=false -- run .antiphon/checkpoints/c1514-s1a.yaml --expected-source-sha <S1A_SHA>
dotnet run --no-build --project tools/Antiphon.Checkpoints -p:UseAppHost=false -- run .antiphon/checkpoints/c1514-s1b.yaml --expected-source-sha <S1B_SHA>
```

Bootstrap is optional if a source-matched native-output tool is already built;
it is the separately budgeted build, never arbitrary OutputPath. The checkpoint
executor obtains its own build/test slots. The no-build CLI above only launches/
waits for that tool; it performs no separate compile/test outside the gate.
Use the corresponding s1l/s1w manifest on the native worker. Await every run
through exit75 with `wait <run-id>`; never settle with an owned run in flight.
No --NoSlot, no force, no live Load mode. One build at a time/worktree, matching
UseAppHost=false on build/run. The YAML isolated outputs are checkpoint-owned
exceptions to native direct-build output, and all end in a forward slash.

Packet s1a; `.antiphon/checkpoints/c1514-s1a.yaml`:

```yaml
schemaVersion: 1
plan: docs/superpowers/testdesign/2026-11-06-load-telemetry-s1-testdesign.md
resultsRoot: .antiphon/checkpoints
build:
  properties: { UseAppHost: "false" }
  slotWaitMinutes: 10
parallel: { maxRows: 1 }
timeouts:
  rowMinutes: 15
  explicitTotalMinutes: 27
  totalOverrideReason: "CARD-1514 S1 phase budget: one isolated build, exact row caps, two-minute settlement; admission charged separately"
  hardWallMinutes: 67
builds:
  - id: bin-c1514-s1a
    project: tests/Antiphon.SessionRunner.Tests
    outputPath: bin-c1514-s1a/
checkpoints:
  - id: CP-1
    after: [S1A]
    build: bin-c1514-s1a
    group: s1a-new
    filter: "/*/*/(C1514HostLoadSampleTests*)|(C1514HostLoadAuthorityTests*)|(C1514HostLoadSpoolTests*)|(C1514HostLoadWireTests*)/*"
    expect: ["C1514HostLoadSampleTests","C1514HostLoadAuthorityTests","C1514HostLoadSpoolTests","C1514HostLoadWireTests"]
    minExecuted: 18
    estimatedMinutes: 5
    timeoutMinutes: 4
    serial: true
  - id: CP-2
    after: [S1A]
    build: bin-c1514-s1a
    group: s1a-legacy
    filter: "/*/*/(HostStatsSamplerTests*)|(HostStatsStoreTests*)|(HostStatsProbeParseTests*)|(HostStatsEndpointTests*)|(TestClassificationGuardTests*)|(NativeLinuxPolicyWitnessTests*)/*"
    expect: ["HostStatsSamplerTests","HostStatsStoreTests","HostStatsProbeParseTests","HostStatsEndpointTests","TestClassificationGuardTests","NativeLinuxPolicyWitnessTests"]
    minExecuted: 33
    estimatedMinutes: 2
    timeoutMinutes: 4
    serial: true
  - id: CP-3
    after: [S1A]
    build: bin-c1514-s1a
    group: s1a-phone
    filter: "/*/*/PhoneHomeCommandDispatcherTests/Host_stats_*"
    expect: ["Host_stats_operation_answers_the_store_snapshot","Host_stats_series_answers_points_and_rejects_a_bad_window","Host_stats_without_the_seam_is_unsupported"]
    minExecuted: 3
    estimatedMinutes: 1
    timeoutMinutes: 2
    serial: true
```

Packet s1b; `.antiphon/checkpoints/c1514-s1b.yaml`:

```yaml
schemaVersion: 1
plan: docs/superpowers/testdesign/2026-11-06-load-telemetry-s1-testdesign.md
resultsRoot: .antiphon/checkpoints
build:
  properties: { UseAppHost: "false" }
  slotWaitMinutes: 10
parallel: { maxRows: 1 }
timeouts:
  rowMinutes: 28
  explicitTotalMinutes: 50
  totalOverrideReason: "CARD-1514 S1 phase budget: one isolated build, exact row caps, two-minute settlement; admission charged separately"
  hardWallMinutes: 90
builds:
  - id: bin-c1514-s1b
    project: tests/Antiphon.Tests
    outputPath: bin-c1514-s1b/
checkpoints:
  - id: CP-4
    after: [S1B]
    build: bin-c1514-s1b
    group: s1b-delivery
    filter: "/*/*/(C1514HostLoadHistoryTests*)|(C1514TaskLoadWindowTests*)|(C1514HostLoadApiTests*)/*"
    expect: ["C1514HostLoadHistoryTests","C1514TaskLoadWindowTests","C1514HostLoadApiTests"]
    minExecuted: 18
    estimatedMinutes: 16
    timeoutMinutes: 8
    serial: true
  - id: CP-5
    after: [S1B]
    build: bin-c1514-s1b
    group: s1b-surfaces
    filter: "/*/*/(C1514HostLoadAggregateTests*)|(C1514HostLoadRecommendationTests*)|(C1514HostLoadScriptTests*)|(HostStatsCacheTests*)|(HostStatsPollServiceTests*)|(HostStatsAntiphonCountersTests*)|(HostStatsEndpointTests*)|(SessionRunnerHttpClientHostStatsTests*)/*"
    expect: ["C1514HostLoadAggregateTests","C1514HostLoadRecommendationTests","C1514HostLoadScriptTests","HostStatsCacheTests","HostStatsPollServiceTests","HostStatsAntiphonCountersTests","HostStatsEndpointTests","SessionRunnerHttpClientHostStatsTests"]
    minExecuted: 40
    estimatedMinutes: 4
    timeoutMinutes: 6
    serial: true
  - id: CP-6
    after: [S1B]
    build: bin-c1514-s1b
    group: s1b-guards
    filter: "/*/*/(TestClassificationGuardTests*)|(NativeLinuxPolicyWitnessTests*)|(TestLaneCategoryGuardTests*)|(LiveLaneRunnerTests*)/(Registry_matches_compiled_metadata*)|(Policy_rows_require_pinned_attribute*)|(Pinned_attribute_contract_matches_native_platform*)|(Markers_do_not_expand_policy_exclusions*)|(every_test_class_is_tagged_unit_xor_integration*)|(WrapperOnlyPaidManifest*)"
    expect: ["Registry_matches_compiled_metadata","Policy_rows_require_pinned_attribute","Pinned_attribute_contract_matches_native_platform","Markers_do_not_expand_policy_exclusions","every_test_class_is_tagged_unit_xor_integration","WrapperOnlyPaidManifest"]
    minExecuted: 6
    estimatedMinutes: 2
    timeoutMinutes: 6
    serial: true
```

Packet s1l; `.antiphon/checkpoints/c1514-s1l.yaml`:

```yaml
schemaVersion: 1
plan: docs/superpowers/testdesign/2026-11-06-load-telemetry-s1-testdesign.md
resultsRoot: .antiphon/checkpoints
build:
  properties: { UseAppHost: "false" }
  slotWaitMinutes: 10
parallel: { maxRows: 1 }
timeouts:
  rowMinutes: 15
  explicitTotalMinutes: 27
  totalOverrideReason: "CARD-1514 S1 phase budget: one isolated build, exact row caps, two-minute settlement; admission charged separately"
  hardWallMinutes: 47
builds:
  - id: bin-c1514-s1l
    project: tests/Antiphon.SessionRunner.Tests
    outputPath: bin-c1514-s1l/
checkpoints:
  - id: CP-7
    after: [S1B]
    build: bin-c1514-s1l
    group: s1-linux
    filter: "/*/*/(C1514HostLoadNativeTests*)|(C1514HostLoadNativeOverheadTests*)|(HostStatsProbeLiveTests*)|(TestClassificationGuardTests*)|(NativeLinuxPolicyWitnessTests*)/(Linux_*)|(Registry_matches_compiled_metadata*)|(Policy_rows_require_pinned_attribute*)|(Pinned_attribute_contract_matches_native_platform*)|(Markers_do_not_expand_policy_exclusions*)"
    expect: ["Linux_ProductionFactoryPreservesHostSamples","Linux_SamplerOverheadStaysWithinBudget","Linux_probe_reads_live_proc","Registry_matches_compiled_metadata","Policy_rows_require_pinned_attribute","Pinned_attribute_contract_matches_native_platform","Markers_do_not_expand_policy_exclusions"]
    minExecuted: 7
    estimatedMinutes: 8
    timeoutMinutes: 10
    serial: true
```

Packet s1w; `.antiphon/checkpoints/c1514-s1w.yaml`:

```yaml
schemaVersion: 1
plan: docs/superpowers/testdesign/2026-11-06-load-telemetry-s1-testdesign.md
resultsRoot: .antiphon/checkpoints
build:
  properties: { UseAppHost: "false" }
  slotWaitMinutes: 10
parallel: { maxRows: 1 }
timeouts:
  rowMinutes: 15
  explicitTotalMinutes: 27
  totalOverrideReason: "CARD-1514 S1 phase budget: one isolated build, exact row caps, two-minute settlement; admission charged separately"
  hardWallMinutes: 47
builds:
  - id: bin-c1514-s1w
    project: tests/Antiphon.SessionRunner.Tests
    outputPath: bin-c1514-s1w/
checkpoints:
  - id: CP-8
    after: [S1B]
    build: bin-c1514-s1w
    group: s1-windows
    filter: "/*/*/(C1514HostLoadNativeTests*)|(C1514HostLoadNativeOverheadTests*)|(HostStatsProbeLiveTests*)|(TestClassificationGuardTests*)|(NativeLinuxPolicyWitnessTests*)/(Windows_*)|(Registry_matches_compiled_metadata*)|(Policy_rows_require_pinned_attribute*)|(Pinned_attribute_contract_matches_native_platform*)|(Markers_do_not_expand_policy_exclusions*)"
    expect: ["Windows_ProductionFactoryPreservesHostSamples","Windows_SamplerOverheadStaysWithinBudget","Windows_probe_reads_live_system_times","Registry_matches_compiled_metadata","Policy_rows_require_pinned_attribute","Pinned_attribute_contract_matches_native_platform","Markers_do_not_expand_policy_exclusions"]
    minExecuted: 7
    estimatedMinutes: 9
    timeoutMinutes: 10
    serial: true
```

#### Native evidence and performance manifest

Native evidence includes source SHA, OS/version/architecture, host core denominator,
effective cgroup/affinity limit, factory runtime type and chosen arm, native-read
bounds, actual K/provenance and spool reopen. Linux reads independently bracket
production /proc values; Windows uses independent GetSystemTimes/memory and
all-group-core reads. Exact live CPU equality is not required across different
read instants; prove null first delta, finite bounded next delta and independent
host core count, with deterministic injected-arm arithmetic in cases S1/S2 of this S1 design. Native
spool checks use the actual filesystem and same compiled HostLoadSpool, not a mock.

For N3/N4 launch a staged real runner with phone-home/Herdr/watchdog disabled,
loopback ephemeral port, private state root and inherited live-runner settings
scrubbed. Keep the sampler/authority/spool path enabled in on pairs. Never bind
17204/8080 or capture real session payload. Two off/on60s pairs with15s warmup
and joined cleanup are the Plan's minimum measurement, not a load stress test.
Compare runner CPU time/wall *100 (one-core units), retain both pairs, on sample
count, tick spans, spool bytes and shared authority request count. Do not normalize
process CPU by host cores or auto-repeat a noisy failure until green. A measured
failure goes to Debug with its actual host load. Existing 0.633-point Linux
incremental CPU is a historical comparator, not the new result or Windows proof.

New test measured p95s are **unknown**. CARD-1514/CARD-1404 own provisional
budgets. Collect first required ordinary, Review and mutation phases, at most
three normal/two loaded comparable samples; never add broad timing-only runs.
The S5b-1 historical comparator cited by the unchanged Plan has11 cold AT builds:
p50=431.6s, empirical p95/max=817.1s;39 admissions p95=255s/max405s. This is the
basis for the28min build allowance and10min admission allowance, not a new p95.

| Phase | Expected time | Cap and measurement obligation |
|---|---:|---|
| Pure arithmetic/parsing | <100ms/method | 2s provisional; fake clocks for retention/staleness/deadlines |
| Local spool/queue/HTTP case | 0.1-2s body | 10s case,3s authority/ingest attempt,5s history body; cancellation includes response read |
| DB case | 0.1-2s body | 10s plus separately measured shared setup; one transaction/page |
| Owned crash/script method | 1-30s | 60s body +10s joined cleanup; internal cuts have5s barrier/readiness observations; no nested build |
| Native factory method | 1-5s | 15s provisional; native prerequisite absence is not pass |
| Native overhead method | 5min | 10min row cap, fixed4x60s+warmup/startup/teardown; new p95 unknown |
| RT isolated build | 3min Linux /4min Windows | Tool's15min build floor applies; Plan6min proposal cannot be represented by row-local budgets without also changing row deadlines |
| AT isolated build with staged probe | 12min | 28min from2x817.1s rounded up; measure staging separately |
| Parent test host/discovery | 10-30s | Included in each row; source/roster/phase receipt required |
| Slot admission | low-load0-5min | 10min per acquisition; exit4 is not run; no unleased retry |
| Shared DB/container/template | provisional0.5-2min/run | One setup per assembly run; readiness/migration/clone separate, no class-owned containers |

The RT15min importer floor is an explicit provisional tool constraint tracked
under CARD-1720/CARD-1514, not a fabricated2x measurement. Narrow it through the
existing tool policy when measured/available; do not pass --row-timeout to inflate
all short rows. Row-specific timeoutMinutes remain4/4/2,8/6/6,10,10 as manifested.

| Packet | Expected minutes (table sum) | Charged ceiling | Hard wall | Outer admissions |
|---|---:|---:|---:|---:|
| S1A, CP-1..3 | 8 | 15+4+4+2+2=27 | 67 | 4 |
| S1B, CP-4..6 | 22 | 28+8+6+6+2=50 | 90 | 4 |
| Linux, CP-7 | 8 | 15+10+2=27 | 47 | 2 |
| Windows, CP-8 | 9 | 15+10+2=27 | 47 | 2 |

Hard wall includes at most10min per outer admission, charged time includes2min
settlement. Slot admission, execution and wall remain separate CARD-1531 facts.
The no-build CLI launch is not a second driver lease. A tool bootstrap adds its
own one lease. No normal expected packet exceeds30min; if any selection/batch/run
actually does, commission bounded performance Debug before repeating it. Loaded
ceilings are not waivers. The conservative server PC cycle below exceeds30min;
Mutation commissioning therefore needs a bounded performance/binding review, not
one giant102-control dispatch.

Record in ignored `.antiphon/checkpoints/<run>/`: exact method/arguments, source
SHA/tree, build and staged-image hashes, OS/physical host and load, K/W/event IDs,
manifest/run/row IDs, raw TRX and unedited CHECKPOINT/CHECKPOINT-TIMING lines.
Measure admission-only union, restore/build/staging, discovery, container/readiness,
migration/template/clone, web host, body, teardown and external DB/HTTP/filesystem/
process calls. Missing phases stay unknown; expected absent phases n/a. Wall time
is not the sum of nested/parallel spans. Preserve original failures and costs.

Shared setup must be real: one Postgres container/template/model and one reusable
loopback host per assembly run; cloned databases and private spool directories
isolate cases. Restart cuts create fresh instances because that is the subject,
with measured cost. Native overhead requires fresh off/on processes by design.
Do not run AT alongside Pty/FakeClaude suites or parallel native overhead against
other load tests. One build at a time in each worktree.

#### Dependency and execution-input closure

This is a **scoped ordinary verification profile**, not Full/rc qualification.
Runner contracts feed both runner and server; changed sampler/probe/transport
code therefore selects both test assemblies. The script depends on server read
DTOs and live placement projection. No command/checkpoint/land consumer changes
in S1. Pty/messaging/fake-provider dependencies compile transitively but have no
changed runtime behavior requiring their whole suites. Checkpoint tool remains
HostCleanup-only. Any actual changed downstream behavior adds a named witness
through TestDesign before Code widens execution.

Direct project-reference inventory read from the inspected source (the new
HostLoad.Probe reference is explicitly proposed, not present yet). The actual
evaluated transitive graph and staged hashes must be retained by Code's build:

```text
src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj [5 direct]
  -> ../Antiphon.Agents.Pty/Antiphon.Agents.Pty.csproj
  -> ../Antiphon.SessionRunner.Contracts/Antiphon.SessionRunner.Contracts.csproj
  -> ../Antiphon.PtyHost/Antiphon.PtyHost.csproj
  -> ../Antiphon.PtyHost.Client/Antiphon.PtyHost.Client.csproj
  -> ../Antiphon.Observability/Antiphon.Observability.csproj
server/Antiphon.Server.csproj [6 direct]
  -> ../src/Antiphon.Agents.Pty/Antiphon.Agents.Pty.csproj
  -> ../src/Antiphon.Messaging/Antiphon.Messaging.csproj
  -> ../src/Antiphon.Messaging.Client/Antiphon.Messaging.Client.csproj
  -> ../src/Antiphon.SessionRunner.Contracts/Antiphon.SessionRunner.Contracts.csproj
  -> ../src/Antiphon.Resilience/Antiphon.Resilience.csproj
  -> ../src/Antiphon.Observability/Antiphon.Observability.csproj
tests/Antiphon.SessionRunner.Tests/Antiphon.SessionRunner.Tests.csproj [3 direct]
  -> ../../src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj
  -> ../Antiphon.CustodyTestChild/Antiphon.CustodyTestChild.csproj
  -> ../../src/Antiphon.FakeLlmApi/Antiphon.FakeLlmApi.csproj
tests/Antiphon.Tests/Antiphon.Tests.csproj [27 direct]
  -> ../../server/Antiphon.Server.csproj
  -> ../../scripts/fixtures/nightly/owned-child/OwnedChild.csproj
  -> ../../src/Antiphon.Resilience/Antiphon.Resilience.csproj
  -> ../../src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj
  -> ../Antiphon.FileUseTestChild/Antiphon.FileUseTestChild.csproj
  -> ../Antiphon.DockerStack.Fixture/Antiphon.DockerStack.Fixture.csproj
  -> ../Antiphon.CustodyTestChild/Antiphon.CustodyTestChild.csproj
  -> ../Antiphon.ScriptHarnessHost/Antiphon.ScriptHarnessHost.csproj
  -> ../Antiphon.ChannelOutbound.Probe/Antiphon.ChannelOutbound.Probe.csproj
  -> ../../src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj
  -> ../../src/Antiphon.FakeLlmApi/Antiphon.FakeLlmApi.csproj
  -> ../../src/Antiphon.NightlyWatchdog/Antiphon.NightlyWatchdog.csproj
  -> ../../tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj
  -> ../../tools/Antiphon.TimeoutAudit/Antiphon.TimeoutAudit.csproj
  -> ../Antiphon.Checkpoints.LifecycleHost/Antiphon.Checkpoints.LifecycleHost.csproj
  -> ../Antiphon.Checkpoints.FilterHost/Antiphon.Checkpoints.FilterHost.csproj
  -> ../Antiphon.Checkpoints.RepeatHost/Antiphon.Checkpoints.RepeatHost.csproj
  -> ../Antiphon.Checkpoints.GitFixture/Antiphon.Checkpoints.GitFixture.csproj
  -> ../Antiphon.TestTiming.Probe/Antiphon.TestTiming.Probe.csproj
  -> ../Antiphon.NativeLinuxPolicy.Probe/Antiphon.NativeLinuxPolicy.Probe.csproj
  -> ../../tools/Antiphon.LandGraphAudit/Antiphon.LandGraphAudit.csproj
  -> ../../tools/Antiphon.LandGraphAudit.Core/Antiphon.LandGraphAudit.Core.csproj
  -> ../../src/Antiphon.Messaging.Client.Testing/Antiphon.Messaging.Client.Testing.csproj
  -> ../../src/Antiphon.Messaging.Gateway/Antiphon.Messaging.Gateway.csproj
  -> ../../src/Antiphon.Messaging.Slack/Antiphon.Messaging.Slack.csproj
  -> ../../src/Antiphon.FakeClaude/Antiphon.FakeClaude.csproj
  -> ../../src/Antiphon.FakeGrok/Antiphon.FakeGrok.csproj
tools/Antiphon.Checkpoints/Antiphon.Checkpoints.csproj [1 direct]
  -> ../../src/Antiphon.HostCleanup/Antiphon.HostCleanup.csproj
proposed tests/Antiphon.HostLoad.Probe/Antiphon.HostLoad.Probe.csproj
  -> ../../server/Antiphon.Server.csproj
  -> ../../src/Antiphon.SessionRunner/Antiphon.SessionRunner.csproj
proposed AT reference -> ../Antiphon.HostLoad.Probe/Antiphon.HostLoad.Probe.csproj (staged helper, ReferenceOutputAssembly=false)
```

Fixture closure: literal K/W/metric corpus and manual arithmetic; scripted OS
reads/fake time; real spool/bounded queues; loopback production HTTP and websocket
clients/dispatcher; typed authority recorder; shared PostgreSQL template and
private clones; registered task event observer; staged owned crash probe; real
host-load.ps1 through ScriptHarnessProcess; existing native factory/probe and
class/Slow/native policy guards. Native OS and pwsh are real inputs, not guessed
from runner catalogue. Policy/census/pin changes remain one atomic publication.

### Cost

All numbers below are **estimated**, not measured S1 executions. This TestDesign
spent **0 build/test/PC execution minutes**. Authoring is separate. Each filter
is the exact CP or PC F above, never an assembly wildcard.

- **Ordinary V/R floor (Code) =47min**, CP-1..8 sum5+2+1+16+4+2+8+9.
  Portable S1A/S1B=30min; separately placed native Linux/Windows=17min.
  This contains22min build (RT3, AT12, Linux3, Windows4) and25min selected row
  execution/setup; do not add those builds again. Optional tool bootstrap3min
  makes setup+ordinary **50min**. No extra whole suite is authorized.
- **Mutation floor =3,151.5min** for102 controls, each with one exact F and
  baseline/red/restored-green. Runner portable29 controls:3min build+0.5min host
  per phase, plus0.5min patch/restoration accounting =11min/control =319min.
  Server71 controls:12min build+1min host per phase plus0.5 =39.5min/control
  =2,804.5min. Native PC-81 Linux:3x(3+1)+0.5=12.5min. Native PC-82 Windows:
  3x(4+1)+0.5=15.5min. Overhead measurements are ordinary N3/N4, not replayed
  during PC-81/82. PC-13 is charged to server H3, where the process-crash kill is.
- PC total decomposition:306 builds =2,838min;306 exact method phase executions
  =262.5min;102 restoration/binding-accounting entries =51min. No shared baseline
  or frozen-binary discount is assumed. Candidates remain binding-required; a
  safe bound-pack optimization needs proof and revised costs before use.
- **Combined setup+Code V/R+Mutation =3,201.5min (53h21m30s)**. Same-scope ordinary
  Review adds47min =>3,248.5min. Implementation, performance Debug, contention,
  placement delay, repairs and deployment are separate and unmeasured. These
  are staged floors, not a single task or a75-minute implementation promise.
- Admission is excluded from those charged floors:12 outer ordinary acquisitions
  plus optional bootstrap1. Historical mean0.775min suggests9.3min (10.075 with
  bootstrap), not a guarantee. PCs have612 build/test acquisitions; at that
  comparator mean474.3min. Native admission is unmeasured; do not infer it from
  Linux or relax10min exit4. The native fixture child itself does no build/test
  driver and takes no nested build lease.

Savings: one reused build per portable packet uses2 cold builds instead of6;
runner saving2x3=6min and server2x12=24min, **30min saved**. With native unchanged,
naive per-row ordinary floor77min becomes47min. Cheap guards share those builds
and run together. No PC build savings assumed (0min) because source-qualified
baseline/red/restore binds actual images; do not trade away preservation evidence.
Measured phase costs from first required runs replace these estimates, with any
>30min selection/cycle sent to bounded Debug before repetition.

### Handoff completeness

Static document census (not an importer execution, compilation, discovery or
mutation run):52 new methods in12 classes (native overhead split to honor the
class-only Slow registry),8 exact checkpoint rows, four packets of3/3/1/1 rows,
132 expected TUnit executions and47 estimated ordinary minutes. Real method
counts/patch compilation/native receipts remain pending after Code.

**guards=102, mapped=102, missing=0, duplicate PC maps=0. All102 PCs are mandatory
runs; none has run in this documents-only task.** The Plan's S1 candidate controls
are refined and split here: CPU/nulls1..7; authority8..12; spool13..19/26..32;
quantiles44..50/91..95; task partitions51/52/85; recommendation61..70; failure
isolation20/40; script77..80. The source Plan's S2/S3 controls are retained as
explicit exclusions, not silently credited.

The intended next stage is **code — S1 telemetry + script**, internal S1A then
S1B, using these exact packets and unchanged admission. Required source/patch
bindings stay open until implemented; no mock-only seam receives proof. If a
real boundary cannot be exercised, return to Plan; a human calibration choice
belongs to Decide only when it actually blocks work. Neither hypothetical
calibration nor the operator's already granted direction needs reconfirmation.
