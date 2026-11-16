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

## Continuation receipts

Both rows below were executed by `scripts/run-checkpoint.ps1` on Linux.
The checkpoint tool's `deadlineMinutes=9` had already stopped the cold
`bin-c1893-guards2/` and `bin-c1893-guards3/` builds. This script keeps its
own build slot and records `buildSource=verified`. CP-3 build time was 607.7s
with compiler `cache=hit`, policy `c1885-v1`, cpus 0-5. CP-3 test wall was
208s. CP-4 reused that build and its test wall was 163s. `run-checkpoint.ps1`
has no `--timeout 570s` parameter. No test timeout or assertion was widened.
Production collector code remains `40730e2ebae97b8060bece919fb7cbd441da9573`.
The tested source for both rows is `4c1d55d376520a66521237fbd0afcdd05adc57cc`.

CP-3 started at load 14.85 (2026-11-16T20:15:08Z). Slot lease
`28464555-a30d-432c-a9f7-a02d0dd163ff`, waited=0s, held=974s.
CP-4 started at load 15.17 (2026-11-16T20:31:47Z). Slot lease
`94698e26-c0ea-4559-b090-14922163987e`, waited=0s, held=192s.

```
CHECKPOINT CP-3 commit=4c1d55d376520a66521237fbd0afcdd05adc57cc build=ok filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(DerivedImpactIndexTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)|(WindowsUnitPlatformAdmissionTests*)/* executed=114 passed=114 failed=0 skipped=1 trx=/work/worktrees/task-74b623f9/.antiphon/checkpoints-c1893-script/CP-3-20261116-201511-0137/run.trx slot=granted waited=0s dirty=0 source=4c1d55d376520a66521237fbd0afcdd05adc57cc sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=4c1d55d376520a66521237fbd0afcdd05adc57cc build=reused filter=/*/*/(HostTopConsumerUpgradeReplayTests*)|(C1514HostLoadHistoryTests*)/* executed=26 passed=26 failed=0 skipped=0 trx=/work/worktrees/task-74b623f9/.antiphon/checkpoints-c1893-script/CP-4-20261116-203149-3205/run.trx slot=granted waited=0s dirty=0 source=4c1d55d376520a66521237fbd0afcdd05adc57cc sourceState=clean buildSource=verified
```

CP-3 TRX class counts, all eleven filter classes present: TestClassificationPolicyTests 42 passed, DerivedImpactIndexTests 27 passed, C1729LayeringGuardTests 11 passed, MechanicalPcContractTests 11 passed, WindowsUnitPlatformAdmissionTests 6 passed and 1 NotExecuted, TaskPlatformGuidanceTests 5 passed, SlowTestRegistryMergeTests 5 passed, NativeLinuxPolicyWitnessTests 3 passed, SlowTestTripwireTests 2 passed, TestClassificationGuardTests 1 passed, TestLaneCategoryGuardTests 1 passed. `C1729LayeringGuardTests.Repository_graph_satisfies_the_committed_allowlist` printed `VIOLATIONS 0 LISTED 162 STALE 0 AMBIGUOUS 3`. `DerivedImpactIndexTests.committed_index_matches_the_builder` printed `derived-impact windowsOnly count=31 linuxOnly count=1`. The NotExecuted result is `WindowsUnitPlatformAdmissionTests.C1210_Windows_linux_only_outcomes_are_exact`, DebugTrace `Skipped: CARD-1210: the child skip-roster audit requires Windows placement.`

CP-4 TRX: HostTopConsumerUpgradeReplayTests 4 passed, C1514HostLoadHistoryTests 22 passed.

`scripts/validate-checkpoint-receipt.ps1 -ExpectedSourceSha 4c1d55d376520a66521237fbd0afcdd05adc57cc` returned `CHECKPOINT SOURCE VALID` for CP-4 (`rows=1`). CP-3 returned `CHECKPOINT SOURCE INVALID reason=receipt_failed` because `skipped=1`. The same file's source check is what runs first: the refusal is the skip count, and the receipt's commit, fingerprint, `dirty=0`, `sourceState=clean` and `buildSource=verified` are the values that check accepts. The qualifier has no platform-skip allowance. That Windows case stays pending.

Pins recomputed on this tree and unchanged: policyHash `c2e3939821a8fef911600c44be767b951c8212bcb62824cb746e48c5aaba6909`, PinnedInputDigest `2f48120a65280582b586f9369e2eaf6b1bdc464a2362c8e49d434bd852479f60`, LegacyExclusionDigest `6d6ff646207dba9e3c09316e5bc77651f6801fe945ccde83b9b58644367b252a`. CP-2's 52/52 result at `40730e2e` was preserved and not rerun. PC-1 through PC-6 remain pending Mutation. Restart: none.
