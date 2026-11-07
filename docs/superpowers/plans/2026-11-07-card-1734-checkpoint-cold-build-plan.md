# CARD-1734: preserve preparatory intermediates and terminal receipts

Original Code/landing owner: a38720f2. Base: fbb2be214a849549cfa72f2e9a17646cc848cfeb.
The caller withdrew the brief's rebase prerequisite; continue on the assigned branch.

## Implementation and verification design

The SessionRunner test project's Linux `CopyOwnedPtyHostAppHostForHerdr` target starts
a separate dotnet process (CARD-1664). Unlike an MSBuild task, that process does not
inherit global artifact isolation properties. Forward those properties only when
artifact output is enabled. Keep the separate process, native apphost/RID, custody
assertions, timeout budgets and source/report validators intact. No product changes.

S1 commits the red-first harness and this plan. S2 forwards the isolation properties.
The harness copies the actual shipped target verbatim into a minimal project; it
does not duplicate the command under test. Separate fixture roots are necessary:
native preparation and cold output custody are the subject. No server, DB or runner
startup is involved. The real TUnit child checks the staged native payload.

V-1: tool cold build after a native preparatory build preserves every existing native
obj byte, executes one native-payload probe and publishes mutually bound report.md,
report.json and phase=done state. V-2: script entry does the same, with source.json
and its terminal CHECKPOINT line. R-1: deliberate fixture target writes a tracked
sentinel; custody still refuses exit 2 and publishes a valid RED terminal report,
leaving changed bytes for manual recovery. The historical missing terminal receipt
is independently checked; a native-obj tampering refusal must never be bypassed.

R-2: whole Unit lane, including classification policy/guard, SlowTestTripwire,
NativeLinuxPolicyWitnessTests, C1729LayeringGuardTests, MechanicalPcContractTests
and TaskPlatformGuidanceTests. R-3: full SlowTestRegistryMergeTests integration
class. R-4: actual SessionRunner project cold build and its full policy witness/
classification guard classes; native helper staging is part of that build.

## Mutation handoff

All controls remain pending for method-scoped SourceLanding Mutation:
PC-1/tool and PC-1/script: omit the forwarded isolation arguments; V-1/V-2 must fail
the native obj snapshot assertion. PC-2: suppress the in-slot tampering refusal;
R-1 must fail its exit/refusal assertion. Mutation owns red/restore/green cycles.
Candidate: docs/superpowers/mutations/2026-11-07-card-1734-cold-build.md (unbound).

## Performance budget

Provisional, not measured p95: each new witness <=90s expected; timeout 180s,
matching existing real-build fixture bounds and allowing cold SDK/restore work.
Collect one red and one green sample per case in this task. No loaded repetitions
requested. Preparation/build/run use the existing bounded process helper; teardown
joins children through CheckpointTestScope. TRX holds per-case duration; existing
test phase instrumentation holds process/setup/teardown spans, unknown remainder
stays unknown. Native incremental compile is separately reported as the brief's
required preparatory build. Tool bootstrap is produced by that project reference.

CP-1 estimated 5 minutes including build; CP-2 Unit estimated 12 minutes (provisional
historical Unit sample has no current p95); CP-3 1 minute; CP-4 3 minutes including
the SessionRunner build. No full-assembly run. Run at most three rows per run,
serial builds, fresh outputs, committed expected SHA. Each run's total derives
from selected estimates; checkpoint importer floor is retained as an existing
tooling exception, not a widened test timeout. Any observed run over 30 minutes
requires Debug. Slot admission is separate and bounded by the existing 45m limit.

S1 measurement (5d9403e839c9025084f5173b33b1342bde18aba1, Linux, shared host):
CP-1 wall 700.14s; build 499.84s; fixture bodies 72.49s (script 24.03s,
tracked-tampering 19.64s, tool 28.53s); row slot wait 30s. The build exceeded
the provisional estimate because isolated outputs rebuild and stage helpers.
Retain the existing row/test timeouts; do not increase the estimate to widen them.
The first two cold-build variants failed at the intended snapshot assertion;
R-1 had a diagnostic-location harness error, corrected by exact JSON build-reason
and named build-log assertions in S2. Historical outcomes remain RED.

## OS and startup impact

Only the existing Linux-selected test build target changes. Witnesses execute its
real Exec command on Linux with native/artifact inputs and assert native bytes,
payload staging and terminal receipts. No new Windows-selected runtime behavior;
the Windows branch continues to omit this target. Startup-path census: none;
restart server/runner: none. Tests policy JSON and pinned hashes remain unchanged.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-c1734-main/` | cold-build | `/*/*/CheckpointColdBuildTests/*` | V-1, V-2, R-1 | all 3 cases, 0 failed/skipped after S2; S1 expects both cold-build variants RED at snapshot assertion | 3 | 5 |
| CP-2 | all | CP-1 | touched | `/*/*/(C1307CliCancellationTests*)\|(WaitCommandTests*)\|(CheckpointToolCopyCleanupTests*)\|(CheckpointStatePublicationTests*)\|(CheckpointAppTests*)/*` | R-5 | all full affected classes, 0 failed | 1 | 5 |
| CP-3 | all | CP-1 | guards | `/*/*/(TestClassificationPolicy*)\|(TestClassificationGuard*)\|(SlowTestRegistryMerge*)\|(SlowTestTripwire*)\|(NativeLinuxPolicyWitnessTests*)\|(C1729LayeringGuardTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)/*` | R-2 scoped, R-3 | 70 guards plus 5 registry cases, 0 failed | 75 | 4 |
| CP-4 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c1734-runner/` | runner-policy | `/*/*/(NativeLinuxPolicyWitnessTests*)\|(TestClassificationGuard*)/*` | R-4 | all listed, 0 failed/skipped | 1 | 3 |
| CP-5 | all | CP-1 | terminal | `/*/*/CheckpointTerminalReceiptTests/*` | V-3, V-4, V-5, V-6 | supported stop, cancel, executor failure and timeout reach a real waiter with valid non-passing receipts | 4 | 3 |

## B-1 repair (Code bfe680c4, Review round 2 input)

Invariant: supported stop and direct executor cancellation/failure/timeout publish
truthful source-bound terminal receipts after joined teardown and before a waiting
process returns. Completed counts stay known; unrun/unknown counts stay null.
Do not weaken receipt validators or native custody.

V-3 is `C1734_Stop_publishes_before_the_executor_waiter_returns`: a real executor
and separate CLI waiter, with a stopper barrier after joined death but before
publication. V-4/V-5/V-6 are the cancel/failure/timeout variants of
`C1734_Executor_terminal_receipt_reaches_a_real_waiter`. Each validates terminal
identity, times, source, report digest and non-passing eligibility; a forged green
must still be refused. R-5 runs the full named adjacent classes in CP-2.
Fresh process startup, cancellation and teardown are the subject, so each case
needs its own executor/waiter. No DB, service or production runner is started.
One-minute existing row deadline is retained for the timeout witness; the new
180-second test bound follows the existing cold fixture bound. Provisional sample
plan: one red and one green, no loaded repetition; record TRX durations and slot/
build phases. CLI/filesystem spans unavailable in the process fixture remain
unknown. No full assembly run is selected.

The repair brief explicitly excludes historical whole-Unit R-2/old CP-2. Its
36-minute timeout, two inherited receipt fixture failures (CARD-1739 owner
68c34eea), and observed intermittent probe failure remain incomplete; CARD-1720/
1728 own lane completion/performance. CP-3 is the single requested guard/registry
batch. This scoped repair never claims whole-Unit qualification.

Additional pending Mutation controls (method-scoped, unbound): PC-3 omit stop/
cancellation receipt write; PC-4 forge passed on cancel; PC-5 let the waiter
return before stop publication. Original PC-1/tool, PC-1/script, PC-2 and Review's
report-publication/digest candidates also remain pending. Code does no deliberate
mutations. Restart: server/runner none; no OS-selected production branch changes.
