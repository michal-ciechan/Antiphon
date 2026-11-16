# CARD-1893: server2 binding and broker 405 diagnosis

2026-11-16, Investigate task `8d0de99b`, read-only diagnosis. **Confirmed:** server2 has no uniquely resolvable current server-side telemetry binding, the running collector lacks its physical binding inputs, and the shared build-slot authority returns a reproducible missing-GET-route signature. The broker is an Antiphon SessionRunner service, separate from messaging/am-service. The exact server binding rows and broker image pin remain unobserved; absent/expired/overlapping server bindings and the precise stale broker image cannot be distinguished within this access boundary. No repair was made.

## Provenance and scope

Source/base: `61259342c25c9960c12ac7171488e52b61dc03be`, branch `feat/card-task-8d0de99b`. Read the full CARD-1893 and its eight history revisions using `scripts/card.ps1 get|history CARD-1893 -Board Antiphon`. Revisions 1–8 record Debug, Code and Review dispatch/settlement; latest is Review `92c62298` succeeded at 21:49:27 on November 16. These events do not prove activation. Merge 253's collector work is caller-supplied pending context, not a measured deployment.

Read the earlier report in full at `origin/feat/card-task-705f622b`, fetched tip `ce399e772010ab89edc018fb133376badb613eb5`, path `docs/investigations/2026-11-16-card-1893-telemetry.md`. Its append failure and earlier 405 observations are independent stored evidence, not newly observed exceptions here.

Live server and runner both advertise `9b98828eb34eb552ce62bcde5a64dd045a872a88`. A source comparison of that commit with this base was empty for the server history/settings/registration/ingest files and runner Program, HostStatsRoutes, BuildSlotRoutes, HostAdmissionSnapshotClient and runner Compose. This connects the cited mechanisms to advertised builds; it is not a verified build receipt for the deployed binaries. The broker exposes no build identity in the responses examined.

## Stored read transcript

Reads were taken at **23:40:10–23:41:11 UTC**. Server reads used the documented `ANTIPHON_API` resolution and inherited task-token header, never a token in output or URL. Here it resolves to `https://antiphon.desktop.codeperf.net`; `http://localhost:17202/api/version` is unreachable inside this runner. Direct runner and broker reads use container-network HTTP. Entries below retain exact response values or explicitly selected fields; bodies are otherwise omitted.

| ID | Request/input | Observation |
| --- | --- | --- |
| E1 | Server `GET /api/version` | `version=9b98828eb34eb552ce62bcde5a64dd045a872a88`, capabilities include `land-v2` |
| E2 | Server `GET /api/session-runners/server2/status` | `available=true`, `dispatchEligible=true`, `platform=linux`, same `buildVersion`; registration store `f519bd08-e53a-47d1-adb1-2ab33475446f`, process boot `02f9c5b8-e641-452d-83bd-6400bc6f986d`; load projection exists but command/dispatch pressure are null and decisions unknown |
| E3 | Server `GET /api/hosts/server2/load` | `physicalHostKey=null`, `current=null`, `coverage=0`, authority state/mode unknown, `recommendedAdditionalTasks=0` |
| E4 | Server `GET /api/hosts/server2/load/history?from=2026-11-16T22:00:00Z&to=2026-11-16T22:05:00Z&resolution=raw&limit=60` | `items=[]`, `nextCursor=null`, `complete=false`, `gaps=["binding-unknown"]` |
| E5 | `GET http://localhost:8080/capabilities` | Same runner version; includes `hostStatsV1` and `hostLoadHistoryV1`; custody store `897f354b-0f0d-4032-93a3-d3addffccbe2` |
| E6 | `GET http://build-slots:8080/health` | 200, body `Healthy` |
| E7 | `GET http://build-slots:8080/build-slots` | 200; selected fields `budget=4`, `occupied=1`, `enabled=true`, `maxCpuCount=6` |
| E8 | `GET http://build-slots:8080/build-slots/host-load`, twice | 405; second read `Server=Kestrel`, `Allow=DELETE`, `Content-Length=0` |
| E9 | `GET http://build-slots:8080/build-slots/metrics`, twice | Same 405/Kestrel/DELETE/empty-body signature |
| E10 | `GET http://localhost:8080/host-stats/history?limit=1`, no `after` | 503; decoded problem title `host_load_history_unavailable` |

Allowlisted installed/process configuration, read at 23:41:10 UTC:

```text
/app/appsettings.json SessionRunner.HostStats:
{"Enabled":true,"IntervalMs":5000,"RetentionMinutes":30,"ProcessSampling":true}
/proc/357/cmdline:
dotnet Antiphon.SessionRunner.dll
/proc/357/environ, only admitted keys:
ANTIPHON_BUILD_SLOTS_URL=http://build-slots:8080/build-slots
SessionRunner__HostStats__Volumes__1=/state
SessionRunner__HostStats__Volumes__0=/work
PhoneHome__RunnerId=server2
```

Only `/app/appsettings.json` matched `/app/appsettings*.json`. The process allowlist included all `SessionRunner__HostStats__*` and `HostLoadHistory__*`, plus environment-name overrides; no physical key, binding revision, source epoch, authority ID/endpoint or history-path override was present. No secret file or secret value was inspected or printed. These are collector inputs, not the other server process's effective options.

## Fault A: the binding mechanism

The server's `HostLoadHistory:Bindings` defaults to an empty list (`server/Application/Settings/HostLoadHistorySettings.cs:5`, `:12`). Startup binds that section into `IOptions<HostLoadHistorySettings>` (`server/Infrastructure/HostLoad/HostLoadRegistration.cs:12`). There is no binding write/provisioning route in the host-load API; its three routes are reads (`server/Api/Endpoints/HostLoadEndpoints.cs:13`, `:28`, `:37`).

“Unresolved” has an exact meaning: `HostLoadHistoryService.Binding` selects bindings with canonical runner ID `server2` and **current time** in `[From, To)`, and returns a binding only if the count is exactly one (`server/Infrastructure/HostLoad/HostLoadHistoryService.cs:175`). E4 reconstructs this branch conclusively: `binding-unknown` is returned at `:72`, before the sample/cursor database queries. It does not prove that a database has no samples, nor that the ingestion cursor is absent or stationary. A history query for an old interval still evaluates the binding at request time (`:62`), not at the query's `from` time.

E2 and E5 prove ordinary runner registration/capability availability; neither creates this configuration. E3/E4 prove that registration is insufficient. With permitted reads, the active binding count is **zero or greater than one**, not an observed exact count. No public response inspected exposes the actual server rows, so “the server has an empty Bindings array” is not established.

The collector half is directly observable: its `PhysicalHostKey`, nullable `BindingRevision` and nullable `SourceEpoch` have no configured inputs in E10's process/appsettings survey; these settings default null (`src/Antiphon.SessionRunner/HostStatsSettings.cs:32`). Runner identity is set from `PhoneHome:RunnerId` (`src/Antiphon.SessionRunner/HostStatsRoutes.cs:33`). Samples copy the three binding fields unchanged (`src/Antiphon.SessionRunner/HostLoadProjection.cs:36`, `:44`). Thus repairing only the server's current binding would still leave new samples unmatched with the observed collector inputs.

Ingestion requires exactly one server row matching canonical runner, sample time, physical key, revision and epoch (`server/Infrastructure/HostLoad/HostLoadIngestService.cs:76`). It also requires a primary row and exactly one primary for that physical key/epoch/time (`:55`, `:84`). Server binding fields are `RunnerId`, `PhysicalHostKey`, `SourceEpoch`, `Revision`, `Primary`, `From`, `To` (`server/Application/Settings/HostLoadHistorySettings.cs:24`). Validation rejects an empty epoch, revision below one and invalid intervals (`:18`). These are configuration identities, not capability registration fields. Neither observed runner store ID is an established telemetry spool ID or a physical-host key/source epoch.

**Classification/owner:** confirmed configuration-contract failure in Antiphon, with the collector omissions observed and the precise server-side subcause unresolved. The server2 operations/configuration owner owns both sets of values; no evidence requires a new runner capability or a messaging-repository change.

**Witness to resolve the remaining subcause:** an owner-provided, non-secret projection of effective `HostLoadHistory:Bindings`, limited to the seven fields above and configuration-source provenance, would distinguish absence, expiry and overlap. It must include server2-temp/other collectors sharing the physical host/epoch so primary uniqueness can be counted. Pair it with the collector's three values and a newly sampled timestamp. No full configuration dump is needed.

**Acceptance witness and activation implications:** a fresh bound sample must reach a stored row with matching physical key/epoch and `IsPrimary=true`, and E4 for a new measured interval must return samples instead of `binding-unknown`. A fresh task must have known start/during/end coverage. Server and collector consume `IOptions.Value`, with collector options retained in its constructor (`HostLoadCaptureService.cs:30`); configuration-file edits alone are not evidence of effective reload. Startup activation is required for these options, and environment changes require process/container replacement. Risks are a brief collection interruption, overlapping primary collectors, mismatched epochs, and permanently unattributed pre-binding samples. None was exercised here.

## Fault B: the broker route mechanism

The precise client request is **GET `http://build-slots:8080/build-slots/host-load`**. The inherited build-slots URL is suffixed with `/host-load` unless `SessionRunner:HostStats:AuthorityEndpoint` is explicit (`src/Antiphon.SessionRunner/HostStatsRoutes.cs:31`); the client calls `http.GetAsync`, then requires success (`src/Antiphon.SessionRunner/HostAdmissionSnapshotClient.cs:40`). HTTP/parse/deadline failures become an unknown snapshot (`:55`), with no local-count fallback in the external endpoint branch. A successful response still needs matching physical identity, an acceptable ID, positive revision/budget, counts, recognized mode/state and fresh timestamp (`:62`).

This URL belongs to the Compose **`build-slots`** service, image `antiphon-server2/session-testing:${BUILD_SLOTS_SHA12}`, entrypoint `dotnet Antiphon.SessionRunner.dll`, `BuildSlotsOnly=true` (`docker-compose.server2-runner.yml:169`). The runner is explicitly pointed there (`:75`). In that mode Program registers and maps BuildSlotRoutes before returning (`src/Antiphon.SessionRunner/Program.cs:43`). It neither starts phone-home nor maps ordinary capability endpoints in that branch. This is separate from the Kafka channel broker described in `docs/telegram.md:226` and the messaging bootstrap configuration in `docker-compose.server2-server.yml:100`; the am-service label in the brief does not identify the observed HTTP target.

Current Antiphon code accepts **GET** at the exact telemetry path (`src/Antiphon.SessionRunner/BuildSlotRoutes.cs:71`), including BuildSlotsOnly. It also accepts GET metrics (`:80`). Its other relevant methods are POST acquire, DELETE lease and POST renew (`:31`, `:58`, `:63`). DELETE in E8's Allow header is therefore not an instruction to change the telemetry client verb.

The older stored route table at commit `6b2117d170a639f879d3868ec5d72159ab53bf5d`, `src/Antiphon.SessionRunner/BuildSlotRoutes.cs:58–69`, contains DELETE `/{leaseId:guid}`, POST renew and GET root, then returns, with neither literal GET. `docs/testing-and-build.md:1310` already records exactly this 405/`Allow: DELETE` signature for the shared broker's missing metrics route: method selection runs before the GUID constraint. E8/E9 reproduce that signature for both additions while E7 proves the old root route remains available. The host-load literal was added in ancestor `b0604d33a2adc858a7157ead92416a8ef52e3282` on November 7, later than metrics.

**Conclusion:** the observed service's public route surface lacks the current GET additions; an older deployed broker route set explains all measurements. Direct container-network Kestrel answers and the specific DELETE signature support route/version mismatch, not the messaging API's verb contract or an observed external reverse-proxy rule. Exact broker image/tag/pin and any unobserved internal customization remain unknown. Merely pointing AuthorityEndpoint at the same URL explicitly cannot add the missing route.

There is a concrete persistence mechanism for stale deployment: `broker_sha12()` uses the stored `BUILD_SLOTS_SHA12` in preference to the rollout SHA (`scripts/c590-remote.sh:979`); `ensure_build_slots_broker()` writes the pin only if absent (`:1017`); both standing/temp Compose calls reuse it (`:971`, `:1003`). Consequently upgrading the runner does not imply upgrading the authority. This pinning explanation is strongly supported by source and the earlier documented incident; today's actual pin has not been read.

**Classification/owner:** Antiphon build-slot broker deployment/configuration, owned by the server2 deployment operator in this repo. Current source already supplies the endpoint. No evidence supports commissioning an am-service route change.

**Witness to close deployment attribution:** approved host-side metadata showing broker image/digest, revision and only the `BUILD_SLOTS_SHA12` value, plus its non-secret HostStats physical key/authority ID/revision, would establish the exact stale pin. This task did not SSH, use a host Docker socket, or read the other container's files. Checked-in broker Compose currently supplies no such HostStats identity (`docker-compose.server2-runner.yml:176–184`); actual broker effective identity is unobserved. In current code, missing physical key/authority ID gives HTTP 200 with unknown state (`BuildSlotRoutes.cs:73`), so a route upgrade alone does not prove usable authority.

**Acceptance witness and activation implications:** E8 must return 200 JSON with known state, matching physical key/authority ID, positive revision, fresh time, and counts consistent with a contemporaneous E7; those same values must survive into a runner sample and the stored sample. Activating a different image or environment requires broker recreation, not just a runner restart. Broker lease/waiter state is in memory (`src/Antiphon.SessionRunner/BuildSlotBroker.cs:37`); at E7 one lease was held, so an immediate recreation would interrupt real build coordination. The documented broker upgrade boundary is no held lease (`docs/testing-and-build.md:1310`). Existing POST/renew/DELETE semantics and budget must remain verifiable after any later activation. No lease was acquired, renewed or released here.

## Dependency order and durable evidence

These are three independent mechanisms. Collector hardening in Merge 253 concerns opening/appending the spool; binding determines attribution; authority GET determines whether admission information in the sample is known. Capture opens the spool **before** reading authority (`src/Antiphon.SessionRunner/HostLoadCaptureService.cs:67`, `:74`). A failed authority read degrades to unknown and still reaches append (`:75`). Thus the 405 cannot explain the earlier pre-open failure, and broker repair is not a prerequisite for storing any sample.

Binding has a stronger first-ingest requirement: unmatched samples can commit and advance the cursor with null host/epoch and `IsPrimary=false` (`server/Infrastructure/HostLoad/HostLoadIngestService.cs:50–73`). Duplicate replay skips the binding calculation (`:46–50`). Later configuration does not automatically reattribute those rows. Task events also require a primary binding at accepted start and are marked observed even when none is found (`server/Infrastructure/HostLoad/HostLoadTaskWindowObserver.cs:43`, `:66`, `:71`). Historical missing coverage must remain historical.

The resulting ordering constraint is: **collector code publication (Merge 253), then valid server/collector binding in effect before the first acceptance samples are ingested, then a known broker authority before the final acceptance window**. Code publication and runtime activation are distinct. If collector activation occurs first, samples from that transitional interval cannot be promised bound/complete. If the broker becomes usable later, earlier samples honestly retain unknown authority; the clean end-to-end window starts only after all three mechanisms are effective. This is a dependency reconstruction, not an authorized deployment sequence.

The durable path is spool append → phone-home history → ingest transaction → committed cursor → subsequent poll supplying that cursor → spool acknowledgement. `HostLoadIngestPump.cs:42–45` reads the committed cursor for polling, `HostLoadIngestService.cs:71–73` commits before returning, and `HostLoadSpool.cs:117–127` acknowledges the supplied frontier. Proving this needs two successive metadata-only observations of sample counts/latest sequence, ingest commit time, and acknowledgement ordinal, with inserted versus duplicate counts distinguished. Never print the opaque cursor or private ledger. History `nextCursor` is pagination and is not an ingest receipt. No database or ledger rows were read here; cursor progress and full acceptance remain unverified.

## Not done, noted

Minimal repair candidates for the next owner, not designed or applied here: publish Merge 253 and activate durable collector storage; the Antiphon/server2 operations owner supplies matching existing-setting binding identities/validity with one primary before first accepted ingest; the Antiphon deployment owner advances the broker image pin to GET-capable code and supplies matching authority identity at a lease-free activation boundary, preserving telemetry store/cursor identity.

## Decisions and remaining evidence

The caller can commission an operations evidence/activation task in this repo, retaining the unresolved server-row and broker-image alternatives above. It needs effective non-secret binding rows and exact broker image/pin before selecting values or claiming either deployment subcause fully confirmed. No physical-host key, epoch, authority ID or new identity was invented here. The existing capability registration already advertises history support.

No source repair, settings write, restart, deployment, secret access, host SSH, process termination, build or test suite ran. This deliverable is documentation only; live GETs and source/stored-route reconstruction supplied the diagnosis. No test-green or end-to-end-recovery claim is made. Authorization already covered these reads and report publication; no confirmation was requested.

--- next stage ---
next: decide
handoff: Decide Antiphon server2 ops follow-ups: inspect only effective binding rows and broker image/pin/identity, then commission activation with Merge 253. Binding must be effective before first accepted ingest; require a fresh known-authority window and metadata-only cursor/ack progress. Broker is Antiphon build-slots, not am-service; exact server binding subcase and stale image remain unobserved.
artifact: docs/investigations/2026-11-16-card-1893-binding-and-broker.md
