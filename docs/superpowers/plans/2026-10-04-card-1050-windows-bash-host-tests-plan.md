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
