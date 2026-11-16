# CARD-1893: server2 durable telemetry diagnosis, 2026-11-16

The failure is current: the collector never opens its history spool, while live sampling continues. Startup default-path resolution into unwritable `/app` is the strongly supported cause reconstructed below; the swallowed production exception itself is unavailable. Independently, the server cannot select a unique active server2 binding, and the shared broker's telemetry GET returns 405. No repair or operational change was made.

## Scope and identities

Read-only Debug task `705f622b-7d33-4a48-9c85-18ed497d130a`, inside the server2 runner container. Measurements below were taken approximately 18:04–18:11 UTC. The full CARD-1893 description was retrieved with `scripts/card.ps1 get CARD-1893 -Board Antiphon`; history returned `No history.` (revision count 0). The survey's CARD-1893 row was read from `origin/feat/card-task-ae89df49`, observed tip `e992ac4572313ca17790e94f419f9a4b2d58a2e7`.

| Identity | Observation |
| --- | --- |
| Assigned source/base | `48a4615dcb4d67033886a3e31629dc7351ed0852`, branch `feat/card-task-705f622b` |
| Running standing runner | Direct `GET http://localhost:8080/capabilities`: `version=9b98828eb34eb552ce62bcde5a64dd045a872a88`, `platform=linux`, advertises `hostLoadHistoryV1` |
| Server's runner status | `GET /api/session-runners/server2/status`: same full `buildVersion`; available and dispatchEligible true; `load=null` |
| Running control plane | `GET /api/version`: `9b98828eb34eb552ce62bcde5a64dd045a872a88`, includes `land-v2` |
| Current runner process | PID 357, UID/GID 1654, cwd/content root `/app`; command is `dotnet Antiphon.SessionRunner.dll`, no further arguments; mapped runtime is .NET `9.0.23` |
| Current process start | Persisted `ANTIPHON_STARTUP` record: `producerStartTimeUtc=2026-11-16T13:58:09.2630989Z`; application started `13:58:10.9881831Z`. Caller supplied container recreation/build activation around 13:56Z; these are distinct timestamps. |
| Phone-home registration identity | Status `runnerStoreId=f519bd08-e53a-47d1-adb1-2ab33475446f`, `processBootId=02f9c5b8-e641-452d-83bd-6400bc6f986d` |
| Direct capability identity | `runnerStoreId=897f354b-0f0d-4032-93a3-d3addffccbe2`; this is the runtime custody store, whereas phone-home registration uses `PhoneHomeStoreIdentity`. Neither is an observed telemetry spool store. |
| Runner DLL SHA-256 | `/app/Antiphon.SessionRunner.dll`: `6d18f9cc1d6741e2cc11f624df387ea28651ebb329cad27b9ed4d3325a495294` |
| Contracts DLL SHA-256 | `/app/Antiphon.SessionRunner.Contracts.dll`: `a38f4ff40ea943916c56876084250b4cad6b244901d67010e0473dcfd727d85d` |

The running build is separate from the assigned source. `git diff 9b98828eb34eb552ce62bcde5a64dd045a872a88 HEAD` was empty for the five files governing default path, capture, spool, ingestion and history: `HostStatsRoutes.cs`, `HostLoadCaptureService.cs`, `HostLoadSpool.cs`, `HostLoadIngestService.cs`, and `HostLoadHistoryService.cs`. Build advertisements and DLL hashes are observations, not a newly reproduced build certificate.

## Current append failure

The installed `/app/appsettings.json` HostStats section contains only `Enabled=true`, `IntervalMs=5000`, `RetentionMinutes=30`, and `ProcessSampling=true`. Allowlisted process configuration shows the two configured volumes `/work` and `/state`, but no `HistoryPath`, history-disable override, physical key, binding revision, source epoch or authority endpoint override. `HOME=/home/app`; `XDG_DATA_HOME` is absent. There are no command-line overrides. The checked-in server2 runner Compose likewise configures volumes but none of the history/binding settings.

The current code explains the failure stage precisely:

1. `HostStatsRoutes.AddHostStats` postconfigures a missing history path as `Path.Combine(Environment.GetFolderPath(LocalApplicationData), "antiphon", "host-load")`. Its resulting options are retained, rather than recomputed on each retry.
2. `HostLoadCaptureService` attempts `_open()` before reading any queued sample or calling the admission authority. It publishes `_spool` only after the constructor returns successfully.
3. `HostLoadSpool` starts with `Directory.CreateDirectory(Path.GetFullPath(path))`, then opens/replaces its private ledger and recovers chunks.
4. Repeated direct `GET /host-stats/history?limit=1` returns **503**, title `host_load_history_unavailable`. In this implementation that title means `_spool` is still null; lock contention would have a different internal exception but is caught by the route under the same public title. Combined with the missing spool roots and uninterrupted startup-open warnings, failure before spool publication is the supported diagnosis.

Filesystem observations explain why opening fails:

| Path | Observed state |
| --- | --- |
| `/app` | root:root, mode 0755; UID 1654 write-access check returns `EACCES` |
| `/home/app/.local` and `/home/app/.local/share` | filesystem birth time `2026-11-16T15:09:42.642558822Z`, later than this process's 13:58 startup |
| `/app/antiphon/host-load` | absent on repeated checks |
| `/home/app/.local/share/antiphon/host-load` | absent on repeated checks |
| `/state/host-load` | absent; no explicit HistoryPath selects it |
| `/state` and `/work` | mounted read/write; live volume stats report about 206 GB free |

A bounded, non-writing child-runtime probe set `XDG_DATA_HOME` **only in that child** to an absent diagnostic pathname and evaluated the same .NET path functions. It returned `LocalApplicationData=""`, while `SpecialFolderOption.DoNotVerify` returned the absent pathname; the combined history path resolved against `/app` was `/app/antiphon/host-load`. The probe created no directory and attempted no spool open. Its PowerShell runtime reports .NET 9.0.10, so it is corroborating reconstruction, not execution inside the standing runner's .NET 9.0.23 process.

Taken together, the startup chronology, absent override, exact path expression and denied parent explain an initial empty LocalApplicationData result becoming a retained relative history path under `/app`. Creating `.local/share` later cannot repair those already-bound options. **High-confidence cause: default-path/permission failure before spool initialization.** The actual private options value and swallowed exception class were not read from process memory; no production syscall trace was available. The birth times do not independently prove the absence of every possible earlier directory incarnation. Preserve those limits rather than claiming an observed production `UnauthorizedAccessException`.

Missing authority or physical binding does not explain this pre-open failure: the authority client runs later and degrades failed HTTP reads to an unknown snapshot; `HostLoadProjection` permits missing physical identity with `binding-unknown`. Neither requires dropping a sample before the spool opens. No evidence here attributes the append failure to current disk exhaustion or to the historical retention operation.

## Measured sample and cursor progress

| UTC observation | Result |
| --- | --- |
| 18:05:56.927, persisted runner log | `Host load append unavailable; live sampling continues; dropped=2927` |
| 18:07:46.931, same process/log | Same warning, `dropped=2949`: 22 further losses in 110 seconds |
| 18:07:37 request | Direct history 503; live `/host-stats` timestamp `18:07:36.0069176Z`, CPU 11.726%, memory total 135182405632 bytes |
| 18:08:24 request | Direct history still 503; live timestamp `18:08:21.0076259Z`, CPU 39.972%, available memory 108963422208 bytes, `/work` and `/state` volumes populated |
| 18:06:31 and 18:08:24 requests | Server history for the fixed new window `[2026-11-16T18:00:00Z, 18:05:00Z)` returned `items=[]`, `nextCursor=null`, `complete=false`, `gaps=["binding-unknown"]` both times |
| Fresh task, both server reads | This Debug task's `/load?attempt=1`: `windows=[]`, `complete=false`, `gaps=["event-or-sample-gap"]` |

The live sampler advances. The current process supplies no durable history page or samples: it has never published its spool, and phone-home operation 50 calls that same capture service. `HostLoadIngestPump.PollOnceAsync` cannot enqueue a newly read page when the runner history call fails; `HostLoadIngestService` advances a committed frontier only from a successfully ingested page. Thus **there is no new sample-driven ingest frontier from this process over the measured interval**.

Direct database cursor state is **unobserved**, not asserted absent. The container has no approved metadata-only database surface for this row, and no credential access was attempted. A history response's `nextCursor` is a query-pagination cursor, **not** `HostLoadIngestCursors.Cursor`; `binding-unknown` returns before querying sample rows. The server history response alone therefore proves neither database emptiness nor cursor nonmovement. No existing cursor secret/ledger content was read or printed. Cursor acceptance remains unmet; there is no claim of successful durable collection or ingestion.

## Independent binding and authority failures

`GET /api/hosts/server2/load` returned `physicalHostKey=null`, `current=null`, coverage 0, authority state/mode unknown and zero recommended additional tasks. History returns `binding-unknown` only when `HostLoadHistoryService.Binding` finds other than exactly one currently valid configured binding for this runner. This proves the server's active binding is unresolved; it does not distinguish absent, expired or overlapping configuration without inspecting the server's effective settings. Server container settings are outside this runner's accessible files.

The collector's own `PhysicalHostKey`, `BindingRevision` and `SourceEpoch` are also unconfigured in the observed inputs. Even after opening a spool, ingestion would store samples without an accepted primary binding unless runner and server identities match. `HostLoadIngestService.Binding` requires matching physical key, revision, source epoch and validity time; exactly one primary collector must represent the physical host/epoch. Unbound samples can still be ingested and acknowledged, so repairing storage alone is insufficient for host-history acceptance.

The inherited `ANTIPHON_BUILD_SLOTS_URL` is `http://build-slots:8080/build-slots`; its derived authority URL is `http://build-slots:8080/build-slots/host-load`. The latter returned **405**, with no JSON body. That broker's `/health` returned 200; `/capabilities` returned 404, so no broker build identity was established. Current source supports the telemetry GET even in BuildSlotsOnly mode. Endpoint/build incompatibility is a follow-up candidate; the exact broker image/configuration was not inspectable here. This is a separate observability defect, not proof that admission leases fail and not the cause of the earlier spool-open failure. A future repair must make this endpoint return a matching known authority snapshot, without substituting the runner's local broker count.

## Historical gaps stay historical

CARD-1893 already records November 13 failures, including drops before and after the 07:33 replacement, and the `15:00–16:00Z` incident window. Repeating that exact history query today still returns `items=[]`, incomplete, `binding-unknown`. This is consistent with the old incident evidence, but the current binding short circuit cannot prove which old database samples exist.

The persisted November 16 log also separates processes: an older process reached `dropped=10048` at 13:57:28; the current process first logged an append warning at `13:58:10.904` with `dropped=0`. Its queue then fills and its loss counter grows. Counter resets are process boundaries, not recovery or recovered samples. The current failure survived the runner replacement. The diagnosis makes no claim about historical disk/I/O pressure: v1 live stats retain 30 minutes and do not recover missing durable I/O-wait evidence.

## Minimal separate repair proposal

1. Commission a small Code slice to classify spool-open/append failures with bounded reason codes and phase, exposing dropped count and last successful append time. Rate-limit/coalesce warnings. Never emit raw exception text, cursor/HMAC material, arbitrary paths, tokens or configuration dumps. Distinguish permission, path/link, ledger/recovery, validation/serialization and authority-unknown conditions while keeping live sampling independent. Guard an empty LocalApplicationData result so it cannot silently select cwd-relative storage.
2. Commission the operational owner separately to select a private durable HistoryPath under the runner's state volume (for example `/state/host-load`), provision least-privilege access for UID 1654, and set matching physical-host key/revision/epoch on runner and server. Verify exactly one primary collector and the shared broker's matching authority identity/GET support. Do not copy or regenerate an existing telemetry cursor/store identity blindly. No settings, broker upgrade, deployment or restart is authorized by this report itself.
3. Qualify a new measured window after that separately commissioned activation: direct history returns samples including I/O wait, memory and volumes; server history is complete for the fixed interval; a fresh task has known start/during/end coverage. Observe the ledger acknowledgement and database cursor advancement twice through approved metadata-only projections (counts, ordinal/sequence frontier and commit times; raw opaque cursor and ledger secret remain private). Distinguish newly inserted samples from duplicate replay and prove cursor commit precedes acknowledgement.
4. In isolated fixtures and the separately authorized restart window, verify identity-preserving replay/restart and bounded distinguishable permission/path/serialization/authority failures. Retain old missing intervals and incomplete acceptance honestly; do not manufacture samples or reset historical loss evidence to obtain a complete flag.

For metadata-only database evidence, the operational owner can inspect server2's `HostLoadIngestCursors` row's existence, `RunnerStoreId`, `CommittedAt`, `Complete` and cursor-present boolean, plus server2 sample count/latest `SampledAt`/`IngestedAt` and source sequence per boot. Compare successive observations without projecting `Cursor` or any raw ledger contents. Successful frontier advancement and complete API coverage are separate checks.

## Verification and boundaries

No build or test suite ran; this deliverable changes only this investigation. HTTP checks were bounded GETs, configuration output was allowlisted, log retrieval was bounded or narrowly filtered, and the child path probe performed only path queries. No service settings change, deployment, restart, deletion, process kill, host SSH, secret/credential file access, cursor acknowledgement request, or source repair was performed. The absent diagnostic XDG path remained absent. Standing authority covered the diagnosis and report publication; no confirmation was requested.

Rechecks use the documented routes: direct `/host-stats`, `/host-stats/history?limit=1` **without `after`**, broker `/build-slots/host-load`, server `/api/session-runners/server2/status`, `/api/hosts/server2/load`, fixed-window `/api/hosts/server2/load/history`, and `/api/agent-tasks/{taskId}/load?attempt=1`. Forward the inherited task token only in the server request header; never put it in URLs or diagnostic output. Do not treat an unknown binding or pagination cursor as an ingest receipt.

--- next stage ---
next: decide
handoff: Commission bounded collector diagnostics/default-path hardening and a separate operational storage/binding/broker repair, then require measured durable sample and metadata-only cursor progress. Current production exception text/private options and database cursor state remain unobserved; historical gaps and deployment acceptance remain incomplete.
artifact: docs/investigations/2026-11-16-card-1893-telemetry.md
