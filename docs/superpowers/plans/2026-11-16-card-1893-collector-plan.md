# CARD-1893 collector path hardening and bounded diagnostics

Code task/landing owner: f99cc46c-d77b-45de-ab1f-a409e2ed1469.
Base: 48a4615dcb4d67033886a3e31629dc7351ed0852 (current origin/master at start).
Diagnosis: origin/feat/card-task-705f622b, ce399e772010ab89edc018fb133376badb613eb5,
docs/investigations/2026-11-16-card-1893-telemetry.md. No supplied Plan/TestDesign
exists; this execution manifest closes the brief's explicitly authorized scope.

Resolve absent paths from absolute local application data using DoNotVerify,
falling back to absolute user-profile data. Never select cwd on missing profile
data. Configured relative paths are rejected explicitly by the worker. Do not
silently replace configured stores. Filesystem access stays off the sampler.
Publish fixed reason counters and at most eight scrubbed diagnostic records,
one warning per minute per collector, no raw paths/exception messages/cursors.
Keep the cursor payload metadata-only and count only successfully persisted
acknowledgement advances. Add fields on existing runner surfaces only.

S1 commits failing witnesses; S2 commits implementation; S3 builds the existing
derived-index driver first, regenerates twice and commits index changes separately.
No storage activation, binding, broker, retention, deployment or restart changes.
Operator waived deliberate mutation execution (2026-11-12); PCs remain pending
for separately commissioned Mutation, never counted as ordinary proof.

## Performance budget

Serial rows, no full assembly/lane. Existing runner fixtures share one loopback
host per test session, with private per-case spool paths. New tests use the same
fixture and fake clock; no database or external calls. Provisional 10-second
case budget follows existing spool/wire tests; bounded polling is five seconds.
Cold builds and guard costs are not measured on this host: estimates below are
provisional, each execution capped at 570 seconds; admission reported separately.
The checkpoint controller accepts bare durations in seconds and rounds scheduled
deadlines to whole minutes. Invoke with `--row-timeout 540s` (nine minutes),
below the brief's 570-second ceiling; controller `--timeout 570s` is not a row
override. An initial `9.5` was incorrectly interpreted as seconds and produced
one-minute build deadlines, with zero test execution. Preserve that failed run;
this corrects the invocation, without widening any test or acceptance timeout.
Run 20261116-194047-0118 kept bin-c1893-guards2/ after build-deadline-exceeded
at deadlineMinutes=9 (load 9.77 at start, 24.63 near the deadline).
Run 20261116-195838-d7e6 was stopped at load 31.75 while bin-c1893-guards3/
still had no Antiphon.Tests.dll (server dll at 20:03:19Z, start load 12.01).
CP-3 uses bin-c1893-guards4/ so those failed outputs are not reused.
The 540s test ceiling stays; do not pass 9m30s.
Record loadavg, TRX case durations and checkpoint phase timings. No new timing
spans or selector rows. No unchanged proof repetitions after green. Driver and
checkpoint bootstrap builds are explicitly unlisted prerequisites, gated, native
default output. Uninstrumented startup/teardown remainder remains unknown.

## Verification design

V-1: absent/empty/configured path states and blocked default candidate report
open stage, safe path class, writable=false and actionable reason, never cwd.
V-2: bounded/coalesced warnings and eight-record cap, fixed drop reasons, no raw
exception message, path, arbitrary exception type name or credentials.
V-3: accepted/appended counts, successful cursor advancement and metadata-only
cursor payload; replay/restart preserves store/sample identity.
V-4: additive diagnostics on /host-stats and /host-stats/history, including 503.
R-1: whole touched collector/spool, host-stats and named adjacent wire classes.
Include HostTopConsumerUpgradeReplayTests (the server's direct capture-service
caller) and C1514HostLoadHistoryTests for ingest/replay/cursor commit ordering.
R-2: impacted-classes rows matching full base..HEAD; host-stats selects
RunnerCapabilitiesTests. Inspect exact final selection before settlement.
R-3: guard union, C1729 VIOLATIONS 0 LISTED 162, index idempotence, pin verification.
R-4: full-range evidence-diff, OS declaration lint, merge-tree and clean push.
Manual deployment acceptance is outside this Code scope: new measured production
coverage, physical binding, broker repair and activation require caller-owned work.

PC-1 default path regression; PC-2 scrub bypass; PC-3 rate-limit bypass;
PC-4 record-cap bypass; PC-5 drop/append counters; PC-6 persisted cursor counter.
Every variant pending Mutation; no deliberate mutants run by Code.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---|---|
| CP-1 | red | tests/Antiphon.SessionRunner.Tests -> bin-c1893-red2/ | collector-red | `/*/*/C1893CollectorDiagnosticsTests/*` | V-1,V-2,V-3,V-4 | intended assertion failures before implementation | 5 | 5 |
| CP-2 | green | tests/Antiphon.SessionRunner.Tests -> bin-c1893-green2/ | collector-and-stats | `/*/*/(C1893CollectorDiagnosticsTests*)\|(C1514HostLoadSpoolTests*)\|(C1514HostLoadWireTests*)\|(C1514HostLoadSampleTests*)\|(C1514HostLoadAuthorityTests*)\|(HostStatsEndpointTests*)\|(HostStatsSamplerTests*)\|(HostStatsStoreTests*)\|(RunnerCapabilitiesTests*)/*` | V-1,V-2,V-3,V-4,R-1,R-2 | all listed, 0 failed | 9 | 7 |
| CP-3 | green | tests/Antiphon.Tests -> bin-c1893-guards4/ | guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)/*` | R-3 | all listed, 0 failed | 11 | 9 |
| CP-4 | green | CP-3 | server-ingest-replay | `/*/*/(HostTopConsumerUpgradeReplayTests*)\|(C1514HostLoadHistoryTests*)/*` | V-3,R-1 | all listed, 0 failed | 5 | 5 |
