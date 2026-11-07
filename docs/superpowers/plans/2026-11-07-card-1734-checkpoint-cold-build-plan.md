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
| CP-2 | all | CP-1 | unit | `/*/*/*/*[Category=Unit]` | R-2 | >= 1 executed, all required Unit guard classes present, 0 failed | 1 | 12 |
| CP-3 | all | CP-1 | registry | `/*/*/SlowTestRegistryMergeTests/*` | R-3 | all listed, 0 failed | 1 | 1 |
| CP-4 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c1734-runner/` | runner-policy | `/*/*/(NativeLinuxPolicyWitnessTests*)\|(TestClassificationGuard*)/*` | R-4 | all listed, 0 failed/skipped | 1 | 3 |
