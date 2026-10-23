# CARD-1564 live startup timing implementation and verification

Code owner / landing owner: `6e89ac58-d2e0-411e-a1f1-f9b27b3b2aa5`.
Base: `4497af9b48136a8003def54331f49ac66fd2dec9`.
Direct Code brief supersedes a separate Plan stage. The card is the specification.

Use a small shared instance-owned recorder, fixed enum labels, monotonic durations,
ActivitySource `Antiphon.Live`, structured startup completions and warnings for external
calls over 1000ms. No arguments, SQL, URLs, paths, headers, exception text or payloads
are accepted by the recorder. Retain at most 64 startup records; external calls have
no retained history. Export is opt-in through the existing OpenTelemetry provider.
Existing operations retain their cancellation, process ownership and deadlines.

Phase vocabulary follows CARD-1514: db-create, db-migrate, fixture-setup,
host-startup, process-wait, filesystem, network-loopback. Operation distinguishes
config-load, database-connect, seed, setup, host-build/start, listeners-ready,
runner-registration, adoption and external DB/git/HTTP/filesystem boundaries.
HTTP is `network` (remote HTTP is not claimed loopback); DB calls are `db-query`.

## Performance budget

No containers, migrations or production services in recorder tests. One in-memory
host fixture per endpoint test is necessary because startup/HTTP is the subject.
Provisional build budget 3 minutes; test execution 30 seconds, admission separate.
No measured p95 yet; collect this first sample under CARD-1564. No timeout increase.
App category: server/shared runner observability; downstream server, runner, AppHost.
No full assembly run: direct brief requests narrow witnesses. New standalone test
project keeps this selection independent of unrelated fixture/checkpoint work.
TRX and checkpoint phase timings retain execution/build/slot separation; inner
unmeasured phases are unknown. Native Windows AppHost acceptance is required if
restart-path scripts are edited; Linux cannot substitute for that probe.

## Verification design

V-1: startup duration, phase/outcome, Activity parent and structured log.
V-2: failed/canceled default and idempotent completion/disposal.
V-3: bounded external slow-call warnings and no startup history growth.
V-4: failing sink isolation, bounded immutable snapshot and no exception payload.
Further wiring/API witnesses will be added before integration changes.
PC inventory pending SourceLanding Mutation: PC-1 remove startup publication;
PC-2 overwrite incomplete outcome; PC-3 suppress slow-call warning;
PC-4 remove sink containment/history bound. Mutation owns red/restore/green.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | S1-S2 | `tests/Antiphon.Observability.Tests -> bin-c1564-contract/` | live-timing-core | `/*/*/LiveTimingTests/*` | V-1,V-2,V-3,V-4 | all listed, 0 failed | 4 | 3 |
| CP-2 | S1-S2 | CP-1 | live-timing-http | `/*/*/StartupEndpointTests/*` | V-5,V-6 | all listed, 0 failed | 2 | 3 |

V-5: actual server VersionEndpoints extension serves the injected live recorder over TestServer.
V-6: real delegating handler creates failed span, preserving exception; synthetic secret in
URI/header/content/exception never enters its tags/logs. Both use in-memory transport.
PC-5 pending Mutation: remove diagnostics route. PC-6: leak request URI into HTTP span.
