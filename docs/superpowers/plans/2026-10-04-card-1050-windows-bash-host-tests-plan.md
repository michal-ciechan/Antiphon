# CARD-1050: place C1008 host contracts on native Linux

Plan, 2026-10-04. Inspected source: `49981e134198957f8662f7573cbd6b7332ae690a`.
Decision: explicitly skip the sixteen named host tests outside native Linux;
retain their PowerShell and source-text coverage in portable tests. Do not add
MSYS path translation to this Linux host simulator.

Complexity: **medium verification, small implementation**. TestDesign is a
separate next stage. Intentional Windows skips need an exact name/reason audit,
while ordinary checkpoint clean certificates require executed, unskipped tests.
Five mixed methods also require preserving their portable assertions, and the
fixture has consumers outside this card. This is not an executable Code
manifest yet; TestDesign must complete the verification design before Code.

## Ground truth

The card was read with `scripts/card.ps1 get CARD-1050 -Board Antiphon`.
Its Windows results are retained triage evidence from task `7951fe34` at
`53e165503`, not a fresh reproduction by this Plan. No tests or builds ran here.

| Card assumption | What the inspected code does | Design consequence |
|---|---|---|
| Twelve tests fail before the remote script starts because bash receives a Windows path. | `C1008HostFixture.Run` in `RollingVolumeRecycleScriptTests.cs:584` passes `Path.Combine(Root, "remote.sh")` directly to `ProcessStartInfo("bash").ArgumentList`. Its injection also embeds native `Root` and `DelegateScriptRunner.RepoRoot` in shell source and invokes another bash fixture. | Converting only the initial script argument would leave embedded paths, nested processes and JSON mount paths inconsistent. |
| Three Git setup tests fail from CRLF and Windows paths. | `RemoteScriptContractTests.C1008GitGraph` at line 714 constructs a C# multiline raw string, interpolates native paths into `git -C`, and sends it unchanged to `bash -c`. | A portability fix would also need line normalization, shell quoting and consistent path identity through Git and the audit oracle. |
| The lock helper never creates `held`. | `C1008_Recycle_refuses_references_and_unknown_census` starts a separate `bash -c` with native paths, real `flock`, redirected stdin and bounded readiness/release waits before calling `Run`. | A guard inside `Run` would be too late. Do not increase its ten-second readiness deadline. |
| Checking in shell files with LF fixes all CRLF input. | `.gitattributes` already forces `scripts/*.sh` to LF. It does not normalize multiline C# string contents, injected shell text or every nested fixture script. `Run` writes the combined text unchanged. | Changing `.gitattributes` alone does not fix this launch family. |
| These simulate a supported Windows deployment lane. | The production remote host lane is Linux. The fake Docker script invokes Node, spawns another bash audit, creates executable Git shims with POSIX modes and a colon-separated PATH, and remaps `/work` into fixture paths. Tests also exercise real symlinks, `flock` and path-byte hashes. UID `1654:1654` is an asserted Docker argument; the fake does not actually switch the test process's uid. | Native Linux is the meaningful host-fixture execution environment. A Git-bash green would not qualify real Linux ownership, locking or deployment. |
| All sixteen failing methods are purely host tests. | Four methods in `RollingVolumeRecycleScriptTests` mix host execution with Windows-capable wrapper/text assertions. `C849_Deploy_prepares_and_verifies_before_acceptance` mixes text checks, C1008 host execution and the existing LinuxShell harness. | Split out the portable assertions before making the original sixteen methods Linux-only. Keep existing host method names for historical evidence. |
| A constructor-level skip affects only these tests. | `C1008HostFixture` allocates `Root` in a property initializer and starts Node during construction. It is also used by `RollingProductionMountTests`, `RetiredTempContainerHostTests` and `RetiredTempContainerScriptTests`. | Use an explicit, side-effect-free entry guard at the sixteen named methods. Do not change constructor/Run admission globally or silently skip other cards' tests. |
| The Linux jq failures and readiness retry failures need the same repair. | CARD-1040 owns native Linux jq qualification; its plan requires C1008 to remain visibly red when required jq is missing. CARD-1049 concerns `Infrastructure/AmServiceDeployReadinessTests`, Windows curl 28 versus 7 and cygpath lookup. Windows jq was present in CARD-1050's triage. | Neither dependency installation nor curl/retry behavior belongs here. No cygpath dependency is introduced. |

Owners consulted: [testing and builds](../../testing-and-build.md),
[orchestration](../../orchestration-loop.md), [HTTP placement](../../ops-http.md),
[project conventions](../../project-context.md), and
[volume recycling](../../docker-stack.md#volume-recycling-and-disk-reclaim-card-1008).
Relevant bodies inspected include all sixteen methods below, `C1008GitGraph`,
both C1008 fixtures, `LinuxShell`, `.gitattributes`, and
`scripts/fixtures/c1008-fake-docker.sh`. The additional fixture consumers were
inspected for their call boundaries, not qualified as part of this card.

## Decisions

- **D-1 — Use native Linux for host behavior.** The required product behavior
  exists on that OS. Reject the MSYS/LF port: it is a wider emulation project
  involving nested bash, Node, Git, mount JSON, symlinks and path hashes, not
  just `cygpath -u` on one argument. Reject WSL adaptation: the fixture currently
  owns native .NET temporary paths and native children, unlike `LinuxShell`'s
  stdin-based Linux harness. Neither port is needed to test the product lane.
- **D-2 — Guard only the named host entries, before any work.** Add the
  side-effect-free static method `C1008HostFixture.RequireNativeLinux()`. It
  throws `TUnit.Core.Exceptions.SkipTestException` when
  `!OperatingSystem.IsLinux()`, with this exact reason:
  `CARD-1050: C1008 host contracts require native Linux (bash, flock and POSIX filesystem semantics); Windows PowerShell wrapper contracts run separately.`
  Call it as the first executable statement of each H-01..H-16 method. It must
  not instantiate the fixture, allocate its root, probe tools, read source or
  launch a process. Reject class-wide skips and a constructor/Run guard: they
  either drop unrelated coverage or change additional fixture consumers, and
  Run is too late for Git/lock setup. Keep Linux missing-tool failures visible.
- **D-3 — Preserve portable coverage through five extractions.** Use the
  mapping below, without weakening any existing assertion. Reject simply
  skipping the mixed methods wholesale: parameter validation, sanitized wrapper
  failures, ASCII transport and retirement requests remain relevant on Windows.
  Do not extend Linux-only scope to the whole test classes.
- **D-4 — Keep production scripts and launch mechanics unchanged.** No changes
  to `c590-remote.sh`, deployment scripts, fake Docker behavior, path conversion,
  PATH, `.gitattributes`, jq guards, retry policy, process limiters or deadlines.
  Only add the explicit opt-in helper to the shared fixture file. Existing
  `LinuxShell` remains unchanged; its retained C849 shell checks now run inside
  the Linux-only host method.
- **D-5 — Prove both lane outcomes.** Windows must report the exact sixteen
  intentional skips and execute the extracted portable tests. Native Linux
  must execute all sixteen host methods with zero skips. Windows skips are
  coverage placement evidence, not sixteen passing host tests. Do not relax
  generic checkpoint receipt validation to accept them as a clean green suite.
- **D-6 — Keep verification bounded.** Two Code slices of 30–60 minutes each,
  narrow filters, no whole Unit/namespace/assembly run. TestDesign freezes one
  method-scoped positive control per changed behavior; do not repeat the
  unchanged recycling safety mutation battery. Commit/push each slice before
  its checkpoint group and never edit source during a run.
- **D-7 — Land this documentation through the caller.** This delegate pushes
  only its assigned branch. The caller lands the succeeded Plan task promptly
  through the normal landing route, then dispatches TestDesign against that
  landed plan. Direct master pushes and rebasing the assigned branch are not
  authorized by its branch contract.

These are resolved design decisions, not defaults awaiting a human choice.

## Exact host roster

All rows are single, non-parameterized TUnit methods at the inspected source.
H-01..H-12 belong to `RemoteScriptContractTests`; H-13..H-16 belong to
`RollingVolumeRecycleScriptTests`. Keep these names after the extractions.

| ID | Method | Windows | Native Linux |
|---|---|---|---|
| H-01 | `C1008_Recycle_exact_default_volumes` | explicit D-2 skip | execute |
| H-02 | `C1008_Recycle_refuses_references_and_unknown_census` | explicit D-2 skip before lock child | execute |
| H-03 | `C1008_Recycle_audits_work_as_1654` | explicit D-2 skip before Git setup | execute |
| H-04 | `C1008_Recycle_refuses_unpublished_and_dirty_work` | explicit D-2 skip before Git setup | execute |
| H-05 | `C1008_Recycle_refuses_uninspectable_git` | explicit D-2 skip before Git setup | execute |
| H-06 | `C1008_Recycle_preserves_tmp_copyup` | explicit D-2 skip | execute |
| H-07 | `C1008_Recycle_resume_requires_matching_receipt` | explicit D-2 skip | execute |
| H-08 | `C1008_Recycle_receipt_records_disk_and_partial_failure` | explicit D-2 skip | execute |
| H-09 | `C1008_Retire_temp_rechecks_absence_and_retirement` | explicit D-2 skip | execute |
| H-10 | `C1008_Retire_temp_reclaims_below_cache_disk_gate` | explicit D-2 skip | execute |
| H-11 | `C1008_Recycle_dry_run_never_mutates` | explicit D-2 skip | execute |
| H-12 | `C849_Deploy_prepares_and_verifies_before_acceptance` | explicit D-2 skip | execute |
| H-13 | `C1008_Option_manifest_is_strict` | explicit D-2 skip | execute host no-context control |
| H-14 | `C1008_Documentation_and_transport_pins_match` | explicit D-2 skip | execute WrongLane control |
| H-15 | `C1008_Refusal_receipts_do_not_leak_secrets` | explicit D-2 skip | execute Git/Docker custody cases |
| H-16 | `C1008_Retired_absent_null_is_accepted` | explicit D-2 skip before wrapper | execute wrapper-to-host contract |

## Portable assertion extractions

Each new name below is a single non-parameterized test in the same class as its
original. Keep `Category("Unit")` through the class; process-spawning methods
retain `ParallelLimiter<ProcessSpawnLimit>`. Text-only methods need no new
process limiter. These are moved assertions, not a new production behavior.

| New method | Original and exact responsibility retained |
|---|---|
| `RemoteScriptContractTests.C849_Deploy_ordering_contract_is_pinned` | H-12's initial source/order checks through `c849_budget_gate` before `build_server2_images`. Keep the subsequent host fixture cases and LinuxShell execution in H-12. Both methods read their own `Remote()` source as needed. |
| `RollingVolumeRecycleScriptTests.C1008_Wrapper_option_manifest_is_strict` | H-13 after its initial `legacy` host block: typed preview, rejected opt-in flags, invalid operation IDs and both real PowerShell JSON validators with all ten vectors each. |
| `RollingVolumeRecycleScriptTests.C1008_Transport_scripts_are_ascii` | H-14's ASCII byte checks for its existing four script paths; leave the WrongLane host assertion in H-14. |
| `RollingVolumeRecycleScriptTests.C1008_Wrapper_refusal_receipts_do_not_leak_secrets` | H-15's final wrapper task-census failure, typed refusal, secret sentinels and absence of `UnhandledExit`; leave its two host blocks in H-15. |
| `RollingVolumeRecycleScriptTests.C1008_Wrapper_retired_absent_null_is_accepted` | H-16's wrapper trace, exit, no-POST, saved retirement stamp, typed recycle project/dryRun and operation-ID syntax assertions. Factor that existing sequence into a private async helper returning the request JSON. Both this portable test and H-16 await it; H-16 first passes D-2, then passes the exact returned operation/stamp into the real host fixture and asserts all four removals. Do not fabricate a request in the host test. |

The other existing portable C1008 methods stay unchanged, including
`C1008_Present_or_unknown_temp_keeps_null_refusal`,
`C1008_Busy_routed_and_land_in_flight_refuse`,
`C1008_Same_sha_and_partial_retries_are_safe`, and
`C1008_Legacy_rolling_and_jq_rosters_remain`. They do not need a broad rerun for
this entry-guard/extraction change; the new tests exercise the affected wrapper
paths. In particular, do not modify the legacy jq roster contract.

## Implementation slices

| Slice | Budget and files | Concrete completion and tests |
|---|---|---|
| S1 | 30–60 minutes. `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs` (only the opt-in helper); `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs`. | Add D-2 helper, guard H-01..H-12, extract `C849_Deploy_ordering_contract_is_pinned`. Preserve every host assertion and process cleanup path. Checkpoint groups W1/L1 below; source audit proves guard precedes lock/Git/fixture work. Commit and push before execution. |
| S2 | 30–60 minutes. `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs`; `docs/testing-and-build.md` (one short C1008 platform paragraph next to jq guidance). | Guard H-13..H-16, perform the four Rolling extractions, document explicit native-Linux host versus portable wrapper coverage. Checkpoint groups W2/L2 below. Commit and push before execution. |

TestDesign may add one tightly scoped platform-admission regression/harness
under `tests/Antiphon.Tests/Scripts/` or `scripts/` if necessary for an executable
Windows skip audit. It must name its exact path, consumer, tests, process custody
and cost in the finalized design. It must not change general checkpoint tooling
or introduce a generic platform abstraction for this card.

Do not put an automatic skip in the common fixture constructor/Run, or modify
the three other consuming classes. Coordinate file overlap with CARD-1040 and
other active C1008 consumers before Code dispatch; overlapping edits are not
permission to broaden the repair.

## Verification handoff — separate TestDesign

### Placement

Both `GET /api/runner-defaults` and `GET /api/session-runners` were read at
approximately 23:07 UTC on 2026-10-04. Defaults revision was 2; eligible Linux
and Windows descriptors were available. This is a placement observation, not
proof of a test environment or build. Re-read the routes before dispatch.
No runner ID, filesystem location or host occupancy is a plan requirement.

Use `-Platform Windows` for W1/W2 and the corresponding platform control.
Use `-Platform Linux` for L1/L2: Windows/WSL does not substitute for native
Linux. Omit `-Runner` throughout. Planning and TestDesign need no OS pin;
`-Platform Any` clears one inherited from a predecessor/card. The caller
coordinates any required second-lane execution; this Plan delegate does not
sub-delegate or have access to the desktop checkout.

### Bounded checkpoint scope to finalize

This table fixes required lanes and rosters, not runnable checkpoint syntax.
TestDesign must expand every named roster into literal method filters and the
owner's `### Checkpoints` schema. Do not select `C1008_*`, a whole class, Unit,
namespace or assembly. Preserve the exact expected method set after TUnit
discovery expands any OR prefix hints.

| Group / slice | Lane | Exact selection by IDs/names above | Expected outcome | Estimated build + execution; row wall budget |
|---|---|---|---|---|
| W1 / S1 | Windows | H-01..H-12 plus `C849_Deploy_ordering_contract_is_pinned` | 12 skips with the exact D-2 reason, 1 pass, 0 failures, no extra/missing cases | 5 minutes; 15 minutes |
| L1 / S1 | Native Linux, required tools present | Same 13 methods | 13 passes, 0 failures/skips | 8 minutes; 24 minutes |
| W2 / S2 | Windows | H-13..H-16 plus the four new Rolling methods | 4 skips with exact D-2 reason, 4 passes, 0 failures, no extra/missing cases | 6 minutes; 18 minutes |
| L2 / S2 | Native Linux, required tools present | Same 8 methods | 8 passes, 0 failures/skips | 6 minutes; 18 minutes |

The proposed ordinary budget is **25 minutes**, including four isolated builds;
lane totals are 11 Windows and 14 Linux. These are estimates, not measurements.
Native Linux prerequisite qualification/activation remains CARD-1040's job;
reuse its qualified environment/evidence. Missing jq stops Linux acceptance
with a prerequisite diagnosis, never a skip introduced by this card. No jq
installation or live rollout is included in these Code budgets.

### Required TestDesign decisions and proof

1. **Make Windows skip evidence executable.** Ordinary checkpoint source
   validation requires zero skipped tests; `Min` counts executed results, not
   discovered/skipped cases. Neither `Min=16` nor a zero-test green is valid.
   Design a narrow assertion-bearing audit that verifies the original methods'
   actual TUnit outcomes and exact skip reason, plus the passing portable
   roster. Preserve their raw TRX and source/build provenance separately from
   the audit verdict. Do not relabel skips as passes or weaken the receipt
   validator. Bound and await any nested test host; no fixture error or exit
   127/128 is a successful skip. This receipt boundary is why TestDesign was
   not folded into the Plan.
2. **Prove early admission.** The guard must precede every `new C1008HostFixture`,
   `C1008GitGraph`, lock process and wrapper call in H-16. A Windows test must
   not allocate a host fixture merely to discover that it should skip. Keep
   the guard's true native-Linux path covered by L1/L2. No tool-availability
   fallback, catch-all exception-to-skip conversion or silent return.
3. **Keep controls proportional.** Finalize one exact-method PC for the shared
   platform-admission behavior and one for preserving meaningful portable
   wrapper assertions if the extraction/audit adds an independent guard.
   Map independently bypassable safety assertions without inventing a PC per
   old recycling scenario. A build/fixture error, zero tests or an unrelated
   shell-launch failure is not the expected assertion red. Supply the concrete
   compiling mutation, decisive assertion, restore/green method and numeric
   PC cost before handing off to Code. Mutation executes after land; Code
   runs ordinary V/R, and Review checks both lane receipts before land.
4. **Check coverage conservation.** Every assertion moved out of the five
   mixed methods has a named portable destination. Linux preserves the full
   host and wrapper-to-host behavior, including real request operation/stamp
   propagation in H-16. The new ASCII/source methods must still read actual
   tracked scripts, never literals copied into tests.
5. **Freeze execution mechanics.** Use the checkpoint tool once per committed
   slice/lane group and foreground-supervised `wait` calls until terminal
   (exit 75 means still running). The tool takes the build slot; every other
   build/test driver requires `scripts/build-slot.ps1`. No unleased retry on
   slot timeout. Use distinct `bin-c1050-*/` outputs with forward slashes,
   bounded rows and serial test hosts; the process limiter is assembly-local.
   Remove only owned alternate outputs after all children exit. Preserve
   unedited CHECKPOINT lines and ignored TRX/JSON/logs, report counts honestly,
   and run `scripts/check-evidence-diff.ps1` over the full Code/Review range.

### Exclusions and acceptance boundary

CARD-1049's curl outcomes/cygpath precondition, CARD-1040's jq provisioning,
production deployment scripts, global shell helpers, runner configuration,
real Docker/volume operations, timeout tuning and other C1008HostFixture
consumers remain outside scope. `LinuxShell` and the shared fixture file are
overlap points, not invitations to repair their other consumers.

No new asynchronous user/session delivery path is introduced. The retained
wrapper-to-host test is local fixture transport, not a production delivery
receipt or real rollout qualification. This card closes only when Windows
placement is audited, portable assertions pass, and the required native Linux
host roster executes successfully. A pushed plan does not claim the fix or
those results. Next: **TestDesign**, after the caller lands this plan.


## Verification design

TestDesign, 2026-10-04, against landed plan/source
`19cd9131393fa104ad2f39879b20018a08400ef8`. This section completes the executable
manifest; the fix design above is unchanged. No builds, Windows runs or Linux
host tests were performed by TestDesign. The estimates below are not timings.

### Inspection

- Read all bodies H-01..H-16, including the complete H-02 lock cleanup,
  `C1008GitGraph`, H-08 bridge/copy recovery and H-12's retained `LinuxShell`
  cases, in `tests/Antiphon.Tests/Scripts/RemoteScriptContractTests.cs` and
  `tests/Antiphon.Tests/Scripts/RollingVolumeRecycleScriptTests.cs` | native
  entry, lock-before-effect, Git setup and preservation -> V-1..V-5, R-1..R-5.
- Read `C1008HostFixture` including root initializer, constructor, `Run`,
  `Volume`, `Container`, `ReloadDocker`, trace and disposal; read
  `C1008WrapperFixture`, `C1008Process`, `DelegateScriptRunner.RepoRoot`,
  `LinuxShell`, `Remote`, `Block` and `Order` | the guard must precede all of
  these host side effects; portable tests keep actual file/process readers ->
  V-1, V-3, V-4, R-1, R-3, R-4.
- Read the four existing portable C1008 methods in Rolling, the full
  `scripts/fixtures/c1008-recycle-cases.json` and
  `scripts/fixtures/c1008-fake-docker.sh`; inspected the other three consumers'
  fixture entry bodies (`RollingProductionMountTests.Prove`,
  `RetiredTempContainerHostTests.Fixture/Cleanup`, and
  `RetiredTempContainerScriptTests.C994_Wrapper_task_and_land_census_is_complete`)
  | no shared constructor/Run admission change; their broader matrices are
  excluded because their code and fixture execution remain unchanged -> R-5.
- Nearest fixtures read for the new audit file:
  `CheckpointRepeatHostTests.RunHost` and its native skip/missing-marker test,
  `CheckpointFixtures.WriteResults`, `TrxReportTests`' join/count/skip/fallback
  tests, and the complete `tools/Antiphon.Checkpoints/Trx/TrxReport.cs` |
  actual child TUnit results, definition-to-result join, zero/missing/duplicate
  results and per-result reasons -> V-2, R-2. The generic parser currently
  discards skip reasons and permits display-name/counter fallbacks; those
  fallbacks cannot establish this card's exact roster.
- Read `PlanCoverageBindingTests`' Roslyn syntax usage,
  `Antiphon.Tests.csproj`, `ProcessSpawnLimit`, `ProductionRunnerGuard`'s
  assembly hook, `PtyBackendEnvGuard`, and the checkpoint schema/importer and
  source-qualification, build-slot, filter and Mutation instructions in
  `docs/testing-and-build.md` | syntax inspection, existing compiler dependency,
  owned child/isolated build and clean outer evidence -> V-1, V-2, R-1, R-2.
  Also read project conventions, orchestration landing/stage rules and the
  C1008 volume-recycling owner section. No production owner changes are needed.

Missing setup is explicit: the assigned mirror has no Windows execution lane;
this stage has not qualified native Linux jq or executed the Windows TUnit reporter.
Code's Windows task supplies the native W1/W2 receipts; its Linux task uses the
CARD-1040 qualified tool environment. Both require the pinned SDK/runtime and
PowerShell; Linux also requires bash, flock, Git, Node, jq and POSIX filesystem
operations already used by the fixture. A missing prerequisite is a failed or
not-run row with its diagnosis. It must not become another skip, a silent return,
an installation in this card, or a relaxed assertion.

#### One scoped audit fixture

Add only `tests/Antiphon.Tests/Scripts/C1008PlatformContractTests.cs` for the
platform harness. It is `[Category("Unit")]`; its two child-host tests have
`[ParallelLimiter<ProcessSpawnLimit>]`. Put its private roster, XML and Roslyn
helpers in that file, with no new package, project, generic checkpoint feature
or production test hook. S1 adds the remote audit and shared helpers; S2 adds
the rolling audit. Its six single-result, non-parameterized methods are:

1. `C1050_Guard_matches_native_platform`: call `RequireNativeLinux` directly,
   without constructing any fixture. Catch the exception as data: Windows must
   produce exactly `SkipTestException`; native Linux must produce no exception.
   The test itself passes rather than skips on either platform. This method
   never probes bash, jq or WSL. Unexpected exceptions fail the assertion.
2. `C1050_Remote_entries_guard_before_work`: use the already referenced Roslyn
   syntax parser on the tracked C# file. Require each of the literal twelve
   H-01..H-12 methods exactly once, with its first executable statement exactly
   `C1008HostFixture.RequireNativeLinux();`. Comments do not count. Compare
   syntax, not a regex that could match a commented call. Include a diagnostic
   naming the offending fully qualified method and `c1050-first-statement`.
3. `C1050_Rolling_entries_guard_before_work`: the same check for H-13..H-16.
   Review separately confirms the helper is the side-effect-free D-2
   conditional and constructor/Run remain unchanged. Source checks complement
   actual results; they are not execution evidence for the host behavior.
4. `C1050_Windows_remote_outcomes_are_exact`: require native Windows as an
   assertion, then run the real child TUnit selection F-W1 below and audit
   twelve named skips and one named portable pass.
5. `C1050_Windows_rolling_outcomes_are_exact`: require native Windows as an
   assertion, then run F-W2 and audit four named skips and four named portable
   passes. These two methods are selected only in Windows rows; running them
   on Linux is an explicit placement failure, not a skip or emulation.
6. `C1050_Audit_rejects_empty_or_incomplete_outcomes`: exercise the same private
   audit assertion against small in-memory TRX documents shaped like the read
   `CheckpointFixtures.WriteResults` fixture, extended with result-local `Output/DebugTrace` skip reasons.
   Start from valid W1 and W2 controls. For each, independently try an empty
   Results element, missing host, duplicate host, substituted host, host marked
   Passed, wrong/empty skip reason, missing portable method, portable marked
   NotExecuted/Failed, unknown testId, missing TestMethod and contradictory
   counters. Require an assertion failure for each, with the expected audit
   label. These are parser/oracle boundary tests, not native Windows evidence.

The Windows child is a fixture subprocess, following the native host pattern in
`CheckpointRepeatHostTests`: launch the **already built current assembly** with
`ProcessStartInfo.ArgumentList` (native `Antiphon.Tests.exe` on Windows), its
runtime files and working directory bound to the current checkout. It performs
no build, no `dotnet run`, and no checkpoint invocation. The checkpoint's leased
outer test driver owns this entire serial fixture workload; do not acquire a
second slot while holding the first. There is no separate unleased build/test
driver. Keep the parent idle while awaiting this one child. Do not run another
assembly or a second audit child concurrently; an assembly-local limiter is not
a cross-process limiter.

Supply only the frozen child filter, `--report-trx`, `--report-trx-filename
raw.trx`, `--results-directory` with a newly created owned directory, and
`--output Detailed`. Clear repeat/worker-mode selectors from the child, retain
the standard production-runner isolation hooks, and do not alter PATH. Exclude
the audit class from the literal child filters, so the child cannot recurse.
Read stdout/stderr concurrently. Use a five-minute child deadline, then on any
cancellation/failure kill only this owned process tree, await exit, and drain
both streams before returning or disposing. Preserve failure artifacts too.
Do not extend any existing host fixture or lock deadline.

Store raw TRX, stdout/stderr, child exit, exact argv, PID/start/end, OS,
assembly path/SHA-256/MVID and a copy/hash of the row's build-source stamp under
ignored `.antiphon/c1050-audit/<fresh-guid>/`. Emit the absolute artifact path
and hashes in the outer result. In SourceLanding Mutation use the assigned
external evidence root via `C1050_EVIDENCE_ROOT`, never the snapshot's reports
store. The outer strict checkpoint receipt binds this same built assembly to
its committed source; Review must join the raw artifact to that outer row by
path/hash and assembly identity. The raw skip-containing TRX is retained as
placement evidence, never submitted as a zero-skip clean certificate. No
rewriting skips, merging counters into the outer TRX or changing the receipt
validator is permitted.

Read raw results by joining `UnitTestResult@testId` to exactly one
`TestDefinitions/UnitTest/TestMethod` class/name; no display-name fallback,
substring class match, filtering away unexpected cases or `Distinct()` before
comparison. Compare ordered **multisets**, including duplicate counts. Require:

- Host observations `(fully qualified method, outcome, reason)` equal exactly
  the literal H roster with outcome `NotExecuted` and the **whole exact D-2
  reason**. Read that result's `Output/DebugTrace` and require its value to equal
  `"Skipped: " + ExpectedReason` exactly. The sixteen early-guard methods emit
  no other trace messages. Do not read `StdOut`, `ErrorInfo/Message`, aggregate
  console text or a reason printed by the audit itself. Missing, ambiguous or
  uncorrelated reasons fail; a string found somewhere in a run log is insufficient.
- Portable observations equal exactly the extraction roster, all `Passed`.
  Each method appears once; failed/skipped/absent portable work cannot count.
- Raw totals agree with those records and child exit is zero: W1 total 13,
  executed/passed 1, failed 0, skipped 12; W2 total 8, executed/passed 4,
  failed 0, skipped 4. Perform roster assertions before the final exit assertion
  so a zero-selection defect gives a decisive missing-roster failure. Report
  every method, outcome and skip reason, plus every portable pass, in outer
  stdout. No green result can be based only on exit zero or discovery output.

The independent expected arrays are literal test identities, not derived from
reflection over discovered tests or from the child filter. The XML helper has
no catch-and-pass path: absent/malformed evidence fails. The in-memory negative
cases above test the audit's reject paths without pretending to run a host.
The reason carrier is verified from the installed package source pins, not left
for Code to invent: TUnit.Engine 1.44.0
[`TestExtensions.GetTrxMessages`](https://raw.githubusercontent.com/thomhurst/TUnit/42e3be6d99bb637d21e1dac711d76991a99e49c3/TUnit.Engine/Extensions/TestExtensions.cs)
adds the skip reason as a `DebugOrTraceTrxMessage` with the `Skipped: ` prefix.
Microsoft.Testing.Extensions.TrxReport 2.2.2
[`TrxReportEngine.AddResults`](https://raw.githubusercontent.com/microsoft/testfx/3ee14aa6d97279c7b2d244657fafdd0c35f40242/src/Platform/Microsoft.Testing.Extensions.TrxReport/TrxReportEngine.cs)
writes that message to result-local `Output/DebugTrace` and writes skipped
outcomes as `NotExecuted`. Both installed `.nuspec` files supply those immutable
source commits; the relevant source bodies were read. This is source inspection,
not a substitute for W1/W2 native execution. A different runtime shape fails W1
for diagnosis; never add a console-text or summary-count fallback.

#### Frozen child selections

The trailing `*` on each literal method operand is the pinned TUnit 1.44 prefix
syntax, not permission to include more methods. Exact TRX equality rejects
unintended suffix matches. No full-path OR is used.

F-W1 (also the host/portable portion of L1):

```text
/*/*/RemoteScriptContractTests/(C1008_Recycle_exact_default_volumes*)|(C1008_Recycle_refuses_references_and_unknown_census*)|(C1008_Recycle_audits_work_as_1654*)|(C1008_Recycle_refuses_unpublished_and_dirty_work*)|(C1008_Recycle_refuses_uninspectable_git*)|(C1008_Recycle_preserves_tmp_copyup*)|(C1008_Recycle_resume_requires_matching_receipt*)|(C1008_Recycle_receipt_records_disk_and_partial_failure*)|(C1008_Retire_temp_rechecks_absence_and_retirement*)|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)|(C1008_Recycle_dry_run_never_mutates*)|(C849_Deploy_prepares_and_verifies_before_acceptance*)|(C849_Deploy_ordering_contract_is_pinned*)
```

F-W2 (also L2):

```text
/*/*/RollingVolumeRecycleScriptTests/(C1008_Option_manifest_is_strict*)|(C1008_Documentation_and_transport_pins_match*)|(C1008_Refusal_receipts_do_not_leak_secrets*)|(C1008_Retired_absent_null_is_accepted*)|(C1008_Wrapper_option_manifest_is_strict*)|(C1008_Transport_scripts_are_ascii*)|(C1008_Wrapper_refusal_receipts_do_not_leak_secrets*)|(C1008_Wrapper_retired_absent_null_is_accepted*)
```

#### Conservation checks

The five extraction rows above are the portable assertion roster, with no
renaming. Review compares their moved bodies to `19cd9131393fa104ad2f39879b20018a08400ef8`:
C849 keeps every source/order check; option-manifest keeps the typed preview,
two rejected flags, four invalid operation IDs and both validators' ten vectors;
ASCII reads all four tracked files; refusal keeps the typed reason, both secret
sentinels and `UnhandledExit` exclusion; retirement keeps the real trace, no POST,
saved stamp, typed project/dryRun and operation syntax checks. Preserve control
flow and custom assertion messages, not just the number of `Should` calls.
A passed method with those assertions deleted is a Review failure.

For H-16 add two recipient-side assertions after the unchanged successful host
call: inspect the host's persisted journal and require its `operationId` equal
the operation from the actual wrapper trace (`c1050-host-operation`), and its
`retiredAt` equal the wrapper's stamp as a UTC instant (`c1050-host-retirement`).
Require the operation-named journal to exist before reading it. Both existing
four-volume removal and exit assertions remain. The shared wrapper helper
returns the actual detached trace JSON, with every original wrapper assertion
inside the helper, so both callers execute them. Do not return a fabricated
request or compare a journal to fixture constants.

Boundary combinations: Windows with bash/WSL/jq present or absent takes the same
OS-only branch; the entry syntax check and direct helper test establish that
no tool probe precedes it. No PATH-removal experiment is required. Native Linux
with all dependencies executes all sixteen; missing jq retains its existing
failure and is owned by CARD-1040. MacOS and other non-Linux platforms take the
same D-2 predicate but are not an execution lane for this card. Source/order
and ASCII tests run on both named platforms. H-16 separately proves the real
portable request and native recipient journal, not merely request creation.

### Delivery inventory

No new or changed asynchronous application/session delivery path exists.
Producer, durable queue identity, recipient eligibility, enqueue recovery and
complete UserPrompt transcript requirements are therefore not applicable; no
request, event, Sent flag or acknowledgement is offered as a delivery verdict.
The application real-queue busy/already-eligible/crash-handoff matrix is excluded
because this change adds no application queue or session input.

The retained local fixture handoff is enumerated explicitly: real PowerShell
wrapper -> C727 fake bridge trace -> shared helper's actual request JSON -> real
Linux `C1008HostFixture.Run("retire-temp-runner", ...)` -> persisted C1008 host
journal and fake Docker state. Join by the wrapper's operation ID and retirement
stamp. H-16 reads that journal and the four removals; the portable test alone
stops at the trace and cannot prove host receipt. Existing H-07/H-08 retain
journal/copy failure and resume cases; their implementation is unchanged.
Fake HTTP/Docker boundaries do not prove live HTTP delivery, Docker uid
execution, real volume removal, deployment or user-session receipt.

The audit subprocess is also a local, awaited fixture operation: parent audit
-> same-build child TUnit host -> fresh raw TRX -> parent assertions -> outer
checkpoint result, joined by the owned result directory and assembly hash.
An absent/partial TRX or interrupted child fails; it is never recovered as a
successful receipt. Re-running a failed CP uses a fresh directory and build.
There is no durable work queue or retry service added here.

### Proves it works now

- V-1: OS-only admission occurs before all host setup | source plus direct
  helper | the three named guard tests | every first statement matches; direct
  guard throws a skip on Windows and returns on native Linux, with no fixture.
- V-2: Windows reports each intentional skip by name/reason and every portable
  pass | real TUnit child plus outer TUnit audit | W1/W2 and the audit-negative
  method | exact 12+1 and 4+4 matrices; empty/partial/wrong outcomes rejected.
- V-3: Linux retains native host coverage | native TUnit | L1/L2 | all sixteen
  historical host names execute, plus all five portable methods; zero skips.
- V-4: portable assertions and wrapper-to-host identity survive extraction |
  real PowerShell, tracked source and native host fixture | five portable names
  plus H-16 | original assertions retained and operation/stamp corroborated by
  host journal and four removals.
- V-5: scope and evidence remain bounded | Review source/receipt inspection |
  six CP rows, complete task-range evidence-policy check and conservation diff |
  no production/fixture mechanics changes, no broad run, no generated evidence
  committed, and native raw receipts joined to clean outer build provenance.

### Guards the regression

- R-1: a late/missing admission call could allocate Root, launch Node/Git or
  enter the flock wait before skipping | per-method syntax assertions labelled
  `c1050-first-statement`, direct helper assertion `c1050-native-admission`.
- R-2: exit zero, discovery-only output, wrong reason, extra/missing/duplicate
  cases or silently dropped portable methods could masquerade as Windows
  coverage | exact `c1050-host-roster` and `c1050-portable-roster` assertions over
  actual native child TRX, supported by negative receipt vectors.
- R-3: extracting a method could leave its portable test empty or weaken its
  original behavior | conservation diff, both native lane results and the
  retained typed-refusal assertion in
  `C1008_Wrapper_refusal_receipts_do_not_leak_secrets`.
- R-4: factoring H-16 could replace the real operation/stamp with fixture
  defaults | H-16's `c1050-host-operation` and `c1050-host-retirement`, plus its
  existing successful exit and exact four-volume-removal assertions.
- R-5: blanket fixture admission or wider production repair could hide other
  cards' behavior | source diff restricted to D-2 helper, sixteen calls, five
  extractions, scoped audit and one owner paragraph; constructor/Run, LinuxShell,
  fake Docker, production scripts, deadlines and existing limiters unchanged.

### Guard inventory

The sixteen entry calls are independently removable, so each receives a distinct
control. This is more than a single representative OS mutation; it does **not**
repeat the unchanged recycling safety battery. G-18/G-19 each use one complete
multiset equality (identity, outcome and result-local reason), not independent
weaker count/name checks. Their PCs include the boundary variants below.
Unchanged production safety guards are preserved by V-3/V-4 and conservation;
no new product authorization, queue, recovery or destructive-operation guard
is introduced. There are no untested changed entry guards.

| Guard | Plan ref and invariant | Positive control |
|---|---|---|
| G-1 | D-2: shared OS predicate skips non-Linux and admits native Linux without probing/allocating | PC-1 |
| G-2 | D-2, H-01: this entry calls the guard before its first original statement | PC-2 |
| G-3 | D-2, H-02: this entry calls the guard before its first original statement | PC-3 |
| G-4 | D-2, H-03: this entry calls the guard before its first original statement | PC-4 |
| G-5 | D-2, H-04: this entry calls the guard before its first original statement | PC-5 |
| G-6 | D-2, H-05: this entry calls the guard before its first original statement | PC-6 |
| G-7 | D-2, H-06: this entry calls the guard before its first original statement | PC-7 |
| G-8 | D-2, H-07: this entry calls the guard before its first original statement | PC-8 |
| G-9 | D-2, H-08: this entry calls the guard before its first original statement | PC-9 |
| G-10 | D-2, H-09: this entry calls the guard before its first original statement | PC-10 |
| G-11 | D-2, H-10: this entry calls the guard before its first original statement | PC-11 |
| G-12 | D-2, H-11: this entry calls the guard before its first original statement | PC-12 |
| G-13 | D-2, H-12: this entry calls the guard before its first original statement | PC-13 |
| G-14 | D-2, H-13: this entry calls the guard before its first original statement | PC-14 |
| G-15 | D-2, H-14: this entry calls the guard before its first original statement | PC-15 |
| G-16 | D-2, H-15: this entry calls the guard before its first original statement | PC-16 |
| G-17 | D-2, H-16: this entry calls the guard before its first original statement | PC-17 |
| G-18 | D-5: exact host result multiset, every skip's full D-2 reason, no absent/extra/duplicate/executed host on Windows | PC-18 |
| G-19 | D-3/D-5: exact portable Passed multiset, no missing/skipped/failed replacement | PC-19 |
| G-20 | D-3: extracted wrapper refusal still asserts the real typed failure and secret custody | PC-20 |
| G-21 | D-3/H-16: the real wrapper operation reaches the persisted host journal | PC-21 |
| G-22 | D-3/H-16: the real wrapper retirement stamp reaches the persisted host journal | PC-22 |

R-5's unchanged mechanics are a Review scope constraint, not another runtime
admission branch. The generic checkpoint source/build validator, process limiter
and assembly isolation hooks are reused unchanged; their own mutation batteries
are outside this card. The harness is not permission to weaken any of them.

### Positive controls

Mutation runs these **after land**, against the exact SourceLanding SHA. Code
runs only V/R; ordinary Review judges this inventory and both lane receipts
before land. Each cycle is baseline green, compiling defect, intended assertion
red, exact restoration, fresh build, restored green. Run with the unchanged
copied `scripts/run-checkpoint.ps1` driver, method-scoped filters below and
`-MinExecuted 1 -Expect Class.Method`. A failing build, fixture launch, timeout,
missing test or zero outer tests does not count as red. Preserve each PC's raw
TRX, assertion text, build exit and restoration evidence externally.

PC-1 uses `/*/*/C1008PlatformContractTests/C1050_Guard_matches_native_platform`.
On **Windows**, replace the helper body with an empty body: expect
`c1050-native-admission` red because no SkipTestException was captured. Restore
and green. On **native Linux**, replace its predicate with `if (true)` keeping
the same throw: the same test catches the unexpected skip as data and fails
`c1050-native-admission` (it must not itself skip). Restore and green. These are
two variants of the shared OS behavior, never missing-tool experiments.

PC-2..PC-17 each delete just the first
`C1008HostFixture.RequireNativeLinux();` from the named entry below. This is a
compiling mutation; the source-audit test does not execute the damaged host.
Expect the exact `c1050-first-statement: Class.Method` assertion red, then restore
that call and run the same exact method green. These controls can run on Linux
and need neither Windows nor jq. Do not batch edits to the same source file.

| PC | Entry to mutate | Exact filter | Expected assertion |
|---|---|---|---|
| PC-2 | `RemoteScriptContractTests.C1008_Recycle_exact_default_volumes` | `/*/*/C1008PlatformContractTests/C1050_Remote_entries_guard_before_work` | `c1050-first-statement: RemoteScriptContractTests.C1008_Recycle_exact_default_volumes` |
| PC-3 | `RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` | `/*/*/C1008PlatformContractTests/C1050_Remote_entries_guard_before_work` | `c1050-first-statement: RemoteScriptContractTests.C1008_Recycle_refuses_references_and_unknown_census` |
| PC-4 | `RemoteScriptContractTests.C1008_Recycle_audits_work_as_1654` | `/*/*/C1008PlatformContractTests/C1050_Remote_entries_guard_before_work` | `c1050-first-statement: RemoteScriptContractTests.C1008_Recycle_audits_work_as_1654` |
| PC-5 | `RemoteScriptContractTests.C1008_Recycle_refuses_unpublished_and_dirty_work` | `/*/*/C1008PlatformContractTests/C1050_Remote_entries_guard_before_work` | `c1050-first-statement: RemoteScriptContractTests.C1008_Recycle_refuses_unpublished_and_dirty_work` |
| PC-6 | `RemoteScriptContractTests.C1008_Recycle_refuses_uninspectable_git` | `/*/*/C1008PlatformContractTests/C1050_Remote_entries_guard_before_work` | `c1050-first-statement: RemoteScriptContractTests.C1008_Recycle_refuses_uninspectable_git` |
| PC-7 | `RemoteScriptContractTests.C1008_Recycle_preserves_tmp_copyup` | `/*/*/C1008PlatformContractTests/C1050_Remote_entries_guard_before_work` | `c1050-first-statement: RemoteScriptContractTests.C1008_Recycle_preserves_tmp_copyup` |
| PC-8 | `RemoteScriptContractTests.C1008_Recycle_resume_requires_matching_receipt` | `/*/*/C1008PlatformContractTests/C1050_Remote_entries_guard_before_work` | `c1050-first-statement: RemoteScriptContractTests.C1008_Recycle_resume_requires_matching_receipt` |
| PC-9 | `RemoteScriptContractTests.C1008_Recycle_receipt_records_disk_and_partial_failure` | `/*/*/C1008PlatformContractTests/C1050_Remote_entries_guard_before_work` | `c1050-first-statement: RemoteScriptContractTests.C1008_Recycle_receipt_records_disk_and_partial_failure` |
| PC-10 | `RemoteScriptContractTests.C1008_Retire_temp_rechecks_absence_and_retirement` | `/*/*/C1008PlatformContractTests/C1050_Remote_entries_guard_before_work` | `c1050-first-statement: RemoteScriptContractTests.C1008_Retire_temp_rechecks_absence_and_retirement` |
| PC-11 | `RemoteScriptContractTests.C1008_Retire_temp_reclaims_below_cache_disk_gate` | `/*/*/C1008PlatformContractTests/C1050_Remote_entries_guard_before_work` | `c1050-first-statement: RemoteScriptContractTests.C1008_Retire_temp_reclaims_below_cache_disk_gate` |
| PC-12 | `RemoteScriptContractTests.C1008_Recycle_dry_run_never_mutates` | `/*/*/C1008PlatformContractTests/C1050_Remote_entries_guard_before_work` | `c1050-first-statement: RemoteScriptContractTests.C1008_Recycle_dry_run_never_mutates` |
| PC-13 | `RemoteScriptContractTests.C849_Deploy_prepares_and_verifies_before_acceptance` | `/*/*/C1008PlatformContractTests/C1050_Remote_entries_guard_before_work` | `c1050-first-statement: RemoteScriptContractTests.C849_Deploy_prepares_and_verifies_before_acceptance` |
| PC-14 | `RollingVolumeRecycleScriptTests.C1008_Option_manifest_is_strict` | `/*/*/C1008PlatformContractTests/C1050_Rolling_entries_guard_before_work` | `c1050-first-statement: RollingVolumeRecycleScriptTests.C1008_Option_manifest_is_strict` |
| PC-15 | `RollingVolumeRecycleScriptTests.C1008_Documentation_and_transport_pins_match` | `/*/*/C1008PlatformContractTests/C1050_Rolling_entries_guard_before_work` | `c1050-first-statement: RollingVolumeRecycleScriptTests.C1008_Documentation_and_transport_pins_match` |
| PC-16 | `RollingVolumeRecycleScriptTests.C1008_Refusal_receipts_do_not_leak_secrets` | `/*/*/C1008PlatformContractTests/C1050_Rolling_entries_guard_before_work` | `c1050-first-statement: RollingVolumeRecycleScriptTests.C1008_Refusal_receipts_do_not_leak_secrets` |
| PC-17 | `RollingVolumeRecycleScriptTests.C1008_Retired_absent_null_is_accepted` | `/*/*/C1008PlatformContractTests/C1050_Rolling_entries_guard_before_work` | `c1050-first-statement: RollingVolumeRecycleScriptTests.C1008_Retired_absent_null_is_accepted` |

- PC-18: Windows, exact filter
  `/*/*/C1008PlatformContractTests/C1050_Windows_remote_outcomes_are_exact`.
  Four separately restored compiling variants: (a) remove H-01's `[Test]`
  attribute only; (b) replace the child filter with
  `/*/*/RemoteScriptContractTests/C1050_DeliberatelyAbsent`; (c) change only the
  helper's reason to `CARD-1050: wrong reason`; (d) replace H-01's body with
  `await Task.CompletedTask;`. Keep the independent expected arrays unchanged.
  All must fail **the outer** `c1050-host-roster` assertion: respectively missing
  identity, zero observed roster, wrong per-result reason, and Passed instead
  of NotExecuted. Variant (b) deliberately makes the *child* empty; it counts
  only if the selected outer test executes and produces this assertion red.
  No shell/fixture exception is an acceptable detection of variant (d).
- PC-19: Windows, exact filter
  `/*/*/C1008PlatformContractTests/C1050_Windows_rolling_outcomes_are_exact`.
  Two separately restored variants: (a) remove only `[Test]` from
  `C1008_Wrapper_option_manifest_is_strict`; (b) add an immediate
  `throw new TUnit.Core.Exceptions.SkipTestException("wrong portable skip");`
  at that method's entry. Require outer `c1050-portable-roster` red for missing
  versus NotExecuted; host skip records remain the expected four. Restore and
  green after each. Compiler warnings are not a failed build or a PC verdict.
- PC-20: Windows, exact filter
  `/*/*/RollingVolumeRecycleScriptTests/C1008_Wrapper_refusal_receipts_do_not_leak_secrets`.
  In that extracted method change the fixture assignment
  `f.State["taskError"] = "SENTINEL_C1008_HTTP_CREDENTIAL";` to
  `f.State.Remove("taskError");`. The real wrapper now follows the success path;
  the retained `run.Output.ShouldContain("RecycleTaskCensusUnknown", ...)`
  must fail at `recycle-receipt-custody: typed census refusal survives`.
  This compiling stimulus mutation proves meaningful preserved wrapper
  assertions, without altering production safety logic. Restore and green.
- PC-21: native Linux, exact filter
  `/*/*/RollingVolumeRecycleScriptTests/C1008_Retired_absent_null_is_accepted`.
  Change only the operation interpolated into the host `Run` extra string to
  `c100800000000000000000000000000000001`; retain the actual request-derived
  operation used by the journal assertion. The wrapper's generated operation
  remains distinct. Expect `c1050-host-operation` red at the operation-named
  journal existence/equality assertion after a successful host call, then
  restore and green. Do not make missing-file I/O the decisive failure.
- PC-22: native Linux, same exact H-16 filter. Change only the stamp sent to
  the host to `2026-10-03T09:31:00Z`, leaving the wrapper JSON and host status
  unchanged. The existing host exit assertion
  `retire-absent-null-accepted: actual host entry using wrapper context` must
  be red (host refuses the mismatched retirement proof). Restore and green;
  the new `c1050-host-retirement` journal assertion additionally checks the
  accepted path. This is a stamp-propagation control, not another retirement
  policy mutation.

For every variant the runner reports actual one-test outer execution and the
specified assertion, not just a nonzero driver exit. PC-18/19 include nested
native evidence because the assertion they protect concerns actual TUnit
reporting. All other PCs remain single-method processes with no broad child
selection. Keep the two host script files unchanged except the one planned
mutation during each cycle, and restore timestamps before rebuilding.

### Out of scope

- CARD-1049 curl/readiness/cygpath failures and CARD-1040 jq installation,
  activation and absence qualification: separate owners; missing prerequisites
  are reported, never hidden by this card.
- MSYS/WSL path conversion, CRLF transport repairs, real rollouts and live
  volume reclamation: explicitly rejected by D-1/D-4.
- Unchanged C1008 production safety mutation scenarios and other shared fixture
  consumers: this card changes test admission/placement and conserves their
  fixture implementation. No whole Unit, class, namespace or assembly run.
- MacOS execution, real uid switching and live application/session delivery:
  neither is claimed by these local fixtures. No session transcript substitute
  or synthetic queue receipt is involved.

### Checkpoints

The table is the complete ordinary scope and supersedes only the provisional
verification estimates above. Every row has one isolated build and one literal
outer filter. The Windows audit's single fixed child selection is part of its
fixture, is disclosed above, and has no extra build. CP-1/CP-3/CP-5 are **Windows**
(`-Platform Windows`); CP-2/CP-4/CP-6 are **native Linux** (`-Platform Linux`). Omit
`-Runner`. The caller dispatches those lanes separately at the same committed
slice SHA; a Linux delegate must not claim the Windows rows.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1050-w1/` | windows-remote | `/*/*/C1008PlatformContractTests/(C1050_Guard_matches_native_platform*)\|(C1050_Remote_entries_guard_before_work*)\|(C1050_Audit_rejects_empty_or_incomplete_outcomes*)\|(C1050_Windows_remote_outcomes_are_exact*)` | V-1, V-2, V-4, V-5, R-1, R-2, R-3, R-5 | exact 4 outer passed, 0 failed/skipped; child F-W1 exact 12 named/reasoned skips + 1 portable pass | 4 | 6 |
| CP-2 | S1 | `tests/Antiphon.Tests -> bin-c1050-l1/` | linux-remote | `/*/*/(RemoteScriptContractTests*)\|(C1008PlatformContractTests*)/(C1008_Recycle_exact_default_volumes*)\|(C1008_Recycle_refuses_references_and_unknown_census*)\|(C1008_Recycle_audits_work_as_1654*)\|(C1008_Recycle_refuses_unpublished_and_dirty_work*)\|(C1008_Recycle_refuses_uninspectable_git*)\|(C1008_Recycle_preserves_tmp_copyup*)\|(C1008_Recycle_resume_requires_matching_receipt*)\|(C1008_Recycle_receipt_records_disk_and_partial_failure*)\|(C1008_Retire_temp_rechecks_absence_and_retirement*)\|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)\|(C1008_Recycle_dry_run_never_mutates*)\|(C849_Deploy_prepares_and_verifies_before_acceptance*)\|(C849_Deploy_ordering_contract_is_pinned*)\|(C1050_Guard_matches_native_platform*)` | V-1, V-3, V-4, V-5, R-1, R-3, R-5 | exact 14 passed (H-01..H-12 + C849 portable + direct guard), 0 failed/skipped | 14 | 8 |
| CP-3 | S2 | `tests/Antiphon.Tests -> bin-c1050-w2/` | windows-rolling | `/*/*/C1008PlatformContractTests/(C1050_Rolling_entries_guard_before_work*)\|(C1050_Windows_rolling_outcomes_are_exact*)` | V-1, V-2, V-4, V-5, R-1, R-2, R-3, R-5 | exact 2 outer passed, 0 failed/skipped; child F-W2 exact 4 named/reasoned skips + 4 portable passes | 2 | 7 |
| CP-4 | S2 | `tests/Antiphon.Tests -> bin-c1050-l2/` | linux-rolling | `/*/*/RollingVolumeRecycleScriptTests/(C1008_Option_manifest_is_strict*)\|(C1008_Documentation_and_transport_pins_match*)\|(C1008_Refusal_receipts_do_not_leak_secrets*)\|(C1008_Retired_absent_null_is_accepted*)\|(C1008_Wrapper_option_manifest_is_strict*)\|(C1008_Transport_scripts_are_ascii*)\|(C1008_Wrapper_refusal_receipts_do_not_leak_secrets*)\|(C1008_Wrapper_retired_absent_null_is_accepted*)` | V-3, V-4, V-5, R-3, R-4, R-5 | exact 8 passed (H-13..H-16 + four portable), 0 failed/skipped | 8 | 6 |
| CP-5 | S2 | `tests/Antiphon.Tests -> bin-c1050-w1-final/` | windows-remote-final | `/*/*/C1008PlatformContractTests/(C1050_Guard_matches_native_platform*)\|(C1050_Remote_entries_guard_before_work*)\|(C1050_Audit_rejects_empty_or_incomplete_outcomes*)\|(C1050_Windows_remote_outcomes_are_exact*)` | V-1, V-2, V-4, V-5, R-1, R-2, R-3, R-5 | exact 4 outer passed, 0 failed/skipped; child F-W1 exact 12 named/reasoned skips + 1 portable pass | 4 | 6 |
| CP-6 | S2 | `tests/Antiphon.Tests -> bin-c1050-l1-final/` | linux-remote-final | `/*/*/(RemoteScriptContractTests*)\|(C1008PlatformContractTests*)/(C1008_Recycle_exact_default_volumes*)\|(C1008_Recycle_refuses_references_and_unknown_census*)\|(C1008_Recycle_audits_work_as_1654*)\|(C1008_Recycle_refuses_unpublished_and_dirty_work*)\|(C1008_Recycle_refuses_uninspectable_git*)\|(C1008_Recycle_preserves_tmp_copyup*)\|(C1008_Recycle_resume_requires_matching_receipt*)\|(C1008_Recycle_receipt_records_disk_and_partial_failure*)\|(C1008_Retire_temp_rechecks_absence_and_retirement*)\|(C1008_Retire_temp_reclaims_below_cache_disk_gate*)\|(C1008_Recycle_dry_run_never_mutates*)\|(C849_Deploy_prepares_and_verifies_before_acceptance*)\|(C849_Deploy_ordering_contract_is_pinned*)\|(C1050_Guard_matches_native_platform*)` | V-1, V-3, V-4, V-5, R-1, R-3, R-5 | exact 14 passed (H-01..H-12 + C849 portable + direct guard), 0 failed/skipped | 14 | 8 |


CP-2 and CP-6 use the same combined class/method filter. Verify the fourteen-name
TRX set; neither operand admits the Windows-only audit methods. `Min` is the
outer TUnit execution floor: 4, 14, 2, 8, 4, 14 respectively. Internal assertions and
raw child skips do not inflate it. The five child portable passes are separately
reported, never added to the outer checkpoint counters.

CP-1/CP-2 close S1. CP-3..CP-6 supply the complete required ordinary scope at
the final S2 SHA; CP-5/CP-6 deliberately requalify S1 behavior after the new
commit, rather than relabelling its old source receipts. Run the tool once per
committed slice/lane: Windows S1 selects CP-1, Linux S1 selects CP-2, Windows S2
selects CP-3,CP-5, Linux S2 selects CP-4,CP-6. Use `--serial` for the two-row
groups so no child-host fixture overlaps another row. Invoke the tool with
`run --plan docs/superpowers/plans/2026-10-04-card-1050-windows-bash-host-tests-plan.md
--rows` followed by the exact selection above and `--expected-source-sha` with
that slice's actual full SHA.
Bootstrap the tool only through `scripts/build-slot.ps1`, into a producer-owned
alternate output. Subsequent `dotnet run --no-build` tool invocations use that
output; rows take their own build slots. If run yields exit 75, supervise `wait`
with at most 60-second waits until terminal; do not settle with a running row.
The imported row wall deadlines are 18, 24, 21, 18, 18 and 24 minutes. Slot timeout is
not run, never permission for an unleased retry. Commit/push S1 before W1/L1;
commit/push S2 before W2/L2. Keep each worktree frozen during its runs.

Run the normal receipt validator for each **outer** clean row at its tested
SHA, retain its unedited CHECKPOINT line and join Windows raw child evidence
as specified above. Review also requires exact Linux executed rosters and
zero skips. Qualify the final-tip required set CP-3,CP-4,CP-5,CP-6 together
against the exact S2 commit for ordinary Review; CP-1/CP-2 remain historical S1
slice evidence. Any later source edit requires requalification of the affected
selection and a reported rerun. Run the task-range evidence-policy checker
against the pushed candidate history. Clean up only inventoried producer-owned
bin outputs after the relevant run, preserving raw ignored evidence.

### Cost

All values are **estimated wall minutes**, excluding broker queue wait. The
ordinary Code V/R floor is **6 + 8 + 7 + 6 + 6 + 8 = 41 minutes**, including
six isolated builds (budget 2 each, 12 total) and 29 minutes of host startup,
tests and audit work. Windows costs 19 minutes, Linux 22. Allow **3 minutes**
additional tool bootstrap and receipt/setup work: Code verification/setup is
**44 minutes**. S1 budgets 25–35 minutes authoring plus 14 verification and 3
setup (42–52 minutes). S2 budgets 15–25 authoring plus 27 verification
(42–52 minutes). Thus each Code slice stays within 30–60 minutes, including its
checks; the harness is part of those slices, not a separate generic tool.

Mutation costs are separate. Each phase includes its isolated build and exact
method test; restoration is included in the green phase budget. The following
conservative floor budgets a baseline for every variant, even where its exact
method just ran green, so no reuse is needed to meet it:

| Controls / exact selection | Variants | Baseline | Red | Restore + green | Total minutes |
|---|---:|---:|---:|---:|---:|
| PC-1, `C1050_Guard_matches_native_platform`, one native Windows and one native Linux cycle | 2 | 2 | 2 | 2 | 12 |
| PC-2..PC-13, `C1050_Remote_entries_guard_before_work`, exact per-entry deletion | 12 | 2 | 2 | 2 | 72 |
| PC-14..PC-17, `C1050_Rolling_entries_guard_before_work`, exact per-entry deletion | 4 | 2 | 2 | 2 | 24 |
| PC-18, `C1050_Windows_remote_outcomes_are_exact`, four receipt defects | 4 | 3 | 3 | 3 | 36 |
| PC-19, `C1050_Windows_rolling_outcomes_are_exact`, two portable-roster defects | 2 | 4 | 4 | 4 | 24 |
| PC-20, `C1008_Wrapper_refusal_receipts_do_not_leak_secrets` | 1 | 3 | 3 | 3 | 9 |
| PC-21, `C1008_Retired_absent_null_is_accepted`, operation propagation | 1 | 3 | 3 | 3 | 9 |
| PC-22, `C1008_Retired_absent_null_is_accepted`, stamp propagation | 1 | 3 | 3 | 3 | 9 |

The Mutation execution floor is **195 minutes** for 27 independently restored
cycles, plus **3 minutes** setup/provenance inventory: **198 minutes**. This
explicit cost follows from giving every independently removable entry guard a
positive control; a two-control representative sample would leave fourteen
entry guards without their own control. Do not enlarge Code's ordinary runs
into this post-land battery. Commission Mutation in supervised 30–60 minute
lane-specific slices if necessary, preserving the same exact landed source and
evidence root; no external executor or sub-delegation from a sourced snapshot.

Combined verification/setup floor is **3 + 41 + 3 + 195 = 242 minutes**.
Code authoring adds **40–60 minutes**, giving **282–302 minutes** including
all ordinary and post-land verification. No measured savings are claimed
(0 minutes): there is no paired baseline, and independently removable entry
guards cannot share a single mutation. Final-tip requalification costs an
explicit 14 minutes over the four initial rows. No whole-Unit/full-class rerun
is hidden in these estimates.

Handoff audit: bodies and nearest fixtures read; **guards=22, mapped=22,
missing=0, duplicate PC maps=0**. Every PC names a compiling defect, an existing
or explicitly designed exact method, a decisive assertion and restored green.
PC execution remains pending post-land; native Windows/Linux V/R remains Code's
work. The design is complete for **next: code** after the caller lands this
pushed documentation through the normal route.
